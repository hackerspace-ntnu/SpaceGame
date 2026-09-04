# Leash Gauntlet — build record

`models/gear/leash_gauntlet.blend` → `Assets/Game/Art/Models/Items/leash_gauntlet.fbx`
→ the Leash artifact, worn on the forearm.

Built 2026-09-02 for the body-equipment rework
(`docs/superpowers/specs/2026-09-02-body-equipment-design.md` §7), and rebuilt
the same evening: the first version put the leash's wrist shell on the family's
webbing cuff with a riveted steel spine, two clamp bands, a nose and keeper
lugs, like the grapple bracer. The user did not want the metal — the leash
should simply connect to the arm — so the cuff and the frame are gone and the
model is the shell alone.

| Part | Where it comes from |
|---|---|
| `Mesh_Leash_Gauntlet` | `components/props/leash_device.blend` — **reused unchanged**, the variation built ahead in Aug 2026 |
| `muzzle`              | an empty at the rope exit |

2,036 triangles. Export: `leash_gauntlet_export.py`.

`leash_device.blend` is untouched: its three collections (`Coll_Leash_Spool`,
`Coll_Leash_Gauntlet`, `Coll_Leash_Winch`) are as they were, and
`components/props/item_devices_export.py` still ships `Coll_Leash_Spool` as
`leash_emitter.fbx`.

## Where the shell sits

Frame as every gauntlet (`_gauntlet.py`): arm along Y, wrist at y = 0, elbow
+Y, forward −Y, dorsal +Z; the export maps Blender `(x, y, z)` onto Unity
`(−x, z, −y)`.

The shell is a 256-degree C, inner radius 0.0455 m, coaxial with the arm, laid
down by `R_x(−90)` at `DEVICE_Y = −0.038`: it spans y 0.010..0.066, a finger
above the wrist joint, with the drum, screen and lamp on top (+Z) and the cable
leaving the fairlead toward the hand (−Y). The snap hook hangs to y −0.071,
over the back of the hand. The C opens toward −X, the little-finger flank.

Unity strips it on with `GauntletFit` (origin at the wrist, 2.3x across, 1.9x
along): the 0.0455 m inner radius becomes 0.105 m, the rig's forearm at the
wrist, so the shell hugs the sleeve.

Measured after the build: FBX bounds, Blender axes, min (−0.043, −0.071,
−0.055) max (0.056, 0.066, 0.090), size (0.099, 0.137, 0.145). Unity axes:
min (−0.056, −0.055, −0.066) max (0.043, 0.090, 0.071).

## The muzzle

`muzzle` is an empty at Blender `(0, −0.032, 0.062)` — the fairlead — which
lands in Unity at `(0, 0.062, 0.032)`. Identity rotation on purpose: through
the export an unrotated empty already has its Unity +Z pointing along Blender
−Y, out of the fairlead toward the hand. `LeashEnd` reads only its position.

`_exportlib.export(..., keep_empties=True)` is what ships it; the default
export writes meshes only.

## Materials

**No palette additions.** The shell brings its own from `leash_device.blend`.

## Unity wiring

`Leash.prefab` instances `leash_gauntlet.fbx` at identity under its root,
points `LeashArtifact.muzzle` at the FBX's `muzzle` node, carries a
`GauntletFit`, and lies on its flank on the pack mat via
`ItemPackOrientation` (the model child turned −90 about Z, `rotationOffset`
identity).
