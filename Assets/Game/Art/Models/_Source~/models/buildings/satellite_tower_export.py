"""Export satellite_tower.blend -> satellite_tower.fbx, baking its procedural look into texture atlases.

    blender --background --python satellite_tower_export.py
    blender --background --python satellite_tower_export.py -- Interior Shack   # re-bake only these atlases

With atlas names after `--`, every group is still unwrapped (the FBX needs every UV) but only the named
atlases are baked; the others keep their PNGs, which match because Smart UV Project is deterministic on
unchanged geometry. Re-bake an atlas whenever any of ITS objects changed shape.

Reads only: the .blend is never saved. Everything below happens in memory, inside `_exportlib.export`'s
`prepare` hook, on the file it has just opened.

The tower's materials are LOCAL procedural `MAT_SatTower_*` node trees driven by world-position noise, and the
model has no UVs at all, so nothing about its look survives an FBX on its own. The export therefore:

  1. Freezes the rig. Drivers and Limit Rotation constraints do not export, and the ORIGINAL object's matrix
     ignores a driven rotation (the dish's 50-degree elevation lives only in the evaluated copy), so every
     object's evaluated world matrix is read first and written back once the drivers are gone.
  2. Converts every curve and every modifier stack (Solidify, the cables' Hooks) to plain mesh data.
  3. Gives the door leaf a static frame to hang off: `SatTower_Int_Door`, an unrotated empty at the hinge, is
     the hinge's new parent, so the Unity `DoorInteraction` has an object of its own that never moves.
  4. UV-unwraps each atlas group into one shared UV space (Smart UV Project, islands scaled to their 3D area,
     then packed), and bakes base colour, metallic, roughness and a tangent-space normal (from the bump) with
     Cycles. Colour, metallic and roughness are baked as EMISSION of the Principled input's own source, so a
     metal's base colour is not darkened the way the Diffuse Color pass darkens it.
  5. Writes three PNGs per atlas under `Assets/Game/Art/Textures/Environment/SatelliteTower/`:
     `_BaseColor` (sRGB), `_MetallicSmoothness` (URP Lit's map: metallic in RGB, 1 - roughness in A) and
     `_Normal` (OpenGL tangent space, which is what Unity reads).
  6. Replaces every baked material with one plain material per atlas (`SatTower_<Atlas>`). Glass and the
     emissive materials (screens, bulbs, stove, lamp) are not baked and keep their own names. The FBX carries
     NO texture references: Unity remaps every material by name onto the URP `.mat` assets in
     `Assets/Game/Art/Materials/Environment/SatelliteTower/` (the importer's remap, as `raxy.fbx` does), because
     `_exportlib` writes with `path_mode='COPY'`, which would copy the PNGs a second time beside the FBX.
"""
import os
import struct
import sys
import zlib

import bmesh
import bpy
import numpy as np
from mathutils import Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))
import _exportlib  # noqa: E402

SRC = os.path.join(HERE, "satellite_tower.blend")
DST = _exportlib.unity_path("Environment", "Structures", "SatelliteTower", "satellite_tower.fbx")
TEXTURES = os.path.join(_exportlib.REPO_ROOT, "Assets", "Game", "Art", "Textures", "Environment", "SatelliteTower")

# Atlas edge in pixels, per atlas: 4096 where 2048 would fall under ~40 px/m on the 1.8x tower.
ATLAS_SIZE = {
    "BaseConcrete": 4096,
    "BaseMetal": 2048,
    "Pedestal": 4096,
    "DishPanels": 4096,
    "DishFrame": 4096,
    "Shack": 2048,
    "Shanties": 2048,
    "Interior": 4096,
}
BAKE_SAMPLES = 4
BAKE_MARGIN_PX = 8
BAKE_TILE_PX = 1024
PACK_MARGIN = 0.003          # UV-space fraction between islands, about 6 px at 2048
SMART_ANGLE_DEG = 66.0

# Object-name prefix -> atlas. Order matters: the first prefix that matches wins.
ATLASES = [
    ("SatTower_Base_Concrete", "BaseConcrete"),
    ("SatTower_Base_", "BaseMetal"),
    ("SatTower_Damage_Debris", "BaseMetal"),
    ("SatTower_Pedestal_", "Pedestal"),
    ("SatTower_Dish_Panel_", "DishPanels"),
    ("SatTower_Dish_Hub", "DishPanels"),
    ("SatTower_Dish_", "DishFrame"),
    ("SatTower_Feed_", "DishFrame"),
    ("SatTower_Cable_", "DishFrame"),
    ("SatTower_Damage_HangStraps", "DishFrame"),
    ("SatTower_Shack_", "Shack"),
    ("SatTower_Shanty_", "Shanties"),
    ("SatTower_Int_", "Interior"),
]

