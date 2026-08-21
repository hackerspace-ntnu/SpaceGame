"""Export nomad.blend to nomad.fbx, the Nomad NPC body.

Meant to be re-run -- it is an export, not a generator, and it never writes to
the .blend it opens.

    blender --background --python nomad_export.py

WHY THIS IS NOT A PLAIN "EXPORT EVERYTHING"
-------------------------------------------
`nomad.blend` is a hand-edited Mixamo file that accumulated **30 copies of the
same 65-bone skeleton**. Exporting the file wholesale produces 30 armatures and
Unity cannot build a Humanoid avatar from that. Three traps decide the recipe:

  * **The live character is found by POSITION, never by parenting.** Meshes that
    render on the body at x~7 are parented to armature copies sitting at x=0
    (`Vest`, `Harness`, `Kneepads` are the notable ones), while other body parts
    hang off `Armature.001`, `.012`, `.014`, `.021`, `.022`, `.023`. The only
    reliable definition of "part of the live nomad" is a world bounding-box
    centre with x in [5, 9].

  * **Binding a mesh to a distant armature copy is a latent explosion.** A mesh
    whose vertices sit at x~7 but whose bones sit at x=0 is 700+ armature-space
    units away from its own skeleton. It looks correct in the rest pose -- the
    armature modifier is identity there -- and flies apart the moment a clip
    plays. Rebinding every kept mesh to `Armature.001` (which shares the live
    body's world matrix) is what fixes this, so the rebind is load-bearing, not
    tidying.

  * **Meshes with no vertex groups do not follow the rig.** The head dome, the
    boolean spheres and a pile of small props carry no weights at all. They get
    a full-weight group on the nearest bone so they ride the skeleton instead of
    hanging in the air where the bind pose left them.

`Helmet.001` is excluded on purpose. It is parked 1.4 m above the head (z 4.24
to 4.87, head top ~2.9) and reads in-game as a white orb floating over the
nomad. It was in the previous export; it should not have been.

The robot helmet the nomad now WEARS is a different thing: nine `Helmet_*`
panels fitted onto the head by hand. They arrive unweighted and are anchored as
one rigid unit -- see HELMET_PREFIX below.

Objects are selected rather than deleted, and the export runs with
`use_selection=True`. That matters: several kept meshes carry SHRINKWRAP
modifiers whose targets are meshes we do NOT export, and deleting a shrinkwrap
target silently changes the geometry that gets written.

Export flags mirror `../astronaut/astronaut_export.py` -- in particular leaf
bones stay OFF, because the `_end` chain tips are already real bones in this
file and turning the option on appends another generation and shifts the bone
count out from under the generated avatar.
"""

import os

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
# Walk up to the Unity project root rather than counting parent directories, so
# this survives the library being moved (it already moved once, into Assets/).
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)

SRC = os.path.join(HERE, "nomad.blend")
DST = os.path.join(REPO, "Assets", "Game", "Art", "Models", "Characters",
                   "Nomad", "nomad.fbx")

# The armature copy that shares the live body's world matrix. Everything kept is
# rebound to this one.
TARGET_ARMATURE = "Armature.001"

# World-space bounding-box centre window that defines "part of the live nomad".
# The duplicate character copies sit at x~0 and x~2.2, with strays flung out to
# x=222 and x=-65.
LIVE_X_MIN, LIVE_X_MAX = 5.0, 9.0

# Anything sitting entirely above this is parked scenery, not worn. Head top is
# ~2.9; the discarded helmet starts at 4.24.
PARKED_Z = 3.2

# The Humanoid avatar is generated from this skeleton, so a change in the bone
# count is a change to the avatar. Checked rather than assumed.
EXPECTED_BONES = 65

# An unweighted mesh taller than this is a hanging garment rather than a prop,
# and anchors at the collar instead of at whatever bone is nearest its middle.
GARMENT_SPAN_Z = 1.2
CAPE_ANCHOR_BONE = "mixamorig:Spine2"

# A cape is graded down this chain by height: collar on the chest, hem on the hips.
#
# Full-weighting the whole cloak to Spine2 -- the recipe used until 17 Aug 2026 -- makes it a
# rigid slab pivoting at the chest. Because the cloth hangs ~1.4 m below that pivot, any forward
# lean of the spine throws the hem a long way forward, and in Unity the cape swung round and
# covered the character's entire front, hiding the tabard and chest plate. Grading the weights
# keeps the collar on the shoulders while the body of the cloth follows the pelvis, which is how
# a cloak actually hangs and removes almost all of the swing.
CAPE_CHAIN = [
    "mixamorig:Spine2",
    "mixamorig:Spine1",
    "mixamorig:Spine",
    "mixamorig:Hips",
]

