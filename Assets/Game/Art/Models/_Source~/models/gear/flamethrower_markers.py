"""Put the flamethrower's `Marker_*` meshes back onto the parts they describe.

A marker is a 4 mm mesh that tells Unity where the bore mouth, the pilot flame, the hands and the
supply gauge are (`flamethrower_BUILD.md`). A hand edit in Blender moves, turns and rescales the
parts; the markers do not follow, and nothing anywhere reports it. What you get is a flame that
leaves from inside the barrel and a gauge bar hanging in the air behind the grip — which is exactly
what the 2026-09-09 edit produced.

So this recomputes every marker from the part it belongs to, and is meant to be re-run after any
hand edit, before `flamethrower_export.py`:

    blender --background models/gear/flamethrower.blend --python models/gear/flamethrower_markers.py

`SEATS` records where each marker sat inside its part's bounding box in the generated 2026-09-07
build, as a fraction per axis. The fraction is the intent — "just inside the collar's rim", "on the
gauge face", "in the core of the grip" — and it survives a part being moved, turned or resized. The
marker also takes the part's own rotation and scale, so the marker is a faithful proxy of the part:
Unity seats the gauge bar in the marker's frame and scales it by the marker's scale, which only
works if the marker carries what the part carries.

`Marker_GripFore` is the one marker whose part is gone — the 2026-09-09 edit deleted
`Mesh_Flamethrower_Foregrip` — so the support hand is placed against the shell it now grips
directly. Nothing in Unity reads it today; it is kept because the item is held two-handed and the
next pose pass will want it.

Writes the .blend in place. It only ever assigns object transforms, so no mesh data is touched.
"""

import bpy
from mathutils import Vector

# marker -> (part it belongs to, fraction of that part's bounds it sat at in the generated build)
SEATS = {
    "Marker_Muzzle": ("Mesh_Flamethrower_Muzzle", (0.553, 0.193, 0.500)),
    "Marker_Pilot": ("Mesh_Flamethrower_Pilot", (0.500, 0.056, 0.500)),
    "Marker_Grip": ("Mesh_Flamethrower_Grip", (0.500, 0.644, 0.560)),
    "Marker_Gauge": ("Mesh_Flamethrower_Gauge", (0.800, 0.500, 0.500)),
    "Marker_GripFore": ("Mesh_Flamethrower_Shell", (0.500, 0.059, -0.304)),
}


def seat(part, fraction):
    """The point at `fraction` of the part's OWN box, in world space.

    Its own box, not its world-aligned one: a part the artist turned has an axis-aligned bounding
    box that is bigger than the part and no longer square to its faces, so "0.8 of the way across"
    measured there lands beside the gauge rather than on it. The generated build had every part at
    rotation zero, which is why the two agreed then and stopped agreeing on 2026-09-09.
    """
    corners = [Vector(c) for c in part.bound_box]
    lo = Vector((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)))
    hi = Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))
    local = Vector([lo[i] + fraction[i] * (hi[i] - lo[i]) for i in range(3)])
    return part.matrix_world @ local


def main():
    missing = []
    for marker_name, (part_name, fraction) in SEATS.items():
        marker = bpy.data.objects.get(marker_name)
        part = bpy.data.objects.get(part_name)
        if marker is None or part is None:
            missing.append(marker_name if marker is None else part_name)
            continue

        was = marker.matrix_world.translation.copy()
        marker.location = seat(part, fraction)
        marker.rotation_euler = part.rotation_euler.copy()
        marker.scale = part.scale.copy()
        print("  %-20s %s  ->  %s   on %s"
              % (marker_name,
                 tuple(round(v, 4) for v in was),
                 tuple(round(v, 4) for v in marker.location),
                 part_name))

    if missing:
        raise SystemExit("Missing object(s): %s" % ", ".join(missing))

    bpy.ops.wm.save_mainfile()
    print("  saved %s" % bpy.data.filepath)


main()
