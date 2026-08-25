"""The Dragon Bazooka — a recoilless launcher with a temple dragon for a muzzle.

A lacquered launch tube on a pistol grip and foregrip, with a cast bronze-and-
gold dragon head bolted over the muzzle so the rocket leaves through its teeth.
The barrel, the grips and the head are all library components; what is unique
to this model is the assembly, the hanging tassel at the collar, and the
markers the Unity prefab builder reads.

Assembled from:

  components/mechanical/launch_tube.blend   Coll_LaunchTube_Banded
  components/mechanical/weapon_grip.blend   Coll_WeaponGrip_Pistol / _Fore
                                            / _Saddle
  components/organic/dragon_head.blend      Coll_DragonHead_Roaring

1.20 m from venturi to snout. Front is -Y, up is +Z. The origin sits at the
PISTOL GRIP, where the trigger hand closes, because that is the point the
weapon is held by and therefore the point Unity's grip pose is measured from.

<b>The jaw ships as its own object with its pivot on the hinge axis.</b> There
is deliberately no armature: one rigid part turning about one axis is carried
by an object pivot, and the FBX then hands Unity a plain transform the artifact
can drive for a roar on firing. Same call, and the same reasoning, as the
sucker puncher's ram.

Generation script — historical record. The .blend is the source of truth;
never re-run this over the file it produced.
"""

import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))
from _buildlib import *  # noqa: E402,F403

from mathutils import Matrix, Vector  # noqa: E402

LIB = os.path.dirname(os.path.dirname(HERE))
TUBE_BLEND = os.path.join(LIB, "components", "mechanical", "launch_tube.blend")
GRIP_BLEND = os.path.join(LIB, "components", "mechanical", "weapon_grip.blend")
HEAD_BLEND = os.path.join(LIB, "components", "organic", "dragon_head.blend")

MATS = [
    "Mat_Paint_Lacquer_Vermilion",  # 0  tassel cords — and every bevel
    "Mat_Metal_Gold_Leaf",          # 1  tassel crown and bead
    "Mat_Fabric_Rope_Hemp",         # 2  the sling loop
]
(VERM, GOLD, ROPE) = range(3)

# ── Assembly coordinates ──────────────────────────────────────────────────
#
# Everything is written in TUBE space — the launch tube's own origin, on the
# bore axis at the breech face — and the whole model is re-origined onto the
# grip at the end. Measuring the assembly from the barrel and the pose from the
# hand is what keeps a change to either from silently moving the other.

TUBE_MUZZLE = -0.950     # launch_tube's Marker_Muzzle
TUBE_RADIUS = 0.055
MOUNT_Z = -(TUBE_RADIUS + 0.017)   # underside of the tube's mount pad

GRIP_Y = -0.300          # where the trigger hand closes
FORE_Y = -0.640
SADDLE_Y = -0.140

# The head's own bore sits 12 mm above its origin (dragon_head.py bores the
# gullet at z = 0.012), so the head drops by that much to put its throat on the
# tube's axis. Getting this wrong does not look wrong — it just fires the
# rocket through the dragon's chin.
HEAD_BORE_Z = 0.012
HEAD_Y = TUBE_MUZZLE - 0.010     # collar face overlaps the muzzle ring

# The head is oversized against the barrel on purpose. At 1:1 the component's
# 152 mm jowl is barely wider than the tube's 110 mm, and the assembly read as
# a mask taped to a pipe — the joint showed as a step and the ornament had no
# authority. A figurehead has to be the widest thing on the weapon.
HEAD_SCALE = 1.22


def append_objects(blend, names, into):
    """Append named objects from a component file.

    Appended, not linked: an export has to carry real mesh data, and a linked
    object arrives as a proxy the FBX writer skips silently. Same helper as
    sucker_puncher.py.
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

    # Force the depsgraph to recompute matrix_world before anyone reads it.
    # A freshly appended object still reports the IDENTITY matrix until the
    # view layer updates, however its location field reads — which silently
    # collapsed the jaw's hinge onto the head's origin, and a jaw that rotates
    # about the wrong point does not fail, it just chews sideways.
    bpy.context.view_layer.update()
    return out


def place(obj, matrix, origin=None):
    """Apply `matrix` into the mesh data, leaving rotation 0 and scale 1.

    The library's convention is that transforms are applied, and it is not
    cosmetic: a rotated object exports with that rotation baked into the FBX
    node, and Unity then hands the game a Transform whose local axes are not
    the ones the code reasons about. `origin` re-seats the pivot at a chosen
    world point — which is how the jaw keeps its hinge.
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


def marker(coll, name, at, mats, size=0.004):
    """A tiny cube carrying a coordinate across the FBX.

    Empties are not exported (object_types={"MESH"}), so a named 4 mm mesh
    survives the trip; DragonBazookaBuilder reads its transform and strips the
    renderer. Same trick as portal_gun.py and gravel_blaster.py.
    """
    p = Part(mats)
    p.box((0, 0, 0), (size, size, size), GOLD)
    obj = p.finish(name, coll)
    obj.location = at
    return obj


