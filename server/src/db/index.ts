import path from 'path';
import { DatabaseSync } from 'node:sqlite';
import { createSchema } from './schema';

const dbPath = process.env.DB_PATH ?? path.join(__dirname, '..', '..', 'sailracing.db');

export const db = new DatabaseSync(dbPath);
db.exec('PRAGMA journal_mode = WAL');
createSchema(db);

// Databases created before a column existed don't get it from CREATE TABLE IF NOT EXISTS.
const boatStateColumns = db.prepare('PRAGMA table_info(race_boat_state)').all() as { name: string }[];
if (!boatStateColumns.some((c) => c.name === 'timingUpdatedAt')) {
  db.exec('ALTER TABLE race_boat_state ADD COLUMN timingUpdatedAt INTEGER NOT NULL DEFAULT 0');
}

/** node:sqlite has no built-in transaction helper (unlike better-sqlite3) — wrap statements manually. */
export function withTransaction<T>(fn: () => T): T {
  db.exec('BEGIN');
  try {
    const result = fn();
    db.exec('COMMIT');
    return result;
  } catch (err) {
    db.exec('ROLLBACK');
    throw err;
  }
}
