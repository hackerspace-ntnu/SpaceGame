"""Shared scaffolding for the Vrescal's per-part build scripts.

The animal is built one body part at a time -- each script in `parts/` writes
its own `.blend` -- and `assemble.py` links them into one file. Everything the
part scripts have to agree on lives here: the working space, the measurement
table taken off the concept art, and the surfacing machinery.

## Working space

    +X   forward (the nose end)
    +Y   port (the animal's left)
    +Z   up
    z = 0   the sole plane -- the ground the animal stands on
    1 unit  = 1 metre

This is a **metre-scale rebuild**. The older `common.py` in this folder works in
a legacy 3.6245-units-per-metre space inherited from a head sculpt that no
longer exists in this build; both it and `body.py` describe a six-legged animal
that the concept art does not, and they are left alone rather than edited.
Nothing here imports them.

## Where the numbers come from

Every dimension in `REF` was measured off the concept art against the human
scale silhouette standing beside the animal, taken as 1.75 m. That fixes the
vertical scale at 251 px/m. Horizontal distances along the body cannot use the
same scale -- the animal is turned roughly 40 degrees toward the viewer, so its
trunk is foreshortened to about 0.77 of its true length, and reading lengths off
the picture directly gives a stumpy animal that is correct in every height and
wrong as a silhouette. The trunk length in `REF` is the corrected figure.

## Why cross-sections are not ellipses

An ellipse has one degree of freedom per axis: taller or wider, and that is all.
Real cross-sections are different *shapes*, not different sizes of one shape --
a hump station is a narrow crest over a broad barrel, a ribcage is slab-sided
with a flat back, a haunch is a rounded egg, a brisket is keeled.

`Profile` therefore takes two independent controls:

  `widths`   half-width as a curve down the section, from crest (t=0) to keel
             (t=1). This is what states "narrow on top, broad low down".
  `ez`/`ey`  superellipse exponents. 2.0 is an ellipse; above it the section
             squares off (a flat back, slab sides); below it the section comes
             to a point (a keel, a hump crest).

Between them a single loft can carry a knife-crested hump, a boxy ribcage and
an egg-shaped haunch without any of them reading as the same tube resized, and
that is the difference between a body and a pipe.
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector, noise

HERE = os.path.dirname(os.path.abspath(__file__))
CREATURES = os.path.dirname(HERE)
LIB = os.path.dirname(os.path.dirname(CREATURES))
for _p in (LIB, CREATURES, HERE):
    if _p not in sys.path:
        sys.path.insert(0, _p)

import _buildlib as B          # noqa: E402
import vrescal_surface as S    # noqa: E402

PARTS = os.path.join(HERE, "parts")

# Ring resolution. Even, because every profile is built down the port side and
# mirrored -- an odd count puts the crest and keel off the centreline and the
# animal stops being symmetric.
RING = 28


# --------------------------------------------------------------------------
# The measurement table
# --------------------------------------------------------------------------
#
# Heights are above the sole plane. Longitudinal positions are metres forward
# of the chest, which is x = 0: the trunk runs aft into negative x and the neck
# and head forward into positive x.

REF = dict(
    # -- trunk ------------------------------------------------------------
    trunk_len=3.30,        # chest (x=0) to the back of the rump (x=-3.30)
    trunk_half_w=0.99,     # widest half-width, at the shoulder
    hump1_apex=3.78,       # front hump -- the tallest point on the animal
    hump1_x=-0.98,
    hump2_apex=3.14,       # rear hump, distinctly smaller
    hump2_x=-1.89,
    saddle=2.93,           # the dip between the two humps
    saddle_x=-1.44,
    withers=2.57,          # back line where the neck leaves the body
    rump_top=2.05,
    belly=1.33,            # ground clearance under the barrel
    brisket=1.37,          # bottom of the chest, between the forelegs

    # -- neck and head ----------------------------------------------------
    neck_base=(0.10, 0.0, 2.22),
    neck_top=(0.83, 0.0, 3.16),   # occiput -- where the head takes over
    head_len=0.90,
    head_top=3.42,
    snout_tip=(1.62, 0.0, 2.85),
    eye_h=3.10,
    jaw_bottom=2.71,

    # -- limbs ------------------------------------------------------------
    # The forelegs sit under the front hump rather than in front of it, so the
    # tallest mass on the animal stands over its own support.
    fore_x=-1.05,
    hind_x=-2.80,
    fore_y=0.70,           # shoulder socket, half-spacing
    hind_y=0.66,
    fore_foot_y=0.62,      # feet barely splay: a trestle stance reads as a toy
    hind_foot_y=0.60,
    elbow=1.12,
    carpus=0.60,
    fetlock=0.26,
    stifle=1.28,
    hock=0.66,
    foot_h=0.26,           # sole to the top of the pad

    # -- tail -------------------------------------------------------------
    tail_base=(-3.28, 0.0, 1.93),
    tail_tip=(-4.34, -0.30, 1.06),
)


def ref(*keys):
    """`REF` lookup that fails loudly on a typo instead of returning None."""
    out = [REF[k] for k in keys]
    return out[0] if len(out) == 1 else out


# --------------------------------------------------------------------------
# Materials
# --------------------------------------------------------------------------
#
# The reference is a three-tone animal and the palette already carries all
# three: a warm tan over the flanks and humps, a cold desaturated teal on the
# throat, belly, inner limbs and feet, and a dark umber for every scute. The
# spines are pale keratin and the eye is the one glossy thing on the animal.

HIDE = "Mat_Hide_Dune_Tan"
UNDER = "Mat_Hide_Slate_Teal"
SCUTE = "Mat_Hide_Scute_Umber"
HORN = "Mat_Hide_Ivory_Spine"
CLAW = "Mat_Hide_Claw_Horn"
EYE = "Mat_Hide_Eye_Amber"
PUPIL = "Mat_Neutral_Black_Matte"

SKIN_SET = [HIDE, UNDER, SCUTE]
HEAD_SET = [HIDE, UNDER, SCUTE, HORN, EYE, PUPIL]


def materials(names):
    return B.link_materials(names)


# --------------------------------------------------------------------------
# File lifecycle
# --------------------------------------------------------------------------

def parse_args():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def start():
    """Begin a part build in an empty scene."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0
    return scene


