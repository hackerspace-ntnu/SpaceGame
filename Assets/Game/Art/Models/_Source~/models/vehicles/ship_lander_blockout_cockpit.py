"""Add the cockpit set to `ship_lander_blockout.blend` and clear the canopy.

Purely additive on geometry: one new collection (`Coll_Lander_Cockpit`) holds
appended copies of three library components placed on the pilot dais under the
canopy dome (dais floor z=4.04, y -9.7..-7.9, probed by ray-cast). Nothing
pre-existing is moved, renamed or removed; the script asserts that before
saving.

The one authorized material change: `Mat_Lander_Canopy` (the Icosphere dome's
glass) is made see-through so the cockpit reads from outside.

Components used (appended, so they are editable here):
    components/props/console_panel.blend   Mesh_ConsolePanel_Bridge
    components/props/steering_yoke.blend   Mesh_SteeringYoke_Wheel
    components/props/crew_seat.blend       Mesh_CrewSeat_Command

    blender --background --python ship_lander_blockout_cockpit.py
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
PROPS = os.path.join(B.LIB_ROOT, "components", "props")

SOURCES = {
    "Mesh_ConsolePanel_Bridge": os.path.join(PROPS, "console_panel.blend"),
    "Mesh_SteeringYoke_Wheel":  os.path.join(PROPS, "steering_yoke.blend"),
    "Mesh_CrewSeat_Command":    os.path.join(PROPS, "crew_seat.blend"),
}

D = math.radians
# name, source mesh, location (m, ship frame: nose -Y, deck z=4.04), rotation.
# Components author +X as forward, so Rz(-90) turns them to face the nose;
# the yoke authors its wheel face +Z / column -Y, so (78, 0, 180) stands the
# column near-vertical with the face raked 12 degrees up at the pilot.
COCKPIT = [
    ("Cockpit_Console_Bridge", "Mesh_ConsolePanel_Bridge",
     (-0.15, -9.45, 4.04), (0, 0, D(-90))),
    ("Cockpit_Steering_Wheel", "Mesh_SteeringYoke_Wheel",
     (-0.15, -8.98, 4.77), (D(78), 0, D(180))),
    ("Cockpit_Seat_Command",   "Mesh_CrewSeat_Command",
     (-0.15, -8.28, 4.04), (0, 0, D(-90))),
]


def append_mesh(name, path):
    if name in bpy.data.meshes:
        return bpy.data.meshes[name]
    with bpy.data.libraries.load(path, link=False) as (src, dst):
        if name not in src.meshes:
            raise SystemExit("%s has no mesh %s" % (path, name))
        dst.meshes = [name]
    return bpy.data.meshes[name]


def clear_canopy():
    mat = bpy.data.materials.get("Mat_Lander_Canopy")
    if mat is None:
        raise SystemExit("Mat_Lander_Canopy not found — glass edit aborted.")
    bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs["Alpha"].default_value = 0.30
    bsdf.inputs["Transmission Weight"].default_value = 0.60
    bsdf.inputs["Roughness"].default_value = 0.05
    mat.blend_method = 'BLEND'
    mat.use_backface_culling = False


def main():
    bpy.ops.wm.open_mainfile(filepath=TARGET)
    before = {o.name: (o.matrix_world.copy(), tuple(c.name for c in o.users_collection))
              for o in bpy.data.objects}
    before_colls = {c.name for c in bpy.data.collections}

    coll = B.collection("Coll_Lander_Cockpit")
    for name, mesh_name, loc, rot in COCKPIT:
        if name in bpy.data.objects:
            raise SystemExit("Object %s already exists — refusing to duplicate." % name)
        obj = bpy.data.objects.new(name, append_mesh(mesh_name, SOURCES[mesh_name]))
        obj.location = loc
        obj.rotation_euler = Euler(rot, 'XYZ')
        coll.objects.link(obj)

    clear_canopy()

    for name, (mw, colls) in before.items():
        o = bpy.data.objects[name]
        assert o.matrix_world == mw and tuple(c.name for c in o.users_collection) == colls, name
    assert before_colls <= {c.name for c in bpy.data.collections}
    bpy.ops.wm.save_mainfile(filepath=TARGET)
    print("Added %d cockpit pieces and cleared the canopy in %s"
          % (len(COCKPIT), TARGET))


if __name__ == "__main__":
    main()
