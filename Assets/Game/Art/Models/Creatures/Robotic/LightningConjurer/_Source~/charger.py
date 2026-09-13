# The chest charger: the ring the conjurer draws its lightning out of.
#
# STEP 4b, after hands_rebuild.py and before rustify.py:
#
#     restore_parts -> rig -> walkerize -> hands_rebuild -> CHARGER -> rustify
#                                                                   -> anim -> export
#
# Additive. Creates four meshes and two bones that did not exist before and
# touches nothing else. Safe to re-run like every other step except rig.py: it
# removes its OWN four meshes and two bones by name and rebuilds them, which is
# only safe because nothing else in the pipeline creates or edits them.
#
# ---- why there is new geometry at all ----------------------------------------
#
# The creature has no thorax. It is an eye-dome (z 26.3-38.2) on a narrow
# Neck/Hips column (z 24.1-28.9) on two legs, with both arms floating detached at
# y = +-6.4. The old attack charged a ball in one cupped palm, which needed no
# body to hang off; the new one arcs between a CHEST CORE and both hands, and
# there was no chest core to arc from.
#
# ---- where it sits, and why it floats ----------------------------------------
#
# CENTRE is 1.7 units proud of the torso's front face (Hips reach x 1.27), not
# bolted to it. Two reasons, and the first is geometric rather than stylistic:
#
#   The dome overhangs the torso. It is a ~5.97-radius sphere centred near
#   (0.28, 24.30 + 7.9), so its underside at x 1.2 is already down at z 26.3 and
#   a ring of any useful size mounted flat on the column intersects it. Pushing
#   the ring forward buys headroom fast -- the dome's underside at x 3.0 is
#   z 26.89 -- which is what lets the ring be 4.7 units across instead of the
#   ~2 units the column itself could carry. A 2-unit ring on a 9 m creature is
#   invisible at the 25 m this thing casts from, and the ring IS the telegraph.
#
#   And it is consistent. Both arms on this creature already float unattached,
#   so a hovering emitter reads as the same machine rather than as a mistake.
#
# ---- the split into four parts -----------------------------------------------
#
# HOUSING is the only part that weathers. It is structure -- a rusted metal hoop
# -- so rustify.py paints it like every other plate. The other three are lit and
# stay Mat_Emissive_Portal_Blue via rustify's EXCEPT table, for the reason that
# table exists at all: the attack is survivable because it is telegraphed, and
# the telegraph is the glow.
#
# ROTOR and TEETH hang off their own bone so they can SPIN. That is the whole
# reason the charger is two bones instead of one: anim.py turns ChargerRotor
# through the charge the way the retired halo used to turn, and a spin-up is the
# clearest read available for "this is winding up to fire". The housing stays
# put, so the spin has something stationary to be seen against -- a ring turning
# with nothing fixed beside it barely reads.
import bpy
from mathutils import Matrix, Vector

RIG = "ConjurerRig"
PARENT_BONE = "Spine"

# World-space centre of the ring, and its axis. The model faces +X, so a ring
# that faces the way the creature does has its axis along +X and lies in the YZ
# plane.
CENTRE = Vector((3.20, -0.06, 24.00))
AXIS = 'X'

# Radii, all measured from the centre. Two constraints shape every number here.
#
# The OUTER edge has to clear the dome, and how much room there is depends on how
# far forward the ring sits: the dome's underside is at z 26.89 at x 3.0 and
# z 27.00 at x 3.2. At CENTRE the ceiling is 27.00, so an outer edge of 2.77 tops
# out at 26.77 and clears by 0.23.
#
# The TEETH have to land in the gap between the rotor's outer edge (1.32) and the
# housing's inner edge (2.13). The first version ran them out to 1.90 against a
# housing whose inner edge was 1.75, which buried them inside the housing tube --
# six blocks that cost geometry and could not be seen. They now span 1.32-2.08
# and sit entirely in open air, which is the only place a spoke reads from.
HOUSING_R, HOUSING_MINOR = 2.45, 0.32     # tube 2.13 - 2.77
ROTOR_R, ROTOR_MINOR = 1.15, 0.17         # tube 0.98 - 1.32
TOOTH_IN, TOOTH_OUT = 1.32, 2.08
TOOTH_W, TOOTH_D = 0.36, 0.26
TEETH = 6
CORE_R, CORE_D = 0.68, 0.30

