"""Ship the jumping rod to Unity.

Exports the whole model file: a model file holds exactly the objects that make
up the model, so `_exportlib.export` is the right tool and its twelve flags stay
in one place. There is no rig to keep (`keep_armature=False`) — the piston and
the coil are separate objects with their origins on the axis they move along,
which is the same capability without a bone hierarchy. See the model docstring.

The origins printed at the end are what the Unity builder binds to. Both
prefabs — the planted rod and the carried item — parent `Foot` and
`SpringSeat` under `Piston` and drive `Piston` and `Spring` from
`JumpingRodSpring`, so a rename or a moved origin in the .blend shows up here
rather than as a null reference in the builder.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/jumping_rod_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "jumping_rod.blend")
DST = unity_path("Items", "jumping_rod.fbx")


def main():
    export(SRC, DST, keep_armature=False)

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    lo = min(min((o.matrix_world @ v.co).z for v in o.data.vertices) for o in meshes)
    hi = max(max((o.matrix_world @ v.co).z for v in o.data.vertices) for o in meshes)
    print("  standing height %.4f m (blender z %.4f .. %.4f)" % (hi - lo, lo, hi))

    for obj in sorted(meshes, key=lambda o: o.name):
        loc = obj.location
        print("  ORIGIN %-32s (%.4f, %.4f, %.4f)" % (obj.name, loc.x, loc.y, loc.z))


main()
