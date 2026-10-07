"""components/props/saddlebag - saddlebags slung over a rail, lifted from the Hauler monowheel.

    blender --background --python saddlebag.py -- --out saddlebag.blend

Built by models/vehicles/monowheel_luggage.py onto the Hauler's side hoops, then taken from that
finished model rather than rebuilt. Re-seated upright with the bag's hanging edge at the origin;
the hanger loops show where the rail passes.

| Collection | Reads as |
| --- | --- |
| `Coll_Saddlebag_CanvasBedroll` | canvas bag, leather flap and buckles, a bedroll tied on top |
| `Coll_Saddlebag_LeatherCanisters` | leather bag, canvas flap, two canisters roped behind it |

Generation script - historical record. The .blend is the source of truth.
"""
import os, sys
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
import _buildlib as B

SRC = os.path.join(B.LIB_ROOT, "models", "vehicles", "desert_monowheel.blend")
KITS = {
    "CanvasBedroll": ("L", ["Saddlebag", "SaddlebagFlap", "SaddlebagFittings", "SideBedroll", "SideBedrollTies"]),
    "LeatherCanisters": ("R", ["Saddlebag", "SaddlebagFlap", "SaddlebagFittings", "Canister0", "Canister1", "CanisterRopes"]),
}

out = B.parse_out()
B.start(out)
for variant, (side, pieces) in KITS.items():
    renames = {"Mesh_%s%s_Hauler" % (p, side): "Mesh_Saddlebag_%s_%s" % (variant, p.replace("Saddlebag", "") or "Body")
               for p in pieces}
    B.append_reframed(SRC, renames, B.collection("Coll_Saddlebag_" + variant), "Mesh_Saddlebag%s_Hauler" % side)
B.save(out)
