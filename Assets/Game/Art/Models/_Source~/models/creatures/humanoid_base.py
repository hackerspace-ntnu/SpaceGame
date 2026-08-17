"""humanoid_base.py — a neutral humanoid sculpting base on the astronaut's Mixamo rig.

    blender --background --python humanoid_base.py -- --out humanoid_base.blend

Builds ONE watertight all-quad mesh (torso + head + 2 arms with 5-finger hands +
2 legs with block feet), 1.75 m, feet on z = 0, plus a 65-bone Mixamo armature
whose names match Assets/Game/Art/Models/Characters/Astronaut/AstronautArmature.fbx.

Method: a stick-figure of edges is skinned with the Skin modifier, then subdivided.
That is what makes the result a SINGLE watertight shell — limbs are welded into the
torso at the vertex level rather than being separate tubes pushed together. Bridging
independent tubes leaves boundary edges, which breaks sculpting and bone-heat
weighting alike.

The mesh is built full-body (not mirrored by modifier): the Skin modifier's limb
junctions are not symmetric enough for Mirror-with-clipping to weld cleanly, so
symmetry is provided by X-mirror topology + Blender's symmetrise instead.

Refuses to overwrite an existing .blend: the .blend is the source of truth.
"""

import argparse
import math
import os
import sys

import bpy
import bmesh
from mathutils import Vector

# --------------------------------------------------------------------------
# arguments
# --------------------------------------------------------------------------

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                              "humanoid_base.blend"))
ap.add_argument("--overwrite", action="store_true")
args = ap.parse_args(argv)

OUT = os.path.abspath(args.out)
if os.path.exists(OUT) and not args.overwrite:
    raise SystemExit(
        f"Refusing to overwrite existing file: {OUT}\n"
        "The .blend is the source of truth. Edit it via MCP instead of regenerating."
    )

HERE = os.path.dirname(os.path.abspath(__file__))
LIB_ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
PALETTE = os.path.join(LIB_ROOT, "palette.blend")

# --------------------------------------------------------------------------
# proportions — 1.75 m, ratios lifted from AstronautArmature.fbx (see BUILD.md)
# --------------------------------------------------------------------------

H = 1.75                      # total height, metres

Z_HEADTOP  = 0.997 * H
Z_HEAD     = 0.753 * H        # skull base
Z_NECK     = 0.710 * H
Z_SHOULDER = 0.717 * H
Z_CHEST    = 0.624 * H
Z_SPINE1   = 0.548 * H
Z_SPINE    = 0.482 * H
Z_HIPS     = 0.425 * H
Z_UPLEG    = 0.393 * H
Z_KNEE     = 0.216 * H
Z_FOOT     = 0.066 * H
Z_TOE      = 0.0045 * H

X_SHOULDER = 0.026 * H
X_ARM      = 0.078 * H
X_ELBOW    = 0.197 * H
X_WRIST    = 0.306 * H
X_UPLEG    = 0.048 * H
X_KNEE     = 0.065 * H
X_FOOT     = 0.076 * H

# arm chain kept close to a true T-pose: elbow and wrist stay near shoulder height,
# which is what Mixamo/Unity Humanoid expects for a bind pose.
Z_ELBOW = Z_SHOULDER - 0.012
Z_WRIST = Z_SHOULDER - 0.022


def V(x, y, z):
    return Vector((x, y, z))


# --------------------------------------------------------------------------
# hand geometry — the ONE definition the mesh graph and the armature both use.
#
# These were duplicated at first, drifted apart, and the result was six finger
# bones sitting outside their own geometry drawing zero bone-heat weight. Bone
# heat does not warn about that; it just ships a joint that does nothing.
# --------------------------------------------------------------------------

KNUCKLE_X = X_WRIST + 0.092

# (name, y offset at knuckle, length, skin radius)
FINGERS = [
    ("Index",  -0.030, 0.086, 0.0155),
    ("Middle", -0.004, 0.094, 0.0160),
    ("Ring",    0.022, 0.086, 0.0150),
    ("Pinky",   0.046, 0.068, 0.0132),
]
THUMB_RADIUS = 0.0180


