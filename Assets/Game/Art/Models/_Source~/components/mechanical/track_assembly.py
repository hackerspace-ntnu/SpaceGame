"""components/mechanical/track_assembly - complete track units: belt, wheels, suspension, fenders.

    blender --background --python track_assembly.py -- --out track_assembly.blend

Lifted from the dune barge (models/vehicles/dune_barge.blend) as assembled — the starboard units,
unmirrored — so a new tracked vehicle can drop in a whole running gear instead of rebuilding it
from track_link / track_wheel / track_bogie / track_fender. Taken from a snapshot of the live
session (dune_barge_track_snapshot.blend, written 2026-09-24 without saving the user's open file).

| Collection | Reads as |
| --- | --- |
| `Coll_TrackAssembly_Main` | 14.7 m track: sprocket, idler, 6 road wheels, 3 return rollers, 86 links, bogie girder with swing arms, gear housing with exposed gear, fenders and skirts |
| `Coll_TrackAssembly_Pod` | 5 m steering pod: sprocket, idler, 2 road wheels, return roller, 32 links, curved fender |

Origin: on the ground under the unit's centre (x = track centre, y = mid-length, z = 0), so it
drops straight onto a floor. Built for the STARBOARD side: inboard is -X (roller brackets,
bogie girder); mirror in X for port. Each wheel keeps its own object with its origin on the hub,
so it can be spun or bound to a bone; the links are separate objects in belt order (001 ...) so
a runtime can walk them round the loop.

Generation script - historical record. The .blend is the source of truth.
"""
import os, re, sys
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
import bpy
from mathutils import Matrix
import _buildlib as B

SRC = os.path.join(B.LIB_ROOT, "models", "vehicles", "dune_barge_track_snapshot.blend")
UNITS = {"Main": (3.45, 3.1), "Pod": (3.35, -9.21)}   # (x, y) of each unit's ground centre on the barge


def unit_names(tag):
    """The snapshot's meshes for one unit: pod parts all carry "_Pod" in their names."""
    with bpy.data.libraries.load(SRC) as (src, _):
        return [n for n in src.objects if n.startswith("Mesh_") and (("_Pod" in n) == (tag == "Pod"))]


out = B.parse_out()
B.start(out)
for tag, (x, y) in UNITS.items():
    names = unit_names(tag)
    renames = {n: "Mesh_TrackAssembly_%s_%s" % (tag, re.sub(r"_(MainR|PodR)", "", n[len("Mesh_"):])) for n in names}
    B.append_reframed(SRC, renames, B.collection("Coll_TrackAssembly_" + tag), Matrix.Translation((x, y, 0.0)))
bpy.ops.file.make_paths_relative()
B.save(out)
