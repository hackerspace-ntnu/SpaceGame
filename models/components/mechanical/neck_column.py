"""components/mechanical/neck_column — robot neck vertebrae and head.

Lifted from the ostrich's neck (`Assets/Models/Environment/creatures/robots/
Ostrich/ostrich_neck.blend`) so the crawler's tail can end in a head that bites
rather than a pincer. The skinned muscles, drive tendons and elastic bands are
deliberately left behind — they are weighted to an eleven-bone chain and this
neck has five joints, so they cannot come across without being rebuilt.

Three things are changed on the way in.

**Assembled parts, not prototypes.** The source file holds both: `NECK_PART_*`
prototypes parked on a grid as a parts palette, and `NECK_Vert_*` / `NECK_HEAD_*`
which are the real thing, positioned on the rig. Only the assembled objects carry
the skull's actual layout — the beak against the cranium, the eyes in their
sockets — so those are what get taken. The prototypes look tempting because they
are already separate, but importing them gives a pile of parts at one origin.

**Everything is re-expressed in bone space.** Each object is baked into the frame
of the bone it hangs off, using the same tail-origin convention as the rest of
this project (`bone_matrix` = armature * bone.matrix_local * translate(0, len, 0)).
Bone space already runs +Y along the chain, which is this library's convention,
so the assembly can place a vertebra straight onto a bone with no correction —
and the ostrich's own half-a-bone-back offset comes along for free, which is what
makes consecutive vertebrae overlap instead of butting.

**Materials.** The ostrich carries twelve of its own local materials
(`NECK_Hull`, `NECK_Brass`, ...). The crawler is entirely palette, and a model
that brings its own materials is what `PALETTE.md` exists to prevent, so every
slot is remapped onto the nearest palette entry. The look shifts toward the
crawler's bleached-and-rusted family, which is the intent.

Four vertebra variations are taken from different stations up the ostrich's neck
so they genuinely differ in proportion, and two of them carry the sensor pod.

    blender --background --python neck_column.py -- --out neck_column.blend
"""

import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import collection, link_materials, parse_out, report, save, start  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
OSTRICH = os.path.join(REPO, "Assets", "Models", "Environment", "creatures",
                       "robots", "Ostrich", "ostrich_neck.blend")

MATS = [
    "Mat_Paint_Hull_Bleached",
    "Mat_Paint_Roof_Green",
    "Mat_Metal_Steel_Dark",
    "Mat_Metal_Steel_Worn",
    "Mat_Metal_Copper_Oxide",
    "Mat_Paint_Warn_Red",
    "Mat_Paint_Olive_Deep",
    "Mat_Glass_Canopy_Tinted",
    "Mat_Plastic_Rubber_Black",
]

REMAP = {
    "NECK_Hull":      "Mat_Paint_Hull_Bleached",
    "NECK_Shell":     "Mat_Paint_Roof_Green",
    "NECK_DarkMetal": "Mat_Metal_Steel_Dark",
    "NECK_Steel":     "Mat_Metal_Steel_Worn",
    "NECK_Brass":     "Mat_Metal_Copper_Oxide",
    "NECK_Accent":    "Mat_Paint_Warn_Red",
    "NECK_Servo":     "Mat_Paint_Olive_Deep",
    "NECK_Lens":      "Mat_Glass_Canopy_Tinted",
    "NECK_Cable":     "Mat_Plastic_Rubber_Black",
    "NECK_Wire":      "Mat_Metal_Copper_Oxide",
    "NECK_Muscle":    "Mat_Plastic_Rubber_Black",
    "NECK_Elastic":   "Mat_Plastic_Rubber_Black",
}

# source object -> (new name, collection)
VERTEBRAE = [
    ("NECK_Vert_01", "Mesh_NeckVert_Sensor", "Coll_NeckColumn_VertSensor"),
    ("NECK_Vert_03", "Mesh_NeckVert_Large",  "Coll_NeckColumn_VertLarge"),
    ("NECK_Vert_06", "Mesh_NeckVert_Mid",    "Coll_NeckColumn_VertMid"),
    ("NECK_Vert_09", "Mesh_NeckVert_Slim",   "Coll_NeckColumn_VertSlim"),
]
HEAD = [
    ("NECK_HEAD_Cranium",   "Mesh_NeckHead_Cranium"),
    ("NECK_HEAD_BeakUpper", "Mesh_NeckHead_Beak"),
    ("NECK_HEAD_EyeL",      "Mesh_NeckHead_EyeL"),
    ("NECK_HEAD_EyeR",      "Mesh_NeckHead_EyeR"),
    ("NECK_HeadPlate",      "Mesh_NeckHead_Plate"),
]
JAW = [("NECK_HEAD_Jaw", "Mesh_NeckHead_Jaw")]
COLLAR = [("NECK_J05_Collar", "Mesh_NeckJointCollar")]

