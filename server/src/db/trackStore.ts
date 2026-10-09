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

export function getTracks(raceId: string): BoatTrack[] {
  const rows = db
    .prepare('SELECT participantId, ts, lat, lon FROM track_points WHERE raceId = ? ORDER BY participantId, ts')
    .all(raceId) as { participantId: string; ts: number; lat: number; lon: number }[];

  const byBoat = new Map<string, TrackSample[]>();
  for (const row of rows) {
    let points = byBoat.get(row.participantId);
    if (!points) {
      points = [];
      byBoat.set(row.participantId, points);
    }
    points.push([row.ts, row.lat, row.lon]);
  }

  return [...byBoat.entries()].map(([participantId, points]) => ({ participantId, points }));
}
