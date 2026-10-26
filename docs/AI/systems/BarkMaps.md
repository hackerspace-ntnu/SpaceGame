---
system: BarkMaps
layer: presentation
summary: Opt-in painted bark and skin textures and moss for StylizedFoliage — thirteen tileable triplanar maps
paths:
  - Assets/Game/Art/Textures/Environment/Foliage/
symptoms:
  - "a bark map is switched on but the trunk still looks flat, or only the moss shows"
  - "a textured trunk is almost black and the bark pattern cannot be seen"
  - "a tree renders solid blue or red in an editor screenshot right after a shader change"
reads_with: [Foliage]
updated: 2026-10-25
---

# Bark Maps

The foliage shader's bark used to be a plain gradient. A bark material can now add a painted,
tileable bark map: it only **shades** the material's own colour (grooves darker, lit ridge lips
lighter) and adds an optional seam tint and moss, so palettes and colour variants still decide
the hue. Off by default. The slot works on any foliage material, not only bark: mushroom caps,
puffball skins, the pale mushroom stalks and the jellyfish bell switch it on too.

Art direction (user, 2026-10-25): plants are **not from Earth but still plants** — plausible
biology, no iconic Earth species (a birch map was made and removed), and **no glowing or neon
surfaces** (a seam-glow option was tried and removed).

## Model

| Map | Look | Previewed on |
| --- | --- | --- |
| `Bark_Ridged` | long vertical plates, open grooves | Broadleaf (+ moss) |
| `Bark_Furrowed` | deep furrows broken by horizontal checks | Oak_Giant (+ moss) |
| `Bark_Weathered` | sanded smooth, fine dry cracks | Dead |
| `Bark_Plated` | flat scales, longer than wide, deep cracks | Pine |
| `Bark_Ringed` | leaf-scar rings with diamond boots | Palm |
| `Bark_Coiled` | fused rope strands twisting up the stem | Spiral |
| `Bark_Pored` | a skin of shallow cups; seam = the pores | Lantern |
| `Bark_Shingled` | overlapping rounded scales, rows lapping down | Bulb |
| `Bark_Veined` | swollen skin, sinuous veins with tapering side veins; seam = veins | Jellyfish |
| `Bark_Nodular` | clustered blisters, the big ones pitted, on crazed skin | Mushroom stalk |
| `Bark_Beaded` | twisting ribs, round seeds tucked in the valleys; seam = seeds | Sail |
| `Cap_Cracked` | a skin split into soft polygonal pads; seam = the cracks | Bolete and nodding caps, pale stalks |
| `Cap_Mottled` | soft blotches, fine flecks and pores; seam = pores | Parasol, honey, inkcap, bonnet caps, puffballs |

- All 1024², tileable, in [Textures/Environment/Foliage/](Assets/Game/Art/Textures/Environment/Foliage/).
  Channels: **R** painted value (0.5 = the material's colour; light baked from the upper left),
  **G** seam (grooves, or the pores / veins / seeds above), **B** height. Import: **sRGB off**,
  mipmaps, Repeat, trilinear, aniso 4.
- Origin: Ridged / Furrowed / Weathered restyle the Quaternius Stylized Nature MegaKit bark sculpt
  (CC0): its normal map integrated back to height, then repainted. The rest are procedural
  heightfields run through the same painter; `Cap_Cracked` (periodic Voronoi pads) and `Cap_Mottled` (FFT-filtered
  noise) likewise. The generators are not in the repo; the PNGs are the source.
- Material properties (`[Header(Bark Texture)]`): `Bark Texture` toggle (keyword `_BARK_TEXTURE`),
  `_BarkMap`, `_BarkTiling` (tiles per metre; 0.3–0.6 looked right for the 3 m player),
  `_BarkStrength`, `_GrooveColor` / `_GrooveStrength` (seam colour and tint — dark for pores and
  cracks, pale for veins, ochre for seeds), `_MossColor` / `_MossAmount` / `_MossHeight` (how far
  up the trunk, in height01).

## Key types

| Type | Role |
| --- | --- |
| [StylizedFoliage.shader](Assets/Game/Art/Shaders/Environment/StylizedFoliage.shader) | ForwardLit carries the rest offset + normal + height01 under `_BARK_TEXTURE` and calls `BarkAlbedo` after the accents. |
| `BarkAlbedo` in [StylizedFoliage.hlsl](Assets/Game/Art/Shaders/Environment/StylizedFoliage.hlsl) | Triplanar sample, `albedo × 2R`, seam tint by G, moss mask. |

## Flows

1. **Vertex**: `barkPos = (float3x3)ObjectToWorld × positionOS` — the *unswayed* offset from the
   pivot in world axes and metres; the world normal and `height01` (UV2.x) ride along.
2. **Fragment**: three projections (ZY, XZ, XY) of `barkPos × _BarkTiling`, blended by `|normal|⁴`.
   Colour = gradient albedo × lerp(1, 2R, `_BarkStrength`), then lerped to `_GrooveColor` by G.
3. **Moss**: `reach = (1 − height01 / _MossHeight) + 0.6 × up-facing`; the mask is
   `smoothstep(0.55, 0.7, _MossAmount × reach + 0.35 G − 0.25 B)`, so it fills grooves near the roots first.

Cost: three extra samples per bark pixel, only on materials with the keyword. Lighting stays per vertex.

## Multiplayer

N/A — pure presentation.

## Persistence

N/A — material state only.

## Gotchas

- **Object space is ×100 and Z-up** (vegetation FBX import). Projecting raw `positionOS` tiled the
  map ~100× too densely: it mipped to flat grey and only the moss showed. Hence the rotate-and-scale
  into world axes, without the translation, so the bark stays glued to the tree as it sways.
- **The shipped bark colours are near-black (~0.1).** The map can only multiply by 0–2, so on them
  the pattern is invisible. Raise the gradient and variation colours ~1.6× (Ridged), ~1.9×
  (Furrowed), ~1.15× (Plated), ~1.3× (Ringed, Weathered), ~1.4× (the purple alien barks), 1× (Bulb,
  Mushroom) when switching a map on. Veined at seam tint 0.4 on the dark purple is barely visible —
  raise the tint.
- **Moss is placed by height01**, the whole plant's 0–1 height, not metres: on a 30 m giant
  `_MossHeight` 0.25 reaches 7 m. Use smaller values on tall trees.
- **Triplanar ignores limb direction**: on a 45° branch the grain runs world-vertical, not along the
  limb. Fine at Ridged/Plated scales; it would show on strongly directional maps.
- **The first editor render after a shader import can come out in placeholder colours** (solid blue
  or red). Turn `ShaderUtil.allowAsyncCompilation` off and throw away one warm-up `cam.Render()`.
- Seen only in edit-mode preview-scene renders on Metal; not in play mode, on D3D, or on a client.

## Extending

- **Switch a bark material on**: tick `Bark Texture`, pick the map from the table, set
  `_BarkTiling` 0.3–0.5, brighten the colours (above). Every plant using that material changes, so
  check its other species too, and check the colour-variant bark materials (`_Charred` etc.) separately.
- **New map**: same channel layout (R centred on 0.5, G seam, B height), tileable, sRGB off.
  Keep to the art direction above: plausible non-Earth plant tissue, muted colour, no emission.
