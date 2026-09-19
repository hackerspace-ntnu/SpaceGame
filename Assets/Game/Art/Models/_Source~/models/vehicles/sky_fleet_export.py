"""Ship the sky city's escort vessels to Unity, one FBX each, for `SkyFleetBuilder`.

The user built three smaller ships beside the city in `sky_city.blend`, out past
x = 50: a three-bag caged freighter with a gondola, a single-bag skiff with sails
and a lantern tower, and a single-bag tug with twin engine pods. They are their
own ships, clustered round the city as its flagship - so each ships as its own
FBX and prefab, and `SkyFleetBuilder` places them round the city.

They were assembled from the city's own parts at the city's own scale, so a
freighter came out half the length of the flagship. Each is shrunk by
`VESSEL_SCALE` about its own centre and moved to the origin. Crew-scale parts
shrink with it: these are ships to see, not yet ships to board.

A vessel is found, not listed: every object whose bounds centre is beyond
`CITY_REACH` is grouped with every object its bounds touch (within `TOUCH`), and
each vessel is picked out by one object only it contains. So a part the user adds
to a ship ships with it.

    blender --background --python models/vehicles/sky_fleet_export.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, LIB)

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

from _exportlib import export, unity_path  # noqa: E402

SRC = os.path.join(HERE, "sky_city.blend")
PALETTE = os.path.join(LIB, "palette.blend")
CITY_REACH = 50.0
TOUCH = 3.0
VESSEL_SCALE = 0.5
# (FBX name, an object only that vessel contains)
VESSELS = (
    ("sky_freighter", "Mesh_SkyCity_Cage.001"),
    ("sky_skiff", "Mesh_SkyCity_Beacon_SensorCupola_Lantern.004"),
    ("sky_tug", "Mesh_SkyCity_Prow.001"),
)
# The user's hull block under the freighter's deck has no material and draws
# plain white in Blender; this is the palette's white, so it looks the same.
UNPAINTED = "Mat_Paint_White_Arctic"


def bounds(o):
    pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
    return (Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))),
            Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts))))


def vessels():
    """Every object beyond the city, grouped into ships by touching bounds."""
    parts = []
    for o in bpy.data.objects:
        if o.type != 'MESH' or not len(o.data.vertices):
            continue
        lo, hi = bounds(o)
        if abs((lo.x + hi.x) / 2) > CITY_REACH:
            parts.append((o, lo, hi))
    parent = list(range(len(parts)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    for i, (_, alo, ahi) in enumerate(parts):
        for j in range(i + 1, len(parts)):
            _, blo, bhi = parts[j]
            if all(alo[k] < bhi[k] + TOUCH and blo[k] < ahi[k] + TOUCH for k in range(3)):
                parent[find(i)] = find(j)
    groups = {}
    for i, (o, _, _) in enumerate(parts):
        groups.setdefault(find(i), []).append(o)
    return list(groups.values())


def keep_vessel(key):
    def prepare():
        ships = [g for g in vessels() if any(o.name == key for o in g)]
        if len(ships) != 1:
            raise SystemExit("No single vessel contains %s" % key)
        ship = ships[0]
        keep = {o.name for o in ship}
        for o in [o for o in bpy.data.objects if o.name not in keep]:
            bpy.data.objects.remove(o, do_unlink=True)
        los, his = zip(*(bounds(o) for o in ship))
        centre = Vector([(min(v[k] for v in los) + max(v[k] for v in his)) / 2 for k in range(3)])
        move = Matrix.Scale(VESSEL_SCALE, 4) @ Matrix.Translation(-centre)
        bpy.context.view_layer.update()
        worlds = {o: o.matrix_world.copy() for o in ship}
        for o, world in worlds.items():
            o.parent = None
            o.matrix_world = move @ world
        unpainted = [o for o in ship if not any(o.data.materials)]
        if unpainted:
            with bpy.data.libraries.load(PALETTE, link=True) as (src, dst):
                dst.materials = [UNPAINTED]
            for o in unpainted:
                o.data.materials.append(bpy.data.materials[UNPAINTED])
        print("  %d part(s) about centre (%.1f, %.1f, %.1f), scaled %.2f; painted %d"
              % (len(ship), centre.x, centre.y, centre.z, VESSEL_SCALE, len(unpainted)))
    return prepare


for name, key in VESSELS:
    dst = unity_path("Environment", "Structures", "SkyFleet", name + ".fbx")
    print("%s -> %s" % (name, dst))
    export(SRC, dst, prepare=keep_vessel(key))
