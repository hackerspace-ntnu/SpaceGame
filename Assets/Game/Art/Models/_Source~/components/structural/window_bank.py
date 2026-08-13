"""components/structural/window_bank — openings in a heavy industrial wall.

`bulkhead_frame` covers the openings people walk through; this covers the ones
they look through. Both are wall penetrations, but a door frame is authored
around a 2.1 m clear height and a threshold, and a window is authored around
sightlines and a sill — sharing one component would mean every call site
carrying a flag that changes almost every dimension.

Authored on the plane y = 0 facing **-Y**, matching the library's forward axis,
with the origin at the centre of the opening. A wall that faces -Y takes these
at a point on its own surface with no rotation; any other face is one 90 degree
turn. Geometry runs from about y = -0.3 (proud hoods and frames) to y = +0.3
(the dark reveal behind), so the component reads as *through* a wall of any
thickness rather than stuck on the front of one.

No booleans: the dark reveal is a backing plate set behind a raised frame,
which gives the same read as a cut hole and cannot fail on a bevelled host.

    blender --background --python window_bank.py -- --out window_bank.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

MATS = [
    "Mat_Metal_Steel_Worn",        # 0 STEEL  frames, mullions, guide rails
    "Mat_Metal_Steel_Dark",        # 1 DARK   shutters, hoods, drum housings
    "Mat_Neutral_Black_Matte",     # 2 BLACK  the reveal — the dark behind glass
    "Mat_Glass_Canopy_Tinted",     # 3 GLASS  glazing
    "Mat_Metal_Rust_Heavy",        # 4 RUST   corrosion, welded-on patch plates
    "Mat_Metal_HullRust_Orange",   # 5 HULL   surround plate, matching the hulk
    "Mat_Metal_Chrome_Scuffed",    # 6 CHROME handles, pull bars, bolt heads
]
STEEL, DARK, BLACK, GLASS, RUST, HULL, CHROME = range(7)

OUT = Vector((0, -1, 0))           # the direction "out of the wall"


def reveal(p, w, h, depth=0.34, mat=BLACK):
    """The dark box behind the opening. This is what sells it as a hole."""
    return p.box((0, depth / 2.0, 0), (w, depth, h), mat)


def frame(p, w, h, thick=0.22, proud=0.20, mat=STEEL, bolts=0):
    """A raised rectangular frame around the opening."""
    faces = []
    y = -proud / 2.0 + 0.01
    for dx, dz, sw, sh in ((0, (h + thick) / 2.0, w + 2 * thick, thick),
                           (0, -(h + thick) / 2.0, w + 2 * thick, thick),
                           ((w + thick) / 2.0, 0, thick, h),
                           (-(w + thick) / 2.0, 0, thick, h)):
        faces += p.box((dx, y, dz), (sw, proud, sh), mat)
    for i in range(bolts):
        t = (i + 0.5) / bolts
        for sz in (-1, 1):
            faces += p.cyl((-w / 2.0 + w * t, -proud, sz * (h + thick) / 2.0),
                           0.038, 0.06, axis='Y', seg=6, mat=CHROME)
    return faces


def sill(p, w, h, mat=DARK):
    """A drip sill under the opening, and the rust it fails to stop.

    Water leaves a window at the sill and runs down the wall below it, so the
    streaks are anchored here rather than scattered — that anchoring is the
    difference between weathering and speckle.
    """
    faces = p.box((0, -0.14, -(h / 2.0) - 0.26), (w + 0.5, 0.34, 0.16), mat)
    for t, wd, run in ((-0.28, 0.19, 1.15), (0.11, 0.13, 0.72),
                       (0.34, 0.22, 1.45)):
        faces += p.box((w * t, -0.05, -(h / 2.0) - 0.34 - run / 2.0),
                       (wd, 0.04, run), RUST)
    return faces


def hood(p, w, z, depth=0.62, drop=0.20, mat=DARK):
    """A sun hood on two brackets — desert kit, and it reads at any distance."""
    faces = p.box((0, -depth / 2.0, z + 0.06), (w, 0.07, depth), mat,
                  rot=Matrix.Rotation(math.radians(-14), 4, 'X'))
    for sx in (-1, 1):
        faces += p.prism([(-0.04, 0.0), (-depth, 0.0), (-0.04, -drop - 0.22)],
                         0.07, axis='X', mat=STEEL,
                         offset=(sx * (w / 2.0 - 0.09), 0, z + 0.06))
    return faces


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def build_porthole(mats, parent):
    """The heavy round port — the reference image's one unmistakable opening."""
    coll = collection("Coll_WindowBank_Porthole", parent)
    p = Part(mats)

    r = 0.72
    p.box((0, 0.20, 0), (2.1, 0.42, 2.1), HULL)              # surround plate
    p.cyl((0, 0.16, 0), r + 0.02, 0.30, axis='Y', seg=24, mat=BLACK)
    p.cyl((0, -0.02, 0), r * 0.92, 0.06, axis='Y', seg=24, mat=GLASS)
    p.tube((0, -0.11, 0), r + 0.20, 0.26, 0.26, axis='Y', seg=24, mat=STEEL)
    p.tube((0, -0.20, 0), r + 0.06, 0.10, 0.10, axis='Y', seg=24, mat=DARK)
    for i in range(12):
        a = 2 * math.pi * i / 12.0
        p.cyl(((r + 0.13) * math.cos(a), -0.25, (r + 0.13) * math.sin(a)),
              0.045, 0.09, axis='Y', seg=6, mat=CHROME)
    # A dog handle at the rim: this port was meant to be opened.
    p.box((r + 0.34, -0.24, -0.05), (0.44, 0.10, 0.10), CHROME,
          rot=Matrix.Rotation(math.radians(28), 4, 'Y'))
    hood(p, 2.0, r + 0.30)
    sill(p, 1.7, 2.0)

    p.bevel(width=0.015, segments=1)
    p.finish("Mesh_WindowBank_Porthole", coll)
    return coll