def save(out_path):
    """Write the part.

    Refuses to clobber unless `--overwrite` was passed. The guard exists
    because a `.blend` is the source of truth the moment anyone opens it in
    Blender: it may carry hand edits that exist nowhere else, and a generator
    run would destroy them silently. Pass the flag while iterating on a file
    that is still only ever machine-written; stop passing it the moment the
    file has been touched by hand.
    """
    out_path = os.path.abspath(out_path)
    if os.path.exists(out_path) and "--overwrite" not in parse_args():
        raise SystemExit(
            "Refusing to overwrite %s\n"
            "The .blend is the source of truth. Re-run with `-- --overwrite` "
            "only if this file has never been hand-edited." % out_path)
    for o in bpy.data.objects:
        if len(o.name) > 4 and o.name[-4] == '.' and o.name[-3:].isdigit():
            raise SystemExit("Auto-suffixed name reached save: %s" % o.name)
        if o.data is not None and hasattr(o.data, "name"):
            o.data.name = o.name
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=out_path)
    print("Wrote %s" % out_path)


def part_path(name):
    return os.path.join(PARTS, "%s.blend" % name)


def collection(name):
    coll = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(coll)
    return coll


# --------------------------------------------------------------------------
# Interpolation
# --------------------------------------------------------------------------

