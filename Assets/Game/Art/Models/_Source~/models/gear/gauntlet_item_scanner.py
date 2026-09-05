"""Gauntlet Item Scanner — the wrist-top radar console.

    blender --background --python gauntlet_item_scanner.py -- --out gauntlet_item_scanner.blend

**`gauntlet_item_scanner.blend` HAS BEEN HAND-EDITED (2026-09-03) AND THIS
SCRIPT NO LONGER REPRODUCES IT.** It is history now, not a build. `start()`
refuses to overwrite the file and that refusal is load bearing: re-running this
would silently throw away the seating the lead corrected by hand. Edit the
.blend, and re-export with `gauntlet_item_scanner_export.py`. What the hand
edits changed, measured from the file, is in `gauntlet_item_scanner_BUILD.md`
under "The hand edits".

The item scanner used to be `handheld_terminal`'s Scanner variation strapped
onto the webbing cuff (`item_scanner.py`). This is its replacement on the
armoured `gauntlet_base` (Mount variation): a chunky dark-steel radar console
bolted to the dorsal hardpoint, a CRT under a raised bezel, a knob on the
wrist-facing front face and a whip antenna at the elbow-end corner.

Built at DOUBLE the first cut's size (2026-09-03): the gauntlet devices read
too small on the astronaut. Every constant below is re-derived rather than the
mesh being scaled, so embeds stay 2-4 mm and the bevels stay crisp — a scaled
4 mm embed is an 8 mm one, and a scaled bevel turns a machined corner into
soap. The growth went UP and FORWARD over the back of the hand; the elbow end
did not move, because the arm has to fold.

## Objects

| Object | What it is |
|---|---|
| `Mesh_ItemScanner_Bracket`      | the console's bracket. Was the bracer's deck until the hand edit rotated it onto the flank with the console; kept and renamed when the bracer left the model (`strip_bracer.py`) |
| `Mesh_ItemScanner_Plinth`       | the foot: the only part on the deck, inside the deck footprint |
| `Mesh_ItemScanner_Housing`      | the console: front face, apron, 25 degree screen slope, roof; antenna lug, four bolt heads |
| `Mesh_ItemScanner_Bezel`        | worn-steel frame standing 10 mm off the slope, 3 mm into it |
| `Mesh_Terminal_Scanner_Screen`  | the CRT plate, recessed 3 mm into the bezel; Unity paints it |
| `Mesh_Terminal_Scanner_Dial`    | the knob on the front face; Unity spins it |
| `Mesh_Terminal_Scanner_Antenna` | the whip; Unity sways it |
| `Mesh_ItemScanner_Lamps`        | two amber lamps on the apron |

The three `Mesh_Terminal_Scanner_*` names are what the prefab's serialized
references are bound to (`ItemScannerArtifact.dial/antenna`,
`ItemScannerScreen.screenRenderer`); the FBX sub-object ids derive from them,
so they stay exactly as they were on the old model.

## Frame

The family frame (`_gauntlet.py`): arm along +Y, wrist at y = 0, elbow +Y,
forward −Y, dorsal +Z, thumb +X on a right forearm. Export maps Blender
(x, y, z) onto Unity (−x, z, −y).

## Why the console stands on a plinth

The body is 0.264 m across — twice the deck's own 0.140 — so it cannot stand
on the deck the way the first cut did: a foot at ±0.132 sunk into the deck
plane puts geometry below z = 0.250 far outside the deck footprint, over the
shell, which the hardpoint contract forbids. So the only part touching the
deck is `Mesh_ItemScanner_Plinth` (x ±0.062, y 0.106..0.316, z 0.246..0.262),
sunk 4 mm and comfortably inside the deck's 10 mm top bevel; it swallows all
four bolt bosses. The body's underside is a flat 0.258 for its whole length —
4 mm into the plinth, 8 mm clear of the deck plane at the sides and about
22 mm clear of the dorsal shell where it cantilevers past the wrist.

## Where everything sits

- **Body**: x ±0.132, y −0.090..0.314, underside z 0.258. Front face rises to
  the apron plane z 0.336 (86 mm above the deck), flat apron back to y 0.110,
  25 degree slope up to (0.286, 0.418), 28 mm roof strip, back face at 0.314.
  The nose overhangs the wrist by 90 mm at z ≥ 0.258, well inside the relaxed
  forward envelope (y ≥ −0.24 above z 0.20, |x| ≤ 0.20).
- **Screen slope frame**: origin at the slope's low edge centre
  (0, 0.110, 0.336); `s` runs up the slope toward the elbow, `n` is the slope
  normal (up and toward the wrist). The screen and bezel are built in a local
  frame whose +X is world −X and +Y is DOWN the slope (−s) — right-handed with
  +Z = n, and it makes the plate's planar UVs come out with `u` toward
  Blender −X (Unity +X, the viewer's right looking down the arm) and `v`
  toward the wrist ("ahead" on the radar) with no flips. Same handedness the
  old model shipped with, so `_FlipX` stays 0.
- **Screen**: 0.196 x 0.142 m plate — true aspect 1.380 — centred on the
  0.194 m slope with 9 mm of bezel margin at each end.
- **Dial**: on the front face at (0.072, −0.090, 0.297), thumb side, axle
  along Y, protruding 50 mm toward the hand. Origin at the axle where it meets
  the face; Unity's `Euler(0, 0, a)` is a spin about Unity Z = Blender −Y, the
  axle. Safety-orange cap (the device's one accent) and a chrome pointer
  stripe down the rim so the spin reads.
- **Antenna**: rooted on a lug on the little-finger (−X) flank at the elbow
  end, origin (−0.150, 0.282, 0.392), leaning 20 degrees back toward the
  elbow, 0.200 m long. Tip lands at (−0.150, 0.350, 0.580) — inside the fold
  envelope (y ≤ 0.36, z ≤ 0.64) with 10 mm to spare on the elbow end, which is
  what sets the length.
- **Lamps**: two on the apron's little-finger half at y 0.020.

## Unity wiring

`ItemScannerScreen.screenRenderer` → `Mesh_Terminal_Scanner_Screen`,
`materialIndex` 0. The plate's true aspect is 1.380; `AspectOf` measures the
renderer's local bounds, which the applied 25 degree tilt makes larger along
the slope, so that check is being fixed on the Unity side.

Generation script — historical record. The .blend is the source of truth;
never re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, os.path.join(_LIB, "components", "props"))
sys.path.insert(0, os.path.join(_LIB, "components", "mechanical"))
sys.path.insert(0, _HERE)

import bpy  # noqa: E402

from _buildlib import *  # noqa: E402,F403
from _gauntlet import BASE_DECK_HX, BASE_DECK_Z, place  # noqa: E402
from _tracked import TrackedPart  # noqa: E402
from handheld_terminal import planar_uv  # noqa: E402
from panel_control import tube_path  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

# Index 0 is a structural metal because `bmesh.ops.bevel` stamps every face it
# creates with material index 0.
DARK, STEEL, CHROME, ORANGE, AMBER, RUBBER = range(6)
MATS = ["Mat_Metal_Steel_Dark",       # housing, plinth, dial skirt, screen rim
        "Mat_Metal_Steel_Worn",       # bezel
        "Mat_Metal_Chrome_Scuffed",   # bolt heads, lamp rings, pointer, antenna tip
        "Mat_Paint_Safety_Orange",    # dial cap — the one accent
        "Mat_Emissive_Amber",         # lamps
        "Mat_Plastic_Rubber_Black"]   # dial body, antenna base and whip
CRT_MAT = "Mat_Emissive_Green_CRT"

BEVEL_W = 0.005

# ── Plinth: the foot, and the only thing standing on the deck ────────────────
PLINTH_HX = 0.062                     # inside BASE_DECK_HX (0.070) and its 10 mm bevel
PLINTH_Y0, PLINTH_Y1 = 0.106, 0.316
PLINTH_Z0 = BASE_DECK_Z - 0.004       # 4 mm into the deck
PLINTH_Z1 = BASE_DECK_Z + 0.012

# ── Body ─────────────────────────────────────────────────────────────────────
HX = 0.132
Y0, Y1 = -0.090, 0.314                # nose over the back of the hand; elbow end unmoved
BODY_Z0 = PLINTH_Z1 - 0.004           # 4 mm into the plinth
FRONT_Z = 0.336                       # front face top / apron plane
APRON_Y1 = 0.110                      # where the screen slope starts
SLOPE_DEG = 25.0
ROOF_Y0 = 0.286
ROOF_Z = FRONT_Z + (ROOF_Y0 - APRON_Y1) * math.tan(math.radians(SLOPE_DEG))
BOLT_XY = [(sx * 0.112, y) for sx in (-1, 1) for y in (-0.060, 0.080)]
BOLT_R, BOLT_H = 0.007, 0.008

# ── Screen slope frame ───────────────────────────────────────────────────────
SLOPE_ORIGIN = Vector((0.0, APRON_Y1, FRONT_Z))
SLOPE_LEN = (ROOF_Y0 - APRON_Y1) / math.cos(math.radians(SLOPE_DEG))
SCREEN_T = SLOPE_LEN / 2.0            # screen centre, up the slope from its low edge
SCREEN_HW, SCREEN_HH = 0.098, 0.071   # plate half-extents (across, along slope)
BEZEL_RIM = 0.016
BEZEL_GAP = 0.0015                    # plate clearance inside the aperture
BEZEL_PROUD, BEZEL_SUNK = 0.010, 0.003
SCREEN_RECESS = 0.003                 # plate face below the bezel face

# ── Dial ─────────────────────────────────────────────────────────────────────
DIAL_AT = (0.072, Y0, 0.297)
DIAL_R, DIAL_DEPTH = 0.024, 0.038
DIAL_SKIRT_R, DIAL_SKIRT_DEPTH = 0.027, 0.012
DIAL_CAP_R, DIAL_CAP_DEPTH = 0.018, 0.007

# ── Antenna ──────────────────────────────────────────────────────────────────
LUG = ((-HX + 0.004, 0.258, 0.352), (-0.168, 0.306, 0.394))
ANTENNA_AT = (-0.150, 0.282, LUG[1][2] - 0.002)
ANTENNA_LEAN_DEG = 20.0               # back, toward the elbow
ANTENNA_LEN = 0.200
ANTENNA_BASE_R, ANTENNA_BASE_LEN = 0.012, 0.030
ANTENNA_R, ANTENNA_TAPER = 0.006, 0.40

# ── Lamps ────────────────────────────────────────────────────────────────────
LAMP_XY = [(-0.096, 0.020), (-0.056, 0.020)]
LAMP_RING_R, LAMP_R = 0.015, 0.011


def slope_dir():
    a = math.radians(SLOPE_DEG)
    return Vector((0.0, math.cos(a), math.sin(a)))


def slope_normal():
    a = math.radians(SLOPE_DEG)
    return Vector((0.0, -math.sin(a), math.cos(a)))


def screen_matrix():
    """Screen-local (x' = −X, y' = down the slope, z = slope normal) into the
    family frame, origin at the screen centre. Right-handed: (−X) × (−s) = n."""
    s, n = slope_dir(), slope_normal()
    rot = Matrix(((-1.0, 0.0, 0.0),
                  (0.0, -s.y, n.y),
                  (0.0, -s.z, n.z))).to_4x4()
    return Matrix.Translation(SLOPE_ORIGIN + s * SCREEN_T) @ rot


def plinth(coll, mats):
    """The foot on the hardpoint: the one part inside the deck footprint.

    Sunk 4 mm, so its bottom face is inside the deck solid rather than on its
    plane, and tall enough to swallow all four bolt bosses (top z 0.254).
    """
    if PLINTH_HX >= BASE_DECK_HX:
        raise SystemExit("plinth must stay inside the deck footprint")
    p = TrackedPart(mats)
    hard = p.slab((-PLINTH_HX, PLINTH_Y0, PLINTH_Z0),
                  (PLINTH_HX, PLINTH_Y1, PLINTH_Z1), DARK)
    p.restamp("plinth")
    p.bevel(hard, width=0.003, segments=2)
    return p.finish("Mesh_ItemScanner_Plinth", coll)


def housing(coll, mats):
    """The console body: one side profile extruded across the arm, with the
    antenna lug on the little-finger flank and four bolt heads on the apron."""
    p = TrackedPart(mats)
    profile = [(Y0, BODY_Z0), (Y1, BODY_Z0), (Y1, ROOF_Z), (ROOF_Y0, ROOF_Z),
               (APRON_Y1, FRONT_Z), (Y0, FRONT_Z)]
    hard = list(p.prism(profile, 2 * HX, axis='X', mat=DARK))
    hard += p.slab(LUG[0], LUG[1], DARK)
    for x, y in BOLT_XY:
        p.cyl((x, y, FRONT_Z + 0.001), BOLT_R, BOLT_H, 'Z', 10, CHROME)
    p.restamp("housing")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_ItemScanner_Housing", coll)


def bezel(coll, mats):
    """Worn-steel frame around the screen aperture, 10 mm proud of the slope
    and 3 mm into it. Built in the screen frame, so its rim is the plate's."""
    p = TrackedPart([mats[STEEL]])
    ox, oy = SCREEN_HW + BEZEL_GAP + BEZEL_RIM, SCREEN_HH + BEZEL_GAP + BEZEL_RIM
    ix, iy = SCREEN_HW + BEZEL_GAP, SCREEN_HH + BEZEL_GAP
    if oy > SLOPE_LEN / 2.0:
        raise SystemExit("bezel overhangs the screen slope")
    lo, hi = -BEZEL_SUNK, BEZEL_PROUD
    hard = []
    hard += p.slab((-ox, -oy, lo), (ox, -iy, hi), 0)
    hard += p.slab((-ox, iy, lo), (ox, oy, hi), 0)
    hard += p.slab((-ox, -iy, lo), (-ix, iy, hi), 0)
    hard += p.slab((ix, -iy, lo), (ox, iy, hi), 0)
    p.restamp("bezel")
    p.bevel(hard, width=0.003, segments=2)
    return place(p.finish("Mesh_ItemScanner_Bezel", coll), screen_matrix())


