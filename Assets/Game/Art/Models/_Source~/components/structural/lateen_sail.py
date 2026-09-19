"""components/structural/lateen_sail - big triangular ship sails on their spars.

`components/structural/sail_rig` is the camp's tensile *shade* hardware - timber
poles and ground anchors for a sail pitched over sand. This is the other thing
entirely: a driving sail, cut as a triangle, bent onto a spar and a boom, big
enough to move a vessel. Nothing in the library did that.

The one decision that matters here is **the sail is not flat**. A triangle of
zero-thickness cloth reads as a signboard from every angle, and no amount of
texture fixes it. So the sail is lofted as a solid: each section across the
chord is a closed profile with camber, so the cloth bellies away to leeward and
the silhouette changes as you walk around it. The belly is deepest at a third of
the chord, which is where a real sail's draft sits, and it slackens toward the
head where the cloth runs out of area to bag.

Cut and conventions:

- **Origin at the tack** - the forward corner of the foot, where the sail is
  made fast to the ship. An assembly places a sail by putting its origin on the
  fitting that holds it, then rakes it about that point. Anchoring on the
  bounding-box centre instead makes a raked sail swing off its own fixing.
- **Luff up +Z, foot aft along +Y**, so the sail stands on the library's
  fore-aft axis and rakes with a single rotation about X.
- **The leech is convex.** Chord falls off as (1-t)^0.85 rather than linearly,
  which puts roach in the trailing edge. A straight-line taper reads as a set
  square; the curve is most of what makes it read as cloth under load.
- **Camber is handed.** The belly bulges toward -X. Port and starboard sails are
  therefore mirror images, not copies - use `mirror_y` in the assembly.

Sizes are ship-scale: `Main` has a 26 m luff on a 15 m foot.

    blender --background --python lateen_sail.py -- --out lateen_sail.blend

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
    "Mat_Fabric_Sail_White",     # 0 the working sail
    "Mat_Fabric_Sail_Red",       # 1 the second suit of cloth
    "Mat_Fabric_Rope_Hemp",      # 2 bolt ropes, reef points, lashings
    "Mat_Wood_Timber_Silvered",  # 3 spar and boom
    "Mat_Metal_Steel_Dark",      # 4 bands, cringles, gooseneck
    "Mat_Fabric_Canvas_Faded",   # 5 corner reinforcement and reef bands
    "Mat_Metal_Rust_Pale",       # 6 weathered ironwork
]
WHITE, RED, ROPE, TIMBER, STEEL, CANVAS, RUST = range(7)

CHORDWISE = 10        # samples across the chord - sets how round the belly reads
SPANWISE = 12         # sections up the luff
ROACH = 0.85          # chord falloff exponent; 1.0 would be a dead-straight leech


# ---------------------------------------------------------------------------
# The sail surface
# ---------------------------------------------------------------------------

def chord_at(foot, t):
    """Width of the sail at height fraction `t`, with roach in the leech."""
    return foot * max(0.0, 1.0 - t) ** ROACH


def section(chord, t, belly, thick=0.05):
    """One closed chordwise profile: out along the lee face, back along the
    weather face. Equal point count at every height, which is what `loft`
    needs, so the head collapses to a sliver rather than changing topology."""
    draft = belly * (1.0 - 0.55 * t)
    lee, weather = [], []
    for i in range(CHORDWISE + 1):
        s = i / CHORDWISE
        y = chord * s
        # Draft peaks at 35% chord - where a working sail's does.
        camber = draft * math.sin(math.pi * min(1.0, s ** 0.78))
        half = thick * math.sin(math.pi * max(0.02, min(0.98, s))) + 0.006
        lee.append((-camber - half, y))
        weather.append((-camber + half, y))
    return lee + list(reversed(weather))


def cloth(p, luff, foot, belly, mat, base=0.0):
    """The sail itself, lofted up the luff.

    `base` is the height the cloth starts at. A reefed sail is genuinely a
    trapezoid - the foot is rolled away and the remaining cloth is the top of
    the same triangle - so the taper is sampled against the FULL triangle and
    only the lower sections are dropped. Building a smaller triangle instead
    would give the reefed sail a different leech curve from the set one, and the
    two would stop reading as the same suit of cloth.
    """
    secs = []
    for j in range(SPANWISE + 1):
        z = base + (luff - base) * j / SPANWISE
        t = z / luff
        secs.append((z, section(chord_at(foot, t), t, belly)))
    return p.loft(secs, axis='Z', mat=mat)


def edge_rope(p, a, b, radius=0.075, mat=ROPE):
    """A bolt rope run along one edge of the sail."""
    a, b = Vector(a), Vector(b)
    d = b - a
    rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    return p.cyl((a + b) / 2, radius, d.length, 'Z', seg=6, mat=mat, rot=rot)


def leech_rope(p, luff, foot, radius=0.07, base=0.0):
    """The trailing edge follows a curve, so its rope is walked, not spanned.

    The leech carries no camber: `section` puts the draft at sin(pi*s^0.78),
    which is zero at s = 1, so the trailing edge lies flat on x = 0.
    """
    pts = []
    for j in range(SPANWISE + 1):
        z = base + (luff - base) * j / SPANWISE
        pts.append(Vector((0.0, chord_at(foot, z / luff), z)))
    faces = []
    for a, b in zip(pts, pts[1:]):
        if (b - a).length > 1e-4:
            faces += edge_rope(p, a, b, radius)
    return faces


def corner(p, at, size, mat=CANVAS, ring=True):
    """A reinforcement patch with a cringle - the load path out of the cloth."""
    at = Vector(at)
    p.box(at, (size * 0.9, size, 0.09), mat)
    if ring:
        p.torus(at, size * 0.30, 0.055, axis='X', maj_seg=10, min_seg=6,
                mat=RUST)


def reef_band(p, luff, foot, belly, t, mat=CANVAS, steps=9):
    """A horizontal band of reef points - the sail can be shortened to it.

    Walked across the chord in short segments that each sit on the cambered
    surface. A single straight box spanning the chord stands off the belly by
    the full draft at mid-chord and reads as a shelf bolted across the sail.
    """
    c = chord_at(foot, t)
    z = t * luff
    draft = belly * (1.0 - 0.55 * t)

    def camber(s):
        return draft * math.sin(math.pi * min(1.0, s ** 0.78))

    for i in range(steps):
        s0, s1 = i / steps, (i + 1) / steps
        x = (camber(s0) + camber(s1)) / 2
        p.box((-x, c * (s0 + s1) / 2, z),
              (0.16, c / steps * 1.02, 0.34), mat)
    for i in range(7):
        s = (i + 0.5) / 7
        p.cyl((-camber(s) - 0.13, c * s, z - 0.62), 0.04, 1.2, 'Z', seg=5,
              mat=ROPE)


def spar(p, length, taper=0.55, radius=0.30, mat=TIMBER, along='Z',
         origin=(0, 0, 0)):
    """The pole the luff is bent onto. Eight-sided, like every other spar in
    this library - a 16-segment barrel reads as a machined tube."""
    o = Vector(origin)
    if along == 'Z':
        c = o + Vector((0, 0, length / 2))
    else:
        c = o + Vector((0, length / 2, 0))
    return p.cyl(c, radius, length, along, seg=8, mat=mat,
                 radius_top=radius * taper)


def bands(p, length, count, radius, along='Z', origin=(0, 0, 0), mat=STEEL):
    """Iron bands down a spar, where the rigging is made fast."""
    o = Vector(origin)
    for i in range(count):
        t = (i + 0.5) / count
        c = o + (Vector((0, 0, length * t)) if along == 'Z'
                 else Vector((0, length * t, 0)))
        p.torus(c, radius * (1.0 - 0.45 * t) + 0.05, 0.06, axis=along,
                maj_seg=8, min_seg=5, mat=mat)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def rigged(coll, mats, name, luff, foot, belly, mat, reefs=(), base=0.0,
           torn=False):
    """A complete sail on its spar and boom.

    `base` above zero reefs the sail: the cloth starts there, and the rolled-up
    remainder is lashed along the band it was reefed to.
    """
    p = Part(mats)
    hem = chord_at(foot, base / luff)          # chord where the cloth now starts
    cloth(p, luff, foot, belly, mat, base=base)
    leech_rope(p, luff, foot, base=base)
    edge_rope(p, (0, 0, base), (0, 0, luff))         # luff
    edge_rope(p, (0, 0, base), (0, hem, base))       # foot, at the cloth's hem
    corner(p, (0, 0.55, base + 0.55), 1.5)           # tack
    corner(p, (0, hem - 0.9, base + 0.7), 1.6)       # clew
    corner(p, (0, 0.5, luff - 1.1), 1.2)             # head
    for t in reefs:
        reef_band(p, luff, foot, belly, t)
    spar(p, luff + 1.4, radius=0.32)
    bands(p, luff + 1.4, 5, 0.32)
    spar(p, foot + 1.0, radius=0.26, along='Y')
    bands(p, foot + 1.0, 4, 0.26, along='Y')
    p.cyl((0, -0.25, 0.30), 0.34, 0.9, 'Y', seg=8, mat=RUST)   # gooseneck
    if base > 0.0:
        # The rolled cloth, sagging between the ties that hold it to the band.
        for i in range(6):
            s = (i + 0.5) / 6
            y = hem * s
            r = 0.54 + 0.18 * math.sin(math.pi * s)
            p.cyl((-0.12, y, base - 0.42 - 0.12 * math.sin(math.pi * s)), r,
                  hem / 6 * 0.94, 'Y', seg=7, mat=mat)
            p.torus((0, y, base - 0.42), r + 0.04, 0.05, axis='Y', maj_seg=7,
                    min_seg=5, mat=ROPE)
        # Lashings from the bundle down to the boom it is carried above.
        for i in range(4):
            y = hem * (i + 0.5) / 4
            p.cyl((0, y, base / 2 - 0.2), 0.045, base - 0.4, 'Z', seg=5,
                  mat=ROPE)
    if torn:
        # A split in the leech, held by three lashings across the tear.
        for i in range(3):
            z = luff * (0.34 + 0.07 * i)
            c = chord_at(foot, z / luff)
            p.cyl((-0.22, c - 1.5, z), 0.05, 2.6, 'Y', seg=5, mat=ROPE)
    p.bevel(width=0.03, segments=1)
    return p.finish(name, coll)


def main(coll, mats):
    """The working sail: full cut, white, one reef band."""
    return rigged(coll, mats, "Mesh_LateenSail_Main",
                  luff=26.0, foot=15.0, belly=1.30, mat=WHITE, reefs=(0.30,))


def patched(coll, mats):
    """The second suit - red cloth, torn leech, two reef bands.

    The tribe's spare, and the reason the two sides of a ship never match.
    """
    return rigged(coll, mats, "Mesh_LateenSail_Patched",
                  luff=24.0, foot=14.0, belly=1.55, mat=RED,
                  reefs=(0.26, 0.48), torn=True)


def reefed(coll, mats):
    """Shortened down - the head still set, the foot rolled on the boom.

    Not a scaled copy of `Main`: the cloth is cut to the reef and the rest is
    present as a bundle, so a ship can carry one sail set and one reefed and
    still read as one rig.
    """
    return rigged(coll, mats, "Mesh_LateenSail_Reefed",
                  luff=26.0, foot=15.0, belly=0.80, mat=WHITE, base=3.1)


def build():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Main", main), ("Patched", patched),
                     ("Reefed", reefed)):
        fn(collection("Coll_LateenSail_%s" % name), mats)
    report()
    save(out)


build()
