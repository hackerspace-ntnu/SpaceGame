"""Build dune_foil_rig.blend from the hand-modelled dune_foil.blend.

The source file is 78 loose objects with no parenting, no pivots and 61 materials
carrying 5 distinct colours. Nothing in it can be posed. This script derives a rigged,
Unity-ready copy without ever writing to the original:

  1. dedupe materials 61 -> 5, one per colour
  2. convert the beveled Bezier spars and ropes to meshes
  3. build a pivot hierarchy of empties (rake / yaw per sail, foil angle of attack)
  4. subdivide the 13-poly sail planes into billowable grids and UV them luff -> leech
  5. drop anchor empties where the running rigging attaches, and delete the static ropes
  6. re-origin to the hull bottom on the centreline, so ride height reads 0 at rest
  7. save the .blend and export the FBX

Run:
  blender --background Assets/Models/_Source~/models/vehicles/dune_foil.blend \
          --python Assets/Models/_Source~/models/vehicles/dune_foil_rig.py
"""

import os
import sys
import math

import bpy
import bmesh
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
# Walk up to the Unity project root rather than counting parent directories, so
# this survives the library being moved (it already moved once, into Assets/).
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)
RIG_BLEND = os.path.join(HERE, "dune_foil_rig.blend")
FBX_DIR = os.path.join(REPO, "Assets", "Models", "Vehicles", "DuneFoil")
FBX_PATH = os.path.join(FBX_DIR, "dune_foil_rig.fbx")

# ---------------------------------------------------------------------------
# 1. materials: one per colour
# ---------------------------------------------------------------------------

# linear base colour -> final name. The source file's 61 used materials collapse onto
# exactly these five; see docs/superpowers/specs/2026-08-11-dune-foil-sailer-design.md.
PALETTE = {
    (0.050, 0.027, 0.010): ("Mat_Foil_Wood_Dark", 0.75, 0.0),
    (0.800, 0.473, 0.180): ("Mat_Foil_Sailcloth", 0.90, 0.0),
    (0.150, 0.054, 0.018): ("Mat_Foil_Wood_Mid", 0.70, 0.0),
    (0.050, 0.059, 0.069): ("Mat_Foil_Metal_Slate", 0.45, 1.0),
    (0.092, 0.092, 0.092): ("Mat_Foil_Metal_Grey", 0.50, 1.0),
}
SAILCLOTH = "Mat_Foil_Sailcloth"


def _base_colour(mat):
    if not mat or not mat.use_nodes:
        return None
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf is None:
        return None
    return tuple(round(v, 3) for v in bsdf.inputs["Base Color"].default_value[:3])


def _make_material(name, colour, roughness, metallic):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    return mat


def dedupe_materials():
    """Collapse every material onto the five palette entries, keyed by base colour."""
    canonical = {}
    for colour, (name, rough, metal) in PALETTE.items():
        canonical[colour] = _make_material(name, colour, rough, metal)

    remapped = 0
    for obj in bpy.data.objects:
        if obj.type not in {"MESH", "CURVE"}:
            continue
        slots = obj.data.materials
        for i, mat in enumerate(slots):
            colour = _base_colour(mat)
            target = canonical.get(colour)
            if target is None:
                # unknown colour: snap to the nearest palette entry rather than keeping a dupe
                if colour is None:
                    target = canonical[(0.050, 0.027, 0.010)]
                else:
                    best = min(PALETTE, key=lambda c: sum((a - b) ** 2 for a, b in zip(c, colour)))
                    target = canonical[best]
            if slots[i] is not target:
                slots[i] = target
                remapped += 1

    # drop everything that is now unused
    for mat in list(bpy.data.materials):
        if mat.users == 0 and mat not in canonical.values():
            bpy.data.materials.remove(mat)

    print(f"[materials] remapped {remapped} slots onto {len(canonical)} materials")
    return canonical


# ---------------------------------------------------------------------------
# 2. part assignment
# ---------------------------------------------------------------------------

