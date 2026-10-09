(() => {
  const raceNameEl = document.getElementById('raceName');
  const boatListEl = document.getElementById('boatList');

  // The map opens with either ?code=<join code> (what the committee app links to) or ?raceId=<race id>.
  const params = new URLSearchParams(window.location.search);
  const code = params.get('code');
  let raceId = params.get('raceId');

  if (!raceId && !code) {
    raceNameEl.textContent = 'No race specified';
    return;
  }

  const STALE_AFTER_MS = 30000;

  // Trails: history is fetched from the recorded fixes (the last half hour, thinned to one point every
  // 5 s so a phone isn't sent a whole race), then extended live from the socket.
  const TRAIL_WINDOW_MS = 30 * 60 * 1000;
  const TRAIL_MIN_GAP_MS = 5000;
  const MAX_TRAIL_POINTS = 3000;
  const MIN_TRAIL_STEP_M = 3; // ignore GPS jitter while a boat sits still

  // One colour per boat, by its place in the race's boat list. The golden-angle step keeps neighbouring
  // boats' hues far apart however many there are, and alternating lightness separates close hues further.
  // The replay page uses the same function so a boat keeps its colour there.
  const boatColor = (i) => `hsl(${Math.round((i * 137.508) % 360)}, 75%, ${i % 2 ? 38 : 50}%)`;

  const map = L.map('map').setView([0, 0], 13);
  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    attribution: '&copy; OpenStreetMap contributors',
    maxZoom: 19
  }).addTo(map);

  const courseLatLngs = []; // start line and marks that have coordinates
  const boats = new Map(); // participantId -> state (see ensureBoat)

  // --- Keeping the boats in view -------------------------------------------------------------------
  // A race can have no course coordinates at all (they're optional), so there may be nothing to frame the
  // map around — it would sit at 0,0 and the boats would be drawn thousands of km off-screen. Instead the
  // map frames the course plus every boat, and re-frames whenever one wanders out of view, until the
  // viewer pans or zooms themselves (then "Fit all boats" brings it back).
  let autoFit = true;
  let fitting = false;
  let lastFitAt = 0;

  function contentLatLngs() {
    const points = [...courseLatLngs];
    for (const b of boats.values()) {
      if (b.marker) points.push(b.marker.getLatLng());
    }
    return points;
  }

  function fitToContent() {
    const points = contentLatLngs();
    if (points.length === 0) return;
    fitting = true;
    try {
      map.fitBounds(L.latLngBounds(points), { padding: [50, 50], maxZoom: 16, animate: false });
    } finally {
      fitting = false;
    }
    lastFitAt = Date.now();
  }

  function ensureVisible() {
    if (!autoFit) return;
    const points = contentLatLngs();
    if (points.length === 0) return;
    const view = map.getBounds().pad(-0.05);
    const allVisible = points.every((p) => view.contains(p));
    if (!allVisible && Date.now() - lastFitAt > 1000) fitToContent();
  }

  map.on('movestart zoomstart', () => {
    if (!fitting) autoFit = false;
  });

  const FitControl = L.Control.extend({
    options: { position: 'topright' },
    onAdd() {
      const button = L.DomUtil.create('button', 'fit-button');
      button.type = 'button';
      button.textContent = 'Fit all boats';
      L.DomEvent.disableClickPropagation(button);
      L.DomEvent.on(button, 'click', () => {
        autoFit = true;
        fitToContent();
      });
      return button;
    }
  });
  new FitControl().addTo(map);

  // --- Boats ---------------------------------------------------------------------------------------
  // The marker's fill is the boat's own colour (matching its trail and legend swatch); the ring around it
  // carries race status — white normally, orange on the final lap, green once finished.
  function boatIcon(color, ring) {
    const width = ring === '#ffffff' ? 2 : 3;
    return L.divIcon({
      className: '',
      html: `<div style="width:14px;height:14px;border-radius:50%;background:${color};border:${width}px solid ${ring};box-shadow:0 0 4px rgba(0,0,0,0.5);"></div>`,
      iconSize: [14, 14]
    });
  }

  function ringFor(state) {
    if (state.finished) return '#2ecc71';
    if (state.isFinalLap) return '#ff6b35';
    return '#ffffff';
  }

  function ensureBoat(participantId, name) {
    let state = boats.get(participantId);
    if (!state) {
      const color = boatColor(boats.size);
      state = {
        participantId, name: name ?? participantId, color,
        laps: 0, lapsCompleted: 0, isFinalLap: false, finished: false,
        lastSeen: null, marker: null, iconKey: null, infoEl: null, liveEl: null,
        trail: L.polyline([], { color, weight: 3, opacity: 0.85 }).addTo(map),
        trailPts: [], lastTrailTs: 0
      };
      boats.set(participantId, state);
    }
    return state;
  }

  // Adds a fix to the boat's trail unless it's older than what's already drawn (history and live fixes
  // overlap around a load or reconnect) or hasn't moved enough to matter.
  function addTrailPoint(state, lat, lon, tsMs) {
    if (!(tsMs > state.lastTrailTs)) return;
    state.lastTrailTs = tsMs;

    const last = state.trailPts[state.trailPts.length - 1];
    if (last && L.latLng(last).distanceTo([lat, lon]) < MIN_TRAIL_STEP_M) return;

    state.trailPts.push([lat, lon]);
    if (state.trailPts.length > MAX_TRAIL_POINTS + 200) {
      state.trailPts.splice(0, state.trailPts.length - MAX_TRAIL_POINTS);
      state.trail.setLatLngs(state.trailPts);
    } else {
      state.trail.addLatLng([lat, lon]);
    }
  }

  function isStale(state) {
    return state.lastSeen === null || Date.now() - state.lastSeen > STALE_AFTER_MS;
  }

  function refreshBoat(state) {
    if (state.marker) {
      const ring = ringFor(state);
      const key = state.color + ring;
      if (key !== state.iconKey) {
        state.marker.setIcon(boatIcon(state.color, ring));
        state.iconKey = key;
      }
      state.marker.setOpacity(isStale(state) ? 0.45 : 1);
    }

    if (state.infoEl) {
      // Labelled, because a bare "0/1" reads like "tracking 0 of 1 boats" rather than laps completed.
      state.infoEl.textContent = state.finished ? 'Finished' : `laps ${state.lapsCompleted}/${state.laps}`;
      state.infoEl.className = state.finished ? 'finished' : state.isFinalLap ? 'final-lap' : '';
    }

    if (state.liveEl) {
      if (state.lastSeen === null) {
        state.liveEl.textContent = '';
      } else if (isStale(state)) {
        state.liveEl.textContent = '○ no signal';
        state.liveEl.className = 'tracking stale';
      } else {
        state.liveEl.textContent = '● live';
        state.liveEl.className = 'tracking live';
      }
    }
  }

  function updatePosition(participantId, lat, lon, timestamp) {
    if (!Number.isFinite(lat) || !Number.isFinite(lon)) return;
    const state = ensureBoat(participantId);
    const latLng = [lat, lon];
    const seenAt = Date.parse(timestamp);
    state.lastSeen = Number.isFinite(seenAt) ? seenAt : Date.now();

    addTrailPoint(state, lat, lon, state.lastSeen);

    if (!state.marker) {
      const ring = ringFor(state);
      state.iconKey = state.color + ring;
      state.marker = L.marker(latLng, { icon: boatIcon(state.color, ring) })
        .bindTooltip(state.name, { permanent: true, direction: 'right', offset: [8, 0], className: 'boat-label' })
        .addTo(map);
    } else {
      state.marker.setLatLng(latLng);
    }

    refreshBoat(state);
    ensureVisible();
  }

  function renderBoatList(race) {
    boatListEl.innerHTML = '';
    for (const rp of race.raceParticipants) {
      const participant = race.fleet.participants.find((p) => p.id === rp.participantId);
      const state = ensureBoat(rp.participantId, participant ? participant.name : rp.participantId);
      state.name = participant ? participant.name : rp.participantId;
      state.laps = rp.laps;
      state.lapsCompleted = rp.lapsCompleted;
      state.isFinalLap = rp.isOnFinalLap;
      state.finished = rp.status === 'Finished';

      const row = document.createElement('div');
      row.className = 'boat-row';
      const label = document.createElement('span');
      const swatch = document.createElement('span');
      swatch.className = 'swatch';
      swatch.style.background = state.color;
      label.append(swatch, document.createTextNode(state.name));
      state.liveEl = document.createElement('span');
      state.infoEl = document.createElement('span');
      row.append(label, state.liveEl, state.infoEl);
      boatListEl.appendChild(row);
      refreshBoat(state);
    }
  }

  // --- Loading ---------------------------------------------------------------------------------------
  async function loadTrails(sinceMs) {
    // History from the recorded fixes, so a map opened mid-race (or after a reconnect) already shows the
    // traces so far. Points older than what a boat's trail already has are skipped, so it's safe to call
    // repeatedly. Recording only runs from the start sequence on; before that, trails simply build up
    // from the moment the map was opened.
    try {
      const res = await fetch(
        `/api/races/${encodeURIComponent(raceId)}/tracks?sinceMs=${Math.floor(sinceMs)}&minGapMs=${TRAIL_MIN_GAP_MS}`
      );
      if (!res.ok) return;
      const { boats: tracks } = await res.json();
      for (const track of tracks) {
        const state = ensureBoat(track.participantId);
        for (const [ts, lat, lon] of track.points) addTrailPoint(state, lat, lon, ts);
      }
    } catch {
      // Live fixes still draw the trails from here on.
    }
  }

  function trailSince() {
    // After a reconnect, only what was missed: from the oldest "last point" across the boats.
    const seen = [...boats.values()].map((b) => b.lastTrailTs).filter((t) => t > 0);
    return seen.length ? Math.min(...seen) - 1000 : Date.now() - TRAIL_WINDOW_MS;
  }

  async function loadSnapshot() {
    // Where each boat was last seen — so boats already tracking (or sitting still) appear immediately
    // rather than only after their next fix. Older servers don't have this; that's fine.
    try {
      const res = await fetch(`/api/races/${encodeURIComponent(raceId)}/positions`);
      if (!res.ok) return;
      const { positions } = await res.json();
      for (const p of positions) updatePosition(p.participantId, p.lat, p.lon, p.timestamp);
    } catch {
      // The live feed still works without it.
    }
  }

  async function refreshRaceState() {
    // Lap counts and finishes, re-read from the server — used on every (re)connect so a map that missed
    // events while disconnected doesn't keep showing old numbers.
    try {
      const res = await fetch(`/api/races/${encodeURIComponent(raceId)}`);
      if (!res.ok) return;
      const race = await res.json();
      for (const rp of race.raceParticipants) {
        const state = ensureBoat(rp.participantId);
        state.laps = rp.laps;
        state.lapsCompleted = rp.lapsCompleted;
        state.isFinalLap = rp.isOnFinalLap;
        state.finished = rp.status === 'Finished';
        refreshBoat(state);
      }
    } catch {
      // Keep what we have; live events continue.
    }
  }

  async function loadRace() {
    const url = raceId
      ? `/api/races/${encodeURIComponent(raceId)}`
      : `/api/races/by-code/${encodeURIComponent(code)}`;
    const res = await fetch(url);
    if (!res.ok) {
      raceNameEl.textContent = 'Race not found';
      return;
    }
    const race = await res.json();
    raceId = race.id; // the live socket joins by race id, even when the page was opened by join code
    raceNameEl.textContent = race.name;
    renderBoatList(race);

    const startLine = race.startLine;
    const hasStartLine =
      startLine.committeeLatitude != null && startLine.committeeLongitude != null &&
      startLine.pinLatitude != null && startLine.pinLongitude != null;
    if (hasStartLine) {
      const line = [[startLine.committeeLatitude, startLine.committeeLongitude], [startLine.pinLatitude, startLine.pinLongitude]];
      L.polyline(line, { color: '#f1c40f', weight: 3 }).addTo(map);
      courseLatLngs.push(...line);
    }

    for (const buoy of race.buoys) {
      if (buoy.latitude == null || buoy.longitude == null) continue; // not yet captured
      L.circleMarker([buoy.latitude, buoy.longitude], { radius: 6, color: '#9b59b6', fillColor: '#9b59b6', fillOpacity: 0.8 })
        .bindTooltip(buoy.name)
        .addTo(map);
      courseLatLngs.push([buoy.latitude, buoy.longitude]);
    }

    fitToContent();
    await loadTrails(Date.now() - TRAIL_WINDOW_MS);
    await loadSnapshot();
    connectSocket();
  }

  // --- Live feed -------------------------------------------------------------------------------------
  let reconnectDelay = 1000;

  function connectSocket() {
    const proto = window.location.protocol === 'https:' ? 'wss' : 'ws';
    const socket = new WebSocket(`${proto}://${window.location.host}/ws`);

    socket.addEventListener('open', () => {
      reconnectDelay = 1000;
      socket.send(JSON.stringify({ type: 'join', raceId, role: 'spectator' }));
      loadTrails(trailSince()).then(loadSnapshot); // catches anything missed while disconnected
      refreshRaceState();
    });

    // A phone network blip, a proxy idle timeout or a laptop sleeping all drop the socket; without this
    // the map would silently stop updating.
    socket.addEventListener('close', () => {
      setTimeout(connectSocket, reconnectDelay);
      reconnectDelay = Math.min(reconnectDelay * 2, 10000);
    });

    socket.addEventListener('message', (event) => {
      const message = JSON.parse(event.data);
      const state = message.participantId ? ensureBoat(message.participantId) : null;

      switch (message.type) {
        case 'positionUpdate':
          updatePosition(message.participantId, message.lat, message.lon, message.timestamp);
          break;
        case 'lapCompleted':
          state.lapsCompleted = message.lapsCompleted;
          state.isFinalLap = !!message.isFinalLap;
          refreshBoat(state);
          break;
        case 'finalLap':
          state.isFinalLap = true;
          refreshBoat(state);
          break;
        case 'finished':
          state.finished = true;
          state.isFinalLap = false;
          refreshBoat(state);
          break;
      }
    });
  }

  // Marker fading and the "live / no signal" labels depend on time passing, not only on messages.
  setInterval(() => boats.forEach(refreshBoat), 5000);

  loadRace();
})();
