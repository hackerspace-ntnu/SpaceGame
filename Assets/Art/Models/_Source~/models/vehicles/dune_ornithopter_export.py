"""Export dune_ornithopter.blend to the FBX Unity consumes.

Like `desert_crawler_export.py` this is meant to be re-run — it is an export,
not a generator, and it never writes to the .blend it opens.

**It keeps the armature**, for the same reason the crawler does: nothing in
Unity rebuilds this rig, `OrnithopterWingRig` walks the live bone hierarchy at
Initialise and `OrnithopterWingAnimator` poses it every frame. Strip the
armature and there are no wings, just a fuselage with some cloth stuck to it.

Four things it does that a plain export would not:

  * **Localises the palette materials.** The model links them from
    `Assets/Art/Models/_Source~/palette.blend`, outside `Assets/`, which would not resolve from a
    copy inside it.

  * **Turns off leaf bones.** Blender otherwise appends a `<bone>_end` child to
    every chain tip. `OrnithopterWingRig` resolves bones by exact name so a
    leaf bone would not confuse it, but ten digits plus five tail digits plus
    the nose and crank tips is 20-odd dead transforms riding in every prefab.

  * **Exports on the default axis conversion**, so the model's −Y forward
    arrives on Unity's +Z. There is deliberately no yaw correction anywhere
    downstream; `OrnithopterBuilder` and the flight motor both assume the nose
    points along +Z.

  * **Checks the six webbed panels are still skinned.** This is the one that
    matters. Both wings and the tail fan — frame and web each — are parented to
    the armature with an Armature modifier and vertex weights, unlike every
    other part, which is bone-parented. If they arrive in Unity as plain
    MeshRenderers the wings will never deform, the flapping will move the
    shoulders and leave the cloth behind, and nothing further up the stack will
    report an error. Cheaper to assert here than to debug there.

    blender --background --python dune_ornithopter_export.py
"""

import os

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
# Walk up to the Unity project root rather than counting parent directories, so
# this survives the library being moved (it already moved once, into Assets/).
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)
SRC = os.path.join(HERE, "dune_ornithopter.blend")
DST = os.path.join(REPO, "Assets", "Models", "Vehicles", "Ornithopter",
                   "dune_ornithopter.fbx")

# Frame and web of both wings and the tail fan. Bone-parenting any of these
# would be a silent regression, so they are named rather than counted.
SKINNED = [
    "Mesh_Wing_L_Frame", "Mesh_Wing_L_Web",
    "Mesh_Wing_R_Frame", "Mesh_Wing_R_Web",
    "Mesh_TailFan_Frame", "Mesh_TailFan_Web",
]


def check_skinning():
    """Every panel must have an Armature modifier and real vertex groups."""
    for name in SKINNED:
        obj = bpy.data.objects.get(name)
        if obj is None:
            raise SystemExit("Missing skinned panel %s" % name)
        mods = [m for m in obj.modifiers if m.type == 'ARMATURE']
        if not mods:
            raise SystemExit(
                "%s has no Armature modifier — it would export as a rigid "
                "mesh and the wing would never deform." % name)
        if not obj.vertex_groups:
            raise SystemExit("%s has no vertex groups — nothing to weight to."
                             % name)
        if mods[0].object is None:
            raise SystemExit("%s's Armature modifier has no armature set."
                             % name)
    print("  %d webbed panel(s) skinned and weighted" % len(SKINNED))


def check_object_mode():
    """An object left in Edit Mode exports its stale base mesh, not its edits.

    The .blend has been hand-edited and was once saved mid-edit; see
    dune_ornithopter_rescale.py. Leaving edit mode flushes the edit mesh down
    into the datablock, which is what the exporter reads.
    """
    for obj in bpy.data.objects:
        if obj.mode != 'OBJECT':
            print("  %s was in %s mode — flushing" % (obj.name, obj.mode))
            bpy.context.view_layer.objects.active = obj
            bpy.ops.object.mode_set(mode='OBJECT')


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s" % SRC)

    bpy.ops.wm.open_mainfile(filepath=SRC)
    check_object_mode()

    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    check_skinning()

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    bones = sum(len(a.data.bones) for a in arms)
    print("Exporting %d meshes and %d bone(s) across %d armature(s) -> %s"
          % (len(meshes), bones, len(arms), DST))

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
        bake_anim=False,
        armature_nodetype='NULL',
        bake_space_transform=False,
        path_mode='COPY',
        embed_textures=False,
    )
    print("Wrote %s (%.1f MB)" % (DST, os.path.getsize(DST) / 1e6))
    # Deliberately no save_mainfile: the .blend is the source of truth.


main()
