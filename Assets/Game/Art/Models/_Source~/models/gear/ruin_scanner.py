"""Ruin Scanner — the forearm-worn cone-of-light emitter.

Replaces the Unity built-in cube the Ruin Scanner artifact wears today. The
device is worn, not held: the same webbing cuff the grapple bracer sits on, a
spine down the back of the forearm clamped to it with the bracer's own bands,
and on the spine an emitter housing — a smooth capsule with a heat sink at the
elbow end, a readout the wearer can glance down at, and a hooded lens at the
wrist end that the scanning cone comes out of.

Assembly in the manner of `grapple_bracer.py`. The cuff and the clamp bands
are reused; the housing and everything on it is unique to this model.

| Object | Where it comes from |
|---|---|
| `Mesh_ArmCuff_Webbing`        | `components/props/arm_cuff.blend`, unchanged |
| `Mesh_RuinScanner_Spine`      | channel down the back of the cuff |
| `Mesh_RuinScanner_ClampFront` | `grapple_bracer._clamp_band`, the family's cuff clamp |
| `Mesh_RuinScanner_ClampRear`  | same |
| `Mesh_RuinScanner_Housing`    | the emitter body |
| `Mesh_RuinScanner_Panel`      | painted top panel with the ready lamps and arming stripe |
| `Mesh_RuinScanner_Heatsink`   | fins at the elbow end |
| `Mesh_RuinScanner_Readout`    | the readout's bezel |
| `Mesh_RuinScanner_Screen`     | the lit face alone, so Unity can paint it |
| `Mesh_RuinScanner_Hood`       | the lens hood and its brass rings |
| `Mesh_RuinScanner_Lens`       | the emissive lens, recessed in the hood |
| `Mesh_RuinScanner_Conduit`    | cable from the housing down into the spine |
| `Emitter`                     | empty at the lens face, +Z out of the lens in Unity |

Every logical part is its own object, per the skill's geometry rules; only
fasteners (rivets, bolt heads) live inside the part they fasten. Nothing in
Unity binds by name except the `Emitter` empty, which the prefab points its
`muzzle` field at.


## The frame this is built in

**Arm along Y, wrist at y = 0, elbow toward +Y, forward is −Y, dorsal is +Z.**
Identical to `grapple_bracer.py`, so the two ship through the same
`ItemGrip` offsets family (`rotationOffset = (0, 0, -90)`, 2.1x wear). The
derivation of those numbers is in `grapple_bracer_BUILD.md` and is not
repeated here.

The cuff arrives through `grapple_bracer.cuff_matrix()` — `R_x(-90) @
R_z(-90)` — which puts its mounting boss under the spine and its buckles on
the −X flank.

The `Emitter` empty has identity rotation. `_exportlib` maps Blender −Y onto
Unity +Z, so an unrotated empty's Unity forward is this model's forward: out
of the lens, down the arm past the hand.


## Scale

Authored at real human scale like everything else in this library and worn
at 2.1x via `holdSize`, for the reason `grapple_bracer.py` records: the rig's
forearm is 0.393 m against 0.26 on a person, and 2.1 is what makes the cuff's
elbow section cover the suit sleeve.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
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
from _tracked import TrackedPart  # noqa: E402
from arm_cuff import rrect  # noqa: E402
import grapple_bracer  # noqa: E402
from grapple_bracer import (CLAMPS, SPINE_Z0, SPINE_Z1,  # noqa: E402
                            _clamp_band, cuff_matrix)
from item_scanner import append_objects, place  # noqa: E402
from panel_control import tube_path  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

CUFF = os.path.join(_LIB, "components", "props", "arm_cuff.blend")

STEEL, DARK, PALE, BRASS, CHROME, RUBBER, BLACK, AMBER, CRT, WARN = range(10)
MATS = ["Mat_Metal_Steel_Worn",        # spine, clamps, housing
        "Mat_Metal_Steel_Dark",        # heat sink, readout bezel
        "Mat_Paint_Hull_Bleached",     # painted top panel
        "Mat_Metal_Brass_Tarnished",   # lens rings
        "Mat_Metal_Chrome_Scuffed",    # fasteners, conduit
        "Mat_Plastic_Rubber_Black",    # cuff pads under the clamps
        "Mat_Neutral_Black_Matte",     # inside of the lens hood
        "Mat_Emissive_Amber",          # the lens and the two ready lamps
        "Mat_Emissive_Green_CRT",      # the readout
        "Mat_Paint_Warn_Red"]          # arming stripe

# `_clamp_band` is the grapple bracer's, and it stamps its faces with the
# bracer's own STEEL and CHROME indices. Those must be the same slots here or
# the bands arrive in the wrong colours without a single error.
if (grapple_bracer.STEEL, grapple_bracer.CHROME) != (STEEL, CHROME):
    raise SystemExit("MATS must keep STEEL and CHROME on the grapple bracer's "
                     "indices for the shared clamp band")

BEVEL_W = 0.0016

# --- the layout -------------------------------------------------------------
#
# Read down the arm from the elbow; a Y unless it says otherwise. The spine and
# clamp stations are the bracer's, so the two gauntlets sit on the cuff the
# same way and the bands land on the same two rings of the sleeve.
SPINE_Y0, SPINE_Y1 = -0.0200, 0.2000

# The housing sits on the spine rails and overhangs the cuff's wrist end by
# 30 mm, so the lens is clear of the hand and its cone has nothing of the
# cuff in front of it. Stations are (y, width, depth); the BOTTOM is held at a
# constant z rather than the centre, so the tail taper lifts the roofline and
# never the floor — a floor that lifts with the taper exposes the rail tops.
HOUSING_Z0 = SPINE_Z1 - 0.0030             # 3 mm into the rail tops
HOUSING = [(-0.0300, 0.0400, 0.0400), (-0.0120, 0.0480, 0.0440),
           (0.0900, 0.0500, 0.0440), (0.1500, 0.0460, 0.0420),
           (0.1720, 0.0420, 0.0360)]
HOUSING_CORNER = 0.35

LENS_Z = HOUSING_Z0 + HOUSING[0][2] / 2.0  # the lens centreline, 0.093
HOOD_Y0, HOOD_Y1 = -0.0520, -0.0260        # mouth and the end buried in the housing
HOOD_R, HOOD_WALL = 0.0210, 0.0040
LENS_Y = -0.0410                           # lens centre, recessed 11 mm in the hood
LENS_FACE_Y = LENS_Y - 0.0030              # where the cone starts
EMITTER = (0.0, LENS_FACE_Y, LENS_Z)

PANEL_Y0, PANEL_Y1 = -0.0100, 0.0780
READOUT_Y = 0.1000
READOUT_TILT = -20.0     # degrees about X; negative tips local +Z toward +Y (the elbow)
FIN_Y0, FIN_COUNT, FIN_STEP = 0.1220, 5, 0.0100


def _housing_at(y):
    """Housing width and depth at `y`, linearly between stations.

    Same job as `arm_cuff._at`: everything sitting on the housing is placed
    against its surface, and the surface tapers, so a fin or a lamp written
    at a fixed z is buried at one end of the body and floating at the other.
    """
    if y <= HOUSING[0][0]:
        return HOUSING[0][1], HOUSING[0][2]
    for (y0, w0, d0), (y1, w1, d1) in zip(HOUSING, HOUSING[1:]):
        if y <= y1:
            t = (y - y0) / (y1 - y0)
            return w0 + (w1 - w0) * t, d0 + (d1 - d0) * t
    return HOUSING[-1][1], HOUSING[-1][2]


def _housing_top(y):
    return HOUSING_Z0 + _housing_at(y)[1]


# ---------------------------------------------------------------------------
# Parts
# ---------------------------------------------------------------------------

def spine(coll, mats):
    """A channel down the back of the cuff: floor between two side rails.

    The floor is narrower than the rails and its underside sits 0.5 mm above
    theirs, so no face of one lies in a plane of the other — the bracer's
    spine builds them coplanar and shares the flicker.
    """
    p = TrackedPart(mats)
    hard = []
    hard += p.slab((-0.0135, SPINE_Y0, SPINE_Z0 + 0.0005),
                   (0.0135, SPINE_Y1, 0.0610), STEEL)
    for sx in (-1, 1):
        hard += p.slab((sx * 0.0130, SPINE_Y0, SPINE_Z0),
                       (sx * 0.0180, SPINE_Y1, SPINE_Z1), STEEL)
    # Only the tail of the channel shows behind the housing; two rivets say
    # the floor is fastened rather than floating.
    p.rivets((0.0, 0.1780, 0.0615), (0.0, 0.1960, 0.0615), 2, radius=0.0022,
             height=0.0022, axis='Z', mat=CHROME)
    p.restamp("spine")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_RuinScanner_Spine", coll)


def clamp(coll, mats, name, station):
    """One of the bracer's clamp bands, with its rubber pad under the spine."""
    y, hx, hz = station
    p = TrackedPart(mats)
    hard = _clamp_band(p, y, hx, hz)
    hard += p.box((0.0, y, SPINE_Z0 - 0.0025), (0.0420, 0.0150, 0.0040), RUBBER)
    p.restamp(name)
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll)


