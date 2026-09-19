"""Flame Gauntlet — a burner on the forearm, fed from a tank on the flank.

    blender --background --python gauntlet_flame.py -- --out gauntlet_flame.blend

The Flamethrower's fire, worn. A burner tube lies along the bracer's deck and ends in a
flared nozzle just past the wrist; a fuel tank rides the little-finger flank on two
outriggers, with a gauge on top and a hose looping forward into the burner's rear union.
Nothing here moves: the fire is Unity's, hung off `Marker_Muzzle`.

## Frame

Family frame from `_gauntlet.py`: arm along +Y, wrist joint at y = 0, elbow +Y, forward
(toward the hand) −Y, dorsal +Z, thumb +X on a right forearm. `_exportlib` maps Blender
(x, y, z) onto Unity (−x, z, −y), so the nozzle, facing −Y here, faces Unity +Z — the
item's forward, and the direction `FlameJet` throws the jet along.

## The layout

| Part | y | z | Notes |
|---|---|---|---|
| `Mesh_FlameGauntlet_Cradle`  | 0.104..0.312 | 0.247..0.264 | two saddles on the deck, sunk 3 mm, bolted |
| `Mesh_FlameGauntlet_Burner`  | −0.030..0.330 | axis 0.290 | the tube, 24 mm bore, dark steel; rear union at the elbow end |
| `Mesh_FlameGauntlet_Nozzle`  | −0.070..−0.020 | axis 0.290 | worn-steel flare, 50 mm at the mouth, dished 14 mm |
| `Mesh_FlameGauntlet_Shroud`  | −0.010..0.100 | axis 0.290 | vented heat shroud round the tube's front third: six ribs, two rings |
| `Mesh_FlameGauntlet_Tank`    | 0.140..0.300 | axis 0.340 | drum at x −0.150 with domed ends, two brass hoops, gauge on top, outlet forward |
| `Mesh_FlameGauntlet_Bracket` | 0.170..0.270 | 0.247..0.302 | outriggers from the deck margin to the tank |
| `Mesh_FlameGauntlet_Hose`    | — | — | tank outlet to the burner's rear union |
| `Mesh_FlameGauntlet_Cover`   | 0.150..0.250 | 0.302..0.310 | safety-orange plate on the burner with a red arming stripe |
| `Mesh_FlameGauntlet_Lamps`   | 0.120 | — | two amber ready lamps beside the burner |

The nozzle reaches 70 mm forward of the wrist at z 0.290, inside the family's forward
reach limit (y ≥ −0.240, z ≥ 0.200); nothing crosses the collar (y < 0.090) below its
crown plus margin. `audit()` asserts all of it.

## Empties

`Marker_Grip` at the origin (the builder adopts it as GripPoint), `Marker_Muzzle` on the
bore axis at the mouth plane (the jet root and the artifact's muzzle), `Marker_Pilot` beside
it (the pilot flame), `Marker_Gauge` on the gauge face. Identity rotation, exported with
`keep_empties=True`.

Generation script — historical record. The .blend is the source of truth; never re-run
this over the file it produced.
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
from _gauntlet import BASE_DECK_Z, BASE_WRIST_EDGE  # noqa: E402
from panel_control import tube_path  # noqa: E402

from mathutils import Vector  # noqa: E402

STEEL, DARK, CHROME, BRASS, RUBBER, ORANGE, RED, AMBER = range(8)
MATS = ["Mat_Metal_Steel_Worn",        # nozzle, shroud ribs, bracket
        "Mat_Metal_Steel_Dark",        # burner tube, cradle, tank drum
        "Mat_Metal_Chrome_Scuffed",    # bolts, rings, unions
        "Mat_Metal_Brass_Tarnished",   # tank hoops, gauge bezel, valve
        "Mat_Plastic_Rubber_Black",    # hose
        "Mat_Paint_Safety_Orange",     # cover plate
        "Mat_Paint_Warn_Red",          # arming stripe
        "Mat_Emissive_Amber"]          # gauge face, lamps

BEVEL_W = 0.0024

ENV_Y1, ENV_Z1, ENV_HX = 0.360, 0.640, 0.210
REACH_Y0, REACH_Z0, REACH_HX = -0.240, 0.200, 0.200
COLLAR_Z = 0.2165

# ── Burner ───────────────────────────────────────────────────────────────────
BORE_Z = 0.290
TUBE_R = 0.024
TUBE_Y0, TUBE_Y1 = -0.030, 0.330
UNION_Y = 0.338
# ── Nozzle ───────────────────────────────────────────────────────────────────
MOUTH_Y, MOUTH_R = -0.070, 0.050
RECESS_Y, RECESS_R = -0.056, 0.036
THROAT_Y = -0.020
NOZ_SEG = 24
# ── Shroud ───────────────────────────────────────────────────────────────────
SHROUD_Y0, SHROUD_Y1, SHROUD_R = -0.010, 0.100, TUBE_R + 0.010
RIBS = 6
# ── Cradle ───────────────────────────────────────────────────────────────────
CRADLE_Y = (0.130, 0.290)
CRADLE_HX, CRADLE_LEN = 0.034, 0.040
# ── Tank, out on the little-finger flank ─────────────────────────────────────
TANK_X, TANK_Z, TANK_R = -0.150, 0.340, 0.048
TANK_Y0, TANK_Y1 = 0.140, 0.300
HOOP_Y = (0.152, 0.288)
GAUGE_Y, VALVE_Y = 0.200, 0.280
OUTLET_Y = TANK_Y0 - 0.018
ARM_Y = (0.180, 0.260)
ARM_X0, ARM_X1 = -0.048, -0.150
ARM_Z0, ARM_Z1 = 0.250, 0.262
RISER_X0, RISER_X1, RISER_Z1 = -0.136, -0.164, 0.302
# ── Dressing ─────────────────────────────────────────────────────────────────
COVER_Y0, COVER_Y1, COVER_HX = 0.150, 0.250, 0.020
STRIPE_Y0, STRIPE_Y1 = 0.228, 0.240
LAMP_X, LAMP_Y, LAMP_R = 0.040, 0.120, 0.007

MUZZLE = Vector((0.0, MOUTH_Y, BORE_Z))
PILOT = Vector((0.020, MOUTH_Y - 0.004, BORE_Z + 0.030))
GAUGE = Vector((TANK_X, GAUGE_Y, TANK_Z + TANK_R + 0.017))


def ring(r, seg=NOZ_SEG):
    return [(r * math.cos(2 * math.pi * i / seg), BORE_Z + r * math.sin(2 * math.pi * i / seg))
            for i in range(seg)]


def cradle(coll, mats):
    """Two saddles cupping the tube's underside, sunk into the deck and bolted."""
    p = TrackedPart(mats)
    zt = BORE_Z - math.sqrt(max(1e-6, (TUBE_R + 0.002) ** 2 - CRADLE_HX ** 2))
    for y in CRADLE_Y:
        prof = [(-CRADLE_HX, BASE_DECK_Z - 0.003), (CRADLE_HX, BASE_DECK_Z - 0.003), (CRADLE_HX, zt)]
        a0 = math.atan2(zt - BORE_Z, CRADLE_HX)
        a1 = -math.pi - a0
        for i in range(1, 6):
            a = a0 + (a1 - a0) * i / 6
            prof.append(((TUBE_R + 0.002) * math.cos(a), BORE_Z + (TUBE_R + 0.002) * math.sin(a)))
        prof.append((-CRADLE_HX, zt))
        p.prism(prof, CRADLE_LEN, axis='Y', mat=DARK, offset=(0.0, y, 0.0))
        for sx in (-1, 1):
            p.cyl((sx * 0.024, y, BASE_DECK_Z - 0.001), 0.0055, 0.008, 'Z', 6, CHROME, radius_top=0.0044)
    p.restamp("cradle")
    return p.finish("Mesh_FlameGauntlet_Cradle", coll)


