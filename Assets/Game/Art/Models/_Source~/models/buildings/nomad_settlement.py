"""Rule-based building generator for the nomad settlement kit.

Builds 20 distinct buildings into one .blend by stacking procedurally generated
drums and dressing them with parts appended from
`components/nomad_settlement/components_clean.blend`.

Random, but every roll is bounded by a rule, and the rules are what make twenty
buildings read as one town instead of twenty unrelated props:

  R1  Taper is a constant 6.4 deg half-angle on every coned drum in the kit.
  R2  A drum never tapers below MIN_TOP_D. If the taper would breach it, the
      drum goes straight rather than finish on some shallower angle.
  R3  Every joint is an overlap (z) plus a radial step, never a coplanar butt.
  R4  Openings are capped both proportionally and absolutely. See OPENING_RULES.
  R5  A frame embeds into the wall by a quarter of its depth; it never sits flush.
  R6  An opening is leaned to the slope of the wall it sits on.
  R7  Openings snap to a 12-sector grid; the door owns sector 0 and blocks its
      neighbours; storeys alternate by half a sector so windows do not column up.
  R8  No opening within EDGE_MARGIN of a storey seam.
  R9  A ring marks a floor seam. One ring is the default, stacking the rare
      exception, and a heavy band never lands on two seams in a row.
  R10 Silhouette spread is allocated, not rolled - archetypes, storey counts,
      crowns and window kinds are dealt from shuffled decks.
  R11 One SEED reproduces the whole settlement exactly.
  R12 The ground storey is always a straight block and always carries the door.
  R13 A fused building's annexes interpenetrate the main body by FUSE_BITE of
      the smaller radius, so they merge instead of kissing tangentially.
  R14 Pipe runs are generated against `wall_radius`, not copied, so every run
      hugs the wall it is on whatever that wall's diameter and taper.
  R15 A ring never crosses an opening. Rings are placed after the openings and
      tested against them; a blocked ring is dropped or demoted to a 90 deg arc
      on a blank stretch of the same wall.
  R16 Nothing is placed in front of a door. The door's sector and both its
      neighbours are barred to boxes, pilasters and pipe runs at every height.

    blender --background --python nomad_settlement.py -- --out <path.blend>
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
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import nomad_rect as rk  # noqa: E402
import nomad_palette as pal  # noqa: E402

SEED = 20260913

# Which colour scheme the town is painted in. Every colour in the kit is mapped
# to a role in `nomad_palette`, so this one name retunes all eight at once and
# the contrast ladder between them is checked before anything is painted.
SCHEME = "nomad"

# Three families in one settlement. The round set keeps the rules it always had;
# the rectangular set is the same discipline applied to the kit's block module;
# the hybrid set puts a round tower on a block plan, which is the shape the two
# kits were clearly meant to make together.
COUNT_ROUND = 20
COUNT_RECT = 12
COUNT_HYBRID = 8
COUNT = COUNT_ROUND + COUNT_RECT + COUNT_HYBRID

KIT = os.path.join(bl.LIB_ROOT, "components", "nomad_settlement",
                   "components_clean.blend")

# ---------------------------------------------------------------- constants
TAPER = math.radians(6.4)            # R1 - the kit's one wall angle
TAPER_PER_M = 2.0 * math.tan(TAPER)  # 0.2243 of diameter lost per metre
SIDES = 32                           # matches every stock drum
MIN_TOP_D = 0.70                     # R2
JOINT_SINK = 0.040                   # R3 - vertical overlap at a storey seam
JOINT_STEP = 0.008                   # R3 - upper drum seats this much inside
EDGE_MARGIN = 0.180                  # R8
EMBED = 0.25                         # R5 - fraction of frame depth in the wall
SECTORS = 12                         # R7
FUSE_BITE = 0.35                     # R13 - how deep an annex cuts into the main
# R16 - how far anything else must stay clear of the door, in radians. Two
# sectors: the door's own and its neighbour either side, plus a little.
DOOR_KEEPOUT = math.radians(65.0)
GRID_PITCH = 17.0
GRID_COLS = 8

# A crowned hat's brim is 1.66x its drum; past this it stops being a roof and
# starts being a parasol over the whole street.
CROWN_HAT_MAX_D = 3.00

# R4 - opening size caps. Proportional cap first, absolute cap second, tightest
# wins. These are what keep windows and doors from swallowing a wall.
OPENING_RULES = {
    "window": dict(max_w_frac=0.20, max_w_abs=0.40,
                   max_h_frac=0.32, max_h_abs=0.46, min_k=0.30),
    # A door is never skipped - a building without one reads as a silo. When the
    # caps would shrink it below min_k the scale is clamped up to min_k instead.
    "door":   dict(max_w_frac=0.30, max_w_abs=0.62,
                   max_h_frac=0.60, max_h_abs=1.05, min_k=0.50),
}

# A door may run up through the first storey seam, because the drums overlap
# there anyway; without this a short ground storey leaves the building doorless.
DOOR_SEAM_BUDGET = 0.45

# Opening catalogue. `lean` is the source's own tilt in the pre-rotated frame,
# measured off the file: 0 for an upright part, (rot_x - 90) for a disc facing
# -Y. `pre_z` rotates a part that was modelled facing +X round to face -Y.
OPENINGS = {
    "round_deep": dict(objs=["Mesh_WindowRoundDeep_Frame",
                             "Mesh_WindowRoundDeep_Glass"], pre_z=0.0, lean=-5.62),
    "round_flat": dict(objs=["Mesh_WindowRoundFlat_Frame",
                             "Mesh_WindowRoundFlat_Glass"], pre_z=0.0, lean=-4.92),
    "rect_large": dict(objs=["Mesh_WindowRectLarge_Frame",
                             "Mesh_WindowRectLarge_Glass"], pre_z=0.0, lean=0.0),
    "rect_small": dict(objs=["Mesh_WindowRectSmall_Frame",
                             "Mesh_WindowRectSmall_Glass"], pre_z=-89.8, lean=-6.78),
    # Measured, not assumed: the door group is 0.642 x 0.181 x 0.996 with its
    # leaf on the +Y side, so it already stands with its width on X and its
    # outer face on +Y. It needs no turn at all - the half turn it carried put
    # the leaf inside the wall and the frame's back to the street, which is
    # what "the doors face the wrong way" meant. What it must never carry is a
    # QUARTER turn: that lays the door flat along its wall and buries it.
    # A half turn leaves the door's bounding box exactly where it was, so this
    # changes which way it faces and nothing else.
    "door":       dict(objs=["Mesh_Door_Frame", "Mesh_Door_Leaf"],
                       pre_z=0.0, lean=0.0),
}

# The kit's smallest aperture, and the only one that still fits a wall a
# metre across once R4's proportional cap has had its say.
SMALL_WINDOW = "rect_small"

WINDOW_SETS = {
    "round": ["round_deep", "round_flat"],
    "rect":  ["rect_large", "rect_small"],
    "mixed": ["round_deep", "round_flat", "rect_large", "rect_small"],
}

# Radially symmetric trim, keyed by the diameter the part was built at so a
# scale factor can be derived from a target diameter, plus how far it stands
# proud of the wall.
RINGS = {
    "thin":  ("Mesh_TowerTrim_BandThin", 2.098, 0.14),
    "tall":  ("Mesh_TowerTrim_BandTall", 2.098, 0.14),
    "heavy": ("Mesh_TowerTrim_BandWide", 2.301, 0.35),
}

# R9 - what a seam gets. A ring marks a floor, so one ring is the default and
# stacking is the exception; banding every seam three deep turns the whole town
# into a stack of tyres and stops reading as storeys at all.
RING_STACKS = [
    (0.64, ["thin"]),
    (0.16, ["thin", "thin"]),
    (0.12, ["tall"]),
    (0.08, ["heavy"]),
]
RING_GAP = 0.075          # vertical spacing inside a ring stack

# Electrical boxes as a fraction of the drum diameter. The low end is small on
# purpose: a scatter of little boxes reads as wiring, one big one reads as a
# feature, and the town wants both.
GEAR_MIN_FRAC = 0.09
GEAR_MAX_FRAC = 0.26

# X3 - the wall-detail panels the user added. They are the point of this pass,
# so a wall carries several; these bound how big any one may get and how much
# of a face may end up covered.
PANEL_MAX_FRAC = 0.30       # of the face width
PANEL_MAX_ABS = 0.95        # metres
PANEL_MIN_K = 0.45
PANEL_CLEAR = 0.06          # gap between neighbouring panels on one face
PANEL_FACE_MARGIN = 0.30    # a panel is flat and small, so it may sit
                            # closer to a corner than a window may

TERRACE_CHANCE = 0.18       # X5 - deliberately rare
RECT_FACE_MARGIN = 0.46     # the corner bevel rounds 0.33 m off each
                            # vertical edge; anything nearer hangs off it

# The kit's quarter ring: 90 deg of arc, radius 1.049 about its own object
# origin, its mid-point at alpha 45 deg. Measured off the file, not assumed.
# It is the detail piece - it goes where a full ring would have to cross an
# opening, and part-way up a storey where a full ring would read as a floor
# line that is not there.
ARC = ("Mesh_TowerTrim_BandArcThird", 2.098, 0.14)
ARC_SPAN = math.radians(90.0)
ARC_MID = math.radians(45.0)
ARC_SEAM_CHANCE = 0.55    # when a full ring is blocked, try an arc instead
ARC_MID_CHANCE = 0.30     # a lone arc part-way up a tall storey
ARC_CLEARANCE = math.radians(6.0)   # margin past an opening's own edge

CROWN_PARTS = {
    "crowned": (["Mesh_RoofCap_Brim", "Mesh_RoofCap_Dome", "Mesh_RoofCap_BandLower",
                 "Mesh_RoofCap_BandUpper", "Mesh_RoofCap_BandBrim",
                 "Mesh_RoofCap_ColumnRing"], "Mesh_RoofCap_Dome", 1.955),
    "deck":    (["Mesh_RoofDeck_Ringwall", "Mesh_RoofVent_Housing",
                 "Mesh_RoofVent_FanWell", "Mesh_RoofVent_PortA", "Mesh_RoofVent_PortB",
                 "Mesh_RoofVent_Elbow", "Curve_RoofVent_CowlA", "Curve_RoofVent_CowlB",
                 "Mesh_RoofDeck_BlockLarge", "Mesh_RoofDeck_BlockMed",
                 "Mesh_RoofDeck_BlockSmall"], "Mesh_RoofDeck_Ringwall", 1.778),
    "opendeck": (["Mesh_RoofDeck_Ringwall", "Mesh_RoofDeck_BlockLarge",
                  "Mesh_RoofDeck_BlockMed"], "Mesh_RoofDeck_Ringwall", 1.778),
    "flat":    (["Mesh_TowerBody_RoofCap"], "Mesh_TowerBody_RoofCap", 1.431),
}

# Electrical boxes. All three are wall furniture and all three read at a glance.
GREEBLES = {
    "panel": ["Mesh_PanelBox_Body", "Mesh_PanelBox_PlateLarge",
              "Mesh_PanelBox_PlateWide", "Mesh_PanelBox_PlateSmall"],
    "meter": ["Mesh_MeterBox_Backplate", "Mesh_MeterBox_Face",
              "Mesh_MeterBox_VentUpper", "Mesh_MeterBox_VentLower",
              "Mesh_MeterBox_Lip", "Mesh_MeterBox_Stud",
              "Mesh_MeterBox_DialPlateUpper", "Mesh_MeterBox_DialPlateLower",
              "Mesh_MeterBox_DialSmall", "Mesh_MeterBox_DialLarge"],
    "loose": ["Mesh_MeterBoxLoose_VentUpper", "Mesh_MeterBoxLoose_VentLower",
              "Mesh_MeterBoxLoose_Lip", "Mesh_MeterBoxLoose_Stud",
              "Mesh_MeterBoxLoose_DialPlateUpper", "Mesh_MeterBoxLoose_DialPlateLower",
              "Mesh_MeterBoxLoose_DialSmall", "Mesh_MeterBoxLoose_DialLarge"],
}

# R14 - pipework. The kit's own run is a bezier that follows the original
# tower's cone at one fixed radius, so copying it onto any other drum leaves
# part of the run hanging in the air. The run is generated instead, sampled
# against `wall_radius`, and built as mesh rather than curve - which also means
# it survives the FBX exporter, whose object_types filter drops every CURVE.
# The gauge is the kit's own: bevel_depth 0.070 at object scale 0.4387.
PIPE_SRC_RADIUS = 0.0307
PIPE_CLAMP = ["Mesh_PipeWrap_ClampA_Upper", "Mesh_PipeWrap_CollarA_Upper"]
PIPE_SEGS = 10
PIPE_STANDOFF = 0.55        # fraction of pipe radius left outside the wall

MASTS = ["Mesh_AntennaMast_Tall", "Mesh_AntennaMast_Thin",
         "Mesh_AntennaMast_Bulky", "Mesh_AntennaMast_Offset"]
DISH = ["Mesh_AntennaDish_Reflector", "Mesh_AntennaDish_Mast"]
STACKS = ["Mesh_TowerBody_StackWide", "Mesh_TowerBody_StackNarrow"]
FOOTPAD = "Mesh_TowerTrim_FootPad"
DOORSTEP = "Mesh_TowerTrim_DoorStep"
PILASTER = ("Mesh_TowerTrim_Pilaster", 1.228)

SOURCES = sorted({n for o in OPENINGS.values() for n in o["objs"]} |
                 {v[0] for v in RINGS.values()} |
                 {n for v in CROWN_PARTS.values() for n in v[0]} |
                 {n for v in GREEBLES.values() for n in v} |
                 set(MASTS) | set(DISH) | set(STACKS) | set(PIPE_CLAMP) |
                 {FOOTPAD, DOORSTEP, PILASTER[0], ARC[0]}) + rk.RECT_SRC

# Every loose detail part in the kit, grouped into panels at load time by
# nomad_rect.discover_details rather than by the kit's collection names, which
# do not separate them.
DETAIL_PREFIXES = ("Mesh_MeterBox", "Mesh_PanelBox")


# ------------------------------------------------------------------ helpers

# These four are shared with the tent generator and live in _buildlib; the local
# names are kept so every call site below reads the same as it always did.
bbox = bl.bbox
group_size = bl.group_size
delta_for = bl.place_delta
stamp = bl.stamp

def wall_radius(drum, z):
    t = (z - drum["z0"]) / drum["h"]
    return 0.5 * (drum["d0"] + max(0.0, min(1.0, t)) * (drum["d1"] - drum["d0"]))


def wall_slope(drum):
    """Half-angle of the drum wall, radians. Zero for a straight drum."""
    return math.atan2(0.5 * (drum["d0"] - drum["d1"]), drum["h"])






def on_wall(unit, drum, alpha, zc, depth_k):
    """World point for something mounted flush on the wall at angle `alpha`."""
    r = wall_radius(drum, zc) + EMBED * depth_k          # R5
    return Vector((unit["ox"] + r * math.sin(alpha),
                   unit["oy"] - r * math.cos(alpha), zc))


def opening_w(size):
    """An opening's aperture width: its larger horizontal extent."""
    return max(size.x, size.y)


