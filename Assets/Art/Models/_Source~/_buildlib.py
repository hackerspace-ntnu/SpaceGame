"""Shared geometry helpers for the model library's generation scripts.

Every component script in this library is a thin layer over this module: the
per-component script decides *what* the thing looks like, this module knows how
to make a bevelled box, a capped cylinder, a row of rivets, and a bmesh that
ends up as a correctly-named object carrying palette materials.

It exists because the alternative — each component script carrying its own copy
of "make a cube and bevel it" — is how a library ends up with fourteen subtly
different bevel widths.

Import it by path, since the scripts run under `blender --background` where the
library root is not on sys.path:

    import sys, os
    sys.path.insert(0, "<repo>/Assets/Models/_Source~")
    from _buildlib import *
"""

import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

LIB_ROOT = os.path.dirname(os.path.abspath(__file__))
PALETTE = os.path.join(LIB_ROOT, "palette.blend")


def repo_root(start=None):
    """The Unity project root — the folder holding ProjectSettings.

    Found by walking up rather than by counting `os.path.dirname()` calls, so
    scripts that reach into `Assets/` keep working no matter how deep in the
    tree this library lives. Counting levels is what broke when the library
    moved from `<repo>/models` to `<repo>/Assets/Models/_Source~`.
    """
    d = os.path.dirname(os.path.abspath(start or __file__))
    while d != os.path.dirname(d):
        if os.path.isdir(os.path.join(d, "ProjectSettings")):
            return d
        d = os.path.dirname(d)
    raise SystemExit("Could not find the Unity project root above %s" % (start or __file__))


REPO_ROOT = repo_root()

# Uniform scale applied by Part.finish() to the emitted mesh and its origin.
# Defaults to 1.0, so scripts that never touch it are unaffected. Set it when a
# component family is authored at convenient round numbers but has to ship at a
# different real-world size — scaling the source beats scaling objects, because
# the object's scale stays 1.0 and the .blend needs no apply step.
SCALE = 1.0


# --------------------------------------------------------------------------
# Scene / file lifecycle
# --------------------------------------------------------------------------

def parse_out():
    """Read `--out <path>` from the args after `--`."""
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--out" not in argv:
        raise SystemExit("Missing required argument: --out <path.blend>")
    return argv[argv.index("--out") + 1]


def start(out_path):
    """Begin a new-file build. Refuses to clobber an existing .blend.

    The .blend is the source of truth: once a file exists it may carry hand
    edits that exist nowhere else, so a generator must never run over it again.
    """
    if os.path.exists(out_path):
        raise SystemExit(
            "Refusing to overwrite existing file: %s\n"
            "The .blend is the source of truth. Edit it via MCP instead of "
            "regenerating." % out_path
        )
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0
    return scene


def save(out_path):
    for o in bpy.data.objects:
        if o.data is not None and hasattr(o.data, "name"):
            o.data.name = o.name
        if len(o.name) > 4 and o.name[-4] == '.' and o.name[-3:].isdigit():
            raise SystemExit("Auto-suffixed object name reached save: %s" % o.name)
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(out_path))
    print("Wrote %s" % out_path)


def link_materials(names):
    """Link the named materials from the shared palette.

    Linked rather than appended, so a palette edit propagates into every model
    that uses it instead of leaving stale copies behind.
    """
    missing = [n for n in names if n not in bpy.data.materials]
    if missing:
        with bpy.data.libraries.load(PALETTE, link=True) as (src, dst):
            unknown = [n for n in missing if n not in set(src.materials)]
            if unknown:
                raise SystemExit(
                    "Not in the palette: %s\nAdd it via scripts/palette.py "
                    "rather than defining it locally." % ", ".join(unknown))
            dst.materials = missing
    return [bpy.data.materials[n] for n in names]


def collection(name, parent=None):
    coll = bpy.data.collections.new(name)
    (parent or bpy.context.scene.collection).children.link(coll)
    return coll


# --------------------------------------------------------------------------
# Part — a bmesh under construction, with per-face material tracking
# --------------------------------------------------------------------------

