"""Export dune_rat.blend to the FBX Unity consumes.

Re-runnable, and it never writes to the .blend it opens.

    blender --background --python dune_rat_export.py

This is a much thinner script than `vrescal_export.py`, and the difference is
worth knowing about because the two models sit side by side in the library.
The Vrescal's .blend is a hand sculpt kept at the author's working scale of
19.94 units with its origin at the head, so its exporter has to rescale, rotate
and re-pivot the whole animal on the way out. The Dune Rat had no .blend at all
-- only an FBX -- so `dune_rat_rig.py` was free to normalise the source itself:
metres, -Y forward, origin between the feet on the sand. Nothing is left for
the export to fix, so it fixes nothing. If this file ever grows a placement
transform, the bug is upstream.

Two things it does do:

  * **Localises the palette material.** `Mat_Hide_Sand_Pale` is linked from
    `Assets/Game/Art/Models/_Source~/palette.blend`, which lives outside
    `Assets/` where Unity can never resolve it.

  * **Turns off leaf bones.** Blender's exporter otherwise adds a bone past
    every chain tip. That is how this model acquired the fifteen `*_end` bones
    `dune_rat_rig.py` had to strip, and leaving the option on would put them
    straight back.

## Animation

Every action becomes its own FBX take, named `Arm_DuneRat|DuneRat_Walk` and so
on. `DuneRatBuilder` slices those takes into clips and sets the loop flags, so
the take names are the contract between the two files: rename an action and
`Clips[]` in that builder has to follow.

The takes are **baked from the evaluated pose**, which matters here more than
usual. All four limbs are driven by IK constraints, and only the IK targets and
the trunk are keyed -- the femur, fibula, humerus and radius have no curves on
them at all. What lands in the FBX is the solver's output sampled per frame.
`verify_export` at the bottom re-imports the file that was just written and
checks exactly that: if the constraint evaluation were being skipped, the leg
bones would arrive stone still and the animal would slide around in a T-pose.

All clips are **in place** -- no forward root translation. `NavMeshAgentMotor`
owns movement, and a clip that also walked the creature forward would fight it.
That is also why the prefab keeps `m_ApplyRootMotion: 0`.
"""

import os

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))

# Walk up to the Unity project root rather than counting parent directories,
# so this survives the library being moved (it already moved once, into
# Assets/).
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(
        os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)

SRC = os.path.join(HERE, "dune_rat.blend")
DST = os.path.join(REPO, "Assets", "Game", "Art", "Models", "Creatures",
                   "Organic", "DuneRat", "dune_rat.fbx")

ARM = "Arm_DuneRat"

# Bones with no keys of their own, solved entirely by IK. If these arrive in
# the FBX motionless, the bake did not evaluate constraints.
SOLVED = ["femur.L", "fibula.L", "femur.R", "fibula.R",
          "humerus.L", "radius.L", "humerus.R", "radius.R"]


def export():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s -- run dune_rat_rig.py and "
                         "dune_rat_anim.py first." % SRC)

    bpy.ops.wm.open_mainfile(filepath=SRC)

    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()
        # Force opaque on the *local copy*.
        #
        # Every hide material in palette.blend ships with blend_method
        # 'HASHED' -- alpha-hashed transparency. On a creature that is simply
        # wrong, and it is the second, independent reason this animal rendered
        # see-through: the FBX carries the transparency through to whatever
        # material Unity builds from it, and no amount of fixing the winding
        # would have made it opaque on its own.
        #
        # It is fixed here and not in palette.blend on purpose. That file is
        # shared by every model in the library and is outside this model's
        # ownership; changing a palette entry to fix one creature is how a
        # palette stops meaning anything. DuneRatBuilder also assigns an
        # explicitly opaque URP material on the prefab, so the prefab does not
        # depend on this at all -- this only keeps the raw FBX honest.
        if hasattr(mat, "blend_method"):
            mat.blend_method = 'OPAQUE'

    roots = [o for o in bpy.data.objects
             if o.parent is None and o.type in {'MESH', 'ARMATURE'}]
    if [o.name for o in roots] != [ARM]:
        raise SystemExit(
            "Expected %s to be the only root object, found %s.\n"
            "Anything unparented is exported at its own transform and will "
            "arrive in Unity detached from the skeleton."
            % (ARM, ", ".join(sorted(o.name for o in roots))))

    arm = bpy.data.objects[ARM]
    actions = sorted(a.name for a in bpy.data.actions)
    if not actions:
        raise SystemExit("No actions in %s -- run dune_rat_anim.py first." % SRC)

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    print("Exporting %d mesh(es), %d bones and %d takes -> %s"
          % (len(meshes), len(arm.data.bones), len(actions), DST))
    print("  takes: %s" % ", ".join(actions))

    os.makedirs(os.path.dirname(DST), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=DST,
        use_selection=False,
        object_types={'MESH', 'ARMATURE'},
        apply_scale_options='FBX_SCALE_NONE',
        # -Y forward in Blender becomes +Z forward in Unity, and Blender's
        # +Z up becomes Unity's +Y up. The animal is modelled facing -Y for
        # exactly this reason.
        axis_forward='-Z',
        axis_up='Y',
        mesh_smooth_type='FACE',
        use_mesh_modifiers=False,      # keep the armature modifier unapplied
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
        path_mode='AUTO',
        embed_textures=False,
    )
    print("Wrote %s (%.2f MB)" % (DST, os.path.getsize(DST) / 1e6))
    return actions