# Panels the source left on a stray material; the dish paint is what they are meant to wear.
STRAY_MATERIALS = {"Material", "MAT_SatTower_DishPaint.001"}
DISH_PAINT = "MAT_SatTower_DishPaint"

DOOR_HINGE = "SatTower_Int_DoorHinge"
DOOR_FRAME = "SatTower_Int_Door"
ROOT = "SatTower_Root"
DROP = {"SatTower_Rig_Control"}


def atlas_of(name):
    for prefix, atlas in ATLASES:
        if name.startswith(prefix):
            return atlas
    return None


def principled(mat):
    return next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")


def is_simple(mat):
    """Glass and lights stay their own materials: a bake would flatten what makes them read."""
    bsdf = principled(mat)
    alpha = bsdf.inputs["Alpha"]
    transmission = bsdf.inputs["Transmission Weight"]
    emission = bsdf.inputs["Emission Strength"]
    colour = bsdf.inputs["Emission Color"].default_value
    see_through = (not alpha.is_linked and alpha.default_value < 0.999) or (
        not transmission.is_linked and transmission.default_value > 0.001)
    glowing = (emission.is_linked or emission.default_value > 0.0) and max(colour[:3]) > 0.0
    return see_through or glowing


# ── 1-3: freeze the rig and flatten everything to meshes ──────────────────────────────────────────

def depth(obj):
    d = 0
    while obj.parent is not None:
        obj, d = obj.parent, d + 1
    return d


def freeze():
    dg = bpy.context.evaluated_depsgraph_get()
    worlds = {o.name: o.evaluated_get(dg).matrix_world.copy() for o in bpy.data.objects}
    meshes = {}
    for o in bpy.data.objects:
        if o.type in {"MESH", "CURVE"}:
            meshes[o.name] = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True,
                                                            depsgraph=dg)

    for o in list(bpy.data.objects):
        if o.name in DROP or o.type == "LIGHT":
            bpy.data.objects.remove(o, do_unlink=True)
    for o in bpy.data.objects:
        o.animation_data_clear()
        for c in list(o.constraints):
            o.constraints.remove(c)
        o.delta_rotation_euler = (0.0, 0.0, 0.0)

    for name, me in meshes.items():
        o = bpy.data.objects[name]
        if o.type == "MESH":
            o.modifiers.clear()
            o.data = me
            continue
        # A curve becomes a mesh object of the same name, in the same place in the hierarchy.
        o.name = name + "__curve"
        mo = bpy.data.objects.new(name, me)
        for coll in o.users_collection:
            coll.objects.link(mo)
        mo.parent = o.parent
        for child in list(o.children):
            child.parent = mo
        bpy.data.objects.remove(o, do_unlink=True)

    for o in sorted(bpy.data.objects, key=depth):
        o.matrix_world = worlds[o.name]
        bpy.context.view_layer.update()

    for o in bpy.data.objects:
        if o.type != "MESH":
            continue
        for slot in o.material_slots:
            if slot.material is not None and slot.material.name in STRAY_MATERIALS:
                slot.material = bpy.data.materials[DISH_PAINT]

    hinge = bpy.data.objects[DOOR_HINGE]
    frame = bpy.data.objects.new(DOOR_FRAME, None)
    for coll in hinge.users_collection:
        coll.objects.link(frame)
    frame.parent = bpy.data.objects[ROOT]
    frame.matrix_world = Matrix.Translation(hinge.matrix_world.translation)
    bpy.context.view_layer.update()
    world = hinge.matrix_world.copy()
    hinge.parent = frame
    hinge.matrix_world = world
    bpy.context.view_layer.update()

    for me in {o.data for o in bpy.data.objects if o.type == "MESH"}:
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.to_mesh(me)
        bm.free()


# ── 4: unwrap ─────────────────────────────────────────────────────────────────────────────────────

def groups():
    out = {}
    for o in bpy.data.objects:
        if o.type != "MESH" or not o.data.polygons:
            continue
        atlas = atlas_of(o.name)
        if atlas is None:
            raise SystemExit("No atlas for %s" % o.name)
        if any(s.material is not None and not is_simple(s.material) for s in o.material_slots):
            out.setdefault(atlas, []).append(o)
    return out


