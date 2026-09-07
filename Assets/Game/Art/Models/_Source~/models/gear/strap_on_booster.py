"""Strap-on booster — a one-shot rocket you clamp to any physics object.

Design: `docs/AI/systems/Artifacts/StrapOnBooster.md`. A moulded cylinder in the
kit's colour with a bell nozzle at one end, a hinged clamp jaw at the other, and a
striped hazard band round the middle so it reads as "this end goes on the thing".
It burns for two seconds along its own facing and falls off, spent.

It is not held like a gun, and the model has to say so before anybody reads a
tooltip (`GDC-L1-UX-0004`). Three things do that, and they are the whole design:

  * **A nose clamp on the axis.** The jaws bite at the front, so the thrust line
    and the grip line are the same line — which is the item's one rule, "how you
    stick it on is the aim".
  * **A saddle and two ratchet straps underneath.** The jaws alone would let it
    pivot, and at a distance they are small. The straps are what reads as
    strapped-on across a room, with their free ends hanging below the mounting
    plane where a target would be.
  * **A hazard band, in stripes rather than in a colour.** Eight alternating
    wedges: a band that keeps its meaning in greyscale (`GDC-L1-UX-0006`).

Size
----
About 0.39 m. Sized from **what it clamps to**, not from the hand: the throat is
`device_clamp`'s 62 mm open by 48 mm deep, which takes the lip of a supply crate,
a hull-plate edge or a saddle rail, and the straps wrap a 0.104 m body with their
tails free. That is the constraint; the 0.4 m in the design doc is the
consequence.

Orientation and origin
----------------------
The booster runs along **Y** with its bell at **+Y**, matching
`dragon_rocket.blend` — thrust therefore drives the target toward −Y, the
library's forward, which is the axis every other item in this project points
along.

The origin is on the **mounting plane**, at the centre of the saddle's contact
face — not on the body axis. An attach pose is stored in the target's local space
and is exactly "put this point on the surface, with its −Z into it", so the
origin is the pose.

    blender --background --python strap_on_booster.py -- --out strap_on_booster.blend

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
from device_clamp import JAW_HINGE, JAW_OPEN  # noqa: E402
from device_kit import (BEVEL_SEG, BEVEL_W, MATS, SEG, BLACK, CHROME, DARK,  # noqa: E402
                        GREY, HAZARD, RED, RUST, SHELL, SLATE, STEEL)
from device_nozzle import BELL_LEN  # noqa: E402

PROPS = os.path.join(LIB, "components", "props")
CLAMP = os.path.join(PROPS, "device_clamp.blend")
GAUGE = os.path.join(PROPS, "device_gauge.blend")
NOZZLE = os.path.join(PROPS, "device_nozzle.blend")

# --- the casing --------------------------------------------------------------

BODY_R = 0.052
BODY_Z = 0.088                          # the bore axis, above the mounting plane
BODY_BACK, BODY_FRONT = 0.112, -0.176
NOSE_TIP = -0.200                       # the flat face the clamp bolts to

BELL_AT = (0.0, BODY_BACK, BODY_Z)
CLAMP_AT = (0.0, NOSE_TIP, BODY_Z)
PAD_AT = (0.0, -0.045, 0.0)
STRAP_Y = (-0.150, 0.040)               # both bands cross the saddle rather than
                                        # sitting clear of it, so their tails run
                                        # down PAST its sides — which is what a
                                        # strap threaded through a saddle looks
                                        # like. Kept off the bell's bolt flange
                                        # (from +0.096, measured off the
                                        # component): a band sunk in a flange
                                        # stops reading as a band.
LAMP_AT = (0.0, 0.006, BODY_Z + BODY_R - 0.006)
# In the gap between the hazard band (ends -0.010) and the aft strap
# (starts +0.022). Set further back it sat under the strap and the bell,
# which is a poor place for the one light that says whether this thing is
# still live.

BAND_LO, BAND_HI = -0.070, -0.010
BAND_R = BODY_R + 0.0025
BAND_WEDGES = 8


def _emit(p, hard, name, coll, origin=(0, 0, 0)):
    p.restamp()
    p.bevel(hard, width=BEVEL_W, segments=BEVEL_SEG)
    return p.finish(name, coll, origin=origin)


def _marker(name, at, coll, mats):
    """A 4 mm cube standing in for an empty — empties do not survive
    `object_types={"MESH"}`, and this library ships its sockets as meshes."""
    p = TrackedPart(mats)
    p.box((0, 0, 0), (0.004, 0.004, 0.004), STEEL)
    obj = p.finish(name, coll)
    obj.location = at
    return obj


# --- geometry only this item has --------------------------------------------

def casing(coll, mats):
    """Moulded cylinder, shouldered nose, and the flat face the jaws bolt to."""
    p = TrackedPart(mats)
    hard = p.cyl((0, (BODY_BACK + BODY_FRONT) / 2, BODY_Z), BODY_R,
                 BODY_BACK - BODY_FRONT, axis='Y', seg=SEG, mat=SHELL)
    # `Part.cyl` puts `radius` at −axis and `radius_top` at +axis, and takes a
    # POSITIVE depth. Written the other way round — 0.052 at the nose and a
    # depth of `NOSE_TIP - BODY_FRONT`, which is negative — it built an inverted
    # cap that swallowed the clamp jaws whole. The numbers looked symmetrical
    # and the render did not.
    hard += p.cyl((0, (BODY_FRONT + NOSE_TIP) / 2, BODY_Z), 0.042,
                  BODY_FRONT - NOSE_TIP, axis='Y', seg=SEG, mat=GREY,
                  radius_top=BODY_R)
    # Two moulded ribs along the top, off the strap lines and the band.
    for sx in (-1, 1):
        hard += p.box((sx * 0.030, -0.010, BODY_Z + BODY_R - 0.004),
                      (0.014, 0.180, 0.014), GREY,
                      rot=Matrix.Rotation(math.radians(sx * 30), 4, 'Y'))
    hard += p.tube((0, BODY_BACK - 0.010, BODY_Z), BODY_R + 0.003, 0.010, 0.018,
                   axis='Y', seg=SEG, mat=DARK)
    return _emit(p, hard, "Mesh_StrapOnBooster_Casing", coll)


def cradle(coll, mats):
    """The saddle mount: a moulded block tying the pad's bosses to the casing.

    Without it the pad hangs under the body with 10 mm of air between them and
    the whole thing reads as two props photographed together.
    """
    p = TrackedPart(mats)
    # Deep and narrow on purpose. At 0.086 wide with its underside at z 0.028
    # the block's own bottom face landed 0.5 mm from the hazard band's lowest
    # wedge — parallel, overlapping, and exactly the pair `_zverify` calls
    # coplanar. Dropping the underside to 0.020 buries the wedge in the block
    # instead, which is the fix this library prefers: embed, never abut.
    hard = p.box((0, PAD_AT[1], 0.046), (0.076, 0.160, 0.052), SHELL)
    for sy in (-1, 1):
        hard += p.box((0, PAD_AT[1] + sy * 0.074, 0.044), (0.062, 0.016, 0.030),
                      GREY, rot=Matrix.Rotation(math.radians(sy * 22), 4, 'X'))
    return _emit(p, hard, "Mesh_StrapOnBooster_Cradle", coll)


def band(coll, mats):
    """Eight alternating wedges round the casing.

    STRIPES and not a painted ring: alternating widths are a pattern, and a
    pattern survives being seen in shadow, in silhouette or by a player who
    cannot separate the two hues. The wedges are 50 mm of tangent over a 43 mm
    arc, so neighbours OVERLAP rather than meet — two boxes abutting on a shared
    plane is the flicker this library's scripts warn about.
    """
    p = TrackedPart(mats)
    hard = []
    for i in range(BAND_WEDGES):
        a = 2 * math.pi * i / BAND_WEDGES
        hard += p.box((BAND_R * math.cos(a), (BAND_LO + BAND_HI) / 2,
                       BODY_Z + BAND_R * math.sin(a)),
                      (0.012, BAND_HI - BAND_LO, 0.050),
                      RED if i % 2 else HAZARD,
                      rot=Matrix.Rotation(-a, 4, 'Y'))
    return _emit(p, hard, "Mesh_StrapOnBooster_HazardBand", coll)


def lamp_spent(coll, mats):
    """The arming light, out: a dark lens behind a cracked cover.

    A material swap would have done the colour, and only the colour. The lens
    goes black AND the cover splits, so a spent booster in the sand still reads
    as spent in a screenshot.
    """
    p = TrackedPart(mats)
    hard = p.tube((0, 0, 0.006), 0.012, 0.004, 0.012, axis='Z', seg=12, mat=DARK)
    p.cyl((0, 0, -0.002), 0.013, 0.008, axis='Z', seg=12, mat=GREY)
    p.cyl((0, 0, 0.009), 0.009, 0.008, axis='Z', seg=12, mat=BLACK)
    for i, a in enumerate((18.0, 74.0, 140.0)):
        r = math.radians(a)
        p.box((0.005 * math.cos(r), 0.005 * math.sin(r), 0.0135),
              (0.019, 0.0025, 0.003), SLATE, rot=Matrix.Rotation(r, 4, 'Z'))
    obj = _emit(p, hard, "Mesh_StrapOnBooster_LampSpent", coll)
    obj.location = LAMP_AT
    return obj


def scorch(coll, mats):
    """Soot flared back over the casing from the bell throat. Spent only."""
    p = TrackedPart(mats)
    hard = p.cyl((0, BODY_BACK - 0.034, BODY_Z), BODY_R + 0.004, 0.052,
                 axis='Y', seg=SEG, mat=RUST, radius_top=BODY_R + 0.001)
    for i in range(6):
        a = 2 * math.pi * i / 6 + 0.4
        hard += p.box(((BODY_R + 0.004) * math.cos(a), BODY_BACK - 0.078,
                       BODY_Z + (BODY_R + 0.004) * math.sin(a)),
                      (0.006, 0.044, 0.026), RUST,
                      rot=Matrix.Rotation(-a, 4, 'Y'))
    return _emit(p, hard, "Mesh_StrapOnBooster_Scorch", coll)


# --- assembly ---------------------------------------------------------------

def place(blend, names, coll, matrix, rename=None):
    """Append, compose the placement onto what each object already carries.

    `rename` is for the parts this model needs TWO of. Blender auto-suffixes a
    second append to `.001`, and `_buildlib.save` refuses to write a file
    containing one — deliberately, because an auto-suffixed name is never a name
    anybody chose.

    The guard below is the real lesson. `_buildlib.append_objects` resolves what
    it appended by looking the name up in `bpy.data.objects`, so if that name is
    already taken it hands back the OLD object and leaves the freshly appended
    `.001` behind, unrenamed and unplaced. It is caught here rather than at save
    time, where the message names a suffix instead of the mistake.
    """
    taken = [n for n in names if n in bpy.data.objects]
    if taken:
        raise SystemExit(
            "Already in the file: %s — append the copy that gets a `rename` "
            "FIRST, so the component's own name is free again for the next one."
            % ", ".join(taken))
    objs = append_objects(blend, names, coll)
    for obj in objs:
        obj.matrix_world = matrix @ obj.matrix_world
        if rename:
            # The MESH datablock as well, not only the object. `save()` renames
            # data to match its object, and it does that in object order — so a
            # renamed object whose data still holds the component's name leaves
            # the next copy's data auto-suffixed to `.001`, which survives every
            # check except a deliberate audit.
            obj.name = rename
            obj.data.name = rename
    bpy.context.view_layer.update()
    return objs


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    armed = collection("Coll_StrapOnBooster_Armed")
    clamped = collection("Coll_StrapOnBooster_Clamped")
    spent = collection("Coll_StrapOnBooster_Spent")

    shared = [casing(armed, mats), cradle(armed, mats), band(armed, mats)]

    # The bell exits +Y and the component exits −Y, so it is turned a half turn
    # about Z. A half turn about Z carries −Y to +Y and −X to +X: the
    # determinant stays +1, so nothing is mirrored and no normals need fixing.
    # Turning it about X instead would also point the bell the right way and
    # would roll the bolt flange a quarter turn out of line with the ribs.
    shared += place(NOZZLE, ["Mesh_DeviceNozzle_Bell"], armed,
                    Matrix.Translation(BELL_AT)
                    @ Matrix.Rotation(math.radians(180), 4, 'Z'))

    # The clamp presses along −Z; the nose needs it pressing along −Y. A
    # rotation about X carries (0, 0, −1) to (0, sin a, −cos a), so only
    # a = −90 degrees lands it on −Y. It also carries the clamp's +Y to −Z,
    # which is what turns the throat from opening sideways to opening top and
    # bottom — the bite a booster wants on the edge of a crate.
    # The half turn about Z that follows swaps which side of the throat the
    # HINGED jaw is on. Without it the lever opens downward, into the mounting
    # plane, where it fouls the saddle and no hand could reach it.
    nose = (Matrix.Translation(CLAMP_AT)
            @ Matrix.Rotation(math.radians(-90), 4, 'X')
            @ Matrix.Rotation(math.radians(180), 4, 'Z'))
    shared += place(CLAMP, ["Mesh_DeviceClamp_JawFixed"], armed, nose)
    shared += place(CLAMP, ["Mesh_DeviceClamp_Pad"], armed,
                    Matrix.Translation(PAD_AT))

    # The strap wraps the X axis; the casing runs along Y. A quarter turn about
    # Z carries +X to +Y and leaves +Z alone, so the band comes round the casing
    # with its ratchet buckle still on top where a hand can reach it.
    for i, y in enumerate(STRAP_Y):
        shared += place(CLAMP, ["Mesh_DeviceClamp_Strap"], armed,
                        Matrix.Translation((0.0, y, BODY_Z))
                        @ Matrix.Rotation(math.radians(90), 4, 'Z'),
                        rename="Mesh_StrapOnBooster_Strap%s"
                               % ("Fore" if i == 0 else "Aft"))

    shared += [
        _marker("Marker_Muzzle", (0.0, BODY_BACK + BELL_LEN, BODY_Z), armed,
                mats),
        _marker("Marker_Mount", PAD_AT, armed, mats),
        _marker("Marker_Clamp", (0.0, NOSE_TIP + 0.024, BODY_Z), armed, mats),
        _marker("Marker_Grip", (0.0, -0.020, BODY_Z), armed, mats),
    ]

    for obj in shared:
        clamped.objects.link(obj)
        spent.objects.link(obj)

    # Clamped needs the same jaw SHUT, which is a second copy: one object cannot
    # carry two rotations. It is appended FIRST because it is the one that gets
    # renamed, which hands the component's own name back for the open copy.
    # Post-multiplying turns it in its OWN frame, whose origin is the hinge pin —
    # pre-multiplying would swing it about the booster's origin and drive it
    # through the casing.
    shut = place(CLAMP, ["Mesh_DeviceClamp_JawMoving"], clamped, nose,
                 rename="Mesh_StrapOnBooster_JawShut")[0]
    shut.matrix_world = shut.matrix_world @ Matrix.Rotation(
        math.radians(-JAW_OPEN), 4, 'X')

    # Armed and Spent both stand with the jaws open, so they share one object.
    open_jaw = place(CLAMP, ["Mesh_DeviceClamp_JawMoving"], armed, nose)[0]
    spent.objects.link(open_jaw)

    lit = place(GAUGE, ["Mesh_DeviceGauge_Lamp"], armed,
                Matrix.Translation(LAMP_AT)
                @ Matrix.Rotation(math.radians(-90), 4, 'X'))[0]
    clamped.objects.link(lit)
    bpy.context.view_layer.update()

    lamp_spent(spent, mats)
    scorch(spent, mats)

    save(out)
    report()


if __name__ == "__main__":
    main()
