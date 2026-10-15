"""components/structural/hull_door — doors, hatches and ramps for the sand crawler.

Every opening a crew member passes through on the tracked land-ship. Each
variation is a FIXED part (frame, coaming, hinge mount) and a MOVING part (leaf,
lid, ramp) as separate objects, and every moving part has its origin ON ITS
HINGE AXIS, so opening it is a single rotation of the object:

    Door_Bulkhead / Door_Boarding   leaf rotates about +Z; NEGATIVE angles swing
                                    the free edge toward -Y (out of the frame's
                                    front face). The frame blocks +Y.
    Hatch_Roof                      lid rotates about +X; NEGATIVE angles lift
                                    the -Y edge and fold the lid back over +Y.
    Ramp_Cargo                      ramp rotates about +X; +90 stands it up
                                    closed, small negative angles lower the toe.

Doors are built standing up, depth along Y, front face toward -Y. Frame origin
is on the floor at the centre of the opening (z=0 is the deck). Openings are
sized for the 3 m player: clear 2.2 x 3.4 m above a 0.15 m sill.

    blender --background --python hull_door.py -- --out hull_door.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

import bmesh
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Metal_HullRust_Orange",   # 0 leaves, lids, ramp deck
    "Mat_Metal_Steel_Worn",        # 1 frames, coaming
    "Mat_Metal_Steel_Dark",        # 2 hinges, dogs, handles
    "Mat_Metal_Rust_Heavy",        # 3 armour plate
    "Mat_Metal_Rust_Deep",         # 4 rivets, bolt heads
    "Mat_Glass_Canopy_Tinted",     # 5 porthole and slit glass
    "Mat_Plastic_Rubber_Black",    # 6 gaskets, grips
    "Mat_Metal_Brass_Tarnished",   # 7 porthole bezel
    "Mat_Metal_Rust_Pale",         # 8 anti-slip ribs, scuffed wear
]
HULL, STEEL, DARK, RUST, DEEP, GLASS, RUBBER, BRASS, PALE = range(9)

SILL = 0.15          # height of the opening's bottom edge above the deck
CLEAR_W = 2.2
CLEAR_H = 3.4


# --------------------------------------------------------------------------
# Plate-with-hole: the one shape a door is made of
# --------------------------------------------------------------------------

def rounded_rect(x0, x1, z0, z1, r, seg=3):
    """Counter-clockwise outline in (x, z); `seg` points per quarter corner."""
    pts = []
    corners = ((x1 - r, z0 + r, -90), (x1 - r, z1 - r, 0),
               (x0 + r, z1 - r, 90), (x0 + r, z0 + r, 180))
    for cx, cz, a0 in corners:
        for i in range(seg + 1):
            a = math.radians(a0 + 90.0 * i / seg)
            pts.append((cx + r * math.cos(a), cz + r * math.sin(a)))
    return pts


def circle(cx, cz, r, n):
    return [(cx + r * math.cos(2 * math.pi * i / n),
             cz + r * math.sin(2 * math.pi * i / n)) for i in range(n)]


def stadium(cx, cz, half_len, r, seg=4):
    """A slot: two half-circles joined by straights, long axis along X."""
    pts = []
    for i in range(seg + 1):
        a = math.radians(-90 + 180.0 * i / seg)
        pts.append((cx + half_len + r * math.cos(a), cz + r * math.sin(a)))
    for i in range(seg + 1):
        a = math.radians(90 + 180.0 * i / seg)
        pts.append((cx - half_len + r * math.cos(a), cz + r * math.sin(a)))
    return pts


def _ray_hit(poly, c, ang):
    """Nearest point where a ray from `c` at `ang` leaves the closed `poly`."""
    d = (math.cos(ang), math.sin(ang))
    best = None
    for (ax, az), (bx, bz) in zip(poly, poly[1:] + poly[:1]):
        ex, ez = bx - ax, bz - az
        den = d[0] * ez - d[1] * ex
        if abs(den) < 1e-12:
            continue
        t = ((ax - c[0]) * ez - (az - c[1]) * ex) / den
        s = ((ax - c[0]) * d[1] - (az - c[1]) * d[0]) / den
        if t > 1e-9 and -1e-9 <= s <= 1 + 1e-9 and (best is None or t < best):
            best = t
    return (c[0] + d[0] * best, c[1] + d[1] * best)


def holed_plate(p, outer, hole, centre, y0, y1, mat):
    """A slab in the XZ plane between y0 and y1, pierced by `hole`.

    Both outlines are resampled on one shared fan of rays from `centre`, taken
    through every vertex of either outline, so corners stay sharp and the two
    loops pair up one-to-one into quads. Both outlines must be star-shaped
    from `centre` — true of every opening on this ship.
    """
    angs = sorted({round(math.atan2(z - centre[1], x - centre[0]) % (2 * math.pi), 5)
                   for x, z in outer + hole})
    o = [_ray_hit(outer, centre, a) for a in angs]
    i = [_ray_hit(hole, centre, a) for a in angs]
    bm2 = bmesh.new()
    ring = {k: [bm2.verts.new((x, y, z)) for x, z in pts]
            for k, (pts, y) in {"o0": (o, y0), "o1": (o, y1),
                                "i0": (i, y0), "i1": (i, y1)}.items()}
    n = len(angs)
    for a in range(n):
        b = (a + 1) % n
        bm2.faces.new((ring["o0"][a], ring["o0"][b], ring["i0"][b], ring["i0"][a]))
        bm2.faces.new((ring["o1"][a], ring["i1"][a], ring["i1"][b], ring["o1"][b]))
        bm2.faces.new((ring["o0"][a], ring["o1"][a], ring["o1"][b], ring["o0"][b]))
        bm2.faces.new((ring["i0"][a], ring["i0"][b], ring["i1"][b], ring["i1"][a]))
    return p._absorb(bm2, mat)


def lathe(p, profile, seg, mat):
    """Revolve an (r, z) polyline about Z. Points with r == 0 become poles, so
    a lid closes at its centre with a triangle fan instead of a degenerate ring.
    A profile with no poles is treated as closed."""
    bm2 = bmesh.new()
    rings = []
    for r, z in profile:
        if r < 1e-6:
            rings.append([bm2.verts.new((0, 0, z))] * seg)
        else:
            rings.append([bm2.verts.new((r * math.cos(2 * math.pi * k / seg),
                                         r * math.sin(2 * math.pi * k / seg), z))
                          for k in range(seg)])
    closed = all(r > 1e-6 for r, _ in profile)
    pairs = list(zip(rings, rings[1:])) + ([(rings[-1], rings[0])] if closed else [])
    for a, b in pairs:
        for k in range(seg):
            q = (a[k], a[(k + 1) % seg], b[(k + 1) % seg], b[k])
            uniq = list(dict.fromkeys(q))
            if len(uniq) >= 3:
                bm2.faces.new(uniq)
    faces = p._absorb(bm2, mat)
    for f in faces:
        f.smooth = abs(f.normal.z) < 0.9
    return faces


def bolt(p, pos, axis, r=0.022, h=0.024, mat=DEEP):
    """Square-headed bolt: four sides keep a row of forty affordable."""
    return p.cyl(pos, r, h, axis, seg=4, mat=mat, radius_top=r * 0.65)


def bolt_row(p, a, b, count, axis, **kw):
    a, b = Vector(a), Vector(b)
    for k in range(count):
        bolt(p, a.lerp(b, (k + 0.5) / count), axis, **kw)


# --------------------------------------------------------------------------
# Doors
# --------------------------------------------------------------------------

def door_frame(half_w, height, depth, corner_r, knuckles, hinge):
    """The fixed surround. `knuckles` are (z0, z1) spans of the frame's own
    hinge barrels, interleaved with the leaf's; `hinge` is (x, y) of the axis."""
    p = Part(MATS)
    opening = rounded_rect(-CLEAR_W / 2, CLEAR_W / 2, SILL, SILL + CLEAR_H,
                           corner_r)
    outer = [(-half_w, 0.0), (half_w, 0.0), (half_w, height), (-half_w, height)]
    holed_plate(p, outer, opening, (0.0, SILL + CLEAR_H / 2),
                -depth / 2, depth / 2, STEEL)
    hx, hy = hinge
    for z0, z1 in knuckles:
        zc, zl = (z0 + z1) / 2, z1 - z0
        p.cyl((hx, hy, zc), 0.055, zl, 'Z', 6, DARK)
        # Bracket from barrel into the frame face, embedded 10 mm.
        p.slab((hx - 0.04, hy, z0 + 0.02), (hx + 0.04, -depth / 2 + 0.01, z1 - 0.02), DARK)
    return p


