"""Gauntlet Ruin Scanner — the ground-penetrating pulse emitter, on the base.

    blender --background --python gauntlet_ruin_scanner.py -- --out gauntlet_ruin_scanner.blend

The third cut of the Ruin Scanner gauntlet. The first (`ruin_scanner.py`) sat
a slim capsule on the webbing cuff and vanished into the suit sleeve. The
second was this device at half these numbers, and the user's verdict on the
whole gauntlet family was that it read too small: "quite visible; do not be
afraid of size." So this is the same machine at **2x linear**, with every
constant re-derived rather than the mesh scaled — embeds are still 2-4 mm and
the bevels are still 4 mm, both of which a mesh scale would have doubled into
mush.

The growth goes **up and forward, never past the elbow**: the housing keeps
the deck's length and grows in height and width, and the horn becomes a
0.30 m dish reaching out over the back of the hand. It is the hero shape of
the device and it is now the first thing read at any distance.

| Object | What it is |
|---|---|
| `Mesh_RuinScanner_Bed`           | machined bed on the deck: pedestal under the housing, front step under the horn |
| `Mesh_RuinScanner_Housing`       | dark-steel box, flared out over the bed, rounded roof |
| `Mesh_RuinScanner_Horn`          | the emitter horn: worn-steel truncated cone, dished at the mouth |
| `Mesh_RuinScanner_Bezel`         | chrome ring round the mouth |
| `Mesh_RuinScanner_Lens`          | amber lens disc, 2 mm inside the mouth |
| `Mesh_RuinScanner_Stripe`        | hazard-red arming band round the horn |
| `Mesh_RuinScanner_Boot`          | rubber boot where the horn enters the housing |
| `Mesh_RuinScanner_Panel`         | safety-orange plate on the roof, the suit-armour accent |
| `Mesh_RuinScanner_Lamps`         | two amber ready lamps on the panel |
| `Mesh_RuinScanner_SightFrame`    | folding rear sight: a frame on a chrome hinge pin |
| `Mesh_RuinScanner_SightPost`     | front sight post with a chrome bead |
| `Emitter`                        | empty at the lens centre on the mouth plane; the prefab's `muzzle` |

## Frame

The gauntlet family's (`_gauntlet.py`): arm along +Y, wrist joint at y = 0,
elbow +Y, forward −Y, dorsal +Z, +X the thumb side of a right forearm. The
export maps Blender (x, y, z) onto Unity (−x, z, −y), so the horn's mouth,
facing −Y here, faces Unity +Z — the direction `ItemGrip` aims — and an
unrotated `Emitter` already points the cone the right way. Origin at the
wrist bone, true suit scale, worn at scale 1.

## Where everything sits

Read down the arm from the elbow; every figure is metres in this frame.

```
 y 0.316  back of the housing (deck ends at 0.320)
 y 0.312  back of the bed
 y 0.294  rear sight frame on its pin; top at z 0.594
 y 0.278..0.150  orange panel on the roof; lamps at y 0.240, x ±0.044
 y 0.140  front sight post, bead top at z 0.596
 y 0.132  horn throat, buried 20 mm inside the housing (r 0.075)
 y 0.120  the bed's step riser — everything forward of it carries the horn
 y 0.116  rubber boot, straddling the housing's front face (y 0.112)
 y 0.102  front of the bed (the deck's front edge is 0.100)
 y 0.035..0.005  arming stripe round the horn
 y −0.028 lens recess floor
 y −0.058 lens face — 2 mm inside the mouth
 y −0.060 mouth plane, r 0.150 — the Emitter
 y −0.073 front of the chrome bezel
```

The horn's axis is z = HORN_Z (0.380) and its mouth is 0.30 m across, so the
mouth spans z 0.230..0.530. Those numbers are a fit, not a taste: forward of
the wrist the glove's puffy cuff wants everything above z 0.20, which puts a
0.15 m mouth radius' floor at an axis of 0.35 at the lowest; the sight line
and the roof want the axis no higher than the housing can cover. 0.380 gives
the mouth 30 mm of clearance under it and keeps the roof at 0.484.

**The housing is flared, and that is what the bed is for.** The device is
now wider than the deck (x ±0.115 against the deck's ±0.070), and the
hardpoint contract says nothing may sit below the deck plane outside the
deck's own footprint. So the load path is a pedestal that fits the deck —
`Mesh_RuinScanner_Bed`, x ±0.066, sunk 4 mm — and the housing sits on top of
it at z 0.272, entirely above the deck plane. The bed's front step (top
z 0.306) is the same part doing the old build's cradle job: it is what the
horn rests in where it leaves the housing. One object, because it is one
machined piece, and because a separate pedestal and cradle share a plane
wherever they meet.

## Unity wiring

`RuinScannerPulse` roots the cone at `muzzle.position` and follows it each
frame; the direction comes from the camera, so the empty's position is the
number that matters and its rotation is identity on purpose. Ship with
`keep_empties=True` (see `gauntlet_ruin_scanner_export.py`) or the FBX has
no `Emitter` and the cone starts at the wrist.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, _HERE)

import bpy  # noqa: E402

from _buildlib import *  # noqa: E402,F403
from _tracked import TrackedPart  # noqa: E402
from _gauntlet import BASE_DECK_Z  # noqa: E402

from mathutils import Vector  # noqa: E402

# Index 0 is a structural metal because `bmesh.ops.bevel` stamps every face it
# creates with material index 0 — see `_buildlib` trap notes.
DARK, STEEL, CHROME, ORANGE, WARN, AMBER, RUBBER = range(7)
MATS = ["Mat_Metal_Steel_Dark",        # bed, housing, sight frame and post
        "Mat_Metal_Steel_Worn",        # the horn
        "Mat_Metal_Chrome_Scuffed",    # bezel, hinge pin, sight bead
        "Mat_Paint_Safety_Orange",     # roof panel — the suit's armour accent
        "Mat_Paint_Warn_Red",          # arming stripe
        "Mat_Emissive_Amber",          # lens, ready lamps
        "Mat_Plastic_Rubber_Black"]    # the boot

# The device's own envelope, asserted in `check()` after the build. Growth is
# up and forward: past the wrist (y < WRIST_Y) the glove is what constrains it,
# and the arm still has to fold at the elbow end.
ENV_X, ENV_Y_ELBOW, ENV_Z = 0.210, 0.360, 0.640
ENV_Y_FWD, ENV_Z_FWD, ENV_X_FWD, WRIST_Y = -0.240, 0.200, 0.200, 0.030

# ── The bed on the deck ──────────────────────────────────────────────────────
BED_HX = 0.066                             # inside the deck's ±0.070
BED_Y0, BED_Y1 = 0.102, 0.312              # inside the deck's 0.100..0.320
BED_Z0 = BASE_DECK_Z - 0.004               # feet sunk 4 mm into the deck
BED_Z1 = 0.276                             # pedestal top, under the housing
STEP_Y = 0.120                             # the riser: forward of it the bed carries the horn
STEP_Z1 = 0.306                            # step top, 3-13 mm inside the horn

# ── Housing ──────────────────────────────────────────────────────────────────
HOUSING_HX = 0.115
HOUSING_Y0, HOUSING_Y1 = 0.112, 0.316
HOUSING_Z0 = BED_Z1 - 0.004                # 4 mm into the bed
HOUSING_Z1 = 0.484
ROOF_R, FOOT_R = 0.060, 0.012

# ── Horn ─────────────────────────────────────────────────────────────────────
HORN_Z = 0.380
MOUTH_Y, THROAT_Y = -0.060, 0.132
MOUTH_R, THROAT_R = 0.150, 0.075
RECESS_R, RECESS_Y = 0.120, -0.028         # the dish the lens sits in, 32 mm deep
HORN_SEG = 48                              # a 0.30 m mouth facets visibly below this

LENS_Y = MOUTH_Y + 0.002                   # lens face, 2 mm inside the mouth
LENS_R, LENS_T = RECESS_R + 0.001, 0.034   # 1 mm into the recess wall, 4 mm into its floor
EMITTER = (0.0, MOUTH_Y, HORN_Z)

BEZEL_MAJOR, BEZEL_MINOR = 0.1355, 0.0150
STRIPE_Y0, STRIPE_Y1 = 0.005, 0.035
STRIPE_PROUD, STRIPE_T = 0.003, 0.007      # 3 mm proud, 4 mm into the wall
BOOT_Y, BOOT_MAJOR, BOOT_MINOR = 0.116, 0.085, 0.014

# ── Roof furniture ───────────────────────────────────────────────────────────
# The panel's half-width is held at 0.070 rather than the housing's 0.115: the
# roof rounds off from x ±0.055, and a wider plate lands tangent to the
# shoulder instead of sinking into it — 0.2 mm of clearance, which flickers.
PANEL_HX, PANEL_Y0, PANEL_Y1 = 0.070, 0.150, 0.278
PANEL_Z0, PANEL_Z1 = HOUSING_Z1 - 0.004, HOUSING_Z1 + 0.004
LAMP_X, LAMP_Y, LAMP_R = 0.044, 0.240, 0.018
SIGHT_Y, SIGHT_HX, SIGHT_W, SIGHT_Z1 = 0.294, 0.060, 0.016, 0.594
PIN_R, PIN_Z = 0.012, HOUSING_Z1 + 0.006
POST_Y, POST_R, POST_Z1 = 0.140, 0.010, 0.584
ROOF_SINK = 0.004

BEVEL_W = 0.004                            # NOT scaled with the device: 8 mm reads as melted


def horn_radius(y):
    """Outer radius of the horn at station y, linear mouth to throat."""
    t = (y - MOUTH_Y) / (THROAT_Y - MOUTH_Y)
    return MOUTH_R + (THROAT_R - MOUTH_R) * t


def ring(r, seg=HORN_SEG):
    return [(r * math.cos(2 * math.pi * i / seg), HORN_Z + r * math.sin(2 * math.pi * i / seg))
            for i in range(seg)]


def rounded_profile(hx, z0, z1, r_top, r_bot, seg_top=8, seg_bot=3):
    """A rounded rectangle in (x, z), counter-clockwise from the bottom right."""
    pts = []

    def corner(cx, cz, r, a0, a1, n):
        for i in range(n + 1):
            a = math.radians(a0 + (a1 - a0) * i / n)
            pts.append((cx + r * math.cos(a), cz + r * math.sin(a)))

    corner(hx - r_bot, z0 + r_bot, r_bot, 270, 360, seg_bot)
    corner(hx - r_top, z1 - r_top, r_top, 0, 90, seg_top)
    corner(-hx + r_top, z1 - r_top, r_top, 90, 180, seg_top)
    corner(-hx + r_bot, z0 + r_bot, r_bot, 180, 270, seg_bot)
    return pts


# ---------------------------------------------------------------------------
# Parts
# ---------------------------------------------------------------------------

def bed(coll, mats):
    """The machined bed: a stepped block extruded ACROSS the arm.

    `prism(axis='X')` maps a profile (u, v) onto (y, z) — `_plane_point('X')`
    is (w, u, v) — so the profile is drawn in the plane the step lives in and
    the extrusion is the width. Written as one part because it is one piece of
    metal: a pedestal under the housing and a taller step under the horn. The
    first cut had those as two objects and they shared a plane wherever they
    met, whichever way round the seam was drawn.
    """
    p = TrackedPart(mats)
    prof = [(BED_Y0, BED_Z0), (BED_Y1, BED_Z0), (BED_Y1, BED_Z1),
            (STEP_Y, BED_Z1), (STEP_Y, STEP_Z1), (BED_Y0, STEP_Z1)]
    p.prism(prof, 2 * BED_HX, axis='X', mat=DARK)
    p.restamp("bed")
    p.bevel(width=BEVEL_W, segments=2)
    return p.finish("Mesh_RuinScanner_Bed", coll)


def housing(coll, mats):
    """The box: a prism of a rounded profile down the arm, standing on the bed.

    Only the curved faces are smooth-shaded; the flat sides and end caps stay
    flat, so the box keeps its edges instead of reading as a soap bar.
    """
    p = TrackedPart(mats)
    prof = rounded_profile(HOUSING_HX, HOUSING_Z0, HOUSING_Z1, ROOF_R, FOOT_R)
    faces = p.prism(prof, HOUSING_Y1 - HOUSING_Y0, axis='Y', mat=DARK,
                    offset=(0.0, (HOUSING_Y0 + HOUSING_Y1) / 2.0, 0.0))
    for f in faces:
        n = f.normal
        f.smooth = abs(n.y) < 0.9 and max(abs(n.x), abs(n.z)) < 0.999
    p.restamp("housing")
    p.bevel(width=BEVEL_W, segments=2)
    return p.finish("Mesh_RuinScanner_Housing", coll)


def horn(coll, mats):
    """The emitter horn, one closed loft along Y: recess floor → recess wall
    → mouth lip → outer cone → throat cap. The recess is a straight-walled
    dish 32 mm deep that the lens fills; the wall is 17 mm thick at the recess
    floor and thickens toward the mouth."""
    p = TrackedPart(mats)
    sections = [(RECESS_Y, ring(RECESS_R)),
                (MOUTH_Y, ring(RECESS_R)),
                (MOUTH_Y, ring(MOUTH_R)),
                (THROAT_Y, ring(THROAT_R))]
    p.loft(sections, axis='Y', mat=STEEL, cap=True)
    p.restamp("horn")
    return p.finish("Mesh_RuinScanner_Horn", coll)


def bezel(coll, mats):
    """Chrome ring straddling the mouth lip: 13 mm proud of the mouth plane,
    its inside edge 0.5 mm inside the lens's rim."""
    p = TrackedPart(mats)
    p.torus((0.0, LENS_Y, HORN_Z), BEZEL_MAJOR, BEZEL_MINOR, axis='Y',
            maj_seg=HORN_SEG, min_seg=8, mat=CHROME)
    p.restamp("bezel")
    return p.finish("Mesh_RuinScanner_Bezel", coll)


