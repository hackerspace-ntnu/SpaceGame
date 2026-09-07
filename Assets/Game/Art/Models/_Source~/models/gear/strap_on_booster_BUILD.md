# Strap-on booster — build record

A one-shot rocket you clamp to any physics object. Design:
[`docs/AI/systems/Artifacts/StrapOnBooster.md`](../../../../../../docs/AI/systems/Artifacts/StrapOnBooster.md).

## The problem this model actually has to solve

This is the one item in the set that is **not held like a gun**, and the model has to say so before
anybody reads a tooltip (`GDC-L1-UX-0004` — the correct action obvious, signified by the object
itself). Three things do that, and they are the whole design:

- **A nose clamp on the axis.** The jaws bite at the front, so the thrust line and the grip line are
  the same line — which is the item's one rule, *"how you stick it on is the aim"*. The design's own
  words are "a bell nozzle at one end, a hinged clamp jaw at the other", and taking that literally
  is what makes the item legible: the clamp is coaxial, not a bracket underneath.
- **A saddle and two ratchet straps.** The jaws alone would let a clamped booster pivot, and at a
  distance they are small. The straps are what reads as *strapped on* across a room, and their free
  ends hang below the mounting plane where a target would be.
- **A hazard band in stripes, not in a colour.** Eight alternating wedges of `Mat_Paint_Warn_Red` and
  `Mat_Paint_Hazard_Yellow`. A pattern keeps its meaning in greyscale and in silhouette, which a
  painted ring does not (`GDC-L1-UX-0006`).

## Size — from what it clamps to, not from the hand

The design says "about 0.4 m", and this one is **0.392 m**. That number is a consequence, not a
target: the clamp's throat is `device_clamp`'s 62 mm open by 48 mm deep, which is what takes the lip
of a supply crate (`Mesh_Crate_Small`, 0.78 × 0.55 × 0.444 m), a hull-plate edge or a saddle rail;
the straps wrap a 0.104 m body with their tails free; the bell needs a 0.028 m throat to read as
thrust rather than as a funnel. Add those up and you get 0.39 m.

For scale, `models/gear/dragon_bazooka.blend` measures 1.3685 m and anchors `ItemScaleLadder` at a
`holdSize` of 1.25. A booster is not on the hand's ladder in spirit — it is sized by its target — but
0.39 m is comfortably a hand tool beside it.

## Reused from the library

| Reused | From | Role |
| --- | --- | --- |
| `Mesh_DeviceClamp_JawFixed`, `Mesh_DeviceClamp_JawMoving` | `components/props/device_clamp.blend` (new, this build) | the nose bite |
| `Mesh_DeviceClamp_Pad` | same | the saddle |
| `Mesh_DeviceClamp_Strap` ×2 | same | the two ratchet bands |
| `Mesh_DeviceNozzle_Bell` | `components/props/device_nozzle.blend` (new, this build) | the exhaust |
| `Mesh_DeviceGauge_Lamp` | `components/props/device_gauge.blend` (new, this build) | the arming light |

**`components/props/dragon_rocket.blend` was rejected deliberately.** It is the right size
(0.30 m) and the right shape, and it is a *firework*: vermilion lacquer, gold leaf, canted fins, a
dragon's ammunition. This is clean issued equipment. Sharing the part would have made both worse —
the same call `dragon_bazooka_BUILD.md` records for the gravel blaster.

**`Mesh_SprayerNozzle_Bell` in `sprayer_kit.blend` was rejected on measurement.** It is 0.180 m
across — nearly twice this booster's entire body diameter, and shaped as a spray cone rather than a
rocket bell. Scaling it to fit would have meant applying a 0.58 scale to another agent's component
and inheriting a wall thickness of 3 mm.

## New components

Three, all under the `device_*` prefix, plus one shared module. Each is a thing this nine-device
family needs more than once, and each was checked against `sprayer_kit.blend` first so the two sets
do not overlap.

