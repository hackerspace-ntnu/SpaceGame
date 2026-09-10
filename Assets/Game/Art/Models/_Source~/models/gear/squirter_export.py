"""Ship the squirter — the two-handed foam sprayer — to Unity.

`squirter.blend` is a HAND-BUILT file: no generator ever produced it, its objects
carry Blender's default names, and nothing here writes back to it. Three things
about how it was built need answering before Unity can use it, and all three are
answered in memory, on a copy, so the source stays exactly as the user left it:

- **The hoses are curves.** Three `BézierCurve*` objects with a bevel depth are
  real tube geometry, and `_exportlib.export` ships `object_types={"MESH"}` — a
  curve is dropped with no warning, so the gun would arrive with its hoses gone.
  They are converted to meshes first, off the evaluated depsgraph so the bevel
  comes with them.
- **It points the wrong way.** The bell is at +Y; every model in this library
  faces −Y, which the export's axis conversion lands on Unity's +Z. Un-rotated,
  the gun would point backwards out of the player's hands. A 180° turn about Z
  is a rotation, not a mirror, so nothing ends up inside out.
- **It is eleven metres long.** The library's convention is 1 unit = 1 m and the
  hand-built file is roughly 8.5x life size, with its origin off in space rather
  than on the model. Unity never trusts a model's own metres for how big an item
  is drawn (`ItemWorldScale`), but the prefab's own muzzle and grip markers are
  authored in these units, so a model at true size is the difference between
  markers that read as metres and markers that read as nothing.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/squirter_export.py
"""

import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import export, to_unity, unity_path  # noqa: E402

SRC = os.path.join(HERE, "squirter.blend")
DST = unity_path("Items", "squirter.fbx")

# The normalised copy. Never `squirter.blend` itself — the hand-built file is the
# source of truth and this script is the one place that would be able to destroy
# it. Written under the OS temp dir so a stray copy cannot be mistaken for a
# model in the library.
WORK = os.path.join("/tmp", "squirter_export_work.blend")

# Longest axis, in metres, of the exported model. The dragon bazooka — the anchor
# of `ItemScaleLadder` — is 1.3685 m in Blender and worn at holdSize 1.25; this is
# the same class of thing, a two-handed weapon-shaped tool, so it is authored just
# under the anchor and worn on the anchor's bracket.
TARGET_LENGTH = 1.30

# The barrel. Its bounds give the muzzle face and the axis the whole gun is built
# around, so the export puts that axis on the origin rather than the bounding
# box's centre, which sits down in the tank.
BARREL = "Cylinder.003"

# The rear handle, the one with the trigger in front of it, and the front handle
# the off hand takes. Printed for the prefab to wire; nothing here moves them.
REAR_GRIP = "Cube.001"
FORE_GRIP = "Cylinder.001"
TANK = "Cylinder"


def curves_to_meshes():
    """Replace every curve with the mesh its bevel actually draws."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    converted = []

    for curve in [o for o in bpy.context.scene.objects if o.type == 'CURVE']:
        mesh = bpy.data.meshes.new_from_object(curve.evaluated_get(depsgraph))
        obj = bpy.data.objects.new(curve.name + "_Mesh", mesh)
        obj.matrix_world = curve.matrix_world.copy()
        curve.users_collection[0].objects.link(obj)
        bpy.data.objects.remove(curve, do_unlink=True)
        converted.append(obj.name)

    bpy.context.view_layer.update()
    return converted


def bounds(objects):
    """World-space bounds of `objects`, with modifiers applied."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    low = Vector((1e9, 1e9, 1e9))
    high = Vector((-1e9, -1e9, -1e9))

    for obj in objects:
        mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
        for vertex in mesh.vertices:
            world = obj.matrix_world @ vertex.co
            for axis in range(3):
                low[axis] = min(low[axis], world[axis])
                high[axis] = max(high[axis], world[axis])
        bpy.data.meshes.remove(mesh)

    return low, high


def transform_all(matrix):
    for obj in bpy.context.scene.objects:
        if obj.type == 'MESH':
            obj.matrix_world = matrix @ obj.matrix_world
    bpy.context.view_layer.update()


def marker(name, point):
    unity = to_unity(point)
    print("  MARKER %-10s blender (%7.4f, %7.4f, %7.4f)  unity (%7.4f, %7.4f, %7.4f)"
          % (name, point.x, point.y, point.z, unity[0], unity[1], unity[2]))


def normalise():
    """Open the source, fix the three things above, and save the working copy."""
    bpy.ops.wm.open_mainfile(filepath=SRC)

    converted = curves_to_meshes()
    print("  converted %d curve(s) to mesh: %s" % (len(converted), ", ".join(converted)))

    transform_all(Matrix.Rotation(3.141592653589793, 4, 'Z'))

    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    barrel_low, barrel_high = bounds([bpy.data.objects[BARREL]])
    low, high = bounds(meshes)

    # The origin: on the barrel axis, half way along the gun. Everything the
    # prefab hangs off the model — the muzzle, the palm — is then a short offset
    # from the root rather than a distance to somewhere out in the scene.
    origin = Vector(((barrel_low.x + barrel_high.x) * 0.5,
                     (low.y + high.y) * 0.5,
                     (barrel_low.z + barrel_high.z) * 0.5))
    scale = TARGET_LENGTH / (high.y - low.y)
    print("  %.3f m long as built; scaling by %.5f to %.3f m, origin at (%.3f, %.3f, %.3f)"
          % (high.y - low.y, scale, TARGET_LENGTH, origin.x, origin.y, origin.z))

    transform_all(Matrix.Scale(scale, 4) @ Matrix.Translation(-origin))

    low, high = bounds(meshes)
    print("  bounds now (%.4f, %.4f, %.4f) to (%.4f, %.4f, %.4f), longest axis %.4f m"
          % (low.x, low.y, low.z, high.x, high.y, high.z, max(high - low)))

    barrel_low, barrel_high = bounds([bpy.data.objects[BARREL]])
    grip_low, grip_high = bounds([bpy.data.objects[REAR_GRIP]])
    fore_low, fore_high = bounds([bpy.data.objects[FORE_GRIP]])
    tank_low, tank_high = bounds([bpy.data.objects[TANK]])

    marker("Muzzle", Vector(((barrel_low.x + barrel_high.x) * 0.5,
                             barrel_low.y,
                             (barrel_low.z + barrel_high.z) * 0.5)))
    # The palm, not the handle's centre: a hand closes over the upper half of a
    # pistol grip, under the trigger rather than level with it.
    marker("Grip", Vector(((grip_low.x + grip_high.x) * 0.5,
                           (grip_low.y + grip_high.y) * 0.5,
                           grip_low.z + (grip_high.z - grip_low.z) * 0.6)))
    marker("Foregrip", Vector(((fore_low.x + fore_high.x) * 0.5,
                               (fore_low.y + fore_high.y) * 0.5,
                               (fore_low.z + fore_high.z) * 0.5)))
    # The outboard flank of the tank, where a gauge can be read while holding it.
    marker("Gauge", Vector((tank_high.x,
                            (tank_low.y + tank_high.y) * 0.5,
                            (tank_low.z + tank_high.z) * 0.5)))

    bpy.ops.wm.save_as_mainfile(filepath=WORK, copy=True)


def main():
    normalise()
    export(WORK, DST, keep_armature=False)
    os.remove(WORK)


main()