def opening_d(size):
    """An opening's depth into the wall: its smaller horizontal extent."""
    return min(size.x, size.y)


def opening_scale(size, kind, wall_d, budget_h, force=False):
    """R4 - the tightest of the proportional and absolute caps, never above 1.

    Returns None when the caps would shrink the part below legibility, unless
    `force`, in which case it clamps up to the floor instead of dropping it.
    """
    r = OPENING_RULES[kind]
    # An opening's width is its larger horizontal extent and its depth the
    # smaller, for every frame in the kit. Reading width off a fixed axis meant
    # reading the door's 0.18 m thickness instead of its 0.64 m width, so the
    # width cap never bound on a door at all.
    k = min(1.0,
            min(r["max_w_frac"] * wall_d, r["max_w_abs"]) / opening_w(size),
            min(r["max_h_frac"] * budget_h, r["max_h_abs"]) / size.z)
    if k >= r["min_k"]:
        return k
    return r["min_k"] if force else None


def cone(name, d0, d1, h, mat, coll, bag):
    """A drum: 32 sides, n-gon caps, flat shaded, origin at its base centre."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=SIDES,
                          radius1=d0 * 0.5, radius2=d1 * 0.5, depth=h,
                          matrix=Matrix.Translation(Vector((0, 0, h * 0.5))))
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    o.data.materials.append(mat)
    coll.objects.link(o)
    bag.append(o)
    return o


# ----------------------------------------------------------------- the deal
def roll_settlement(rng, n):
    """R10 - deal the silhouette axes from shuffled decks rather than rolling
    each building independently, so the set is guaranteed to differ - and so it
    is guaranteed to contain the extremes rather than regressing to a mean."""
    kinds = (["tall"] * 3 + ["large"] * 3 + ["normal"] * (n - 6))[:n]
    crowns = (["flat", "crowned", "deck", "opendeck"] * ((n + 3) // 4))[:n]
    windows = (["round", "rect", "mixed"] * ((n + 2) // 3))[:n]
    # A lot of the town is fused: most buildings carry at least one annex.
    fuse = ([0] * 6 + [1] * 5 + [2] * 5 + [3] * 4)[:n]
    rng.shuffle(kinds)
    rng.shuffle(crowns)
    rng.shuffle(windows)
    rng.shuffle(fuse)

    plain = ([1] * 3 + [2] * 4 + [3] * 4 + [4] * 3)
    rng.shuffle(plain)
    specs = []
    for i in range(n):
        kind = kinds[i]
        if kind == "tall":
            # Height alone does not read as tall - slenderness does. A narrow
            # base is what turns eight storeys into a tower instead of a silo.
            storeys = rng.randint(8, 11)
            base_d = rng.uniform(1.50, 2.20)
        elif kind == "large":
            storeys = rng.randint(2, 4)
            base_d = rng.uniform(4.50, 7.00)
        else:
            storeys = plain.pop()
            base_d = rng.uniform(1.40, 3.00)
        lo_h = 1.25 if storeys == 1 else 0.90
        annexes = max(fuse[i], 3 if kind == "large" else 0)
        specs.append(dict(
            idx=i + 1, kind=kind, storeys=storeys, base_d=base_d,
            crown=crowns[i], windows=windows[i], annexes=annexes,
            # R12 - the ground storey is a plain straight block carrying the door
            storey_h=[rng.uniform(lo_h, 1.60) for _ in range(storeys)],
            taper=[False] + [rng.random() < 0.72 for _ in range(storeys - 1)],
            skirt=rng.random() < 0.45,
            pilasters=rng.choice([0, 0, 3, 4, 6]),
            greebles=rng.choice([5, 6, 7, 7, 8, 9]),
            pipes=rng.choice([1, 2, 2, 3, 3]),
            masts=rng.choice([1, 2, 2, 3, 3, 4]),
            dish=rng.random() < 0.55,
            stacks=rng.choice([0, 1, 1, 2]),
            seed=rng.randrange(1 << 30),
            family="round",
            plan=None, block_k=1.0, foundation=False,
            terrace=False, panels=0,
        ))
    for s in specs:
        if s["crown"] == "crowned" and s["storeys"] < 2:
            s["crown"] = rng.choice(["flat", "deck", "opendeck"])
    # R10 - no two neighbours in the layout share both storey count and crown.
    for i in range(1, n):
        if (specs[i]["storeys"], specs[i]["crown"]) == \
           (specs[i - 1]["storeys"], specs[i - 1]["crown"]):
            specs[i]["crown"] = rng.choice(
                [c for c in CROWN_PARTS if c != specs[i - 1]["crown"]
                 and not (c == "crowned" and specs[i]["storeys"] < 2)])
    # Panels go on every family; a round wall takes fewer because it curves away.
    for sp in specs:
        sp["panels"] = rng.choice([7, 8, 9, 10, 11])
    return specs


def roll_rect(rng, n, family, start_idx):
    """The rectangular and hybrid deals. Same discipline as the round one:
    plan, storey count and window family are dealt, not rolled."""
    plans = [rk.PLANS[i % len(rk.PLANS)] for i in range(n)]
    windows = (["round", "rect", "mixed"] * ((n + 2) // 3))[:n]
    rng.shuffle(plans)
    rng.shuffle(windows)
    specs = []
    for i in range(n):
        plan = plans[i]
        specs.append(dict(
            idx=start_idx + i + 1, family=family, kind=family,
            # Mostly one and two storeys. A settlement is huts with a few
            # towers in it, not a skyline; the tall roll is deliberately rare.
            plan=plan,
            storeys=min(len(plan[1]),
                        rng.choice([1, 1, 1, 1, 2, 2, 2, 2, 2, 3])),
            block_k=rng.uniform(0.72, 1.10),
            windows=windows[i],
            foundation=rng.random() < 0.75,
            terrace=rng.random() < TERRACE_CHANCE,
            panels=rng.choice([10, 12, 13, 14, 16, 18]),
            crown=rng.choice(["flat", "deck", "opendeck"]),
            masts=rng.choice([1, 2, 2, 3, 4]),
            dish=rng.random() < 0.60,
            stacks=rng.choice([0, 1, 1, 2]),
            base_d=0.0, annexes=0, greebles=0, pipes=rng.choice([1, 2, 2]),
            skirt=False, pilasters=0,
            storey_h=[], taper=[],
            seed=rng.randrange(1 << 30),
        ))
        # The hybrid grows a round tower out of the block plan.
        if family == "hybrid":
            specs[-1]["storeys"] = min(len(plan[1]), rng.choice([1, 1, 2, 2]))
            specs[-1]["tower_d"] = rng.uniform(1.25, 1.85)
            specs[-1]["tower_storeys"] = rng.randint(2, 4)
            specs[-1]["storey_h"] = [rng.uniform(0.95, 1.45)
                                     for _ in range(specs[-1]["tower_storeys"])]
            specs[-1]["taper"] = [False] + [rng.random() < 0.7
                                            for _ in range(
                                                specs[-1]["tower_storeys"] - 1)]
    return specs


def build_stack(base_d, heights, tapers, base_z):
    """Resolve a drum stack: diameters, heights and seams. Rules R1-R3."""
    drums = []
    z, d = base_z, base_d
    for h, want_taper in zip(heights, tapers):
        # R1 before R2: a drum is either on the kit's 6.4 deg angle or straight.
        # Clamping a cone at MIN_TOP_D instead would leave it on some shallower
        # angle, and one wall at 2.8 deg in a town of 6.4 deg walls is the thing
        # the eye picks out.
        want = d - TAPER_PER_M * h
        d1 = want if (want_taper and want >= MIN_TOP_D) else d
        drums.append(dict(d0=d, d1=d1, h=h, z0=z, z1=z + h))
        z = z + h - JOINT_SINK                      # R3
        # R3 - the next drum steps inside this one, except at the diameter
        # floor, where stepping in would clamp both to the same radius and
        # leave two coincident walls. There it sleeves over instead.
        d = (d1 - JOINT_STEP if d1 - JOINT_STEP >= MIN_TOP_D
             else d1 + JOINT_STEP)
    return drums


def crown_for(spec, top_d, rng, is_annex):
    """A hat wider than the street is not a roof; fall back when it would be."""
    c = "flat" if is_annex and rng.random() < 0.5 else spec["crown"]
    if c == "crowned" and top_d > CROWN_HAT_MAX_D:
        c = rng.choice(["deck", "opendeck", "flat"])
    return c


# ------------------------------------------------------------------- units
def band_clear(unit, z0, z1, pad=0.025):
    """R15 - true when the height band [z0, z1] crosses no opening at all."""
    return not any(z1 + pad > o0 and z0 - pad < o1
                   for (o0, o1, _a, _w) in unit["openings"])


def arc_slots(unit, z0, z1, pad=0.025):
    """Headings where a 90 deg arc at this height clears every opening.

    Worked in angles, not sector indices: odd storeys are offset half a sector
    by R7, so an opening's index does not say where it actually is. The arc
    reaches ARC_SPAN/2 either side of its centre; add half a sector for the
    width of the opening itself.
    """
    busy = [(a, w) for (o0, o1, a, w) in unit["openings"]
            if z1 + pad > o0 and z0 - pad < o1]
    out = []
    for n in range(SECTORS):
        alpha = n * 2 * math.pi / SECTORS
        if not off_door(unit, alpha):
            continue
        if any(angle_gap(alpha, a) < ARC_SPAN * 0.5 + w + ARC_CLEARANCE
               for (a, w) in busy):
            continue
        out.append(n)
    return out


def stamp_arc(unit, drum, src, zc, coll, name, bag, rng):
    """A quarter ring aimed at a stretch of blank wall. Returns False if there
    is no blank stretch wide enough."""
    s = src[ARC[0]]
    h = group_size([s]).z
    k = (2 * wall_radius(drum, zc) + ARC[2]) / ARC[1]
    slots = arc_slots(unit, zc - h * k * 0.5, zc + h * k * 0.5)
    if not slots:
        return False
    alpha = rng.choice(slots) * 2 * math.pi / SECTORS
    t = Vector((unit["ox"], unit["oy"], zc - h * k * 0.5))
    stamp([s], delta_for([s], t, k, alpha - ARC_MID, 0.0, 0.0,
                         anchor="origin", pivot=s),
          coll, name, bag)
    return True


def place_rings(unit, src, coll, prefix, bag, rng):
    """R9/R15 - one ring per floor seam, and it never crosses an opening.

    Runs after the openings are placed, so a ring that would cut through a
    window or a door is either dropped or demoted to a quarter arc on a blank
    stretch of the same wall.
    """
    drums = unit["drums"]
    prev_heavy = False
    for j, drum in enumerate(drums):
        roll, acc = rng.random(), 0.0
        stack = RING_STACKS[-1][1]
        for p, s in RING_STACKS:
            acc += p
            if roll <= acc:
                stack = s
                break
        if "heavy" in stack and prev_heavy:
            stack = ["thin"]
        prev_heavy = "heavy" in stack

        z = drum["z1"] - JOINT_SINK * 0.5
        for r_i, style in enumerate(stack):
            name, src_d, over = RINGS[style]
            s = src[name]
            h = group_size([s]).z
            k = (2 * wall_radius(drum, min(z, drum["z1"])) + over) / src_d
            lo, hi = z - h * k, z
            if not band_clear(unit, lo, hi):                          # R15
                if r_i == 0 and rng.random() < ARC_SEAM_CHANCE:
                    stamp_arc(unit, drum, src, z - h * k * 0.5, coll,
                              "%s_Arc%dS" % (prefix, j), bag, rng)
                break
            t = Vector((unit["ox"], unit["oy"], lo))
            stamp([s], delta_for([s], t, k, 0.0, 0.0, 0.0, anchor="base"),
                  coll, "%s_Ring%d%d" % (prefix, j, r_i), bag)
            z -= h * k + RING_GAP

        # Part-way up a storey a full ring would read as a floor line that is
        # not there, so the detail piece there is always an arc.
        if drum["h"] > 1.10 and rng.random() < ARC_MID_CHANCE:
            stamp_arc(unit, drum, src,
                      drum["z0"] + drum["h"] * rng.uniform(0.35, 0.62),
                      coll, "%s_Arc%dM" % (prefix, j), bag, rng)


def place_openings(unit, spec, src, coll, prefix, bag, rng, with_door):
    """R4-R8, R12. The door lives on the ground block, at the unit's facing."""
    kinds = WINDOW_SETS[spec["windows"]]
    drums = unit["drums"]
    ground = drums[0]
    door_face = unit["facing"]
    blocked = set(unit["blocked"])

    if with_door:
        o = OPENINGS["door"]
        srcs = [src[n] for n in o["objs"]]
        size = group_size(srcs, o["pre_z"])
        budget = ground["h"] + (DOOR_SEAM_BUDGET * drums[1]["h"]
                                if len(drums) > 1 else 0.0)
        k = opening_scale(size, "door", ground["d0"], budget, force=True)
        h = size.z * k
        zc = ground["z0"] + 0.015 + h * 0.5
        rx = math.radians(-math.degrees(wall_slope(ground)) - o["lean"])   # R6
        sector = int(round(door_face / (2 * math.pi / SECTORS))) % SECTORS
        alpha = sector * 2 * math.pi / SECTORS
        made = stamp(srcs, delta_for(srcs, on_wall(unit, ground, alpha, zc,
                                                   opening_d(size) * k),
                                     k, alpha, rx, o["pre_z"]),
                     coll, prefix + "_Door", bag)
        dlo, dhi = bbox(made)
        blocked |= {(sector - 1) % SECTORS, sector, (sector + 1) % SECTORS}  # R7
        unit["used"].add(sector)
        # R16 - nothing else may sit in front of a door: not a box, not a
        # pilaster, not a pipe. The door owns its sector and both neighbours
        # for the full height of the unit, because a box directly above a door
        # still reads as blocking it.
        unit["door_sectors"] |= {(sector - 1) % SECTORS, sector,
                                 (sector + 1) % SECTORS}
        unit["openings"].append((dlo.z, dhi.z, alpha,
                                 math.atan2(opening_w(size) * k * 0.5,
                                            wall_radius(ground, zc))))
        unit["door_z"] = (dlo.z, dhi.z)
        unit["door_alpha"] = alpha

        step = src[DOORSTEP]
        sk = k * (ground["d0"] / 1.955)
        r = wall_radius(ground, ground["z0"]) + 0.10
        st = Vector((unit["ox"] + r * math.sin(alpha),
                     unit["oy"] - r * math.cos(alpha), ground["z0"]))
        stamp([step], delta_for([step], st, sk, alpha, 0.0, 0.0, anchor="base"),
              coll, prefix + "_Step", bag)

    for j, drum in enumerate(drums):
        usable = drum["h"] - 2 * EDGE_MARGIN                    # R8
        if usable <= 0.25:
            continue
        slope = wall_slope(drum)
        offset = 0.0 if j % 2 == 0 else math.pi / SECTORS       # R7
        free = [n for n in range(SECTORS)
                if n not in (blocked if j == 0 else unit["blocked"])]
        rng.shuffle(free)
        # Window count follows the circumference. R4 caps a window at 0.55 m
        # however wide the wall, so a 6 m drum with the same two windows as a
        # 1.5 m hut reads as a blank silo rather than a building.
        want = min(len(free), rng.choice([1, 2, 2, 3, 3, 4])
                   + int(drum["d0"] / 1.30))
        for n in free[:want]:
            zc = drum["z0"] + EDGE_MARGIN + usable * rng.uniform(0.25, 0.75)
            wall_d = 2 * wall_radius(drum, zc)
            # Try the building's own window family first. High on a slender
            # tower the wall gets too narrow for a porthole and R4 drops it,
            # which leaves the top half of a ten-storey tower blank - so fall
            # back to the one genuinely small opening the kit has.
            k = None
            for kind in (rng.choice(kinds), SMALL_WINDOW):
                o = OPENINGS[kind]
                srcs = [src[x] for x in o["objs"]]
                size = group_size(srcs, o["pre_z"])
                k = opening_scale(size, "window", wall_d, drum["h"])
                if k and size.z * k <= usable:
                    break
                k = None
            if not k:
                continue
            alpha = offset + n * 2 * math.pi / SECTORS
            rx = math.radians(-math.degrees(slope) - o["lean"])  # R6
            made = stamp(srcs,
                         delta_for(srcs, on_wall(unit, drum, alpha, zc,
                                                 opening_d(size) * k),
                                   k, alpha, rx, o["pre_z"]),
                         coll, "%s_Win%d%02d" % (prefix, j, n), bag)
            unit["used"].add(n)
            wlo, whi = bbox(made)
            unit["openings"].append((wlo.z, whi.z, alpha,
                                     math.atan2(opening_w(size) * k * 0.5,
                                                wall_radius(drum, zc))))


