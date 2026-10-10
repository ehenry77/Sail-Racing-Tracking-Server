import { randomUUID } from 'crypto';
import { db, withTransaction } from './index';
import { RaceDto, RaceParticipantStatus, ResultPublicationDto } from '../types/models';

const JOIN_CODE_CHARS = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'; // no 0/O/1/I to avoid confusion

function generateJoinCode(): string {
  let code = '';
  for (let i = 0; i < 5; i++) {
    code += JOIN_CODE_CHARS[Math.floor(Math.random() * JOIN_CODE_CHARS.length)];
  }
  return code;
}

function uniqueJoinCode(): string {
  const existing = db.prepare('SELECT 1 FROM races WHERE joinCode = ?');
  let code = generateJoinCode();
  while (existing.get(code)) {
    code = generateJoinCode();
  }
  return code;
}

const upsertRaceStmt = db.prepare(`
  INSERT INTO races (id, contractVersion, joinCode, name, status, fleetId, fleetName, lapsDefault,
                      finishSameAsStart, finishLatitude, finishLongitude, startAt, shortenCourseAppliedAt,
                      createdAt, updatedAt)
  VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
  ON CONFLICT(id) DO UPDATE SET
    name = excluded.name,
    status = excluded.status,
    fleetId = excluded.fleetId,
    fleetName = excluded.fleetName,
    lapsDefault = excluded.lapsDefault,
    finishSameAsStart = excluded.finishSameAsStart,
    finishLatitude = excluded.finishLatitude,
    finishLongitude = excluded.finishLongitude,
    startAt = excluded.startAt,
    shortenCourseAppliedAt = excluded.shortenCourseAppliedAt,
    updatedAt = excluded.updatedAt
`);

const deleteParticipantsStmt = db.prepare('DELETE FROM participants WHERE raceId = ?');
const insertParticipantStmt = db.prepare(
  'INSERT INTO participants (id, raceId, name, helm, tcf) VALUES (?, ?, ?, ?, ?)'
);

const deleteBuoysStmt = db.prepare('DELETE FROM buoys WHERE raceId = ?');
const insertBuoyStmt = db.prepare(`
  INSERT INTO buoys (id, raceId, sequence, name, latitude, longitude, capturedViaGps)
  VALUES (?, ?, ?, ?, ?, ?, ?)
`);

const upsertStartLineStmt = db.prepare(`
  INSERT INTO start_lines (raceId, committeeLatitude, committeeLongitude, pinLatitude, pinLongitude)
  VALUES (?, ?, ?, ?, ?)
  ON CONFLICT(raceId) DO UPDATE SET
    committeeLatitude = excluded.committeeLatitude,
    committeeLongitude = excluded.committeeLongitude,
    pinLatitude = excluded.pinLatitude,
    pinLongitude = excluded.pinLongitude
`);

const upsertBoatStateStmt = db.prepare(`
  INSERT INTO race_boat_state (raceId, participantId, laps, lapsCompleted, isOnFinalLap, status,
                                finishTime, elapsedSeconds, correctedSeconds, rank)
  VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
  ON CONFLICT(raceId, participantId) DO UPDATE SET
    laps = excluded.laps
`);

function replaceRaceAggregate(dto: RaceDto, id: string, joinCode: string, createdAt: string): void {
  withTransaction(() => {
    upsertRaceStmt.run(
      id,
      dto.contractVersion ?? 1,
      joinCode,
      dto.name,
      dto.status,
      dto.fleet.id,
      dto.fleet.name,
      dto.lapsDefault,
      dto.finishSameAsStart ? 1 : 0,
      dto.finishLatitude ?? null,
      dto.finishLongitude ?? null,
      dto.startAt ?? null,
      dto.shortenCourseAppliedAt ?? null,
      createdAt,
      new Date().toISOString()
    );

    deleteParticipantsStmt.run(id);
    for (const p of dto.fleet.participants) {
      insertParticipantStmt.run(p.id, id, p.name, p.helm, p.tcf);
    }

    deleteBuoysStmt.run(id);
    for (const b of dto.buoys) {
      insertBuoyStmt.run(b.id, id, b.sequence, b.name, b.latitude, b.longitude, b.capturedViaGps ? 1 : 0);
    }

    upsertStartLineStmt.run(
      id,
      dto.startLine.committeeLatitude,
      dto.startLine.committeeLongitude,
      dto.startLine.pinLatitude,
      dto.startLine.pinLongitude
    );

    for (const rp of dto.raceParticipants) {
      upsertBoatStateStmt.run(
        id,
        rp.participantId,
        rp.laps,
        rp.lapsCompleted ?? 0,
        rp.isOnFinalLap ? 1 : 0,
        rp.status ?? 'Racing',
        rp.finishTime ?? null,
        rp.elapsedSeconds ?? null,
        rp.correctedSeconds ?? null,
        rp.rank ?? null
      );
    }
  });
}

