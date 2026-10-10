import { db, withTransaction } from './index';

export interface TrackPoint {
  raceId: string;
  participantId: string;
  ts: number;
  lat: number;
  lon: number;
  accuracy: number | null;
}

/** [epoch ms, lat, lon] — compact on the wire since a race can hold tens of thousands of points. */
export type TrackSample = [number, number, number];

export interface BoatTrack {
  participantId: string;
  points: TrackSample[];
}

const insertStmt = db.prepare(
  'INSERT INTO track_points (raceId, participantId, ts, lat, lon, accuracy) VALUES (?, ?, ?, ?, ?, ?)'
);

export function insertTrackPoints(points: TrackPoint[]): void {
  if (points.length === 0) {
    return;
  }

  withTransaction(() => {
    for (const p of points) {
      insertStmt.run(p.raceId, p.participantId, p.ts, p.lat, p.lon, p.accuracy);
    }
  });
}

/**
 * All recorded points for a race, per boat, oldest first. A live map doesn't need a whole race at full
 * resolution on every load (a 30-boat race is ~160,000 points), so callers can limit it:
 * `sinceMs` keeps only points at or after that time, and `minGapMs` thins each boat's points so
 * consecutive ones are at least that far apart.
 */
export function getTracks(raceId: string, sinceMs?: number, minGapMs?: number): BoatTrack[] {
  const sql =
    'SELECT participantId, ts, lat, lon FROM track_points WHERE raceId = ?' +
    (sinceMs !== undefined ? ' AND ts >= ?' : '') +
    ' ORDER BY participantId, ts';
  const rows = (
    sinceMs !== undefined ? db.prepare(sql).all(raceId, sinceMs) : db.prepare(sql).all(raceId)
  ) as { participantId: string; ts: number; lat: number; lon: number }[];

  const byBoat = new Map<string, TrackSample[]>();
  for (const row of rows) {
    let points = byBoat.get(row.participantId);
    if (!points) {
      points = [];
      byBoat.set(row.participantId, points);
    }

    if (minGapMs && points.length > 0 && row.ts - points[points.length - 1][0] < minGapMs) {
      continue;
    }
    points.push([row.ts, row.lat, row.lon]);
  }

  return [...byBoat.entries()].map(([participantId, points]) => ({ participantId, points }));
}

/** Drops a race's recorded fixes — used when its start sequence is cancelled, so the aborted lead-in doesn't skew the replay. */
export function deleteTracks(raceId: string): void {
  db.prepare('DELETE FROM track_points WHERE raceId = ?').run(raceId);
}
