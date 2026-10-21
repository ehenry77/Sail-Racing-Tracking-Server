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
- **Finish photos** read the webcam with `ffmpeg` (or `fswebcam`): `sudo apt install ffmpeg`. See "Finish photos" below.
- **GPS capture** is not available on Linux (it has no standard location service) — type the buoy and start-line
  coordinates in, or capture them with the Windows app.
- **Data** is kept in `~/.local/share/SailRacing/` (database `sailracing.db3`, `settings.json`). If the window
  does not open, look for `crash.log` there, or run with `SR_DEBUG=1` to see the app's warnings in the terminal.
- To continue a race that was started on another PC, use **Take over a race…** on the Race tab.

## Finish photos

The Timing Sheet can photograph each boat as it finishes, with the committee PC's webcam. Switch it on in
**Settings → Take a photo with this camera when a boat finishes** (the camera is the one chosen under "Video input").
Each boat row also has a **Take photo** button for a photo by hand, and **View photo** once it has one.

- The photo is captioned along the bottom with the boat and race, the moment it was taken — date and **24-hour** time
  with seconds and the UTC offset, e.g. `FINISH 2026-10-20 14:32:05 (UTC+02:00)` — and the GPS position: this PC's own
  location where it has one (Windows), otherwise the race's finish line.
- Photos are saved on the PC under `Documents/Sail-Racing photos/<race>/<boat> yyyyMMdd-HHmmss.jpg` and are not sent to
  the server. The finish is photographed when the last lap is entered by hand, or when the server detects it.
- With the automatic photo on, the camera is switched on while the Timing Sheet is open (so its light is lit), which makes
  the photo at the finish immediate.
- Windows: if the camera is blocked, allow it in Settings → Privacy & security → Camera, including "Let desktop apps access
  your camera". Phones/tablets use the system camera screen.

All times shown in the apps are 24-hour (the time pickers too), and so is the clock on the replay page.

## Video of the start

The committee app can record a video of each start. Switch it on in **Settings → Record a video of each start, from one
minute before the start until All Clear**, and choose whether the microphone's sound is included (the camera and
microphone are the ones chosen under "Video input" / "Audio input").

- Pressing **Begin Start Sequence** lines the video up: the camera is switched on a few seconds before the last minute and
  recording begins about 3 s before **T-1:00**, so the whole last minute is on it. It stops the moment **All Clear** is
  pressed. If less than a minute is left when the sequence begins, it starts at once.
- It carries on if you leave the Start Sequence screen, and a **Record video now / Stop recording** button on that screen
  starts or stops it by hand. Cancelling the sequence stops and keeps a recording that has begun, and drops one still waiting.
- Videos are MP4 files in `Documents/Sail-Racing videos/<race>/<race> start yyyyMMdd-HHmmss.mp4` and are not sent to the
  server; **Open video** on the Start Sequence screen plays the last one. (The finish photos are saved the same way.)
- **Time and GPS on the picture.** Like the photos, the video is stamped along the bottom: the race and "START", the date and
  **24-hour** time to the second with the UTC offset (a live clock), and the GPS position — this PC's own when it has one
  (Windows; asked for every 10 s, so it follows the boat), otherwise the finish line. Drawing on the picture needs
  **ffmpeg**: on Windows run `winget install Gyan.FFmpeg` in a terminal (then restart the app; the app also finds it in
  `%LOCALAPPDATA%\SailRacing\ffmpeg\` or next to the program, or where the `SAILRACING_FFMPEG` variable points), on Linux
  `sudo apt install ffmpeg`. Without ffmpeg (or with a build that has no `drawtext` filter) Windows records with its own
  camera interface and the video has no text on the picture; the app says so. In every case a **`.srt` subtitle file** with
  the same name sits beside the video, with the time and GPS for every second — VLC shows it over the video automatically.
- ffmpeg records at the camera's default size. Linux needs the microphone to be reachable through PulseAudio/PipeWire; if
  the microphone cannot be opened, the picture is recorded alone and the app says so. Phones and tablets cannot record a
  start video.

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
