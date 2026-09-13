"""Export each removable hull module of the lander as its own FBX — the item the player carries.

Like player_ship_export.py this is an export, not a generator: `ship_lander_blockout.blend` is the
user's hand-built file and is opened read-only, never saved. The .blend is re-opened once per kind
rather than juggling one scene, because "delete everything except this object" is obviously correct
and a partially-torn-down scene is not.

Each module is exported at its **true ship scale** — a nuclear motor really is 11 m long — so the
thing lying in the sand is bit-for-bit the thing that ends up bolted to the hull. Only the pose is
normalised: parent dropped, rotation cleared, and the bounds centre moved to the origin so the
Rigidbody's centre of mass and the box collider agree with the pivot.

    blender --background --python ship_parts_export.py
"""

import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ship_parts  # noqa: E402

REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)
SRC = os.path.join(HERE, "ship_lander_blockout.blend")
DST_DIR = os.path.join(REPO, "Assets", "Game", "Art", "Models", "Items", "ShipParts")


def bounds_centre(obj):
    """Centre of the object's local bounding box, expressed in the object's parent frame."""
    local = sum((Vector(c) for c in obj.bound_box), Vector()) / 8.0
    return obj.matrix_world.to_3x3() @ local


def export_kind(kind, raws):
    bpy.ops.wm.open_mainfile(filepath=SRC)

    # The turbines and fittings link their materials from palette.blend, which sits outside
    # Assets/ and would not resolve from an FBX.
    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    objects = {o.name: o for o in bpy.data.objects}
    source = ship_parts.item_source(objects, raws)

    keep = bpy.data.objects[source]
    for obj in [o for o in bpy.data.objects if o is not keep]:
        bpy.data.objects.remove(obj, do_unlink=True)

    keep.parent = None
    keep.rotation_euler = (0.0, 0.0, 0.0)
    keep.location = (0.0, 0.0, 0.0)
    bpy.context.view_layer.update()
    keep.location = -bounds_centre(keep)
    keep.name = ship_parts.ROLE_PREFIX + kind

    path = os.path.join(DST_DIR, "%s.fbx" % kind.lower())
    os.makedirs(DST_DIR, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path,
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

    d = keep.dimensions
    print("Wrote %s  (%s, %.2f x %.2f x %.2f m)" % (path, source, d.x, d.y, d.z))


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s" % SRC)

    for kind, raws in ship_parts.PART_KINDS:
        export_kind(kind, raws)

    print("Exported %d ship part(s) -> %s" % (len(ship_parts.PART_KINDS), DST_DIR))
    # Deliberately no save_mainfile anywhere above.


if __name__ == "__main__":
    main()
