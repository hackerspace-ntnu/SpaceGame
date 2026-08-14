"""Turn the raw `golem.fbx` kitbash into a rigged `golem.blend`.

**This is a one-shot converter, not a re-runnable generator.** It reads the
untouched FBX that shipped with the project -- 30 loose, unnamed, unparented
boxes -- and writes the .blend that becomes the source of truth from here on.
Once `golem.blend` exists it may carry hand edits, so this refuses to overwrite
it unless you pass `-- --force`.

    blender --background --python golem_rig.py
    blender --background --python golem_rig.py -- --force

## What the FBX actually contained

30 mesh objects called `Cube.032` .. `Cube.062` (`Cube.044` was already
missing), every one with `parent=None`, no armature, no vertex groups, no
materials and no actions. The names carry no anatomy. The assembly was
identified by rendering orthographic views and by reading the world-space
bounds, and it is a **hunched, knuckle-walking stone construct**: a boulder
torso, a low-slung head thrust forward, long four-segment arms whose fists rest
flat on the ground, and short three-segment legs set well behind them. Hands and
feet share the same ground plane (z = -1.127), so the rest pose is a four-point
stance and the rig is built to honour it rather than to stand the model up.

Axes in the FBX, and therefore in the .blend: **+Y is forward** (the face looks
that way), +Z is up, and the golem's own right is +X. `golem_export.py` turns
+Y forward into the library's -Y convention on the way to Unity.

## What this script does to the geometry

  1. Applies rotation and scale into the mesh data and recalculates normals.
     Six pieces carried a negative determinant -- `Cube.047` was at -6.29 --
     which mirrors the mesh and turns it inside out. Blender's solid view hides
     that; Unity's lighting does not.
  2. Names every piece for what it is.
  3. Gives every piece one of three stone materials.

None of that moves a vertex in world space, so the silhouette is byte-for-byte
the artist's.

## Why rigid bone parenting

The golem is 30 separate hard rocks. Smooth skinning would stretch the boulders
across every joint, which is exactly the wrong read for stone. Each rock is
parented to one bone instead, so it stays a rigid chunk and the joints read as
rock sliding past rock. The consequence lands on the Unity side:
bone-parented meshes are real child transforms, so `GolemBuilder.cs` must keep
`optimizeGameObjects` and `optimizeBones` off or the importer deletes the very
transforms the clips animate.

## The skeleton

17 bones, all placed from measured piece bounds rather than guessed:

    Bone_Root                      on the ground, midway between fists and feet
      Bone_Hips                    hip line, y = -7.93, z = 3.30
        Bone_Spine
          Bone_Chest               shoulder line, y = -3.14, z = 6.69
            Bone_Head
            Bone_Clav_{R,L}
              Bone_UpArm_{R,L}
                Bone_LoArm_{R,L}
                  Bone_Hand_{R,L}
        Bone_Thigh_{R,L}
          Bone_Shin_{R,L}
            Bone_Foot_{R,L}

The spine is deliberately short. A stone torso is one rigid mass and
`Mesh_Golem_Torso_Core` alone spans y -9.41 .. -1.66, from over the pelvis to
under the head; it can only be parented to one bone, so any large spine flex
would open a gap between it and the rocks around it. `golem_anim.py` keeps
spine and hip flex under about 6 degrees for that reason, which is also how a
heavy construct should move.
"""

import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))

# Walk up to the Unity project root looking for ProjectSettings/ rather than
# counting parent directories -- this library has already been moved once.
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(
        os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)

# The artist's untouched kitbash, kept here rather than under Assets/ because
# `golem_export.py` writes the *shipping* FBX to that path -- if this read from
# there, running the pipeline twice would rig the rig. It arrived on `main` as
# `Assets/Game/Art/Models/Creatures/golem.fbx` and this is the only copy of it
# outside git history.
FBX = os.path.join(HERE, "golem_source.fbx")
DST = os.path.join(HERE, "golem.blend")

ARM = "Arm_Golem"

