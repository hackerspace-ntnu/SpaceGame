"""Turn the user's horse1.blend into the game's robot horse, in a COPY.

Run (from the repo root):

    blender --background --python Assets/Game/Art/Models/_Source~/models/creatures/robot_horse/robot_horse_rig.py -- [source.blend]

Source defaults to ~/Documents/Blender/horse1.blend (the user's file, authored outside the
repo like the sand nomads). It is never written. The copy lands beside this script as
robot_horse.blend, and the script refuses to overwrite one that already exists -- the copy is
the source of truth from then on, and hand edits in it must not be regenerated away.

What was wrong, measured on 2026-09-07
--------------------------------------
The file held three things at three heights: the armature and the joined, weight-painted
`horse` mesh at ground level; the 839 loose primitive parts it was joined from (Cube.*,
Cylinder.*, Plane.*) parked 4 m above; and an organic reference horse (`84218.002`) 7 m up
with texture materials that point at files that are not in the .blend.

The weights on `horse` were automatic weights over a machine made of rigid plates:
13,664 of 17,055 vertices summed to something other than 1, 868 carried more than the four
influences Unity keeps, 33 belonged to `root` from over half a metre away, and every plate
that straddled two bones bent like skin. That is the "something not right with the weight
paint".

What this does
--------------
1. Deletes the parked duplicates and the reference mesh from the COPY.
2. Binds the robot RIGIDLY: the mesh is split into its connected islands (one per plate,
   strut, pin), each island goes 100% to the bone that already held most of its weight --
   the artist's own intent, read back -- or to the nearest deform bone if it had none.
3. Fixes the hoof bones: they were children of the IK targets, which do not exist once the
   FBX is baked, so in Unity a hoof would have stayed on the ground while the leg walked
   away. They are reparented to the cannon bones (metatarsal / metacarpal) at the same
   rest position. The IK and pole bones are deleted: the clips are authored FK
   (robot_horse_anim.py), and non-deform bones would only ship as junk transforms.
4. Removes the no-op Mirror modifier, names the objects to the library convention, gives the
   mesh a single material slot (the Unity builder dresses it from the palette), and saves.
5. Probes the leg axes and prints where a hoof goes for +15 deg about each world axis on the
   femur, so robot_horse_anim.py's sign conventions are read off the rig, not assumed.
"""
import bpy
import bmesh
import collections
import math
import os
import shutil
import sys
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
DST = os.path.join(HERE, "robot_horse.blend")
DEFAULT_SRC = os.path.expanduser("~/Documents/Blender/horse1.blend")

ARM_NAME = "Arm_RobotHorse"
BODY_NAME = "Mesh_RobotHorse_Body"
REFERENCE_MESH = "84218.002"
LOOSE_FAMILIES = ("Cube", "Cylinder", "Plane")
HOOF_PARENTS = {
    "hoof_B.L": "metarsal.L", "hoof_B.R": "metarsal.R",
    "hoof_F.L": "metacarpal.L", "hoof_F.R": "metacarpal.R",
}
CONTROL_BONES = ("IK_back.L", "IK_back.R", "IK_front.L", "IK_front.R",
                 "pole_back.L", "pole_back.R", "pole_fromt.L", "pole_fromt.R")
MATERIAL = "Mat_Metal_Rust_Heavy"