# Derived by inspecting world bounds and curve endpoints against the renders. The four
# sails each get a post (stays up when furled), fabric and support (both hidden when furled).
GROUPS = {
    "Main": {
        "post": ["BézierCurve"],
        "fabric": ["Plane", "Plane.001"],
        "support": ["Cube.013", "Cube.014", "Cube.015", "Cube.016",
                    "Cube.017", "Cube.018", "Cube.019", "Cube.020", "BézierCurve.001"],
    },
    "Jib": {
        "post": ["BézierCurve.003"],
        "fabric": ["Plane.003", "Plane.002"],
        "support": ["Cube.021", "Cube.022", "Cube.023", "Cube.024",
                    "Cube.025", "Cube.026", "Cube.027", "Cube.012", "BézierCurve.002"],
    },
    "WingS": {
        "post": ["BézierCurve.004"],
        "fabric": ["Plane.004", "Plane.005"],
        "support": ["Cube.028", "Cube.029", "Cube.030", "Cube.031",
                    "Cube.032", "Cube.033", "Cube.034", "Cube.035", "BézierCurve.005"],
    },
    "WingP": {
        "post": ["BézierCurve.009"],
        "fabric": ["Plane.009", "Plane.008"],
        "support": ["Cube.044", "Cube.045", "Cube.046", "Cube.047",
                    "Cube.048", "Cube.049", "Cube.050", "Cube.051", "BézierCurve.008"],
    },
}

# The running rigging. These are deleted; Unity redraws them each frame between the
# recorded anchor points so they follow the sail they are sheeted to.
SHEETS = {
    "MainSheet": "BézierCurve.006",
    "JibSheet": "BézierCurve.007",
}

# The foil: everything below the hull.
FOIL_STRUT = ["Cube", "Cylinder.007", "Cylinder.008"]

# Deck controls the player operates. Each becomes a station node with its own collider.
STATION_PARTS = {
    "Station_Hoist": ["Cylinder", "Cylinder.001", "Cylinder.002"],
    "Station_MainSheet": ["Cylinder.009", "Cylinder.010"],
    "Station_JibSheet": ["Cylinder.011"],
    "Station_MastRake": ["Cube.003", "Cube.010"],
}


def obj(name):
    o = bpy.data.objects.get(name)
    if o is None:
        raise KeyError(f"expected object '{name}' in the source model")
    return o


def world_bounds(objects):
    mn = Vector((1e9, 1e9, 1e9))
    mx = Vector((-1e9, -1e9, -1e9))
    for o in objects:
        if o.type != "MESH":
            continue
        for corner in o.bound_box:
            p = o.matrix_world @ Vector(corner)
            for i in range(3):
                mn[i] = min(mn[i], p[i])
                mx[i] = max(mx[i], p[i])
    return mn, mx


# ---------------------------------------------------------------------------
# 3. curves -> meshes
# ---------------------------------------------------------------------------

def curve_endpoints():
    """World-space endpoints of every Bezier curve, recorded before conversion."""
    ends = {}
    for o in bpy.data.objects:
        if o.type != "CURVE":
            continue
        pts = []
        for spline in o.data.splines:
            for bp in spline.bezier_points:
                pts.append(o.matrix_world @ bp.co)
        if len(pts) >= 2:
            ends[o.name] = (pts[0].copy(), pts[-1].copy())
    return ends


def apply_transforms():
    """Bake rotation and scale into the mesh data, leaving every object at identity.

    The artist built the sails by scaling planes rather than editing their vertices, so they
    arrive with scales like (114, 242, 114) and mesh data measured in hundredths of a unit.
    Anything that works in object space then sees a space ~100x smaller than the world — the
    sailcloth shader displaces by metres and would throw the cloth a hundred metres sideways.
    Applying the transforms puts the geometry in real units and makes object space mean what it
    says. Location is left alone, so every pivot and origin stays where it was authored.
    """
    bpy.ops.object.select_all(action="DESELECT")
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if not meshes:
        return
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    worst = max((max(abs(v) for v in o.scale) for o in meshes), default=1.0)
    print(f"[transforms] applied to {len(meshes)} meshes; largest remaining scale {worst:.3f}")


def convert_curves_to_mesh():
    bpy.ops.object.select_all(action="DESELECT")
    curves = [o for o in bpy.data.objects if o.type == "CURVE"]
    if not curves:
        return
    for o in curves:
        o.select_set(True)
    bpy.context.view_layer.objects.active = curves[0]
    bpy.ops.object.convert(target="MESH")
    print(f"[curves] converted {len(curves)} spars/ropes to mesh")


# ---------------------------------------------------------------------------
# 4. sail meshes: subdivide and UV luff -> leech
# ---------------------------------------------------------------------------