def screen(coll, mats, crt):
    """The CRT plate. Slot 0 is the CRT, on the front face only; slot 1 the
    dark rim. Not bevelled, and UV-mapped before it is tilted."""
    p = TrackedPart([crt, mats[DARK]])
    faces = p.slab((-SCREEN_HW, -SCREEN_HH, -BEZEL_SUNK),
                   (SCREEN_HW, SCREEN_HH, BEZEL_PROUD - SCREEN_RECESS), 1)
    p.bm.normal_update()
    front = [f for f in faces if f.normal.z > 0.9]
    if len(front) != 1:
        raise SystemExit("screen plate has %d front face(s)" % len(front))
    front[0].material_index = 0
    obj = p.finish("Mesh_Terminal_Scanner_Screen", coll)
    planar_uv(obj, u_axis=0, v_axis=1)
    return place(obj, screen_matrix())


def dial(coll, mats):
    """The knob, protruding toward the hand from the front face.

    Origin at the axle where it meets the face. The skirt is 3 mm inside the
    face, so no face of the knob shares the housing's front plane.
    """
    x, y, z = DIAL_AT
    p = TrackedPart(mats)
    p.cyl((x, y + 0.003 - DIAL_SKIRT_DEPTH / 2, z), DIAL_SKIRT_R,
          DIAL_SKIRT_DEPTH, 'Y', 20, DARK)
    body_y0 = y + 0.003 - DIAL_SKIRT_DEPTH + 0.003
    p.cyl((x, body_y0 - DIAL_DEPTH / 2, z), DIAL_R, DIAL_DEPTH, 'Y', 20, RUBBER)
    cap_y0 = body_y0 - DIAL_DEPTH + 0.003
    p.cyl((x, cap_y0 - DIAL_CAP_DEPTH / 2, z), DIAL_CAP_R, DIAL_CAP_DEPTH,
          'Y', 20, ORANGE)
    # Pointer: a chrome stripe down the rim at twelve o'clock, carried onto
    # the cap face. Off-axis, so the spin is visible.
    p.box((x, body_y0 - DIAL_DEPTH / 2, z + DIAL_R - 0.001),
          (0.0060, DIAL_DEPTH - 0.008, 0.0060), CHROME)
    p.box((x, cap_y0 - DIAL_CAP_DEPTH - 0.001, z + DIAL_CAP_R * 0.5),
          (0.0050, 0.0040, DIAL_CAP_R * 0.9), CHROME)
    p.restamp("dial")
    return p.finish("Mesh_Terminal_Scanner_Dial", coll, origin=DIAL_AT)


