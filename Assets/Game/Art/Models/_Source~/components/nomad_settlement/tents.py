"""Tensile shade sails for the nomad settlement.

Eighteen pitched sails, every one a tensioned membrane on thin raked timber poles —
no walls of their own, no furniture, no props, no metal. They replace the earlier
ten pastel tarp shelters, of which only the shade sail was worth keeping; this is
that one, turned into a family.

Eight are freestanding. The last ten are **wall-mounted**: they hang half their
corners off a building and drop the masts those corners would have needed, down
to `WallLean`, which has no mast at all. See S9 and the `Wall*` recipes.

Rules, so eighteen sails read as one camp:

  S1  One cloth colour per sail. No stripes, no borders, no second note. The
      camp's variety comes from silhouette and size, not from panelling.
  S2  Four saturated cloth colours only - orange, azure, red and white. These
      are pitched sails in full sun, not sun-killed tarps; the earlier pastel
      family read as washed out at camp distance and is retired.
  S3  Every edge is scalloped. A tensioned membrane pulls its free edges into
      concave curves, and a sail with straight edges reads as a flat billboard.
  S4  Every sail is a saddle. Corner heights alternate and the field dishes
      between them, so no sail is a plane from any angle.
  S5  Masts rake away from the sail they carry, the way a real mast is set so
      the cloth's pull brings it back toward upright.
  S6  Every corner is tied off - to a mast head with a guy to an anchor, to a
      fixing on a wall, or by a rope straight down to an anchor of its own.
  S7  One SEED reproduces the whole camp.
  S8  Wood and rope only, and poles stay thin. Girth does not follow length -
      see GIRTH_POW.
  S9  A wall-mounted sail puts its wall edge on y = 0 and hangs toward -Y, with
      its root empty on the wall face at ground level. It is dropped at the foot
      of a facade, not at the centre of a pitch.

What is generated and what is reused:

  * The **canopies are generated**. A sail's shape is a function of where you
    are on the membrane, so eighteen silhouettes means eighteen surfaces.
  * The **rigging is reused** from `components/structural/sail_rig.blend` -
    four masts, three ground anchors and three wall fixings, built for this job
    because the awning kit's poles each carry an outrigger brace modelled for a
    vertical stance, and raking one lays its brace across the sail it holds up.

    blender --background --python tents.py -- --out <path.blend>
"""