def prepare_sail(mesh_obj, luff_a, luff_b, cuts=4):
    """Subdivide a sail plane into a billowable grid and UV it against its luff spar.

    U runs 0 at the luff (the edge lashed to the post) to 1 at the leech (free edge).
    V runs 0 at the foot to 1 at the head, measured along the spar.
    The shader bellies the sail by U and tapers it by V, so both need to be real
    distances rather than whatever the source plane happened to carry.
    """
    bm = bmesh.new()
    bm.from_mesh(mesh_obj.data)

    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=cuts, use_grid_fill=True)
    bm.verts.ensure_lookup_table()

    mw = mesh_obj.matrix_world
    axis = (luff_b - luff_a)
    axis_len = axis.length
    axis_n = axis.normalized() if axis_len > 1e-6 else Vector((0, 0, 1))

    # distance from the luff line, and travel along it, for every vertex
    dists, alongs = [], []
    for v in bm.verts:
        w = mw @ v.co
        rel = w - luff_a
        t = rel.dot(axis_n)
        perp = (rel - axis_n * t).length
        dists.append(perp)
        alongs.append(t)

    d_min, d_max = min(dists), max(dists)
    a_min, a_max = min(alongs), max(alongs)
    d_span = max(d_max - d_min, 1e-5)
    a_span = max(a_max - a_min, 1e-5)

    uv_layer = bm.loops.layers.uv.get("UVMap") or bm.loops.layers.uv.new("UVMap")
    for face in bm.faces:
        for loop in face.loops:
            i = loop.vert.index
            u = (dists[i] - d_min) / d_span
            v = (alongs[i] - a_min) / a_span
            loop[uv_layer].uv = (u, v)

    bm.to_mesh(mesh_obj.data)
    bm.free()
    mesh_obj.data.update()
    return len(mesh_obj.data.polygons)


# ---------------------------------------------------------------------------
# 5. hierarchy
# ---------------------------------------------------------------------------

def reparent(child, parent):
    """Parent while keeping the world transform.

    Reads back and re-applies matrix_world around the parenting rather than computing a
    parent inverse: a freshly created object's matrix_world is stale until the depsgraph
    catches up, and every pivot here is created moments before something is hung off it.
    """
    bpy.context.view_layer.update()
    world = child.matrix_world.copy()
    child.parent = parent
    child.matrix_parent_inverse = Matrix.Identity(4)
    bpy.context.view_layer.update()
    child.matrix_world = world


def make_empty(name, location, parent=None, rotation=None, size=0.5):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "PLAIN_AXES"
    e.empty_display_size = size
    bpy.context.scene.collection.objects.link(e)
    e.location = location
    if rotation is not None:
        e.rotation_euler = rotation
    if parent is not None:
        reparent(e, parent)
    return e


def best_spar_axis(foot, points):
    """Direction through `foot` that a bowed spar deviates from least.

    The masts on this model bow 7.5% of their chord. Rotating the sail about the straight
    foot-to-head chord would sweep the luff off the mast by 2*sagitta*sin(t/2) — over a
    metre on the main at a realistic sheet angle. Rotating about the best-fit axis instead
    roughly halves that, for the cost of a search, and keeps every part of the rig a rigid
    transform. If it ever does read as detached, the exact fix is a bone chain along the
    spar with the sail skinned to it, twisting each bone about its own local tangent.
    """
    def worst(d):
        return max((p - foot - d * (p - foot).dot(d)).length for p in points)

    head = max(points, key=lambda p: (p - foot).length)
    best_d = (head - foot).normalized()
    best_e = worst(best_d)

    # coarse-to-fine search over directions around the chord
    step = 0.20
    for _ in range(6):
        improved = False
        for axis in (Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))):
            for sign in (1.0, -1.0):
                cand = (best_d + axis * (sign * step)).normalized()
                e = worst(cand)
                if e < best_e - 1e-6:
                    best_d, best_e, improved = cand, e, True
        if not improved:
            step *= 0.5
    return best_d, best_e


def spar_rotation(direction):
    """Euler that points the empty's +Z along `direction`.

    Blender +Z exports to Unity +Y, so a Unity local-Y rotation on this node swings the
    sail about its own spar. That is what keeps the sail attached to a raked mast.
    """
    if direction.length < 1e-6:
        return (0.0, 0.0, 0.0)
    return direction.normalized().to_track_quat("Z", "Y").to_euler()