def antenna(coll, mats):
    """The whip: rubber base on the lug, tapered rod, chrome tip. Origin at
    the root, so Unity's sway rotates it about where it is planted."""
    root = Vector(ANTENNA_AT)
    a = math.radians(ANTENNA_LEAN_DEG)
    d = Vector((0.0, math.sin(a), math.cos(a)))
    rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    p = TrackedPart(mats)
    p.cyl(root + d * (ANTENNA_BASE_LEN / 2), ANTENNA_BASE_R, ANTENNA_BASE_LEN,
          'Z', 10, RUBBER, rot=rot)
    rod0 = root + d * (ANTENNA_BASE_LEN - 0.004)
    rod_len = ANTENNA_LEN - ANTENNA_BASE_LEN - 0.008
    tube_path(p, [rod0, rod0 + d * (rod_len * 0.45), rod0 + d * rod_len],
              ANTENNA_R, RUBBER, seg=8, joint=False, taper=ANTENNA_TAPER)
    tip_r = ANTENNA_R * ANTENNA_TAPER
    p.cyl(rod0 + d * (rod_len + 0.004), tip_r * 1.6, 0.012, 'Z', 8, CHROME,
          rot=rot)
    p.restamp("antenna")
    return p.finish("Mesh_Terminal_Scanner_Antenna", coll, origin=ANTENNA_AT)


