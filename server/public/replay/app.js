(async () => {
  const $ = (id) => document.getElementById(id);
  const raceNameEl = $('raceName');
  const boatListEl = $('boatList');
  const playBtn = $('play');
  const speedSel = $('speed');
  const slider = $('slider');
  const clockEl = $('clock');

  // Opened with ?code=<join code> (what the committee app links to) or ?raceId=<race id>.
  const params = new URLSearchParams(window.location.search);
  const code = params.get('code');
  const raceIdParam = params.get('raceId');
  if (!code && !raceIdParam) {
    raceNameEl.textContent = 'No race specified';
    return;
  }

  const raceRes = await fetch(
    raceIdParam
      ? `/api/races/${encodeURIComponent(raceIdParam)}`
      : `/api/races/by-code/${encodeURIComponent(code)}`
  );
  if (!raceRes.ok) {
    raceNameEl.textContent = 'Race not found';
    return;
  }
  const race = await raceRes.json();
  raceNameEl.textContent = race.name;

  const tracksRes = await fetch(`/api/races/${encodeURIComponent(race.id)}/tracks`);
  const tracks = tracksRes.ok ? await tracksRes.json() : { boats: [], startAt: race.startAt };

  const map = L.map('map').setView([0, 0], 13);
  L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
    attribution: '&copy; OpenStreetMap contributors',
    maxZoom: 19
  }).addTo(map);

  const bounds = [];

  // Course: start line and marks, same rules as the live map (skip anything not yet captured).
  const sl = race.startLine;
  if (sl.committeeLatitude != null && sl.committeeLongitude != null && sl.pinLatitude != null && sl.pinLongitude != null) {
    const line = [[sl.committeeLatitude, sl.committeeLongitude], [sl.pinLatitude, sl.pinLongitude]];
    L.polyline(line, { color: '#f1c40f', weight: 3 }).addTo(map);
    bounds.push(...line);
  }
  for (const buoy of race.buoys) {
    if (buoy.latitude == null || buoy.longitude == null) continue;
    L.circleMarker([buoy.latitude, buoy.longitude], { radius: 6, color: '#9b59b6', fillColor: '#9b59b6', fillOpacity: 0.8 })
      .bindTooltip(buoy.name)
      .addTo(map);
    bounds.push([buoy.latitude, buoy.longitude]);
  }

  // Same colour function and boat order as the live map, so a boat keeps its colour in both.
  const boatColor = (i) => `hsl(${Math.round((i * 137.508) % 360)}, 75%, ${i % 2 ? 38 : 50}%)`;
  const colorIndexOf = new Map(race.raceParticipants.map((p, i) => [p.participantId, i]));
  const nameOf = new Map(race.fleet.participants.map((p) => [p.id, p.name]));

  const boats = tracks.boats
    .filter((b) => b.points.length > 0)
    .map((b, i) => {
      const color = boatColor(colorIndexOf.get(b.participantId) ?? colorIndexOf.size + i);
      const name = nameOf.get(b.participantId) ?? b.participantId;
      return {
        name,
        color,
        pts: b.points, // [epoch ms, lat, lon], oldest first
        idx: -1, // index of the last point already added to the trail
        trail: L.polyline([], { color, weight: 3, opacity: 0.85 }).addTo(map),
        marker: L.marker([0, 0], {
          icon: L.divIcon({
            className: '',
            html: `<div class="boat-dot" style="background:${color}"></div>`,
            iconSize: [12, 12]
          })
        }).bindTooltip(name, { permanent: true, direction: 'right', offset: [8, 0], className: 'boat-label' })
      };
    });

  if (boats.length === 0) {
    boatListEl.textContent = 'No traces were recorded for this race.';
    if (bounds.length) map.fitBounds(bounds, { padding: [40, 40] });
    clockEl.textContent = 'No traces';
    return;
  }

  for (const b of boats) {
    const row = document.createElement('div');
    row.className = 'boat-row';
    row.innerHTML = `<span class="swatch" style="background:${b.color}"></span>`;
    row.append(document.createTextNode(b.name));
    boatListEl.appendChild(row);
    for (const p of b.pts) bounds.push([p[1], p[2]]);
  }
  map.fitBounds(bounds, { padding: [40, 40] });

  const tMin = Math.min(...boats.map((b) => b.pts[0][0]));
  const tMax = Math.max(...boats.map((b) => b.pts[b.pts.length - 1][0]));
  const startAtMs = tracks.startAt ? Date.parse(tracks.startAt) : null;

  let current = tMin;
  let playing = false;
  let lastFrame = 0;

  const pad = (n) => String(n).padStart(2, '0');
  function formatDuration(ms) {
    const s = Math.floor(Math.abs(ms) / 1000);
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    return (h ? `${h}:${pad(m)}` : pad(m)) + `:${pad(s % 60)}`;
  }
  function updateClock() {
    const wall = new Date(current).toLocaleTimeString();
    clockEl.textContent = startAtMs
      ? `${current < startAtMs ? 'T-' : 'T+'}${formatDuration(current - startAtMs)}  (${wall})`
      : wall;
  }

  function render(t) {
    for (const b of boats) {
      const n = b.pts.length;
      if (t < b.pts[0][0]) {
        // Boat hasn't started recording yet.
        if (b.idx !== -1) {
          b.trail.setLatLngs([]);
          b.idx = -1;
        }
        if (map.hasLayer(b.marker)) map.removeLayer(b.marker);
        continue;
      }

      // Last point at or before t.
      let lo = 0;
      let hi = n - 1;
      while (lo < hi) {
        const mid = (lo + hi + 1) >> 1;
        if (b.pts[mid][0] <= t) lo = mid;
        else hi = mid - 1;
      }

      // Between two fixes, glide linearly — unless the gap is long (signal lost), then hold position.
      let lat = b.pts[lo][1];
      let lon = b.pts[lo][2];
      if (lo < n - 1) {
        const a = b.pts[lo];
        const c = b.pts[lo + 1];
        const gap = c[0] - a[0];
        if (gap > 0 && gap <= 30000) {
          const f = (t - a[0]) / gap;
          lat = a[1] + (c[1] - a[1]) * f;
          lon = a[2] + (c[2] - a[2]) * f;
        }
      }

      if (lo !== b.idx) {
        if (lo > b.idx && lo - b.idx <= 500) {
          for (let i = b.idx + 1; i <= lo; i++) b.trail.addLatLng([b.pts[i][1], b.pts[i][2]]);
        } else {
          b.trail.setLatLngs(b.pts.slice(0, lo + 1).map((p) => [p[1], p[2]]));
        }
        b.idx = lo;
      }

      b.marker.setLatLng([lat, lon]);
      if (!map.hasLayer(b.marker)) b.marker.addTo(map);
    }
    updateClock();
  }

  function updateSlider() {
    slider.value = tMax > tMin ? Math.round(((current - tMin) / (tMax - tMin)) * 1000) : 0;
  }

  function frame(now) {
    if (!playing) return;
    const dt = now - lastFrame;
    lastFrame = now;
    current += dt * Number(speedSel.value);
    if (current >= tMax) {
      current = tMax;
      playing = false;
      playBtn.textContent = '↺ Replay';
    }
    render(current);
    updateSlider();
    if (playing) requestAnimationFrame(frame);
  }

  playBtn.addEventListener('click', () => {
    if (!playing && current >= tMax) current = tMin;
    playing = !playing;
    playBtn.textContent = playing ? '⏸ Pause' : '▶ Play';
    if (playing) {
      lastFrame = performance.now();
      requestAnimationFrame(frame);
    }
  });

  slider.addEventListener('input', () => {
    current = tMin + (Number(slider.value) / 1000) * (tMax - tMin);
    render(current);
    if (!playing) playBtn.textContent = current >= tMax ? '↺ Replay' : '▶ Play';
  });

  playBtn.disabled = false;
  slider.disabled = tMax <= tMin;
  render(current);
  updateSlider();
})();
