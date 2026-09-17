"""The sky soldier: a long-coated Sky Tribe rifleman, dressed on a copy of the human template.

    blender --background --python models/characters/sky_soldier/sky_soldier.py -- --out models/characters/sky_soldier/sky_soldier.blend

A COPY of `components/organic/human_mannequin.blend`'s Slim build - its armature (the
Nomads' 65-bone Mixamo skeleton) and every body part - stands at the origin in
`Coll_SkySoldier_Body`, to model on or hide. Over it, the apparel components, appended in
the variation this character wears (see sky_soldier_BUILD.md):

    officer_cap     Coll_Cap_FlatTop
    goggle_mask     Coll_Mask_TwinGoggles
    high_collar     Coll_Collar_FaceWrap
    trail_scarf     Coll_Scarf_Streaming
    slab_coat       Coll_Coat_Slab
    shoulder_guard  Coll_Guard_Layered
    limb_armour     Greaves, Cuisses, Sleeves, Vambraces, Boots, Gloves
    belt_kit        Coll_Belt_RadioPack

Every piece is bound to this file's armature the way the body is: an Armature modifier and
one full-weight vertex group, named by the `bind_bone` the component recorded. So the
whole soldier poses and animates from `Arm_SkySoldier`, and each piece stays its own object.

Generation script -- historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, LIB)

import bpy  # noqa: E402

import _buildlib as B  # noqa: E402

MANNEQUIN = os.path.join(LIB, "components", "organic", "human_mannequin.blend")
APPAREL = os.path.join(LIB, "components", "apparel")
BODY_BUILD = "Slim"
BIND = "bind_bone"

WEARS = (
    ("officer_cap", ("Coll_Cap_FlatTop",)),
    ("goggle_mask", ("Coll_Mask_TwinGoggles",)),
    ("high_collar", ("Coll_Collar_FaceWrap",)),
    ("trail_scarf", ("Coll_Scarf_Streaming",)),
    ("slab_coat", ("Coll_Coat_Slab",)),
    ("shoulder_guard", ("Coll_Guard_Layered",)),
    ("limb_armour", ("Coll_Armour_Greaves", "Coll_Armour_Cuisses", "Coll_Armour_Sleeves",
                     "Coll_Armour_Vambraces", "Coll_Armour_Boots", "Coll_Armour_Gloves")),
    ("belt_kit", ("Coll_Belt_RadioPack",)),
)


def append_collections(blend, names):
    with bpy.data.libraries.load(blend, link=False) as (src, dst):
        missing = [n for n in names if n not in set(src.collections)]
        if missing:
            raise SystemExit("Not in %s: %s" % (blend, ", ".join(missing)))
        dst.collections = list(names)
    return [bpy.data.collections[n] for n in names]


def body(root):
    """The Slim body, renamed as this soldier's and moved from its build slot to the origin."""
    source = "Coll_Mannequin_%s" % BODY_BUILD
    (coll,) = append_collections(MANNEQUIN, [source])
    root.children.link(coll)
    coll.name = "Coll_SkySoldier_Body"
    arm = next(o for o in coll.objects if o.type == 'ARMATURE')
    bpy.context.view_layer.update()
    shift = -(arm.matrix_world @ arm.data.bones["mixamorig:Hips"].head_local).x
    for o in coll.objects:
        o.location.x += shift
        o.name = o.name.replace("Mannequin_%s" % BODY_BUILD, "SkySoldier_Body")
        if o.data is not None:
            o.data.name = o.name
    arm.name = arm.data.name = "Arm_SkySoldier"
    bpy.context.view_layer.update()
    return arm


def dress(root, arm):
    apparel = B.collection("Coll_SkySoldier_Apparel", root)
    for component, collections in WEARS:
        for coll in append_collections(os.path.join(APPAREL, component + ".blend"), collections):
            apparel.children.link(coll)
            for o in coll.objects:
                bone = o.get(BIND)
                if bone is None or bone not in arm.data.bones:
                    raise SystemExit("%s has no usable %s (%r)" % (o.name, BIND, bone))
                o.vertex_groups.new(name=bone).add(range(len(o.data.vertices)), 1.0, 'REPLACE')
                mod = o.modifiers.new(name="Armature", type='ARMATURE')
                mod.object = arm


def main():
    out = B.parse_out()
    B.start(out)
    root = B.collection("Coll_SkySoldier")
    arm = body(root)
    dress(root, arm)
    B.save(out)
    B.report()


main()
