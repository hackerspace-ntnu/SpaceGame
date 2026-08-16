"""The Vrescal's main body -- the trunk, and nothing else.

No neck, no legs, no tail, no head, no rig. Those are separate scripts against
the sockets this one publishes at the bottom of the file.

    blender --background --python vrescal/body.py

Writes `vrescal/vrescal_wip.blend` -- **open that**, not `vrescal.blend`. The
shipped file still holds the previous four-legged animal and is deliberately
untouched until every part script here is finished and assembled. The WIP file
is generated output and is overwritten on every run; edit this script, not it.

## The silhouette

This is a **front-heavy** animal. The forequarter is enormous and carries a tall
hump; the body then falls away continuously to a low, light rear. That is the
whole design, and every number in `STATIONS` serves it:

    front hump apex     7.21 m      <- the tallest point, between the fore pairs
    fore shoulder line  6.18 m
    rear top            3.70 m      <- barely half the front
    belly               2.40 m
    trunk length        7.28 m
    trunk depth, front  5.02 m      <- nearly three times the previous build's
    trunk depth, rear   0.97 m

It carries **six legs: four fore, two hind**, which is what a mass distribution
this lopsided needs. The two fore pairs straddle the hump apex rather than
sitting in front of it, so the tallest part of the animal is directly over its
support. Socket positions are published as `FORE_LEG_X` and `HIND_LEG_X`.

**The trunk has to be longer than it is tall.** At 4.9 m long against 7.2 m of
height an earlier table produced something that could only read as a lump --
correct in every individual measurement and unrecognisable as a body. Depth is
what makes the front massive; length is what makes it an animal.

## Why the sections are shaped rather than elliptical

An ellipse cannot say "narrow at the crest and broad low down", and that single
sentence is most of what a big-shouldered animal looks like. `common.Section`
takes a half-width profile instead -- `w_top`, `w_mid`, `w_bot` and `t_mid`, the
height of the widest point -- so the hump stations can run a 0.80 crest over a
3.95 belly (a knife-backed mass) while the rump runs 2.40 over 3.10 (a rounded
egg). Same loft, genuinely different shapes, and it is the difference between a
body and a pipe.

`lean` slides the upper half of a section forward. The forequarter uses it so
the shoulder mass overhangs the legs beneath it instead of sitting square on
top of them, which is what stops the front reading as a stacked box.
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
CREATURES = os.path.dirname(HERE)
LIB = os.path.dirname(os.path.dirname(CREATURES))
for path in (LIB, CREATURES, HERE):
    if path not in sys.path:
        sys.path.insert(0, path)

import _buildlib as B          # noqa: E402
import vrescal_surface as S    # noqa: E402
import common as C             # noqa: E402

MATERIAL = "Mat_Hide_Dune_Tan"
SUBSURF = 2

# --------------------------------------------------------------------------
# Cross-sections, nose end first
# --------------------------------------------------------------------------
#
#   x        top      bot     w_mid  t_mid  w_top  w_bot  lean
#
# t_mid is where the widest point sits, 0 at the crest and 1 at the keel. Note
# how it climbs to 0.72 through the hump -- the mass hangs low and the crest is
# narrow -- and falls back to 0.44 over the rump, which is a rounder section.
# w_top does the same story from the other side: 0.80 at the hump peak against
# a 3.95 mid, and 2.40 against 3.10 at the hips.

STATIONS = [
    (-8.60,   3.20, -3.40, 2.45, 0.46, 1.10, 1.30,  0.00),   # chest, neck socket
    (-10.40,  6.20, -3.95, 3.10, 0.52, 1.45, 1.75,  0.20),
    (-12.20,  9.40, -4.25, 3.70, 0.58, 1.90, 2.15,  0.30),   # fore pair 1
    (-13.90, 11.80, -4.30, 4.00, 0.62, 2.20, 2.40,  0.35),
    (-15.50, 13.30, -4.30, 4.10, 0.64, 2.35, 2.45,  0.30),
    (-17.00, 13.90, -4.30, 4.10, 0.64, 2.40, 2.45,  0.15),   # HUMP APEX
    (-18.50, 13.50, -4.30, 4.05, 0.62, 2.35, 2.45,  0.00),
    (-20.10, 12.20, -4.30, 3.95, 0.60, 2.20, 2.40, -0.10),   # fore pair 2
    (-21.90, 10.20, -4.28, 3.80, 0.56, 2.10, 2.35, -0.15),
    (-23.80,  7.90, -4.25, 3.65, 0.52, 2.05, 2.30, -0.15),
    (-25.80,  5.90, -4.15, 3.50, 0.49, 2.10, 2.25, -0.10),
    (-27.80,  4.20, -4.05, 3.35, 0.47, 2.20, 2.20,  0.00),
    (-29.80,  2.80, -3.90, 3.20, 0.45, 2.25, 2.15,  0.00),   # hind pair
    (-31.60,  1.50, -3.55, 2.95, 0.45, 2.10, 1.95,  0.00),
    (-33.30,  0.40, -3.10, 2.45, 0.45, 1.75, 1.60,  0.00),   # croup
    (-35.00, -0.70, -2.60, 1.80, 0.47, 1.30, 1.20,  0.00),   # rump, tail socket
]


def sections():
    return [C.Section(x, top, bot, w_mid, t_mid, w_top, w_bot, lean)
            for (x, top, bot, w_mid, t_mid, w_top, w_bot, lean) in STATIONS]


# --------------------------------------------------------------------------
# Anatomy, trunk only
# --------------------------------------------------------------------------
#
# Kept deliberately weak. The trunk's volumes are in the sections above; these
# only say where the surface tightens or swells locally. A blob strong enough to
# *be* a mass inflates into a sphere stuck on the flank -- that mistake cost
# five rebuilds on the previous version of this animal.

def muscles():
    b = S.Blob
    return [
        b((-12.60, 3.30, 5.20), (2.60, 1.30, 3.40), 0.30),    # scapular shelf
        b((-23.20, 3.30, 5.60), (2.40, 1.25, 3.20), 0.26),    # rear of the hump
        b((-13.60, 3.60, -1.60), (2.60, 1.20, 2.30), 0.22),   # rib 1
        b((-18.10, 3.55, -1.80), (2.60, 1.20, 2.30), 0.20),   # rib 2
        b((-18.60, 3.40, -1.60), (2.40, 1.15, 2.10), 0.18),   # rib 3
        b((-29.20, 3.00, -0.90), (2.20, 1.30, 2.00), 0.26),   # gluteal shelf
        b((-9.80, 0.00, -3.20), (2.30, 1.70, 1.10), 0.24),    # sternum keel
        b((-21.00, 0.00, -4.40), (5.20, 2.40, 1.00), 0.18),   # belly
    ]


def folds():
    b = S.Blob
    return [
        # Behind each fore pair and ahead of the hind pair the skin gathers.
        # Without these the flank is one unbroken sheet from chest to croup.
        b((-13.40, 3.55, -1.10), (0.60, 1.45, 3.00), -0.22),
        b((-20.90, 3.50, -1.30), (0.60, 1.45, 3.00), -0.20),
        b((-28.20, 3.15, -0.90), (0.60, 1.35, 2.60), -0.18),
        # The saddle where the hump falls away into the back.
        b((-21.60, 0.00, 6.60), (1.30, 2.60, 1.60), -0.24),
    ]


SKIN_NOISE = (0.60, 0.062)


# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

def build(material, name="Mesh_Vrescal_Body"):
    """Loft the trunk, subdivide, and run the anatomy passes over it."""
    rings = C.loft(sections())

    bm = bmesh.new()
    rows = [[bm.verts.new(p) for p in ring] for ring in rings]
    for a, b_ in zip(rows, rows[1:]):
        for i in range(C.RING):
            j = (i + 1) % C.RING
            bm.faces.new((a[i], a[j], b_[j], b_[i]))

    # Cap both ends flat. The neck and tail scripts open them again at their
    # sockets; leaving them open here would make the preview inside-out and
    # hide any normals problem until three scripts later.
    bm.faces.new(list(reversed(rows[0])))
    bm.faces.new(rows[-1])

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    mesh.materials.append(material)

    mod = obj.modifiers.new("Subsurf", 'SUBSURF')
    mod.levels = mod.render_levels = SUBSURF
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)

    bm = bmesh.new()
    bm.from_mesh(mesh)
    S.displace(bm, muscles())
    S.displace(bm, folds())
    S.skin_noise(bm, SKIN_NOISE[0], SKIN_NOISE[1], octaves=3)
    for f in bm.faces:
        f.smooth = True
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return obj


# --------------------------------------------------------------------------
# Sockets -- the contract with the other part scripts
# --------------------------------------------------------------------------

NECK_SOCKET = (-8.60, 0.0, 0.60)
TAIL_SOCKET = (-35.00, 0.0, -1.60)

# Fore pairs straddle the hump peak so the tallest mass sits over its support.
FORE_LEG_X = (-12.20, -20.10)
HIND_LEG_X = (-29.80,)

# Half-width of the body at each leg station, so a limb script can place its
# socket inside the silhouette rather than guessing and standing proud of it.
LEG_SOCKET_HALF_WIDTH = {-12.20: 3.70, -20.10: 3.95, -29.80: 3.20}


def main():
    out = C.parse_out()
    C.start(out)
    material = B.link_materials([MATERIAL])[0]
    obj = build(material)

    # A ground plane, because every judgement about this animal is a judgement
    # about how tall it is, and a floating mesh answers that question wrong.
    bpy.ops.mesh.primitive_plane_add(size=90.0,
                                     location=(-20.0, 0.0, C.GROUND))
    bpy.context.active_object.name = "Ref_Ground"

    C.report("body", sections())
    zs = [v.co.z for v in obj.data.vertices]
    rear_from = STATIONS[-3][0]      # follows the table rather than a literal
    rear = max(v.co.z for v in obj.data.vertices if v.co.x < rear_from)
    print("  apex %.2f m, rear top %.2f m, length %.2f m, tris %d"
          % (C.height(max(zs)), C.height(rear),
             (STATIONS[0][0] - STATIONS[-1][0]) / C.UNITS_PER_M,
             sum(len(p.vertices) - 2 for p in obj.data.polygons)))
    C.save(out)


if __name__ == "__main__":
    main()