def finger_point(sx, dy, flen, t):
    """A point along a finger, t in [0,1] from knuckle to tip."""
    return V(sx * (KNUCKLE_X + flen * t), 0.004 + dy, Z_WRIST - 0.010 - 0.010 * t)


def thumb_point(sx, t):
    """A point along the thumb, t in [0,1] from base to tip."""
    return V(sx * (X_WRIST + 0.052 + 0.048 * t),
             0.004 - 0.034 - 0.032 * t,
             Z_WRIST - 0.014 - 0.010 * t)


# --------------------------------------------------------------------------
# the skeleton graph — (position, skin radius) nodes joined into chains
# --------------------------------------------------------------------------

def body_graph(anatomical):
    """Returns (nodes, edges, root_index).

    nodes: list of (Vector, radius_x, radius_z)
    edges: list of (i, j)
    """
    a = 1.0
    if anatomical:
        a = 1.09          # thicken the muscle bellies for the anatomical variant

    nodes = []
    edges = []

    def add(p, rx, rz=None):
        nodes.append((p, rx, rz if rz is not None else rx))
        return len(nodes) - 1

    def chain(idxs):
        for i, j in zip(idxs, idxs[1:]):
            edges.append((i, j))

    # ---- spine, neck, head (the trunk) ----------------------------------
    # Skin radii are (along-X, along-Z) in the plane perpendicular to the chain.
    # For a vertical chain that is (width, DEPTH) — so the second number controls
    # front-to-back thickness and must be well under the first, or the torso
    # renders as a cylinder rather than reading as a chest and back.
    n_pelvis = add(V(0, 0, Z_HIPS - 0.055), 0.104, 0.072)
    n_hips   = add(V(0, 0, Z_HIPS),         0.110, 0.075)
    n_waist  = add(V(0, 0, Z_SPINE),        0.100 * (1 / a if anatomical else 1), 0.070)
    n_ribs   = add(V(0, 0, Z_SPINE1),       0.112 * a, 0.074)
    n_chest  = add(V(0, 0, Z_CHEST),        0.130 * a, 0.081)
    n_clav   = add(V(0, 0, Z_SHOULDER - 0.022), 0.146 * a, 0.076)
    # Neck is SHORT — two close nodes — and the head sits directly on it. A long
    # neck chain is what turns the head into an egg on a stalk.
    n_neck0  = add(V(0, -0.002, Z_NECK - 0.014), 0.044, 0.042)
    n_neck1  = add(V(0, -0.004, Z_NECK + 0.030), 0.041, 0.040)
    # jaw/chin: pushed forward (-Y) so the head has a face direction
    n_jaw    = add(V(0, -0.020, Z_HEAD + 0.028), 0.062, 0.056)
    n_skull  = add(V(0, -0.008, Z_HEAD + 0.086), 0.077, 0.084)
    n_crown  = add(V(0,  0.002, Z_HEAD + 0.148), 0.050, 0.052)

    chain([n_pelvis, n_hips, n_waist, n_ribs, n_chest, n_clav,
           n_neck0, n_neck1, n_jaw, n_skull, n_crown])

    # ---- arms + hands ----------------------------------------------------
    for sx in (1.0, -1.0):
        n_sh = add(V(sx * X_ARM, -0.004, Z_SHOULDER - 0.006), 0.054 * a, 0.054 * a)
        n_bi = add(V(sx * (X_ARM + (X_ELBOW - X_ARM) * 0.45), 0.0,
                     Z_SHOULDER - 0.008), 0.043 * a, 0.043 * a)
        n_el = add(V(sx * X_ELBOW, 0.002, Z_ELBOW), 0.036, 0.036)
        n_fa = add(V(sx * (X_ELBOW + (X_WRIST - X_ELBOW) * 0.35), 0.003,
                     Z_ELBOW - 0.006), 0.035 * a, 0.035 * a)
        n_wr = add(V(sx * X_WRIST, 0.004, Z_WRIST), 0.025, 0.023)

        # connect the arm into the CLAVICLE node, so it welds into the trunk
        edges.append((n_clav, n_sh))
        chain([n_sh, n_bi, n_el, n_fa, n_wr])

        # palm: a flat slab. Radii here are (depth-in-Y, height-in-Z) because the
        # chain runs along X, so the palm is wide in Y and thin in Z.
        n_palm0 = add(V(sx * (X_WRIST + 0.030), 0.004, Z_WRIST - 0.004), 0.046, 0.019)
        n_palm1 = add(V(sx * (X_WRIST + 0.072), 0.004, Z_WRIST - 0.008), 0.048, 0.018)
        edges.append((n_wr, n_palm0))
        edges.append((n_palm0, n_palm1))

        # five fingers, three nodes each (knuckle, mid, tip), on the SHARED
        # finger curve the armature also uses. Radii are ~2x a first attempt:
        # below about 0.012 m the Skin modifier collapses a branch into the palm
        # slab and the finger silently vanishes.
        # k starts at 1: the palm node already stands in for the knuckle (t=0).
        # Adding a t=0 node duplicates it and produces non-manifold edges.
        for _fname, dy, flen, rad in FINGERS:
            prev = n_palm1
            for k in (1, 2, 3):
                t = k / 3.0
                p = finger_point(sx, dy, flen, t)
                nid = add(p, rad * (1.0 - 0.26 * t), rad * (1.0 - 0.26 * t))
                edges.append((prev, nid))
                prev = nid

        # thumb, angled forward and down off the base of the palm
        prev = n_palm0
        for k in (1, 2, 3):
            t = k / 3.0
            nid = add(thumb_point(sx, t),
                      THUMB_RADIUS * (1.0 - 0.24 * t),
                      THUMB_RADIUS * (1.0 - 0.24 * t))
            edges.append((prev, nid))
            prev = nid

    # ---- legs + block feet ------------------------------------------------
    for sx in (1.0, -1.0):
        n_hip  = add(V(sx * X_UPLEG, 0.0, Z_UPLEG), 0.078 * a, 0.078 * a)
        n_th   = add(V(sx * (X_UPLEG + (X_KNEE - X_UPLEG) * 0.45), -0.002,
                       Z_UPLEG - (Z_UPLEG - Z_KNEE) * 0.45), 0.064 * a, 0.064 * a)
        n_kn   = add(V(sx * X_KNEE, -0.004, Z_KNEE), 0.049, 0.049)
        n_cal  = add(V(sx * (X_KNEE + (X_FOOT - X_KNEE) * 0.35), 0.002,
                       Z_KNEE - (Z_KNEE - Z_FOOT) * 0.35), 0.047 * a, 0.047 * a)
        n_ank  = add(V(sx * X_FOOT, 0.010, Z_FOOT), 0.032, 0.032)

        # the leg hangs off the PELVIS node
        edges.append((n_pelvis, n_hip))
        chain([n_hip, n_th, n_kn, n_cal, n_ank])

        # Block foot. The chain runs along Y (heel -> toe), so the radii read as
        # (width-in-X, height-in-Z). The sole is set so the lowest skinned point
        # lands on z = 0 once the whole body is normalised to Z_HEADTOP.
        n_heel = add(V(sx * X_FOOT,  0.062, 0.046), 0.043, 0.044)
        n_ball = add(V(sx * X_FOOT, -0.070, 0.040), 0.047, 0.038)
        n_toe  = add(V(sx * X_FOOT, -0.140, 0.036), 0.041, 0.030)
        edges.append((n_ank, n_heel))
        edges.append((n_heel, n_ball))
        edges.append((n_ball, n_toe))

    return nodes, edges, n_pelvis