def place_crown(unit, kind, src, coll, prefix, bag):
    names, pivot_name, src_d = CROWN_PARTS[kind]
    srcs = [src[n] for n in names]
    top = unit["drums"][-1]
    k = top["d1"] / src_d
    target = Vector((unit["ox"], unit["oy"], top["z1"] - JOINT_SINK))    # R3
    stamp(srcs, delta_for(srcs, target, k, 0.0, 0.0, 0.0,
                          anchor="axis", pivot=src[pivot_name]),
          coll, prefix + "_Crown", bag)
    return top["z1"] - JOINT_SINK + group_size(srcs).z * k


def place_furniture(unit, spec, src, coll, prefix, bag, rng, roof_z):
    top = unit["drums"][-1]
    r = top["d1"] * 0.5
    for i in range(spec["stacks"]):
        s = src[STACKS[i % len(STACKS)]]
        k = min(1.2, max(0.6, top["d1"] / 1.955))
        a = rng.uniform(0, 2 * math.pi)
        rad = r * rng.uniform(0.15, 0.55)
        t = Vector((unit["ox"] + rad * math.sin(a),
                    unit["oy"] - rad * math.cos(a), roof_z - 0.05))
        stamp([s], delta_for([s], t, k, a, 0.0, 0.0, anchor="base"),
              coll, "%s_Stack%d" % (prefix, i), bag)
    for i in range(spec["masts"]):
        s = src[MASTS[rng.randrange(len(MASTS))]]
        k = rng.uniform(0.55, 1.0) * min(1.6, max(0.6, top["d1"] / 1.4))
        a = rng.uniform(0, 2 * math.pi)
        rad = r * rng.uniform(0.10, 0.60)
        t = Vector((unit["ox"] + rad * math.sin(a),
                    unit["oy"] - rad * math.cos(a), roof_z - 0.05))
        stamp([s], delta_for([s], t, k, a, 0.0, 0.0, anchor="base"),
              coll, "%s_Mast%d" % (prefix, i), bag)
    for di in range(1 if not spec["dish"] else rng.choice([1, 1, 2])):
        if not spec["dish"]:
            break
        srcs = [src[n] for n in DISH]
        k = rng.uniform(0.5, 0.9) * min(1.6, max(0.7, top["d1"] / 1.6))
        a = rng.uniform(0, 2 * math.pi)
        rad = r * 0.45
        t = Vector((unit["ox"] + rad * math.sin(a),
                    unit["oy"] - rad * math.cos(a), roof_z - 0.05))
        stamp(srcs, delta_for(srcs, t, k, a, 0.0, 0.0, anchor="base"),
              coll, "%s_Dish%d" % (prefix, di), bag)


