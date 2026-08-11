"""Bridge and cabin control surfaces.

The helm variation is the ship's bridge: a wrapping three-segment console that
stands across the cockpit under the windscreen, with the steering column
emerging from its centre. The other three exist because a cockpit built from one
console shape reads as a kit, and because the cabin needs switchgear that is not
a flight station.

Floor-standing variations have their origin at deck level, centred on the
footprint. Wall and ceiling variations have it on the mounting face.

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

DARK, PANEL, STEEL, CREAM, GREEN, AMBER, RED, RUST, BLACK = range(9)
MATS = ["Mat_Metal_Steel_Dark", "Mat_Neutral_Panel_Grey",
        "Mat_Metal_Steel_Worn", "Mat_Plastic_Cream_Aged",
        "Mat_Emissive_Green_CRT", "Mat_Emissive_Amber",
        "Mat_Emissive_Red_Warn", "Mat_Metal_Rust_Heavy",
        "Mat_Neutral_Black_Matte"]

FASCIA = math.radians(38)   # rake of the instrument face


def switch_bank(p, centre, width, height, rot, cols, rows, seed=0,
                lamp=AMBER):
    """Rows of toggles, breakers and lamps on a rotated face.

    `rot` orients the bank; everything is laid out in its local XY and pushed
    slightly proud along local Z, so the same routine dresses a raked fascia,
    a vertical wall panel and an overhead alike.
    """
    import random
    rng = random.Random(seed)
    c = Vector(centre)
    for r in range(rows):
        for i in range(cols):
            u = -width / 2 + width * (i + 0.5) / cols
            v = -height / 2 + height * (r + 0.5) / rows
            local = rot @ Vector((u, v, 0.012))
            kind = rng.random()
            if kind < 0.28:
                p.box(c + local, (width / cols * 0.5, height / rows * 0.24,
                                  0.028), DARK, rot=rot)
                p.box(c + rot @ Vector((u, v + 0.012, 0.030)),
                      (width / cols * 0.22, height / rows * 0.30, 0.022),
                      CREAM, rot=rot)
            elif kind < 0.52:
                p.cyl(c + local, min(width / cols, height / rows) * 0.30,
                      0.026, 'Z', 8, DARK, rot=rot)
                p.cyl(c + rot @ Vector((u, v, 0.026)),
                      min(width / cols, height / rows) * 0.16, 0.02, 'Z', 6,
                      lamp, rot=rot)
            elif kind < 0.72:
                p.box(c + local, (width / cols * 0.62, height / rows * 0.42,
                                  0.02), BLACK, rot=rot)
            else:
                p.cyl(c + local, min(width / cols, height / rows) * 0.26,
                      0.03, 'Z', 10, STEEL, rot=rot)
                p.box(c + rot @ Vector((u, v, 0.034)),
                      (width / cols * 0.10, height / rows * 0.34, 0.012),
                      CREAM, rot=rot)


def screen(p, centre, w, h, rot, mat=GREEN, bezel=DARK):
    """A recessed display in a proud bezel."""
    c = Vector(centre)
    p.box(c, (w + 0.06, h + 0.06, 0.05), bezel, rot=rot)
    p.box(c + rot @ Vector((0, 0, 0.028)), (w, h, 0.012), mat, rot=rot)
    for s in (-1, 1):
        p.box(c + rot @ Vector((s * (w / 2 + 0.018), 0, 0.030)),
              (0.012, h * 0.86, 0.014), STEEL, rot=rot)


def helm(coll, mats):
    """The bridge. Three segments wrapping the pilot, 2.24 m across, with the
    steering column boss at centre and a knee well underneath."""
    p = Part(mats)
    H = 1.00              # height above deck
    D = 0.72              # depth front to back

    # Centre carcass: a raked-front prism, undercut so the pilot's knees clear
    # it and the deck stays visible under the bridge.
    p.prism([(-D / 2, 0.0), (D / 2, 0.0), (D / 2, H - 0.30),
             (D / 2 - 0.30, H), (-D / 2, H), (-D / 2, 0.34),
             (-D / 2 + 0.16, 0.34), (-D / 2 + 0.16, 0.0)],
            1.24, 'Y', PANEL)

    # Raked fascia over the centre.
    fas = Matrix.Rotation(-FASCIA, 4, 'Y')
    p.box((D / 2 - 0.14, 0.0, H - 0.11), (0.40, 1.24, 0.06), DARK, rot=fas)
    screen(p, (D / 2 - 0.20, -0.34, H - 0.06), 0.34, 0.20, fas)
    screen(p, (D / 2 - 0.20, 0.34, H - 0.06), 0.34, 0.20, fas, mat=AMBER)
    switch_bank(p, (D / 2 - 0.06, 0.0, H - 0.20), 0.20, 0.34, fas, 2, 5,
                seed=3)

    # Steering column boss.
    p.cyl((D / 2 - 0.30, 0, H - 0.12), 0.13, 0.24, 'X', 14, DARK,
          rot=Matrix.Rotation(math.radians(25), 4, 'Y'))
    p.torus((D / 2 - 0.22, 0, H - 0.08), 0.15, 0.02, 'X', 14, 8, STEEL)

    # Angled wing consoles.
    for s in (-1, 1):
        yaw = s * math.radians(-26)
        rot = Matrix.Rotation(yaw, 4, 'Z')
        c = Vector((-0.10, s * 0.94, 0))
        p.box(c + Vector((0, 0, H / 2)), (D * 0.92, 0.62, H), PANEL, rot=rot)
        face = rot @ Matrix.Rotation(-FASCIA, 4, 'Y')
        p.box(c + rot @ Vector((D * 0.30, 0, H - 0.06)), (0.36, 0.60, 0.06),
              DARK, rot=face)
        if s < 0:
            switch_bank(p, tuple(c + rot @ Vector((D * 0.30, 0, H - 0.03))),
                        0.30, 0.50, face, 3, 6, seed=11, lamp=GREEN)
        else:
            screen(p, tuple(c + rot @ Vector((D * 0.30, 0, H - 0.02))),
                   0.26, 0.40, face, mat=GREEN)
        # Throttle quadrant on the starboard wing.
        if s > 0:
            p.box(c + rot @ Vector((-0.06, 0, H + 0.03)), (0.22, 0.34, 0.07),
                  DARK, rot=rot)
            for i, lean in enumerate((-16, 4)):
                p.box(c + rot @ Vector((-0.06 + i * 0.07, 0, H + 0.16)),
                      (0.035, 0.05, 0.24), STEEL,
                      rot=rot @ Matrix.Rotation(math.radians(lean), 4, 'Y'))
                p.cyl(c + rot @ Vector((-0.06 + i * 0.07, 0, H + 0.28)), 0.035,
                      0.05, 'Z', 8, RUST)

    # Coaming lip across the whole console, so the fascia has a horizon.
    p.box((D / 2 - 0.30, 0.0, H + 0.04), (0.10, 1.30, 0.05), STEEL)
    for s in (-1, 1):
        p.box((-0.10 + math.cos(math.radians(26)) * 0.0, s * 0.94, H + 0.04),
              (0.10, 0.62, 0.05), STEEL,
              rot=Matrix.Rotation(s * math.radians(-26), 4, 'Z'))
    # Kick strip and cable exit at the base.
    p.box((-D / 2 + 0.08, 0.0, 0.10), (0.16, 2.10, 0.20), DARK)
    p.cyl((-D / 2 + 0.02, 0.55, 0.22), 0.05, 0.30, 'X', 8, BLACK)
    p.bevel(width=0.008, segments=2)
    return p.finish("Mesh_ConsolePanel_Helm", coll)


def nav(coll, mats):
    """Free-standing navigation station — a screen stack on a pedestal, for
    the second crew position or against a cabin wall."""
    p = Part(mats)
    p.cyl((0, 0, 0.03), 0.30, 0.06, 'Z', 12, DARK)
    p.cyl((0, 0, 0.42), 0.10, 0.72, 'Z', 12, STEEL)
    p.box((0, 0, 0.80), (0.44, 0.62, 0.14), PANEL)
    fas = Matrix.Rotation(-math.radians(52), 4, 'Y')
    p.box((0.06, 0, 0.92), (0.34, 0.60, 0.07), DARK, rot=fas)
    screen(p, (0.10, -0.14, 0.95), 0.22, 0.20, fas, mat=GREEN)
    screen(p, (0.10, 0.16, 0.95), 0.22, 0.20, fas, mat=AMBER)
    switch_bank(p, (-0.06, 0.0, 0.88), 0.16, 0.52,
                Matrix.Rotation(math.radians(0), 4, 'Y'), 2, 6, seed=5)
    # Chart shelf and a mug ring worn into it.
    p.box((0.12, 0, 0.70), (0.34, 0.58, 0.03), CREAM)
    p.torus((0.20, 0.20, 0.716), 0.045, 0.006, 'Z', 12, 6, RUST)
    p.bevel(width=0.007, segments=2)
    return p.finish("Mesh_ConsolePanel_Nav", coll)


def breaker(coll, mats):
    """Wall-mounted breaker and fuse panel. Mounts facing +X; origin on the
    wall plane so it hangs with one translation."""
    p = Part(mats)
    W, H = 0.62, 0.86
    p.slab((0.0, -W / 2, -H / 2), (0.10, W / 2, H / 2), PANEL)
    p.slab((0.10, -W / 2 + 0.04, -H / 2 + 0.04), (0.13, W / 2 - 0.04,
                                                  H / 2 - 0.04), BLACK)
    face = Matrix.Rotation(math.radians(90), 4, 'Y')
    switch_bank(p, (0.13, 0.0, 0.06), 0.52, 0.58, face, 4, 6, seed=17,
                lamp=RED)
    # Hinged cover swung open against the wall, and a warning plate.
    p.slab((0.10, W / 2, -H / 2), (0.13, W / 2 + 0.40, H / 2), PANEL)
    for z in (-H / 3, H / 3):
        p.cyl((0.115, W / 2, z), 0.022, 0.10, 'Z', 8, STEEL)
    p.box((0.14, W / 2 + 0.20, 0.0), (0.02, 0.22, 0.14), AMBER)
    p.rivets((0.10, -W / 2 + 0.03, H / 2 - 0.03),
             (0.10, W / 2 - 0.03, H / 2 - 0.03), 5, radius=0.016,
             height=0.014, axis='X', mat=STEEL)
    p.bevel(width=0.006, segments=2)
    return p.finish("Mesh_ConsolePanel_Breaker", coll)


def overhead(coll, mats):
    """Ceiling-hung switch panel over the bridge. Origin at the ceiling face,
    panel hanging below it and raked down toward the crew."""
    p = Part(mats)
    W, L = 0.54, 1.05
    for s in (-1, 1):
        p.box((0, s * (L / 2 - 0.10), -0.09), (0.10, 0.05, 0.18), STEEL)
    rake = Matrix.Rotation(math.radians(-12), 4, 'Y')
    p.box((0, 0, -0.22), (W, L, 0.11), PANEL, rot=rake)
    p.box((0.02, 0, -0.29), (W - 0.10, L - 0.06, 0.04), DARK, rot=rake)
    switch_bank(p, (0.02, -0.26, -0.30), 0.36, 0.44,
                rake @ Matrix.Rotation(math.radians(180), 4, 'Y'), 3, 5,
                seed=23, lamp=AMBER)
    switch_bank(p, (0.02, 0.28, -0.30), 0.36, 0.40,
                rake @ Matrix.Rotation(math.radians(180), 4, 'Y'), 3, 4,
                seed=29, lamp=RED)
    # Grab handle along the lower edge — an overhead panel is what you hold.
    p.cyl((-W / 2 + 0.04, 0, -0.30), 0.022, L - 0.20, 'Y', 8, STEEL)
    for s in (-1, 1):
        p.box((-W / 2 + 0.04, s * (L / 2 - 0.10), -0.26), (0.05, 0.04, 0.09),
              STEEL)
    p.bevel(width=0.006, segments=2)
    return p.finish("Mesh_ConsolePanel_Overhead", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Helm", helm), ("Nav", nav), ("Breaker", breaker),
                     ("Overhead", overhead)):
        fn(collection("Coll_ConsolePanel_" + name), mats)

    report()
    save(out)


main()
