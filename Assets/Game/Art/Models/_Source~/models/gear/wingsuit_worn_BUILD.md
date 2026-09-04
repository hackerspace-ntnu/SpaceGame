# Wingsuit (worn) — build record

What the wingsuit looks like **on a player**, as opposed to `wingsuit.blend`, which is the flight
suit: a slim spar case on the lash rail, two spar ends past the flanks, and two membranes Unity
straps to the arm bones when the wings deploy. Worn, all you ever saw was the case — a box on
somebody's back, which says nothing about what the item is.

This is the other thing the same item has to be: **the wings shown between the arms**, on a figure
standing with its arms out at 45°. Two cloth panels running from each shoulder out along the arm
and down past the hip, carried on an over-shoulder yoke that laces back to the rail.

Source: `wingsuit_worn.py` → `wingsuit_worn.blend` → `wingsuit_worn_export.py` →
`Assets/Game/Art/Models/Items/wingsuit_worn.fbx`. Consumed by `WingsuitBuilder`
(Tools ▸ SpaceGame ▸ Items ▸ Build Wingsuit).

## Decomposition

Nine objects, nothing joined, no armature. The cloth deforms in `SpaceGame/ClothWind`; the yoke,
straps, spars and cuffs are rigid.

| Object | What it is | Material |
| --- | --- | --- |
| `Mesh_WingsuitWorn_Yoke` | The plate on the lash rail the whole thing laces back to | `Mat_Metal_Steel_Dark`, `Mat_Fabric_Canvas_Sand`, `Mat_Metal_Brass_Tarnished` |
| `Mesh_WingsuitWorn_Strap_L` / `_R` | Webbing from the yoke, over the clavicle, to the wing root | `Mat_Fabric_Canvas_Faded`, `Mat_Metal_Brass_Tarnished` |
| `Mesh_WingsuitWorn_Batten_L` / `_R` | The leading-edge spar the cloth is stretched over | `Mat_Metal_Steel_Worn`, `Mat_Metal_Brass_Tarnished` |
| `Mesh_WingsuitWorn_Membrane_L` / `_R` | The cloth | `Mat_Fabric_Wing_Beige` (replaced in Unity) |
| `Mesh_WingsuitWorn_Cuff_L` / `_R` | The ferrule and forearm wrap where the wing runs out | `Mat_Metal_Steel_Worn`, `Mat_Fabric_Canvas_Faded`, `Mat_Metal_Brass_Tarnished` |

Gone from the flight suit, deliberately: the spar case, the two clamps and the two spar stubs. All
three exist to describe a wing that is *folded away*, and there is nothing folded away here.

**Two real meshes per pair, never one mirrored.** A negative scale inverts winding, fights the
humanoid rig's non-mirrored bone frames, and makes every sign in the fit ambiguous — the trap the
gauntlet family documented once. 500 triangles is the price of no ambiguity.

**The shape maths is shared.** `_wingsuit.py` holds the loft — chord falloff, camber, hem, skin
taper — and both `wingsuit.py` and this file import it. It was extracted when the worn suit needed
the same cloth at a different size, and the extraction was proved behaviour-preserving by a
per-vertex, per-face fingerprint diff of every object in `wingsuit.blend` before and after.

## The frame — this is why every number is what it is

Authored in the **wearer's** frame at true scale, origin **on the spine bone** — the bone
`WornSeat` seats a torso item on: +X the wearer's left, +Z up, −Y forward. Unity's frame is
`(x, y, z) → (−x, z, −y)` of this, so +X here is Unity −X, which is the wearer's left there too.

Taken off the game rather than guessed — `PlayerCharacter.prefab`'s bind pose, read through the
skinned mesh's bind matrices, 2026-09-03 — in this file's own axes, metres:

| | |
| --- | --- |
| upper arm joint | (±0.233, 0.012, 0.637) |
| hip joint | (±0.143, −0.029, −0.269) |
| clavicle top | (±0.075, 0.003, 0.712) |
| lash rail | (0.000, 0.522, 0.630) |
| shoulder to wrist | 0.864 along the arm |

**Enlarged 2026-09-04: `WING_SCALE = 2`.** The panel used to end AT the arm — span
`ARM_LENGTH * SPAN_FRACTION`, cuff on the wrist — which is exactly why it read as small on the gear
screen: a wingsuit whose wing is as long as a forearm is a sleeve. Asked which half should double
when the arm cannot, the user chose the cloth running out **past the hands** rather than only
deepening the chord. So the constant scales the WING and nothing else — span, both chords, camber,
skin and the spar — while the yoke and the shoulder straps are left alone, because they are fitted
to a body that did not change size.

