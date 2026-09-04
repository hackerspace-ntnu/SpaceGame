"""Repair station — the lander's scrap-fed workstation, assembled.

`components/props/repair_bench.py`'s Bulkhead bench with a
`components/props/scrap_hopper.py` Chute standing on its worktop, plus one
marker cube at the gauge screen's face for Unity to hang the progress readout
on. Nothing is modelled here; every part is appended from its component file
and renamed to the ROLE it plays on the station, because the Unity builder
(`RepairStationBuilder`) finds parts by these names:

| Object | Role in `RepairWorkstation` |
|---|---|
| `Mesh_RepairStation_Spindle`   | `spinningParts` — turns about its local X once online |
| `Mesh_RepairStation_StatusLamp`| `statusLight` — retinted broken -> repaired |
| `Mesh_RepairStation_HopperLid` | `clunkTarget` — nudged when scrap is accepted |
| `Marker_RepairStation_Gauge`   | where `RepairProgressUI` is drawn |

Origin at deck level, centre of the footprint, facing -Y (Unity +Z after export).

    blender --background --python repair_station.py -- --out repair_station.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import os
import sys

import bpy
from mathutils import Vector

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)

from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

BENCH = os.path.join(_LIB, "components", "props", "repair_bench.blend")
HOPPER = os.path.join(_LIB, "components", "props", "scrap_hopper.blend")

BENCH_PARTS = {
    "Mesh_RepairBench_Cabinet_Bulkhead": "Mesh_RepairStation_Cabinet",
    "Mesh_RepairBench_Worktop_Bulkhead": "Mesh_RepairStation_Worktop",
    "Mesh_RepairBench_BackPanel_Bulkhead": "Mesh_RepairStation_BackPanel",
    "Mesh_RepairBench_SpindleHousing_Bulkhead": "Mesh_RepairStation_SpindleHousing",
    "Mesh_RepairBench_Spindle_Bulkhead": "Mesh_RepairStation_Spindle",
    "Mesh_RepairBench_StatusLamp_Bulkhead": "Mesh_RepairStation_StatusLamp",
    "Mesh_RepairBench_Vice_Bulkhead": "Mesh_RepairStation_Vice",
}
HOPPER_PARTS = {
    "Mesh_ScrapHopper_Chute": "Mesh_RepairStation_Hopper",
    "Mesh_ScrapHopper_ChuteLid": "Mesh_RepairStation_HopperLid",
}

# The chute stands on the worktop (top at 0.92) sunk 5 mm so its base plate is
# not coplanar with the steel, centred between the spindle and the vice.
HOPPER_AT = Vector((0.0, 0.04, 0.915))

# Front face of the gauge screen, from repair_bench.py's gauge_bezel: panel
# front 0.23, frame 0.016 deep and 1 mm embedded, screen 1.5 mm proud of it.
GAUGE_FACE = (0.0, 0.23 - 0.016 + 0.001 - 0.0015, 1.42)


def append_objects(blend, names, into):
    """Append (not link) named objects from a component file — an export needs
    real mesh data, and a linked object arrives as a proxy the FBX writer skips.
    Same helper as dragon_bazooka.py, including the depsgraph update: a freshly
    appended object reports the identity matrix until the view layer updates."""
    with bpy.data.libraries.load(blend, link=False) as (src, dst):
        missing = [n for n in names if n not in set(src.objects)]
        if missing:
            raise SystemExit("Not in %s: %s" % (blend, ", ".join(missing)))
        dst.objects = list(names)
    out = []
    for name in names:
        obj = bpy.data.objects[name]
        into.objects.link(obj)
        out.append(obj)
    bpy.context.view_layer.update()
    return out


def marker(coll, mats, at, name):
    """0.004 m cube whose ORIGIN is the point — a consumer reads the node's
    transform, and a marker whose position is all in its vertices reads back as
    (0,0,0). Same convention as holo_base's Marker_HoloAnchor_*."""
    p = Part(mats)
    p.box(at, (0.004, 0.004, 0.004), 0)
    return p.finish(name, coll, origin=at)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(["Mat_Metal_Steel_Dark"])
    coll = collection("Coll_RepairStation")

    for obj in append_objects(BENCH, list(BENCH_PARTS), coll):
        obj.name = BENCH_PARTS[obj.name]

    for obj in append_objects(HOPPER, list(HOPPER_PARTS), coll):
        obj.name = HOPPER_PARTS[obj.name]
        obj.location = obj.location + HOPPER_AT

    marker(coll, mats, GAUGE_FACE, "Marker_RepairStation_Gauge")

    save(out)
    report()


if __name__ == "__main__":
    main()