def spline1d(ts, vs, t):
    """Catmull-Rom through scalar control points on a non-uniform knot vector.

    Not smoothstep-between-neighbours: that has zero derivative at every
    control point, which puts a flat spot at each one, and on a body profile
    those flat spots read as faint rings all down the animal.
    """
    n = len(ts)
    if t <= ts[0]:
        return vs[0]
    if t >= ts[-1]:
        return vs[-1]
    # Finite-difference tangents, one-sided at the ends.
    m = []
    for i in range(n):
        if i == 0:
            m.append((vs[1] - vs[0]) / (ts[1] - ts[0]))
        elif i == n - 1:
            m.append((vs[-1] - vs[-2]) / (ts[-1] - ts[-2]))
        else:
            m.append((vs[i + 1] - vs[i - 1]) / (ts[i + 1] - ts[i - 1]))
    for i in range(n - 1):
        if ts[i] <= t <= ts[i + 1]:
            h = ts[i + 1] - ts[i]
            u = (t - ts[i]) / h
            u2, u3 = u * u, u * u * u
            return ((2 * u3 - 3 * u2 + 1) * vs[i]
                    + (u3 - 2 * u2 + u) * h * m[i]
                    + (-2 * u3 + 3 * u2) * vs[i + 1]
                    + (u3 - u2) * h * m[i + 1])
    return vs[-1]


def spline3d(points, samples):
    """Sample a Catmull-Rom curve through `points`, by index parameter."""
    pts = [Vector(p) for p in points]
    ts = list(range(len(pts)))
    out = []
    for k in range(samples):
        t = (len(pts) - 1) * k / float(samples - 1)
        out.append(Vector((
            spline1d(ts, [p.x for p in pts], t),
            spline1d(ts, [p.y for p in pts], t),
            spline1d(ts, [p.z for p in pts], t))))
    return out


def lerp(a, b, t):
    return a + (b - a) * t


def _pw(x, e):
    """Superellipse term: signed |x| raised to 2/e.

    e = 2 gives the plain ellipse. Above 2 the curve squares off toward a flat
    face; below 2 it draws in toward a point.
    """
    if x == 0.0:
        return 0.0
    return math.copysign(abs(x) ** (2.0 / e), x)


# --------------------------------------------------------------------------
# Cross-sections
# --------------------------------------------------------------------------

class Profile:
    """A closed cross-section in local (u lateral, v vertical) coordinates.

    `widths` is [(t, half_width), ...] with t = 0 at the crest and t = 1 at the
    keel -- the shape control described in the module docstring. The crest and
    keel keep a sliver of width rather than collapsing to a point: a zero-width
    ring end is a pole, `remove_doubles` merges it, subdivision pinches around
    it and the shading falls apart all down the spine.
    """

    def __init__(self, up, down, widths, ez=2.0, ey=2.0):
        self.up = float(up)
        self.down = float(down)
        self.ts = [float(t) for t, _w in widths]
        self.ws = [float(w) for _t, w in widths]
        self.ez = ez
        self.ey = ey

    def half_width(self, t):
        return max(0.0, spline1d(self.ts, self.ws, t))

    def points(self, n=RING):
        """Closed ring of (u, v), crest first, running down the port side."""
        half = n // 2
        mid = (self.up + self.down) * 0.5
        amp = (self.up - self.down) * 0.5
        pts = []
        for k in range(half + 1):
            a = math.pi * k / half
            c, s = math.cos(a), math.sin(a)
            t = (1.0 - c) * 0.5
            v = mid + amp * _pw(c, self.ez)
            u = self.half_width(t) * _pw(s, self.ey)
            pts.append((u, v))
        for k in range(half - 1, 0, -1):
            u, v = pts[k]
            pts.append((-u, v))
        return pts


class Section(Profile):
    """A `Profile` planted at a world x, spanning `top`..`bot` in world z.

    `lean` slides the upper half of the section forward, which is how a
    shoulder mass overhangs the leg beneath it rather than sitting square on
    top of it. It is what stops the forequarter reading as a stacked box.
    """

    def __init__(self, x, top, bot, widths, ez=2.0, ey=2.0, lean=0.0):
        Profile.__init__(self, top, bot, widths, ez, ey)
        self.x = x
        self.lean = lean

    def ring(self, n=RING):
        span = max(1e-6, self.up - self.down)
        out = []
        for u, v in self.points(n):
            t = (self.up - v) / span
            out.append(Vector((self.x + self.lean * (1.0 - t) ** 2, u, v)))
        return out


