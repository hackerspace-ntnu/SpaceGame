# jetpack.blend — build record

Hand-built asset. The pod itself — tank, housing, struts, fin, nozzle pair, rings, the two
geometry-nodes exhausts — was modelled by hand in Blender and is not reproducible from a script.
`jetpack_mirror.py` only *arranges* that work; it never rebuilds it.

## What the file holds

| Collection | Contents | Use |
| --- | --- | --- |
| `Coll_Jetpack_Worn` | Two pods, 0.8925 m apart, hung under `MOUNT_Jetpack_R` / `MOUNT_Jetpack_L` | Worn model — a pod either side of the expedition rig's back panel |
| `Coll_Jetpack_Item` | The same two pods, 0.03 m apart, under `ITEM_Jetpack_R` / `ITEM_Jetpack_L` | Inventory / carried item model — the two motors side by side |
| `Collection` | The scene light | Lighting only, not part of either model |

## Numbers, and where they come from

| Value | Source |
| --- | --- |
| Mount X = ±0.44625 m | Half the lash rail's tip span (`components/props/expedition_rig.blend`, `Mesh_Rig_LashRail` spans x −0.8925 … 0.8925). Half, because `JetpackBuilder.SizeScale` wears the pack at 2x: pods on the actual tips stand 3.99 m across the wearer and miss the rig entirely |
| Pod scale = 0.08117 | The authored pod is 8.008 m tall; 0.65 m is the worn size (the rig's back panel is 0.629 m tall, its wings 0.84 m long) |
| Mount point | The top face of the housing block, centred on the housing in X and Y — the rail line crosses the top of the housing, tank above it, nozzles below |
| Mount yaw = −90° about Z | The pod is authored with its struts along −Y. Yawed −90° they point along −X, i.e. inboard at the pack, so the bars run along the lash rail instead of sticking out fore-and-aft with nothing to grab |
| Item spacing = 0.7315 m | The inboard flank's distance to the mount **measured after the yaw**, plus half of a 0.03 m gap. Two traps in one number: the struts swing into that gap, and the pod is not symmetric about its mount (the nozzle yoke overhangs one side), so neither the unrotated width nor half the pod's width works |

Resulting extents: worn pair 1.105 × 0.397 × 0.657 m, item pair 0.944 × 0.397 × 0.657 m,
measured item gap 0.0300 m. `JetpackBuilder.WornSize` and `HoldSize` are those longest axes
times `SizeScale`, so changing a spacing here means re-pinning the number there.

## How the arrangement is built

- **The left pod is the right pod.** Its objects are linked duplicates sharing the same mesh
  datablocks, parented to an empty whose X scale is negative. Editing either side edits both, and
  a re-run of the script does not accumulate copies.
- **The mirrored mount's yaw carries the opposite sign.** `mirror(T @ R(y) @ S(s))` is
  `T' @ R(−y) @ S(−s, s, s)`; the same yaw on both empties swings one pod's struts outboard.
  The script measures the built result and aborts if the right strut lands outboard of its mount,
  because a yaw sign is the easiest thing in this file to get backwards and it looks plausible
  either way.
- **Every measurement is taken from `matrix_basis`, never `matrix_world`.** Once the parts hang
  under a mount their world pose carries this run's scale and yaw; measuring that would fold the
  placement into the next run. Re-running the script over the built file is a no-op.
- **The scale lives on the empties, not in the vertices.** Baking 0.08117 into the mesh would leave
  the `BEVEL` modifier widths and the geometry-nodes exhausts at their authored absolute sizes,
  12× too large for the scaled pod. Object-level scale carries the modifier output with it.
  The consequence: the mesh objects deliberately do **not** have applied transforms.
- **Placement is a parent inverse**, not an edited local matrix, so every hand-authored
  `location` / `rotation_euler` / `scale` on the pod's parts still reads exactly as the user left it.

## Modified from the authored state

Every change is authorised by the request ("mirror it… place them apart exactly the distance so it
is attached to the gear torso backpack parts", the follow-up asking for an inventory model of the
two motors side by side, and "the motors does not fit on the backpack rig thing… make the distance
between the motors half the size"):

1. The pod was scaled to 0.65 m, yawed −90° and moved onto the right-hand mount. It was authored
   at ~8 m with its struts along −Y.
2. Objects were renamed from `Cube` / `Cylinder.003` / `Torus.001` to `Mesh_Jetpack_<Role>_R`,
   with `_L`, `_ItemR` and `_ItemL` for the other three instances. No geometry, modifier or
   material was touched.
3. The worn spacing was halved, from the rail tips to ±0.44625 m (2026-09-07). Halved HERE and not
   in Unity: `WornSeat` scales the whole model to `WornFit.size`, so shrinking that number shrinks
   the pods along with the gap and changes nothing about how they sit on the rig.

## Known gaps

- Materials are still the authored `Material.002`–`Material.012`, not palette entries. Recolouring
  them would change the look the user chose, so it was left alone.
- The fin follows the struts round with the −90° yaw and now sweeps along X with them. Only the
  struts were the reason for the yaw; if the fin should stay fore-and-aft it needs its own
  counter-rotation on the part, not on the mount.
- The item spacing is still measured from the pod's inboard flank and untouched by the worn
  halving — the carried model was never the complaint.