class Part:
    """One mesh under construction.

    Every geometry call returns the faces it created, so material assignment
    and selective bevelling can target them. `mat` indexes the material list
    the Part was constructed with.
    """

    def __init__(self, materials):
        self.bm = bmesh.new()
        self.materials = list(materials)

    # -- internals ---------------------------------------------------------

    def _tag(self, faces, mat):
        for f in faces:
            f.material_index = mat
        return faces

    def _absorb(self, bm2, mat):
        """Merge a scratch bmesh into this part, returning the new faces."""
        bmesh.ops.recalc_face_normals(bm2, faces=bm2.faces)
        mesh = bpy.data.meshes.new("_scratch")
        bm2.to_mesh(mesh)
        bm2.free()
        self.bm.faces.ensure_lookup_table()
        n_before = len(self.bm.faces)
        self.bm.from_mesh(mesh)
        self.bm.faces.ensure_lookup_table()
        bpy.data.meshes.remove(mesh)
        return self._tag(self.bm.faces[n_before:], mat)

    # -- primitives --------------------------------------------------------

    def box(self, center, size, mat=0, rot=None):
        """Axis-aligned box, optionally rotated about its own centre.

        `size` is the full extent, not the half-extent — a 2 m cube is (2,2,2).
        """
        m = (Matrix.Translation(Vector(center)) @ (rot or Matrix.Identity(4))
             @ Matrix.Diagonal(Vector(size)).to_4x4())
        res = bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)
        return self._tag(_faces_of(res["verts"]), mat)

    def slab(self, lo, hi, mat=0):
        """Box given as opposite corners — the form most measurements arrive in.

        Corners are sorted per axis, so a caller that mirrors a slab by negating
        one side gets a box rather than an inside-out one.
        """
        lo, hi = Vector(lo), Vector(hi)
        a = Vector((min(lo.x, hi.x), min(lo.y, hi.y), min(lo.z, hi.z)))
        b = Vector((max(lo.x, hi.x), max(lo.y, hi.y), max(lo.z, hi.z)))
        return self.box((a + b) / 2.0, b - a, mat)

    def cyl(self, center, radius, depth, axis='Z', seg=16, mat=0, cap=True,
            radius_top=None, rot=None):
        """`rot` tilts the cylinder off its axis — needed for swept bends and
        anything that does not run square to the ship."""
        m = (Matrix.Translation(Vector(center)) @ (rot or Matrix.Identity(4))
             @ _axis_rot(axis))
        res = bmesh.ops.create_cone(
            self.bm, cap_ends=cap, cap_tris=False, segments=seg,
            radius1=radius,
            radius2=radius if radius_top is None else radius_top,
            depth=depth, matrix=m)
        faces = _faces_of(res["verts"])
        # Smooth the barrel, leave the caps flat. Doing this here rather than
        # bevelling curved edges later is what keeps the poly budget sane.
        _smooth_around(faces, (m.to_3x3() @ Vector((0, 0, 1))).normalized())
        return self._tag(faces, mat)

    def tube(self, center, radius, thickness, depth, axis='Z', seg=16, mat=0):
        """Hollow pipe — outer and inner walls closed by two annular end caps."""
        bm2 = bmesh.new()
        ro, ri = radius, radius - thickness
        hz = depth / 2.0
        rings = []
        for r in (ro, ri):
            for w in (-hz, hz):
                rings.append([
                    bm2.verts.new(_plane_point(
                        axis, r * math.cos(2 * math.pi * i / seg),
                        r * math.sin(2 * math.pi * i / seg), w))
                    for i in range(seg)])
        ob, ot, ib, it = rings
        for a, b in ((ob, ot), (ib, it), (ob, ib), (ot, it)):
            for i in range(seg):
                j = (i + 1) % seg
                bm2.faces.new((a[i], a[j], b[j], b[i]))
        bmesh.ops.translate(bm2, vec=Vector(center), verts=bm2.verts)
        faces = self._absorb(bm2, mat)
        _smooth_around(faces, _axis_vec(axis))
        return faces

    def torus(self, center, major, minor, axis='Z', maj_seg=24, min_seg=10,
              mat=0):
        bm2 = bmesh.new()
        grid = []
        for i in range(maj_seg):
            a = 2 * math.pi * i / maj_seg
            row = []
            for j in range(min_seg):
                b = 2 * math.pi * j / min_seg
                r = major + minor * math.cos(b)
                row.append(bm2.verts.new(_plane_point(
                    axis, r * math.cos(a), r * math.sin(a),
                    minor * math.sin(b))))
            grid.append(row)
        for i in range(maj_seg):
            for j in range(min_seg):
                i2, j2 = (i + 1) % maj_seg, (j + 1) % min_seg
                bm2.faces.new((grid[i][j], grid[i2][j], grid[i2][j2],
                               grid[i][j2]))
        bmesh.ops.translate(bm2, vec=Vector(center), verts=bm2.verts)
        faces = self._absorb(bm2, mat)
        for f in faces:          # a torus is curved in both directions
            f.smooth = True
        return faces

    def prism(self, profile, extrude, axis='Y', mat=0, offset=(0, 0, 0)):
        """Extrude a closed 2-D profile into a solid.

        `profile` is a list of (u, v) in the plane perpendicular to `axis`.
        This is how every non-boxy silhouette in the library gets made — hull
        cross-sections, seat shells, console wedges, wing aerofoils.
        """
        bm2 = bmesh.new()
        half = extrude / 2.0
        lo = [bm2.verts.new(_plane_point(axis, u, v, -half)) for u, v in profile]
        hi = [bm2.verts.new(_plane_point(axis, u, v, half)) for u, v in profile]
        bm2.faces.new(lo)
        bm2.faces.new(list(reversed(hi)))
        n = len(profile)
        for i in range(n):
            j = (i + 1) % n
            bm2.faces.new((lo[i], lo[j], hi[j], hi[i]))
        bmesh.ops.translate(bm2, vec=Vector(offset), verts=bm2.verts)
        return self._absorb(bm2, mat)

    def loft(self, sections, axis='Y', mat=0, cap=True):
        """Bridge a sequence of equal-length profiles placed along `axis`.

        `sections` is [(w, [(u, v), ...]), ...] — the offset along the axis and
        the profile at that station. This is what gives the hull its tapering
        shape instead of a stack of boxes.
        """
        bm2 = bmesh.new()
        rings = [[bm2.verts.new(_plane_point(axis, u, v, w)) for u, v in prof]
                 for w, prof in sections]
        n = len(sections[0][1])
        for a, b in zip(rings, rings[1:]):
            for i in range(n):
                j = (i + 1) % n
                bm2.faces.new((a[i], a[j], b[j], b[i]))
        if cap:
            bm2.faces.new(rings[0])
            bm2.faces.new(list(reversed(rings[-1])))
        faces = self._absorb(bm2, mat)
        _smooth_around(faces, _axis_vec(axis))
        return faces

    # -- detail generators -------------------------------------------------

    def rivets(self, start, end, count, radius=0.018, height=0.012,
               axis='Z', mat=0):
        """A row of dome-headed fasteners between two points."""
        start, end = Vector(start), Vector(end)
        faces = []
        for i in range(count):
            t = (i + 0.5) / count
            faces += self.cyl(start.lerp(end, t), radius, height, axis, seg=6,
                              mat=mat, radius_top=radius * 0.7)
        return faces

    def seam(self, a, b, width=0.024, depth=0.02, axis='Z', mat=0):
        """A raised strip between two points — the cheapest read of 'plating'."""
        a, b = Vector(a), Vector(b)
        d = (b - a)
        length = d.length
        d = d.normalized()
        up = _axis_vec(axis)
        side = d.cross(up)
        if side.length < 1e-6:
            raise ValueError("seam direction is parallel to its axis")
        side.normalize()
        up = side.cross(d).normalized()
        rot = Matrix((side, d, up)).transposed().to_4x4()
        return self.box((a + b) / 2.0, (width, length, depth), mat, rot=rot)

    def greeble(self, lo, hi, count, seed=0, scale=(0.05, 0.18), mat=0,
                flatten='Z'):
        """Scatter small boxes through a region — machinery clutter.

        Deterministic for a given seed, so rebuilding produces the same model.
        """
        rng = random.Random(seed)
        lo, hi = Vector(lo), Vector(hi)
        faces = []
        for _ in range(count):
            p = Vector((rng.uniform(lo.x, hi.x), rng.uniform(lo.y, hi.y),
                        rng.uniform(lo.z, hi.z)))
            s = Vector((rng.uniform(*scale), rng.uniform(*scale),
                        rng.uniform(*scale)))
            s['XYZ'.index(flatten)] *= 0.4
            faces += self.box(p, s, mat)
        return faces

    def louvres(self, lo, hi, count, axis='Y', mat=0, thickness=0.02):
        """A stack of angled slats filling a rectangular opening."""
        lo, hi = Vector(lo), Vector(hi)
        faces = []
        span = hi.z - lo.z
        rot = Matrix.Rotation(math.radians(35), 4, 'Y')
        for i in range(count):
            z = lo.z + span * (i + 0.5) / count
            faces += self.box(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, z),
                              (hi.x - lo.x, hi.y - lo.y,
                               span / count * 0.9 + thickness),
                              mat, rot=rot)
        return faces

    # -- finishing ---------------------------------------------------------

    def shade(self, faces, smooth=True):
        for f in faces:
            f.smooth = smooth
        return faces

    def bevel(self, faces=None, width=0.012, segments=2, angle=50.0):
        """Bevel edges. Without `faces`, considers the whole part.

        Angle-limited so flat plating keeps crisp panel lines while corners
        soften — a uniform bevel over everything is what makes cheap models
        look like melted soap.

        The 50 degree default is what keeps this affordable. A 12-segment
        cylinder has 30 degrees between neighbouring side faces and a torus at
        minor resolution 8 has 45, so both fall under the threshold and are left
        alone; box corners at 90 degrees are caught. Bevelling curved surfaces
        as well multiplies their triangle count for no visible gain — they are
        already smooth-shaded.
        """
        pool = (self.bm.edges if faces is None
                else {e for f in faces for e in f.edges})
        edges = [e for e in pool
                 if len(e.link_faces) == 2
                 and e.calc_face_angle(0.0) > math.radians(angle)]
        if not edges:
            return
        bmesh.ops.bevel(self.bm, geom=edges, offset=width, segments=segments,
                        profile=0.5, affect='EDGES', clamp_overlap=True)

    def finish(self, name, coll, origin=(0, 0, 0)):
        """Emit the object. `origin` is in the space the geometry was built in
        and becomes the object's pivot."""
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-5)

        mesh = bpy.data.meshes.new(name)
        self.bm.to_mesh(mesh)
        self.bm.free()

        if SCALE != 1.0:
            mesh.transform(Matrix.Diagonal((SCALE, SCALE, SCALE, 1.0)))

        # Origin at the logical pivot: shift the mesh data and place the object
        # back, so the object's location carries the meaning.
        origin = Vector(origin) * SCALE
        if origin.length_squared > 0:
            mesh.transform(Matrix.Translation(-origin))

        for m in self.materials:
            mesh.materials.append(m)

        obj = bpy.data.objects.new(name, mesh)
        obj.location = origin
        coll.objects.link(obj)
        return obj


