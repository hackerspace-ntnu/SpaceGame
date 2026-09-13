# Ornithopter (worn) — build record

What the Wing Pack looks like **on a player's back**, as opposed to `wing_pack_folded.blend`,
which is what it looks like in their hand.

The worn form throws the aircraft away and keeps the two things that say *flight*: the webbed
wings, and the spoked shoulder mechanics that beat them. They mount on the two ends of the
expedition rig's lash rail — the one part of the pack that does **not** fold in, and which sticks
out well past each flank at almost exactly shoulder height.

Source: `ornithopter_worn.py` (a derivation, not a generator) → two `.blend` files →
`ornithopter_worn_export.py` → two `.fbx` under `Assets/Game/Art/Models/Vehicles/Ornithopter/`.
Consumed by `WingPackBuilder` (Tools ▸ Vehicles ▸ Build Wing Pack Item), which nests them as the
`WornModel` and `InspectModel` children `WornVisual` switches between.

## Two files, two poses, one machine (2026-09-05)

This script writes **both** worn shapes. The player sees a different one in each place:

| `--commit` | file | FBX | pose | worn |
| --- | --- | --- | --- | --- |
| default | `ornithopter_worn.blend` | `ornithopter_worn.fbx` | STOWED | day to day, in the world |
| `--spread` | `ornithopter_worn_on_person.blend` | `ornithopter_worn_on_person.fbx` | OPEN | on the gear screen (I) |

|  | OPEN | STOWED |
| --- | --- | --- |
| span | 5.512 m | **1.973 m** |
| height | 1.781 m | **1.152 m** |
| depth | 1.371 m | **0.369 m** |
| reach from each mount | 2.775 m | **1.093 m** |
| objects / triangles | 12 / 8,736 | 12 / 8,736 |
| scale of the geometry | 0.538647 | 0.538647 |
| `WornFit` field | `inspectSize` = 5.51 | `size` = 1.97 |

**Nothing is culled and nothing is scaled between them.** The last two rows are the point: the
same twelve parts at the same scale, folded or open. The wearer carries the whole machine either
way; stowed it takes up a third of the room.

**Stowed** was asked for on 2026-09-05 — *"fold it behind real good so it is the same mass, but
looks much smaller"* — because 5.5 m across a walking character's back is a wingspan, not
something anybody wears through a desert. At 1.973 m the widest thing on the pack is **the lash
rail itself**; the wings have stopped contributing to the silhouette's width altogether.

**Open** is not history. The gear screen is the one place a player looks *at* their own back on
purpose, with the camera flown round for it, so that is where the wings get to be wings. Both
models mount on the same two rail tips (origins at x = ±0.885, clamps at ±0.83, printed by the
exporter for both), which is what lets Unity swap one for the other with nothing moving.

Two sizes rather than one, because they are two objects with two spans — not one object drawn
larger. Sizing both off `size` would squeeze 5.51 m of spread wing into 1.97 m, which is the same
failure as scaling the worn wings by hand: it drags the shoulder pivots off the bar tips.

**The file names read backwards.** `ornithopter_worn` is the one worn in ordinary play;
`ornithopter_worn_on_person` is the gear screen's. The second name predates the split. Renaming it
to `..._spread` would be an improvement and is left undone only because it is the name the file
already ships under.

## Rebuilding it safely

`--commit` refuses to overwrite the shipped `.blend`, because a file that exists may carry hand
edits that live nowhere else. That guard is answered with a **control run**, not by deleting the
file and hoping: `--out /tmp/control.blend` writes the same parameters to a scratch path, and a
per-object vertex/polygon fingerprint of the two files proves the script still reproduces what
shipped. Run it with the *old* parameters — the point is to reproduce what shipped, not to
compare a new pose against an old file. Done before the 2026-09-04 re-pose and again before the
2026-09-05 fold; both passed, and that is what made deleting the shipped files safe.

`--spread` was checked the same way, against the wing as it stood in git before the fold: every
part matches to **5e-5 m**, the whole difference being that `SPAR_SCALE` is now one frozen number
where it used to be measured per side (0.53864775 left, 0.53866220 right). The two sides are
exactly mirrored now, where they used to differ in the fifth decimal.

## Derivation, not modelling — with one exception

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

The exception is the **furl** (below), which does move web vertices, and only in the stowed build.
It is still a deformation of the source mesh rather than authored geometry — no vertex is added or
removed, and the triangle count is unchanged — but it is the one place this file does something the
rig cannot.

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

`ROOT_HALF = 0.885` is the first of those, and it never moves — it is the mount. The rail sits
0.63 m above the spine bone and the soles about 1.45 m below it, so **the ground is 2.08 m under
the rail**; the stowed bundle hangs 0.69 m of that, leaving 1.4 m of clearance where the spread
wing had 0.47.