def loft(sections, n=RING):
    """World rings for a list of `Section`s."""
    return [s.ring(n) for s in sections]


# --------------------------------------------------------------------------
# Swept parts -- neck, limbs, tail, horns
# --------------------------------------------------------------------------

def frames(pts):
    """Parallel-transported frames along a centreline.

    Returns (origin, tangent, side, up) per point, with `side` toward port.
    Transporting the frame rather than rebuilding it from world up at every
    station is what keeps a swept part from twisting where its path turns
    steep -- a neck that rises through vertical would otherwise flip.
    """
    n = len(pts)
    tans = []
    for i in range(n):
        if i == 0:
            t = pts[1] - pts[0]
        elif i == n - 1:
            t = pts[-1] - pts[-2]
        else:
            t = pts[i + 1] - pts[i - 1]
        tans.append(t.normalized())

    ref_up = Vector((0.0, 0.0, 1.0))
    if abs(tans[0].dot(ref_up)) > 0.98:
        ref_up = Vector((1.0, 0.0, 0.0))
    side = ref_up.cross(tans[0]).normalized()

    out = []
    prev = tans[0]
    for p, t in zip(pts, tans):
        q = prev.rotation_difference(t)
        side = (q @ side)
        prev = t
        up = t.cross(side).normalized()
        side = up.cross(t).normalized()
        out.append((p, t, side, up))
    return out


def sweep(path, profiles, n=RING, roll=None):
    """Rings for `profiles` placed on the frames of `path`.

    `profiles` is one `Profile` per path point. `roll` is an optional
    per-station rotation of the profile about the tangent, in radians.
    """
    if len(profiles) != len(path):
        raise SystemExit("sweep: %d profiles for %d path points"
                         % (len(profiles), len(path)))
    rings = []
    for i, (p, t, side, up) in enumerate(frames(path)):
        if roll:
            q = Matrix.Rotation(roll[i], 4, t)
            side, up = (q @ side), (q @ up)
        rings.append([p + side * u + up * v
                      for u, v in profiles[i].points(n)])
    return rings


def tube(path, radii, n=RING, squash=None, ez=2.0, ey=2.0, crest=None):
    """Convenience sweep: a round-ish tube of varying radius.

    `squash` scales the vertical extent relative to the radius -- a limb is
    slightly deeper than it is wide, a tail is the other way about. `crest`
    optionally narrows the top of each section, which is what turns a plain
    tube into a ridged neck.
    """
    profs = []
    for i, r in enumerate(radii):
        v = r * (squash[i] if squash else 1.0)
        top = crest[i] if crest else 1.0
        profs.append(Profile(v, -v,
                             [(0.0, r * 0.05), (0.14, r * 0.62 * top),
                              (0.5, r), (0.86, r * 0.72), (1.0, r * 0.05)],
                             ez=ez, ey=ey))
    return sweep(path, profs, n)


# --------------------------------------------------------------------------
# Mesh assembly
# --------------------------------------------------------------------------

def bridge(bm, rows, closed=True):
    """Bridge equal-length rings into a quad grid, returning the vertex rows."""
    vrows = [[bm.verts.new(p) for p in row] for row in rows]
    for a, b in zip(vrows, vrows[1:]):
        n = len(a)
        for i in range(n if closed else n - 1):
            j = (i + 1) % n
            bm.faces.new((a[i], a[j], b[j], b[i]))
    return vrows


def cap(bm, vrow, flip=False):
    bm.faces.new(list(reversed(vrow)) if flip else list(vrow))


