"""Export the dish transmitter and its feed-cabin cradle, one FBX each.

    blender --background --python dish_transmitter_export.py

`Coll_DishTransmitter` is the carried ship module (Items/ShipParts); `Coll_DishTransmitterCradle`
is the saddle bolted to the satellite dish's feed horn, which stays when the module is taken
(Environment/Structures/SatelliteTower). `Coll_DishTransmitterBroken` is the lander's own burnt-out
unit (Items/ShipParts) and `Coll_ShipTransmitterCradle` the wall mount it sits in aboard the lander
(Vehicles/PlayerShip). The .blend is never written back.
"""
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
from _exportlib import export, unity_path  # noqa: E402

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "dish_transmitter.blend")

JOBS = (
    ("Coll_DishTransmitter", unity_path("Items", "ShipParts", "dish_transmitter.fbx")),
    ("Coll_DishTransmitterCradle",
     unity_path("Environment", "Structures", "SatelliteTower", "dish_transmitter_cradle.fbx")),
    ("Coll_DishTransmitterBroken", unity_path("Items", "ShipParts", "dish_transmitter_broken.fbx")),
    ("Coll_ShipTransmitterCradle", unity_path("Vehicles", "PlayerShip", "ship_transmitter_cradle.fbx")),
)

for collection, dst in JOBS:
    print("%s -> %s" % (collection, dst))
    export(SRC, dst, keep_collection=collection)
