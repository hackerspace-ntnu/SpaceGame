#!/usr/bin/env python3
"""Build the plain-language library site from the exported renders and the hand-written blurbs.

    python3 tools/build_library_site.py [-o docs/library/site.html]

Two inputs, produced by two different processes and deliberately kept apart:

  docs/library/library.json  what exists, and its picture — regenerated from the Unity project by
                             Tools/SpaceGame/Export Library Site Data, and safe to overwrite
  docs/library/blurbs.md     what each thing is, in plain language — written by hand, never
                             touched by the exporter

The output is one self-contained HTML file with every image inlined as a data URI, so it can be
published as an Artifact or opened straight off disk with no server and no asset folder beside it.

Entries with no blurb are reported and still shipped; a picture with no words is more useful than
a hole in the grid. Blurbs with no matching entry are reported as well, since that normally means
something was renamed or removed.
"""

import argparse
import base64
import html
import io
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
LIBRARY_DIR = ROOT / "docs" / "library"

# Order the tabs appear in, and what each is called on the page. Anything the exporter emits under
# a category not listed here still ships, in a trailing tab named after the raw category.
CATEGORIES = [
    ("artifacts", "Artifacts", "Things you hold and use"),
    ("agents", "Creatures & People", "Everything alive out there"),
    ("vehicles", "Vehicles", "Machines you ride, fly and crew"),
    ("gear", "Gear & Ship Parts", "Worn kit, supplies and hull modules"),
]

# Size guard. An Artifact page is capped at 16 MB rendered, and base64 costs about a third on top
# of the raw bytes, so warn well before the cliff rather than at it.
SIZE_WARN_MB = 12.0

# The exporter's `kind` is the project's own word for the thing — an artifact's EquipKind, or the
# folder a prefab came from. Those are accurate but internal, so each gets the word a player would
# use. An unmapped kind falls through as-is rather than being hidden, since a new one showing up
# raw on the page is how you find out the exporter learned a category.
KIND_LABELS = {
    "Hand": "Held",
    "Gauntlet": "Worn · forearm",
    "Back": "Worn · back",
    "creatures": "Creature",
    "Robots": "Robot",
    "Characters": "Person",
    "Vehicles": "Vehicle",
    "ShipParts": "Ship part",
    "Supplies": "Supply",
    "Equipment": "Worn kit",
}


def read_blurbs(path):
    """Parse `## id` sections out of the hand-written markdown.

    Everything above the first heading is the file's own preamble and is dropped.
    """
    if not path.exists():
        return {}

    blurbs = {}
    current = None
    lines = []
    for line in path.read_text(encoding="utf-8").splitlines():
        heading = re.match(r"^##\s+(\S+)\s*$", line)
        if heading:
            if current:
                blurbs[current] = "\n".join(lines).strip()
            current = heading.group(1)
            lines = []
        elif current:
            lines.append(line)
    if current:
        blurbs[current] = "\n".join(lines).strip()
    return blurbs


def paragraphs(text):
    """Markdown-lite: blank-line paragraphs, `**bold**` and `*italic*`. Nothing else is used in
    blurbs.md, and anything else would be a sign the prose is getting too clever for a card."""
    out = []
    for block in re.split(r"\n\s*\n", text):
        block = html.escape(" ".join(block.split()))
        block = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", block)
        block = re.sub(r"(?<!\*)\*([^*]+?)\*(?!\*)", r"<em>\1</em>", block)
        if block:
            out.append(f"<p>{block}</p>")
    return "\n".join(out)


