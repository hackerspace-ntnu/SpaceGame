"""Export the lander's empty oxygen-plant wall mount.

    blender --background --python oxygen_mount_empty_export.py

`Coll_OxygenMountEmpty` is what stays on the wall once the crash has torn the plant out
(Vehicles/PlayerShip/oxygen_mount_empty.fbx), in the plant's fixture frame: origin on the wall at
floor level between the rails, the room forward (Unity +Z). The three `Marker_CableEnd_A/B/C` are
6 mm mesh cubes whose pivots sit at the ripped cables' copper tips, so they ship with the meshes and
Unity reads their positions off the child transforms. The .blend is never written back.
"""
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "oxygen_mount_empty.blend")
DST = unity_path("Vehicles", "PlayerShip", "oxygen_mount_empty.fbx")

print("Coll_OxygenMountEmpty -> %s" % DST)
export(SRC, DST, keep_collection="Coll_OxygenMountEmpty")
describe()
