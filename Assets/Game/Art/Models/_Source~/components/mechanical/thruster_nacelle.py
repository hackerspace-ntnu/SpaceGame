"""Engine pods for the RV ship.

All four variations share one language — a lofted cowl, a segmented shroud, an
exhaust bell with internal vanes — at wildly different scales, which is what
makes a 2.8 m wing pod and a 0.3 m attitude jet look like they came off the same
production line.

Axis runs along X: intake at +X, exhaust at -X, matching the ship's nose-forward
authoring frame. Origin sits at each pod's actual mounting point, so placement
is a single translation to where the mount is.

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

SLATE, STEEL, DARK, RUST, BLACK, AMBER, COPPER = 0, 1, 2, 3, 4, 5, 6
MATS = ["Mat_Neutral_Slate_Dark", "Mat_Metal_Steel_Worn",
        "Mat_Metal_Steel_Dark", "Mat_Metal_Rust_Heavy",
        "Mat_Neutral_Black_Matte", "Mat_Emissive_Amber",
        "Mat_Metal_Copper_Oxide"]


def ring(r, n=20, squash=1.0):
    """A circular profile in the plane perpendicular to the pod axis."""
    return [(r * math.cos(2 * math.pi * i / n),
             r * squash * math.sin(2 * math.pi * i / n)) for i in range(n)]


def turbine(p, x, r, blades=11, mat=DARK):
    """Compressor face — a hub and a ring of angled blades, visible down the
    intake. Cheap, and it is what stops an intake reading as a painted hole."""
    p.cyl((x, 0, 0), r * 0.30, 0.14, 'X', 12, mat)
    for i in range(blades):
        a = 2 * math.pi * i / blades
        rot = (Matrix.Rotation(a, 4, 'X')
               @ Matrix.Rotation(math.radians(32), 4, 'Z'))
        p.box((x, math.cos(a) * r * 0.62, math.sin(a) * r * 0.62),
              (0.05, r * 0.62, 0.10), mat, rot=rot)


def bell(p, x, r_in, r_out, length, mat=DARK, vanes=8):
    """Exhaust nozzle: flared shroud, inner cone, radial vanes, hot core."""
    p.loft([(x, ring(r_in, 20)),
            (x - length * 0.55, ring(r_in * 1.04, 20)),
            (x - length, ring(r_out, 20))], 'X', mat, cap=False)
    p.tube((x - length + 0.02, 0, 0), r_out, 0.06, 0.08, 'X', 20, STEEL)
    for i in range(vanes):
        a = 2 * math.pi * i / vanes
        p.box((x - length * 0.5, math.cos(a) * r_in * 0.72,
               math.sin(a) * r_in * 0.72),
              (length * 0.8, r_in * 0.5, 0.045), mat,
              rot=Matrix.Rotation(a, 4, 'X'))
    p.cyl((x - length * 0.35, 0, 0), r_in * 0.34, length * 0.7, 'X', 14, BLACK,
          radius_top=r_in * 0.10)
    p.cyl((x - length * 0.92, 0, 0), r_out * 0.80, 0.05, 'X', 20, AMBER)


def main_pod(coll, mats):
    """The wing-mounted mains. 2.84 m long, 2.00 m across — the dimensions the
    existing ship already flies with."""
    p = Part(mats)
    L, R = 2.84, 1.00
    nose, tail = L / 2, -L / 2

    # Cowl: fat amidships, pinched at both ends.
    p.loft([(tail + 0.30, ring(R * 0.80, 24)),
            (tail + 0.95, ring(R * 0.99, 24)),
            (0.10, ring(R, 24)),
            (nose - 0.55, ring(R * 0.94, 24)),
            (nose - 0.12, ring(R * 0.80, 24))], 'X', SLATE, cap=False)

    # Intake lip and compressor.
    p.tube((nose - 0.12, 0, 0), R * 0.80, 0.10, 0.16, 'X', 24, STEEL)
    turbine(p, nose - 0.42, R * 0.72)
    p.loft([(nose - 0.12, ring(R * 0.72, 20)),
            (nose - 0.62, ring(R * 0.66, 20))], 'X', BLACK, cap=False)

    # Shroud split into three bands, so the pod is not one smooth sausage.
    for x in (tail + 0.95, 0.10, nose - 0.55):
        p.tube((x, 0, 0), R * 1.02, 0.05, 0.09, 'X', 24, STEEL)
    # Longitudinal spine fairings.
    for a_deg in (35, 145, 215, 325):
        a = math.radians(a_deg)
        p.box((0.05, math.cos(a) * R * 0.99, math.sin(a) * R * 0.99),
              (L * 0.62, 0.20, 0.09), STEEL, rot=Matrix.Rotation(a, 4, 'X'))

    bell(p, tail + 0.30, R * 0.78, R * 0.92, 0.62)

    # Mount saddle on top — this is what the wing spar clamps to.
    p.slab((-0.62, -0.34, R * 0.86), (0.62, 0.34, R + 0.20), DARK)
    p.rivets((-0.50, -0.26, R + 0.20), (0.50, -0.26, R + 0.20), 6,
             radius=0.03, height=0.03, mat=STEEL)
    p.rivets((-0.50, 0.26, R + 0.20), (0.50, 0.26, R + 0.20), 6,
             radius=0.03, height=0.03, mat=STEEL)

    # Plumbing and clutter down the flank: the rundown read.
    p.cyl((0.10, -R * 0.55, R * 0.80), 0.055, 1.70, 'X', 8, COPPER)
    p.cyl((0.10, R * 0.55, R * 0.80), 0.055, 1.70, 'X', 8, COPPER)
    p.greeble((-0.9, -R * 0.55, R * 0.72), (0.9, R * 0.55, R * 0.86), 14,
              seed=11, scale=(0.07, 0.20), mat=DARK)
    # Rust bleeding back from the exhaust.
    p.tube((tail + 0.52, 0, 0), R * 0.86, 0.04, 0.30, 'X', 24, RUST)

    p.bevel(width=0.014, segments=2)
    # Top mount face is the connection point.
    return p.finish("Mesh_Thruster_Main", coll, origin=(0, 0, R + 0.20))


def tail_pod(coll, mats):
    """The stern main drive, sunk into the back of the hull. 3.37 x 3.18 x 2.49
    — a slab-sided block rather than a tube, so the ship's rear silhouette stays
    boxy and RV-ish instead of turning into a fighter."""
    p = Part(mats)
    L, HY, HZ = 3.37, 1.59, 1.245
    nose, tail = L / 2, -L / 2

    # Chamfered box body, tapering aft.
    def slabprof(hy, hz, ch):
        return [(-hy + ch, -hz), (hy - ch, -hz), (hy, -hz + ch),
                (hy, hz - ch), (hy - ch, hz), (-hy + ch, hz),
                (-hy, hz - ch), (-hy, -hz + ch)]

    p.loft([(nose, slabprof(HY * 0.92, HZ * 0.92, 0.30)),
            (nose - 0.70, slabprof(HY, HZ, 0.34)),
            (tail + 0.85, slabprof(HY, HZ, 0.34)),
            (tail + 0.30, slabprof(HY * 0.88, HZ * 0.86, 0.30))],
           'X', SLATE, cap=True)

    # Twin exhaust bells side by side. Built inline rather than through bell(),
    # which only draws on the axis.
    for s in (-1, 1):
        y = s * 0.74
        p.loft([(tail + 0.32, [(y + u, v) for u, v in ring(0.58, 18)]),
                (tail - 0.10, [(y + u, v) for u, v in ring(0.62, 18)]),
                (tail - 0.42, [(y + u, v) for u, v in ring(0.76, 18)])],
               'X', DARK, cap=False)
        p.tube((tail - 0.40, y, 0), 0.76, 0.06, 0.09, 'X', 18, STEEL)
        p.cyl((tail - 0.30, y, 0), 0.60, 0.06, 'X', 18, AMBER)
        p.cyl((tail + 0.05, y, 0), 0.22, 0.60, 'X', 12, BLACK)

    # Radiator fins along the top — the heat-rejection read.
    for i in range(7):
        x = -1.25 + i * 0.40
        p.slab((x, -HY * 0.78, HZ), (x + 0.14, HY * 0.78, HZ + 0.34), STEEL)
    # Access panels and pipework on the flanks.
    for s in (-1, 1):
        p.slab((-0.9, s * HY, -0.5), (0.5, s * (HY + 0.07), 0.62), DARK)
        p.rivets((-0.82, s * (HY + 0.07), -0.42),
                 (0.42, s * (HY + 0.07), -0.42), 8, radius=0.026,
                 height=0.022, axis='Y', mat=STEEL)
        p.cyl((0.2, s * (HY + 0.10), -0.85), 0.09, 2.0, 'X', 8, COPPER)
    p.greeble((-1.3, -HY * 0.7, HZ + 0.34), (1.2, HY * 0.7, HZ + 0.42), 16,
              seed=7, scale=(0.10, 0.26), mat=DARK)
    # Scorching around the nozzles.
    p.tube((tail + 0.20, 0.74, 0), 0.92, 0.05, 0.24, 'X', 18, RUST)
    p.tube((tail + 0.20, -0.74, 0), 0.92, 0.05, 0.24, 'X', 18, RUST)

    p.bevel(width=0.016, segments=2)
    # Origin at the forward face, where it butts into the hull.
    return p.finish("Mesh_Thruster_Tail", coll, origin=(nose, 0, 0))


def maneuver_cluster(coll, mats):
    """Four-nozzle attitude jet in a recessed housing. Mounts flush to the
    hull skin, thrust outward along +Z. Origin on the skin plane."""
    p = Part(mats)
    p.tube((0, 0, 0.05), 0.30, 0.07, 0.10, 'Z', 16, STEEL)
    p.slab((-0.24, -0.24, -0.10), (0.24, 0.24, 0.04), BLACK)
    for sx in (-1, 1):
        for sy in (-1, 1):
            c = (sx * 0.115, sy * 0.115)
            p.cyl((c[0], c[1], 0.10), 0.075, 0.16, 'Z', 10, DARK,
                  radius_top=0.098)
            p.cyl((c[0], c[1], 0.02), 0.05, 0.06, 'Z', 8, AMBER)
    p.rivets((-0.26, -0.26, 0.06), (0.26, -0.26, 0.06), 4, radius=0.018,
             height=0.014, mat=DARK)
    p.rivets((-0.26, 0.26, 0.06), (0.26, 0.26, 0.06), 4, radius=0.018,
             height=0.014, mat=DARK)
    p.bevel(width=0.006, segments=2)
    return p.finish("Mesh_Thruster_Maneuver", coll)


def vernier(coll, mats):
    """A single steering nozzle on a short stalk — bolted on wherever the ship
    needs one more, which is exactly how a ship like this gets built."""
    p = Part(mats)
    p.cyl((0, 0, 0.03), 0.11, 0.06, 'Z', 12, STEEL)
    p.cyl((0, 0, 0.14), 0.055, 0.20, 'Z', 10, DARK)
    p.loft([(0.20, ring(0.055, 14)), (0.30, ring(0.075, 14)),
            (0.40, ring(0.13, 14))], 'Z', DARK, cap=False)
    p.tube((0.0, 0, 0.40), 0.13, 0.025, 0.03, 'Z', 14, STEEL)
    p.cyl((0, 0, 0.24), 0.05, 0.04, 'Z', 10, AMBER)
    p.cyl((0.09, 0, 0.12), 0.022, 0.16, 'X', 6, COPPER)
    p.bevel(width=0.005, segments=2)
    return p.finish("Mesh_Thruster_Vernier", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Main", main_pod), ("Tail", tail_pod),
                     ("Maneuver", maneuver_cluster), ("Vernier", vernier)):
        fn(collection("Coll_Thruster_" + name), mats)

    report()
    save(out)


main()
