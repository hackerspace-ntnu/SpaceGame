"""Console pedestals — what a console head stands on.

Three variations with three silhouettes, not three paint jobs:

    Cabinet  a closed cream cabinet on a rubber plinth: service door, vent,
             kick plate, orange band. The standing terminal's column — the
             one the request needed.
    Stem     a tapered lectern stem on a round foot with a conduit up its
             back. For a map table or a lone readout in a corridor.
    Rack     an open steel tube frame with a mid shelf and rubber feet. For
             an outpost or a workshop, where nothing is enclosed.

All three top out at `TOP` = 1.06 m, so a `crt_monitor` head standing on
any of them puts its screen centre near 1.40 m — eye height for a person at
this scale, and after the lander's 1.7x fixture scale (see
`repair_station_BUILD.md`) the crew's own 2.45 m eye. A head is buried
EMBED into the top so the two never share a plane.

Objects, each its own renderer:

    Mesh_Pedestal_Cabinet_Column / _Plinth
    Mesh_Pedestal_Stem_Column    / _Plinth
    Mesh_Pedestal_Rack_Frame     / _Shelf

Origin at floor level, centre of the footprint, front toward -Y.

    blender --background --python console_pedestal.py -- --out console_pedestal.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, _HERE)

from _console_kit import *  # noqa: E402,F403
from _console_kit import EMBED, MATS  # noqa: E402
from _buildlib import collection, link_materials, parse_out, report, save, start  # noqa: E402
from panel_control import tube_path  # noqa: E402

TOP = 1.060

# The cabinet's envelope, read by the model that stands a head on it. The
# front is 10 mm behind a Kiosk bezel's back plane and the back 10 mm behind
# the head's, so neither face is shared.
CAB_HALF_W = 0.35
CAB_FRONT, CAB_BACK = -0.15, 0.18
PLINTH_H = 0.06
# The cabinet front carries nothing between these heights — a deck mounts here.
DECK_LO, DECK_HI = 0.780, TOP - 0.050


def cabinet(coll, mats):
    # Plinth: a rubber foot wider than the cabinet, with four bolt heads.
    pl = TrackedPart(mats)
    hard = rounded_slab_z(pl, -0.38, 0.38, CAB_FRONT - 0.05, CAB_BACK + 0.03,
                          0.0, PLINTH_H, 0.030, RUBBER)
    for sx in (-1, 1):
        for y in (CAB_FRONT - 0.025, CAB_BACK + 0.005):
            pl.cyl((sx * 0.36, y, PLINTH_H), 0.008, 0.006, 'Z', 8, CHROME)
    emit(pl, "Mesh_Pedestal_Cabinet_Plinth", coll, hard=hard)

    p = TrackedPart(mats)
    hard, fine = [], []
    hard += p.slab((-CAB_HALF_W, CAB_FRONT, PLINTH_H - EMBED),
                   (CAB_HALF_W, CAB_BACK, TOP), CREAM)

    # Kick plate, inset from the sides so it shares no plane with them.
    fine += p.slab((-0.34, CAB_FRONT + EMBED, PLINTH_H + 0.005),
                   (0.34, CAB_FRONT - 0.004, 0.110), RUBBER)

    # Service door: a lighter panel standing proud, latch right, hinges left.
    hard += rounded_slab(p, -0.29, 0.29, 0.140, 0.580, CAB_FRONT + EMBED,
                         CAB_FRONT - 0.004, 0.020, SHELL)
    fine += p.box((0.245, CAB_FRONT - 0.009, 0.360), (0.018, 0.012, 0.060),
                  CHROME)
    for z in (0.200, 0.520):
        fine += p.box((-0.283, CAB_FRONT - 0.009, z), (0.016, 0.012, 0.080),
                      DARK)

    fine += vent(p, -0.20, 0.20, 0.620, 0.700, CAB_FRONT, bars=5)

    # Orange band and, above it, the dark collar the head sits in. Both wrap
    # front and sides; the side pieces stop inside the front piece rather
    # than on its face. The front between them, DECK_LO..DECK_HI, is left
    # bare: that is where a keyboard deck's hinge bar and bracket enter the
    # cabinet (`standing_terminal.py`).
    for z0, z1, mat, proud in ((0.740, 0.780, ORANGE, 0.007),
                               (TOP - 0.050, TOP - 0.020, DARK, 0.005)):
        fine += p.slab((-0.34, CAB_FRONT + EMBED, z0),
                       (0.34, CAB_FRONT - proud, z1), mat)
        for sx in (-1, 1):
            fine += p.slab((sx * (CAB_HALF_W - EMBED), CAB_FRONT, z0),
                           (sx * (CAB_HALF_W + proud), CAB_BACK - 0.010, z1),
                           mat)

    # Cable grommet on the back.
    p.cyl((0, CAB_BACK + 0.004, 0.300), 0.020, 0.014, 'Y', 12, RUBBER)
    return emit(p, "Mesh_Pedestal_Cabinet_Column", coll, hard=hard, fine=fine)


def stem(coll, mats):
    pl = TrackedPart(mats)
    pl.cyl((0, 0.01, 0.025), 0.32, 0.050, 'Z', 32, RUBBER)
    pl.cyl((0, 0.01, 0.050), 0.24, 0.006, 'Z', 32, CHROME)
    emit(pl, "Mesh_Pedestal_Stem_Plinth", coll)

    p = TrackedPart(mats)
    # A loft between two rounded rectangles: wide at the foot, narrow at the
    # top. Sides shaded like the kit's slabs — arcs smooth, flats flat.
    z0, z1 = PLINTH_H - 0.010, TOP - 0.020
    faces = p.loft([(z0, rounded_rect(-0.22, 0.22, -0.16, 0.16, 0.05, 6)),
                    (z1, rounded_rect(-0.15, 0.15, -0.11, 0.11, 0.04, 6))],
                   axis='Z', mat=CREAM)
    for f in faces:
        n = f.normal.normalized()
        f.smooth = abs(n.z) < 0.9 and max(abs(n.x), abs(n.y)) < 0.995
    for f in faces:
        for e in f.edges:
            if len(e.link_faces) == 2 and \
                    e.link_faces[0].smooth != e.link_faces[1].smooth:
                e.smooth = False
    # Top plate the head stands on, buried into the stem's top.
    hard = rounded_slab_z(p, -0.17, 0.17, -0.13, 0.13, z1 - EMBED, TOP,
                          0.030, DARK)
    # Conduit up the back, kept inside the tapering face by its own radius.
    tube_path(p, [(0, 0.160 + 0.006, PLINTH_H + 0.010),
                  (0, 0.110 + 0.006, z1 - 0.030)], 0.012, DARK, seg=8,
              joint=False)
    p.cyl((0, 0.166, PLINTH_H + 0.010), 0.020, 0.016, 'Z', 12, RUBBER)
    return emit(p, "Mesh_Pedestal_Stem_Column", coll, hard=hard)


def rack(coll, mats):
    legs = [(sx * 0.30, y) for sx in (-1, 1) for y in (-0.13, 0.15)]
    p = TrackedPart(mats)
    for x, y in legs:
        p.cyl((x, y, 0.015), 0.035, 0.030, 'Z', 12, RUBBER)
        p.cyl((x, y, (0.027 + TOP - 0.004) / 2.0), 0.018,
              TOP - 0.004 - 0.027, 'Z', 10, STEEL)
    ring = [(-0.30, -0.13), (0.30, -0.13), (0.30, 0.15), (-0.30, 0.15),
            (-0.30, -0.13)]
    for z in (0.150, TOP - 0.020):
        tube_path(p, [(x, y, z) for x, y in ring], 0.014, STEEL, seg=8,
                  joint=False)
    tube_path(p, [(-0.30, 0.15, 0.150), (0.30, 0.15, TOP - 0.020)], 0.010,
              STEEL, seg=8, joint=False)
    # Top plate: the mounting surface, sitting over the top rails.
    hard = rounded_slab_z(p, -0.33, 0.33, -0.16, 0.18, TOP - 0.012, TOP, 0.030,
                          GREY)
    emit(p, "Mesh_Pedestal_Rack_Frame", coll, hard=hard)

    s = TrackedPart(mats)
    hard = rounded_slab_z(s, -0.28, 0.28, -0.11, 0.13, 0.500, 0.520, 0.020,
                          GREY)
    for y in (-0.13, 0.15):
        s.cyl((0, y, 0.503 - 0.010), 0.010, 0.60, 'X', 8, STEEL)
    emit(s, "Mesh_Pedestal_Rack_Shelf", coll, hard=hard)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    cabinet(collection("Coll_Pedestal_Cabinet"), mats)
    stem(collection("Coll_Pedestal_Stem"), mats)
    rack(collection("Coll_Pedestal_Rack"), mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