def burner(coll, mats):
    """The tube, with a rear cap and the union the hose feeds."""
    p = TrackedPart(mats)
    p.cyl((0.0, (TUBE_Y0 + TUBE_Y1) / 2, BORE_Z), TUBE_R, TUBE_Y1 - TUBE_Y0, 'Y', 16, DARK)
    p.cyl((0.0, TUBE_Y1 + 0.002, BORE_Z), TUBE_R + 0.004, 0.012, 'Y', 16, CHROME)      # rear cap ring
    p.cyl((0.0, UNION_Y + 0.006, BORE_Z), 0.011, 0.020, 'Y', 8, BRASS)                 # union
    p.restamp("burner")
    return p.finish("Mesh_FlameGauntlet_Burner", coll)


def nozzle(coll, mats):
    """The flare: one closed loft, recess floor → recess wall → mouth lip → cone → throat."""
    p = TrackedPart(mats)
    sections = [(RECESS_Y, ring(RECESS_R)),
                (MOUTH_Y, ring(RECESS_R)),
                (MOUTH_Y, ring(MOUTH_R)),
                (THROAT_Y, ring(TUBE_R + 0.001))]
    p.loft(sections, axis='Y', mat=STEEL, cap=True)
    p.torus((0.0, MOUTH_Y + 0.003, BORE_Z), MOUTH_R - 0.006, 0.006, axis='Y', maj_seg=NOZ_SEG, min_seg=8, mat=CHROME)
    # Igniter stub where the pilot burns.
    p.cyl((PILOT.x, PILOT.y + 0.010, PILOT.z), 0.006, 0.020, 'Y', 8, BRASS)
    p.restamp("nozzle")
    return p.finish("Mesh_FlameGauntlet_Nozzle", coll)


