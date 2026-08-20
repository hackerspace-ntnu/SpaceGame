"""components/mechanical/tail_segment — armoured segments for a scorpion tail.

One segment is a structural core with an overlapping dorsal carapace, a split
belly plate, and the actuator that bends the joint at its near end. Segments
chain nose-to-tail: the origin sits on the **near** joint pin, the geometry runs
out along **+Y**, and the carapace deliberately overhangs the far end so that
the next segment tucks underneath it the way a scorpion's tergites lap.

Authoring frame: +Y is "outward along the tail", +Z is the dorsal side. On the
crawler each segment is placed at uniform scale, so the nominal 3.00 m length
and 1.10 m outer radius below are what every placement is a multiple of — a
segment at scale 0.64 is 1.92 m long and 0.70 m across, and nothing has to be
squashed on one axis to fit.

Five variations, differing in silhouette and structure rather than paint:

    Root     widest, twin rams, flared shoulder collar — carries the whole tail
    Heavy    ribbed carapace with a dorsal keel — the workhorse mid-segment
    Vented   louvred flanks over an amber-lit heat core
    Patched  field repair: mismatched plate, strap bands, one rivet row missing
    Slim     minimal armour over an exposed core, for the distal end

    blender --background --python tail_segment.py -- --out tail_segment.blend
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Paint_Hull_Bleached",   # 0
    "Mat_Metal_Steel_Dark",      # 1
    "Mat_Metal_Steel_Worn",      # 2
    "Mat_Paint_Olive_Deep",      # 3
    "Mat_Paint_Roof_Green",      # 4
    "Mat_Metal_Rust_Heavy",      # 5
    "Mat_Paint_Warn_Red",        # 6
    "Mat_Emissive_Amber",        # 7
    "Mat_Neutral_Black_Matte",   # 8
    "Mat_Metal_Chrome_Scuffed",  # 9
    "Mat_Plastic_Rubber_Black",  # 10
    "Mat_Metal_Copper_Oxide",    # 11
]
(HULL, DARK, STEEL, OLIVE, GREEN, RUST, RED, AMBER, BLACK, CHROME, RUBBER,
 COPPER) = range(12)

# Nominal envelope. Every placement scales these uniformly.
LEN = 3.00
R = 1.10                 # outer radius of the carapace at the near end
DORSAL = (16.0, 164.0)   # carapace angular sweep, degrees, 90 = straight up
VENTRAL = (206.0, 334.0)
LAP = 0.34               # how far the carapace overhangs the far end


# ---------------------------------------------------------------------------
# Profile helpers
# ---------------------------------------------------------------------------

def arc(r_out, r_in, a0, a1, n):
    """A closed annular-sector profile in the (x, z) plane.

    Outer edge swept a0 to a1, inner edge swept back — so the result is a band
    of constant thickness, which is what an armour shell actually is. Every
    station of a loft has to carry the same point count, hence the fixed n.
    """
    pts = []
    for i in range(n):
        a = math.radians(a0 + (a1 - a0) * i / (n - 1))
        pts.append((r_out * math.cos(a), r_out * math.sin(a)))
    for i in range(n - 1, -1, -1):
        a = math.radians(a0 + (a1 - a0) * i / (n - 1))
        pts.append((r_in * math.cos(a), r_in * math.sin(a)))
    return pts


def ring(r, n):
    """A closed convex profile — the structural core's cross-section."""
    return [(r * math.cos(2 * math.pi * i / n), r * math.sin(2 * math.pi * i / n))
            for i in range(n)]


def shell(p, stations, span, thick, mat, n=11):
    """Loft an armour band along +Y. `stations` is [(y, outer_radius), ...]."""
    return p.loft([(y, arc(r, r - thick, span[0], span[1], n))
                   for y, r in stations], axis='Y', mat=mat)


def at(angle, r, y):
    """A point on the segment's surface, for hanging detail off."""
    a = math.radians(angle)
    return (r * math.cos(a), y, r * math.sin(a))