def build_body_object(name, anatomical, mat):
    nodes, edges, root = body_graph(anatomical)

    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bverts = [bm.verts.new(p) for (p, _rx, _rz) in nodes]
    bm.verts.index_update()
    for i, j in edges:
        try:
            bm.edges.new((bverts[i], bverts[j]))
        except ValueError:
            pass
    bm.to_mesh(me)
    bm.free()

    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)

    # Skin modifier turns the stick figure into one watertight shell
    skin = ob.modifiers.new("Skin", 'SKIN')
    skin.use_smooth_shade = True
    skin.branch_smoothing = 0.30

    sk = me.skin_vertices[0].data
    for i, (_p, rx, rz) in enumerate(nodes):
        sk[i].radius = (rx, rz)
    sk[root].use_root = True

    # subdivide the skinned result into a dense, smooth, sculptable cage
    sub = ob.modifiers.new("Subdivision", 'SUBSURF')
    sub.levels = 2
    sub.render_levels = 2

    return ob


# --------------------------------------------------------------------------
# armature — Mixamo names, matching AstronautArmature.fbx
# --------------------------------------------------------------------------

P = "mixamorig:"


def build_armature():
    arm_data = bpy.data.armatures.new("HUMANOIDBASE_Rig")
    arm_obj = bpy.data.objects.new("HUMANOIDBASE_Rig", arm_data)
    bpy.context.collection.objects.link(arm_obj)

    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    def mk(name, head, tail, parent=None, connect=False):
        b = eb.new(P + name)
        b.head = Vector(head)
        b.tail = Vector(tail)
        b.use_connect = connect
        if parent is not None:
            b.parent = eb[P + parent]
        return b

    mk("Hips",   (0, 0, Z_HIPS),   (0, 0, Z_SPINE))
    mk("Spine",  (0, 0, Z_SPINE),  (0, 0, Z_SPINE1), "Hips", True)
    mk("Spine1", (0, 0, Z_SPINE1), (0, 0, Z_CHEST),  "Spine", True)
    mk("Spine2", (0, 0, Z_CHEST),  (0, 0, Z_NECK),   "Spine1", True)
    mk("Neck",   (0, 0, Z_NECK),   (0, 0, Z_HEAD),   "Spine2", True)
    mk("Head",   (0, 0, Z_HEAD),   (0, 0, Z_HEADTOP - 0.075), "Neck", True)
    mk("HeadTop_End", (0, 0, Z_HEADTOP - 0.075), (0, 0, Z_HEADTOP), "Head", True)

    for side, sx in (("Left", 1.0), ("Right", -1.0)):
        mk(f"{side}Shoulder", (sx * X_SHOULDER, -0.006, Z_SHOULDER),
           (sx * X_ARM, -0.006, Z_SHOULDER - 0.006), "Spine2")
        mk(f"{side}Arm", (sx * X_ARM, -0.006, Z_SHOULDER - 0.006),
           (sx * X_ELBOW, 0.002, Z_ELBOW), f"{side}Shoulder", True)
        mk(f"{side}ForeArm", (sx * X_ELBOW, 0.002, Z_ELBOW),
           (sx * X_WRIST, 0.004, Z_WRIST), f"{side}Arm", True)
        mk(f"{side}Hand", (sx * X_WRIST, 0.004, Z_WRIST),
           (sx * (X_WRIST + 0.052), 0.004, Z_WRIST - 0.004), f"{side}ForeArm", True)

        # Finger bones ride the SAME curve as the mesh nodes (finger_point /
        # thumb_point). Duplicating these expressions is what once put six
        # phalanx bones outside their own geometry with zero weight.
        for fname, dy, flen, _rad in [("Thumb", None, None, None)] + FINGERS:
            parent = f"{side}Hand"
            if fname == "Thumb":
                pts = [thumb_point(sx, k / 3.0) for k in range(4)]
            else:
                pts = [finger_point(sx, dy, flen, k / 3.0) for k in range(4)]

            # Mixamo ships 4 bones per finger: 1,2,3 plus a *4 tip terminator
            tip_step = pts[3] - pts[2]
            allp = pts + [pts[3] + tip_step * 0.6]
            for j in (1, 2, 3, 4):
                mk(f"{side}Hand{fname}{j}", allp[j - 1], allp[j], parent, j > 1)
                parent = f"{side}Hand{fname}{j}"

    for side, sx in (("Left", 1.0), ("Right", -1.0)):
        mk(f"{side}UpLeg", (sx * X_UPLEG, 0.0, Z_UPLEG),
           (sx * X_KNEE, -0.004, Z_KNEE), "Hips")
        mk(f"{side}Leg", (sx * X_KNEE, -0.004, Z_KNEE),
           (sx * X_FOOT, 0.010, Z_FOOT), f"{side}UpLeg", True)
        mk(f"{side}Foot", (sx * X_FOOT, 0.010, Z_FOOT),
           (sx * X_FOOT, -0.088, Z_TOE + 0.020), f"{side}Leg", True)
        mk(f"{side}ToeBase", (sx * X_FOOT, -0.088, Z_TOE + 0.020),
           (sx * X_FOOT, -0.150, Z_TOE + 0.014), f"{side}Foot", True)
        mk(f"{side}Toe_End", (sx * X_FOOT, -0.150, Z_TOE + 0.014),
           (sx * X_FOOT, -0.196, Z_TOE + 0.012), f"{side}ToeBase", True)

    bpy.ops.object.mode_set(mode='OBJECT')
    return arm_obj


