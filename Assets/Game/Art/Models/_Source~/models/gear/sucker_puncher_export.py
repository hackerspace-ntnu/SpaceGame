"""Ship the Sucker Puncher to Unity.

Exports the whole model file — a model `.blend` holds exactly the objects that
make up the model, so `_exportlib.export` is the right tool and its flags stay
in one place.

The rig is dropped (`keep_armature=False`) because there is none: everything
that moves does so as one rigid group along one axis, and the three objects in
that group already share an origin on it. See the model's docstring.

The pivot dump at the end is the point of running this rather than exporting by
hand. `SuckerPuncherBuilder` parents the ram objects and reads the markers by
serialized reference, and it needs to know where each origin landed — printing
it here beats measuring it in the editor afterwards.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/sucker_puncher_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "sucker_puncher.blend")
DST = unity_path("Items", "sucker_puncher.fbx")

# The three objects the prefab parents under one moving transform. Kept here as
# well as in the model script because this is the list the Unity side consumes.
RAM_OBJECTS = ("Mesh_RamSlide_Carriage", "Mesh_SuckerPuncher_RamArm",
               "Mesh_KnuckleBlock_Segmented")


def main():
    export(SRC, DST, keep_armature=False)

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    for obj in sorted(meshes, key=lambda o: o.name):
        loc = obj.location
        tag = "RAM " if obj.name in RAM_OBJECTS else "    "
        print("  %sPIVOT %-34s (%.4f, %.4f, %.4f)"
              % (tag, obj.name, loc.x, loc.y, loc.z))


main()
