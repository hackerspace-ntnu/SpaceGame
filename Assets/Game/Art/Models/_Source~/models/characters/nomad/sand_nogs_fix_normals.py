"""Un-mirror the sand nomads' mirrored parts so Unity draws them right side out.

    blender --background --python sand_nogs_fix_normals.py -- [path/to/sand_nogs.blend] [--dry-run]

WRITES to the .blend (timestamped `sand_nogs_pre_normals_*.blend` copy beside it). Run after
sand_nogs_fix_rig.py and before sand_nogs_export.py.

The problem it fixes
--------------------
Nine to ten parts per character -- the pouches, two arm bands, a cuff and a couple of rings --
carry a NEGATIVE scale on all three axes: they were placed by mirroring a duplicate through a
point. Blender renders that correctly, because it flips the winding of a mirrored object on the
fly, and `Recalculate Outside` reports nothing wrong, because in the mesh's own space the normals
ARE outward. The FBX exporter writes the negative scale onto the node, and Unity's skinned-mesh
path does not flip the winding back, so every one of those parts draws inside out in the game:
the ring you can see through, the pouch that is a hole.

The fix bakes the scale into the vertices (so the object's scale is +1 and the world placement is
unchanged), then recalculates the normals outward, since baking a point-mirror turns every face
inside out in mesh space. The rig binding is untouched: vertex groups are per vertex and the
armature modifier reads world space.
"""

import datetime
import os
import shutil
import sys

import bmesh
import bpy
from mathutils import Matrix

DEFAULT_BLEND = os.path.join(os.path.expanduser("~"), "Documents", "Blender", "sand_nogs.blend")
CHARACTERS = ["Umber", "Tan", "Maroon", "StrawHat"]


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    dry = "--dry-run" in argv
    paths = [a for a in argv if not a.startswith("--")]
    return (paths[0] if paths else DEFAULT_BLEND), dry


def driven_by(armature):
    return sorted((o for o in bpy.data.objects
                   if o.type == 'MESH'
                   and any(m.type == 'ARMATURE' and m.object == armature for m in o.modifiers)),
                  key=lambda o: o.name)


def bake_scale_and_face_outward(obj):
    """Fold the object's scale into its vertices and make every face point outward."""
    scale = obj.scale.copy()
    obj.data.transform(Matrix.Diagonal((scale.x, scale.y, scale.z, 1.0)))
    obj.scale = (1.0, 1.0, 1.0)

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def main():
    blend, dry = parse_args()
    if not os.path.exists(blend):
        raise SystemExit("No model at %s" % blend)

    if not dry:
        stamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
        backup = os.path.join(os.path.dirname(blend), "sand_nogs_pre_normals_%s.blend" % stamp)
        shutil.copyfile(blend, backup)
        print("Backed up -> %s" % backup)

    bpy.ops.wm.open_mainfile(filepath=blend)

    total = 0
    for name in CHARACTERS:
        arm = bpy.data.objects.get("Armature_Nomad%s" % name)
        if arm is None:
            raise SystemExit("No Armature_Nomad%s -- run sand_nogs_fix_rig.py first." % name)

        fixed = []
        for obj in driven_by(arm):
            if obj.matrix_world.to_3x3().determinant() >= 0.0:
                continue
            if obj.data.users > 1:
                # Two objects sharing one mesh cannot both bake their own scale into it.
                obj.data = obj.data.copy()
            bake_scale_and_face_outward(obj)
            fixed.append(obj.name)

        total += len(fixed)
        print("=== %s: un-mirrored %d part(s): %s" % (name, len(fixed), ", ".join(fixed)))

    if dry:
        print("\nDRY RUN -- nothing saved (%d parts would change)." % total)
        return

    bpy.ops.wm.save_mainfile(filepath=blend)
    print("Saved %s (%d parts un-mirrored)" % (blend, total))


main()
