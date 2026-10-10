import { getRaceStatus } from '../db/raceStore';
import { insertTrackPoints, TrackPoint } from '../db/trackStore';

const FLUSH_INTERVAL_MS = 2000;
const MIN_POINT_SPACING_MS = 1500; // per boat — protects the database from a client flooding fixes
const STATUS_CACHE_MS = 5000;
const MAX_BUFFERED_POINTS = 50000; // if the database keeps failing, don't grow without bound
const MAX_CLOCK_SKEW_MS = 24 * 60 * 60 * 1000;

let buffer: TrackPoint[] = [];
const lastTimestamp = new Map<string, number>();
const statusCache = new Map<string, { recording: boolean; at: number }>();

/** Fixes are kept from the start sequence through the race, not while a race is still being set up or after it finishes. */
function isRecording(raceId: string): boolean {
  const now = Date.now();
  const cached = statusCache.get(raceId);
  if (cached && now - cached.at < STATUS_CACHE_MS) {
    return cached.recording;
  }

  const status = getRaceStatus(raceId);
  const recording = status === 'StartSequence' || status === 'Racing';
  statusCache.set(raceId, { recording, at: now });
  return recording;
}

/** Forgets a race's buffered-but-unwritten fixes and cached recording state (see deleteTracks). */
export function forgetRace(raceId: string): void {
  buffer = buffer.filter((p) => p.raceId !== raceId);
  statusCache.delete(raceId);
  for (const key of [...lastTimestamp.keys()]) {
    if (key.startsWith(`${raceId}:`)) lastTimestamp.delete(key);
  }
}

const round6 = (n: number): number => Math.round(n * 1e6) / 1e6;

export function recordPosition(
  raceId: string,
  participantId: string,
  lat: number,
  lon: number,
  accuracy: number | null,
  timestampIso: string
): void {
  if (!participantId || !Number.isFinite(lat) || !Number.isFinite(lon) || Math.abs(lat) > 90 || Math.abs(lon) > 180) {
    return;
  }

  if (!isRecording(raceId)) {
    return;
  }

  // Phone GPS timestamps are accurate, but a bad clock shouldn't be able to place a fix days away.
  let ts = Date.parse(timestampIso);
  if (!Number.isFinite(ts) || Math.abs(ts - Date.now()) > MAX_CLOCK_SKEW_MS) {
    ts = Date.now();
  }

  const key = `${raceId}:${participantId}`;
  const previous = lastTimestamp.get(key);
  if (previous !== undefined && Math.abs(ts - previous) < MIN_POINT_SPACING_MS) {
    return;
  }
  lastTimestamp.set(key, ts);

  buffer.push({
    raceId,
    participantId,
    ts,
    lat: round6(lat),
    lon: round6(lon),
    accuracy: accuracy !== null && Number.isFinite(accuracy) ? accuracy : null
  });
}

/** Writes buffered fixes in one transaction. Safe to call any time; a failure keeps the points for the next try. */
export function flushTracks(): void {
  if (buffer.length === 0) {
    return;
  }

  const batch = buffer;
  buffer = [];
  try {
    insertTrackPoints(batch);
  } catch (err) {
    console.error('Could not write track points, will retry:', err);
    buffer = batch.concat(buffer).slice(-MAX_BUFFERED_POINTS);
  }
}

export function startTrackRecorder(): void {
  setInterval(flushTracks, FLUSH_INTERVAL_MS).unref();

  // A host restart sends SIGTERM — write what's buffered instead of losing the last couple of seconds.
  for (const signal of ['SIGTERM', 'SIGINT'] as const) {
    process.once(signal, () => {
      flushTracks();
      process.exit(0);
    });
  }
}
