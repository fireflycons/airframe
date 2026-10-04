// airframe web UI: polls GET /aircraft and renders the nearest aircraft,
// a radar and a traffic table. Settings are posted to POST /observer.

const SVG_NS = 'http://www.w3.org/2000/svg';
const LEVEL_THRESHOLD = 200; // ft/min either side of zero that counts as level
const POINTS = ['N', 'NNE', 'NE', 'ENE', 'E', 'ESE', 'SE', 'SSE', 'S', 'SSW', 'SW', 'WSW', 'W', 'WNW', 'NW', 'NNW'];

const interval = Number(document.body.dataset.intervalMs) || 5000;
const $ = (id) => document.getElementById(id);

let latest = null; // last successful /aircraft response

// ---- Formatting ----------------------------------------------------------

const int = new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 });
const oneDp = new Intl.NumberFormat(undefined, { minimumFractionDigits: 1, maximumFractionDigits: 1 });
const deg = (n) => String(Math.round(n) % 360).padStart(3, '0');
const point = (b) => POINTS[Math.round(b / 22.5) % 16];
const ident = (a) => a.flight || a.icao.toUpperCase();

// ADS-B emitter categories C1 and C2 are surface emergency and service
// vehicles (e.g. "LEADER", "ROVER"), not aircraft.
const isEmergencyVehicle = (a) => a.category === 'C1';
const isVehicle = (a) => a.category === 'C1' || a.category === 'C2';
const isGround = (a) => a.onGround || isVehicle(a);
// Radar symbol for each ADS-B emitter category. A1 is a light aircraft
// (< 15,500 lb), i.e. general aviation.
const CATEGORY_SYMBOLS = {
  A1: 'light-top', A2: 'plane-top', A3: 'plane-top', A4: 'plane-top', A5: 'plane-top',
  A6: 'fast-top', A7: 'heli-top',
  B1: 'glider-top', B2: 'balloon-top', B3: 'parachute-top', B4: 'hang-glider-top',
  B6: 'drone-top', B7: 'rocket-top',
  C1: 'vehicle-top', C2: 'vehicle-top', C3: 'obstacle-top', C4: 'obstacle-top', C5: 'obstacle-top',
};

// A GA flight usually uses its registration as its callsign.
const normReg = (s) => (s || '').replaceAll('-', '').toUpperCase();
const isRegistrationCallsign = (a) => !!a.registration && normReg(a.flight) === normReg(a.registration);

function radarSymbol(a) {
  const symbol = CATEGORY_SYMBOLS[a.category];
  if (symbol) return `#${symbol}`;
  return isRegistrationCallsign(a) ? '#light-top' : '#plane-top';
}

function verticalRate(a) {
  return a.barometricClimbRate || a.geometricClimbRate;
}

// iconKind picks the icon for the table and hero: a vehicle, an aircraft on
// the ground, or an aircraft's vertical movement.
function iconKind(a) {
  if (isEmergencyVehicle(a)) return 'emergency vehicle';
  if (isVehicle(a)) return 'vehicle';
  if (a.onGround) return 'ground';
  const rate = verticalRate(a);
  if (rate > LEVEL_THRESHOLD) return 'climb';
  if (rate < -LEVEL_THRESHOLD) return 'descend';
  return 'level';
}

function svg(tag, attrs = {}) {
  const el = document.createElementNS(SVG_NS, tag);
  for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, v);
  return el;
}

function kindIcon(a) {
  const kind = iconKind(a);
  const symbol = isVehicle(a) ? 'vehicle' : kind;
  const icon = svg('svg', { class: `vs ${kind}`, viewBox: '0 0 24 24', role: 'img', 'aria-label': kind });
  const title = svg('title');
  title.textContent = kind;
  icon.append(title, svg('use', { href: `#${symbol}` }));
  return icon;
}

// ---- Rendering -----------------------------------------------------------