HOUSING = "Charger_Housing"
ROTOR = "Charger_Rotor"
TEETH_OBJ = "Charger_Teeth"
CORE = "Charger_Core"

BONE = "Charger"
ROTOR_BONE = "ChargerRotor"

arm = bpy.data.objects[RIG]


def log(m):
    print(f"[charger] {m}")


# ---------------------------------------------------------------- re-runnable
# Clear this script's own previous output so the constants above can be re-tuned
# without a cold rebuild of the whole creature. Only the four meshes and two
# bones named in this file are touched -- nothing else in the pipeline creates
# them, so there is no hand-made work to lose here.
stale = [bpy.data.objects[n] for n in (HOUSING, ROTOR, TEETH_OBJ, CORE)
         if n in bpy.data.objects]
if stale:
    for o in stale:
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if me.users == 0:
            bpy.data.meshes.remove(me)
    log(f"removed {len(stale)} part(s) from a previous run")

if any(n in arm.data.bones for n in (BONE, ROTOR_BONE)):
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    for n in (ROTOR_BONE, BONE):          # child first
        if n in arm.data.edit_bones:
            arm.data.edit_bones.remove(arm.data.edit_bones[n])
    bpy.ops.object.mode_set(mode='OBJECT')
    log("removed the previous run's bones")


def rigid_bone_parent(obj, bone_name):
    """Bone-parent `obj` to `bone_name`, leaving its world transform identical.

    Blender anchors bone parenting at the bone TAIL, so the effective parent
    matrix P carries a +Y translation of bone.length. With matrix_parent_inverse
    = P^-1 the chain collapses to matrix_basis, which must therefore be set to
    the object's ORIGINAL WORLD matrix. Same helper and same trap as rig.py and
    hands_rebuild.py.
    """
    world = obj.matrix_world.copy()
    bone = arm.data.bones[bone_name]
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    P = arm.matrix_world @ bone.matrix_local @ Matrix.Translation(
        (0.0, bone.length, 0.0))
    obj.matrix_parent_inverse = P.inverted()
    obj.matrix_basis = world


def finish(obj, name):
    """Name it, put its origin on the ring centre, and file it under Character.

    Transforms are applied rather than left on the object because bone-parenting
    multiplies them back in: an object carrying a 90 deg rotation would be
    re-rotated by the bone it hangs off, and the ring would face sideways.

    Every part's origin goes to the ring CENTRE, because that is what a rotor
    spinning inside a housing needs -- all four have to turn about one point.

    Moving an origin is two steps, and doing it in one is a trap this cost me
    once already. Simply assigning matrix_world = Translation(CENTRE) sets the
    object's LOCATION and drags its geometry with it, which is harmless for the
    primitives (created centred on CENTRE, so they were already there) and wrong
    for the joined teeth: join() leaves the merged object's origin on the first
    tooth, out on the rim, so the assignment slid all six inward by that offset.
    Bake the location into the mesh first, then hand the offset back to the
    object, and the geometry does not move at all.
    """
    obj.name = name
    obj.data.name = name
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.data.transform(Matrix.Translation(-CENTRE))
    obj.matrix_world = Matrix.Translation(CENTRE)
    char = bpy.data.collections.get("Character")
    if char and obj.name not in char.objects:
        for c in list(obj.users_collection):
            c.objects.unlink(obj)
        char.objects.link(obj)
    return obj


def torus(major, minor, mseg, nseg):
    bpy.ops.mesh.primitive_torus_add(
        location=CENTRE, major_radius=major, minor_radius=minor,
        major_segments=mseg, minor_segments=nseg)
    o = bpy.context.active_object
    # primitive_torus_add lies in the XY plane with its axis on +Z; the ring has
    # to face the way the creature does, so stand it up onto +X.
    o.rotation_euler = (0.0, 1.5707963, 0.0)
    return o


# ------------------------------------------------------------------ build
for o in bpy.context.selected_objects:
    o.select_set(False)

housing = finish(torus(HOUSING_R, HOUSING_MINOR, 24, 8), HOUSING)
rotor = finish(torus(ROTOR_R, ROTOR_MINOR, 20, 6), ROTOR)

