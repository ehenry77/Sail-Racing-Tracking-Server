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
      raceSubEl.textContent = `Join code ${race.joinCode} — pick your boat to start tracking`;
      renderRoster(race);
    } catch (err) {
      errorEl.textContent = 'Could not load this race. Check the link and try again.';
      raceSubEl.textContent = '';
    }
  }

  function renderRoster(race) {
    rosterEl.innerHTML = '';
    for (const participant of race.fleet.participants) {
      const btn = document.createElement('button');
      btn.className = 'roster-item';
      btn.textContent = `${participant.name}${participant.helm ? ' — ' + participant.helm : ''}`;
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

    socket.addEventListener('close', () => {
      setStatus('off', 'Disconnected — reopen this page to resume');
    });
  }

  function startTracking(race, participant) {
    rosterCard.style.display = 'none';
    hero.style.display = 'block';
    boatLabel.textContent = `Tracking as ${participant.name}`;
    setStatus('off', 'Connecting…');

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
