"""Export mars_colony.blend -> one FBX per colony building. Reads only: the .blend is never saved.

    blender --background --python mars_colony_export.py

Each `Coll_Colony_<Name>` ships as `Environment/Structures/AstronautSettlement/colony_<snake>.fbx`, moved onto the origin
by its `instance_offset` (the building's ground centre). Not `_exportlib.export_collections`: the hulls are hollowed
and opened by live Boolean modifiers whose cutters live in `Colony_Cutters`, outside the building collections, so the
cutters have to move with the building or every doorway and window is cut in the wrong place.

What ships per building: the hull (with its doorways, windows and room volume cut out), the door plugs (Unity swings
them as the outer airlock hatches), and the `<Building>.<unit>.Liner` interior shells. `Colony_Removed` (the dark
boxes that faked an interior) and `Colony_Cutters` never ship.
"""
import os, re, sys
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = HERE
while ROOT != os.path.dirname(ROOT) and not os.path.isdir(os.path.join(ROOT, 'ProjectSettings')):
    ROOT = os.path.dirname(ROOT)
sys.path.insert(0, os.path.join(ROOT, 'Assets/Game/Art/Models/_Source~'))
import _exportlib

SRC = os.path.join(HERE, 'mars_colony.blend')
OUT = os.path.join(ROOT, 'Assets/Game/Art/Models/Environment/Structures/AstronautSettlement/')


def snake(name):
    return re.sub(r'(?<!^)(?=[A-Z])', '_', name).lower()


bpy.ops.wm.open_mainfile(filepath=SRC)
print('  %d mesh(es) unparented' % _exportlib._unparent_meshes())
print('  %d material(s) localised' % _exportlib._localise_materials())

for coll in [c for c in bpy.data.collections if c.name.startswith('Coll_Colony_')]:
    name = coll.name[len('Coll_Colony_'):]
    meshes = [o for o in coll.all_objects if o.type == 'MESH']
    cutters = {m.object for o in meshes for m in o.modifiers if m.type == 'BOOLEAN' and m.object}
    shift = -coll.instance_offset.copy()
    moved = meshes + sorted(cutters, key=lambda o: o.name)
    for o in moved:
        o.location += shift
    bpy.context.view_layer.update()
    for o in bpy.data.objects:
        o.select_set(False)
    for o in meshes:
        o.select_set(True)
    _exportlib._write_fbx(os.path.join(OUT, 'colony_%s.fbx' % snake(name)), {'MESH'}, use_selection=True)
    for o in moved:
        o.location -= shift
    print('  %s: %d meshes, %d cutters' % (name, len(meshes), len(cutters)))
print('DONE')
