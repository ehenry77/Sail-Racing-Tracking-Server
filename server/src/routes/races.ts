import { Router } from 'express';
import {
  createRace,
  getRace,
  getRaceByCode,
  getResults,
  saveResults,
  setAllClear,
  setStartAt,
  shortenCourse,
  updateRace
} from '../db/raceStore';
import { getTracks } from '../db/trackStore';
import { getLastPositions } from '../services/positionCache';
import { flushTracks } from '../services/trackRecorder';
import { broadcast } from '../ws/registry';
import { RaceDto, ResultPublicationDto } from '../types/models';

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
  const updated = updateRace(req.params.id, dto);
  if (!updated) {
    res.status(404).json({ error: 'Race not found' });
    return;
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