def place_wall_gear(unit, spec, src, coll, prefix, bag, rng, count):
    """Electrical boxes. Wall furniture, so the same R5/R6 wall fit as a window.

    R16 - never in the door's sectors, at any height. Boxes climb the whole
    building, not just the bottom two storeys, and their size range runs well
    below the window scale so a wall can carry several without reading busy.
    """
    drums = unit["drums"]
    free = [n for n in range(SECTORS)
            if off_door(unit, n * 2 * math.pi / SECTORS)]                   # R16
    if not free:
        return
    # X3 - draw from the discovered detail panels when the kit has them, so the
    # user's new wall details land on the round family as well as the boxy one.
    panels = src.get("__panels") or []
    for i in range(count):
        if panels and rng.random() < 0.75:
            p = panels[rng.randrange(len(panels))]
            srcs, size = p["objs"], Vector((p["w"], p["d"], p["h"]))
        else:
            kind = rng.choice(list(GREEBLES))
            srcs = [src[n] for n in GREEBLES[kind]]
            size = group_size(srcs)
        drum = drums[rng.randrange(len(drums))]
        k = min(1.2, rng.uniform(GEAR_MIN_FRAC, GEAR_MAX_FRAC)
                * drum["d0"] / size.x,
                (drum["h"] - 0.30) / size.z)
        if k < 0.35:
            continue
        zc = drum["z0"] + drum["h"] * rng.uniform(0.25, 0.78)
        a = (free[rng.randrange(len(free))] * 2 * math.pi / SECTORS
             + rng.uniform(-0.10, 0.10))
        if not off_door(unit, a):
            continue
        rx = math.radians(-math.degrees(wall_slope(drum)))
        stamp(srcs, delta_for(srcs, on_wall(unit, drum, a, zc, size.y * k),
                              k, a, rx, 0.0),
              coll, "%s_Gear%d" % (prefix, i), bag)


