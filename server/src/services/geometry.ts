export interface LatLon {
  lat: number;
  lon: number;
}

const EARTH_RADIUS_METERS = 6371000;

export function haversineMeters(a: LatLon, b: LatLon): number {
  const toRad = (d: number) => (d * Math.PI) / 180;
  const dLat = toRad(b.lat - a.lat);
  const dLon = toRad(b.lon - a.lon);
  const lat1 = toRad(a.lat);
  const lat2 = toRad(b.lat);
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(lat1) * Math.cos(lat2) * Math.sin(dLon / 2) ** 2;
  return 2 * EARTH_RADIUS_METERS * Math.asin(Math.sqrt(h));
}

/** Local planar (equirectangular) projection around `origin`, in meters. Accurate enough at race-course scale. */
function toMeters(origin: LatLon, p: LatLon): { x: number; y: number } {
  const toRad = (d: number) => (d * Math.PI) / 180;
  const x = toRad(p.lon - origin.lon) * Math.cos(toRad(origin.lat)) * EARTH_RADIUS_METERS;
  const y = toRad(p.lat - origin.lat) * EARTH_RADIUS_METERS;
  return { x, y };
}

function cross(o: LatLon, a: LatLon, b: LatLon): number {
  return (a.lon - o.lon) * (b.lat - o.lat) - (a.lat - o.lat) * (b.lon - o.lon);
}

/** True if movement segment (p1,p2) crosses the fixed segment (p3,p4). Orientation test on raw lat/lon. */
export function segmentsIntersect(p1: LatLon, p2: LatLon, p3: LatLon, p4: LatLon): boolean {
  const d1 = cross(p3, p4, p1);
  const d2 = cross(p3, p4, p2);
  const d3 = cross(p1, p2, p3);
  const d4 = cross(p1, p2, p4);

  return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
}

export function distanceToSegmentMeters(p: LatLon, a: LatLon, b: LatLon): number {
  const pm = toMeters(a, p);
  const am = { x: 0, y: 0 };
  const bm = toMeters(a, b);
  const dx = bm.x - am.x;
  const dy = bm.y - am.y;
  const lengthSq = dx * dx + dy * dy;
  let t = lengthSq === 0 ? 0 : ((pm.x - am.x) * dx + (pm.y - am.y) * dy) / lengthSq;
  t = Math.max(0, Math.min(1, t));
  const projX = am.x + t * dx;
  const projY = am.y + t * dy;
  return Math.hypot(pm.x - projX, pm.y - projY);
}
