# Holo Base — build record

A base for the map hologram (`MapHologramTerrain`) to project over. Brief:
minimal, and anonymous when turned off.

## Reused, by path

- `components/mechanical/panel_control.py` — `rocker_bank`, `rotary_selector`,
  `ribbed_knob`, `guarded_toggle`, imported as builders (the sanctioned path;
  appending a .blend for a 40-triangle rocker is more machinery than the part
  is worth). Material indices 0–9 match its `MATS` index-for-index.
- `components/props/holo_emitter.py` — new component (below), same
  importable-builder pattern, extends the index contract with `GLASS` at 10.

## New component, and why it is separate

`components/props/holo_emitter.blend` — the projector lens heads
(`Coll_HoloEmitter_Dish` / `_Ring` / `_Stud`). Separate from any base because
a hologram lens is exactly what the next console, dashboard, or helmet wants
without the furniture underneath. All heads face **+Z** from a mount plane.

## Assembly

`holo_base.blend`, four variations with distinct silhouettes:

| Collection | Head | Size | Needed / ahead |
|---|---|---|---|
| `Coll_HoloBase_Puck` | Stud | Ø0.36 × 0.10 m | the request (most minimal) |
| `Coll_HoloBase_Pedestal` | Dish | Ø0.40 × 0.88 m | built ahead |
| `Coll_HoloBase_Table` | Ring | 0.91 × 0.58 m | built ahead (fits the 1.07 m hologram footprint) |
| `Coll_HoloBase_Tripod` | Dish | Ø~0.5 × 0.72 m | built ahead (field/outpost) |

Each collection also carries a `Marker_HoloAnchor_*` cube (0.004 m, at the
emitter aperture) — wire it to `MapHologramTerrain.projectorAnchor`.

Exported per-variation by `holo_base_export.py` to
`Assets/Game/Art/Models/Props/holo_base_{puck,pedestal,table,tripod}.fbx`.

## Materials

All from the existing palette; nothing added. Steel_Dark / Panel_Grey shells,
Black_Matte lens throats, Chrome_Scuffed lens rings, Glass_Canopy_Tinted
lenses, Rubber_Black feet/cables, one small Emissive_Amber standby pip per
unit. The pip is the only light: off, the base recedes (GDC-L1-UX-0003) but
still signifies "device" (GDC-L1-UX-0004); on, the runtime hologram shader is
the salient element — the model itself never glows.

## No armature

Nothing deforms or moves in play: the hologram is a shader, the controls are
decorative at this scale, and the tripod legs ship fixed because no deploy/fold
mechanic exists to drive them.

## Gotchas hit

- Tripod leg segments must have their centres computed **on the leg line**;
  `rot` spins a cylinder about its own centre, and `Matrix.Rotation(θ,4,'Y')`
  tilts the axis the opposite way from the computed direction (the library's
  known Y-rotation sign trap) — the first build scattered the legs.
