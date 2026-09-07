# Vacuum canister — build record

A wide-mouthed vessel you suck a creature into. Design:
[`docs/AI/systems/Artifacts/VacuumCanister.md`](../../../../../../docs/AI/systems/Artifacts/VacuumCanister.md).

Written as the decomposition was decided, not proposed for approval, so a later reader can see
*why* the model is cut this way without reverse-engineering it from geometry.

## The problem this model actually has to solve

Empty and full are **two item identities**, the way an empty and a charged bottle are, and the
design's own words are "so the player can read a shelf of them at a glance". That is a readability
brief, not a modelling brief, so it is answered on **three redundant channels** —
`GDC-L1-UX-0003` (rank by salience; never make the player hunt) and `GDC-L1-UX-0006` (never carry
meaning in colour alone):

| | Channel | Empty | Full |
| --- | --- | --- | --- |
| 1 | **Lid — silhouette.** Reads at any distance, from any angle, in shadow, in a thumbnail. | thrown back off the mouth, throat and iris leaves visible | shut over the mouth, hook engaged in the collar's keeper |
| 2 | **Window — a lit outline.** Reads whatever the glass material does. | dark frame, bare slate emitter rod behind the pane | a cyan gasket lit right round the pane |
| 3 | **Contents — mass.** Reads when the glass is transparent. | nothing | five lit containment rings with a small dark animal curled between them |
| — | Colour | — | the cyan, and a red locked tab. **Only ever confirms 1–3.** |

Channel 2 exists because of a judgement the model does not get to make: `Mat_Glass_Canopy_Tinted`
is authored in the palette as glazing ("cockpit canopy, side viewports, gauge covers"), but whether
the Unity material for that slot is actually transparent is the wave-2 assets agent's call. A model
must not stake its whole reading on somebody else's shader, so the gasket sits **outside** the
glass and is unaffected by it.

## Reused from the library

| Reused | From | Why it served |
| --- | --- | --- |
| `Mesh_SprayerGauge_Plate` | `components/props/sprayer_kit.blend` | The kit's shared `SupplyGauge` face. It already satisfies the whole contract `OxygenGearBuilder.MeasureGauge` reads — exactly one emissive strip in `Mat_Emissive_Green_CRT`, symmetric about the gauge mesh's own middle, facing −Y. Building a second bar would have been the "two subtly different greys" failure with a different subject. |
| `Mesh_DeviceNozzle_IrisRing` + `Mesh_DeviceNozzle_IrisLeaf_1…6` | `components/props/device_nozzle.blend` (new, this build) | The mouth's shutter. |

`Mesh_SprayerNozzle_Iris` in `sprayer_kit.blend` was considered and is **not** interchangeable: it
is a single static mesh, and this design names the irising shutter as a moving part. Ours is six
separately hinged leaves.

The vessel itself follows `components/props/oxygen_tank.blend`'s language deliberately — it stands
on +Z with its instrument face on −Y, pale enamel shell with one saturated accent on the parts that
have their own shape (collar, foot band, lid rim) — so the two read as the same issued kit on a
shelf.

## New components

| Component | Why it is separate |
| --- | --- |
| `components/props/device_nozzle.blend` | Muzzles are the part of a device the player aims, and this family has nine of them. See `device_*` in the strap-on booster's record for the full accounting. |
| `components/props/device_kit.py` | The material list and bevel width the whole family is built against. A second copy of either is how a family ends up with two whites and four bevels (`GDC-L1-CONTENT-0003`). |

## How it assembles

Origin at the centre of the base. Axis +Z. Window and gauge on −Y, handle on +Y.

```
   z 0.528  ┄ open lid's far rim ┄┄┄┄┄┄ (Empty only)
   z 0.458  ─── knob ── LidShut / LidOpen, both hinged at (0, 0.090, 0.408)
   z 0.416  ═══ mouth rim ══ iris ring, seated at z 0.405
   z 0.356  ┌── collar r 0.100, blue, with the latch keeper on -Y
   z 0.330  │   Mesh_SprayerGauge_Plate, seat y -0.086
   z 0.300  │ ┌ window: 4-bar frame, 16 mm pane, cyan gasket (Full)
   z 0.110  │ └   emitter rod / field rings / captive behind it
   z 0.028  └── barrel, a TUBE r 0.088 wall 0.010
   z 0.000  ─── splayed foot, six rubber legs
```

- **The barrel is a tube, not a cylinder.** The iris leaves fold 60 mm back into the throat when the
  shutter is open; a capped barrel would have them poking out through their own vessel. It is also
  what makes the window a window.
- **The mouth points +Z**, which is where the design puts it — latch on top, shutter under the latch.
  It is therefore *not* the axis the item is aimed along; the prefab's `ItemGrip.rotationOffset` has
  to say so, and `Marker_Muzzle` plus `Marker_Grip` are what wave 2 derives that from.
- **The gauge is on the neck, not on the collar.** The design says "beside the latch"; the collar is
  where the latch keeper and the lid's hook both live, and a gauge there is either inside the keeper
  or buried under the collar's own radius. Directly below it, on the barrel, is the same place to
  look and the only one that is readable in the hand.

## Variations

Bold is what the design needs; the rest is built ahead, per the skill's overproduction rule. All
differ in structure, not only colour.

- **`Coll_VacuumCanister_Empty`** — 0.216 × 0.411 × 0.528 m, 7 908 tris
- **`Coll_VacuumCanister_Full`** — 0.216 × 0.268 × 0.458 m, 9 460 tris
- `Coll_VacuumCanister_Cracked` — 0.216 × 0.260 × 0.555 m, 7 932 tris. Latch sprung, pane in three
  tilted shards, no field. Loot and set dressing; nothing references it and it does not ship as an
  FBX.

