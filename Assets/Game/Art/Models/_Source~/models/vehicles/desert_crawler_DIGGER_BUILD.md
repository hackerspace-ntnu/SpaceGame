# Desert Crawler — front digging head

Build record for the front-end attachment, kept separate from
`desert_crawler_BUILD.md` because that document records the original build.

The arm that used to live on this machine — a scorpion tail, then a claw, then a
five-joint ostrich neck, plus a pair of revolver cargo magazines — is gone. All
of it was removed on request. What history remains of it is in `backups/` and in
the component files it left behind, which are still in the library and still
usable: `tail_segment`, `claw_chela`, `drum_magazine`, `neck_column`.

---

## What is on the machine now

A transverse cutter drum on two arms under the prow, and nothing else.

| | |
|---|---|
| component | `components/mechanical/cutter_drum.blend` |
| parts | drum, arm, hood, ram (barrel + rod) |
| added | 9 objects, 6,376 triangles, 2 bones |
| removed | 24 arm meshes, 19 bones, 3 empty collections |
| rig | `CRAWLER_Rig` back to 31 original bones + `Dig_Boom`, `Dig_Drum` |
| model total | 93 objects, 251,374 triangles, all materials from the palette |

Deliberately plain. Big simple forms, four rows of teeth, three weld bands, one
hazard band on each arm. The teeth are what say "this digs"; rivet rows and
greeble would only muddy a part read at twenty metres. The whole attachment is
6,376 triangles against the machine's 251,374.

## Where it could go

Almost nowhere, as it turned out. The front legs sweep inboard to x = 2.09 and
occupy y −8.68…−3.86 down to z = −3.14, so the only clear route from the hull to
the ground is the centreline corridor |x| < 2. The arms run down it at x = ±1.55
and the drum sits at y = −11.00, forward of everything the legs reach. It
overhangs its own bearings either side, which is what a through-axle drum does.

The pivot is at y = −7.90, just forward of `Mesh_Crawler_Gantry.003` (which
starts at −7.63) and just below the prow (which bottoms out at z 5.64), so the
mount hangs under the nose without penetrating either.

Teeth reach z = −4.10 against a ground plane of −4.31: 0.21 m clearance at rest,
so it reads as poised to cut and the boom pitches down into the sand.

## Two shape decisions that came from looking at it

**The hood's end plates follow its arc.** Square cheeks — the obvious way to cap
a curved shell — read as a plain slab from the side and hid the drum completely,
which is the one thing this part must not do.

**The arm is flat-shaded.** `Part.loft` smooth-shades every face that curves
around its axis, which turns a four-sided box beam into something that reads as
a tube. `p.shade(faces, smooth=False)` on the beam fixes it.

## Rig

    Root → Dig_Boom → Dig_Drum

`Dig_Drum` points along +X so rotating it about its own local Y spins the drum
about world X. Arms, hood, brace and ram rods hang off `Dig_Boom`; the ram
barrels stay on `Root`, so the pair extends as the boom pitches instead of
stretching. Meshes are bone-parented with `matrix_parent_inverse` left at
identity, matching `desert_crawler.py` — FBX has no such concept and drops it.

## Verification

`desert_crawler_digger.py` snapshots every pre-existing object's world matrix on
open and re-checks it before saving, aborting if anything not on the removal
list moved or vanished. On the run that landed: **84 pre-existing objects
untouched, 24 removed as asked.**

Rehearse against a copy with `-- --target <copy.blend>`.

**Close the file in Blender first.** A running Blender session holds its own copy
in memory, so its next save writes that image over anything written from
outside — which happened once during this work, three minutes after a successful
write. A file being unchanged on disk does not mean it is safe to write around;
the session is what matters, not the file. That version was recoverable only
because Blender had rotated it into `desert_crawler.blend1`.

`models/` is untracked in git, so `backups/` is the only history that exists.

---

# Collector bucket and lift lever

Applied by `desert_crawler_collector.py`, 2026-08-10.

The hand-built bucket (`Cube.001` body + `Cube.002` rim) was unparented — it
neither moved with the machine nor articulated. It is now on the rig with the
minimum mechanism that makes it work as a front loader.

    Root -> Collector_Lift -> Collector_Bucket

`Collector_Lift` raises and lowers the assembly on two arms pinned to the deck;
`Collector_Bucket` is the tip axis, so rolling it forward swings the bucket down
to dump. Two rams per side, reusing `Coll_CutterDrum_Ram`: lift ram from a deck
anchor to the arm, tilt ram from the arm to the bucket's upper rear. Each has
its barrel and rod on **different** bones, so the pair extends rather than
stretches.

| | |
|---|---|
| new component | `components/mechanical/collector_lever.blend` (arm, mount) |
| added | 12 objects, 4,416 triangles, 2 bones |
| arms at | x = +/-4.60 — outboard of the bucket (4.37), clear of the deck `Cube` (2.37) |

Measured on the built rig: lowering `Collector_Lift` 28 degrees carries the
bucket 2.9 m and extends the lift ram 0.97 m while its barrel stays put on the
hull; tipping `Collector_Bucket` 40 degrees moves the bucket and its tilt rod
only. Nothing else on the machine moves for either.

## The bucket's scale was applied

Both halves carried unapplied non-uniform object scale (about 4.4, 2.8, 4.4).
Bone-parenting geometry like that computes a basis matrix that can shear the
mesh, so the scale was baked into the vertices. The bucket looks identical, the
transform is now rigid, and the parenting uses an identity parent-inverse like
the rest of the rig — which matters because FBX drops that field.

Object names were left alone. `Cube.001` and `Cube.002` are poor names but they
are the author's, and renaming is not this script's business.

## Verifying "nothing moved"

The check compares **world-space bounding-box corners**, not matrices. Applying
the bucket's scale legitimately rewrites its `matrix_world` while leaving the
geometry exactly where it was, and a matrix comparison reported that as a 3.4 m
move that had not happened. Corners measure what actually matters: worst drift
across 101 pre-existing objects was 1.35e-06 m.
