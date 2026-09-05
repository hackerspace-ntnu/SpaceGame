"""components/props/repair_bench — the crew's maintenance bench.

The lander's aft room had a gear wall and a map projector and nothing that said
"things get fixed here". This is the fixture the scrap-fed `RepairWorkstation`
lives on: a cabinet, a steel worktop, and — on the variations that have one — a
grinder spindle that spins once the machine is online, a status lamp, and a
fascia with a bezel the gauge is drawn into.

Three variations, distinct in silhouette rather than colour:

| Collection | Shape | Where |
|---|---|---|
| `Coll_RepairBench_Bulkhead` | bench with an upright back panel, 1.40 x 0.70 x 1.75 | against a hull wall — the lander's |
| `Coll_RepairBench_Island`   | open bench with a console pedestal, 1.20 x 0.80 x 1.30 | mid-floor, an outpost workshop |
| `Coll_RepairBench_Compact`  | tall locker with a fold-down leaf, 0.80 x 0.50 x 1.78 | a cramped cabin |

Each variation is several OBJECTS, not one mesh, because the game has to reach
its parts by name: the spindle spins (`spinningParts`), the lamp is retinted
(`statusLight` — so it is the only lit part and carries ONE material), and the
Compact's leaf hinges. Origins: the bench stands on z = 0 at the centre of its
footprint and faces -Y; the spindle's origin is the wheel centre on its axle
(local X), the lamp's is its base, the leaf's is its hinge line.

Material language is the lander's own cockpit kit: Panel_Grey carcass, dark
steel frames, white enamel fronts with safety-orange grips
(`console_panel_bridge`, `crew_seat_command`). Controls are the shared
`panel_control` builders, so indices 0-9 of MATS match theirs index-for-index.

No armature — see scrap_hopper.py for the reasoning; the same holds here.

    blender --background --python repair_bench.py -- --out repair_bench.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, os.path.join(_LIB, "components", "mechanical"))

from _buildlib import collection, link_materials, parse_out, report, save, start  # noqa: E402
from _tracked import TrackedPart  # noqa: E402
from panel_control import guarded_toggle, ribbed_knob, rocker_bank  # noqa: E402

from mathutils import Matrix  # noqa: E402

# 0-9 are panel_control's contract; extras follow. MATS[0] is structural metal
# because every bevel face lands on index 0.
STEEL, DARK, RUBBER, CHROME, CREAM, RED, BLUE, AMBER, BLACK, CRT = range(10)
GREY, ORANGE, WHITE, WARN = 10, 11, 12, 13
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Plastic_Rubber_Black", "Mat_Metal_Chrome_Scuffed",
        "Mat_Plastic_Cream_Aged", "Mat_Paint_Warn_Red",
        "Mat_Paint_Blue_Station", "Mat_Emissive_Amber",
        "Mat_Neutral_Black_Matte", "Mat_Emissive_Green_CRT",
        "Mat_Neutral_Panel_Grey", "Mat_Paint_Safety_Orange",
        "Mat_Paint_White_Arctic", "Mat_Emissive_Red_Warn"]

BEVEL_BODY = 0.004
BEVEL_CTRL = 0.0012


# ---------------------------------------------------------------------------
# Shared fittings
# ---------------------------------------------------------------------------

def grip_bar(p, centre, length, mat=ORANGE):
    """Horizontal pull bar on two stand-offs, proud of a -Y face at y0."""
    x0, y0, z0 = centre
    for sx in (-1, 1):
        p.box((x0 + sx * (length / 2 - 0.02), y0 - 0.012, z0),
              (0.014, 0.026, 0.014), DARK)
    p.cyl((x0, y0 - 0.028, z0), 0.011, length, 'X', 10, mat)


def louvre_vent(p, x0, x1, y_face, z0, z1, count=6):
    """Slats tilted about X on a -Y face — top edges in, bottom edges out."""
    pitch = (z1 - z0) / count
    rot = Matrix.Rotation(math.radians(35), 4, 'X')
    for i in range(count):
        p.box(((x0 + x1) / 2, y_face - 0.004, z0 + pitch * (i + 0.5)),
              (x1 - x0, 0.005, pitch * 1.25), BLACK, rot=rot)


def gauge_bezel(p, centre, width, height, y_face, depth=0.016):
    """Dark frame with a matte-black screen the runtime gauge is drawn on.
    The screen is embedded 2 mm into the frame; nothing here is coplanar."""
    x0, z0 = centre
    hard = [*p.box((x0, y_face - depth / 2 + 0.001, z0),
                   (width, depth, height), DARK)]
    frame_front = y_face - depth + 0.001
    p.box((x0, frame_front + 0.0015, z0),
          (width - 0.04, 0.009, height - 0.04), BLACK)     # 3 mm proud of the frame
    return hard


def lamp_collar(p, centre, top_z):
    """Dark collar the status lamp stands in, embedded 3 mm into `top_z`."""
    x0, y0 = centre
    p.cyl((x0, y0, top_z + 0.012), 0.045, 0.030, 'Z', 16, DARK)
    return top_z + 0.027


def status_lamp(coll, mats, name, base):
    """The one object the game retints. One material, nothing else on it."""
    x0, y0, z0 = base
    p = TrackedPart(mats)
    p.cyl((x0, y0, z0 + 0.028), 0.032, 0.060, 'Z', 16, WARN)
    p.cyl((x0, y0, z0 + 0.068), 0.032, 0.020, 'Z', 16, WARN, radius_top=0.012)
    return p.finish(name, coll, origin=base)


def spindle_rig(coll, mats, suffix, cx, y0, top):
    """Grinder motor on a pedestal with its wheel outboard on +X, as two
    objects: the housing (static) and the spindle (spins about local X)."""
    h = TrackedPart(mats)
    hard = []
    hard += h.box((cx, y0, top + 0.040), (0.14, 0.14, 0.090), DARK)     # pedestal
    h.cyl((cx, y0, top + 0.140), 0.090, 0.220, 'X', 20, DARK)           # motor can
    for dx in (-0.06, 0.0, 0.06):
        h.torus((cx + dx, y0, top + 0.140), 0.092, 0.006, 'X', 20, 8, DARK)
    h.cyl((cx + 0.120, y0, top + 0.140), 0.050, 0.020, 'X', 16, STEEL)  # end cap
    # Guard post rises off the end cap; the hood spans post and wheel.
    hard += h.box((cx + 0.130, y0, top + 0.205), (0.030, 0.020, 0.130), STEEL)
    hard += h.box((cx + 0.170, y0, top + 0.268), (0.120, 0.260, 0.012), DARK)  # hood
    # Tool rest in front of the wheel, on a bracket up from the worktop.
    hard += h.box((cx + 0.190, y0 - 0.045, top + 0.045), (0.020, 0.030, 0.100), DARK)
    hard += h.box((cx + 0.190, y0 - 0.045, top + 0.098), (0.050, 0.030, 0.010), STEEL)
    h.restamp()
    h.bevel(hard, width=BEVEL_BODY, segments=2)
    h.finish("Mesh_RepairBench_SpindleHousing_" + suffix, coll)

    wx, wz = cx + 0.190, top + 0.140
    s = TrackedPart(mats)
    s.cyl((cx + 0.150, y0, wz), 0.012, 0.100, 'X', 10, CHROME)          # axle
    s.cyl((wx, y0, wz), 0.100, 0.030, 'X', 24, GREY)                    # wheel
    s.cyl((wx, y0, wz), 0.028, 0.040, 'X', 16, CHROME)                  # hub
    s.cyl((wx + 0.026, y0, wz), 0.015, 0.012, 'X', 6, DARK)             # nut
    s.finish("Mesh_RepairBench_Spindle_" + suffix, coll, origin=(wx, y0, wz))


def vice(coll, mats, suffix, cx, cy, top):
    """Bench vice, jaws along Y, handle out the front."""
    p = TrackedPart(mats)
    hard = []
    hard += p.slab((cx - 0.08, cy - 0.08, top - 0.005), (cx + 0.08, cy + 0.08, top + 0.030), DARK)
    hard += p.box((cx, cy + 0.06, top + 0.075), (0.14, 0.05, 0.10), DARK)   # fixed jaw
    hard += p.box((cx, cy - 0.03, top + 0.075), (0.14, 0.05, 0.10), DARK)   # moving jaw
    p.box((cx, cy + 0.033, top + 0.075), (0.12, 0.006, 0.08), STEEL)
    p.box((cx, cy - 0.003, top + 0.075), (0.12, 0.006, 0.08), STEEL)
    for sx in (-1, 1):
        p.cyl((cx + sx * 0.05, cy, top + 0.065), 0.010, 0.24, 'Y', 10, STEEL)
    p.cyl((cx, cy - 0.09, top + 0.065), 0.012, 0.16, 'Y', 10, CHROME)     # screw
    p.cyl((cx, cy - 0.17, top + 0.065), 0.007, 0.16, 'X', 8, CHROME)      # handle
    for sx in (-1, 1):
        p.cyl((cx + sx * 0.085, cy - 0.17, top + 0.065), 0.011, 0.02, 'X', 8, RUBBER)
    p.restamp()
    p.bevel(hard, width=BEVEL_BODY, segments=2)
    p.finish("Mesh_RepairBench_Vice_" + suffix, coll)


def drawer_front(p, x0, x1, z0, z1, y_face):
    """White enamel front embedded 2 mm into the carcass behind it."""
    hard = [*p.slab((x0, y_face - 0.025, z0), (x1, y_face + 0.002, z1), WHITE)]
    grip_bar(p, ((x0 + x1) / 2, y_face - 0.025, (z0 + z1) / 2), (x1 - x0) * 0.5)
    return hard


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def bulkhead(coll, mats):
    """Wall-backed bench: cabinet, worktop, upright back panel, spindle, vice,
    lamp. Built to stand with its +Y face against a bulkhead."""
    W, D, TOP = 1.40, 0.70, 0.90
    hw, hd = W / 2, D / 2
    front = -hd + 0.03                       # carcass front, drawers stand proud

    p = TrackedPart(mats)
    hard = []
    hard += p.slab((-hw + 0.05, -hd + 0.06, 0.0), (hw - 0.05, hd - 0.02, 0.08), DARK)
    p.box((0.0, -hd + 0.059, 0.04), (W - 0.14, 0.004, 0.06), BLACK)          # kick strip
    # Carcass runs 1 cm up into the worktop and stops 5 mm short of its back
    # edge, so neither its top nor its back is coplanar with the top's.
    hard += p.slab((-hw, front, 0.08), (hw, hd - 0.005, TOP - 0.03), GREY)
    for x in (-hw + 0.02, 0.0, hw - 0.02):                                   # stiles
        hard += p.box((x, front - 0.004, (0.08 + TOP - 0.03) / 2), (0.04, 0.012, TOP - 0.11), DARK)
    hard += drawer_front(p, -0.66, -0.04, 0.11, 0.46, front)
    hard += drawer_front(p, -0.66, -0.04, 0.49, 0.83, front)
    hard += p.slab((0.04, front - 0.025, 0.11), (0.66, front + 0.002, 0.83), WHITE)  # door
    louvre_vent(p, 0.40, 0.60, front - 0.025, 0.66, 0.78)
    p.box((0.62, front - 0.034, 0.47), (0.016, 0.020, 0.16), ORANGE)         # latch bar
    p.box((0.20, front - 0.029, 0.78), (0.16, 0.010, 0.05), CREAM)           # label plate
    p.restamp()
    p.bevel(hard, width=BEVEL_BODY, segments=2)
    p.finish("Mesh_RepairBench_Cabinet_Bulkhead", coll)

    t = TrackedPart(mats)
    thard = []
    thard += t.slab((-hw - 0.02, -hd - 0.02, TOP - 0.04), (hw + 0.02, hd, TOP + 0.02), STEEL)
    thard += t.box((0.0, -hd - 0.025, TOP - 0.01), (W + 0.04, 0.02, 0.05), DARK)  # edge band
    t.box((0.41, -0.05, TOP + 0.022), (0.42, 0.50, 0.008), RUBBER)           # mat, right
    thard += t.box((0.0, hd - 0.01, TOP + 0.040), (W + 0.04, 0.02, 0.05), STEEL)  # upstand
    t.restamp()
    t.bevel(thard, width=BEVEL_BODY, segments=2)
    t.finish("Mesh_RepairBench_Worktop_Bulkhead", coll)

    b = TrackedPart(mats)
    bhard = []
    pf = hd - 0.12                                                            # panel front
    bhard += b.slab((-hw, pf, TOP + 0.01), (hw, hd - 0.005, 1.60), GREY)
    bhard += b.box((0.0, hd - 0.06, 1.615), (W + 0.04, 0.14, 0.04), DARK)    # top rail
    for sx in (-1, 1):
        bhard += b.box((sx * (hw + 0.01), hd - 0.06, 1.265), (0.04, 0.13, 0.71), DARK)
    bhard += gauge_bezel(b, (0.0, 1.42), 0.42, 0.20, pf)
    bhard += b.box((0.40, pf - 0.0065, 1.10), (0.44, 0.015, 0.10), DARK)     # control fascia
    ctrl = []
    cy = pf - 0.015                    # controls' own bezels 2-4 mm proud of the fascia
    ctrl += rocker_bank(b, (0.24, cy, 1.10), count=3, colours=(BLUE, RED, BLUE))
    ctrl += guarded_toggle(b, (0.40, cy, 1.10))
    ctrl += ribbed_knob(b, (0.54, cy, 1.10))
    louvre_vent(b, -0.60, -0.30, pf, 1.20, 1.50)
    b.cyl((-0.64, pf - 0.010, 1.26), 0.012, 0.56, 'Z', 10, RUBBER)          # conduit
    bhard += b.box((-0.64, pf - 0.012, 1.00), (0.06, 0.03, 0.08), DARK)      # junction box
    b.rivets((-0.60, pf - 0.001, 1.545), (0.60, pf - 0.001, 1.545), 8,
             radius=0.007, height=0.006, axis='Y', mat=STEEL)
    lamp_z = lamp_collar(b, (0.0, hd - 0.06), 1.635)
    b.restamp()
    b.bevel(bhard, width=BEVEL_BODY, segments=2)
    b.bevel(ctrl, width=BEVEL_CTRL, segments=2)
    b.finish("Mesh_RepairBench_BackPanel_Bulkhead", coll)

    status_lamp(coll, mats, "Mesh_RepairBench_StatusLamp_Bulkhead", (0.0, hd - 0.06, lamp_z))
    spindle_rig(coll, mats, "Bulkhead", cx=-0.50, y0=0.10, top=TOP + 0.02)
    vice(coll, mats, "Bulkhead", cx=0.42, cy=-0.16, top=TOP + 0.02)


def island(coll, mats):
    """Free-standing bench: no back panel, a console pedestal at the right
    end, the lamp on a mast at the back-left corner."""
    W, D, TOP = 1.20, 0.80, 0.90
    hw, hd = W / 2, D / 2
    front = -hd + 0.03

    p = TrackedPart(mats)
    hard = []
    hard += p.slab((-hw + 0.05, -hd + 0.06, 0.0), (hw - 0.05, hd - 0.06, 0.08), DARK)
    # Right half is a closed carcass with drawers; the left half is an OPEN bay
    # — back, end panel and a shelf — so the two ends read differently.
    hard += p.slab((0.0, front, 0.08), (hw, hd - 0.03, TOP - 0.03), GREY)
    hard += p.slab((-hw, hd - 0.06, 0.08), (0.0, hd - 0.03, TOP - 0.03), GREY)      # bay back
    hard += p.slab((-hw, front, 0.08), (-hw + 0.02, hd - 0.03, TOP - 0.03), GREY)   # bay end
    hard += p.slab((-hw + 0.02, front + 0.02, 0.46), (0.0, hd - 0.06, 0.48), STEEL)  # shelf
    hard += p.box((-0.30, 0.0, 0.548), (0.30, 0.22, 0.14), CREAM)            # a case on the shelf
    for x in (-hw + 0.02, 0.0, hw - 0.02):
        hard += p.box((x, front - 0.004, (0.08 + TOP - 0.03) / 2), (0.04, 0.012, TOP - 0.11), DARK)
    for z0, z1 in ((0.11, 0.33), (0.36, 0.58), (0.61, 0.83)):                 # right: 3 drawers
        hard += drawer_front(p, 0.04, 0.56, z0, z1, front)
    p.restamp()
    p.bevel(hard, width=BEVEL_BODY, segments=2)
    p.finish("Mesh_RepairBench_Cabinet_Island", coll)

    t = TrackedPart(mats)
    thard = []
    thard += t.slab((-hw - 0.02, -hd - 0.02, TOP - 0.04), (hw + 0.02, hd + 0.02, TOP + 0.02), STEEL)
    for sy in (-1, 1):
        thard += t.box((0.0, sy * (hd + 0.025), TOP - 0.01), (W + 0.04, 0.02, 0.05), DARK)
    t.box((-0.30, 0.0, TOP + 0.022), (0.50, 0.50, 0.008), RUBBER)
    t.restamp()
    t.bevel(thard, width=BEVEL_BODY, segments=2)
    t.finish("Mesh_RepairBench_Worktop_Island", coll)

    c = TrackedPart(mats)
    chard = []
    chard += c.box((0.44, 0.22, TOP + 0.185), (0.30, 0.30, 0.34), DARK)     # pedestal
    chard += c.box((0.44, 0.062, TOP + 0.21), (0.28, 0.02, 0.28), GREY)     # fascia
    chard += gauge_bezel(c, (0.44, TOP + 0.26), 0.22, 0.12, 0.052)
    ctrl = []
    ctrl += rocker_bank(c, (0.39, 0.052 - 0.001, TOP + 0.12), count=3, colours=(BLUE, RED, BLUE))
    ctrl += guarded_toggle(c, (0.52, 0.052 - 0.001, TOP + 0.12))
    c.cyl((-0.50, 0.30, TOP + 0.15), 0.012, 0.30, 'Z', 10, STEEL)          # lamp mast
    lamp_z = lamp_collar(c, (-0.50, 0.30), TOP + 0.30)
    c.restamp()
    c.bevel(chard, width=BEVEL_BODY, segments=2)
    c.bevel(ctrl, width=BEVEL_CTRL, segments=2)
    c.finish("Mesh_RepairBench_Console_Island", coll)

    status_lamp(coll, mats, "Mesh_RepairBench_StatusLamp_Island", (-0.50, 0.30, lamp_z))
    spindle_rig(coll, mats, "Island", cx=-0.46, y0=0.12, top=TOP + 0.02)
    vice(coll, mats, "Island", cx=0.10, cy=-0.20, top=TOP + 0.02)


def compact(coll, mats):
    """Tall locker unit with a fold-down work leaf, for a cabin with no room
    for a bench. No spindle: nothing that spins fits in a locker."""
    W, D, H = 0.80, 0.50, 1.75
    hw = W / 2
    front, back = -0.22, 0.28

    p = TrackedPart(mats)
    hard = []
    hard += p.slab((-hw + 0.04, front + 0.05, 0.0), (hw - 0.04, back - 0.02, 0.08), DARK)
    hard += p.slab((-hw, front, 0.08), (hw, back, H), GREY)
    for x in (-hw + 0.02, hw - 0.02):
        hard += p.box((x, front - 0.004, (0.08 + H) / 2), (0.04, 0.012, H - 0.08), DARK)
    hard += p.slab((-0.36, front - 0.025, 0.11), (0.36, front + 0.002, 0.84), WHITE)   # lower door
    p.box((0.32, front - 0.034, 0.47), (0.016, 0.020, 0.16), ORANGE)
    hard += p.slab((-0.36, front - 0.025, 1.06), (0.36, front + 0.002, 1.68), WHITE)   # upper door
    louvre_vent(p, -0.28, 0.28, front - 0.025, 1.40, 1.62)
    p.box((-0.32, front - 0.034, 1.30), (0.016, 0.020, 0.16), ORANGE)
    hard += p.box((0.0, front - 0.008, 0.95), (W - 0.08, 0.018, 0.16), DARK)          # fascia band
    hard += gauge_bezel(p, (-0.16, 0.95), 0.26, 0.12, front - 0.016)
    ctrl = []
    ctrl += rocker_bank(p, (0.14, front - 0.017, 0.95), count=2, colours=(BLUE, RED))
    ctrl += guarded_toggle(p, (0.28, front - 0.017, 0.95))
    hard += p.box((0.0, front - 0.012, 0.865), (W - 0.06, 0.03, 0.03), STEEL)         # hinge rail
    lamp_z = lamp_collar(p, (0.0, 0.03), H)
    p.restamp()
    p.bevel(hard, width=BEVEL_BODY, segments=2)
    p.bevel(ctrl, width=BEVEL_CTRL, segments=2)
    p.finish("Mesh_RepairBench_Cabinet_Compact", coll)

    # Leaf hinged on the rail, shown deployed: out over -Y, stays folded down.
    hinge = (0.0, front - 0.027, 0.865)
    leaf = TrackedPart(mats)
    lhard = []
    lhard += leaf.slab((-0.36, front - 0.43, 0.855), (0.36, front - 0.022, 0.875), WHITE)
    lhard += leaf.box((0.0, front - 0.435, 0.865), (0.72, 0.02, 0.03), STEEL)         # front edge
    leaf.cyl((0.0, front - 0.027, 0.862), 0.011, 0.66, 'X', 10, STEEL)                # barrel
    # Folding stays from under the leaf's far half down to the cabinet front.
    # +45 about X carries local +Z toward -Y, so the high end lands OUT on the
    # leaf and the low end on the cabinet (the library's Y/X sign trap, checked).
    stay = Matrix.Rotation(math.radians(45), 4, 'X')
    for sx in (-1, 1):
        lhard += leaf.box((sx * 0.30, front - 0.14, 0.715), (0.016, 0.016, 0.41), DARK, rot=stay)
    leaf.restamp()
    leaf.bevel(lhard, width=BEVEL_BODY, segments=2)
    leaf.finish("Mesh_RepairBench_Leaf_Compact", coll, origin=hinge)

    status_lamp(coll, mats, "Mesh_RepairBench_StatusLamp_Compact", (0.0, 0.03, lamp_z))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    bulkhead(collection("Coll_RepairBench_Bulkhead"), mats)
    island(collection("Coll_RepairBench_Island"), mats)
    compact(collection("Coll_RepairBench_Compact"), mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
