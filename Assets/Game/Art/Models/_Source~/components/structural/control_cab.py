"""components/structural/control_cab — an armoured observation head for a mast.

Nothing in this library had this and no amount of stacking `tower_bay` gets it.
Every other habitable box here — `cabin_module`, `hab_capsule`, `tower_bay`,
`slab_block`, `outpost_block` — is a hull you look *out of* through as few holes
as possible. This is the one place on an outpost that exists to see, so it is
the one place that spends its armour budget on a continuous band of glass.

The band is **canted outward** as it rises, so somebody at it looks straight
down the mast without the lit room behind them reflecting back off it. That is
why control towers, ship bridges and crane cabs all look like this, and it is
the feature that makes the silhouette read as *something is watching from up
there* rather than as another storey.

## Not an airport control tower

The first version of this file was exactly that, and it was the most terrestrial
object in the whole model: a clean box, a tidy parapet, a flush glazed band, and
a service storey underneath punched with a regular grid of little square
windows. All of that has gone.

What makes it read as off-world hardware instead:

- **A brow.** The glazing sits under a heavy visor that projects 0.9 m past it
  on angled stays. An unshaded band of glass is a shopfront; the same band under
  a visor is an instrument.
- **A chamfered plan.** The corners are cut, so the band wraps eight facets and
  there is no 90 degree arris anywhere on the head.
- **A flared bearing.** The cab visibly widens onto its support instead of its
  floor simply stopping in mid-air.
- **Sensor pods** clamped on the corners, and a dark rusted skirt below the
  glass rather than a painted spandrel.
- **`Annex` is now blind.** The window grid is replaced by armoured ribs, a
  louvre bank, a conduit spine and one small port.

Origin at the base centre of the floor pan, on the plane it lands on, so placing
a cab is `z = deck_top` and nothing else.

    blender --background --python control_cab.py -- --out control_cab.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

MATS = [
    "Mat_Paint_Coral_Faded",     # 0 CORAL  the visor and roof band — the mass
    "Mat_Metal_HullRust_Orange", # 1 HULL   oxidised plate over most of the body
    "Mat_Metal_Rust_Heavy",      # 2 RUST   corrosion, weld-on repair, streaks
    "Mat_Neutral_Slate_Dark",    # 3 SLATE  armour, mullions, soffits, skirt
    "Mat_Glass_Canopy_Tinted",   # 4 GLASS  the panes that are not lit
    "Mat_Neutral_Black_Matte",   # 5 BLACK  the reveal behind everything
    "Mat_Metal_Steel_Worn",      # 6 STEEL  stays, buttresses, rails, ladders
    "Mat_Metal_Steel_Dark",      # 7 DARK   fittings, sensor pods, cases
    "Mat_Paint_Hull_Bleached",   # 8 BLEACH the sun-killed skin of the derelict
    "Mat_Emissive_Amber",        # 9 AMBER  the lit panes, used sparingly
    "Mat_Metal_Copper_Oxide",    # 10 COPPER verdigris conduit — the odd note
    "Mat_Paint_Warn_Red",        # 11 RED   hazard marking
]
(CORAL, HULL, RUST, SLATE, GLASS, BLACK, STEEL, DARK, BLEACH, AMBER, COPPER,
 RED) = range(12)


def chamfer_plan(w, d, cut):
    """A rectangle with its corners cut off — the plan of every cab here."""
    hw, hd = w / 2.0, d / 2.0
    return [(-hw + cut, -hd), (hw - cut, -hd), (hw, -hd + cut), (hw, hd - cut),
            (hw - cut, hd), (-hw + cut, hd), (-hw, hd - cut), (-hw, -hd + cut)]


def mass(p, z0, z1, w0, d0, w1, d1, cut0, cut1, mat=HULL):
    """A chamfered, flat-shaded body section."""
    f = p.loft([(z0, chamfer_plan(w0, d0, cut0)),
                (z1, chamfer_plan(w1, d1, cut1))], axis='Z', mat=mat)
    return p.shade(f, False)


def basis(xdir, zdir):
    """A rotation with local +X along `xdir` and local +Z along `zdir`.

    `Vector.rotation_difference` is the obvious way to aim a box and it is wrong
    for a panel. It returns the *minimal* rotation onto the target, whose axis
    is `+Z x zdir` — for a leaning facet that axis lies along the facet's chord,
    so local X survives only on the facets whose chord happens to run along X.
    On the facets whose chord runs along Y, local X tilts into the XZ plane and
    the panel's full chord length gets applied sideways: a 5.3 m facet ends up
    projecting 2.6 m out past the hull it is supposed to be skinning.

    Naming both axes removes the ambiguity. This is the same construction
    `_buildlib.seam` uses for the same reason.
    """
    z = Vector(zdir).normalized()
    x = Vector(xdir)
    x = (x - z * x.dot(z)).normalized()
    return Matrix((x, z.cross(x), z)).transposed().to_4x4()


def facet_band(p, z1, z2, w1, d1, w2, d2, cut1, cut2, thick, mat_of,
               mullion=STEEL, bar=0.15):
    """The canted glazed band, as one leaning panel per facet of the plan.

    Built as separate rotated boxes rather than one lofted shell. A loft would
    be fewer triangles, but an open lofted band has no inside and its normals
    come out whichever way the recalc picks; a box is closed and manifold, so
    the glazing can never end up facing into the room.

    `mat_of(i)` decides each facet's material, which is how the band comes out
    mostly dark with two or three panes lit rather than as a uniform glowing
    ring.
    """
    lo = chamfer_plan(w1, d1, cut1)
    hi = chamfer_plan(w2, d2, cut2)
    n = len(lo)
    for i in range(n):
        a, b = Vector(lo[i] + (z1,)), Vector(lo[(i + 1) % n] + (z1,))
        c, e = Vector(hi[i] + (z2,)), Vector(hi[(i + 1) % n] + (z2,))
        mid_lo, mid_hi = (a + b) / 2.0, (c + e) / 2.0
        dv = mid_hi - mid_lo
        if dv.length < 1e-6:
            continue
        chord = b - a
        rot = basis(chord, dv)
        p.box((mid_lo + mid_hi) / 2.0, (chord.length, thick, dv.length),
              mat_of(i), rot=rot)
        # the mullion on this facet's leading edge, leaning with the glass
        d2v = c - a
        p.box((a + c) / 2.0, (bar, thick * 1.9, d2v.length), mullion,
              rot=basis(chord, d2v))


def visor(p, z, w, d, cut, drop=0.20, out=0.46, mat=CORAL):
    """The brow over the glazing, on angled stays. The single most important part.

    An unshaded band of glass is a shopfront. The same band under a hood that
    projects most of a metre, tilted down, on visible stays, is an instrument
    that somebody armoured — and it throws a hard shadow across the glass, which
    is what keeps the band reading dark from below.
    """
    prof = chamfer_plan(w + out * 2, d + out * 2, cut + out * 0.6)
    f = p.loft([(z, prof), (z + drop, chamfer_plan(w + out * 1.2,
                                                   d + out * 1.2,
                                                   cut + out * 0.4))],
               axis='Z', mat=mat)
    p.shade(f, False)
    n = len(prof)
    for i in range(0, n, 2):                        # stays under the overhang
        ax, ay = prof[i]
        p.box((ax * 0.82, ay * 0.82, z - 0.30), (0.13, 0.13, 0.80), STEEL,
              rot=Matrix.Rotation(math.radians(26), 4,
                                  'Y' if abs(ax) > abs(ay) else 'X'))


def bearing_splay(p, z0, z1, w, d, cut, n=8, mat=STEEL):
    """A flared bearing under the floor pan — the cab widening onto its support.

    This started out as long raking struts taking the cab back to the shaft,
    which looked right and was wrong: they hung 2.9 m *below the component's own
    origin plane*, so anything this stacked onto got a set of steel spears
    through its roof. A component whose geometry escapes its own base cannot be
    placed by `z = deck_top`, which is the one thing every origin in this
    library promises.

    The load path is still visible, just expressed inside the envelope — a
    splayed collar and a ring of knee brackets — and the deck below it
    (`lattice_mast/Collar`) brings its own knee braces anyway.
    """
    f = p.loft([(z0, chamfer_plan(w * 0.72, d * 0.72, cut * 0.7)),
                (z1, chamfer_plan(w, d, cut))], axis='Z', mat=SLATE)
    p.shade(f, False)
    prof = chamfer_plan(w, d, cut)
    for i in range(0, len(prof), max(1, len(prof) // n)):
        ax, ay = prof[i]
        p.box((ax * 0.86, ay * 0.86, (z0 + z1) / 2), (0.24, 0.24, z1 - z0),
              mat, rot=Matrix.Rotation(math.radians(11), 4,
                                       'Y' if abs(ax) > abs(ay) else 'X'))
    p.box((0, 0, z1 - 0.06), (w + 0.18, d + 0.18, 0.14), DARK)


def sensor_pod(p, at, z, r=0.34, h=0.86, dish=False):
    """A clamped-on instrument — the kit that makes a head an observation head."""
    x, y = at
    p.box((x, y, z + 0.12), (r * 2.1, r * 2.1, 0.24), SLATE)
    p.cyl((x, y, z + 0.24 + h / 2), r, h, seg=8, mat=DARK)
    if dish:
        p.cyl((x, y, z + 0.30 + h), r * 1.5, 0.14, seg=10, mat=BLEACH,
              radius_top=r * 1.15)
        p.cyl((x, y, z + 0.44 + h), 0.07, 0.28, seg=6, mat=STEEL)
    else:
        p.cyl((x, y, z + 0.30 + h), r * 0.55, 0.30, seg=8, mat=STEEL)
        p.box((x, y, z + 0.52 + h), (r * 1.3, 0.09, 0.09), STEEL)


def skirt(p, z0, z1, w, d, cut, seed=0, patches=8):
    """The rusted body below the glass — plated, ribbed, streaked.

    This is the part that used to be a clean painted spandrel. Now it carries
    the same salvage-plate language as the hulls further down the tower, so the
    cab reads as the top of one structure rather than as a different building
    that happens to be up there.
    """
    rng = random.Random(seed)
    mass(p, z0, z1, w, d, w - 0.30, d - 0.30, cut, cut * 0.9, HULL)
    hw, hd = w / 2.0, d / 2.0
    for _ in range(patches):
        z = rng.uniform(z0 + 0.25, z1 - 0.3)
        mat = rng.choice((RUST, HULL, RUST, BLEACH))
        if rng.random() < 0.5:
            s = rng.choice((-1, 1))
            p.box((rng.uniform(-hw + 0.9, hw - 0.9), s * (hd + 0.03), z),
                  (rng.uniform(0.8, 1.9), 0.11, rng.uniform(0.4, 0.9)), mat)
        else:
            s = rng.choice((-1, 1))
            p.box((s * (hw + 0.03), rng.uniform(-hd + 0.9, hd - 0.9), z),
                  (0.11, rng.uniform(0.8, 1.7), rng.uniform(0.4, 0.9)), mat)
    for u in (-w * 0.31, w * 0.09, w * 0.34):        # ribs, irregular
        p.box((u, -hd - 0.04, (z0 + z1) / 2), (0.26, 0.30, z1 - z0 - 0.1), SLATE)
    for k in range(4):                               # streaks under the glass
        p.box((-w * 0.34 + k * w * 0.23, -hd - 0.07, z1 - 0.42),
              (0.16, 0.10, 0.80), RUST)


def roof_kit(p, z, w, d, seed=0, mast_pads=3):
    """Deck, hatch, aerial pads and plant — what sits on any cab roof.

    Deliberately sparse and dark: the tall aerials belong to `mast_rig` and to
    whatever the model builds for itself, so a cab that is not on a mast does
    not drag a forest of antennae into every scene it appears in.
    """
    rng = random.Random(seed)
    p.box((0, 0, z + 0.09), (w - 0.5, d - 0.5, 0.18), SLATE)
    p.box((w * 0.22, d * 0.18, z + 0.42), (1.20, 1.00, 0.60), SLATE)
    p.box((w * 0.22, d * 0.18, z + 0.76), (0.60, 0.44, 0.14), STEEL)
    p.box((-w * 0.26, d * 0.16, z + 0.34), (0.85, 0.85, 0.44), DARK)
    p.box((w * 0.04, -d * 0.28, z + 0.16), (1.00, 1.00, 0.14), STEEL)  # hatch
    p.box((w * 0.04, -d * 0.28, z + 0.25), (0.66, 0.66, 0.09), SLATE)
    for k in range(mast_pads):
        p.box((-w * 0.30 + k * w * 0.24, -d * 0.30, z + 0.28),
              (0.38, 0.38, 0.36), STEEL)
    for k in range(3):                                # roof fin bank
        p.box((-w * 0.36, -d * 0.05 + k * 0.24, z + 0.52), (1.10, 0.08, 0.70),
              STEEL)
    for _ in range(4):
        p.box((rng.uniform(-w * 0.35, w * 0.35), rng.uniform(-d * 0.35, d * 0.35),
               z + 0.28), (rng.uniform(0.25, 0.55), rng.uniform(0.25, 0.55),
                           0.26), rng.choice((DARK, RUST, SLATE)))


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def cab_wide(coll, mats):
    """The hero: 9.0 x 8.0 m over a chamfered plan, glazed and canted all round.

    The proportion is set by the glazing, not by the floor: 2.40 m of glass
    leaning out 0.62 m a side is what reads as an observation head. Make the
    band shorter and it becomes a caravan; make the cant shallower and it
    becomes an ordinary window.
    """
    w, d, cut = 9.0, 8.0, 1.35
    p = Part(mats)
    mass(p, 0.0, 0.46, w + 0.55, d + 0.55, w + 0.55, d + 0.55, cut + 0.2,
         cut + 0.2, SLATE)                                    # floor pan
    bearing_splay(p, 0.02, 0.46, w + 0.55, d + 0.55, cut + 0.2)
    skirt(p, 0.46, 1.70, w, d, cut, seed=4)
    p.box((0, 0, 1.76), (w + 0.16, d + 0.16, 0.16), SLATE)    # sill capping

    z1, z2 = 1.82, 4.22
    w2, d2 = w + 1.24, d + 1.24
    p.box((0, 0, (z1 + z2) / 2), (w - 0.5, d - 0.5, z2 - z1), BLACK)  # the room
    p.box((0, 0, z2 - 0.34), (w - 0.9, d - 0.9, 0.14), AMBER)        # lit ceiling
    facet_band(p, z1, z2, w, d, w2, d2, cut, cut + 0.5, 0.14,
               mat_of=lambda i: AMBER if i in (1, 5) else
               (SLATE if i % 2 == 0 else GLASS))
    visor(p, z2, w2, d2, cut + 0.5, drop=0.62, out=0.50)
    p.box((0, 0, z2 + 0.72), (w2 + 0.30, d2 + 0.30, 0.18), SLATE)

    roof_z = z2 + 0.80
    for i, (sx, sy) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        sensor_pod(p, (sx * (w2 / 2 - 0.55), sy * (d2 / 2 - 0.55)), roof_z,
                   dish=(i % 2 == 0))
    roof_kit(p, roof_z, w2, d2, seed=6)
    p.bevel(width=0.018, segments=1)
    return p.finish("Mesh_ControlCab_Wide", coll)


def cab_compact(coll, mats):
    """A 6.0 x 5.5 m watch head: glazed forward, armoured plate at the back.

    Not a scaled `Wide`. A head this size is entered from the back wall rather
    than through a floor hatch, so the rear facets are solid and carry the
    airlock — which changes the silhouette from a lantern to a turret.
    """
    w, d, cut = 6.0, 5.5, 0.95
    p = Part(mats)
    mass(p, 0.0, 0.42, w + 0.45, d + 0.45, w + 0.45, d + 0.45, cut + 0.15,
         cut + 0.15, SLATE)
    bearing_splay(p, 0.02, 0.42, w + 0.45, d + 0.45, cut + 0.15, n=6)
    skirt(p, 0.42, 1.62, w, d, cut, seed=9, patches=6)
    p.box((0, 0, 1.68), (w + 0.14, d + 0.14, 0.14), SLATE)

    z1, z2 = 1.74, 3.68
    w2, d2 = w + 0.92, d + 0.92
    p.box((0, 0, (z1 + z2) / 2), (w - 0.45, d - 0.45, z2 - z1), BLACK)
    p.box((0, 0, z2 - 0.28), (w - 0.8, d - 0.8, 0.12), AMBER)
    # facets 4,5 are the back of a chamfered plan — plate, not glass
    facet_band(p, z1, z2, w, d, w2, d2, cut, cut + 0.4, 0.13,
               mat_of=lambda i: HULL if i in (3, 4, 5) else
               (AMBER if i == 1 else (SLATE if i % 2 == 0 else GLASS)))
    visor(p, z2, w2, d2, cut + 0.4, drop=0.52, out=0.42)
    p.box((0, 0, z2 + 0.60), (w2 + 0.26, d2 + 0.26, 0.16), SLATE)
    # the hatch in the plated rear
    p.box((0.5, d / 2 + 0.62, 2.55), (1.30, 0.30, 2.05), SLATE)
    p.box((0.5, d / 2 + 0.76, 2.55), (1.02, 0.12, 1.80), DARK)
    p.box((0.5, d / 2 + 0.88, 3.66), (1.75, 0.62, 0.15), SLATE,
          rot=Matrix.Rotation(math.radians(-18), 4, 'X'))

    roof_z = z2 + 0.68
    sensor_pod(p, (-w2 / 2 + 0.60, -d2 / 2 + 0.60), roof_z, dish=True)
    sensor_pod(p, (w2 / 2 - 0.60, -d2 / 2 + 0.55), roof_z, r=0.26, h=1.10)
    roof_kit(p, roof_z, w2, d2, seed=11, mast_pads=2)
    p.bevel(width=0.018, segments=1)
    return p.finish("Mesh_ControlCab_Compact", coll)


def cab_annex(coll, mats):
    """The blind service storey under a cab. 9.0 x 8.0 m, 4.35 m. No windows.

    Same plan as `Wide` so they stack directly. This is where the first version
    was at its most terrestrial — a grid of twenty little square windows on a
    plant room, which is a thing no pressurised structure would ever have. It is
    now what it should always have been: plate, ribs, a louvre bank, a conduit
    spine, one port and an airlock. The solid grey mass that makes the glazed
    head above it look light.
    """
    w, d, h, cut = 9.0, 8.0, 4.35, 1.25
    p = Part(mats)
    mass(p, 0.0, h, w, d, w - 0.34, d - 0.34, cut, cut * 0.88, HULL)
    mass(p, 0.0, 0.34, w + 0.44, d + 0.44, w + 0.44, d + 0.44, cut + 0.2,
         cut + 0.2, SLATE)
    p.box((0, 0, h - 0.20), (w + 0.26, d + 0.26, 0.40), SLATE)   # head band
    p.box((0, 0, h + 0.06), (w + 0.10, d + 0.10, 0.14), CORAL)
    rng = random.Random(23)
    for _ in range(12):                                          # salvage plate
        z = rng.uniform(0.5, h - 0.5)
        s = rng.choice((-1, 1))
        if rng.random() < 0.55:
            p.box((rng.uniform(-3.4, 3.4), s * (d / 2 + 0.04), z),
                  (rng.uniform(0.9, 2.1), 0.12, rng.uniform(0.5, 1.2)),
                  rng.choice((RUST, HULL, RUST, BLEACH)))
        else:
            p.box((s * (w / 2 + 0.04), rng.uniform(-3.0, 3.0), z),
                  (0.12, rng.uniform(0.9, 1.9), rng.uniform(0.5, 1.2)),
                  rng.choice((RUST, HULL, RUST)))
    for u in (-3.2, -0.6, 2.4, 3.9):                             # ribs
        p.box((u, -d / 2 - 0.05, h / 2), (0.30, 0.32, h - 0.55), SLATE)
        p.box((u, d / 2 + 0.05, h / 2), (0.30, 0.32, h - 0.55), SLATE)
    # louvre bank instead of a window rank
    p.box((-2.10, -d / 2 - 0.14, h * 0.58), (3.00, 0.30, 2.00), SLATE)
    p.box((-2.10, -d / 2 - 0.26, h * 0.58), (2.70, 0.10, 1.76), BLACK)
    for k in range(7):
        p.box((-2.10, -d / 2 - 0.34, h * 0.58 - 0.78 + k * 0.26),
              (2.60, 0.12, 0.14), STEEL,
              rot=Matrix.Rotation(math.radians(28), 4, 'X'))
    # conduit spine up the flank, and a cable tray round the head band
    for k in range(3):
        p.cyl((w / 2 + 0.22 + k * 0.20, 1.6, h / 2), 0.11, h - 0.6, seg=7,
              mat=COPPER if k % 2 else STEEL)
    for k in range(2):
        p.box((w / 2 + 0.30, 1.6, 0.9 + k * 2.2), (0.66, 0.70, 0.24), DARK)
    p.cyl((0, -d / 2 - 0.30, h - 0.72), 0.13, w * 0.78, axis='X', seg=7,
          mat=COPPER)
    # one port, and the way in
    p.cyl((3.30, -d / 2 - 0.04, 2.60), 0.46, 0.26, axis='Y', seg=10, mat=SLATE)
    p.cyl((3.30, -d / 2 - 0.16, 2.60), 0.30, 0.22, axis='Y', seg=10, mat=BLACK)
    p.cyl((3.30, -d / 2 - 0.24, 2.60), 0.24, 0.06, axis='Y', seg=10, mat=AMBER)
    p.box((-0.20, -d / 2 - 0.12, 1.20), (1.44, 0.32, 2.40), SLATE)
    p.box((-0.20, -d / 2 - 0.26, 1.20), (1.12, 0.12, 2.12), DARK)
    p.box((-0.20, -d / 2 - 0.40, 2.62), (1.95, 0.72, 0.16), SLATE,
          rot=Matrix.Rotation(math.radians(19), 4, 'X'))
    p.box((-0.20, -d / 2 - 0.18, 2.92), (1.20, 0.06, 0.22), RED)
    for k in range(4):                                           # streaks
        p.box((-3.6 + k * 2.3, -d / 2 - 0.08, h * 0.45), (0.16, 0.10, 1.90),
              RUST)
    p.bevel(width=0.018, segments=1)
    return p.finish("Mesh_ControlCab_Annex", coll)


def cab_drum(coll, mats):
    """A ten-sided drum head, 7.4 m across, glazed the whole way round.

    The plan is the variation. A chamfered box still has four dominant faces; a
    drum has an unbroken horizon, which is what a search or approach head wants,
    and it gives the family a silhouette that is not "box with a visor".
    """
    r1, r2, n = 3.70, 4.34, 10
    p = Part(mats)

    def ring(r, z):
        return [(r * math.cos(2 * math.pi * i / n + math.pi / n),
                 r * math.sin(2 * math.pi * i / n + math.pi / n), z)
                for i in range(n)]

    p.cyl((0, 0, 0.24), r1 + 0.34, 0.48, seg=n, mat=SLATE)
    bearing_splay(p, 0.02, 0.48, r1 * 1.9, r1 * 1.9, r1 * 0.5, n=6)
    p.cyl((0, 0, 1.06), r1, 1.16, seg=n, mat=HULL)
    rng = random.Random(15)
    for _ in range(9):                                    # plate on the drum
        a = rng.uniform(0, 2 * math.pi)
        p.box((r1 * math.cos(a) * 1.01, r1 * math.sin(a) * 1.01,
               rng.uniform(0.7, 1.5)), (0.9, 0.12, 0.55),
              rng.choice((RUST, HULL, BLEACH)),
              rot=Matrix.Rotation(a + math.pi / 2, 4, 'Z'))
    p.cyl((0, 0, 1.70), r1 + 0.12, 0.16, seg=n, mat=SLATE)

    z1, z2 = 1.78, 4.08
    p.cyl((0, 0, (z1 + z2) / 2), r1 - 0.30, z2 - z1, seg=n, mat=BLACK)
    p.cyl((0, 0, z2 - 0.30), r1 - 0.6, 0.12, seg=n, mat=AMBER)
    lo, hi = ring(r1, z1), ring(r2, z2)
    for i in range(n):
        a, b = Vector(lo[i]), Vector(lo[(i + 1) % n])
        c, e = Vector(hi[i]), Vector(hi[(i + 1) % n])
        mid_lo, mid_hi = (a + b) / 2.0, (c + e) / 2.0
        dv = mid_hi - mid_lo
        chord = b - a
        p.box((mid_lo + mid_hi) / 2.0, (chord.length, 0.13, dv.length),
              AMBER if i in (1, 6) else (SLATE if i % 2 else GLASS),
              rot=basis(chord, dv))
        d2v = c - a
        p.box((a + c) / 2.0, (0.14, 0.24, d2v.length), STEEL,
              rot=basis(chord, d2v))
    p.cyl((0, 0, z1), r1 + 0.07, 0.17, seg=n, mat=STEEL)
    p.cyl((0, 0, z2 + 0.20), r2 + 0.52, 0.42, seg=n, mat=CORAL)   # the visor
    p.cyl((0, 0, z2 + 0.46), r2 + 0.20, 0.16, seg=n, mat=SLATE)
    for k in range(5):                                            # visor stays
        a = 2 * math.pi * k / 5
        p.box(((r2 + 0.30) * math.cos(a), (r2 + 0.30) * math.sin(a), z2 - 0.28),
              (0.13, 0.13, 0.78), STEEL, rot=Matrix.Rotation(math.radians(24),
                                                             4, 'Y'))
    roof_z = z2 + 0.54
    for k in range(3):
        a = 2 * math.pi * k / 3 + 0.6
        sensor_pod(p, ((r2 - 0.8) * math.cos(a), (r2 - 0.8) * math.sin(a)),
                   roof_z, dish=(k == 0))
    roof_kit(p, roof_z, r2 * 1.5, r2 * 1.5, seed=19, mast_pads=2)
    # An octagon-and-a-bit is a faceted solid, not a coarse cylinder: `Part.cyl`
    # smooth-shades its barrel, which would round ten deliberate corners into a
    # soft tube. Everything here is flat by design, so it is flattened wholesale.
    p.shade(list(p.bm.faces), False)
    p.bevel(width=0.018, segments=1)
    return p.finish("Mesh_ControlCab_Drum", coll)


def cab_derelict(coll, mats):
    """`Wide`'s envelope after the glass has gone. Bleached, unlit, bent.

    A genuine silhouette change rather than a repaint: with the glazing out, the
    mullion cage stands on its own, the visor has torn off along one side and
    droops, and the sensor pods are gone bar their pads. Built to the same plan
    so it drops onto the same deck as the intact head.
    """
    w, d, cut = 9.0, 8.0, 1.35
    p = Part(mats)
    mass(p, 0.0, 0.46, w + 0.55, d + 0.55, w + 0.55, d + 0.55, cut + 0.2,
         cut + 0.2, SLATE)
    bearing_splay(p, 0.02, 0.46, w + 0.55, d + 0.55, cut + 0.2)
    skirt(p, 0.46, 1.70, w, d, cut, seed=27, patches=14)
    p.box((0, 0, 1.76), (w + 0.16, d + 0.16, 0.16), SLATE)

    z1, z2 = 1.82, 4.22
    w2, d2 = w + 1.24, d + 1.24
    p.box((0, 0, (z1 + z2) / 2), (w - 0.5, d - 0.5, z2 - z1), BLACK)
    # the cage survives; two panes still in their frames, the rest gone
    facet_band(p, z1, z2, w, d, w2, d2, cut, cut + 0.5, 0.12,
               mat_of=lambda i: GLASS if i in (2, 3) else BLACK, bar=0.16)
    for k in range(3):                                    # bent mullions
        p.box((-2.6 + k * 2.4, -(d2 / 2 - 0.15), z2 - 0.55),
              (0.13, 0.13, 1.55), STEEL,
              rot=Matrix.Rotation(math.radians(15 + k * 11), 4, 'Y'))
    # The visor, torn away on the -Y side and drooping. Built with the same
    # helper as the intact heads rather than as its own loft: a bare
    # `chamfer_plan` loft caps top and bottom, so what should read as a rim came
    # out as a solid 11 x 10 m lid sitting over the whole cab.
    visor(p, z2, w2, d2, cut + 0.5, drop=0.50, out=0.34, mat=BLEACH)
    p.box((0, -(d2 / 2 + 0.55), z2 + 0.10), (w2 * 0.66, 1.05, 0.16), BLEACH,
          rot=Matrix.Rotation(math.radians(-27), 4, 'X'))
    p.box((0, 0, z2 + 0.66), (w2 + 0.26, d2 + 0.26, 0.16), SLATE)

    roof_z = z2 + 0.74
    p.box((0, 0, roof_z + 0.09), (w2 - 0.5, d2 - 0.5, 0.18), DARK)
    for i, (sx, sy) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        p.box((sx * (w2 / 2 - 0.55), sy * (d2 / 2 - 0.55), roof_z + 0.14),
              (0.72, 0.72, 0.22), SLATE)                  # pads, pods gone
        if i == 2:
            p.cyl((sx * (w2 / 2 - 0.55), sy * (d2 / 2 - 0.55), roof_z + 0.55),
                  0.30, 0.70, seg=8, mat=DARK,
                  rot=Matrix.Rotation(math.radians(22), 4, 'X'))
    p.box((w2 * 0.20, d2 * 0.16, roof_z + 0.40), (1.15, 0.95, 0.50), DARK)
    for k in range(5):
        p.box((-w / 2 + 0.7 + k * 1.9, -(d2 / 2 + 0.10), z2 + 0.30),
              (0.22, 0.10, 0.72), RUST)
    p.bevel(width=0.018, segments=1)
    return p.finish("Mesh_ControlCab_Derelict", coll)


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Wide", cab_wide), ("Compact", cab_compact),
                     ("Annex", cab_annex), ("Drum", cab_drum),
                     ("Derelict", cab_derelict)):
        fn(collection("Coll_ControlCab_%s" % name), mats)
    report()
    save(out)


main()