def select_only(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def unwrap(objs):
    for o in objs:
        if not o.data.uv_layers:
            o.data.uv_layers.new(name="UVMap")
    select_only(objs)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=np.radians(SMART_ANGLE_DEG), island_margin=0.0,
                             area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.select_all(action="SELECT")
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(rotate=True, margin_method="FRACTION", margin=PACK_MARGIN)
    bpy.ops.object.mode_set(mode="OBJECT")


def world_area(objs):
    total = 0.0
    for o in objs:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bm.transform(o.matrix_world)
        total += sum(f.calc_area() for f in bm.faces)
        bm.free()
    return total


# ── 4-5: bake ─────────────────────────────────────────────────────────────────────────────────────
#
# Baked in BAKE_TILE_PX tiles rather than in one go. Cycles allocates 76 bytes per pixel of the target image
# for a bake whatever its tile size setting, so a 2048 atlas asks for one 300 MB block, and on a machine
# running Unity beside Blender that allocation fails ("Malloc returns null ... in Cycles Aligned Alloc").
# Each tile bakes the whole group with its UVs scaled and shifted so that tile's square fills 0..1; Cycles
# skips every triangle outside it.

def new_image(name, size, colour):
    img = bpy.data.images.new(name, size, size, alpha=True)
    img.colorspace_settings.name = "sRGB" if colour else "Non-Color"
    return img


def target(mats, img):
    """Every material of the group bakes into `img`: Cycles writes to each material's active image node."""
    for mat in mats:
        nodes = mat.node_tree.nodes
        node = nodes.get("__bake_target") or nodes.new("ShaderNodeTexImage")
        node.name = "__bake_target"
        node.image = img
        nodes.active = node


def pixels(img, size):
    buf = np.empty(size * size * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    return buf.reshape(size, size, 4)


def uvs_of(objs):
    out = []
    for o in objs:
        layer = o.data.uv_layers.active.data
        buf = np.empty(len(layer) * 2, dtype=np.float32)
        layer.foreach_get("uv", buf)
        out.append(buf)
    return out


def set_uvs(objs, uvs):
    for o, buf in zip(objs, uvs):
        o.data.uv_layers.active.data.foreach_set("uv", buf)
        o.data.update()


def bake_tiled(mats, objs, name, colour, size, bake):
    """One atlas-sized RGBA8 array, baked tile by tile through `bake()`.

    Bytes, not floats: a 4096 float atlas is 268 MB per map, and this runs beside Unity with little
    commit to spare. The tile is a byte image, so its pixels are already the encoded values a PNG
    stores; nothing is lost by quantising them here.
    """
    tiles = size // BAKE_TILE_PX
    whole = np.zeros((size, size, 4), dtype=np.uint8)
    original = uvs_of(objs)
    tile = new_image(name + "_tile", BAKE_TILE_PX, colour)
    target(mats, tile)
    select_only(objs)
    try:
        for ty in range(tiles):
            for tx in range(tiles):
                shifted = []
                for buf in original:
                    uv = buf.reshape(-1, 2) * tiles - np.array([tx, ty], dtype=np.float32)
                    shifted.append(uv.ravel())
                set_uvs(objs, shifted)
                bake()
                y0, x0 = ty * BAKE_TILE_PX, tx * BAKE_TILE_PX
                whole[y0:y0 + BAKE_TILE_PX, x0:x0 + BAKE_TILE_PX] = to_bytes(pixels(tile, BAKE_TILE_PX))
    finally:
        set_uvs(objs, original)
        bpy.data.images.remove(tile)
    return whole


def bake_input(mats, objs, input_name, name, colour, size):
    """Bake one Principled input as emission: the input's own source, never lit, never darkened by metallic."""
    rewired = []
    for mat in mats:
        tree = mat.node_tree
        bsdf = principled(mat)
        out = next(n for n in tree.nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output)
        surface = out.inputs["Surface"]
        previous = surface.links[0].from_socket if surface.is_linked else None
        emit = tree.nodes.new("ShaderNodeEmission")
        emit.inputs["Strength"].default_value = 1.0
        source = bsdf.inputs[input_name]
        if source.is_linked:
            tree.links.new(source.links[0].from_socket, emit.inputs["Color"])
        else:
            v = source.default_value
            emit.inputs["Color"].default_value = (v[0], v[1], v[2], 1.0) if hasattr(v, "__len__") else (v, v, v, 1.0)
        tree.links.new(emit.outputs["Emission"], surface)
        rewired.append((tree, emit, surface, previous))
    try:
        return bake_tiled(mats, objs, name, colour, size,
                          lambda: bpy.ops.object.bake(type="EMIT", margin=BAKE_MARGIN_PX, use_clear=True))
    finally:
        for tree, emit, surface, previous in rewired:
            tree.nodes.remove(emit)
            if previous is not None:
                tree.links.new(previous, surface)


def bake_normal(mats, objs, name, size):
    return bake_tiled(mats, objs, name, False, size,
                      lambda: bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT",
                                                  margin=BAKE_MARGIN_PX, use_clear=True))


PNG_SIGNATURE = bytes((0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))


def to_bytes(rgba):
    return (np.clip(rgba, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)


def save(name, data):
    """Write an RGBA8 array (bottom row first, as Blender stores pixels) as a PNG, with the stdlib.

    Written directly rather than through a Blender image, which would hold a float copy of the
    whole atlas in memory a second time.
    """
    path = os.path.join(TEXTURES, name + ".png")
    height, width = data.shape[:2]
    rows = np.empty((height, 1 + width * 4), dtype=np.uint8)
    rows[:, 0] = 0                                    # filter type: none
    rows[:, 1:] = data[::-1].reshape(height, width * 4)

    def chunk(kind, body):
        return (struct.pack(">I", len(body)) + kind + body
                + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF))

    with open(path, "wb") as f:
        f.write(PNG_SIGNATURE)
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(rows.tobytes(), 6)))
        f.write(chunk(b"IEND", b""))
    print("  wrote %s" % os.path.relpath(path, _exportlib.REPO_ROOT))