def drum_at(drums, z):
    for d in drums:
        if d["z0"] - 0.06 <= z <= d["z1"] + 0.06:
            return d
    return drums[0] if z < drums[0]["z0"] else drums[-1]


def pipe_tube(name, pts, rad, mat, coll, bag):
    """Sweep a circle along `pts`. One reference vector for the whole run, not
    one per sample: switching it mid-run rotates the ring's origin and puts a
    visible twist in the tube."""
    span = (pts[-1] - pts[0]).normalized()
    up = Vector((0, 0, 1)) if abs(span.z) < 0.85 else Vector((1, 0, 0))
    bm = bmesh.new()
    rings = []
    for i, p in enumerate(pts):
        if i == 0:
            t = pts[1] - pts[0]
        elif i == len(pts) - 1:
            t = pts[-1] - pts[-2]
        else:
            t = pts[i + 1] - pts[i - 1]
        t.normalize()
        n1 = t.cross(up)
        if n1.length < 1e-4:
            n1 = t.cross(Vector((0, 1, 0)))
        n1.normalize()
        n2 = t.cross(n1).normalized()
        rings.append([bm.verts.new(
            p + rad * (math.cos(k * 2 * math.pi / PIPE_SEGS) * n1
                       + math.sin(k * 2 * math.pi / PIPE_SEGS) * n2))
            for k in range(PIPE_SEGS)])
    for a, b in zip(rings, rings[1:]):
        for k in range(PIPE_SEGS):
            k2 = (k + 1) % PIPE_SEGS
            bm.faces.new((a[k], a[k2], b[k2], b[k]))
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


def place_pipes(unit, src, coll, prefix, bag, rng, count):
    """R14 - service runs sampled against the wall they climb."""
    drums = unit["drums"]
    base_z, top_z = drums[0]["z0"], drums[-1]["z1"]
    rad = min(0.055, max(0.024, PIPE_SRC_RADIUS * drums[0]["d0"] / 1.955))
    tube_mat = bpy.data.materials["Mat_Nomad_Metal_Charcoal"]
    clamps = [src[n] for n in PIPE_CLAMP]
    # The kit's bracket is deliberately heavy, but scaled straight off the pipe
    # gauge it swamps a thin run, so it is held back from the full ratio.
    ck = 0.75 * rad / PIPE_SRC_RADIUS
    csize = group_size(clamps)
    free = [n for n in range(SECTORS)
            if n not in unit["used"] and off_door(unit, n * 2 * math.pi / SECTORS)]
    free = free or [n for n in range(SECTORS)
                    if off_door(unit, n * 2 * math.pi / SECTORS)]            # R16
    if not free:
        return
    for i in range(count):
        riser = len(drums) > 1 and rng.random() < 0.6
        # Start in a sector no opening claimed, so a run does not climb over a
        # window. A wrap will still cross one - that is what a wrap does.
        a0 = (free[rng.randrange(len(free))] * 2 * math.pi / SECTORS
              + rng.uniform(-0.06, 0.06))
        if riser:
            z0 = base_z + rng.uniform(0.10, 0.40)
            z1 = min(top_z - 0.10, z0 + rng.uniform(1.30, (top_z - base_z) * 0.95))
            # A riser is a vertical service run. Any real sweep on it reads as a
            # cable drooping across the facade rather than pipework bolted to it.
            sweep = rng.uniform(-0.35, 0.35)
        else:
            d = drums[rng.randrange(len(drums))]
            # Hug the top or bottom of the storey; the middle belongs to windows.
            frac = rng.choice([rng.uniform(0.12, 0.28), rng.uniform(0.72, 0.88)])
            z0 = d["z0"] + d["h"] * frac
            z1 = z0 + rng.uniform(-0.14, 0.14)
            sweep = rng.choice([-1.0, 1.0]) * rng.uniform(1.40, 3.60)
        if z1 - z0 < 0.05 and abs(sweep) < 0.2:
            continue
        # R16 - a run may pass over or under a door, never across it. Tested on
        # the whole swept interval, not at sample points: a coarse sample can
        # step straight over the doorway and report it clear.
        dz = unit.get("door_z")
        if dz and min(z0, z1) - rad < dz[1] + 0.06 and \
                max(z0, z1) + rad > dz[0] - 0.06:
            n_probe = max(24, int(abs(sweep) / 0.03))
            if any(not off_door(unit, a0 + sweep * (q / n_probe))
                   for q in range(n_probe + 1)):
                continue
        steps = max(12, int(abs(sweep) / 0.10) + int(abs(z1 - z0) / 0.10))
        pts = []
        for s in range(steps + 1):
            t = s / steps
            a, z = a0 + sweep * t, z0 + (z1 - z0) * t
            r = wall_radius(drum_at(drums, z), z) + rad * PIPE_STANDOFF
            pts.append(Vector((unit["ox"] + r * math.sin(a),
                               unit["oy"] - r * math.cos(a), z)))
        pipe_tube("%s_Pipe%d" % (prefix, i), pts, rad, tube_mat, coll, bag)
        # A run is held on by brackets at both ends and one in the middle.
        for m, t in enumerate((0.0, 0.5, 1.0)):
            a, z = a0 + sweep * t, z0 + (z1 - z0) * t
            # R16 - the bracket is a solid block; it has to clear the doorway
            # on its own account, not just because its run's centreline did.
            if dz and not off_door(unit, a) and \
                    z + csize.z * ck * 0.5 > dz[0] - 0.05 and \
                    z - csize.z * ck * 0.5 < dz[1] + 0.05:
                continue
            d = drum_at(drums, z)
            rx = math.radians(-math.degrees(wall_slope(d)))
            stamp(clamps, delta_for(clamps, on_wall(unit, d, a, z, csize.y * ck),
                                    ck, a, rx, 0.0),
                  coll, "%s_PipeClamp%d%d" % (prefix, i, m), bag)


def place_skirt(unit, src, coll, prefix, bag):
    """The ground block's footing pads, in place of the old cast-foot plinth."""
    ground = unit["drums"][0]
    pad = src[FOOTPAD]
    k = 0.55 * ground["d0"] / 2.130
    for i in range(3):
        a = (i + 0.5) * 2 * math.pi / 3 + unit["facing"]
        r = ground["d0"] * 0.44
        t = Vector((unit["ox"] + r * math.sin(a),
                    unit["oy"] - r * math.cos(a), ground["z0"]))
        stamp([pad], delta_for([pad], t, k, a, 0.0, 0.0, anchor="base"),
              coll, "%s_Pad%d" % (prefix, i), bag)


def place_pilasters(unit, count, src, coll, prefix, bag):
    ground = unit["drums"][0]
    s = src[PILASTER[0]]
    k = ground["h"] / PILASTER[1]
    depth = group_size([s]).y * k
    for i in range(count):
        a = (i + 0.5) * 2 * math.pi / count
        # R16 - a pilaster landing across the doorway is worse than a gap in
        # the colonnade, so that one is simply left out.
        if not off_door(unit, a):
            continue
        # R5 again: a pilaster centred on the wall radius is buried by its own
        # thickness. Push it out so only its back edge is inside.
        r = wall_radius(ground, ground["z0"] + ground["h"] * 0.5) + depth * 0.30
        t = Vector((unit["ox"] + r * math.sin(a),
                    unit["oy"] - r * math.cos(a), ground["z0"]))
        stamp([s], delta_for([s], t, k, a, 0.0, 0.0, anchor="base"),
              coll, "%s_Pilaster%d" % (prefix, i), bag)


# ==========================================================================
# The rectangular family
# ==========================================================================

def rect_faces(cells, k, z0, z1):
    """Every block's outward faces, internal ones dropped (X7)."""
    return rk.cell_faces(cells, k, z0, z1)


def face_free(face, t, half, z0=None, z1=None, margin=None):
    """X3 - true when this patch of wall is still clear.

    Two things on one face clash only if they overlap BOTH sideways and in
    height. Checking sideways alone let one panel claim a whole face and the
    other fifteen were dropped, which is why a wall ended up almost bare.
    """
    if abs(t) + half > face["w"] * 0.5 - (RECT_FACE_MARGIN if margin is None
                                          else margin):
        return False
    for u in face["used"]:
        ut, uh = u[0], u[1]
        if abs(t - ut) >= half + uh + PANEL_CLEAR:
            continue
        if z0 is not None and len(u) > 2:
            if z0 >= u[3] + PANEL_CLEAR or z1 <= u[2] - PANEL_CLEAR:
                continue
        return False
    return True


