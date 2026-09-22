import { getBoatTrackerState, getRace, getRaceStatus, updateBoatTrackerState } from '../db/raceStore';
import { distanceToSegmentMeters, haversineMeters, LatLon, segmentsIntersect } from './geometry';

const MARK_RADIUS_METERS = 20;
const LINE_DISARM_DISTANCE_METERS = 20;

// Ephemeral only: raw GPS fixes are not persisted, just the last fix per boat needed to test the
// next movement segment against the course geometry.
const lastPositionByBoat = new Map<string, LatLon>();

export interface TrackerEvent {
  type: 'lapCompleted' | 'finalLap' | 'finished';
  payload: Record<string, unknown>;
}

/**
 * Feeds one GPS fix into the per-race, per-boat course state machine: start/finish line ->
 * buoy 0 -> buoy 1 -> ... -> back to start/finish, repeating per lap. Only active once the race
 * is in Racing status (post All-Clear). Returns the events to broadcast, if any.
 */
export function processPosition(
  raceId: string,
  participantId: string,
  position: LatLon,
  timestampIso: string
): TrackerEvent[] {
  if (getRaceStatus(raceId) !== 'Racing') {
    return [];
  }

  const race = getRace(raceId);
  if (!race) {
    return [];
  }

  const state = getBoatTrackerState(raceId, participantId);
  if (!state || state.status !== 'Racing') {
    return [];
  }

  const key = `${raceId}:${participantId}`;
  const prev = lastPositionByBoat.get(key);
  lastPositionByBoat.set(key, position);

  if (!prev) {
    return []; // need two fixes to test a crossing
  }

  const events: TrackerEvent[] = [];
  const marks = race.buoys;

  const lineA: LatLon | null =
    race.startLine.committeeLatitude != null && race.startLine.committeeLongitude != null
      ? { lat: race.startLine.committeeLatitude, lon: race.startLine.committeeLongitude }
      : null;
  const lineB: LatLon | null =
    race.startLine.pinLatitude != null && race.startLine.pinLongitude != null
      ? { lat: race.startLine.pinLatitude, lon: race.startLine.pinLongitude }
      : null;
  const finishIsLine = race.finishSameAsStart || race.finishLatitude == null || race.finishLongitude == null;
  const finishPoint: LatLon | null = finishIsLine
    ? null
    : { lat: race.finishLatitude as number, lon: race.finishLongitude as number };
  // A start/finish line with either endpoint not yet captured can't be tested for a crossing at all.
  const finishLineReady = !finishIsLine || (lineA != null && lineB != null);

  if (state.currentTargetIndex < marks.length) {
    const mark = marks[state.currentTargetIndex];
    if (mark.latitude == null || mark.longitude == null) {
      // No coordinates captured for this mark yet — can't gate on it, so let the boat proceed to
      // the next target rather than getting stuck waiting for a GPS test that can never pass.
      state.currentTargetIndex += 1;
    } else {
      const dist = haversineMeters(position, { lat: mark.latitude, lon: mark.longitude });
      if (dist <= MARK_RADIUS_METERS) {
        state.currentTargetIndex += 1;
      }
    }
  } else if (!finishLineReady) {
    // Start/finish line incomplete: lap counting can't run until it's captured. Leave state as-is.
  } else {
    const crossed = finishIsLine
      ? state.armed && segmentsIntersect(prev, position, lineA!, lineB!)
      : state.armed && haversineMeters(position, finishPoint!) <= MARK_RADIUS_METERS;

    if (crossed) {
      state.lapsCompleted += 1;
      state.currentTargetIndex = 0;
      state.armed = false;

      events.push({
        type: 'lapCompleted',
        payload: {
          participantId,
          lapsCompleted: state.lapsCompleted,
          isFinalLap: state.lapsCompleted === state.laps - 1
        }
      });

      if (state.lapsCompleted === state.laps - 1 && !state.isOnFinalLap) {
        state.isOnFinalLap = true;
        events.push({ type: 'finalLap', payload: { participantId } });
      }

      if (state.lapsCompleted >= state.laps) {
        state.status = 'Finished';
        state.isOnFinalLap = false;
        state.finishTime = timestampIso;
        state.elapsedSeconds = race.startAt
          ? (new Date(timestampIso).getTime() - new Date(race.startAt).getTime()) / 1000
          : null;

        events.push({
          type: 'finished',
          payload: { participantId, finishTime: state.finishTime, elapsedSeconds: state.elapsedSeconds }
        });
      }
    } else if (!state.armed) {
      const distFromFinish = finishIsLine
        ? distanceToSegmentMeters(position, lineA!, lineB!)
        : haversineMeters(position, finishPoint!);
      if (distFromFinish > LINE_DISARM_DISTANCE_METERS) {
        state.armed = true;
      }
    }
  }

  updateBoatTrackerState(state);
  return events;
}
