"""Export ship_lander_blockout.blend to the two FBXs PlayerShipBuilder consumes.

Like ship_rv_export.py this is an export, not a generator: it opens the user's hand-built
.blend read-only and never saves it (ship_lander_blockout.blend is hand-edited — see
ship_lander_blockout_BUILD.md — so nothing here may write to it).

What it does beyond a plain FBX export:

  * Drops the reference hull. `Ref_ExampleHull` is the scaled Tripo example the ship was
    modelled around — a 5 748-vert non-manifold shell that must not ship.
  * Renames the role meshes in memory. The moving parts carry hand-given names already
    (sliding_door_1..4, back_door*); the boarding stair and sill platform are still on
    Blender defaults, so they are renamed here rather than archaeologically in the builder.
    The .blend keeps its own names because nothing is saved.
  * Localises any palette-linked materials (turbines/fittings link from palette.blend,
    which sits outside Assets/ and would not resolve from the FBX).
  * Exports meshes only, default axis conversion — the nose arrives along Unity's -X and
    the builder yaws it onto +Z, exactly as ShipRVBuilder does.
  * **Bakes a second FBX of nothing but collision hulls** (see `bake_collision`). Unity can
    only put a convex MeshCollider on a Rigidbody, and no per-mesh rule survives contact with
    a hull the player walks around inside — so the collision proxy is authored here, where the
    closed source geometry is available, rather than guessed at in the builder.

Both FBXs come out of one run on purpose. They are exported from the same scene with the same
flags, which is what lets the builder overlay one on the other with a single transform; two
scripts would be two chances for the axis conversion or the scale option to drift apart.

    blender --background --python player_ship_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
sys.path.insert(0, LIB)
import ship_parts  # noqa: E402
import _collisionlib  # noqa: E402

REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)
SRC = os.path.join(HERE, "ship_lander_blockout.blend")
OUT_DIR = os.path.join(REPO, "Assets", "Game", "Art", "Models", "Vehicles", "PlayerShip")
DST = os.path.join(OUT_DIR, "player_ship.fbx")
COLLISION_DST = os.path.join(OUT_DIR, "player_ship_collision.fbx")

# Objects that exist for reference only and must not reach Unity.
DROP = {"Ref_ExampleHull"}

# Blender-default names -> the role names PlayerShipBuilder measures from. Only the meshes
# the builder needs to find get a name; the structural slabs stay as they are, because the
# builder treats them generically (collision from the bake below).
RENAME = {
    "Cube.129": "Mesh_BoardingStair",
    "Cube.119": "Mesh_BoardingStair_Foot",
    "Cube.043": "Mesh_SillPlatform",
    # Landmarks the builder measures the cockpit and interior from.
    "Icosphere": "Mesh_CanopyDome",
    "Cube.026": "Mesh_Deck_Fore",
    "Cube.001": "Mesh_Deck_Main",
}

# Prefix every baked hull carries, so the builder can read each hull's source mesh back off its
# name and check that this script and PlayerShipBuilder.NoStructuralCollider still agree about
# who owns what. The two lists below are the Blender half of that agreement.
COLLISION_PREFIX = "COL_"

# Meshes the bake leaves alone because something else in the prefab gives them a collider:
# the moving parts get one on their hinge pivot, and the canopy dome gets none at all (a 3 m
# character's head sits inside the glass ball, and an honest collider there brains the pilot).
COLLISION_SKIP = {
    "back_door", "back_door_support", "back_door_support.001", "back_door_support.002",
    "sliding_door_1", "sliding_door_2", "sliding_door_3", "sliding_door_4",
    "Mesh_BoardingStair", "Mesh_BoardingStair_Foot", "Mesh_SillPlatform",
    "Mesh_CanopyDome",
}

# Meshes that must keep a collider on their OWN object rather than dissolve into the bake:
# a socket switches its module's collider off to make the hole a hole, and a MountStation on a
# command chair needs a click target that moves with the chair. The builder fits each of these
# with its own convex MeshCollider.
COLLISION_OWN_COLLIDER_PREFIXES = (
    "Part_", "Cockpit_", "Turbine_", "Thruster_", "Intake_", "RCS_", "Sensor_",
)

# Ceiling on the phantom solid the proxy is allowed to add, as a multiple of the ship's real
# volume. The decomposition lands at ~1.05 today; anything approaching 1.15 means a mesh has
# changed shape enough that the tuning in _collisionlib no longer fits it, and the interior is
# about to fill in again where nobody would think to look.
MAX_OVERFILL = 1.15

# Unity cooks a convex MeshCollider down to 255 hull vertices, silently. A piece over the limit
# would be quietly reshaped after leaving here, so it fails the bake instead.
MAX_HULL_VERTS = 255

FBX_FLAGS = dict(
    use_selection=False,
    object_types={'MESH'},
    apply_scale_options='FBX_SCALE_NONE',
    axis_forward='-Z',
    axis_up='Y',
    mesh_smooth_type='FACE',
    use_mesh_modifiers=True,
    bake_space_transform=False,
    path_mode='COPY',
    embed_textures=False,
)


def bake_collision():
    """Replace the scene with convex collision hulls and export them.

    Destroys the loaded scene, so it runs last. Nothing is saved back to the .blend.
    """
    sources = [o for o in bpy.data.objects
               if o.type == 'MESH'
               and o.name not in COLLISION_SKIP
               and not o.name.startswith(COLLISION_OWN_COLLIDER_PREFIXES)]

    # The source objects are listed before any hull exists, because `bpy.data.objects` holds the
    # new hulls too — clearing the scene by walking it afterwards deletes the bake along with it.
    doomed = list(bpy.data.objects)

    depsgraph = bpy.context.evaluated_depsgraph_get()
    hulls, solid_volume, hull_volume = [], 0.0, 0.0
    for obj in sources:
        pieces, volume, hulled = _collisionlib.decompose_object(obj, depsgraph)
        solid_volume += volume
        hull_volume += hulled

        for index, points in enumerate(pieces):
            bm = _collisionlib.hull_mesh(points)
            if len(bm.verts) > MAX_HULL_VERTS:
                raise SystemExit(
                    "Collision hull %s piece %d has %d vertices; Unity cooks convex colliders "
                    "down to %d and would reshape it after export."
                    % (obj.name, index, len(bm.verts), MAX_HULL_VERTS))

            mesh = bpy.data.meshes.new("%s%s_%d" % (COLLISION_PREFIX, obj.name, index))
            bm.to_mesh(mesh)
            bm.free()

            # The hull points are already in world space, so the piece is linked at the identity
            # and the whole set overlays the visual model as one rigid group.
            hull = bpy.data.objects.new(mesh.name, mesh)
            bpy.context.scene.collection.objects.link(hull)
            hulls.append(hull)

    overfill = hull_volume / solid_volume if solid_volume > 1e-6 else 0.0
    if overfill > MAX_OVERFILL:
        raise SystemExit(
            "Collision bake overfills by %.2fx (limit %.2fx): %d hulls hold %.1f m3 around "
            "%.1f m3 of ship. The interior would be solid where the hulls bulge — retune "
            "_collisionlib (TARGET_FILL / MAX_DEPTH) against the changed geometry."
            % (overfill, MAX_OVERFILL, len(hulls), hull_volume, solid_volume))

    for obj in doomed:
        bpy.data.objects.remove(obj, do_unlink=True)

    print("Collision: %d hulls from %d meshes, %.1f m3 around %.1f m3 (%.2fx) -> %s"
          % (len(hulls), len(sources), hull_volume, solid_volume, overfill, COLLISION_DST))
    bpy.ops.export_scene.fbx(filepath=COLLISION_DST, **FBX_FLAGS)


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s" % SRC)

    bpy.ops.wm.open_mainfile(filepath=SRC)

    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    for obj in [o for o in bpy.data.objects if o.name in DROP]:
        bpy.data.objects.remove(obj, do_unlink=True)

    renames = dict(RENAME)

    # The removable modules get their role names from the shared table rather than a second copy
    # of it here, because ship_parts_export.py has to agree with this exactly or the item a player
    # carries and the socket it belongs in are different parts.
    renames.update(ship_parts.role_names({o.name: o for o in bpy.data.objects}))

    for raw, role in renames.items():
        obj = bpy.data.objects.get(raw)
        if obj is None:
            raise SystemExit("Expected mesh '%s' is missing — the .blend has changed; "
                             "update RENAME before exporting." % raw)
        obj.name = role

    # Flatten parenting so the builder can rely on Transform.Find over direct children.
    for obj in bpy.data.objects:
        if obj.type == 'MESH' and obj.parent is not None:
            world = obj.matrix_world.copy()
            obj.parent = None
            obj.matrix_world = world

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    print("Exporting %d meshes -> %s" % (len(meshes), DST))

    os.makedirs(OUT_DIR, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=DST, **FBX_FLAGS)
    print("Wrote %s" % DST)

    bake_collision()
    # Deliberately no save_mainfile: the .blend is the user's hand-built source of truth.


if __name__ == "__main__":
    main()
