"""components/structural/gas_envelope - lifting-gas bladders for airships.

The library had no envelope of any kind: `fuselage_pod` is a rigid aircraft
body, `hab_capsule` a pressure vessel people live inside, and neither reads as
cloth holding gas. A blimp needs a bag that is obviously soft, obviously
strapped down, and obviously straining upward against the frame that holds it.

Three things make it read as a bladder rather than a balloon:

- **Sixteen facets and flat shading.** A smooth-shaded ovoid reads as a CG
  primitive at any poly count. A hand-sewn gasbag is panels of cloth, and the
  creases between them are the whole silhouette - so the skin is deliberately
  faceted and the shading is left flat. Do not "improve" this by smoothing it.
- **The straps are structural, not decoration.** Longitudinal straps sit ON the
  creases, which is where a sewn seam goes and where a real net would bear;
  circumferential ribs stand 0.14 m proud between them. The bag hangs in the
  net, the net hangs in the ship.
- **Blunt ends, not points.** The profile flattens to a stub at each end and
  takes a steel collar - the gas fitting. A tapered point reads as an airship
  hull; a stub with a fitting reads as a bladder somebody fills.

Everything is modelled **along Y**, the library's fore-aft axis, origin on the
envelope's own axis at mid-length. An assembly places one by putting its origin
at the centre of the cradle that carries it, with no rotation.

Geometry note: the skin's ring is rolled by half a facet, so a flat facet sits
at top and bottom - a cradle bears on a flat, not on a crease. Every rib and
patch is built from the same ring at the same roll, so they follow the creases
instead of cutting through them. A `torus` will NOT do for a rib: its facets sit
half a step out of phase with the skin's and it sinks 0.075 m inside the bag at
every crease.

    blender --background --python gas_envelope.py -- --out gas_envelope.blend

Generation script - historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

MATS = [
    "Mat_Fabric_Sail_Orange",    # 0 the bag itself
    "Mat_Fabric_Canvas_Faded",   # 1 strap webbing and rib bands
    "Mat_Metal_Steel_Dark",      # 2 end collars, buckles, gas fittings
    "Mat_Metal_Rust_Pale",       # 3 weathered fittings and clamp rings
    "Mat_Fabric_Wing_Ochre",     # 4 repair patches - a mismatched orange
    "Mat_Fabric_Rope_Hemp",      # 5 lashings and stitching
]
SKIN, WEB, STEEL, RUST, PATCH, ROPE = range(6)

SEG = 16                     # facets around. Deliberately low - see the docstring
ROLL = math.pi / SEG         # half a facet, so a flat sits top and bottom
TIP = 0.17                   # end stub radius, as a fraction of the max radius


# ---------------------------------------------------------------------------
# The shared bladder language
# ---------------------------------------------------------------------------

def ell(r, squash, theta, off=0.0):
    """A point on the section, pushed `off` metres along its outward normal.

    The section is an ellipse whenever `squash` is not 1, and a band laid on it
    has to follow the **ellipse normal**, not the radius. Offsetting radially
    instead lifts every band off the top of a squashed bag - on `Patched`, by
    0.85 m, with the band hanging in mid-air over a bag it is supposed to be
    clamping. The normal of x^2/a^2 + z^2/b^2 = 1 runs along (b cos, a sin).
    """
    a, b = r, r * squash
    n = Vector((b * math.cos(theta), a * math.sin(theta)))
    if n.length > 1e-9:
        n.normalize()
    return Vector((a * math.cos(theta), b * math.sin(theta))) + n * off


def ring(r, squash=1.0, off=0.0):
    """One profile ring in the XZ plane, at the shared roll."""
    return [tuple(ell(r, squash, ROLL + 2 * math.pi * i / SEG, off))
            for i in range(SEG)]


def stations(length, radius, count=15, power=0.40, sag=0.0):
    """[(y, radius, drop)] down the envelope, clustered toward the blunt ends.

    Cosine spacing puts stations where the silhouette actually turns. `sag`
    droops the axis toward the middle, for a bag that is not fully inflated.
    """
    out = []
    for i in range(count):
        t = (1.0 - math.cos(math.pi * i / (count - 1))) / 2.0
        s = math.sin(math.pi * t) ** power
        out.append(((t - 0.5) * length,
                    radius * max(TIP, s),
                    -sag * math.sin(math.pi * t)))
    return out


def radius_at(sts, y):
    """Interpolated radius and droop, so ribs sit on the skin they decorate."""
    for (y0, r0, d0), (y1, r1, d1) in zip(sts, sts[1:]):
        if y0 <= y <= y1:
            k = (y - y0) / max(1e-6, y1 - y0)
            return r0 + (r1 - r0) * k, d0 + (d1 - d0) * k
    return (sts[0][1], sts[0][2]) if y < sts[0][0] else (sts[-1][1], sts[-1][2])


def skin(p, sts, squash=1.0):
    """The bag. Flat-shaded on purpose - the creases are the silhouette."""
    secs = [(y, [(u, v + d) for u, v in ring(r, squash)]) for y, r, d in sts]
    return p.shade(p.loft(secs, axis='Y', mat=SKIN), smooth=False)


def strap(p, sts, theta, squash=1.0, width=0.30, thick=0.07, off=0.02,
          mat=WEB, trim=1):
    """One longitudinal strap, laid over the skin along the ray `theta`.

    Runs the length of the bag between the end collars. Built as a loft whose
    profile follows the skin's own section at every station, so it stays in
    contact with a surface that is changing diameter under it.
    """
    secs = []
    for y, r, d in sts[trim:len(sts) - trim]:
        c = ell(r, squash, theta, off)
        n = (ell(r, squash, theta, 1.0) - c).normalized()
        t = Vector((-n.y, n.x))
        prof = [tuple(c - t * (width / 2) - n * (thick / 2)),
                tuple(c + t * (width / 2) - n * (thick / 2)),
                tuple(c + t * (width / 2) + n * (thick / 2)),
                tuple(c - t * (width / 2) + n * (thick / 2))]
        secs.append((y, [(u, v + d) for u, v in prof]))
    return p.shade(p.loft(secs, axis='Y', mat=mat), smooth=False)


def rib(p, sts, y, squash=1.0, stand=0.14, half=0.17, off=0.01, mat=WEB):
    """A circumferential band standing proud of the skin.

    Four stations give it a trapezoidal section, so it reads as a band clamped
    over the bag rather than a disc pushed through it.
    """
    r, d = radius_at(sts, y)
    secs = []
    for dy, dr in ((-half, off), (-half * 0.6, off + stand),
                   (half * 0.6, off + stand), (half, off)):
        secs.append((y + dy, [(u, v + d) for u, v in ring(r, squash, dr)]))
    return p.shade(p.loft(secs, axis='Y', mat=mat, cap=False), smooth=False)


def tangent_rot(sts, theta, y, squash=1.0):
    """Rotation putting local +Z on the surface normal and +Y along the bag."""
    r, _ = radius_at(sts, y)
    d2 = ell(r, squash, theta, 1.0) - ell(r, squash, theta, 0.0)
    n = Vector((d2.x, 0.0, d2.y)).normalized()
    t = Vector((-n.z, 0.0, n.x))
    return Matrix((t, Vector((0, 1, 0)), n)).transposed().to_4x4()


def on_skin(sts, theta, y, squash=1.0, lift=0.0):
    """World point on the bag's surface, for anything bolted to it."""
    r, d = radius_at(sts, y)
    c = ell(r, squash, theta, lift)
    return Vector((c.x, y, c.y + d))


