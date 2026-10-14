import { Router } from 'express';
import {
  createRace,
  getRace,
  getRaceByCode,
  getResults,
  resetBoatStates,
  saveResults,
  setAllClear,
  setBoatTiming,
  setStartAt,
  shortenCourse,
  updateRace
} from '../db/raceStore';
import { deleteTracks, getTracks } from '../db/trackStore';
import { getLastPositions } from '../services/positionCache';
import { flushTracks, forgetRace } from '../services/trackRecorder';
import { broadcast } from '../ws/registry';
import { RaceDto, RaceParticipantStatus, ResultPublicationDto } from '../types/models';

const router = Router();

router.post('/', (req, res) => {
  const dto = req.body as RaceDto;
  if (!dto || !dto.name || !dto.fleet) {
    res.status(400).json({ error: 'Invalid race payload' });
    return;
  }

  const { raceId, joinCode } = createRace(dto);
  res.status(201).json({ raceId, joinCode });
});

router.put('/:id', (req, res) => {
  const dto = req.body as RaceDto;
  const startWas = getRace(req.params.id)?.startAt ?? null;
  const updated = updateRace(req.params.id, dto);
  if (!updated) {
    res.status(404).json({ error: 'Race not found' });
    return;
  }

  // The committee cancelled the start sequence (its start time was cleared): throw away what was recorded
  // during the aborted lead-in and put every boat back to the pre-race state.
  if (startWas && !dto.startAt) {
    forgetRace(req.params.id);
    deleteTracks(req.params.id);
    resetBoatStates(req.params.id);
  }

  broadcast(req.params.id, { type: 'raceStatusChanged', status: dto.status });
  res.status(200).json({ raceId: req.params.id });
});

router.get('/by-code/:code', (req, res) => {
  const race = getRaceByCode(req.params.code);
  if (!race) {
    res.status(404).json({ error: 'Race not found' });
    return;
  }

  res.json(race);
});

router.get('/:id', (req, res) => {
  const race = getRace(req.params.id);
  if (!race) {
    res.status(404).json({ error: 'Race not found' });
    return;
  }

  res.json(race);
});

// Last known position of each boat, for a map opened mid-race (see positionCache).
router.get('/:id/positions', (req, res) => {
  if (!getRace(req.params.id)) {
    res.status(404).json({ error: 'Race not found' });
    return;
  }

  res.json({ positions: getLastPositions(req.params.id) });
});

router.get('/:id/tracks', (req, res) => {
  const race = getRace(req.params.id);
  if (!race) {
    res.status(404).json({ error: 'Race not found' });
    return;
  }

  // Optional limits for a live map (see getTracks); anything that isn't a sensible number is ignored.
  const optionalMs = (value: unknown, max: number): number | undefined => {
    const n = Number(value);
    return value !== undefined && Number.isFinite(n) && n >= 0 ? Math.min(n, max) : undefined;
  };
  const sinceMs = optionalMs(req.query.sinceMs, Number.MAX_SAFE_INTEGER);
  const minGapMs = optionalMs(req.query.minGapMs, 60000);

  flushTracks(); // include fixes still waiting in the write buffer
  res.json({ raceId: race.id, startAt: race.startAt, boats: getTracks(race.id, sinceMs, minGapMs) });
});

router.patch('/:id/shorten-course', (req, res) => {
  const newLaps = (req.body?.newLaps ?? []) as { participantId: string; laps: number }[];
  shortenCourse(req.params.id, newLaps);
  broadcast(req.params.id, { type: 'courseShortened', raceParticipants: newLaps });
  res.status(200).json({ ok: true });
});

router.post('/:id/start-sequence', (req, res) => {
  const startAt = req.body?.startAt as string;
  if (!startAt) {
    res.status(400).json({ error: 'startAt is required' });
    return;
  }

  setStartAt(req.params.id, startAt);
  broadcast(req.params.id, { type: 'raceStatusChanged', status: 'StartSequence' });
  res.status(200).json({ ok: true });
});

router.post('/:id/all-clear', (req, res) => {
  setAllClear(req.params.id);
  broadcast(req.params.id, { type: 'raceStatusChanged', status: 'Racing' });
  res.status(200).json({ ok: true });
});

