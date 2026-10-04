"""Rig the Strider elder (the `cyborg` collection) in strider_characters.blend as a four-legged
rigid-part walker for the crab walker's locomotion stack.

    blender --background strider_characters.blend --python strider_elder_rig.py

Refuses when Char_StriderElder exists. Never run against the user's strider1.blend.

The elder is a humanoid torso, head and arms on four robotic legs (components/mechanical/
walker_leg.blend's Coll_WalkerLeg_Raised, placed four times). Every part is RIGID -- parented to a
bone, not skinned -- which is the shape `WalkerRig` reads (Locomotion.md):

  * Per leg a chain Coxa_<id> (yaw, vertical) -> Hip_<id> -> Knee_<id> -> Ankle_<id> -> Foot_<id>
    (the sole's roll hinge, at ground level), the joints at the leg parts' own origins (the kit's
    pivots: _Upper at the hip, _Lower at the knee, _Foot at the ankle). Ids FL/FR/RL/RR: front is
    Blender -Y, the character's left is +X.
  * A `<Joint>Pin_<id>` cylinder under every leg joint, its long axis on the hinge: WalkerRig
    measures each hinge from its pin, never from the hierarchy.
  * The leg meshes are named Mesh_ElderLeg_<id>_{Upper,Lower,Foot}: the builder hangs its limb
    collision boxes off them by those suffixes, as it does for the crab.
  * Torso bones Root -> Pelvis -> Chest -> Neck -> Head; everything above the legs is rigid to the
    nearest of them, so the chest and head can sway (StriderElderSway). The arms have no bones:
    nothing animates them (the elder has no clips).

Parts sharing a bone are joined, so the elder draws one renderer per bone instead of 144 parts.
"""

import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import strider_characters_rig as kit  # noqa: E402  (blender --background has no package path)

NAME = "StriderElder"
SOURCE_COLLECTION = "cyborg"
LEG_PREFIX = "Mesh_WalkerLeg_Raised_"
LEG_PARTS = ("Upper", "Lower", "Foot")
PIN_MATERIAL = "Mat_Metal_Steel_Dark"
PIN_RADIUS = 0.018
PIN_LENGTH = 0.07
COXA_HEIGHT = 0.08      # the yaw bone stands this far above the hip, pointing down onto it
FOOT_LENGTH = 0.08      # the roll bone's length, outward from the sole

# Human kit segment bone -> elder torso bone. Arms and shoulders ride the chest.
TORSO = {"Hips": "Pelvis", "Spine": "Chest", "Chest": "Chest", "Neck": "Neck", "Head": "Head"}


def torso_bone(obj):
    bone = kit.segment_bone(obj)
    if bone is None:
        return None
    return TORSO.get(bone, "Chest")


def legs(meshes):
    """{id: {part: obj}} for the four legs, ids from each hip pivot's quadrant."""
    by_suffix = {}
    for obj in meshes:
        base = kit.strip_copy_suffix(obj.name)
        if base.startswith(LEG_PREFIX):
            part = base[len(LEG_PREFIX):]
            suffix = obj.name[len(base):]
            by_suffix.setdefault(suffix, {})[part] = obj
    out = {}
    for parts in by_suffix.values():
        if set(parts) != set(LEG_PARTS):
            raise SystemExit("A leg is missing parts: %s" % sorted(o.name for o in parts.values()))
        hip = parts["Upper"].matrix_world.translation
        leg_id = ("F" if hip.y < 0.0 else "R") + ("L" if hip.x > 0.0 else "R")
        if leg_id in out:
            raise SystemExit("Two legs in the %s quadrant" % leg_id)
        out[leg_id] = parts
    if len(out) != 4:
        raise SystemExit("Expected four legs, found %d" % len(out))
    return out


