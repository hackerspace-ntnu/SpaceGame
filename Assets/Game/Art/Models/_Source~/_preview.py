"""Render a quick turntable/ortho preview of whatever .blend it is run against.

Verification tool for the library's generation scripts: build a component, look
at it, fix it, move on. Opens an existing file read-only and never saves, so it
is safe to point at hand-edited work.

    blender --background <file.blend> --python _preview.py -- \
        --out /tmp/shot.png [--view top|front|side|iso] [--res 900]
        [--only Coll_Name] [--spread]
"""

import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def arg(flag, default=None):
    return argv[argv.index(flag) + 1] if flag in argv else default


OUT = arg("--out", "/tmp/preview.png")
VIEW = arg("--view", "iso")
RES = int(arg("--res", 900))
ONLY = arg("--only")
SPREAD = "--spread" in argv

scene = bpy.context.scene

# Lay collections out in a row so every variation is visible in one shot,
# instead of all of them stacked at the origin.
if SPREAD:
    colls = [c for c in bpy.data.collections if c.objects]
    cursor = 0.0
    for c in colls:
        objs = [o for o in c.objects if o.type == 'MESH']
        if not objs:
            continue
        xs = [o.matrix_world @ Vector(corner)
              for o in objs for corner in o.bound_box]
        lo = min(v.x for v in xs)
        hi = max(v.x for v in xs)
        shift = cursor - lo
        for o in objs:
            o.location.x += shift
        cursor += (hi - lo) + 0.45
        # matrix_world is cached; without this the next collection measures
        # stale bounds and the final framing misses everything that moved.
        bpy.context.view_layer.update()

if ONLY:
    keep = {o.name for o in bpy.data.collections[ONLY].objects}
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.name not in keep:
            o.hide_render = True

bpy.context.view_layer.update()
visible = [o for o in bpy.data.objects
           if o.type == 'MESH' and not o.hide_render]
pts = [o.matrix_world @ Vector(c) for o in visible for c in o.bound_box]
lo = Vector((min(p.x for p in pts), min(p.y for p in pts),
             min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts),
             max(p.z for p in pts)))
mid = (lo + hi) / 2
span = max((hi - lo).x, (hi - lo).y, (hi - lo).z, 0.5)

DIRS = {
    "top":   Vector((0.0, 0.0, 1.0)),
    "front": Vector((0.0, -1.0, 0.0)),
    "side":  Vector((1.0, 0.0, 0.0)),
    "iso":   Vector((0.85, -0.95, 0.62)).normalized(),
}
d = DIRS.get(VIEW, DIRS["iso"])

cam_data = bpy.data.cameras.new("Cam_Preview")
cam_data.type = 'ORTHO'
cam_data.ortho_scale = span * 1.16
cam = bpy.data.objects.new("Cam_Preview", cam_data)
scene.collection.objects.link(cam)
cam.location = mid + d * span * 3.0
cam.rotation_mode = 'QUATERNION'
cam.rotation_quaternion = d.to_track_quat('Z', 'Y')
scene.camera = cam

key = bpy.data.lights.new("Light_Key", type='SUN')
key.energy = 4.0
key.angle = math.radians(12)
ko = bpy.data.objects.new("Light_Key", key)
ko.rotation_euler = (math.radians(52), 0, math.radians(38))
scene.collection.objects.link(ko)

fill = bpy.data.lights.new("Light_Fill", type='SUN')
fill.energy = 1.4
fo = bpy.data.objects.new("Light_Fill", fill)
fo.rotation_euler = (math.radians(64), 0, math.radians(-135))
scene.collection.objects.link(fo)

world = bpy.data.worlds.new("World_Preview")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (
    0.42, 0.40, 0.36, 1.0)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.75
scene.world = world

scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = RES
scene.render.resolution_y = RES
scene.render.film_transparent = False
scene.render.filepath = os.path.abspath(OUT)
scene.render.image_settings.file_format = 'PNG'
bpy.ops.render.render(write_still=True)
print("Preview -> %s" % OUT)
