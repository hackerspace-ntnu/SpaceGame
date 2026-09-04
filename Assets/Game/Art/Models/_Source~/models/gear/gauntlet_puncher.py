"""Sucker Puncher gauntlet — the steam-driven punching ram.

    blender --background --python gauntlet_puncher.py -- --out gauntlet_puncher.blend

The Sucker Puncher at **double the first pass's device size** — the gauntlets
read too small on the astronaut, so every part of the machine is twice the
linear size it was. Two rails laid on the bracer's deck ARE the track: a sled
rides them, a beam off the sled carries the segmented knuckle block forward
over the back of the hand, and a big-bore steam cylinder lying between the
rails shoves the whole ram down the arm when it fires.

**The rails belong to the puncher, not to the arm.** They come out of
`gauntlet_base.blend`'s Rail variation through `_gauntlet.append_rails`, which
renames them `Mesh_SuckerPuncher_Rail{Left,Right}` on the way in — the file
they live in is an accident of how the base was authored, and no other gauntlet
has ever used them. Everything else the bracer is made of is worn permanently
and is not in this model; only the deck's constants are assumed, from `_gauntlet`.

## Frame

Family frame from `_gauntlet.py`: arm along +Y, wrist joint at y = 0, elbow +Y,
forward (toward the hand) −Y, dorsal +Z, thumb +X on a right forearm.
`_exportlib` maps Blender (x, y, z) onto Unity (−x, z, −y), so the ram slides
along Blender −Y = Unity +Z, the item's forward. Origin at the wrist bone, true
suit scale, worn at scale 1.

## Why the stroke is 0.168 m and not 0.34

Doubling the device does not double the stroke, because neither thing that
bounds it doubled:

1. **The rails are 0.240 m long** (y 0.090..0.330) and the sled has to stay on
   them, so travel ≤ 0.240 − sled − end margins. With a 0.064 m shoe and 4 mm at
   each end that is **0.168**. A 0.34 m stroke would need a sled of negative
   length.
2. **The in-line cylinder chain.** Between the fist's forward limit (y = −0.24)
   and the elbow limit (y = 0.36) everything has to fit at rest: the block
   (0.152 deep), the plate and the rod's pin and clevis, the shell (≥ stroke +
   piston + engagement) and the shell's own rear steam stub — and the whole
   moving half of that list sits one stroke further back at rest. Write it out
   and it collapses to `2·stroke ≤ 0.347`, i.e. **stroke ≤ 0.173**, whatever the
   rails do. `audit()` re-derives both bounds from the constants below and
   refuses to save if either is broken.

So 0.168 m is the geometric maximum for a single in-line ram on this arm, and
the fist's reach still grew: the strike face travels from y −0.070 to y −0.238,
against −0.040..−0.210 at half size, on a head twice the size.

## The layout

| Part | y (rest) | z | Notes |
|---|---|---|---|
| `Mesh_RamSlide_Carriage` | 0.262..0.326 | 0.262..0.406 | shoes cap the rails, pillars outboard of the gland, bridge over the shell, orange plate on top |
| `Mesh_SuckerPuncher_RamArm` | 0.080..0.280 | 0.286..0.450 | beam over the cylinder, rib, root collar, head plate, rod lug, gusset |
| `Mesh_KnuckleBlock_Segmented` | −0.070..0.082 | 0.245..0.479 | 2x; strike face 0.070 m past the wrist at rest, 0.238 at full stroke |
| `Mesh_RamSlide_Rod` | 0.093..0.326 | axis 0.306 | pinned at (0, 0.098, 0.306); 2x bore, 1.06 length |
| `Mesh_RamSlide_Cylinder` | 0.130..0.357 | axis 0.306 | 2x bore, 0.87 length — a short big-bore shell; anchor y 0.330 |
| `Mesh_SuckerPuncher_Cradle` | 0.152..0.268 | 0.245..0.274 | two shallow saddles sunk into the deck, inboard of the shoes |
| `Mesh_SuckerPuncher_Boiler` | 0.116..0.331 | 0.288..0.430 | drum out on the little-finger flank, gauge on top, safety valve = the vent |
| `Mesh_SuckerPuncher_BoilerBracket` | 0.164..0.286 | 0.247..0.302 | feet sunk in the deck margin, arms passing UNDER the shoe path |
| `Mesh_SuckerPuncher_SteamLine` | 0.321..0.359 | 0.33..0.39 | boiler outlet into the shell's rear stub, behind the sled |

The three clearances that decide the shape, all re-checked by `audit()`:

- the **shoes** grip the rails from above and outside in two stepped jaws, so
  that no part of them is inside the shell's gland or end-cap rings (0.0512) as
  the sled slides over them — which is why they are neither symmetric about the
  rail nor a single block;
- the **pillars** stand outboard of the gland ring (|x| 0.056 against 0.0512),
  because the sled passes over the gland at full stroke;
- the **boiler's bracket arms** cross from the deck out to the tank *underneath*
  the shoe path (z ≤ 0.258 against a shoe bottom of 0.262). That is the only way
  out to the flank: the sled sweeps the whole length of the deck, so there is no
  y at which an arm could cross at deck height without being run over.

## The ram pivot

`Mesh_RamSlide_Carriage`, `Mesh_SuckerPuncher_RamArm`,
`Mesh_KnuckleBlock_Segmented` and `Mesh_RamSlide_Rod` all have their origin at
`RAM_PIVOT` — on the rail axis under the sled's centre. Unity parents the four
under one transform and slides one local offset by `STROKE`.

## Empties

`Marker_Grip` at the origin (the builder adopts it as GripPoint) and
`Marker_Vent` at the boiler's safety valve. Identity rotation, exported with
`keep_empties=True`.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, _HERE)
sys.path.insert(0, os.path.join(_LIB, "components", "mechanical"))

import bpy  # noqa: E402
from _buildlib import *  # noqa: E402,F403
from _tracked import TrackedPart  # noqa: E402
from _gauntlet import (LIB, append_rails, append_objects,  # noqa: E402
                       BASE_DECK_Z, BASE_RAIL_Z, BASE_RAIL_Y0, BASE_RAIL_Y1,
                       BASE_WRIST_EDGE)
from panel_control import tube_path  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

SLIDE = os.path.join(LIB, "components", "mechanical", "ram_slide.blend")
HEAD = os.path.join(LIB, "components", "mechanical", "knuckle_block.blend")

# Index 0 is a structural metal: `bmesh.ops.bevel` stamps its new faces with 0.
STEEL, DARK, CHROME, BRASS, RUBBER, ORANGE, AMBER = range(7)
MATS = ["Mat_Metal_Steel_Worn",        # sled bridge and pillars, beam, cradle, brackets
        "Mat_Metal_Steel_Dark",        # shoes, collar, rib, head plate, boiler drum
        "Mat_Metal_Chrome_Scuffed",    # bolt heads, rivets
        "Mat_Metal_Brass_Tarnished",   # boiler hoops, gauge bezel, valve, unions, cradle bolts
        "Mat_Plastic_Rubber_Black",    # steam line
        "Mat_Paint_Safety_Orange",     # the one accent: the sled's bridge plate
        "Mat_Emissive_Amber"]          # gauge face

BEVEL_W = 0.0032                       # 2x the half-size build's 0.0016

# ── The device envelope (the brief's relaxed one) ────────────────────────────
ENV_Y1, ENV_Z1, ENV_HX = 0.360, 0.640, 0.210
REACH_Y0, REACH_Z0, REACH_HX = -0.240, 0.200, 0.200   # forward of the wrist
COLLAR_Z = 0.2165                      # the base collar's crown

# ── The track (the base's, unchanged) and the stroke it allows ───────────────
RAIL_AXIS_Z = (BASE_DECK_Z + BASE_RAIL_Z) / 2.0       # 0.261
SHOE_LEN = 0.064
CARRIAGE_Y1 = BASE_RAIL_Y1 - 0.004                    # 4 mm off the rear rail end
CARRIAGE_Y0 = CARRIAGE_Y1 - SHOE_LEN
STROKE = round(CARRIAGE_Y0 - BASE_RAIL_Y0 - 0.004, 3)  # 0.168: 4 mm off the front end
CARRIAGE_Y = (CARRIAGE_Y0 + CARRIAGE_Y1) / 2.0
RAM_PIVOT = Vector((0.0, CARRIAGE_Y, RAIL_AXIS_Z))

# ── The sled ─────────────────────────────────────────────────────────────────
# The shoe is STEPPED, not a block: a lower jaw wrapping the rail's top-outer
# corner, and an upper jaw set 10 mm further out. A solid block from 0.262 to
# 0.296 at |x| 0.045 puts its inner-top corner 3 mm inside the shell's gland and
# end-cap rings — the audit caught exactly that on the first build. The step is
# the cheapest way to keep a shoe that grips the bar and a sled that clears the
# widest thing it slides over.
SHOE_X0, SHOE_X1 = 0.046, 0.069        # lower jaw: over the rail's outer half
SHOE_Z0, SHOE_MID, SHOE_Z1 = 0.262, 0.273, 0.296
JAW_X0 = 0.056                         # upper jaw, on the pillars' line
PILLAR_X0, PILLAR_X1 = 0.056, 0.086    # inner face outboard of the gland ring
PILLAR_Z0, PILLAR_Z1 = 0.294, 0.368
BRIDGE_HX, BRIDGE_Z0, BRIDGE_Z1 = 0.092, 0.364, 0.400
PLATE_HX, PLATE_INSET = 0.076, 0.010   # the orange accent on the bridge

# ── The cylinder: 2x bore, 0.87 length ───────────────────────────────────────
CYL_SCALE = Vector((2.0, 0.87, 2.0))
CYL_R = 0.021 * CYL_SCALE.x            # barrel 0.042
CYL_GLAND_R = 0.0256 * CYL_SCALE.x     # gland ring 0.0512 — the widest section
CYL_AXIS_Z = BASE_DECK_Z + CYL_GLAND_R + 0.0048       # 0.306
CYL_ANCHOR_Y = 0.330
CYL_BODY_LEN = 0.230 * CYL_SCALE.y     # 0.2001
CYL_REAR_Y = CYL_ANCHOR_Y + 0.0309 * CYL_SCALE.y      # 0.3569 — the steam stub's tip
GLAND_FACE_Y = CYL_ANCHOR_Y - CYL_BODY_LEN            # 0.1299
CRADLE_Y = (0.170, 0.250)
CRADLE_HX, CRADLE_LEN, CRADLE_ARC_R = 0.030, 0.036, CYL_R + 0.002

# ── The rod and the head ─────────────────────────────────────────────────────
# 2x bore; 1.06 long, which is what it takes for the piston to still be 11 mm
# inside the gland at full stroke without its rear face reaching the barrel's
# end at rest. At the authored length it broke out of the gland by 0.9 mm.
ROD_SCALE = Vector((2.0, 1.06, 2.0))
ROD_LEN, ROD_PISTON = 0.215 * ROD_SCALE.y, 0.016 * ROD_SCALE.y
ROD_CLEVIS = 0.028 * ROD_SCALE.y
ROD_PIN = Vector((0.0, 0.098, CYL_AXIS_Z))

HEAD_SCALE = 2.0
HEAD_DEPTH = 0.0758 * HEAD_SCALE       # mounting face to strike face: 0.1516
HEAD_MOUNT = Vector((0.0, 0.082, 0.360))
HEAD_PLATE_Y0, HEAD_PLATE_Y1 = 0.080, 0.096           # 2 mm into the block's backing plate
HEAD_PLATE_HX, HEAD_PLATE_Z0, HEAD_PLATE_Z1 = 0.100, 0.300, 0.450
LUG_HX, LUG_Y0, LUG_Y1, LUG_Z0, LUG_Z1 = 0.011, 0.094, 0.118, 0.286, 0.326

# ── The beam ─────────────────────────────────────────────────────────────────
BEAM_HX, BEAM_Y0, BEAM_Y1 = 0.060, HEAD_PLATE_Y1 - 0.006, CARRIAGE_Y0 + 0.006
BEAM_Z0, BEAM_Z1 = 0.362, 0.398        # 5 mm over the gland ring's crown
RIB_HX, RIB_Y0, RIB_Y1, RIB_Z0, RIB_Z1 = 0.028, 0.110, 0.250, 0.396, 0.412
COLLAR_HX, COLLAR_Y0, COLLAR_Y1 = 0.076, 0.244, 0.266  # clamp round the beam's root
COLLAR_Z0, COLLAR_Z1 = 0.356, 0.406
GUSSET_Y1, GUSSET_T = 0.124, 0.030     # ends 6 mm forward of the gland face

# ── The boiler, out on the little-finger flank ───────────────────────────────
BOILER_X, BOILER_Z, BOILER_R = -0.150, 0.340, 0.048
BOILER_Y0, BOILER_Y1 = 0.140, 0.300
BOILER_RING_Y = (0.152, 0.288)
GAUGE_Y, VALVE_Y = 0.200, 0.288
VENT = Vector((BOILER_X, VALVE_Y, 0.430))
ARM_Y = (0.180, 0.270)                 # clear of the deck's bolt bosses (y 0.107, 0.313)
ARM_X0, ARM_X1 = -0.048, -0.150
ARM_Z0, ARM_Z1 = 0.251, 0.258          # UNDER the shoe path (0.262), above the deck plane
FOOT_X0, FOOT_X1, FOOT_Z0 = -0.048, -0.070, 0.247     # sunk 3 mm into the deck margin
RISER_X0, RISER_X1, RISER_Z1 = -0.136, -0.164, 0.302

RAM = ("Mesh_RamSlide_Carriage", "Mesh_SuckerPuncher_RamArm",
       "Mesh_KnuckleBlock_Segmented", "Mesh_RamSlide_Rod")


def place_at(obj, at, origin=None, scale=None):
    """Seat an appended component at `at`, baking the transform into the mesh.

    `origin` re-seats the pivot at a chosen world point rather than carrying the
    component's own — what lets the four ram objects share `RAM_PIVOT`, so the
    prefab can parent them under one transform at local zero.

    `scale` is a per-axis vector, because this build doubles the ram_slide parts
    radially without doubling them along the arm: a stroke the rails cannot give
    does not need a 0.46 m shell, and a short big-bore cylinder is the honest
    shape for a fist this size. Applied into the mesh, so the object still ships
    at scale 1.
    """
    m = Matrix.Translation(Vector(at))
    if scale is not None:
        m = m @ Matrix.Diagonal(Vector(scale).to_4d())
    world = m @ obj.matrix_world
    origin = Vector(origin) if origin is not None else world.to_translation()
    obj.data.transform(Matrix.Translation(-origin) @ world)
    obj.location = origin
    obj.rotation_euler = (0.0, 0.0, 0.0)
    obj.scale = (1.0, 1.0, 1.0)
    return obj


def carriage(coll, mats):
    """The sled: two shoes capping the base's rails, two pillars, a bridge.

    A gantry, because the cylinder lies between the rails under it. The shoes
    grip from above and outside — their inner faces stand 4 mm off the barrel's
    widest section, which is why they are not symmetric about the rail — and the
    pillars stand outboard of the gland ring, which the sled passes over at full
    stroke.
    """
    p = TrackedPart(mats)
    hard = []
    for sx in (-1, 1):
        hard += p.slab((sx * SHOE_X0, CARRIAGE_Y0, SHOE_Z0),
                       (sx * SHOE_X1, CARRIAGE_Y1, SHOE_MID), DARK)
        hard += p.slab((sx * JAW_X0, CARRIAGE_Y0, SHOE_MID - 0.003),
                       (sx * SHOE_X1, CARRIAGE_Y1, SHOE_Z1), DARK)
        p.slab((sx * PILLAR_X0, CARRIAGE_Y0 + 0.008, PILLAR_Z0),
               (sx * PILLAR_X1, CARRIAGE_Y1 - 0.008, PILLAR_Z1), STEEL)
    hard += p.slab((-BRIDGE_HX, CARRIAGE_Y0, BRIDGE_Z0), (BRIDGE_HX, CARRIAGE_Y1, BRIDGE_Z1),
                   STEEL)
    # The one orange accent: 2 mm sunk into the bridge, 6 mm proud of it.
    hard += p.slab((-PLATE_HX, CARRIAGE_Y0 + PLATE_INSET, BRIDGE_Z1 - 0.002),
                   (PLATE_HX, CARRIAGE_Y1 - PLATE_INSET, BRIDGE_Z1 + 0.006), ORANGE)
    for sx in (-1, 1):
        for y in (CARRIAGE_Y0 + 0.012, CARRIAGE_Y1 - 0.012):
            p.cyl((sx * 0.084, y, BRIDGE_Z1 + 0.003), 0.0070, 0.010, 'Z', 6, CHROME,
                  radius_top=0.0056)
    p.restamp("carriage")
    p.bevel(hard, width=BEVEL_W, segments=1)
    return p.finish("Mesh_RamSlide_Carriage", coll, origin=RAM_PIVOT)


def ram_arm(coll, mats):
    """Beam from the sled's bridge to the head plate, and the plate itself.

    Written in world coordinates and re-origined onto `RAM_PIVOT`: its job is to
    bridge two things already fixed — the bridge's underside and the knuckle
    block's backing plate. It rides 5 mm over the cylinder's gland, the tallest
    thing under its path anywhere in the stroke.

    The lug the piston rod pins to hangs BELOW the plate, on the bore's axis, so
    the rod's clevis sits in open air between the lug and the gland face. A lug
    on the plate's own centre would need the clevis inside the shell.
    """
    p = TrackedPart(mats)
    hard = []
    hard += p.slab((-BEAM_HX, BEAM_Y0, BEAM_Z0), (BEAM_HX, BEAM_Y1, BEAM_Z1), STEEL)
    hard += p.slab((-COLLAR_HX, COLLAR_Y0, COLLAR_Z0), (COLLAR_HX, COLLAR_Y1, COLLAR_Z1), DARK)
    # Stiffening rib along the beam's spine, riveted.
    p.slab((-RIB_HX, RIB_Y0, RIB_Z0), (RIB_HX, RIB_Y1, RIB_Z1), DARK)
    p.rivets((0.0, RIB_Y0 + 0.026, RIB_Z1 + 0.002), (0.0, RIB_Y1 - 0.026, RIB_Z1 + 0.002), 4,
             radius=0.0062, height=0.0064, axis='Z', mat=CHROME)
    hard += p.slab((-HEAD_PLATE_HX, HEAD_PLATE_Y0, HEAD_PLATE_Z0),
                   (HEAD_PLATE_HX, HEAD_PLATE_Y1, HEAD_PLATE_Z1), DARK)
    hard += p.slab((-LUG_HX, LUG_Y0, LUG_Z0), (LUG_HX, LUG_Y1, LUG_Z1), STEEL)
    # Gusset behind the plate, under the beam: a wedge extruded across X. It
    # stops forward of the gland face — a longer one runs into the barrel.
    p.prism([(HEAD_PLATE_Y1 - 0.002, HEAD_PLATE_Z0 + 0.020),
             (HEAD_PLATE_Y1 - 0.002, BEAM_Z0 + 0.004),
             (GUSSET_Y1, BEAM_Z0 + 0.004)], GUSSET_T, axis='X', mat=STEEL)
    for sx in (-1, 1):
        p.cyl((sx * 0.070, HEAD_PLATE_Y1 + 0.003, (HEAD_PLATE_Z0 + HEAD_PLATE_Z1) / 2),
              0.0080, 0.012, 'Y', 8, CHROME, radius_top=0.0064)
    p.restamp("ram_arm")
    p.bevel(hard, width=BEVEL_W, segments=1)
    return p.finish("Mesh_SuckerPuncher_RamArm", coll, origin=RAM_PIVOT)


def cradle(coll, mats):
    """Two shallow saddles sunk into the deck, cupping the barrel's underside.

    Narrow (|x| ≤ 0.030) on purpose: the sled's shoes sweep the whole deck at
    |x| 0.045..0.069, so anything standing on the deck between them has to stay
    inboard of that. A saddle cupping the barrel higher would be wider than the
    shoes allow, which is why it holds the bottom third and no more.
    """
    p = TrackedPart(mats)
    zt = CYL_AXIS_Z - math.sqrt(CRADLE_ARC_R ** 2 - CRADLE_HX ** 2)
    a0 = math.atan2(zt - CYL_AXIS_Z, CRADLE_HX)
    a1 = -math.pi - a0
    for y in CRADLE_Y:
        prof = [(-CRADLE_HX, BASE_DECK_Z - 0.003), (CRADLE_HX, BASE_DECK_Z - 0.003),
                (CRADLE_HX, zt)]
        for i in range(1, 6):
            a = a0 + (a1 - a0) * i / 6
            prof.append((CRADLE_ARC_R * math.cos(a), CYL_AXIS_Z + CRADLE_ARC_R * math.sin(a)))
        prof.append((-CRADLE_HX, zt))
        p.prism(prof, CRADLE_LEN, axis='Y', mat=STEEL, offset=(0.0, y, 0.0))
        for sx in (-1, 1):
            p.cyl((sx * 0.020, y, BASE_DECK_Z - 0.001), 0.0060, 0.008, 'Z', 6, BRASS,
                  radius_top=0.0048)
    p.restamp("cradle")
    return p.finish("Mesh_SuckerPuncher_Cradle", coll)


def boiler(coll, mats):
    """The steam tank out on the little-finger flank: a drum with domed ends,
    two brass hoops, a gauge on top readable from above (+Z), a safety valve at
    the top-rear — the vent — and the outlet union on the rear dome."""
    p = TrackedPart(mats)

    def c(y):
        return (BOILER_X, y, BOILER_Z)

    p.cyl(c((BOILER_Y0 + BOILER_Y1) / 2), BOILER_R, BOILER_Y1 - BOILER_Y0, 'Y', 12, DARK)
    # Domes: `radius_top` is the +Y end. Each overlaps the drum by 14 mm.
    p.cyl(c(BOILER_Y0 - 0.010), 0.032, 0.028, 'Y', 12, DARK, radius_top=BOILER_R)
    p.cyl(c(BOILER_Y1 + 0.010), BOILER_R, 0.028, 'Y', 12, DARK, radius_top=0.032)
    for y in BOILER_RING_Y:
        p.cyl(c(y), BOILER_R + 0.004, 0.016, 'Y', 12, BRASS)
    top = BOILER_Z + BOILER_R
    p.cyl((BOILER_X, GAUGE_Y, top - 0.004), 0.020, 0.024, 'Z', 10, BRASS)
    p.cyl((BOILER_X, GAUGE_Y, top + 0.011), 0.015, 0.006, 'Z', 10, AMBER)
    p.cyl((BOILER_X, VALVE_Y, top + 0.017), 0.011, 0.038, 'Z', 8, BRASS)
    p.cyl((BOILER_X, VALVE_Y, VENT.z - 0.006), 0.016, 0.012, 'Z', 8, BRASS)
    p.cyl(c(BOILER_Y1 + 0.018), 0.013, 0.026, 'Y', 8, BRASS)
    p.restamp("boiler")
    return p.finish("Mesh_SuckerPuncher_Boiler", coll)


def boiler_bracket(coll, mats):
    """Two outriggers carrying the tank off the deck.

    They cross from the deck out to the flank **under the shoe path** — 4 mm
    below the shoes' undersides — because the sled sweeps the whole length of
    the deck and there is no y at which an arm could cross at deck height
    without being run over. The feet sink 3 mm into the deck's outer margin; the
    risers stand outboard of everything that moves.
    """
    p = TrackedPart(mats)
    hard = []
    for y in ARM_Y:
        hard += p.slab((ARM_X0, y - 0.016, ARM_Z0), (ARM_X1, y + 0.016, ARM_Z1), STEEL)
        p.slab((FOOT_X0, y - 0.016, FOOT_Z0), (FOOT_X1, y + 0.016, ARM_Z1), STEEL)
        hard += p.slab((RISER_X0, y - 0.014, ARM_Z1 - 0.003),
                       (RISER_X1, y + 0.014, RISER_Z1), STEEL)
    p.restamp("bracket")
    p.bevel(hard, width=BEVEL_W, segments=1)
    return p.finish("Mesh_SuckerPuncher_BoilerBracket", coll)


def steam_line(coll, mats):
    """Rubber line from the boiler's outlet union into the shell's rear steam
    stub, routed behind the sled's rest position so nothing sweeps through it."""
    p = TrackedPart(mats)
    # The component's own hose stub, at its middle knuckle rather than its tip:
    # a 9 mm tube capped on the tip would put its rim past the elbow limit.
    stub = (0.0, CYL_ANCHOR_Y + 0.012 * CYL_SCALE.y, CYL_AXIS_Z + 0.0378 * CYL_SCALE.z)
    tube_path(p, [(BOILER_X, 0.328, BOILER_Z),
                  (BOILER_X + 0.010, 0.344, 0.358),
                  (-0.060, 0.350, 0.376),
                  stub], 0.009, RUBBER, seg=6)
    p.restamp("steam_line")
    return p.finish("Mesh_SuckerPuncher_SteamLine", coll)


