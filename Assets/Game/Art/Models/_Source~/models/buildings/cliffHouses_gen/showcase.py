"""Example scene: a small sky town assembled only from collection instances of the pack."""
import bpy, random
from math import sin, cos, pi, tau, atan2, sqrt
from mathutils import Vector
from chlib import S, REG


def build_showcase(world):
    sc = bpy.data.scenes.new("CH_Showcase")
    sc.world = world
    top = bpy.data.collections.new("Showcase")
    sc.collection.children.link(top)
    cnt = {}

    def put(name, loc, rz=0.0, sc_=1.0):
        cnt[name] = cnt.get(name, 0) + 1
        ob = bpy.data.objects.new("Show_%s_%02d" % (name, cnt[name]), None)
        ob.instance_type = 'COLLECTION'
        ob.instance_collection = REG[name].coll
        ob.empty_display_size = 0.5
        ob.location = Vector(loc) * S
        ob.rotation_euler = (0, 0, rz)
        ob.scale = (sc_, sc_, sc_)
        top.objects.link(ob)
        return ob

    def span(name, p0, p1):
        d = Vector(p1) - Vector(p0)
        put(name, p0, atan2(d.y, d.x) - pi / 2)

    zA, zB = 2.5, 4.0
    # ---- island A (main) and terrace B
    put("Cliff_Underside", (0, 0, 0.3), 0.3, 1.25)
    put("Cliff_Cap_Grass", (0, 0, zA))
    put("Cliff_Block_Tall", (9, 3, -6.6), 0.8)
    put("Cliff_Underside", (9, 3, -6.2), 2.0, 1.05)
    put("Cliff_Cap_Grass", (9, 3, zB), pi)
    put("Cliff_Slab_Leaning", (4.5, -3.2, -5.5), 2.4)
    put("Cliff_Ledge_Grass", (14.3, 3.0, zB), pi / 2)
    put("Cliff_Ledge_Grass", (-1.5, 4.2, zA - 0.1), pi)
    put("House_Straw_Cottage", (-2.4, -0.6, zA), -0.5)
    put("House_Clay_Dome", (2.9, 2.1, zA + 0.05), -1.9)
    put("TreeHouse_Hollow", (9.6, 3.2, zB), -1.3)
    put("House_Hanging_Pod", (17.4, 3.0, zB - 2.0), -1.2)
    put("Path_Ramp_Straight", (0.4, -2.6, zA), -pi / 2 + 0.35)
    put("Well_Round", (0.6, -3.4, zA), 0.4, 0.8)
    put("Fire_Bowl", (-0.3, 2.9, zA), 0)
    put("Tree_Oak_Green", (-4.4, 3.2, zA), 0.4)
    put("Tree_Oak_Blossom", (4.6, -3.2, zA), 1.0)
    put("Tree_Oak_Amber", (12.8, -0.4, zB), -0.6)
    put("Tree_Oak_Golden", (12.6, 6.3, zB), 2.0)
    put("Lantern_Post", (-4.8, -2.9, zA), 0.6)
    put("Lantern_Post", (5.6, 0.2, zB), 2.4)
    span("Lantern_String", (-2.4, -0.6, zA + 6.0), (2.9, 2.1, zA + 5.4))
    span("Rope_Span_Bunting", (2.9, 2.1, zA + 5.4), (9.0, 2.4, zB + 5.5))
    put("Rope_Hanging_Cluster", (-3.6, -3.6, zA - 2.6), 0)
    put("Rope_Hanging_Cluster", (12.5, -1.0, zB - 2.8), 2)
    put("Wind_Chime", (15.3, 4.6, zB - 1.6), 0)
    put("Vine_Hanging", (-5.2, 0.6, zA + 0.1), pi / 2)
    put("Vine_Hanging", (1.2, -4.45, zA + 0.1), 0.1)
    put("Vine_Hanging", (9.5, -1.5, zB + 0.1), 0.05)
    put("Glow_Mushrooms", (-3.5, 3.9, zA), 0)
    # ---- platform on a spire, north of A
    put("Cliff_Spire", (-2.0, 9.4, -11.6), 0.5)
    put("Pillar_Wood_Braced", (-2.0, 9.4, zA - 0.3), 0)
    put("Platform_Round_M", (-2.0, 9.4, zA), 0.25)
    put("Market_Stall", (-2.2, 9.9, zA), 0.7)
    put("Path_Walkway_Flat", (-1.7, 3.2, zA), 0.07)
    # ---- island C (south) with the tower, stone bridge
    put("Cliff_Block_Wide", (2.5, -15.5, -5.3), 0.2)
    put("Cliff_Underside", (2.5, -15.5, -5.0), 1.0, 1.2)
    put("Cliff_Cap_Grass", (2.5, -15.5, zA), 0.15)
    put("House_Onion_Tower", (3.2, -16.0, zA), 0.3)
    put("Tree_Oak_Teal", (-1.6, -17.6, zA), 0)
    span("Bridge_Stone_Arch", (1.6, -3.4, zA), (1.6, -13.4, zA))
    # ---- island D (west) spiral tree, straw bridge + big net
    put("Cliff_Stack_Bulb", (-11.0, -12.5, -9.5), 0.7)
    put("TreeHouse_Spiral", (-10.7, -12.3, zA), 0.9)
    span("Bridge_Straw", (-9.2, -10.7, zA), (-4.6, -2.96, zA))
    put("Net_Large", (-5.2, -11.6, zA - 1.0), 0.5)
    # ---- island F (far west) rope bridge
    put("Cliff_Stack_Bulb", (-19.5, 4.5, -9.5), 2.2)
    put("Tree_Oak_Golden", (-19.6, 4.9, zA), 1.0, 0.9)
    put("Lantern_Post", (-18.0, 3.6, zA), 1.0)
    span("Bridge_Rope_Plank", (-5.3, 1.0, zA), (-17.2, 3.9, zA))
    # ---- island E (north) lodge, long net bridge
    put("Cliff_Cap_Grass", (8.5, 30.0, zB), 0.4)
    put("Cliff_Underside", (8.5, 30.0, zB - 2.3), 0.9, 1.25)
    put("House_Mushroom_Lodge", (8.8, 30.3, zB), pi + 0.2)
    put("Tree_Oak_Green", (13.0, 31.5, zB), 2.0)
    span("Bridge_Rope_Net", (9.0, 7.3, zB), (8.6, 25.3, zB))
    # ---- giant tree island (east)
    gx, gy = 25.0, 4.0
    put("Cliff_Cap_Grass", (gx, gy, zA), 0.6, 1.3)
    put("Cliff_Underside", (gx, gy, zA - 2.6), 0.2, 1.45)
    put("GiantTree_Trunk_Base", (gx, gy, zA), 0.3)
    put("GiantTree_Trunk_Section", (gx, gy, zA + 10.0), 1.1)
    put("GiantTree_Crown_Fork", (gx, gy, zA + 18.0), 0.5)
    put("Trunk_Walkway_Spiral", (gx, gy, zA + 3.2), 0.0)
    put("Trunk_Walkway_Spiral", (gx, gy, zA + 7.2), pi)
    put("Trunk_Platform_Ring", (gx, gy, zA + 11.2), 0.0)
    put("Trunk_Door_Hollow", (gx, gy, zA + 11.2), 0.35)
    put("Trunk_Window_Knot", (gx, gy, zA + 13.2), -0.9)
    put("Trunk_Window_Knot", (gx, gy, zA + 7.0), -0.4)
    put("Trunk_Stairs_Pegs", (gx, gy, zA + 11.2), -1.3)
    put("Trunk_Platform_Nest", (gx, gy, zA + 15.2), 0.75)
    put("Trunk_Mushroom_Steps", (gx, gy, zA + 0.2), 0.2)
    for th, z in ((-2.3, 16.5), (0.4, 15.0), (2.4, 17.0)):
        put("GiantTree_Limb_Long" if z < 16.8 else "GiantTree_Limb_Rising", (gx + cos(th) * 2.5, gy + sin(th) * 2.5, zA + z), th - pi / 2)
    put("GiantTree_Canopy_Shelf", (gx + 1, gy, zA + 27.0), 0.3)
    put("GiantTree_Canopy_Shelf", (gx - 3, gy + 2, zA + 30.0), 2.0, 0.8)
    put("GiantTree_Canopy_Clump", (gx + 5, gy - 4, zA + 25.0), 1.0)
    put("GiantTree_Canopy_Clump", (gx - 6, gy - 2, zA + 24.0), 2.5)
    put("Banner_Ribbon_Spiral", (gx - 4.6, gy - 5.2, zA + 16.3), 0)
    put("Lantern_Cluster", (gx + 0.5, gy - 8.5, zA + 16.6), 0)
    put("GiantTree_Root_Arch", (gx - 5.5, gy - 5.5, zA), 0.8, 0.8)
    put("Palm_Small", (gx + 5.5, gy - 4.5, zA), 2.0)
    put("Plant_Broadleaf", (gx - 2.5, gy - 6.2, zA), 0.5)
    put("Fern_Cluster", (gx + 2.0, gy - 6.0, zA), 0)
    put("Flower_Giant", (gx + 3.6, gy - 6.3, zA), 1.0)
    put("Gate_Arch_Town", (gx - 0.5, gy - 6.8, zA), 0.1)
    put("Water_Tower_Barrel", (11.5, 33.0, zB), 0.4)
    put("Landing_Pier", (4.5, -19.6, zA), 0.25)
    put("Fountain_Clay", (-1.8, 2.6, zA), 0, 0.6)
    put("Airship_Small", (-12.0, 16.0, zA + 9.0), -0.6)
    put("Birds_Flock", (10.0, -8.0, zA + 16.0), 0.4)
    put("Birds_Flock", (-16.0, -2.0, zA + 11.0), 2.0)
    put("Waterfall_Cliff", (5.2, -0.9, zA - 0.05), 1.2)
    put("Lanterns_Floating", (1.0, -9.0, zA + 1.0), 0)
    put("Kite_String", (-18.8, 5.6, zA), 0.7)
    put("Windmill_Round", (13.0, 28.0, zB), 2.4)
    put("Tower_Watch_Stilts", (-2.0, 9.4, zA), 0.2) if False else None
    # ---- scatter plants
    rnd = random.Random(7)
    plants = ["Bush_Round", "Bush_Flowering", "Grass_Patch", "Grass_Tuft", "Flowers_Sunny", "Flowers_Poppy", "Flowers_Bells", "Grass_Patch"]
    caps = [((0, 0), zA, 0.0, [(-2.4, -0.6, 3.1), (2.9, 2.1, 3.1), (0.6, -3.4, 1.2)]), ((9, 3), zB, pi, [(9.6, 3.2, 4.2)]),
            ((2.5, -15.5), zA, 0.15, [(3.2, -16.0, 3.4)]), ((8.5, 30.0), zB, 0.4, [(8.8, 30.3, 4.4)])]
    for (cx, cy), z, rz, avoid in caps:
        made = 0
        tries = 0
        while made < 16 and tries < 400:
            tries += 1
            ang, d = rnd.uniform(0, tau), sqrt(rnd.random())
            x, y = cos(ang) * d * 4.9, sin(ang) * d * 3.9
            wx, wy = cx + x * cos(rz) - y * sin(rz), cy + x * sin(rz) + y * cos(rz)
            if any((wx - ax) ** 2 + (wy - ay) ** 2 < ar * ar for ax, ay, ar in avoid):
                continue
            put(plants[made % len(plants)], (wx, wy, z + 0.02), rnd.uniform(0, tau), rnd.uniform(0.8, 1.2))
            made += 1
    # ---- camera + sun
    cd = bpy.data.cameras.new("Cam_Showcase")
    cd.lens = 30
    cam = bpy.data.objects.new("Cam_Showcase", cd)
    sc.collection.objects.link(cam)
    cam.location = Vector((-22, -74, 20.0)) * S
    tgt = Vector((7.0, 0.5, 10.0)) * S
    cam.rotation_euler = (cam.location - tgt).to_track_quat('Z', 'Y').to_euler()
    cd.clip_end = 2000
    sc.camera = cam
    sd = bpy.data.lights.new("Sun_Showcase", 'SUN')
    sd.energy = 3.0
    sd.angle = 0.05
    sun = bpy.data.objects.new("Sun_Showcase", sd)
    sun.rotation_euler = (0.95, 0.0, 0.55)
    sc.collection.objects.link(sun)
    sc.render.resolution_x, sc.render.resolution_y = 1280, 1600
    sc.render.engine = 'BLENDER_EEVEE'
    return sc