# ---------------------------------------------------------------------------
# The parts every segment shares
# ---------------------------------------------------------------------------

def build_core(p, r0, r1):
    """Tapered structural spine — visible in the gaps between armour."""
    p.loft([(0.10, ring(r0 * 0.62, 10)),
            (LEN * 0.45, ring((r0 * 0.62 + r1 * 0.58) / 2, 10)),
            (LEN - 0.06, ring(r1 * 0.58, 10))], axis='Y', mat=DARK)
    # Longitudinal stringers, so the bare core does not read as a plain tube.
    for k in range(6):
        a = 30 + k * 60
        p.seam(at(a, r0 * 0.60, 0.20), at(a, r1 * 0.56, LEN - 0.18),
               width=0.055, depth=0.045, axis='Y', mat=STEEL)


def build_yoke(p, r0, heavy=False):
    """The near-end joint: two cheek plates and the pin they turn on."""
    cheek = r0 * (0.98 if heavy else 0.88)
    for sx in (-1, 1):
        p.box((sx * cheek, 0.30, 0.0), (0.17 if heavy else 0.13, 0.86, r0 * 1.05),
              STEEL)
        p.cyl((sx * cheek, 0.0, 0.0), r0 * 0.34, 0.20 if heavy else 0.15, 'X',
              14, STEEL)
        p.cyl((sx * (cheek + 0.10), 0.0, 0.0), r0 * 0.14, 0.14, 'X', 10, CHROME)
    # The pin itself runs right through.
    p.cyl((0, 0, 0), r0 * 0.15, cheek * 2.3, 'X', 12, DARK)
    # Collar tying the yoke into the core.
    p.tube((0, 0.62, 0), r0 * 0.70, 0.09, 0.26, 'Y', 14, STEEL)


def build_ram(p, r0, r1, offset_x, mat_barrel=DARK):
    """A hydraulic ram slung on the belly, anchored across the joint.

    Anchored *behind* the origin so it visibly crosses the pin it drives —
    a ram that stops at the joint line reads as a pipe, not an actuator.
    """
    y0, y1 = -0.34, LEN * 0.66
    z0, z1 = -r0 * 0.74, -r1 * 0.62
    d = Vector((0.0, y1 - y0, z1 - z0))
    length = d.length
    rot = Matrix.Rotation(math.atan2(d.z, d.y), 4, 'X')
    mid = (0.5 * (y0 + y1), 0.5 * (z0 + z1))
    p.cyl((offset_x, mid[0], mid[1]), r0 * 0.155, length * 0.62, 'Y', 12,
          mat_barrel, rot=rot)
    p.cyl((offset_x, mid[0] + d.y * 0.26, mid[1] + d.z * 0.26), r0 * 0.085,
          length * 0.92, 'Y', 10, CHROME, rot=rot)
    # Eye ends.
    p.cyl((offset_x, y0, z0), r0 * 0.13, 0.16, 'X', 10, STEEL)
    p.cyl((offset_x, y1, z1), r0 * 0.10, 0.13, 'X', 10, STEEL)
    # Feed hose looping off the barrel.
    p.cyl((offset_x + r0 * 0.16, y0 + 0.55, z0 + 0.16), 0.035, 0.80, 'Y', 8,
          RUBBER, rot=Matrix.Rotation(math.radians(-14), 4, 'X'))


def build_loom(p, r0, r1):
    """Cable bundle down the ventral channel, clipped at intervals."""
    for k, (off, mat) in enumerate(((-0.13, COPPER), (0.0, RUBBER),
                                    (0.13, COPPER))):
        z0, z1 = -r0 * 0.50, -r1 * 0.44
        d = Vector((0.0, LEN - 0.30, z1 - z0))
        rot = Matrix.Rotation(math.atan2(d.z, d.y), 4, 'X')
        p.cyl((off, 0.22 + (LEN - 0.30) / 2, (z0 + z1) / 2), 0.042, d.length,
              'Y', 8, mat, rot=rot)
    for k in range(3):
        y = 0.55 + k * (LEN - 1.0) / 2
        t = y / LEN
        p.box((0.0, y, -(r0 * (1 - t) + r1 * t) * 0.50), (0.42, 0.09, 0.10),
              STEEL)