def patch(p, sts, theta, y, squash=1.0, size=(1.9, 2.6), mat=PATCH):
    """A repair panel laid flat on the skin, with stitches round its edge."""
    c = on_skin(sts, theta, y, squash, lift=0.03)
    rot = tangent_rot(sts, theta, y, squash)
    f = p.box(c, (size[0], size[1], 0.06), mat, rot=rot)
    for sx, sy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
        a = c + rot @ Vector((sx * size[0] / 2 * 0.94,
                              sy * size[1] / 2 * 0.94, 0.02))
        b = c + rot @ Vector((sx * size[0] / 2 * 0.80,
                              sy * size[1] / 2 * 0.80, 0.02))
        p.rivets(a, b, 3, radius=0.05, height=0.04, axis='Z', mat=ROPE)
    return f


def collar(p, sts, end, mat=STEEL):
    """The gas fitting on a blunt end: clamp ring, drum, and a short spigot."""
    y, r = sts[0 if end < 0 else -1][0], sts[0 if end < 0 else -1][1]
    p.tube((0, y + end * 0.10, 0), r * 1.08, 0.18, 0.36, axis='Y', seg=SEG,
           mat=mat)
    p.cyl((0, y + end * 0.34, 0), r * 0.82, 0.42, axis='Y', seg=SEG, mat=RUST,
          radius_top=r * 0.62)
    p.cyl((0, y + end * 0.70, 0), r * 0.26, 0.46, axis='Y', seg=10, mat=mat)
    p.torus((0, y + end * 0.90, 0), r * 0.26, 0.07, axis='Y', maj_seg=10,
            min_seg=6, mat=RUST)