def lens(coll, mats):
    """The amber lens: a disc whose face is 2 mm inside the mouth, its barrel
    1 mm into the recess wall and its back 4 mm into the recess floor.
    Origin at the Emitter, the point the cone roots on."""
    p = TrackedPart(mats)
    p.cyl((0.0, LENS_Y + LENS_T / 2.0, HORN_Z), LENS_R, LENS_T, axis='Y',
          seg=HORN_SEG, mat=AMBER)
    p.restamp("lens")
    return p.finish("Mesh_RuinScanner_Lens", coll, origin=EMITTER)


def stripe(coll, mats):
    """Arming band: a conical sleeve following the horn's taper, 3 mm proud
    and 4 mm into the wall. Not bevelled — a bevel would stamp steel edges
    onto the paint."""
    p = TrackedPart(mats)
    r0, r1 = horn_radius(STRIPE_Y0), horn_radius(STRIPE_Y1)
    loop = [(STRIPE_Y0, ring(r0 + STRIPE_PROUD)),
            (STRIPE_Y1, ring(r1 + STRIPE_PROUD)),
            (STRIPE_Y1, ring(r1 + STRIPE_PROUD - STRIPE_T)),
            (STRIPE_Y0, ring(r0 + STRIPE_PROUD - STRIPE_T)),
            (STRIPE_Y0, ring(r0 + STRIPE_PROUD))]
    p.loft(loop, axis='Y', mat=WARN, cap=False)
    p.restamp("stripe")
    return p.finish("Mesh_RuinScanner_Stripe", coll)


