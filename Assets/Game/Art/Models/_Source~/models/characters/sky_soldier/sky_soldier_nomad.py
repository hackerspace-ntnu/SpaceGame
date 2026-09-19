"""The sky soldier on a real Nomad body: Maroon's sculpted suit, the Nomad's robot helmet.

    blender --background --python models/characters/sky_soldier/sky_soldier_nomad.py -- --out models/characters/sky_soldier/sky_soldier_nomad.blend

Built from two existing characters, both only READ:

  * `~/Documents/Blender/sand_nogs.blend` - Nomad Maroon (`Armature_NomadMaroon`, the Nomads'
    65-bone Mixamo rig) with only its suit, gloves and boots. Its hood, breathing mask, long
    cloth strip, pole, packs, pouches, straps, rings and leg wraps are left behind: the user
    asked for Maroon's body with some of its clothing removed.
  * `models/characters/nomad/nomad.blend` - the robot helmet (`Robot Helmet Parts`), scaled
    from the Nomad's head to Maroon's and bound rigidly to Maroon's Head bone.

The character is moved so its hips sit over x = 0 and its boot soles on z = 0, facing -Y.
Every material is replaced from the palette; Maroon and the helmet carried local ones.

Maroon's suit is smoothed (its sculpted creases taken out, on request), and the sky soldier's
garments are modelled on it in a sculpt style with small folds: a long teal coat in three
panels with orange trim, a cowl under the helmet, and an orange scarf blown out behind.

Generation script -- historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, LIB)

import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import _buildlib as B  # noqa: E402

SAND_NOGS = os.path.join(os.path.expanduser("~"), "Documents", "Blender", "sand_nogs.blend")
NOMAD = os.path.join(LIB, "models", "characters", "nomad", "nomad.blend")

MAROON_ARMATURE = "Armature_NomadMaroon"
MAROON_KEEP = {"Maroon_Suit": "Suit", "Maroon_Gloves": "Gloves", "Maroon_Boots": "Boots", "Maroon_Dome_01": "Head"}
MAROON_SOLE_Z = -1.589        # lowest point of Maroon's boots, measured
MAROON_HEAD_CENTRE = Vector((-4.199, -0.063, 1.361))   # Maroon_Dome_01's box centre
NOMAD_HEAD_CENTRE = Vector((7.107, 0.040, 2.680))      # the Nomad's head sphere (Sphere.001)
NOMAD_HEAD_WIDTH = 0.361
# Maroon's head is a rounder dome than the Nomad's sphere, so its width over-reads the helmet
# size needed: at the width ratio (1.26) the helmet dwarfed the shoulders. Judged by render.
HELMET_SCALE = 1.0
HELMET_NUDGE = Vector((0.0, 0.07, -0.05))              # back and down onto the collar
HEAD_BONE = "mixamorig:Head"

HELMET_PARTS = ("Helmet_Shell", "Helmet_Face", "Helmet_Lens", "Helmet_Brow", "Helmet_Jaw",
                "Helmet_EarPods", "Helmet_TopRail", "Helmet_NeckRim", "Helmet_Studs")

# object -> palette material. The suit goes the concept's deep teal; the helmet keeps the
# Nomad's read (dark shell, orange face, amber visor) with the soldier's teal on the ear pods.
PAINT = {
    "Suit": "Mat_Paint_Teal_Deep",
    "Gloves": "Mat_Neutral_Slate_Dark",
    "Boots": "Mat_Neutral_Slate_Dark",
    "Head": "Mat_Neutral_Black_Matte",   # only ever seen through the visor
    "Helmet_Shell": "Mat_Neutral_Slate_Dark",
    "Helmet_Face": "Mat_Paint_Safety_Orange",
    "Helmet_Lens": "Mat_Emissive_Amber",
    "Helmet_Brow": "Mat_Metal_Steel_Dark",
    "Helmet_Jaw": "Mat_Neutral_Black_Matte",
    "Helmet_EarPods": "Mat_Fabric_Tarp_Azure",
    "Helmet_TopRail": "Mat_Metal_Steel_Dark",
    "Helmet_NeckRim": "Mat_Metal_Steel_Dark",
    "Helmet_Studs": "Mat_Metal_Brass_Tarnished",
}


def append_objects(blend, names):
    with bpy.data.libraries.load(blend, link=False) as (src, dst):
        missing = [n for n in names if n not in set(src.objects)]
        if missing:
            raise SystemExit("Not in %s: %s" % (blend, ", ".join(missing)))
        dst.objects = list(names)
    return [bpy.data.objects[n] for n in names]


def paint(obj, key, palette):
    mat = palette[PAINT[key]]
    obj.data.materials.clear()
    obj.data.materials.append(mat)


def body(coll, palette):
    # Appending the meshes brings their parent armature with them; appending it as well
    # would make a second copy.
    parts = append_objects(SAND_NOGS, list(MAROON_KEEP))
    arm = parts[0].parent
    if arm is None or arm.name != MAROON_ARMATURE or any(p.parent != arm for p in parts):
        raise SystemExit("Maroon's parts are no longer all parented to %s" % MAROON_ARMATURE)
    coll.objects.link(arm)
    for obj in parts:
        coll.objects.link(obj)
        key = MAROON_KEEP[obj.name]
        obj.name = obj.data.name = "Mesh_SkySoldierN_" + key
        paint(obj, key, palette)
    bpy.context.view_layer.update()
    hips = arm.matrix_world @ arm.data.bones["mixamorig:Hips"].head_local
    shift = Vector((-hips.x, 0.0, -MAROON_SOLE_Z))
    arm.location += shift
    arm.name = arm.data.name = "Arm_SkySoldierN"
    bpy.context.view_layer.update()
    return arm, shift


def helmet(coll, palette, arm, shift):
    """The Nomad's helmet, carried from the Nomad's head onto Maroon's and bound to its Head bone."""
    fit = (Matrix.Translation(MAROON_HEAD_CENTRE + shift + HELMET_NUDGE)
           @ Matrix.Scale(HELMET_SCALE, 4)
           @ Matrix.Translation(-NOMAD_HEAD_CENTRE))
    for obj in append_objects(NOMAD, list(HELMET_PARTS)):
        coll.objects.link(obj)
        key = obj.name
        obj.parent = None
        # Bake the fit into the mesh so the object keeps scale 1, origin at the head centre.
        obj.data = obj.data.copy()
        # matrix_basis: a freshly appended object's matrix_world is not evaluated yet, and three
        # parts (ear pods, lens, top rail) carry their own transform - read stale, they landed
        # at the character's feet. None of the parts has a parent in nomad.blend.
        obj.data.transform(fit @ obj.matrix_basis)
        obj.matrix_world = Matrix.Identity(4)
        centre = MAROON_HEAD_CENTRE + shift + HELMET_NUDGE
        obj.data.transform(Matrix.Translation(-centre))
        obj.location = centre
        obj.modifiers.clear()
        obj.vertex_groups.clear()
        obj.vertex_groups.new(name=HEAD_BONE).add(range(len(obj.data.vertices)), 1.0, 'REPLACE')
        obj.modifiers.new(name="Armature", type='ARMATURE').object = arm
        obj.name = obj.data.name = "Mesh_SkySoldierN_" + key
        paint(obj, key, palette)


