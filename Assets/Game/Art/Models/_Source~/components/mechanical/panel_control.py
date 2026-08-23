"""Panel control fittings — the small hardware that makes a surface a console.

A rocker bank, a rotary selector, a ribbed knob, a guarded toggle and a
connector strip. Individually trivial; together they are the reason a cream box
reads as an instrument rather than a lunchbox. Every one of them was about to be
written inline inside the handheld terminal, which is exactly how a library ends
up with nine slightly different knobs.

Sized as real panel hardware — 0.02-0.05 m — so they sit on a handheld device
without being rescaled. `console_panel.blend` is the vehicle-scale counterpart
and is deliberately not reused here: its fittings are authored against 1-2.7 m
panels and carry bolt density to match.

Every builder faces **-Y**: the fitting protrudes toward -Y from a plate whose
face is at the given y. That matches the library's -Y-forward convention, so a
control placed on a device front needs no rotation.

The builders are importable. `handheld_terminal.py` calls them directly rather
than appending this component, because a rocker is 40 triangles and appending a
whole .blend to get one is more machinery than the part is worth — the point of
sharing is that there is one definition of what a rocker looks like.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

from mathutils import Matrix, Vector  # noqa: E402

# Index 0 first: `bmesh.ops.bevel` stamps new faces with material index 0, so
# whatever sits here colours every chamfered edge in the file. Structural steel,
# never an accent.
STEEL, DARK, RUBBER, CHROME, CREAM, RED, BLUE, AMBER, BLACK, CRT = range(10)
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Plastic_Rubber_Black", "Mat_Metal_Chrome_Scuffed",
        "Mat_Plastic_Cream_Aged", "Mat_Paint_Warn_Red",
        "Mat_Paint_Blue_Station", "Mat_Emissive_Amber",
        "Mat_Neutral_Black_Matte", "Mat_Emissive_Green_CRT"]

# Bevel width for panel hardware. A third of the library's structural default —
# at 0.02 m a 2.6 mm bevel eats the part.
BEVEL_W = 0.0012


# --------------------------------------------------------------------------
# Shared low-level helper
# --------------------------------------------------------------------------

def tube_path(p, pts, radius, mat, seg=8, joint=True, taper=1.0):
    """A run of tube through a list of points — handles, guards, whip masts.

    `_buildlib` has no swept-tube primitive (an older `Part.sweep` is referenced
    by `leash_device.py` and no longer exists), so this places one cylinder per
    segment and, with `joint`, a short fat cylinder at each interior corner to
    fill the notch two perpendicular tubes leave between them.

    `taper` is the radius multiplier at the far end, applied linearly — a whip
    antenna is a tube that thins, and a constant-radius one reads as a pipe.
    """
    pts = [Vector(q) for q in pts]
    faces = []
    total = max(1, len(pts) - 1)
    for i in range(total):
        a, b = pts[i], pts[i + 1]
        d = b - a
        length = d.length
        if length < 1e-6:
            continue
        r0 = radius * (1.0 + (taper - 1.0) * (i / total))
        r1 = radius * (1.0 + (taper - 1.0) * ((i + 1) / total))
        rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        faces += p.cyl((a + b) / 2.0, r0, length, 'Z', seg, mat,
                       radius_top=r1, rot=rot)
        if joint and 0 < i < total:
            faces += p.cyl(a, r0 * 1.06, r0 * 1.7, 'Z', seg, mat, rot=rot)
    return faces


# --------------------------------------------------------------------------
# Fittings — each returns the faces it made, boxy ones separated for bevelling
# --------------------------------------------------------------------------

def rocker_bank(p, at, count=3, colours=(BLUE, RED, BLUE), pitch=0.0125,
                width=0.0095, height=0.019, tilt=9.0):
    """A row of coloured rocker paddles in a recessed dark surround.

    Tilted about X so each paddle catches light on its top half — a flat
    coloured rectangle reads as a sticker, a tilted one reads as a switch.
    """
    x0, y0, z0 = at
    hard = []
    span = pitch * count + 0.006
    hard += p.slab((x0 - span / 2, y0 - 0.003, z0 - height / 2 - 0.004),
                   (x0 + span / 2, y0 + 0.008, z0 + height / 2 + 0.004), DARK)
    rot = Matrix.Rotation(math.radians(tilt), 4, 'X')
    for i in range(count):
        x = x0 + (i - (count - 1) / 2.0) * pitch
        col = colours[i % len(colours)]
        hard += p.box((x, y0 - 0.0055, z0), (width, 0.009, height), col, rot=rot)
        # Chrome sliver along the paddle's lower lip: the highlight that says
        # the thing is moulded plastic sitting in a metal frame.
        p.box((x, y0 - 0.0088, z0 - height / 2 + 0.0022),
              (width * 0.86, 0.0022, 0.0026), CHROME, rot=rot)
    return hard


def rotary_selector(p, at, radius=0.0105, lever=True):
    """Slotted rotary selector in a chrome bezel — the ignition-key read."""
    x0, y0, z0 = at
    hard = []
    p.cyl((x0, y0 + 0.002, z0), radius * 1.75, 0.010, 'Y', 16, CHROME)
    p.cyl((x0, y0 - 0.004, z0), radius * 1.35, 0.008, 'Y', 16, DARK)
    p.cyl((x0, y0 - 0.010, z0), radius, 0.014, 'Y', 14, CHROME)
    # The slot. A groove is cheaper as a dark box laid into the face than as
    # real boolean geometry, and at this size nothing can tell.
    hard += p.box((x0, y0 - 0.0172, z0), (radius * 1.5, 0.0022, 0.0030), DARK)
    if lever:
        hard += p.box((x0 + radius * 0.9, y0 - 0.014, z0 - radius * 0.5),
                      (0.010, 0.006, 0.0055), CHROME,
                      rot=Matrix.Rotation(math.radians(-28), 4, 'Y'))
    # Index pips around the bezel — the marks a selector turns between.
    for i in range(5):
        a = math.radians(-58 + i * 29)
        p.cyl((x0 + math.cos(a) * radius * 2.15, y0 + 0.001,
               z0 + math.sin(a) * radius * 2.15),
              0.0013, 0.004, 'Y', 6, DARK)
    return hard


def ribbed_knob(p, at, radius=0.0155, depth=0.026, ribs=16, pointer=True):
    """Big fluted control knob with a chrome cap and an offset pointer stud.

    The pointer stud is the part that matters for anything that spins this in
    engine: without an off-axis feature the knob's rotation is invisible.
    """
    x0, y0, z0 = at
    hard = []
    p.cyl((x0, y0 - depth / 2, z0), radius, depth, 'Y', 20, RUBBER)
    for i in range(ribs):
        a = 2 * math.pi * i / ribs
        p.box((x0 + math.cos(a) * radius, y0 - depth / 2 + 0.001,
               z0 + math.sin(a) * radius),
              (0.0042, depth * 0.88, 0.0030), RUBBER,
              rot=Matrix.Rotation(-a, 4, 'Y'))
    p.cyl((x0, y0 - depth * 0.94, z0), radius * 0.55, 0.006, 'Y', 16, CHROME)
    p.cyl((x0, y0 + 0.002, z0), radius * 1.22, 0.006, 'Y', 20, DARK)
    if pointer:
        hard += p.cyl((x0, y0 - depth - 0.002, z0 + radius * 0.52),
                      0.0042, 0.006, 'Y', 10, CHROME)
    return hard


def guarded_toggle(p, at, height=0.016):
    """Toggle stick inside a wire guard — the switch you must mean to throw."""
    x0, y0, z0 = at
    hard = []
    p.cyl((x0, y0 + 0.001, z0), 0.0062, 0.008, 'Y', 12, CHROME)
    lean = Matrix.Rotation(math.radians(22), 4, 'X')
    hard += p.box((x0, y0 - height * 0.5, z0 + 0.002),
                  (0.0034, height, 0.0034), DARK, rot=lean)
    p.cyl((x0, y0 - height - 0.001, z0 + 0.005), 0.0030, 0.005, 'Y', 8, RED)
    tube_path(p, [(x0 - 0.0105, y0 - 0.002, z0 - 0.010),
                  (x0 - 0.0105, y0 - height - 0.006, z0 - 0.008),
                  (x0 - 0.0105, y0 - height - 0.008, z0 + 0.011),
                  (x0 + 0.0105, y0 - height - 0.008, z0 + 0.011),
                  (x0 + 0.0105, y0 - height - 0.006, z0 - 0.008),
                  (x0 + 0.0105, y0 - 0.002, z0 - 0.010)],
              0.0013, CHROME, seg=6)
    return hard


def connector_strip(p, at, rows=2, dots=5, pitch=0.0058, lamp=AMBER,
                    height=None):
    """A recessed multi-pin connector — the greeble that says 'this plugs in'.

    Two rows of pins in a dark well behind a raised grey frame. Cheap, and at
    icon distance it is the difference between a moulded box and a device.
    """
    x0, y0, z0 = at
    hard = []
    w = pitch * dots + 0.004
    h = height if height is not None else (pitch * rows + 0.005)
    hard += p.slab((x0 - w / 2, y0 - 0.004, z0 - h / 2),
                   (x0 + w / 2, y0 + 0.006, z0 + h / 2), STEEL)
    hard += p.slab((x0 - w / 2 + 0.0016, y0 - 0.0055, z0 - h / 2 + 0.0016),
                   (x0 + w / 2 - 0.0016, y0 - 0.0035, z0 + h / 2 - 0.0016),
                   BLACK)
    for r in range(rows):
        z = z0 + (r - (rows - 1) / 2.0) * (h - 0.006) / max(1, rows - 1) \
            if rows > 1 else z0
        for i in range(dots):
            x = x0 + (i - (dots - 1) / 2.0) * pitch
            p.cyl((x, y0 - 0.0052, z), 0.0016, 0.004, 'Y', 6,
                  lamp if r == 0 else CHROME)
    return hard


# --------------------------------------------------------------------------
# Variations — each fitting on a small mounting plate, usable on its own
# --------------------------------------------------------------------------

def _plate(p, size=0.050, depth=0.010, mat=CREAM):
    """The square of panel a standalone fitting is delivered on."""
    return p.slab((-size / 2, 0.0, -size / 2), (size / 2, depth, size / 2), mat)


def rocker3(coll, mats):
    p = Part(mats)
    hard = _plate(p)
    hard += rocker_bank(p, (0, 0, 0.004))
    p.box((0, -0.0015, -0.017), (0.030, 0.003, 0.004), DARK)   # legend strip
    p.rivets((-0.020, -0.001, 0.021), (0.020, -0.001, 0.021), 3,
             radius=0.0016, height=0.0022, axis='Y', mat=CHROME)
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_Control_Rocker3", coll)


def rotary(coll, mats):
    p = Part(mats)
    hard = _plate(p)
    hard += rotary_selector(p, (0, 0, 0.002))
    hard += connector_strip(p, (0, 0, -0.019), rows=1, dots=4, pitch=0.005)
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_Control_Rotary", coll)


def knob_ribbed(coll, mats):
    """Origin on the knob's own axis, pointing -Y — it is meant to be spun."""
    p = Part(mats)
    hard = _plate(p)
    hard += ribbed_knob(p, (0, 0, 0))
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_Control_KnobRibbed", coll)


def toggle_guard(coll, mats):
    p = Part(mats)
    hard = _plate(p)
    hard += guarded_toggle(p, (0, 0, -0.002))
    p.cyl((0.017, -0.0015, 0.017), 0.0035, 0.005, 'Y', 8, AMBER)
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_Control_ToggleGuard", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    rocker3(collection("Coll_Control_Rocker3"), mats)
    rotary(collection("Coll_Control_Rotary"), mats)
    knob_ribbed(collection("Coll_Control_KnobRibbed"), mats)
    toggle_guard(collection("Coll_Control_ToggleGuard"), mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
