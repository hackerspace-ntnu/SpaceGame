"""Keyboard decks — the input tray of the console family.

Three variations, each a different footprint rather than a recolour:

    Full     15u main block, nav cluster, numpad, palm pads with buttons.
             0.78 m wide — the standing terminal's deck, the one the request
             needed.
    Compact  the main block alone, no pads. 0.37 m — a desk or a cockpit.
    Keypad   numpad plus a column of four function keys. 0.15 m — a locker
             door, a wall panel, an airlock post.

The keys are the reference's: cream caps with orange groups — Esc, the last
four function keys, Backspace, Enter, the modifiers, the nav cluster and the
numpad's operators — so the deck reads as an instrument from across a room
and not as a grey texture (GDC-L1-UX-0003: the orange ranks the keys a player
would actually look for).

Objects per variation, each its own renderer:

    Mesh_Keyboard_<V>_Tray      the moulded tray: dark key well, rear hinge
                                bar, front bumper, status lamps
    Mesh_Keyboard_<V>_Keys      every keycap in one mesh — a hundred keys as
                                separate objects would be a hundred draw
                                calls for nothing anyone can select
    Mesh_Keyboard_Full_SidePads the palm pads and their buttons (Full only)

Frame: the tray lies FLAT. Top face at z = 0, keys standing +Z, the rear edge
along X at y = 0 and the tray extending toward -Y (the user's side). The
origin is on the HINGE BAR AXIS at the rear edge, (0, 0, -0.025), so a model
that mounts a deck tilts it by rotating about its own origin: the rear edge
stays on the console and the front drops toward the user.

    blender --background --python keyboard_deck.py -- --out keyboard_deck.blend

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

PITCH = 0.019              # key centre to key centre, one unit
CAP = 0.0155               # keycap footprint; the rest of the pitch is gap
CAP_H = 0.010
GAP = PITCH - CAP
WELL_TOP = EMBED           # the dark well stands EMBED proud of the tray
KEY_Z0 = WELL_TOP - EMBED  # caps planted EMBED into the well — and the same
                           # EMBED above its back, which a deeper plant would
                           # come within `_zverify`'s tolerance of
TRAY_T = 0.050
HINGE_Z = -0.025           # the hinge bar axis — and the origin
ORIGIN = (0.0, 0.0, HINGE_Z)

# Row centres from the rear edge. The function row sits a little apart from
# the block, the way it does on the reference.
ROW_Y = (-0.036, -0.062, -0.081, -0.100, -0.119, -0.138)

S, O = SHELL, ORANGE

F_ROW = ([(1, O), (0.5, None)] + [(1, S)] * 4 + [(0.5, None)] + [(1, S)] * 4
         + [(0.5, None)] + [(1, O)] * 4)
MAIN = (
    [(1, S)] * 13 + [(2, O)],
    [(1.5, S)] + [(1, S)] * 12 + [(1.5, S)],
    [(1.75, S)] + [(1, S)] * 11 + [(2.25, O)],
    [(2.25, S)] + [(1, S)] * 10 + [(2.75, S)],
    [(1.25, O), (1.25, S), (1.25, O), (6.25, S), (1.25, O), (1.25, S),
     (1.25, S), (1.25, O)],
)
MAIN_UNITS = 15


def lay_row(p, x_left, y, spec):
    """Keys left to right from `x_left`, one PITCH per unit. A `None`
    material is a gap."""
    cum = 0.0
    for units, mat in spec:
        if mat is not None:
            cx = x_left + (cum + units / 2.0) * PITCH
            keycap(p, cx, y, units * PITCH - GAP, CAP, KEY_Z0, CAP_H, mat)
        cum += units


def tall_key(p, x_left, col, row_a, row_b, mat):
    """One key spanning two rows — numpad + and Enter."""
    cx = x_left + (col + 0.5) * PITCH
    cy = (ROW_Y[row_a] + ROW_Y[row_b]) / 2.0
    keycap(p, cx, cy, CAP, PITCH + CAP, KEY_Z0, CAP_H, mat)


def main_block(p, x_left):
    lay_row(p, x_left, ROW_Y[0], F_ROW)
    for i, spec in enumerate(MAIN):
        lay_row(p, x_left, ROW_Y[i + 1], spec)


def nav_cluster(p, x_left):
    for r in (1, 2):
        lay_row(p, x_left, ROW_Y[r], [(1, O)] * 3)
    lay_row(p, x_left, ROW_Y[4], [(1, None), (1, O), (1, None)])
    lay_row(p, x_left, ROW_Y[5], [(1, O)] * 3)


def numpad(p, x_left):
    lay_row(p, x_left, ROW_Y[1], [(1, S), (1, S), (1, S), (1, O)])
    lay_row(p, x_left, ROW_Y[2], [(1, S)] * 3)
    tall_key(p, x_left, 3, 2, 3, O)
    lay_row(p, x_left, ROW_Y[3], [(1, S)] * 3)
    lay_row(p, x_left, ROW_Y[4], [(1, S)] * 3)
    tall_key(p, x_left, 3, 4, 5, O)
    lay_row(p, x_left, ROW_Y[5], [(2, S), (1, S)])


def tray(coll, mats, tag, half_w, depth, well_x, well_y, lamps_x):
    """The moulded tray. `well_x`/`well_y` are the dark well's extents,
    `lamps_x` where the two status lamps sit on the rear margin."""
    p = TrackedPart(mats)
    hard, fine = [], []
    hard += rounded_slab_z(p, -half_w, half_w, -depth, 0.0, -TRAY_T, 0.0,
                           0.035, CREAM)
    # The well the keys stand in. Proud rather than sunk, see `_console_kit.slot`.
    fine += p.slab((well_x[0], well_y[0], -EMBED), (well_x[1], well_y[1], WELL_TOP),
                   DARK)
    # Hinge bar along the rear edge — the deck's pivot and its mounting.
    p.cyl((0, 0.0, HINGE_Z), 0.027, 2 * half_w - 0.10, 'X', 16, DARK)
    for sx in (-1, 1):
        p.cyl((sx * (half_w - 0.045), 0.0, HINGE_Z), 0.030, 0.014, 'X', 16,
              CHROME)
    # Rubber bumper along the front edge, inset from the rounded corners.
    fine += p.box((0, -depth + 0.0025, -0.030),
                  (2 * half_w - 0.10, 0.011, 0.028), RUBBER)
    for i, mat in enumerate((AMBER, CRT)):
        p.cyl((lamps_x + i * 0.022, -0.013, 0.0035), 0.005, 0.013, 'Z', 10, mat)
    return emit(p, "Mesh_Keyboard_%s_Tray" % tag, coll, hard=hard, fine=fine,
                origin=ORIGIN)


def full(coll, mats):
    """Main block, nav cluster and numpad, flanked by palm pads with two
    buttons each — the reference's own layout, at full size."""
    half_w = 0.39
    x_main = -0.228
    x_nav = x_main + (MAIN_UNITS + 1) * PITCH
    x_num = x_nav + 4 * PITCH
    tray(coll, mats, "Full", half_w, 0.27, (x_main - 0.010, x_num + 4 * PITCH + 0.010),
         (-0.152, -0.022), 0.190)

    k = TrackedPart(mats)
    main_block(k, x_main)
    nav_cluster(k, x_nav)
    numpad(k, x_num)
    emit(k, "Mesh_Keyboard_Full_Keys", coll, origin=ORIGIN)

    # Palm pads: thick cushions standing proud of the tray, a stacked pair of
    # square buttons on each. The right pad's lower button is the orange one.
    s = TrackedPart(mats)
    hard = []
    for sx in (-1, 1):
        x0, x1 = sorted((sx * 0.27, sx * 0.37))
        hard += rounded_slab_z(s, x0, x1, -0.240, -0.030, -EMBED, 0.018,
                               0.030, CREAM)
        for j, y in enumerate((-0.095, -0.165)):
            mat = ORANGE if (sx > 0 and j == 1) else SHELL
            keycap(s, sx * 0.32, y, 0.030, 0.030, 0.018 - 0.004, 0.009, mat,
                   taper=0.85)
    emit(s, "Mesh_Keyboard_Full_SidePads", coll, hard=hard, origin=ORIGIN)