def pin(name, at, axis, material, coll):
    """A cylinder at `at` whose long axis lies along `axis` (world space)."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=PIN_RADIUS, radius2=PIN_RADIUS,
                          depth=PIN_LENGTH)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(material)
    obj = bpy.data.objects.new(name, me)
    coll.objects.link(obj)
    rot = Vector((0, 0, 1)).rotation_difference(axis.normalized()).to_matrix().to_4x4()
    obj.matrix_world = Matrix.Translation(at) @ rot
    return obj


def parent_to_bone(obj, rig, bone):
    world = obj.matrix_world.copy()
    obj.parent = rig
    obj.parent_type = 'BONE'
    obj.parent_bone = bone
    obj.matrix_world = world


def rig_elder():
    out_name = "Char_" + NAME
    if bpy.data.collections.get(out_name) is not None:
        raise SystemExit("%s already exists: the elder is rigged. Never re-rig over it." % out_name)
    src = kit.collection(SOURCE_COLLECTION)
    hand_rigs = [o for o in src.all_objects if o.type == 'ARMATURE']
    objs = kit.dedupe(list(src.all_objects))
    meshes = [o for o in objs if o.type == 'MESH']

    leg_parts = legs(meshes)
    joints = {}
    for leg_id, parts in leg_parts.items():
        hip = parts["Upper"].matrix_world.translation.copy()
        knee = parts["Lower"].matrix_world.translation.copy()
        ankle = parts["Foot"].matrix_world.translation.copy()
        foot = parts["Foot"]
        sole_z = min((foot.matrix_world @ v.co).z for v in foot.data.vertices)
        joints[leg_id] = (hip, knee, ankle, Vector((ankle.x, ankle.y, sole_z)))

    leg_objs = {o for parts in leg_parts.values() for o in parts.values()}
    torso = [o for o in meshes if o not in leg_objs]
    segments = [(o, torso_bone(o)) for o in torso if torso_bone(o)]
    pivots = {}
    for obj, bone in segments:
        pivots.setdefault(bone, obj.matrix_world.translation.copy())
    for needed in ("Pelvis", "Chest", "Neck", "Head"):
        if needed not in pivots:
            raise SystemExit("The elder has no segment for %s" % needed)

    kit.bake(meshes)
    for rig in hand_rigs:
        bpy.data.objects.remove(rig, do_unlink=True)
    linked = kit.link_palette(meshes, extra=(PIN_MATERIAL,))

    out = bpy.data.collections.new(out_name)
    bpy.context.scene.collection.children.link(out)

    hip_z = sum(j[0].z for j in joints.values()) / 4.0
    root = Vector((0.0, 0.0, hip_z))
    layout = [
        ("Root", None, root, root + Vector((0, 0, 0.1))),
        ("Pelvis", "Root", pivots["Pelvis"], pivots["Chest"]),
        ("Chest", "Pelvis", pivots["Chest"], pivots["Neck"]),
        ("Neck", "Chest", pivots["Neck"], pivots["Head"]),
        ("Head", "Neck", pivots["Head"], pivots["Head"] + Vector((0, 0, 0.2))),
    ]
    for leg_id, (hip, knee, ankle, sole) in sorted(joints.items()):
        outward = Vector((sole.x, sole.y, 0.0)).normalized()
        layout += [
            ("Coxa_" + leg_id, "Root", hip + Vector((0, 0, COXA_HEIGHT)), hip),
            ("Hip_" + leg_id, "Coxa_" + leg_id, hip, knee),
            ("Knee_" + leg_id, "Hip_" + leg_id, knee, ankle),
            ("Ankle_" + leg_id, "Knee_" + leg_id, ankle, sole),
            ("Foot_" + leg_id, "Ankle_" + leg_id, sole, sole + outward * FOOT_LENGTH),
        ]
    rig = kit.build_armature(NAME, layout, out)

    # Torso parts: rigid to the nearest torso bone by vertex vote (no garments on a rigid rig).
    trees = kit.nearest_bone_trees(segments)
    groups = {}
    for obj in torso:
        bone = torso_bone(obj)
        if bone is None:
            votes = {}
            for v in obj.data.vertices:
                d = kit.distances(trees, v.co, list(trees))
                best = min(d, key=d.get)
                votes[best] = votes.get(best, 0) + 1
            bone = max(votes, key=votes.get)
        groups.setdefault(bone, []).append(obj)
    for bone, objs_on_bone in sorted(groups.items()):
        print("  %-8s %3d part(s)" % (bone, len(objs_on_bone)))
        joined = kit.join(objs_on_bone, "Mesh_Elder_" + bone, out)
        parent_to_bone(joined, rig, bone)

    steel = linked[PIN_MATERIAL]
    for leg_id, parts in sorted(leg_parts.items()):
        hip, knee, ankle, sole = joints[leg_id]
        hinge = (knee - hip).cross(ankle - knee)
        if hinge.length < 1e-6:
            raise SystemExit("Leg %s is straight: its hinge plane is undefined" % leg_id)
        outward = Vector((sole.x, sole.y, 0.0)).normalized()
        for part, bone in (("Upper", "Hip_"), ("Lower", "Knee_"), ("Foot", "Foot_")):
            obj = parts[part]
            obj.name = obj.data.name = "Mesh_ElderLeg_%s_%s" % (leg_id, part)
            for c in list(obj.users_collection):
                c.objects.unlink(obj)
            out.objects.link(obj)
            parent_to_bone(obj, rig, bone + leg_id)
        for joint, at, axis in (("Coxa", hip, Vector((0, 0, 1))), ("Hip", hip, hinge), ("Knee", knee, hinge),
                                ("Ankle", ankle, hinge), ("Foot", sole, outward)):
            parent_to_bone(pin("%sPin_%s" % (joint, leg_id), at, axis, steel, out), rig, "%s_%s" % (joint, leg_id))

    for child in list(src.children_recursive):
        bpy.data.collections.remove(child)
    bpy.data.collections.remove(src)
    bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=False, do_recursive=True)

    parts = [o for o in out.objects if o.type == 'MESH']
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in parts)
    print("  %s: %d bones, %d renderers, %d tris" % (NAME, len(rig.data.bones), len(parts), tris))


def main():
    if not bpy.data.filepath.endswith("strider_characters.blend"):
        raise SystemExit("Run against strider_characters.blend only, never the user's strider1.blend.")
    rig_elder()
    bpy.ops.wm.save_mainfile()
    print("  saved %s" % bpy.data.filepath)


if __name__ == "__main__":
    main()
