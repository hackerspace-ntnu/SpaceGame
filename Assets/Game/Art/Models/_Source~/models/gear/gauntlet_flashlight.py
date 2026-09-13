"""Gauntlet Flashlight — the forearm lamp, on the base.

    blender --background --python gauntlet_flashlight.py -- --out gauntlet_flashlight.blend

The torch the astronaut wears instead of a helmet lamp. It is deliberately the
Ruin Scanner's machine: the same bed, housing, horn, bezel, boot and roof
furniture, at the same numbers, because that device is already a housing with
an emitter horn out of the front and it was fitted to the glove and the fold
envelope the hard way (`gauntlet_ruin_scanner_BUILD.md`). Re-deriving those
dimensions for a lamp would be inventing a second answer to a question that
has one.

What changes is what the horn is *for*, and that is three edits:

| Ruin Scanner | Flashlight |
|---|---|
| a solid 34 mm amber lens filling the recess | gone — the dish is open and you look into it |
| nothing behind the lens | `Mesh_Flashlight_Reflector`, a chrome paraboloid, with `Mesh_Flashlight_Bulb` at its throat |
| hazard stripe, rear sight frame, front bead post | gone |

**There is no cover glass, and that is a decision.** The first cut had a
warm-white disc across the mouth, which read as a torch instantly — and hid
the reflector completely. Glass would not have helped: `Mat_Glass_Canopy_Tinted`
carries its transparency as Principled *transmission*, which the FBX exporter
drops, so it arrives in Unity opaque. Either the face glows and the dish is
dead geometry, or the dish is the face. The dish is better: chrome catches the
world's light when the lamp is off, the bulb is the only emissive part, and
`FlashlightGauntletArtifact` darkens that bulb when the torch is switched
off — so the model tells the truth about its own state.

Dropping the sights is the point of the silhouette: a scanner is aimed and a
torch is not, so the roof reads flat and the eye goes to the dish. The stripe
went with them — an arming band says "this fires", and this one lights.

| Object | What it is |
|---|---|
| `Mesh_Flashlight_Bed`         | machined bed on the deck: pedestal under the housing, front step under the horn |
| `Mesh_Flashlight_Housing`     | dark-steel box, flared out over the bed, rounded roof |
| `Mesh_Flashlight_Horn`        | worn-steel truncated cone, dished 32 mm at the mouth |
| `Mesh_Flashlight_Reflector`   | chrome paraboloid shell inside the dish, holed at the throat |
| `Mesh_Flashlight_Bulb`        | the emitter element, through the reflector's hole |
| `Mesh_Flashlight_Bezel`       | chrome ring round the mouth |
| `Mesh_Flashlight_Boot`        | rubber boot where the horn enters the housing |
| `Mesh_Flashlight_Panel`       | safety-orange plate on the roof, the suit-armour accent |
| `Mesh_Flashlight_Lamps`       | two amber charge lamps on the panel |
| `Emitter`                     | empty on the dish axis at the mouth plane; where the Light hangs |

## Frame

The gauntlet family's (`_gauntlet.py`): arm along +Y, wrist joint at y = 0,
elbow +Y, forward −Y, dorsal +Z, +X the thumb side of a right forearm. The
export maps Blender (x, y, z) onto Unity (−x, z, −y), so the mouth, facing −Y
here, faces **Unity +Z** — and a Light parented to an unrotated `Emitter`
shines straight out of the horn with no rotation offset to get wrong.

## The dish, and why the numbers are what they are

The horn is untouched, so the cavity the lamp has to live in is fixed: the
recess is 32 mm deep (mouth plane y −0.060, floor y −0.028) and 0.240 m
across. Everything below is packed into that, front to back:

```
 y −0.060  mouth plane, r 0.150 — the Emitter
 y −0.046  reflector rim, front face — 14 mm back inside the mouth
 y −0.044  bulb tip
 y −0.042  reflector rim, back face (shell is 4 mm thick along the axis)
 y −0.028  the recess floor the reflector's vertex passes through
 y −0.024  reflector vertex, front face — 4 mm INTO the solid horn
 y −0.020  reflector vertex, back face, and the bulb's tail
```

The rim sits 14 mm back from the mouth plane so the horn's own lip shades it,
rather than the dish ending flush with the bezel. Nothing of the reflector's
back half is visible; it is there so the part is a closed solid rather than a
one-sided surface, which is what a lofted dish
would otherwise be. The vertex is deliberately sunk past the recess floor so
no face of the reflector shares the floor's plane.

The paraboloid is `y = REFL_VERTEX_Y − REFL_DEPTH·t²` with `r = REFL_R·t`.
`REFL_R` is 0.121 against the recess wall's 0.120, so the rim embeds 1 mm
rather than floating with a gap that shows as a black ring.

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
DARK, STEEL, CHROME, ORANGE, WARM, AMBER, RUBBER = range(7)
MATS = ["Mat_Metal_Steel_Dark",        # bed, housing
        "Mat_Metal_Steel_Worn",        # the horn
        "Mat_Metal_Chrome_Scuffed",    # bezel, reflector
        "Mat_Paint_Safety_Orange",     # roof panel — the suit's armour accent
        "Mat_Emissive_Cabin_Warm",     # the bulb — the only emissive part of the lamp
        "Mat_Emissive_Amber",          # charge lamps
        "Mat_Plastic_Rubber_Black"]    # the boot

# The device's own envelope, asserted in `check()` after the build. The Ruin
# Scanner's, unchanged: this device is smaller than that one in every
# direction, so the limits are inherited rather than re-argued.
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
RECESS_R, RECESS_Y = 0.120, -0.028         # the dish the lamp sits in, 32 mm deep
HORN_SEG = 48                              # a 0.30 m mouth facets visibly below this

BEZEL_MAJOR, BEZEL_MINOR = 0.1355, 0.0150
BOOT_Y, BOOT_MAJOR, BOOT_MINOR = 0.116, 0.085, 0.014

# ── The lamp inside the dish ─────────────────────────────────────────────────
# The Ruin Scanner's solid amber plug is gone and nothing replaces it: the dish
# is open to the air and the reflector is what the mouth shows. See the module
# docstring for why no cover glass.
BEZEL_SEAT_Y = MOUTH_Y + 0.002             # where the bezel rides the mouth lip

REFL_R = RECESS_R + 0.001                  # rim 1 mm into the recess wall
REFL_RIM_Y = -0.046                        # 4 mm clear of the glass's back
REFL_VERTEX_Y = -0.024                     # 4 mm INTO the recess floor
REFL_T = 0.004                             # shell thickness, along the axis
REFL_HOLE_R = 0.015                        # the throat the bulb comes through
REFL_SEG, REFL_STATIONS = 32, 7            # a shell nobody looks at closely

BULB_R, BULB_Y0, BULB_Y1 = 0.017, -0.044, -0.020

# ── Roof furniture ───────────────────────────────────────────────────────────
# The panel's half-width is held at 0.070 rather than the housing's 0.115: the
# roof rounds off from x ±0.055, and a wider plate lands tangent to the
# shoulder instead of sinking into it — 0.2 mm of clearance, which flickers.
PANEL_HX, PANEL_Y0, PANEL_Y1 = 0.070, 0.150, 0.278
PANEL_Z0, PANEL_Z1 = HOUSING_Z1 - 0.004, HOUSING_Z1 + 0.004
LAMP_X, LAMP_Y, LAMP_R = 0.044, 0.240, 0.018

EMITTER = (0.0, MOUTH_Y, HORN_Z)

BEVEL_W = 0.004                            # NOT scaled with the device: 8 mm reads as melted


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

    `prism(axis='X')` maps a profile (u, v) onto (y, z), so the profile is
    drawn in the plane the step lives in and the extrusion is the width. One
    part because it is one piece of metal — a pedestal under the housing and a
    taller step under the horn. Two objects would share a plane wherever they
    met, whichever way round the seam was drawn.
    """
    p = TrackedPart(mats)
    prof = [(BED_Y0, BED_Z0), (BED_Y1, BED_Z0), (BED_Y1, BED_Z1),
            (STEP_Y, BED_Z1), (STEP_Y, STEP_Z1), (BED_Y0, STEP_Z1)]
    p.prism(prof, 2 * BED_HX, axis='X', mat=DARK)
    p.restamp("bed")
    p.bevel(width=BEVEL_W, segments=2)
    return p.finish("Mesh_Flashlight_Bed", coll)


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
    return p.finish("Mesh_Flashlight_Housing", coll)