# ---------------------------------------------------------------------------
# Measured anchors, in the FBX's own units. All of these came out of the
# world-space bounds of the pieces named beside them; nothing here is invented.
# ---------------------------------------------------------------------------

GROUND = -1.127          # lowest vertex in the file: the underside of the fists
CX = -20.20              # body centreline in x
MIRROR_ARM = -20.130     # the arm pieces mirror about this, exactly
MIRROR_LEG = -20.299     # the leg and pelvis pieces mirror about this

# ---------------------------------------------------------------------------
# Renames. The FBX order is meaningless, so this table is the only record of
# which box is which.
# ---------------------------------------------------------------------------

RENAMES = {
    "Cube.032": "Mesh_Golem_Back_Crown",
    "Cube.033": "Mesh_Golem_Head",
    "Cube.034": "Mesh_Golem_Back_Rock_L",
    "Cube.035": "Mesh_Golem_Shoulder_Cap_R",
    "Cube.036": "Mesh_Golem_Chest_Upper_R",
    "Cube.037": "Mesh_Golem_Back_Hump",
    "Cube.038": "Mesh_Golem_Back_Rock_Top",
    "Cube.039": "Mesh_Golem_Chest_Lower_R",
    "Cube.040": "Mesh_Golem_Chest_Lower_L",
    "Cube.041": "Mesh_Golem_Forearm_Plate_L",
    "Cube.042": "Mesh_Golem_Fist_Plate_L",
    "Cube.043": "Mesh_Golem_Chest_Rock_L",
    "Cube.045": "Mesh_Golem_Shoulder_Rock_R",
    "Cube.046": "Mesh_Golem_Shoulder_Cap_L",
    "Cube.047": "Mesh_Golem_Torso_Core",
    "Cube.048": "Mesh_Golem_Fist_R",
    "Cube.049": "Mesh_Golem_Forearm_R",
    "Cube.050": "Mesh_Golem_UpperArm_R",
    "Cube.051": "Mesh_Golem_Pauldron_R",
    "Cube.052": "Mesh_Golem_Foot_R",
    "Cube.053": "Mesh_Golem_Shin_R",
    "Cube.054": "Mesh_Golem_Thigh_R",
    "Cube.055": "Mesh_Golem_Pelvis",
    "Cube.056": "Mesh_Golem_Thigh_L",
    "Cube.057": "Mesh_Golem_Shin_L",
    "Cube.058": "Mesh_Golem_Foot_L",
    "Cube.059": "Mesh_Golem_Pauldron_L",
    "Cube.060": "Mesh_Golem_UpperArm_L",
    "Cube.061": "Mesh_Golem_Forearm_L",
    "Cube.062": "Mesh_Golem_Fist_L",
}

# ---------------------------------------------------------------------------
# Materials.
#
# The shared palette has no stone family -- 35 entries across Emissive, Fabric,
# Glass, Hide, Metal, Neutral, Paint, Plastic and Wood, and the only
# non-metallic greys in it are `Mat_Neutral_Panel_Grey` (an interior wall
# panel) and two Fabric entries. Painting a boulder with a material called
# Fabric is a lie the library index would then repeat, so these three are
# authored locally instead and `golem_BUILD.md` asks for them to be promoted
# into `palette.blend` by whoever owns that file. Nothing else in the library
# does this; it is a deliberate, documented exception, not an oversight.
# ---------------------------------------------------------------------------

STONE = {
    # name:            (hex,       roughness)
    "Mat_Stone_Pale":   ("A9A296", 0.85),   # sun-facing masses
    "Mat_Stone_Shadow": ("6A655C", 0.90),   # packing rocks in the shadowed gaps
    "Mat_Stone_Grit":   ("4A463F", 0.94),   # ground contact: fists and feet
}