def face_point(unit, face, t, z, out_offset):
    """World point on a face, `out_offset` proud of it."""
    tan = Vector((math.cos(face["alpha"]), math.sin(face["alpha"]), 0.0))
    p = (Vector((unit["ox"], unit["oy"], 0.0)) + face["c"]
         + face["n"] * out_offset + tan * t)
    p.z = z
    return p


def place_rect_openings(unit, spec, src, coll, prefix, bag, rng, faces, ground):
    """R4-R8 and R12 on a flat wall. The door goes on the front face."""
    kinds = WINDOW_SETS[spec["windows"]]
    for fi, face in enumerate(faces):
        drum_h = face["z1"] - face["z0"]
        usable = drum_h - 2 * EDGE_MARGIN
        if usable <= 0.25:
            continue
        # Fewer, larger-spaced openings: a wall of windows reads as an office
        # block, and these are mud huts.
        if rng.random() < 0.22:
            continue
        want = max(1, int(face["w"] / 2.30)) + rng.choice([0, 0, 0, 1])
        for _ in range(want):
            zc = face["z0"] + EDGE_MARGIN + usable * rng.uniform(0.25, 0.75)
            k = None
            for kind in (rng.choice(kinds), SMALL_WINDOW):
                o = OPENINGS[kind]
                srcs = [src[x] for x in o["objs"]]
                size = group_size(srcs, o["pre_z"])
                k = opening_scale(size, "window", face["w"], drum_h)
                if k and size.z * k <= usable:
                    break
                k = None
            if not k:
                continue
            half = opening_w(size) * k * 0.5
            t = rng.uniform(-1, 1) * (face["w"] * 0.5 - RECT_FACE_MARGIN - half)
            if not face_free(face, t, half):
                continue
            face["used"].append((t, half, zc - size.z * k * 0.5,
                                 zc + size.z * k * 0.5))
            unit["openings"].append((zc - size.z * k * 0.5, zc + size.z * k * 0.5,
                                     face["alpha"], math.radians(12)))
            tgt = face_point(unit, face, t, zc, EMBED * opening_d(size) * k)
            stamp(srcs, delta_for(srcs, tgt, k, face["alpha"],
                                  math.radians(-o["lean"]), o["pre_z"]),
                  coll, "%s_Win%d%02d" % (prefix, fi, len(face["used"])), bag)


def place_rect_door(unit, spec, src, coll, prefix, bag, rng, face, ground_h):
    o = OPENINGS["door"]
    srcs = [src[n] for n in o["objs"]]
    size = group_size(srcs, o["pre_z"])
    k = opening_scale(size, "door", face["w"], ground_h, force=True)
    h = size.z * k
    zc = face["z0"] + 0.015 + h * 0.5
    half = opening_w(size) * k * 0.5
    t = rng.uniform(-1, 1) * max(0.0, face["w"] * 0.5 - RECT_FACE_MARGIN - half)
    face["used"].append((t, half + 0.22, face["z0"], zc + h * 0.5))
    tgt = face_point(unit, face, t, zc, EMBED * opening_d(size) * k)
    made = stamp(srcs, delta_for(srcs, tgt, k, face["alpha"],
                                 math.radians(-o["lean"]), o["pre_z"]),
                 coll, prefix + "_Door", bag)
    dlo, dhi = bbox(made)
    unit["openings"].append((dlo.z, dhi.z, face["alpha"], math.radians(20)))
    unit["door_alpha"] = face["alpha"]
    unit["door_z"] = (dlo.z, dhi.z)
    unit["door_face"] = face
    unit["door_t"] = t
    unit["door_pos"] = (dlo + dhi) * 0.5
    unit["door_half"] = (dhi - dlo) * 0.5

    step = src[DOORSTEP]
    sk = k * 1.15
    st = face_point(unit, face, t, face["z0"], 0.16)
    stamp([step], delta_for([step], st, sk, face["alpha"], 0.0, 0.0,
                            anchor="foot"),
          coll, prefix + "_Step", bag)
    return k


def tgt_probe(unit, face, t, z):
    """Where a thing at (t, z) on this face lands in the world."""
    return face_point(unit, face, t, z, 0.0)


def place_panels(unit, src, coll, prefix, bag, rng, faces, count):
    """X3 - the wall details, in quantity, spread over every face."""
    panels = src["__panels"]
    if not panels:
        return
    placed = 0
    for _ in range(count * 8):
        if placed >= count:
            break
        face = faces[rng.randrange(len(faces))]
        p = panels[rng.randrange(len(panels))]
        # Height matters as much as width: the tallest panel is 1.05 m and a
        # storey is 1.22 m, so a panel scaled only to the face never fitted
        # between floor and ceiling and was silently dropped every time.
        room = (face["z1"] - face["z0"]) - 0.28
        k = min(1.25, rng.uniform(0.60, 1.0)
                * min(PANEL_MAX_FRAC * face["w"], PANEL_MAX_ABS) / p["w"],
                room / p["h"])
        if k < PANEL_MIN_K:
            continue
        half = p["w"] * k * 0.5
        h = p["h"] * k
        lo = face["z0"] + 0.14
        hi = face["z1"] - 0.14 - h
        if hi <= lo:
            continue
        zc = rng.uniform(lo, hi) + h * 0.5
        t = rng.uniform(-1, 1) * max(0.0,
                                     face["w"] * 0.5 - PANEL_FACE_MARGIN - half)
        if not face_free(face, t, half, zc - h * 0.5, zc + h * 0.5,
                         margin=PANEL_FACE_MARGIN):
            continue
        dp = unit.get("door_pos")
        if dp is not None:
            dh = unit["door_half"]
            if (abs(tgt_probe(unit, face, t, zc).x - dp.x) < dh.x + half + 0.30
                    and abs(tgt_probe(unit, face, t, zc).y - dp.y) < dh.y + 0.45
                    and abs(zc - dp.z) < dh.z + h * 0.5 + 0.20):
                continue
        face["used"].append((t, half, zc - h * 0.5, zc + h * 0.5))
        tgt = face_point(unit, face, t, zc, -p["d"] * k * 0.14)
        stamp(p["objs"], delta_for(p["objs"], tgt, k, face["alpha"], 0.0, 0.0),
              coll, "%s_Panel%d" % (prefix, placed), bag)
        placed += 1
    return placed


def place_rect_bands(unit, src, coll, prefix, bag, rng, cell, k, z):
    """X8 - every storey of every block gets its own ring.

    One band stretched over the plan's outer rectangle floated across the notch
    of an L; a ring belongs to the block it rings, and every storey gets one so
    the storeys read as storeys.
    """
    x0, x1, y0, y1 = rk.cell_rect(cell, k)
    wide = (x1 - x0) > 2.6 * k
    style = rng.choice(["thin", "thin", "thick", "wide"] if wide
                       else ["thin", "thin", "thick"])
    name, bw, bd, bh = rk.BANDS[style]
    sB = src[name]
    sx = ((x1 - x0) + 2 * BAND_PROUD) / bw
    sy = ((y1 - y0) + 2 * BAND_PROUD) / bd
    tgt = Vector((unit["ox"] + (x0 + x1) * 0.5, unit["oy"] + (y0 + y1) * 0.5,
                  z - bh * 0.5))
    stamp([sB], delta_for([sB], tgt, (sx, sy, 1.0), 0.0, 0.0, 0.0, anchor="base"),
          coll, "%s_Band" % prefix, bag)


BAND_PROUD = rk.BAND_PROUD

