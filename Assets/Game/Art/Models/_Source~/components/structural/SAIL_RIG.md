# Sail rig — masts and ground anchors

Hardware for tensile shade sails: four masts and three ground anchors, in seven collections in
`sail_rig.blend`. Built for `components/nomad_settlement/tents.blend`, but nothing in it is specific
to that camp — any tensioned cloth, awning or banner can be pitched on these.

```bash
cd Assets/Game/Art/Models/_Source~
blender --background --python components/structural/sail_rig.py -- \
    --out components/structural/sail_rig.blend
```

## The parts

| Object | Size | What it is |
| --- | --- | --- |
| `Mesh_SailRig_MastStraight` | 0.30 × 0.30 × 3.66 | Plain tapered shaft, round foot shoe, ankle collar, head eye. The default |
| `Mesh_SailRig_MastStepped` | 0.34 × 0.34 × 3.66 | Two-stage shaft with a mid collar. Heavier — for the loaded corners |
| `Mesh_SailRig_MastTripod` | 1.19 × 1.05 × 3.66 | Shaft on three splayed legs with pads. Stands on ground that will not take a shoe |
| `Mesh_SailRig_MastStub` | 0.32 × 0.32 × 1.46 | Short heavy post for a corner pulled down near the ground |
| `Mesh_SailRig_AnchorPin` | 0.14 × 0.14 × 0.34 | A driven pin with a washer and an eye. The default tie-down |
| `Mesh_SailRig_AnchorPlate` | 0.21 × 0.21 × 0.16 | Ground plate, two lugs and a shackle, where a pin will not bite |
| `Mesh_SailRig_AnchorBlock` | 0.32 × 0.26 × 0.26 | A small ballast block with a ring, for sand |

5784 tris for the whole set. Materials: `Mat_Metal_Steel_Worn`, `Mat_Metal_Steel_Dark`,
`Mat_Metal_Rust_Heavy` — all from the shared palette, none defined locally.

## Conventions these depend on

- **Masts are modelled along +Z, foot at z = 0, axis on x = y = 0.** That is what lets a caller scale
  one to a length and rake it about its foot without the shaft walking off the point it was planted
  on. Anchors sit on z = 0 the same way.
- **The head eye is at a known height** — 3.60 m on the three full masts, 1.40 m on the stub — and
  that, not the top of the ring above it, is what a caller scales by. A sail corner shackles onto the
  eye, so `k = wanted_length / eye_height`.
- **Every mast foot is a round shoe, never a square plate.** A raked mast tips its own foot with it;
  a tilted disc still reads as a shoe bedded into sand, a tilted square reads as a dropped object
  lying on it. This is the whole reason the shoes are round and it is easy to undo by accident.

## Known gaps

- **No LOD, no collision, no UVs**, like the rest of the settlement blockout kit.
- **No armature.** Nothing on a mast or an anchor moves.
- Each part is one `Part`-built object, so a shoe or a collar cannot be selected separately from its
  shaft. That is the same granularity as the awning kit's poles.