# -- sculpt-style garments --------------------------------------------------
#
# Modelled, not simulated: each garment is a grid laid off the (smoothed) suit's measured surface,
# so it sits on this body wherever it is, with small hand-placed folds rather than simulated ones -
# the user asked for sculpt-style cloth with folds that are not too large. Thickness and a
# subdivision are applied, and each takes its skin weights from the suit it covers.

GARMENT_MATS = ("Mat_Fabric_Tarp_Azure", "Mat_Paint_Safety_Orange", "Mat_Fabric_Sail_Orange")
SUIT_SMOOTHING = 35            # vertex-average passes; at 90 the limbs thinned to sticks
BODY_Y = -0.03
CLEAR = 0.035                  # coat clearance off the suit
TORSO_REACH = 0.42             # a hit further out than this is an arm, not the torso


def with_object(obj):
    return bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj],
                                     selected_editable_objects=[obj])


def apply_modifier(obj, name):
    with with_object(obj):
        bpy.ops.object.modifier_apply(modifier=name)


def smooth_suit(suit):
    """Take the sculpted creases out of Maroon's suit, keeping its shape (user's request)."""
    bm = bmesh.new()
    bm.from_mesh(suit.data)
    for _ in range(SUIT_SMOOTHING):
        bmesh.ops.smooth_vert(bm, verts=bm.verts, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.to_mesh(suit.data)
    bm.free()


class Surface:
    """The posed-at-rest suit in world space, for measuring where cloth should lie."""

    def __init__(self, suit):
        from mathutils.bvhtree import BVHTree
        bpy.context.view_layer.update()
        evaluated = suit.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = evaluated.to_mesh()
        self.bvh = BVHTree.FromPolygons([suit.matrix_world @ v.co for v in mesh.vertices],
                                        [p.vertices[:] for p in mesh.polygons])
        evaluated.to_mesh_clear()

    def radius(self, z, angle_deg, fallback):
        """Distance from the body axis to the suit's outside along a horizontal ray, or `fallback`."""
        a = math.radians(angle_deg)
        direction = Vector((math.cos(a), math.sin(a), 0.0))
        axis = Vector((0.0, BODY_Y, z))
        hit = self.bvh.ray_cast(axis + direction * 1.5, -direction, 1.5)[0]
        if hit is None:
            return fallback
        # At chest height a ray toward the sides meets the A-posed arm first; a coat that took that
        # as the body flared out to arm's width and, never moving inward, kept it to the hem.
        distance = (hit - axis).length
        return distance if distance <= TORSO_REACH else fallback

    def top(self, x, y, fallback):
        hit = self.bvh.ray_cast(Vector((x, y, 4.0)), Vector((0.0, 0.0, -1.0)), 2.5)[0]
        return hit.z if hit is not None else fallback


def grid_object(name, coll, rows, material, palette, thickness, closed=False):
    """A mesh from a grid of points, thickened outward, subdivided and smooth-shaded."""
    bm = bmesh.new()
    verts = [[bm.verts.new(p) for p in row] for row in rows]
    cols = len(rows[0])
    for r in range(len(rows) - 1):
        for c in range(cols if closed else cols - 1):
            k = (c + 1) % cols
            bm.faces.new((verts[r][c], verts[r][k], verts[r + 1][k], verts[r + 1][c]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    coll.objects.link(obj)
    obj.data.materials.append(palette[material])
    # Normals must face away from the body for the shell to grow outward.
    centre = sum((v.co for v in mesh.vertices), Vector()) / len(mesh.vertices)
    outward = sum(((p.center - Vector((0.0, BODY_Y, p.center.z))).dot(p.normal) for p in mesh.polygons))
    if outward < 0 and closed or (not closed and outward < 0):
        mesh.flip_normals()
    sol = obj.modifiers.new("Solidify", 'SOLIDIFY')
    sol.thickness = thickness
    sol.offset = 1.0
    apply_modifier(obj, "Solidify")
    sub = obj.modifiers.new("Subdivision", 'SUBSURF')
    sub.levels = 1
    apply_modifier(obj, "Subdivision")
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj, centre


# Garments are weighted by HEIGHT up the spine, not copied from the suit. Copied, the coat's hem
# took the legs' weights (or, with the legs filtered out, a fallback bone), and a bent spine tore
# the coat across the waist: the top followed the chest while the hem stayed with the hips.
# A blend between neighbouring spine bones by height cannot tear, whatever the pose.
SPINE_CHAIN = ("Hips", "Spine", "Spine1", "Spine2", "Neck")


def skin(obj, arm, chain=SPINE_CHAIN):
    """Weight every vertex between the two spine bones whose heads it lies between."""
    bones = [("mixamorig:" + b, (arm.matrix_world @ arm.data.bones["mixamorig:" + b].head_local).z) for b in chain]
    groups = [obj.vertex_groups.new(name=name) for name, _ in bones]
    world = obj.matrix_world
    for v in obj.data.vertices:
        z = (world @ v.co).z
        if z <= bones[0][1]:
            groups[0].add([v.index], 1.0, 'REPLACE')
            continue
        if z >= bones[-1][1]:
            groups[-1].add([v.index], 1.0, 'REPLACE')
            continue
        for k in range(len(bones) - 1):
            z0, z1 = bones[k][1], bones[k + 1][1]
            if z0 <= z <= z1:
                t = (z - z0) / (z1 - z0)
                groups[k].add([v.index], 1.0 - t, 'REPLACE')
                groups[k + 1].add([v.index], t, 'REPLACE')
                break
    obj.modifiers.new("Armature", 'ARMATURE').object = arm


def rigid(obj, arm, bone):
    obj.vertex_groups.new(name=bone).add(range(len(obj.data.vertices)), 1.0, 'REPLACE')
    obj.modifiers.new("Armature", 'ARMATURE').object = arm


def folds(angle_deg, z, seed):
    """Small vertical folds: a few soft ridges round the coat, fading out toward the shoulders."""
    depth = max(0.0, min(1.0, (2.35 - z) / 1.4))
    ridge = (0.6 * math.sin(math.radians(angle_deg) * 11.0 + seed)
             + 0.4 * math.sin(math.radians(angle_deg) * 23.0 + seed * 2.3 + z * 1.7))
    return 0.011 * depth * ridge


def coat_rows(surface, a0, a1, hem, seed, columns=40, rows=48):
    """Rows from the shoulder line to the hem; each point sits off the suit and never moves inward
    on the way down, so the coat falls from the chest in an A-line instead of hugging the legs."""
    angles = [a0 + (a1 - a0) * c / (columns - 1) for c in range(columns)]
    tops = []
    for a in angles:
        rad = math.radians(a)
        x, y = 0.25 * math.cos(rad), BODY_Y + 0.19 * math.sin(rad)
        tops.append(surface.top(x, y, 2.6) + 0.04)
    grid = []
    previous = [0.0] * columns
    for r in range(rows):
        t = r / (rows - 1)
        row = []
        for c, a in enumerate(angles):
            z = tops[c] + (hem - tops[c]) * t
            body = surface.radius(z, a, previous[c] - CLEAR if r else 0.24) + CLEAR
            flare = max(0.0, 2.1 - z) * 0.06
            radius = max(body, previous[c]) if r else body
            previous[c] = radius
            radius += flare + folds(a, z, seed)
            rad = math.radians(a)
            hem_wave = 0.012 * math.sin(rad * 9.0 + seed) * t
            row.append(Vector((radius * math.cos(rad), BODY_Y + radius * math.sin(rad), z + hem_wave)))
        grid.append(row)
    return grid


def edge_strip(points, outward_of, width=0.035, lift=0.016):
    """A trim strip lying on a garment's face along `points`, lifted clear of the cloth."""
    rows = []
    for i, p in enumerate(points):
        ahead = points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)]
        radial = Vector((p.x, p.y - BODY_Y, 0.0)).normalized()
        across = ahead.cross(radial).normalized() * outward_of
        base = p + radial * lift
        rows.append([base, base + across * width])
    return rows


def coat(coll, palette, surface, suit, arm):
    front_hem, back_hem = 0.78, 0.86
    panels = (("FrontRight", 204.0, 268.0, front_hem, 1.3, "a1"),
              ("FrontLeft", 272.0, 336.0, front_hem, 2.9, "a0"),
              ("Back", 24.0, 156.0, back_hem, 4.1, None))
    for label, a0, a1, hem, seed, opening in panels:
        rows = coat_rows(surface, a0, a1, hem, seed)
        obj, _ = grid_object("Mesh_SkySoldierN_Coat_" + label, coll, rows, "Mat_Fabric_Tarp_Azure", palette, 0.022)
        skin(obj, arm)
        # Orange trim down the front opening and along the hem, lying on the cloth's outside.
        strips = [edge_strip([row[-1] for row in rows], -1.0 if opening == "a1" else 1.0)] if opening else []
        if opening == "a0":
            strips = [edge_strip([row[0] for row in rows], 1.0)]
        hem_points = rows[-1]
        strips.append([[p + Vector((p.x, p.y - BODY_Y, 0.0)).normalized() * 0.016,
                        p + Vector((p.x, p.y - BODY_Y, 0.0)).normalized() * 0.016 + Vector((0, 0, 0.04))] for p in hem_points])
        for k, strip in enumerate(strips):
            trim, _ = grid_object("Mesh_SkySoldierN_Coat_%s_Trim%d" % (label, k + 1), coll, strip,
                                  "Mat_Paint_Safety_Orange", palette, 0.012)
            skin(trim, arm)


def cowl(coll, palette, surface, suit, arm):
    """A short thick collar round the neck and shoulders, under the helmet's neck rim."""
    around = 48
    rows = []
    for z, grow in ((2.44, 0.05), (2.54, 0.07), (2.62, 0.08), (2.67, 0.1)):
        row = []
        for i in range(around):
            a = 360.0 * i / around
            radius = max(surface.radius(z, a, 0.2), 0.16) + grow + 0.008 * math.sin(math.radians(a) * 13.0)
            rad = math.radians(a)
            row.append(Vector((radius * math.cos(rad), BODY_Y + radius * math.sin(rad), z)))
        rows.append(row)
    obj, _ = grid_object("Mesh_SkySoldierN_Cowl", coll, rows, "Mat_Fabric_Tarp_Azure", palette, 0.03, closed=True)
    skin(obj, arm)


def catmull(points, per_span=6):
    pts = [Vector(p) for p in points]
    out = []
    for i in range(len(pts) - 1):
        p0, p1, p2, p3 = pts[max(i - 1, 0)], pts[i], pts[i + 1], pts[min(i + 2, len(pts) - 1)]
        for k in range(per_span):
            t = k / per_span
            out.append(0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    out.append(pts[-1])
    return out


def scarf(coll, palette, arm):
    """A thick loop over the cowl and a tail blown out behind to the left, rippling as it narrows."""
    around = 40
    loop_rows = []
    for z, r in ((2.5, 0.29), (2.575, 0.3), (2.64, 0.285)):
        loop_rows.append([Vector((r * math.cos(math.radians(360.0 * i / around)),
                                  BODY_Y + r * math.sin(math.radians(360.0 * i / around)), z)) for i in range(around)])
    loop, _ = grid_object("Mesh_SkySoldierN_Scarf_Loop", coll, loop_rows, "Mat_Fabric_Sail_Orange", palette, 0.035, closed=True)
    rigid(loop, arm, "mixamorig:Spine2")

    path = catmull([(0.08, 0.26, 2.55), (0.28, 0.44, 2.45), (0.52, 0.64, 2.28), (0.8, 0.8, 2.2), (1.05, 0.96, 2.04), (1.3, 1.08, 2.0)])
    rows = []
    n = len(path)
    for i, p in enumerate(path):
        t = i / (n - 1)
        width = 0.24 * (1.0 - t) + 0.1 * t
        ahead = (path[min(i + 1, n - 1)] - path[max(i - 1, 0)]).normalized()
        up = Vector((0, 0, 1)) - ahead * ahead.z
        up.normalize()
        twist = math.radians(45.0 * math.sin(t * math.pi * 2.6))
        side = ahead.cross(up).normalized()
        edge = up * math.cos(twist) + side * math.sin(twist)
        ripple = side * (0.04 * math.sin(t * math.pi * 4.0) * t) + Vector((0, 0, 0.03 * math.sin(t * math.pi * 3.0)))
        rows.append([p + ripple + edge * (width / 2), p + ripple - edge * (width / 2)])
    tail, _ = grid_object("Mesh_SkySoldierN_Scarf_Tail", coll, rows, "Mat_Fabric_Sail_Orange", palette, 0.025)
    rigid(tail, arm, "mixamorig:Spine2")


def main():
    out = B.parse_out()
    B.start(out)
    palette = {m.name: m for m in B.link_materials(sorted(set(PAINT.values()) | set(GARMENT_MATS)))}
    root = B.collection("Coll_SkySoldierN")
    body_coll = B.collection("Coll_SkySoldierN_Body", root)
    arm, shift = body(body_coll, palette)
    helmet(B.collection("Coll_SkySoldierN_Helmet", root), palette, arm, shift)
    suit = bpy.data.objects["Mesh_SkySoldierN_Suit"]
    smooth_suit(suit)
    surface = Surface(suit)
    wear = B.collection("Coll_SkySoldierN_Garments", root)
    coat(wear, palette, surface, suit, arm)
    cowl(wear, palette, surface, suit, arm)
    scarf(wear, palette, arm)

    # Appending brought the source files' own meshes and materials along; the helmet's original
    # meshes were replaced by copies, and while they lingered they kept their materials alive.
    for mesh in [m for m in bpy.data.meshes if m.users == 0]:
        bpy.data.meshes.remove(mesh)
    for mat in [m for m in bpy.data.materials if m.library is None and m.users == 0]:
        bpy.data.materials.remove(mat)

    B.save(out)
    B.report()


main()