def build_rect_unit(spec, unit, src, coll, prefix, bag, rng, mats):
    """One boxy building: blocks, bands, roof, openings, details, arch, terrace."""
    sand, bone, trim = mats
    plan_name, storeys = spec["plan"]
    k = spec["block_k"]
    o = Vector((unit["ox"], unit["oy"], 0.0))
    n_st = min(len(storeys), spec["storeys"])
    faces_all = []
    roofs = []
    z = 0.0

    # X1 - the foundation is a slab, so it may take a per-axis scale.
    if spec["foundation"]:
        x0, x1, y0, y1 = rk.plan_footprint(storeys[0], k)
        fname, fw, fd, fh = rk.FOUNDATIONS["rect" if (x1 - x0) > (y1 - y0) * 1.2
                                           else "sq"]
        sF = src[fname]
        sx = ((x1 - x0) + 0.95) / fw
        sy = ((y1 - y0) + 0.95) / fd
        stamp([sF], delta_for([sF], o + Vector(((x0 + x1) * 0.5, (y0 + y1) * 0.5,
                                                0.0)),
                              (sx, sy, 0.85), 0.0, 0.0, 0.0, anchor="base"),
              coll, prefix + "_Foundation", bag)
        z = fh * 0.85 - rk.STOREY_BITE * 0.5

    for j in range(n_st):
        cells = storeys[j]
        h = rk.storey_height(cells, k)
        for ci, (kind, dx, dy) in enumerate(cells):
            bname = rk.BLOCKS[kind][0]
            sB = src[bname]
            stamp([sB], delta_for([sB], o + Vector((dx * k, dy * k, z)),
                                  k, 0.0, 0.0, 0.0, anchor="base"),
                  coll, "%s_Blk%d%d" % (prefix, j, ci), bag)
        tapered = any("taper" in c[0] for c in cells)
        faces = rect_faces(cells, k, z + 0.02, z + h - 0.02)
        if not tapered:
            faces_all.append((j, faces, cells, z, h))
        # X6 - the bevel eats 0.18 m off a block's top and bottom, so a storey
        # that only sinks JOINT_SINK leaves a visible crack at every seam.
        z += h - rk.STOREY_BITE
        # X8 - a ring round every block of every storey, including the top one,
        # so the storeys read as storeys rather than as one tall lump.
        for ci, cell in enumerate(cells):
            place_rect_bands(unit, src, coll, "%s_B%d%d" % (prefix, j, ci),
                             bag, rng, cell, k, z + rk.STOREY_BITE * 0.5)
        # X7 - anything with no block above it gets capped here, or an L-plan's
        # wing is left open and the next storey's roof is stretched over the
        # notch, hanging in the air.
        above = storeys[j + 1] if j + 1 < n_st else []
        for ci, cell in enumerate(cells):
            if rk.cell_covered(cell, above, k):
                continue
            roofs.append(("%s_R%d%d" % (prefix, j, ci), cell, z + rk.STOREY_BITE))

    # --- the door, on the front face of the lowest untapered storey ---------
    ground_j, ground_faces, ground_cells, gz, gh = faces_all[0]
    door_face = ground_faces[0]
    place_rect_door(unit, spec, src, coll, prefix, bag, rng, door_face, gh)

    for (j, faces, cells, bz, bh) in faces_all:
        place_rect_openings(unit, spec, src, coll, "%s_S%d" % (prefix, j),
                            bag, rng, faces, cells)

    # --- X5 the terrace, and only where a door opens onto it ----------------
    if spec["terrace"] and len(faces_all) > 1:
        j, faces, cells, bz, bh = faces_all[1]
        face = faces[rng.randrange(len(faces))]
        tname, tw, td, th = rk.TERRACES[rng.choice(["small", "large"])]
        sT = src[tname]
        tk = min(1.35, (face["w"] * 0.72) / tw)
        # X5 - recessed: most of its depth is inside the building, so it reads
        # as a cut-in balcony rather than a shelf bolted to the wall.
        tgt = face_point(unit, face, 0.0, bz - 0.02, -td * tk * 0.10)
        stamp([sT], delta_for([sT], tgt, tk, face["alpha"], 0.0, 0.0,
                              anchor="base"),
              coll, prefix + "_Terrace", bag)
        od = OPENINGS["door"]
        dsrcs = [src[x] for x in od["objs"]]
        dsize = group_size(dsrcs, od["pre_z"])
        dk = opening_scale(dsize, "door", face["w"], bh, force=True)
        dz = bz + 0.03 + dsize.z * dk * 0.5
        face["used"].append((0.0, opening_w(dsize) * dk * 0.5 + 0.20))
        stamp(dsrcs, delta_for(dsrcs, face_point(unit, face, 0.0, dz,
                                                 EMBED * opening_d(dsize) * dk),
                               dk, face["alpha"], 0.0, od["pre_z"]),
              coll, prefix + "_TerraceDoor", bag)

    # --- X3 the wall details, in quantity -----------------------------------
    flat = [f for (_j, fs, _c, _z, _h) in faces_all for f in fs]
    place_panels(unit, src, coll, prefix, bag, rng, flat, spec["panels"])

    # --- the roofs, one per block that nothing stands on -------------------
    top_h = 0.0
    for name, cell, rz in roofs:
        x0, x1, y0, y1 = rk.cell_rect(cell, k)
        rname, rw, rd, rh = rk.ROOFS["long" if (x1 - x0) > (y1 - y0) * 1.25
                                     else ("small" if (x1 - x0) < 2.0 * k
                                           else "sq")]
        sR = src[rname]
        stamp([sR], delta_for([sR],
                              o + Vector(((x0 + x1) * 0.5, (y0 + y1) * 0.5,
                                          rz - SINK_R)),
                              ((x1 - x0) / rw, (y1 - y0) / rd, 1.0),
                              0.0, 0.0, 0.0, anchor="base"),
              coll, name, bag)
        top_h = max(top_h, rz - SINK_R + rh)
    return max(top_h, z + rk.STOREY_BITE)


SINK_R = 0.03

def sector_of(alpha):
    """The sector index a world angle falls in."""
    return int(round(alpha / (2 * math.pi / SECTORS))) % SECTORS


def angle_gap(a, b):
    """Smallest absolute angle between two world headings, radians."""
    return abs((a - b + math.pi) % (2 * math.pi) - math.pi)


def off_door(unit, alpha):
    """R16 - true when heading `alpha` clears the doorway by DOOR_KEEPOUT.

    Tested as an angle rather than a sector index: a sector index is rounded,
    so a part a degree off a boundary passes the index test and still ends up
    across the door.
    """
    da = unit.get("door_alpha")
    return da is None or angle_gap(alpha, da) >= DOOR_KEEPOUT


def sector_span(alpha, half):
    """The sector indices within `half` radians of world angle `alpha`."""
    out = set()
    for n in range(SECTORS):
        d = abs((n * 2 * math.pi / SECTORS - alpha + math.pi) % (2 * math.pi) - math.pi)
        if d <= half:
            out.add(n)
    return out


def new_unit(ox, oy, facing=0.0, blocked=None):
    return dict(ox=ox, oy=oy, facing=facing, blocked=blocked or set(),
                used=set(), door_sectors=set(), openings=[], drums=[])


def build_rect_building(spec, src, root, rng):
    """X1-X5. A boxy building, and for the hybrid family a round tower on top
    of its own block plan - which is the shape the two kits were made to make."""
    coll = bl.collection("Coll_NomadBuilding_%02d" % spec["idx"], root)
    prefix = "B%02d" % spec["idx"]
    bag = []
    mats = (bpy.data.materials["Mat_Nomad_Clay_Sand"],
            bpy.data.materials["Mat_Nomad_Clay_Bone"],
            bpy.data.materials["Mat_Nomad_Clay_Terracotta"])
    unit = new_unit(spec["ox"], spec["oy"])
    top_z = build_rect_unit(spec, unit, src, coll, prefix + "_M", bag, rng, mats)

    n_units = 1
    if spec["family"] == "hybrid":
        # The tower stands on the block roof, set back from its edge.
        plan_cells = spec["plan"][1][min(spec["storeys"], len(spec["plan"][1])) - 1]
        x0, x1, y0, y1 = rk.plan_footprint(plan_cells, spec["block_k"])
        tx = spec["ox"] + (x0 + x1) * 0.5 + rng.uniform(-0.18, 0.18)
        ty = spec["oy"] + (y0 + y1) * 0.5 + rng.uniform(-0.18, 0.18)
        tow = new_unit(tx, ty)
        tow["drums"] = build_stack(spec["tower_d"], spec["storey_h"],
                                   spec["taper"], top_z - JOINT_SINK)
        sand = mats[0]
        p = prefix + "_T"
        for j, d in enumerate(tow["drums"]):
            o = cone("%s_Drum%d" % (p, j), d["d0"], d["d1"], d["h"], sand,
                     coll, bag)
            o.location = Vector((tow["ox"], tow["oy"], d["z0"]))
        place_openings(tow, spec, src, coll, p, bag, rng, with_door=False)
        place_rings(tow, src, coll, p, bag, rng)
        place_wall_gear(tow, spec, src, coll, p, bag, rng, 2)
        place_pipes(tow, src, coll, p, bag, rng, spec["pipes"])
        kind = crown_for(spec, tow["drums"][-1]["d1"], rng, False)
        top_z = place_crown(tow, kind, src, coll, p, bag)
        place_furniture(tow, spec, src, coll, p, bag, rng, top_z)
        n_units = 2
    else:
        # A flat family still deserves a skyline.
        fake = new_unit(spec["ox"], spec["oy"])
        fake["drums"] = [dict(d0=2.0 * spec["block_k"], d1=2.0 * spec["block_k"],
                              h=0.4, z0=top_z - 0.4, z1=top_z)]
        place_furniture(fake, spec, src, coll, prefix + "_M", bag, rng, top_z)

    root_empty = bpy.data.objects.new("Empty_%s_Root" % prefix, None)
    root_empty.empty_display_type = 'PLAIN_AXES'
    root_empty.empty_display_size = 0.8
    root_empty.location = Vector((spec["ox"], spec["oy"], 0.0))
    coll.objects.link(root_empty)
    bpy.context.view_layer.update()
    for o in bag:
        o.parent = root_empty
        o.matrix_parent_inverse = root_empty.matrix_world.inverted()
    coll.instance_offset = Vector((spec["ox"], spec["oy"], 0.0))
    return len(bag) + 1, top_z, n_units - 1