def family(name):
    base = name.split(".")[0]
    return "".join(ch for ch in base if not ch.isdigit())


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    src = argv[0] if argv else DEFAULT_SRC
    if not os.path.exists(src):
        raise SystemExit("No source at %s" % src)
    if os.path.exists(DST):
        raise SystemExit("%s already exists. It is the source of truth now; edit it, do not "
                         "regenerate it." % DST)

    shutil.copyfile(src, DST)
    bpy.ops.wm.open_mainfile(filepath=DST)

    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = bpy.data.objects["horse"]

    # 1. The parked duplicates and the reference.
    dropped = 0
    for o in list(bpy.data.objects):
        if o.type != "MESH" or o is body:
            continue
        if o.name == REFERENCE_MESH or family(o.name) in LOOSE_FAMILIES:
            bpy.data.objects.remove(o, do_unlink=True)
            dropped += 1
    print("  dropped %d loose/reference object(s)" % dropped)
    for mat in list(bpy.data.materials):
        if mat.users == 0:
            bpy.data.materials.remove(mat)

    # 2. Rigid binding per island.
    for m in list(body.modifiers):
        if m.type == "MIRROR":
            body.modifiers.remove(m)
    deform = [b for b in arm.data.bones if b.use_deform]
    M = arm.matrix_world
    seg = {b.name: (M @ b.head_local, M @ b.tail_local) for b in deform}
    group_index = {g.index: g.name for g in body.vertex_groups}

    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.verts.ensure_lookup_table()
    islands = _islands(bm)
    bm.free()

    def nearest_bone(p):
        best, best_d = None, 1e9
        for name, (a, t) in seg.items():
            ab = t - a
            u = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
            d = (p - (a + ab * u)).length
            if d < best_d:
                best, best_d = name, d
        return best

    mesh = body.data
    assigned = collections.Counter()
    by_nearest = 0
    per_vertex_bone = [None] * len(mesh.vertices)
    for island in islands:
        votes = collections.Counter()
        centre = Vector()
        for vi in island:
            v = mesh.vertices[vi]
            centre += body.matrix_world @ v.co
            for g in v.groups:
                name = group_index.get(g.group)
                if name in seg and g.weight > 1e-4:
                    votes[name] += g.weight
        centre /= len(island)
        if votes:
            bone = votes.most_common(1)[0][0]
        else:
            bone = nearest_bone(centre)
            by_nearest += 1
        assigned[bone] += 1
        for vi in island:
            per_vertex_bone[vi] = bone

    for g in list(body.vertex_groups):
        body.vertex_groups.remove(g)
    groups = {}
    for name in seg:
        groups[name] = body.vertex_groups.new(name=name)
    for vi, bone in enumerate(per_vertex_bone):
        groups[bone].add([vi], 1.0, "REPLACE")
    print("  %d islands bound rigidly (%d by nearest bone, the rest by their own dominant weight)"
          % (len(islands), by_nearest))
    print("  parts per bone:", dict(assigned.most_common()))

    # 3. Hoof bones onto the cannon bones; controls gone; IK gone.
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    for pb in arm.pose.bones:
        for c in list(pb.constraints):
            pb.constraints.remove(c)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.data.edit_bones
    for hoof, parent in HOOF_PARENTS.items():
        eb[hoof].parent = eb[parent]
        eb[hoof].use_connect = False
    for name in CONTROL_BONES:
        if name in eb:
            eb.remove(eb[name])
    bpy.ops.object.mode_set(mode="OBJECT")

    # 4. Names, material, tidy.
    arm.name = ARM_NAME
    arm.data.name = ARM_NAME
    body.name = BODY_NAME
    body.data.name = BODY_NAME
    body.data.materials.clear()
    mat = bpy.data.materials.get(MATERIAL) or bpy.data.materials.new(MATERIAL)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (0.42, 0.20, 0.08, 1.0)
        bsdf.inputs["Metallic"].default_value = 0.9
        bsdf.inputs["Roughness"].default_value = 0.75
    body.data.materials.append(mat)

    # 5. Axis probe, so the animation script's signs come from the rig.
    _probe(arm)

    bpy.ops.wm.save_mainfile(filepath=DST)
    print("  saved %s" % DST)


def _islands(bm):
    seen = [False] * len(bm.verts)
    out = []
    for v in bm.verts:
        if seen[v.index]:
            continue
        stack = [v]
        seen[v.index] = True
        island = []
        while stack:
            cur = stack.pop()
            island.append(cur.index)
            for e in cur.link_edges:
                o = e.other_vert(cur)
                if not seen[o.index]:
                    seen[o.index] = True
                    stack.append(o)
        out.append(island)
    return out


def _probe(arm):
    """+15 deg about each WORLD axis on a bone: where does its hoof go?"""
    def world_axis_local(pb, axis):
        basis = pb.bone.matrix_local.to_3x3()
        return (basis.inverted() @ Vector(axis)).normalized()

    def tip_world(name):
        bpy.context.view_layer.update()
        return arm.matrix_world @ arm.pose.bones[name].tail

    print("  AXIS PROBE (tip displacement, world metres; forward is -Y, up is +Z):")
    for bone, tip in (("femur.L", "hoof_B.L"), ("fibula.L", "hoof_B.L"), ("metarsal.L", "hoof_B.L"),
                      ("hoof_B.L", "hoof_B.L"),
                      ("scapula.L", "hoof_F.L"), ("humerus.L", "hoof_F.L"), ("radius.L", "hoof_F.L"),
                      ("metacarpal.L", "hoof_F.L"), ("hoof_F.L", "hoof_F.L"),
                      ("spine1", "head"), ("spine4", "head"), ("neck1", "head"), ("head", "head"),
                      ("tail1", "tail4")):
        pb = arm.pose.bones[bone]
        pb.rotation_mode = "XYZ"
        rest = tip_world(tip)
        for label, axis in (("X", (1, 0, 0)), ("Y", (0, 1, 0)), ("Z", (0, 0, 1))):
            pb.rotation_euler = Matrix.Rotation(math.radians(15), 3, world_axis_local(pb, axis)).to_euler("XYZ")
            moved = tip_world(tip) - rest
            pb.rotation_euler = (0.0, 0.0, 0.0)
            print("    %-13s +15deg about world %s -> tip moves (%+.2f, %+.2f, %+.2f)"
                  % (bone, label, moved.x, moved.y, moved.z))


if __name__ == "__main__":
    main()
