"""Sprayer kit — the shared parts every issued sprayer in the set is built from.

    blender --background --python sprayer_kit.py -- --out sprayer_kit.blend

## What this is

Nine hand-held items share one look: **clean issued equipment**. A moulded
white shell, a bare-steel reservoir clamped to it, one moulded grip, one
trigger, and a `SupplyGauge` plate on every tank so the level is readable
without opening a menu. This module is that kit: the shapes are here, the
*colour* is the caller's, and the assembled models are elsewhere.

It follows `_console_kit.py` and `models/gear/_gauntlet.py` — a family kit sits
beside its components and is **imported, not copied**, so nine models cannot
end up with nine subtly different gauge plates.

## Two ways to use it

1. **Import the builders.** `sprayer_kit.tank(p, ...)`, `.gauge_plate(...)`,
   `.grip_moulded(...)`, `.nozzle_bell(...)` all take a `TrackedPart` and
   material *indices*, so a model builds a kit part in its own accent colour
   inside its own object. This is what the four sprayer models do, and it is
   what makes a blue cryo bottle and an orange fuel bottle the same part.
2. **Append the built variations.** `sprayer_kit.blend` holds one finished copy
   of each part in its own `Coll_SprayerKit_*` collection, in the kit's neutral
   colours. Append those when a model wants the part exactly as issued.

## The material contract

Every model in the family opens its table with the same eight entries, in the
same order, and puts its function colour at index 8:

    MATS = KIT_MATS + ["Mat_Paint_Safety_Orange", ...]

That is the whole contract. A builder only ever addresses 0..7 plus whatever
index the caller hands it, so a part can be built in any colour without the
kit knowing which. Index 0 is a structural metal on purpose:
`bmesh.ops.bevel` stamps every face it creates with material index 0.

## The gauge is geometry, not decoration

`OxygenGearBuilder` finds a supply gauge by **material**, not by submesh index:
it looks for `Mat_Emissive_Green_CRT` on a named mesh, measures the lit slab's
own vertices, and lays a track and a fill bar over it. So `gauge_plate` builds
a lit rectangle that is *centred on its own object's middle in both in-plane
axes* — the builder mirrors the rect about the gauge mesh's centre, and a strip
sitting off-centre produces a bar that is too long and visibly skewed (the
bottle's 36% error, `SupplyGauge.md`). Keep the plate symmetric about the strip
and the mirror is a no-op.

## Frame

Library convention: +Z up, −Y forward, 1 unit = 1 m. The gun-shaped members of
the family are built with the muzzle at negative Y, matching `net_gun`,
`gravel_blaster` and `dragon_bazooka`.

Generation script — historical record for `sprayer_kit.blend`. The .blend is
the source of truth; never re-run the `main()` here over the file it produced.
The builder functions above it are a live module and are meant to be imported.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
if _LIB not in sys.path:
    sys.path.insert(0, _LIB)

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

from _buildlib import (Part, collection, link_materials, parse_out,  # noqa: E402
                       report, save, start)
from _tracked import TrackedPart  # noqa: E402

# ---------------------------------------------------------------------------
# The shared material table
# ---------------------------------------------------------------------------

DARK, SHELL, GREY, BLACK, RUBBER, CHROME, CRT, WORN = range(8)
ACCENT = 8                       # by convention, the caller's function colour

KIT_MATS = [
    "Mat_Metal_Steel_Dark",       # 0 DARK   frames, barrels, bevel fallback
    "Mat_Paint_White_Arctic",     # 1 SHELL  the issued moulded shell
    "Mat_Neutral_Panel_Grey",     # 2 GREY   secondary mouldings, guards
    "Mat_Neutral_Black_Matte",    # 3 BLACK  gauge backing, seals, recesses
    "Mat_Plastic_Rubber_Black",   # 4 RUBBER hoses, grip pads, boots
    "Mat_Metal_Chrome_Scuffed",   # 5 CHROME collars, bezels, clamp bands
    "Mat_Emissive_Green_CRT",     # 6 CRT    the gauge's lit contents strip
    "Mat_Metal_Steel_Worn",       # 7 WORN   bare-steel reservoirs and clamps
]

# 4 mm reads as melted on a 0.5 m item; 2 mm is the family's edge softening.
BEVEL_W = 0.002

# --------------------------------------------------------------------------
# The gauge — one shape, nine models
# --------------------------------------------------------------------------

# A gauge plate is 90 x 26 mm of face with a 62 x 12 mm lit strip in it. The
# numbers are the oxygen bottle's own (`Mesh_OxygenTank_Gauge`, 0.090 x 0.026 x
# 0.0675) rounded to this family's smaller items, so a player who has read one
# reads all of them (GDC-L1-UX-0004).
GAUGE_L, GAUGE_H = 0.090, 0.030
GAUGE_STRIP_L, GAUGE_STRIP_H = 0.062, 0.012

# Three plates stacked outward, each embedded in the one under it — the oxygen
# bottle's own construction. Offsets are from the skin, along `out`:
#
#   bezel  -0.0040 .. +0.0040   half sunk into the tank
#   well   +0.0020 .. +0.0070   2 mm into the bezel
#   strip  +0.0045 .. +0.0085   2.5 mm into the well, 1.5 mm proud of it
#
# Nothing meets on a plane at any step, which is the rule the model scripts
# themselves warn about and the rule `SupplyGauge` restates for the fill bar.
GAUGE_BEZEL_T = 0.0080
GAUGE_WELL_T = 0.0050
GAUGE_STRIP_T = 0.0040
GAUGE_WELL_C = 0.0045
GAUGE_STRIP_C = 0.0065


def gauge_plate(p, centre, out, along, length=GAUGE_L, height=GAUGE_H,
                accent=None):
    """The supply gauge: a dark plate standing proud of a tank, with a lit strip.

    `out` is the unit vector pointing out of the model at this point, `along`
    the direction the bar runs in. Both are world-space in the build frame; the
    plate is built from that basis rather than from an axis letter so it can sit
    on a flank that does not face down an axis.

    The strip shares the plate's centre in both in-plane axes — see the module
    docstring for why that is not an aesthetic choice.

    A `accent` index, if given, adds two end caps in the model's function
    colour, so the gauge is colour-coded to its item without the *reading*
    depending on colour (GDC-L1-UX-0006).
    """
    out = Vector(out).normalized()
    along = Vector(along).normalized()
    up = out.cross(along).normalized()
    along = up.cross(out).normalized()          # re-orthogonalise, never assume
    rot = Matrix((along, up, out)).transposed().to_4x4()
    c = Vector(centre)

    lg, hg = length, height
    sl, sh = GAUGE_STRIP_L, GAUGE_STRIP_H

    faces = []
    faces += p.box(c, (lg, hg, GAUGE_BEZEL_T), BLACK, rot=rot)
    faces += p.box(c + out * GAUGE_WELL_C,
                   (lg - 0.010, hg - 0.008, GAUGE_WELL_T), DARK, rot=rot)
    # The lit contents strip — the only face wearing Mat_Emissive_Green_CRT,
    # which is the handle `OxygenGearBuilder` finds the instrument by.
    p.box(c + out * GAUGE_STRIP_C, (sl, sh, GAUGE_STRIP_T), CRT, rot=rot)
    if accent is not None:
        for s in (-1, 1):
            faces += p.box(c + out * GAUGE_WELL_C
                           + along * (s * (lg / 2 - 0.007)),
                           (0.008, hg - 0.010, GAUGE_WELL_T * 1.1), accent,
                           rot=rot)
    return faces


# --------------------------------------------------------------------------
# Reservoirs
# --------------------------------------------------------------------------

def tank(p, centre, axis, radius, length, body=WORN, cap=CHROME, seg=20,
         dome=0.30, bands=2, band_mat=CHROME):
    """A capped pressure vessel: a domed cylinder with rolled end caps.

    `dome` is the end dome's depth as a fraction of the radius — 0 gives a flat
    can, 1 a hemisphere. Built as one loft so the shoulder is continuous; a
    cylinder with a separate cap has a seam that flickers wherever the two
    radii agree.

    `bands` chrome hoops ride the barrel. They are part of the same mesh: they
    are not separately useful, and separating them would put two faces on the
    barrel's own surface.
    """
    def ring(r):
        return [(r * math.cos(2 * math.pi * i / seg),
                 r * math.sin(2 * math.pi * i / seg)) for i in range(seg)]

    d = radius * dome
    half = length / 2.0
    stations = [(-half, radius * 0.30), (-half + d * 0.45, radius * 0.80),
                (-half + d, radius), (half - d, radius),
                (half - d * 0.45, radius * 0.80), (half, radius * 0.30)]
    off = Vector(centre)
    faces = p.loft([(w, ring(r)) for w, r in stations], axis=axis, mat=body,
                   cap=True)
    # loft() builds around the origin of the chosen axis plane, so move it.
    _translate(p, faces, off)

    # The two rolled caps, in the cap colour, sunk into the domes.
    for s in (-1, 1):
        faces += p.cyl(off + _vec(axis) * (s * (half - d * 0.25)),
                       radius * 0.62, radius * 0.34, axis=axis, seg=seg,
                       mat=cap, radius_top=radius * 0.44)
    for i in range(bands):
        t = (i + 1) / (bands + 1)
        w = -half + d + (length - 2 * d) * t
        faces += p.cyl(off + _vec(axis) * w, radius * 1.035, radius * 0.16,
                       axis=axis, seg=seg, mat=band_mat)
    return faces


def _vec(axis):
    return {'X': Vector((1, 0, 0)), 'Y': Vector((0, 1, 0)),
            'Z': Vector((0, 0, 1))}[axis]


def _translate(p, faces, delta):
    """Move exactly the vertices of `faces`. `loft` and `prism` build about the
    origin, and a whole-part translate would drag every earlier part with it."""
    if delta.length_squared == 0:
        return faces
    verts = {v for f in faces for v in f.verts}
    for v in verts:
        v.co += delta
    return faces


# --------------------------------------------------------------------------
# Shell
# --------------------------------------------------------------------------

def rounded_rect(hu, v0, v1, r_top, r_bot, seg_top=6, seg_bot=3):
    """A rounded rectangle profile, counter-clockwise from the bottom right.

    Lifted from `gauntlet_flashlight.rounded_profile` — it is the shape every
    moulded shell in this library is a prism of.
    """
    pts = []

    def corner(cu, cv, r, a0, a1, n):
        for i in range(n + 1):
            a = math.radians(a0 + (a1 - a0) * i / n)
            pts.append((cu + r * math.cos(a), cv + r * math.sin(a)))

    corner(hu - r_bot, v0 + r_bot, r_bot, 270, 360, seg_bot)
    corner(hu - r_top, v1 - r_top, r_top, 0, 90, seg_top)
    corner(-hu + r_top, v1 - r_top, r_top, 90, 180, seg_top)
    corner(-hu + r_bot, v0 + r_bot, r_bot, 180, 270, seg_bot)
    return pts


def shell(p, hx, z0, z1, y0, y1, mat=SHELL, r_top=0.026, r_bot=0.014,
          taper=1.0):
    """The moulded body: a rounded-rectangle section lofted down −Y.

    `taper` scales the front station, which is what stops the family's shells
    reading as extruded bricks. Only the curved faces are smooth-shaded, so the
    flat flanks keep their edges.
    """
    back = rounded_rect(hx, z0, z1, r_top, r_bot)
    zc = (z0 + z1) / 2.0
    front = [(u * taper, zc + (v - zc) * taper) for u, v in back]
    faces = p.loft([(y0, front), (y1, back)], axis='Y', mat=mat, cap=True)
    for f in faces:
        n = f.normal
        f.smooth = abs(n.y) < 0.9 and max(abs(n.x), abs(n.z)) < 0.999
    return faces


# --------------------------------------------------------------------------
# The hand: one moulded grip, one trigger, for the whole family
# --------------------------------------------------------------------------

# Grip numbers are the human hand's, not the item's, so they do NOT scale with
# the model: a 0.9 m lance and a 0.5 m pistol are held by the same hand
# (GDC-L1-UX-0005). Taken from `weapon_grip.pistol`, which was fitted once.
GRIP_LEN = 0.132
GRIP_RAKE = 0.34                 # backward run per unit of drop, not an angle


def grip_moulded(p, top, accent=None, length=GRIP_LEN, rake=GRIP_RAKE,
                 trigger=True, guard=True):
    """The kit's grip: a rubber-over-shell pistol grip with trigger and guard.

    This is the family's signature part — the same grip on every issued item,
    which is what a kit *is* (GDC-L1-UX-0004). It is deliberately not
    `components/mechanical/weapon_grip.blend`'s `Coll_WeaponGrip_Pistol`: that
    one has wooden cheeks and a canvas wrap, which is a scavenged weapon's
    language, not issued equipment's.

    The grip is ONE loft whose stations walk backward as they descend — the
    rake lives in the station offsets, because a stack of rotated boxes turns
    into loose confetti (`weapon_grip.pistol` records that failure).

    `top` is where the grip meets the body, on the body's underside. Every
    item in this family is symmetric about x = 0 and the grip's own panels are
    placed at absolute ±x, so an off-centre grip is refused rather than built
    silently wrong.

    Returns the hard faces to bevel; the palm point is `grip_palm(top)`.
    """
    top = Vector(top)
    if abs(top.x) > 1e-6:
        raise ValueError("grip_moulded is built about x = 0; got x=%.4f"
                         % top.x)

    def oval(hw, hd, yc, n=12, squash=0.7):
        pts = []
        for i in range(n):
            a = 2 * math.pi * i / n
            cs, sn = math.cos(a), math.sin(a)
            pts.append((hw * math.copysign(abs(cs) ** squash, cs),
                        yc + hd * math.copysign(abs(sn) ** squash, sn)))
        return pts

    stations = ((0.00, 0.020, 0.030), (0.26, 0.023, 0.033),
                (0.58, 0.023, 0.031), (0.84, 0.021, 0.027),
                (1.00, 0.018, 0.022))
    core = p.loft([(top.z - length * t,
                    oval(hw, hd, top.y + rake * length * t))
                   for t, hw, hd in stations], axis='Z', mat=RUBBER)
    for f in core:
        f.smooth = abs(f.normal.z) < 0.9

    hard = []
    # One moulded side panel per side, LOFTED down the same raked stations as
    # the core rather than stacked out of blocks. Stacked blocks were the first
    # cut: each one is straight, the rake is not, and the panel came out as a
    # visible staircase down the side of the grip.
    # The panel starts at t = 0, flush with `top` and therefore inside the body
    # it hangs off. Starting it at 0.10 left 13 mm of bare dark core between the
    # white panel and the white shell, which read as a grip floating clear of
    # the gun — the panel is what joins the two visually, so it has to reach.
    panel_t = (0.0, 0.26, 0.50, 0.74, 0.94)
    for sx in (-1, 1):
        sections = []
        for t in panel_t:
            y = top.y + rake * length * t
            hd = 0.022 - 0.006 * t                    # narrows toward the heel
            sections.append((top.z - length * t,
                             [(sx * 0.017, y - hd), (sx * 0.025, y - hd + 0.003),
                              (sx * 0.025, y + hd - 0.003), (sx * 0.017, y + hd)]))
        if sx > 0:                                    # keep both windings CCW
            sections = [(w, list(reversed(prof))) for w, prof in sections]
        hard += p.loft(sections, axis='Z', mat=SHELL, cap=True)
        # One accent chip high on the panel, where the thumb web sits: the
        # function colour reaches the hand without the grip becoming a stripe.
        if accent is not None:
            hard += p.box((sx * 0.024, top.y + rake * length * 0.20,
                           top.z - length * 0.20),
                          (0.005, 0.024, 0.016), accent)

    # Trigger: a blade hanging forward of the grip's top, and its guard bow.
    # Both are optional — the same part serves as a support-hand foregrip, and
    # a second trigger under the forend would be a lie about the controls.
    if trigger:
        ty, tz = top.y - 0.030, top.z - 0.040
        hard += p.box((0.0, ty, tz), (0.014, 0.010, 0.044), DARK,
                      rot=Matrix.Rotation(math.radians(-14), 4, 'X'))
    if guard:
        bow = [(0.0, top.y - 0.052, top.z - 0.014),
               (0.0, top.y - 0.058, top.z - 0.046),
               (0.0, top.y - 0.040, top.z - 0.068),
               (0.0, top.y - 0.006, top.z - 0.070)]
        for a, b in zip(bow, bow[1:]):
            a, b = Vector(a), Vector(b)
            d = b - a
            hard += p.cyl((a + b) / 2.0, 0.007, d.length * 1.25, axis='Z',
                          seg=8, mat=GREY,
                          rot=d.to_track_quat('Z', 'Y').to_matrix().to_4x4())
    return core + hard


def grip_palm(top, length=GRIP_LEN, rake=GRIP_RAKE):
    """Where the hand closes on `grip_moulded` — inside the grip, on its axis.

    A marker on the grip's *surface* holds the item a centimetre clear of the
    palm; the hand closes around the grip, not onto it (`net_gun.py`).
    """
    top = Vector(top)
    t = 0.44
    return Vector((0.0, top.y + rake * length * t, top.z - length * t))


def trigger_collar(p, centre, radius, axis='Z', accent=None, span=0.052):
    """The can's answer to a grip: a thumb trigger on a collar, with a guard.

    The slick can has no pistol grip — the hand wraps the can body — so the
    trigger has to sit where a thumb lands and the guard has to keep a
    hand-full of can off it. Same parts as `grip_moulded`, different mounting.
    """
    c = Vector(centre)
    faces = p.cyl(c, radius * 1.06, 0.026, axis=axis, seg=24, mat=GREY)
    # The lever, lying forward of the collar along −Y.
    faces += p.box(c + Vector((0.0, -radius * 0.86, 0.006)),
                   (0.030, radius * 0.62, 0.012), DARK,
                   rot=Matrix.Rotation(math.radians(9), 4, 'X'))
    # Guard bow under the lever, standing clear of it.
    bow = [c + Vector((0.0, -radius * 0.30, -0.024)),
           c + Vector((0.0, -radius * 1.10, -0.030)),
           c + Vector((0.0, -radius * 1.34, -0.006)),
           c + Vector((0.0, -radius * 1.30, span * 0.30))]
    for a, b in zip(bow, bow[1:]):
        d = b - a
        faces += p.cyl((a + b) / 2.0, 0.007, d.length * 1.3, axis='Z', seg=8,
                       mat=SHELL if accent is None else accent,
                       rot=d.to_track_quat('Z', 'Y').to_matrix().to_4x4())
    return faces


# --------------------------------------------------------------------------
# Nozzles — the half of the silhouette that says what the item does
# --------------------------------------------------------------------------

def nozzle_bell(p, at, throat_r, mouth_r, depth, mat=SHELL, lip=CHROME,
                seg=28, wall=0.006, axis='Y'):
    """A flared bell: an OPEN horn from throat to mouth, with a rolled lip.

    The foam gun's read. A bell says 'this comes out wide and slow' before the
    player has fired it (GDC-L1-UX-0004), which is the opposite claim to the
    flamethrower's narrow lance.

    Built as a closed loop of rings — outer surface forward to the mouth, the
    rim, inner surface back to the throat, throat annulus, close — rather than
    as a capped loft. A capped loft is a solid cone, and a solid cone read as a
    white disc filling the mouth, hiding the shutter behind it. `cap=False` on
    its own would leave a one-sided surface that shows its backfaces the moment
    the item swings past the camera; the same loop trick as
    `gauntlet_flashlight.reflector`.
    """
    at = Vector(at)
    ax = _vec(axis)

    def ring(r):
        return [(r * math.cos(2 * math.pi * i / seg),
                 r * math.sin(2 * math.pi * i / seg)) for i in range(seg)]

    d = depth
    # Front is −axis: the mouth is `depth` ahead of the throat.
    outer = [(0.0, throat_r), (-d * 0.35, throat_r * 1.25),
             (-d * 0.72, mouth_r * 0.78), (-d, mouth_r)]
    inner = [(-d + 0.004, mouth_r - wall),
             (-d * 0.72 + 0.004, mouth_r * 0.78 - wall),
             (-d * 0.35, throat_r * 1.25 - wall), (0.0, throat_r - wall)]
    loop = outer + inner + [outer[0]]
    faces = p.loft([(w, ring(r)) for w, r in loop], axis=axis, mat=mat,
                   cap=False)
    _translate(p, faces, at)
    faces += p.torus(at - ax * d, mouth_r * 0.985, mouth_r * 0.075,
                     axis=axis, maj_seg=seg, min_seg=8, mat=lip)
    return faces


def iris_vanes(p, at, mouth_r, count=6, mat=CHROME, open01=0.45, cant=26.0,
               outer=0.90, inset=0.020):
    """The bell's shutter, as overlapping vanes set into the mouth.

    Bell axis is Y, like every nozzle in this family, so the vane ring lies in
    XZ and one rotation about Y places every blade. Composing a second axis
    letter for a general case is exactly the kind of guess the library's
    rotation rule exists to stop.

    Modelled part-open on purpose: a shutter drawn shut is a disc and a shutter
    drawn fully open is nothing at all. Part-open is the only state that shows
    the player there *is* an iris, and it leaves the wave-2 rig something to
    turn (GDC-L1-UX-0004).

    Each vane is canted `cant` degrees about its own radial axis, so
    consecutive blades overlap in depth rather than meeting edge to edge.

    `outer` and `inset` keep the ring **inside** the bell it sits in. A canted
    plate's far corner is `sqrt((mid + halflen)^2 + halfwidth^2)` from the
    axis, not `mid + halflen`, so vanes sized against the mouth radius poked
    visibly out through the lip — measured, not guessed.
    """
    at = Vector(at)
    faces = []
    inner = mouth_r * (0.20 + 0.55 * open01)
    out_r = mouth_r * outer
    mid = (out_r + inner) / 2.0
    half = (out_r - inner) / 2.0 + 0.003
    wide = 2 * math.pi * mid / count * 1.15
    for i in range(count):
        a = 2 * math.pi * i / count
        spin = Matrix.Rotation(a, 4, 'Y')
        c = at + Vector((0.0, inset, 0.0)) + spin @ Vector((mid, 0.0, 0.0))
        faces += p.box(c, (2 * half, 0.005, wide), mat,
                       rot=spin @ Matrix.Rotation(math.radians(cant), 4, 'X'))
    return faces


def nozzle_finned(p, y_front, y_back, r, at=(0.0, 0.0), fins=6, fin_r=None,
                  mat=DARK, fin_mat=CHROME, seg=18):
    """A cooling barrel: a tube behind a stack of annular fins.

    `at` is the bore's (x, z) — the barrel rarely runs down the model's own
    centreline, and every other builder here takes an absolute placement.

    The cryo sprayer's read. Fins say 'this thing sheds heat' — the only
    silhouette in the set that is about temperature, and the reason the sprayer
    does not read as a second foam gun at a glance (GDC-L1-UX-0004).
    """
    fin_r = fin_r or r * 1.85
    x, z = at
    faces = p.cyl((x, (y_front + y_back) / 2.0, z), r, y_back - y_front,
                  axis='Y', seg=seg, mat=mat)
    span = (y_back - y_front) * 0.62
    for i in range(fins):
        y = y_front + 0.018 + span * i / max(1, fins - 1)
        # Fins grow backward: the tip is the coldest and thinnest part, and a
        # stack that tapers reads as a direction rather than as a stack.
        rr = fin_r * (0.78 + 0.22 * i / max(1, fins - 1))
        faces += p.cyl((x, y, z), rr, 0.005, axis='Y', seg=seg, mat=fin_mat)
    return faces


def nozzle_fan(p, at, half_width, height, depth, mat=SHELL, lip=CHROME):
    """A flat fan head: a wide, shallow slot that sprays a sheet, not a jet.

    The slick can's read, and the one nozzle in the set that is not round —
    which is what makes the can identifiable in the hand at a glance even
    though it is the smallest item (GDC-L1-UX-0003).
    """
    at = Vector(at)
    # A wedge widening toward the mouth, in the XZ plane, opening along −Y.
    prof_back = [(-half_width * 0.30, -height * 0.34),
                 (half_width * 0.30, -height * 0.34),
                 (half_width * 0.30, height * 0.34),
                 (-half_width * 0.30, height * 0.34)]
    prof_front = [(-half_width, -height * 0.5), (half_width, -height * 0.5),
                  (half_width, height * 0.5), (-half_width, height * 0.5)]
    faces = p.loft([(-depth, prof_front), (0.0, prof_back)], axis='Y', mat=mat,
                   cap=True)
    _translate(p, faces, at)
    # The slot itself, sunk into the mouth face. Without it the head is a
    # capped wedge and reads as a blunt block — the one cue that says this
    # sprays a sheet is a mouth you can see into (GDC-L1-UX-0004).
    p.box(at + Vector((0.0, -depth + 0.007, 0.0)),
          (half_width * 1.55, 0.016, height * 0.34), BLACK)
    # The lip: two rails top and bottom of the slot, framing it.
    for s in (-1, 1):
        faces += p.box(at + Vector((0.0, -depth + 0.004,
                                    s * (height * 0.5 + 0.003))),
                       (half_width * 2.05, 0.014, 0.007), lip)
    return faces


# --------------------------------------------------------------------------
# Plumbing
# --------------------------------------------------------------------------

def hose(p, points, radius, mat=RUBBER, seg=8, ribbed=False, rib_scale=1.32):
    """A flexible line swept along a polyline as overlapping cylinders.

    Each segment is built 30% longer than the gap it spans so consecutive
    segments interpenetrate at the joint. That is deliberate rather than lazy:
    a sphere at every joint costs three times the triangles, and the library's
    own rule is that touching surfaces either embed or stand off — never meet.

    `ribbed` alternates the radius segment by segment, which is how the cryo
    line gets its corrugation. `Part.torus` cannot do it: it takes an axis
    letter and no rotation, so its rings cannot follow a tangent.
    """
    pts = [Vector(q) for q in points]
    faces = []
    for i, (a, b) in enumerate(zip(pts, pts[1:])):
        d = b - a
        if d.length < 1e-6:
            continue
        r = radius * (rib_scale if (ribbed and i % 2 == 0) else 1.0)
        faces += p.cyl((a + b) / 2.0, r, d.length * 1.30, axis='Z', seg=seg,
                       mat=mat,
                       rot=d.to_track_quat('Z', 'Y').to_matrix().to_4x4())
    return faces


def arc(a, b, bulge, steps=9, up=(0, 0, 1)):
    """Sample a circular-ish arc from `a` to `b`, bowed `bulge` metres toward
    `up`. The path a hose takes when it loops out of a bottle and back to a
    muzzle — a straight line between two fittings reads as a pipe, not a hose.
    """
    a, b, up = Vector(a), Vector(b), Vector(up).normalized()
    return [a.lerp(b, i / (steps - 1))
            + up * (bulge * math.sin(math.pi * i / (steps - 1)))
            for i in range(steps)]


def clamp_band(p, centre, axis, radius, width=0.014, mat=CHROME, lug=DARK):
    """A band clamping a reservoir to a shell, with a bolt lug on one side."""
    c = Vector(centre)
    faces = p.cyl(c, radius * 1.06, width, axis=axis, seg=20, mat=mat)
    faces += p.box(c + Vector((radius * 1.02, 0.0, 0.0)),
                   (0.016, width * 0.9, 0.016), lug)
    return faces


def marker(coll, name, at, mats, size=0.004):
    """A tiny cube whose only job is to carry a coordinate across the FBX.

    Blender empties do not survive `object_types={"MESH"}`, and the FBX arrives
    in Unity axis-converted, so hand-deriving a muzzle position on the Unity
    side means composing two conventions and hoping. A named 4 mm mesh survives
    and the prefab builder reads its transform. `portal_gun.py` documents the
    whole reasoning; this is the family's copy of the same three lines.
    """
    q = Part(mats)
    q.box((0, 0, 0), (size, size, size), DARK)
    obj = q.finish(name, coll)
    obj.location = tuple(at)
    return obj


# --------------------------------------------------------------------------
# The component file: one issued copy of each part
# --------------------------------------------------------------------------

def _emit(p, hard, name, coll, label=None):
    p.restamp(label or name)
    if hard:
        p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll)


def _tank_squat(coll, mats):
    p = TrackedPart(mats)
    hard = tank(p, (0, 0, 0), 'Y', 0.048, 0.170, bands=2)
    return _emit(p, hard, "Mesh_SprayerTank_Squat", coll)


def _tank_cartridge(coll, mats):
    p = TrackedPart(mats)
    hard = tank(p, (0, 0, 0), 'Y', 0.038, 0.120, dome=0.18, bands=1)
    return _emit(p, hard, "Mesh_SprayerTank_Cartridge", coll)


def _tank_lance(coll, mats):
    p = TrackedPart(mats)
    hard = tank(p, (0, 0, 0), 'Y', 0.044, 0.320, dome=0.55, bands=3)
    return _emit(p, hard, "Mesh_SprayerTank_Lance", coll)


def _gauge(coll, mats):
    p = TrackedPart(mats)
    hard = gauge_plate(p, (0, 0, 0), (0, -1, 0), (1, 0, 0))
    return _emit(p, hard, "Mesh_SprayerGauge_Plate", coll)


def _grip(coll, mats):
    p = TrackedPart(mats)
    hard = grip_moulded(p, (0.0, 0.0, 0.0))
    return _emit(p, hard, "Mesh_SprayerGrip_Moulded", coll)


def _collar(coll, mats):
    p = TrackedPart(mats)
    hard = trigger_collar(p, (0, 0, 0), 0.055)
    return _emit(p, hard, "Mesh_SprayerTrigger_Collar", coll)


def _bell(coll, mats):
    p = TrackedPart(mats)
    hard = nozzle_bell(p, (0, 0, 0), 0.030, 0.085, 0.110)
    return _emit(p, hard, "Mesh_SprayerNozzle_Bell", coll)


def _iris(coll, mats):
    p = TrackedPart(mats)
    hard = iris_vanes(p, (0, -0.110, 0), 0.085)
    return _emit(p, hard, "Mesh_SprayerNozzle_Iris", coll)


def _finned(coll, mats):
    p = TrackedPart(mats)
    hard = nozzle_finned(p, -0.120, 0.0, 0.022)
    return _emit(p, hard, "Mesh_SprayerNozzle_Finned", coll)


def _fan(coll, mats):
    p = TrackedPart(mats)
    hard = nozzle_fan(p, (0, 0, 0), 0.042, 0.034, 0.046)
    return _emit(p, hard, "Mesh_SprayerNozzle_Fan", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(KIT_MATS)

    _tank_squat(collection("Coll_SprayerKit_TankSquat"), mats)
    _tank_cartridge(collection("Coll_SprayerKit_TankCartridge"), mats)
    _tank_lance(collection("Coll_SprayerKit_TankLance"), mats)
    _gauge(collection("Coll_SprayerKit_GaugePlate"), mats)
    _grip(collection("Coll_SprayerKit_GripMoulded"), mats)
    _collar(collection("Coll_SprayerKit_TriggerCollar"), mats)
    bell = collection("Coll_SprayerKit_NozzleBell")
    _bell(bell, mats)
    _iris(bell, mats)                     # the shutter belongs with its bell
    _finned(collection("Coll_SprayerKit_NozzleFinned"), mats)
    _fan(collection("Coll_SprayerKit_NozzleFan"), mats)

    report()
    save(out)


if __name__ == "__main__":
    main()
