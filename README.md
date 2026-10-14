# Sail-Racing

A regatta management system: a committee app for running races — .NET MAUI on Windows (and iOS/Android),
Avalonia on Linux (and Windows/macOS) — and a Node.js server for live GPS tracking, a competitor join page,
a live map and race replay.

## Project structure

```
core/      SailRacing.Core — everything that is not user interface, shared by both committee apps: models,
           local database, server client, sync, take-over, start-sequence logic and the view models
           (+ SailRacing.Core.Tests)
app/       .NET MAUI committee app (Windows, iOS, Android) — screens and MAUI platform services
desktop/   Avalonia committee app (Linux, Windows, macOS) — screens and desktop platform services
           (+ SailRacing.Desktop.Tests, headless UI tests)
server/    Node.js/TypeScript server — race replication, WebSocket tracking hub, competitor & map pages
shared/    Hand-maintained contract between the server and the apps: JSON Schema, OpenAPI, WebSocket event catalog
```

Both committee apps run the same code from `core/` (the same view models, database, sync and start sequence);
only the screens and the platform services — navigation, dialogs, files, clipboard, speech, location, settings —
are written twice, behind the interfaces in `core/SailRacing.Core/Platform/PlatformServices.cs`.

## Committee app on Windows (`app/`, .NET MAUI)

Requires the .NET SDK with the MAUI workload installed:

```bash
dotnet workload install maui
```

Build and run:

```bash
dotnet build app/SailRacing.csproj
dotnet build app/SailRacing.csproj -t:Run -f net9.0-windows10.0.19041.0
```

Point the app at a deployed server in the Settings tab (stored by [`AppConfig`](core/SailRacing.Core/Services/AppConfig.cs)).

## Committee app on Linux (`desktop/`, Avalonia)

.NET MAUI has no Linux support, so Linux (and, if you like, Windows and macOS) runs this Avalonia app instead.
It has the same four tabs and screens and the same start sequence, timing sheet, results, take-over and offline
behaviour. Needs the .NET 9 SDK to build; the published build needs nothing installed.

```bash
# run from source
dotnet run --project desktop/SailRacing.Desktop

# a self-contained build to copy to the Linux PC (no .NET needed there)
dotnet publish desktop/SailRacing.Desktop -c Release -r linux-x64 --self-contained -o publish/linux-x64
./publish/linux-x64/SailRacing
```

On the Linux PC:

- **Voice announcements** need a speech tool: `sudo apt install espeak-ng` (also works with `espeak` or
  speech-dispatcher's `spd-say`). Without one the app runs normally but silently.
- **GPS capture** is not available on Linux (it has no standard location service) — type the buoy and start-line
  coordinates in, or capture them with the Windows app.
- **Data** is kept in `~/.local/share/SailRacing/` (database `sailracing.db3`, `settings.json`). If the window
  does not open, look for `crash.log` there, or run with `SR_DEBUG=1` to see the app's warnings in the terminal.
- To continue a race that was started on another PC, use **Take over a race…** on the Race tab.

## Tests

```bash
dotnet test core/SailRacing.Core.Tests          # shared logic: settings, start-sequence timeline, join codes, wiring
dotnet test desktop/SailRacing.Desktop.Tests    # renders every screen headless and walks the main flows
```

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
