"""Leash Gauntlet — the wrist-worn tether emitter.

The leash artifact used to be `leash_device`'s pistol-grip `Spool`, carried in
the hand. It is now worn: the same component file's `Gauntlet` variation — a
short C-shaped wrist shell with a wound drum, a fairlead and a snap hook hanging
off its front — strapped straight onto the forearm just above the wrist.

That is the whole model. The first build (2026-09-02, same day) put the shell on
the family's webbing cuff with a riveted steel spine, two clamp bands, a nose
and keeper lugs, like the grapple bracer. The user did not want the metal: the
leash should simply connect to the arm. So this is assembly of one component
plus one empty, and nothing is authored here but where the shell sits.

| Object | Where it comes from |
|---|---|
| `Mesh_Leash_Gauntlet` | `components/props/leash_device.blend`, unchanged |
| `muzzle`              | an empty at the rope exit — the prefab pays the rope out from it |

Frame, as every gauntlet: arm along Y, wrist at y = 0, elbow +Y, forward -Y,
dorsal +Z. Unity seats the model on the forearm bone (`GauntletFit`) with its
origin at the wrist, so the shell — which spans 0.056 m of arm — sits from
0.010 to 0.066 up the forearm: the first hand's-width above the wrist, the
place a wrist tether is worn. Its 0.0455 m inner radius at the family's 2.3x
is 0.105 m, the rig's forearm at the wrist.

The device is laid down by `R_x(-90)`: its shell axis (local +Z) goes along
the arm toward the elbow, and its front face (local -Y, where the drum, screen
and lamp are) turns up to +Z, the top of the forearm. The cable leaves the
fairlead along local -Z, which is now -Y — toward the hand. The C opens toward
local -X, the little-finger flank.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, _HERE)

import bpy  # noqa: E402

from _buildlib import *  # noqa: E402,F403
from _gauntlet import PROPS, append_objects, place  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

DEVICE = os.path.join(PROPS, "leash_device.blend")

# The shell spans local z 0.048..0.104 (its `cyl_patch` was built about its
# centre, not from its base). DEVICE_Y puts the shell's wrist-end rim at
# y = 0.010, a finger above the wrist joint, and its elbow-end rim at 0.066.
DEVICE_Y = -0.0380

# Rope exit: the device's fairlead, local (0, -0.062, 0.006), after the lay-down.
MUZZLE = (0.0, DEVICE_Y + 0.0060, 0.0620)


def device_matrix():
    """Device local (shell axis +Z, drum on -Y) onto the wrist, drum up."""
    return (Matrix.Translation(Vector((0.0, DEVICE_Y, 0.0)))
            @ Matrix.Rotation(math.radians(-90), 4, 'X'))


def muzzle(coll):
    """The rope exit. An empty, so it ships as a bare transform.

    Identity rotation on purpose: the export maps Blender -Y onto Unity +Z, so
    an unrotated empty already has its Unity +Z pointing out of the fairlead
    toward the hand. `LeashEnd` reads only its position.
    """
    e = bpy.data.objects.new("muzzle", None)
    e.empty_display_type = 'ARROWS'
    e.empty_display_size = 0.02
    e.location = Vector(MUZZLE)
    coll.objects.link(e)
    return e


def main():
    out = parse_out()
    start(out)
    coll = collection("Coll_LeashGauntlet")

    for obj in append_objects(DEVICE, ["Mesh_Leash_Gauntlet"], coll):
        place(obj, device_matrix())

    muzzle(coll)
    save(out)
    report()


if __name__ == "__main__":
    main()
