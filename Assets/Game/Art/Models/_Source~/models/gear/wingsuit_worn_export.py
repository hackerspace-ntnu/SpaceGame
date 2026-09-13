"""Ship the worn wingsuit to Unity.

Nine static objects, no armature. The membranes deform in the shader
(`SpaceGame/ClothWind`) rather than on bones, and the yoke, straps, spars and
cuffs are rigid, so there is nothing to keep.

Two things printed below are what the Unity side binds to, and both are why this
prints rather than just exporting:

**The bounds.** The model is authored at TRUE WEARER SCALE with its origin on
the spine bone, so `WingsuitWornBuilder` must not resize it — `WornFit.size` is
pinned to the span below, which turns a re-export that changes the scale into a
number that disagrees instead of a wing that quietly drifts off the shoulders.

**The membrane object space.** `ClothWind` pins a garment by a gradient along one
object-space axis. `WingsuitBuilder` measures that axis off the mesh's own
vertices on every run rather than carrying a constant, and the worn builder does
the same; these numbers are the check that the measurement found the chord.
Note the panel's basis is BAKED into its mesh here (the whole wing is rolled aft
about the arm axis), so the chord is no longer a clean −Y the way the flight
suit's is — which is exactly why nothing downstream may assume an axis.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/wingsuit_worn_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import export, to_unity, unity_path  # noqa: E402

SRC = os.path.join(HERE, "wingsuit_worn.blend")
DST = unity_path("Items", "wingsuit_worn.fbx")


def main():
    export(SRC, DST, keep_armature=False)

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']

    lo = [1e9] * 3
    hi = [-1e9] * 3
    for obj in meshes:
        m = obj.matrix_world
        for v in obj.data.vertices:
            w = m @ v.co
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])
    size = [hi[i] - lo[i] for i in range(3)]
    print("  WORN SPAN  %.4f m  (depth %.4f, height %.4f)" % tuple(size))
    print("  WORN BOUNDS blender lo (%.4f, %.4f, %.4f) hi (%.4f, %.4f, %.4f)"
          % (*lo, *hi))
    print("  -> pin WornFit.size to %.2f on Wingsuit.prefab" % size[0])

    for obj in sorted(meshes, key=lambda o: o.name):
        loc = obj.location
        print("  ORIGIN %-34s blender (%.4f, %.4f, %.4f)  unity (%.4f, %.4f, %.4f)"
              % (obj.name, loc.x, loc.y, loc.z, *to_unity(loc)))

    for obj in sorted(meshes, key=lambda o: o.name):
        if "Membrane" not in obj.name:
            continue
        xs = [v.co.x for v in obj.data.vertices]
        ys = [v.co.y for v in obj.data.vertices]
        zs = [v.co.z for v in obj.data.vertices]
        print("  MEMBRANE %-30s object-space x %.4f..%.4f  y %.4f..%.4f  z %.4f..%.4f"
              % (obj.name, min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))

    bad = [o.name for o in meshes if o.matrix_world.to_3x3().determinant() < 0.0]
    if bad:
        raise SystemExit("Inside-out after export: %s" % ", ".join(sorted(bad)))
    print("  handedness: %d object(s), all positive-determinant" % len(meshes))


main()