def bladder(coll, mats, name, length, radius, ribs, straps=8, squash=1.0,
            sag=0.0, patches=(), power=0.40):
    """One complete envelope, as a single object."""
    p = Part(mats)
    sts = stations(length, radius, power=power, sag=sag)
    skin(p, sts, squash=squash)
    step = SEG // straps
    for i in range(straps):
        strap(p, sts, ROLL + 2 * math.pi * (i * step) / SEG, squash)
    for y in ribs:
        rib(p, sts, y, squash)
    collar(p, sts, -1)
    collar(p, sts, 1)
    for theta, y, size in patches:
        patch(p, sts, theta, y, squash, size)
    return p.finish(name, coll)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def plain(coll, mats):
    """The workhorse. Four ribs, eight straps, fully inflated."""
    return bladder(coll, mats, "Mesh_GasEnvelope_Plain",
                   length=24.0, radius=7.2, straps=8,
                   ribs=(-8.4, -2.8, 2.8, 8.4))


def banded(coll, mats):
    """Heavily netted - sixteen straps and seven ribs.

    Reads as the bag under most load: the one directly under the city's weight.
    """
    return bladder(coll, mats, "Mesh_GasEnvelope_Banded",
                   length=26.0, radius=7.6, straps=16,
                   ribs=(-10.0, -6.6, -3.2, 0.0, 3.2, 6.6, 10.0))


def patched(coll, mats):
    """Half-slack and mended. The one the tribe worries about.

    Squashed to 0.88 in Z and sagging 0.45 m at mid-length, which is what makes
    it read as under-inflated rather than merely smaller.
    """
    return bladder(coll, mats, "Mesh_GasEnvelope_Patched",
                   length=23.0, radius=7.0, straps=8, squash=0.88, sag=0.45,
                   ribs=(-7.8, -1.2, 5.6),
                   patches=((ROLL + 2 * math.pi * 3 / SEG, -4.2, (2.4, 3.2)),
                            (ROLL + 2 * math.pi * 11 / SEG, 3.0, (1.8, 2.4)),
                            (ROLL + 2 * math.pi * 6 / SEG, 7.9, (1.5, 1.9))))


def small(coll, mats):
    """A spare bladder - stubbier in proportion, not merely scaled down.

    A uniform shrink of `Plain` would read as the same bag seen from further
    away. Raising `power` to 0.56 fattens the mid-body against a shorter hull,
    so the proportion itself differs.
    """
    return bladder(coll, mats, "Mesh_GasEnvelope_Small",
                   length=13.5, radius=4.6, straps=8, power=0.56,
                   ribs=(-4.2, 0.0, 4.2))


def build():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Plain", plain), ("Banded", banded),
                     ("Patched", patched), ("Small", small)):
        fn(collection("Coll_GasEnvelope_%s" % name), mats)
    report()
    save(out)


build()