def boot(coll, mats):
    """Rubber boot round the horn where it enters the housing. Its inner half
    is buried in the horn's wall, its rear half in the housing, and its top is
    5 mm under the roof — what shows is a rubber ring on the front face."""
    p = TrackedPart(mats)
    p.torus((0.0, BOOT_Y, HORN_Z), BOOT_MAJOR, BOOT_MINOR, axis='Y',
            maj_seg=32, min_seg=8, mat=RUBBER)
    p.restamp("boot")
    return p.finish("Mesh_RuinScanner_Boot", coll)


def panel(coll, mats):
    """The safety-orange plate on the roof, 4 mm proud and sunk 4 mm into the
    roof at its centre, 2 mm into the shoulders at its edges."""
    p = TrackedPart(mats)
    p.slab((-PANEL_HX, PANEL_Y0, PANEL_Z0), (PANEL_HX, PANEL_Y1, PANEL_Z1), ORANGE)
    p.restamp("panel")
    return p.finish("Mesh_RuinScanner_Panel", coll)


def lamps(coll, mats):
    """Two amber ready lamps standing 13 mm out of the panel, slightly domed.

    Their undersides are 3 mm into the panel — and, deliberately, 1 mm above
    the roof plane the panel is sunk into, so no face of a lamp lies in the
    roof's plane even though both are hidden inside the panel."""
    p = TrackedPart(mats)
    for sx in (-1, 1):
        p.cyl((sx * LAMP_X, LAMP_Y, PANEL_Z1 + 0.005), LAMP_R, 0.016, axis='Z',
              seg=16, mat=AMBER, radius_top=LAMP_R * 0.85)
    p.restamp("lamps")
    return p.finish("Mesh_RuinScanner_Lamps", coll)


