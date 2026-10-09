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

  const map = L.map('map').setView([0, 0], 13);
  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    attribution: '&copy; OpenStreetMap contributors',
    maxZoom: 19
  }).addTo(map);

  const boatMarkers = new Map(); // participantId -> L.Marker
  const boatState = new Map(); // participantId -> { name, isFinalLap, finished }

  function boatIcon(color) {
    return L.divIcon({
      className: '',
      html: `<div style="width:14px;height:14px;border-radius:50%;background:${color};border:2px solid white;box-shadow:0 0 4px rgba(0,0,0,0.5);"></div>`,
      iconSize: [14, 14]
    });
  }

  function colorFor(state) {
    if (state.finished) return '#2ecc71';
    if (state.isFinalLap) return '#ff6b35';
    return '#3498db';
  }

  function renderBoatList(race) {
    boatListEl.innerHTML = '';
    for (const rp of race.raceParticipants) {
      const participant = race.fleet.participants.find((p) => p.id === rp.participantId);
      const row = document.createElement('div');
      row.className = 'boat-row';
      const label = document.createElement('span');
      label.textContent = participant ? participant.name : rp.participantId;
      const info = document.createElement('span');
      info.textContent = rp.status === 'Finished' ? 'Finished' : `${rp.lapsCompleted}/${rp.laps}`;
      info.className = rp.status === 'Finished' ? 'finished' : rp.isOnFinalLap ? 'final-lap' : '';
      row.append(label, info);
      boatListEl.appendChild(row);

      boatState.set(rp.participantId, {
        name: participant ? participant.name : rp.participantId,
        isFinalLap: rp.isOnFinalLap,
        finished: rp.status === 'Finished'
      });
    }
  }

  function updateBoatRowUI(participantId) {
    const state = boatState.get(participantId);
    if (!state) return;
    const rows = boatListEl.querySelectorAll('.boat-row');
    for (const row of rows) {
      const label = row.firstChild;
      if (label && label.textContent === state.name) {
        const info = row.lastChild;
        info.className = state.finished ? 'finished' : state.isFinalLap ? 'final-lap' : '';
        info.textContent = state.finished ? 'Finished' : info.textContent;
      }
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

    const bounds = [];

    const hasStartLine =
      race.startLine.committeeLatitude != null &&
      race.startLine.committeeLongitude != null &&
      race.startLine.pinLatitude != null &&
      race.startLine.pinLongitude != null;
    if (hasStartLine) {
      const startLineLatLngs = [
        [race.startLine.committeeLatitude, race.startLine.committeeLongitude],
        [race.startLine.pinLatitude, race.startLine.pinLongitude]
      ];
      L.polyline(startLineLatLngs, { color: '#f1c40f', weight: 3 }).addTo(map);
      bounds.push(...startLineLatLngs);
    }

    for (const buoy of race.buoys) {
      if (buoy.latitude == null || buoy.longitude == null) {
        continue; // not yet captured
      }
      L.circleMarker([buoy.latitude, buoy.longitude], {
        radius: 6,
        color: '#9b59b6',
        fillColor: '#9b59b6',
        fillOpacity: 0.8
      })
        .bindTooltip(buoy.name)
        .addTo(map);
      bounds.push([buoy.latitude, buoy.longitude]);
    }

    if (bounds.length > 0) {
      map.fitBounds(bounds, { padding: [40, 40] });
    }

    connectSocket();
  }

  function connectSocket() {
    const proto = window.location.protocol === 'https:' ? 'wss' : 'ws';
    const socket = new WebSocket(`${proto}://${window.location.host}/ws`);

    socket.addEventListener('open', () => {
      socket.send(JSON.stringify({ type: 'join', raceId, role: 'spectator' }));
    });

    socket.addEventListener('message', (event) => {
      const message = JSON.parse(event.data);

      if (message.type === 'positionUpdate') {
        const latLng = [message.lat, message.lon];
        const state = boatState.get(message.participantId) ?? { name: message.participantId, isFinalLap: false, finished: false };
        boatState.set(message.participantId, state);

        let marker = boatMarkers.get(message.participantId);
        if (!marker) {
          marker = L.marker(latLng, { icon: boatIcon(colorFor(state)) }).bindTooltip(state.name).addTo(map);
          boatMarkers.set(message.participantId, marker);
        } else {
          marker.setLatLng(latLng);
          marker.setIcon(boatIcon(colorFor(state)));
        }
      }

      if (message.type === 'finalLap') {
        const state = boatState.get(message.participantId);
        if (state) {
          state.isFinalLap = true;
          const marker = boatMarkers.get(message.participantId);
          if (marker) marker.setIcon(boatIcon(colorFor(state)));
          updateBoatRowUI(message.participantId);
        }
      }

      if (message.type === 'finished') {
        const state = boatState.get(message.participantId);
        if (state) {
          state.finished = true;
          state.isFinalLap = false;
          const marker = boatMarkers.get(message.participantId);
          if (marker) marker.setIcon(boatIcon(colorFor(state)));
          updateBoatRowUI(message.participantId);
        }
      }
    });
  }

  loadRace();
})();