# The cape. It arrived as unparented Blender cloth-sim planes and picked up plausible-looking
# vertex groups along the way, but those weights are inherited from whatever mesh they were
# duplicated from -- driving them produces folded slivers. The documented recipe is one
# full-weight group at the collar, with the ClothWind shader supplying all the motion.
#
# Renamed from Plane/Plane.008 by nomad_fix_rig.py; kept in step with ClothMeshNames in
# NomadPrefabBuilder.cs. The old list also carried Plane.007/.009/.010 and Scarf Remeshed.001 --
# rendering each in isolation showed those are a grey shoulder pad, two thigh pads and the neck
# scarf, none of which are cape, so they are ordinary skinned geometry now.
CLOTH_MESHES = {
    "Cloth_Cape_01", "Cloth_Cape_02",
}

# The robot helmet, fitted to the head by hand and split into colourable panels by
# nomad_split_helmet.py. Nine objects that are ONE rigid object in the fiction, so they must all
# ride ONE bone. Left to `ensure_weights` they do not: nearest-bone puts the shell, brow, lens and
# top rail on `HeadTop_End`, the face, jaw, ear pods and neck rim on `Head`, and the chin studs on
# `Neck` -- so the moment the head turns, the studs stay behind and the helmet comes apart at the
# chin. `Head` is the bone the helmet is fitted around, and every panel is within 0.44 of it.
HELMET_PREFIX = "Helmet_"
HELMET_ANCHOR_BONE = "mixamorig:Head"

# Meshes inside the live window that are nonetheless not part of the character.
#
# `Robot_Helmet_02` is one of the three loose helmets `nomad_add_helmets.py` extracted. It was
# tried on the head and then superseded by the split panels above, which enclose it completely --
# rendering the head with and without it is the same picture, so all it can contribute in Unity is
# ~1.1k hidden triangles and z-fighting where its shell grazes the inside of Helmet_Shell.
SKIP_MESHES = {
    "Robot_Helmet_02",
}

# How far a mesh may sit from the bones it is actually weighted to before we treat those weights
# as inherited junk. A worn prop rides the bone it sits on; a pouch painted onto spine bones but
# modelled out at the wrist simply stays behind when the arm comes down.
MAX_WEIGHT_DRIFT = 0.4

# Sim-only modifiers. They contribute no geometry but do cost time to evaluate,
# and a COLLISION modifier on an exported mesh is meaningless in Unity.
DROP_MODIFIERS = {"COLLISION", "CLOTH", "SOFT_BODY", "PARTICLE_SYSTEM"}


def world_bbox(obj):
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def is_live(obj):
    """Part of the character standing at x~7, and actually worn rather than parked."""
    lo, hi = world_bbox(obj)
    centre_x = (lo.x + hi.x) / 2.0
    if obj.name in SKIP_MESHES:
        return False
    if not (LIVE_X_MIN <= centre_x <= LIVE_X_MAX):
        return False
    if lo.z > PARKED_Z:
        return False
    return True


def nearest_bone(armature, point):
    """Bone whose midpoint is closest to `point`, both in world space."""
    best, best_d2 = None, None
    for bone in armature.data.bones:
        mid = armature.matrix_world @ ((bone.head_local + bone.tail_local) / 2.0)
        d2 = (mid - point).length_squared
        if best_d2 is None or d2 < best_d2:
            best, best_d2 = bone.name, d2
    return best


def rebind(mesh, armature):
    """Reparent to `armature` keeping the mesh exactly where it renders now.

    Setting `matrix_parent_inverse` to the armature's inverted world matrix
    before assigning `matrix_world` leaves the local basis equal to the old
    world matrix. Skipping it makes Blender fold the armature's 0.01 scale into
    the basis, which is the documented way to collapse these objects to the
    origin.
    """
    keep = mesh.matrix_world.copy()
    mesh.parent = armature
    mesh.matrix_parent_inverse = armature.matrix_world.inverted()
    mesh.matrix_world = keep

    for mod in list(mesh.modifiers):
        if mod.type in DROP_MODIFIERS:
            mesh.modifiers.remove(mod)
        elif mod.type == 'ARMATURE':
            mod.object = armature


def bone_mid(armature, bone):
    return armature.matrix_world @ ((bone.head_local + bone.tail_local) / 2.0)


def weighted_bones(mesh, armature):
    """The armature bones this mesh actually carries weight on."""
    names = set()
    for vert in mesh.data.vertices:
        for entry in vert.groups:
            if entry.weight <= 0.0 or entry.group >= len(mesh.vertex_groups):
                continue
            names.add(mesh.vertex_groups[entry.group].name)

    return [armature.data.bones[n] for n in names if n in armature.data.bones]


