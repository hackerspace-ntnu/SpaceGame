"""Sucker Puncher — the steam-driven power fist.

The hand goes *through* an open frame and closes on a bar. A boiler on the forearm feeds a
cylinder on the frame's spine; firing it throws a carriage down twin rails, and the carriage
drags a short arm — and the segmented knuckle block on the end of it — out past the fist.

Mostly assembly. Every surface except the arm that marries the mechanism to the head comes
from a component.

## Origin: the grip point

`(0, 0, 0)` is the centre of the bar the fingers close on — the point `HandGripFrame` seats
the item at. Every landmark is therefore a known offset from the origin rather than a
number to rediscover:

    knuckle row     y = -0.097      wrist bone      y = +0.079
    back of hand    z = +0.066      spine deck      z = +0.082

## Scale: 1:1 with the game

Authored in world metres and shipped with `ItemGrip.holdSize` set to the model's own
longest axis, so world scale is pinned at 1.0 and the metres here are metres in the game.
`EquipItemSocket.ApplyScale` divides out the rig's scale before applying the authored one,
so this is not a correction for anything — it is a lock. A cavity sized against measured
hand dimensions is invalidated by any scaling at all, so the size a worn item ships at
should be stated rather than inherited.

## What ships, and why each object is separate

| Object | Moves? | Separate because |
|---|---|---|
| `Mesh_ArmCuff_Plated`             | fixed  | reused component |
| `Mesh_GauntletShell_Frame`        | fixed  | the frame the hand goes through |
| `Mesh_GauntletShell_WristCollar`  | fixed  | reused component |
| `Mesh_GauntletShell_Boiler`       | fixed  | reused component |
| `Mesh_GauntletShell_HazardPlate`  | fixed  | the guard over the mechanism |
| `Mesh_RamSlide_Rails`             | fixed  | the track |
| `Mesh_RamSlide_Cylinder`          | fixed  | the shell the steam pushes against |
| `Mesh_RamSlide_Rod`               | **ram** | slides out of the shell as the ram runs |
| `Mesh_SuckerPuncher_Bracket`      | fixed  | plumbing and seats — unique to this model |
| `Mesh_RamSlide_Carriage`          | **ram** | rides the rails |
| `Mesh_SuckerPuncher_RamArm`       | **ram** | unique to this model |
| `Mesh_KnuckleBlock_Segmented`     | **ram** | the head |

## The ram pivot, and the one thing Unity has to know

The **four** moving objects all have their origin at the same point, `RAM_PIVOT`, on the
rail axis. So the Unity prefab parents them under one transform placed there and animates a
single local Z — no per-part offsets, no pivot to rediscover in the editor.

The piston rod is one of them. At 0.17 m of throw a rod welded to the shell would visibly
tear away from the carriage it is driving; ridden along with the ram it slides out of the
shell exactly as a real one does.

The ram arm lives entirely **forward of the knuckle bridge** (y < -0.122). That is what
keeps it out of the hand: the earlier version ran side struts down past the fingers, which
put moving steel either side of the knuckles.

## Axes

The library builds −Y forward / +Z up, and `_exportlib`'s FBX flags map Blender `(x, y, z)`
onto Unity `(x, z, −y)`. Blender **−Y lands on Unity +Z**, so the fist punches along the
item's local forward in both, and the ram slides in +Z once it is in Unity.

Blender **+Z is the back of the hand**, which in hand space is the item's +X — hence the
prefab's `rotationOffset` of (0, 0, −90). Without it the gauntlet is worn edge-on, with
the guard plate out past the thumb.

## No armature

Everything that moves does so as one rigid group along one straight axis, and that group
already shares an origin on it — the same capability as a single-bone rig, minus a
hierarchy Unity would have to unpick on import. Same call as `item_scanner.blend`.

Generation script — historical record. The .blend is the source of truth; never re-run this
over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, os.path.join(_LIB, "components", "mechanical"))

import bpy  # noqa: E402
from _buildlib import *  # noqa: E402,F403
from _tracked import TrackedPart  # noqa: E402
from panel_control import tube_path  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

CUFF = os.path.join(_LIB, "components", "props", "arm_cuff.blend")
SHELL = os.path.join(_LIB, "components", "props", "gauntlet_shell.blend")
SLIDE = os.path.join(_LIB, "components", "mechanical", "ram_slide.blend")
HEAD = os.path.join(_LIB, "components", "mechanical", "knuckle_block.blend")

STEEL, DARK, BRASS, CHROME, RUBBER, RED = range(6)
MATS = ["Mat_Metal_Steel_Worn",       # index 0: bevel's colour, and the frame
        "Mat_Metal_Steel_Dark",
        "Mat_Metal_Brass_Tarnished",
        "Mat_Metal_Chrome_Scuffed",
        "Mat_Plastic_Rubber_Black",
        "Mat_Paint_Warn_Red"]

BEVEL_W = 0.0014

WRIST_Y = 0.079
KNUCKLE_Y = -0.097
BRIDGE_FRONT = KNUCKLE_Y - 0.025     # the frame's front face; no hand past here
DECK_Z = 0.082                       # top of the spine

# The mechanism sits 10 mm proud of the spine rather than flat on it. `ram_slide`'s rail
# assembly hangs its bolt bosses and the bottom of its anchor plate ~20 mm BELOW its own
# mounting plane — which is correct for a track bolted through a chassis, and puts 128
# vertices into the back of the hand if that plane is the spine itself. The standoff is
# filled by the spacer in `bracket()`, so the bosses land in something.
MECH_Z = DECK_Z + 0.010
RAIL_AXIS_Z = MECH_Z + 0.018

RAM_PIVOT = Vector((0.0, -0.088, MECH_Z))

# 0.17 m of visible throw. The rails are 0.280 long and the carriage 0.062 deep, so the
# stop yoke sits at 0.178 of travel — this leaves 8 mm and no more.
STROKE = 0.170
HEAD_MOUNT = Vector((0.0, -0.152, 0.022))

# Where the rod is pinned to the carriage: the carriage's clevis, in world terms. The rod
# rides the ram group from here and slides back into the shell at rest.
ROD_PIN = Vector((0.0, RAM_PIVOT.y + 0.034, MECH_Z + 0.012))

RAILS_AT = Vector((0.0, -0.030, MECH_Z))
# Far enough back that the 0.230 m shell swallows the rod's 0.215 m at rest, and on the
# rod's own axis so the two read as one machine.
CYLINDER_AT = Vector((0.0, 0.190, ROD_PIN.z))
CUFF_AT = Vector((0.0, WRIST_Y + 0.030, 0.014))

# The cuff is a 0.215 m component built for a human forearm. This rig's forearm is 0.404 m
# and correspondingly thick, so the component is scaled rather than replaced — a bracer at
# its authored size would be swallowed by the arm it is supposed to clamp.
CUFF_SCALE = 1.18
# Pushed back up the forearm to clear the longer cylinder, which now reaches y +0.190.
BOILER_AT = Vector((0.0, WRIST_Y + 0.185, 0.062))


def audit_cavity():
    """Prove the assembled model leaves the hand somewhere to be.

    `gauntlet_shell.py` checks its own parts, but the shell is not the whole gauntlet: the
    ram arm, the plumbing bracket and every placement in this file can put geometry back
    into the hand, and the original failure was exactly that kind of whole-model mistake.
    So the check is repeated here over everything that ships, at its final position, with
    the ram swept through its full stroke as well as at rest.

    Reported rather than raised for the ram sweep — a couple of millimetres of finger
    clearance at full extension is a tuning question, not a build failure — but a resting
    intrusion is fatal, because that is the pose the item spends its life in.
    """
    # `place()` writes obj.location, and Blender does not recompute matrix_world until the
    # depsgraph ticks. Without this the audit measures every placed object at its
    # PRE-placement position and reports intrusions that are not there — the forearm cuff
    # read as 354 vertices inside the hand while actually sitting 17 mm clear of it.
    bpy.context.view_layer.update()

    cavity = {"x": (-0.072, 0.072), "y": (-0.108, 0.092), "z": (-0.038, 0.066)}
    handle = {"y": (-0.032, 0.026), "z": (-0.032, 0.024)}
    ram = {"Mesh_RamSlide_Carriage", "Mesh_SuckerPuncher_RamArm",
           "Mesh_KnuckleBlock_Segmented", "Mesh_RamSlide_Rod"}

    worst = 0
    for obj in bpy.data.objects:
        if obj.type != 'MESH' or obj.name.startswith("Marker_"):
            continue
        for shift in ((0.0, 0.0, 0.0), (0.0, -STROKE, 0.0)):
            if shift[1] and obj.name not in ram:
                continue
            hits = 0
            for v in obj.data.vertices:
                p = (obj.matrix_world @ v.co) + Vector(shift)
                if not all(cavity[a][0] < c < cavity[a][1]
                           for a, c in zip("xyz", (p.x, p.y, p.z))):
                    continue
                if all(handle[a][0] < c < handle[a][1] for a, c in zip("yz", (p.y, p.z))):
                    continue
                hits += 1
            if not hits:
                continue
            where = "at full stroke" if shift[1] else "at rest"
            print("  !! %s puts %d vertices in the hand %s" % (obj.name, hits, where))
            if not shift[1]:
                raise SystemExit("%s is inside the hand. The hand has to fit." % obj.name)
            worst = max(worst, hits)

    print("  hand cavity clear%s" % (" at rest (%d vertices enter it mid-stroke)" % worst
                                     if worst else ""))


def append_objects(blend, names, into):
    """Append named objects from a component file.

    Appended, not linked: an export has to carry real mesh data, and a linked object
    arrives as a proxy the FBX writer skips silently.
    """
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
    return out


def place(obj, matrix, origin=None):
    """Apply `matrix` into the mesh data, leaving rotation 0 and scale 1.

    The library's convention is that transforms are applied, and it is not cosmetic: a
    rotated object exports with that rotation baked into the FBX node, and Unity then hands
    the game a Transform whose local axes are not the ones the code reasons about.

    `origin` re-seats the pivot at a chosen world point rather than carrying the
    component's own. That is what lets the three ram objects share `RAM_PIVOT`, so the
    prefab can parent them under one transform at local zero.
    """
    world = matrix @ obj.matrix_world
    if origin is None:
        origin = world.to_translation()
    origin = Vector(origin)
    obj.data.transform(Matrix.Translation(-origin) @ world)
    obj.location = origin
    obj.rotation_euler = (0.0, 0.0, 0.0)
    obj.scale = (1.0, 1.0, 1.0)
    return obj


def ram_arm(coll, mats):
    """The short arm that hangs the head off the carriage.

    Written as two endpoints in world coordinates and re-origined onto `RAM_PIVOT`, because
    its whole job is to bridge two points that are already fixed — the carriage's mounting
    face and the head's backing plate.

    It lives **entirely forward of the knuckle bridge**. The first version ran two long
    struts down the outside of the hand to reach a head mounted level with the fingers;
    that is moving steel either side of the knuckles, and it is what made the gauntlet a
    cage. Pushing the whole ram forward instead means the arm only ever occupies air.
    """
    p = TrackedPart(mats)
    face_y = RAM_PIVOT.y - 0.042        # the carriage's forward mounting face
    hard = []

    # Yoke across the carriage face.
    hard += p.box((0.0, face_y - 0.008, RAIL_AXIS_Z), (0.148, 0.016, 0.032), STEEL)
    hard += p.box((0.0, face_y + 0.004, RAIL_AXIS_Z), (0.108, 0.012, 0.046), DARK)

    # Drop plate: down and forward, from the rail axis to the head's backing plate.
    for sx in (-1, 1):
        tube_path(p, [(sx * 0.062, face_y - 0.010, RAIL_AXIS_Z - 0.006),
                      (sx * 0.062, HEAD_MOUNT.y + 0.010, HEAD_MOUNT.z + 0.030)],
                  0.0080, STEEL, seg=8)
        p.cyl((sx * 0.062, face_y - 0.010, RAIL_AXIS_Z - 0.006), 0.0112, 0.014, 'Y', 8,
              BRASS)

    hard += p.slab((-0.070, HEAD_MOUNT.y - 0.012, HEAD_MOUNT.z - 0.052),
                   (0.070, HEAD_MOUNT.y, HEAD_MOUNT.z + 0.052), STEEL)
    for sx in (-1, 1):
        hard += p.box((sx * 0.062, HEAD_MOUNT.y + 0.012, HEAD_MOUNT.z + 0.034),
                      (0.014, 0.024, 0.028), DARK)

    p.rivets((-0.052, HEAD_MOUNT.y - 0.014, HEAD_MOUNT.z + 0.040),
             (0.052, HEAD_MOUNT.y - 0.014, HEAD_MOUNT.z + 0.040), 5, radius=0.0032,
             height=0.004, axis='Y', mat=CHROME)

    # Danger band on the yoke — the one face of the ram a bystander sees coming.
    hard += p.slab((-0.064, face_y - 0.017, RAIL_AXIS_Z + 0.010),
                   (0.064, face_y - 0.015, RAIL_AXIS_Z + 0.014), RED)

    p.restamp("ram_arm")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_SuckerPuncher_RamArm", coll, origin=RAM_PIVOT)


def bracket(coll, mats):
    """Plumbing and seats — the only fixed geometry unique to this model.

    Without it the boiler feeds nothing and the cylinder rests on air, and the three
    subassemblies read as three objects photographed together rather than as one machine.
    """
    p = TrackedPart(mats)
    hard = []

    # Seat under the cylinder, bridging its overhang back onto the wrist yoke.
    hard += p.box((0.0, 0.068, RAIL_AXIS_Z - 0.030), (0.052, 0.036, 0.020), DARK)
    hard += p.box((0.0, 0.030, RAIL_AXIS_Z - 0.032), (0.040, 0.040, 0.012), STEEL)

    # Spacer under the rail anchor, tying the track down to the spine.
    hard += p.box((0.0, -0.032, (DECK_Z + MECH_Z) / 2), (0.130, 0.030, 0.018), STEEL)

    # Delivery line, boiler to cylinder, carried down the right-hand side.
    tube_path(p, [(0.014, BOILER_AT.y - 0.056, 0.104),
                  (0.034, 0.148, 0.100),
                  (0.034, 0.112, 0.096),
                  (0.012, 0.096, 0.098)], 0.0050, RUBBER, seg=6)

    # Exhaust line running forward along the spine, and the clamps holding it.
    tube_path(p, [(0.030, 0.040, 0.092),
                  (0.044, 0.000, 0.086),
                  (0.044, -0.076, 0.085)], 0.0048, RUBBER, seg=6)
    for y in (-0.014, -0.062):
        hard += p.box((0.044, y, DECK_Z + 0.002), (0.016, 0.010, 0.014), DARK)

    # Union blocks at both ends of the run, so the hoses terminate in something.
    p.cyl((0.012, 0.096, 0.098), 0.0082, 0.012, 'Y', 8, BRASS)
    p.cyl((0.044, -0.076, 0.085), 0.0080, 0.012, 'Y', 8, BRASS)

    p.restamp("bracket")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_SuckerPuncher_Bracket", coll)


def markers(coll, mats):
    """4 mm cubes the Unity prefab reads pivots off, as `portal_gun.blend` does.

    Cheaper and far more reliable than measuring the FBX in the editor: the numbers that
    matter here are decided in this file, so this is where they should be recorded.
    """
    spots = {
        # The origin IS the grip point, so this marker is at zero. Kept anyway, because the
        # prefab reads it by name and a future re-origining should not silently move the
        # hand somewhere else.
        "Marker_Grip": (0.0, 0.0, 0.0),
        # The strike face at rest. Impact effects and the melee trace start here.
        "Marker_Fist": (0.0, HEAD_MOUNT.y - 0.075, HEAD_MOUNT.z),
        # Where the cylinder dumps its steam on firing: at the gland, the front of the
        # shell, which is the one place a real ram vents from.
        "Marker_Vent": (0.0, -0.046, ROD_PIN.z + 0.026),
        # The pressure gauge face, for a charge/cooldown readout.
        "Marker_Gauge": (0.0, BOILER_AT.y - 0.074, BOILER_AT.z + 0.036),
    }
    for name, at in spots.items():
        p = TrackedPart(mats)
        p.box(at, (0.004, 0.004, 0.004), STEEL)
        p.finish(name, coll, origin=at)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_SuckerPuncher")

    # Forearm mount. The cuff is built running +Z toward the elbow, so it is laid down onto
    # the model's -Y-forward axis: -90 about X sends its +Z to +Y.
    append_objects(CUFF, ["Mesh_ArmCuff_Plated"], coll)
    place(bpy.data.objects["Mesh_ArmCuff_Plated"],
          Matrix.Translation(CUFF_AT)
          @ Matrix.Scale(CUFF_SCALE, 4)
          @ Matrix.Rotation(math.radians(-90), 4, 'X'))

    # The frame family already shares the grip point as its origin, so three of the four go
    # on untouched.
    append_objects(SHELL, ["Mesh_GauntletShell_Frame",
                           "Mesh_GauntletShell_WristCollar",
                           "Mesh_GauntletShell_HazardPlate",
                           "Mesh_GauntletShell_Boiler"], coll)
    place(bpy.data.objects["Mesh_GauntletShell_Boiler"], Matrix.Translation(BOILER_AT))

    # Mechanism.
    # `Coll_RamSlide_SpringReturn` is deliberately NOT used. At a 6 cm twitch a decorative
    # coil beside the carriage was harmless; at 17 cm there is nowhere on the deck it can
    # sit that the carriage, the guard plate or the cylinder does not already occupy — and
    # the rod sliding out of its shell now shows the mechanism working on its own.
    append_objects(SLIDE, ["Mesh_RamSlide_Rails", "Mesh_RamSlide_Cylinder",
                           "Mesh_RamSlide_Rod", "Mesh_RamSlide_Carriage"], coll)
    place(bpy.data.objects["Mesh_RamSlide_Rails"], Matrix.Translation(RAILS_AT))
    place(bpy.data.objects["Mesh_RamSlide_Cylinder"], Matrix.Translation(CYLINDER_AT))
    place(bpy.data.objects["Mesh_RamSlide_Rod"],
          Matrix.Translation(ROD_PIN), origin=RAM_PIVOT)

    # The ram: one shared origin across all three, so Unity animates one number.
    place(bpy.data.objects["Mesh_RamSlide_Carriage"],
          Matrix.Translation(RAM_PIVOT), origin=RAM_PIVOT)
    append_objects(HEAD, ["Mesh_KnuckleBlock_Segmented"], coll)
    place(bpy.data.objects["Mesh_KnuckleBlock_Segmented"],
          Matrix.Translation(HEAD_MOUNT), origin=RAM_PIVOT)
    ram_arm(coll, mats)

    bracket(coll, mats)
    audit_cavity()
    markers(coll, mats)

    save(out)
    total = report()
    print("  RAM_PIVOT  (%.4f, %.4f, %.4f)   stroke %.3f m along -Y"
          % (*RAM_PIVOT, STROKE))
    print("  knuckle bridge front y=%.3f — the ram must stay forward of it" % BRIDGE_FRONT)
    return total


if __name__ == "__main__":
    main()
