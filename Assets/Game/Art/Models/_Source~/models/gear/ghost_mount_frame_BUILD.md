# Ghost Mount Frame — build record

`models/gear/ghost_mount_frame.blend` → `Assets/Game/Art/Models/Items/ghost_mount_frame.fbx`
→ `Assets/Game/Prefabs/Items/Equipment/Ghosts/GhostBack.prefab` (built by `GearGhostBuilder`).

The body screen's placeholder for an empty back site. A rack, not a pack: every player already
wears the expedition rig, so a pack silhouette over the shoulders would read as a second backpack.
Built to be seen PAST THE SHOULDERS — the front view's only sight of the back.

| Part | Dimensions |
|---|---|
| `Mesh_GhostMountFrame` | 0.90 wide x 0.55 tall x 0.05 deep; two uprights 0.72 m apart, a crossbar, a lower rail |

48 triangles, one object, one material. Generated 2026-09-03 by `ghost_mount_frame.py`; no hand
edits. Export: `ghost_mount_frame_export.py`.

- Origin at the bottom centre; up is +Z in Blender → +Y in Unity, so `WornFit.localEuler (0,0,0)`
  stands it along the spine. Edge-on in a screenshot → fix `localEuler` in `GearGhostBuilder`.
- `WornFit.size` 0.9 keeps it 1:1; `localPosition (0, 0.05, -0.22)` is the wing pack's seat.

## How the members are seated

The **uprights alone set every outer bound**, so the frame measures exactly `WIDTH x BAR x HEIGHT`
however the other members are tuned:

| Member | Extent (Blender, metres) |
|---|---|
| Uprights (x2) | x ±0.335..±0.385, y −0.025..0.025, z 0..0.55 |
| Crossbar | x −0.45..0.45, y −0.023..0.023, z 0.498..0.548 |
| Lower rail | x −0.36..0.36, y −0.015..0.015, z 0.2325..0.2625 |

The crossbar is `2 * EMBED` (4 mm) narrower in Y than the uprights and its top sits `EMBED` (2 mm)
below theirs; the lower rail is thinner again and its ends stop 25 mm inside each upright. **That
offset is not cosmetic.** Sized flush — the crossbar the same 0.05 section as the uprights, its top
at the same z = 0.55 — the two boxes share a top plane over the 50 mm the crossbar crosses each
upright, and share their y = ±0.025 flanks over the same span. Coplanar overlapping faces z-fight,
and they do so inside one mesh just as readily as between two objects. Offsetting the *interior*
members and leaving the uprights to define the silhouette fixes it without changing a measurement
anyone reads.

Verified after the build: FBX bounds, Blender axes, min (−0.4500, −0.0250, 0.0000) max
(0.4500, 0.0250, 0.5500), size (0.9000, 0.0500, 0.5500). Unity axes: min (−0.4500, 0.0000, −0.0250)
max (0.4500, 0.5500, 0.0250). Preview rendered iso and front (`_preview.py`) and read: a Π-shaped
frame standing up its own +Z, crossbar overhanging each upright by 0.09 m, lower rail across the
opening at 45% height.

## Decomposition

One object, deliberately. The frame is a single welded rack whose members have no independent life
— no upright, crossbar or rail could plausibly appear on another model, and none of them is ever
selected, moved or animated on its own. The library's `Part` idiom is exactly this: accumulate the
primitives that make up **one** logical part, emit one named mesh. Splitting a 48-triangle
placeholder into four objects would buy nothing and cost four draw calls on something the game
draws translucent behind the player.

## Variations

**One, and only one is wanted.** The overproduce-variations rule serves things that repeat in a
scene; this is a single UI affordance that renders at most once per player, and a second silhouette
for "empty back slot" would be a second answer to a question with one answer. Nothing was built
ahead.

## Articulation

**No armature.** Nothing on the frame moves — it is a static ghost the shader tints and fades.

## Materials

**No palette additions.** `Mat_Paint_Hull_Bleached` alone, linked from `palette.blend`. Material
choice barely matters here: the runtime ghost shader replaces it with a translucent tint at ~22%
opacity, and one palette material keeps the file honest about which one it started from.

---

The **gauntlet** placeholder is `models/gear/ghost_device.blend`, shipped by
`ghost_device_export.py`. It used to be `Coll_GauntletBase_Plain` out of
`components/props/gauntlet_base.blend`, on the reasoning that shipping the same mesh every gauntlet
was built on kept the ghost from drifting. That premise died on 2026-09-04, when the bracer became
something the player wears permanently: a ghost bracer would now be a translucent copy of a solid
thing already on the arm. What an empty slot is missing is the DEVICE, so the ghost is a blank
device standing on the real bracer's deck.
