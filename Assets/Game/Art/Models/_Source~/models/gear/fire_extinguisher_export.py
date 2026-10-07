"""Export the fire extinguisher and its wall bracket, one FBX each.

    blender --background --python fire_extinguisher_export.py

`Coll_FireExtinguisher` is the held item (Items/fire_extinguisher.fbx); `Coll_ExtinguisherBracket`
is the wall mount it hangs in aboard the lander (Vehicles/PlayerShip). The .blend is never written
back.
"""
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
from _exportlib import export, unity_path  # noqa: E402

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fire_extinguisher.blend")

JOBS = (
    ("Coll_FireExtinguisher", unity_path("Items", "fire_extinguisher.fbx")),
    ("Coll_ExtinguisherBracket", unity_path("Vehicles", "PlayerShip", "extinguisher_bracket.fbx")),
)

for collection, dst in JOBS:
    print("%s -> %s" % (collection, dst))
    export(SRC, dst, keep_collection=collection)