def horn(coll, mats):
    """The emitter horn, one closed loft along Y: recess floor → recess wall
    → mouth lip → outer cone → throat cap. A solid cone with a 32 mm dish
    carved into its face; everything the lamp is made of lives in that dish."""
    p = TrackedPart(mats)
    sections = [(RECESS_Y, ring(RECESS_R)),
                (MOUTH_Y, ring(RECESS_R)),
                (MOUTH_Y, ring(MOUTH_R)),
                (THROAT_Y, ring(THROAT_R))]
    p.loft(sections, axis='Y', mat=STEEL, cap=True)
    p.restamp("horn")
    return p.finish("Mesh_Flashlight_Horn", coll)


def reflector(coll, mats):
    """The chrome paraboloid, as a closed shell with a hole at its throat.

    Built as a loop of rings rather than a single dished surface: a loft of one
    surface is one-sided, and a one-sided reflector shows its backfaces the
    moment the arm swings past the camera. The loop runs front face (rim to
    throat), across the throat, back face (throat to rim), across the rim, and
    closes — `cap=False`, because the loop already closes itself.

    The vertex sits 4 mm past the recess floor, inside solid horn, so no face
    of this part lies in the floor's plane.
    """
    p = TrackedPart(mats)
    depth = REFL_VERTEX_Y - REFL_RIM_Y          # positive: the dish's axial depth
    t_hole = REFL_HOLE_R / REFL_R

    def station(t):
        """(y, r) on the concave face at parameter t — 0 at the vertex, 1 at the rim."""
        return REFL_VERTEX_Y - depth * t * t, REFL_R * t

    ts = [t_hole + (1.0 - t_hole) * i / (REFL_STATIONS - 1) for i in range(REFL_STATIONS)]

    loop = []
    for t in reversed(ts):                      # concave face, rim inward to the throat
        y, r = station(t)
        loop.append((y, ring(r, REFL_SEG)))
    for t in ts:                                # convex face, throat back out to the rim
        y, r = station(t)
        loop.append((y + REFL_T, ring(r, REFL_SEG)))
    loop.append(loop[0])                        # close the rim

    p.loft(loop, axis='Y', mat=CHROME, cap=False)
    p.restamp("reflector")
    return p.finish("Mesh_Flashlight_Reflector", coll)


