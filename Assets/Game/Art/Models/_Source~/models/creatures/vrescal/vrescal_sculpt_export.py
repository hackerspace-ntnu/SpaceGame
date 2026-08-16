"""Export the rigged sculpt to the FBX Unity's VrescalBuilder reads.

    blender --background --python vrescal_sculpt_export.py

Writes `Assets/Game/Art/Models/Creatures/Organic/Vrescal/vrescal.fbx`, replacing
the previous build's export. Deliberately no `save_mainfile`: the .blend is the
source of truth and this only ever reads it.

Same shape as the older `vrescal_export.py`, with two differences worth stating.

**The scale factor is declared, not derived.** The old script computed it as
`SHIP_LENGTH / SCULPT_LENGTH` from two hand-measured numbers, because its sculpt
sat in an arbitrary space. This rig was *built* at exactly `UNITS_PER_M`, so the
factor is simply its reciprocal and the shipped length is a printed consequence
rather than an input that can silently disagree with the file.

**The pivot is imported from the rig.** `vrescal_export.PIVOT` and
`vrescal_rebuild.PIVOT_X` are the same number written twice, and its own comment
warns that the two disagreeing puts the root bone off Unity's origin. Importing
it removes the possibility.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import vrescal_rig as RIG          # noqa: E402

REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(
        os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)

# Overridable, so the low-poly rig can ship through this same verified export
# path rather than a copy of it. Unset, the defaults are unchanged.
SRC = os.environ.get("VRESCAL_EXPORT_SRC",
                     os.path.join(HERE, "vrescal_rigged.blend"))
DST = os.environ.get("VRESCAL_EXPORT_DST",
                     os.path.join(REPO, "Assets", "Game", "Art", "Models",
                                  "Creatures", "Organic", "Vrescal",
                                  "vrescal.fbx"))

FACTOR = 1.0 / RIG.UNITS_PER_M
PIVOT = (RIG.PIVOT_X_M * RIG.UNITS_PER_M, 0.0, RIG.GROUND)

EXPECTED_TAKES = ["Vrescal_Attack", "Vrescal_Death", "Vrescal_Hurt",
                  "Vrescal_Idle", "Vrescal_Run", "Vrescal_Walk"]


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No rig at %s -- run vrescal_rig.py first." % SRC)
    bpy.ops.wm.open_mainfile(filepath=SRC)

    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    roots = [o for o in bpy.data.objects
             if o.parent is None and o.type in {'MESH', 'ARMATURE'}]
    if [o.name for o in roots] != ["Arm_Vrescal"]:
        raise SystemExit(
            "Expected Arm_Vrescal to be the only root object, found %s.\n"
            "Anything still unparented would be left behind at sculpt scale "
            "and orientation." % ", ".join(sorted(o.name for o in roots)))

    actions = sorted(a.name for a in bpy.data.actions)
    if actions != EXPECTED_TAKES:
        raise SystemExit(
            "Actions do not match what VrescalBuilder expects.\n"
            "  found:  %s\n  wanted: %s\nRun vrescal_sculpt_anim.py."
            % (", ".join(actions), ", ".join(EXPECTED_TAKES)))

    # Subdivision is a modelling aid here, not part of the shipped asset. The
    # export applies modifiers, so a hand-added Subdivision silently multiplies
    # the shipped triangle count -- level 1 turns 6 010 triangles into 36 060,
    # undoing the whole point of the low-poly cut. It is therefore disabled for
    # the export unless explicitly asked for, and always reported either way.
    # Every earlier build had no Subdivision at all, so this is a no-op there.
    ship_subsurf = os.environ.get("VRESCAL_EXPORT_SUBSURF") == "1"
    for ob in bpy.data.objects:
        for m in [m for m in getattr(ob, "modifiers", []) if m.type == 'SUBSURF']:
            tri = sum(len(p.vertices) - 2 for p in ob.data.polygons)
            if ship_subsurf:
                print("  SHIPPING Subdivision level %d on %s (cage %d tris -> "
                      "roughly %d)" % (m.levels, ob.name, tri, tri * 3 ** m.levels))
            else:
                m.show_render = m.show_viewport = False
                print("  Subdivision level %d on %s disabled for export "
                      "(cage stays %d tris; set VRESCAL_EXPORT_SUBSURF=1 to ship it)"
                      % (m.levels, ob.name, tri))

    # world' = scale . yaw . (world - PIVOT), applied to the one root. Right to
    # left: slide PIVOT onto the origin, turn +X forward into -Y forward, then
    # shrink to shipping size.
    place = (Matrix.Diagonal((FACTOR, FACTOR, FACTOR, 1.0))
             @ Matrix.Rotation(math.radians(-90.0), 4, 'Z')
             @ Matrix.Translation(-Vector(PIVOT)))
    roots[0].matrix_world = place @ roots[0].matrix_world
    bpy.context.view_layer.update()

    mesh = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    pts = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts),
                 min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts),
                 max(p.z for p in pts)))
    bones = sum(len(a.data.bones)
                for a in bpy.data.objects if a.type == 'ARMATURE')
    print("  factor  %.5f (1 / %.4f)" % (FACTOR, RIG.UNITS_PER_M))
    print("  pivot   (%.3f, %.3f, %.3f) in working units" % PIVOT)
    print("  shipped %.2f x %.2f x %.2f m   (x %.2f..%.2f, z %.2f..%.2f)"
          % (hi.x - lo.x, hi.y - lo.y, hi.z - lo.z, lo.x, hi.x, lo.z, hi.z))
    print("  %d bones, %d takes: %s" % (bones, len(actions), ", ".join(actions)))

    os.makedirs(os.path.dirname(DST), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=DST,
        use_selection=False,
        object_types={'MESH', 'ARMATURE'},
        apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z',
        axis_up='Y',
        mesh_smooth_type='FACE',
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        armature_nodetype='NULL',
        bake_space_transform=False,
        path_mode='COPY',
        embed_textures=False,
    )
    print("  wrote %s (%.2f MB)" % (DST, os.path.getsize(DST) / 1e6))


main()
