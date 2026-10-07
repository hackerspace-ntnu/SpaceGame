"""components/structural/track_fender — mudguards and skirts over the crawler's tracks.

A tracked land-ship throws sand; the guards over its track runs are what keep
it off the deck, and they are what takes every knock, so they read the hull's
age at a glance: the curved one, the flat one that sags, the one torn open at
the front.

Mudguards are 3.0 m long (Y) x 1.5 m wide (X) and run FORWARD (-Y) from their
origin, which is the rear-bottom centre of the guard: set it on the rear end of
a track run's top and the guard lies along the track. The down-turned side
lips hang 0.08 m below that line, outside a track up to 1.44 m wide.

Modelled for the STARBOARD (+X) track: the inboard, hull side is -X, where the
flat guard carries its brackets and the skirt faces its bolt heads outboard to
+X. Mirror in X for the port side.

    blender --background --python track_fender.py -- --out track_fender.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Metal_HullRust_Orange",   # 0 guard plate
    "Mat_Metal_Rust_Heavy",        # 1 torn and patched plate, skirt
    "Mat_Metal_Steel_Worn",        # 2 seam straps, rails
    "Mat_Metal_Steel_Dark",        # 3 brackets
    "Mat_Metal_Rust_Deep",         # 4 bolt heads
    "Mat_Metal_Rust_Pale",         # 5 sand-scoured lips
]
HULL, RUST, STEEL, DARK, DEEP, PALE = range(6)

LENGTH = 3.0
HALF_W = 0.75
TH = 0.025          # guard plate thickness
LIP = 0.08          # how far the rolled side lips turn down
STRAIGHT = 2.0      # the curved guard runs flat this far, then arcs down
RADIUS = LENGTH - STRAIGHT


def curve_station(s):
    """Point and outward normal on the curved guard's centre surface.

    `s` < 0 is a distance back along the flat run (0 at the arc's start);
    otherwise it is the arc angle in degrees, 0 at the top, 90 at the front.
    Returns ((y, z), (ny, nz)), with the underside at z=0 on the flat run.
    """
    if s < 0:
        return (-STRAIGHT - s, TH / 2), (0.0, 1.0)
    a = math.radians(s)
    return ((-STRAIGHT - RADIUS * math.sin(a), TH / 2 - RADIUS + RADIUS * math.cos(a)),
            (-math.sin(a), math.cos(a)))


def guard_row(station, xs, lip_cols, bend=(0.0, 0.0)):
    """One row of sheet points across the guard. Columns in `lip_cols` are
    pulled down against the surface normal to form the rolled lip; `bend`
    pushes the row's outboard half out (+X) and up the normal."""
    (y, z), (ny, nz) = station
    row = []
    for j, x in enumerate(xs):
        drop = -LIP if j in lip_cols else 0.0
        push = bend[1] * max(0.0, x) / HALF_W
        row.append(Vector((x + bend[0] * max(0.0, x) / HALF_W,
                           y + ny * (drop + push), z + nz * (drop + push))))
    return row


def strap(p, s, mat=STEEL, bolts=5, half=0.72):
    """A seam strap across the guard at station `s`, bolted along its length."""
    (y, z), (ny, nz) = curve_station(s)
    a = math.radians(max(s, 0.0))
    rot = Matrix.Rotation(a, 4, 'X')           # local +Z onto the normal
    lift = TH / 2 + 0.01 - 0.004
    p.box((0, y + ny * lift, z + nz * lift), (half * 2, 0.10, 0.02), mat, rot=rot)
    top = lift + 0.012
    for k in range(bolts):
        x = -half + 0.12 + (2 * half - 0.24) * k / (bolts - 1)
        bolt(p, (x, y + ny * top, z + nz * top), rot=rot)


def bolt(p, pos, axis='Z', rot=None, r=0.024, h=0.026, mat=DEEP):
    """Square-headed bolt: four sides keep a row of them affordable."""
    return p.cyl(pos, r, h, axis, seg=4, mat=mat, radius_top=r * 0.65, rot=rot)


def build_curved(coll):
    """Flat for 2 m, then a 1 m radius arc down over the front idler."""
    p = Part(MATS)
    xs = (-HALF_W, -HALF_W + 0.03, HALF_W - 0.03, HALF_W)
    stations = [-STRAIGHT, 0.0, 15.0, 30.0, 45.0, 60.0, 75.0, 90.0]
    faces = p.sheet([guard_row(curve_station(s), xs, (0, 3)) for s in stations], TH, HULL)
    for f in faces:            # the lips wear bright where sand scours them
        if abs(f.calc_center_median().x) > HALF_W - 0.02:
            f.material_index = PALE
    strap(p, -1.0)
    strap(p, 45.0)
    p.finish("Mesh_Fender_Curved", coll)


def build_torn(coll):
    """The curved guard after a rock: the front edge ripped ragged and the
    outboard front corner bent up and out. Patched once, on the flat."""
    p = Part(MATS)
    xs = (-HALF_W, -HALF_W + 0.03, -0.30, 0.10, 0.45, HALF_W - 0.03, HALF_W)
    rows = [guard_row(curve_station(s), xs, (0, 6)) for s in (-STRAIGHT, 0.0, 20.0, 40.0)]
    rows.append(guard_row(curve_station(58.0), xs, (0, 6), bend=(0.06, 0.08)))
    # The ragged edge: every column stops at its own angle, and the bent
    # corner carries on flaring outboard.
    jag = (72.0, 86.0, 66.0, 88.0, 70.0, 80.0, 77.0)
    rows.append([guard_row(curve_station(a), xs, (0, 6), bend=(0.18, 0.26))[j]
                 for j, a in enumerate(jag)])
    p.sheet(rows, TH, RUST)
    strap(p, -1.4, bolts=4)
    # Patch plate riveted over an old tear on the flat.
    (y, z), _ = curve_station(-0.5)
    p.slab((-0.35, y - 0.30, z + TH / 2 - 0.004), (0.25, y + 0.25, z + TH / 2 + 0.012), HULL)
    for x, dy in ((-0.28, -0.24), (0.18, -0.24), (-0.28, 0.19), (0.18, 0.19)):
        bolt(p, (x, y + dy, z + TH / 2 + 0.014))
    p.finish("Mesh_Fender_Torn", coll)


def build_flat(coll):
    """A flat plate that has sagged between its brackets, hung off the hull
    by three knee gussets along its inboard (-X) edge."""
    p = Part(MATS)
    xs = (-HALF_W, -HALF_W + 0.03, 0.0, HALF_W - 0.03, HALF_W)
    rows = []
    for k in range(5):
        y = -LENGTH * k / 4
        sag = -0.05 * math.sin(math.pi * k / 4)
        row = []
        for j, x in enumerate(xs):
            dip = sag - (0.02 * math.cos(math.pi * x / (2 * HALF_W)) if 0 < j < 4 else 0.0)
            lip = -LIP if j in (0, 4) else 0.0
            row.append((x, y, TH / 2 + dip + lip))
        rows.append(row)
    p.sheet(rows, TH, HULL)
    # Inboard mounting flange that bolts to the hull side.
    # It runs straight while the plate sags, so it reaches below the sag.
    p.slab((-HALF_W + 0.005, -LENGTH + 0.05, -0.06), (-HALF_W + 0.035, -0.05, 0.45), DARK)
    for y in (-0.3, -LENGTH / 2, -LENGTH + 0.3):
        # Each gusset's foot follows the sag at its station, sunk into the plate.
        foot = TH - 0.05 * math.sin(math.pi * -y / LENGTH) - 0.025
        p.prism([(-HALF_W + 0.03, foot), (-HALF_W + 0.03, 0.42), (-0.30, foot)],
                0.025, 'Y', DARK, offset=(0, y, 0))
    for y in (-0.9, -2.1):
        for z in (0.12, 0.32):
            bolt(p, (-HALF_W + 0.039, y, z), 'X')
    p.finish("Mesh_Fender_Flat", coll)


def build_skirt(coll):
    """Vertical armour skirt, 3.0 long x 1.2 tall, hung beside the track to
    shield its upper run. Origin at the top centre; bolt heads face +X."""
    p = Part(MATS)
    ys = [LENGTH / 2 - LENGTH * k / 8 for k in range(9)]
    dent_x = (0.0, -0.03, 0.02, -0.06, -0.01, 0.03, -0.05, -0.02, 0.0)
    dent_z = (0.0, 0.05, 0.01, 0.09, 0.02, 0.06, 0.12, 0.03, 0.0)
    rows = [[(0.0, y, 0.0) for y in ys],
            [(0.0, y, -0.60) for y in ys],
            [(dx, y, -1.20 + dz) for y, dx, dz in zip(ys, dent_x, dent_z)]]
    p.sheet(rows, 0.04, RUST, smooth=False)
    # Hanger rail below the top edge and a stiffener across the middle.
    p.slab((0.015, -LENGTH / 2, -0.16), (0.065, LENGTH / 2, -0.02), STEEL)
    p.slab((0.015, -LENGTH / 2 + 0.1, -0.64), (0.05, LENGTH / 2 - 0.1, -0.56), STEEL)
    for k in range(6):
        bolt(p, (0.069, -1.25 + 0.5 * k, -0.09), 'X')
    for k in range(4):
        bolt(p, (0.054, -1.05 + 0.7 * k, -0.60), 'X')
    p.finish("Mesh_Fender_Skirt", coll)


def build():
    out = parse_out()
    start(out)
    global MATS
    MATS = link_materials(MATS)

    build_curved(collection("Coll_Fender_Curved"))
    build_flat(collection("Coll_Fender_Flat"))
    build_torn(collection("Coll_Fender_Torn"))
    build_skirt(collection("Coll_Fender_Skirt"))

    report()
    save(out)


build()
