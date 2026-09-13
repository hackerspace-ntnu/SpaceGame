"""Oxygen generator — the wall-mounted plant that refills bottles and charges cells.

>>> HAND-EDITED. `oxygen_generator.blend` is the source of truth and carries edits
>>> that exist nowhere else. NEVER re-run this script over it — see the build
>>> record next to it for what the hand edits were.


Laid out from the reference photograph of a service-module oxygen unit — a
column strapped to a rack panel, a control head with coloured caps on top, a
drum with a bolted circular hatch, a banded stack and a base plate of capped
ports — but built in the oxygen tank's own language: pale enamel, one accent per
function, wide chamfers, and as few parts as the read allows.

    2.10 m tall, 0.60 m wide, 0.30 m deep at the tower.
    With a bottle plugged in it reaches 0.86 m out from the wall.

The two docks
-------------
    z 1.60   TANK, middle-high. The bolted circular HATCH is the dock: a bottle
             plugs into it skirt-first and stands straight out from the wall at
             ninety degrees, with the filler arm reaching down onto its cap.
    z 0.70   CELL, middle-low. A green-lipped slot. A slab power cell lies flat
             against the machine, its rectangular charging port in a matching
             rectangular socket.

Round versus rectangular, before either is painted — the colour on each is
confirmation, not the message (GDC-L1-UX-0003), and a cell physically cannot
enter a collar (GDC-L1-UX-0004). Both sit at the heights a standing player's
hands are already at.

What was cut, and why
---------------------
The first build had a saddle cradle lying the bottle along the wall, pipe runs
up both flanks, a pump housing, a slatted equipment panel and a fascia carrying
six kinds of switchgear. Two thirds of the bottle was hidden behind cradle
hardware, and the machine read as a grey texture rather than as a shape. Every
one of those is gone. What is left is the photograph's own five blocks, and the
bottle is now the most legible thing on the wall — which is right, because it is
the only part of the machine a player ever touches.

Orientation and origin
----------------------
The machine's BACK is its mounting plane: that face lies in XZ at y = 0, the
column grows toward -Y, the rack panel sits behind at +Y, and the origin is the
centre of the back at floor level.

    blender --background --python oxygen_generator.py -- --out oxygen_generator.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, LIB)
for _sub in ("mechanical", "props"):
    sys.path.insert(0, os.path.join(LIB, "components", _sub))
from _buildlib import collection, link_materials, parse_out, report, save, start  # noqa: E402
from _tracked import TrackedPart  # noqa: E402
from panel_control import connector_strip, tube_path  # noqa: E402
import grid_panel  # noqa: E402
from dock_cradle import CELL_Y, COLLAR_D, COLLAR_R  # noqa: E402
from power_cell import SLAB_H  # noqa: E402
from oxygen_tank import OXY_LEN, OXY_PLUG  # noqa: E402

# 0-15 match `oxygen_tank` / `power_cell` / `dock_cradle` position for
# position, so appended parts and locally built ones share one list; 16-17 are
# this model's own. Index 0 is structural steel: `bmesh.ops.bevel` stamps every
# edge it creates with it.
(STEEL, DARK, RUBBER, CHROME, CREAM, RED, BLUE, AMBER, BLACK, CRT,
 SHELL, GREY, ORANGE, YELLOW, GREEN, SLATE, CANVAS, BRASS) = range(18)
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Plastic_Rubber_Black", "Mat_Metal_Chrome_Scuffed",
        "Mat_Plastic_Cream_Aged", "Mat_Paint_Warn_Red",
        "Mat_Paint_Blue_Station", "Mat_Emissive_Amber",
        "Mat_Neutral_Black_Matte", "Mat_Emissive_Green_CRT",
        "Mat_Paint_White_Arctic", "Mat_Neutral_Panel_Grey",
        "Mat_Paint_Safety_Orange", "Mat_Plastic_Safety_Yellow",
        "Mat_Paint_Cell_Green", "Mat_Neutral_Slate_Dark",
        "Mat_Fabric_Canvas_Faded", "Mat_Metal_Brass_Tarnished"]

# Two widths, and the split matters. BEVEL_W is the art style's chamfer on the
# machine's big blocks — it has to be visible from across a room. FINE_W is for
# panel hardware: at 10 mm a chamfer exceeds half the thickness of a 14 mm slot
# or a 22 mm vent slat, which does not merely look soft — it swells the part
# past its own bounds and starts clashing with neighbours that were clear of it.
BEVEL_W = 0.010
FINE_W = 0.003

HEIGHT = 2.100
TOWER_X, TOWER_Y = 0.260, -0.260        # tower half-width, front face
POST_X = TOWER_X + 0.010                # corner posts stand PROUD of the tower.
                                        # Flush at TOWER_X they shared its side
                                        # plane and the pair flickered.
# `grid_panel` builds a panel whose face plane is y = 0 with all its geometry
# BEHIND it, in y 0.015 - 0.045 — read off `frame()`, which lays its plate at
# `FRAME_D - FACE_T` .. `FRAME_D`, not assumed. Dropped in at y = 0 it therefore
# leaves a 15 mm reveal behind the tower rather than sharing a plane with it.
RACK_STRAP_Y = grid_panel.FRAME_D - 0.015

# The tower's back at y = 0 is the machine's mounting plane, and it is the ONE
# part allowed to reach it. Every block bolted to the tower stops short of it by
# a stepped amount: built flush, plinth, posts, stack, bands, shoulders and
# fascia all ended on the same plane as the tower's own back and the whole
# column flickered from behind. Nothing back there is ever seen — the rack panel
# covers it — so the step costs nothing.
BACK = (-0.006, -0.012, -0.018, -0.024)

# Five blocks up the column, the photograph's own division. The two dock heights
# are what the brief fixed; the rest was laid out around them.
Z_BASE = 0.180                          # base plate / plinth
Z_CELL = 0.700                          # cell slot centre  (middle-low)
Z_STACK_LO, Z_STACK_HI = 0.900, 1.320   # banded process stack
Z_DOCK = 1.600                          # tank collar centre (middle-high)
Z_DRUM_LO, Z_DRUM_HI = 1.360, 1.840     # the drum the collar is set into
Z_HEAD = 1.860                          # control head

DRUM_R = 0.230
HATCH_Y = -0.320                        # the drum's front face: the dock plane

# A bottle plugs in skirt-first, so its own origin ends up OXY_PLUG behind the
# collar's mouth. Derived, never typed: the collar's depth and the bottle's plug
# depth both live in the files that own them.
DOCK_Y = HATCH_Y - COLLAR_D + OXY_PLUG

COMPONENTS = os.path.join(LIB, "components")
CRADLE_BLEND = os.path.join(COMPONENTS, "mechanical", "dock_cradle.blend")
COLLAR_PARTS = ("Mesh_DockCradle_Collar_Ring", "Mesh_DockCradle_Collar_Lever")
SHOE_PARTS = ("Mesh_DockCradle_Shoe_Body", "Mesh_DockCradle_Shoe_Socket")


def append_objects(blend, names, into):
    """Append (not link) named objects from a component file — an export needs
    real mesh data, and a linked object arrives as a proxy the FBX writer skips.
    Same helper as `repair_station.py`, including the depsgraph update: a freshly
    appended object reports the identity matrix until the view layer updates."""
    with bpy.data.libraries.load(blend, link=False) as (src, dst):
        missing = [n for n in names if n not in set(src.objects)]
        if missing:
            raise SystemExit("Not in %s: %s" % (blend, ", ".join(missing)))
        dst.objects = list(names)
    out = []
    for name in names:
        obj = bpy.data.objects[name]
        into.objects.link(obj)
        out.append(obj)
    bpy.context.view_layer.update()
    return out


def _emit(p, hard, name, coll, origin=(0, 0, 0), fine=()):
    """Bevel the big blocks wide and the small fittings narrow, then emit.

    `fine` is bevelled first: bevelling the coarse set afterwards would re-walk
    edges the fine pass already rounded.
    """
    p.restamp()
    if fine:
        p.bevel(fine, width=FINE_W, segments=2)
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll, origin=origin)


# ---------------------------------------------------------------------------
# The machine
# ---------------------------------------------------------------------------

def rack_panel(coll):
    """The bulkhead panel the unit is strapped to, from `grid_panel.pegboard`.

    Reused rather than rebuilt: its builders take the rectangle to fill, which
    is exactly what that file exists for. Wider than the machine on every side,
    so the unit reads as mounted ON something.

    It gets its OWN material list, because `grid_panel.MATS` is six entries in a
    different order from this model's eighteen and its builders index into their
    own list. Sharing one list is only possible where the indices line up, which
    is why the canister, cell and cradle files agree on 0-15 and this one does
    not pretend to.
    """
    p = TrackedPart(link_materials(grid_panel.MATS))
    grid_panel.pegboard(p, -0.360, 0.360, 0.020, 2.220)
    p.restamp()
    return p.finish("Mesh_OxyGen_RackPanel", coll)


def tower(coll, mats):
    """The column: plinth, cell slot, banded stack, corner posts.

    Five plain blocks with a wide chamfer. The cell slot is genuinely SUNK into
    the face rather than drawn on it, which is the whole affordance — a hole is
    a place to put something, a rectangle is a label.
    """
    p = TrackedPart(mats)
    hard = []

    # Plinth, wider and deeper than the tower — the machine's foot.
    hard += p.slab((-0.290, TOWER_Y - 0.030, BACK[0]), (0.290, 0.0, Z_BASE),
                   GREY)

    # The tower proper, and four corner posts standing 10 mm proud of it.
    hard += p.slab((-TOWER_X, TOWER_Y, Z_BASE), (TOWER_X, 0.0, Z_HEAD), SHELL)
    for sx in (-1, 1):
        hard += p.slab((sx * POST_X, TOWER_Y - 0.010, Z_BASE - 0.012),
                       (sx * (TOWER_X - 0.030), BACK[1], Z_HEAD + 0.008),
                       GREY)

    # The cell slot, sunk 44 mm with an orange-free (green) lip top and bottom.
    ch = SLAB_H + 0.016
    hard += p.slab((-0.244, TOWER_Y + 0.044, Z_CELL - ch / 2 - 0.030),
                   (0.244, TOWER_Y + 0.004, Z_CELL + ch / 2 + 0.030), SLATE)

    # Banded process stack between the two docks.
    hard += p.slab((-0.212, TOWER_Y - 0.026, Z_STACK_LO),
                   (0.212, BACK[2], Z_STACK_HI), GREY)
    for z in (Z_STACK_LO + 0.052, Z_STACK_HI - 0.052):
        hard += p.slab((-0.226, TOWER_Y - 0.038, z - 0.016),
                       (0.226, BACK[3], z + 0.016), SHELL)
    # Vent: one sunk black recess with four slats standing in it.
    fine = p.slab((-0.140, TOWER_Y - 0.020, Z_STACK_LO + 0.098),
                  (0.140, TOWER_Y - 0.008, Z_STACK_HI - 0.098), BLACK)
    for i in range(4):
        z = Z_STACK_LO + 0.116 + i * 0.048
        fine += p.slab((-0.128, TOWER_Y - 0.030, z),
                       (0.128, TOWER_Y - 0.020, z + 0.022), GREY)
    return _emit(p, hard, "Mesh_OxyGen_Tower", coll, fine=fine)


def hatch_drum(coll, mats):
    """The drum whose bolted circular face is the bottle dock.

    In the photograph this is a maintenance hatch. Here it does a job: it is the
    only round feature on the machine and the bottle is the only round thing a
    player carries, so making it the socket costs nothing and means the dock is
    legible from the far side of a room.
    """
    p = TrackedPart(mats)
    hard = []
    zc = (Z_DRUM_LO + Z_DRUM_HI) / 2.0
    p.cyl((0, HATCH_Y + DRUM_R, zc), DRUM_R, Z_DRUM_HI - Z_DRUM_LO, 'Z', 28,
          SHELL)
    for z in (Z_DRUM_LO + 0.034, Z_DRUM_HI - 0.034):
        p.cyl((0, HATCH_Y + DRUM_R, z), DRUM_R + 0.010, 0.032, 'Z', 28, GREY)
    # Shoulders filling the corners between the round drum and the square tower.
    # Inset from the posts, so nothing here meets the tower's own side plane.
    # Inset in z as well as pulled off the back plane: sharing the drum's own
    # top and bottom cap planes was the second clash here.
    for sx in (-1, 1):
        hard += p.slab((sx * (TOWER_X - 0.006), TOWER_Y + 0.070,
                        Z_DRUM_LO + 0.010),
                       (sx * 0.176, BACK[2], Z_DRUM_HI - 0.010), SHELL)
    return _emit(p, hard, "Mesh_OxyGen_HatchDrum", coll)


def hatch(coll, mats):
    """The bolted disc the bottle plugs into — the tank dock's own face plate.

    A separate object because the game reaches it by role: it is the surface a
    bottle mates with, and the part a builder parents an item to.
    """
    p = TrackedPart(mats)
    hard = []
    p.cyl((0, HATCH_Y + 0.024, Z_DOCK), COLLAR_R + 0.048, 0.048, 'Y', 26, GREY)
    p.cyl((0, HATCH_Y + 0.006, Z_DOCK), COLLAR_R + 0.028, 0.016, 'Y', 26,
          SHELL)
    for i in range(10):
        a = 2 * math.pi * i / 10
        p.cyl((math.cos(a) * (COLLAR_R + 0.034), HATCH_Y + 0.012,
               Z_DOCK + math.sin(a) * (COLLAR_R + 0.034)),
              0.011, 0.026, 'Y', 8, DARK)
    fine = p.box((0, HATCH_Y + 0.012, Z_DOCK - COLLAR_R - 0.086),
                 (0.180, 0.024, 0.048), SHELL)
    fine += p.box((0, HATCH_Y + 0.002, Z_DOCK - COLLAR_R - 0.086),
                  (0.146, 0.016, 0.028), SLATE)
    return _emit(p, hard, "Mesh_OxyGen_Hatch", coll, origin=(0, HATCH_Y, Z_DOCK),
                 fine=fine)


def filler(coll, mats):
    """The arm that reaches down onto a docked bottle's cap and fills it.

    It comes from ABOVE rather than end-on because the bottle's own bail sweeps
    the space in front of its cap — the handle a player grabs and the coupling
    cannot occupy the same 60 mm. Hung off the drum's top band, so the arm has a
    visible reason to be where it is.
    """
    p = TrackedPart(mats)
    hard = []
    # The nozzle sits over the bottle's cap, which is at the far end of it.
    nose_y = DOCK_Y - OXY_LEN + 0.056
    top = Z_DOCK + 0.230
    hard += p.box((0, HATCH_Y - 0.050, Z_DRUM_HI - 0.020), (0.150, 0.100, 0.070),
                  GREY)
    hard += p.box((0, (HATCH_Y - 0.050 + nose_y) / 2.0, top),
                  (0.096, abs(nose_y - HATCH_Y + 0.050), 0.070), SHELL)
    hard += p.box((0, nose_y, top - 0.016), (0.130, 0.116, 0.084), GREY)
    fine = p.box((0, nose_y, top + 0.030), (0.086, 0.078, 0.030), ORANGE)
    # The coupling head, dropping onto the cap.
    p.cyl((0, nose_y, top - 0.086), 0.044, 0.108, 'Z', 16, CHROME)
    p.cyl((0, nose_y, top - 0.132), 0.030, 0.028, 'Z', 14, DARK)
    p.cyl((0, nose_y, top + 0.052), 0.024, 0.018, 'Z', 12, AMBER)
    # Supply hose, slung from the drum along the arm.
    tube_path(p, [(0.072, HATCH_Y - 0.046, Z_DRUM_HI + 0.010),
                  (0.072, HATCH_Y - 0.090, top + 0.018),
                  (0.072, nose_y + 0.030, top + 0.018)],
              0.017, RUBBER, seg=8)
    return _emit(p, hard, "Mesh_OxyGen_Filler", coll, origin=(0, nose_y, top),
                 fine=fine)


def control_head(coll, mats):
    """The fascia: three capped valves, one connector bank, one amber lamp.

    Yellow caps because yellow is the machine's service colour throughout — the
    base plate's caps are the same yellow — and deliberately NOT orange or
    green, which are spoken for by the two docks and would say 'a bottle goes
    here'. The amber lamp is the only emissive above the docks, so it is where
    the eye goes when nothing is plugged in.
    """
    p = TrackedPart(mats)
    hard = []
    fy = TOWER_Y - 0.040
    hard += p.slab((-0.280, fy, Z_HEAD), (0.280, BACK[0], HEIGHT), SHELL)
    hard += p.slab((-0.256, fy - 0.008, Z_HEAD + 0.024),
                   (0.256, fy, HEIGHT - 0.024), SLATE)
    fine = []
    for x in (-0.170, -0.056, 0.058):
        p.cyl((x, fy - 0.020, Z_HEAD + 0.116), 0.040, 0.044, 'Y', 16, GREY)
        p.cyl((x, fy - 0.038, Z_HEAD + 0.116), 0.033, 0.016, 'Y', 16, YELLOW)
        fine += p.box((x, fy - 0.050, Z_HEAD + 0.116), (0.014, 0.014, 0.052),
                      DARK)
    connector_strip(p, (0.180, fy - 0.020, Z_HEAD + 0.108), rows=2, dots=6,
                    pitch=0.0130)
    p.cyl((0.180, fy - 0.014, Z_HEAD + 0.052), 0.028, 0.024, 'Y', 12, GREY)
    # Buried INTO the bezel rather than stacked on its front face: at 1 mm in
    # front the two were parallel and within the flicker threshold.
    p.cyl((0.180, fy - 0.022, Z_HEAD + 0.052), 0.020, 0.018, 'Y', 12, AMBER)
    return _emit(p, hard, "Mesh_OxyGen_ControlHead", coll, fine=fine)


def base_panel(coll, mats):
    """The bottom plate: three capped service ports, a gauge and two connectors.

    Lifted almost literally from the photograph, where the same row sits under
    the column. It is the detail that makes the machine look plumbed into
    something rather than standing on the floor by itself.
    """
    p = TrackedPart(mats)
    hard = []
    fy = TOWER_Y - 0.038
    hard += p.slab((-0.256, fy, 0.030), (0.256, TOWER_Y - 0.004, Z_BASE - 0.026),
                   SLATE)
    for x in (-0.168, -0.060, 0.048):
        p.cyl((x, fy - 0.008, 0.116), 0.040, 0.028, 'Y', 16, GREY)
        p.cyl((x, fy - 0.024, 0.116), 0.032, 0.016, 'Y', 16, YELLOW)
    p.cyl((0.166, fy - 0.008, 0.116), 0.044, 0.030, 'Y', 18, GREY)
    p.cyl((0.166, fy - 0.025, 0.116), 0.034, 0.014, 'Y', 18, SLATE)
    p.cyl((0.166, fy - 0.033, 0.116), 0.026, 0.006, 'Y', 18, CRT)
    for x in (-0.112, 0.112):
        p.cyl((x, fy - 0.008, 0.060), 0.032, 0.026, 'Y', 6, DARK)
        tube_path(p, [(x, fy - 0.024, 0.060), (x, fy - 0.064, 0.040),
                      (x, fy - 0.074, 0.008)], 0.015, RUBBER, seg=8)
    return _emit(p, hard, "Mesh_OxyGen_BasePanel", coll)


def straps(coll, mats):
    """Webbing straps and brass buckles lashing the unit to the rack panel.

    Straight out of the photograph, and they earn their place twice: they say
    the machine is fitted rather than modelled in, and they are the only soft
    material on 2 m of hard surface. Three, not four — the fourth sat across the
    drum and had nothing to bear on.
    """
    p = TrackedPart(mats)
    hard, fine = [], []
    span = RACK_STRAP_Y - TOWER_Y
    for z in (Z_BASE + 0.240, Z_STACK_LO + 0.210, Z_DRUM_HI + 0.010):
        for sx in (-1, 1):
            hard += p.box((sx * (POST_X + 0.026), TOWER_Y + span / 2.0, z),
                          (0.052, span, 0.056), CANVAS)
            fine += p.box((sx * (POST_X + 0.046), RACK_STRAP_Y, z),
                          (0.034, 0.038, 0.068), BRASS)
        hard += p.box((0, TOWER_Y - 0.014, z), (2 * POST_X + 0.006, 0.020,
                                                0.048), CANVAS)
        fine += p.box((0.092, TOWER_Y - 0.028, z), (0.074, 0.030, 0.062), BRASS)
    return _emit(p, hard, "Mesh_OxyGen_Straps", coll, fine=fine)


def marker(coll, mats, at, name, rot=None):
    """A 6 mm cube whose ORIGIN is a docked pose.

    The generator knows where a bottle and a cell sit, because the docks were
    placed against those items' own published dimensions. Writing that pose into
    the file means a Unity builder parents an item to a transform instead of
    re-deriving the arithmetic from three component files and getting it 4 mm
    wrong.
    """
    p = TrackedPart(mats)
    p.box(at, (0.006, 0.006, 0.006), CHROME)
    obj = p.finish(name, coll, origin=at)
    if rot is not None:
        obj.rotation_euler = rot.to_euler()
    return obj


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_OxygenGenerator")

    rack_panel(coll)
    tower(coll, mats)
    hatch_drum(coll, mats)
    hatch(coll, mats)
    filler(coll, mats)
    control_head(coll, mats)
    base_panel(coll, mats)
    straps(coll, mats)

    # The two docks, appended from the component and moved onto the machine.
    # Renamed to their role here: a reader of the model should not have to know
    # which component file a part came from to know what it does.
    for obj in append_objects(CRADLE_BLEND, list(COLLAR_PARTS), coll):
        obj.location = Vector(obj.location) + Vector((0.0, HATCH_Y, Z_DOCK))
        obj.name = obj.name.replace("Mesh_DockCradle_Collar",
                                    "Mesh_OxyGen_TankDock")
    for obj in append_objects(CRADLE_BLEND, list(SHOE_PARTS), coll):
        obj.location = Vector(obj.location) + Vector(
            (0.0, TOWER_Y, Z_CELL - (SLAB_H + 0.016) / 2.0))
        obj.name = obj.name.replace("Mesh_DockCradle_Shoe",
                                    "Mesh_OxyGen_CellDock")

    # Docked poses.
    #
    # The bottle plugs in base-first, so its local +Z has to point OUT of the
    # wall, along -Y, and its face — window, spine, latch — has to end up
    # pointing UP at the filler rather than down at the floor. Checked term by
    # term rather than assumed, because a rotation that is 180 degrees out about
    # its own axis still looks like a bottle in a socket:
    #
    #     Rx(90) . (0,0,1)  = (0,-1,0)   local up  -> out of the wall
    #     Ry(180). (0,0,-1) = (0,0,1)    local front -> up
    #
    # so the pose is Ry(180) @ Rx(90), and NOT Rx(90) alone, which lands the
    # gauge window face down against the floor.
    plug = (Matrix.Rotation(math.radians(180), 4, 'Y')
            @ Matrix.Rotation(math.radians(90), 4, 'X'))
    marker(coll, mats, (0.0, DOCK_Y, Z_DOCK), "Marker_OxyGen_TankDock",
           rot=plug)
    marker(coll, mats,
           (0.0, TOWER_Y + CELL_Y, Z_CELL - (SLAB_H + 0.016) / 2.0 + 0.008),
           "Marker_OxyGen_CellDock")

    save(out)
    report()


if __name__ == "__main__":
    main()