def door_leaf_hinges(p, knuckles, hinge, leaf_x0, face_y, strap_len):
    """Leaf-side barrels on the axis and straps across the leaf's face."""
    hx, hy = hinge
    for z0, z1 in knuckles:
        zc, zl = (z0 + z1) / 2, z1 - z0
        p.cyl((hx, hy, zc), 0.055, zl, 'Z', 6, DARK)
        p.slab((hx, face_y - 0.025, zc - zl * 0.4),
               (leaf_x0 + strap_len, hy, zc + zl * 0.4), DARK)


def build_bulkhead(coll):
    """Interior pressure door: round-cornered, a porthole to see who is coming,
    three dogs down the free edge. Frame 2.8 x 4.0 x 0.3."""
    depth = 0.30
    hinge = (-1.30, -0.23)
    frame_kn = [(0.905, 1.20), (3.205, 3.50)]
    leaf_kn = [(0.60, 0.90), (2.90, 3.20)]

    f = door_frame(1.40, 4.00, depth, 0.35,
                   frame_kn, hinge)
    # Dog wedges on the frame face that the leaf's levers bear against.
    for z in (0.9, 1.85, 2.8):
        f.slab((1.25, -0.34, z - 0.06), (1.36, -depth / 2 + 0.01, z + 0.06), DARK)
    f.finish("Mesh_Door_Bulkhead_Frame", coll)

    # Leaf: 0.12 thick, 20 mm proud of the frame face, overlapping the opening.
    y0, y1 = -0.29, -0.17
    lx0, lx1, lz0, lz1 = -1.22, 1.22, SILL - 0.08, SILL + CLEAR_H + 0.08
    port_c, port_r = (0.25, 2.75), 0.26
    p = Part(MATS)
    holed_plate(p, rounded_rect(lx0, lx1, lz0, lz1, 0.45, 2),
                circle(*port_c, port_r, 10), port_c, y0, y1, HULL)
    # Glass inside the bore at mid-thickness, rim buried 5 mm in the steel.
    p.cyl((port_c[0], (y0 + y1) / 2, port_c[1]), port_r + 0.005, 0.02, 'Y', 10, GLASS)
    # Stiffener bars across the front face: what makes it a ship's door.
    for z in (0.75, 1.9):
        p.slab((lx0 + 0.2, y0 - 0.03, z - 0.07), (lx1 - 0.35, y0 + 0.01, z + 0.07), STEEL)
    # Dogs: hub on the face, lever reaching over the edge to its frame wedge.
    for z in (0.9, 1.85, 2.8):
        p.cyl((1.05, y0 - 0.025, z), 0.05, 0.06, 'Y', 6, DARK)
        p.slab((1.02, -0.37, z - 0.03), (1.30, -0.34, z + 0.03), DARK)
    # Pull handle for the crew: one bar on two stand-offs.
    p.slab((0.62, y0 - 0.16, 1.35), (0.68, y0 - 0.12, 1.75), RUBBER)
    for z in (1.40, 1.70):
        p.slab((0.62, y0 - 0.13, z - 0.03), (0.68, y0 + 0.01, z + 0.03), DARK)
    door_leaf_hinges(p, leaf_kn, hinge, lx0, y0, 0.55)
    p.finish("Mesh_Door_Bulkhead_Leaf", coll, origin=(hinge[0], hinge[1], 0.0))