function renderHeader(data) {
  const { lat, lon } = data.location;
  $('observer').textContent = `${lat.toFixed(4)}, ${lon.toFixed(4)} · ${data.radius} NM`;
}

// tickClock updates the clock, then reschedules itself just after the next
// whole second so it ticks evenly, independent of polling.
function tickClock() {
  $('updated').textContent = new Date().toLocaleTimeString();
  setTimeout(tickClock, 1000 - (Date.now() % 1000) + 5);
}
tickClock();

// The server this page came from, so a screensaver that has fallen back to
// another instance (e.g. the local service) shows which one it is on.
$('server').textContent = location.origin;

function renderStatus(error) {
  const dot = $('status-dot');
  dot.dataset.state = error ? 'error' : 'ok';
  dot.title = error ? `Error: ${error.message}` : 'Receiving data';
}

function renderHero(data, nearest) {
  $('hero-body').hidden = !nearest;
  $('hero-empty').hidden = !!nearest;
  if (!nearest) {
    $('hero-empty').textContent = `No aircraft within ${data.radius} NM`;
    return;
  }

  const a = nearest;
  $('h-callsign').textContent = ident(a);
  $('h-airline').textContent = a.airline || ' ';
  const type = [a.typeCode, a.description !== a.typeCode && a.description].filter(Boolean).join(' — ');
  $('h-meta').textContent = [type, a.registration, a.icao.toUpperCase()].filter(Boolean).join(' · ');

  $('h-alt').textContent = a.onGround ? 'GROUND' : int.format(a.barometricAltitude || a.geometricAltitude);
  $('h-alt-unit').textContent = a.onGround ? '' : 'ft';
  $('h-vs-icon').replaceChildren(kindIcon(a));
  $('h-vs').textContent = int.format(verticalRate(a));
  $('h-gs').textContent = int.format(a.groundSpeed);
  $('h-track').textContent = `${deg(a.track)}°`;
  $('h-track-point').textContent = point(a.track);
  $('h-track-arrow').style.transform = `rotate(${a.track}deg)`;
  $('h-dist').textContent = oneDp.format(a.distance);
  $('h-brg').textContent = `${deg(a.bearing)}°`;
  $('h-brg-point').textContent = point(a.bearing);
}

function renderTable(others) {
  const cell = (text, cls) => {
    const td = document.createElement('td');
    td.textContent = text;
    if (cls) td.className = cls;
    return td;
  };

  if (others.length === 0) {
    const td = cell('No other traffic', 'empty');
    td.colSpan = 9;
    const tr = document.createElement('tr');
    tr.append(td);
    $('traffic').replaceChildren(tr);
    return;
  }

  $('traffic').replaceChildren(...others.map((a) => {
    const tr = document.createElement('tr');
    const icon = document.createElement('td');
    icon.append(kindIcon(a));
    tr.append(
      icon,
      cell(ident(a), 'flight'),
      cell(a.airline, 'carrier'),
      cell(isVehicle(a) ? 'Vehicle' : a.typeCode),
      cell(isGround(a) ? 'GND' : int.format(a.barometricAltitude || a.geometricAltitude), 'num'),
      cell(int.format(a.groundSpeed), 'num'),
      cell(deg(a.track), 'num'),
      cell(oneDp.format(a.distance), 'num'),
      cell(deg(a.bearing), 'num'),
    );
    return tr;
  }));
}