def housing(coll, mats):
    """The emitter body: a lofted capsule on the spine, plus its side bolts.

    `loft(axis='Y')` maps a profile (u, v) onto (x, z) — `_plane_point('Y')`
    is (u, w, v) — so each station's profile is offset in v so that its
    bottom lands on `HOUSING_Z0`.
    """
    p = TrackedPart(mats)
    sections = []
    for y, w, d in HOUSING:
        prof = rrect(w, d, HOUSING_CORNER)
        zc = HOUSING_Z0 + d / 2.0
        sections.append((y, [(u, v + zc) for u, v in prof]))
    p.loft(sections, axis='Y', mat=STEEL)

    # Bolt heads where the hood collar meets the body, on both flanks.
    y = -0.0200
    hw = _housing_at(y)[0] / 2.0
    for sx in (-1, 1):
        for dz in (-0.0090, 0.0090):
            p.cyl((sx * (hw + 0.0005), y, LENS_Z + dz), 0.0026, 0.0030, 'X',
                  8, CHROME)
    p.restamp("housing")
    return p.finish("Mesh_RuinScanner_Housing", coll)


def panel(coll, mats):
    """Painted top panel, 1.5 mm proud, carrying the lamps and arming stripe.

    The housing roof is flat between the second and third stations, which is
    why the panel ends at 0.078 and the readout starts behind it.
    """
    p = TrackedPart(mats)
    hard = []
    top = _housing_top(PANEL_Y0)
    hard += p.slab((-0.0130, PANEL_Y0, top - 0.0015), (0.0130, PANEL_Y1, top + 0.0015),
                   PALE)
    for sx in (-1, 1):
        p.cyl((sx * 0.0080, 0.0680, top + 0.0025), 0.0035, 0.0030, 'Z', 10, AMBER)
    hard += p.box((0.0, -0.0030, top + 0.0020), (0.0200, 0.0050, 0.0015), WARN)
    p.rivets((-0.0100, 0.0300, top + 0.0020), (0.0100, 0.0300, top + 0.0020), 3,
             radius=0.0018, height=0.0018, axis='Z', mat=CHROME)
    p.restamp("panel")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_RuinScanner_Panel", coll)