def build_markings(p, r0, r1, tag, lamp=True):
    """Stencils and lamps — the read that says 'maintained by someone'."""
    # Hazard band around the carapace shoulder.
    n = 9
    for i in range(n):
        a = DORSAL[0] + (DORSAL[1] - DORSAL[0]) * (i + 0.5) / n
        if i % 2:
            continue
        pt = at(a, r0 * 1.005, 0.46)
        p.box(pt, (0.19, 0.20, 0.19), RED,
              rot=Matrix.Rotation(math.radians(a - 90), 4, 'Y'))
    # Segment number plate.
    p.box(at(56, r0 * 1.01, LEN * 0.52), (0.30, 0.42, 0.30), OLIVE,
          rot=Matrix.Rotation(math.radians(-34), 4, 'Y'))
    if lamp:
        pt = at(124, r0 * 1.0, LEN * 0.30)
        p.cyl(pt, 0.085, 0.13, 'Z', 10, BLACK,
              rot=Matrix.Rotation(math.radians(34), 4, 'Y'))
        p.cyl((pt[0] * 1.04, pt[1], pt[2] * 1.04), 0.058, 0.10, 'Z', 10, AMBER,
              rot=Matrix.Rotation(math.radians(34), 4, 'Y'))


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def carapace_stations(r0, r1, bulge=1.0):
    """Outer radius along the segment — swollen slightly at mid-span so the
    silhouette is a muscle rather than a cone, then lapping past the far end."""
    return [(-0.02, r0 * 0.94),
            (LEN * 0.22, r0 * bulge),
            (LEN * 0.62, (r0 * 0.45 + r1 * 0.55) * bulge),
            (LEN + LAP, r1 * 0.93)]


def variant_root(coll):
    r0, r1 = R, R * 0.93
    p = Part(PALETTE)
    build_core(p, r0, r1)
    build_yoke(p, r0, heavy=True)
    shell(p, carapace_stations(r0, r1, 1.02), DORSAL, 0.13, HULL)
    shell(p, carapace_stations(r0 * 0.96, r1 * 0.96), VENTRAL, 0.10, OLIVE)
    # Flared shoulder collar — this segment carries the whole tail, and it
    # should look like it does.
    p.loft([(0.18, arc(r0 * 1.00, r0 * 0.86, 0, 360, 15)),
            (0.52, arc(r0 * 1.26, r0 * 1.06, 0, 360, 15)),
            (0.86, arc(r0 * 1.10, r0 * 0.94, 0, 360, 15))], axis='Y', mat=GREEN)
    for k in range(8):
        a = k * 45 + 22
        p.box(at(a, r0 * 1.20, 0.52), (0.16, 0.30, 0.16), STEEL,
              rot=Matrix.Rotation(math.radians(a - 90), 4, 'Y'))
    build_ram(p, r0, r1, -r0 * 0.46)
    build_ram(p, r0, r1, r0 * 0.46)
    build_loom(p, r0, r1)
    build_markings(p, r0, r1, "01")
    p.rivets(at(DORSAL[0] + 4, r0 * 1.01, 0.30),
             at(DORSAL[0] + 4, r1 * 1.01, LEN - 0.10), 9, 0.032, 0.024, 'Y', STEEL)
    p.rivets(at(DORSAL[1] - 4, r0 * 1.01, 0.30),
             at(DORSAL[1] - 4, r1 * 1.01, LEN - 0.10), 9, 0.032, 0.024, 'Y', STEEL)
    p.bevel(width=0.016, segments=2)
    return p.finish("Mesh_TailSeg_Root", coll)


