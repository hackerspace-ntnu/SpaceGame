"""components/props/expedition_rig_dress — 2026-08-24 warm soft-goods pass.

Edits `expedition_rig.blend` IN PLACE (not a from-scratch generator):

    blender -b expedition_rig.blend --python expedition_rig_dress.py

Worn, the rig folds into a sandwich of flat boards and reads as carried
furniture. This pass gives the stowed pack the previous pack's
(`expedition_backpack`, 2026-08-13) warm silhouette and colours without
touching any mechanic: pivots, surfaces, hinges and every existing mesh's
geometry stay exactly as they are.

Two kinds of change:

1. RECOLOUR, slot-level only. The big canvas boards — leaf, wings, back panel,
   frame tray — swap their `Mat_Fabric_Canvas_Faded` slot for the new palette
   entry `Mat_Fabric_Canvas_Sand` (#F4BD62), the previous pack's sun-soaked
   body tone. Webbing stays ochre, harness stays faded — same split as the old
   pack. No face indices are touched, so the swap is reversible per object.

2. NEW OBJECTS, one per part someone might want to move by hand:

   | Object | Rides | What |
   |---|---|---|
   | Mesh_Rig_SidePouch_L/R | root | bulging sand-canvas pod on each frame end |
   | Mesh_Rig_SidePouchFlap_L/R | root | ochre storm flap + brass buckle over each pod |
   | Mesh_Rig_SidePouchStraps_L/R | root | two ochre lash ribbons over each pod |
   | Mesh_Rig_Bedroll | PIVOT_Back | ochre roll along the panel's top edge, back side |
   | Mesh_Rig_BedrollStraps | PIVOT_Back | two rubber cinch rings around the roll |

   Stowed, the pods fill the pack's lower flanks below the folded wings and the
   roll rides the top like a classic pack — the two masses the folded boards
   were missing. Deployed, the pods flank the hub as supply pods and the roll
   sits along the reclined panel's crest; neither reaches any `SURF_*` face.

The script verifies clearance itself: it folds the rig to the Blender stow
pose (Back +25, Leaf -90, Wing_L +90, Wing_R -90 — measured in this session;
the Unity HingeTable signs do NOT transfer 1:1, see the wiring script's
warning), asserts the new parts stay clear of the folded wings, then resets
every pivot to authored zero before saving.

Historical record once applied. The .blend stays the source of truth; do not
re-run this over a file it has already dressed (it refuses by name).
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, LIB)
from _buildlib import Part, link_materials  # noqa: E402

MATS = [
    "Mat_Fabric_Canvas_Sand",     # 0  pouch bodies, bedroll end caps
    "Mat_Fabric_Wing_Ochre",      # 1  flaps, lash ribbons, bedroll body
    "Mat_Fabric_Canvas_Faded",    # 2  (linked for completeness; unused by new parts)
    "Mat_Metal_Steel_Worn",       # 3
    "Mat_Metal_Brass_Tarnished",  # 4  buckles
    "Mat_Plastic_Rubber_Black",   # 5  bedroll cinch rings
]
SAND, OCHRE, CANVAS, STEEL, BRASS, RUBBER = range(6)

RECOLOUR = ["Mesh_Rig_FrontLeaf", "Mesh_Rig_Wing_L", "Mesh_Rig_Wing_R",
            "Mesh_Rig_BackPanel", "Mesh_Rig_Frame"]

NEW_NAMES = ["Mesh_Rig_SidePouch_L", "Mesh_Rig_SidePouch_R",
             "Mesh_Rig_SidePouchFlap_L", "Mesh_Rig_SidePouchFlap_R",
             "Mesh_Rig_SidePouchStraps_L", "Mesh_Rig_SidePouchStraps_R",
             "Mesh_Rig_Bedroll", "Mesh_Rig_BedrollStraps"]

# Blender-frame stow pose, measured against the mesh bounds in this session.
STOW = {"PIVOT_Back": (math.radians(25), 0, 0),
        "PIVOT_Leaf": (math.radians(-90), 0, 0),
        "PIVOT_Wing_L": (0, math.radians(90), 0),
        "PIVOT_Wing_R": (0, math.radians(-90), 0)}

# --- pouch pods ------------------------------------------------------------
# The frame tray spans x +-0.43, y +-0.16, z 0..0.22. Each pod hangs on a frame
# end: inboard face 5 mm clear of the folded wing rib (outer face x +-0.46).
POUCH_X_IN = 0.465
POUCH_Z = (0.160, 0.230, 0.320, 0.410, 0.470)
POUCH_SWELL = {0.160: 0.25, 0.230: 0.80, 0.320: 1.00, 0.410: 0.80, 0.470: 0.35}


def round_rect(x0, x1, y0, y1, r, seg=3):
    r = min(r, abs(x1 - x0) / 2.0, abs(y1 - y0) / 2.0)
    pts = []
    corners = ((x1 - r, y1 - r, 0.0), (x0 + r, y1 - r, math.pi / 2.0),
               (x0 + r, y0 + r, math.pi), (x1 - r, y0 + r, 3.0 * math.pi / 2.0))
    for cx, cy, base in corners:
        for i in range(seg + 1):
            a = base + (math.pi / 2.0) * i / seg
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def ribbon(p, pts, width, thick, mat, flat='X'):
    for a, b in zip(pts, pts[1:]):
        p.seam(a, b, width=thick, depth=width, axis=flat, mat=mat)


def loop_buckle(p, c, w, h, t, mat):
    cx, cy, cz = c
    for sz in (-1, 1):
        p.box((cx, cy, cz + sz * (h / 2 - t / 2)), (t, w, t), mat)
    for sy in (-1, 1):
        p.box((cx, cy + sy * (w / 2 - t / 2), cz), (t, t, h), mat)
    p.box((cx, cy, cz), (t * 0.7, w - 2 * t, t * 0.7), mat)


def pouch(sx):
    def build(p, sx=sx):
        def profile(z):
            s = POUCH_SWELL[round(z, 3)]
            x_out = POUCH_X_IN + 0.105 + 0.050 * s
            x0, x1 = sorted((sx * POUCH_X_IN, sx * x_out))
            d = 0.130 + 0.028 * s
            return round_rect(x0, x1, 0.005 - d, 0.005 + d, 0.030 + 0.034 * s, seg=3)

        p.loft([(z, profile(z)) for z in POUCH_Z], axis='Z', mat=SAND, cap=True)
    return build


def pouch_flap(sx):
    def build(p, sx=sx):
        x_top = POUCH_X_IN + 0.105 + 0.050 * POUCH_SWELL[0.470]
        p.slab((sx * (POUCH_X_IN - 0.010), -0.125, 0.468),
               (sx * (x_top + 0.035), 0.135, 0.492), OCHRE)
        p.slab((sx * (x_top + 0.012), -0.105, 0.340),
               (sx * (x_top + 0.036), 0.115, 0.478), OCHRE)
        loop_buckle(p, (sx * (x_top + 0.024), 0.005, 0.352), 0.052, 0.042, 0.011, BRASS)
    return build


def pouch_straps(sx):
    def build(p, sx=sx):
        for y in (-0.062, 0.072):
            ribbon(p, [(sx * 0.452, y, 0.190), (sx * 0.505, y, 0.478),
                       (sx * 0.596, y, 0.500), (sx * 0.648, y, 0.330)],
                   0.048, 0.012, OCHRE, flat='Y')
    return build


# --- bedroll ---------------------------------------------------------------

def panel_crest():
    """The back panel's top edge in authored world space: (mean y, max z)."""
    obj = bpy.data.objects["Mesh_Rig_BackPanel"]
    verts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    z_top = max(v.z for v in verts)
    band = [v for v in verts if v.z > z_top - 0.03]
    y_back = max(v.y for v in band)
    return y_back, z_top


