"""Export astronaut_decorations.blend -> one FBX per prop. Reads only: the .blend is never saved.

    blender --background --python astronaut_decorations_export.py

Every `Coll_AstroDeco_<Name>` ships as `Environment/Decorations/Astronaut/astro_<snake>.fbx` on its own origin (the
`Root_<Name>` ground centre = the collection's `instance_offset`). Front is Blender -Y = Unity +Z. Unity wraps each in
`Prefabs/Environment/Decorations/Astronaut/AstroDeco_<Name>.prefab` and the colony buildings place those prefabs.
"""
import os, re, sys
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = HERE
while ROOT != os.path.dirname(ROOT) and not os.path.isdir(os.path.join(ROOT, 'ProjectSettings')):
    ROOT = os.path.dirname(ROOT)
sys.path.insert(0, os.path.join(ROOT, 'Assets/Game/Art/Models/_Source~'))
import _exportlib

SRC = os.path.join(HERE, 'astronaut_decorations.blend')
OUT = os.path.join(ROOT, 'Assets/Game/Art/Models/Environment/Decorations/Astronaut/')


def snake(name):
    return re.sub(r'(?<!^)(?=[A-Z])', '_', name).lower()


# The collection names are read from the file itself, so a prop added there ships without editing this list.
bpy.ops.wm.open_mainfile(filepath=SRC)
names = [c.name[len('Coll_AstroDeco_'):] for c in bpy.data.collections if c.name.startswith('Coll_AstroDeco_')]
_exportlib.export_collections(SRC, [('Coll_AstroDeco_' + n, os.path.join(OUT, 'astro_%s.fbx' % snake(n))) for n in names])
print('DONE', len(names))
