"""models/vehicles/sky_city - the Sky Tribe's floating home.

The faction design calls for it and nothing had been built: "Sky Tribe... they
live in floating vehicles in the sky (not created yet - backlog)". This is that
vehicle. It is not a ship with people on it; it is a place people live that
happens to fly, and the modelling follows from that distinction.

The fiction the shapes carry: somebody else built the lattice - an industrial
lifting hull, hazard yellow, riveted, far too rectilinear to be nomad work. The
tribe inherited or salvaged it, slung three gas bags in its cage, and then grew
a town along its flanks out of timber, cloth and scrap. Every surface is one of
those two languages and they never blend: painted steel below, cloth and ply
above and outboard.

Frame: the library convention, **-Y forward, +X starboard, +Z up**, origin on the
keel centreline at the promenade's walking surface. 116 m long, 22 m across the
side decks, 30 m across the sail outriggers, 25 m tall to the masthead.

## The one structural idea

The three bags hang inside an **open cage** - eleven ring frames on six
longitudinal stringers, at a constant 8.4 m radius about an axis 10 m above the
deck. The bags are 7.0, 7.6 and 7.2 m, so each rattles inside the cage by a
different amount. That is the whole salvage story in one number: the cage was
not built for these bags.

It also does the level-design work. The side promenades run outboard of the keel
at x 4.2 to 11.2, and the cage sweeps down over them - 10 m of headroom at the
outboard rail, 2.7 m where it meets the keel. A player walking inboard has to
duck, walking outboard opens up to sky. That gradient, not a sign, is what makes
the deck read as a street with an edge.

## Where the constitution bore on it

- **GDC-L1-LEVEL-0002** (make space legible - paths, edges, districts, nodes,
  landmarks). A 116 m walkable deck is a level, and an evenly-cluttered one
  would be a samey maze. So: the promenade is a continuous unobstructed lane at
  x 4.2-7.0 with every dwelling outboard of it (a **path** with a hard **edge**);
  the three bags divide the ship into three **districts** that differ in what
  they hold - homes forward, market amidships, workshops aft; the ring frames at
  the district boundaries are the **nodes**; and the helm cab forward, the dish
  mast aft and the two sails are **landmarks** visible from anywhere on deck. The
  aft apron is left deliberately empty, which is what makes the rest read as
  crowded rather than merely noisy.
- **GDC-L1-PERF-0004** (budget the frame). This is one hero landmark that will
  be on screen with a whole world behind it, so the detail budget is spent on
  silhouette and spent once: `catwalk_span` (72 tris/m of railing) does every
  run above head height and `handrail` (309 tris/m) only the stretches a player
  stands at. Every repeated dwelling, crate and lantern is a `stamp` copy
  sharing one mesh datablock, so ninety placements cost the memory of about
  twenty meshes and batch as such.

Both are `contextual`, and both are applied here rather than merely cited: the
empty apron and the two-tier railing rule are the concrete consequences.

## Reuse

Primary structure - keel, cage, decks, outriggers, stern gear, prow, gantry - is
bespoke, because nothing in a kit is 116 m long. Everything hung on it is the
library's: `shanty_addon`, `hab_capsule`, `cabin_module`, `control_cab`,
`sensor_cupola`, `catwalk_span`, `handrail`, `awning_shade`, `facade_awning`,
`mast_rig`, `window_bank`, `hull_plate`, `supply_crate`, `fuel_barrel`,
`floodlight_bank`, `camp_lantern`, plus the two components written for this
model, `gas_envelope` and `lateen_sail`. See sky_city_BUILD.md.

    blender --background --python sky_city.py -- --out sky_city.blend

Generation script - historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
import _buildlib as bl  # noqa: E402
from _buildlib import Part  # noqa: E402

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

COMPONENTS = os.path.join(bl.LIB_ROOT, "components")
SEED = 20260915

# ---------------------------------------------------------------------------
# Envelope. Every number a later reader might want is here rather than inline.
# ---------------------------------------------------------------------------
BOW, STERN = -56.0, 56.0     # -Y is forward
DECK = 0.0                   # promenade walking surface
KEEL_HW = 4.2                # keel girder half-width
KEEL_BOT = -4.6              # underside of the girder
SIDE_IN, SIDE_OUT = 4.2, 11.2       # side promenade, inboard/outboard edge
LANE_OUT = 7.0               # outboard limit of the clear walking lane
CAGE_R, CAGE_Z = 8.4, 10.0   # the cage the gas bags hang in
RING_Y = [-50.0, -40.0, -30.0, -20.0, -10.0, 0.0,
          10.0, 20.0, 30.0, 40.0, 50.0]
STRINGER_A = [0.0, 60.0, 120.0, 180.0, -60.0, -120.0]   # degrees about the axis
BAG_Y = [-28.0, 0.0, 28.0]
GANTRY_Z = CAGE_Z + CAGE_R + 1.3     # 19.7 - the lookout route over the bags
GANTRY_HW = 1.3
RAIL_H = 1.10                # matches components/structural/handrail
# The walkways slung off the hull, which is where the town's second storey went
# after two earlier positions turned out to be occupied.
#
# Between the houses at x = 7.8 the build assertion caught a stair's top corner
# inside a gas bag: the bags are fattest amidships and anything tall enough to
# be useful there reaches them. Moved outboard to x = 13.4 they then ran clean
# through the sails, which at 38 m occupy x 11.5-14.5 from y -23 to +12 on both
# sides - a zone that only exists once the sails are at their final size.
#
# So: SKYWALKS sit at x = 14.0, outboard of the widest dwelling (12.65 m) and
# forward or aft of the sail zone. UNDERWALKS hang beneath the keel, where
# nothing else on the ship goes at all - the bags are all above z = +2.4 - and
# are the ones that read as genuinely precarious.
SKYWALK_X = 14.0
SKYWALKS = [(-40.0, 1, 2.6), (-40.0, -1, 3.8),
            (34.0, 1, 3.2), (34.0, -1, 4.4)]
UNDERWALK_X, UNDERWALK_Z = 6.0, -5.8
UNDERWALKS = [(-30.0, 1), (0.0, -1), (24.0, 1)]
SAIL_X, SAIL_Y, SAIL_Z = 13.5, -10.0, 1.2
SAIL_RAKE = math.radians(18.0)
SAIL_K = 1.45                # 26 m of component luff becomes 38 m of ship sail
# The only two stations where a centreline climb is clear of both the gas bags
# and the helm cab: a 2.6 m window between the cab's aft face (y = -43.0) and
# the first bag's nose (y = -40.5), and the open deck aft of the last bag.
LADDER_Y = (-41.7, 44.0)
LADDER_H = 3.39              # one `Mesh_Handrail_Ladder`, unscaled

MATS = [
    "Mat_Paint_Hazard_Yellow",   # 0 the inherited lattice - its original paint
    "Mat_Metal_Steel_Worn",      # 1 bare steel where the paint is gone
    "Mat_Metal_Rust_Heavy",      # 2 weathering at every joint
    "Mat_Metal_Steel_Dark",      # 3 fittings, bolts, brackets
    "Mat_Wood_Ply_Worn",         # 4 scavenged decking over the steel
    "Mat_Wood_Timber_Silvered",  # 5 nomad timber - spars, props, rails
    "Mat_Paint_Safety_Orange",   # 6 deck edges and hazard stripes
    "Mat_Fabric_Rope_Hemp",      # 7 standing rigging and lashings
    "Mat_Metal_HullRust_Orange",  # 8 rusted plate patches
    "Mat_Neutral_Black_Matte",   # 9 grating voids and shadow gaps
    "Mat_Emissive_Amber",        # 10 deck lamps
    "Mat_Glass_Canopy_Tinted",   # 11 glazing
    "Mat_Fabric_Canvas_Faded",   # 12 tarpaulins and lashed covers
]
(YELLOW, STEEL, RUST, DARK, PLY, TIMBER, ORANGE, ROPE, HULLRUST, BLACK,
 LAMP, GLASS, CANVAS) = range(13)


# ---------------------------------------------------------------------------
# Small shared geometry helpers
# ---------------------------------------------------------------------------

def beam(p, a, b, w, h, mat):
    """A box member between two points, rolled so `h` is its vertical-ish axis."""
    a, b = Vector(a), Vector(b)
    d = b - a
    if d.length < 1e-6:
        return []
    rot = d.to_track_quat('Y', 'Z').to_matrix().to_4x4()
    return p.box((a + b) / 2, (w, d.length, h), mat, rot=rot)


def cage_pt(angle_deg, y, r=CAGE_R):
    """A point on the cage ring at `y`, `angle_deg` measured from starboard."""
    a = math.radians(angle_deg)
    return Vector((r * math.cos(a), y, CAGE_Z + r * math.sin(a)))


def rope(p, a, b, sag=0.0, radius=0.06, steps=1, mat=ROPE):
    """A rope run, optionally sagging under its own weight."""
    a, b = Vector(a), Vector(b)
    pts = []
    for i in range(steps + 1):
        t = i / steps
        q = a.lerp(b, t)
        q.z -= sag * math.sin(math.pi * t)
        pts.append(q)
    faces = []
    for u, v in zip(pts, pts[1:]):
        d = v - u
        if d.length < 1e-6:
            continue
        rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        faces += p.cyl((u + v) / 2, radius, d.length, 'Z', seg=5, mat=mat,
                       rot=rot)
    return faces


# ---------------------------------------------------------------------------
# Primary structure - bespoke, because no kit part is 116 m long
# ---------------------------------------------------------------------------

def keel(coll, mats):
    """The inherited box girder: four longerons, frames, and diagonal bracing.

    Hazard yellow, because whoever built it painted it that way and the tribe
    never repainted. Rust and bare steel appear at the joints and at the ends,
    where a salvaged hull wears first.
    """
    p = Part(mats)
    rng = random.Random(SEED + 1)
    xs, zs = (-KEEL_HW, KEEL_HW), (KEEL_BOT, DECK - 0.22)
    for x in xs:                                   # longerons
        for z in zs:
            beam(p, (x, BOW, z), (x, STERN, z), 0.46, 0.46, YELLOW)
    for i in range(int((STERN - BOW) / 4.0) + 1):   # transverse frames
        y = BOW + i * 4.0
        for z in zs:
            beam(p, (-KEEL_HW, y, z), (KEEL_HW, y, z), 0.30, 0.30, YELLOW)
        for x in xs:
            beam(p, (x, y, KEEL_BOT), (x, y, DECK - 0.22), 0.30, 0.30, YELLOW)
        if i % 3 == 0:
            p.torus((0, y, KEEL_BOT + 0.2), 0.5, 0.09, axis='Y', maj_seg=8,
                    min_seg=5, mat=RUST)
    for i in range(int((STERN - BOW) / 8.0)):        # side and belly diagonals
        y0 = BOW + i * 8.0
        up = i % 2 == 0
        for x in xs:
            beam(p, (x, y0, KEEL_BOT if up else DECK - 0.22),
                 (x, y0 + 8.0, DECK - 0.22 if up else KEEL_BOT),
                 0.20, 0.20, STEEL)
        beam(p, (-KEEL_HW, y0, KEEL_BOT), (KEEL_HW, y0 + 8.0, KEEL_BOT),
             0.18, 0.18, STEEL)
    # Scavenged plate patched over the girder where something tore through it.
    for _ in range(26):
        y = rng.uniform(BOW + 4, STERN - 4)
        x = rng.choice(xs) * rng.uniform(0.98, 1.04)
        p.box((x, y, rng.uniform(KEEL_BOT + 0.6, DECK - 0.6)),
              (0.12, rng.uniform(1.6, 3.4), rng.uniform(0.9, 2.2)),
              rng.choice((HULLRUST, STEEL, RUST)))
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_SkyCity_Keel", coll)


def cage(coll, mats):
    """Eleven ring frames on six stringers - what the gas bags hang inside.

    The rings are sixteen-sided, matching the bags' own facet count, so a ring
    reads as built around a bag rather than merely near one.
    """
    p = Part(mats)
    seg = 16
    for y in RING_Y:
        pts = [cage_pt(360.0 * i / seg, y) for i in range(seg)]
        for a, b in zip(pts, pts[1:] + pts[:1]):
            if a.z < DECK + 0.4 and b.z < DECK + 0.4:
                continue          # the keel occupies the bottom of the ring
            beam(p, a, b, 0.34, 0.34, YELLOW)
        for ang in (-150.0, -30.0):      # legs down onto the keel girder
            f = cage_pt(ang, y)
            beam(p, f, (math.copysign(KEEL_HW, f.x), y, DECK - 0.2),
                 0.28, 0.28, STEEL)
    for ang in STRINGER_A:
        a, b = cage_pt(ang, BOW + 6.0), cage_pt(ang, STERN - 6.0)
        beam(p, a, b, 0.32, 0.32, YELLOW)
    # Diagonal bracing in the two upper faces, one bay in two.
    for i in range(len(RING_Y) - 1):
        if i % 2:
            continue
        for lo, hi in ((60.0, 120.0), (0.0, 60.0), (120.0, 180.0)):
            beam(p, cage_pt(lo, RING_Y[i]), cage_pt(hi, RING_Y[i + 1]),
                 0.16, 0.16, STEEL)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_SkyCity_Cage", coll)


def decks(coll, mats):
    """Promenade, side galleries and the cantilevered scraps hung off them.

    The clear lane is x 4.2 to 7.0 on both sides and stays plated the whole
    length. Outboard of it the deck is patchy on purpose - ply over steel, a
    grating here, a hole there - and outboard of THAT the tribe has hung
    platforms on brackets over nothing at all.
    """
    p = Part(mats)
    rng = random.Random(SEED + 2)
    for sx in (-1, 1):
        # Brackets carrying the gallery off the girder.
        for i in range(int((STERN - BOW) / 3.2) + 1):
            y = BOW + i * 3.2
            beam(p, (sx * KEEL_HW, y, DECK - 0.30),
                 (sx * SIDE_OUT, y, DECK - 0.30), 0.16, 0.34, STEEL)
            if i % 2 == 0:
                beam(p, (sx * KEEL_HW, y, DECK - 1.9),
                     (sx * SIDE_OUT, y, DECK - 0.34), 0.13, 0.13, STEEL)
        # The clear lane - continuous, plated, orange-edged. The path.
        p.box((sx * (SIDE_IN + LANE_OUT) / 2, 0.0, DECK - 0.12),
              (LANE_OUT - SIDE_IN, STERN - BOW, 0.20), PLY)
        p.box((sx * LANE_OUT, 0.0, DECK - 0.05), (0.22, STERN - BOW, 0.10),
              ORANGE)
        # Outboard strip, laid in patches with gaps left in it.
        y = BOW
        while y < STERN:
            run = rng.uniform(3.0, 9.0)
            if rng.random() < 0.82:
                w = rng.uniform(2.6, SIDE_OUT - LANE_OUT)
                p.box((sx * (LANE_OUT + w / 2), y + run / 2, DECK - 0.12),
                      (w, run * 0.97, 0.18),
                      rng.choice((PLY, PLY, STEEL, TIMBER)))
            y += run + rng.uniform(0.0, 1.6)
        # Platforms cantilevered past the edge, on raking props.
        for _ in range(7):
            y = rng.uniform(BOW + 8, STERN - 12)
            w, ln = rng.uniform(2.2, 3.6), rng.uniform(2.6, 4.4)
            cx = sx * (SIDE_OUT + w / 2)
            p.box((cx, y, DECK - 0.14), (w, ln, 0.16), TIMBER)
            beam(p, (sx * SIDE_OUT, y, DECK - 2.6),
                 (cx + sx * w / 2, y, DECK - 0.20), 0.14, 0.14, TIMBER)
    # Brackets and raking props carrying the hanging walkways. Built here rather
    # than with the catwalks themselves so that a span always has something
    # under it - a walkway hung on nothing is the single thing that most makes
    # a structure read as unfinished rather than as jury-rigged.
    for y, sx, z in SKYWALKS:
        for dy in (-2.9, 0.0, 2.9):
            beam(p, (sx * SIDE_OUT, y + dy, DECK + z - 0.22),
                 (sx * (SKYWALK_X + 1.0), y + dy, DECK + z - 0.22),
                 0.16, 0.26, TIMBER)
        for dy in (-2.9, 2.9):
            beam(p, (sx * SIDE_OUT, y + dy, DECK - 2.4),
                 (sx * (SKYWALK_X + 0.6), y + dy, DECK + z - 0.28),
                 0.15, 0.15, TIMBER)
        # A hand line strung along the outboard side of the span.
        rope(p, (sx * (SKYWALK_X + 1.0), y - 3.1, DECK + z + RAIL_H + 0.1),
             (sx * (SKYWALK_X + 1.0), y + 3.1, DECK + z + RAIL_H + 0.1),
             sag=0.22, steps=3, radius=0.04)
    # Hangers for the walkways slung under the keel, dropped off the girder's
    # bottom longeron rather than the deck edge.
    for y, sx in UNDERWALKS:
        for dy in (-2.9, 0.0, 2.9):
            beam(p, (sx * KEEL_HW, y + dy, KEEL_BOT),
                 (sx * (UNDERWALK_X + 1.0), y + dy, UNDERWALK_Z - 0.2),
                 0.15, 0.15, STEEL)
            rope(p, (sx * KEEL_HW, y + dy, KEEL_BOT + 0.3),
                 (sx * (UNDERWALK_X + 0.9), y + dy, UNDERWALK_Z + RAIL_H),
                 radius=0.05)
        beam(p, (sx * (UNDERWALK_X + 1.0), y - 3.2, UNDERWALK_Z - 0.2),
             (sx * (UNDERWALK_X + 1.0), y + 3.2, UNDERWALK_Z - 0.2),
             0.16, 0.20, STEEL)
    # Deck lamps down the lane, alternating sides - the run of light a player
    # follows at night, and the reason the lane reads as the route.
    for i in range(14):
        y = BOW + 6.0 + i * 7.6
        sx = 1 if i % 2 else -1
        p.cyl((sx * (LANE_OUT - 0.3), y, DECK + 1.1), 0.07, 2.2, 'Z', seg=6,
              mat=DARK)
        p.cyl((sx * (LANE_OUT - 0.3), y, DECK + 2.3), 0.20, 0.26, 'Z', seg=8,
              mat=LAMP)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_SkyCity_Decks", coll)


def cradles(coll, mats):
    """What actually carries each bag: a saddle, two hoops and the tie straps.

    The cage holds the bags in; these hold them up. Sized per bag, so the
    saddle under the slack one sits lower than the saddle under the fat one.
    """
    p = Part(mats)
    for y, r in zip(BAG_Y, (7.0, 7.6, 7.2)):
        for dy in (-6.5, 6.5):
            seg = 16
            pts = [cage_pt(180.0 + 180.0 * i / seg, y + dy, r + 0.35)
                   for i in range(seg + 1)]
            for a, b in zip(pts, pts[1:]):
                beam(p, a, b, 0.26, 0.26, STEEL)
            for ang in (200.0, 340.0):
                f = cage_pt(ang, y + dy, r + 0.35)
                beam(p, f, (math.copysign(KEEL_HW, f.x), y + dy, DECK - 0.2),
                     0.22, 0.22, STEEL)
            # Tie straps over the crown, hemp, pulled down to the ring.
            for ang in (56.0, 124.0):
                rope(p, cage_pt(ang, y + dy, r + 0.30),
                     cage_pt(ang, y + dy, CAGE_R - 0.2), radius=0.09)
        # The saddle the bag rests in.
        for i in range(9):
            a = 200.0 + 140.0 * i / 8
            f = cage_pt(a, y, r + 0.30)
            beam(p, (f.x, y - 6.5, f.z), (f.x, y + 6.5, f.z), 0.18, 0.18,
                 TIMBER)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_SkyCity_Cradles", coll)


def outriggers(coll, mats):
    """The two sail booms projecting past the deck edge, and their stays.

    The sails are mounted outboard of the promenade on purpose: sheeted over
    the deck they would sweep the one continuous walking route on the ship.
    """
    p = Part(mats)
    for sx in (-1, 1):
        root = Vector((sx * KEEL_HW, SAIL_Y, DECK - 0.4))
        tip = Vector((sx * (SAIL_X + 0.6), SAIL_Y, DECK - 0.1))
        beam(p, root, tip, 0.52, 0.62, YELLOW)
        for dy in (-3.4, 3.4):
            beam(p, (sx * KEEL_HW, SAIL_Y + dy, DECK - 0.4), tuple(tip),
                 0.26, 0.30, STEEL)
        beam(p, (sx * SIDE_OUT, SAIL_Y, DECK - 3.4), tuple(tip), 0.24, 0.24,
             STEEL)
        p.cyl((sx * SAIL_X, SAIL_Y, DECK + 0.5), 0.62, 1.5, 'Z', seg=8,
              mat=DARK)
        p.torus((sx * SAIL_X, SAIL_Y, DECK + 1.15), 0.68, 0.10, axis='Z',
                maj_seg=8, min_seg=5, mat=RUST)
        # Standing rigging: masthead back to the hull and out to the cage.
        head = Vector((sx * SAIL_X, SAIL_Y - 27.0 * math.sin(SAIL_RAKE),
                       SAIL_Z + 27.0 * math.cos(SAIL_RAKE)))
        rope(p, head, (sx * SIDE_OUT, SAIL_Y + 22.0, DECK - 0.2), steps=4,
             sag=0.5, radius=0.08)
        rope(p, head, (sx * SIDE_OUT, BOW + 10.0, DECK - 0.2), steps=4,
             sag=0.4, radius=0.08)
        rope(p, head, tuple(cage_pt(90.0 if sx > 0 else 90.0, SAIL_Y + 6.0)),
             steps=3, sag=0.3, radius=0.07)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_SkyCity_Outriggers", coll)


def prow(coll, mats):
    """The bow: a raked cutwater, the anchor fairleads, and a lookout platform.

    The forward landmark. Everything about it is the inherited hull's language -
    plate, rivets, hazard paint - because this is the end the tribe never
    rebuilt.
    """
    p = Part(mats)
    for z in (KEEL_BOT, DECK - 0.22):
        beam(p, (-KEEL_HW, BOW, z), (0.0, BOW - 5.2, z + 0.9), 0.40, 0.40,
             YELLOW)
        beam(p, (KEEL_HW, BOW, z), (0.0, BOW - 5.2, z + 0.9), 0.40, 0.40,
             YELLOW)
    beam(p, (0, BOW - 5.2, KEEL_BOT + 0.9), (0, BOW - 5.2, DECK + 0.7),
         0.48, 0.48, YELLOW)
    p.loft([(BOW - 5.0, [(-0.7, KEEL_BOT + 1.1), (0.7, KEEL_BOT + 1.1),
                         (0.7, DECK - 0.3), (-0.7, DECK - 0.3)]),
            (BOW + 1.0, [(-KEEL_HW, KEEL_BOT), (KEEL_HW, KEEL_BOT),
                         (KEEL_HW, DECK - 0.22), (-KEEL_HW, DECK - 0.22)]),
            (BOW + 7.0, [(-KEEL_HW, KEEL_BOT), (KEEL_HW, KEEL_BOT),
                         (KEEL_HW, DECK - 0.22), (-KEEL_HW, DECK - 0.22)])],
           axis='Y', mat=HULLRUST)
    p.rivets((-KEEL_HW, BOW + 0.4, DECK - 1.2), (0, BOW - 4.8, DECK - 0.5),
             9, radius=0.10, height=0.07, axis='Y', mat=DARK)
    p.rivets((KEEL_HW, BOW + 0.4, DECK - 1.2), (0, BOW - 4.8, DECK - 0.5),
             9, radius=0.10, height=0.07, axis='Y', mat=DARK)
    # The lookout platform over the stem - a place to stand at the very front.
    p.box((0, BOW - 3.2, DECK - 0.10), (4.6, 4.0, 0.20), PLY)
    for sx in (-1, 1):
        for dy in (-1.6, 1.4):
            p.cyl((sx * 2.1, BOW - 3.2 + dy, DECK + RAIL_H / 2), 0.07,
                  RAIL_H, 'Z', seg=6, mat=TIMBER)
        beam(p, (sx * 2.1, BOW - 4.9, DECK + RAIL_H),
             (sx * 2.1, BOW - 1.5, DECK + RAIL_H), 0.06, 0.06, TIMBER)
        rope(p, (sx * 2.1, BOW - 4.9, DECK + RAIL_H * 0.55),
             (sx * 2.1, BOW - 1.5, DECK + RAIL_H * 0.55), radius=0.035)
    beam(p, (-2.1, BOW - 4.9, DECK + RAIL_H), (2.1, BOW - 4.9, DECK + RAIL_H),
         0.06, 0.06, TIMBER)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_SkyCity_Prow", coll)


def stern_gear(coll, mats):
    """Rudder fin, two ducted airscrews on pylons, and the engine house.

    The aft landmark, and the only part of the ship that is unambiguously a
    vehicle rather than a town.
    """
    p = Part(mats)
    # Engine house on the keel.
    p.box((0, STERN - 6.0, DECK + 2.0), (7.2, 8.0, 4.4), HULLRUST)
    p.box((0, STERN - 6.0, DECK + 4.3), (7.6, 8.4, 0.35), STEEL)
    for sx in (-1, 1):
        p.box((sx * 3.7, STERN - 6.0, DECK + 2.2), (0.30, 6.4, 2.6), GLASS)
        p.louvres((sx * 3.0, STERN - 2.2, DECK + 0.6),
                  (sx * 3.6, STERN - 2.0, DECK + 3.4), 6, axis='Y', mat=DARK)
    # Airscrew pylons and ducts.
    for sx in (-1, 1):
        hub = Vector((sx * 9.4, STERN + 2.6, DECK + 5.4))
        beam(p, (sx * 3.4, STERN - 4.0, DECK + 1.2), tuple(hub), 0.62, 0.72,
             YELLOW)
        beam(p, (sx * SIDE_OUT, STERN - 8.0, DECK - 0.3), tuple(hub),
             0.26, 0.26, STEEL)
        p.tube(tuple(hub), 3.5, 0.34, 2.6, axis='Y', seg=14, mat=YELLOW)
        p.torus(tuple(hub + Vector((0, 1.3, 0))), 3.45, 0.16, axis='Y',
                maj_seg=14, min_seg=6, mat=RUST)
        p.torus(tuple(hub - Vector((0, 1.3, 0))), 3.45, 0.16, axis='Y',
                maj_seg=14, min_seg=6, mat=RUST)
        p.cyl(tuple(hub), 0.85, 3.0, 'Y', seg=10, mat=DARK)
        for i in range(4):          # blades, stopped at rest
            a = math.radians(38 + 90 * i)
            p.box(tuple(hub + Vector((math.cos(a) * 1.9, 0.0,
                                      math.sin(a) * 1.9))),
                  (0.42, 0.34, 3.5), TIMBER,
                  rot=Matrix.Rotation(a + math.pi / 2, 4, 'Y')
                  @ Matrix.Rotation(math.radians(22), 4, 'Z'))
    # Rudder: fin on the centreline, hinged on a post.
    p.cyl((0, STERN + 1.6, DECK + 3.0), 0.44, 9.0, 'Z', seg=8, mat=DARK)
    p.loft([(STERN + 1.4, [(-0.30, DECK + 0.4), (0.30, DECK + 0.4),
                           (0.30, DECK + 7.4), (-0.30, DECK + 7.4)]),
            (STERN + 7.6, [(-0.16, DECK + 1.9), (0.16, DECK + 1.9),
                           (0.16, DECK + 6.6), (-0.16, DECK + 6.6)])],
           axis='Y', mat=CANVAS)
    for dz in (1.0, 3.0, 5.0, 7.0):
        beam(p, (0, STERN + 1.4, DECK + dz), (0, STERN + 7.4, DECK + dz),
             0.10, 0.10, TIMBER)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_SkyCity_SternGear", coll)


def ladder(p, x, y, z0, z1, mat=STEEL, w=0.62, pitch=0.31):
    """Two stiles and a run of rungs. Cheap on purpose.

    `components/structural/handrail`'s `Ladder` is 3424 triangles for 3.39 m -
    about 1000 per metre, which buys cage hoops and shaped brackets worth having
    where a player's hands are. This ship needs roughly 90 vertical metres of
    climb, and stamping the kit part for all of it spent 89 000 triangles, 23%
    of the whole model, on ladders nobody looks at. This is 55 per metre.
    """
    h = z1 - z0
    for s in (-1, 1):
        p.box((x + s * w / 2, y, (z0 + z1) / 2), (0.07, 0.07, h), mat)
    n = max(1, int(h / pitch))
    for i in range(n):
        p.box((x, y, z0 + h * (i + 0.5) / n), (w, 0.05, 0.05), mat)


def climbs(coll, mats):
    """Every ladder on the ship, as one object.

    The two long centreline climbs to the crown gantry, the short ones out to
    the hanging walkways, and the drops through the deck to the under-keel runs.
    """
    p = Part(mats)
    for y in LADDER_Y:
        ladder(p, 1.3, y, DECK, GANTRY_Z)
        # A back guard on the long climbs, which are 19.7 m over open deck.
        for s in (-1, 1):
            p.box((1.3 + s * 0.66, y + 0.42, (DECK + GANTRY_Z) / 2),
                  (0.06, 0.06, GANTRY_Z - DECK), DARK)
    for y, sx, z in SKYWALKS:
        ladder(p, sx * 12.6, y, DECK, DECK + z + RAIL_H)
    for y, sx in UNDERWALKS:
        ladder(p, sx * (UNDERWALK_X + 1.1), y + 2.4,
               DECK + UNDERWALK_Z, DECK + 0.2)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_SkyCity_Climbs", coll)


def gantry(coll, mats):
    """The lookout route over the top of the bags, on the cage's crown.

    A second level, reached by ladders. Narrow, railed on one side only, and
    deliberately unpleasant - it is a place to go and look, not a second street.
    """
    p = Part(mats)
    # Reaches past the end bags to the two ladder stations, which are the only
    # places on the centreline where a climb is not inside a gas bag.
    y0, y1 = LADDER_Y[0] - 2.0, LADDER_Y[1] + 2.0
    p.box((0, (y0 + y1) / 2, GANTRY_Z - 0.09), (GANTRY_HW * 2, y1 - y0, 0.14),
          STEEL)
    n = int((y1 - y0) / 2.2)
    for i in range(n + 1):
        y = y0 + (y1 - y0) * i / n
        p.cyl((GANTRY_HW - 0.12, y, GANTRY_Z + RAIL_H / 2), 0.055, RAIL_H,
              'Z', seg=5, mat=STEEL)
        if i % 2 == 0:
            beam(p, (0, y, GANTRY_Z - 0.16), tuple(cage_pt(60.0, y)),
                 0.14, 0.14, STEEL)
            beam(p, (0, y, GANTRY_Z - 0.16), tuple(cage_pt(120.0, y)),
                 0.14, 0.14, STEEL)
    for dz in (RAIL_H, RAIL_H * 0.55):
        beam(p, (GANTRY_HW - 0.12, y0, GANTRY_Z + dz),
             (GANTRY_HW - 0.12, y1, GANTRY_Z + dz), 0.05, 0.05, STEEL)
    # Bunting strung the length of it, which is what makes it read as lived-in
    # rather than as maintenance access.
    for i in range(int((y1 - y0) / 9.0)):
        a = y0 + 2.0 + i * 9.0
        rope(p, (GANTRY_HW - 0.12, a, GANTRY_Z + RAIL_H + 0.9),
             (GANTRY_HW - 0.12, a + 9.0, GANTRY_Z + RAIL_H + 0.9),
             sag=1.1, steps=5, radius=0.035)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_SkyCity_Gantry", coll)


# ---------------------------------------------------------------------------
# Kit placement
# ---------------------------------------------------------------------------

KIT = {
    "components/structural/shanty_addon.blend": [
        "Mesh_Shanty_Box", "Mesh_Shanty_LeanTo", "Mesh_Shanty_Stack",
        "Mesh_Shanty_Awning", "Mesh_Shanty_Water"],
    "components/structural/hab_capsule.blend": [
        "Mesh_HabCapsule_Pod", "Mesh_HabCapsule_Short"],
    "components/structural/cabin_module.blend": [
        "Mesh_CabinModule_Habitat", "Mesh_CabinModule_Workshop"],
    "components/structural/control_cab.blend": ["Mesh_ControlCab_Compact"],
    "components/structural/sensor_cupola.blend": [
        "Mesh_SensorCupola_Dish", "Mesh_SensorCupola_Dome",
        "Mesh_SensorCupola_Lantern"],
    "components/structural/catwalk_span.blend": [
        "Mesh_Catwalk_Straight", "Mesh_Catwalk_Bridge", "Mesh_Catwalk_Stair",
        "Mesh_Catwalk_Corner"],
    "components/structural/handrail.blend": [
        "Mesh_Handrail_Straight", "Mesh_Handrail_Ladder",
        "Mesh_Handrail_Gate"],
    "components/structural/awning_shade.blend": [
        "Mesh_Awning_Sagging", "Mesh_Awning_Torn", "Mesh_Awning_LeanTo"],
    "components/structural/mast_rig.blend": [
        "Mesh_MastRig_Pennant", "Mesh_MastRig_Flag"],
    "components/structural/window_bank.blend": [
        "Mesh_WindowBank_Porthole", "Mesh_WindowBank_Shuttered"],
    "components/props/supply_crate.blend": [
        "Mesh_Crate_Large", "Mesh_Crate_Stack", "Mesh_Crate_Open",
        "Mesh_Crate_Long"],
    "components/props/fuel_barrel.blend": [
        "Mesh_Barrel_Drum", "Mesh_Barrel_Stack", "Mesh_Barrel_Tank"],
    "components/props/floodlight_bank.blend": ["Mesh_FloodlightBank_Single"],
    "components/props/camp_lantern.blend": [
        "Mesh_Lantern_Body", "Mesh_Lantern_Glass", "Mesh_Lantern_Handle"],
    "components/structural/gas_envelope.blend": [
        "Mesh_GasEnvelope_Plain", "Mesh_GasEnvelope_Banded",
        "Mesh_GasEnvelope_Patched"],
    "components/structural/lateen_sail.blend": [
        "Mesh_LateenSail_Main", "Mesh_LateenSail_Patched"],
}


def put(src, names, coll, prefix, bag, target, k=1.0, rz=0.0, rx=0.0,
        anchor="base"):
    """Stamp one kit part (or group) onto the ship."""
    srcs = [src[n] for n in names] if isinstance(names, (list, tuple)) \
        else [src[names]]
    delta = bl.place_delta(srcs, Vector(target), k=k, rz=rz, rx=rx,
                           anchor=anchor, pivot=srcs[0])
    return bl.stamp(srcs, delta, coll, prefix, bag)


def place_envelopes(src, coll, bag):
    """Three bags, biggest amidships where the lift is wanted."""
    for i, (y, name) in enumerate(zip(
            BAG_Y, ("Mesh_GasEnvelope_Patched", "Mesh_GasEnvelope_Banded",
                    "Mesh_GasEnvelope_Plain"))):
        put(src, name, coll, "Mesh_SkyCity_Bag%d" % (i + 1), bag,
            (0.0, y, CAGE_Z), anchor="center")


def mirror_x_source(obj, name, into):
    """A copy of `obj` mirrored across x = 0, baked into its own mesh data.

    Port and starboard on a **-Y-forward** model is a mirror in X.
    `_buildlib.mirror_y` mirrors across y = 0, which is the centreline only for
    the library's other convention (+X forward, as `ship_rv` uses), and here
    would flip the ship end for end.

    The mirror is baked rather than left as a negative scale on the object. A
    negative-determinant RIGID renderer is normally harmless - Unity reverses
    its culling mode and cancels the flip exactly - but this model is marked
    static in Unity, and a batcher that bakes transforms into a combined mesh
    has no renderer transform left to read that sign off. Baking here removes
    the question. `_exportlib`'s `fix_inverted` is NOT the alternative: it
    scattered three of the forty nomad buildings up to 137 m because that file
    shares mesh datablocks between objects, and so does this one - 43 meshes
    across 109 objects. See ArtPipeline.md.
    """
    mesh = obj.data.copy()
    mesh.name = name
    mesh.transform(Matrix.Diagonal((-1.0, 1.0, 1.0, 1.0)))
    mesh.flip_normals()
    new = bpy.data.objects.new(name, mesh)
    new.location = (-obj.location.x, obj.location.y, obj.location.z)
    into.objects.link(new)
    bpy.context.view_layer.update()
    return new


def place_sails(src, coll, bag, hidden):
    """One sail each side, on the outriggers, raked forward.

    Starboard carries the working white suit, port the red spare - the two sides
    of the rig deliberately do not match.
    """
    put(src, "Mesh_LateenSail_Main", coll, "Mesh_SkyCity_SailStbd", bag,
        (SAIL_X, SAIL_Y, SAIL_Z), k=SAIL_K, rx=SAIL_RAKE, anchor="origin")
    src["__port_sail"] = mirror_x_source(
        src["Mesh_LateenSail_Patched"], "Mesh_LateenSail_PatchedPort", hidden)
    put(src, "__port_sail", coll, "Mesh_SkyCity_SailPort", bag,
        (-SAIL_X, SAIL_Y, SAIL_Z), k=SAIL_K, rx=SAIL_RAKE, anchor="origin")


# (kind, y, side, x, rz-degrees, scale) for the dwellings. Written out rather
# than scattered at random: which district holds what is a design decision, and
# the two sides deliberately do not match.
HOMES = [
    # Forward district - where people sleep. Densest, most stacked.
    ("Mesh_Shanty_Stack", -36.0, 1, 9.2, -14, 1.00),
    ("Mesh_Shanty_Box", -30.5, 1, 9.6, 8, 0.95),
    ("Mesh_HabCapsule_Pod", -24.0, 1, 9.8, -4, 1.05),
    ("Mesh_Shanty_LeanTo", -19.0, 1, 10.1, 22, 1.10),
    ("Mesh_Shanty_Box", -34.0, -1, 9.4, -9, 1.08),
    ("Mesh_Shanty_Awning", -27.5, -1, 9.9, 15, 1.00),
    ("Mesh_Shanty_Stack", -21.0, -1, 9.1, -20, 0.92),
    ("Mesh_HabCapsule_Pod", -15.5, -1, 10.2, 6, 1.00),
    # Amidships - the market. Lower, more open, more cloth.
    ("Mesh_Shanty_Awning", -8.0, -1, 9.6, -6, 1.06),
    ("Mesh_Shanty_LeanTo", -2.0, -1, 10.0, 12, 0.98),
    ("Mesh_CabinModule_Habitat", 4.5, -1, 9.0, -3, 0.72),
    ("Mesh_Shanty_Box", 1.5, 1, 9.7, 18, 0.90),
    ("Mesh_Shanty_Awning", 7.5, 1, 10.0, -11, 1.04),
    # Aft district - workshops and water.
    ("Mesh_Shanty_Water", 14.0, 1, 9.8, 5, 1.12),
    ("Mesh_CabinModule_Workshop", 20.5, 1, 8.9, -7, 0.74),
    ("Mesh_Shanty_Water", 17.0, -1, 9.9, -16, 1.00),
    ("Mesh_HabCapsule_Short", 24.5, -1, 9.2, 4, 0.78),
    ("Mesh_Shanty_LeanTo", 30.0, -1, 10.1, -24, 1.05),
    ("Mesh_Shanty_Box", 28.0, 1, 9.5, 11, 0.96),
]


def place_town(src, coll, bag):
    """Dwellings, awnings, walkways, clutter and flags."""
    rng = random.Random(SEED + 3)
    for i, (name, y, sx, x, deg, k) in enumerate(HOMES):
        put(src, name, coll, "Mesh_SkyCity_Home%02d" % (i + 1), bag,
            (sx * x, y, DECK - 0.04), k=k, rz=math.radians(deg + (0 if sx > 0
                                                                  else 180)))
    # The gaps between neighbouring dwellings on each side. Everything that is
    # not a dwelling goes in one of these, computed rather than guessed: a
    # hand-picked y that happens to land on a house puts a catwalk through its
    # roof and an awning through its front wall, which is what the first pass
    # did in five places. The outboard strip is only 4.2 m deep, so there is no
    # room to stand anything in FRONT of a house without blocking the lane.
    spans = []
    for sx in (1, -1):
        ys = sorted(h[1] for h in HOMES if h[2] == sx)
        for a, b in zip(ys, ys[1:]):
            if b - a >= 8.0:
                spans.append(((a + b) / 2, sx))
    # Awnings, pitched in the gaps as market stalls rather than over doorways.
    for i, (y, sx) in enumerate(spans):
        put(src, rng.choice(("Mesh_Awning_Sagging", "Mesh_Awning_Torn",
                             "Mesh_Awning_LeanTo")),
            coll, "Mesh_SkyCity_Shade%02d" % (i + 1), bag,
            (sx * 8.9, y + rng.uniform(-0.8, 0.8), DECK - 0.04),
            k=rng.uniform(0.70, 0.85),
            rz=math.radians(rng.uniform(-30, 30) + (0 if sx > 0 else 180)))
    # The hanging walkways - the town's second storey, slung outboard over the
    # drop. Reached by ladder, never by stair: a ladder is 0.8 m square and will
    # fit between two houses, and climbing one out over open air is the whole
    # point of the route.
    # The ladders themselves are procedural - see `climbs()`. The kit's detailed
    # `Handrail_Ladder` appears exactly twice, at the two deck-level stations a
    # player boards by, where the hands are.
    for i, (y, sx, z) in enumerate(SKYWALKS):
        put(src, "Mesh_Catwalk_Straight", coll,
            "Mesh_SkyCity_Walk%02d" % (i + 1), bag,
            (sx * SKYWALK_X, y, DECK + z), rz=math.radians(90))
    for i, (y, sx) in enumerate(UNDERWALKS):
        put(src, "Mesh_Catwalk_Straight", coll,
            "Mesh_SkyCity_Under%02d" % (i + 1), bag,
            (sx * UNDERWALK_X, y, DECK + UNDERWALK_Z), rz=math.radians(90))
    for i, y in enumerate(LADDER_Y):
        put(src, "Mesh_Handrail_Ladder", coll,
            "Mesh_SkyCity_Boarding%02d" % (i + 1), bag, (1.3, y, DECK))
    # The climbs to the crown gantry are built in `climbs()`, not stamped here:
    # six stacked kit ladders per station was 41 000 triangles for two ladders.
    # A railed stretch where a player actually stands: the lookout rail on the
    # aft apron. `handrail` is four times the cost per metre of `catwalk_span`,
    # so it is used here and nowhere else.
    for i in range(6):
        put(src, "Mesh_Handrail_Straight", coll,
            "Mesh_SkyCity_Rail%02d" % (i + 1), bag,
            (SIDE_OUT - 0.2, 40.0 + i * 2.24, DECK), rz=0.0)
    for i in range(6):
        put(src, "Mesh_Handrail_Straight", coll,
            "Mesh_SkyCity_RailP%02d" % (i + 1), bag,
            (-SIDE_OUT + 0.2, 40.0 + i * 2.24, DECK), rz=math.radians(180))
    # Clutter, lashed down where a deck is actually used.
    #
    # Rejection-sampled against the dwellings. Scattering freely across the same
    # outboard strip the houses stand on drops crates INSIDE them - seven of the
    # first pass's twenty-two ended up in somebody's front room, which reads as
    # a bug from every angle that can see through a doorway.
    # Weighted toward the cheap ones: `Crate_Large` is 7204 triangles and
    # `Barrel_Drum` 1864, and at deck-clutter size nothing distinguishes them.
    crates = ("Mesh_Barrel_Drum", "Mesh_Barrel_Drum", "Mesh_Crate_Stack",
              "Mesh_Crate_Stack", "Mesh_Crate_Open", "Mesh_Barrel_Stack")
    taken = [(h[2] * h[3], h[1], 3.4) for h in HOMES]
    placed = 0
    for attempt in range(400):
        if placed == 12:
            break
        sx = rng.choice((-1, 1))
        x = sx * rng.uniform(LANE_OUT + 0.7, SIDE_OUT - 0.9)
        y = rng.uniform(BOW + 10, 34.0)
        if any(math.hypot(x - tx, y - ty) < r for tx, ty, r in taken):
            continue
        placed += 1
        put(src, rng.choice(crates), coll,
            "Mesh_SkyCity_Stow%02d" % placed, bag, (x, y, DECK - 0.04),
            k=rng.uniform(0.9, 1.25), rz=rng.uniform(0, math.pi * 2))
        taken.append((x, y, 1.9))
    # Lanterns hung on the cage legs.
    for i in range(9):
        y = BOW + 12.0 + i * 8.0
        sx = 1 if i % 2 else -1
        put(src, ["Mesh_Lantern_Body", "Mesh_Lantern_Glass",
                  "Mesh_Lantern_Handle"],
            coll, "Mesh_SkyCity_Lamp%02d" % (i + 1), bag,
            (sx * rng.uniform(5.0, 6.4), y, DECK + 2.5), k=1.9)
    # Pennants and flags on the cage crown - read from a long way off.
    for i, (y, ang) in enumerate([(-42.0, 70.0), (-16.0, 110.0), (6.0, 66.0),
                                  (32.0, 116.0), (46.0, 90.0)]):
        f = cage_pt(ang, y)
        put(src, rng.choice(("Mesh_MastRig_Pennant", "Mesh_MastRig_Flag")),
            coll, "Mesh_SkyCity_Flag%02d" % (i + 1), bag,
            (f.x, f.y, f.z), k=rng.uniform(1.1, 1.6),
            rz=rng.uniform(0, math.pi * 2))
    # Floodlights on the aft apron, pointed at the landing area.
    for i, sx in enumerate((-1, 1)):
        put(src, "Mesh_FloodlightBank_Single", coll,
            "Mesh_SkyCity_Flood%02d" % (i + 1), bag,
            (sx * (SIDE_OUT - 1.4), 38.0, DECK), k=2.2,
            rz=math.radians(20 if sx > 0 else -20))


def place_fittings(src, coll, bag):
    """Helm, sensors and the glazing that tells a player where the crew is."""
    put(src, "Mesh_ControlCab_Compact", coll, "Mesh_SkyCity_Helm", bag,
        (0.0, BOW + 9.0, DECK), k=1.05)
    put(src, "Mesh_WindowBank_Porthole", coll, "Mesh_SkyCity_HelmGlassS", bag,
        (3.6, BOW + 9.0, DECK + 1.6), rz=math.radians(90))
    put(src, "Mesh_WindowBank_Shuttered", coll, "Mesh_SkyCity_HelmGlassP", bag,
        (-3.6, BOW + 12.0, DECK + 1.6), rz=math.radians(-90))
    put(src, "Mesh_SensorCupola_Dish", coll, "Mesh_SkyCity_Dish", bag,
        (0.0, 44.0, GANTRY_Z), k=2.4)
    put(src, "Mesh_SensorCupola_Dome", coll, "Mesh_SkyCity_Dome", bag,
        (0.0, 36.0, GANTRY_Z), k=1.1)
    put(src, "Mesh_SensorCupola_Lantern", coll, "Mesh_SkyCity_Beacon", bag,
        (0.0, -38.0, GANTRY_Z), k=1.0)
    # The gangway: the one span that crosses the keel, over the aft apron where
    # the deck is otherwise empty. Landing here is the only reason to cross.
    put(src, "Mesh_Catwalk_Bridge", coll, "Mesh_SkyCity_Gangway", bag,
        (0.0, 44.0, DECK + 3.4))


# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# Clearance invariant
#
# Nothing may be inside a gas bag. This is the failure the model is most prone
# to - the bags are the biggest things aboard, they are smooth so nothing visibly
# "lands" on them, and a ladder or a flagpole a metre inside one looks fine in
# every view that does not happen to graze it.
#
# Three cheaper checks were tried and all three lie here:
#   - AABB overlap calls a lamp under a bag's flank a clash; an ovoid fills
#     about half its own bounding box.
#   - `closest_point_on_mesh` plus normal sign reported a catwalk at y = -9
#     inside a bag spanning y -40..-15: the end collars are concave, so a point
#     beyond the cap finds a face it is technically behind.
#   - Ray-crossing parity fails on `Banded`, whose sixteen straps are separate
#     closed shells sitting 20 mm off the skin, so crossings pair up wrongly.
#     It gets the bag's own centre wrong, which is how that one was caught.
#
# What does hold is the analytic form, because these bags are ours: the profile
# below is the same expression `gas_envelope.stations` lofts. Keep the two in
# step - if the component's profile changes, this changes with it.
# ---------------------------------------------------------------------------

ENV_TIP, ENV_POWER = 0.17, 0.40     # must match components/structural/gas_envelope
ENV_PROUD = 0.16                    # ribs and straps standing off the skin
# (centre y, length, radius, squash, sag) in placement order.
BAG_SPEC = [(BAG_Y[0], 23.0, 7.0, 0.88, 0.45),
            (BAG_Y[1], 26.0, 7.6, 1.00, 0.00),
            (BAG_Y[2], 24.0, 7.2, 1.00, 0.00)]


def in_bag(p, margin=0.10):
    """True if `p` is inside any gas bag, or within `margin` of its skin."""
    for yc, length, radius, squash, sag in BAG_SPEC:
        t = (p.y - (yc - length / 2.0)) / length
        if not 0.0 <= t <= 1.0:
            continue
        r = radius * max(ENV_TIP, math.sin(math.pi * t) ** ENV_POWER)
        r += ENV_PROUD + margin
        dz = p.z - CAGE_Z + sag * math.sin(math.pi * t)
        if (p.x / r) ** 2 + (dz / (r * squash)) ** 2 < 1.0:
            return True
    return False


def assert_bags_clear(skip=("GasEnvelope", "Cage", "Cradles")):
    """Fail the build if any placement has a vertex inside a bag."""
    bad = []
    for o in bpy.data.objects:
        if o.type != 'MESH' or any(s in o.name for s in skip):
            continue
        m = o.matrix_world
        n = sum(1 for v in o.data.vertices if in_bag(m @ v.co))
        if n:
            bad.append((n, o.name))
    if bad:
        bad.sort(reverse=True)
        raise SystemExit(
            "Inside a gas bag: "
            + "; ".join("%s (%d verts)" % (n, c) for c, n in bad[:10]))
    print("  bag clearance: OK")


def assert_no_mirrors():
    """Fail the build if any object carries a negative-determinant transform.

    The prefab marks these renderers static, and a mirror that survives to Unity
    renders inside-out once a batcher bakes the transform away. Nothing in
    Blender shows it - the viewport compensates - so it is asserted here.
    """
    bad = [o.name for o in bpy.data.objects
           if o.type == 'MESH' and o.matrix_world.to_3x3().determinant() < 0]
    if bad:
        raise SystemExit("Negative-determinant (mirrored) objects: %s" % bad)
    print("  no mirrored transforms: OK")


def dedupe_materials():
    """Fold appended material copies back onto the palette originals.

    Two distinct collisions happen here and only one of them is the familiar
    one. Appending a component file can land `Mat_X.001` beside a local `Mat_X`
    - that is the `.001` fold below, and it is what every other generator in the
    library guards against.

    The other is invisible to that check. **A local and a linked datablock can
    share a name**, because `bpy.data.materials` is keyed on name *and* library.
    `append` makes everything local, so a kit part arrives carrying a local
    `Mat_Metal_Steel_Worn` that sits happily beside the linked one with no
    suffix on either, and `bpy.data.materials.get(name)` picks whichever it
    likes. The model then ships materials that the palette does not reach -
    edit the palette and they do not change. Remap local onto linked by name.
    """
    # A kit part's own materials are mostly NOT in this model's `MATS`, so there
    # is no linked twin waiting for them - `Mat_Metal_Brass_Tarnished` arrives
    # with the lantern and the ornithopter brass and belongs to neither list.
    # Link every local name the palette actually holds, then remap onto it.
    local = sorted({m.name for m in bpy.data.materials if m.library is None})
    if local:
        with bpy.data.libraries.load(bl.PALETTE, link=True) as (src, dst):
            dst.materials = [n for n in local if n in set(src.materials)]
    linked = {m.name: m for m in bpy.data.materials if m.library is not None}
    for m in list(bpy.data.materials):
        if m.library is None and m.name in linked:
            m.user_remap(linked[m.name])
            bpy.data.materials.remove(m)
    for m in list(bpy.data.materials):
        if len(m.name) > 4 and m.name[-4] == '.' and m.name[-3:].isdigit():
            base = bpy.data.materials.get(m.name[:-4])
            if base is not None and base is not m:
                m.user_remap(base)
                bpy.data.materials.remove(m)
    for m in list(bpy.data.materials):
        if m.users == 0 or (m.users == 1 and m.use_fake_user):
            bpy.data.materials.remove(m)


def build():
    out = bl.parse_out()
    bl.start(out)

    # Link the palette FIRST. `link_materials` skips any name already present in
    # `bpy.data.materials`, and appending a kit part brings LOCAL copies of its
    # materials under their exact names - so appending first leaves the model
    # holding a local `Mat_Metal_Steel_Worn` that the palette never reaches, and
    # `link_materials` hands it back without complaint. Linking first means the
    # appended copies arrive suffixed and `dedupe_materials` folds them away.
    bl.link_materials(MATS)

    hidden = bl.collection("Coll_PartSource")
    names = []
    for blend, parts in KIT.items():
        bl.append_objects(os.path.join(bl.LIB_ROOT, blend), parts, hidden)
        names += parts
    dedupe_materials()
    # Re-fetch after the fold. `dedupe_materials` drops anything with no users,
    # and at this point nothing has been built yet, so the palette links made
    # above may have been purged - holding the old list gives
    # "ReferenceError: StructRNA of type Material has been removed" at the first
    # `finish()`. Calling it again re-links whatever went missing.
    mats = bl.link_materials(MATS)
    src = {n: bpy.data.objects[n] for n in names}
    structure = bl.collection("Coll_SkyCity_Structure")
    town = bl.collection("Coll_SkyCity_Town")
    lift = bl.collection("Coll_SkyCity_Lift")
    rig = bl.collection("Coll_SkyCity_Rig")

    for fn in (keel, cage, decks, cradles, prow, stern_gear, gantry, climbs):
        fn(structure, mats)
    outriggers(rig, mats)

    bag = []
    place_envelopes(src, lift, bag)
    place_sails(src, rig, bag, hidden)
    place_town(src, town, bag)
    place_fittings(src, town, bag)

    for o in list(hidden.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.collections.remove(hidden)
    dedupe_materials()

    dupes = [o.name for o in bpy.data.objects
             if len(o.name) > 4 and o.name[-4] == '.' and o.name[-3:].isdigit()]
    if dupes:
        raise SystemExit("Auto-suffixed names reached save: %s" % dupes[:8])

    assert_bags_clear()
    assert_no_mirrors()
    lo, hi = bl.bbox([o for o in bpy.data.objects if o.type == 'MESH'])
    bl.report()
    print("  ENVELOPE: %.1f x %.1f x %.1f m  (objects: %d)"
          % (hi.x - lo.x, hi.y - lo.y, hi.z - lo.z,
             len([o for o in bpy.data.objects if o.type == 'MESH'])))
    bl.save(out)


build()