def bulb(coll, mats):
    """The emitter element: a domed pin through the reflector's throat.

    2 mm fatter than the hole it passes through, so the shell closes on it
    instead of leaving a ring you can see the dark through. Its tail runs back
    into the solid horn.
    """
    p = TrackedPart(mats)
    p.cyl((0.0, (BULB_Y0 + BULB_Y1) / 2.0, HORN_Z), BULB_R, BULB_Y1 - BULB_Y0,
          axis='Y', seg=16, mat=WARM, radius_top=BULB_R * 0.7)
    p.restamp("bulb")
    return p.finish("Mesh_Flashlight_Bulb", coll)


def bezel(coll, mats):
    """Chrome ring straddling the mouth lip: 13 mm proud of the mouth plane,
    its inside edge just inside the recess wall."""
    p = TrackedPart(mats)
    p.torus((0.0, BEZEL_SEAT_Y, HORN_Z), BEZEL_MAJOR, BEZEL_MINOR, axis='Y',
            maj_seg=HORN_SEG, min_seg=8, mat=CHROME)
    p.restamp("bezel")
    return p.finish("Mesh_Flashlight_Bezel", coll)


def boot(coll, mats):
    """Rubber boot round the horn where it enters the housing. Its inner half
    is buried in the horn's wall, its rear half in the housing, and its top is
    5 mm under the roof — what shows is a rubber ring on the front face."""
    p = TrackedPart(mats)
    p.torus((0.0, BOOT_Y, HORN_Z), BOOT_MAJOR, BOOT_MINOR, axis='Y',
            maj_seg=32, min_seg=8, mat=RUBBER)
    p.restamp("boot")
    return p.finish("Mesh_Flashlight_Boot", coll)


def panel(coll, mats):
    """The safety-orange plate on the roof, 4 mm proud and sunk 4 mm into the
    roof at its centre, 2 mm into the shoulders at its edges."""
    p = TrackedPart(mats)
    p.slab((-PANEL_HX, PANEL_Y0, PANEL_Z0), (PANEL_HX, PANEL_Y1, PANEL_Z1), ORANGE)
    p.restamp("panel")
    return p.finish("Mesh_Flashlight_Panel", coll)


def lamps(coll, mats):
    """Two amber charge lamps standing 13 mm out of the panel, slightly domed.

    Their undersides are 3 mm into the panel — and, deliberately, 1 mm above
    the roof plane the panel is sunk into, so no face of a lamp lies in the
    roof's plane even though both are hidden inside the panel."""
    p = TrackedPart(mats)
    for sx in (-1, 1):
        p.cyl((sx * LAMP_X, LAMP_Y, PANEL_Z1 + 0.005), LAMP_R, 0.016, axis='Z',
              seg=16, mat=AMBER, radius_top=LAMP_R * 0.85)
    p.restamp("lamps")
    return p.finish("Mesh_Flashlight_Lamps", coll)


def emitter(coll):
    """Where the light comes from: the dish's axis on the mouth plane.
    Identity rotation on purpose — see the module docstring."""
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
    refuses to fold: a breach is a build error, not a note in a report nobody
    reads.
    """
    device = 0
    lo = [1e9] * 3
    hi = [-1e9] * 3
    fwd_z, fwd_x = 1e9, 0.0
    for o in sorted(coll.objects, key=lambda o: o.name):
        if o.type == 'EMPTY':
            print("  EMPTY %-30s at (%.4f, %.4f, %.4f)" % (o.name, *o.location))
            continue
        if not o.name.startswith("Mesh_Flashlight_"):
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
    coll = collection("Coll_GauntletFlashlight")
    mats = link_materials(MATS)

    bed(coll, mats)
    housing(coll, mats)
    horn(coll, mats)
    reflector(coll, mats)
    bulb(coll, mats)
    bezel(coll, mats)
    boot(coll, mats)
    panel(coll, mats)
    lamps(coll, mats)
    emitter(coll)

    save(out)
    report()
    check(coll)


if __name__ == "__main__":
    main()
