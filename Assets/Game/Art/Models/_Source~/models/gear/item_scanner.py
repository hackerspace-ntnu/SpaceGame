"""Item Scanner — the forearm-mounted salvage finder.

Assembly only. Every surface on this model comes from a component; what is
authored here is the bracket that marries them and the placement that makes the
two read as one device rather than a box balanced on a boot.

Objects shipped, and why each is separate:

| Object | Separate because |
|---|---|
| `Mesh_Terminal_Scanner_Case`    | the static body |
| `Mesh_Terminal_Scanner_Screen`  | Unity paints the radar shader on this alone |
| `Mesh_Terminal_Scanner_Dial`    | the game spins it while scanning; origin on its axis |
| `Mesh_Terminal_Scanner_Antenna` | the game whips it; origin at its root |
| `Mesh_ArmCuff_Grip`             | reused component, no scanner-specific edit |
| `Mesh_ItemScanner_Bracket`      | the only geometry unique to this model |

Component names are kept rather than renamed to `Mesh_ItemScanner_*`, so the
provenance of each piece is readable straight off the outliner. Nothing in Unity
binds by name — the prefab wires serialized Transform references.

**No armature.** The skill's default is to add one wherever anything could move,
and two things here do: the dial and the antenna. Both are rigid, neither
deforms, and both are already separate objects with their origins on their axis
of motion — which is the cleaner form of exactly the same capability, and skips
a bone hierarchy Unity would have to unpick on import.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, os.path.join(_LIB, "components", "mechanical"))

import bpy  # noqa: E402
from _buildlib import *  # noqa: E402,F403
from panel_control import tube_path  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

TERMINAL = os.path.join(_LIB, "components", "props", "handheld_terminal.blend")
CUFF = os.path.join(_LIB, "components", "props", "arm_cuff.blend")

STEEL, DARK, RUBBER, CHROME, LEATHER, PALE, CANVAS, BRASS, BLACK = range(9)
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Plastic_Rubber_Black", "Mat_Metal_Chrome_Scuffed",
        "Mat_Fabric_Seat_Ochre", "Mat_Paint_Hull_Bleached",
        "Mat_Fabric_Canvas_Faded", "Mat_Metal_Brass_Tarnished",
        "Mat_Neutral_Black_Matte"]

# Where the terminal sits on the grip: leaned back 14 degrees so the screen
# faces the holder's eye when the unit is raised, and yawed 5 so it is not
# square to the haft. The grip's collar tops out at z = 0.184.
DECK_Z = 0.181
PLACE = (Matrix.Translation(Vector((0.002, -0.004, DECK_Z)))
         @ Matrix.Rotation(math.radians(-14), 4, 'X')
         @ Matrix.Rotation(math.radians(5), 4, 'Z'))


def append_objects(blend, names, into):
    """Append named objects from a component file into `into`.

    Appended, not linked: an export has to carry real mesh data, and a linked
    object arrives as a proxy the FBX writer skips silently.
    """
    with bpy.data.libraries.load(blend, link=False) as (src, dst):
        missing = [n for n in names if n not in set(src.objects)]
        if missing:
            raise SystemExit("Not in %s: %s" % (blend, ", ".join(missing)))
        dst.objects = list(names)
    out = []
    for name in names:
        obj = bpy.data.objects[name]
        into.objects.link(obj)
        out.append(obj)
    return out


def place(obj, matrix):
    """Apply `matrix` into the mesh data and the object's origin, leaving the
    object at rotation 0 and scale 1.

    The library's convention is that transforms are applied, and it is not
    cosmetic here: a rotated object exports with that rotation baked into the
    FBX node, and Unity then hands the game a Transform whose local axes are not
    the ones the code reasons about. The origin still carries the meaning —
    it moves with the matrix, the mesh rotates about it.
    """
    obj.data.transform(matrix.to_3x3().to_4x4())
    obj.location = matrix @ obj.location
    return obj


def bracket(coll, mats):
    """The yoke that clamps the terminal to the grip — unique to this model.

    A plate across the grip's collar, seating lugs, and a strap over the top.
    Without it the case floats, and the piece exists to show the load path from
    a 0.146 m instrument down onto a 0.074 m haft — the wider the head got
    relative to the grip, the more work this had to do.
    """
    p = Part(mats)
    hard = []

    top_w, top_d = 0.078, 0.068
    hard += p.slab((-top_w / 2, -top_d / 2, 0.170), (top_w / 2, top_d / 2, 0.179),
                   STEEL)
    hard += p.slab((-top_w / 2 - 0.004, -top_d / 2 - 0.004, 0.162),
                   (top_w / 2 + 0.004, top_d / 2 + 0.004, 0.171), DARK)

    # Shoulders reaching out past the collar to meet a case half again as wide.
    for sx in (-1, 1):
        hard += p.box((sx * 0.048, 0.0, 0.1745), (0.026, 0.040, 0.008), STEEL)
        p.cyl((sx * 0.048, 0.0, 0.1795), 0.0072, 0.004, 'Z', 8, CHROME)

    # Seating lugs. Kept to the few millimetres of daylight between the bracket
    # plate and the leaned-back case: anything taller is geometry buried inside
    # the terminal, which costs triangles and shows up as interior faces.
    for sx in (-1, 1):
        hard += p.box((sx * 0.030, -0.024, 0.1805), (0.014, 0.014, 0.006),
                      STEEL)
        hard += p.box((sx * 0.030, 0.024, 0.1820), (0.014, 0.014, 0.009), STEEL)

    # Strap over the deck, and a coiled lead disappearing into the grip.
    hard += p.box((0.0, 0.002, 0.175), (0.104, 0.018, 0.006), CANVAS)
    hard += p.box((0.044, 0.002, 0.176), (0.014, 0.024, 0.010), BRASS)
    tube_path(p, [(-0.040, 0.026, 0.170), (-0.048, 0.020, 0.152),
                  (-0.044, 0.008, 0.130)], 0.0038, RUBBER, seg=6)

    p.rivets((-0.032, 0.0, 0.1795), (0.032, 0.0, 0.1795), 4, radius=0.0022,
             height=0.0028, axis='Z', mat=CHROME)

    p.bevel(hard, width=0.0016, segments=2)
    return p.finish("Mesh_ItemScanner_Bracket", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_ItemScanner")

    append_objects(CUFF, ["Mesh_ArmCuff_Grip"], coll)
    for obj in append_objects(TERMINAL, [
            "Mesh_Terminal_Scanner_Case", "Mesh_Terminal_Scanner_Screen",
            "Mesh_Terminal_Scanner_Dial", "Mesh_Terminal_Scanner_Antenna"],
            coll):
        place(obj, PLACE)

    bracket(coll, mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
