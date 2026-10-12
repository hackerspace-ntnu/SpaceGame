"""Rebuild the Hauler and Patched variations from the hand-finished Runner.

Run INSIDE desert_monowheel.blend (Blender MCP):

    exec(open(r"<this file>").read())

The generator's own Hauler and Patched were never finished and predate every hand edit
to the Runner, so they are deleted and rebuilt as copies of the Runner -- which the user
calls perfect and which this script never touches -- plus what made each one distinct:

| Collection | Runner copy plus |
| --- | --- |
| `Coll_Monowheel_Hauler`  | the Hauler's cargo: rug-wrapped bundle, bedroll and lashing on the ski, a trade-goods sack either side of the ski's rear, and a saddlebag on each side hoop (bedroll left, canisters right) -- all from `monowheel_luggage.py` |
| `Coll_Monowheel_Patched` | the Patched's wear: rust patch plates bolted to the ring, mismatched paddle boards (two silvered timber, one steel), one board torn off leaving its bare mount, a rusted plate riveted over the ski deck, and a tattered pennant on a mast above the tail post |

Historical record: refuses to run once the new variants exist (marked `from_runner`).
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(bpy.data.filepath))
for p in (os.path.abspath(os.path.join(HERE, "..", "..")), HERE):
    if p not in sys.path:
        sys.path.insert(0, p)
import desert_monowheel as DM
import monowheel_luggage as L

O = bpy.data.objects
SRC = "Runner"
for name in ("Hauler", "Patched"):
    c = bpy.data.collections.get("Coll_Monowheel_%s" % name)
    if c is not None and c.get("from_runner"):
        raise RuntimeError("%s already rebuilt from the Runner -- edit it in place." % name)

TIMBER_BOARDS = (1, 6)      # paddle boards swapped to silvered timber
STEEL_BOARD = 3             # a scavenged steel plate
TORN_BOARD = 5              # torn off: the mount stays, the board is gone
MAST_HEIGHT = 0.9           # pennant mast above the tail post cap
PENNANT = (0.95, 0.42)      # length streaming back, height at the mast


# ── clear out the unfinished generator variants ─────────────────────────────

def delete_collection(name):
    coll = bpy.data.collections.get(name)
    if coll is None:
        return 0
    doomed = list(coll.all_objects)
    for o in doomed:
        data = o.data
        O.remove(o)
        if data is not None and data.users == 0:
            {bpy.types.Mesh: bpy.data.meshes, bpy.types.Armature: bpy.data.armatures}[type(data)].remove(data)
    bpy.data.collections.remove(coll)
    return len(doomed)


# ── copy the Runner ──────────────────────────────────────────────────────────

def copy_runner(tag):
    src = bpy.data.collections["Coll_Monowheel_%s" % SRC]
    dst = bpy.data.collections.new("Coll_Monowheel_%s" % tag)
    bpy.context.scene.collection.children.link(dst)
    dst["from_runner"] = True
    twin = {}
    for o in src.objects:
        n = o.copy()
        if o.data is not None:
            n.data = o.data.copy()
        n.name = o.name.replace("_" + SRC, "_" + tag)
        if n.data is not None:
            n.data.name = n.name
        dst.objects.link(n)
        twin[o] = n
    for o, n in twin.items():
        if o.parent in twin:
            n.parent, n.parent_type, n.parent_bone = twin[o.parent], o.parent_type, o.parent_bone
            n.matrix_parent_inverse = o.matrix_parent_inverse.copy()
    bpy.context.view_layer.update()
    return dst


def to_chassis(tag, objs):
    # A just-built object reads an identity matrix_world until the view layer
    # updates; parenting it before that drops it at the origin.
    bpy.context.view_layer.update()
    arm = O["Arm_Monowheel_%s" % tag]
    b = arm.data.bones["Bone_Chassis"]
    rest = arm.matrix_world @ b.matrix_local @ Matrix.Translation((0, b.length, 0))
    for o in objs:
        w = o.matrix_world.copy()
        o.parent, o.parent_type, o.parent_bone = arm, 'BONE', "Bone_Chassis"
        o.matrix_parent_inverse = rest.inverted()
        o.matrix_world = w


def to_object(objs, parent):
    for o in objs:
        w = o.matrix_world.copy()
        o.parent = parent
        o.matrix_parent_inverse = parent.matrix_world.inverted()
        o.matrix_world = w


# ── Hauler ───────────────────────────────────────────────────────────────────

def hauler():
    coll = copy_runner("Hauler")
    new = L.add({
        "tag": "Hauler", "coll": coll,
        # the cheek rails come down onto the ski's rear half, so the bundle rides forward
        "deck": "Mesh_SkiDeck_Hauler", "deck_fit": (-3.0, -1.6), "bundle_y": -2.7,
        # one sack rides the rear rails behind the seat, centred: the cheek rails climb
        # to the tail post there and close in to +-0.3 m, so two side by side cut them.
        # Past y = 2.5 it is clear of the paddle sweep, and short of the post at 3.24.
        "sacks": [(0.0, 2.78, "Mesh_TradeGoods_SackPile_Sack01")],
        "sack_rest": (["Mesh_RearRailL_Hauler", "Mesh_RearRailR_Hauler"], (2.5, 3.2), 0.6),
        "hoops": {1: "Mesh_SideHoopL_Hauler", -1: "Mesh_SideHoopR_Hauler"},
        # behind the foot pedals (which reach back to y = -0.23). The hoop's straight run
        # has cross-sections only at y = 0.84 and 1.20 before it bends up toward the tail
        # at 1.48, so both canisters hang within 0.84..1.20.
        "bag_y": 0.40, "bag_size": (0.26, 0.62, 0.52),
        "kits": {1: "bedroll", -1: "canisters"}, "canister_y": (0.88, 1.18),
    })
    to_chassis("Hauler", new)
    return new


# ── Patched ──────────────────────────────────────────────────────────────────

def ring_patches(ring):
    """The generator's patch plates, carried onto the hand-moved ring.

    The Runner's ring is the generator's ring (same radius and band), moved and
    tilted by hand -- so the plates are built in the generator's frame, hub at
    (0, 0, HUB_Z), and moved by the ring's own world matrix.
    """
    before = set(O)
    DM.build_patches({"name": "Patched"}, ring.users_collection[0])
    made = [o for o in O if o not in before]
    bpy.context.view_layer.update()
    to_ring = ring.matrix_world @ Matrix.Translation((0.0, 0.0, -DM.HUB_Z))
    for o in made:
        o.matrix_world = to_ring @ o.matrix_world
    bpy.context.view_layer.update()
    to_object(made, ring)
    return made


def mismatched_boards():
    steel, timber = (B_mat("Mat_Metal_Steel_Worn"), B_mat("Mat_Wood_Timber_Silvered"))
    for i in TIMBER_BOARDS:
        O["Mesh_Paddle%02d_Patched" % i].data.materials[0] = timber
    O["Mesh_Paddle%02d_Patched" % STEEL_BOARD].data.materials[0] = steel
    torn = O["Mesh_Paddle%02d_Patched" % TORN_BOARD]
    data = torn.data
    O.remove(torn)
    bpy.data.meshes.remove(data)


def B_mat(name):
    return L.B.link_materials([name])[0]


def ski_patch(coll):
    deck_z, frame = L.deck_frame(O["Mesh_SkiDeck_Patched"], (-3.0, -1.6))
    y, x = -2.05, -0.22
    at = Matrix.Translation((x, y, deck_z(y) + 0.008)) @ frame
    p = DM.part("rust", "iron")
    p.box((0, 0, 0), (0.46, 0.58, 0.014))
    for dx in (-0.19, 0.19):
        for dy in (-0.25, 0.25):
            p.cyl((dx, dy, 0.009), 0.014, 0.01, seg=6, mat=1)
    p.bm.transform(at)
    return p.finish("Mesh_SkiPatch_Patched", coll, origin=at.translation)


def pennant(coll):
    cap = O["Mesh_TailPostCapHigh_Patched"]
    pts = [cap.matrix_world @ v.co for v in cap.data.vertices]
    base = Vector((sum(p.x for p in pts) / len(pts), sum(p.y for p in pts) / len(pts), max(p.z for p in pts)))
    top = base + Vector((0, 0, MAST_HEIGHT))
    mast = DM.part("timber", "rope")
    DM.bar(mast, base - Vector((0, 0, 0.12)), top, r=0.028)
    mast.torus(base + Vector((0, 0, 0.04)), 0.045, 0.012, maj_seg=12, min_seg=6, mat=1)
    made = [mast.finish("Mesh_PennantMast_Patched", coll, origin=base)]

    length, height = PENNANT
    rag = [1.0, 0.82, 0.95, 0.7, 0.88, 0.62, 0.8]      # frayed, uneven trailing edge
    rows = []
    for i in range(len(rag)):
        t = i / (len(rag) - 1)
        z = top.z - 0.04 - height * t * (0.55 + 0.45 * (1 - t))
        reach = length * rag[i] * (1 - 0.55 * t)
        rows.append([Vector((0.05 * math.sin(3.2 * k / 7 * math.pi) * (k / 7),
                             base.y + 0.03 + reach * k / 7, z - 0.06 * (k / 7) ** 2))
                     for k in range(8)])
    flag = DM.part("canvas")
    flag.sheet(rows, 0.012)
    made.append(flag.finish("Mesh_Pennant_Patched", coll, origin=top))
    return made


def patched():
    coll = copy_runner("Patched")
    ring = O["Mesh_Ring_Patched"]
    L.use_palette()
    ring_patches(ring)
    mismatched_boards()
    added = [ski_patch(coll)] + pennant(coll)
    to_chassis("Patched", added)


# ── run ──────────────────────────────────────────────────────────────────────

gone = delete_collection("Coll_Monowheel_Hauler") + delete_collection("Coll_Monowheel_Patched")
hauler()
patched()
layer = bpy.context.view_layer.layer_collection.children
for name in ("Coll_Monowheel_Hauler", "Coll_Monowheel_Patched"):
    layer[name].hide_viewport = True
print("removed %d old variant objects; rebuilt Hauler and Patched from the Runner" % gone)
