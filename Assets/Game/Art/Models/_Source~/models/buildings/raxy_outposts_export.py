"""Read the outpost .blend files -> OutpostLayouts.json. Reads only: no .blend is ever saved.

    blender --background --python raxy_outposts_export.py

Every outpost in these files is an ARRANGEMENT of finished `Coll_Deco_*` pieces: each piece is a `Root_<Kind>` empty
(the piece's ground centre, which is the collection's `instance_offset` in decorations.blend) with its meshes parented
to it. So an outpost ships as a layout -- one row per piece: its kind, and where it stands -- and Unity places the
existing `Deco_<Kind>` prefabs (with their seats, spots, colliders and props) at those rows.

An outpost is a spatial cluster of pieces (XY overlap, `GAP` margin), found the way the nomad builds were: collections
do not group them. A `Root_` with no mesh under it, and anything further than `STRAY` metres from the origin, is a
leftover and is left out. Pieces are written relative to their outpost's footprint centre on its floor -- z = 0, or the level
most of its pieces stand at when that is not 0 (`Outpost_Raxy_10` was built 2.4 m up on a platform, `Outpost_Raxy_05` half a
metre down) -- in UNITY axes: (x, y, z) -> (-x, z, -y), the same flip every FBX takes through `_exportlib` (see `_exportlib.to_unity`).
Piece-local axes follow, so a piece's rotation is the matrix P R P^T and its scale is permuted (sx, sz, sy).

The Raxy outposts are named `Outpost_Raxy_NN` and the scavenger one `Outpost_Scavenger_NN`, numbered in reading order
of the sheet (top row first, left to right) so a rename of a file never renumbers them.
"""
import json
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = HERE
while ROOT != os.path.dirname(ROOT) and not os.path.isdir(os.path.join(ROOT, 'ProjectSettings')):
    ROOT = os.path.dirname(ROOT)
OUT = os.path.join(ROOT, 'Assets/Game/Art/Models/Environment/Structures/Outpost/OutpostLayouts.json')

SOURCES = [('Raxy_outpost.blend', 'Outpost_Raxy'), ('Raxy_outpost_2.blend', 'Outpost_Raxy'), ('scavenger_outpost.blend', 'Outpost_Scavenger')]
GAP = 1.5        # metres: pieces closer than this belong to one outpost
STRAY = 150.0    # metres from the origin: further out is a leftover, not an outpost
ROW = 20.0       # metres: clusters within this of each other in y are one row of the sheet
MIN_PIECES = 4   # fewer than this is a stray fragment, not an outpost
RAISED = 0.3     # metres: an outpost whose pieces mostly stand this far off z = 0 was built on a platform or in a pit; its ground is where they stand

# Blender (x, y, z) -> Unity (-x, z, -y).
P = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))


def descendants(obj):
    for child in obj.children:
        yield child
        yield from descendants(child)


def pieces_of(scene_objects):
    pieces = []
    for obj in scene_objects:
        if obj.type != 'EMPTY' or not obj.name.startswith('Root_'):
            continue
        loc = obj.matrix_world.translation
        if abs(loc.x) > STRAY or abs(loc.y) > STRAY:
            continue
        meshes = [d for d in descendants(obj) if d.type == 'MESH']
        if not meshes:
            continue
        xs, ys = [], []
        for mesh in meshes:
            for corner in mesh.bound_box:
                world = mesh.matrix_world @ Vector(corner)
                xs.append(world.x)
                ys.append(world.y)
        pieces.append({'obj': obj, 'kind': obj.name.split('.')[0][len('Root_'):],
                       'box': (min(xs), min(ys), max(xs), max(ys))})
    return pieces


def overlap(a, b):
    return a[0] - GAP < b[2] and b[0] - GAP < a[2] and a[1] - GAP < b[3] and b[1] - GAP < a[3]


def clusters_of(pieces):
    parent = list(range(len(pieces)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for i in range(len(pieces)):
        for j in range(i + 1, len(pieces)):
            if overlap(pieces[i]['box'], pieces[j]['box']):
                parent[find(i)] = find(j)
    groups = {}
    for i, piece in enumerate(pieces):
        groups.setdefault(find(i), []).append(piece)
    return [g for g in groups.values() if len(g) >= MIN_PIECES]


def ground_level(group):
    """z of the floor the outpost stands on: where most of its pieces' origins are, when that is not z = 0."""
    heights = {}
    for piece in group:
        z = round(piece['obj'].matrix_world.translation.z / 0.05) * 0.05
        heights[z] = heights.get(z, 0) + 1
    common = max(heights, key=heights.get)
    return common if abs(common) >= RAISED else 0.0


def footprint_centre(group):
    lo_x = min(p['box'][0] for p in group)
    lo_y = min(p['box'][1] for p in group)
    hi_x = max(p['box'][2] for p in group)
    hi_y = max(p['box'][3] for p in group)
    return Vector(((lo_x + hi_x) / 2, (lo_y + hi_y) / 2, ground_level(group)))


def piece_row(piece, origin):
    world = piece['obj'].matrix_world
    loc, quat, scale = world.decompose()
    position = P @ (loc - origin)
    rotation = (P @ quat.to_matrix() @ P.transposed()).to_quaternion()
    return {'kind': piece['kind'],
            'name': piece['obj'].name,
            'position': [round(position.x, 4), round(position.y, 4), round(position.z, 4)],
            'rotation': [round(rotation.x, 5), round(rotation.y, 5), round(rotation.z, 5), round(rotation.w, 5)],
            'scale': [round(scale.x, 4), round(scale.z, 4), round(scale.y, 4)]}


def main():
    outposts = []
    counts = {}
    for filename, prefix in SOURCES:
        bpy.ops.wm.open_mainfile(filepath=os.path.join(HERE, filename))
        groups = clusters_of(pieces_of(bpy.data.objects))
        centres = [footprint_centre(g) for g in groups]
        order = sorted(range(len(groups)), key=lambda i: (-round(centres[i].y / ROW), centres[i].x))
        for i in order:
            counts[prefix] = counts.get(prefix, 0) + 1
            name = '%s_%02d' % (prefix, counts[prefix])
            rows = [piece_row(p, centres[i]) for p in groups[i]]
            kinds = {}
            for row in rows:
                kinds[row['kind']] = kinds.get(row['kind'], 0) + 1
            xs = [p['box'][2] - p['box'][0] for p in groups[i]]
            print('OUTPOST %-22s %-24s %3d pieces centre (%.1f, %.1f)  %s'
                  % (name, filename, len(rows), centres[i].x, centres[i].y, dict(sorted(kinds.items()))))
            outposts.append({'name': name, 'source': filename, 'pieces': rows})

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'w') as handle:
        json.dump({'outposts': outposts}, handle, indent=1)
    print('DONE %d outposts -> %s' % (len(outposts), OUT))


main()