def build_boarding(coll):
    """Exterior boarding door. Same opening, everything heavier: a 0.4 deep
    frame with a drip hood, a 0.14 leaf behind riveted armour, a vision slit
    instead of a porthole, three strap hinges."""
    depth = 0.40
    hinge = (-1.34, -0.29)
    frame_kn = [(0.555, 0.80), (2.005, 2.25), (3.455, 3.70)]
    leaf_kn = [(0.30, 0.55), (1.75, 2.00), (3.20, 3.45)]

    f = door_frame(1.50, 4.20, depth, 0.25,
                   frame_kn, hinge)
    # Drip hood over the lintel: sand and rain shed off the doorway.
    f.prism([(-depth / 2 + 0.01, 3.80), (-depth / 2 - 0.30, 3.80),
             (-depth / 2 + 0.01, 4.10)], 3.0, 'X', STEEL)
    bolt_row(f, (1.36, -depth / 2 - 0.01, 0.3), (1.36, -depth / 2 - 0.01, 3.7), 3, 'Y')
    f.finish("Mesh_Door_Boarding_Frame", coll)

    y0, y1 = -0.36, -0.22
    lx0, lx1, lz0, lz1 = -1.24, 1.24, SILL - 0.08, SILL + CLEAR_H + 0.08
    slit_c = (0.0, 2.80)
    p = Part(MATS)
    holed_plate(p, rounded_rect(lx0, lx1, lz0, lz1, 0.30, 2),
                stadium(*slit_c, 0.40, 0.06, 2), slit_c, y0, y1, HULL)
    p.slab((-0.47, (y0 + y1) / 2 - 0.01, 2.735), (0.47, (y0 + y1) / 2 + 0.01, 2.865), GLASS)
    # Armour: two riveted plates either side of the slit, 30 mm, embedded 5 mm.
    ay0, ay1 = y0 - 0.03, y0 + 0.005
    p.slab((lx0 + 0.16, ay0, lz0 + 0.12), (lx1 - 0.12, ay1, 2.62), RUST)
    p.slab((lx0 + 0.16, ay0, 2.98), (lx1 - 0.12, ay1, lz1 - 0.14), RUST)
    # Visor lip over the slit.
    p.prism([(ay0 + 0.005, 2.93), (ay0 - 0.16, 2.93), (ay0 + 0.005, 3.05)], 1.0, 'X', DARK)
    for z in (lz0 + 0.24, 2.50):
        bolt_row(p, (lx0 + 0.30, ay0 - 0.005, z), (lx1 - 0.25, ay0 - 0.005, z), 3, 'Y')
    bolt_row(p, (lx0 + 0.30, ay0 - 0.005, 3.30), (lx1 - 0.25, ay0 - 0.005, 3.30), 2, 'Y')
    # Locking bar across the free edge.
    p.slab((0.70, ay0 - 0.06, 1.45), (1.18, ay0 + 0.01, 1.60), DARK)
    door_leaf_hinges(p, leaf_kn, hinge, lx0, ay0, 0.75)
    p.finish("Mesh_Door_Boarding_Leaf", coll, origin=(hinge[0], hinge[1], 0.0))