def shroud(coll, mats):
    """Six ribs between two rings round the tube's front third: vented, and cheap."""
    p = TrackedPart(mats)
    for y in (SHROUD_Y0 + 0.006, SHROUD_Y1 - 0.006):
        p.torus((0.0, y, BORE_Z), SHROUD_R, 0.005, axis='Y', maj_seg=20, min_seg=8, mat=CHROME)
    for i in range(RIBS):
        a = 2 * math.pi * i / RIBS + math.pi / RIBS
        x, z = SHROUD_R * math.cos(a), BORE_Z + SHROUD_R * math.sin(a)
        p.box((x, (SHROUD_Y0 + SHROUD_Y1) / 2, z), (0.008, SHROUD_Y1 - SHROUD_Y0, 0.008), STEEL)
    p.restamp("shroud")
    return p.finish("Mesh_FlameGauntlet_Shroud", coll)


def tank(coll, mats):
    """The fuel drum: domed ends, two brass hoops, gauge on top, valve at the rear top, the
    outlet on the front dome pointing at the burner."""
    p = TrackedPart(mats)

    def c(y):
        return (TANK_X, y, TANK_Z)

    p.cyl(c((TANK_Y0 + TANK_Y1) / 2), TANK_R, TANK_Y1 - TANK_Y0, 'Y', 12, DARK)
    p.cyl(c(TANK_Y0 - 0.010), 0.032, 0.028, 'Y', 12, DARK, radius_top=TANK_R)
    p.cyl(c(TANK_Y1 + 0.010), TANK_R, 0.028, 'Y', 12, DARK, radius_top=0.032)
    for y in HOOP_Y:
        p.cyl(c(y), TANK_R + 0.004, 0.016, 'Y', 12, BRASS)
    top = TANK_Z + TANK_R
    p.cyl((TANK_X, GAUGE_Y, top - 0.004), 0.020, 0.024, 'Z', 10, BRASS)
    p.cyl((TANK_X, GAUGE_Y, top + 0.011), 0.015, 0.006, 'Z', 10, AMBER)
    p.cyl((TANK_X, VALVE_Y, top + 0.012), 0.010, 0.028, 'Z', 8, BRASS)
    p.cyl(c(OUTLET_Y), 0.012, 0.024, 'Y', 8, BRASS)
    p.restamp("tank")
    return p.finish("Mesh_FlameGauntlet_Tank", coll)


def bracket(coll, mats):
    """Two outriggers from the deck's margin out to risers under the drum."""
    p = TrackedPart(mats)
    hard = []
    for y in ARM_Y:
        hard += p.slab((ARM_X0, y - 0.014, ARM_Z0), (ARM_X1, y + 0.014, ARM_Z1), STEEL)
        p.slab((ARM_X0, y - 0.014, BASE_DECK_Z - 0.003), (ARM_X0 - 0.022, y + 0.014, ARM_Z1), STEEL)
        hard += p.slab((RISER_X0, y - 0.012, ARM_Z1 - 0.003), (RISER_X1, y + 0.012, RISER_Z1), STEEL)
    p.restamp("bracket")
    p.bevel(hard, width=BEVEL_W, segments=1)
    return p.finish("Mesh_FlameGauntlet_Bracket", coll)