def grade_cape(mesh, armature):
    """Weight a cape from collar to hem across CAPE_CHAIN, by vertex height.

    Returns the number of chain bones that actually received weight, or None if the mesh has no
    vertical extent to grade along.
    """
    available = [b for b in CAPE_CHAIN if b in armature.data.bones]
    if not available:
        return None

    world = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
    if not world:
        return None

    top = max(p.z for p in world)
    bottom = min(p.z for p in world)
    if (top - bottom) < 1e-5:
        return None

    for group in list(mesh.vertex_groups):
        mesh.vertex_groups.remove(group)
    groups = [mesh.vertex_groups.new(name=name) for name in available]

    last = len(available) - 1
    for index, point in enumerate(world):
        # 0 at the collar, 1 at the hem.
        t = (top - point.z) / (top - bottom)
        slot = t * last
        lower = min(int(slot), last)
        upper = min(lower + 1, last)
        blend = slot - lower

        if lower == upper:
            groups[lower].add([index], 1.0, 'REPLACE')
        else:
            groups[lower].add([index], 1.0 - blend, 'REPLACE')
            groups[upper].add([index], blend, 'REPLACE')

    if not any(m.type == 'ARMATURE' for m in mesh.modifiers):
        mod = mesh.modifiers.new(name="Armature", type='ARMATURE')
        mod.object = armature

    return len(available)


def anchor_to(mesh, armature, bone_name):
    """Replace every weight on `mesh` with a single full-weight group on `bone_name`."""
    for group in list(mesh.vertex_groups):
        mesh.vertex_groups.remove(group)

    group = mesh.vertex_groups.new(name=bone_name)
    group.add([v.index for v in mesh.data.vertices], 1.0, 'REPLACE')

    if not any(m.type == 'ARMATURE' for m in mesh.modifiers):
        mod = mesh.modifiers.new(name="Armature", type='ARMATURE')
        mod.object = armature


def reanchor_stragglers(mesh, armature):
    """Re-bind a small prop whose weights point at bones nowhere near it.

    Several pouches, pads and armbands were duplicated from other parts of the body and kept
    those parts' vertex groups. In the .blend's spread rest pose nothing looks wrong, because
    the armature contributes no deformation there. Play any clip and they hang in mid-air where
    the limb used to be. Only small rigid pieces qualify -- a garment legitimately spans many
    bones, so it is left alone.
    """
    lo, hi = world_bbox(mesh)
    if (hi - lo).length > 1.2:
        return None

    bones = weighted_bones(mesh, armature)
    if not bones:
        return None

    centre = (lo + hi) / 2.0

    # Compare the CLOSEST bone the mesh is weighted to against the closest bone that exists.
    # An average would be wrong: the boots are one mesh spanning both feet, so their weights
    # average out to a point between the ankles and half a metre above the soles, which reads as
    # drift even though the binding is perfectly good. What actually distinguishes junk weights
    # is that none of them are anywhere near the geometry.
    held = min((bone_mid(armature, b) - centre).length for b in bones)
    best = min((bone_mid(armature, b) - centre).length for b in armature.data.bones)

    if held <= best + MAX_WEIGHT_DRIFT:
        return None

    bone = nearest_bone(armature, centre)
    anchor_to(mesh, armature, bone)
    return bone, held


def ensure_weights(mesh, armature):
    """Give an unweighted mesh a full-weight group on the bone it should ride.

    Without this the mesh is exported as static geometry parented to the root and
    stays behind when the skeleton moves.

    Nearest-bone is right for compact props -- a head dome picks `Head`, a
    shoulder ring picks `LeftShoulder`. It is wrong for the hanging garments: a
    cape sheet runs from collar to shin, so its centre lands nearest `Hips` and
    the collar then tears away from the shoulders as the torso bends. Those
    anchor at `Spine2`, which is where the cape is stitched on and what the
    ClothWind material's collar/hem span is tuned against.
    """
    if mesh.vertex_groups:
        return None

    lo, hi = world_bbox(mesh)
    if (hi.z - lo.z) > GARMENT_SPAN_Z:
        bone = CAPE_ANCHOR_BONE
    else:
        bone = nearest_bone(armature, (lo + hi) / 2.0)

    group = mesh.vertex_groups.new(name=bone)
    group.add([v.index for v in mesh.data.vertices], 1.0, 'REPLACE')

    if not any(m.type == 'ARMATURE' for m in mesh.modifiers):
        mod = mesh.modifiers.new(name="Armature", type='ARMATURE')
        mod.object = armature

    return bone


