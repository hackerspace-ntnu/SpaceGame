// The Look Lab shell: scene panels, manifest-driven sliders, presets, A/B flicker, and
// the POST that drives the running Editor.
//
// The sliders are built from the stage manifests' `params` blocks, not hand-written. That
// is what stops a lab that covers "everything downstream of the framebuffer" turning into
// a sprawl of bespoke panels — adding a stage file is enough to get its controls.

import { LabRenderer } from './gl.js';
import { buildPalette, paletteTexture } from './palette.js';

const PALETTE_WIDTH = 256; // must equal MaxPaletteSize in PastelQuantizeRenderFeature.cs
const GRID_COLUMNS = 3;
const MAX_PANELS = 6;      // six-up: as many as fit before a panel is too small to judge

const canvas = document.getElementById('canvas');
const labels = document.getElementById('labels');
const tabs = document.getElementById('tabs');
const stack = document.getElementById('stack');
const presetBar = document.getElementById('presets');
const status = document.getElementById('status');

const renderer = new LabRenderer(canvas);

const state = {
  stages: [],        // [{ id, label, params, body }]
  scenes: [],        // [{ name, pack }]
  values: {},        // param id -> number | number[]
  presets: [],       // names
  current: null,     // preset name, or null for an unsaved look
  pinned: null,      // { name, values } for A/B flicker
  flicker: false,
  gridMode: true,
  activeScene: null,
};

function say(message, isError = false) {
  status.textContent = message;
  status.classList.toggle('error', isError);
}

// --- loading -----------------------------------------------------------------

async function parseStage(name) {
  const text = await (await fetch(`../stages/${name}`)).text();
  const split = text.indexOf('\n---\n');
  if (split < 0) throw new Error(`${name}: no '---' line between the header and the GLSL`);
  const header = JSON.parse(text.slice(0, split));
  return { ...header, body: text.slice(split + 5) };
}

async function load() {
  const [stageNames, scenes, presets] = await Promise.all([
    fetch('/api/stages').then((r) => r.json()),
    fetch('/api/scenes').then((r) => r.json()),
    fetch('/api/presets').then((r) => r.json()),
  ]);

  // By declared order, not by filename: a stage's position in the chain is part of what
  // it means — anything that feeds the snap has to reach the frame before it — and a
  // rename must not be able to silently reverse that.
  state.stages = (await Promise.all(stageNames.map(parseStage)))
    .sort((a, b) => (a.order ?? 100) - (b.order ?? 100) || a.id.localeCompare(b.id));
  state.scenes = scenes;
  state.presets = presets;

  for (const stage of state.stages) {
    for (const [id, spec] of Object.entries(stage.params)) {
      state.values[id] = Array.isArray(spec.default) ? [...spec.default] : spec.default;
    }
  }

  renderer.compile(state.stages, scalarParamNames());

  await Promise.all(state.scenes.map(async (scene) => {
    const image = await loadImage(`../scenes/${encodeURIComponent(scene.name)}/color_ldr.png`);
    renderer.setSource(scene.name, image);
  }));

  state.activeScene = state.scenes[0]?.name ?? null;
  buildTabs();
  buildControls();
  buildPresetBar();
  pendingRebuild = true;
  requestDraw();

  if (!state.scenes.length) {
    say('No scene packs yet — capture some with SpaceGame > Look Lab > Capture Scene Pack.',
        true);
  } else {
    say(`${state.scenes.length} scene(s), ${state.stages.length} stage(s). ` +
        'Hold B to flicker against the pinned preset.');
  }
}

function loadImage(url) {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error('could not load ' + url));
    image.src = url;
  });
}

/** Only scalars become uniforms; list and `rebuild` params drive the CPU palette build. */
function scalarParamNames() {
  const names = [];
  for (const stage of state.stages) {
    for (const [id, spec] of Object.entries(stage.params)) {
      if (!spec.list && !spec.rebuild) names.push(id);
    }
  }
  return names;
}

// --- palette -----------------------------------------------------------------

function shapeFromValues(values) {
  return {
    hueCount: Math.round(values.hueCount),
    lightnesses: values.lightnesses,
    chromaFractions: values.chromaFractions,
    chromaCeiling: values.chromaCeiling,
    neutralCount: Math.round(values.neutralCount),
    neutralMinL: values.neutralMinL,
    neutralMaxL: values.neutralMaxL,
  };
}

function rebuildPalette(values) {
  const colors = buildPalette(shapeFromValues(values));
  if (colors.length > PALETTE_WIDTH) {
    say(`That lattice builds ${colors.length} colours; the shader holds ${PALETTE_WIDTH}.`,
        true);
    return false;
  }
  renderer.setPalette(paletteTexture(colors, PALETTE_WIDTH), PALETTE_WIDTH, colors.length);
  return true;
}

