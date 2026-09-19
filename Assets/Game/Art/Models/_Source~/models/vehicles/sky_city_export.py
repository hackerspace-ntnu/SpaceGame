"""Ship the sky city to Unity as one FBX for `SkyCityBuilder` to build from.

One file, not one per collection: the city's collections are parts of a single
125 m place, not a contact sheet of separable props. The builder tells the parts
apart by object name.

An export, not a generator: it never writes back to the `.blend`. What it ships,
beyond the visible model:

- **`COL_SkyCity_####`** - the collision set `sky_city_traversal` authors, split
  here into one object per convex island. The builder turns each into a
  BoxCollider or a convex MeshCollider and discards the mesh. Split in Blender,
  where the islands are topological facts, rather than re-found in Unity: the
  import splits vertices along hard edges, and welding them back by position
  would fuse two boxes that merely share a corner into one hull that fills the
  gap between them.
- **`LAD_SkyCity_##`** - the ladder markers, as empties. FBX custom properties
  do not reach Unity without an import postprocessor, so each marker's
  `climb_top_z` and `exit` are shipped as two child empties instead,
  `LAD_SkyCity_##_Top` and `LAD_SkyCity_##_Exit`: positions a builder reads
  straight off the hierarchy.

What it leaves out:

- The hidden stamp sources in `Coll_SkyCity_TraversalSources` - templates the
  cranes and goods were copied from, standing at the origin.
- Everything whose bounds centre is more than `CITY_REACH` from the centreline:
  the other vessels being assembled beside the city in the same file. They are
  not part of this place and would ride along with its prefab.
- Meshes with no geometry left (the gangway the traversal pass emptied).

`fix_inverted` stays off, for the reason ArtPipeline.md gives: this file shares
mesh datablocks between objects, and Unity already renders the user's mirrored
parts correctly by reversing culling on a negative-determinant renderer.

    blender --background --python models/vehicles/sky_city_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, LIB)

import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

from _exportlib import export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "sky_city.blend")
DST = unity_path("Environment", "Structures", "sky_city.fbx")
CITY_REACH = 50.0
SOURCES = "Coll_SkyCity_TraversalSources"
LADDER_PREFIX = "LAD_SkyCity_"
COLLISION = "COL_SkyCity"


def split_collision():
    """Replace `COL_SkyCity` with one object per connected island."""
    src = bpy.data.objects[COLLISION]
    coll = src.users_collection[0]
    bm = bmesh.new()
    bm.from_mesh(src.data)
    bm.transform(src.matrix_world)
    seen, count = set(), 0
    for f in bm.faces:
        if f in seen:
            continue
        stack, faces = [f], []
        seen.add(f)
        while stack:
            g = stack.pop()
            faces.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h not in seen:
                        seen.add(h)
                        stack.append(h)
        verts = sorted({v for g in faces for v in g.verts}, key=lambda v: v.index)
        index = {v: i for i, v in enumerate(verts)}
        count += 1
        me = bpy.data.meshes.new("%s_%04d" % (COLLISION, count))
        me.from_pydata([v.co.copy() for v in verts], [], [[index[v] for v in g.verts] for g in faces])
        coll.objects.link(bpy.data.objects.new(me.name, me))
    bm.free()
    bpy.data.objects.remove(src, do_unlink=True)
    return count


def prepare():
    doomed = set()
    sources = bpy.data.collections.get(SOURCES)
    if sources is not None:
        doomed.update(sources.all_objects)
    for o in bpy.data.objects:
        if o.type == 'MESH':
            if not len(o.data.vertices):
                doomed.add(o)
            elif abs(sum((o.matrix_world @ Vector(c)).x for c in o.bound_box) / 8) > CITY_REACH:
                doomed.add(o)
        elif o.type == 'EMPTY' and not o.name.startswith(LADDER_PREFIX):
            doomed.add(o)
    for o in doomed:
        bpy.data.objects.remove(o, do_unlink=True)
    print("  dropped %d object(s): stamp sources, other vessels, empties, emptied meshes" % len(doomed))

    ladders = [o for o in bpy.data.objects if o.name.startswith(LADDER_PREFIX)]
    for lad in ladders:
        foot = lad.matrix_world.translation
        for suffix, point in (("Top", Vector((foot.x, foot.y, lad["climb_top_z"]))),
                              ("Exit", Vector(lad["exit"]))):
            child = bpy.data.objects.new("%s_%s" % (lad.name, suffix), None)
            lad.users_collection[0].objects.link(child)
            child.location = point
            child.parent = lad
            child.matrix_parent_inverse = lad.matrix_world.inverted()
    print("  %d ladder marker(s), each with Top and Exit" % len(ladders))
    print("  %d collision island(s)" % split_collision())


print("sky city -> %s" % DST)
export(SRC, DST, keep_empties=True, prepare=prepare)