# ---- the teeth ---------------------------------------------------------------
# Six blocks radiating out of the rotor into the gap between it and the housing.
# Built as one object rather than six: they never move independently, and six
# objects would be six entries in rustify's EXCEPT table and six draw calls for
# one silhouette.
import math

tooth_objs = []
mid = (TOOTH_IN + TOOTH_OUT) / 2.0
length = TOOTH_OUT - TOOTH_IN
for i in range(TEETH):
    a = 2.0 * math.pi * i / TEETH
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0))
    t = bpy.context.active_object
    # Sized along the ring's own axes: X is the ring's thickness, Y is radial,
    # Z is the tooth's width across the rim.
    t.scale = (TOOTH_D, length, TOOTH_W)
    # Out to its radius, then round the rim. The ring lies in the YZ plane, so
    # "round the rim" is a rotation about the ring's axis, X.
    t.matrix_world = (Matrix.Translation(CENTRE)
                      @ Matrix.Rotation(a, 4, 'X')
                      @ Matrix.Translation((0.0, mid, 0.0))
                      @ Matrix.Diagonal((TOOTH_D, length, TOOTH_W, 1.0)))
    tooth_objs.append(t)

bpy.context.view_layer.objects.active = tooth_objs[0]
for t in tooth_objs:
    t.select_set(True)
bpy.ops.object.join()
teeth = finish(bpy.context.active_object, TEETH_OBJ)
for o in bpy.context.selected_objects:
    o.select_set(False)

# ---- the core ----------------------------------------------------------------
# A shallow disc, not a sphere. The old charge was a ball in a hand and read as
# an object being held; this one is a lens set into a machine, and a flat face
# pointing at the target is what makes the beam look like it came OUT of
# something rather than past it.
bpy.ops.mesh.primitive_cylinder_add(
    vertices=16, radius=CORE_R, depth=CORE_D, location=CENTRE)
core_o = bpy.context.active_object
core_o.rotation_euler = (0.0, 1.5707963, 0.0)
core = finish(core_o, CORE)

# ------------------------------------------------------------------ bones
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
ebs = arm.data.edit_bones

# Head at the ring centre, so the Unity transform named "Charger" IS the point
# the arcs are drawn from -- ConjurerChestCharge looks this bone up by name and
# uses its position directly, with no offset to keep in sync.
#
# Tail along +X, the ring's axis, so a rotation of ChargerRotor about world X
# spins the ring in its own plane instead of tumbling it.
for name, parent in ((BONE, PARENT_BONE), (ROTOR_BONE, BONE)):
    if name in ebs:
        continue
    eb = ebs.new(name)
    eb.head = CENTRE
    eb.tail = CENTRE + Vector((0.90, 0.0, 0.0))
    eb.roll = 0.0
    eb.parent = ebs[parent]
    eb.use_connect = False

bpy.ops.object.mode_set(mode='OBJECT')

rigid_bone_parent(housing, BONE)
rigid_bone_parent(core, BONE)
rigid_bone_parent(rotor, ROTOR_BONE)
rigid_bone_parent(teeth, ROTOR_BONE)

log(f"built {HOUSING}/{CORE} on {BONE}, {ROTOR}/{TEETH_OBJ} on {ROTOR_BONE}; "
    f"rig now has {len(arm.data.bones)} bones")

# ------------------------------------------------------------------ verify
problems = []
for n in (BONE, ROTOR_BONE):
    if n not in arm.data.bones:
        problems.append(f"bone {n} missing")
if BONE in arm.data.bones and arm.data.bones[BONE].parent.name != PARENT_BONE:
    problems.append(f"{BONE} is not parented to {PARENT_BONE}")

for n, want in ((HOUSING, BONE), (CORE, BONE),
                (ROTOR, ROTOR_BONE), (TEETH_OBJ, ROTOR_BONE)):
    o = bpy.data.objects.get(n)
    if o is None:
        problems.append(f"mesh {n} missing")
    elif o.parent_type != 'BONE' or o.parent_bone != want:
        problems.append(f"{n} is not bone-parented to {want}")
    elif (o.matrix_world.translation - CENTRE).length > 1e-4:
        problems.append(f"{n} origin drifted off the ring centre")

if problems:
    for p in problems:
        print(f"[charger] FAIL: {p}")
    raise SystemExit("[charger] verification failed")

bpy.ops.wm.save_mainfile()
log("OK, SAVED")
