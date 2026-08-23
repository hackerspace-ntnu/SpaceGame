"""Handheld CRT terminals — carried instruments with a live screen.

Four variations of the same idea: a cream-cased field instrument built around a
green phosphor display, in the salvaged-military language the rest of the
library speaks. `Scanner` is the hero and ships to the game as the item scanner;
the other three were built ahead.

Two things about these files are unusual and deliberate:

**The screen is its own object, not a face on the case.** Every variation emits
`Mesh_Terminal_<var>_Case` and `Mesh_Terminal_<var>_Screen`. The display has to
be a separate renderer in Unity or the scanner's radar shader would repaint the
whole device, and a separate object is also the only way a
`MaterialPropertyBlock` can address the screen alone.

**The screen carries UVs; nothing else in this library does.** `_buildlib` never
writes a UV layer — none of the vehicle and building components need one, since
they are shaded by flat palette materials. A procedural display shader is
addressed in 0..1 screen space, so without UVs every fragment samples (0,0) and
the display renders as a single flat colour. `planar_uv()` below is the fix and
is applied to the screen plates only.

The `Scanner` variation additionally splits out `_Dial` and `_Antenna`, each
with its origin on its own axis of motion, so the game can spin the knob and
whip the antenna without a rig. Rigid mechanical parts as separate objects beat
an armature here: nothing deforms, and a bone hierarchy in the FBX would buy
complexity Unity has to unpick.

Control hardware comes from `components/mechanical/panel_control.py` — see its
docstring for why the builders are imported rather than the .blend appended.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_COMPONENTS = os.path.dirname(_HERE)
sys.path.insert(0, os.path.dirname(_COMPONENTS))
sys.path.insert(0, os.path.join(_COMPONENTS, "mechanical"))

from _buildlib import *  # noqa: E402,F403
from panel_control import (  # noqa: E402
    connector_strip, guarded_toggle, ribbed_knob, rocker_bank,
    rotary_selector, tube_path)

from mathutils import Matrix, Vector  # noqa: E402

# Must match panel_control.MATS index-for-index: its builders write material
# indices, not names, so a divergence here silently repaints every knob.
# Index 0 is STEEL because `bmesh.ops.bevel` stamps new faces with index 0.
STEEL, DARK, RUBBER, CHROME, CREAM, RED, BLUE, AMBER, BLACK, CRT = range(10)
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Plastic_Rubber_Black", "Mat_Metal_Chrome_Scuffed",
        "Mat_Plastic_Cream_Aged", "Mat_Paint_Warn_Red",
        "Mat_Paint_Blue_Station", "Mat_Emissive_Amber",
        "Mat_Neutral_Black_Matte", "Mat_Emissive_Green_CRT"]

BEVEL_W = 0.0014


# --------------------------------------------------------------------------
# Screen plates
# --------------------------------------------------------------------------

def planar_uv(obj, u_axis=0, v_axis=2, flip_u=False):
    """Give `obj` a 0..1 planar UV layer projected along its thin axis.

    Mapped from the mesh's own bounding box, so the display's front face fills
    exactly 0..1 in both directions no matter where the plate ended up. The rim
    faces get degenerate strips, which is correct: they are never seen and a
    shader drawing 0..1 content on them would only leak colour onto the bezel.
    """
    mesh = obj.data
    co = [v.co for v in mesh.vertices]
    lo = Vector((min(c[i] for c in co) for i in range(3)))
    hi = Vector((max(c[i] for c in co) for i in range(3)))
    span = Vector((max(hi[i] - lo[i], 1e-6) for i in range(3)))

    uv = mesh.uv_layers.new(name="UVMap")
    for loop in mesh.loops:
        c = mesh.vertices[loop.vertex_index].co
        u = (c[u_axis] - lo[u_axis]) / span[u_axis]
        uv.data[loop.index].uv = (1.0 - u if flip_u else u,
                                  (c[v_axis] - lo[v_axis]) / span[v_axis])
    return obj


def screen_plate(coll, mats, name, lo, hi, origin=(0, 0, 0)):
    """A flat display plate spanning `lo`..`hi`, facing -Y.

    Deliberately not bevelled: a bevel would fold new faces into the UV island
    and pull the display's own edge pixels around the rim.
    """
    p = Part(mats)
    p.slab(lo, hi, CRT)
    return planar_uv(p.finish(name, coll, origin))


# --------------------------------------------------------------------------
# Shared case furniture
# --------------------------------------------------------------------------

def _bezel(p, x0, x1, z0, z1, y_face, rim=0.007, depth=0.008):
    """A raised frame around a display aperture, with a black cavity behind it.

    The cavity matters more than the frame: without a recess the screen plate
    floats on the surface and the device reads as a sticker on a box.
    """
    hard = []
    y_out = y_face - depth
    hard += p.slab((x0 - rim, y_face, z0 - rim), (x1 + rim, y_out, z0), CREAM)
    hard += p.slab((x0 - rim, y_face, z1), (x1 + rim, y_out, z1 + rim), CREAM)
    hard += p.slab((x0 - rim, y_face, z0), (x0, y_out, z1), CREAM)
    hard += p.slab((x1, y_face, z0), (x1 + rim, y_out, z1), CREAM)
    hard += p.slab((x0, y_face, z0), (x1, y_face + 0.008, z1), BLACK)
    # Chrome bead around the aperture — the highlight that reads as glass edge.
    for a, b in (((x0, z0 - 0.0018), (x1, z0 - 0.0018)),
                 ((x0, z1 + 0.0018), (x1, z1 + 0.0018))):
        p.box(((a[0] + b[0]) / 2, y_out + 0.0012, a[1]),
              (x1 - x0 + rim * 1.4, 0.0022, 0.0022), CHROME)
    return hard


def _grille(p, lo, hi, bars=7, mat_bar=DARK):
    """A slotted vent panel: a black well with bars across it.

    Cheaper and crisper than `Part.louvres`, which angles its slats for a
    0.6 m vehicle vent and turns to mush at 0.05 m.
    """
    lo, hi = Vector(lo), Vector(hi)
    hard = list(p.slab(lo, hi, BLACK))
    span = hi.y - lo.y
    for i in range(bars):
        y = lo.y + span * (i + 0.5) / bars
        hard += p.box(((lo.x + hi.x) / 2, y, hi.z),
                      (hi.x - lo.x, span / bars * 0.55, 0.0026), mat_bar)
    return hard


def _coil_cable(p, at, turns=9, major=0.011, minor=0.0028, pitch=0.0062):
    """A strain-relief cable coil. Rings rather than a helix — at 3 mm nobody
    can tell, and a real swept helix costs four times the triangles."""
    x0, y0, z0 = at
    for i in range(turns):
        p.torus((x0, y0 + math.sin(i * 1.3) * 0.0006, z0 + i * pitch),
                major, minor, 'X', 10, 5, RUBBER)


def _side_connector(p, at, dots=5, pitch=0.0055, lamp=AMBER):
    """`connector_strip` turned to face -X, for a device flank."""
    x0, y0, z0 = at
    h = pitch * dots + 0.005
    hard = list(p.slab((x0 - 0.005, y0 - 0.010, z0 - h / 2),
                       (x0 + 0.006, y0 + 0.010, z0 + h / 2), STEEL))
    hard += p.slab((x0 - 0.0062, y0 - 0.0078, z0 - h / 2 + 0.0018),
                   (x0 - 0.0042, y0 + 0.0078, z0 + h / 2 - 0.0018), BLACK)
    for i in range(dots):
        z = z0 + (i - (dots - 1) / 2.0) * pitch
        p.cyl((x0 - 0.0056, y0, z), 0.0015, 0.004, 'X', 6, lamp)
    return hard


# --------------------------------------------------------------------------
# Scanner — the hero variation
# --------------------------------------------------------------------------

def scanner(coll, mats):
    """Held scanner unit: bail handle, whip mast, big CRT, control deck.

    The proportions come from the reference, with one deliberate departure: the
    screen is sized for a player looking down at it mid-game, not for a
    photograph. It fills 0.116 x 0.094 m — about 78% of the instrument face, and
    a little over twice the area a faithful copy of the reference bezel would
    give. The controls lost the room: the deck under the screen is a 22 mm strip
    rather than the reference's deep panel, and the card slot went with it.

    Everything else is the reference. The back housing carries the handle and
    the grille and steps left and rearward of the face; the cable coil and
    connectors are on the left flank only, which is what stops the thing reading
    as a symmetrical toy.
    """
    p = Part(mats)
    hard = []

    # Back housing. Sits left and rearward of the instrument face, so the two
    # boxes step rather than stack.
    hard += p.slab((-0.086, -0.008, 0.004), (0.030, 0.052, 0.144), CREAM)
    hard += p.slab((-0.088, 0.004, 0.024), (-0.078, 0.044, 0.124), STEEL)

    # Instrument face — the front block carrying screen and controls.
    hard += p.slab((-0.066, -0.050, 0.000), (0.072, 0.006, 0.150), CREAM)
    # Corner posts, so the front block reads as a chassis with panels in it.
    for sx, sz in ((-0.062, 0.006), (0.068, 0.006), (-0.062, 0.144),
                   (0.068, 0.144)):
        hard += p.box((sx, -0.048, sz), (0.010, 0.010, 0.014), STEEL)

    # Screen aperture and cavity — the whole point of this variation.
    hard += _bezel(p, -0.054, 0.062, 0.046, 0.140, -0.050, rim=0.006)
    p.rivets((-0.062, -0.052, 0.042), (-0.062, -0.052, 0.144), 5,
             radius=0.0018, height=0.0026, axis='Y', mat=CHROME)
    p.rivets((0.070, -0.052, 0.042), (0.070, -0.052, 0.144), 5,
             radius=0.0018, height=0.0026, axis='Y', mat=CHROME)

    # Ribbed thumb-slider in the gap between screen and deck. The reference's
    # card slot lived here and is gone: at this screen size there is 6 mm of
    # face left, and a slot drawn into it reads as a scratch.
    for i in range(9):
        p.box((-0.014 + i * 0.0036, -0.0535, 0.0345), (0.0024, 0.004, 0.007),
              CHROME)
    hard += p.slab((-0.024, -0.0525, 0.030), (0.030, -0.049, 0.039), DARK)

    # Control deck. Rockers left, selector centre; the big knob is its own
    # object so the game can spin it.
    hard += p.slab((-0.062, -0.052, 0.004), (0.068, -0.047, 0.026), DARK)
    hard += rocker_bank(p, (-0.040, -0.049, 0.015), count=3,
                        colours=(BLUE, RED, BLUE), pitch=0.0115,
                        width=0.0085, height=0.013)
    hard += rotary_selector(p, (0.000, -0.049, 0.014), radius=0.0080)
    hard += p.slab((-0.064, -0.052, 0.026), (0.070, -0.048, 0.030), RED)

    # Left flank: strain-relief coil, connectors, guard rails. All of it on one
    # side on purpose — see the docstring.
    _coil_cable(p, (-0.090, 0.024, 0.058), turns=9)
    hard += _side_connector(p, (-0.086, -0.030, 0.104), dots=5)
    hard += _side_connector(p, (-0.086, -0.030, 0.056), dots=5, lamp=RED)
    tube_path(p, [(-0.094, -0.036, 0.016), (-0.094, -0.036, 0.132)],
              0.0032, STEEL, seg=6, joint=False)
    tube_path(p, [(-0.094, -0.014, 0.016), (-0.094, -0.014, 0.132)],
              0.0032, STEEL, seg=6, joint=False)

    # Top: vent grille over the back housing, and the bail handle above it.
    hard += _grille(p, (-0.070, 0.004, 0.138), (-0.014, 0.048, 0.145), bars=6)
    handle_x = -0.052
    tube_path(p, [(handle_x, -0.004, 0.142), (handle_x, -0.012, 0.162),
                  (handle_x, -0.008, 0.175), (handle_x, 0.044, 0.179),
                  (handle_x, 0.054, 0.168), (handle_x, 0.050, 0.146)],
              0.0048, STEEL, seg=8)
    for sy in (-0.004, 0.050):
        hard += p.box((handle_x, sy, 0.142), (0.014, 0.012, 0.010), CHROME)

    # Right flank: fire button and the antenna's mount boss.
    p.cyl((0.074, -0.020, 0.082), 0.0075, 0.008, 'X', 12, RED)
    p.cyl((0.074, -0.020, 0.082), 0.0105, 0.004, 'X', 12, CHROME)
    hard += p.box((0.056, 0.030, 0.146), (0.026, 0.026, 0.010), STEEL)
    p.cyl((0.056, 0.030, 0.150), 0.0088, 0.012, 'Z', 10, RUBBER)

    # Status lamps below the deck, and a hazard stencil on the base. They moved
    # off the face when the screen took it: a lamp beside a full-width bezel is
    # a lamp on top of the picture.
    for i, mat in enumerate((AMBER, CRT, RED)):
        p.cyl((-0.052 + i * 0.012, -0.0525, 0.0015), 0.0028, 0.005, 'Y', 8, mat)
    hard += p.slab((0.006, -0.051, -0.0005), (0.050, -0.049, 0.0035), RED)

    p.bevel(hard, width=BEVEL_W, segments=2)
    case = p.finish("Mesh_Terminal_Scanner_Case", coll)

    screen_plate(coll, mats, "Mesh_Terminal_Scanner_Screen",
                 (-0.0525, -0.0505, 0.0465), (0.0605, -0.0485, 0.1385))

    # Knob and mast, each with its origin on its own axis of motion.
    knob = Part(mats)
    knob.bevel(ribbed_knob(knob, (0.046, -0.048, 0.014), radius=0.0132,
                           depth=0.022, ribs=16),
               width=BEVEL_W, segments=2)
    knob.finish("Mesh_Terminal_Scanner_Dial", coll, origin=(0.046, -0.048, 0.014))

    mast = Part(mats)
    base = (0.056, 0.030, 0.148)
    mast.cyl((base[0], base[1], base[2] + 0.006), 0.0062, 0.016, 'Z', 10, RUBBER)
    tube_path(mast, [(base[0], base[1], base[2] + 0.010),
                     (base[0] + 0.004, base[1] - 0.004, base[2] + 0.060),
                     (base[0] + 0.011, base[1] - 0.012, base[2] + 0.104),
                     (base[0] + 0.020, base[1] - 0.022, base[2] + 0.140)],
              0.0042, RUBBER, seg=6, taper=0.42)
    mast.cyl((base[0] + 0.020, base[1] - 0.022, base[2] + 0.142), 0.0028,
             0.006, 'Z', 8, CHROME)
    mast.finish("Mesh_Terminal_Scanner_Antenna", coll, origin=base)
    return case


# --------------------------------------------------------------------------
# Variations built ahead
# --------------------------------------------------------------------------

def compact(coll, mats):
    """Palm-sized readout: no handle, no mast, one dial. The pocket instrument
    a scavenger carries when the scanner is too much to hold."""
    p = Part(mats)
    hard = []

    # Rounded shell, lofted so the silhouette is a wedge rather than a brick —
    # this is the variation that has to look nothing like the Scanner.
    prof = [(-0.036, 0.000), (0.036, 0.000), (0.040, 0.016), (0.036, 0.062),
            (-0.036, 0.062), (-0.040, 0.016)]
    hard += p.loft([(-0.024, prof), (-0.019, [(u * 1.04, v) for u, v in prof]),
                    (0.014, [(u * 1.04, v) for u, v in prof]),
                    (0.019, prof)], axis='Y', mat=CREAM)

    hard += _bezel(p, -0.028, 0.028, 0.030, 0.056, -0.024, rim=0.005,
                   depth=0.006)
    hard += p.slab((-0.032, -0.026, 0.006), (0.032, -0.021, 0.024), DARK)
    hard += rocker_bank(p, (-0.016, -0.023, 0.015), count=2,
                        colours=(BLUE, RED), pitch=0.011, width=0.008,
                        height=0.013)
    hard += ribbed_knob(p, (0.017, -0.022, 0.015), radius=0.010, depth=0.016,
                        ribs=12)
    hard += connector_strip(p, (0.000, -0.022, 0.062), rows=1, dots=4,
                            pitch=0.005)
    p.cyl((0.030, -0.024, 0.048), 0.0032, 0.005, 'Y', 8, AMBER)
    p.rivets((-0.034, -0.024, 0.004), (0.034, -0.024, 0.004), 4,
             radius=0.0016, height=0.002, axis='Y', mat=CHROME)

    p.bevel(hard, width=BEVEL_W, segments=2)
    case = p.finish("Mesh_Terminal_Compact_Case", coll)
    screen_plate(coll, mats, "Mesh_Terminal_Compact_Screen",
                 (-0.0265, -0.0245, 0.0315), (0.0265, -0.0225, 0.0545))
    return case


def rugged(coll, mats):
    """Armoured clamshell with the lid propped open over the screen.

    The open lid is the whole silhouette — it gives the variation a diagonal
    nothing else in the family has, and reads instantly as 'the tough one'.
    """
    p = Part(mats)
    hard = []

    hard += p.slab((-0.062, -0.038, 0.000), (0.062, 0.040, 0.086), CREAM)
    # Corner bumpers.
    for sx in (-1, 1):
        for sy in (-1, 1):
            hard += p.box((sx * 0.060, sy * 0.036, 0.043),
                          (0.014, 0.014, 0.090), RUBBER)
    hard += _bezel(p, -0.040, 0.040, 0.030, 0.072, -0.038, rim=0.006)

    # Lid, hinged along the top edge and propped back.
    lid = Matrix.Rotation(math.radians(-62), 4, 'X')
    hard += p.box((0.000, -0.044, 0.108), (0.116, 0.070, 0.010), CREAM,
                  rot=lid)
    for sx in (-1, 1):
        p.cyl((sx * 0.050, -0.038, 0.078), 0.0062, 0.014, 'X', 10, CHROME)
    tube_path(p, [(-0.050, -0.038, 0.078), (-0.050, -0.062, 0.096)],
              0.0028, STEEL, seg=6, joint=False)
    tube_path(p, [(0.050, -0.038, 0.078), (0.050, -0.062, 0.096)],
              0.0028, STEEL, seg=6, joint=False)

    hard += p.slab((-0.056, -0.040, 0.006), (0.056, -0.035, 0.026), DARK)
    hard += guarded_toggle(p, (-0.034, -0.037, 0.016))
    hard += rotary_selector(p, (0.000, -0.037, 0.014), radius=0.0080)
    hard += ribbed_knob(p, (0.036, -0.036, 0.014), radius=0.0125, depth=0.020,
                        ribs=14)
    hard += _side_connector(p, (-0.062, -0.010, 0.052), dots=4)
    hard += _grille(p, (-0.030, -0.020, 0.082), (0.030, 0.024, 0.088), bars=5)

    p.bevel(hard, width=BEVEL_W, segments=2)
    case = p.finish("Mesh_Terminal_Rugged_Case", coll)
    screen_plate(coll, mats, "Mesh_Terminal_Rugged_Screen",
                 (-0.0385, -0.0385, 0.0315), (0.0385, -0.0365, 0.0705))
    return case


def wrist(coll, mats):
    """Low-profile wrist plate with a wide strip display and no protrusions.

    Built ahead for anything that needs a readout worn under a sleeve — a suit
    telemetry cuff, a dosimeter — where the Scanner's bulk is wrong.
    """
    p = Part(mats)
    hard = []

    # Curved to sit on a forearm: a shallow arc of plates rather than a slab.
    for i in range(7):
        a = math.radians(-48 + i * 16)
        hard += p.box((math.sin(a) * 0.046, -math.cos(a) * 0.046 + 0.046,
                       0.000), (0.015, 0.012, 0.092), CREAM,
                      rot=Matrix.Rotation(-a, 4, 'Z'))
    hard += p.slab((-0.040, -0.006, -0.004), (0.040, 0.008, 0.096), CREAM)
    hard += _bezel(p, -0.034, 0.034, 0.040, 0.066, -0.006, rim=0.005,
                   depth=0.005)
    hard += p.slab((-0.036, -0.008, 0.012), (0.036, -0.004, 0.030), DARK)
    hard += rocker_bank(p, (-0.014, -0.005, 0.021), count=4,
                        colours=(BLUE, RED, BLUE, AMBER), pitch=0.0092,
                        width=0.0068, height=0.012)
    hard += ribbed_knob(p, (0.026, -0.004, 0.021), radius=0.0085, depth=0.013,
                        ribs=10)
    hard += connector_strip(p, (0.000, -0.005, 0.080), rows=2, dots=6,
                            pitch=0.0052)
    for sx in (-1, 1):
        hard += p.box((sx * 0.044, 0.020, 0.046), (0.012, 0.052, 0.016),
                      RUBBER)

    p.bevel(hard, width=BEVEL_W, segments=2)
    case = p.finish("Mesh_Terminal_Wrist_Case", coll)
    screen_plate(coll, mats, "Mesh_Terminal_Wrist_Screen",
                 (-0.0325, -0.0075, 0.0415), (0.0325, -0.0055, 0.0645))
    return case


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    scanner(collection("Coll_Terminal_Scanner"), mats)
    compact(collection("Coll_Terminal_Compact"), mats)
    rugged(collection("Coll_Terminal_Rugged"), mats)
    wrist(collection("Coll_Terminal_Wrist"), mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