MATERIAL_OF = {
    "Mat_Stone_Pale": [
        "Mesh_Golem_Head", "Mesh_Golem_Back_Crown", "Mesh_Golem_Back_Hump",
        "Mesh_Golem_Torso_Core", "Mesh_Golem_Pelvis",
        "Mesh_Golem_Shoulder_Cap_R", "Mesh_Golem_Shoulder_Cap_L",
        "Mesh_Golem_Shoulder_Rock_R",
        "Mesh_Golem_UpperArm_R", "Mesh_Golem_UpperArm_L",
        "Mesh_Golem_Thigh_R", "Mesh_Golem_Thigh_L",
        "Mesh_Golem_Shin_R", "Mesh_Golem_Shin_L",
    ],
    "Mat_Stone_Shadow": [
        "Mesh_Golem_Back_Rock_L", "Mesh_Golem_Back_Rock_Top",
        "Mesh_Golem_Chest_Upper_R", "Mesh_Golem_Chest_Lower_R",
        "Mesh_Golem_Chest_Lower_L", "Mesh_Golem_Chest_Rock_L",
        "Mesh_Golem_Pauldron_R", "Mesh_Golem_Pauldron_L",
        "Mesh_Golem_Forearm_R", "Mesh_Golem_Forearm_L",
        "Mesh_Golem_Forearm_Plate_L",
    ],
    "Mat_Stone_Grit": [
        "Mesh_Golem_Fist_R", "Mesh_Golem_Fist_L", "Mesh_Golem_Fist_Plate_L",
        "Mesh_Golem_Foot_R", "Mesh_Golem_Foot_L",
    ],
}

# ---------------------------------------------------------------------------
# Bones. (name, head, tail, parent, connected)
#
# The right-side limb bones are listed once and mirrored; the mirror plane is
# the one the *geometry* actually uses, which differs by 0.17 between the arms
# and the legs. Using a single average would leave every limb bone a little off
# its own rocks.
# ---------------------------------------------------------------------------

SPINE = [
    ("Bone_Root",  (CX, -5.30, GROUND), (CX, -3.80, GROUND), None,          False),
    ("Bone_Hips",  (CX, -7.93, 3.30),   (CX, -6.34, 4.43),   "Bone_Root",   False),
    ("Bone_Spine", (CX, -6.34, 4.43),   (CX, -4.75, 5.56),   "Bone_Hips",   True),
    ("Bone_Chest", (CX, -4.75, 5.56),   (CX, -3.16, 6.69),   "Bone_Spine",  True),
    ("Bone_Head",  (CX, -3.60, 6.30),   (CX, -0.40, 5.80),   "Bone_Chest",  False),
]

ARM_R = [
    ("Bone_Clav",  (-19.00, -3.60, 6.60), (-16.90, -2.90, 7.10), "Bone_Chest", False),
    ("Bone_UpArm", (-16.90, -2.90, 7.10), (-16.55, -2.40, 4.46), "Bone_Clav",  True),
    ("Bone_LoArm", (-16.55, -2.40, 4.46), (-16.60, -2.25, 2.03), "Bone_UpArm", True),
    ("Bone_Hand",  (-16.60, -2.25, 2.03), (-16.80, -2.30, -1.05), "Bone_LoArm", True),
]

LEG_R = [
    ("Bone_Thigh", (-18.35, -7.93, 4.35), (-18.05, -8.20, 2.65), "Bone_Hips",  False),
    ("Bone_Shin",  (-18.05, -8.20, 2.65), (-17.60, -8.33, 0.35), "Bone_Thigh", True),
    ("Bone_Foot",  (-17.60, -8.33, 0.35), (-17.40, -6.50, -0.95), "Bone_Shin",  True),
]

