"""Surface treatment that turns a lofted tube into an animal.

A loft of elliptical cross-sections is a good way to get a *silhouette* and a
terrible way to get a body. It has no anatomy: no shoulder mass, no ribcage, no
crease where the leg leaves the flank, and a perfectly even surface that reads
as extruded plastic no matter how carefully the outline is drawn. Sticking
separate armour lumps on top of that does not fix it -- it reads as a smooth
tube with potatoes glued to it, which is exactly what the first attempt was.

This module is the fix. It runs four passes over the base mesh, in order:

1. **`muscle`** -- large anatomical masses, added as smooth ellipsoidal bumps
   displaced along the surface normal. Scapula, triceps, pectoral, gluteal,
   quadriceps, the ribcage, the throat. These are what give the body form under
   the skin, and they are the single biggest difference between this and the
   first attempt.
2. **`fold`** -- creases, as *negative* displacement in a narrow band. Skin
   gathers where limbs meet the body and where a neck bends, and the eye reads
   those gathers as flesh. Both blob and ring shapes, the ring being what a neck
   wrinkle actually is.
3. **`skin_noise`** -- low-amplitude multi-octave turbulence. Nothing organic is
   ever perfectly smooth, and this is cheap.
4. **`plate_mosaic`** -- the armour. Not scattered objects: a Voronoi
   tessellation *of the body surface itself*, each cell lifted into a plate with
   a wall dropping back to the hide. The gaps between cells show skin, which is
   the pale crackle running between the dark scutes in the reference. Armour
   built this way cannot float, cannot intersect its neighbours, and follows
   every curve of the body for free, because it *is* the body surface.

All four preserve vertex weights: passes 1-3 only move existing vertices, and
`plate_mosaic` copies the deform layer of whatever vertex it duplicates. So the
whole treatment is invisible to the rig.
"""

import math
import random

import bmesh
from mathutils import Vector, noise


# --------------------------------------------------------------------------
# Fields
# --------------------------------------------------------------------------

def _bump(t):
    """Smooth 1-at-centre, 0-at-edge falloff with zero derivative at both ends.

    `(1 - t^2)^2` rather than a cosine or a Gaussian because it reaches exactly
    zero at t = 1, so a muscle has a real boundary and does not quietly inflate
    the whole animal by half a percent.
    """
    if t >= 1.0:
        return 0.0
    u = 1.0 - t * t
    return u * u


class Blob:
    """An ellipsoidal mass. Positive swells the surface, negative creases it."""

    def __init__(self, centre, radii, strength, mirror=True, one_sided=True):
        self.c = Vector(centre)
        self.r = Vector(radii)
        self.s = strength
        self.mirror = mirror
        self.one_sided = one_sided

    def at(self, p, n):
        d = p - self.c
        # A muscle must not push out the far side of the body it sits inside.
        if self.one_sided and d.dot(n) <= 0.0:
            return 0.0
        t = math.sqrt((d.x / self.r.x) ** 2 + (d.y / self.r.y) ** 2
                      + (d.z / self.r.z) ** 2)
        return self.s * _bump(t)


class Ring:
    """A crease running round an axis -- what a neck or joint wrinkle is.

    Distance is measured to the *circle*, not to its centre, so the falloff is a
    torus. A blob cannot express this: it would dimple the middle of the neck
    rather than gathering skin around it.
    """

    def __init__(self, centre, axis, radius, tube, strength):
        self.c = Vector(centre)
        self.a = Vector(axis).normalized()
        self.R = radius
        self.tube = tube
        self.s = strength
        self.mirror = False
        self.one_sided = False

    def at(self, p, n):
        d = p - self.c
        along = d.dot(self.a)
        radial = (d - self.a * along).length
        t = math.sqrt((radial - self.R) ** 2 + along * along) / self.tube
        return self.s * _bump(t)


def _mirrored(fields):
    """Expand every field marked `mirror` into a port and starboard copy."""
    out = []
    for f in fields:
        out.append(f)
        if getattr(f, "mirror", False) and abs(f.c.y) > 1e-6:
            g = Blob((f.c.x, -f.c.y, f.c.z), f.r, f.s, mirror=False,
                     one_sided=f.one_sided)
            out.append(g)
    return out


# --------------------------------------------------------------------------
# Passes
# --------------------------------------------------------------------------