def variant_heavy(coll):
    r0, r1 = R * 0.98, R * 0.90
    p = Part(PALETTE)
    build_core(p, r0, r1)
    build_yoke(p, r0)
    shell(p, carapace_stations(r0, r1), DORSAL, 0.12, HULL)
    shell(p, carapace_stations(r0 * 0.95, r1 * 0.95), VENTRAL, 0.09, OLIVE)
    # Transverse ribs plus a dorsal keel — the workhorse silhouette.
    for k in range(4):
        y = 0.42 + k * (LEN - 0.95) / 3
        t = y / LEN
        rr = (r0 * (1 - t) + r1 * t) * 1.03
        p.loft([(y - 0.07, arc(rr, rr - 0.17, DORSAL[0] - 2, DORSAL[1] + 2, 11)),
                (y + 0.07, arc(rr, rr - 0.17, DORSAL[0] - 2, DORSAL[1] + 2, 11))],
               axis='Y', mat=GREEN)
    keel = [(y, (r0 * (1 - y / LEN) + r1 * (y / LEN)) * 1.13)
            for y in (0.30, LEN * 0.5, LEN + LAP * 0.5)]
    p.loft([(y, arc(rr, rr - 0.20, 78, 102, 5)) for y, rr in keel], axis='Y',
           mat=STEEL)
    build_ram(p, r0, r1, -r0 * 0.42)
    build_loom(p, r0, r1)
    build_markings(p, r0, r1, "02", lamp=False)
    p.bevel(width=0.015, segments=2)
    return p.finish("Mesh_TailSeg_Heavy", coll)


def variant_vented(coll):
    r0, r1 = R * 0.95, R * 0.86
    p = Part(PALETTE)
    build_core(p, r0, r1)
    build_yoke(p, r0)
    shell(p, carapace_stations(r0, r1), DORSAL, 0.12, HULL)
    shell(p, carapace_stations(r0 * 0.95, r1 * 0.95), VENTRAL, 0.09, OLIVE)
    # Heat core glowing through louvred flanks on both sides.
    for sx in (-1, 1):
        p.cyl((sx * r0 * 0.40, LEN * 0.48, 0.0), r0 * 0.30, LEN * 0.52, 'Y', 12,
              AMBER)
        p.box((sx * r0 * 0.80, LEN * 0.48, 0.0), (0.20, LEN * 0.60, r0 * 0.86),
              BLACK)
        p.louvres((sx * r0 * 0.86 - 0.09, LEN * 0.20, -r0 * 0.40),
                  (sx * r0 * 0.86 + 0.09, LEN * 0.76, r0 * 0.40), 6, 'Y', GREEN)
        # Frame around the opening.
        for dz in (-r0 * 0.46, r0 * 0.46):
            p.box((sx * r0 * 0.86, LEN * 0.48, dz), (0.13, LEN * 0.64, 0.10),
                  STEEL)
    # Exhaust stub on the dorsal shoulder.
    p.cyl(at(74, r0 * 1.06, LEN * 0.80), 0.15, 0.34, 'Z', 10, RUST,
          rot=Matrix.Rotation(math.radians(16), 4, 'Y'))
    build_ram(p, r0, r1, r0 * 0.42)
    build_loom(p, r0, r1)
    build_markings(p, r0, r1, "03")
    p.bevel(width=0.014, segments=2)
    return p.finish("Mesh_TailSeg_Vented", coll)