def lamps(coll, mats):
    """Two amber lamps in chrome rings, sunk 4 mm into the apron."""
    p = TrackedPart(mats)
    for x, y in LAMP_XY:
        p.cyl((x, y, FRONT_Z), LAMP_RING_R, 0.008, 'Z', 12, CHROME)
        p.cyl((x, y, FRONT_Z + 0.003), LAMP_R, 0.010, 'Z', 12, AMBER)
    p.restamp("lamps")
    return p.finish("Mesh_ItemScanner_Lamps", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    crt, = link_materials([CRT_MAT])
    coll = collection("Coll_GauntletItemScanner")

    plinth(coll, mats)
    housing(coll, mats)
    bezel(coll, mats)
    screen(coll, mats, crt)
    dial(coll, mats)
    antenna(coll, mats)
    lamps(coll, mats)

    save(out)
    report()
    a = math.radians(ANTENNA_LEAN_DEG)
    tip = Vector(ANTENNA_AT) + Vector((0.0, math.sin(a), math.cos(a))) * ANTENNA_LEN
    print("  roof z %.4f  slope len %.4f  plate aspect %.4f"
          % (ROOF_Z, SLOPE_LEN, SCREEN_HW / SCREEN_HH))
    print("  antenna tip (%.4f, %.4f, %.4f)" % (tip.x, tip.y, tip.z))
    for name in ("Mesh_Terminal_Scanner_Screen", "Mesh_Terminal_Scanner_Dial",
                 "Mesh_Terminal_Scanner_Antenna"):
        o = bpy.data.objects[name]
        print("  %-32s origin (%.4f, %.4f, %.4f)  mats %s"
              % (name, o.location.x, o.location.y, o.location.z,
                 [m.name for m in o.data.materials]))


if __name__ == "__main__":
    main()