ARM = "NECK_Rig"


def bone_matrix(arm, bone_name):
    """The frame a BONE-parented child hangs in: origin on the bone's tail,
    +Y running head-to-tail. Same convention as desert_crawler.py."""
    bone = arm.data.bones[bone_name]
    return (arm.matrix_world @ bone.matrix_local
            @ Matrix.Translation(Vector((0.0, bone.length, 0.0))))


def build():
    out = parse_out()
    scene = start(out)
    link_materials(MATS)

    if not os.path.exists(OSTRICH):
        raise SystemExit("Ostrich neck not found: %s" % OSTRICH)

    wanted = [s for s, _, _ in VERTEBRAE] + [s for s, _ in HEAD + JAW + COLLAR]
    with bpy.data.libraries.load(OSTRICH, link=False) as (src, dst):
        missing = [n for n in wanted + [ARM] if n not in set(src.objects)]
        if missing:
            raise SystemExit("ostrich_neck.blend has no %s" % missing)
        dst.objects = wanted + [ARM]

    arm = bpy.data.objects[ARM]
    scene.collection.objects.link(arm)
    bpy.context.view_layer.update()

    def bring(src_name, new_name):
        obj = bpy.data.objects[src_name]
        scene.collection.objects.link(obj)
        if obj.parent_type != 'BONE' or not obj.parent_bone:
            raise SystemExit("%s is not bone-parented; cannot derive its frame"
                             % src_name)
        # world = bone_matrix @ parent_inverse @ basis, so the transform
        # relative to the bone's frame is just the last two — no depsgraph in
        # it. Reading matrix_world here instead returns a stale value, because
        # the object was linked into the scene moments ago and the view layer
        # has not re-evaluated; that lands the skull about ten metres adrift.
        rel = obj.matrix_parent_inverse @ obj.matrix_basis
        obj.parent = None
        obj.matrix_basis = Matrix.Identity(4)
        obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.data.transform(rel)
        for slot in obj.material_slots:
            if slot.material is None:
                continue
            target = REMAP.get(slot.material.name)
            if target is None:
                raise SystemExit("No palette mapping for %r on %s"
                                 % (slot.material.name, src_name))
            slot.material = bpy.data.materials[target]
        obj.name = new_name
        obj.data.name = new_name
        lo = Vector([min(c[i] for c in obj.bound_box) for i in range(3)])
        hi = Vector([max(c[i] for c in obj.bound_box) for i in range(3)])
        print("  %-24s from %-20s span=(%.3f, %.3f, %.3f)  y=%.3f..%.3f"
              % (new_name, src_name, *(hi - lo), lo.y, hi.y))
        return obj

    for src, new, coll_name in VERTEBRAE:
        collection(coll_name).objects.link(bring(src, new))
    collar = bring(*COLLAR[0])
    collection("Coll_NeckColumn_Joint").objects.link(collar)

    head_coll = collection("Coll_NeckColumn_Head")
    for src, new in HEAD:
        head_coll.objects.link(bring(src, new))
    jaw_coll = collection("Coll_NeckColumn_Jaw")
    for src, new in JAW:
        jaw_coll.objects.link(bring(src, new))

    # The armature was scaffolding for deriving frames; the meshes are baked.
    data = arm.data
    bpy.data.objects.remove(arm, do_unlink=True)
    bpy.data.armatures.remove(data)
    for obj in list(scene.collection.objects):
        scene.collection.objects.unlink(obj)

    for mat in list(bpy.data.materials):
        if mat.name.startswith("NECK_") and mat.users == 0:
            bpy.data.materials.remove(mat)

    leftover = [m.name for m in bpy.data.materials if m.library is None]
    if leftover:
        raise SystemExit("Non-palette materials survived: %s" % leftover)

    print("\nNeck column parts:")
    report()
    save(out)


build()