export function createRace(dto: RaceDto): { raceId: string; joinCode: string } {
  const id = dto.id || randomUUID();

  // Idempotent: the committee app can re-send a create for a race the server already has (e.g. its
  // local copy lost the join code). The upsert keeps the stored row's code, so returning a freshly
  // generated one here would hand the app a code that no longer matches the server's.
  const existing = db.prepare('SELECT createdAt, joinCode FROM races WHERE id = ?').get(id) as
    | { createdAt: string; joinCode: string }
    | undefined;
  if (existing) {
    replaceRaceAggregate(dto, id, existing.joinCode, existing.createdAt);
    return { raceId: id, joinCode: existing.joinCode };
  }

  const joinCode = uniqueJoinCode();
  const createdAt = new Date().toISOString();
  replaceRaceAggregate(dto, id, joinCode, createdAt);
  return { raceId: id, joinCode };
}

export function updateRace(id: string, dto: RaceDto): boolean {
  const existing = db.prepare('SELECT createdAt, joinCode FROM races WHERE id = ?').get(id) as
    | { createdAt: string; joinCode: string }
    | undefined;
  if (!existing) {
    return false;
  }

  replaceRaceAggregate(dto, id, existing.joinCode, existing.createdAt);
  return true;
}

function hydrateRace(raceRow: any): RaceDto {
  const participants = db.prepare('SELECT * FROM participants WHERE raceId = ?').all(raceRow.id) as any[];
  const buoys = db
    .prepare('SELECT * FROM buoys WHERE raceId = ? ORDER BY sequence')
    .all(raceRow.id) as any[];
  const startLine = db.prepare('SELECT * FROM start_lines WHERE raceId = ?').get(raceRow.id) as any;
  const boatStates = db.prepare('SELECT * FROM race_boat_state WHERE raceId = ?').all(raceRow.id) as any[];

  return {
    id: raceRow.id,
    contractVersion: raceRow.contractVersion,
    joinCode: raceRow.joinCode,
    name: raceRow.name,
    status: raceRow.status,
    fleet: {
      id: raceRow.fleetId,
      name: raceRow.fleetName,
      participantIds: participants.map((p) => p.id),
      participants: participants.map((p) => ({ id: p.id, name: p.name, helm: p.helm, tcf: p.tcf }))
    },
    startLine: startLine
      ? {
          committeeLatitude: startLine.committeeLatitude,
          committeeLongitude: startLine.committeeLongitude,
          pinLatitude: startLine.pinLatitude,
          pinLongitude: startLine.pinLongitude
        }
      : { committeeLatitude: 0, committeeLongitude: 0, pinLatitude: 0, pinLongitude: 0 },
    finishSameAsStart: !!raceRow.finishSameAsStart,
    finishLatitude: raceRow.finishLatitude,
    finishLongitude: raceRow.finishLongitude,
    buoys: buoys.map((b) => ({
      id: b.id,
      sequence: b.sequence,
      name: b.name,
      latitude: b.latitude,
      longitude: b.longitude,
      capturedViaGps: !!b.capturedViaGps
    })),
    lapsDefault: raceRow.lapsDefault,
    raceParticipants: boatStates.map((s) => ({
      participantId: s.participantId,
      laps: s.laps,
      lapsCompleted: s.lapsCompleted,
      isOnFinalLap: !!s.isOnFinalLap,
      status: s.status as RaceParticipantStatus,
      finishTime: s.finishTime,
      elapsedSeconds: s.elapsedSeconds,
      correctedSeconds: s.correctedSeconds,
      rank: s.rank
    })),
    startAt: raceRow.startAt,
    shortenCourseAppliedAt: raceRow.shortenCourseAppliedAt
  };
}

export function getRace(id: string): RaceDto | null {
  const row = db.prepare('SELECT * FROM races WHERE id = ?').get(id);
  return row ? hydrateRace(row) : null;
}

export function getRaceByCode(code: string): RaceDto | null {
  const row = db.prepare('SELECT * FROM races WHERE joinCode = ?').get(code.toUpperCase());
  return row ? hydrateRace(row) : null;
}

export function setStartAt(raceId: string, startAt: string): void {
  db.prepare('UPDATE races SET startAt = ?, status = ?, updatedAt = ? WHERE id = ?').run(
    startAt,
    'StartSequence',
    new Date().toISOString(),
    raceId
  );
}