| Component | Variations | Why it is separate |
| --- | --- | --- |
| `components/props/device_clamp.blend` | `Pad` 0.110 × 0.200 × 0.039 · `Jaw` 0.032 × 0.139 × 0.089 (two objects) · `Strap` 0.036 × 0.117 × 0.172 · `Magnet` 0.090 × 0.090 × 0.045 | Nothing here is about rockets. A cradle, a biting jaw and a ratchet strap are what any of this kit needs to be attached to a crate, a hull, a saddle rail or a person's back. |
| `components/props/device_nozzle.blend` | `Bell` 0.116 × 0.094 × 0.116 · `Brass` 0.038 × 0.076 × 0.038 · `Iris` 0.172 × 0.076 × 0.172 (seven objects) | A muzzle is the part of a device the player aims, so it is also the part that has to say what the device does. |
| `components/props/device_gauge.blend` | `Dial` 0.056 × 0.033 × 0.056 (two objects) · `Lamp` 0.026 × 0.019 × 0.026 · `Ladder` 0.036 × 0.024 × 0.084 | Three readouts `sprayer_kit.blend` does not have. It deliberately has **no plain fill bar**: `Mesh_SprayerGauge_Plate` already is one and satisfies the whole `SupplyGauge` contract. |
| `components/props/device_kit.py` | — | The 20-slot material list and the bevel width the family is built against. Index 0 must stay a structural metal: `bmesh.ops.bevel` stamps material 0 onto every face it creates. |

`Magnet` is the only variation built purely ahead; nothing references it.

## How it assembles

Bell at **+Y**, matching `dragon_rocket.blend`, so thrust drives the target toward −Y — the library's
forward and the axis every other item points along. The origin is on the **mounting plane** at the
centre of the saddle's contact face, *not* on the body axis: an attach pose is stored in the target's
local space and is exactly "put this point on the surface, with its −Z into it", so the origin is
the pose.

```
        y -0.200   nose face ── JawFixed (down) / JawMoving (up, open 34 deg)
        y -0.176   ═ casing front, r 0.052, axis at z 0.088
        y -0.150   ⌒ StrapFore
   -0.070..-0.010  ▤ hazard band, 8 wedges at r 0.0545
        y +0.006   • arming lamp, on top
        y +0.040   ⌒ StrapAft
        y +0.112   ═ casing rear ── bell flange
        y +0.190   ▽ bell exit ── Marker_Muzzle
   under it        ▬ Cradle over Pad, contact plane at z 0  ── Marker_Mount
```

Three rotations, all derived rather than guessed, and all three are the kind that look almost right
when wrong:

- **The bell** turns a half turn about **Z**. That carries −Y to +Y and −X to +X, determinant +1, so
  nothing is mirrored. A half turn about X would also point it correctly and would roll the bolt
  flange a quarter turn out of line with the casing ribs.
- **The clamp** turns **−90° about X, then 180° about Z**. The first lands the clamp's press
  direction (−Z) on −Y, because a rotation about X carries (0, 0, −1) to (0, sin a, −cos a). The
  second swaps which side of the throat the hinged jaw is on: without it the lever opens *downward*,
  into the mounting plane, where it fouls the saddle and no hand could reach it.
- **The straps** turn **+90° about Z**, which carries +X (their wrap axis) to +Y and leaves +Z alone,
  so the band comes round the casing with its ratchet buckle still on top.

Both straps deliberately cross the saddle rather than sitting clear of it, so their tails run down
past its sides — which is what a strap threaded through a saddle looks like.

## Variations

- **`Coll_StrapOnBooster_Armed`** — 0.121 × 0.392 × 0.178 m, 7 370 tris. Jaws open, arming light lit.
  The carried item and the thing that flies.
- **`Coll_StrapOnBooster_Spent`** — 0.121 × 0.392 × 0.178 m, 8 290 tris. Light out behind a cracked
  cover, soot flared back over the casing from the bell throat. Both changes are **geometry**, not a
  material swap, so a spent booster in the sand reads as spent in a screenshot.
- `Coll_StrapOnBooster_Clamped` — jaws shut. Built ahead as the authored shut pose; it does not ship
  as an FBX, because a prefab with both states in one transform is better served by rotating
  `Mesh_DeviceClamp_JawMoving` about its own local X, which is exactly what its origin on the hinge
  pin is for. The collection exists to match that rotation against.

Every shared object is built once and linked into all three.

## Articulation — and why there is no armature

