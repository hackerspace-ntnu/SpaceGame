# Sail rig — timber masts, ground anchors and wall fixings

Hardware for tensile shade sails: four masts, three ground anchors and three wall fixings, in ten
collections in `sail_rig.blend`. Built for `components/nomad_settlement/tents.blend`, but nothing in it is specific
to that camp — any tensioned cloth, awning or banner can be pitched on these.

**Everything here is wood and rope.** No metal, no fittings, no machined parts. A nomad camp pitches
its sails on cut poles lashed with hemp; a steel mast with a forged eye reads as somebody else's
infrastructure standing in the sand.

```bash
cd Assets/Game/Art/Models/_Source~
blender --background --python components/structural/sail_rig.py -- \
    --out components/structural/sail_rig.blend
```

## The parts

| Object | Size | What it is |
| --- | --- | --- |
| `Mesh_SailRig_MastPole` | 0.09 × 0.09 × 3.70 | One tapered pole, hemp whipping at the head. The default |
| `Mesh_SailRig_MastLashed` | 0.12 × 0.12 × 3.70 | Two poles spliced and bound with three turns — a long mast out of short timber |
| `Mesh_SailRig_MastTripod` | 1.14 × 1.00 × 3.83 | Shear legs: three poles crossed and bound. Stands on rock or loose sand |
| `Mesh_SailRig_MastStub` | 0.12 × 0.12 × 1.48 | A short stout post for a corner pulled near the ground |
| `Mesh_SailRig_AnchorStake` | 0.07 × 0.07 × 0.28 | A driven peg with one rope turn. The default tie-down |
| `Mesh_SailRig_AnchorCleat` | 0.31 × 0.09 × 0.15 | A deadman board pinned by two stakes, rope loop over it |
| `Mesh_SailRig_AnchorLog` | 0.17 × 0.34 × 0.16 | A short log laid on the sand with rope round it — ballast, not a pin |
| `Mesh_SailRig_WallHook` | 0.08 × 0.19 × 0.07 | A peg driven into a wall with a rope turn — the cheapest fixing |
| `Mesh_SailRig_WallCleat` | 0.18 × 0.15 × 0.10 | A timber block pegged flat to the wall, rope eye standing proud |
| `Mesh_SailRig_WallBracket` | 0.14 × 0.53 × 0.34 | A knee brace that stands the tie 0.46 m off the facade |

7988 tris for the whole set. Materials: `Mat_Wood_Timber_Silvered`, `Mat_Wood_Ply_Worn`,
`Mat_Fabric_Rope_Hemp` — all from the shared palette, none defined locally, none metal.

## Conventions these depend on

- **Masts are modelled along +Z, foot at z = 0, axis on x = y = 0.** That is what lets a caller scale
  one to a length and rake it about its foot without the shaft walking off the point it was planted
  on. Anchors sit on z = 0 the same way.
- **The tie point is the hemp whipping, not a ring**, and its height is known — 3.60 m on the three
  full masts, 1.40 m on the stub. A sail corner and its guy both land there, so
  `k = wanted_length / tie_height`.
- **Poles are thin, and a caller must not fatten them.** 84 mm at the butt on a 3.6 m mast, tapering
  to 56 mm. Scaling a mast uniformly to reach 4.9 m makes it 36% thicker, which turns a spar into a
  pile and the camp into scaffolding. Scale `(girth, girth, length)` with girth following length
  only weakly — `GIRTH_POW` in `components/nomad_settlement/tents.py` is the worked example.
- **Shafts are 8-sided on purpose.** A 16-segment barrel reads as a machined tube; at eight facets a
  pole reads as something cut and shaved by hand. Do not "improve" the segment count.
- **Every butt is round**, so a raked mast's foot tips without a plate corner digging in or hanging.
  The first version of this rig was steel with square base plates and needed round shoes bolted on
  to fix exactly that; bare timber gets it for free.
- **Wall fixings use the opposite convention to everything else here.** A fixing is placed by the
  wall it is nailed to, not by the ground: **the wall face is the plane y = 0, the fixing hangs off
  it toward −Y, and its origin is on that face.** Its tie point then sits at a known depth out from
  the wall — 0.125 m on the hook, 0.108 m on the cleat, 0.460 m (and 0.055 m up) on the bracket —
  so a caller placing one by its eye must set it back by exactly that, and a sail corner meant for a
  bracket has to be authored 0.46 m clear of the facade. `fix_corner()` in
  `components/nomad_settlement/tents.py` is the worked example: author the MOUNT on the wall and let
  it hand back the corner, never the other way round.
- **The eye offset turns with the fixing.** For a wall that runs along Y rather than X the fixing is
  rotated about Z, and its eye rotates with it — a fixing points along -Y at rz = 0, and rotating by
  rz carries that onto (sin rz, -cos rz), so a wall at +X facing -X wants rz = -90 degrees and one at
  -X wants +90. Anything reading an eye position must push the offset through the object's matrix,
  not add it to the object's location.

## Known gaps

- **No LOD, no collision, no UVs**, like the rest of the settlement blockout kit.
- **No armature.** Nothing on a mast or an anchor moves.
- Each part is one `Part`-built object, so a lashing cannot be selected separately from its pole.
  That is the same granularity as the awning kit's poles.
- `_zverify` on this file reports clashes between *different variations*, because all ten sit at
  the origin with their bases on z = 0. They are never placed together; see
  [ArtPipeline](../../../../../docs/AI/systems/ArtPipeline.md).