BIND = {
    "Bone_Head":  ["Mesh_Golem_Head"],
    "Bone_Chest": ["Mesh_Golem_Back_Crown", "Mesh_Golem_Back_Rock_L",
                   "Mesh_Golem_Shoulder_Cap_R", "Mesh_Golem_Chest_Upper_R",
                   "Mesh_Golem_Back_Rock_Top", "Mesh_Golem_Chest_Lower_R",
                   "Mesh_Golem_Chest_Lower_L", "Mesh_Golem_Chest_Rock_L",
                   "Mesh_Golem_Shoulder_Rock_R", "Mesh_Golem_Shoulder_Cap_L"],
    "Bone_Spine": ["Mesh_Golem_Back_Hump", "Mesh_Golem_Torso_Core"],
    "Bone_Hips":  ["Mesh_Golem_Pelvis"],

    "Bone_Clav_R":  ["Mesh_Golem_Pauldron_R"],
    "Bone_UpArm_R": ["Mesh_Golem_UpperArm_R"],
    "Bone_LoArm_R": ["Mesh_Golem_Forearm_R"],
    "Bone_Hand_R":  ["Mesh_Golem_Fist_R"],
    "Bone_Clav_L":  ["Mesh_Golem_Pauldron_L"],
    "Bone_UpArm_L": ["Mesh_Golem_UpperArm_L"],
    # The two unpaired rocks on the golem's left arm ride with the segment they
    # sit on -- the kitbash is asymmetric there on purpose.
    "Bone_LoArm_L": ["Mesh_Golem_Forearm_L", "Mesh_Golem_Forearm_Plate_L"],
    "Bone_Hand_L":  ["Mesh_Golem_Fist_L", "Mesh_Golem_Fist_Plate_L"],

    "Bone_Thigh_R": ["Mesh_Golem_Thigh_R"],
    "Bone_Shin_R":  ["Mesh_Golem_Shin_R"],
    "Bone_Foot_R":  ["Mesh_Golem_Foot_R"],
    "Bone_Thigh_L": ["Mesh_Golem_Thigh_L"],
    "Bone_Shin_L":  ["Mesh_Golem_Shin_L"],
    "Bone_Foot_L":  ["Mesh_Golem_Foot_L"],
}


# ---------------------------------------------------------------------------

def mirror(p, plane):
    return (2.0 * plane - p[0], p[1], p[2])


def apply_rotation_and_scale(obj):
    """Bake rotation and scale into the mesh and turn the normals outward.

    Location is left alone: it is where the artist put the rock. `bound_box`
    and `matrix_world` both go stale until the depsgraph re-evaluates, so
    nothing downstream of this may read them before a `view_layer.update()`.
    """
    loc = obj.location.copy()
    obj.data.transform(Matrix.Translation(-loc) @ obj.matrix_basis)
    obj.rotation_euler = (0.0, 0.0, 0.0)
    obj.scale = (1.0, 1.0, 1.0)
    obj.location = loc

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def srgb_to_linear(v):
    v = v / 255.0
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4


def make_materials():
    made = {}
    for name, (hexcol, rough) in STONE.items():
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        rgb = tuple(srgb_to_linear(int(hexcol[i:i + 2], 16)) for i in (0, 2, 4))
        for node in mat.node_tree.nodes:
            if node.type == 'BSDF_PRINCIPLED':
                node.inputs["Base Color"].default_value = rgb + (1.0,)
                node.inputs["Roughness"].default_value = rough
                node.inputs["Metallic"].default_value = 0.0
        # Workbench and the outliner read this one, not the node.
        mat.diffuse_color = rgb + (1.0,)
        made[name] = mat
    return made


def build_armature(coll):
    arm_data = bpy.data.armatures.new(ARM)
    arm_obj = bpy.data.objects.new(ARM, arm_data)
    coll.objects.link(arm_obj)

    spec = list(SPINE)
    for src, plane in ((ARM_R, MIRROR_ARM), (LEG_R, MIRROR_LEG)):
        for name, head, tail, parent, conn in src:
            for side, sign in (("R", 1), ("L", -1)):
                p = parent if parent.startswith("Bone_Chest") or \
                    parent.startswith("Bone_Hips") else parent + "_" + side
                spec.append((name + "_" + side,
                             head if sign > 0 else mirror(head, plane),
                             tail if sign > 0 else mirror(tail, plane),
                             p, conn))

    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones
    for name, head, tail, parent, conn in spec:
        b = eb.new(name)
        b.head, b.tail = Vector(head), Vector(tail)
        if parent:
            b.parent = eb[parent]
            b.use_connect = conn
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm_obj