The cuff is the seam between those two facts. It is a webbing wrap round the *forearm*, so it stays
at `CUFF_SPAN` (the arm's own reach, the old full span) and now reads as the spar's mid-span
anchor, with the spar and cloth running on past it. Carried out to the new tip it would be a
forearm wrap closed round thin air 0.8 m past the wearer's hand. Its ferrule is sized off
`spar_radius_at(CUFF_SPAN / span)` for the same reason — off the tip radius it would be visibly
thinner than the spar it grips.

Result: **2.60 m span, 1.86 m tall, 0.60 m deep, 5,192 triangles, 9 objects** (was 1.58 × 1.03).
The cloth's lowest point is 0.83 m below the hip joint and still 0.17 m above the ankle. The old
span was smaller
than the 2.13 m the horizontal-armed cut measured for the obvious reason: an arm at 45° reaches
0.71 of the way out that a level one does.

## Four decisions worth the words

**The arm line is a 45° A, not a T** — the gear screen holds the wearer's arms at exactly that
angle (`InspectStance.DefaultDroop`), so the model and the screen are one number in two places and
have to move together. The first cut used a true T because it is the easy case: droop the leading
edge and the chord direction tilts *inboard* by the same angle, and at 45° a square 0.60 m chord
puts the root's trailing corner at x = 0.11, inside a torso whose half-width is about 0.20.

**`SWEEP` is what buys the lowered arm.** A loft's sections are perpendicular to its span by
construction and no arrangement of them can tilt a chord line, so the finished panel is **sheared**
along its own span instead: 1.0 m of rake per metre of chord. That carries the root's trailing
corner outboard onto the flank, and makes the free edge run wrist-to-hip the way a real arm wing's
does. It has a hard ceiling rather than a stylistic one — the trailing edge's own x is
`x + SWEEP · chord(x)`, so past about 1.35 (at this chord curve) it stops advancing and the panel
folds back through itself.

**"A bit folded, not truly extended"** (user, 2026-09-03) is spent on the cloth running out at 0.92
of the arm, so the wrist is bare, and on a chord falloff of 1.25 that sweeps the trailing edge back
rather than running it out as a long triangle.

**The back tilt is 18°, and eight of those degrees are structural.** Rolling the panel aft about the
arm axis carries its inboard corner *behind* the wearer instead of beside them. Measured, not
eyeballed: at 10° that corner lands at (0.106, 0.136) — inside a torso whose half-width is about
0.20 — and at 18° it lands at (0.179, 0.229), clear behind the hip. It is bounded above by the gear
screen's head-on camera, because a wing seen edge-on is a line.

**The leading edge is set 0.11 m off the arm, ALONG THE CHORD.** Not a look decision, an occlusion
one, and it was measured off a render: this astronaut's upper arm is about 0.12 m in radius, so a
leading edge on the arm line puts the cloth *inside* the arm and the wing disappears behind it from
the one angle that matters. Along the chord rather than along world down, so it stays correct at
any droop — which it had to, the moment the arm line moved from horizontal to 45°.

**One trap the droop set, and it is the kind that looks fine.** Every part of a side is placed from
`root(side_sign)` and stepped along the basis's own axes — the cuff sits at `root + basisX · CUFF_SPAN`.
It was written as a world `+X` step while the arm line was horizontal, where the two are the same
thing, and the moment the arm dropped to 45° both cuffs flew off sideways, level with the shoulders
and a foot clear of the wing they belong to.

## Two things the loft taught this file

**Station count is not a budget decision.** At the flight suit's 9×6 and this steeper falloff the
trailing edge came out visibly **sawtoothed** — nine flat quads across a curve that now turns hard
near the root, which reads as a notched hem rather than as cloth. 20×10 is where the teeth
disappear, and it costs about 700 triangles a panel.

**A wing placed on the left and one placed on the right get world bases of opposite handedness**,
so the axis that is "aft" for one side is "forward" for the other. `Wing.camber` therefore takes a
per-side sign — that is how both panels bow the same way in the world without a mirrored mesh.
`side_basis` asserts its determinant is positive on both sides rather than trusting the derivation.

## Materials

All six come from the palette; nothing was added. `Mat_Fabric_Wing_Beige` is the ornithopter's
sailcloth, which the flight suit already shares.

**`Mat_Metal_Steel_Worn` is material index 0 and that is not alphabetical.** `Part.bevel` stamps
index 0 onto every face it creates, so whatever sits first is what all the softened edges come out
as; with the cloth first the flight suit's clamp corners shipped as beige sailcloth once.

In Unity the membranes' material is **replaced** by the builder with a `SpaceGame/ClothWind`
material named `WingsuitWornMembrane`, which `WingsuitRecolor` paints in the wearer's suit colour.

## Verification

- `_zverify.py`: **0 clashing pairs**. The yoke's canvas facing sinks into its plate, its brass eyes
  are buried in it, the batten stands proud of the leading edge, and the strap's segments overlap
  rather than abut.
- Every object positive-determinant, asserted in the export.
- Origins are the **shoulder end of each leading edge** — the point the wing is strapped by — and
  the rail for the yoke.

## Unity

`WingsuitBuilder` nests it as the child **`WornModel`**, switched off on the asset;
[`WornVisual`](Assets/Game/Scripts/Items/Equipped/WornVisual.cs) switches it on and the flight
model off when the suit is worn, and `WingsuitWings` switches it back off for the length of a
glide. `WornFit.size` is pinned to **2.60** — the authored span, which the exporter prints — and
`WornFit.anchorToBone` is **on**: this model is shaped around the wearer, so it sits on the spine
bone rather than on the pack's lash rail half a metre behind it.

**No rotation is applied to the nested model, unlike the flight one.** An FBX from `_exportlib`
arrives already converted: every mesh node carries `(x, y, z) → (−x, z, −y)` and its own −90° X,
with the vertices left in Blender's axes. A −90° X on the parent is therefore a *second*
conversion; applied here it put both wings at the waist pointing backwards, and it still looked
plausibly like a wingsuit.
