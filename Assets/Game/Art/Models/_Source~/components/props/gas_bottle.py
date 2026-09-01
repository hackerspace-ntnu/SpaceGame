"""Gas bottles — hand-sized pressure vessels with a readable gauge.

Three ways of carrying a charge of compressed gas on a person: one upright
bottle, a twinned pair on a saddle clamp, and a flat flask that lies against a
body or a hull. Anything that fires, inflates, cuts or breathes off stored
pressure can take one of these instead of growing its own tank.

`components/props/fuel_barrel.blend` is the library's existing vessel and it is
a 0.9 m drum — a different object, not this one at another size. These are
0.10-0.13 m and are read at arm's length or as a 256 px icon, which is why the
gauge is the largest single detail on all three rather than a token disc.

**The gauge is the point.** It is the one part of a pressure bottle that says
what the thing is for, so it gets a bezel, ticks, a needle and a lit sector —
four parts on a 25 mm dial — while the vessel behind it stays plain. `gauge()`
is shared by all three variations so they read as one manufacturer's instrument
rather than three different dials. What it deliberately does not get is a glass
cover; see the note in `gauge()`.

Origin is at the **base**, on the bottle's axis, and the axis runs up **+Z**.
A bottle is positioned by where it stands or by where its cradle clamps it, and
an origin at the base means either works with no offset.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(_HERE)))
sys.path.insert(0, os.path.join(os.path.dirname(_HERE), "mechanical"))
sys.path.insert(0, _HERE)

from _buildlib import _axis_rot  # noqa: E402
from _buildlib import *  # noqa: E402,F403
from cable_drum import TrackedPart  # noqa: E402
from panel_control import tube_path  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

# Index 0 is STEEL because `bmesh.ops.bevel` stamps every face it creates with
# material index 0 — see project_buildlib_traps.
STEEL, DARK, PALE, ORANGE, CHROME, RUBBER, BLACK, BRASS, AMBER, WARN = range(10)
MATS = ["Mat_Metal_Steel_Worn",        # bottle wall, saddle, straps
        "Mat_Metal_Steel_Dark",        # valve bodies, gauge cans
        "Mat_Paint_Hull_Bleached",     # painted shoulder, dial face
        "Mat_Paint_Safety_Orange",     # contents bands
        "Mat_Metal_Chrome_Scuffed",    # handwheels, bezels, unions
        "Mat_Plastic_Rubber_Black",    # base bumper, hose
        "Mat_Neutral_Black_Matte",     # dial ticks, shadow gaps
        "Mat_Metal_Brass_Tarnished",   # valve seats and fittings
        "Mat_Emissive_Amber",          # the lit sector of the dial
        "Mat_Paint_Warn_Red"]          # the needle

BEVEL_W = 0.0016

DIAL_R = 0.0125        # every gauge in this file, so they read as one part


# --------------------------------------------------------------------------
# The instrument
# --------------------------------------------------------------------------

def gauge(p, at, axis='Y', r=DIAL_R, needle_deg=38.0, sign=1.0):
    """A pressure dial facing along `axis`, centred on `at`.

    Built in a local Z-up frame and mapped onto `axis` by `_axis_rot`, which is
    the matrix `Part.cyl` itself uses — so the dial, its ticks and its needle
    are all placed by the same transform and cannot disagree about which way
    is up. Placing the round parts with `axis=` and the boxy ones by hand is
    how a needle ends up 90 degrees out of its own dial.

    `sign` flips which way the instrument looks along the axis.

    Returns the boxy faces for the caller's bevel list. The bezel is round
    already and a chamfer at 2 mm minor radius would eat it.
    """
    M = _axis_rot(axis)
    at = Vector(at)

    def place(u, v, w):
        return at + M @ Vector((u, v, w * sign))

    def face_rot(angle):
        return M @ Matrix.Rotation(angle, 4, 'Z')

    hard = []

    # Can, then the dial face a hair proud of it.
    #
    # **No glass disc over the dial, deliberately.** The first pass put one
    # there — `Mat_Glass_Canopy_Tinted` is documented for exactly this — and it
    # rendered as a flat opaque lens that hid the ticks, the needle and the lit
    # sector completely, leaving a 25 mm blank plate where the instrument was
    # supposed to be. On a dial this size the cover costs the whole read and
    # buys one specular highlight. The bezel alone says "glazed".
    p.cyl(place(0, 0, -0.0040), r, 0.0090, axis, 16, DARK)
    p.cyl(place(0, 0, 0.0012), r - 0.0022, 0.0016, axis, 16, PALE)

    # The lit sector: five short blocks stepping round the top of the dial.
    # An arc rather than a ring, because a gauge that glows all over is a lamp.
    # It owns 28-132 degrees and the ticks own the rest, so the two details sit
    # at the same radius without fighting for it.
    for i in range(5):
        a = math.radians(28 + 26 * i)
        hard += p.box(place(math.cos(a) * (r - 0.0040),
                            math.sin(a) * (r - 0.0040), 0.0026),
                      (0.0032, 0.0022, 0.0016), AMBER, rot=face_rot(a))

    for i in range(7):
        a = math.radians(154 + 33 * i)
        hard += p.box(place(math.cos(a) * (r - 0.0040),
                            math.sin(a) * (r - 0.0040), 0.0024),
                      (0.0030, 0.0013, 0.0014), BLACK, rot=face_rot(a))

    na = math.radians(needle_deg)
    hard += p.box(place(math.cos(na) * (r - 0.0064) * 0.5,
                        math.sin(na) * (r - 0.0064) * 0.5, 0.0032),
                  (r - 0.0064, 0.0016, 0.0016), WARN, rot=face_rot(na))
    p.cyl(place(0, 0, 0.0036), 0.0019, 0.0022, axis, 10, DARK)

    p.torus(place(0, 0, 0.0034), r - 0.0016, 0.0020, axis, 18, 6, CHROME)
    return hard


# --------------------------------------------------------------------------
# Shared vessel vocabulary
# --------------------------------------------------------------------------

def _dome(radius, z0, z1, steps=4, up=True):
    """Stations for a hemispherical end cap, as `loft` sections along Z.

    Offsets are absolute along the axis — `loft` does not accumulate them, and
    writing a cap as if it started at zero builds it inside the vessel. That is
    trap 3 in item_devices_BUILD.md and it is silent.
    """
    out = []
    for i in range(steps + 1):
        t = i / steps
        a = (math.pi / 2) * (t if up else 1.0 - t)
        z = z0 + (z1 - z0) * t
        out.append((z, math.cos(a) * radius if up else math.sin(a) * radius))
    return out


def _ring_profile(radius, seg=16):
    return [(math.cos(2 * math.pi * i / seg) * radius,
             math.sin(2 * math.pi * i / seg) * radius) for i in range(seg)]


def bottle_body(p, radius, z_base, z_shoulder, z_neck, mat=STEEL):
    """Base dome, parallel wall, shoulder dome and neck, as one loft.

    One loft rather than a stack of solids: coincident rings between stacked
    parts are what `finish()`'s `remove_doubles` welds into non-manifold edges,
    and on a smooth-shaded vessel that shows up as a black seam ring.
    """
    stations = []
    stations += [(z, rr) for z, rr in _dome(radius, z_base, z_base + radius * 0.7,
                                            steps=3, up=False)][:-1]
    stations += [(z_base + radius * 0.7, radius), (z_shoulder, radius)]
    stations += [(z, rr) for z, rr in _dome(radius, z_shoulder,
                                            z_shoulder + radius * 0.85,
                                            steps=4, up=True)][1:]
    stations += [(z_neck, radius * 0.30)]
    faces = p.loft([(z, _ring_profile(max(rr, 0.0020))) for z, rr in stations],
                   axis='Z', mat=mat)
    return faces


def contents_bands(p, radius, z0, gap=0.0090):
    """Two painted bands saying what is in the bottle.

    Rings rather than a painted cylinder section, so they stand proud and read
    in silhouette as well as in colour — the same trick the harpoon's high-vis
    bands use, at a tenth the size.
    """
    for i in range(2):
        p.torus((0, 0, z0 + gap * i), radius + 0.0006, 0.0018, 'Z', 16, 5,
                ORANGE)


def valve_block(p, z, width=0.0250, mats_wheel=CHROME):
    """The bonnet on top of a bottle: block, handwheel, outlet spigot.

    Returns its boxy faces. Everything on it is placed off `z`, which is the
    top of the neck, so a taller bottle moves the whole assembly with it.
    """
    hard = list(p.box((0, 0, z + 0.0080), (width, 0.0200, 0.0160), DARK))
    p.torus((0, 0, z + 0.0035), width * 0.34, 0.0026, 'Z', 14, 6, BRASS)
    p.cyl((0, 0, z + 0.0195), 0.0042, 0.0070, 'Z', 10, CHROME)
    p.torus((0, 0, z + 0.0235), 0.0092, 0.0026, 'Z', 16, 6, mats_wheel)
    for i in range(4):
        a = math.radians(45 + 90 * i)
        hard += p.box((math.cos(a) * 0.0050, math.sin(a) * 0.0050, z + 0.0235),
                      (0.0090, 0.0022, 0.0022), mats_wheel,
                      rot=Matrix.Rotation(a, 4, 'Z'))
    return hard


# --------------------------------------------------------------------------
# Variations
# --------------------------------------------------------------------------

def single(coll, mats):
    """One upright bottle: 0.126 m tall, 32 mm across, gauge on the bonnet.

    The one the grapple bracer ships on. Slimmest of the three, which is what
    lets it lie along the outboard flank of a forearm without adding to the
    width the wearer has to get through a doorway.
    """
    p = TrackedPart(mats)
    hard = []

    r = 0.0160
    bottle_body(p, r, 0.0, 0.0740, 0.0960)
    p.torus((0, 0, 0.0030), r - 0.0020, 0.0030, 'Z', 16, 6, RUBBER)   # bumper
    # Painted collar on the PARALLEL wall, not on the shoulder: above
    # z_shoulder the vessel is already doming inward, so a band written there
    # is wider than the bottle under it and reads as a loose flange.
    p.cyl((0, 0, 0.0600), r + 0.0007, 0.0150, 'Z', 16, PALE)          # collar
    contents_bands(p, r, 0.0300)

    hard += valve_block(p, 0.0960)
    hard += gauge(p, (0.0, -0.0170, 0.1050), axis='Y', needle_deg=34.0,
                  sign=-1.0)

    # Outlet, and the first bend of the hose leaving it.
    p.cyl((0.0180, 0.0, 0.1030), 0.0040, 0.0110, 'X', 10, BRASS)
    tube_path(p, [(0.0235, 0.0, 0.1030), (0.0300, 0.0, 0.0980),
                  (0.0320, 0.0, 0.0880)], 0.0032, RUBBER, seg=6)

    print("  Coll_GasBottle_Single: %d face(s) re-stamped" % p.restamp())
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_GasBottle_Single", coll)


def twin(coll, mats):
    """Two bottles on a saddle clamp under one manifold and one gauge.

    Twice the charge in a squatter package. The silhouette separator is the
    pair itself — from any bearing this is two circles, and the single is one.
    """
    p = TrackedPart(mats)
    hard = []

    r = 0.0130
    for sx in (-1, 1):
        x = sx * (r + 0.0025)
        stations = []
        stations += [(z, rr) for z, rr in _dome(r, 0.0, r * 0.7, 3, False)][:-1]
        stations += [(r * 0.7, r), (0.0640, r)]
        stations += [(z, rr) for z, rr in _dome(r, 0.0640, 0.0640 + r * 0.85,
                                                4, True)][1:]
        stations += [(0.0840, r * 0.30)]
        faces = p.loft([(z, [(u + x, v) for u, v in
                             _ring_profile(max(rr, 0.0020))])
                        for z, rr in stations], axis='Z', mat=STEEL)
        p.shade(faces, True)
        p.torus((x, 0, 0.0300), r + 0.0006, 0.0016, 'Z', 14, 5, ORANGE)
        p.cyl((x, 0, 0.0880), 0.0055, 0.0090, 'Z', 10, BRASS)

    # Saddle clamp round both bottles, and the crossover manifold above them.
    for z in (0.0250, 0.0540):
        hard += p.box((0, 0, z), (0.0600, 0.0300, 0.0070), STEEL)
        for sx in (-1, 1):
            p.cyl((sx * 0.0270, 0.0, z), 0.0034, 0.0090, 'Z', 8, CHROME)
    hard += p.box((0, 0, 0.0930), (0.0420, 0.0170, 0.0130), DARK)
    p.cyl((0, 0, 0.1030), 0.0042, 0.0080, 'Z', 10, CHROME)
    p.torus((0, 0, 0.1075), 0.0092, 0.0026, 'Z', 16, 6, CHROME)

    hard += gauge(p, (0.0, -0.0145, 0.0985), axis='Y', needle_deg=62.0,
                  sign=-1.0)

    print("  Coll_GasBottle_Twin: %d face(s) re-stamped" % p.restamp())
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_GasBottle_Twin", coll)


def flask(coll, mats):
    """A flat oval flask with the gauge recessed into its face.

    The lie-flat member of the family: 0.104 x 0.068 x 0.028, so it sits
    against a back, a thigh or a hull without standing off it. Its gauge faces
    out of the broad side rather than off a bonnet, which is the only way an
    instrument on a flat tank is readable.
    """
    p = TrackedPart(mats)
    hard = []

    def oval(w, d, seg=20):
        return [(math.cos(2 * math.pi * i / seg) * w,
                 math.sin(2 * math.pi * i / seg) * d) for i in range(seg)]

    # prism(axis='Y') maps a profile (u, v) onto (x, z) and extrudes along Y.
    # Written down rather than guessed — see trap 4 in item_devices_BUILD.md.
    body = p.prism(oval(0.0330, 0.0330), 0.0260, axis='Y', mat=STEEL,
                   offset=(0, 0, 0.0350))
    p.shade(body, True)
    for sy in (-1, 1):
        p.loft([(sy * 0.0130, oval(0.0330, 0.0330)),
                (sy * 0.0140, oval(0.0300, 0.0300))],
               axis='Y', mat=STEEL, cap=True)

    p.cyl((0, 0, 0.0350), 0.0345, 0.0100, 'Y', 20, PALE)   # painted waistband
    for sz in (-1, 1):
        p.torus((0, 0, 0.0350 + sz * 0.0230), 0.0230, 0.0018, 'Y', 16, 5,
                ORANGE)

    # Filler cap and strap lugs.
    hard += p.box((0.0, 0.0, 0.0700), (0.0180, 0.0170, 0.0080), DARK)
    p.cyl((0, 0, 0.0760), 0.0070, 0.0060, 'Z', 12, CHROME)
    for sx in (-1, 1):
        hard += p.box((sx * 0.0290, 0.0, 0.0130), (0.0080, 0.0180, 0.0070),
                      STEEL)
        p.torus((sx * 0.0300, 0.0, 0.0130), 0.0055, 0.0020, 'X', 12, 5, CHROME)

    # Recessed gauge: a well sunk into the face, dial at the bottom of it.
    p.cyl((0, -0.0132, 0.0400), DIAL_R + 0.0035, 0.0030, 'Y', 18, DARK)
    hard += gauge(p, (0.0, -0.0148, 0.0400), axis='Y', needle_deg=15.0,
                  sign=-1.0)

    print("  Coll_GasBottle_Flask: %d face(s) re-stamped" % p.restamp())
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_GasBottle_Flask", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    single(collection("Coll_GasBottle_Single"), mats)
    twin(collection("Coll_GasBottle_Twin"), mats)
    flask(collection("Coll_GasBottle_Flask"), mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