def subsurf(mesh, levels):
    """Catmull-Clark the mesh in place, via a temporary object.

    Done with the modifier rather than `bmesh.ops.subdivide_edges` because only
    the modifier is actually Catmull-Clark -- bmesh subdivision splits faces
    without moving the limit surface, which adds triangles and no smoothness.
    """
    if levels <= 0:
        return
    tmp = bpy.data.objects.new("_subsurf_tmp", mesh)
    bpy.context.scene.collection.objects.link(tmp)
    mod = tmp.modifiers.new("Subsurf", 'SUBSURF')
    mod.levels = mod.render_levels = levels
    bpy.context.view_layer.objects.active = tmp
    tmp.select_set(True)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    tmp.select_set(False)
    bpy.context.scene.collection.objects.unlink(tmp)
    bpy.data.objects.remove(tmp)


def build(rows, name, mats, coll, levels=2, cap_first=True, cap_last=True,
          closed=True, shape=None, paint=None, origin=None, weld=1e-5):
    """Rings in, finished object out.

    `shape` runs against the bmesh after subdivision -- the surfacing pass
    where muscles, folds and noise go. `paint` picks a material index per face
    centroid. Order matters: subdivide first so the shaping fields have
    vertices to work with, paint last so it sees the shaped surface.
    """
    bm = bmesh.new()
    vrows = bridge(bm, rows, closed=closed)
    if cap_first:
        cap(bm, vrows[0], flip=True)
    if cap_last:
        cap(bm, vrows[-1])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=weld)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for m in mats:
        mesh.materials.append(m)
    subsurf(mesh, levels)

    bm = bmesh.new()
    bm.from_mesh(mesh)
    if shape:
        shape(bm)
    bm.normal_update()
    if paint:
        for f in bm.faces:
            f.material_index = paint(f.calc_center_median(), f.normal)
    for f in bm.faces:
        f.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

    obj = bpy.data.objects.new(name, mesh)
    coll.objects.link(obj)
    if origin is not None:
        set_origin(obj, Vector(origin))
    return obj


def set_origin(obj, world_point):
    """Move the object's pivot to a world point without moving the geometry."""
    obj.data.transform(Matrix.Translation(-world_point))
    obj.location = world_point


# --------------------------------------------------------------------------
# Surfacing
# --------------------------------------------------------------------------

Blob = S.Blob
Ring = S.Ring
displace = S.displace
plate_mosaic = S.plate_mosaic


def skin(bm, scale=1.9, amount=0.016, octaves=3):
    """Low-amplitude turbulence. Nothing organic is ever perfectly smooth."""
    S.skin_noise(bm, scale, amount, octaves=octaves)


def wrinkle(bm, centre, axis, radius, tube, strength):
    """A single gathered skin crease, as a torus of negative displacement."""
    displace(bm, [Ring(centre, axis, radius, tube, strength)])


def hide_paint(tan_i=0, teal_i=1, low=1.62, high=2.05, jitter=0.16):
    """Material picker: cold underside below, warm hide above, ragged between.

    The boundary is dithered with turbulence rather than drawn at a height,
    because a clean horizontal line across a flank reads as a painted stripe.
    Countershading on a real animal has a broken edge.
    """
    def pick(c, _n):
        edge = lerp(low, high, 0.5) + noise.turbulence(c * 2.6, 2, False) * jitter
        return teal_i if c.z < edge else tan_i
    return pick


def report(objs=None):
    total = 0
    for o in sorted(objs or bpy.data.objects, key=lambda o: o.name):
        if o.type != 'MESH':
            continue
        tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
        total += tris
        d = o.dimensions
        print("  %-28s tris=%-7d  dims %5.2f x %5.2f x %5.2f m"
              % (o.name, tris, d.x, d.y, d.z))
    print("  TOTAL TRIS: %d" % total)
    return total


def measure(obj, label=""):
    """Print the world-space extent of an object, in metres above the sole."""
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts),
                 min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts),
                 max(p.z for p in pts)))
    print("  %-16s x %6.2f..%6.2f  y %6.2f..%6.2f  z %6.2f..%6.2f"
          % (label or obj.name, lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))
    return lo, hi