import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.abspath(
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")))
import _buildlib as bl  # noqa: E402

SEED = 20260914
GRID_PITCH = 12.0
GRID_COLS = 5

RIG = os.path.join(bl.LIB_ROOT, "components", "structural", "sail_rig.blend")

# S2 - the camp's whole cloth palette. Azure already existed as a saturated
# worksite tarp, which is exactly this job; the other three were added.
ORANGE = "Mat_Fabric_Sail_Orange"
BLUE = "Mat_Fabric_Tarp_Azure"
RED = "Mat_Fabric_Sail_Red"
WHITE = "Mat_Fabric_Sail_White"
ROPE_MAT = "Mat_Fabric_Rope_Hemp"
CLOTH_MATS = [ORANGE, BLUE, RED, WHITE, ROPE_MAT]

CLOTH_THICK = 0.022
GUY_RADIUS = 0.016
RAKE = math.radians(13.0)   # S5 - how far a mast leans away from its sail
GUY_RUN = 0.62              # guy anchor distance as a fraction of mast height

# S8 - a mast is stretched to length, but its girth is NOT stretched with it.
# Scaling a pole uniformly to reach 4.9 m also makes it 36% fatter, and the big
# sails ended up standing on piles while the small ones sat on twigs. Timber
# comes in one size in a camp, so girth follows length only weakly.
GIRTH_POW = 0.35

# Reused rigging, as (object, tie height, foot radius, rakeable).
#
# The tie height is the height of the part's hemp whipping in its own modelled
# space - what a mast is scaled by, since that is the point the sail corner has
# to land on, not the top of the pole above it. The foot radius is how far the
# butt of the pole reaches from its axis: raking a mast about its foot tips that
# butt, so the mast is bedded into the sand by `foot * sin(rake)` and its far
# edge buries instead of hanging in the air. A tripod's feet reach too far to
# bed that way, so it never rakes - it stands, which is what shear legs are for.
MASTS = {
    "pole":   ("Mesh_SailRig_MastPole", 3.60, 0.042, True),
    "lashed": ("Mesh_SailRig_MastLashed", 3.60, 0.052, True),
    "tripod": ("Mesh_SailRig_MastTripod", 3.60, 0.620, False),
    "stub":   ("Mesh_SailRig_MastStub", 1.40, 0.052, True),
}
ANCHORS = {
    "stake": "Mesh_SailRig_AnchorStake",
    "cleat": "Mesh_SailRig_AnchorCleat",
    "log":   "Mesh_SailRig_AnchorLog",
}

# S9 - wall fixings, as (object, eye offset from the wall face). A fixing is
# placed by the wall it is nailed to rather than by the ground, so unlike every
# other part here it is positioned from its own origin: the wall face is y = 0,
# the fixing hangs off it toward -Y, and `Camp.wall_fix()` sets it back by this
# offset so its rope eye lands exactly on the sail corner.
WALLFIX = {
    "hook":    ("Mesh_SailRig_WallHook", (0.0, -0.125, 0.0)),
    "cleat":   ("Mesh_SailRig_WallCleat", (0.0, -0.108, 0.0)),
    "bracket": ("Mesh_SailRig_WallBracket", (0.0, -0.460, 0.055)),
}
RIG_SRC = sorted(set(v[0] for v in MASTS.values()) | set(ANCHORS.values())
                 | set(v[0] for v in WALLFIX.values()))


def fix_corner(mount, kind, rz=0.0):
    """Where the sail corner lands for a fixing bolted to the wall at `mount`.

    A wall sail is authored the other way round from a pitched one: you choose
    where the fixing is nailed and the corner follows, because the corner is
    wherever that fixing's rope eye ends up. Authoring the corner first and
    working back to the fixing is what buried the `WallBracket`'s whole 0.46 m
    knee brace inside the building on the first cut of these, with only its ring
    showing on the facade.

    `rz` turns the fixing for a wall that does not run along X. A fixing points
    along -Y at rz = 0, and rotating by rz carries that onto (sin rz, -cos rz),
    so a wall at +X facing -X wants rz = -90 degrees and one at -X wants +90.
    """
    eye = Vector(WALLFIX[kind][1])
    eye.rotate(Matrix.Rotation(rz, 4, 'Z'))
    return tuple(Vector(mount) + eye)


# ------------------------------------------------------------------ cloth
def sheet(name, nu, nv, fn, mat, coll, bag, thick=CLOTH_THICK):
    """A membrane: a parametric quad surface given real thickness.

    `fn(u, v)` returns a point for u, v in [0, 1]. Solidified rather than left
    as a plane, so nothing in the camp is a single-sided face in engine.
    """
    bm = bmesh.new()
    grid = [[bm.verts.new(fn(i / nu, j / nv)) for j in range(nv + 1)]
            for i in range(nu + 1)]
    faces = []
    for i in range(nu):
        for j in range(nv):
            a, b = grid[i][j], grid[i + 1][j]
            c, d = grid[i + 1][j + 1], grid[i][j + 1]
            if len({a, b, c, d}) < 4:      # a degenerate cell at a fan centre
                continue
            try:
                faces.append(bm.faces.new((a, b, c, d)))
            except ValueError:             # that quad already exists
                continue
    bmesh.ops.solidify(bm, geom=faces, thickness=thick)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    o.data.materials.append(mat)
    coll.objects.link(o)
    bag.append(o)
    return o


def tube(name, pts, rad, mat, coll, bag, segs=6):
    """Sweep a circle along a polyline. One reference vector for the whole run,
    so the ring's origin cannot rotate mid-span and twist the tube."""
    span = (pts[-1] - pts[0]).normalized()
    up = Vector((0, 0, 1)) if abs(span.z) < 0.85 else Vector((1, 0, 0))
    bm = bmesh.new()
    rings = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        n1 = t.cross(up)
        if n1.length < 1e-4:
            n1 = t.cross(Vector((0, 1, 0)))
        n1.normalize()
        n2 = t.cross(n1).normalized()
        rings.append([bm.verts.new(
            p + rad * (math.cos(k * 2 * math.pi / segs) * n1
                       + math.sin(k * 2 * math.pi / segs) * n2))
            for k in range(segs)])
    for a, b in zip(rings, rings[1:]):
        for k in range(segs):
            bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
    bm.faces.new(rings[0])
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    o.data.materials.append(mat)
    coll.objects.link(o)
    bag.append(o)
    return o


def guy(name, a, b, sag, mat, coll, bag):
    """S6 - a rope from a mast head or a sail corner down to a ground anchor."""
    pts = []
    for i in range(9):
        t = i / 8.0
        p = a.lerp(b, t)
        p.z -= sag * 4.0 * t * (1.0 - t)
        pts.append(p)
    return tube(name, pts, GUY_RADIUS, mat, coll, bag)


# ------------------------------------------------------------- sail fields
def quad_field(corners, scallop, dip):
    """A four-cornered membrane: bilinear between the corners, edges pulled in.

    The corner heights alone make the saddle - a bilinear patch over four
    corners at alternating heights *is* a hypar (S4). `scallop` bows each free
    edge inward by remapping the parameter domain rather than displacing the
    surface, so the corners stay exactly on their mast heads (S3).
    """
    c00, c10, c11, c01 = (Vector(c) for c in corners)

    def fn(u, v):
        su = scallop * math.sin(math.pi * v)
        sv = scallop * math.sin(math.pi * u)
        uu = su + u * (1.0 - 2.0 * su)
        vv = sv + v * (1.0 - 2.0 * sv)
        p = (c00 * (1 - uu) * (1 - vv) + c10 * uu * (1 - vv)
             + c11 * uu * vv + c01 * (1 - uu) * vv)
        p.z -= dip * math.sin(math.pi * uu) * math.sin(math.pi * vv)
        return p
    return fn


def radial_field(corners, crown, scallop, dip):
    """An N-cornered membrane fanned out from a crown point.

    `corners` runs anticlockwise; `crown` is the height of the centre above the
    mean corner height. Each edge between two corners is a concave arc pulled
    toward the centre, which is what makes a five- or six-point sail read as
    cloth in tension rather than a flat polygon.
    """
    pts = [Vector(c) for c in corners]
    n = len(pts)
    mid = Vector((sum(p.x for p in pts) / n, sum(p.y for p in pts) / n,
                  sum(p.z for p in pts) / n + crown))

    def boundary(u):
        s = (u % 1.0) * n
        i = int(s) % n
        t = s - int(s)
        a, b = pts[i], pts[(i + 1) % n]
        p = a.lerp(b, t)
        # Pull the edge in across the ground only. Pulling the whole point
        # toward the crown drags the edge's height up with it, and on a sail
        # whose corners sit well below the crown that folds the free edge into
        # a crease instead of curving it.
        pull = scallop * math.sin(math.pi * t)
        p.x += (mid.x - p.x) * pull
        p.y += (mid.y - p.y) * pull
        p.z -= 0.5 * dip * math.sin(math.pi * t)
        return p

    def fn(u, v):
        b = boundary(u)
        p = mid.lerp(b, v)
        p.z = mid.z + (b.z - mid.z) * v ** 1.25
        p.z -= dip * math.sin(math.pi * v)
        return p
    return fn


# -------------------------------------------------------------------- camp
class Camp:
    """One sail under construction: its collection, its part bag, its origin."""

    def __init__(self, idx, name, ox, oy, src, rng):
        self.idx, self.name = idx, name
        self.o = Vector((ox, oy, 0.0))
        self.src, self.rng, self.bag = src, rng, []
        self.seen = {}
        self.coll = bl.collection("Coll_NomadSail_%s" % name)
        self.tag = "S%02d" % idx

    def uniq(self, label):
        """Repeated parts - six masts, eight anchors - need distinct names, and
        letting Blender auto-suffix them would ship `.001` names to the file."""
        n = self.seen.get(label, 0)
        self.seen[label] = n + 1
        return label if n == 0 else "%s%d" % (label, n)

    # -- rigging ----------------------------------------------------------
    def mast(self, tip, kind="pole", rake=RAKE, guyed=True,
             anchor="stake", out=None):
        """Plant a mast whose head eye lands exactly on `tip`.

        The mast rakes away from the sail, so its foot stands *inboard* of the
        head. `out` is the outward direction in XY; without one it is taken
        from the tent origin toward the corner, which is what a sail pitched
        about its own centre wants.
        """
        tip = Vector(tip)
        d = Vector((out[0], out[1], 0.0)) if out else Vector((tip.x, tip.y, 0.0))
        if d.length < 1e-4:
            d = Vector((1.0, 0.0, 0.0))
        d.normalize()

        name, eye_z, foot_r, rakeable = MASTS[kind]
        if not rakeable:
            rake = 0.0
        sink = foot_r * math.sin(rake)
        run = (tip.z + sink) * math.tan(rake)
        foot = Vector((tip.x - d.x * run, tip.y - d.y * run, -sink))
        length = (tip.z + sink) / math.cos(rake)
        s = self.src[name]
        # Tilt about X carries +Z onto (sin(rx)*sin(rz), -sin(rx)*cos(rz),
        # cos(rx)), so the lean direction is (sin(rz), -cos(rz)) - which fixes
        # rz for a wanted `d`. Getting this backwards rakes every mast into its
        # own sail, so it is derived, never guessed.
        rz = math.atan2(d.x, -d.y)
        stretch = length / eye_z
        girth = stretch ** GIRTH_POW
        bl.stamp([s], bl.place_delta([s], self.o + foot,
                                     (girth, girth, stretch), rz, rake, 0.0,
                                     anchor="foot"),
                 self.coll, "%s_%s" % (self.tag, self.uniq("Mast")), self.bag)

        if guyed:
            far = Vector((tip.x + d.x * tip.z * GUY_RUN,
                          tip.y + d.y * tip.z * GUY_RUN, 0.05))
            self.rope("Guy", tip, far, sag=0.05 + 0.03 * tip.z / 3.0)
            self.anchor(far - Vector((0, 0, 0.05)), anchor, rz)
        return foot

    def anchor(self, at, kind="stake", rz=0.0):
        s = self.src[ANCHORS[kind]]
        bl.stamp([s], bl.place_delta([s], self.o + Vector(at), 1.0, rz, 0.0,
                                     0.0, anchor="foot"),
                 self.coll, "%s_%s" % (self.tag, self.uniq("Anchor")), self.bag)

    def wall_fix(self, mount, kind="cleat", rz=0.0):
        """S9 - bolt a fixing to a building so a sail corner can hang off it
        instead of standing a mast under it.

        `mount` is on the wall face, which is y = 0 for a wall running along X;
        the sail then hangs toward -Y. Pair every call with `fix_corner()` on
        the same mount and kind, which is where the corner actually lands.
        """
        name, _ = WALLFIX[kind]
        s = self.src[name]
        delta = (Matrix.Translation(self.o + Vector(mount))
                 @ Matrix.Rotation(rz, 4, 'Z'))
        bl.stamp([s], delta, self.coll,
                 "%s_%s" % (self.tag, self.uniq("WallFix")), self.bag)

    def tiedown(self, corner, kind="stake", reach=0.55, out=None):
        """S6 - a corner with no mast, roped straight out to the sand."""
        corner = Vector(corner)
        d = (Vector((out[0], out[1], 0.0)) if out
             else Vector((corner.x, corner.y, 0.0)))
        if d.length < 1e-4:
            d = Vector((1.0, 0.0, 0.0))
        d.normalize()
        far = Vector((corner.x + d.x * reach, corner.y + d.y * reach, 0.06))
        self.rope("Tie", corner, far, sag=0.04)
        self.anchor(far - Vector((0, 0, 0.06)), kind, math.atan2(d.x, -d.y))

    # -- cloth ------------------------------------------------------------
    def cloth(self, nu, nv, fn, mat):
        return sheet("%s_%s" % (self.tag, self.uniq("Canopy")), nu, nv,
                     lambda u, v: self.o + fn(u, v), mat, self.coll, self.bag)

    def rope(self, label, a, b, sag=0.06):
        return guy("%s_%s" % (self.tag, self.uniq(label)), self.o + Vector(a),
                   self.o + Vector(b), sag, self.src["__rope"], self.coll,
                   self.bag)

    def finish(self):
        e = bpy.data.objects.new("Empty_%s_%s_Root" % (self.tag, self.name), None)
        e.empty_display_type = 'PLAIN_AXES'
        e.empty_display_size = 0.5
        e.location = self.o
        self.coll.objects.link(e)
        bpy.context.view_layer.update()
        for o in self.bag:
            o.parent = e
            o.matrix_parent_inverse = e.matrix_world.inverted()
        self.coll.instance_offset = self.o
        return len(self.bag) + 1


def ring(n, radius, heights, phase=0.0, squash=1.0):
    """N corners on an ellipse, one height each - the spine of every radial sail."""
    return [(radius * math.sin(phase + i * 2 * math.pi / n),
             -radius * squash * math.cos(phase + i * 2 * math.pi / n),
             heights[i % len(heights)]) for i in range(n)]


# ------------------------------------------------------------------- sails
def s_quad_small(c, M):
    """The plain one: a small square hypar on four straight masts."""
    W, H, TW = 1.70, 2.40, 0.46
    corners = [(-W, -W, H - TW), (W, -W, H + TW), (W, W, H - TW), (-W, W, H + TW)]
    c.cloth(12, 12, quad_field(corners, 0.13, 0.26), M[ORANGE])
    for p in corners:
        c.mast(p, "pole")


def s_quad_large(c, M):
    """The camp's biggest square: a deep 7 m hypar on stepped masts."""
    W, H, TW = 3.50, 3.80, 0.95
    corners = [(-W, -W, H - TW), (W, -W, H + TW), (W, W, H - TW), (-W, W, H + TW)]
    c.cloth(18, 18, quad_field(corners, 0.16, 0.55), M[BLUE])
    for p in corners:
        c.mast(p, "lashed", anchor="log")


def s_tri(c, M):
    """A three-point sail - the fewest corners a membrane can have."""
    corners = ring(3, 2.90, [3.30, 2.15, 2.55], phase=math.radians(20))
    c.cloth(21, 8, radial_field(corners, 0.34, 0.16, 0.22), M[RED])
    for p in corners:
        c.mast(p, "pole")


def s_tri_tall(c, M):
    """One tall peak, two corners pulled to the ground - a wing, not a roof."""
    corners = [(0.0, -2.85, 4.40), (2.70, 2.05, 1.45), (-2.70, 2.05, 1.45)]
    c.cloth(21, 9, radial_field(corners, 0.28, 0.17, 0.26), M[WHITE])
    c.mast(corners[0], "pole", anchor="cleat")
    for p in corners[1:]:
        c.tiedown(p, "log", reach=0.70)


def s_penta(c, M):
    """Five corners at alternating heights - the camp's rippling awning."""
    corners = ring(5, 3.10, [3.45, 2.10, 3.05, 2.30, 2.85])
    c.cloth(30, 9, radial_field(corners, 0.40, 0.15, 0.28), M[ORANGE])
    for i, p in enumerate(corners):
        c.mast(p, "tripod" if i % 2 == 0 else "pole")


def s_hex_low(c, M):
    """Wide, low and gentle: a six-point sail you walk under without ducking."""
    corners = ring(6, 3.80, [2.35, 1.55], squash=0.86)
    c.cloth(36, 8, radial_field(corners, 0.28, 0.13, 0.20), M[BLUE])
    for i, p in enumerate(corners):
        c.mast(p, "pole" if i % 2 == 0 else "stub", anchor="stake")


def s_ribbon(c, M):
    """A long narrow run - a shaded corridor rather than a shaded room."""
    W, L = 1.15, 4.20
    corners = [(-W, -L, 2.95), (W, -L, 1.85), (W, L, 2.85), (-W, L, 1.75)]
    c.cloth(8, 24, quad_field(corners, 0.16, 0.30), M[RED])
    for p in corners:
        c.mast(p, "pole" if p[2] > 2.4 else "stub",
               out=(p[0] * 2.2, p[1]), anchor="cleat")


def s_kite(c, M):
    """A four-point kite stretched between one high mast and one low anchor -
    the most twisted sheet in the camp."""
    corners = [(-1.20, -2.40, 1.30), (2.60, -0.60, 3.95),
               (1.10, 2.55, 1.70), (-2.45, 0.75, 2.95)]
    c.cloth(16, 16, quad_field(corners, 0.14, 0.40), M[WHITE])
    c.mast(corners[1], "lashed", anchor="log")
    c.mast(corners[3], "pole", anchor="cleat")
    for i in (0, 2):
        c.mast(corners[i], "stub", anchor="stake")


# ------------------------------------------------------- wall-mounted sails
# Half the masts of a freestanding sail, because the building carries the other
# half. Every one of these has its wall edge on y = 0 and hangs toward -Y, and
# its root empty sits on the wall face at ground level - so the collection
# instance is dropped at the foot of a facade, not at the centre of a pitch.
def hang(c, mounts, kind, rz=0.0):
    """Bolt a run of fixings to a wall and hand back the corners they make."""
    for m in mounts:
        c.wall_fix(m, kind, rz)
    return [fix_corner(m, kind, rz) for m in mounts]


def s_wall_quad(c, M):
    """The wall counterpart of QuadSmall: a hypar with two corners on the
    building and two on poles. Two masts instead of four."""
    W, L = 1.85, 3.40
    wallc = hang(c, [(-W, 0.0, 3.35), (W, 0.0, 2.75)], "cleat")
    free = [(W, -L, 3.15), (-W, -L, 2.55)]
    c.cloth(14, 14, quad_field(wallc + free, 0.13, 0.30), M[ORANGE])
    for p in free:
        c.mast(p, "pole", anchor="stake")


def s_wall_tri(c, M):
    """Three corners, two of them on the wall. One mast in the whole sail."""
    wallc = hang(c, [(2.05, 0.0, 2.65), (-1.90, 0.0, 3.45)], "hook")
    free = (0.15, -3.85, 2.95)
    c.cloth(21, 9, radial_field(wallc + [free], 0.30, 0.16, 0.24), M[BLUE])
    c.mast(free, "pole", anchor="cleat")


def s_wall_strip(c, M):
    """A shaded walkway down the length of a facade, on brackets that stand the
    tie clear of whatever the wall already carries. Two stubs instead of four
    full masts."""
    W, L = 2.20, 2.15
    wallc = hang(c, [(-W, 0.0, 2.95), (W, 0.0, 2.45)], "bracket")
    free = [(W, -L, 1.85), (-W, -L, 2.25)]
    c.cloth(20, 10, quad_field(wallc + free, 0.15, 0.22), M[RED])
    for p in free:
        c.mast(p, "stub", anchor="stake")


def s_wall_lean(c, M):
    """No masts at all: the wall holds the high edge and the low edge is roped
    straight down to the sand. The cheapest shelter in the camp."""
    W, L = 1.95, 3.10
    wallc = hang(c, [(-W, 0.0, 3.75), (W, 0.0, 3.45)], "cleat")
    free = [(W, -L, 1.15), (-W, -L, 1.45)]
    c.cloth(14, 16, quad_field(wallc + free, 0.14, 0.26), M[WHITE])
    for p in free:
        c.tiedown(p, "log", reach=0.75, out=(0.0, -1.0))


def s_wall_porch(c, M):
    """Narrow and deep - a porch over a doorway rather than a shaded yard."""
    W, L = 1.25, 4.10
    wallc = hang(c, [(-W, 0.0, 3.50), (W, 0.0, 3.25)], "cleat")
    free = [(W, -L, 2.85), (-W, -L, 3.10)]
    c.cloth(10, 20, quad_field(wallc + free, 0.14, 0.30), M[RED])
    for p in free:
        c.mast(p, "pole", anchor="cleat")


def s_wall_corner(c, M):
    """Pinned to TWO walls where a building turns a corner: four fixings carry
    it and one pole holds the open side. The wall that runs along Y is at
    x = +3.10 and faces -X, so its fixings are turned -90 degrees."""
    XW = 3.10
    a = hang(c, [(-2.30, 0.0, 3.30), (0.40, 0.0, 3.05)], "cleat")
    b = hang(c, [(XW, -0.70, 3.20), (XW, -2.60, 2.80)], "cleat",
             rz=math.radians(-90))
    free = (0.10, -3.90, 2.95)
    corners = [free, b[1], b[0], a[1], a[0]]      # anticlockwise
    c.cloth(30, 9, radial_field(corners, 0.34, 0.15, 0.26), M[ORANGE])
    c.mast(free, "pole", anchor="log")


def s_wall_fan(c, M):
    """A fan: three fixings spread along one long wall gathering to a single
    pole. One mast where a freestanding four-point sail needs four."""
    a = hang(c, [(-2.70, 0.0, 2.95), (0.0, 0.0, 3.30), (2.70, 0.0, 2.85)],
             "hook")
    free = (0.0, -3.60, 2.55)
    corners = [free, a[2], a[1], a[0]]            # anticlockwise
    c.cloth(28, 10, radial_field(corners, 0.26, 0.14, 0.26), M[BLUE])
    c.mast(free, "pole", anchor="stake")


def s_wall_canopy_long(c, M):
    """The long one: 7.2 m of facade under cover on three brackets and three
    poles, where the freestanding six-point sail needs six."""
    a = hang(c, [(-3.60, 0.0, 2.80), (0.0, 0.0, 3.00), (3.60, 0.0, 2.75)],
             "bracket")
    free = [(-3.20, -2.40, 2.05), (0.0, -2.60, 2.35), (3.20, -2.40, 2.10)]
    corners = [free[0], free[1], free[2], a[2], a[1], a[0]]   # anticlockwise
    c.cloth(36, 9, radial_field(corners, 0.22, 0.13, 0.22), M[WHITE])
    for p in free:
        c.mast(p, "pole", anchor="stake")


def s_wall_billow(c, M):
    """The inverse of WallLean: the wall holds the LOW edge and the sail lifts
    away from the building, so it opens to the sky instead of shedding to it."""
    W, L = 2.25, 3.00
    wallc = hang(c, [(-W, 0.0, 1.85), (W, 0.0, 2.80)], "hook")
    free = [(W, -L, 3.20), (-W, -L, 4.05)]
    c.cloth(14, 14, quad_field(wallc + free, 0.14, 0.34), M[ORANGE])
    for p in free:
        c.mast(p, "pole", anchor="log")


def s_wall_spur(c, M):
    """The smallest thing in the camp: a scrap of shade off a wall on one stub.
    Two hooks and a post."""
    a = hang(c, [(-1.30, 0.0, 2.55), (1.30, 0.0, 2.25)], "hook")
    free = (0.10, -1.90, 1.70)
    corners = [free, a[1], a[0]]                  # anticlockwise
    c.cloth(18, 8, radial_field(corners, 0.18, 0.15, 0.16), M[BLUE])
    c.mast(free, "stub", anchor="stake")


SAILS = [("QuadSmall", s_quad_small), ("QuadLarge", s_quad_large),
         ("Tri", s_tri), ("TriTall", s_tri_tall), ("Penta", s_penta),
         ("HexLow", s_hex_low), ("Ribbon", s_ribbon), ("Kite", s_kite),
         ("WallQuad", s_wall_quad), ("WallTri", s_wall_tri),
         ("WallStrip", s_wall_strip), ("WallLean", s_wall_lean),
         ("WallPorch", s_wall_porch), ("WallCorner", s_wall_corner),
         ("WallFan", s_wall_fan), ("WallCanopyLong", s_wall_canopy_long),
         ("WallBillow", s_wall_billow), ("WallSpur", s_wall_spur)]


# ------------------------------------------------------------------- main
def dedupe_materials():
    """Appending a component file can land `Mat_X.001` beside `Mat_X`. Fold the
    copies back onto the original so the camp ships one of each."""
    for m in list(bpy.data.materials):
        if len(m.name) > 4 and m.name[-4] == '.' and m.name[-3:].isdigit():
            base = bpy.data.materials.get(m.name[:-4])
            if base is not None:
                m.user_remap(base)
                bpy.data.materials.remove(m)
    for m in list(bpy.data.materials):
        if m.users == 0 or (m.users == 1 and m.use_fake_user):
            bpy.data.materials.remove(m)


def main():
    out = bl.parse_out()
    bl.start(out)
    rng = random.Random(SEED)

    hidden = bl.collection("Coll_PartSource")
    bl.append_objects(RIG, RIG_SRC, hidden)
    src = {n: bpy.data.objects[n] for n in RIG_SRC}
    dedupe_materials()

    mats = bl.link_materials(CLOTH_MATS)
    M = {n: m for n, m in zip(CLOTH_MATS, mats)}
    src["__rope"] = M[ROPE_MAT]

    for i, (name, fn) in enumerate(SAILS):
        c = Camp(i + 1, name, (i % GRID_COLS) * GRID_PITCH,
                 -(i // GRID_COLS) * GRID_PITCH, src,
                 random.Random(rng.randrange(1 << 30)))
        fn(c, M)
        n = c.finish()
        lo, hi = bl.bbox([o for o in c.coll.objects if o.type == 'MESH'])
        print("  %-12s parts=%3d  footprint %.2f x %.2f m  height %.2f m"
              % (name, n, hi.x - lo.x, hi.y - lo.y, hi.z))

    for o in list(hidden.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.collections.remove(hidden)

    dupes = [o.name for o in bpy.data.objects
             if len(o.name) > 4 and o.name[-4] == '.' and o.name[-3:].isdigit()]
    if dupes:
        raise SystemExit("Auto-suffixed names reached save: %s" % dupes[:8])

    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(out))
    print("Wrote %s  sails=%d objects=%d meshes=%d materials=%d"
          % (out, len(SAILS), len(bpy.data.objects), len(bpy.data.meshes),
             len(bpy.data.materials)))


if __name__ == "__main__":
    main()
