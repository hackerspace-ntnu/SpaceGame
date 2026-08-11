"""Helm controls.

Replaces the twelve-cylinder placeholder wheel that ShipRVBuilder currently
assembles from Unity primitives at runtime. The builder's own comment asks for
this: "swap the children for a modelled yoke whenever one exists".

Authoring frame matches what the builder expects, so the mount transform does
not have to change: the wheel lies in the local XY plane with its face along
+Z, the column runs down local -Y, and the origin is the hub centre. The builder
tilts the whole thing back toward the pilot.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

from mathutils import Matrix  # noqa: E402

DARK, CHROME, RUBBER, STEEL, GREEN, AMBER, RED, RUST = 0, 1, 2, 3, 4, 5, 6, 7
MATS = ["Mat_Metal_Steel_Dark", "Mat_Metal_Chrome_Scuffed",
        "Mat_Plastic_Rubber_Black", "Mat_Metal_Steel_Worn",
        "Mat_Emissive_Green_CRT", "Mat_Emissive_Amber",
        "Mat_Emissive_Red_Warn", "Mat_Metal_Rust_Heavy"]

R = 0.34          # rim radius — the size the cockpit is already built around


def column(p, length=0.46, boot=True):
    """Steering column running down local -Y from the hub."""
    p.cyl((0, -R - length / 2 + 0.06, 0), 0.055, length, 'Y', 14, DARK)
    if boot:
        # Concertina gaiter where the column enters the console.
        for i in range(5):
            y = -R - 0.16 - i * 0.065
            p.torus((0, y, 0), 0.075, 0.022, 'Y', 14, 8, RUBBER)
    p.cyl((0, -R - length + 0.02, 0), 0.10, 0.05, 'Y', 14, STEEL)
    # Stalk switches, because a helm with no secondary controls looks unused.
    p.cyl((0.10, -R - 0.10, 0), 0.014, 0.16, 'X', 6, DARK)
    p.cyl((0.19, -R - 0.10, 0), 0.020, 0.05, 'X', 8, RUBBER)
    p.cyl((-0.09, -R - 0.16, 0), 0.014, 0.13, 'X', 6, DARK)


def hub(p, r=0.10, screen=GREEN):
    """Hub with a recessed holographic readout — the futuristic beat."""
    p.cyl((0, 0, 0.0), r, 0.075, 'Z', 16, DARK)
    p.tube((0, 0, 0.045), r, 0.022, 0.03, 'Z', 16, CHROME)
    p.cyl((0, 0, 0.048), r * 0.72, 0.012, 'Z', 16, screen)
    # Projector cowl standing off the face, so the readout reads as a hologram
    # rather than a sticker.
    for i in range(3):
        a = 2 * math.pi * i / 3 + math.pi / 6
        p.box((math.cos(a) * r * 0.80, math.sin(a) * r * 0.80, 0.085),
              (0.035, 0.02, 0.055), CHROME, rot=Matrix.Rotation(a, 4, 'Z'))
    p.cyl((0, 0, 0.105), r * 0.44, 0.014, 'Z', 14, GREEN)


def ring(coll, mats):
    """The one the ship flies with: a full rim with a glowing inner race,
    three spokes and moulded grips. Futuristic without stopping being a wheel."""
    p = Part(mats)
    p.torus((0, 0, 0), R, 0.036, 'Z', 40, 12, DARK)
    # Chrome cap ring on the front face and an emissive race behind it.
    p.torus((0, 0, 0.022), R, 0.016, 'Z', 40, 10, CHROME)
    p.torus((0, 0, -0.026), R, 0.010, 'Z', 40, 8, GREEN)
    # Inner floating ring, held off the rim on short posts.
    p.torus((0, 0, 0.006), R - 0.075, 0.013, 'Z', 32, 8, CHROME)
    for i in range(6):
        a = 2 * math.pi * i / 6 + math.radians(30)
        p.box((math.cos(a) * (R - 0.038), math.sin(a) * (R - 0.038), 0.006),
              (0.075, 0.018, 0.018), CHROME, rot=Matrix.Rotation(a, 4, 'Z'))

    # Three spokes, the lower two swept.
    for a_deg in (90, 214, 326):
        a = math.radians(a_deg)
        p.box((math.cos(a) * R * 0.55, math.sin(a) * R * 0.55, 0.0),
              (R * 0.92, 0.055, 0.042), DARK, rot=Matrix.Rotation(a, 4, 'Z'))
        p.box((math.cos(a) * R * 0.55, math.sin(a) * R * 0.55, 0.026),
              (R * 0.80, 0.030, 0.012), GREEN, rot=Matrix.Rotation(a, 4, 'Z'))
    hub(p)

    # Moulded grips at nine and three o'clock, with finger scallops.
    for s in (-1, 1):
        for i in range(5):
            a = math.radians(180 if s < 0 else 0) + s * math.radians(-24 + i * 12)
            p.torus((math.cos(a) * R, math.sin(a) * R, 0), 0.050, 0.020, 'Z',
                    10, 8, RUBBER)
    # Thumb controls on the top spoke.
    for x in (-0.055, 0.055):
        p.box((x, R * 0.62, 0.030), (0.038, 0.038, 0.016), DARK)
    p.cyl((-0.055, R * 0.62, 0.040), 0.014, 0.008, 'Z', 8, AMBER)
    p.cyl((0.055, R * 0.62, 0.040), 0.014, 0.008, 'Z', 8, RED)
    column(p)
    p.bevel(width=0.005, segments=2)
    return p.finish("Mesh_SteeringYoke_Ring", coll)


def butterfly(coll, mats):
    """Aircraft-style two-horn yoke — flat-bottomed, so the pilot's knees clear
    it. Different silhouette, same parts bin."""
    p = Part(mats)
    # Two horns swept up and out from a central beam.
    p.box((0, 0.0, 0), (0.34, 0.055, 0.05), DARK)
    for s in (-1, 1):
        for i in range(7):
            t = i / 6.0
            a = math.radians(-70 * s * t)
            x = s * (0.17 + math.sin(abs(a)) * 0.20)
            y = (1 - math.cos(a)) * 0.30
            p.cyl((x, y, 0), 0.030, 0.10, 'X', 10, DARK,
                  rot=Matrix.Rotation(a, 4, 'Z'))
        # Grip on the outboard half of each horn.
        for i in range(4):
            t = 0.45 + i * 0.16
            a = math.radians(-70 * s * t)
            x = s * (0.17 + math.sin(abs(a)) * 0.20)
            y = (1 - math.cos(a)) * 0.30
            p.torus((x, y, 0), 0.046, 0.020, 'X', 10, 8, RUBBER)
    hub(p, r=0.085)
    # Trim wheel and a red master-caution under the hub.
    p.cyl((0, -0.14, 0), 0.055, 0.028, 'Y', 16, RUST)
    p.cyl((0.0, -0.10, 0), 0.022, 0.05, 'Y', 10, RED)
    column(p, length=0.40)
    p.bevel(width=0.005, segments=2)
    return p.finish("Mesh_SteeringYoke_Butterfly", coll)


def twin(coll, mats):
    """Two side-sticks on a pivoting cross-bar — the fly-by-wire option, and
    the variation with no wheel in it at all."""
    p = Part(mats)
    p.box((0, 0.0, 0), (0.52, 0.07, 0.06), DARK)
    p.torus((0, 0, 0), 0.075, 0.024, 'Y', 14, 8, CHROME)
    for s in (-1, 1):
        base = (s * 0.26, 0.0, 0.0)
        p.cyl(base, 0.05, 0.07, 'X', 12, STEEL)
        # Grip canted inboard, so the two sticks toe in toward the pilot.
        rot = Matrix.Rotation(math.radians(-14 * s), 4, 'Y')
        p.cyl((s * 0.30, 0.0, 0.09), 0.033, 0.20, 'Z', 12, DARK, rot=rot)
        p.prism([(-0.045, 0.0), (0.045, 0.0), (0.05, 0.13), (0.0, 0.17),
                 (-0.05, 0.13)], 0.085, 'Y', RUBBER,
                offset=(s * 0.30, 0.0, 0.18))
        for i in range(3):
            p.torus((s * 0.30, 0.0, 0.20 + i * 0.038), 0.048, 0.012, 'Z', 10,
                    6, RUBBER)
        # Trigger and hat switch.
        p.box((s * 0.30, -0.055, 0.24), (0.05, 0.03, 0.05), DARK)
        p.cyl((s * 0.30, 0.0, 0.315), 0.020, 0.02, 'Z', 8,
              GREEN if s < 0 else AMBER)
    p.cyl((0, 0, -0.16), 0.055, 0.26, 'Z', 12, DARK)
    p.cyl((0, 0, -0.30), 0.11, 0.05, 'Z', 14, STEEL)
    p.bevel(width=0.005, segments=2)
    return p.finish("Mesh_SteeringYoke_Twin", coll)


def salvaged(coll, mats):
    """A road vehicle's wheel bolted onto a spacecraft column, complete with
    the adapter plate and mismatched bolts. The most RV thing on the ship."""
    p = Part(mats)
    rr = R * 0.86
    p.torus((0, 0, 0), rr, 0.028, 'Z', 32, 10, RUST)
    # Cracked rubber wrap, missing in two places.
    for i in range(28):
        a = 2 * math.pi * i / 28
        if 6 <= i <= 8 or 19 <= i <= 20:
            continue
        p.torus((math.cos(a) * rr, math.sin(a) * rr, 0), 0.044, 0.018, 'Z', 8,
                6, RUBBER)
    # Four flat spokes, one visibly bent.
    for i in range(4):
        a = math.pi / 2 * i + math.radians(45)
        bend = math.radians(6) if i == 2 else 0.0
        p.box((math.cos(a) * rr * 0.52, math.sin(a) * rr * 0.52, 0),
              (rr * 0.95, 0.05, 0.02), RUST,
              rot=Matrix.Rotation(a, 4, 'Z') @ Matrix.Rotation(bend, 4, 'X'))
    # Adapter plate: the wheel and the column were never meant to meet.
    p.cyl((0, 0, -0.01), 0.115, 0.022, 'Z', 6, STEEL)
    p.cyl((0, 0, 0.012), 0.085, 0.03, 'Z', 14, DARK)
    for i in range(5):
        a = 2 * math.pi * i / 5
        p.cyl((math.cos(a) * 0.09, math.sin(a) * 0.09, 0.012), 0.017, 0.045,
              'Z', 6, CHROME)
    p.cyl((0, 0, 0.032), 0.05, 0.012, 'Z', 12, AMBER)
    column(p, length=0.44, boot=False)
    p.cyl((0, -R - 0.12, 0), 0.075, 0.05, 'Y', 10, RUST)
    p.bevel(width=0.005, segments=2)
    return p.finish("Mesh_SteeringYoke_Salvaged", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Ring", ring), ("Butterfly", butterfly), ("Twin", twin),
                     ("Salvaged", salvaged)):
        fn(collection("Coll_SteeringYoke_" + name), mats)

    report()
    save(out)


main()