def compact(coll, mats):
    """The main block alone: what fits on a desk beside a `Desk` monitor."""
    half_w = 0.185
    x_main = -MAIN_UNITS * PITCH / 2.0
    tray(coll, mats, "Compact", half_w, 0.20, (x_main - 0.010, -x_main + 0.010),
         (-0.152, -0.022), 0.110)
    k = TrackedPart(mats)
    main_block(k, x_main)
    emit(k, "Mesh_Keyboard_Compact_Keys", coll, origin=ORIGIN)


def keypad(coll, mats):
    """Numpad with a column of four orange function keys down its left."""
    half_w = 0.075
    x_left = -2.5 * PITCH
    tray(coll, mats, "Keypad", half_w, 0.16, (x_left - 0.008, -x_left + 0.008),
         (-0.152, -0.050), 0.020)
    k = TrackedPart(mats)
    for r in range(1, 5):
        lay_row(k, x_left, ROW_Y[r], [(1, O)])
    numpad(k, x_left + PITCH)
    emit(k, "Mesh_Keyboard_Keypad_Keys", coll, origin=ORIGIN)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    full(collection("Coll_Keyboard_Full"), mats)
    compact(collection("Coll_Keyboard_Compact"), mats)
    keypad(collection("Coll_Keyboard_Keypad"), mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