Every shell object is built **once** and linked into all three collections, so a later edit to the
vessel cannot land on one identity and miss the others. Only the lid, the pane and the contents are
per-variation.

**The two identities are not the same height** — 0.458 m shut, 0.528 m with the lid thrown back —
because an open-lidded jar genuinely is taller. `holdSize` is per prefab, so wave 2 sets each from
its own longest axis; the mean is 0.49, which is the design's "about 0.5 m".

## Articulation — and why there is no armature

Two moving parts, both rigid, both turning about one axis, both carrying their pivot in their own
object origin. That is the same call `dragon_bazooka.py` made for its jaw: an armature for one rigid
part turning about one axis is dead weight, and the FBX hands Unity a plain transform instead.

| Part | Pivot | Motion |
| --- | --- | --- |
| `Mesh_VacuumCanister_LidOpen` / `_LidShut` / `_LidSprung` | (0, 0.090, 0.408), local X | shut is 0°, open is −148° |
| `Mesh_DeviceNozzle_IrisLeaf_1…6` | each leaf's own hinge pin; **local +Z is the hinge axis on all six** | authored open; one shared local rotation closes them |

The iris leaves are the one place in this build where objects keep a **rotation** instead of having
it applied. It is deliberate and it is documented in `device_nozzle.py`: baking the arrangement into
the meshes would have left six leaves whose hinge axes all differ and are recoverable only by
trigonometry.

## Markers

Shipped as 4 mm meshes, because empties do not survive `object_types={"MESH"}` — the same trick
`net_gun.blend` uses. The prefab reads the position and deletes the cube.

| Marker | Blender | Unity | What it is |
| --- | --- | --- | --- |
| `Marker_Muzzle` | (0, 0, 0.424) | (0, 0.424, 0) | mouth centre, 4 mm clear of the rim. The suction beam's origin. |
| `Marker_Grip` | (0, 0.130, 0.240) | (0, 0.240, −0.130) | inside the D-handle. `ItemGrip.gripPoint`. |
| `Marker_Gauge` | (0, −0.095, 0.330) | (0, 0.330, 0.095) | the face of `Mesh_SprayerGauge_Plate`. |

## Palette

**No material was added.** Everything is an existing palette entry, listed in
`components/props/device_kit.py`. The family's shell is `Mat_Paint_White_Arctic`, matching the
sprayer kit; the canister's one function colour is `Mat_Paint_Blue_Station`, worn by the collar, the
foot band and the lid rim — three parts that each have their own shape, so the identity survives
shadow and colour blindness. `Mat_Emissive_Portal_Blue` is the containment field, and it is the only
emissive on the model besides the supply bar's `Mat_Emissive_Green_CRT`.

## Faults found and fixed while building

Each looked correct in the numbers and only failed on inspection.

1. **The window surround enclosed the pane.** Built as one solid box it completely contained the
   glass, so the captive, the field and the glass itself rendered as a grey slab — the entire point
   of the item, invisible. It is four bars now.
2. **The glass was exactly 2 mm from the surround's face** — `_zverify`'s coplanar threshold, and a
   whole window edge's worth of flicker. It is 16 mm thick now, its front 4 mm inside the frame and
   its back punched 2 mm past the barrel's bore.
3. **The lid's hook passed through the collar.** At y −0.086 against a collar of radius 0.100 the
   latch went straight through the thing it fastens. The cap now **overhangs** the collar (0.108
   against 0.100), which is also the oxygen bottle's language, and the collar grew a keeper the hook
   drops over.
4. **The hook read as a floating cube** on the open lid: 16 mm of tongue overlapping a cap rim by
   4 mm. It is 30 mm now.
5. **The interior was bright.** A dark liner above and below the window makes the mouth read as a
   throat; deliberately not a full sleeve, because behind the window the interior has to *be* the
   interior.
6. `_zverify` per collection: **0 clashing pairs** on Empty, Full and Cracked. The whole-file run
   reports seven — every one of them a pane against another variation's pane at the same origin, a
   documented false positive for a file holding several variations.

## Shipping

`models/gear/vacuum_canister_export.py` writes two FBXs, because it is two items.

| FBX | From | Measured |
| --- | --- | --- |
| `Assets/Game/Art/Models/Items/vacuum_canister.fbx` | `Coll_VacuumCanister_Empty` | 0.216 × 0.411 × 0.528 m |
| `Assets/Game/Art/Models/Items/vacuum_canister_full.fbx` | `Coll_VacuumCanister_Full` | 0.216 × 0.268 × 0.458 m |

`Coll_VacuumCanister_Cracked` deliberately does not ship: an FBX in `Assets/` that nothing
references is landfill. Adding it is one row in `TARGETS`.

## Decisions you may want made differently

- **The mouth on top.** It is what the design says (latch on top, shutter under it), and it means the
  aim axis is not the model's own +Z. If aiming turns out to fight the pose, the fix is a
  `rotationOffset`, not a remodel.
- **The lit gasket round the pane** is insurance against an opaque glass material. If the Unity glass
  is genuinely transparent, the gasket is redundant with the field rings behind it — harmless, but
  it could go.
- **`LID_OPEN = -148°`** is a judgement about how loudly "open" should read. It costs 70 mm of height
  and 145 mm of depth behind the canister. Anything shallower reads less clearly as open and, past
  about −100°, is actually *taller*, because the cap's rim swings up rather than back.
- **The captive is a shape, not a creature** — a hunched body, a lowered head, two ears, four tucked
  legs, a tail, 60 mm across, seen through tinted glass. Anything finer is invisible at that size and
  costs triangles on an item the player carries everywhere.
