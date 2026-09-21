import path from 'path';
import { DatabaseSync } from 'node:sqlite';
import { createSchema } from './schema';

const dbPath = process.env.DB_PATH ?? path.join(__dirname, '..', '..', 'sailracing.db');

export const db = new DatabaseSync(dbPath);
db.exec('PRAGMA journal_mode = WAL');
createSchema(db);

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