def build():
    # scene furniture the source still carries; a stray camera inside a prefab hijacks rendering
    for junk in ("Camera", "Light"):
        o = bpy.data.objects.get(junk)
        if o:
            bpy.data.objects.remove(o, do_unlink=True)

    dedupe_materials()

    ends = curve_endpoints()

    # Record the sail luff lines and sheet anchors while the curves still exist.
    luffs = {}
    for group, parts in GROUPS.items():
        post = parts["post"][0]
        a, b = ends[post]
        # order foot -> head so V runs upward
        if a.z > b.z:
            a, b = b, a
        luffs[group] = (a, b)

    sheet_anchors = {name: ends[curve] for name, curve in SHEETS.items()}

    convert_curves_to_mesh()
    apply_transforms()

    # --- root -------------------------------------------------------------
    hull = obj("Sphere.001")
    hmn, hmx = world_bounds([hull])
    root_loc = Vector((0.0, (hmn.y + hmx.y) * 0.5, hmn.z))

    root = make_empty("FOIL_Root", root_loc, size=2.0)

    hull_node = make_empty("Hull", root_loc, parent=root, size=1.0)
    foil_node = make_empty("Foil_Strut", Vector((0.0, 0.0, hmn.z)), parent=root, size=1.0)

    foil_objs = [obj(n) for n in FOIL_STRUT]
    for i, o in enumerate(foil_objs):
        o.name = "Foil_Blade" if i == 0 else f"Foil_Fairing_{i:02d}"
    fmn, fmx = world_bounds(foil_objs)
    # angle-of-attack pivot at the foil wing's leading edge, on the strut centreline
    foil_wing = make_empty("Foil_Wing",
                           Vector(((fmn.x + fmx.x) * 0.5, (fmn.y + fmx.y) * 0.5, fmn.z)),
                           parent=foil_node, size=1.0)
    for o in foil_objs:
        reparent(o, foil_wing)

    # --- sails ------------------------------------------------------------
    assigned = set(FOIL_STRUT) | {"Sphere.001"}
    sail_report = {}

    for group, parts in GROUPS.items():
        foot, head = luffs[group]

        # Fit the rotation axis to the spar's actual (bowed) centreline, not its chord.
        post_obj = obj(parts["post"][0])
        pmw = post_obj.matrix_world
        spar_pts = [pmw @ v.co for v in post_obj.data.vertices]
        axis_dir, sweep_err = best_spar_axis(foot, spar_pts)
        chord_err = max((p - foot - (head - foot).normalized()
                         * (p - foot).dot((head - foot).normalized())).length for p in spar_pts)
        print(f"[axis] {group}: max spar offset {chord_err:.2f}m about the chord "
              f"-> {sweep_err:.2f}m about the fitted axis")
        rot = spar_rotation(axis_dir)

        rake = make_empty(f"{group}_Rake", foot, parent=root, rotation=(0, 0, 0), size=1.2)
        yaw = make_empty(f"{group}_Yaw", foot, parent=rake, rotation=rot, size=1.0)
        sail = make_empty(f"Sail_{group}", foot, parent=yaw, size=0.8)
        clew = None

        # Rename as we go: the source carries Blender defaults with a non-ASCII 'Bézier',
        # which is a poor thing for the Unity builder to match on.
        # The spar hangs off the YAW node, not the rake node, so it swings with the sail.
        #
        # Hanging it off the rake node instead — leaving the spar fixed while only the cloth
        # rotates — is the obvious reading of "rotate the sail, not the post", and it does not
        # survive contact with this model. These spars bow 7.5% of their length, and the sail is
        # lashed along that curve; rotate the cloth rigidly about any straight axis and the luff
        # peels off the spar at about 0.09 m per degree. Measured: flush at the authored pose,
        # 1.9 m adrift at 15 degrees of sheet, 5.1 m at 60. The sail visibly tears off its own
        # mast, which is worse than the thing the separation was meant to achieve.
        #
        # Swinging them together is also what the geometry actually depicts: a single curved
        # spar with the sail laced to it is a yard, and a yard rotates with its sail. The spar
        # still sits outside the Sail_ node, so furling takes the cloth and battens and leaves
        # the spar standing — which is the part of the requirement that matters on screen.
        for i, name in enumerate(parts["post"]):
            o = obj(name)
            reparent(o, yaw)
            assigned.add(name)
            o.name = f"{group}_Post" if i == 0 else f"{group}_Post_{i}"
        for i, name in enumerate(parts["support"]):
            o = obj(name)
            reparent(o, sail)
            assigned.add(name)
            o.name = f"{group}_Batten_{i:02d}"

        polys = 0
        cloth = []
        for i, name in enumerate(parts["fabric"]):
            o = obj(name)
            polys += prepare_sail(o, foot, head)
            reparent(o, sail)
            assigned.add(name)
            o.name = f"{group}_Cloth_{i:02d}"
            cloth.append(o)

        # Clew: the actual fabric vertex furthest from the luff line and lowest on the
        # spar — the aft foot corner a sheet is made fast to. Taken off the mesh rather
        # than off the bounding box, whose far corner sits in empty space for a sail cut
        # on a diagonal, which every sail on this rig is.
        axis_n = (head - foot).normalized()
        best, best_score = None, -1.0
        for o in cloth:
            mw = o.matrix_world
            for v in o.data.vertices:
                w = mw @ v.co
                rel = w - foot
                along = rel.dot(axis_n)
                perp = (rel - axis_n * along).length
                # favour distance from the mast, penalise height up it
                score = perp - 0.35 * max(along, 0.0)
                if score > best_score:
                    best_score, best = score, w.copy()
        clew = make_empty(f"Anchor_{group}_Clew", best, parent=sail, size=0.4)

        sail_report[group] = {"polys": polys, "clew": [round(v, 2) for v in best]}
        print(f"[sail] {group}: {polys} polys, clew at {sail_report[group]['clew']}")

    # --- deck stations ----------------------------------------------------
    for station, names in STATION_PARTS.items():
        objs = [obj(n) for n in names]
        smn, smx = world_bounds(objs)
        node = make_empty(station, (smn + smx) * 0.5, parent=hull_node, size=0.6)
        for i, o in enumerate(objs):
            reparent(o, node)
            assigned.add(o.name)
            o.name = f"{station}_Part_{i:02d}"

    # --- running rigging anchors -----------------------------------------
    rig_node = make_empty("Rigging", root_loc, parent=hull_node, size=0.6)
    for name, (a, b) in sheet_anchors.items():
        # deck end of the sheet: the endpoint nearer the hull centreline
        deck_end = a if abs(a.x) + abs(a.y - root_loc.y) < abs(b.x) + abs(b.y - root_loc.y) else b
        make_empty(f"Anchor_{name}_Deck", deck_end, parent=rig_node, size=0.4)
    for curve in SHEETS.values():
        o = bpy.data.objects.get(curve)
        if o:
            bpy.data.objects.remove(o, do_unlink=True)

    # --- everything else is hull -----------------------------------------
    reparent(hull, hull_node)
    hull.name = "Hull_Shell"
    deck_i = 0
    for o in list(bpy.data.objects):
        if o.type != "MESH" or o.parent is not None or o.name in assigned:
            continue
        reparent(o, hull_node)
        o.name = f"Deck_Fitting_{deck_i:02d}"
        deck_i += 1

    # --- re-origin --------------------------------------------------------
    # Move the whole rig so FOIL_Root sits at the world origin. Unity then gets a prefab
    # whose pivot is the hull bottom on the centreline: ride height reads 0 at rest.
    root.location = (0.0, 0.0, 0.0)
    bpy.context.view_layer.update()

    counts = {"MESH": 0, "EMPTY": 0}
    for o in bpy.data.objects:
        counts[o.type] = counts.get(o.type, 0) + 1
    print(f"[build] {counts} | materials {len(bpy.data.materials)}")
    return root


def export(root):
    os.makedirs(FBX_DIR, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        # FBX_SCALE_ALL puts the metres-to-FBX-units conversion in the file header instead of
        # baking a x100 onto every object transform, which is what FBX_SCALE_NONE does. Unity
        # then imports the rig with scale 1 throughout, and anything working in object space —
        # the sailcloth shader above all — sees real metres.
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        object_types={"EMPTY", "MESH"},
        use_mesh_modifiers=True,
        # Triangulate on the way out. The source carries n-gons that Unity's importer rejects
        # as self-intersecting and silently drops; a game engine wants triangles regardless.
        use_triangles=True,
        mesh_smooth_type="FACE",
        bake_space_transform=False,
        add_leaf_bones=False,
        path_mode="COPY",
    )
    print(f"[export] {FBX_PATH}")


if __name__ == "__main__":
    root = build()
    bpy.ops.wm.save_as_mainfile(filepath=RIG_BLEND)
    print(f"[save] {RIG_BLEND}")
    export(root)
