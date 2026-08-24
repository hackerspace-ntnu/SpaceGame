"""Ship the grapple bracer to Unity.

Exports the whole model file, like `item_scanner_export.py` and unlike
`components/props/item_devices_export.py` — a model file holds exactly the
objects that make up the model, so `_exportlib.export` is the right tool and
its flags stay in one place.

No rig (`keep_armature=False`): nothing on this device articulates. The drum
could spin and nothing in the game spins it; the one part that moves is the
harpoon, and it moves by being destroyed here and instantiated as
`hookHeadPrefab` out in the world.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/grapple_bracer_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "grapple_bracer.blend")
DST = unity_path("Items", "grapple_bracer.fbx")


def main():
    export(SRC, DST, keep_armature=False)

    # The prefab wires the seated harpoon by serialized reference so it can be
    # hidden while the hook is in flight, and it puts the rope's muzzle on the
    # fairlead. Both need to know where things landed, and printing it here
    # beats measuring it in the editor afterwards.
    #
    # Blender (x, y, z) arrives in Unity as (x, z, -y), so the Unity-local
    # figures below are the ones to type into the prefab.
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    for obj in sorted(meshes, key=lambda o: o.name):
        b = obj.location
        print("  PIVOT %-28s blender (%.4f, %.4f, %.4f)  unity (%.4f, %.4f, %.4f)"
              % (obj.name, b.x, b.y, b.z, b.x, b.z, -b.y))

    pts = [obj.matrix_world @ v.co for obj in meshes for v in obj.data.vertices]
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    size = [hi[i] - lo[i] for i in range(3)]
    print("  BOUNDS blender size (%.4f, %.4f, %.4f) — longest %.4f"
          % (size[0], size[1], size[2], max(size)))
    print("  holdSize for a 2.1x wear = %.4f" % (max(size) * 2.1))


main()
