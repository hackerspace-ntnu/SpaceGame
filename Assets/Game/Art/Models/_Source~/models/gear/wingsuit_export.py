"""Ship the wingsuit to Unity.

Exports the whole model file — nine objects, no armature. The membranes deform
in the shader rather than on bones (SpaceGame/ClothWind, driven by airspeed), and
everything else about the suit is rigid, so there is nothing to keep.

The names and origins printed at the end are what `WingsuitBuilder` binds to.
`Mesh_Wingsuit_Membrane_L` / `_R` in particular are looked up by name and handed
to `WingsuitWings`, which reparents them onto the wearer's upper-arm bones — so a
rename here shows up as a null reference in the builder rather than as a wing
that quietly stays on the pack.

The membrane bounds are printed in the membrane's OWN object space because that
is the frame ClothWind pins in: the builder measures the anchor off these
vertices every run rather than carrying a constant, which is the mistake the
nomad's cape made once and paid for with a cloak that wrapped round its front.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/wingsuit_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import export, to_unity, unity_path  # noqa: E402

SRC = os.path.join(HERE, "wingsuit.blend")
DST = unity_path("Items", "wingsuit.fbx")


def main():
    export(SRC, DST, keep_armature=False)

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']

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


main()