def build_slotrow(mats, parent):
    """A recessed band of narrow slots — a control room's horizon."""
    coll = collection("Coll_WindowBank_SlotRow", parent)
    p = Part(mats)

    w, h, n = 4.4, 1.05, 4
    reveal(p, w, h)
    for i in range(n):
        x = -w / 2.0 + w * (i + 0.5) / n
        p.box((x, -0.02, 0), (w / n - 0.20, 0.06, h - 0.22), GLASS)
    for i in range(1, n):
        x = -w / 2.0 + w * i / n
        p.box((x, -0.09, 0), (0.20, 0.24, h), STEEL)
    frame(p, w, h, thick=0.26, proud=0.24, bolts=7)
    hood(p, w + 0.5, h / 2.0 + 0.30, depth=0.72)
    sill(p, w, h)

    p.bevel(width=0.015, segments=1)
    p.finish("Mesh_WindowBank_SlotRow", coll)
    return coll


def build_shuttered(mats, parent):
    """An armoured roll shutter caught half down.

    Half down rather than open or closed on purpose: a stopped mechanism is the
    single cheapest way to say that whatever ran this machine stopped running
    it mid-task, and it gives the bone the armature needs.
    """
    coll = collection("Coll_WindowBank_Shuttered", parent)
    p = Part(mats)

    w, h = 2.6, 2.2
    reveal(p, w, h)
    frame(p, w, h, thick=0.24, proud=0.22)
    for sx in (-1, 1):                                        # guide rails
        p.box((sx * (w / 2.0 + 0.02), -0.16, 0), (0.16, 0.30, h + 0.3), STEEL)
    # Drum housing over the head.
    p.box((0, -0.24, h / 2.0 + 0.44), (w + 0.56, 0.52, 0.50), DARK)
    for sx in (-1, 1):
        p.cyl((sx * (w / 2.0 + 0.28), -0.24, h / 2.0 + 0.44), 0.24, 0.10,
              axis='X', seg=12, mat=CHROME)
    # Slats, filling the upper 55 percent of the opening.
    drop = h * 0.55
    slats = 9
    for i in range(slats):
        z = h / 2.0 - drop * (i + 0.5) / slats
        p.box((0, -0.13, z), (w - 0.06, 0.10, drop / slats - 0.02), DARK)
    p.box((0, -0.15, h / 2.0 - drop - 0.04), (w - 0.06, 0.15, 0.13), STEEL)
    p.box((0, -0.24, h / 2.0 - drop - 0.04), (0.7, 0.07, 0.07), CHROME)
    sill(p, w, h)

    p.bevel(width=0.015, segments=1)
    p.finish("Mesh_WindowBank_Shuttered", coll)
    return coll


def build_blown(mats, parent):
    """Glazing gone, frame bent, one corner welded shut with scrap.

    The repair matters as much as the damage. A broken window says something
    hit it; a broken window with a plate welded over half of it says somebody
    was still living here afterwards, and then stopped.
    """
    coll = collection("Coll_WindowBank_Blown", parent)
    p = Part(mats)

    w, h = 2.6, 2.2
    reveal(p, w, h, depth=0.55)
    # The frame, with two members knocked out of true.
    p.box((0, -0.10, (h + 0.24) / 2.0), (w + 0.48, 0.22, 0.24), RUST,
          rot=Matrix.Rotation(math.radians(2.5), 4, 'Y'))
    p.box((0, -0.10, -(h + 0.24) / 2.0), (w + 0.48, 0.22, 0.24), STEEL)
    p.box(((w + 0.24) / 2.0, -0.10, 0.05), (0.24, 0.22, h - 0.1), RUST,
          rot=Matrix.Rotation(math.radians(-4), 4, 'Y'))
    p.box((-(w + 0.24) / 2.0, -0.10, 0), (0.24, 0.22, h), STEEL)
    # Shards still in the rebate.
    for x, z, ang, s in ((-0.95, 0.72, 34, (0.42, 0.05, 0.30)),
                         (0.30, 0.86, -21, (0.55, 0.05, 0.22)),
                         (1.02, -0.30, 58, (0.26, 0.05, 0.46)),
                         (-1.06, -0.62, -47, (0.22, 0.05, 0.38))):
        p.box((x, -0.02, z), s, GLASS,
              rot=Matrix.Rotation(math.radians(ang), 4, 'Y'))
    # Scrap plate welded over the lower corner, and a bar across the rest.
    p.box((-0.62, -0.14, -0.58), (1.35, 0.06, 0.95), RUST,
          rot=Matrix.Rotation(math.radians(6), 4, 'Y'))
    for i in range(6):
        p.cyl((-1.22 + i * 0.24, -0.19, -0.15), 0.04, 0.06, axis='Y', seg=6,
              mat=CHROME)
    p.box((0.25, -0.20, 0.42), (2.2, 0.09, 0.09), STEEL,
          rot=Matrix.Rotation(math.radians(-11), 4, 'Y'))
    sill(p, w, h)

    p.bevel(width=0.015, segments=1)
    p.finish("Mesh_WindowBank_Blown", coll)
    return coll


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    root = bpy.context.scene.collection

    build_porthole(mats, root)
    build_slotrow(mats, root)
    build_shuttered(mats, root)
    build_blown(mats, root)

    report()
    save(out)


if __name__ == "__main__":
    main()
