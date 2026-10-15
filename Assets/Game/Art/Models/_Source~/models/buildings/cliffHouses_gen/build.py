"""cliffHouses asset pack generator.  Run:  blender --background --python build.py -- <out.blend> [--no-previews]
Creates a NEW file every time. Never run it over a hand-edited cliffHouses.blend - the .blend is the source of truth."""
import bpy, sys, os, importlib
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.join(HERE, "cliffHouses.blend")
ONLY = None
for x in argv:
    if x.startswith("--only="):
        ONLY = set(x[7:].split(","))
PREV = "--no-previews" not in argv
THUMBS = os.path.join(os.path.dirname(OUT), "_thumbs")

bpy.ops.wm.read_factory_settings(use_empty=True)
import chlib
from chlib import *
MODS = ["assets_kit", "assets_props", "assets_nature", "assets_structures", "assets_buildings", "assets_more", "assets_more2"]
for m in MODS:
    if os.path.exists(os.path.join(HERE, m + ".py")):
        importlib.import_module(m)

scene = bpy.context.scene
scene.name = "CliffHouses"
scene.unit_settings.system = 'METRIC'
build_all(ONLY)

# ---- lay the assets out in rows per category -------------------------------------------------
CAT_ORDER = ["Buildings", "Building Kit", "Giant Tree", "Bridges", "Platforms & Paths", "Nets & Ropes", "Pillars", "Cliff & Rocks",
             "Trees", "Plants", "Lights", "Decorations", "Interior"]
y = 0.0
for cat in CAT_ORDER:
    row = [a for a in ORDER if a.cat == cat]
    if not row:
        continue
    x = 0.0
    depth = 0.0
    boxes = [bbox(a) for a in row]
    depth = max(hi.y - lo.y for lo, hi in boxes)
    y -= depth / 2 + 4
    for a, (lo, hi) in zip(row, boxes):
        w = hi.x - lo.x
        a.root.location = (x - lo.x, y - (lo.y + hi.y) / 2, 0)
        x += w + 3.0
    y -= depth / 2 + 4
bpy.context.view_layer.update()

# ---- mark as assets ----------------------------------------------------------------------------
cats = sorted({a.cat for a in ORDER})
with open(os.path.join(os.path.dirname(OUT), "cliffHouses_catalogs.txt"), "w") as f:
    f.write("%s:CliffHouses:CliffHouses\n" % cat_uuid("__root__"))
    for c in CAT_ORDER:
        if c in cats:
            f.write("%s:CliffHouses/%s:CliffHouses-%s\n" % (cat_uuid(c), c, c))
for a in ORDER:
    c = a.coll
    c.instance_offset = a.root.location
    c.asset_mark()
    ad = c.asset_data
    ad.catalog_id = cat_uuid(a.cat)
    ad.description = a.desc
    ad.author = "Claude for Ferdinand"
    for t in ("CliffHouses", a.cat) + tuple(a.tags):
        ad.tags.new(t, skip_if_exists=True)

# ---- world -------------------------------------------------------------------------------------
w = bpy.data.worlds.new("World_CliffHouses")
w.use_nodes = True
bg = w.node_tree.nodes["Background"]
bg.inputs[0].default_value = (0.62, 0.78, 1.0, 1)
bg.inputs[1].default_value = 0.8
scene.world = w

# ---- previews ----------------------------------------------------------------------------------
def render_previews():
    import numpy as np
    os.makedirs(THUMBS, exist_ok=True)
    ps = bpy.data.scenes.new("CH_PreviewTmp")
    ps.world = w
    ps.render.engine = 'CYCLES'
    ps.cycles.samples = 16
    ps.cycles.use_denoising = False
    ps.cycles.max_bounces = 2
    ps.render.resolution_x = ps.render.resolution_y = 256
    ps.render.film_transparent = True
    ps.render.image_settings.file_format = 'PNG'
    ps.render.image_settings.color_mode = 'RGBA'
    ps.view_settings.view_transform = 'Standard'
    cd = bpy.data.cameras.new("CH_PrevCam"); cd.type = 'ORTHO'
    cam = bpy.data.objects.new("CH_PrevCam", cd); ps.collection.objects.link(cam); ps.camera = cam
    sd = bpy.data.lights.new("CH_PrevSun", 'SUN'); sd.energy = 2.4; sd.angle = 0.2
    sun = bpy.data.objects.new("CH_PrevSun", sd); ps.collection.objects.link(sun)
    sun.rotation_euler = (0.95, 0.0, 0.75)
    d = Vector((0.8, -1.0, 0.62)).normalized()
    for a in ORDER:
        ps.collection.children.link(a.coll)
        lo, hi = bbox(a)
        ctr = (lo + hi) / 2
        cam.rotation_euler = d.to_track_quat('Z', 'Y').to_euler()
        bpy.context.view_layer.update()
        rot = cam.rotation_euler.to_matrix()
        rx, ry = rot @ Vector((1, 0, 0)), rot @ Vector((0, 1, 0))
        ext = 0.0
        for cx in (lo.x, hi.x):
            for cy in (lo.y, hi.y):
                for cz in (lo.z, hi.z):
                    v = Vector((cx, cy, cz)) - ctr
                    ext = max(ext, abs(v.dot(rx)), abs(v.dot(ry)))
        cd.ortho_scale = ext * 2.12
        cam.location = ctr + d * ((hi - lo).length * 2 + 20)
        cd.clip_end = (hi - lo).length * 6 + 100
        path = os.path.join(THUMBS, a.name + ".png")
        ps.render.filepath = path
        bpy.ops.render.render(write_still=True, scene=ps.name)
        ps.collection.children.unlink(a.coll)
        img = bpy.data.images.load(path)
        px = np.empty(256 * 256 * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        pv = a.coll.preview_ensure()
        pv.image_size = (256, 256)
        pv.image_pixels_float.foreach_set(px)
        bpy.data.images.remove(img)
    bpy.data.objects.remove(cam); bpy.data.objects.remove(sun)
    bpy.data.cameras.remove(cd); bpy.data.lights.remove(sd)
    bpy.data.scenes.remove(ps)

if PREV:
    render_previews()

if "--no-showcase" not in argv and ONLY is None:
    import showcase
    show = showcase.build_showcase(w)
    if "--render-showcase" in argv:
        show.render.engine = 'CYCLES'
        show.cycles.samples = 48
        show.cycles.max_bounces = 3
        show.view_settings.view_transform = 'Standard'
        show.render.resolution_percentage = 60
        show.render.filepath = os.path.join(os.path.dirname(OUT), "showcase.png")
        bpy.ops.render.render(write_still=True, scene=show.name)
        show.render.resolution_percentage = 100
        show.render.engine = 'BLENDER_EEVEE'
        show.render.filepath = "//cliffHouses_showcase.png"

tris = 0
for a in ORDER:
    t = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in a.coll.objects if o.type == 'MESH')
    tris += t
    print("ASSET %-28s %-18s parts=%2d tris=%5d" % (a.name, a.cat, len(a.coll.objects) - 1, t))
print("TOTAL assets", len(ORDER), "tris", tris, "materials", len(bpy.data.materials))
bpy.ops.wm.save_as_mainfile(filepath=OUT, compress=True)
print("SAVED", OUT)
