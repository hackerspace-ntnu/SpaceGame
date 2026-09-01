"""Add turbines and spaceship fittings to `ship_lander_blockout.blend`.

Purely additive: the file is opened, two new collections are added
(`Coll_Lander_Turbines`, `Coll_Lander_Fittings`) holding appended copies of
library components placed around the ×30 reference hull, and the file is saved
in place. Nothing pre-existing is moved, renamed or removed; the script
asserts that before saving.

Components used (all appended, so they are editable here):
    components/mechanical/turbine.blend        Long, Short, Ducted, Stub
    components/mechanical/thruster_nacelle.blend  Tail, Vernier
    components/mechanical/vent_grille.blend    Scoop
    components/structural/sensor_cupola.blend  Radome

    blender --background --python ship_lander_blockout_turbines.py
"""

import math
import os
import sys

import bpy
from mathutils import Euler

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
import _buildlib as B  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
TARGET = os.path.join(HERE, "ship_lander_blockout.blend")
COMP = os.path.join(B.LIB_ROOT, "components")

SOURCES = {
    "Mesh_Turbine_Long":      os.path.join(COMP, "mechanical", "turbine.blend"),
    "Mesh_Turbine_Short":     os.path.join(COMP, "mechanical", "turbine.blend"),
    "Mesh_Turbine_Ducted":    os.path.join(COMP, "mechanical", "turbine.blend"),
    "Mesh_Turbine_Stub":      os.path.join(COMP, "mechanical", "turbine.blend"),
    "Mesh_Thruster_Tail":     os.path.join(COMP, "mechanical", "thruster_nacelle.blend"),
    "Mesh_Thruster_Vernier":  os.path.join(COMP, "mechanical", "thruster_nacelle.blend"),
    "Mesh_Vent_Scoop":        os.path.join(COMP, "mechanical", "vent_grille.blend"),
    "Mesh_SensorCupola_Radome": os.path.join(COMP, "structural", "sensor_cupola.blend"),
}

D = math.radians
# name, source mesh, location (m, ship frame: nose -Y, ground z=0), rotation euler (rad)
TURBINES = [
    ("Turbine_Long_Port",       "Mesh_Turbine_Long",   (-7.0, 1.5, 4.2), (0, 0, 0)),
    ("Turbine_Long_Stbd",       "Mesh_Turbine_Long",   (7.0, 1.5, 4.2), (0, 0, 0)),
    ("Turbine_Short_Port",      "Mesh_Turbine_Short",  (-5.3, 8.0, 6.6), (0, 0, 0)),
    ("Turbine_Short_Stbd",      "Mesh_Turbine_Short",  (5.3, 8.0, 6.6), (0, 0, 0)),
    ("Turbine_Ducted_Port",     "Mesh_Turbine_Ducted", (-9.6, 4.4, 7.4), (0, 0, 0)),
    ("Turbine_Ducted_Stbd",     "Mesh_Turbine_Ducted", (9.6, 4.4, 7.4), (0, 0, 0)),
    ("Turbine_Stub_RoofPort",   "Mesh_Turbine_Stub",   (-2.3, 11.0, 9.9), (D(180), 0, 0)),
    ("Turbine_Stub_RoofStbd",   "Mesh_Turbine_Stub",   (2.3, 11.0, 9.9), (D(180), 0, 0)),
    ("Turbine_Stub_BellyPort",  "Mesh_Turbine_Stub",   (-3.6, -6.0, 1.9), (0, 0, 0)),
    ("Turbine_Stub_BellyStbd",  "Mesh_Turbine_Stub",   (3.6, -6.0, 1.9), (0, 0, 0)),
]
FITTINGS = [
    ("Thruster_Main_Tail",      "Mesh_Thruster_Tail",     (0.0, 14.6, 7.6), (0, 0, 0)),
    ("RCS_NosePort",            "Mesh_Thruster_Vernier",  (-2.9, -12.0, 4.2), (0, D(90), 0)),
    ("RCS_NoseStbd",            "Mesh_Thruster_Vernier",  (2.9, -12.0, 4.2), (0, D(-90), 0)),
    ("RCS_TailPort",            "Mesh_Thruster_Vernier",  (-2.6, 13.0, 8.4), (0, D(90), 0)),
    ("RCS_TailStbd",            "Mesh_Thruster_Vernier",  (2.6, 13.0, 8.4), (0, D(-90), 0)),
    ("Intake_Scoop_Port",       "Mesh_Vent_Scoop",        (-3.4, -1.5, 8.9), (0, 0, 0)),
    ("Intake_Scoop_Stbd",       "Mesh_Vent_Scoop",        (3.4, -1.5, 8.9), (0, 0, 0)),
    ("Sensor_Radome_Roof",      "Mesh_SensorCupola_Radome", (0.0, -0.5, 9.0), (0, 0, 0)),
]


def append_mesh(name, path):
    if name in bpy.data.meshes:
        return bpy.data.meshes[name]
    with bpy.data.libraries.load(path, link=False) as (src, dst):
        if name not in src.meshes:
            raise SystemExit("%s has no mesh %s" % (path, name))
        dst.meshes = [name]
    return bpy.data.meshes[name]


def place(entries, coll):
    for name, mesh_name, loc, rot in entries:
        if name in bpy.data.objects:
            raise SystemExit("Object %s already exists — refusing to duplicate." % name)
        obj = bpy.data.objects.new(name, append_mesh(mesh_name, SOURCES[mesh_name]))
        obj.location = loc
        obj.rotation_euler = Euler(rot, 'XYZ')
        coll.objects.link(obj)


def main():
    bpy.ops.wm.open_mainfile(filepath=TARGET)
    before = {o.name: (o.matrix_world.copy(), tuple(c.name for c in o.users_collection))
              for o in bpy.data.objects}
    before_colls = {c.name for c in bpy.data.collections}

    turbines = B.collection("Coll_Lander_Turbines")
    fittings = B.collection("Coll_Lander_Fittings")
    place(TURBINES, turbines)
    place(FITTINGS, fittings)

    for name, (mw, colls) in before.items():
        o = bpy.data.objects[name]
        assert o.matrix_world == mw and tuple(c.name for c in o.users_collection) == colls, name
    assert before_colls <= {c.name for c in bpy.data.collections}
    bpy.ops.wm.save_mainfile(filepath=TARGET)
    print("Added %d turbines and %d fittings to %s" % (len(TURBINES), len(FITTINGS), TARGET))


if __name__ == "__main__":
    main()
