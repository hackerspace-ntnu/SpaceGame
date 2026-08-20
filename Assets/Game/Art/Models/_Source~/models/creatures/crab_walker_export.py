"""Export the crab walker variants to the FBXs Unity consumes.

Like `desert_crawler_export.py` this is meant to be re-run — it is an export,
not a generator, and it never writes to the .blend files it opens.

    blender --background --python crab_walker_export.py            # all three
    blender --background --python crab_walker_export.py -- --legs 6

**It keeps the armature**, and that is the whole point of these models.
`WalkerRig.Build` walks the LIVE bone hierarchy every time the machine
initialises, looking for `Coxa_/Hip_/Knee_/Ankle_/Foot_` leg chains and `Arm_`
claw roots, and measuring each joint's axle off the `*Pin*` mesh parented to it.
Strip the armature and there is no walker, just a pile of legs.

Three things it does that a plain export would not:

  * **Localises the palette materials.** The models link them from
    `Assets/Game/Art/Models/_Source~/palette.blend`, outside `Assets/`, which would not resolve from a
    copy inside it.
  * **Turns off leaf bones.** Blender otherwise appends a `<bone>_end` child to
    every chain tip, and `WalkerRig` walks an arm's chain by hierarchy — a leaf
    bone arriving ahead of the real joint is a silent wrong answer, not an error.
  * **Exports on the default axis conversion**, so the model's −Y forward
    arrives on Unity's +Z. `CrabWaveGait` orders its wave by `HomeLocal.x` and
    splits its rows by `HomeLocal.z` in Unity space, so this is what makes the
    wave march along the beam rather than down the machine.
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
# Walk up to the Unity project root rather than counting parent directories, so
# this survives the library being moved (it already moved once, into Assets/).
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)
DST_DIR = os.path.join(REPO, "Assets", "Models", "Creatures", "Robotic", "Crab")


def export(legs):
    src = os.path.join(HERE, "crab_walker_%d.blend" % legs)
    dst = os.path.join(DST_DIR, "crab_walker_%d.fbx" % legs)
    if not os.path.exists(src):
        raise SystemExit("No model at %s — run crab_walker.py first." % src)

    bpy.ops.wm.open_mainfile(filepath=src)

    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    bones = sum(len(a.data.bones) for a in arms)
    print("Exporting %d meshes and %d bone(s) across %d armature(s) -> %s"
          % (len(meshes), bones, len(arms), dst))

    os.makedirs(DST_DIR, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=dst,
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
    print("Wrote %s (%.1f MB)" % (dst, os.path.getsize(dst) / 1e6))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    wanted = [int(argv[argv.index("--legs") + 1])] if "--legs" in argv else [4, 6, 8]
    for legs in wanted:
        export(legs)
    # Deliberately no save_mainfile: the .blend files are the source of truth.


main()
