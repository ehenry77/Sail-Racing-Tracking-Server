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

- Competitor join page: `GET /race/:joinCode`
- Live map: `GET /map?raceId=<id>`

## Deploy

```bash
docker build -t sail-racing-server .
docker run -p 3000:3000 -v sailracing-data:/app/data sail-racing-server
```

Mount `/app/data` as a persistent volume so the SQLite database survives container restarts.
