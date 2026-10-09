# Sail-Racing server

Node.js/TypeScript server: source of truth for a race once the committee app replicates it, real-time
hub for competitor GPS tracking, and host for the competitor join page and live map.

## Develop

```bash
npm install
npm run dev
```

## Build & run

```bash
npm run build
npm start
```

Environment variables:
- `PORT` — HTTP port (default `3000`)
- `DB_PATH` — SQLite database file path (default `./sailracing.db`)

## Endpoints

See [`/shared/contracts/openapi.yaml`](../shared/contracts/openapi.yaml) for the REST API and
[`/shared/contracts/websocket-events.md`](../shared/contracts/websocket-events.md) for the WebSocket
event catalog.

- Competitor join page: `GET /race/:joinCode` (add `?boat=<participantId>` to preselect a boat)
- Live map: `GET /map?code=<joinCode>` (or `?raceId=<id>`)
- Race replay: `GET /replay?code=<joinCode>` (or `?raceId=<id>`)

## GPS traces and replay

Every position fix streamed by a competitor is recorded (`track_points` table) from the start sequence
until results are published, and the replay page plays them back with a speed control and scrubber.
Fixes are buffered in memory and written in one transaction every 2 seconds, so a hard crash can lose
the last couple of seconds; a normal restart (SIGTERM) flushes first. A race of 30 boats over three hours
is roughly 160,000 rows. The data lives in the same SQLite file as the races, so back that file up (and
consider pointing `DB_PATH` outside the deployed folder).

## Deploy

```bash
docker build -t sail-racing-server .
docker run -p 3000:3000 -v sailracing-data:/app/data sail-racing-server
```

Mount `/app/data` as a persistent volume so the SQLite database survives container restarts.