def bake_atlas(atlas, objs):
    mats = sorted({s.material for o in objs for s in o.material_slots if s.material is not None},
                  key=lambda m: m.name)
    area = world_area(objs)
    size = ATLAS_SIZE[atlas]
    print("  atlas %s: %d object(s), %d material(s), %.0f m2, %d px, %.0f px/m"
          % (atlas, len(objs), len(mats), area, size, size / np.sqrt(max(area, 1e-6))))
    unwrap(objs)

    if ONLY and atlas not in ONLY:
        print("  atlas %s: unwrapped, bake skipped (keeping its PNGs)" % atlas)
        return

    prefix = "SatTower_%s_" % atlas
    save(prefix + "BaseColor", bake_input(mats, objs, "Base Color", prefix + "BaseColor", True, size))

    packed = bake_input(mats, objs, "Metallic", prefix + "Metallic", False, size)
    packed[..., 1] = packed[..., 2] = packed[..., 0]
    packed[..., 3] = 255 - bake_input(mats, objs, "Roughness", prefix + "Roughness", False, size)[..., 0]
    save(prefix + "MetallicSmoothness", packed)
    del packed

    save(prefix + "Normal", bake_normal(mats, objs, prefix + "Normal", size))


# ── 6: one material per atlas ─────────────────────────────────────────────────────────────────────

def reassign(atlas, objs):
    flat = bpy.data.materials.new("SatTower_%s" % atlas)
    flat.use_nodes = True
    for o in objs:
        old = [s.material for s in o.material_slots]
        keep = [flat] + sorted({m for m in old if m is not None and is_simple(m)}, key=lambda m: m.name)
        index = {m.name: i for i, m in enumerate(keep)}
        remap = [index[m.name] if m is not None and is_simple(m) else 0 for m in old]
        faces = [remap[p.material_index] if p.material_index < len(remap) else 0 for p in o.data.polygons]
        o.data.materials.clear()
        for m in keep:
            o.data.materials.append(m)
        o.data.polygons.foreach_set("material_index", faces)


ONLY = set(sys.argv[sys.argv.index("--") + 1:]) if "--" in sys.argv else set()


def prepare():
    unknown = ONLY - set(ATLAS_SIZE)
    if unknown:
        raise SystemExit("Unknown atlas name(s): %s" % ", ".join(sorted(unknown)))
    os.makedirs(TEXTURES, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = BAKE_SAMPLES
    freeze()
    for atlas, objs in sorted(groups().items()):
        bake_atlas(atlas, objs)
        reassign(atlas, objs)


_exportlib.export(SRC, DST, keep_armature=True, keep_empties=True, prepare=prepare)
_exportlib.describe()
print("DONE")