def data_uri(path):
    """Inline one render, keying its magenta background out to transparency.

    LibraryExporter shoots against magenta because Unity's preview rig will not return an alpha
    channel — asking it for a transparent background yields solid black, which is a box on a light
    card. Keying here rather than in Unity also means the choice of card colour stays a decision
    the site gets to make.

    A pixel's alpha is how far it is from the key colour, so the antialiased rim of the model
    fades out instead of leaving a magenta fringe. The colour is then un-mixed from those rim
    pixels; without that they keep a pink cast wherever the model meets the background.
    """
    from PIL import Image
    import numpy as np

    img = np.asarray(Image.open(path).convert("RGB")).astype(np.float32) / 255.0
    key = np.array([1.0, 0.0, 1.0], dtype=np.float32)

    # Distance from the key, normalised so a fully keyed pixel is 0 and anything a fifth of the
    # way off it is fully opaque. Magenta is far from every colour the models use, so this
    # threshold never bites into the model itself.
    dist = np.linalg.norm(img - key, axis=-1) / 0.2
    alpha = np.clip(dist, 0.0, 1.0)

    safe = np.maximum(alpha, 1e-4)[..., None]
    rgb = np.clip((img - key * (1.0 - safe)) / safe, 0.0, 1.0)

    # Despill. Un-mixing alone leaves a pink rim on anything thinner than a pixel — the ostrich's
    # neck vertebrae and the cargo cage's wires are mostly background by area, so their recovered
    # colour is dominated by what they were shot against. Magenta is red and blue without green,
    # so the excess of the smaller of those two channels over green is spill and nothing else;
    # pulling it back leaves grey. An orange or a neutral has no such excess and is untouched.
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    spill = np.clip(np.minimum(r, b) - g, 0.0, None)
    rgb = np.stack([r - spill, g, b - spill], axis=-1)

    rgba = np.concatenate([np.clip(rgb, 0.0, 1.0), alpha[..., None]], axis=-1)
    out = Image.fromarray((rgba * 255.0 + 0.5).astype(np.uint8), mode="RGBA")

    buf = io.BytesIO()
    out.save(buf, format="PNG", optimize=True)
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode("ascii")


def build(entries, blurbs):
    by_category = {}
    for entry in entries:
        by_category.setdefault(entry["category"], []).append(entry)

    known = [c for c in CATEGORIES if c[0] in by_category]
    extra = [(c, c.title(), "") for c in sorted(by_category) if c not in dict((k, 1) for k, *_ in CATEGORIES)]
    tabs = known + extra

    nav = "\n".join(
        f'<button class="tab" data-cat="{html.escape(key)}">{html.escape(label)}'
        f'<span class="count">{len(by_category[key])}</span></button>'
        for key, label, _ in tabs
    )

    sections = []
    for key, label, tagline in tabs:
        cards = []
        for entry in sorted(by_category[key], key=lambda e: e["name"]):
            name = html.escape(entry["name"])
            blurb = blurbs.get(entry["id"], "")
            summary = html.escape(" ".join(blurb.split())[:150]) if blurb else "No description yet."
            body = paragraphs(blurb) if blurb else '<p class="missing">No description written yet.</p>'

            if entry.get("image"):
                image_path = LIBRARY_DIR / entry["image"]
                figure = (f'<img src="{data_uri(image_path)}" alt="{name}" loading="lazy">'
                          if image_path.exists() else '<div class="noimage">no render</div>')
            else:
                figure = '<div class="noimage">no render</div>'

            kind = html.escape(KIND_LABELS.get(entry.get("kind", ""), entry.get("kind", "")))
            source = html.escape(entry.get("source", ""))

            cards.append(f"""<article class="plate" tabindex="0" data-search="{html.escape((entry['name'] + ' ' + blurb).lower())}">
  <div class="stage">{figure}</div>
  <div class="ident">
    <h3>{name}</h3>
    <span class="kind">{kind}</span>
  </div>
  <p class="summary">{summary}</p>
  <div class="detail">
    {body}
    <p class="provenance">{source}</p>
  </div>
</article>""")

        sections.append(f"""<section class="cat" data-cat="{html.escape(key)}">
  <p class="tagline">{html.escape(tagline)}</p>
  <div class="grid">
{chr(10).join(cards)}
  </div>
</section>""")

    return (TEMPLATE.replace("{{NAV}}", nav)
                    .replace("{{SECTIONS}}", "\n".join(sections))
                    .replace("{{TOTAL}}", str(len(entries)))
                    .replace("{{CLASSES}}", str(len(tabs))))


