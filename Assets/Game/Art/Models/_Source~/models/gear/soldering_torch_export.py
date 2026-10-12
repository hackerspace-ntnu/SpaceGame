"""Export the handheld gas soldering torch.

    blender --background --python soldering_torch_export.py

`Coll_SolderingTorch` is the held item (Items/soldering_torch.fbx): held by the canister like a
pistol grip, origin at the grip point (canister axis, 40 % up its height), the nozzle forward
(Unity +Z) and 12 degrees down. The .blend is never written back.
"""
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
from _exportlib import describe, export, unity_path  # noqa: E402

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "soldering_torch.blend")
DST = unity_path("Items", "soldering_torch.fbx")

print("Coll_SolderingTorch -> %s" % DST)
export(SRC, DST, keep_collection="Coll_SolderingTorch")
describe()
