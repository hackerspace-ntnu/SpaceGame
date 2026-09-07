"""Vacuum canister — the bottle you suck a creature into, and the bottle it is in.

Design: `docs/AI/systems/Artifacts/VacuumCanister.md`. A wide-mouthed vessel with a
thick glass window down one side and a heavy latch on top. Hold Use on something
and it is dragged in; the canister then *is a different item*, carrying that
specific creature, until somebody uncorks it.

Empty and full are two item identities, so they are two variations here — and the
whole job of the model is that a player can tell a shelf of them apart from across
a room. That is done on **three redundant channels**, only one of which is colour
(`GDC-L1-UX-0003` ranks by salience, `GDC-L1-UX-0006` forbids colour-only):

  1. THE LID, which is silhouette and reads from any angle and any distance. Empty:
     thrown back off the mouth, the open throat and the iris leaves visible. Full:
     shut over the mouth with the hook engaged and a red locked tab showing.
  2. WHAT IS IN THE GLASS, which is mass. Empty: a bare slate emitter rod in an
     empty vessel. Full: five lit containment rings with a small dark animal
     curled between them — the joke the design doc says is worth the extra work.
  3. Colour, third and last: the field's cyan. It only ever confirms 1 and 2.

Orientation and origin
----------------------
The canister STANDS: axis +Z, origin at the centre of its base, window and gauge
facing −Y (the library's forward), handle on +Y. That is `oxygen_tank.blend`'s
convention exactly, so the two read as the same issued kit on a shelf.

The **mouth points +Z**, which is where the design puts it — the latch is on top
and the shutter is under the latch. It is therefore not the axis the item is aimed
along, and the prefab's `ItemGrip.rotationOffset` has to say so. `Marker_Muzzle`
carries the mouth centre and `Marker_Grip` the hand, so wave 2 can derive that
without opening Blender.

    blender --background --python vacuum_canister.py -- --out vacuum_canister.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, LIB)
sys.path.insert(0, os.path.join(LIB, "components", "props"))

from _buildlib import (append_objects, collection, link_materials, parse_out,  # noqa: E402
                       report, save, start)
from _tracked import TrackedPart  # noqa: E402
from device_kit import (BEVEL_SEG, BEVEL_W, MATS, SEG, BLACK, BLUE, CHROME,  # noqa: E402
                        CYAN, DARK, GLASS, GREY, RED, RUBBER, SHELL, SLATE,
                        STEEL)

PROPS = os.path.join(LIB, "components", "props")
NOZZLE = os.path.join(PROPS, "device_nozzle.blend")
SPRAYER = os.path.join(PROPS, "sprayer_kit.blend")

# --- the vessel, bottom to top ----------------------------------------------

FOOT_TOP = 0.034
BODY_LO, BODY_HI = 0.028, 0.366
BODY_R, BODY_WALL = 0.088, 0.010
COLLAR_LO, COLLAR_HI = 0.356, 0.416
COLLAR_R = 0.100                        # the widest point on the canister

MOUTH_Z = 0.416                         # the rim plane the iris sits in
IRIS_SEAT = MOUTH_Z - 0.011             # the iris component's own origin: its
                                        # ring reaches 0.011 past its seat plane
                                        # toward the exit, measured off the file

HINGE = Vector((0.0, 0.090, 0.408))     # the lid's pivot, on the collar's back
LID_DISC_Z = 0.432                      # the shut lid's plate, 3 mm clear of the
                                        # mouth rim, which tops out at 0.420
LID_OPEN = -148.0                       # degrees. NEGATIVE takes the lid up and
                                        # BACK: a rotation about X carries +Y to
                                        # (cos, sin) in the YZ plane, so a lid
                                        # reaching -Y from its hinge only rises
                                        # for a negative angle. The positive
                                        # version drives it down through the
                                        # collar and looks almost right.

WINDOW_LO, WINDOW_HI = 0.110, 0.300
WINDOW_HALF_X = 0.052

HANDLE_LO, HANDLE_HI = 0.170, 0.310
HANDLE_OUT = 0.142

GAUGE_AT = (0.0, -0.086, 0.330)         # seat of Mesh_SprayerGauge_Plate, on the
                                        # NECK rather than on the collar itself.
                                        # The design says "beside the latch", and
                                        # the collar is where the latch keeper
                                        # and the lid's hook both live: a gauge
                                        # there is either inside the keeper or
                                        # buried under the collar's own radius.
                                        # Directly below it, on the barrel, is
                                        # the same place to look and the only one
                                        # that is actually readable in the hand.


def _emit(p, hard, name, coll, origin=(0, 0, 0)):
    p.restamp()
    p.bevel(hard, width=BEVEL_W, segments=BEVEL_SEG)
    return p.finish(name, coll, origin=origin)


def _marker(name, at, coll, mats):
    """A 4 mm cube standing in for an empty.

    Empties do not survive `object_types={"MESH"}`, and this library ships its
    sockets as tiny meshes for exactly that reason — see `net_gun.blend`. The
    Unity prefab reads the position and deletes the cube.
    """
    p = TrackedPart(mats)
    p.box((0, 0, 0), (0.004, 0.004, 0.004), STEEL)
    obj = p.finish(name, coll)
    obj.location = at
    return obj


# --- shared shell -----------------------------------------------------------

def foot(coll, mats):
    """Splayed base with six standing legs, so it sits and reads as moulded."""
    p = TrackedPart(mats)
    hard = p.cyl((0, 0, FOOT_TOP / 2), 0.098, FOOT_TOP, axis='Z', seg=SEG,
                 mat=SHELL, radius_top=0.090)
    hard += p.tube((0, 0, 0.006), 0.099, 0.012, 0.012, axis='Z', seg=SEG,
                   mat=BLUE)
    for i in range(6):
        a = 2 * math.pi * i / 6
        p.box((0.093 * math.cos(a), 0.093 * math.sin(a), 0.010),
              (0.020, 0.014, 0.020), RUBBER, rot=Matrix.Rotation(a, 4, 'Z'))
    return _emit(p, hard, "Mesh_VacuumCanister_Foot", coll)


def body(coll, mats):
    """Hollow barrel with a recessed window bay.

    A TUBE and not a solid cylinder: the iris leaves fold 60 mm back into the
    throat when the shutter is open, and a capped barrel would have them poking
    through their own vessel. It is also what makes the window a window.
    """
    p = TrackedPart(mats)
    hard = p.tube((0, 0, (BODY_LO + BODY_HI) / 2), BODY_R, BODY_WALL,
                  BODY_HI - BODY_LO, axis='Z', seg=SEG, mat=SHELL)
    hard += p.cyl((0, 0, BODY_LO + 0.010), BODY_R - 0.004, 0.020, axis='Z',
                  seg=SEG, mat=GREY)

    # Two moulded ribs on the flanks, clear of the window and the handle.
    for sx in (-1, 1):
        hard += p.box((sx * (BODY_R - 0.002), 0.0, (WINDOW_LO + WINDOW_HI) / 2),
                      (0.014, 0.048, WINDOW_HI - WINDOW_LO + 0.030), GREY)

    # Window surround: FOUR BARS, not a plate. Built as one solid box it
    # enclosed the pane entirely — the glass was inside it, the captive behind
    # that, and the whole point of the item rendered as a grey slab. Nothing in
    # the numbers said so; only looking at it did.
    zc = (WINDOW_LO + WINDOW_HI) / 2
    zh = WINDOW_HI - WINDOW_LO
    for sx in (-1, 1):
        hard += p.box((sx * (WINDOW_HALF_X + 0.006), -(BODY_R - 0.004), zc),
                      (0.012, 0.024, zh + 0.024), GREY)
    for z in (WINDOW_LO - 0.006, WINDOW_HI + 0.006):
        hard += p.box((0, -(BODY_R - 0.004), z),
                      (WINDOW_HALF_X * 2 + 0.024, 0.024, 0.012), GREY)

    # Dark liner above and below the window, so the mouth reads as a THROAT and
    # the lit field inside reads against something dark. Deliberately not a full
    # sleeve: behind the window the interior has to be the interior.
    for lo, hi in ((BODY_LO + 0.008, WINDOW_LO - 0.014),
                   (WINDOW_HI + 0.014, BODY_HI - 0.004)):
        hard += p.tube((0, 0, (lo + hi) / 2), BODY_R - BODY_WALL + 0.001, 0.006,
                       hi - lo, axis='Z', seg=SEG, mat=SLATE)
    return _emit(p, hard, "Mesh_VacuumCanister_Body", coll)


def collar(coll, mats):
    """The blue neck ring — the widest thing on the canister, and its accent.

    The accent is structural: it is the collar, the foot band and the lid rim,
    all of which have a distinct shape, so the canister survives being read in
    shadow or by a colour-blind player.
    """
    p = TrackedPart(mats)
    hard = p.cyl((0, 0, (COLLAR_LO + COLLAR_HI) / 2), COLLAR_R,
                 COLLAR_HI - COLLAR_LO, axis='Z', seg=SEG, mat=BLUE)
    hard += p.tube((0, 0, COLLAR_HI - 0.008), COLLAR_R + 0.002, 0.014, 0.016,
                   axis='Z', seg=SEG, mat=GREY)
    # The catch the lid's hook drops over. Without it the hook closes onto air
    # and the latch reads as a moulding rather than as something that fastens.
    hard += p.cyl((0, -0.110, 0.402), 0.008, 0.044, axis='X', seg=10,
                  mat=CHROME)
    hard += p.box((0, -0.104, 0.402), (0.048, 0.014, 0.030), STEEL)
    # Hinge lugs for the lid, on the back.
    for sx in (-1, 1):
        hard += p.box((sx * 0.030, HINGE.y - 0.006, HINGE.z - 0.004),
                      (0.016, 0.030, 0.036), STEEL)
    return _emit(p, hard, "Mesh_VacuumCanister_Collar", coll)


def handle(coll, mats):
    """A D-handle on the back, which is the only place a hand fits.

    The window owns the front and the ribs own the flanks, so the grip goes
    opposite the thing the player is meant to look at — a carry handle, not a
    pistol grip, because this is a jug (`GDC-L1-UX-0004`).
    """
    p = TrackedPart(mats)
    inner, outer = BODY_R - 0.006, HANDLE_OUT
    hard = p.box((0, (inner + outer) / 2, HANDLE_LO + 0.014),
                 (0.052, outer - inner, 0.028), GREY)
    hard += p.box((0, (inner + outer) / 2, HANDLE_HI - 0.014),
                  (0.052, outer - inner, 0.028), GREY)
    hard += p.box((0, outer - 0.011, (HANDLE_LO + HANDLE_HI) / 2),
                  (0.046, 0.022, HANDLE_HI - HANDLE_LO), RUBBER)
    for i in range(5):
        z = HANDLE_LO + 0.034 + i * 0.018
        p.box((0, outer - 0.021, z), (0.048, 0.006, 0.007), DARK)
    return _emit(p, hard, "Mesh_VacuumCanister_Handle", coll)


def emitter(coll, mats):
    """The containment rod down the axis, visible through the window.

    Present whether the canister holds anything or not, which is the point: it
    gives the EMPTY variation something to be empty *around*. A window onto
    nothing at all reads as a moulding, not as a window.
    """
    p = TrackedPart(mats)
    hard = p.cyl((0, 0, 0.215), 0.014, 0.290, axis='Z', seg=12, mat=SLATE)
    for z in (0.100, 0.215, 0.330):
        hard += p.cyl((0, 0, z), 0.022, 0.014, axis='Z', seg=12, mat=CHROME)
    return _emit(p, hard, "Mesh_VacuumCanister_Emitter", coll)


# --- per-variation ----------------------------------------------------------

def glass(coll, mats, name, broken=False):
    """The window pane, set back 4 mm inside its surround.

    `broken` splits it into three shards with gaps, which is a SILHOUETTE change
    rather than a material one — a cracked canister has to read as cracked in a
    screenshot, not only in a material inspector.
    """
    p = TrackedPart(mats)
    # 16 mm of glass, seated so its front is 4 mm INSIDE the surround and its
    # back pokes 2 mm past the barrel's bore. Built flush with the surround it
    # was 2 mm from it — the exact separation `_zverify` calls coplanar, and the
    # exact thing that flickers along a whole window edge in play.
    y = -(BODY_R - 0.004)
    if not broken:
        p.box((0, y, (WINDOW_LO + WINDOW_HI) / 2),
              (WINDOW_HALF_X * 2 + 0.010, 0.016,
               WINDOW_HI - WINDOW_LO + 0.010), GLASS)
    else:
        for i, (z0, z1, tilt) in enumerate(
                ((WINDOW_LO, WINDOW_LO + 0.070, -7.0),
                 (WINDOW_LO + 0.086, WINDOW_LO + 0.150, 4.0),
                 (WINDOW_LO + 0.168, WINDOW_HI, -3.0))):
            p.box((0.004 * (i - 1), y, (z0 + z1) / 2),
                  (WINDOW_HALF_X * 2, 0.016, z1 - z0), GLASS,
                  rot=Matrix.Rotation(math.radians(tilt), 4, 'Y'))
    p.restamp()
    return p.finish(name, coll)


def lid(coll, mats, name, angle, locked):
    """The heavy latch, authored at `angle` about the hinge.

    Every box is rigid-transformed about the pivot: the centre is rotated AND the
    same rotation is handed to `Part.box`. Rotating only the centre moves a box
    without turning it; passing only `rot` turns each box about its own middle
    and the lid comes apart.
    """
    p = TrackedPart(mats)
    rot = Matrix.Rotation(math.radians(angle), 4, 'X')

    def at(pt):
        return HINGE + rot.to_3x3() @ (Vector(pt) - HINGE)

    # The cap OVERHANGS the collar: 0.108 against the collar's 0.100. That is
    # what lets the hook come down outside the collar instead of through it, and
    # it is also the oxygen bottle's language — a chunky overhanging cap.
    hard = p.cyl(at((0, 0, LID_DISC_Z)), 0.100, 0.018, axis='Z', seg=SEG,
                 mat=SHELL, rot=rot)
    hard += p.tube(at((0, 0, LID_DISC_Z)), 0.108, 0.009, 0.024, axis='Z',
                   seg=SEG, mat=BLUE)
    hard += p.box(at((0, 0.052, LID_DISC_Z - 0.004)), (0.070, 0.084, 0.016),
                  GREY, rot=rot)
    hard += p.cyl(HINGE, 0.011, 0.070, axis='X', seg=12, mat=DARK)
    # The hook comes down OUTSIDE the collar, at y -0.112 against a collar of
    # radius 0.100. Built at -0.086 it looked right in the numbers and passed
    # straight through the collar it is supposed to hook onto.
    # 30 mm of tongue, not 16. At 16 it overlapped the cap's rim by 4 mm and
    # rendered as a cube floating beside an open lid.
    hard += p.box(at((0, -0.111, LID_DISC_Z - 0.008)), (0.034, 0.030, 0.048),
                  CHROME, rot=rot)
    hard += p.cyl(at((0, 0, LID_DISC_Z + 0.016)), 0.017, 0.020, axis='Z',
                  seg=12, mat=DARK, rot=rot)
    if locked:
        # Only ever a CONFIRMATION. The lid being shut at all is the reading.
        p.box(at((0, -0.116, LID_DISC_Z - 0.034)), (0.024, 0.010, 0.016), RED,
              rot=rot)
    return _emit(p, hard, name, coll, origin=tuple(HINGE))


def field(coll, mats):
    """Five lit rings with a gap between each — a containment field, not a slug.

    A solid emissive column would hide the captive, which is the only thing in
    this model anybody actually looks at. Rings leave the animal visible between
    them and still read as "something is running in here".
    """
    p = TrackedPart(mats)
    for i in range(5):
        p.torus((0, 0, 0.108 + i * 0.052), 0.046, 0.0055, axis='Z',
                maj_seg=SEG, min_seg=6, mat=CYAN)

    # A lit gasket round the OUTSIDE of the pane as well. The rings and the
    # captive are only visible if the window's Unity material is actually
    # transparent, and that is a decision this file does not get to make — the
    # palette's `Mat_Glass_Canopy_Tinted` is authored as glazing, but a model
    # must not stake its whole reading on somebody else's shader. The gasket is
    # outside the glass and therefore reads whatever happens to it.
    zc, zh = (WINDOW_LO + WINDOW_HI) / 2, WINDOW_HI - WINDOW_LO
    for sx in (-1, 1):
        p.box((sx * 0.0555, -0.094, zc), (0.007, 0.010, zh + 0.008), CYAN)
    for z in (WINDOW_LO - 0.004, WINDOW_HI + 0.004):
        p.box((0, -0.094, z), (0.119, 0.010, 0.007), CYAN)

    p.restamp()
    return p.finish("Mesh_VacuumCanister_Field", coll)


def captive(coll, mats):
    """A small dark animal, curled up, floating at window height.

    Sixty millimetres of silhouette seen through tinted glass at arm's length —
    so it is built as a shape, not as a creature: a hunched body, a lowered
    head, two ears, four tucked legs and a tail. Anything finer is invisible
    through the pane and costs triangles on an item the player carries.
    """
    p = TrackedPart(mats)
    hard = p.cyl((0, 0.004, 0.204), 0.021, 0.046, axis='Y', seg=10, mat=SLATE,
                 radius_top=0.017)
    hard += p.cyl((0, -0.030, 0.196), 0.013, 0.022, axis='Y', seg=8, mat=SLATE,
                  radius_top=0.010)
    for sx in (-1, 1):
        p.box((sx * 0.009, -0.028, 0.210), (0.006, 0.010, 0.014), BLACK,
              rot=Matrix.Rotation(math.radians(sx * 16), 4, 'Y'))
        for dy in (-0.012, 0.016):
            p.box((sx * 0.014, dy, 0.183), (0.008, 0.012, 0.018), BLACK)
    p.box((0, 0.034, 0.214), (0.007, 0.026, 0.007), BLACK,
          rot=Matrix.Rotation(math.radians(28), 4, 'X'))
    return _emit(p, hard, "Mesh_VacuumCanister_Captive", coll)


# --- assembly ---------------------------------------------------------------

def place_iris(into, mats):
    """Append the shutter and stand it up so the mouth looks +Z.

    The component exits along −Y. A rotation about X carries −Y to
    (0, −cos a, −sin a), so only **a = −90 degrees** lands it on +Z; +90 buries
    the mouth in the canister and looks, from most angles, entirely plausible.
    The leaves keep their own rotations by composing rather than by assignment,
    so each leaf's LOCAL +Z is still its hinge axis afterwards.
    """
    names = ["Mesh_DeviceNozzle_IrisRing"] + [
        "Mesh_DeviceNozzle_IrisLeaf_%d" % (i + 1) for i in range(6)]
    objs = append_objects(NOZZLE, names, into)
    stand = Matrix.Rotation(math.radians(-90), 4, 'X')
    lift = Matrix.Translation((0.0, 0.0, IRIS_SEAT))
    for obj in objs:
        obj.matrix_world = lift @ stand @ obj.matrix_world
    bpy.context.view_layer.update()
    return objs


def place_gauge(into):
    """Append the kit's shared SupplyGauge face onto the collar.

    `Mesh_SprayerGauge_Plate` and not a second bar of this file's own: it is the
    same nine-device kit, it already carries exactly one emissive strip in
    `Mat_Emissive_Green_CRT` symmetric about its own middle, and that is the
    whole contract `OxygenGearBuilder.MeasureGauge` reads.
    """
    obj = append_objects(SPRAYER, ["Mesh_SprayerGauge_Plate"], into)[0]
    obj.location = GAUGE_AT
    bpy.context.view_layer.update()
    return obj


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    empty = collection("Coll_VacuumCanister_Empty")
    full = collection("Coll_VacuumCanister_Full")
    cracked = collection("Coll_VacuumCanister_Cracked")
    variations = (empty, full, cracked)

    # Everything the three share is built ONCE and linked into all three, so a
    # later edit to the vessel cannot land on one identity and miss the others.
    shared = [foot(empty, mats), body(empty, mats), collar(empty, mats),
              handle(empty, mats), emitter(empty, mats),
              _marker("Marker_Muzzle", (0.0, 0.0, MOUTH_Z + 0.008), empty,
                      mats),
              _marker("Marker_Grip", (0.0, HANDLE_OUT - 0.012, 0.240), empty,
                      mats),
              _marker("Marker_Gauge", (0.0, GAUGE_AT[1] - 0.009, GAUGE_AT[2]),
                      empty, mats)]
    shared += place_iris(empty, mats)
    shared.append(place_gauge(empty))
    for obj in shared:
        full.objects.link(obj)
        cracked.objects.link(obj)

    glass(empty, mats, "Mesh_VacuumCanister_GlassClear")
    lid(empty, mats, "Mesh_VacuumCanister_LidOpen", LID_OPEN, locked=False)

    glass(full, mats, "Mesh_VacuumCanister_GlassSealed")
    lid(full, mats, "Mesh_VacuumCanister_LidShut", 0.0, locked=True)
    field(full, mats)
    captive(full, mats)

    glass(cracked, mats, "Mesh_VacuumCanister_GlassBroken", broken=True)
    lid(cracked, mats, "Mesh_VacuumCanister_LidSprung", -32.0, locked=False)

    save(out)
    report()


if __name__ == "__main__":
    main()
