"""CRT monitors — the display head of the console family.

Four variations of one idea: a moulded cream housing with a rounded-rectangle
bezel, a black well and a flat green screen plate set into it. The language is
a cassette-futurism desktop terminal reference — greige mouldings, a column of
backlit orange keys beside the tube, a floppy slot and a lamp bar under it —
rebuilt at the sizes the game needs.

`Kiosk` is the hero (the standing terminal's head) and the one the request
needed. The rest were built ahead:

    Kiosk   0.60 x 0.45  4:3   key column, floppy slot, lamp bar, vent
    Desk    0.32 x 0.24  4:3   the reference's own monitor, for desks and cabins
    Wide    0.80 x 0.45  16:9  symmetric bezel, lamp bar, rocker bank
    Radar   0.45 x 0.45  1:1   range dial on a wide right rim; the dial spins

The screen is built for being LOOKED AT up close, not for a photograph. It is
a flat plate rather than a curved tube, it takes most of the fascia, and its
UV rectangle is exactly its own bounds, so whatever Unity draws on it — a
render texture, a procedural shader like `ItemScannerScreen` — maps 1:1 with
no distortion (GDC-L1-UX-0003: the display is the most salient thing on the
housing; everything else is quieter and smaller).

Three objects per variation, each its own renderer:

    Mesh_CrtMonitor_<V>_Housing   body, bezel, well liner, slot, vent, buttons
    Mesh_CrtMonitor_<V>_Screen    the display plate — the only mesh with UVs;
                                  origin at the CENTRE of its front face
    Mesh_CrtMonitor_<V>_Lit       backlit keys and status lamps in one
                                  renderer, so the game can dim or blink them

`Radar` adds `_Dial`, origin on its own axis, the way the item scanner's knob
is done: the game turns it without a rig.

Frame: origin at the centre of the housing's bottom face, screen facing -Y.
The housing spans y -D/2 .. +D/2 - 0.01: the back gives up 10 mm so a pedestal
of the same depth can stand under it without sharing its back plane.

Overscan: the plate is 2.5 mm smaller than the bezel aperture per side and
buried 1.5 mm into the black liner, whose rounded inner corners hide the
plate's square ones. Content in the outermost ~0.4% of the UV rectangle and in
the extreme corners is under the liner — a CRT's own overscan, and the price
of a well with no seam in it.

    blender --background --python crt_monitor.py -- --out crt_monitor.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, _HERE)

from _console_kit import *  # noqa: E402,F403
from _console_kit import EMBED, MATS  # noqa: E402
from _buildlib import collection, link_materials, parse_out, report, save, start  # noqa: E402
from handheld_terminal import screen_plate  # noqa: E402
from panel_control import ribbed_knob, rocker_bank, rotary_selector  # noqa: E402

FASCIA_T = 0.040        # bezel depth, front face to back face
PLATE_RECESS = 0.016    # screen face behind the bezel front
APERTURE_R = 0.014      # bezel aperture corner radius
LINER_IN = 0.004        # liner reaches this far into the aperture
PLATE_MARGIN = 0.0025   # plate edge inside the aperture edge


def screen_face_y(depth):
    """Where the screen face sits relative to the housing origin — exported so
    the model that mounts the head can place a marker by arithmetic."""
    return -depth / 2.0 + PLATE_RECESS


def monitor(coll, mats, tag, sw, sh, rim_l, rim_r, rim_t, lip, depth,
            key_column=False, key_size=0.032, corner_r=0.060,
            rockers=False, dial=False):
    """Build one variation. Returns the housing object.

    `sw`/`sh` are the aperture (the visible screen), the rims are the bezel
    widths around it, `lip` the bezel height under the screen that carries
    the slot and lamps, `depth` the housing front to back.
    """
    W = rim_l + sw + rim_r
    x0, x1 = -W / 2.0, W / 2.0
    zf0, zf1 = 0.006, lip + sh + rim_t
    sx0, sx1 = x0 + rim_l, x0 + rim_l + sw
    sz0, sz1 = lip, lip + sh
    yf = -depth / 2.0                        # bezel front
    yb = depth / 2.0 - 0.010                 # body back
    y_face = screen_face_y(depth)            # screen plate front
    s = min(1.0, sw / 0.60)                  # lip hardware scales with the tube

    p = TrackedPart(mats)
    hard, fine = [], []

    # Body: a prism along X, 10 mm inside the bezel outline on every side so the
    # bezel reads as a plate standing proud of a housing. Taller at the front,
    # the top slopes back and chamfers down to the rear face.
    inset = 0.010
    y_body = yf + FASCIA_T - inset
    L = yb - y_body
    zt = zf1 - inset
    prof = [(y_body, 0.0), (yb, 0.0), (yb, zt - 0.35 * L),
            (yb - 0.12 * L, zt - 0.20 * L), (y_body, zt)]
    hard += p.prism(prof, W - 2 * inset, axis='X', mat=CREAM)

    # Bezel and well liner — one ring each, see `_console_kit.rounded_frame`.
    hard += rounded_frame(p, (x0, x1, zf0, zf1, corner_r),
                          (sx0, sx1, sz0, sz1, APERTURE_R),
                          yf, yf + FASCIA_T, CREAM, seg=6)
    fine += rounded_frame(p, (sx0 - EMBED, sx1 + EMBED, sz0 - EMBED,
                              sz1 + EMBED, APERTURE_R + EMBED),
                          (sx0 + LINER_IN, sx1 - LINER_IN, sz0 + LINER_IN,
                           sz1 - LINER_IN, APERTURE_R - LINER_IN),
                          yf + 0.004, y_face + 0.010, BLACK, seg=6)

    lit = TrackedPart(mats)
    lit_fine = []

    # Key column on the left rim: four backlit squares over a round dark button.
    if key_column:
        xk = x0 + rim_l / 2.0
        pitch = key_size + 0.024
        for i in range(4):
            z = sz1 - 0.07 * s - i * pitch
            fine += p.box((xk, yf, z),
                          (key_size + 0.012, 2 * EMBED, key_size + 0.012), DARK)
            lit_fine += square_key(lit, xk, z, key_size, yf, AMBER)
        zb = sz0 + 0.06 * s
        p.cyl((xk, yf - 0.001, zb), key_size * 0.85, 0.008, 'Y', 20, CHROME)
        p.cyl((xk, yf - 0.0055, zb), key_size * 0.65, 0.021, 'Y', 20, DARK)
        p.rivets((xk, yf, zf0 + 0.02), (xk, yf, zf1 - 0.02), 2,
                 radius=0.004, height=0.004, axis='Y', mat=CHROME)

    # Lower lip: floppy slot on the left, lamp bar centred under the tube,
    # vent on the right. On the reference these sit under the monitor; here
    # they are what stops the lip reading as a blank strip.
    z_mid = lip * 0.55
    slot_w = 0.16 * s
    slot_x0 = sx0 + 0.02 * s
    fine += slot(p, slot_x0, slot_x0 + slot_w, z_mid - 0.005, z_mid + 0.005, yf)
    fine += p.box((slot_x0 + slot_w / 2.0, yf - 0.002, z_mid - 0.013),
                  (slot_w + 0.010, 0.010, 0.005), CHROME)
    p.cyl((slot_x0 + slot_w + 0.020, yf - 0.004, z_mid), 0.006, 0.014, 'Y',
          10, ORANGE)

    bar_w = 0.16 * s
    xc = (sx0 + sx1) / 2.0 + 0.02 * s
    fine += p.slab((xc - bar_w / 2.0, yf + EMBED, z_mid - 0.015 * s),
                   (xc + bar_w / 2.0, yf - EMBED, z_mid + 0.015 * s), DARK)
    for i, mat in enumerate((AMBER, CRT, LAMP_RED)):
        lamp(lit, xc + (i - 1) * 0.05 * s, z_mid, yf, mat, radius=0.007 * s)

    vent_w = 0.16 * s
    fine += vent(p, sx1 - vent_w, sx1, lip * 0.25, lip * 0.80, yf, bars=5)

    if rockers:
        fine += rocker_bank(p, (sx1 - vent_w - 0.10, yf, z_mid), count=3,
                            colours=(ORANGE, ORANGE, RED), pitch=0.026,
                            width=0.019, height=0.030)

    # Back: a recessed grey service panel with its own vent, so the head holds
    # up from behind when it stands in the middle of a room.
    z_back = zt - 0.35 * L
    y_panel = yb + 0.004
    fine += p.slab((x0 + 0.06, yb - EMBED, 0.06),
                   (x1 - 0.06, y_panel, z_back - 0.06), GREY)
    fine += vent(p, -0.10, 0.10, 0.10, z_back - 0.10, y_panel,
                 bars=6, mat_bar=DARK, facing=1)

    # Radar: range dial on the right rim, a selector under it. The knob is its
    # own object so it can turn; the dark collar it sits in is on the housing.
    if dial:
        xd = x1 - rim_r / 2.0
        zd = sz1 - 0.10
        # Deep enough to swallow the knob's own collar (see `ribbed_knob`),
        # which would otherwise end on this collar's back plane.
        p.cyl((xd, yf - 0.001, zd), 0.040, 0.012, 'Y', 24, DARK)
        fine += rotary_selector(p, (xd, yf, zd - 0.13), radius=0.016)
        lit_fine += square_key(lit, xd, sz0 + 0.03, key_size, yf, AMBER)
        d = TrackedPart(mats)
        d_fine = ribbed_knob(d, (xd, yf - 0.002, zd), radius=0.030,
                             depth=0.036, ribs=18)
        emit(d, "Mesh_CrtMonitor_%s_Dial" % tag, coll, fine=d_fine,
             origin=(xd, yf - 0.002, zd))

    housing = emit(p, "Mesh_CrtMonitor_%s_Housing" % tag, coll,
                   hard=hard, fine=fine)
    emit(lit, "Mesh_CrtMonitor_%s_Lit" % tag, coll, fine=lit_fine)

    cx, cz = (sx0 + sx1) / 2.0, (sz0 + sz1) / 2.0
    screen_plate(coll, mats, "Mesh_CrtMonitor_%s_Screen" % tag,
                 (sx0 + PLATE_MARGIN, y_face, sz0 + PLATE_MARGIN),
                 (sx1 - PLATE_MARGIN, y_face + 0.002, sz1 - PLATE_MARGIN),
                 origin=(cx, y_face, cz))
    return housing


# Sizes the model script reads rather than retypes.
KIOSK = dict(sw=0.60, sh=0.45, rim_l=0.13, rim_r=0.05, rim_t=0.06, lip=0.12,
             depth=0.40, key_column=True, key_size=0.032)
DESK = dict(sw=0.32, sh=0.24, rim_l=0.09, rim_r=0.035, rim_t=0.04, lip=0.075,
            depth=0.30, key_column=True, key_size=0.022, corner_r=0.045)
WIDE = dict(sw=0.80, sh=0.45, rim_l=0.045, rim_r=0.045, rim_t=0.045,
            lip=0.10, depth=0.36, rockers=True)
RADAR = dict(sw=0.45, sh=0.45, rim_l=0.05, rim_r=0.15, rim_t=0.05, lip=0.10,
             depth=0.38, dial=True, key_size=0.028)


def outline(spec):
    """(width, height, depth) of a variation's housing, bezel included."""
    return (spec["rim_l"] + spec["sw"] + spec["rim_r"],
            spec["lip"] + spec["sh"] + spec["rim_t"], spec["depth"])


def screen_centre(spec):
    """(x, y, z) of a variation's screen face centre in the housing frame."""
    W = outline(spec)[0]
    return (-W / 2.0 + spec["rim_l"] + spec["sw"] / 2.0,
            screen_face_y(spec["depth"]), spec["lip"] + spec["sh"] / 2.0)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    monitor(collection("Coll_CrtMonitor_Kiosk"), mats, "Kiosk", **KIOSK)
    monitor(collection("Coll_CrtMonitor_Desk"), mats, "Desk", **DESK)
    monitor(collection("Coll_CrtMonitor_Wide"), mats, "Wide", **WIDE)
    monitor(collection("Coll_CrtMonitor_Radar"), mats, "Radar", **RADAR)
    save(out)
    report()


if __name__ == "__main__":
    main()