function renderRadar(data, sorted, nearest) {
  const rings = [0.25, 0.5, 0.75, 1].map((f) => {
    const t = svg('text', { x: 0.015, y: -f + 0.05 });
    t.textContent = `${+(data.radius * f).toFixed(1)}`;
    return t;
  });
  $('ring-labels').replaceChildren(...rings);

  // Draw furthest first so the nearest aircraft is on top.
  const blips = sorted.toReversed().map((a) => {
    const r = Math.min(a.distance / data.radius, 1.05);
    const b = (a.bearing * Math.PI) / 180;
    const x = r * Math.sin(b);
    const y = -r * Math.cos(b);
    const classes = ['blip'];
    if (a === nearest) classes.push('nearest');
    if (isGround(a)) classes.push('on-ground');
    if (isVehicle(a)) classes.push('vehicle');
    if (isEmergencyVehicle(a)) classes.push('emergency');

    const g = svg('g', { class: classes.join(' ') });
    const glyph = svg('g', { transform: `translate(${x} ${y}) rotate(${a.track})` });
    const size = a === nearest ? 0.11 : 0.08;
    glyph.append(svg('use', { href: radarSymbol(a), x: -size / 2, y: -size / 2, width: size, height: size }));
    g.append(glyph);
    // Ground traffic clusters at airports, where labels would be unreadable.
    if (!isGround(a)) {
      const label = svg('text', { x: x + 0.05, y: y + 0.015 });
      label.textContent = ident(a);
      g.append(label);
    }
    return g;
  });
  $('blips').replaceChildren(...blips);
}

function render(data) {
  const sorted = (data.aircraft ?? []).toSorted((a, b) => a.distance - b.distance);
  // Vehicles are never the nearest aircraft; they stay in the table and radar.
  const nearest = sorted.find((a) => !isVehicle(a));
  renderHeader(data);
  renderHero(data, nearest);
  renderTable(sorted.filter((a) => a !== nearest));
  renderRadar(data, sorted, nearest);
}

// ---- Polling -------------------------------------------------------------

async function errorFrom(res) {
  try {
    const body = await res.json();
    if (body.error) return new Error(body.error);
  } catch { /* not JSON */ }
  return new Error(`${res.status} ${res.statusText}`);
}

async function refresh() {
  try {
    // The server may block while it wakes from idle and fetches fresh data.
    const res = await fetch('/aircraft', { signal: AbortSignal.timeout(Math.max(30000, interval * 3)) });
    if (!res.ok) throw await errorFrom(res);
    latest = await res.json();
    render(latest);
    renderStatus(null);
  } catch (err) {
    // Keep the last good render on screen.
    renderStatus(err);
  }
}

let timer = 0;
let busy = false;
let again = false;

function schedule(ms) {
  clearTimeout(timer);
  if (!document.hidden) timer = setTimeout(poll, ms);
}

// poll refreshes once, then schedules the next poll only after this one
// has finished, so requests never overlap.
async function poll() {
  if (busy) {
    again = true;
    return;
  }
  busy = true;
  again = false;
  try {
    await refresh();
  } finally {
    busy = false;
    schedule(again ? 0 : interval);
  }
}

// Stop polling in a background tab so the service can go idle.
document.addEventListener('visibilitychange', () => {
  if (document.hidden) clearTimeout(timer);
  else poll();
});

// ---- Settings ------------------------------------------------------------

const dialog = $('settings');
const form = $('settings-form');

$('open-settings').addEventListener('click', () => {
  $('settings-error').textContent = '';
  if (latest) {
    form.lat.value = latest.location.lat;
    form.lon.value = latest.location.lon;
    form.radius.value = latest.radius;
  }
  dialog.showModal();
});

$('cancel-settings').addEventListener('click', () => dialog.close());

form.addEventListener('submit', async (e) => {
  e.preventDefault();
  const apply = form.querySelector('button[type="submit"]');
  apply.disabled = true;
  $('settings-error').textContent = '';
  try {
    const res = await fetch('/observer', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        location: { lat: form.lat.valueAsNumber, lon: form.lon.valueAsNumber },
        radius: form.radius.valueAsNumber,
      }),
    });
    if (!res.ok) throw await errorFrom(res);
    dialog.close();
    poll();
  } catch (err) {
    $('settings-error').textContent = err.message;
  } finally {
    apply.disabled = false;
  }
});

poll();