## The scale is FROZEN, and that is the one trap in this rebuild

`SPAR_SCALE = EXTENDED_REACH / EXTENDED_RIG_REACH = 2.775 / 5.1518 = 0.538647`.

It used to be derived from the wing **as posed** — `EXTENDED_REACH / reach()` measured after the
pose was applied. That is exactly wrong for a folded model, and it fails silently and completely:
**folding a wing shortens its reach**, so re-deriving would have scaled the folded stack straight
back up to the size it was folded out of. The renders would have shown the same silhouette, the
printed reach would have agreed with itself, and the only symptom would be a fold that did
nothing.

Frozen, the fold is a pose and nothing but a pose. `EXTENDED_REACH` keeps its original meaning
and its original justification — how far the wing reaches **when open**, bounded by the 2.08 m of
ground under the rail — and stays the number to move if the machine itself should be bigger.

## The fold — what STOWED does that OPEN does not

`OPEN` is the plain spread pose: `flap −52, sweep 16, roll 38, splay −105, twist 14`, with
`plane`, `yaw` and `wrist` at zero and **the furl off**. Everything below is the stowed build.

Three hinges, closed in the order the wing physically would:

| Step | Parameter | Value | What it does |
|---|---|---|---|
| 1. the fan shuts | `SPLAY` | −105° | five digit spars swing onto one line |
| 2. the wrist folds | `WRIST` | −175° | the shut fan lies back along the arm |
| 3. the elbow folds | `SWEEP` | +175° | the arm lies back along the shoulder bar |
| then: hang it | `FLAP` | −100° | the bundle drops off the rail tip, leaning inboard |
| and lie it flat | `PLANE` | −105° | the fold plane lies back against the pack |
| feather the spars | `TWIST` | 12° | five spars stay readable instead of one fused blade |
| | `ROLL`, `YAW` | 0° | nothing left to turn toward a camera |

**`SPLAY` is not "the fan's opening angle"**, which is what this file used to claim. The five
spars sit **104.7°** apart at rest and the grading spreads exactly `SPLAY` across them, so −105 is
the value that lays them **parallel**. That is why the same −105 read as one continuous open sail
in the spread pose and as a shut fan in this one: it is the same stack of parallel spars either
way, with the cloth taut across it or gathered onto it.

`SWEEP` and `WRIST` are five degrees short of dead flat so the links do not stack into one
another. That is as tight as the chain goes: reach 2.775 → 1.093 m.

`FLAP −100` hangs the bundle from the rail tip ten degrees past vertical, so it leans **inboard
behind the pack** rather than out past the wearer's flank. Swept −80 through −135; −100 is where
the span bottoms out at 1.99 m, which is the rail, so past that the wings are no longer what sets
the width and there is nothing left to win.

`PLANE −105` lies the folded wing back against the pack instead of standing it square across it.
Worth 0.40 → 0.22 m of protrusion behind the rail for nothing: span and height do not move. The
shoulder uses `'YXZ'` euler order so this roll lands on a wing still lying in its own plane —
under the default `'XYZ'` it is applied to an already-flapped chain, where it stops rolling the
fold plane and starts swinging the bundle fore and aft.

**The overshoot is unavoidable and is not a defect.** The arm is 0.95 m and folds back over a
0.52 m shoulder bar, so it ends **0.47 m above the rail** — two spar tips standing above the
wearer's shoulders. Every way of burying it costs more than it saves: folding the arm less puts
the wrist 0.8 m out to the side, which is the width this whole change exists to remove.

## The furl — the one thing the rig cannot do, and only when stowed

Closing the fan stacks the five spars, and that part is honest articulation. The web is not: it is
a single skinned sheet, so linear blend skinning carries it across the closed fan as one smooth
1.4 m sail. Posed alone the wing reads as **a folded frame with a bedsheet draped over it** — the
metal put away and the cloth not — which is precisely the read a stowed wing must not have. It was
the first thing every fold sweep showed and no pose fixes it.

So `furl()` gathers the cloth by hand, radially onto the frame it hangs from: for every web
vertex, find the nearest point on any posed spar (shoulder, arm, five digits) and pull the vertex
in toward it.

- `FURL_RADIUS = 0.055` — a canvas thickness against a spar stack about 0.04 m across.
- `FURL_SLACK = 0.06` — what stops it being a shrink-wrap. The cloth that had furthest to travel
  still ends up furthest out, so the bundle keeps the tapered, bunched profile of canvas gathered
  against a spar instead of the perfect sleeve a constant radius gives.

