# WebSocket event catalog

Plain `ws` server. Browser clients use the native `WebSocket` API; the MAUI committee app uses `ClientWebSocket`. Connect to `wss://<server>/ws`.

## Client → Server

### `join`
Sent immediately after connecting.
```json
{ "type": "join", "raceId": "uuid", "role": "competitor|spectator|committee", "participantId": "uuid|null" }
```

### `position`
Sent by a competitor's browser page as GPS fixes arrive (throttled client-side to movement/time thresholds).
```json
{ "type": "position", "raceId": "uuid", "participantId": "uuid", "lat": 0.0, "lon": 0.0, "accuracy": 0.0, "timestamp": "ISO-8601" }
```

## Server → Clients

### `positionUpdate`
Broadcast to all clients in the race whenever any competitor's position changes.
```json
{ "type": "positionUpdate", "participantId": "uuid", "lat": 0.0, "lon": 0.0, "accuracy": 0.0, "timestamp": "ISO-8601" }
```

### `lapCompleted`
```json
{ "type": "lapCompleted", "participantId": "uuid", "lapsCompleted": 2, "isFinalLap": false }
```

### `finalLap`
Fired once, when `lapsCompleted == laps - 1`.
```json
{ "type": "finalLap", "participantId": "uuid" }
```

### `finished`
Fired on the finish-line crossing that completes the last lap.
```json
{ "type": "finished", "participantId": "uuid", "finishTime": "ISO-8601", "elapsedSeconds": 0.0 }
```

### `courseShortened`
```json
{ "type": "courseShortened", "raceParticipants": [ { "participantId": "uuid", "laps": 3 } ] }
```

### `raceStatusChanged`
```json
{ "type": "raceStatusChanged", "status": "Setup|StartSequence|Racing|Finished" }
```

### `error`
```json
{ "type": "error", "message": "..." }
```

Every message carries a top-level `type` discriminator. Lap-counting/finish detection (`lapCompleted`, `finalLap`, `finished`) is only active once `raceStatusChanged` has moved the race to `Racing` (i.e. after the committee's All Clear).