/** Puts every boat back to the pre-race state (no laps, not finished) — used when the start sequence is cancelled. */
export function resetBoatStates(raceId: string): void {
  db.prepare(
    `UPDATE race_boat_state SET lapsCompleted = 0, isOnFinalLap = 0, status = 'Racing', finishTime = NULL,
       elapsedSeconds = NULL, correctedSeconds = NULL, rank = NULL, currentTargetIndex = 0, armed = 1
     WHERE raceId = ?`
  ).run(raceId);
}

export function setAllClear(raceId: string): void {
  db.prepare('UPDATE races SET status = ?, updatedAt = ? WHERE id = ?').run(
    'Racing',
    new Date().toISOString(),
    raceId
  );
}

export function shortenCourse(raceId: string, newLaps: { participantId: string; laps: number }[]): void {
  const update = db.prepare('UPDATE race_boat_state SET laps = ? WHERE raceId = ? AND participantId = ?');
  withTransaction(() => {
    for (const item of newLaps) {
      update.run(item.laps, raceId, item.participantId);
    }
    db.prepare('UPDATE races SET shortenCourseAppliedAt = ?, updatedAt = ? WHERE id = ?').run(
      new Date().toISOString(),
      new Date().toISOString(),
      raceId
    );
  });
}

export function saveResults(payload: ResultPublicationDto): void {
  withTransaction(() => {
    db.prepare(`
      INSERT INTO results (raceId, publishedAt, resultsJson)
      VALUES (?, ?, ?)
      ON CONFLICT(raceId) DO UPDATE SET publishedAt = excluded.publishedAt, resultsJson = excluded.resultsJson
    `).run(payload.raceId, payload.publishedAt, JSON.stringify(payload.results));

    const rankUpdate = db.prepare(
      'UPDATE race_boat_state SET status = ?, rank = ?, correctedSeconds = ? WHERE raceId = ? AND participantId = ?'
    );
    for (const entry of payload.results) {
      rankUpdate.run(entry.status, entry.rank, entry.correctedSeconds, payload.raceId, entry.participantId);
    }

    // Publishing results ends the race on the server too (the app only pushes status on a race save),
    // which is what stops GPS trace recording.
    db.prepare('UPDATE races SET status = ?, updatedAt = ? WHERE id = ?').run(
      'Finished',
      new Date().toISOString(),
      payload.raceId
    );
  });
}

export function getResults(raceId: string): ResultPublicationDto | null {
  const row = db.prepare('SELECT * FROM results WHERE raceId = ?').get(raceId) as
    | { raceId: string; publishedAt: string; resultsJson: string }
    | undefined;
  if (!row) {
    return null;
  }

  return { raceId: row.raceId, publishedAt: row.publishedAt, results: JSON.parse(row.resultsJson) };
}

export interface BoatTrackerState {
  raceId: string;
  participantId: string;
  laps: number;
  lapsCompleted: number;
  isOnFinalLap: boolean;
  status: RaceParticipantStatus;
  finishTime: string | null;
  elapsedSeconds: number | null;
  currentTargetIndex: number;
  armed: boolean;
}

export function getBoatTrackerState(raceId: string, participantId: string): BoatTrackerState | null {
  const row = db
    .prepare('SELECT * FROM race_boat_state WHERE raceId = ? AND participantId = ?')
    .get(raceId, participantId) as any;
  if (!row) {
    return null;
  }

  return {
    raceId: row.raceId,
    participantId: row.participantId,
    laps: row.laps,
    lapsCompleted: row.lapsCompleted,
    isOnFinalLap: !!row.isOnFinalLap,
    status: row.status,
    finishTime: row.finishTime,
    elapsedSeconds: row.elapsedSeconds,
    currentTargetIndex: row.currentTargetIndex,
    armed: !!row.armed
  };
}

const updateTrackerStateStmt = db.prepare(`
  UPDATE race_boat_state
  SET lapsCompleted = ?,
      isOnFinalLap = ?,
      status = ?,
      finishTime = ?,
      elapsedSeconds = ?,
      currentTargetIndex = ?,
      armed = ?
  WHERE raceId = ? AND participantId = ?
`);

export function updateBoatTrackerState(state: BoatTrackerState): void {
  updateTrackerStateStmt.run(
    state.lapsCompleted,
    state.isOnFinalLap ? 1 : 0,
    state.status,
    state.finishTime,
    state.elapsedSeconds,
    state.currentTargetIndex,
    state.armed ? 1 : 0,
    state.raceId,
    state.participantId
  );
}

export function getRaceStatus(raceId: string): RaceDto['status'] | null {
  const row = db.prepare('SELECT status FROM races WHERE id = ?').get(raceId) as
    | { status: RaceDto['status'] }
    | undefined;
  return row?.status ?? null;
}
