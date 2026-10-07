"""Export the `stairs` collection of mars_colony.blend -> colony_stairs.fbx. Reads only: the .blend is never saved.

    blender --background --python colony_stairs_export.py

The stairs are modelled beside the colony; the FBX moves them onto an origin that every airlock door can use: x at the
middle of the top step, y at the outer face of the hatch (where the top step starts), z on the ground. They descend
along Blender -Y, which is Unity +Z, so a prefab places one at a door's outer face turned to the door's outward direction.
Mesh parts are unparented in place and shipped as they are; nothing about the shape is changed here.
"""
import bpy, os
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = HERE
while ROOT != os.path.dirname(ROOT) and not os.path.isdir(os.path.join(ROOT, 'ProjectSettings')):
    ROOT = os.path.dirname(ROOT)
import sys
sys.path.insert(0, os.path.join(ROOT, 'Assets/Game/Art/Models/_Source~'))
import _exportlib

SRC = os.path.join(HERE, 'mars_colony.blend')
DST = os.path.join(ROOT, 'Assets/Game/Art/Models/Environment/Structures/AstronautSettlement/colony_stairs.fbx')

bpy.ops.wm.open_mainfile(filepath=SRC)
parts = [o for o in bpy.data.collections['stairs'].objects if o.type == 'MESH']
print('stairs parts', len(parts))
_exportlib._unparent_meshes()
_exportlib._localise_materials()
bpy.context.view_layer.update()

# The top step is the highest block; its centre in x and its outer (-Y) face fix the origin.
top = max(parts, key=lambda o: max((o.matrix_world @ Vector(v)).z for v in o.bound_box))
bb = [top.matrix_world @ Vector(v) for v in top.bound_box]
origin = Vector(((min(v.x for v in bb) + max(v.x for v in bb)) / 2, max(v.y for v in bb), 0.0))
print('top step', top.name, 'origin', tuple(round(c, 3) for c in origin))

for o in bpy.data.objects:
    o.select_set(False)
for o in parts:
    o.location = o.location - origin
    o.select_set(True)
bpy.context.view_layer.update()
lo = Vector((1e9,) * 3)
hi = Vector((-1e9,) * 3)
for o in parts:
    for v in o.bound_box:
        w = o.matrix_world @ Vector(v)
        lo = Vector(min(a, b) for a, b in zip(lo, w))
        hi = Vector(max(a, b) for a, b in zip(hi, w))
print('bounds after move lo', tuple(round(c, 3) for c in lo), 'hi', tuple(round(c, 3) for c in hi))
_exportlib._write_fbx(DST, {'MESH'}, use_selection=True)
print('DONE')