def recentre(armature):
    """Slide the whole rig so the armature's origin lands on the world origin.

    The live nomad is modelled at x~7.08, beside the duplicate copies. Exported as-is, every
    node in the FBX inherits that offset, so Unity builds a prefab whose visible body stands
    4.5 m to the side of its own transform -- the NavMeshAgent then walks the root around while
    the character trails alongside it.

    Moving the ARMATURE moves its children rigidly, and skinning is untouched because it is
    computed in armature space: the same translation appears in the meshes' world matrices and
    in the armature's, so it cancels.
    """
    delta = armature.matrix_world.translation.copy()
    armature.matrix_world = Matrix.Translation(-delta) @ armature.matrix_world
    bpy.context.view_layer.update()
    return delta


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s" % SRC)

    bpy.ops.wm.open_mainfile(filepath=SRC)

    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    target = bpy.data.objects.get(TARGET_ARMATURE)
    if target is None or target.type != 'ARMATURE':
        raise SystemExit("No armature named %s -- refusing to export a Humanoid "
                         "character with no rig." % TARGET_ARMATURE)

    bones = len(target.data.bones)
    if bones != EXPECTED_BONES:
        raise SystemExit(
            "Expected %d bones on %s, found %d. If the rig genuinely changed, "
            "update EXPECTED_BONES and re-check the Humanoid avatar and the "
            "Mixamo clips in Unity." % (EXPECTED_BONES, TARGET_ARMATURE, bones))

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    live = sorted((o for o in meshes if is_live(o)), key=lambda o: o.name)
    if not live:
        raise SystemExit("No meshes in the live window -- the character moved?")

    weighted = []
    caped = []
    helmeted = []
    reanchored = []
    for mesh in live:
        rebind(mesh, target)

        if mesh.name.startswith(HELMET_PREFIX):
            anchor_to(mesh, target, HELMET_ANCHOR_BONE)
            helmeted.append(mesh.name)
            continue

        if mesh.name in CLOTH_MESHES:
            graded = grade_cape(mesh, target)
            if graded is None:
                # No vertical extent to grade along; fall back to pinning at the collar.
                anchor_to(mesh, target, CAPE_ANCHOR_BONE)
                caped.append((mesh.name, "pinned at " + CAPE_ANCHOR_BONE))
            else:
                caped.append((mesh.name, "graded across %d spine bones" % graded))
            continue

        bone = ensure_weights(mesh, target)
        if bone:
            weighted.append((mesh.name, bone))
            continue

        moved = reanchor_stragglers(mesh, target)
        if moved:
            reanchored.append((mesh.name, moved[0], moved[1]))

    print("Keeping %d of %d meshes; dropped %d outside the live window or parked "
          "above z=%.1f" % (len(live), len(meshes), len(meshes) - len(live), PARKED_Z))
    for name, how in caped:
        print("  cape panel      %-24s -> %s" % (name, how))
    if helmeted:
        print("  helmet panels   %-24s -> %s" % ("%d panels" % len(helmeted), HELMET_ANCHOR_BONE))
    for name, bone in weighted:
        print("  weighted unrigged mesh %-24s -> %s" % (name, bone))
    for name, bone, drift in reanchored:
        print("  re-anchored     %-24s -> %s (nearest weighted bone was %.2f m away)"
              % (name, bone, drift))

    missing_cloth = CLOTH_MESHES - {m.name for m in live}
    for name in sorted(missing_cloth):
        print("  WARNING: cape mesh %s is not in the live set" % name)

    # Last, so the bounding boxes and bone distances above were all measured in the coordinates
    # the .blend actually uses.
    moved = recentre(target)
    print("Recentred the rig from (%.3f, %.3f, %.3f) to the origin"
          % (moved.x, moved.y, moved.z))

    # Selection is set directly rather than through bpy.ops.object.select_all, which needs a
    # window context and dies with "context is incorrect" when the .blend was last saved in pose
    # or edit mode -- which a hand-edited character file frequently is.
    view_layer = bpy.context.view_layer
    view_layer.objects.active = target
    if target.mode != 'OBJECT':
        try:
            bpy.ops.object.mode_set(mode='OBJECT')
        except RuntimeError:
            pass

    for obj in view_layer.objects:
        obj.select_set(False)
    for mesh in live:
        mesh.select_set(True)
    target.select_set(True)

    verts = sum(len(m.data.vertices) for m in live)
    print("Exporting %d meshes (%d source verts) and %d bones -> %s"
          % (len(live), verts, bones, DST))

    os.makedirs(os.path.dirname(DST), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=DST,
        use_selection=True,
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
