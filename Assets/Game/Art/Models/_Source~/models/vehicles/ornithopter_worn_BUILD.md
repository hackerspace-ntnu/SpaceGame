# Ornithopter (worn) — build record

What the Wing Pack looks like **on a player's back**, as opposed to `wing_pack_folded.blend`,
which is what it looks like in their hand.

A folded aircraft strapped to somebody's shoulders reads as luggage. The worn form throws the
aircraft away and keeps the two things that say *flight*: the webbed wings, and the spoked
shoulder mechanics that beat them. They hang down each side of the wearer off the two ends of the
expedition rig's lash rail — the one part of the pack that does **not** fold in, and which sticks
out well past each flank at almost exactly shoulder height.

Source: `ornithopter_worn.py` (a derivation, not a generator) → `ornithopter_worn.blend` →
`ornithopter_worn_export.py` → `Assets/Game/Art/Models/Vehicles/Ornithopter/ornithopter_worn.fbx`.
Consumed by `WingPackBuilder` (Tools ▸ Vehicles ▸ Build Wing Pack Item).

## Rebuilding it safely

`--commit` refuses to overwrite the shipped `.blend`, because a file that exists may carry hand
edits that live nowhere else. That guard is answered with a **control run**, not by deleting the
file and hoping: `--out /tmp/control.blend` writes the same parameters to a scratch path, and a
per-object vertex/polygon fingerprint of the two files proves the script still reproduces what
shipped. Both worn models passed that check byte-for-byte before the 2026-09-04 rebuild, which is
what made deleting them safe.

## Derivation, not modelling

No wing or gear geometry was authored. The script opens `dune_ornithopter.blend` — which carries
hand edits and is **never written** — culls fifteen parts, poses the rig in memory, bakes, and
saves the result to a new file. The same shape as `wing_pack_folded.py`, and for the same reason:
the wings are skinned, so they cannot be posed anywhere but in Blender.

| Kept | Dropped |
| --- | --- |
| `Mesh_Wing_L/R_Frame`, `Mesh_Wing_L/R_Web` | fuselage core, nose, boom |
| `Mesh_Bearing_L/R` (the open yoke) | tail hub, tail fan frame and web |
| `Mesh_DriveWheel_L/R` (the spoked wheel) | the centre drive cog |
| `Mesh_Crank_L/R` | the prone cradle: pad, grip bar, two stirrups |
| | `Mesh_Pylon_L/R`, `Mesh_Strut_L/R` — the supporting truss and tie-rods |

Renamed on the way out to `Mesh_OrniWorn_*`, so nothing downstream can confuse a worn wing with
the aircraft's.

**Nothing is joined**, unlike the folded bundle. That file bakes to one mesh because the held pack
never articulates and no part of it ever needs naming; a worn wing is looked at, so its twelve
parts stay twelve named objects.

## The frame — this is why every number is what it is

Authored in the **wearer's** frame at true wearer scale, origin **on the lash rail**: +X to the
wearer's left, +Z up, −Y forward. `WornSeat` puts a back item's origin on that rail, so this
model's origin lands there and its two shoulder pivots reach out along the rail's own two
protruding bars to their tips.

Measured off the game rather than guessed — `PlayerCharacter.prefab` with the folded
`ExpeditionRig` on its spine, 2026-09-03 — in the spine bone's frame, metres:

| | |
| --- | --- |
| lash rail tips | x = ±0.885, y = 0.630, z = −0.522 |
| upper arm joint | x = ±0.233, y = 0.637 |
| ankle | x = ±0.228, y = −1.259 |

`ROOT_HALF = 0.885` is the first of those, and it never moves — it is the mount. `TARGET_REACH`
is bounded by the last: the rail sits 0.63 m above the spine bone and the soles about 1.45 m
below it, so **the ground is 2.08 m under the rail** and that is the whole budget a hanging wing
has.

**Enlarged 2026-09-04, from 1.85 m of reach to 2.775** — half again — because the worn wings read
too small on the gear screen, which is the one place a player ever sees their own back. Growing
them in Unity was tried first and does not work: `WornFit.size` is a uniform scale about the rail,
so it drags the roots off the bar tips and hangs the tips through the ground. The length had to be
spent here, and spent OUTBOARD:

| | before | after |
| --- | --- | --- |
| reach from the pivot | 1.85 | **2.775** |
| `FLAP` (shoulder) | −72° | **−52°** |
| `SPLAY` (fan opening) | −52° | **−105°** |
| `ROLL` / `TWIST` | 35° / 18° | **38° / 14°** |
| span | 3.47 m | **5.51 m** |
| tip below the rail | 1.59 m | **1.61 m** (0.47 m clear of the soles) |

The span is what grew; the droop did not, and that is the point. At the old `FLAP` the 1.5× wing
hung 2.38 m down and swept a third of a metre through the sand. `SPLAY` is the other half of the
change: its magnitude *is* the fan's opening angle, and at −52 the wing read as five bare spars
with cloth scalloped between them. Opened to −105 the web closes into one continuous sail and the
spar tips stop protruding past the trailing edge — the "make the fabric a larger part of the
model" ask (user, 2026-09-04), bought with no geometry change at all.