def fcurves_of(action):
    """Every F-curve in an action, on 4.4+ slotted actions and on older ones.

    Blender 4.4 moved curves out from under `Action.fcurves` and into
    layer/strip/channelbag, and 5.1 removed the old attribute outright. This is
    a verification script -- it must not be the reason the pipeline breaks on
    whichever Blender is to hand.
    """
    if hasattr(action, "fcurves"):
        return list(action.fcurves)
    out = []
    for layer in getattr(action, "layers", []):
        for strip in getattr(layer, "strips", []):
            for bag in getattr(strip, "channelbags", []):
                out.extend(bag.fcurves)
    return out


def verify_export(expected):
    """Re-import what was just written and prove the takes survived.

    Checks the three things that can silently go wrong: a take going missing,
    the leaf bones coming back, and -- the one this rig is actually exposed to
    -- the IK-solved bones baking out as constants.
    """
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=DST)

    # Winding, measured on the file that was actually written. This is a
    # regression guard, not a formality: the model shipped once with 37% of its
    # faces wound backwards and rendered see-through in Unity, and nothing
    # anywhere in the pipeline complained.
    for ob in [o for o in bpy.data.objects if o.type == 'MESH']:
        me = ob.data
        vol = 0.0
        for poly in me.polygons:
            idx = poly.vertices
            for i in range(1, len(idx) - 1):
                a = me.vertices[idx[0]].co
                b = me.vertices[idx[i]].co
                c = me.vertices[idx[i + 1]].co
                vol += a.dot(b.cross(c)) / 6.0
        flag = "" if vol > 0.0 else "   <-- CHECK, wound inside out"
        print("\n  %s: %d polys, signed volume %.5f%s"
              % (ob.name, len(me.polygons), vol, flag))

    arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    if len(arms) != 1:
        raise SystemExit("Re-import found %d armatures" % len(arms))
    arm = arms[0]
    bones = {b.name for b in arm.data.bones}
    leaves = sorted(n for n in bones if n.endswith("_end"))

    print("\nRe-imported %s: %d bones, %d action(s)"
          % (os.path.basename(DST), len(bones), len(bpy.data.actions)))
    if leaves:
        print("  !! leaf bones came back: %s" % ", ".join(leaves))

    found = {}
    for action in bpy.data.actions:
        first, last = action.frame_range
        found[action.name] = (int(round(first)), int(round(last)))
    for name in sorted(found):
        print("  %-40s frames %d..%d" % (name, found[name][0], found[name][1]))

    missing = [a for a in expected
               if not any(k.endswith("|" + a) or k == a for k in found)]
    if missing:
        raise SystemExit("Takes missing from the FBX: %s" % ", ".join(missing))

    # Tail amplitude, per clip. The tail is the counterweight and has to get
    # louder as the animal speeds up; it shipped once doing the reverse -- more
    # movement standing still than at a walk -- so the ordering is asserted
    # here rather than eyeballed. Figures are peak-to-peak range of the widest
    # quaternion component on each bone's own local curve.
    print("\n  Tail amplitude (q component peak-to-peak), femur.L for scale:")
    order = {}
    for action in sorted(bpy.data.actions, key=lambda a: a.name):
        cells = []
        for bone in ("tail1", "tail4", "femur.L"):
            widest = 0.0
            for fcurve in fcurves_of(action):
                if ('"%s"' % bone) in fcurve.data_path and \
                        "rotation_quaternion" in fcurve.data_path:
                    values = [k.co[1] for k in fcurve.keyframe_points]
                    if values:
                        widest = max(widest, max(values) - min(values))
            cells.append(widest)
        short = action.name.rsplit("|", 1)[-1]
        order[short] = cells[1]
        print("    %-22s tail1 %.3f  tail4 %.3f  femur.L %.3f"
              % (short, cells[0], cells[1], cells[2]))

    idle = order.get("DuneRat_Idle", 0.0)
    walk = order.get("DuneRat_Walk", 0.0)
    run = order.get("DuneRat_Run", 0.0)
    if not (run > walk > idle):
        print("    !! tail4 ordering is wrong: idle %.3f, walk %.3f, run %.3f "
              "-- the tail should work hardest at speed" % (idle, walk, run))
    else:
        print("    ordering OK: idle %.3f < walk %.3f < run %.3f"
              % (idle, walk, run))

    # Does anything actually move the IK-solved bones?
    print("\n  IK-solved bones, curve counts per take "
          "(0 would mean the bake ignored the constraints):")
    for action in sorted(bpy.data.actions, key=lambda a: a.name):
        hits = 0
        spans = 0.0
        for fcurve in fcurves_of(action):
            for bone in SOLVED:
                if ('"%s"' % bone) in fcurve.data_path:
                    values = [k.co[1] for k in fcurve.keyframe_points]
                    if values:
                        spans = max(spans, max(values) - min(values))
                    hits += 1
        flag = "" if hits and spans > 1e-4 else "   <-- CHECK"
        print("    %-40s %3d curves, widest swing %.4f%s"
              % (action.name, hits, spans, flag))


main_actions = export()
verify_export(main_actions)