# --------------------------------------------------------------------------
# SkinPart — a mesh whose vertices carry armature weights
# --------------------------------------------------------------------------

class SkinPart:
    """A mesh built vertex by vertex, each carrying its own bone weights.

    `Part` is the right tool for rigid mechanical pieces: it merges scratch
    bmeshes together and forgets which vertex came from where, which is fine
    when the whole object is parented to one bone. A membrane that folds needs
    the opposite — every vertex's weights have to be known at the moment it is
    created — so this keeps plain vertex and face lists with a weight dict
    alongside each vertex.

    Weights are given as {group_name: value} and are normalised on output, so
    callers can write the falloff they mean without bookkeeping.
    """

    def __init__(self, materials):
        self.materials = list(materials)
        self.co = []
        self.w = []
        self.faces = []
        self.fmat = []
        self.fsmooth = []

    # -- construction ------------------------------------------------------

    def vert(self, co, weights):
        self.co.append(Vector(co))
        self.w.append({k: v for k, v in weights.items() if v > 1e-5})
        return len(self.co) - 1

    def face(self, idx, mat=0, smooth=True):
        self.faces.append(tuple(idx))
        self.fmat.append(mat)
        self.fsmooth.append(smooth)

    def bridge(self, rows, mat=0, smooth=True, closed=False, mat_rows=None):
        """Bridge a sequence of equal-length index rows into a quad grid.

        `mat_rows` optionally overrides the material per row band, which is how
        the hem along a membrane's free edge gets its own material without a
        second mesh.
        """
        for r, (a, b) in enumerate(zip(rows, rows[1:])):
            m = mat if mat_rows is None else mat_rows(r, len(rows) - 1)
            n = len(a)
            for i in range(n if closed else n - 1):
                j = (i + 1) % n
                self.face((a[i], a[j], b[j], b[i]), m, smooth)

    # -- finishing ---------------------------------------------------------

    def finish(self, name, coll, group_order, origin=(0, 0, 0)):
        """Emit the object with its vertex groups populated.

        `group_order` fixes the vertex-group index order. That matters: weights
        are stored per vertex against group *indices*, so a caller that later
        renames the groups (to bind one component to a left and a right
        armature, say) keeps the weights intact as long as the order holds.
        """
        mesh = bpy.data.meshes.new(name)
        verts = [tuple(c * SCALE) for c in self.co]
        mesh.from_pydata(verts, [], [list(f) for f in self.faces])
        mesh.update()

        origin = Vector(origin) * SCALE
        if origin.length_squared > 0:
            mesh.transform(Matrix.Translation(-origin))

        for m in self.materials:
            mesh.materials.append(m)
        for poly, mat, sm in zip(mesh.polygons, self.fmat, self.fsmooth):
            poly.material_index = mat
            poly.use_smooth = sm

        mesh.validate(verbose=False)

        obj = bpy.data.objects.new(name, mesh)
        obj.location = origin
        coll.objects.link(obj)

        groups = {g: obj.vertex_groups.new(name=g) for g in group_order}
        for i, w in enumerate(self.w):
            total = sum(w.values())
            if total <= 0:
                continue
            for g, v in w.items():
                if g not in groups:
                    raise SystemExit(
                        "vertex %d weights unknown group %r (declared: %s)"
                        % (i, g, group_order))
                groups[g].add([i], v / total, 'REPLACE')
        return obj