def sight_frame(coll, mats):
    """Folding rear sight: two posts and a crossbar on a chrome hinge pin
    lying on the roof. Origin on the pin's axis, the fold pivot."""
    p = TrackedPart(mats)
    hard = []
    for sx in (-1, 1):
        hard += p.slab((sx * SIGHT_HX - SIGHT_W / 2, SIGHT_Y - SIGHT_W / 2, HOUSING_Z1 - ROOF_SINK),
                       (sx * SIGHT_HX + SIGHT_W / 2, SIGHT_Y + SIGHT_W / 2, SIGHT_Z1), DARK)
    hard += p.slab((-SIGHT_HX - SIGHT_W / 2, SIGHT_Y - SIGHT_W / 2, SIGHT_Z1 - SIGHT_W),
                   (SIGHT_HX + SIGHT_W / 2, SIGHT_Y + SIGHT_W / 2, SIGHT_Z1), DARK)
    p.cyl((0.0, SIGHT_Y, PIN_Z), PIN_R, 2 * SIGHT_HX + 0.044, axis='X', seg=12, mat=CHROME)
    p.restamp("sight frame")
    p.bevel(hard, width=0.002, segments=1)
    return p.finish("Mesh_RuinScanner_SightFrame", coll, origin=(0.0, SIGHT_Y, PIN_Z))