One moving part: `Mesh_DeviceClamp_JawMoving`, origin on the hinge pin, authored open at 34°, shut at
0° about its local X. `Coll_StrapOnBooster_Clamped`'s `Mesh_StrapOnBooster_JawShut` is that same
component with the rotation applied, kept as a reference pose. One rigid part about one axis does not
earn an armature.

## Markers

| Marker | Blender | Unity | What it is |
| --- | --- | --- | --- |
| `Marker_Muzzle` | (0, 0.190, 0.088) | (0, 0.088, −0.190) | the bell exit, and therefore **the thrust exit**. Flame and smoke go here. |
| `Marker_Mount` | (0, −0.045, 0) | (0, 0, 0.045) | the centre of the saddle's contact face, and the model's own origin plane. **This is the attach pose**: put it on the surface with its −Z into it. |
| `Marker_Clamp` | (0, −0.176, 0.088) | (0, 0.088, 0.176) | the nose throat, between the jaws. Where a bitten edge sits. |
| `Marker_Grip` | (0, −0.020, 0.088) | (0, 0.088, 0.020) | on the bore axis, where a hand wraps the casing. Carried like a thermos. |

## Palette

**No material was added.** The function colours are `Mat_Paint_Warn_Red` and
`Mat_Paint_Hazard_Yellow`, alternating, plus `Mat_Emissive_Red_Warn` for the arming light and
`Mat_Metal_Rust_Heavy` for the spent booster's scorch. Shell is the family's
`Mat_Paint_White_Arctic`, straps are `Mat_Fabric_Canvas_Faded`.

## Faults found and fixed while building

1. **The nose cone was built inside out and swallowed the clamp.** `Part.cyl` puts `radius` at −axis
   and `radius_top` at +axis and takes a **positive** depth; written the other way round — 0.052 at
   the nose and a depth of `NOSE_TIP - BODY_FRONT`, which is negative — it produced an inverted cap
   that covered the jaws entirely. The numbers looked symmetrical. The render did not.
2. **A second `append_objects` of the same component silently returns the FIRST copy.** It resolves
   what it appended by name, so if the name is already taken it hands back the old object and leaves
   an unrenamed, unplaced `.001` behind — which surfaces much later as `save()` refusing an
   auto-suffixed name. `place()` now guards on it and says what to do: append the copy that gets a
   `rename` *first*, so the component's own name is free again.
3. **The saddle block's underside landed 0.5 mm from the hazard band's lowest wedge** — parallel,
   overlapping, exactly the pair `_zverify` calls coplanar. Dropping the block's underside to
   z 0.020 buries the wedge in it instead: embed, never abut.
4. **The arming light was under the aft strap and the bell.** A poor place for the one light that
   says whether the thing is still live. It moved to the gap between the band and the strap.
5. `_zverify` per collection: **0 clashing pairs** on Armed, Clamped and Spent.

## Shipping

`models/gear/strap_on_booster_export.py` writes two FBXs.

| FBX | From | Measured |
| --- | --- | --- |
| `Assets/Game/Art/Models/Items/strap_on_booster.fbx` | `Coll_StrapOnBooster_Armed` | 0.121 × 0.392 × 0.178 m |
| `Assets/Game/Art/Models/Items/strap_on_booster_spent.fbx` | `Coll_StrapOnBooster_Spent` | 0.121 × 0.392 × 0.178 m |

## Decisions you may want made differently

- **The clamp is coaxial at the nose**, which is what the design's sentence says and what makes the
  aim rule legible. A booster that clamped *sideways* would need the pad to be the primary mount and
  the jaws to be outriggers, which is a different model.
- **Both straps cross the saddle.** They interpenetrate its edges, which is deliberate (threaded, not
  abutting) and is the one place in this model where two parts are meant to pass through each other.
- **The Spent variant keeps the live bell mesh** and adds soot around it rather than shipping a second
  scorched bell. One appended copy per component keeps the file honest; the scorch does the reading.
- **No `SupplyGauge` on this item, and that is deliberate.** The design gives it `Charges: 1`, not a
  tank — it is a consumable, not a reservoir — so a fill bar would be drawing a fraction that does
  not exist. `SupplyCharge.None` is not zero, and the same rule that says "only write a charge for an
  item that carries one" says this model must not offer a face for one. Its state readout is the
  arming lamp: lit, or out and cracked.