def markers(coll):
    for name, at in (("Marker_Grip", (0.0, 0.0, 0.0)), ("Marker_Vent", tuple(VENT))):
        obj = bpy.data.objects.new(name, None)
        obj.empty_display_type = 'ARROWS'
        obj.empty_display_size = 0.05
        obj.location = Vector(at)
        coll.objects.link(obj)


def audit():
    """Prove the geometry against the brief before saving.

    Everything is checked at rest AND with the ram swept to full stroke: the
    shared pivot, the two independent bounds on the stroke, the relaxed
    envelope, the reach-over-the-hand rule, the base's collar, and — for
    everything but the rod, which lives inside it — the cylinder bore. Raised,
    not reported: a ram that hits its own cylinder is a build failure.
    """
    bpy.context.view_layer.update()      # matrix_world is stale after place_at
    ram = [bpy.data.objects[n] for n in RAM]
    for o in ram:
        if (o.location - RAM_PIVOT).length > 1e-6:
            raise SystemExit("%s origin %s is not RAM_PIVOT %s"
                             % (o.name, tuple(o.location), tuple(RAM_PIVOT)))

    rail_max = BASE_RAIL_Y1 - BASE_RAIL_Y0 - SHOE_LEN - 0.008
    # The in-line chain, from the constants: block, plate, rod fittings, piston
    # and the shell's rear stub all have to fit between the fist's forward limit
    # and the elbow limit, and the moving half sits one stroke further back.
    fittings = (ROD_PIN.y - HEAD_MOUNT.y) + ROD_CLEVIS + 0.002
    chain_max = ((ENV_Y1 - 0.002 - 0.0309 * CYL_SCALE.y) - REACH_Y0
                 - HEAD_DEPTH - fittings - ROD_PISTON - 0.010) / 2.0
    if STROKE > min(rail_max, chain_max) + 1e-9:
        raise SystemExit("stroke %.3f exceeds rails %.3f / chain %.3f"
                         % (STROKE, rail_max, chain_max))

    def in_bore(q):
        """Inside the shell proper. Its AABB would not do — the rear steam stub
        lifts it 83 mm above the barrel, and the beam legitimately passes over
        the gland at 5 mm."""
        return (GLAND_FACE_Y - 0.002 < q.y < CYL_ANCHOR_Y + 0.002
                and math.hypot(q.x, q.z - CYL_AXIS_Z) < CYL_GLAND_R - 0.001)

    device = [o for o in bpy.data.objects
              if o.type == 'MESH' and not o.name.startswith("Mesh_GauntletBase_")]
    for o in device:
        poses = [(0.0, 0.0, 0.0), (0.0, -STROKE, 0.0)] if o in ram else [(0.0, 0.0, 0.0)]
        for shift in poses:
            where = "at full stroke" if shift[1] else "at rest"
            bad = 0
            for v in o.data.vertices:
                q = (o.matrix_world @ v.co) + Vector(shift)
                if q.y > ENV_Y1 or q.z > ENV_Z1 or abs(q.x) > ENV_HX:
                    bad += 1
                elif q.y < BASE_WRIST_EDGE and (q.y < REACH_Y0 or q.z < REACH_Z0
                                                or abs(q.x) > REACH_HX):
                    bad += 1
                elif q.y < 0.090 and q.z < COLLAR_Z + 0.004:
                    bad += 1
                elif o in ram and o.name != "Mesh_RamSlide_Rod" and in_bore(q):
                    bad += 1
            if bad:
                raise SystemExit("%s puts %d vertices out of bounds %s" % (o.name, bad, where))

    piston = ROD_PIN.y + ROD_LEN - ROD_PISTON - STROKE
    if piston < GLAND_FACE_Y:
        raise SystemExit("the piston leaves the cylinder at full stroke")
    print("  audit: stroke %.3f (rails allow %.3f, the in-line chain %.3f); envelope, collar "
          "and bore clear; piston %.0f mm inside the gland at full stroke"
          % (STROKE, rail_max, chain_max, (piston - GLAND_FACE_Y) * 1000))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_GauntletPuncher")

    append_rails(coll)

    cylinder, rod = append_objects(SLIDE, ["Mesh_RamSlide_Cylinder", "Mesh_RamSlide_Rod"], coll)
    place_at(cylinder, (0.0, CYL_ANCHOR_Y, CYL_AXIS_Z), scale=CYL_SCALE)
    place_at(rod, ROD_PIN, origin=RAM_PIVOT, scale=ROD_SCALE)
    head, = append_objects(HEAD, ["Mesh_KnuckleBlock_Segmented"], coll)
    place_at(head, HEAD_MOUNT, origin=RAM_PIVOT, scale=(HEAD_SCALE,) * 3)

    carriage(coll, mats)
    ram_arm(coll, mats)
    cradle(coll, mats)
    boiler(coll, mats)
    boiler_bracket(coll, mats)
    steam_line(coll, mats)
    markers(coll)

    audit()
    save(out)
    report()

    device = sum(tri_count(o) for o in bpy.data.objects
                 if o.type == 'MESH' and not o.name.startswith("Mesh_GauntletBase_"))
    print("  DEVICE TRIS (without the base): %d" % device)
    u = (-RAM_PIVOT.x, RAM_PIVOT.z, -RAM_PIVOT.y)
    print("  RAM_PIVOT blender (%.4f, %.4f, %.4f)  unity (%.4f, %.4f, %.4f)  "
          "stroke %.3f m along Blender -Y / Unity +Z" % (*RAM_PIVOT, *u, STROKE))
    for n in RAM:
        print("  ram %-32s origin (%.4f, %.4f, %.4f)" % (n, *bpy.data.objects[n].location))
    for n in ("Marker_Grip", "Marker_Vent"):
        b = bpy.data.objects[n].location
        print("  %-12s blender (%.4f, %.4f, %.4f)  unity (%.4f, %.4f, %.4f)"
              % (n, *b, -b.x, b.z, -b.y))


if __name__ == "__main__":
    main()
