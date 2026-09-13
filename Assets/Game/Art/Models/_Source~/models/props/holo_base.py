"""Holo base — the furniture a map hologram projects from.

Four bases, one per situation, sharing the emitter heads from
`components/props/holo_emitter.py` and the control fittings from
`components/mechanical/panel_control.py`:

| Collection | Silhouette | Where it lives |
|---|---|---|
| `Coll_HoloBase_Puck`     | deck puck, 0.36 m across   | bolted to a ship floor or desk |
| `Coll_HoloBase_Pedestal` | waisted column, 0.87 m     | beside the helm, a lobby |
| `Coll_HoloBase_Table`    | octagonal chart table      | an ops room |
| `Coll_HoloBase_Tripod`   | three-legged field unit    | an outpost, a camp |

Design brief was "minimal and anonymous when turned off": dark steel and panel
grey, no big emissive surfaces — the only light is the emitter's small amber
standby pip, which stays as the signifier that this is a device at all
(GDC-L1-UX-0004); when running, the hologram itself is the salient element and
the base recedes (GDC-L1-UX-0003).

Each collection carries a `Marker_HoloAnchor_*` empty-sized cube at the
emitter aperture — wire it to `MapHologramTerrain.projectorAnchor` in Unity so
the hologram floats over the base instead of beside the player's helmet.

**No armature.** Nothing on any base deforms or moves in play: the hologram is
a runtime shader, the knobs are decorative at this scale, and the tripod's legs
ship fixed because the game has no deploy/fold mechanic to drive them.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, os.path.join(_LIB, "components", "mechanical"))
sys.path.insert(0, os.path.join(_LIB, "components", "props"))

from _buildlib import *  # noqa: E402,F403
from _tracked import TrackedPart  # noqa: E402
from panel_control import (  # noqa: E402
    rocker_bank, rotary_selector, ribbed_knob, guarded_toggle)
from holo_emitter import (  # noqa: E402
    emitter_dish, emitter_ring, emitter_stud)

from mathutils import Matrix  # noqa: E402

# Indices 0-9 must match panel_control.MATS index-for-index, and 10 (GLASS)
# holo_emitter's — their builders write indices, not names. Extras follow.
STEEL, DARK, RUBBER, CHROME, CREAM, RED, BLUE, AMBER, BLACK, CRT = range(10)
GLASS, GREY, RUST = 10, 11, 12
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Plastic_Rubber_Black", "Mat_Metal_Chrome_Scuffed",
        "Mat_Plastic_Cream_Aged", "Mat_Paint_Warn_Red",
        "Mat_Paint_Blue_Station", "Mat_Emissive_Amber",
        "Mat_Neutral_Black_Matte", "Mat_Emissive_Green_CRT",
        "Mat_Glass_Canopy_Tinted", "Mat_Neutral_Panel_Grey",
        "Mat_Metal_Rust_Heavy"]

# Bevel widths: structural shells vs. panel hardware. A knob under a 2.5 mm
# bevel is a blob.
BEVEL_BODY = 0.0025
BEVEL_CTRL = 0.0012


def anchor(coll, mats, z, tag):
    """0.004 m marker cube at the emitter aperture — the projectorAnchor
    hook-up point, same convention as launch_tube's Marker_* objects.

    The origin must BE the aperture, not sit at the world origin with the cube
    floating in mesh space: consumers read the exported node's transform, and a
    marker whose position is all in its vertices reads back as (0,0,0)."""
    p = Part(mats)
    p.box((0, 0, z), (0.004, 0.004, 0.004), DARK)
    return p.finish("Marker_HoloAnchor_%s" % tag, coll, origin=(0, 0, z))


def puck(coll, mats):
    """Deck puck — the anonymous one. A chamfered drum you would walk past,
    until the standby pip says otherwise."""
    p = TrackedPart(mats)
    hard, ctrl = [], []
    p.cyl((0, 0, 0.004), 0.175, 0.008, 'Z', 24, RUBBER)
    p.cyl((0, 0, 0.032), 0.180, 0.048, 'Z', 24, DARK, radius_top=0.166)
    p.cyl((0, 0, 0.060), 0.160, 0.008, 'Z', 24, GREY)
    # Four hold-down bolts through the top plate.
    for i in range(4):
        a = math.radians(45 + i * 90)
        p.cyl((math.cos(a) * 0.140, math.sin(a) * 0.140, 0.0655),
              0.007, 0.005, 'Z', 8, STEEL)
    # Front fascia with a two-paddle rocker.
    hard += p.box((0, -0.163, 0.036), (0.100, 0.024, 0.036), DARK)
    ctrl += rocker_bank(p, (0, -0.175, 0.036), count=2, colours=(BLUE, RED))
    hard += emitter_stud(p, (0, 0, 0.064), radius=0.055)
    p.bevel(hard, width=BEVEL_BODY, segments=2)
    p.bevel(ctrl, width=BEVEL_CTRL, segments=2)
    return p.finish("Mesh_HoloBase_Puck", coll)


def pedestal(coll, mats):
    """Waisted floor column with the dish head — the lobby/helm-side unit."""
    p = TrackedPart(mats)
    hard, ctrl = [], []
    p.cyl((0, 0, 0.025), 0.200, 0.050, 'Z', 20, DARK, radius_top=0.165)
    # Waist: taper down, then flare back out.
    p.cyl((0, 0, 0.255), 0.105, 0.410, 'Z', 20, GREY, radius_top=0.072)
    p.cyl((0, 0, 0.615), 0.072, 0.310, 'Z', 20, GREY, radius_top=0.108)
    # Cable conduit up the back, into a junction box at the collar.
    for dx in (-0.018, 0.018):
        p.cyl((dx, 0.090, 0.38), 0.008, 0.70, 'Z', 8, RUBBER)
    hard += p.box((0, 0.105, 0.720), (0.070, 0.045, 0.075), DARK)
    # Collar and deck the head sits on.
    p.cyl((0, 0, 0.780), 0.130, 0.055, 'Z', 20, DARK)
    p.torus((0, 0, 0.757), 0.128, 0.006, 'Z', 20, 8, CHROME)
    p.cyl((0, 0, 0.812), 0.145, 0.014, 'Z', 20, GREY)
    # Controls on a fascia plate at the collar's front.
    hard += p.box((0, -0.134, 0.780), (0.145, 0.018, 0.058), DARK)
    ctrl += rotary_selector(p, (-0.038, -0.143, 0.780))
    ctrl += guarded_toggle(p, (0.040, -0.143, 0.780))
    # Foot bolts.
    for i in range(6):
        a = math.radians(i * 60)
        p.cyl((math.cos(a) * 0.180, math.sin(a) * 0.180, 0.052),
              0.008, 0.006, 'Z', 8, STEEL)
    hard += emitter_dish(p, (0, 0, 0.819), radius=0.120)
    p.bevel(hard, width=BEVEL_BODY, segments=2)
    p.bevel(ctrl, width=BEVEL_CTRL, segments=2)
    return p.finish("Mesh_HoloBase_Pedestal", coll)


def table(coll, mats):
    """Octagonal chart table with the flush ring emitter — the ops-room unit.
    Big enough that the 1.07 m hologram footprint sits over it comfortably."""
    p = TrackedPart(mats)
    hard, ctrl = [], []
    hard += p.slab((-0.300, -0.300, 0.0), (0.300, 0.300, 0.035), DARK)
    hard += p.box((0, 0, 0.260), (0.260, 0.220, 0.450), GREY)
    p.louvres((-0.104, 0.111, 0.10), (0.104, 0.118, 0.30), 6, axis='X',
              mat=BLACK)
    hard += p.box((0, 0, 0.492), (0.340, 0.300, 0.020), DARK)
    # Octagon top, faces squared to the axes so one fronts the player.
    oct_pts = [(math.cos(math.radians(22.5 + 45 * i)) * 0.475,
                math.sin(math.radians(22.5 + 45 * i)) * 0.475)
               for i in range(8)]
    hard += p.prism(oct_pts, 0.060, axis='Z', mat=DARK, offset=(0, 0, 0.520))
    # Recessed-looking dark well the hologram rises from, 0.8 mm proud.
    p.cyl((0, 0, 0.5478), 0.400, 0.0056, 'Z', 32, BLACK)
    # Rim bolts on the facet midpoints.
    for i in range(8):
        a = math.radians(45 * i)
        r = 0.475 * math.cos(math.radians(22.5)) - 0.020
        p.cyl((math.cos(a) * r, math.sin(a) * r, 0.5525),
              0.008, 0.006, 'Z', 8, STEEL)
    # Control fascia hung under the front facet.
    hard += p.box((0, -0.418, 0.475), (0.180, 0.040, 0.055), DARK)
    ctrl += rocker_bank(p, (-0.045, -0.438, 0.475), count=3)
    ctrl += ribbed_knob(p, (0.055, -0.438, 0.475))
    # Cable drop from the top down the column's back.
    p.cyl((0, 0.140, 0.26), 0.010, 0.46, 'Z', 8, RUBBER)
    hard += emitter_ring(p, (0, 0, 0.5506), radius=0.170)
    p.bevel(hard, width=BEVEL_BODY, segments=2)
    p.bevel(ctrl, width=BEVEL_CTRL, segments=2)
    return p.finish("Mesh_HoloBase_Table", coll)


def tripod(coll, mats):
    """Field tripod — the expedition unit, all legs and no cabinet."""
    p = TrackedPart(mats)
    hard, ctrl = [], []
    p.cyl((0, 0, 0.585), 0.060, 0.110, 'Z', 12, DARK)
    # Three telescoped legs splayed 22 degrees. Segment centres must sit ON
    # the leg line — `rot` spins a cylinder about its own centre, so tilting
    # segments at eyeballed positions leaves them floating apart.
    splay = math.radians(22)
    for i in range(3):
        a = math.radians(90 + i * 120)
        # Ry(-splay) so the cylinder axis and the computed leg direction lie
        # on the same line — the +splay version tilts the tube the other way.
        tilt = (Matrix.Rotation(a, 4, 'Z')
                @ Matrix.Rotation(-splay, 4, 'Y'))
        ax, ay, az = math.cos(a) * 0.045, math.sin(a) * 0.045, 0.600
        # Downward unit direction of this leg.
        dx = math.cos(a) * math.sin(splay)
        dy = math.sin(a) * math.sin(splay)
        dz = -math.cos(splay)

        def on_leg(t):
            return (ax + dx * t, ay + dy * t, az + dz * t)

        # Outer tube, clamp collar, chrome inner tube, upright rubber foot.
        p.cyl(on_leg(0.16), 0.015, 0.32, 'Z', 10, DARK, rot=tilt)
        p.cyl(on_leg(0.30), 0.018, 0.050, 'Z', 10, STEEL, rot=tilt)
        p.cyl(on_leg(0.45), 0.010, 0.36, 'Z', 8, CHROME, rot=tilt)
        fx, fy, _ = on_leg(0.60)
        p.cyl((fx, fy, 0.022), 0.027, 0.044, 'Z', 10, RUBBER)
    # Brace ring tying the legs mid-height.
    p.torus((0, 0, 0.303), 0.165, 0.007, 'Z', 18, 8, STEEL)
    # Crown deck and head.
    p.cyl((0, 0, 0.648), 0.078, 0.016, 'Z', 12, GREY)
    hard += p.box((0, -0.064, 0.585), (0.052, 0.014, 0.052), DARK)
    ctrl += guarded_toggle(p, (0, -0.071, 0.585))
    hard += emitter_dish(p, (0, 0, 0.656), radius=0.095)
    p.bevel(hard, width=BEVEL_BODY, segments=2)
    p.bevel(ctrl, width=BEVEL_CTRL, segments=2)
    return p.finish("Mesh_HoloBase_Tripod", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    c = collection("Coll_HoloBase_Puck")
    puck(c, mats)
    anchor(c, mats, 0.094, "Puck")

    c = collection("Coll_HoloBase_Pedestal")
    pedestal(c, mats)
    anchor(c, mats, 0.871, "Pedestal")

    c = collection("Coll_HoloBase_Table")
    table(c, mats)
    anchor(c, mats, 0.581, "Table")

    c = collection("Coll_HoloBase_Tripod")
    tripod(c, mats)
    anchor(c, mats, 0.708, "Tripod")

    save(out)
    report()


if __name__ == "__main__":
    main()