def sight_post(coll, mats):
    """Front sight: a post out of the roof with a chrome bead on top. The
    sight line over it clears the horn's top by 40 mm."""
    p = TrackedPart(mats)
    z0 = HOUSING_Z1 - ROOF_SINK
    p.cyl((0.0, POST_Y, (z0 + POST_Z1) / 2.0), POST_R, POST_Z1 - z0, axis='Z',
          seg=10, mat=DARK)
    p.cyl((0.0, POST_Y, POST_Z1), POST_R + 0.004, 0.012, axis='Z', seg=10, mat=CHROME)
    p.restamp("sight post")
    return p.finish("Mesh_RuinScanner_SightPost", coll)


def emitter(coll):
    """Where the cone starts: the lens centre on the mouth plane. Identity
    rotation on purpose — see the module docstring."""
    obj = bpy.data.objects.new("Emitter", None)
    obj.empty_display_type = 'ARROWS'
    obj.empty_display_size = 0.06
    obj.location = Vector(EMITTER)
    coll.objects.link(obj)
    return obj


# ---------------------------------------------------------------------------
# Verification
# ---------------------------------------------------------------------------

def check(coll):
    """Print every device object's origin and bounds, then assert the envelope.

    Fails loudly rather than shipping a gauntlet that clips the glove or
    refuses to fold: the numbers here are the brief's, and a breach is a build
    error, not a note in a report nobody reads.
    """
    device = 0
    lo = [1e9] * 3
    hi = [-1e9] * 3
    fwd_z, fwd_x = 1e9, 0.0
    for o in sorted(coll.objects, key=lambda o: o.name):
        if o.type == 'EMPTY':
            print("  EMPTY %-30s at (%.4f, %.4f, %.4f)" % (o.name, *o.location))
            continue
        if not o.name.startswith("Mesh_RuinScanner_"):
            continue
        pts = [o.matrix_world @ v.co for v in o.data.vertices]
        for p in pts:
            for i in range(3):
                lo[i], hi[i] = min(lo[i], p[i]), max(hi[i], p[i])
            if p.y < WRIST_Y:
                fwd_z = min(fwd_z, p.z)
                fwd_x = max(fwd_x, abs(p.x))
        n = tri_count(o)
        device += n
        print("  %-30s origin (%.3f, %.3f, %.3f)  x %.3f..%.3f  y %.3f..%.3f  z %.3f..%.3f  tris %d"
              % (o.name, *o.location,
                 min(p.x for p in pts), max(p.x for p in pts),
                 min(p.y for p in pts), max(p.y for p in pts),
                 min(p.z for p in pts), max(p.z for p in pts), n))
    print("  DEVICE x %.4f..%.4f  y %.4f..%.4f  z %.4f..%.4f"
          % (lo[0], hi[0], lo[1], hi[1], lo[2], hi[2]))
    print("  FORWARD of the wrist (y < %.3f): min z %.4f, max |x| %.4f"
          % (WRIST_Y, fwd_z, fwd_x))
    print("  DEVICE TRIS: %d" % device)

    breaches = []
    if max(abs(lo[0]), abs(hi[0])) > ENV_X:
        breaches.append("width |x| %.4f > %.3f" % (max(abs(lo[0]), abs(hi[0])), ENV_X))
    if hi[1] > ENV_Y_ELBOW:
        breaches.append("elbow end y %.4f > %.3f" % (hi[1], ENV_Y_ELBOW))
    if lo[1] < ENV_Y_FWD:
        breaches.append("forward reach y %.4f < %.3f" % (lo[1], ENV_Y_FWD))
    if hi[2] > ENV_Z:
        breaches.append("height z %.4f > %.3f" % (hi[2], ENV_Z))
    if fwd_z < ENV_Z_FWD:
        breaches.append("over the glove: z %.4f < %.3f" % (fwd_z, ENV_Z_FWD))
    if fwd_x > ENV_X_FWD:
        breaches.append("over the glove: |x| %.4f > %.3f" % (fwd_x, ENV_X_FWD))
    if breaches:
        raise SystemExit("Envelope breached: " + "; ".join(breaches))
    print("  envelope OK")


def main():
    out = parse_out()
    start(out)
    coll = collection("Coll_GauntletRuinScanner")
    mats = link_materials(MATS)

    bed(coll, mats)
    housing(coll, mats)
    horn(coll, mats)
    bezel(coll, mats)
    lens(coll, mats)
    stripe(coll, mats)
    boot(coll, mats)
    panel(coll, mats)
    lamps(coll, mats)
    sight_frame(coll, mats)
    sight_post(coll, mats)
    emitter(coll)

    save(out)
    report()
    check(coll)


if __name__ == "__main__":
    main()