# --------------------------------------------------------------------------
# Roof hatch
# --------------------------------------------------------------------------

def build_hatch(coll):
    """Round roof hatch, clear 1.8 m, on a 0.4 m coaming. Origin at the centre
    of the coaming's foot, on the roof plate."""
    seg = 12
    top = 0.40
    hinge = (0.0, 1.10, top + 0.045)
    c = Part(MATS)
    lathe(c, [(0.90, 0.0), (1.15, 0.0), (1.15, 0.04), (0.98, 0.07),
              (0.98, top), (0.90, top)], seg, STEEL)
    for x in (-0.55, 0.55):
        c.cyl((x, hinge[1], hinge[2]), 0.05, 0.18, 'X', 6, DARK)
        c.slab((x - 0.08, 0.78, 0.10), (x + 0.08, hinge[1], top), DARK)
    c.finish("Mesh_Hatch_Roof_Coaming", coll)

    p = Part(MATS)
    lathe(p, [(0.0, top + 0.005), (1.02, top + 0.005), (1.02, top + 0.075),
              (0.85, top + 0.11), (0.0, top + 0.13)], seg, HULL)
    # Handwheel on a short stem.
    wz = top + 0.30
    p.cyl((0, 0, top + 0.20), 0.05, 0.18, 'Z', 6, DARK)
    p.torus((0, 0, wz), 0.32, 0.03, 'Z', 10, 4, DARK)
    for k in range(3):
        rot = Matrix.Rotation(math.radians(60 * k), 4, 'Z')
        p.box((0, 0, wz), (0.64, 0.035, 0.035), DARK, rot=rot)
    # Lid-side hinge barrels between the coaming's, straps back onto the lid.
    for x in (-0.25, 0.25):
        p.cyl((x, hinge[1], hinge[2]), 0.05, 0.36, 'X', 6, DARK)
        p.slab((x - 0.10, 0.55, top + 0.09), (x + 0.10, hinge[1], top + 0.14), DARK)
    p.finish("Mesh_Hatch_Roof_Lid", coll, origin=hinge)


