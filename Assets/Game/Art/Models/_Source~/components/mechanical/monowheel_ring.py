"""components/mechanical/monowheel_ring - the monowheels' paddle rings, lifted into the library.

    blender --background --python monowheel_ring.py -- --out monowheel_ring.blend

Taken from the HAND-FINISHED monowheels, not rebuilt: the user calls the Runner perfect, so its
ring is the reference. Each ring is re-seated with its hub at the origin and its axle along X
(the model's rig tilt removed); paddles and mounts stay parented to the band, so turning the
band turns the wheel.

| Collection | Source | Reads as |
| --- | --- | --- |
| `Coll_MonowheelRing_Wooden` | Runner | iron band, eight wooden paddle boards on iron mounts |
| `Coll_MonowheelRing_Patched` | Patched | rust patch plates, mismatched boards, one board torn off |

Generation script - historical record. The .blend is the source of truth.
"""
import os, re, sys
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
import bpy
import _buildlib as B

SRC = os.path.join(B.LIB_ROOT, "models", "vehicles", "desert_monowheel.blend")
PIECE = re.compile(r"^Mesh_(Ring|Paddle\d\d|PaddleMount\d\d|RingPatch\d\d)_(Runner|Patched)$")


def ring(variant, source_tag):
    with bpy.data.libraries.load(SRC) as (src, _):
        names = [n for n in src.objects if PIECE.match(n) and n.endswith("_" + source_tag)]
    renames = {}
    for n in names:
        piece = PIECE.match(n).group(1).replace("PaddleMount", "Mount")
        renames[n] = "Mesh_MonowheelRing_%s_%s" % (variant, "Band" if piece == "Ring" else piece)
    B.append_reframed(SRC, renames, B.collection("Coll_MonowheelRing_" + variant), "Mesh_Ring_" + source_tag)


out = B.parse_out()
B.start(out)
ring("Wooden", "Runner")
ring("Patched", "Patched")
B.save(out)
