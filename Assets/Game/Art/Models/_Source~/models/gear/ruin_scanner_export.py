"""Ship the ruin scanner to Unity.

Exports the whole model file, like `grapple_bracer_export.py`: a model file
holds exactly the objects that make up the model, so `_exportlib.export` is
the right tool and its flags stay in one place.

No rig (`keep_armature=False`): nothing on this device articulates. The one
thing that "moves" is the cone of light, and that is spawned by
`RuinScannerPulse` out in the world, rooted on the `Emitter` empty.

`keep_empties=True` is the one flag this export needs that the bracer's does
not. The prefab's `muzzle` field points at `Emitter`; `_exportlib` ships
meshes only unless told otherwise, and an FBX without the empty leaves the
cone starting from the prefab root, in the middle of the forearm.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/ruin_scanner_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "ruin_scanner.blend")
DST = unity_path("Items", "ruin_scanner.fbx")


def main():
    export(SRC, DST, keep_armature=False, keep_empties=True)

    # Blender (x, y, z) arrives in Unity as (-x, z, -y) — the X flip is the
    # handedness change, measured in grapple_bracer_BUILD.md. The Unity-local
    # figures below are the ones to type into the prefab.
    objs = [o for o in bpy.data.objects if o.type in ('MESH', 'EMPTY')]
    for obj in sorted(objs, key=lambda o: o.name):
        b = obj.location
        print("  PIVOT %-28s blender (%.4f, %.4f, %.4f)  unity (%.4f, %.4f, %.4f)"
              % (obj.name, b.x, b.y, b.z, -b.x, b.z, -b.y))

    meshes = [o for o in objs if o.type == 'MESH']
    pts = [obj.matrix_world @ v.co for obj in meshes for v in obj.data.vertices]
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    size = [hi[i] - lo[i] for i in range(3)]
    print("  BOUNDS blender size (%.4f, %.4f, %.4f) — longest %.4f"
          % (size[0], size[1], size[2], max(size)))
    print("  holdSize for a 2.1x wear = %.4f" % (max(size) * 2.1))


main()
