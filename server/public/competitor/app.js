(() => {
  const raceNameEl = document.getElementById('raceName');
  const raceSubEl = document.getElementById('raceSub');
  const errorEl = document.getElementById('error');
  const rosterCard = document.getElementById('rosterCard');
  const rosterEl = document.getElementById('roster');
  const hero = document.getElementById('hero');
  const statusDot = document.getElementById('statusDot');
  const statusText = document.getElementById('statusText');
  const boatLabel = document.getElementById('boatLabel');
  const stopBtn = document.getElementById('stopBtn');
  const centerBtn = document.getElementById('centerBtn');

  const joinCode = window.location.pathname.split('/').filter(Boolean).pop();

  let socket = null;
  let watchId = null;
  let wakeLock = null;
  let lastSentAt = 0;
  let lastSentPos = null;
  let staleTimer = null;

  const MIN_SEND_INTERVAL_MS = 2000;
  const MIN_MOVE_METERS = 3;

  function haversineMeters(a, b) {
    const R = 6371000;
    const toRad = (d) => (d * Math.PI) / 180;
    const dLat = toRad(b.lat - a.lat);
    const dLon = toRad(b.lon - a.lon);
    const h =
      Math.sin(dLat / 2) ** 2 +
      Math.cos(toRad(a.lat)) * Math.cos(toRad(b.lat)) * Math.sin(dLon / 2) ** 2;
    return 2 * R * Math.asin(Math.sqrt(h));
  }

  // --- Background map ------------------------------------------------------------------------------
  // The map is the page background (OpenStreetMap, no key). It shows the course, this phone's own boat
  // with its trail, and the other boats as the live feed reports them. If the map library can't load
  // (e.g. the CDN is unreachable) every map call below quietly does nothing and tracking is unaffected.
  const STALE_AFTER_MS = 30000;
  const MIN_TRAIL_STEP_M = 3;
  const MAX_TRAIL_POINTS = 3000;

  // Same palette function as the live map and replay, so a boat keeps its colour everywhere.
  const boatColor = (i) => `hsl(${Math.round((i * 137.508) % 360)}, 75%, ${i % 2 ? 38 : 50}%)`;

  const hasMap = typeof L !== 'undefined';
  const map = hasMap ? L.map('map', { zoomControl: false, attributionControl: true }).setView([20, 0], 2) : null;
  if (map) {
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '&copy; OpenStreetMap contributors',
      maxZoom: 19
    }).addTo(map);
    L.control.zoom({ position: 'topright' }).addTo(map);
  }

  const courseLatLngs = [];
  const boats = new Map(); // participantId -> { name, color, marker, trail, trailPts, lastSeen }
  const colorIndex = new Map(); // participantId -> position in the race's boat list
  let myId = null;
  let follow = true; // keep this boat centred until the skipper pans or zooms
  let programmatic = false;

  function boatIcon(color, isMe) {
    const size = isMe ? 18 : 14;
    return L.divIcon({
      className: '',
      html: `<div style="width:${size}px;height:${size}px;border-radius:50%;background:${color};border:${isMe ? 3 : 2}px solid #fff;box-shadow:0 0 5px rgba(0,0,0,0.6);"></div>`,
      iconSize: [size, size]
    });
  }

  function ensureBoat(participantId, name) {
    let boat = boats.get(participantId);
    if (!boat) {
      if (!colorIndex.has(participantId)) colorIndex.set(participantId, colorIndex.size);
      const color = boatColor(colorIndex.get(participantId));
      boat = { name: name ?? participantId, color, marker: null, trail: null, trailPts: [], lastSeen: null };
      boats.set(participantId, boat);
    }
    if (name) boat.name = name;
    return boat;
  }

  function moveBoat(participantId, lat, lon, seenAt) {
    if (!map || !Number.isFinite(lat) || !Number.isFinite(lon)) return;
    const isMe = participantId === myId;
    const boat = ensureBoat(participantId);
    boat.lastSeen = Number.isFinite(seenAt) ? seenAt : Date.now();

    if (!boat.trail) {
      boat.trail = L.polyline([], { color: boat.color, weight: isMe ? 4 : 3, opacity: 0.85 }).addTo(map);
    }
    const last = boat.trailPts[boat.trailPts.length - 1];
    if (!last || L.latLng(last).distanceTo([lat, lon]) >= MIN_TRAIL_STEP_M) {
      boat.trailPts.push([lat, lon]);
      if (boat.trailPts.length > MAX_TRAIL_POINTS) boat.trailPts.splice(0, boat.trailPts.length - MAX_TRAIL_POINTS);
      boat.trail.setLatLngs(boat.trailPts);
    }

    if (!boat.marker) {
      boat.marker = L.marker([lat, lon], { icon: boatIcon(boat.color, isMe), zIndexOffset: isMe ? 1000 : 0 })
        .bindTooltip(isMe ? `${boat.name} (you)` : boat.name, {
          permanent: true, direction: 'right', offset: [10, 0], className: isMe ? 'boat-label me-label' : 'boat-label'
        })
        .addTo(map);
    } else {
      boat.marker.setLatLng([lat, lon]);
    }
    boat.marker.setOpacity(1);

    if (isMe && follow) centerOnMe(false);
    else if (!isMe && !myPositionKnown()) fitToContent();
  }

  function myPositionKnown() {
    return !!(myId && boats.get(myId) && boats.get(myId).marker);
  }

  function setView(fn) {
    programmatic = true;
    try {
      fn();
    } finally {
      programmatic = false;
    }
  }

  function centerOnMe(forceZoom) {
    const me = myId && boats.get(myId);
    if (!map || !me || !me.marker) return;
    const zoom = forceZoom || map.getZoom() < 14 ? 16 : map.getZoom();
    setView(() => map.setView(me.marker.getLatLng(), zoom, { animate: false }));
  }

  function fitToContent() {
    if (!map) return;
    const points = [...courseLatLngs];
    for (const b of boats.values()) if (b.marker) points.push(b.marker.getLatLng());
    if (points.length === 0) return;
    setView(() => map.fitBounds(L.latLngBounds(points), { padding: [60, 60], maxZoom: 16, animate: false }));
  }

  if (map) {
    // Dragging or zooming by hand stops the map following the boat; "Centre on me" brings it back.
    map.on('movestart zoomstart', () => {
      if (!programmatic) follow = false;
    });
    centerBtn.addEventListener('click', () => {
      follow = true;
      if (myPositionKnown()) centerOnMe(true);
      else fitToContent();
    });
    // Fade boats whose feed went quiet, like the live map does.
    setInterval(() => {
      for (const b of boats.values()) {
        if (b.marker && b !== boats.get(myId)) {
          b.marker.setOpacity(b.lastSeen !== null && Date.now() - b.lastSeen <= STALE_AFTER_MS ? 1 : 0.45);
        }
      }
    }, 5000);
  } else {
    centerBtn.style.display = 'none';
  }

  function drawCourse(race) {
    if (!map) return;
    const startLine = race.startLine;
    if (
      startLine &&
      startLine.committeeLatitude != null && startLine.committeeLongitude != null &&
      startLine.pinLatitude != null && startLine.pinLongitude != null
    ) {
      const line = [
        [startLine.committeeLatitude, startLine.committeeLongitude],
        [startLine.pinLatitude, startLine.pinLongitude]
      ];
      L.polyline(line, { color: '#f1c40f', weight: 4 }).addTo(map);
      courseLatLngs.push(...line);
    }
    for (const buoy of race.buoys || []) {
      if (buoy.latitude == null || buoy.longitude == null) continue; // not yet captured
      L.circleMarker([buoy.latitude, buoy.longitude], { radius: 7, color: '#fff', weight: 2, fillColor: '#9b59b6', fillOpacity: 0.9 })
        .bindTooltip(buoy.name)
        .addTo(map);
      courseLatLngs.push([buoy.latitude, buoy.longitude]);
    }
    fitToContent();
  }

  async function loadOthers(raceId) {
    // Where the other boats were last seen, so they appear straight away instead of after their next fix.
    try {
      const res = await fetch(`/api/races/${encodeURIComponent(raceId)}/positions`);
      if (!res.ok) return;
      const { positions } = await res.json();
      for (const p of positions) {
        if (p.participantId !== myId) moveBoat(p.participantId, p.lat, p.lon, Date.parse(p.timestamp));
      }
    } catch {
      // The live feed fills them in as they move.
    }
  }

  function setStatus(state, label) {
    statusDot.className = `dot ${state}`;
    statusText.textContent = label;
  }

  async function requestWakeLock() {
    try {
      if ('wakeLock' in navigator) {
        wakeLock = await navigator.wakeLock.request('screen');
      }
    } catch {
      // best-effort; tracking still works without it, just riskier if the phone sleeps
    }
  }

  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible' && watchId !== null) {
      requestWakeLock();
    }
  });

  async function loadRace() {
    try {
      const res = await fetch(`/api/races/by-code/${encodeURIComponent(joinCode)}`);
      if (!res.ok) {
        throw new Error('Race not found');
      }
      const race = await res.json();
      raceNameEl.textContent = race.name;

      // Boat colours follow the race's boat order, exactly as on the live map.
      for (const rp of race.raceParticipants || []) {
        const p = race.fleet.participants.find((x) => x.id === rp.participantId);
        if (!colorIndex.has(rp.participantId)) colorIndex.set(rp.participantId, colorIndex.size);
        ensureBoat(rp.participantId, p ? p.name : undefined);
      }
      drawCourse(race);

      // A per-boat link from the committee app carries ?boat=<participantId> so the skipper doesn't
      // have to find themselves in the roster.
      const boatId = new URLSearchParams(window.location.search).get('boat');
      const preselected = boatId && race.fleet.participants.find((p) => p.id === boatId);
      if (preselected) {
        raceSubEl.textContent = `Join code ${race.joinCode}`;
        renderPreselected(race, preselected);
      } else {
        raceSubEl.textContent = `Join code ${race.joinCode} — pick your boat to start tracking`;
        renderRoster(race);
      }
    } catch (err) {
      errorEl.textContent = 'Could not load this race. Check the link and try again.';
      raceSubEl.textContent = '';
    }
  }

  function describeBoat(participant) {
    return `${participant.name}${participant.helm ? ' — ' + participant.helm : ''}`;
  }

  function renderPreselected(race, participant) {
    const heading = rosterCard.querySelector('strong');
    heading.textContent = 'Your boat';
    rosterEl.innerHTML = '';

    const start = document.createElement('button');
    start.className = 'roster-item';
    start.textContent = `Start tracking — ${describeBoat(participant)}`;
    start.addEventListener('click', () => startTracking(race, participant));
    rosterEl.appendChild(start);

    const other = document.createElement('button');
    other.className = 'roster-item';
    other.style.opacity = '0.7';
    other.textContent = 'Not your boat? Choose another';
    other.addEventListener('click', () => {
      heading.textContent = 'Who are you?';
      renderRoster(race);
    });
    rosterEl.appendChild(other);
  }

  function renderRoster(race) {
    rosterEl.innerHTML = '';
    for (const participant of race.fleet.participants) {
      const btn = document.createElement('button');
      btn.className = 'roster-item';
      btn.textContent = describeBoat(participant);
      btn.addEventListener('click', () => startTracking(race, participant));
      rosterEl.appendChild(btn);
    }
  }

  function connectSocket(raceId, participantId) {
    const proto = window.location.protocol === 'https:' ? 'wss' : 'ws';
    socket = new WebSocket(`${proto}://${window.location.host}/ws`);

    socket.addEventListener('open', () => {
      socket.send(JSON.stringify({ type: 'join', raceId, role: 'competitor', participantId }));
    });

    socket.addEventListener('message', (event) => {
      let message;
      try {
        message = JSON.parse(event.data);
      } catch {
        return;
      }
      if (message.type === 'positionUpdate' && message.participantId !== myId) {
        moveBoat(message.participantId, message.lat, message.lon, Date.parse(message.timestamp));
      }
    });

    socket.addEventListener('close', () => {
      setStatus('off', 'Disconnected — reopen this page to resume');
    });
  }

  function startTracking(race, participant) {
    rosterCard.style.display = 'none';
    hero.style.display = 'block';
    boatLabel.textContent = `Tracking as ${participant.name}`;
    setStatus('off', 'Connecting…');

    myId = participant.id;
    ensureBoat(myId, participant.name);
    follow = true;
    loadOthers(race.id);

    connectSocket(race.id, participant.id);
    requestWakeLock();

    if (!('geolocation' in navigator)) {
      setStatus('off', 'Geolocation not supported on this device');
      return;
    }

    watchId = navigator.geolocation.watchPosition(
      (pos) => {
        const now = Date.now();
        const current = { lat: pos.coords.latitude, lon: pos.coords.longitude };
        // The map shows every fix the phone gets, whether or not it was worth sending to the server.
        moveBoat(participant.id, current.lat, current.lon, pos.timestamp);
        const movedEnough = !lastSentPos || haversineMeters(lastSentPos, current) >= MIN_MOVE_METERS;
        const enoughTimePassed = now - lastSentAt >= MIN_SEND_INTERVAL_MS;

        if ((movedEnough || enoughTimePassed) && socket && socket.readyState === WebSocket.OPEN) {
          socket.send(
            JSON.stringify({
              type: 'position',
              raceId: race.id,
              participantId: participant.id,
              lat: current.lat,
              lon: current.lon,
              accuracy: pos.coords.accuracy,
              timestamp: new Date(pos.timestamp).toISOString()
            })
          );
          lastSentAt = now;
          lastSentPos = current;
          setStatus('live', 'Tracking live');
          resetStaleTimer();
        }
      },
      () => setStatus('off', 'Location permission denied'),
      { enableHighAccuracy: true, maximumAge: 1000, timeout: 15000 }
    );
  }

  function resetStaleTimer() {
    if (staleTimer) {
      clearTimeout(staleTimer);
    }
    staleTimer = setTimeout(() => setStatus('stale', 'No recent fix — check GPS signal'), 15000);
  }

  stopBtn.addEventListener('click', () => {
    if (watchId !== null) {
      navigator.geolocation.clearWatch(watchId);
      watchId = null;
    }
    if (socket) {
      socket.close();
    }
    if (wakeLock) {
      wakeLock.release().catch(() => {});
    }
    setStatus('off', 'Stopped');
  });

  loadRace();
})();