def variant_patched(coll):
    r0, r1 = R * 0.92, R * 0.83
    p = Part(PALETTE)
    build_core(p, r0, r1)
    build_yoke(p, r0)
    # The carapace is short — a chunk of it is simply gone, replaced by a
    # mismatched plate strapped over the hole.
    p.loft([(-0.02, arc(r0 * 0.94, r0 * 0.94 - 0.11, *DORSAL, 11)),
            (LEN * 0.44, arc(r0 * 0.99, r0 * 0.99 - 0.11, *DORSAL, 11))],
           axis='Y', mat=HULL)
    p.loft([(LEN * 0.50, arc(r0 * 0.97, r0 * 0.97 - 0.10, 30, 150, 9)),
            (LEN + LAP, arc(r1 * 0.93, r1 * 0.93 - 0.10, 30, 150, 9))],
           axis='Y', mat=RUST)
    shell(p, carapace_stations(r0 * 0.95, r1 * 0.95), VENTRAL, 0.09, GREEN)
    # Strap bands holding the repair on.
    for y in (LEN * 0.47, LEN * 0.86):
        t = y / LEN
        rr = (r0 * (1 - t) + r1 * t) * 1.05
        p.loft([(y - 0.06, arc(rr, rr - 0.09, 0, 360, 15)),
                (y + 0.06, arc(rr, rr - 0.09, 0, 360, 15))], axis='Y', mat=STEEL)
        p.box(at(90, rr * 1.02, y), (0.26, 0.20, 0.16), CHROME)
    # Weld beads and a scab of plate over the shoulder.
    p.box(at(48, r0 * 1.02, LEN * 0.36), (0.44, 0.62, 0.30), RUST,
          rot=Matrix.Rotation(math.radians(-42), 4, 'Y'))
    for k in range(5):
        p.seam(at(30 + k * 6, r0 * 1.03, LEN * 0.20),
               at(36 + k * 6, r0 * 1.03, LEN * 0.62),
               width=0.05, depth=0.035, axis='Y', mat=RUST)
    p.greeble((-r0 * 0.5, LEN * 0.30, r0 * 0.55), (r0 * 0.5, LEN * 0.90, r0 * 0.80),
              7, seed=4, scale=(0.07, 0.20), mat=STEEL)
    build_ram(p, r0, r1, -r0 * 0.44, mat_barrel=RUST)
    build_loom(p, r0, r1)
    build_markings(p, r0, r1, "04")
    p.bevel(width=0.013, segments=2)
    return p.finish("Mesh_TailSeg_Patched", coll)


def variant_slim(coll):
    r0, r1 = R * 0.88, R * 0.76
    p = Part(PALETTE)
    build_core(p, r0 * 1.08, r1 * 1.08)
    build_yoke(p, r0)
    # Armour only over the top third; the core is deliberately bare here so the
    # tip reads as mechanism rather than shell.
    p.loft([(0.16, arc(r0 * 0.92, r0 * 0.92 - 0.10, 44, 136, 9)),
            (LEN * 0.55, arc(r0 * 0.90, r0 * 0.90 - 0.10, 44, 136, 9)),
            (LEN + LAP * 0.6, arc(r1 * 0.88, r1 * 0.88 - 0.10, 44, 136, 9))],
           axis='Y', mat=HULL)
    for sx in (-1, 1):
        p.box((sx * r0 * 0.66, LEN * 0.52, 0.0),
              (0.10, LEN * 0.80, r0 * 0.52), OLIVE)
    build_ram(p, r0, r1, 0.0)
    build_loom(p, r0 * 0.9, r1 * 0.9)
    build_markings(p, r0, r1, "05", lamp=True)
    p.rivets(at(50, r0 * 0.95, 0.30), at(50, r1 * 0.92, LEN - 0.10), 8,
             0.026, 0.020, 'Y', STEEL)
    p.bevel(width=0.012, segments=2)
    return p.finish("Mesh_TailSeg_Slim", coll)


# ---------------------------------------------------------------------------

def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    for name, fn in (("Root", variant_root), ("Heavy", variant_heavy),
                     ("Vented", variant_vented), ("Patched", variant_patched),
                     ("Slim", variant_slim)):
        fn(collection("Coll_TailSeg_%s" % name))

    print("\nTail segment variations:")
    report()
    save(out)


build()
