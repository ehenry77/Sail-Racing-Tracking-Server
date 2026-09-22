import { DatabaseSync } from 'node:sqlite';

export function createSchema(db: DatabaseSync): void {
  db.exec(`
    CREATE TABLE IF NOT EXISTS races (
      id TEXT PRIMARY KEY,
      contractVersion INTEGER NOT NULL DEFAULT 1,
      joinCode TEXT UNIQUE,
      name TEXT NOT NULL,
      status TEXT NOT NULL DEFAULT 'Setup',
      fleetId TEXT,
      fleetName TEXT,
      lapsDefault INTEGER NOT NULL DEFAULT 3,
      finishSameAsStart INTEGER NOT NULL DEFAULT 1,
      finishLatitude REAL,
      finishLongitude REAL,
      startAt TEXT,
      shortenCourseAppliedAt TEXT,
      createdAt TEXT NOT NULL,
      updatedAt TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS participants (
      id TEXT NOT NULL,
      raceId TEXT NOT NULL REFERENCES races(id) ON DELETE CASCADE,
      name TEXT NOT NULL,
      helm TEXT NOT NULL,
      tcf REAL NOT NULL DEFAULT 1.0,
      PRIMARY KEY (raceId, id)
    );

    CREATE TABLE IF NOT EXISTS buoys (
      id TEXT NOT NULL,
      raceId TEXT NOT NULL REFERENCES races(id) ON DELETE CASCADE,
      sequence INTEGER NOT NULL,
      name TEXT NOT NULL,
      latitude REAL,
      longitude REAL,
      capturedViaGps INTEGER NOT NULL DEFAULT 0,
      PRIMARY KEY (raceId, id)
    );

    CREATE TABLE IF NOT EXISTS start_lines (
      raceId TEXT PRIMARY KEY REFERENCES races(id) ON DELETE CASCADE,
      committeeLatitude REAL,
      committeeLongitude REAL,
      pinLatitude REAL,
      pinLongitude REAL
    );

    CREATE TABLE IF NOT EXISTS race_boat_state (
      raceId TEXT NOT NULL REFERENCES races(id) ON DELETE CASCADE,
      participantId TEXT NOT NULL,
      laps INTEGER NOT NULL,
      lapsCompleted INTEGER NOT NULL DEFAULT 0,
      isOnFinalLap INTEGER NOT NULL DEFAULT 0,
      status TEXT NOT NULL DEFAULT 'Racing',
      finishTime TEXT,
      elapsedSeconds REAL,
      correctedSeconds REAL,
      rank INTEGER,
      currentTargetIndex INTEGER NOT NULL DEFAULT 0,
      armed INTEGER NOT NULL DEFAULT 1,
      PRIMARY KEY (raceId, participantId)
    );

    CREATE TABLE IF NOT EXISTS results (
      raceId TEXT PRIMARY KEY REFERENCES races(id) ON DELETE CASCADE,
      publishedAt TEXT NOT NULL,
      resultsJson TEXT NOT NULL
    );
  `);
}
