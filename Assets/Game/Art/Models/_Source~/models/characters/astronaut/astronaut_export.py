"""Export astronaut.blend to astronaut_tobb.fbx, the PlayerCharacter body.

Meant to be re-run -- it is an export, not a generator, and it never writes to
the .blend it opens.

This model does NOT come from the library generators. It is a Mixamo-rigged
character that was hand-edited in Blender, so it does not use `_exportlib.export`:
that helper passes `add_leaf_bones=False`, which is right for the generated
walkers and wrong here. Three things are load-bearing:

  * **Leaf bones stay OFF**, even though the shipped FBX visibly carries `_end`
    and `_end_end` chain tips. Those tips are already REAL bones in the .blend --
    baked in by an earlier round-trip through this same exporter -- so they
    export on their own. Turning `add_leaf_bones` on appends a further generation
    (`_end_end_end`) and takes the skeleton from 91 bones to 104. That is the
    silent kind of break: `astronaut_tobb.fbx` imports as Humanoid with
    `avatarSetup: 1`, generating its OWN avatar from this exact skeleton, which
    `PlayerCharacter.prefab` then binds by the FBX's GUID. The model would still
    import, but the bone count shifts underneath the avatar and the Humanoid
    mapping degrades. Verified by re-importing both FBXs and diffing bone names.
  * **The armature is kept.** It is a skinned character; there is nothing without
    the rig.
  * **Default axis conversion** (`-Z` forward, `Y` up) and `FBX_SCALE_NONE`,
    matching how everything else in the project was exported. The armature sits
    at scale 0.01 in the .blend and the importer runs with `useFileScale: 1`, so
    the arriving scale must not be renegotiated here.

Do not point this at `AstronautArmature.fbx`. That is the separate animation-clip
rig referenced by the scenes and the ~20 Mixamo clips under
`Assets/Game/Art/Animations/Player/`; this script only owns the player body mesh.

    blender --background --python astronaut_export.py
"""

import os

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
# Walk up to the Unity project root rather than counting parent directories, so
# this survives the library being moved (it already moved once, into Assets/).
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)

SRC = os.path.join(HERE, "astronaut.blend")
DST = os.path.join(REPO, "Assets", "Game", "Art", "Models", "Characters",
                   "Astronaut", "astronaut_tobb.fbx")

# The Humanoid avatar is generated from this skeleton, so a change in the bone
# count is a change to the avatar. Checked rather than assumed.
EXPECTED_BONES = 91


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s" % SRC)

    bpy.ops.wm.open_mainfile(filepath=SRC)

    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    bones = sum(len(a.data.bones) for a in arms)

    print("Exporting %d meshes and %d bone(s) across %d armature(s) -> %s"
          % (len(meshes), bones, len(arms), DST))

    if not arms:
        raise SystemExit("No armature in %s -- refusing to export a Humanoid "
                         "character with no rig." % SRC)

    # The avatar is generated from this skeleton, so a changed bone count is a
    # changed avatar. Fail loudly here rather than let Unity regenerate a subtly
    # different Humanoid mapping on import.
    if bones != EXPECTED_BONES:
        raise SystemExit(
            "Expected %d bones, found %d. If the rig genuinely changed, update "
            "EXPECTED_BONES and re-check the Humanoid avatar and the Mixamo "
            "clips in Unity." % (EXPECTED_BONES, bones))

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
