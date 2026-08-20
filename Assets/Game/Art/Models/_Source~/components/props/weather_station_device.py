"""Portable weather stations — the carried instrument behind the storm artifact.

Three fittings that read as the same programme of equipment rather than three
unrelated gadgets: all of them are a painted instrument body on a steel frame
with one lit readout, and all of them put something that catches the wind above
the body. What separates them is silhouette, which is the axis that survives
being shrunk to a 256 px inventory icon — a spinning cup head, a flat vane, and
a naked rod cage are tellable apart at a glance where three differently-painted
boxes would not be.

Sized as carried equipment: 0.23-0.26 m tall, which is a thing you clip to a
pack, not a thing you bolt to a roof. Origin sits on the base plate so the
model drops onto a surface without needing a Z nudge.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

from mathutils import Matrix  # noqa: E402

# Index 0 is load-bearing: `bmesh.ops.bevel` stamps every face it creates with
# material index 0, so whatever sits first here is the colour of every bevelled
# edge in the file. Steel is the right default — a chamfer through the paint
# should read as the bare metal under it. Putting the accent blue first instead
# turns every bevelled edge pale blue, which is what the first build did.
STEEL, BLUE, DARK, CRT, AMBER, GLASS, RUBBER, CHROME, RUST, COPPER = range(10)
MATS = ["Mat_Metal_Steel_Worn", "Mat_Paint_Blue_Station", "Mat_Metal_Steel_Dark",
        "Mat_Emissive_Green_CRT", "Mat_Emissive_Amber",
        "Mat_Glass_Canopy_Tinted", "Mat_Plastic_Rubber_Black",
        "Mat_Metal_Chrome_Scuffed", "Mat_Metal_Rust_Heavy",
        "Mat_Metal_Copper_Oxide"]

# Bevel only the boxy geometry. Running `p.bevel()` over a whole part at this
# scale destroys it: a 2.6 mm bevel on a 5.5 mm swept tube is half its radius,
# and where several tubes converge on one point `finish()`'s remove_doubles
# then welds the over-bevelled ends into a solid blob. The first build of the
# beacon grew a smooth metal dome over its cage exactly this way. So every
# builder below accumulates the faces that *should* take an edge and passes
# just those.
BEVEL_W = 0.0026


def _base_plate(p, half, hard, z=0.0, thick=0.018):
    """Shared footing. Every variation stands on the same plate so they look
    like one product line and so the origin means the same thing on all three."""
    hard += p.slab((-half, -half, z), (half, half, z + thick), DARK)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.cyl((sx * (half - 0.016), sy * (half - 0.016), z + thick),
                  0.009, 0.010, 'Z', 8, RUBBER)
    return z + thick


def _readout(p, centre, w, h, hard, tilt=0.0):
    """A recessed lit panel behind glass. The one emissive note per model —
    more than one and the icon reads as a christmas tree at thumbnail size."""
    rot = Matrix.Rotation(tilt, 4, 'X')
    hard += p.box(centre, (w, 0.012, h), DARK, rot=rot)
    p.box(centre, (w * 0.86, 0.016, h * 0.76), CRT, rot=rot)
    p.box(centre, (w * 0.92, 0.020, h * 0.84), GLASS, rot=rot)


def field(coll, mats):
    """Cup anemometer over a slab body — the standard-issue unit.

    This is the one wired to the artifact. The three-cup head is the reason:
    it is the only silhouette in the set that is unmistakably a weather
    instrument rather than a generic radio.
    """
    p = Part(mats)
    hard = []
    top = _base_plate(p, 0.072, hard)

    # Instrument body, slightly tapered so it does not read as a plain box.
    hard += p.loft(
        [(0.000, [(-0.062, -0.044), (0.062, -0.044), (0.062, 0.044), (-0.062, 0.044)]),
         (0.128, [(-0.058, -0.040), (0.058, -0.040), (0.058, 0.040), (-0.058, 0.040)]),
         (0.150, [(-0.048, -0.034), (0.048, -0.034), (0.048, 0.034), (-0.048, 0.034)])],
        axis='Z', mat=BLUE, cap=True)
    for sx in (-1, 1):
        hard += p.box((sx * 0.063, 0, top + 0.070), (0.010, 0.090, 0.104), STEEL)

    _readout(p, (0, -0.046, top + 0.082), 0.076, 0.050, hard,
             tilt=math.radians(-8))
    p.cyl((0.000, -0.046, top + 0.020), 0.026, 0.014, 'Y', 20, CHROME)
    p.cyl((0.000, -0.052, top + 0.020), 0.020, 0.008, 'Y', 20, GLASS)
    p.rivets((-0.050, -0.046, top + 0.132), (0.050, -0.046, top + 0.132), 4,
             radius=0.005, height=0.004, axis='Y', mat=DARK)

    # Vent stack down the back — instruments that measure air have to breathe.
    p.louvres((-0.040, 0.040, top + 0.024), (0.040, 0.048, top + 0.118), 6,
              mat=DARK, thickness=0.004)

    # Mast and the cup head.
    mast_z = top + 0.150
    p.cyl((0, 0, mast_z + 0.036), 0.010, 0.072, 'Z', 10, STEEL)
    p.cyl((0, 0, mast_z + 0.074), 0.018, 0.012, 'Z', 12, DARK)
    for i in range(3):
        a = 2 * math.pi * i / 3
        ex, ey = math.cos(a) * 0.052, math.sin(a) * 0.052
        p.sweep([(0, 0, mast_z + 0.078), (ex, ey, mast_z + 0.078)],
                0.0045, CHROME, seg=6)
        # Hemispherical cup, opening tangential to the arm so the head reads
        # as something that would actually spin.
        p.ellipsoid((ex, ey, mast_z + 0.078), (0.019, 0.019, 0.017), CHROME,
                    seg=12, rings=6, t0=-1.0, t1=0.15, cap=True)
    p.cyl((0, 0, mast_z + 0.086), 0.006, 0.020, 'Z', 8, AMBER)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_WeatherStation_Field", coll)


def vane(coll, mats):
    """Wind vane and tail fin on a low drum — the flat-silhouette member."""
    p = Part(mats)
    hard = []
    top = _base_plate(p, 0.066, hard)

    p.cyl((0, 0, top + 0.052), 0.058, 0.104, 'Z', 20, BLUE)
    p.tube((0, 0, top + 0.104), 0.058, 0.008, 0.014, 'Z', 20, STEEL)
    p.tube((0, 0, top + 0.014), 0.060, 0.010, 0.016, 'Z', 20, DARK)
    _readout(p, (0, -0.058, top + 0.058), 0.058, 0.042, hard)
    hard += p.greeble((-0.030, 0.046, top + 0.030), (0.030, 0.052, top + 0.086),
                      5, seed=11, scale=(0.008, 0.018), mat=DARK, flatten='Y')

    # Spindle and vane. Built from boxes on explicit axes rather than `prism`,
    # whose (u, v, w) plane mapping put the fin 4 cm clear of its own boom in
    # the first build.
    sp = top + 0.104
    p.cyl((0, 0, sp + 0.040), 0.008, 0.080, 'Z', 10, STEEL)
    p.cyl((0, 0, sp + 0.078), 0.014, 0.012, 'Z', 12, CHROME)
    hard += p.box((0, 0.006, sp + 0.078), (0.007, 0.132, 0.009), CHROME)
    # Fin: thin in X, swept back so the silhouette is a wedge not a rectangle.
    # axis='X' maps (u, v, w) -> (w, u, v), so u is Y and v is Z.
    hard += p.loft([(-0.0016, [(0.028, sp + 0.058), (0.072, sp + 0.040),
                               (0.072, sp + 0.108), (0.028, sp + 0.098)]),
                    (0.0016, [(0.028, sp + 0.058), (0.072, sp + 0.040),
                              (0.072, sp + 0.108), (0.028, sp + 0.098)])],
                   axis='X', mat=RUST, cap=True)
    hard += p.box((0, 0.030, sp + 0.078), (0.010, 0.030, 0.010), DARK)
    p.cyl((0, -0.056, sp + 0.078), 0.011, 0.024, 'Y', 10, DARK)
    p.cyl((0, 0, sp + 0.086), 0.005, 0.014, 'Z', 8, AMBER)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_WeatherStation_Vane", coll)


def beacon(coll, mats):
    """Caged lightning rod on a squat can — the field-expedient one.

    Deliberately the scruffiest of the three: exposed rod, roll cage, visible
    coil. Gives the set a low-end variant for scattering around a camp.
    """
    p = Part(mats)
    hard = []
    top = _base_plate(p, 0.078, hard, thick=0.014)

    p.cyl((0, 0, top + 0.040), 0.066, 0.080, 'Z', 18, BLUE)
    # Weathering band only — a full-height rust tube turned the whole can into
    # one orange mass and swallowed the coil.
    p.tube((0, 0, top + 0.012), 0.068, 0.007, 0.020, 'Z', 18, RUST)
    p.cyl((0, 0, top + 0.086), 0.058, 0.014, 'Z', 18, DARK)
    _readout(p, (0, -0.066, top + 0.046), 0.050, 0.034, hard)

    # Copper coil wound round the can — the "this makes lightning" read.
    # Verdigris rather than emissive: PALETTE.md lists Copper_Oxide for coil
    # windings, and an emissive coil rendered near-white, its overlapping upper
    # turns reading from above as a solid dome capping the model.
    turns, pts = 4, []
    for i in range(turns * 12 + 1):
        t = i / (turns * 12)
        a = 2 * math.pi * turns * t
        pts.append((math.cos(a) * 0.070, math.sin(a) * 0.070,
                    top + 0.020 + t * 0.052))
    p.sweep(pts, 0.0038, COPPER, seg=6)

    # Roll cage. The bars stop short of the axis and are capped by a separate
    # hub, rather than all converging on one shared point — coincident tube
    # ends are what remove_doubles welds into a blob.
    for i in range(4):
        a = math.pi / 2 * i + math.pi / 4
        x, y = math.cos(a) * 0.050, math.sin(a) * 0.050
        p.sweep([(x, y, top + 0.094), (x * 0.66, y * 0.66, top + 0.140),
                 (x * 0.22, y * 0.22, top + 0.162)], 0.0050, DARK, seg=6)
    p.torus((0, 0, top + 0.126), 0.040, 0.005, 'Z', 16, 6, DARK)
    p.cyl((0, 0, top + 0.166), 0.016, 0.012, 'Z', 12, DARK)

    p.cyl((0, 0, top + 0.132), 0.007, 0.096, 'Z', 10, CHROME)
    p.ellipsoid((0, 0, top + 0.184), (0.013, 0.013, 0.013), CHROME,
                seg=12, rings=6)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_WeatherStation_Beacon", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    field(collection("Coll_WeatherStation_Field"), mats)
    vane(collection("Coll_WeatherStation_Vane"), mats)
    beacon(collection("Coll_WeatherStation_Beacon"), mats)
    save(out)
    report()


main()