# --------------------------------------------------------------------------
# materials
# --------------------------------------------------------------------------

def link_palette_material(name):
    existing = bpy.data.materials.get(name)
    if existing:
        return existing
    if os.path.exists(PALETTE):
        with bpy.data.libraries.load(PALETTE, link=True) as (src, dst):
            if name in src.materials:
                dst.materials = [name]
        mat = bpy.data.materials.get(name)
        if mat:
            return mat
    print(f"  WARNING: '{name}' not found in palette; creating a local stub")
    return bpy.data.materials.new(name)


# --------------------------------------------------------------------------
# weight repair
# --------------------------------------------------------------------------

# Mixamo's *4 finger bones, Toe_End and HeadTop_End are pure chain terminators.
# They are unweighted on the astronaut too, and Unity's Humanoid mapping does not
# use them to deform. Only a dead bone OUTSIDE this set is a defect.
TERMINATOR_SUFFIXES = ("Thumb4", "Index4", "Middle4", "Ring4", "Pinky4",
                       "Toe_End", "HeadTop_End")


def repair_dead_bones(ob, arm):
    """Give weight to any deform bone that bone heat left empty.

    Bone heat silently drops bones whose geometry is thinner than it can resolve
    — here, the finger phalanges. It does NOT warn: it ships a joint that rotates
    and moves nothing. (The same trap is recorded in vrescal_sculpt_BUILD.md.)

    For each dead bone, the nearest vertices to its own segment are assigned to
    it directly, so every joint in the chain actually drives geometry.
    """
    totals = {}
    for v in ob.data.vertices:
        for g in v.groups:
            n = ob.vertex_groups[g.group].name
            totals[n] = totals.get(n, 0.0) + g.weight

    dead = [b for b in arm.data.bones
            if totals.get(b.name, 0.0) < 1e-6
            and not b.name.endswith(TERMINATOR_SUFFIXES)]
    if not dead:
        print(f"  {ob.name}: no dead bones")
        return

    coords = [v.co for v in ob.data.vertices]
    repaired = []
    for b in dead:
        head, tail = b.head_local, b.tail_local
        seg = tail - head
        L2 = seg.length_squared or 1e-12
        # distance from each vertex to the bone SEGMENT (not just its head)
        scored = []
        for i, co in enumerate(coords):
            t = max(0.0, min(1.0, (co - head).dot(seg) / L2))
            scored.append(((co - (head + seg * t)).length, i))
        scored.sort()
        # a phalanx is small; 12 verts is enough to own it without stealing the
        # whole finger from its neighbours
        take = scored[:12]
        vg = ob.vertex_groups.get(b.name) or ob.vertex_groups.new(name=b.name)
        radius = max(1e-4, max(d for d, _ in take))
        for d, i in take:
            w = max(0.05, 1.0 - (d / radius) ** 2)
            vg.add([i], w, 'ADD')
        repaired.append(b.name)

    print(f"  {ob.name}: repaired {len(repaired)} dead bone(s): "
          f"{', '.join(repaired[:6])}{' …' if len(repaired) > 6 else ''}")