def tassel(coll, mats, name, at, cords=8, length=0.150):
    """The silk tassel hanging off the muzzle collar.

    The one piece of geometry unique to this model, and it earns its place: the
    tube and the head are both hard lacquered shells, and without something
    soft hanging off it the weapon reads as machinery with a mask on rather
    than as a ceremonial object.
    """
    p = Part(mats)
    at = Vector(at)

    p.cyl(at, 0.022, 0.028, 'Z', 12, GOLD, radius_top=0.013)
    p.torus(at + Vector((0, 0, 0.016)), 0.011, 0.004, 'X', 16, 8, GOLD)

    for i in range(cords):
        a = 2 * math.pi * i / cords
        spread = 0.016
        top = at + Vector((math.cos(a) * spread * 0.4, math.sin(a) * spread * 0.4,
                           -0.010))
        # Cords splay as they fall and are cut to different lengths, because a
        # bundle of identical parallel rods is the one thing a tassel is not.
        drop = length * (0.72 + 0.28 * ((i * 7) % cords) / max(cords - 1, 1))
        bottom = top + Vector((math.cos(a) * spread, math.sin(a) * spread,
                               -drop))
        steps = 6
        for k in range(steps):
            t0, t1 = k / steps, (k + 1) / steps
            a0, b0 = top.lerp(bottom, t0), top.lerp(bottom, t1)
            mid = (a0 + b0) / 2.0
            d = b0 - a0
            turn = Vector((0, 0, 1)).rotation_difference(d.normalized()) \
                                    .to_matrix().to_4x4()
            p.box(mid, (0.0055, 0.0055, d.length * 1.1), VERM, rot=turn)

    return p.finish(name, coll)


def sling_loop(p, at, mats_index=ROPE):
    """A rope loop for the carry sling. Two per weapon.

    Ring plane contains the barrel axis (axis='X'), because the strap runs
    fore-and-aft through it. On 'Y' the loops lie across the tube and present
    themselves edge-on from every angle a shouldered weapon is ever seen from,
    which turned them into two beige tabs stuck to the spine.
    """
    return p.torus(Vector(at), 0.017, 0.005, 'X', 16, 8, mats_index)


# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    model = collection("Coll_DragonBazooka")

    # ── The barrel, at the origin of tube space ──
    (tube,) = append_objects(TUBE_BLEND, ["Mesh_LaunchTube_Banded"], model)
    place(tube, Matrix.Identity(4))

    # ── The dragon head over the muzzle ──
    # Skull and jaw move together, so one matrix drives both; the jaw keeps its
    # own pivot through `origin`, which is what leaves it animatable.
    head_shift = (Matrix.Translation((0.0, HEAD_Y, -HEAD_BORE_Z * HEAD_SCALE))
                  @ Matrix.Scale(HEAD_SCALE, 4))
    (skull, jaw) = append_objects(
        HEAD_BLEND, ["Mesh_DragonHead_Roaring", "Mesh_DragonJaw_Roaring"],
        model)
    place(skull, head_shift)
    jaw_pivot = head_shift @ jaw.matrix_world.to_translation()
    place(jaw, head_shift, origin=jaw_pivot)

    # ── Grips ──
    (pistol,) = append_objects(GRIP_BLEND, ["Mesh_WeaponGrip_Pistol"], model)
    place(pistol, Matrix.Translation((0.0, GRIP_Y, MOUNT_Z)))

    (foregrip,) = append_objects(GRIP_BLEND, ["Mesh_WeaponGrip_Fore"], model)
    place(foregrip, Matrix.Translation((0.0, FORE_Y, MOUNT_Z)))

    # The shoulder saddle is turned over so its trough opens DOWNWARD: the
    # component is authored as a rest something sits in, and here the thing it
    # cradles is the gunner, not the gun.
    (saddle,) = append_objects(GRIP_BLEND, ["Mesh_WeaponGrip_Saddle"], model)
    place(saddle, Matrix.Translation((0.0, SADDLE_Y, MOUNT_Z + 0.008))
                  @ Matrix.Rotation(math.pi, 4, 'Y'))

    # ── Unique furniture ──
    tassel(model, mats, "Mesh_DragonBazooka_Tassel",
           (0.0, TUBE_MUZZLE + 0.055, -TUBE_RADIUS - 0.004))

    loops = Part(mats)
    sling_loop(loops, (0.0, -0.210, TUBE_RADIUS + 0.020))
    sling_loop(loops, (0.0, -0.760, TUBE_RADIUS + 0.020))
    loops.finish("Mesh_DragonBazooka_SlingLoops", model)

    # ── Markers, in tube space ──
    # Where the rocket leaves the teeth, where the hand closes, where the
    # backblast erupts, and the jaw's hinge. Read by DragonBazookaBuilder.
    marker(model, "Marker_Muzzle", (0.0, HEAD_Y - 0.196 * HEAD_SCALE, 0.0),
           mats)
    marker(model, "Marker_Grip", (0.0, GRIP_Y + 0.010, MOUNT_Z - 0.060), mats)
    marker(model, "Marker_Breech", (0.0, 0.098, 0.0), mats)
    marker(model, "Marker_JawHinge", jaw_pivot, mats)

    report()
    save(out)


main()