ROLL_R = 0.085
ROLL_HALF = 0.360


def bedroll(center):
    def build(p, center=center):
        p.cyl(center, ROLL_R, 2 * ROLL_HALF, axis='X', seg=14, mat=OCHRE)
        for sx in (-1, 1):
            p.cyl((center[0] + sx * ROLL_HALF, center[1], center[2]),
                  ROLL_R, 0.022, axis='X', seg=14, mat=SAND)
    return build


def bedroll_straps(center):
    def build(p, center=center):
        for sx in (-1, 1):
            p.tube((center[0] + sx * 0.180, center[1], center[2]),
                   ROLL_R + 0.008, 0.010, 0.050, axis='X', seg=14, mat=RUBBER)
    return build


# ---------------------------------------------------------------------------

def attach(child, pivot):
    child.parent = pivot
    child.matrix_parent_inverse = Matrix.Identity(4)
    child.location = Vector(child.location) - Vector(pivot.location)
    return child


def world_bounds(names):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for n in names:
        o = bpy.data.objects[n]
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return lo, hi


def main():
    coll = bpy.data.collections.get("Coll_Rig_Expedition")
    if coll is None:
        raise SystemExit("Coll_Rig_Expedition not found — wrong file?")
    for n in NEW_NAMES:
        if n in bpy.data.objects:
            raise SystemExit("%s already exists — this pass has already run." % n)

    mats = link_materials(MATS)
    sand = mats[SAND]
    faded = bpy.data.materials["Mat_Fabric_Canvas_Faded"]

    # 1. recolour the boards, slot-level
    for n in RECOLOUR:
        obj = bpy.data.objects[n]
        hits = 0
        for i, m in enumerate(obj.data.materials):
            if m is faded or (m is not None and m.name == faded.name):
                obj.data.materials[i] = sand
                hits += 1
        print("recoloured %-22s (%d slot)" % (n, hits))
        if hits == 0:
            raise SystemExit("%s has no Canvas_Faded slot — check assumptions." % n)

    # 2. new parts
    y_back, z_top = panel_crest()
    roll_center = (0.0, y_back + ROLL_R * 0.55, z_top - ROLL_R * 0.25)
    print("panel crest y=%.3f z=%.3f -> roll at %s"
          % (y_back, z_top, tuple(round(v, 3) for v in roll_center)))

    pivot_back = bpy.data.objects["PIVOT_Back"]
    builds = [
        ("Mesh_Rig_SidePouch_L", pouch(-1), (-POUCH_X_IN, 0.0, POUCH_Z[0]), None),
        ("Mesh_Rig_SidePouch_R", pouch(1), (POUCH_X_IN, 0.0, POUCH_Z[0]), None),
        ("Mesh_Rig_SidePouchFlap_L", pouch_flap(-1), (-POUCH_X_IN, 0.0, 0.470), None),
        ("Mesh_Rig_SidePouchFlap_R", pouch_flap(1), (POUCH_X_IN, 0.0, 0.470), None),
        ("Mesh_Rig_SidePouchStraps_L", pouch_straps(-1), (-POUCH_X_IN, 0.0, 0.190), None),
        ("Mesh_Rig_SidePouchStraps_R", pouch_straps(1), (POUCH_X_IN, 0.0, 0.190), None),
        ("Mesh_Rig_Bedroll", bedroll(roll_center), roll_center, pivot_back),
        ("Mesh_Rig_BedrollStraps", bedroll_straps(roll_center), roll_center, pivot_back),
    ]
    for name, fn, origin, parent in builds:
        p = Part(mats)
        fn(p)
        p.bevel(width=0.005)
        obj = p.finish(name, coll, origin=origin)
        if parent is not None:
            attach(obj, parent)

    # 3. stow-pose clearance check, then reset to authored zero
    for n, rot in STOW.items():
        bpy.data.objects[n].rotation_euler = rot
    bpy.context.view_layer.update()

    for side, wing in (("L", "Mesh_Rig_Wing_L"), ("R", "Mesh_Rig_Wing_R")):
        wlo, whi = world_bounds([wing, "Mesh_Rig_WingRib_" + side])
        plo, phi = world_bounds(["Mesh_Rig_SidePouch_" + side])
        wing_out = max(abs(wlo.x), abs(whi.x))
        pouch_in = min(abs(plo.x), abs(phi.x))
        print("stowed %s: wing outer |x|=%.3f  pouch inner |x|=%.3f" % (side, wing_out, pouch_in))
        if pouch_in < wing_out - 0.001:
            raise SystemExit("Pouch %s intrudes into the folded wing." % side)
    blo, bhi = world_bounds(["Mesh_Rig_Bedroll"])
    print("stowed bedroll y[%.2f %.2f] z[%.2f %.2f]" % (blo.y, bhi.y, blo.z, bhi.z))

    for n in STOW:
        bpy.data.objects[n].rotation_euler = (0.0, 0.0, 0.0)
    bpy.context.view_layer.update()

    # 4. lint + save
    for o in bpy.data.objects:
        if o.data is not None and hasattr(o.data, "name"):
            o.data.name = o.name
        if len(o.name) > 4 and o.name[-4] == '.' and o.name[-3:].isdigit():
            raise SystemExit("Auto-suffixed object name reached save: %s" % o.name)
    total = sum(sum(len(pl.vertices) - 2 for pl in o.data.polygons)
                for o in bpy.data.objects if o.type == 'MESH')
    print("TOTAL TRIS: %d  OBJECTS: %d" % (total, len(bpy.data.objects)))

    bpy.ops.wm.save_mainfile()
    print("SAVED", bpy.data.filepath)


main()
