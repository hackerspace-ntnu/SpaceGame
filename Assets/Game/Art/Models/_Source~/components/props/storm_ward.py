"""The storm ward: a stake you drive into the sand that beats storms back.

    blender --background --python components/props/storm_ward.py -- --out components/props/storm_ward.blend

Knee-high scrap tech, the desert's own rather than a factory's: a rusted stake on
three splayed legs, a copper coil wound round a drum, a glass-caged blue core and,
above it, the thin emitter ring every shockwave leaves from. 0.95 m to the tip of
the mast, 0.62 m across the feet.

Origin at the BASE, where it meets the ground, because a placeable is spawned at a
raycast hit on the floor. Two empties are read by `StormWardBuilder` rather than
guessed at in C#: `FX_Emitter` (the ring's centre, where the shockwave starts)
and `LIGHT_Core` (inside the glass).

Five parts, each its own object so any one can be reshaped by hand: legs, stake,
coil, core, ring.

Generation script -- historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""
import math
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))

import bpy  # noqa: E402
from mathutils import Matrix  # noqa: E402

import _buildlib as B  # noqa: E402

# Index 0 first: bevels stamp new faces with index 0 unless told otherwise.
RUST, STEEL, COPPER, BRASS, ROPE, GLASS, CORE = range(7)
MATS = ["Mat_Metal_Rust_Heavy", "Mat_Metal_Steel_Dark", "Mat_Metal_Copper_Oxide",
        "Mat_Metal_Brass_Tarnished", "Mat_Fabric_Rope_Hemp", "Mat_Glass_Canopy_Tinted",
        "Mat_Emissive_Portal_Blue"]

LEG_SPREAD = 0.31      # foot distance from the stake
LEG_TOP_Z = 0.34       # where the legs clamp to the stake
DRUM_Z = (0.36, 0.52)
CORE_Z = 0.62
CORE_BASE = CORE_Z - 0.080   # just inside the lower brass cap
CORE_TOP = CORE_Z + 0.080
RING_Z = 0.80
MAST_TOP = 0.95


def strut(p, a, inner, outer, width, mat):
    """A square bar in the vertical plane at bearing `a`, from (radius, z) `inner` to
    `outer`. Rotated about Y by the bar's lean, then swung to the bearing."""
    (r0, z0), (r1, z1) = inner, outer
    dr, dz = r1 - r0, z1 - z0
    length = math.hypot(dr, dz)
    rot = Matrix.Rotation(a, 4, 'Z') @ Matrix.Rotation(math.atan2(dr, dz), 4, 'Y')
    rm, zm = (r0 + r1) / 2, (z0 + z1) / 2
    p.box((math.cos(a) * rm, math.sin(a) * rm, zm), (width, width, length), mat, rot=rot)


def legs(coll, mats):
    p = B.Part(mats)
    for i in range(3):
        a = math.radians(90 + i * 120)
        foot = (math.cos(a) * LEG_SPREAD, math.sin(a) * LEG_SPREAD)
        strut(p, a, (0.045, LEG_TOP_Z), (LEG_SPREAD, 0.02), 0.028, RUST)
        # A flat sand pad, sat 2 mm into the leg so no face is shared.
        p.cyl((foot[0], foot[1], 0.008), 0.045, 0.016, 'Z', 10, STEEL)
    # The clamp collar the legs hang from.
    p.tube((0.0, 0.0, LEG_TOP_Z), 0.058, 0.018, 0.05, 'Z', 14, STEEL)
    p.bevel(width=0.004, segments=1)
    p.finish("Mesh_StormWard_Legs", coll)


def stake(coll, mats):
    p = B.Part(mats)
    # The stake's point stops just above the ground between the feet.
    p.cyl((0.0, 0.0, 0.14), 0.022, 0.22, 'Z', 10, RUST, radius_top=0.030)
    # Up to the core's lower cap, not through it: the core stands on the stake.
    p.cyl((0.0, 0.0, (0.25 + CORE_BASE) / 2), 0.030, CORE_BASE - 0.25, 'Z', 12, RUST)
    # Mast from the core's top cap up through the ring, and a crooked vane at its top.
    p.cyl((0.0, 0.0, (CORE_TOP + MAST_TOP) / 2), 0.012, MAST_TOP - CORE_TOP, 'Z', 8, STEEL)
    p.box((0.05, 0.0, MAST_TOP - 0.03), (0.10, 0.004, 0.045), BRASS,
          rot=Matrix.Rotation(math.radians(7), 4, 'Y'))
    # Three struts raking out from the core's lower cap to the ring, 5 mm into both ends.
    for i in range(3):
        a = math.radians(30 + i * 120)
        strut(p, a, (0.045, CORE_Z - 0.083), (0.125, RING_Z), 0.012, STEEL)
    p.bevel(width=0.003, segments=1)
    p.finish("Mesh_StormWard_Stake", coll)


def coil(coll, mats):
    p = B.Part(mats)
    lo, hi = DRUM_Z
    p.cyl((0.0, 0.0, (lo + hi) / 2), 0.075, hi - lo, 'Z', 16, STEEL)
    p.helix(lo + 0.012, hi - 0.012, 0.081, 0.006, 7, COPPER)
    # Rope lashings above and below - the drum is tied on, not welded.
    p.torus((0.0, 0.0, lo - 0.004), 0.078, 0.008, 'Z', 16, 6, ROPE)
    p.torus((0.0, 0.0, hi + 0.004), 0.078, 0.008, 'Z', 16, 6, ROPE)
    p.finish("Mesh_StormWard_Coil", coll)


def core(coll, mats):
    p = B.Part(mats)
    p.cyl((0.0, 0.0, CORE_Z), 0.028, 0.11, 'Z', 12, CORE)
    p.cyl((0.0, 0.0, CORE_Z), 0.046, 0.15, 'Z', 12, GLASS)
    p.cyl((0.0, 0.0, CORE_Z - 0.083), 0.056, 0.016, 'Z', 12, BRASS)
    p.cyl((0.0, 0.0, CORE_Z + 0.083), 0.056, 0.016, 'Z', 12, BRASS)
    p.finish("Mesh_StormWard_Core", coll)


def ring(coll, mats):
    p = B.Part(mats)
    p.torus((0.0, 0.0, RING_Z), 0.13, 0.011, 'Z', 28, 8, BRASS)
    # The glow strip sits half inside the ring's inner face, so it shows as a lit seam.
    p.torus((0.0, 0.0, RING_Z), 0.118, 0.005, 'Z', 28, 6, CORE)
    for i in range(6):
        a = math.radians(i * 60)
        p.box((math.cos(a) * 0.13, math.sin(a) * 0.13, RING_Z), (0.022, 0.03, 0.03), STEEL,
              rot=Matrix.Rotation(a, 4, 'Z'))
    p.finish("Mesh_StormWard_Ring", coll)


def empty(coll, name, z):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.05
    e.location = (0.0, 0.0, z)
    coll.objects.link(e)


def main():
    out = B.parse_out()
    B.start(out)
    mats = B.link_materials(MATS)
    coll = B.collection("Coll_StormWard")
    legs(coll, mats)
    stake(coll, mats)
    coil(coll, mats)
    core(coll, mats)
    ring(coll, mats)
    empty(coll, "FX_Emitter", RING_Z)
    empty(coll, "LIGHT_Core", CORE_Z)
    B.save(out)
    B.report()


main()