// --- UI ----------------------------------------------------------------------

function buildTabs() {
  tabs.replaceChildren();
  const grid = button(`Grid (${Math.min(state.scenes.length, MAX_PANELS)})`, () => {
    state.gridMode = true;
    buildTabs();
    requestDraw();
  });
  grid.setAttribute('aria-pressed', String(state.gridMode));
  tabs.append(grid);

  state.scenes.forEach((scene) => {
    const tab = button(scene.name, () => {
      state.gridMode = false;
      state.activeScene = scene.name;
      buildTabs();
      requestDraw();
    });
    tab.setAttribute('aria-pressed',
      String(!state.gridMode && state.activeScene === scene.name));
    tab.title = describePack(scene.pack);
    tabs.append(tab);
  });
}

function describePack(pack) {
  if (!pack || !pack.capturedUtc) return 'no pack.json — provenance unknown';
  return [`captured ${pack.capturedUtc}`, `${pack.width}x${pack.height}`,
          `scene ${pack.scene}`, `git ${(pack.gitSha || '?').slice(0, 8)}`,
          `volumes: ${(pack.volumeProfiles || []).join(', ') || 'none'}`].join('\n');
}

function buildControls() {
  stack.replaceChildren();
  for (const stage of state.stages) {
    const heading = document.createElement('h2');
    heading.textContent = stage.label;
    stack.append(heading);
    for (const [id, spec] of Object.entries(stage.params)) {
      stack.append(spec.list
        ? listControl(id, spec)
        : sliderControl(spec, () => state.values[id], (v) => { state.values[id] = v; }, id));
    }
  }
}

function sliderControl(spec, get, set, labelText) {
  const row = document.createElement('div');
  row.className = 'row';
  const label = document.createElement('label');
  label.textContent = labelText;
  const range = document.createElement('input');
  range.type = 'range';
  range.min = spec.min;
  range.max = spec.max;
  range.step = spec.step ?? 0.01;
  range.value = get();
  range.id = 'p-' + labelText.replace(/\W/g, '-');
  label.htmlFor = range.id;
  const readout = document.createElement('output');
  readout.textContent = format(get(), spec);
  range.addEventListener('input', () => {
    set(spec.integer ? Math.round(+range.value) : +range.value);
    readout.textContent = format(get(), spec);
    onValuesChanged(spec.rebuild || spec.list);
  });
  row.append(label, range, readout);
  return row;
}

function format(value, spec) {
  return spec.integer ? String(value) : (+value).toFixed(2);
}

/** A list param gets one slider per entry plus add/remove, so the number of lightness
 *  steps is itself tunable without a bespoke panel. */
function listControl(id, spec) {
  const wrapper = document.createElement('div');
  const redraw = () => {
    wrapper.replaceChildren();
    state.values[id].forEach((_, index) => {
      wrapper.append(sliderControl(spec,
        () => state.values[id][index],
        (v) => { state.values[id][index] = v; },
        `${id}[${index}]`));
    });
    const controls = document.createElement('div');
    controls.className = 'list-controls';
    controls.append(
      button('+', () => {
        const list = state.values[id];
        list.push(list.length ? list[list.length - 1] : spec.min);
        redraw();
        onValuesChanged(true);
      }),
      button('−', () => {
        if (state.values[id].length <= 1) return;
        state.values[id].pop();
        redraw();
        onValuesChanged(true);
      }));
    wrapper.append(controls);
  };
  redraw();
  return wrapper;
}

function button(text, onClick) {
  const element = document.createElement('button');
  element.textContent = text;
  element.addEventListener('click', onClick);
  return element;
}

function buildPresetBar() {
  presetBar.replaceChildren();
  for (const name of state.presets) {
    const element = button(state.pinned?.name === name ? '📌 ' + name : name,
                           () => selectPreset(name));
    element.setAttribute('aria-pressed', String(state.current === name));
    presetBar.append(element);
  }
  if (!state.presets.length) {
    const empty = document.createElement('span');
    empty.textContent = 'none yet';
    empty.style.color = 'var(--dim)';
    presetBar.append(empty);
  }
}

// --- presets -----------------------------------------------------------------

async function selectPreset(name) {
  state.values = await fetch(`../presets/${encodeURIComponent(name)}.json`)
    .then((r) => r.json());
  buildControls();
  onValuesChanged(true);
  state.current = name; // onValuesChanged clears it; a load is not an edit
  buildPresetBar();
}