TEMPLATE = """<title>The SpaceGame Field Catalogue</title>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Chivo:wght@700;900&family=IBM+Plex+Mono:wght@500&family=IBM+Plex+Sans:wght@400;500&display=swap">
<style>
  /* Cool mineral ground throughout. Every render is a warm orange-and-steel model standing on
     transparency, so the page stays grey-blue and lets the models carry the only real colour;
     brass is spent on interactive state and nothing else. */
  :root {
    --ground: #e9ecf0;
    --plate: #f7f9fb;
    --stage: #dfe4ea;
    --ink: #11151b;
    --muted: #5a6573;
    --rule: #ced5dd;
    --brass: #8a6015;
    --on-brass: #fdf8ef;
    --lift: 0 1px 2px rgba(17,21,27,.07);
  }
  @media (prefers-color-scheme: dark) {
    :root:not([data-theme="light"]) {
      --ground: #0e1116;
      --plate: #161b22;
      --stage: #1d232b;
      --ink: #e4e8ee;
      --muted: #8a94a2;
      --rule: #262e38;
      --brass: #d9a154;
      --on-brass: #14100a;
      --lift: 0 1px 2px rgba(0,0,0,.45);
    }
  }
  :root[data-theme="dark"] {
    --ground: #0e1116;
    --plate: #161b22;
    --stage: #1d232b;
    --ink: #e4e8ee;
    --muted: #8a94a2;
    --rule: #262e38;
    --brass: #d9a154;
    --on-brass: #14100a;
    --lift: 0 1px 2px rgba(0,0,0,.45);
  }

  *, *::before, *::after { box-sizing: border-box; }

  body {
    background: var(--ground);
    color: var(--ink);
    font: 400 16px/1.6 "IBM Plex Sans", ui-sans-serif, system-ui, -apple-system, sans-serif;
  }
  .wrap { max-width: 1240px; margin: 0 auto; padding: 0 24px 96px; }

  .label {
    font: 500 11px/1 "IBM Plex Mono", ui-monospace, SFMono-Regular, Menlo, monospace;
    letter-spacing: .14em;
    text-transform: uppercase;
  }

  /* Masthead ------------------------------------------------------------------------------ */
  .masthead { padding: 64px 0 36px; border-bottom: 1px solid var(--rule); }
  .eyebrow { color: var(--muted); margin: 0 0 18px; }
  h1 {
    font: 900 clamp(38px, 6.4vw, 68px)/0.98 Chivo, ui-sans-serif, system-ui, sans-serif;
    letter-spacing: -.028em;
    text-wrap: balance;
    margin: 0 0 20px;
    max-width: 15ch;
  }
  .lede { max-width: 62ch; color: var(--muted); margin: 0; font-size: 17px; }

  .facts {
    list-style: none; margin: 34px 0 0; padding: 0;
    display: flex; flex-wrap: wrap; gap: 14px 48px;
  }
  .facts div { color: var(--muted); margin-bottom: 3px; }
  .facts b {
    display: block;
    font: 700 26px/1.1 Chivo, ui-sans-serif, system-ui, sans-serif;
    font-variant-numeric: tabular-nums;
    letter-spacing: -.01em;
  }

  /* Controls ------------------------------------------------------------------------------ */
  .controls {
    position: sticky; top: 0; z-index: 5;
    background: var(--ground);
    border-bottom: 1px solid var(--rule);
    padding: 16px 0;
    margin-bottom: 34px;
    display: flex; flex-wrap: wrap; gap: 14px 28px; align-items: center;
  }
  .tabs { display: flex; flex-wrap: wrap; gap: 4px; }
  .tab {
    font: 500 11px/1 "IBM Plex Mono", ui-monospace, Menlo, monospace;
    letter-spacing: .14em; text-transform: uppercase;
    cursor: pointer; color: var(--muted);
    background: transparent; border: 1px solid transparent; border-radius: 3px;
    padding: 9px 12px;
    display: inline-flex; align-items: baseline; gap: 8px;
  }
  .tab:hover { color: var(--ink); border-color: var(--rule); }
  .tab[aria-selected="true"] { background: var(--brass); border-color: var(--brass); color: var(--on-brass); }
  .count { font-variant-numeric: tabular-nums; opacity: .62; }

  input[type="search"] {
    font: 400 14px/1 "IBM Plex Sans", ui-sans-serif, system-ui, sans-serif;
    flex: 1 1 210px; max-width: 300px; padding: 10px 12px;
    border: 1px solid var(--rule); border-radius: 3px;
    background: var(--plate); color: var(--ink);
  }
  input[type="search"]::placeholder { color: var(--muted); }

  :focus-visible { outline: 2px solid var(--brass); outline-offset: 2px; }

  /* Plates -------------------------------------------------------------------------------- */
  .tagline { color: var(--muted); margin: 0 0 20px; }
  .grid { display: grid; gap: 16px; grid-template-columns: repeat(auto-fill, minmax(238px, 1fr)); }

  .plate {
    background: var(--plate);
    border: 1px solid var(--rule);
    border-radius: 4px;
    padding: 14px;
    cursor: pointer;
    box-shadow: var(--lift);
    display: flex; flex-direction: column; gap: 10px;
  }
  .plate:hover { border-color: var(--muted); }

  .stage {
    background: var(--stage);
    border-radius: 2px;
    aspect-ratio: 1;
    display: grid; place-items: center;
    overflow: hidden;
  }
  .stage img { width: 100%; height: 100%; object-fit: contain; }
  .noimage { color: var(--muted); font-size: 12px; }

  .ident { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 10px; }
  .plate h3 {
    margin: 0;
    font: 700 17px/1.2 Chivo, ui-sans-serif, system-ui, sans-serif;
    letter-spacing: -.012em;
  }
  .kind {
    font: 500 10px/1 "IBM Plex Mono", ui-monospace, Menlo, monospace;
    letter-spacing: .12em; text-transform: uppercase;
    color: var(--muted);
  }
  .summary {
    margin: 0; color: var(--muted); font-size: 13.5px; line-height: 1.5;
    display: -webkit-box; -webkit-line-clamp: 3; -webkit-box-orient: vertical; overflow: hidden;
  }
  .detail { display: none; }
  .detail p { margin: 0 0 .85em; max-width: 64ch; }
  .detail p:last-child { margin-bottom: 0; }
  .provenance {
    font: 500 11px/1.5 "IBM Plex Mono", ui-monospace, Menlo, monospace;
    color: var(--muted);
    padding-top: 12px; margin-top: 14px;
    border-top: 1px solid var(--rule);
    overflow-wrap: anywhere;
  }
  .missing { color: var(--muted); font-style: italic; }

  /* Opened: the plate takes the full row and reads as a spread, render beside prose. */
  .plate.open {
    grid-column: 1 / -1;
    cursor: default;
    display: grid;
    grid-template-columns: minmax(0, 320px) minmax(0, 1fr);
    grid-template-areas: "stage ident" "stage detail";
    align-content: start;
    gap: 8px 28px;
    padding: 20px;
  }
  .plate.open .stage { grid-area: stage; aspect-ratio: 1; align-self: start; }
  .plate.open .ident { grid-area: ident; }
  .plate.open .detail { grid-area: detail; display: block; }
  .plate.open .summary { display: none; }
  .plate.open h3 { font-size: 26px; }
  @media (max-width: 640px) {
    .plate.open { grid-template-columns: minmax(0, 1fr); grid-template-areas: "stage" "ident" "detail"; }
  }

  .cat { display: none; }
  .cat.active { display: block; }
  .empty { color: var(--muted); padding: 48px 0; }

  footer {
    color: var(--muted); font-size: 13.5px;
    border-top: 1px solid var(--rule); margin-top: 64px; padding-top: 22px;
    max-width: 68ch;
  }
  footer code {
    font: 500 12.5px/1 "IBM Plex Mono", ui-monospace, Menlo, monospace;
    color: var(--ink);
  }

  @media (prefers-reduced-motion: no-preference) {
    .plate, .tab { transition: border-color .12s ease, background-color .12s ease, color .12s ease; }
  }
</style>

<div class="wrap">
  <header class="masthead">
    <p class="eyebrow label">Survey of a desert planet</p>
    <h1>The SpaceGame Field Catalogue</h1>
    <p class="lede">Everything that exists in the game today — every gadget you can hold, everything
      alive out there, every machine you can ride, and the kit and hull modules in between. Each one
      photographed from the game's own files and described in plain language. Click any plate to
      read it.</p>
    <ul class="facts">
      <li><b>{{TOTAL}}</b><div class="label">Catalogued</div></li>
      <li><b>{{CLASSES}}</b><div class="label">Classes</div></li>
      <li><b>22° / 135°</b><div class="label">One camera angle, all of them</div></li>
    </ul>
  </header>

  <div class="controls">
    <div class="tabs">{{NAV}}</div>
    <input type="search" id="q" placeholder="Search every plate…" autocomplete="off" aria-label="Search the catalogue">
  </div>

  {{SECTIONS}}

  <p class="empty" id="empty" hidden>Nothing in the catalogue matches that.</p>

  <footer>Every model is shot from its own prefab at the same three-quarter angle and fitted to its
    own bounds, which is what makes a pogo stick and a six-legged habitat read as one set. Rebuild
    the pictures with <code>Tools/SpaceGame/Export Library Site Data</code> in Unity, then the page
    with <code>tools/build_library_site.py</code>. The words live in
    <code>docs/library/blurbs.md</code> and are written by hand.</footer>
</div>

<script>
  const tabs = [...document.querySelectorAll('.tab')];
  const cats = [...document.querySelectorAll('.cat')];
  const search = document.getElementById('q');
  const empty = document.getElementById('empty');

  function selectTab(key) {
    tabs.forEach(t => t.setAttribute('aria-selected', String(t.dataset.cat === key)));
    applyFilter();
  }

  // A search spans every class, so it has to widen the view past the selected tab — otherwise
  // typing a creature's name while Artifacts is up looks like the creature does not exist.
  function applyFilter() {
    const q = search.value.trim().toLowerCase();
    const selected = tabs.find(t => t.getAttribute('aria-selected') === 'true');
    let hits = 0;

    cats.forEach(cat => {
      let shownHere = 0;
      cat.querySelectorAll('.plate').forEach(plate => {
        const match = !q || plate.dataset.search.includes(q);
        plate.hidden = !match;
        if (match) shownHere++;
      });
      hits += shownHere;
      cat.classList.toggle('active',
        q ? shownHere > 0 : cat.dataset.cat === selected.dataset.cat);
    });

    empty.hidden = hits > 0;
  }

  function toggle(plate) {
    const wasOpen = plate.classList.contains('open');
    document.querySelectorAll('.plate.open').forEach(p => p.classList.remove('open'));
    if (!wasOpen) plate.classList.add('open');
  }

  tabs.forEach(t => t.addEventListener('click', () => selectTab(t.dataset.cat)));
  search.addEventListener('input', applyFilter);

  document.addEventListener('click', e => {
    const plate = e.target.closest('.plate');
    if (plate) toggle(plate);
  });

  document.addEventListener('keydown', e => {
    if (e.key === 'Escape') {
      document.querySelectorAll('.plate.open').forEach(p => p.classList.remove('open'));
      return;
    }
    if ((e.key === 'Enter' || e.key === ' ') && e.target.classList.contains('plate')) {
      e.preventDefault();
      toggle(e.target);
    }
  });

  selectTab(tabs[0].dataset.cat);
</script>
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("-o", "--out", default=str(LIBRARY_DIR / "site.html"),
                        help="output HTML file (default: docs/library/site.html)")
    args = parser.parse_args()

    manifest = LIBRARY_DIR / "library.json"
    if not manifest.exists():
        sys.exit(f"{manifest} not found — run Tools/SpaceGame/Export Library Site Data in Unity first.")

    entries = json.loads(manifest.read_text(encoding="utf-8"))
    blurbs = read_blurbs(LIBRARY_DIR / "blurbs.md")

    missing = [e["id"] for e in entries if e["id"] not in blurbs]
    orphans = sorted(set(blurbs) - {e["id"] for e in entries})
    norender = [e["id"] for e in entries if not e.get("image")]

    out = pathlib.Path(args.out)
    out.write_text(build(entries, blurbs), encoding="utf-8")

    size_mb = out.stat().st_size / 1e6
    print(f"{out} — {len(entries)} entries, {size_mb:.1f} MB")
    if missing:
        print(f"  no blurb ({len(missing)}): {', '.join(missing)}")
    if orphans:
        print(f"  blurb with no entry ({len(orphans)}): {', '.join(orphans)}")
    if norender:
        print(f"  no render ({len(norender)}): {', '.join(norender)}")
    if size_mb > SIZE_WARN_MB:
        print(f"  WARNING: {size_mb:.1f} MB is close to the 16 MB artifact cap — "
              f"lower Resolution in LibraryExporter.cs")


if __name__ == "__main__":
    main()