const STATUS_BY_LOWERCASE: Record<string, RaceParticipantStatus> = {
  racing: 'Racing',
  finished: 'Finished',
  dnf: 'DNF',
  dns: 'DNS',
  ret: 'RET',
  ocs: 'OCS'
};

/**
 * Manual timing from a committee PC (Timing Sheet laps, finishes, DNF/DNS/RET/OCS). Each boat's entry carries
 * the time the committee made it; the newest write wins per boat, so a PC that was offline can deliver its
 * queue later without overwriting anything more recent. Applied entries are broadcast so the live map, the
 * replay's results and any other committee PC follow.
 */
router.post('/:id/timing', (req, res) => {
  const raceId = req.params.id;
  if (!getRace(raceId)) {
    res.status(404).json({ error: 'Race not found' });
    return;
  }

  const boats = Array.isArray(req.body?.boats) ? (req.body.boats as Record<string, unknown>[]) : null;
  if (!boats) {
    res.status(400).json({ error: 'Expected { boats: [...] }' });
    return;
  }

  const applied: string[] = [];
  const stale: string[] = [];
  const rejected: string[] = [];

  for (const b of boats) {
    const participantId = typeof b.participantId === 'string' ? b.participantId : '';
    const status = STATUS_BY_LOWERCASE[String(b.status ?? '').toLowerCase()];
    const lapsCompleted = Number(b.lapsCompleted);
    const updatedAtMs = Number(b.updatedAtMs);
    if (!participantId || !status || !Number.isInteger(lapsCompleted) || lapsCompleted < 0 || !Number.isFinite(updatedAtMs)) {
      rejected.push(participantId || '(missing id)');
      continue;
    }

    const finishTime = typeof b.finishTime === 'string' && !Number.isNaN(Date.parse(b.finishTime)) ? b.finishTime : null;
    const elapsed = b.elapsedSeconds == null ? null : Number(b.elapsedSeconds);
    const result = setBoatTiming(raceId, {
      participantId,
      lapsCompleted,
      isOnFinalLap: !!b.isOnFinalLap,
      status,
      finishTime,
      elapsedSeconds: elapsed !== null && Number.isFinite(elapsed) ? elapsed : null,
      updatedAtMs
    });

    if (result.outcome === 'unknown') {
      rejected.push(participantId);
      continue;
    }
    if (result.outcome === 'stale') {
      stale.push(participantId);
      continue;
    }

    applied.push(participantId);
    const before = result.previous!;
    const after = result.current!;

    // The same events the GPS tracker sends, so the live map and replay need no changes ...
    if (after.lapsCompleted !== before.lapsCompleted) {
      broadcast(raceId, {
        type: 'lapCompleted',
        participantId,
        lapsCompleted: after.lapsCompleted,
        isFinalLap: after.isOnFinalLap
      });
    }
    if (after.isOnFinalLap && !before.isOnFinalLap) {
      broadcast(raceId, { type: 'finalLap', participantId });
    }
    if (after.status === 'Finished' && before.status !== 'Finished') {
      broadcast(raceId, {
        type: 'finished',
        participantId,
        finishTime: after.finishTime,
        elapsedSeconds: after.elapsedSeconds
      });
    }
    // ... plus the full state, which also carries DNF/DNS/RET/OCS and corrections that undo a lap or a finish.
    broadcast(raceId, {
      type: 'boatTiming',
      participantId,
      lapsCompleted: after.lapsCompleted,
      isOnFinalLap: after.isOnFinalLap,
      status: after.status,
      finishTime: after.finishTime,
      elapsedSeconds: after.elapsedSeconds
    });
  }

  res.status(200).json({ applied, stale, rejected });
});

router.post('/:id/results', (req, res) => {
  const payload = req.body as ResultPublicationDto;
  payload.raceId = req.params.id;
  saveResults(payload);
  broadcast(req.params.id, { type: 'raceStatusChanged', status: 'Finished' });
  res.status(201).json({ ok: true });
});

router.get('/:id/results', (req, res) => {
  const results = getResults(req.params.id);
  if (!results) {
    res.status(404).json({ error: 'No results published yet' });
    return;
  }

  res.json(results);
});

export default router;
