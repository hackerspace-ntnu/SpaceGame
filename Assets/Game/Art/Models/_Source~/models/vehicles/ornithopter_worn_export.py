"""Ship the worn wing pack to Unity.

Twelve static objects, no armature: the worn form never articulates. It is a
display of equipment, and the thing that moves under it is the wearer's spine.

The origin is load-bearing and is why this prints its bounds. `WornSeat` puts a
back item's origin on the expedition rig's lash rail, and this model is authored
with its origin exactly there and its two shoulder pivots reaching out to the
rail's two protruding bar tips at x = ±0.885 m. So the FBX must arrive at TRUE
WEARER SCALE and `OrnithopterWornBuilder` must not rescale it — the size printed
below is what the prefab's `WornFit.size` is pinned to, so a re-export that
changes the span is caught by that number disagreeing rather than by the wings
quietly drifting off the bar.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/vehicles/ornithopter_worn_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import export, to_unity, unity_path  # noqa: E402

SRC = os.path.join(HERE, "ornithopter_worn.blend")
DST = unity_path("Vehicles", "Ornithopter", "ornithopter_worn.fbx")


def main():
    export(SRC, DST, keep_armature=False)

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']

    lo = [1e9, 1e9, 1e9]
    hi = [-1e9, -1e9, -1e9]
    for obj in meshes:
        m = obj.matrix_world
        for v in obj.data.vertices:
            w = m @ v.co
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])
    size = [hi[i] - lo[i] for i in range(3)]
    print("  WORN SPAN  %.4f m  (depth %.4f, height %.4f)" % (size[0], size[1], size[2]))
    print("  WORN BOUNDS blender lo (%.4f, %.4f, %.4f) hi (%.4f, %.4f, %.4f)"
          % (*lo, *hi))
    print("  -> pin WornFit.size to %.2f on WingPack.prefab" % size[0])

    for obj in sorted(meshes, key=lambda o: o.name):
        loc = obj.location
        print("  ORIGIN %-34s blender (%.4f, %.4f, %.4f)  unity (%.4f, %.4f, %.4f)"
              % (obj.name, loc.x, loc.y, loc.z, *to_unity(loc)))

    # Handedness: a negative-determinant object exports as a negative scale and
    # Unity renders it inside-out with a clean console. The .blend was built with
    # the winding already repaired (`ornithopter_worn.py: flatten`), so this is a
    # guard rather than a fix — if it ever prints, the fix belongs upstream.
    bad = [o.name for o in meshes if o.matrix_world.to_3x3().determinant() < 0.0]
    if bad:
        raise SystemExit("Inside-out after export: %s" % ", ".join(sorted(bad)))
    print("  handedness: %d object(s), all positive-determinant" % len(meshes))


main()