Vertices keep their **bearing** about the spar, and that is what stops the sheet's two faces from
collapsing through each other: whatever was outboard of the frame stays outboard of it, just
closer. 437 vertices move.

The spars are read off the armature **before the bake**, because there is nothing left to ask once
the pose is frozen and the rig is dropped.

**`FURL_RADIUS` zero switches it off, and the OPEN pose sets it to zero.** Furling belongs to a wing
that has been put away; run it on a spread one and it drags the taut sail off its own spars and onto
them — the sail destroyed rather than gathered. That is the one parameter that is not merely tuned
differently between the two builds but has to be *absent* from one of them.

## The clamps — the only new geometry

`Mesh_OrniWorn_Clamp_L/R`: a flat strap jaw on the rail with a strut up to the bearing. Without
it the mechanics hang beside the bar touching nothing, which reads as a wing hovering next to the
player rather than one bolted to their pack.

The bore is not a guess. The rail's own mesh was binned along its axis: every band reads
**0.134 m fore-aft by 0.040 m thick**, with the outermost 0.15 m of each end thickened by its loop
buckle. So the jaw is a flat strap clamp, not a round collar, and it sits at x = ±0.83 — just
inboard of the tip, where the strap is clean.

The strut is aimed at the **measured** bearing centre rather than at a typed offset, because the
pose swings the whole shoulder assembly off the bar by an amount that changes with every tweak. A
hard-coded strut would leave a visible gap the moment `FLAP` moved, and nothing would report it.
The fold moved the bearing from 1.6 m outboard to 0.5 m below the rail and the strut followed
without being touched — which is the whole reason it is measured.

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

- `_zverify.py`: **1 clashing pair, 0.002 m², 0.35 mm apart, both faces inside one mesh**
  (`Mesh_OrniWorn_Wing_L_Frame`). The build that shipped before this one reported exactly the same
  count and area in `Mesh_OrniWorn_Wing_R_Web`, so it is the source spar geometry rather than the
  fold. No pair is coincident (<0.05 mm) and no pair crosses two objects.
- Every object positive-determinant, asserted in the export.
- L and R are exact mirrors — every part's centre matches its opposite in x with identical y, z.
  Worth printing every time the pose changes: `Bone_Arm`'s local Y takes a per-side sign and the
  digits' does not, and asymmetric bounds are the only way to catch getting it wrong.
- Measured back in Unity after import: 12 renderers, 8,736 triangles, 0 negative-scale nodes,
  bounds (1.973, 1.152, 0.369) — identical to Blender's.
- Origins are each side's **shoulder pivot** (±0.885, 0, 0), not the world origin: that is the
  point these parts actually turn about.

## Unity

`WingPackBuilder` nests the two as the children **`WornModel`** (stowed) and **`InspectModel`**
(open), both switched off on the asset;
[`WornVisual`](Assets/Game/Scripts/Items/Equipped/WornVisual.cs) switches one on and everything
else off. `WornSeat.Apply` takes the form and picks the matching `WornFit` size, so the swap is a
re-seat: `BodyFocusSession` asks for `Inspected` when the gear screen opens and `Worn` when it
closes. An item with no `InspectModel` — which is every other item — falls back to its worn model
and is unaffected.

`WornFit.size` is pinned to **1.97** and `WornFit.inspectSize` to **5.51**, both authored spans the
exporter prints, so a re-export that changed a scale shows up as a number disagreeing rather than
as wings drifting off the bar.

**Never fold or grow the wings by moving either number.** They are uniform scales about the rail:
change one and that model's two shoulder pivots leave the bar tips they are measured onto. Growing
them that way was tried on 2026-09-04 and hung the tips through the ground; shrinking them that way
would have made a smaller *machine* rather than a folded one. Both belong in the pose here, and the
numbers are then copied from what the exporter prints.

**Both models must keep the same origins**, or the swap stops being free — the exporter prints them
for each file and they read `±0.8300, ±0.8850` in both.

**No rotation is applied to the nested model, unlike the folded bundle.** An FBX from `_exportlib`
arrives already converted: every mesh node carries `(x, y, z) → (−x, z, −y)` and its own −90° X,
with the vertices left in Blender's axes. A −90° X on the parent is therefore a *second*
conversion. On the bundle that is deliberate (it is hand-held, and the extra turn points its length
out of the fist); here it put the wings 0.6 m below the shoulders and half a metre behind them, and
it still looked plausibly like a pair of wings.

## Verifying the two together

`ornithopter_worn_export.py` ships the pair in one run and **refuses to ship a pair that disagree
on part count**. That is the check worth having: the two are one machine in two poses, so if one is
rebuilt from a changed cull and the other is not, the symptom in game is a part that appears and
disappears when the gear screen opens — which reads as a rendering bug, not as a stale export.