# --------------------------------------------------------------------------
# Cargo ramp
# --------------------------------------------------------------------------

def build_ramp(coll):
    """Rear cargo ramp, 3.2 wide x 4.0 long x 0.2 thick. Hinge axis along X
    through the origin; the deck lies flat toward +Y with its top at z=+0.1."""
    half_w, length, thick = 1.6, 4.0, 0.20
    p = Part(MATS)
    # Deck, with a chamfered toe so it meets the sand without a lip.
    p.prism([(0.10, -thick / 2), (length - 0.35, -thick / 2), (length, 0.06),
             (length, thick / 2), (0.10, thick / 2)], half_w * 2 - 0.2, 'X', HULL)
    # Kerb rails either side, riveted to the deck edge.
    for s in (-1, 1):
        p.slab((s * (half_w - 0.105), 0.25, -thick / 2), (s * half_w, length - 0.05, thick / 2 + 0.16), STEEL)
        bolt_row(p, (s * (half_w + 0.008), 0.3, 0.12), (s * (half_w + 0.008), length - 0.4, 0.12), 5, 'X')
    # Anti-slip ribs across the deck, embedded 5 mm.
    for k in range(13):
        y = 0.35 + k * 0.27
        p.slab((-half_w + 0.12, y - 0.025, thick / 2 - 0.005), (half_w - 0.12, y + 0.025, thick / 2 + 0.035), PALE)
    # Two stiffeners underneath.
    for x in (-0.8, 0.8):
        p.slab((x - 0.06, 0.15, -thick / 2 - 0.10), (x + 0.06, length - 0.45, -thick / 2 + 0.005), STEEL)
    # Ramp-side hinge knuckles.
    for x in (-1.30, -0.45, 0.45, 1.30):
        p.cyl((x, 0, 0), 0.10, 0.34, 'X', 8, DARK)
    p.finish("Mesh_Ramp_Cargo_Ramp", coll)

    # Hull-side half of the hinge: knuckles between the ramp's and a sill bar.
    m = Part(MATS)
    for x in (-0.875, 0.0, 0.875):
        m.cyl((x, 0, 0), 0.10, 0.34, 'X', 8, DARK)
        m.slab((x - 0.15, -0.30, -0.10), (x + 0.15, 0.0, 0.03), DARK)
    m.slab((-half_w, -0.40, -0.10), (half_w, -0.14, 0.0), STEEL)
    m.finish("Mesh_Ramp_Cargo_HingeMount", coll)


def build():
    out = parse_out()
    start(out)
    global MATS
    MATS = link_materials(MATS)

    build_bulkhead(collection("Coll_Door_Bulkhead"))
    build_boarding(collection("Coll_Door_Boarding"))
    build_hatch(collection("Coll_Hatch_Roof"))
    build_ramp(collection("Coll_Ramp_Cargo"))

    report()
    save(out)


build()