def heatsink(coll, mats):
    """Fins across the roof at the elbow end, each rooted in the taper.

    Deliberately not bevelled: a 2.5 mm fin has no edge to chamfer, and the
    bevel's index-0 faces turned the first build's fins two-thirds steel.
    """
    p = TrackedPart(mats)
    for i in range(FIN_COUNT):
        y = FIN_Y0 + i * FIN_STEP
        top = _housing_top(y)
        p.box((0.0, y, top + 0.0020), (0.0280, 0.0025, 0.0140), DARK)
    p.restamp("heatsink")
    return p.finish("Mesh_RuinScanner_Heatsink", coll)


def readout_matrix():
    """The readout's tilt. `R_x(-20)` tips local +Z toward +Y — up and toward
    the elbow, where the wearer's eye is. `+20` faces it at the hand."""
    return Matrix.Rotation(math.radians(READOUT_TILT), 4, 'X')


def readout(coll, mats):
    """The readout bezel: a wedge sunk into the roof, its high edge at the elbow."""
    p = TrackedPart(mats)
    hard = p.box((0.0, READOUT_Y, _housing_top(READOUT_Y) + 0.0005),
                 (0.0300, 0.0260, 0.0120), DARK, rot=readout_matrix())
    p.restamp("readout")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_RuinScanner_Readout", coll)


