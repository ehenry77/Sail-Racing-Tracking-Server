export interface LastPosition {
  participantId: string;
  lat: number;
  lon: number;
  accuracy: number | null;
  timestamp: string;
}

const MAX_AGE_MS = 6 * 60 * 60 * 1000;

// In memory only: this is just "where was each boat last seen", so a map opened mid-race can show the
// boats straight away instead of waiting for each one's next fix. The durable record for replay is the
// track_points table; a server restart simply empties this until boats report again.
const byRace = new Map<string, Map<string, LastPosition & { receivedAt: number }>>();

export function rememberPosition(raceId: string, position: LastPosition): void {
  if (!Number.isFinite(position.lat) || !Number.isFinite(position.lon)) {
    return;
  }

  let boats = byRace.get(raceId);
  if (!boats) {
    boats = new Map();
    byRace.set(raceId, boats);
  }
  boats.set(position.participantId, { ...position, receivedAt: Date.now() });
}

export function getLastPositions(raceId: string): LastPosition[] {
  const boats = byRace.get(raceId);
  if (!boats) {
    return [];
  }

  return [...boats.values()].map(({ receivedAt: _receivedAt, ...position }) => position);
}

function prune(): void {
  const cutoff = Date.now() - MAX_AGE_MS;
  for (const [raceId, boats] of byRace) {
    for (const [participantId, position] of boats) {
      if (position.receivedAt < cutoff) {
        boats.delete(participantId);
      }
    }
    if (boats.size === 0) {
      byRace.delete(raceId);
    }
  }
}

setInterval(prune, 10 * 60 * 1000).unref();