def build_building(spec, src, root, rng):
    if spec["family"] != "round":
        return build_rect_building(spec, src, root, rng)
    coll = bl.collection("Coll_NomadBuilding_%02d" % spec["idx"], root)
    prefix = "B%02d" % spec["idx"]
    bag = []
    sand = bpy.data.materials["Mat_Nomad_Clay_Sand"]
    # R12 - the ground block is the pale stone footing the kit's own tower had
    # under its adobe. It also keeps Clay_Bone in use now the plinth is gone.
    bone = bpy.data.materials["Mat_Nomad_Clay_Bone"]

    main = dict(ox=spec["ox"], oy=spec["oy"], facing=0.0,
                blocked=set(), used=set(), door_sectors=set(), openings=[],
                drums=build_stack(spec["base_d"], spec["storey_h"],
                                  spec["taper"], 0.0))

    # --- R13: annexes fused into the main body ----------------------------
    annexes = []
    used = []
    for i in range(spec["annexes"]):
        for _ in range(24):
            a = rng.uniform(0, 2 * math.pi)
            # Keep clear of the main door and of the other annexes.
            if abs((a + math.pi) % (2 * math.pi) - math.pi) < 0.75:
                continue
            if any(abs((a - b + math.pi) % (2 * math.pi) - math.pi) < 1.15
                   for b in used):
                continue
            break
        else:
            continue
        used.append(a)
        # An annex narrower than MIN_TOP_D is not a room, it is a bollard,
        # and it cannot hold a door either.
        ad = max(0.95, spec["base_d"] * rng.uniform(0.45, 0.80))
        n_st = max(1, rng.randint(1, max(1, spec["storeys"] - 1)))
        heights = [rng.uniform(0.95, 1.45) for _ in range(n_st)]
        R, ar = spec["base_d"] * 0.5, ad * 0.5
        dist = R + ar - FUSE_BITE * min(R, ar)                  # R13
        annexes.append(dict(
            ox=spec["ox"] + dist * math.sin(a),
            oy=spec["oy"] - dist * math.cos(a),
            facing=a, blocked=sector_span(a + math.pi, 0.8), used=set(),
            door_sectors=set(), openings=[],
            drums=build_stack(ad, heights, [False] + [rng.random() < 0.6] * (n_st - 1),
                              0.0)))
        # The main body loses the sectors the annex is buried in.
        main["blocked"] |= sector_span(a, 0.55)

    # --- geometry ---------------------------------------------------------
    for tag, unit, is_annex in ([("M", main, False)] +
                                [("A%d" % (i + 1), u, True)
                                 for i, u in enumerate(annexes)]):
        p = "%s_%s" % (prefix, tag)
        for j, d in enumerate(unit["drums"]):
            o = cone("%s_Drum%d" % (p, j), d["d0"], d["d1"], d["h"],
                     bone if j == 0 else sand, coll, bag)
            o.location = Vector((unit["ox"], unit["oy"], d["z0"]))
        # Openings first: R15 and R16 both need to know where they ended up.
        place_openings(unit, spec, src, coll, p, bag, rng, with_door=True)
        place_rings(unit, src, coll, p, bag, rng)
        if unit is main and spec["pilasters"]:
            place_pilasters(unit, spec["pilasters"], src, coll, p, bag)
        if spec["skirt"]:
            place_skirt(unit, src, coll, p, bag)
        # Scale the box count with the wall there is to put them on, or a ten
        # storey tower ends up with the same three boxes as a one storey hut.
        place_wall_gear(unit, spec, src, coll, p, bag, rng,
                        (spec["greebles"] if unit is main
                         else max(2, spec["greebles"] - 1))
                        + len(unit["drums"]) // 2)
        place_pipes(unit, src, coll, p, bag, rng,
                    spec["pipes"] if unit is main else max(1, spec["pipes"] - 1))
        kind = crown_for(spec, unit["drums"][-1]["d1"], rng, is_annex)
        roof_z = place_crown(unit, kind, src, coll, p, bag)
        if unit is main:
            place_furniture(unit, spec, src, coll, p, bag, rng, roof_z)
        elif rng.random() < 0.5:
            place_furniture(unit, dict(stacks=rng.choice([0, 1, 1]),
                                       masts=rng.choice([1, 1, 2]),
                                       dish=rng.random() < 0.35),
                            src, coll, p, bag, rng, roof_z)

    # One empty per building is the move handle: grab it and the whole building
    # follows, annexes included, which loose objects in a collection do not.
    root_empty = bpy.data.objects.new("Empty_%s_Root" % prefix, None)
    root_empty.empty_display_type = 'PLAIN_AXES'
    root_empty.empty_display_size = 0.8
    root_empty.location = Vector((spec["ox"], spec["oy"], 0.0))
    coll.objects.link(root_empty)
    bpy.context.view_layer.update()
    for o in bag:
        o.parent = root_empty
        o.matrix_parent_inverse = root_empty.matrix_world.inverted()
    coll.instance_offset = Vector((spec["ox"], spec["oy"], 0.0))
    return len(bag) + 1, main["drums"][-1]["z1"], len(annexes)


def main():
    out = bl.parse_out()
    bl.start(out)
    rng = random.Random(SEED)

    src_coll = bl.collection("Coll_KitSource")
    bl.append_objects(KIT, SOURCES, src_coll)
    # Pull in every loose detail part as well; they are named by the kit, not
    # by any table here, so they are collected by prefix.
    with bpy.data.libraries.load(KIT, link=False) as (lf, lt):
        extra = [n for n in lf.objects
                 if n.startswith(DETAIL_PREFIXES) and n not in SOURCES]
        # A copy: Blender rewrites data_to.objects in place on exit, which
        # would turn `extra` from a list of names into a list of Objects.
        lt.objects = list(extra)
    linked = {o.name for o in src_coll.objects}
    for n in extra:
        if n not in linked:
            src_coll.objects.link(bpy.data.objects[n])
    bpy.context.view_layer.update()
    src = {n: bpy.data.objects[n] for n in SOURCES}
    for n in extra:
        src[n] = bpy.data.objects[n]

    # Append the kit's whole palette, not just whatever rode in on the parts
    # used. Clay_Bone in particular now reaches the file only as the ground
    # block's material, which no appended object carries.
    with bpy.data.libraries.load(KIT, link=False) as (lib_from, lib_to):
        lib_to.materials = [n for n in lib_from.materials
                            if n not in bpy.data.materials]

    # The kit leaves the antenna masts, the dish and the bevelled curves with no
    # material at all; they render default grey and read as wire. Give them the
    # kit's own metal here rather than editing the hand-made component file.
    metal = bpy.data.materials["Mat_Nomad_Metal_Grey"]
    for o in src.values():
        if o.data is not None and hasattr(o.data, "materials") \
                and not len(o.data.materials):
            o.data.materials.append(metal)

    # X3 - group the kit's loose detail parts into panels. The kit's own
    # collections mix four kinds of thing in one, so the grouping comes from
    # where the parts sit, not from what the collections are called.
    loose = [o for o in src.values()
             if o.type == 'MESH' and o.name.startswith(DETAIL_PREFIXES)]
    src["__panels"] = rk.discover_details(loose)
    print("  detail panels found: %s"
          % ", ".join("%.2fx%.2f" % (p["w"], p["h"]) for p in src["__panels"]))

    # The kit now ships eighty-odd materials because duplicating an object in
    # the UI duplicates its material. They are byte-identical copies of the
    # same eight colours, so fold them back before anything uses them.
    for m in list(bpy.data.materials):
        if len(m.name) > 4 and m.name[-4] == '.' and m.name[-3:].isdigit():
            base = bpy.data.materials.get(m.name[:-4])
            if base is not None:
                m.user_remap(base)
                bpy.data.materials.remove(m)

    # Paint the scheme on last, so the town's colours come from the role map
    # rather than from whatever the kit happens to carry today. This is also
    # what keeps a hand-tuned colour - the bone course is yellow, not cream -
    # from being lost the next time the settlement is rebuilt.
    print("  palette: %r on %d materials"
          % (SCHEME, pal.apply(SCHEME)))

    root = bl.collection("Coll_Settlement")
    specs = roll_settlement(rng, COUNT_ROUND)
    specs += roll_rect(rng, COUNT_RECT, "rect", COUNT_ROUND)
    specs += roll_rect(rng, COUNT_HYBRID, "hybrid", COUNT_ROUND + COUNT_RECT)
    for i, spec in enumerate(specs):
        spec["ox"] = (i % GRID_COLS) * GRID_PITCH
        spec["oy"] = -(i // GRID_COLS) * GRID_PITCH
        n, top, annexes = build_building(spec, src, root, random.Random(spec["seed"]))
        print("  %s  %-7s %-10s storeys=%2d annexes=%d parts=%3d top=%5.2f"
              % ("B%02d" % spec["idx"], spec["family"],
                 spec["plan"][0] if spec["plan"] else spec["kind"],
                 spec["storeys"], annexes, n, top))

    for o in list(src_coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.collections.remove(src_coll)

    dupes = [o.name for o in bpy.data.objects
             if len(o.name) > 4 and o.name[-4] == '.' and o.name[-3:].isdigit()]
    if dupes:
        raise SystemExit("Auto-suffixed names reached save: %s" % dupes[:8])

    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(out))
    print("Wrote %s  objects=%d collections=%d meshes=%d"
          % (out, len(bpy.data.objects), len(bpy.data.collections),
             len(bpy.data.meshes)))


main()