def screen(coll, mats):
    """The lit face alone, 1 mm proud of the bezel and 0.2 mm sunk into it.

    Separate so Unity can put a shader on it without touching the bezel, the
    way `item_scanner` ships its screen.
    """
    p = TrackedPart(mats)
    rot = readout_matrix()
    centre = (Vector((0.0, READOUT_Y, _housing_top(READOUT_Y) + 0.0005))
              + rot.to_3x3() @ Vector((0.0, 0.0, 0.0064)))
    p.box(centre, (0.0240, 0.0200, 0.0012), CRT, rot=rot)
    p.restamp("screen")
    return p.finish("Mesh_RuinScanner_Screen", coll)


def hood(coll, mats):
    """Lens hood: a black-lined steel tube out of the housing's front face,
    a brass collar hiding the seam and a brass ring at the mouth."""
    p = TrackedPart(mats)
    depth = HOOD_Y1 - HOOD_Y0
    yc = (HOOD_Y0 + HOOD_Y1) / 2.0
    p.tube((0.0, yc, LENS_Z), HOOD_R, HOOD_WALL * 0.5, depth, 'Y', 16, STEEL)
    p.tube((0.0, yc, LENS_Z), HOOD_R - HOOD_WALL * 0.5 + 0.0003, HOOD_WALL * 0.5,
           depth - 0.0010, 'Y', 16, BLACK)
    p.torus((0.0, HOOD_Y1 - 0.0050, LENS_Z), HOOD_R + 0.0012, 0.0030, 'Y', 16, 6,
            BRASS)
    p.torus((0.0, HOOD_Y0 + 0.0008, LENS_Z), HOOD_R - 0.0008, 0.0026, 'Y', 16, 6,
            BRASS)
    p.restamp("hood")
    return p.finish("Mesh_RuinScanner_Hood", coll)


def lens(coll, mats):
    """The lens: a domed amber frustum, wide end into the hood wall.

    `cyl`'s `radius_top` is the +Y end, so the narrow face is the one at
    −Y — the one that looks out of the hood.
    """
    p = TrackedPart(mats)
    inner = HOOD_R - HOOD_WALL
    p.cyl((0.0, LENS_Y, LENS_Z), inner - 0.0035, 0.0060, 'Y', 16, AMBER,
          radius_top=inner + 0.0005)
    p.restamp("lens")
    return p.finish("Mesh_RuinScanner_Lens", coll, origin=EMITTER)


def conduit(coll, mats):
    """Cable out of the housing's tail, down into the starboard spine rail.

    Both ends are buried: it starts inside the body and ends inside the rail,
    so it reads as a run between two things rather than a stub."""
    p = TrackedPart(mats)
    tube_path(p, [(0.0100, 0.1650, HOUSING_Z0 + 0.0120),
                  (0.0100, 0.1860, HOUSING_Z0 + 0.0040),
                  (0.0155, 0.1860, SPINE_Z0 + 0.0100)], 0.0025, CHROME, seg=6)
    p.restamp("conduit")
    return p.finish("Mesh_RuinScanner_Conduit", coll)


def emitter(coll):
    """Where the cone starts: on the lens face, on the hood's axis.

    Identity rotation on purpose — see the module docstring for what that
    means after export.
    """
    obj = bpy.data.objects.new("Emitter", None)
    obj.empty_display_type = 'ARROWS'
    obj.empty_display_size = 0.03
    obj.location = Vector(EMITTER)
    coll.objects.link(obj)
    return obj


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_RuinScanner")

    for obj in append_objects(CUFF, ["Mesh_ArmCuff_Webbing"], coll):
        place(obj, cuff_matrix())

    spine(coll, mats)
    clamp(coll, mats, "Mesh_RuinScanner_ClampFront", CLAMPS[0])
    clamp(coll, mats, "Mesh_RuinScanner_ClampRear", CLAMPS[1])
    housing(coll, mats)
    panel(coll, mats)
    heatsink(coll, mats)
    readout(coll, mats)
    screen(coll, mats)
    hood(coll, mats)
    lens(coll, mats)
    conduit(coll, mats)
    emitter(coll)

    save(out)
    report()


if __name__ == "__main__":
    main()
