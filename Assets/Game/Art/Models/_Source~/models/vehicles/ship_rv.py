"""ShipRV — the rundown RV spacecraft.

A rebuild of Assets/Game/Prefabs/agents/vehicle/"ship_model 1.blend" at a much higher
detail level, keeping the original's overall dimensions and shape so the ship
still reads as the same vessel and still fits the Unity prefab built from it.

Authoring frame is the original's, deliberately: nose along +X, +Y is port, Z up.
That is not this library's -Y-forward convention, but ShipRVBuilder yaws the
model 90 degrees about Y to put the nose down Unity's +Z, and every hinge, seat
and collision box in that script is measured off the meshes at build time. Any
other frame would silently rotate the whole prefab. The deviation is recorded in
ship_rv_BUILD.md.

Interior contract, unchanged from every previous pass because the Unity prefab
and the player capsule both depend on it:
    deck top z=-1.08, ceiling z=1.12  -> 2.20 m headroom

The overall envelope is NOT preserved from the original any more. Deleting the
stern drive and putting the engines on real wings changes the bounding box on
every axis, deliberately — see ship_rv_BUILD.md. ShipRVBuilder measures its
collision, hinges and seat points off the meshes at build time, so it follows.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

COMPONENTS = os.path.join(LIB_ROOT, "components")

# ── Envelope ────────────────────────────────────────────────────────────────
# The hull is ONE tapered form from nose to tail, not a stack of boxes.
#
# Two earlier passes each failed in an opposite direction, and the shape below
# is the reaction to both. The first tapered the section with a rounded corner
# chamfer and came out a barrel — bubbly, no edges anywhere for light to catch.
# The second removed the taper entirely and made it a parallel-sided box with a
# separate stepped cab bolted on the front: crisp, but a shipping container with
# a caravan on it, and it read as three rectangles rather than a vehicle.
#
# What fixes both at once is a section with FEW, BIG, FLAT facets and hard
# creases between them, swept along a hull that genuinely tapers in plan and in
# profile. Chamfer radius is not the tool — chine lines are. So the section is a
# narrow keel, a wide belly facet flaring out to the chine, a vertical side
# strake, a wide shoulder facet angling back in, and a narrow roof. Five planes
# a side, four hard creases, nothing curved.
BOT, TOP = -1.46, 2.03          # hull skin bottom / top at the widest station
DECK_TOP, CEIL_BOT = -1.08, 1.12
CAB_FWD, CAB_AFT = 1.20, -6.40  # walkable cabin extent along X
BEAM_Y = 2.38                   # max half-beam, at the chine
SIDE_Y = BEAM_Y                 # the clamshell panels sit in the side plane
DECK_Y = 2.06
SKIN = 0.10                     # hull wall thickness

# The vertical side strake, as insets from the station's own bottom and top.
# Constant along the whole hull, which is what makes the chine lines read as two
# continuous creases running the length of the ship rather than as per-station
# accidents. Everything else about the section scales with the station.
BAND_DN, BAND_UP = 0.36, 0.67
KEEL_K = 0.60                   # keel width as a fraction of half-beam
# Roof width is per-station (the fifth MASTER column) rather than global. It has
# to be: the cabin wants a narrow roof and big shoulders, but the cockpit wants
# a wide flat one, because the windscreen's header sits just under the roof edge
# and a roof that pinches in up there makes the screen wider than the hull it is
# set into. That is exactly how the last attempt ended up with glass poking out
# through the shoulders.

# Parallel midbody. The clamshell doors are flat panels, so the side plane has
# to be genuinely parallel wherever they open; the taper lives outside this
# span. Real hulls are built exactly this way and it costs nothing in silhouette
# — 5.4 m of 12.6 is parallel, and the eye reads the 7.2 m of taper.
PAR_AFT, PAR_FWD = -4.30, 1.10

OPEN_Z0, OPEN_Z1 = BOT + BAND_DN + 0.04, TOP - BAND_UP - 0.04   # -1.06 .. 1.32
OPEN_X0, OPEN_X1 = PAR_AFT + 0.20, PAR_FWD - 0.18               # -4.10 .. 0.92

# Windscreen. The roofed section stops at the header and the bonnet picks up at
# the sill; the glass IS the hull between them. The old cab had a flat front
# face with a rectangular hole cut in it and a visor slab laid over the top,
# which is where the overlapping geometry at the nose came from — there is no
# plate-with-a-hole anywhere now, so nothing can overlap.
HEADER_X, HEADER_Z = 3.05, 1.42
SILL_X, SILL_Z = 4.00, 0.20
NOSE_X = 4.75                   # forward-most point

# Tail and the cargo doorway cut in its aft face.
TAIL_X = -7.85
RAMP_HW, RAMP_TOP = 1.05, 1.10
RAMP_Z0 = DECK_TOP + 0.02       # hinge line, just clear of the deck

# Wing geometry. The fold axle sits on the shoulder crease, not on the roof, and
# the spar reaches 2.2 m outboard of the hull side with the nacelle carried at
# its tip — see build_wings.
AXLE_X, AXLE_Y, AXLE_Z = -2.42, 2.30, 1.24
POD_Y, POD_Z = 3.58, 1.47       # nacelle saddle: sits on the wing's top surface
TIP_Y = 5.15

HULL, RUST, SLATE, STEEL, DARK, PANEL, GLASS, BLACK, AMBER, WARM = range(10)
MATS = ["Mat_Metal_HullRust_Orange", "Mat_Metal_Rust_Heavy",
        "Mat_Neutral_Slate_Dark", "Mat_Metal_Steel_Worn",
        "Mat_Metal_Steel_Dark", "Mat_Neutral_Panel_Grey",
        "Mat_Glass_Canopy_Tinted", "Mat_Neutral_Black_Matte",
        "Mat_Emissive_Amber", "Mat_Emissive_Cabin_Warm"]


# ── Hull cross-section ──────────────────────────────────────────────────────
# Ten points around the girth, five flat planes a side. Segment i runs from
# point i to point i+1; segments 3 (starboard) and 8 (port) are the vertical
# side strakes the clamshell panels open, so those are the ones the open-shell
# variant omits.
SIDE_SEGMENTS = (3, 8)


def section(hw, bot, top, rk):
    """Hull girth at one station: keel, belly flare, side strake, shoulder,
    roof. Every one of those is a plane and every junction is a crease.

    Nothing here is rounded. The old version chamfered the four long corners
    with a radius, which meant the only strong lines on the whole hull were the
    top and bottom edges of the side wall — everything between them washed out.
    Splaying the belly and shoulder into full-width facets instead puts two long
    creases down each side of the ship at the two places the eye actually looks.
    """
    z0, z1 = bot + BAND_DN, top - BAND_UP
    if z1 <= z0:                       # a station too shallow for a strake
        z0 = z1 = (bot + top) / 2.0
    kw, rw = hw * KEEL_K, hw * rk
    return [
        (kw, bot),                # 0  keel, port edge
        (0.0, bot),               # 1  keel centreline
        (-kw, bot),               # 2  keel, starboard edge
        (-hw, z0),                # 3  chine, starboard   <- belly crease
        (-hw, z1),                # 4  shoulder, starboard <- shoulder crease
        (-rw, top),               # 5  roof, starboard edge
        (0.0, top),               # 6  roof centreline
        (rw, top),                # 7  roof, port edge
        (hw, z1),                 # 8  shoulder, port
        (hw, z0),                 # 9  chine, port
    ]


# ── Longitudinal shape ──────────────────────────────────────────────────────
# One table for the whole ship: (x, half-beam, bottom, top). The cabin, tail and
# cockpit shells are all sliced out of this, which is the point — three objects
# that interpolate the same curve read as one continuous hull, where three
# objects with their own independent profiles read as three boxes stuck
# together. That is what went wrong last time.
# The belly stays flat and the tail is cut off square. Tapering top and bottom
# symmetrically at both ends — which is what "just taper it" produces — makes an
# almond, and an almond in profile is a blimp. A vehicle needs a flat underside
# to sit on and a transom to end at; all the taper the silhouette needs it gets
# from the plan view and from the nose dropping away forward.
MASTER = [
    # x,        half-beam, bottom, top,  roof width factor
    (TAIL_X,    1.74,      -1.38,  1.72, 0.60),   # transom, blunt and raked
    (-7.20,     1.98,      -1.44,  1.90, 0.58),
    (CAB_AFT,   2.16,      -1.46,  1.99, 0.56),   # cabin / tail shell split
    (-5.35,     2.30,      BOT,    TOP,  0.56),
    (PAR_AFT,   BEAM_Y,    BOT,    TOP,  0.56),   # parallel midbody begins
    (-2.70,     BEAM_Y,    BOT,    TOP,  0.56),
    (-1.00,     BEAM_Y,    BOT,    TOP,  0.56),
    (PAR_FWD,   BEAM_Y,    BOT,    TOP,  0.58),   # parallel midbody ends
    (CAB_FWD,   2.36,      BOT,    2.01, 0.62),   # cabin / cockpit shell split
    (2.30,      2.06,      -1.44,  1.83, 0.74),
    (HEADER_X,  1.78,      -1.38,  1.52, 0.86),   # roof stops at the header
]


def station(x):
    """(half-beam, bottom, top, roof factor) anywhere along the hull."""
    if x <= MASTER[0][0]:
        return MASTER[0][1:]
    if x >= MASTER[-1][0]:
        return MASTER[-1][1:]
    for (x0, *a), (x1, *b) in zip(MASTER, MASTER[1:]):
        if x0 <= x <= x1:
            t = (x - x0) / (x1 - x0)
            return tuple(u + (v - u) * t for u, v in zip(a, b))
    raise AssertionError("x outside MASTER")


def hw_at(x, z):
    """Half-width of the hull skin at a point, following the facets.

    The deck, ceiling and interior lining are all cut to this rather than to a
    constant, so nothing pokes through the skin where the hull narrows — which
    is the failure mode that made the last hull stay parallel-sided.
    """
    hw, bot, top, rk = station(x)
    z0, z1 = bot + BAND_DN, top - BAND_UP
    if z <= bot or z >= top:
        return hw * (KEEL_K if z <= bot else rk)
    if z < z0:
        return hw * (KEEL_K + (1 - KEEL_K) * (z - bot) / (z0 - bot))
    if z > z1:
        return hw * (rk + (1 - rk) * (top - z) / (top - z1))
    return hw


def inside_hw(x, z, clamp=None):
    """Usable half-width inside the skin at a point."""
    v = hw_at(x, z) - SKIN - 0.05
    return v if clamp is None else min(v, clamp)


def stations_between(x0, x1):
    """The MASTER x values spanning [x0, x1], endpoints included."""
    xs = [x0] + [m[0] for m in MASTER if x0 < m[0] < x1] + [x1]
    return xs


# ── Forward of the header ───────────────────────────────────────────────────
# The section loses its roof here and the top edge becomes a cut line: first the
# windscreen rake, then the bonnet. (x, half-beam, bottom, cut).
#
# This is a second table rather than more MASTER rows because the profile has a
# different topology — seven points, no roof — and because MASTER's station()
# clamps outside its range. Sizing the sill and the A-pillars off station() got
# them the header's 1.78 m half-beam at a point where the hull is 1.40, and they
# stood 38 cm out in mid-air. Anything measured ahead of the header goes through
# fwd_hw_at instead.
FWD = [
    (HEADER_X, 1.78, -1.38, HEADER_Z),
    (3.55, 1.62, -1.34, 0.80),           # mid-screen
    (SILL_X, 1.40, -1.28, SILL_Z),
    (4.45, 1.16, -1.16, 0.02),
    (NOSE_X, 0.94, -0.94, -0.14),        # blunt chisel, not a point
]


def fwd_station(x):
    """(half-beam, bottom, cut) anywhere on the forward hull."""
    if x <= FWD[0][0]:
        return FWD[0][1:]
    if x >= FWD[-1][0]:
        return FWD[-1][1:]
    for (x0, *a), (x1, *b) in zip(FWD, FWD[1:]):
        if x0 <= x <= x1:
            t = (x - x0) / (x1 - x0)
            return tuple(u + (v - u) * t for u, v in zip(a, b))
    raise AssertionError("x outside FWD")


def cowl_z0(bot, cut):
    """Foot of the side strake on a roofless station."""
    return min(bot + BAND_DN, cut - 0.04)


def fwd_hw_at(x, z):
    """Half-width of the forward hull skin, the cowl-profile counterpart of
    hw_at."""
    hw, bot, cut = fwd_station(x)
    z0 = cowl_z0(bot, cut)
    if z <= bot:
        return hw * KEEL_K
    if z >= z0:
        return hw
    return hw * (KEEL_K + (1 - KEEL_K) * (z - bot) / (z0 - bot))


def inner(profile, t=SKIN):
    """Profile offset inward, giving the skin real thickness.

    Offsetting toward the centroid rather than along each edge normal: the
    section is convex enough that the difference is under a millimetre, and it
    cannot self-intersect at the corners the way an edge offset can.
    """
    cy = sum(y for y, _ in profile) / len(profile)
    cz = sum(z for _, z in profile) / len(profile)
    out = []
    for y, z in profile:
        d = Vector((y - cy, z - cz))
        n = d.length
        k = max(0.0, (n - t)) / n if n > 1e-6 else 0.0
        out.append((cy + d.x * k, cz + d.y * k))
    return out


def plate(p, x0, x1, prof0, prof1, i, mat, gap=0.016):
    """One hull plate: profile segment `i` spanning two stations.

    Each plate is its own closed solid with a gap around it, so the hull reads
    as bolted plating and the shell has genuine wall thickness — the player can
    stand inside and see a wall rather than the back of a one-sided surface.
    """
    n = len(prof0)
    j = (i + 1) % n
    o0, o1 = prof0, prof1
    i0, i1 = inner(prof0), inner(prof1)

    def quad(outer, innr):
        a, b = Vector(outer[i]), Vector(outer[j])
        d = (b - a)
        if d.length > 2 * gap:
            d = d.normalized() * gap
            a, b = a + d, b - d
        c, e = Vector(innr[i]), Vector(innr[j])
        d2 = (e - c)
        if d2.length > 2 * gap:
            d2 = d2.normalized() * gap
            c, e = c + d2, e - d2
        return [tuple(a), tuple(b), tuple(e), tuple(c)]

    p.loft([(x0 + gap, quad(o0, i0)), (x1 - gap, quad(o1, i1))], 'X', mat,
           cap=True)


def hull_run(p, x0, x1, skip_sides=False, mat=HULL, extra=()):
    """Plate a stretch of hull between two x, following MASTER.

    `skip_sides` omits the side strake over the door opening, which is the only
    difference between the two shell variants. `extra` adds intermediate
    stations where a stretch needs more than the master table gives it.
    """
    xs = sorted(set(stations_between(x0, x1)) | {e for e in extra
                                                 if x0 < e < x1})
    for xa, xb in zip(xs, xs[1:]):
        prof0, prof1 = section(*station(xa)), section(*station(xb))
        spans_opening = (xb > OPEN_X0 and xa < OPEN_X1)
        for i in range(len(prof0)):
            if skip_sides and spans_opening and i in SIDE_SEGMENTS:
                continue
            # Belly in the darker rust, roof in slate, sides and facets in hull
            # colour. Three materials laid on the facets rather than on plate
            # boundaries, so the colour change lands on a crease and reinforces
            # the section instead of fighting it.
            m = RUST if i in (0, 1) else (SLATE if i in (5, 6) else mat)
            plate(p, xa, xb, prof0, prof1, i, m)


def chine_rails(p, x0, x1, upper=True):
    """A rail along each of the two long creases.

    One strip per crease and nothing else. The old hull carried a skirt, a
    waist rail and a roof kerb per side and the result was busy without being
    any crisper; a single rail sitting exactly on the chine is what actually
    draws the line. `upper` is off through the cockpit, where the shoulder
    crease converges into the windscreen's A-pillar and a rail would collide
    with it.
    """
    xs = stations_between(x0, x1)
    lines = [(lambda st: st[1] + BAND_DN, 0.08, RUST)]
    if upper:
        lines.append((lambda st: st[2] - BAND_UP, 0.06, STEEL))
    for s in (-1, 1):
        for z_of, thick, mat in lines:
            # One continuous loft down the whole run, not a box per station
            # pair: capping every pair left a visible end plate at each station
            # and turned the rail into a dotted line of tick marks.
            def ring(x):
                st = station(x)
                z, hw = z_of(st), st[0]
                return [(s * (hw - 0.08), z - thick),
                        (s * (hw + 0.03), z - thick),
                        (s * (hw + 0.03), z + thick),
                        (s * (hw - 0.08), z + thick)]

            p.loft([(x, ring(x)) for x in xs], 'X', mat, cap=True)


def opening_frame(p):
    """Reveal around each side opening — the cut edge the player sees when the
    clamshell is open. Without it the hull looks like paper at the doorway."""
    for s in (-1, 1):
        y = s * SIDE_Y
        for z in (OPEN_Z0, OPEN_Z1):
            p.slab((OPEN_X0 - 0.06, y - s * SKIN - s * 0.03,
                    z - 0.05 if z > 0 else z),
                   (OPEN_X1 + 0.06, y + s * 0.03,
                    z + 0.05 if z < 0 else z), STEEL)
        for x in (OPEN_X0, OPEN_X1):
            p.slab((x - 0.05, y - s * SKIN - s * 0.03, OPEN_Z0),
                   (x + 0.05, y + s * 0.03, OPEN_Z1), STEEL)
        # Hinge spine above and below, where the panels actually pivot.
        for z in (OPEN_Z0 - 0.12, OPEN_Z1 + 0.12):
            p.cyl((( OPEN_X0 + OPEN_X1) / 2, y - s * 0.04, z), 0.05,
                  OPEN_X1 - OPEN_X0, 'X', 10, DARK)


# ── Unique hull geometry ────────────────────────────────────────────────────

def build_shells(coll, mats):
    """The two switchable hull variants, plus the cockpit and tail sections."""
    made = {}
    # Two extra stations inside the parallel run: without them the midbody is
    # one 5.4 m plate per facet and the bevel has nothing to bite on at the
    # ends. They cost ~40 triangles a facet and keep the panel lines regular.
    mid = (-3.20, -1.90)

    p = Part(mats)
    hull_run(p, CAB_AFT, CAB_FWD, skip_sides=False, extra=mid)
    chine_rails(p, CAB_AFT, CAB_FWD)
    p.bevel(width=0.010, segments=1)
    made["closed"] = p.finish("Mesh_HullShell_Closed", coll)

    p = Part(mats)
    hull_run(p, CAB_AFT, CAB_FWD, skip_sides=True, extra=mid)
    chine_rails(p, CAB_AFT, CAB_FWD)
    opening_frame(p)
    p.bevel(width=0.010, segments=1)
    made["open"] = p.finish("Mesh_HullShell_Open", coll)

    # ── Forward hull: no stepped cab, no punched window ─────────────────────
    # The roofed section simply continues forward off MASTER, narrowing and
    # dropping until the roof runs out at the screen header. Ahead of that the
    # section loses its top and becomes the cowl; the glass spans the gap. So
    # the cab is not a box parked on the front of another box — it is the same
    # hull tapering to a point, which is the whole shape complaint.
    p = Part(mats)
    hull_run(p, CAB_FWD, HEADER_X, extra=(1.70,))
    # Carry the belly rail forward. It stopped dead at the cabin joint before,
    # which put a hard end-cap halfway down the ship on the one line that is
    # supposed to run its whole length.
    chine_rails(p, CAB_FWD, HEADER_X, upper=False)

    # Ahead of the header the section keeps its keel, belly facets and side
    # strakes but has no roof: `cut` is the top edge, and it follows the screen
    # rake down to the sill and then the bonnet down to the nose. The two zones
    # differ only in whether that top edge is decked over.
    def cowl(hw, bot, cut):
        z0 = cowl_z0(bot, cut)
        return [(hw * KEEL_K, bot), (0.0, bot), (-hw * KEEL_K, bot),
                (-hw, z0), (-hw, cut), (hw, cut), (hw, z0)]

    TOP_SEG = 4                              # the decked-over top face
    for k in range(len(FWD) - 1):
        x0, hw0, b0, c0 = FWD[k]
        x1, hw1, b1, c1 = FWD[k + 1]
        pr0, pr1 = cowl(hw0, b0, c0), cowl(hw1, b1, c1)
        under_glass = x1 <= SILL_X
        for i in range(len(pr0)):
            if under_glass and i == TOP_SEG:
                continue                     # the windscreen goes here
            plate(p, x0, x1, pr0, pr1, i, RUST if i in (0, 1) else HULL)

    # ── Windscreen frame ────────────────────────────────────────────────────
    # Header, sill and two A-pillars, each sized off the hull at its own point
    # on the screen rather than to a fixed width. The pillars matter more than
    # they look: without them the screen's sides are the raw ends of the cowl
    # plates, which step in and out station by station and read as a staircase.
    hdr_w = fwd_hw_at(HEADER_X, HEADER_Z)
    sill_w = fwd_hw_at(SILL_X, SILL_Z)
    p.loft([(HEADER_X - 0.12, [(-hdr_w, HEADER_Z - 0.05), (hdr_w, HEADER_Z - 0.05),
                               (hdr_w, HEADER_Z + 0.10), (-hdr_w, HEADER_Z + 0.10)]),
            (HEADER_X + 0.06, [(-hdr_w + 0.06, HEADER_Z - 0.10),
                               (hdr_w - 0.06, HEADER_Z - 0.10),
                               (hdr_w - 0.06, HEADER_Z + 0.02),
                               (-hdr_w + 0.06, HEADER_Z + 0.02)])],
           'X', STEEL, cap=True)
    p.loft([(SILL_X - 0.08, [(-sill_w + 0.06, SILL_Z - 0.06),
                             (sill_w - 0.06, SILL_Z - 0.06),
                             (sill_w - 0.06, SILL_Z + 0.08),
                             (-sill_w + 0.06, SILL_Z + 0.08)]),
            (SILL_X + 0.12, [(-sill_w + 0.02, SILL_Z - 0.16),
                             (sill_w - 0.02, SILL_Z - 0.16),
                             (sill_w - 0.02, SILL_Z - 0.04),
                             (-sill_w + 0.02, SILL_Z - 0.04)])],
           'X', STEEL, cap=True)
    for s in (-1, 1):
        p.loft([(HEADER_X - 0.02,
                 [(s * (hdr_w - 0.20), HEADER_Z - 0.12),
                  (s * hdr_w, HEADER_Z - 0.12),
                  (s * hdr_w, HEADER_Z + 0.06),
                  (s * (hdr_w - 0.20), HEADER_Z + 0.06)]),
                (SILL_X + 0.02,
                 [(s * (sill_w - 0.20), SILL_Z - 0.10),
                  (s * sill_w, SILL_Z - 0.10),
                  (s * sill_w, SILL_Z + 0.08),
                  (s * (sill_w - 0.20), SILL_Z + 0.08)])],
               'X', STEEL, cap=True)

    # Two lamps recessed in the nose face. The bumper bar and its lamp pods are
    # gone — a truck bumper on a tapered nose was the last thing still saying
    # "van" rather than "ship".
    for s in (-1, 1):
        p.cyl((NOSE_X - 0.10, s * 0.36, -0.52), 0.16, 0.14, 'X', 12, STEEL)
        p.cyl((NOSE_X - 0.05, s * 0.36, -0.52), 0.12, 0.05, 'X', 12, AMBER)
    p.bevel(width=0.010, segments=1)
    made["cockpit"] = p.finish("Mesh_HullShell_Cockpit", coll)

    # ── Tail section behind the cargo bulkhead ──────────────────────────────
    # Sliced off the same MASTER curve as the cabin, so the taper carries
    # straight through the joint instead of restarting at it.
    p = Part(mats)
    hull_run(p, MASTER[0][0], CAB_AFT)
    chine_rails(p, MASTER[0][0], CAB_AFT)
    # Aft face with the cargo doorway cut into it, set just inside the last
    # station so it closes the tail rather than standing off the back of it.
    a_top = station(TAIL_X)[2]
    aw = hw_at(TAIL_X + 0.05, 0.0) - 0.04
    for s in (-1, 1):
        p.slab((TAIL_X + 0.02, s * RAMP_HW, RAMP_Z0 - 0.06),
               (TAIL_X + 0.10, s * aw, a_top - 0.26), SLATE)
    p.slab((TAIL_X + 0.02, -RAMP_HW, RAMP_TOP),
           (TAIL_X + 0.10, RAMP_HW, a_top - 0.26), SLATE)
    p.bevel(width=0.010, segments=1)
    made["tail"] = p.finish("Mesh_HullShell_Tail", coll)
    return made


def build_deck(coll, mats):
    """Deck and ceiling substrates. The library's deck plates dress these
    rather than tiling the whole floor — twenty-four plate instances would cost
    more than the rest of the ship put together."""
    # Both are lofted to the hull's own half-width at their height rather than
    # laid out as constant-width slabs. That is what lets the hull taper at all:
    # a fixed 2.10 m floor is precisely why the last version had to keep its
    # sides parallel from end to end.
    d_x0, d_x1 = MASTER[0][0] + 0.34, CAB_FWD + 0.34

    # The deck section is a trapezoid, not a rectangle: its underside is cut to
    # the hull at the underside's own height. A rectangular floor slab is wider
    # at the bottom than the belly facet allows and pushes its bottom corners
    # out through the skin — which showed up as a dotted line of tabs running
    # the length of the belly.
    p = Part(mats)
    p.loft([(x, [(-inside_hw(x, DECK_TOP - 0.14, DECK_Y), DECK_TOP - 0.14),
                 (inside_hw(x, DECK_TOP - 0.14, DECK_Y), DECK_TOP - 0.14),
                 (inside_hw(x, DECK_TOP, DECK_Y), DECK_TOP),
                 (-inside_hw(x, DECK_TOP, DECK_Y), DECK_TOP)])
            for x in stations_between(d_x0, d_x1)], 'X', STEEL, cap=True)
    # Transverse floor beams under the deck, visible through the grating.
    for i in range(8):
        x = d_x0 + 0.60 + i * 1.10
        w = inside_hw(x, DECK_TOP - 0.28, DECK_Y)
        p.slab((x - 0.06, -w, DECK_TOP - 0.28), (x + 0.06, w, DECK_TOP - 0.14),
               DARK)
    # Longitudinal seams in the deck surface.
    for y in (-0.95, 0.95):
        p.slab((d_x0 + 0.12, y - 0.02, DECK_TOP),
               (d_x1 - 0.12, y + 0.02, DECK_TOP + 0.012), DARK)
    p.bevel(width=0.006, segments=1)
    deck = p.finish("Mesh_Deck_Plate", coll)

    c_x0, c_x1 = MASTER[0][0] + 0.45, CAB_FWD + 0.24

    def cw(x):
        return inside_hw(x, CEIL_BOT, 1.70)

    p = Part(mats)
    p.loft([(x, [(-cw(x), CEIL_BOT), (cw(x), CEIL_BOT),
                 (cw(x), CEIL_BOT + 0.16), (-cw(x), CEIL_BOT + 0.16)])
            for x in stations_between(c_x0, c_x1)], 'X', PANEL, cap=True)
    # Ribs, and a recessed centre channel the light strips sit in.
    for i in range(7):
        x = c_x0 + 0.75 + i * 1.20
        p.slab((x - 0.07, -cw(x), CEIL_BOT - 0.07),
               (x + 0.07, cw(x), CEIL_BOT), STEEL)
    p.slab((c_x0 + 0.20, -0.34, CEIL_BOT + 0.02),
           (c_x1 - 0.10, 0.34, CEIL_BOT + 0.18), DARK)
    p.bevel(width=0.006, segments=1)
    ceiling = p.finish("Mesh_Ceiling_Plate", coll)
    return deck, ceiling


def build_doors(coll, mats):
    """The six moving panels. Each is modelled around its own hinge line and
    its origin is put *on* that line, so ShipRVBuilder's measured pivots and the
    armature bones land in the same place."""
    made = {}
    mid_z = (OPEN_Z0 + OPEN_Z1) / 2

    def side_panel(name, port, upper):
        s = 1 if port else -1
        z0 = mid_z - 0.07 if upper else OPEN_Z0
        z1 = OPEN_Z1 if upper else mid_z + 0.07
        p = Part(mats)
        y_out = s * SIDE_Y
        y_in = s * (SIDE_Y - 0.11)
        lo = (OPEN_X0 + 0.03, min(y_in, y_out), z0)
        hi = (OPEN_X1 - 0.03, max(y_in, y_out), z1)
        p.slab(lo, hi, HULL)
        # Inner face: the player sees this from inside the cabin.
        p.slab((lo[0] + 0.08, y_in - s * 0.03, z0 + 0.06),
               (hi[0] - 0.08, y_in, z1 - 0.06), PANEL)
        # Stiffening ribs. Four, not six, and the rivet row along the free edge
        # is gone — the panels sit under the chine rails now and the fasteners
        # were noise at the scale the ship is actually seen from.
        for i in range(4):
            x = OPEN_X0 + 0.60 + i * 1.25
            p.slab((x - 0.05, min(y_in, y_out) - 0.02, z0 + 0.05),
                   (x + 0.05, max(y_in, y_out) + 0.02, z1 - 0.05), STEEL)
        # A porthole in each upper leaf, so the cabin has daylight when shut.
        if upper:
            cx = OPEN_X0 + 1.6 if port else OPEN_X1 - 1.6
            p.tube((cx, y_out, (z0 + z1) / 2), 0.30, 0.07, 0.14, 'Y', 12,
                   STEEL)
            p.cyl((cx, y_out - s * 0.02, (z0 + z1) / 2), 0.26, 0.03, 'Y', 12,
                  GLASS)
        # Grab handle on the outside, weather seal on the inside.
        hz = z1 - 0.30 if upper else z0 + 0.30
        p.cyl(((OPEN_X0 + OPEN_X1) / 2, y_out + s * 0.06, hz), 0.026, 0.70,
              'X', 8, STEEL)
        for dx in (-0.32, 0.32):
            p.box(((OPEN_X0 + OPEN_X1) / 2 + dx, y_out + s * 0.03, hz),
                  (0.05, 0.07, 0.05), DARK)
        p.slab((lo[0], y_in - s * 0.02, z0 + 0.02),
               (hi[0], y_in, z0 + 0.05), BLACK)
        p.bevel(width=0.008, segments=1)
        # Origin on the hinge line: outboard edge, top edge for upper leaves
        # and bottom edge for lower ones — the clamshell axis.
        return p.finish(name, coll,
                        origin=(0.0, y_out, z1 if upper else z0))

    made["port_upper"] = side_panel("Mesh_DoorPanel_SideUpperPort", True, True)
    made["port_lower"] = side_panel("Mesh_DoorPanel_SideLowerPort", True, False)
    made["stbd_upper"] = side_panel("Mesh_DoorPanel_SideUpperStarboard",
                                    False, True)
    made["stbd_lower"] = side_panel("Mesh_DoorPanel_SideLowerStarboard",
                                    False, False)

    # ── Cargo ramp: hinged on its bottom edge, drops aft to the ground ──────
    p = Part(mats)
    hw, ht = RAMP_HW, RAMP_TOP
    x_out = TAIL_X - 0.06
    p.slab((x_out, -hw + 0.03, RAMP_Z0), (x_out + 0.12, hw - 0.03, ht), HULL)
    p.slab((x_out + 0.12, -hw + 0.10, RAMP_Z0 + 0.08),
           (x_out + 0.18, hw - 0.10, ht - 0.08), STEEL)
    for i in range(4):
        y = -hw + 0.32 + i * 0.48
        p.slab((x_out + 0.16, y - 0.05, RAMP_Z0 + 0.05),
               (x_out + 0.22, y + 0.05, ht - 0.04), DARK)
    for s in (-1, 1):
        p.cyl((x_out - 0.03, s * (hw - 0.30), 0.14), 0.028, 0.60, 'Z', 8, STEEL)
    p.box((x_out - 0.03, 0.0, ht - 0.34), (0.05, 0.44, 0.10), DARK)
    p.bevel(width=0.008, segments=1)
    made["ramp"] = p.finish("Mesh_DoorPanel_CargoRamp", coll,
                            origin=(TAIL_X, 0.0, RAMP_Z0))

    # ── Cockpit bulkhead door: hinged on its starboard edge ─────────────────
    p = Part(mats)
    dw, dh = 0.55, 2.10
    x = CAB_FWD + 0.02
    p.slab((x - 0.05, -dw, DECK_TOP), (x + 0.05, dw, DECK_TOP + dh), PANEL)
    p.slab((x + 0.05, -dw + 0.07, DECK_TOP + 0.06),
           (x + 0.09, dw - 0.07, DECK_TOP + dh - 0.06), STEEL)
    for i in range(3):
        z = DECK_TOP + 0.30 + i * 0.62
        p.slab((x + 0.09, -dw + 0.14, z), (x + 0.12, dw - 0.14, z + 0.10),
               DARK)
    # Window and a lever handle.
    p.tube((x, 0.0, DECK_TOP + 1.62), 0.24, 0.06, 0.12, 'X', 16, STEEL)
    p.cyl((x, 0.0, DECK_TOP + 1.62), 0.20, 0.03, 'X', 16, GLASS)
    p.cyl((x + 0.07, -dw + 0.18, DECK_TOP + 1.02), 0.035, 0.10, 'X', 10, DARK)
    p.box((x + 0.14, -dw + 0.32, DECK_TOP + 1.02), (0.05, 0.30, 0.05), STEEL)
    p.box((x + 0.06, dw - 0.10, DECK_TOP + 1.02), (0.04, 0.12, 0.16), AMBER)
    p.bevel(width=0.008, segments=1)
    made["bulkhead"] = p.finish("Mesh_DoorPanel_Bulkhead", coll,
                                origin=(x, dw, DECK_TOP + dh / 2))
    return made


# Wing planform: (y, z at mid-thickness, leading-edge x, chord, thickness).
#
# The spar reaches TIP_Y, 2.77 m outboard of the hull side; the old one stopped
# 0.10 m past it and was a fairing, not a wing. Two things beyond raw length do
# the work. The leading edge sweeps 0.9 m aft over the span, so in plan it is a
# wing rather than a rectangular fin — that read comes almost entirely from the
# sweep. And the nacelle sits at POD_Y with three-quarters of a metre of wing
# still outboard of it, because a pod that covers the whole exposed span turns
# the wing back into the stub pylon this was supposed to stop being.
WING_SECTIONS = (
    (AXLE_Y - 0.14, AXLE_Z,        -1.30, 2.06, 0.62),
    (2.90,          AXLE_Z + 0.03, -1.44, 1.92, 0.54),
    (POD_Y,         AXLE_Z + 0.07, -1.62, 1.74, 0.44),
    (4.40,          AXLE_Z + 0.13, -1.86, 1.46, 0.30),
    (TIP_Y,         AXLE_Z + 0.20, -2.16, 1.12, 0.20),
)


def build_wings(coll, mats):
    """Wing root fairing, the two folding spars, and their axles."""
    made = {}
    # The root straddles the shoulder crease rather than sitting on the roof:
    # the wings now grow out of the hull's own shoulder line, which is where the
    # eye expects a wing root and is why the engines no longer read as roof
    # cargo. It is a faceted fairing, tapered at both ends, not a slab.
    p = Part(mats)

    def root_prof(hw, z0, z1, inset):
        return [(-hw + inset, z0), (hw - inset, z0), (hw, z1), (-hw, z1)]

    p.loft([(-3.55, root_prof(2.14, AXLE_Z - 0.30, 1.62, 0.16)),
            (-3.10, root_prof(2.44, AXLE_Z - 0.38, 1.74, 0.06)),
            (-1.76, root_prof(2.44, AXLE_Z - 0.38, 1.74, 0.06)),
            (-1.30, root_prof(2.14, AXLE_Z - 0.30, 1.62, 0.16))],
           'X', SLATE, cap=True)
    for x in (-2.96, -2.42, -1.88):
        p.slab((x - 0.06, -2.48, AXLE_Z - 0.20), (x + 0.06, 2.48, 1.76), STEEL)
    p.bevel(width=0.010, segments=1)
    made["root"] = p.finish("Mesh_WingRoot_Block", coll)

    def spar(name, s):
        p = Part(mats)
        # Loft along Y; profile coordinates are (x, z). The trailing edge is
        # thinner than the leading edge so the aerofoil has a direction.
        secs = []
        for y, z, le, chord, thick in WING_SECTIONS:
            secs.append((s * y, [(le - chord, z - thick / 2 + 0.05),
                                 (le, z - thick / 2),
                                 (le, z + thick / 2),
                                 (le - chord, z + thick / 2 - 0.03)]))
        p.loft(secs, 'Y', SLATE, cap=True)
        # A single spanwise spar cap along the top. One strip, not three ribs:
        # the wing is read at distance and the fold line is the interesting
        # part, not fastener rows.
        p.loft([(s * y, [(le - chord * 0.62, z + thick / 2 - 0.03),
                         (le - chord * 0.30, z + thick / 2 - 0.03),
                         (le - chord * 0.30, z + thick / 2 + 0.09),
                         (le - chord * 0.62, z + thick / 2 + 0.09)])
                for y, z, le, chord, thick in WING_SECTIONS], 'Y', STEEL,
               cap=True)
        # Pylon saddle under the nacelle, so the pod is carried rather than
        # balanced on the aerofoil.
        p.slab((AXLE_X - 0.74, s * (POD_Y - 0.44), AXLE_Z),
               (AXLE_X + 0.30, s * (POD_Y + 0.44), POD_Z + 0.06), DARK)
        # Tip fin. Cheap, and a wing that ends in a vertical surface reads as a
        # wing from angles where the planform is foreshortened to nothing.
        p.loft([(s * (TIP_Y - 0.02), [(-2.16, AXLE_Z + 0.12),
                                      (-3.28, AXLE_Z + 0.12),
                                      (-3.28, AXLE_Z + 0.28),
                                      (-2.16, AXLE_Z + 0.28)]),
                (s * (TIP_Y + 0.14), [(-2.30, AXLE_Z + 0.74),
                                      (-3.10, AXLE_Z + 0.74),
                                      (-3.10, AXLE_Z + 0.84),
                                      (-2.30, AXLE_Z + 0.84)])],
               'Y', SLATE, cap=True)
        p.bevel(width=0.010, segments=1)
        return p.finish(name, coll, origin=(AXLE_X, s * AXLE_Y, AXLE_Z))

    made["wing_port"] = spar("Mesh_Wing_Port", 1)
    made["wing_stbd"] = spar("Mesh_Wing_Starboard", -1)

    def axle(name, s):
        p = Part(mats)
        p.cyl((AXLE_X, s * AXLE_Y, AXLE_Z), 0.22, 1.36, 'X', 14, STEEL)
        for dx in (-0.56, 0.56):
            p.cyl((AXLE_X + dx, s * AXLE_Y, AXLE_Z), 0.29, 0.14, 'X', 14, DARK)
        p.cyl((AXLE_X, s * AXLE_Y, AXLE_Z), 0.09, 1.72, 'X', 10, DARK)
        p.bevel(width=0.008, segments=1)
        return p.finish(name, coll, origin=(AXLE_X, s * AXLE_Y, AXLE_Z))

    made["axle_port"] = axle("Mesh_WingAxle_Port", 1)
    made["axle_stbd"] = axle("Mesh_WingAxle_Starboard", -1)
    return made


def build_canopy(coll, mats):
    """Windscreen glazing.

    One big raked pane spanning header to sill, 3.1 m across at the top and
    1.6 m of rake — roughly twice the glazed area of the old letterbox, and it
    fills a real opening in the hull instead of being laid over a hole in a flat
    plate. Its edges are the header beam, the cowl lip and the hull's own side
    strakes, so there is nothing for it to overlap.
    """
    p = Part(mats)
    # Width is taken from the hull at the screen's own top and bottom edges, so
    # the pane can never be wider than the opening it sits in — which is what
    # produced the overlapping nose geometry before.
    gh = fwd_hw_at(HEADER_X, HEADER_Z) - 0.18
    gs = fwd_hw_at(SILL_X, SILL_Z) - 0.18
    p.loft([(HEADER_X + 0.02, [(-gh, HEADER_Z - 0.04), (gh, HEADER_Z - 0.04),
                               (gh, HEADER_Z + 0.04), (-gh, HEADER_Z + 0.04)]),
            (SILL_X - 0.02, [(-gs, SILL_Z - 0.04), (gs, SILL_Z - 0.04),
                             (gs, SILL_Z + 0.04), (-gs, SILL_Z + 0.04)])],
           'X', GLASS, cap=True)
    # Quarter lights let into each cockpit flank, raked to match the screen.
    # Two panes, no mullions — the mullions were the other half of the
    # overlapping geometry at the nose.
    for s in (-1, 1):
        w0 = hw_at(2.36, 0.85) - 0.02
        w1 = hw_at(3.00, 0.70) - 0.02
        p.loft([(2.36, [(s * (w0 - 0.08), 0.34), (s * w0, 0.34),
                        (s * w0, 1.12), (s * (w0 - 0.08), 1.12)]),
                (3.00, [(s * (w1 - 0.08), 0.30), (s * w1, 0.30),
                        (s * w1, 0.96), (s * (w1 - 0.08), 0.96)])],
               'X', GLASS, cap=True)
    p.bevel(width=0.004, segments=1)
    return p.finish("Mesh_Canopy_Glass", coll)


def build_liners(coll, mats):
    """Interior wall lining.

    Without this the player stands in the cabin looking at the inside face of
    the hull plating, which is hull-orange — correct for a shell built as
    solids, wrong for a room. The liners only need to cover the strips above
    and below the side openings; the openings themselves are covered by the
    door panels, which carry grey inner faces of their own.
    """
    p = Part(mats)

    def strip(x0, x1, z0, z1, s, mat=PANEL, thick=0.05):
        """A lofted wall panel that hugs the hull's inner face.

        Lofted rather than slabbed because the hull no longer has parallel
        sides: aft of the midbody the wall closes in, and a constant-width
        lining would stand proud of the skin there — or worse, outside it.
        """
        def w(x):
            return hw_at(x, (z0 + z1) / 2.0) - SKIN - 0.03

        p.loft([(x, [(s * w(x), z0), (s * (w(x) - thick), z0),
                     (s * (w(x) - thick), z1), (s * w(x), z1)])
                for x in stations_between(x0, x1)], 'X', mat, cap=True)

    tail_x = MASTER[0][0] + 0.45
    for s in (-1, 1):
        # Cant rail: the wall strip between ceiling level and the top of the
        # opening, which is what the player sees when a clamshell swings up.
        strip(CAB_AFT + 0.14, CAB_FWD - 0.06, CEIL_BOT, OPEN_Z1, s)
        # Forward and aft of the opening, where the wall is solid full height.
        strip(CAB_AFT + 0.14, OPEN_X0 - 0.02, DECK_TOP, CEIL_BOT, s)
        strip(OPEN_X1 + 0.02, CAB_FWD - 0.06, DECK_TOP, CEIL_BOT, s)
        # The tail is walkable too — the ramp lands in it — so it gets the same
        # lining rather than opening into a black void at the back of the bay.
        strip(tail_x, CAB_AFT + 0.14, DECK_TOP, CEIL_BOT, s)
    # Tail ceiling, and the aft face either side of and over the cargo doorway.
    p.loft([(x, [(-inside_hw(x, CEIL_BOT, 1.70), CEIL_BOT - 0.06),
                 (inside_hw(x, CEIL_BOT, 1.70), CEIL_BOT - 0.06),
                 (inside_hw(x, CEIL_BOT, 1.70), CEIL_BOT),
                 (-inside_hw(x, CEIL_BOT, 1.70), CEIL_BOT)])
            for x in stations_between(tail_x, CAB_AFT + 0.14)],
           'X', PANEL, cap=True)
    aft_face = MASTER[0][0] + 0.10
    aw = hw_at(aft_face, 0.0) - SKIN - 0.03
    for s in (-1, 1):
        p.slab((aft_face, s * 1.05, DECK_TOP), (aft_face + 0.06, s * aw,
                                                CEIL_BOT), PANEL)
    p.slab((aft_face, -1.05, 1.10), (aft_face + 0.06, 1.05, CEIL_BOT), PANEL)
    # Forward bulkhead lining either side of the cockpit door.
    fw = hw_at(CAB_FWD, 0.0) - SKIN - 0.03
    for s in (-1, 1):
        p.slab((CAB_FWD - 0.06, s * 0.72, DECK_TOP),
               (CAB_FWD, s * fw, CEIL_BOT), PANEL)
    p.slab((CAB_FWD - 0.06, -0.72, DECK_TOP + 2.10), (CAB_FWD, 0.72, CEIL_BOT),
           PANEL)
    p.bevel(width=0.008, segments=1)
    return p.finish("Mesh_Interior_Liner", coll)


# ── Component placement ─────────────────────────────────────────────────────

_protos = {}


def proto(rel_path, obj_name):
    """Append a component's mesh data once and cache it.

    The appended *object* is dropped immediately: only its mesh is wanted, and
    an appended object that is never linked to a collection still shows up in
    bpy.data, which silently doubles every triangle count taken off the file.
    """
    key = (rel_path, obj_name)
    if key not in _protos:
        path = os.path.join(COMPONENTS, rel_path)
        with bpy.data.libraries.load(path, link=False) as (src, dst):
            if obj_name not in src.objects:
                raise SystemExit("%s has no object %r" % (path, obj_name))
            dst.objects = [obj_name]
        holder = dst.objects[0]
        data = holder.data
        data.name = "Data_" + obj_name
        bpy.data.objects.remove(holder)
        _protos[key] = data
    return _protos[key]


def place(rel_path, obj_name, new_name, coll, loc, rot=(0, 0, 0), mirror=False):
    """Instance a library component into the model.

    Components are appended rather than linked: the ship has to survive an FBX
    export into Unity, and a linked datablock is one more thing that can fail to
    resolve on a machine that does not have the library beside it. The component
    files stay authoritative for editing.

    `loc` is where the component's own origin lands — the mesh data is already
    expressed relative to that origin, so no further offset is applied.
    """
    src = proto(rel_path, obj_name)
    if mirror:
        data = src.copy()
        data.name = "Data_" + new_name
        data.transform(Matrix.Diagonal((1.0, -1.0, 1.0, 1.0)))
        data.flip_normals()
    else:
        data = src
    obj = bpy.data.objects.new(new_name, data)
    obj.location = Vector(loc)
    obj.rotation_euler = rot
    coll.objects.link(obj)
    return obj


def build_engines(coll):
    """Two wing pods, and two attitude jets on the tail.

    The 3.8 x 3.6 x 3.0 m stern drive is gone. It was carried on the aft roof —
    it did not fit anywhere else — and it dominated the silhouette from every
    angle while doing nothing the wing pods do not already do. Two turbines on
    two wings is a complete propulsion story, so the block was 8k triangles
    buying negative value. The nose RCS pod went with it: it sat on the bonnet
    directly under the new windscreen's sightline.
    """
    # The pod's origin is its top mount saddle, so it is rolled 180 degrees to
    # put the saddle underneath, where the wing's pylon meets it. Both pods use
    # the same orientation rather than mirroring: they are a matched pair off one
    # production line, and a mirrored greeble pattern reads no differently.
    roll = (math.pi, 0.0, 0.0)
    for s, side in ((1, "Port"), (-1, "Starboard")):
        place("mechanical/thruster_nacelle.blend", "Mesh_Thruster_Main",
              "Mesh_Thruster_Main" + side, coll,
              (AXLE_X, s * POD_Y, POD_Z), roll)
    # Attitude jets either side of the cargo ramp, firing aft. Small, cheap, and
    # they keep the tail from ending in a bare plate now the stern drive is off.
    for s, side in ((1, "Port"), (-1, "Starboard")):
        place("mechanical/thruster_nacelle.blend", "Mesh_Thruster_Vernier",
              "Mesh_Thruster_Vernier" + side, coll,
              (MASTER[0][0] + 0.06, s * 1.24, 0.30),
              (0.0, math.radians(-90), 0.0))


def build_cockpit(coll):
    """Bridge: console, helm wheel, and the two chairs."""
    place("props/console_panel.blend", "Mesh_ConsolePanel_Helm",
          "Mesh_Bridge_Console", coll, (2.62, 0.0, DECK_TOP),
          (0, 0, math.radians(180)))
    # The wheel lies in its own XY plane; yaw it to face aft, then rake it back
    # toward the pilot the way ShipRVBuilder always tilted the placeholder.
    # The Twin yoke rather than the Ring: 4.0k triangles against 9.3k for a part
    # the player only ever sees from behind, and two sticks read as a ship's
    # controls where a rim read as a van's steering wheel.
    place("props/steering_yoke.blend", "Mesh_SteeringYoke_Twin",
          "Mesh_Bridge_Wheel", coll, (2.30, 0.42, DECK_TOP + 1.06),
          (math.radians(65), 0.0, math.radians(90)))
    place("props/crew_seat.blend", "Mesh_CrewSeat_Pilot",
          "Mesh_Bridge_SeatPilot", coll, (1.72, 0.42, DECK_TOP))
    place("props/crew_seat.blend", "Mesh_CrewSeat_Copilot",
          "Mesh_Bridge_SeatCopilot", coll, (1.72, -0.52, DECK_TOP),
          mirror=True)
    # Overhead switch panel and the breaker board were cut for budget — 6.4k
    # and 6.1k triangles for surfaces the pilot never faces. Both are in the
    # library (Coll_ConsolePanel_Overhead / _Breaker) if they are wanted back.


def build_interior(coll):
    """The RV fit-out: berth, galley, storage, services and lighting."""
    # Port wall, running aft from the bulkhead. ShipRVBuilder measures the
    # repair workstation off the port lower panel and lands it at the panel's
    # mid-length — x -2.2..-1.0 here — so the galley and locker are kept
    # forward of that and the open shelving well aft of it.
    place("props/galley_unit.blend", "Mesh_Galley_Compact",
          "Mesh_Interior_Galley", coll, (0.55, DECK_Y - 0.02, DECK_TOP),
          (0, 0, math.radians(-90)))
    place("props/wall_locker.blend", "Mesh_WallLocker_Tall",
          "Mesh_Interior_LockerTall", coll, (-0.45, DECK_Y - 0.02, DECK_TOP),
          (0, 0, math.radians(-90)))
    place("props/wall_locker.blend", "Mesh_WallLocker_OpenShelf",
          "Mesh_Interior_Shelf", coll, (-4.55, DECK_Y - 0.02, DECK_TOP),
          (0, 0, math.radians(-90)))
    # Starboard wall.
    place("props/bunk.blend", "Mesh_Bunk_Stacked",
          "Mesh_Interior_Bunk", coll, (-1.30, -DECK_Y + 0.02, DECK_TOP),
          (0, 0, math.radians(90)))
    place("props/wall_locker.blend", "Mesh_WallLocker_Dented",
          "Mesh_Interior_LockerDented", coll, (-4.30, -DECK_Y + 0.02,
                                               DECK_TOP),
          (0, 0, math.radians(90)))
    place("props/crew_seat.blend", "Mesh_CrewSeat_Stool",
          "Mesh_Interior_Stool", coll, (-2.90, -0.95, DECK_TOP))

    # Deck accents over the plain substrate. Two, not four — the hatch and the
    # grate are the two that read as something rather than as texture.
    place("structural/deck_plate.blend", "Mesh_DeckPlate_Grate",
          "Mesh_Deck_GrateFwd", coll, (-0.60, -0.50, DECK_TOP - 0.06))
    place("structural/deck_plate.blend", "Mesh_DeckPlate_Hatch",
          "Mesh_Deck_Hatch", coll, (-3.10, -0.50, DECK_TOP - 0.06))

    # Ceiling lighting down the centre channel. The beacon, festoon, duct, cable
    # bundle, manifold, vent and extract fan are all cut — 15k triangles of
    # ceiling and corner clutter in a bay lit by three strip lights, none of it
    # visible in silhouette and most of it behind the player. All still in the
    # library if the interior is ever the subject rather than the backdrop.
    for i, x in enumerate((-4.90, -2.60, -0.30)):
        place("props/light_fixture.blend", "Mesh_Light_Strip",
              "Mesh_Interior_Light%d" % i, coll, (x, 0.0, CEIL_BOT + 0.02),
              (0, 0, math.radians(90)))
    # Doorway surrounds.
    place("structural/bulkhead_frame.blend", "Mesh_BulkheadFrame_Door",
          "Mesh_Interior_FrameBulkhead", coll, (CAB_FWD, 0.0, DECK_TOP),
          (0, 0, math.radians(90)))
    # On the cabin/tail joint rather than deeper aft. The frame is 2.87 m tall
    # and stands above the ceiling into the roof void; back at x=-7.10 the hull
    # has narrowed enough that its top corners came out through the shoulder.
    place("structural/bulkhead_frame.blend", "Mesh_BulkheadFrame_Reinforced",
          "Mesh_Interior_FrameCargo", coll, (-6.55, 0.0, DECK_TOP),
          (0, 0, math.radians(90)))


def build_exterior_detail(coll):
    """Hull plating accents and the bolted-on clutter that sells the wear.

    Cut hard from the previous pass. The hull now carries its own read — five
    facets, two chine rails, a long taper — and the greebles were competing with
    it rather than supporting it; scattered vents, scoops, belly plates and a
    lamp on a hull that was already a plated box is what made the ship look
    fussy. What is left is two roof plates and a patch, which break up the one
    genuinely large flat area, and the ramp hinges, which are mechanism.
    """
    flat = "structural/hull_plate.blend"
    # Roof plates on the aft roof — the only surface big enough to need them.
    for i, x in enumerate((-5.20, -4.30)):
        place(flat, "Mesh_HullPlate_Flat", "Mesh_Hull_RoofPlate%d" % i, coll,
              (x, -0.48, station(x)[2] - 0.01))
    place(flat, "Mesh_HullPlate_Patched", "Mesh_Hull_RoofPatch", coll,
          (-0.60, -0.48, TOP - 0.01))
    # Hinges on the two cargo-ramp pivots, where the mechanism is visible.
    for s in (-1, 1):
        place("mechanical/hinge_heavy.blend", "Mesh_Hinge_Strap",
              "Mesh_Hull_RampHinge%s" % ("Port" if s > 0 else "Starboard"),
              coll, (TAIL_X + 0.02, s * 0.70, RAMP_Z0),
              (0, math.radians(90), 0))


# ── Rig ─────────────────────────────────────────────────────────────────────

def build_rig(coll, doors, wings):
    """Armature over every moving part.

    Bones sit exactly on the hinge axes the panels were modelled around, so the
    .blend animates correctly on its own. Meshes are parented rigidly to bones
    rather than skinned — a hinge that deforms at the pivot smears.
    """
    arm_data = bpy.data.armatures.new("Arm_ShipRV")
    arm = bpy.data.objects.new("Arm_ShipRV", arm_data)
    coll.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')

    mid_z = (OPEN_Z0 + OPEN_Z1) / 2
    spec = [
        ("Bone_Root", (0, 0, DECK_TOP), (0, 0, DECK_TOP + 0.6), None),
        ("Bone_WingPort", (AXLE_X, AXLE_Y, AXLE_Z),
         (AXLE_X + 1.22, AXLE_Y, AXLE_Z), "Bone_Root"),
        ("Bone_WingStarboard", (AXLE_X, -AXLE_Y, AXLE_Z),
         (AXLE_X + 1.22, -AXLE_Y, AXLE_Z), "Bone_Root"),
        ("Bone_PanelPortUpper", (0, SIDE_Y, OPEN_Z1),
         (0, SIDE_Y + 0.5, OPEN_Z1), "Bone_Root"),
        ("Bone_PanelPortLower", (0, SIDE_Y, mid_z + 0.07),
         (0, SIDE_Y + 0.5, mid_z + 0.07), "Bone_Root"),
        ("Bone_PanelStarboardUpper", (0, -SIDE_Y, OPEN_Z1),
         (0, -SIDE_Y - 0.5, OPEN_Z1), "Bone_Root"),
        ("Bone_PanelStarboardLower", (0, -SIDE_Y, mid_z + 0.07),
         (0, -SIDE_Y - 0.5, mid_z + 0.07), "Bone_Root"),
        ("Bone_CargoRamp", (TAIL_X, 0, RAMP_Z0),
         (TAIL_X - 0.64, 0, RAMP_Z0), "Bone_Root"),
        ("Bone_BulkheadDoor", (CAB_FWD + 0.02, 0.55, DECK_TOP),
         (CAB_FWD + 0.02, 0.55, DECK_TOP + 2.10), "Bone_Root"),
    ]
    for name, head, tail, parent in spec:
        b = arm_data.edit_bones.new(name)
        b.head, b.tail = head, tail
        if parent:
            b.parent = arm_data.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')

    binding = [
        (doors["port_upper"], "Bone_PanelPortUpper"),
        (doors["port_lower"], "Bone_PanelPortLower"),
        (doors["stbd_upper"], "Bone_PanelStarboardUpper"),
        (doors["stbd_lower"], "Bone_PanelStarboardLower"),
        (doors["ramp"], "Bone_CargoRamp"),
        (doors["bulkhead"], "Bone_BulkheadDoor"),
        (wings["wing_port"], "Bone_WingPort"),
        (wings["axle_port"], "Bone_WingPort"),
        (wings["wing_stbd"], "Bone_WingStarboard"),
        (wings["axle_stbd"], "Bone_WingStarboard"),
    ]
    for obj, bone in binding:
        world = obj.matrix_world.copy()
        obj.parent = arm
        obj.parent_type = 'BONE'
        obj.parent_bone = bone
        obj.matrix_world = world
    return arm


# ── Main ────────────────────────────────────────────────────────────────────

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    root = collection("Coll_ShipRV")
    unique = collection("Coll_ShipRV_Unique", root)
    comps = collection("Coll_ShipRV_Components", root)
    rig = collection("Coll_ShipRV_Rig", root)

    build_shells(unique, mats)
    build_deck(unique, mats)
    build_liners(unique, mats)
    doors = build_doors(unique, mats)
    wings = build_wings(unique, mats)
    build_canopy(unique, mats)

    build_engines(comps)
    build_cockpit(comps)
    build_interior(comps)
    build_exterior_detail(comps)

    build_rig(rig, doors, wings)

    print("\n=== ShipRV")
    total = report()
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in bpy.data.objects:
        if o.type != 'MESH':
            continue
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])
    print("  BOUNDS  %s -> %s" % (tuple(round(v, 2) for v in lo),
                                  tuple(round(v, 2) for v in hi)))
    print("  SIZE    %s   (original 12.71 x 6.16 x 6.57)"
          % (tuple(round(v, 2) for v in (hi - lo)),))
    print("  TRIS    %d" % total)
    save(out)


# Guarded so the shape functions above (station, hw_at, fwd_hw_at, the tables)
# can be imported and queried by a checking script without triggering a build.
if __name__ == "__main__":
    main()