document.getElementById('fork').addEventListener('click', async () => {
  const name = prompt('Preset name', state.current ?? 'untitled');
  if (!name) return;
  const response = await fetch(`/api/presets/${encodeURIComponent(name)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(state.values),
  });
  if (!response.ok) return say(await response.text(), true);
  if (!state.presets.includes(name)) state.presets.push(name);
  state.presets.sort();
  state.current = name;
  buildPresetBar();
  say(`Saved preset "${name}".`);
});

document.getElementById('pin').addEventListener('click', () => {
  state.pinned = {
    name: state.current ?? 'unsaved',
    values: structuredClone(state.values),
  };
  buildPresetBar();
  say(`Pinned "${state.pinned.name}". Hold B to flicker against it.`);
});

document.getElementById('delete').addEventListener('click', async () => {
  if (!state.current) return say('No preset selected.', true);
  const name = state.current;
  const response = await fetch(`/api/presets/${encodeURIComponent(name)}`,
                               { method: 'DELETE' });
  if (!response.ok) return say(await response.text(), true);
  state.presets = state.presets.filter((p) => p !== name);
  state.current = null;
  buildPresetBar();
  say(`Deleted preset "${name}".`);
});

// --- A/B flicker -------------------------------------------------------------
//
// A held key rather than side by side: the eye detects change far better than it detects
// difference, so flicker resolves a shift two panels apart would hide.

window.addEventListener('keydown', (event) => {
  if (event.key.toLowerCase() !== 'b' || event.repeat || !state.pinned) return;
  // instanceof first: a keydown's target is not always an Element (it is the document or
  // the window when nothing is focused, and for any programmatic dispatch), and calling
  // .matches on those throws inside the listener — which silently kills the flicker.
  if (event.target instanceof Element && event.target.matches('input, textarea')) return;
  state.flicker = true;
  pendingRebuild = true;
  requestDraw();
});

window.addEventListener('keyup', (event) => {
  if (event.key.toLowerCase() !== 'b' || !state.flicker) return;
  state.flicker = false;
  pendingRebuild = true;
  requestDraw();
});

// --- drawing and the bridge --------------------------------------------------

let queued = false;
let pendingRebuild = false;

function onValuesChanged(needsRebuild) {
  pendingRebuild = pendingRebuild || Boolean(needsRebuild);
  state.current = null; // an edited preset is an unsaved look until it is saved again
  requestDraw();
  pushLook();
}

/** Panels repaint on change, not at 60fps — these are stills, so an idle stack is free. */
function requestDraw() {
  if (queued) return;
  queued = true;
  requestAnimationFrame(() => {
    queued = false;
    const values = state.flicker && state.pinned ? state.pinned.values : state.values;
    if (pendingRebuild) {
      pendingRebuild = false;
      if (!rebuildPalette(values)) return;
    }
    renderer.draw(layout(), uniformsFrom(values));
    drawLabels();
  });
}

function uniformsFrom(values) {
  const uniforms = {};
  for (const name of scalarParamNames()) uniforms[name] = values[name];
  return uniforms;
}

/** Panel rects in CSS pixels. Each panel is drawn at its *displayed* size, which is what
 *  keeps a 204-entry nearest-neighbour search affordable across six of them. */
function layout() {
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  if (!state.gridMode) {
    return state.activeScene
      ? [{ scene: state.activeScene, x: 0, y: 0, width, height }]
      : [];
  }
  const names = state.scenes.slice(0, MAX_PANELS).map((s) => s.name);
  const rows = Math.max(1, Math.ceil(names.length / GRID_COLUMNS));
  const cellWidth = width / GRID_COLUMNS;
  const cellHeight = height / rows;
  return names.map((scene, i) => ({
    scene,
    x: (i % GRID_COLUMNS) * cellWidth,
    y: Math.floor(i / GRID_COLUMNS) * cellHeight,
    width: cellWidth,
    height: cellHeight,
  }));
}

function drawLabels() {
  labels.replaceChildren();
  for (const panel of layout()) {
    const span = document.createElement('span');
    span.textContent = state.flicker ? `${panel.scene} — pinned` : panel.scene;
    span.style.left = `${panel.x + 6}px`;
    span.style.top = `${panel.y + 6}px`;
    labels.append(span);
  }
}

/** Drives the running Editor. Coalesced to one in-flight request: a slider drag fires
 *  faster than the round trip, and queueing them would make the Editor lag behind the
 *  page by an ever-growing backlog. */
let inFlight = false;
let sendAgain = false;

async function pushLook() {
  if (inFlight) { sendAgain = true; return; }
  inFlight = true;
  try {
    const response = await fetch('/api/look', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        enabled: true,
        blend: state.values.blend,
        palette: shapeFromValues(state.values),
      }),
    });
    if (!response.ok) say('Bridge write failed: ' + await response.text(), true);
  } catch (error) {
    say('Bridge unreachable: ' + error.message, true);
  } finally {
    inFlight = false;
    if (sendAgain) { sendAgain = false; pushLook(); }
  }
}

window.addEventListener('resize', requestDraw);

load().catch((error) => say(error.message, true));
