# Sail-Racing

A regatta management system: a .NET MAUI committee app (Windows, iOS, Android) for running races, and
a Node.js server for live GPS tracking, a competitor join page, and a live map.

## Project structure

```
app/       .NET MAUI committee app — Competitors / Fleets / Race tabs
server/    Node.js/TypeScript server — race replication, WebSocket tracking hub, competitor & map pages
shared/    Hand-maintained contract between the two: JSON Schema, OpenAPI, WebSocket event catalog
```

## Committee app (`app/`)

Requires the .NET SDK with the MAUI workload installed:

```bash
dotnet workload install maui
```

Build and run:

```bash
dotnet build app/SailRacing.csproj
dotnet build app/SailRacing.csproj -t:Run -f net9.0-windows10.0.19041.0
```

Point the app at a deployed server by setting `AppConfig.ServerBaseUrl` (see [`app/Services/AppConfig.cs`](app/Services/AppConfig.cs)).

## Server (`server/`)

```bash
cd server
npm install
npm run dev
```

See [`server/README.md`](server/README.md) for build, deploy, and API details.

## Contracts (`shared/`)

`shared/contracts/` is the source of truth for the wire format both sides implement independently
(C# on the app side, TypeScript on the server side) — see
[`shared/contracts/openapi.yaml`](shared/contracts/openapi.yaml) and
[`shared/contracts/websocket-events.md`](shared/contracts/websocket-events.md).