def hose(coll, mats):
    """Rubber line from the tank's outlet round the front of the bracket and up into the
    burner's rear union, routed clear of everything on the deck."""
    p = TrackedPart(mats)
    tube_path(p, [(TANK_X, OUTLET_Y - 0.012, TANK_Z),
                  (TANK_X + 0.020, OUTLET_Y - 0.040, TANK_Z + 0.010),
                  (-0.070, 0.120, 0.330),
                  (-0.030, 0.300, 0.345),
                  (0.0, UNION_Y + 0.014, BORE_Z)], 0.008, RUBBER, seg=6)
    p.restamp("hose")
    return p.finish("Mesh_FlameGauntlet_Hose", coll)


def cover(coll, mats):
    """The orange accent, wrapped over the top of the tube on a small saddle, with the stripe."""
    p = TrackedPart(mats)
    hard = p.slab((-COVER_HX, COVER_Y0, BORE_Z + TUBE_R - 0.004), (COVER_HX, COVER_Y1, BORE_Z + TUBE_R + 0.004), ORANGE)
    p.slab((-COVER_HX + 0.003, STRIPE_Y0, BORE_Z + TUBE_R + 0.004), (COVER_HX - 0.003, STRIPE_Y1, BORE_Z + TUBE_R + 0.0055), RED)
    p.restamp("cover")
    p.bevel(hard, width=BEVEL_W, segments=1)
    return p.finish("Mesh_FlameGauntlet_Cover", coll)


def lamps(coll, mats):
    """Two amber ready lamps on the deck beside the tube, domed."""
    p = TrackedPart(mats)
    for sx in (-1, 1):
        p.cyl((sx * LAMP_X, LAMP_Y, BASE_DECK_Z + 0.004), LAMP_R, 0.014, 'Z', 10, AMBER, radius_top=LAMP_R * 0.8)
    p.restamp("lamps")
    return p.finish("Mesh_FlameGauntlet_Lamps", coll)


def markers(coll):
    for name, at in (("Marker_Grip", (0.0, 0.0, 0.0)), ("Marker_Muzzle", tuple(MUZZLE)),
                     ("Marker_Pilot", tuple(PILOT)), ("Marker_Gauge", tuple(GAUGE))):
        obj = bpy.data.objects.new(name, None)
        obj.empty_display_type = 'ARROWS'
        obj.empty_display_size = 0.05
        obj.location = Vector(at)
        coll.objects.link(obj)


def audit():
    bpy.context.view_layer.update()
    for o in bpy.data.objects:
        if o.type != 'MESH':
            continue
        bad = 0
        for v in o.data.vertices:
            q = o.matrix_world @ v.co
            if q.y > ENV_Y1 or q.z > ENV_Z1 or abs(q.x) > ENV_HX:
                bad += 1
            elif q.y < BASE_WRIST_EDGE and (q.y < REACH_Y0 or q.z < REACH_Z0 or abs(q.x) > REACH_HX):
                bad += 1
            elif q.y < 0.090 and q.z < COLLAR_Z + 0.004:
                bad += 1
        if bad:
            raise SystemExit("%s puts %d vertices out of bounds" % (o.name, bad))
    print("  audit: envelope and collar clear; mouth at y %.3f, z %.3f" % (MOUTH_Y, BORE_Z))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_GauntletFlame")

    cradle(coll, mats)
    burner(coll, mats)
    nozzle(coll, mats)
    shroud(coll, mats)
    tank(coll, mats)
    bracket(coll, mats)
    hose(coll, mats)
    cover(coll, mats)
    lamps(coll, mats)
    markers(coll)

    audit()
    save(out)
    report()
    for n in ("Marker_Grip", "Marker_Muzzle", "Marker_Pilot", "Marker_Gauge"):
        b = bpy.data.objects[n].location
        print("  %-13s blender (%.4f, %.4f, %.4f)  unity (%.4f, %.4f, %.4f)" % (n, *b, -b.x, b.z, -b.y))


if __name__ == "__main__":
    main()
