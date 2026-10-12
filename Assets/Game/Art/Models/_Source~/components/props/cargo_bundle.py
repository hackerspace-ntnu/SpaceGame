"""components/props/cargo_bundle - lashed cargo loads, lifted from the finished monowheels.

    blender --background --python cargo_bundle.py -- --out cargo_bundle.blend

| Collection | Source | Reads as |
| --- | --- | --- |
| `Coll_CargoBundle_Rug` | Hauler ski | canvas bundle under a red rug with border and fringe, rope lashing, a bedroll on top |
| `Coll_CargoBundle_BedrollStack` | DoubleWide nose | three bedrolls stacked and tied (the user's arrangement) |

Re-seated upright with the load's base at the origin (the models set them on sloped decks).

Generation script - historical record. The .blend is the source of truth.
"""
import os, sys
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
import _buildlib as B

MONO = os.path.join(B.LIB_ROOT, "models", "vehicles")

out = B.parse_out()
B.start(out)
rug = ["CargoBundle", "CargoRug", "CargoRugBorder", "CargoRugFringe", "CargoLashing", "CargoBedroll", "CargoBedrollTies"]
B.append_reframed(os.path.join(MONO, "desert_monowheel.blend"),
                  {"Mesh_%s_Hauler" % p: "Mesh_CargoBundle_Rug_%s" % p.replace("Cargo", "") for p in rug},
                  B.collection("Coll_CargoBundle_Rug"), "Mesh_CargoBundle_Hauler")
stack = ["CargoBedroll", "CargoBedrollTies", "CargoBedroll1", "CargoBedrollTies1", "CargoBedroll2", "CargoBedrollTies2"]
B.append_reframed(os.path.join(MONO, "desert_monowheel_double.blend"),
                  {"Mesh_%s_DoubleWide" % p: "Mesh_CargoBundle_BedrollStack_%s" % p.replace("Cargo", "") for p in stack},
                  B.collection("Coll_CargoBundle_BedrollStack"), "Mesh_CargoBedroll_DoubleWide")
B.save(out)