def bind(obj, arm_obj, bone_name):
    """Rigid-parent an object to a bone, leaving it exactly where it is.

    Bone parenting measures from the bone's *tail*, so the offset can only be
    resolved once the parent is set; assigning `matrix_world` back does that.
    """
    world = obj.matrix_world.copy()
    obj.parent = arm_obj
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    obj.matrix_parent_inverse = Matrix.Identity(4)
    bpy.context.view_layer.update()
    obj.matrix_world = world


def main():
    force = "--force" in (sys.argv[sys.argv.index("--") + 1:]
                          if "--" in sys.argv else [])
    if os.path.exists(DST) and not force:
        raise SystemExit(
            "%s already exists. It is the source of truth from here on and may\n"
            "carry hand edits; this script rebuilds it from the raw FBX and\n"
            "would destroy them. Pass `-- --force` if that is what you want."
            % DST)
    if not os.path.exists(FBX):
        raise SystemExit("No model at %s" % FBX)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=FBX)

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    shared = [o.name for o in meshes if o.data.users > 1]
    if shared:
        raise SystemExit("Mesh data shared between objects: %s. Applying "
                         "transforms would corrupt every user." % shared)

    missing = sorted(set(RENAMES) - {o.name for o in meshes})
    extra = sorted({o.name for o in meshes} - set(RENAMES))
    if missing or extra:
        raise SystemExit(
            "golem.fbx is not the file this script was written against.\n"
            "  missing: %s\n  unexpected: %s" % (missing, extra))

    # -- 1. transforms and normals ------------------------------------------
    flipped = [o.name for o in meshes if o.matrix_world.to_3x3().determinant() < 0]
    for obj in meshes:
        apply_rotation_and_scale(obj)
    bpy.context.view_layer.update()
    print("Applied rotation/scale to %d meshes; %d had a negative determinant "
          "and were inside out: %s"
          % (len(meshes), len(flipped), ", ".join(sorted(flipped))))

    # -- 2. names ------------------------------------------------------------
    for old, new in RENAMES.items():
        obj = bpy.data.objects[old]
        obj.name = new
        obj.data.name = new

    # -- 3. materials --------------------------------------------------------
    mats = make_materials()
    for mat_name, members in MATERIAL_OF.items():
        for obj_name in members:
            obj = bpy.data.objects[obj_name]
            obj.data.materials.clear()
            obj.data.materials.append(mats[mat_name])
    unpainted = [o.name for o in bpy.data.objects
                 if o.type == 'MESH' and not o.data.materials]
    if unpainted:
        raise SystemExit("No material assigned to %s" % unpainted)
    print("Assigned %d stone materials across %d meshes."
          % (len(mats), sum(len(v) for v in MATERIAL_OF.values())))

    # -- 4. armature and binding --------------------------------------------
    rig_coll = bpy.data.collections.new("Coll_Golem_Rig")
    bpy.context.scene.collection.children.link(rig_coll)
    arm_obj = build_armature(rig_coll)

    bound = 0
    for bone_name, members in BIND.items():
        if bone_name not in arm_obj.data.bones:
            raise SystemExit("No bone %s" % bone_name)
        for obj_name in members:
            bind(bpy.data.objects[obj_name], arm_obj, bone_name)
            bound += 1
    loose = [o.name for o in bpy.data.objects
             if o.type == 'MESH' and o.parent is None]
    if loose:
        raise SystemExit(
            "Unparented after binding: %s. golem_export.py requires the "
            "armature to be the only root object -- anything left loose "
            "would ship at the wrong scale and orientation." % loose)
    print("Built %d bones and rigid-parented all %d meshes to them."
          % (len(arm_obj.data.bones), bound))

    bpy.context.scene.render.fps = 30
    bpy.ops.wm.save_as_mainfile(filepath=DST)
    print("Saved %s" % DST)


main()
