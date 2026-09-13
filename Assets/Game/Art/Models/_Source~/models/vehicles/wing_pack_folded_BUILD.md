# Wing Pack (folded ornithopter) — build record

The in-hand form of the Wing Pack item: the dune ornithopter in its stowed
configuration, baked to one static mesh at hand size. Replaces the eight
primitive boxes `WingPackBuilder.cs` used to assemble ("a strapped bundle of
spars") with the actual craft, folded.

## Derivation, not modelling

No geometry was authored. `wing_pack_folded.py` opens `dune_ornithopter.blend`
— which carries hand edits and is **never written** — poses the rig in memory,
and saves the baked result to this new file. The fold is pure pose work, using
the axis/sign conventions from `dune_ornithopter_BUILD.md`:

| Move | Value | What it does |
|---|---|---|
| Shoulder flap | −26° | droops the swept wings against the body sides |
| Arm sweep | ±83° | arms laid back near-parallel with the fuselage |
| Digit splay | −96° graded | the five spars of each wing collapse onto the arm line |
| Digit twist | 42° graded | web feathered flat against the spar stack |
| Tail fan splay | −55° | fan closed |
| Boom telescope | 0.55 / 0.45 | Boom_2 slides into Boom_1, the tail hub into Boom_2 |

Two rig facts the script has to work around:

- **`Bone_Boom_2` and `Bone_TailHub` are connected bones**, and a connected
  bone silently ignores pose translation — the telescope did nothing until the
  script unpins `use_connect` in memory first.
- **Pose translation happens in the bone's rest frame**, which is what makes
  `ARM_TUCK` (each wing slid 0.20 of the arm's length inboard along its own
  outboard axis) a one-liner. Without it the fold leaves a wide gap where the
  shoulder pylons hold the wings off the body.

## Bake

Skinned panels get their Armature modifier applied; bone-parented rigid parts
keep their posed world transform and lose the bone; everything joins into one
`Mesh_WingPack_Folded` (collection `Coll_WingPack_Folded`). The port side is a
mirrored placement, so normals are rebuilt once after the join. Scaled so the
nose-to-tail length is 0.95 m (matching the primitive bundle it replaces):
final bounds 0.43 × 0.95 × 0.36 m, origin at the bounds centre.

**No armature is kept, deliberately.** The item never articulates — unfolding
is `WingPackItem` spawning the real `DuneOrnithopter.prefab`. Re-posing means
re-running the script (delete this .blend first; it refuses to overwrite).

## Materials

All inherited from the assembly, which links them from `palette.blend`; the
script localises them on commit so the baked file stands alone, the same way
the exports do. Nothing added to the palette.

## Variations

None. This is the single stowed form of one specific machine, keyed to the
item that spawns it — a second "differently folded" variant would be noise,
not an asset.

## Unity

`wing_pack_folded_export.py` →
`Assets/Game/Art/Models/Vehicles/Ornithopter/wing_pack_folded.fbx` (static, no
armature, −Y forward onto Unity's +Z, ~0.4 MB). Nested into
`Assets/Game/Prefabs/Items/Equipment/WingPack.prefab` as `FoldedCraft` by
`Assets/Game/Editor/Vehicles/WingPackBuilder.cs`.