Result: **5.51 m span, 1.78 m tall, 1.37 m deep, 8,736 triangles, 12 objects.** Same twelve parts,
same triangle count: nothing was modelled, the wing was re-posed and re-scaled about its pivot.

## The pose

| Move | Value | What it does |
|---|---|---|
| Shoulder flap | −72° | the wings come DOWN each side of the wearer |
| Arm sweep | ±16° | a little rearward rake, so a wing is not a flat sideways slab |
| Arm roll | ±35° | turns each fan out of the fore-aft plane — see below |
| Digit splay | −52° graded | part-closed: a wing at rest, not a wing in flight |
| Digit twist | 18° graded | web feathered, so the spars read against the cloth |

**Arm roll takes a per-side sign and digit twist does not, even though both are a rotation about a
bone's local Y.** `dune_ornithopter_BUILD.md`'s axis table covers the digits and says local Y
needs no sign; that does not carry to `Bone_Arm`. Measured, not reasoned: with the same sign on
both arms the model's bounds came out asymmetric (−1.51 to +1.86 at 40°) while the digits stayed
symmetric. The only way to tell is to print the bounds.

**Why roll at all.** At zero roll each wing's membrane lies in a fore-aft plane, so it is edge-on
from the front — and the gear screen, which is where a player decides what to wear, looks at the
character head on. 35° turns enough of the panel toward the camera to read as cloth and costs
0.34 m of span; 70° would face the camera fully and costs 1.0 m, which is a wingspan wider than
the wearer is tall.

## The clamps — the only new geometry

`Mesh_OrniWorn_Clamp_L/R`: a flat strap jaw on the rail with a strut up to the bearing. Without
it the mechanics hang beside the bar touching nothing, which reads as a wing hovering next to the
player rather than one bolted to their pack.

The bore is not a guess. The rail's own mesh was binned along its axis: every band reads
**0.134 m fore-aft by 0.040 m thick**, with the outermost 0.15 m of each end thickened by its loop
buckle. So the jaw is a flat strap clamp, not a round collar, and it sits at x = ±0.83 — just
inboard of the tip, where the strap is clean.

The strut is aimed at the **measured** bearing centre rather than at a typed offset, because the
flap swings the whole shoulder assembly down off the bar by an amount that changes with every pose
tweak. A hard-coded strut would leave a visible gap the moment `FLAP` moved, and nothing would
report it.

## Two traps this file paid for

**`bound_box` is a cached evaluation and is silently stale** right after a script has
retransformed a mesh and reset its object matrix. The first cut aimed both wing struts at
(0, 0, −0.088) because of it — through the wearer's spine instead of onto the bearings — and the
numbers looked plausible enough to print without complaint. Everything measures off
`obj.data.vertices` now.

**The port side is a mirrored placement in the source assembly**, so half the objects arrive with
a negative-determinant matrix once the bone parents are baked out. Blender draws that correctly and
the FBX carries the flip straight through to Unity, which renders the mesh inside-out with nothing
in the console. `flatten()` catches it by the determinant and rebuilds the winding; the export
asserts no object survives with a negative one.

## Materials

All inherited from the assembly, which links them from `palette.blend`; localised on commit so the
file stands alone, the way the exports do. The clamps reuse `Mat_Metal_Steel_Worn` and
`Mat_Metal_Brass_Tarnished`, which the aircraft already carries. Nothing added to the palette.

## Verification

- `_zverify.py`: **0 clashing pairs**. The clamp's bolts are buried in its plates, and the strut
  runs past the yoke's centre rather than abutting it.
- Every object positive-determinant, asserted in the export.
- Origins are each side's **shoulder pivot** (±0.885, 0, 0), not the world origin: that is the
  point these parts actually turn about.

## Unity

`WingPackBuilder` nests it as the child **`WornModel`**, switched off on the asset;
[`WornVisual`](Assets/Game/Scripts/Items/Equipped/WornVisual.cs) switches it on and the folded
bundle off when the pack is worn. `WornFit.size` is pinned to **5.51** — the authored span, which
the exporter prints — so a re-export that changed the scale shows up as a number disagreeing
rather than as wings drifting off the bar.

**Never grow the wings by raising that number.** It is a uniform scale about the rail: raise it and
the two shoulder pivots leave the bar tips they are measured onto, and the tips drop through the
ground. Tried on 2026-09-04 at 2×, and both failures were visible in the first session. A bigger
wing is a `TARGET_REACH` change here, followed by the pose sweep above.

**No rotation is applied to the nested model, unlike the folded bundle.** An FBX from `_exportlib`
arrives already converted: every mesh node carries `(x, y, z) → (−x, z, −y)` and its own −90° X,
with the vertices left in Blender's axes. A −90° X on the parent is therefore a *second*
conversion. On the bundle that is deliberate (it is hand-held, and the extra turn points its length
out of the fist); here it put the wings 0.6 m below the shoulders and half a metre behind them, and
it still looked plausibly like a pair of wings.
