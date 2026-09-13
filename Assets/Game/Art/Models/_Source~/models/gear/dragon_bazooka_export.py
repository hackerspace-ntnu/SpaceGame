"""Ship the Dragon Bazooka and its ammunition to Unity.

Three FBXs, because they are three separate things in the game: the weapon in
the player's hands, the rocket it fires, and the whelps that rocket bursts
into. The two rounds come out of the shared component file rather than the
model, which is why they need `keep=` — a component file holds every variation
stacked at the origin, and exported whole it arrives as one lump.

The pivot dump at the end is the point of running this rather than exporting by
hand. `DragonBazookaBuilder` reads the markers and the jaw by serialized
reference and needs to know where each origin landed; printing it here beats
measuring it in the editor afterwards.

The rig is dropped (`keep_armature=False`) because there is none. The only
moving part is the jaw, which turns about one axis as one rigid piece and
carries that axis in its own object origin — see the model's docstring.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/dragon_bazooka_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import export, unity_path  # noqa: E402

LIB = os.path.dirname(os.path.dirname(HERE))
MODEL = os.path.join(HERE, "dragon_bazooka.blend")
ROCKETS = os.path.join(LIB, "components", "props", "dragon_rocket.blend")

# The jaw is called out because it is the one object the Unity prefab keeps as
# a live transform instead of static geometry.
JAW = "Mesh_DragonJaw_Roaring"

FIREWORK = ["Mesh_DragonRocket_Firework"]
WHELP = ["Mesh_DragonRocket_Whelp"]


def dump_pivots(highlight=()):
    for obj in sorted((o for o in bpy.data.objects if o.type == 'MESH'),
                      key=lambda o: o.name):
        loc = obj.location
        tag = "JAW " if obj.name in highlight else "    "
        print("  %sPIVOT %-34s (%.4f, %.4f, %.4f)"
              % (tag, obj.name, loc.x, loc.y, loc.z))


def main():
    print("== Dragon Bazooka ==")
    export(MODEL, unity_path("Items", "dragon_bazooka.fbx"),
           keep_armature=False)
    dump_pivots({JAW})

    print("== Dragon Rocket (hero) ==")
    export(ROCKETS, unity_path("Items", "dragon_rocket.fbx"),
           keep_armature=False, keep=FIREWORK)

    print("== Dragon Rocket (whelp) ==")
    export(ROCKETS, unity_path("Items", "dragon_rocket_whelp.fbx"),
           keep_armature=False, keep=WHELP)


main()