def displace(bm, fields, limit=None):
    """Apply a list of Blob/Ring fields along vertex normals.

    Summed before displacing, not applied one at a time: overlapping muscles
    should blend into one mass, and displacing sequentially would move the
    surface out from under the next field's falloff and produce lumps at the
    seams.
    """
    fields = _mirrored(fields)
    bm.normal_update()
    moves = []
    for v in bm.verts:
        if limit is not None and not limit(v):
            moves.append(0.0)
            continue
        moves.append(sum(f.at(v.co, v.normal) for f in fields))
    for v, d in zip(bm.verts, moves):
        if d:
            v.co = v.co + v.normal * d


def skin_noise(bm, scale, amount, octaves=3, limit=None):
    """Multi-octave turbulence along the normal. Organic irregularity, cheaply."""
    bm.normal_update()
    for v in bm.verts:
        if limit is not None and not limit(v):
            continue
        n = noise.turbulence(v.co * scale, octaves, False)
        v.co = v.co + v.normal * (n * amount)


# --------------------------------------------------------------------------
# Armour
# --------------------------------------------------------------------------

def _seed_faces(faces, count, rng):
    """Farthest-point sampling over face centroids.

    Uniform random picks clump -- Poisson-ish spacing is what makes a scale
    field read as a tessellation rather than as spilled gravel -- and farthest
    point is the cheapest way there without a spatial index.
    """
    if not faces:
        return []
    cents = [f.calc_center_median() for f in faces]
    first = rng.randrange(len(faces))
    chosen = [first]
    best = [(c - cents[first]).length_squared for c in cents]
    while len(chosen) < min(count, len(faces)):
        i = max(range(len(cents)), key=lambda k: best[k])
        if best[i] <= 0.0:
            break
        chosen.append(i)
        ci = cents[i]
        for k, c in enumerate(cents):
            d = (c - ci).length_squared
            if d < best[k]:
                best[k] = d
    return chosen


def plate_mosaic(bm, faces, count, gap, height, mat, rng,
                 min_faces=3, height_jitter=0.35):
    """Lift a Voronoi tessellation of `faces` into armour plates.

    Each cell is duplicated, pulled in from its neighbours by `gap`, raised by
    `height`, and skirted with a wall back down to the hide. The original faces
    are left in place, so what shows through the gaps is skin.

    `gap` is a world distance rather than a fraction, which is what keeps the
    crack an even width across cells of very different sizes -- a proportional
    inset gives fat cracks around big plates and hairlines around small ones,
    and the field stops reading as one material.

    Returns the number of plates built.
    """
    deform = bm.verts.layers.deform.active
    seeds = _seed_faces(faces, count, rng)
    if not seeds:
        return 0
    cents = [f.calc_center_median() for f in faces]
    seed_pts = [cents[i] for i in seeds]

    cells = {}
    for idx, f in enumerate(faces):
        c = cents[idx]
        best, bd = 0, (c - seed_pts[0]).length_squared
        for k in range(1, len(seed_pts)):
            d = (c - seed_pts[k]).length_squared
            if d < bd:
                best, bd = k, d
        cells.setdefault(best, []).append(f)

    bm.normal_update()
    built = 0
    for _key, cell in cells.items():
        if len(cell) < min_faces:
            continue
        cell_set = set(cell)
        verts = {v for f in cell for v in f.verts}
        centre = Vector((0, 0, 0))
        for v in verts:
            centre += v.co
        centre /= len(verts)

        h = height * (1.0 + rng.uniform(-height_jitter, height_jitter))

        copy = {}
        for v in verts:
            away = v.co - centre
            L = away.length
            # Clamp the inset so a small cell cannot collapse through itself.
            pull = min(gap, L * 0.42)
            p = v.co + (away.normalized() * -pull if L > 1e-6 else Vector())
            nv = bm.verts.new(p + v.normal * h)
            if deform is not None:
                # BMDeformVert is not assignable as a whole -- it has to be
                # filled group by group, or every plate arrives unweighted and
                # the armour stays behind when the animal moves.
                src, dstv = v[deform], nv[deform]
                for gi in src.keys():
                    dstv[gi] = src[gi]
            copy[v] = nv

        for f in cell:
            try:
                nf = bm.faces.new([copy[v] for v in f.verts])
                nf.material_index = mat
                nf.smooth = True
            except ValueError:
                pass

        # Wall: every edge of the cell that no *other* cell face shares.
        for f in cell:
            for e in f.edges:
                if sum(1 for lf in e.link_faces if lf in cell_set) != 1:
                    continue
                a, b = e.verts
                try:
                    wf = bm.faces.new((a, b, copy[b], copy[a]))
                    wf.material_index = mat
                    wf.smooth = False
                except ValueError:
                    pass
        built += 1

    bm.normal_update()
    return built