# --------------------------------------------------------------------------
# Small helpers
# --------------------------------------------------------------------------

def _faces_of(verts):
    return list({f for v in verts for f in v.link_faces})


def _axis_vec(axis):
    return {'X': Vector((1, 0, 0)), 'Y': Vector((0, 1, 0)),
            'Z': Vector((0, 0, 1))}[axis]


def _axis_rot(axis):
    """Rotation taking +Z onto the named axis."""
    return {'Z': Matrix.Identity(4),
            'X': Matrix.Rotation(math.radians(90), 4, 'Y'),
            'Y': Matrix.Rotation(math.radians(-90), 4, 'X')}[axis]


def _smooth_around(faces, axis_dir):
    """Smooth-shade faces that curve around `axis_dir`, leave end caps flat.

    A cap's normal is parallel to the axis; a barrel face's is perpendicular to
    it. Shading the barrel smooth is what lets a 12-sided cylinder read as round
    without paying for 32 sides or a bevel.
    """
    for f in faces:
        f.smooth = abs(f.normal.normalized().dot(axis_dir)) < 0.9


def _plane_point(axis, u, v, w):
    """Map a profile coordinate (u, v) plus an along-axis offset w into 3-D."""
    if axis == 'Y':
        return Vector((u, w, v))
    if axis == 'X':
        return Vector((w, u, v))
    return Vector((u, v, w))


def mirror_y(obj, coll, name):
    """Duplicate an object mirrored across y=0, with normals corrected.

    The ship is symmetric about its centreline; this is how the starboard half
    of anything is made without a hand-placed second copy drifting out of line.
    """
    mesh = obj.data.copy()
    mesh.name = name
    mesh.transform(Matrix.Diagonal((1.0, -1.0, 1.0, 1.0)))
    mesh.flip_normals()
    new = bpy.data.objects.new(name, mesh)
    new.location = (obj.location.x, -obj.location.y, obj.location.z)
    coll.objects.link(new)
    return new


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def report():
    total = 0
    for o in sorted(bpy.data.objects, key=lambda o: o.name):
        if o.type != 'MESH':
            continue
        n = tri_count(o)
        total += n
        print("  %-44s tris=%-6d dims=(%.2f, %.2f, %.2f)"
              % (o.name, n, *o.dimensions))
    print("  TOTAL TRIS: %d" % total)
    return total