# --------------------------------------------------------------------------
# assembly
# --------------------------------------------------------------------------

def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0

    mat = link_palette_material("Mat_Hide_Sand_Pale")

    coll_cage = bpy.data.collections.new("Coll_HumanoidBase_Cage")
    coll_anat = bpy.data.collections.new("Coll_HumanoidBase_Anatomical")
    coll_rig = bpy.data.collections.new("Coll_HumanoidBase_Rig")
    for c in (coll_cage, coll_anat, coll_rig):
        scene.collection.children.link(c)

    made = []
    for anatomical, coll, obj_name in (
        (False, coll_cage, "Mesh_HumanoidBase_Cage"),
        (True,  coll_anat, "Mesh_HumanoidBase_Anatomical"),
    ):
        ob = build_body_object(obj_name, anatomical, mat)

        # Skin + Subsurf are APPLIED here: their output is the sculpt cage.
        # Leaving them live would mean the user sculpts a stick figure.
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.select_all(action='DESELECT')
        ob.select_set(True)
        bpy.ops.object.modifier_apply(modifier="Skin")
        bpy.ops.object.modifier_apply(modifier="Subdivision")

        # weld, drop the seam, recalc normals
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        # the Skin modifier leaves a stray vert at each unused chain root
        stray = [v for v in bm.verts if not v.link_faces]
        if stray:
            bmesh.ops.delete(bm, geom=stray, context='VERTS')
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(ob.data)
        bm.free()
        ob.data.update()

        # Skin caps the crown short of the target height, and the skinned sole
        # lands a millimetre or two off zero. Normalise both in one pass: scale
        # about the sole so the body is exactly Z_HEADTOP tall, then translate
        # the sole onto z = 0 exactly.
        top = max(v.co.z for v in ob.data.vertices)
        bot = min(v.co.z for v in ob.data.vertices)
        if top > bot:
            k = Z_HEADTOP / (top - bot)
            for v in ob.data.vertices:
                v.co.z = (v.co.z - bot) * k
            ob.data.update()

        for p in ob.data.polygons:
            p.use_smooth = True

        # a fresh Subsurf stays LIVE on top, as the sculpting smoothing level
        sub = ob.modifiers.new("Subdivision", 'SUBSURF')
        sub.levels = 0
        sub.render_levels = 2

        for c in list(ob.users_collection):
            c.objects.unlink(ob)
        coll.objects.link(ob)
        made.append(ob)

    arm = build_armature()
    for c in list(arm.users_collection):
        c.objects.unlink(arm)
    coll_rig.objects.link(arm)

    # bind both meshes; Armature modifier must precede the live Subsurf
    for ob in made:
        bpy.ops.object.select_all(action='DESELECT')
        ob.select_set(True)
        arm.select_set(True)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.parent_set(type='ARMATURE_AUTO')
        idx = ob.modifiers.find("Armature")
        if idx > 0:
            with bpy.context.temp_override(object=ob):
                for _ in range(idx):
                    bpy.ops.object.modifier_move_up(modifier="Armature")

        repair_dead_bones(ob, arm)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=OUT)
    print(f"\nSaved {OUT}")

    print("\n=== measured ===")
    for ob in made:
        me = ob.data
        bm = bmesh.new()
        bm.from_mesh(me)
        bnd = sum(1 for e in bm.edges if len(e.link_faces) == 1)
        nm = sum(1 for e in bm.edges if len(e.link_faces) > 2)
        loose = sum(1 for v in bm.verts if not v.link_edges)
        bm.free()
        nq = sum(1 for p in me.polygons if len(p.vertices) == 4)
        nt = sum(1 for p in me.polygons if len(p.vertices) == 3)
        ng = sum(1 for p in me.polygons if len(p.vertices) > 4)
        d = ob.dimensions
        unw = sum(1 for v in me.vertices if not v.groups)
        print(f"  {ob.name}: {d.x:.3f} x {d.y:.3f} x {d.z:.3f} m  verts {len(me.vertices)}  "
              f"quads {nq} tris {nt} ngons {ng}")
        print(f"      boundary {bnd}  non-manifold {nm}  loose {loose}  unweighted {unw}  "
              f"vgroups {len(ob.vertex_groups)}")
    print(f"  armature: {len(arm.data.bones)} bones")


main()
