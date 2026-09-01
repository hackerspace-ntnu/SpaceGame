"""Shoulder-fired launch tubes — the barrel half of a recoilless weapon.

A open-ended tube with a flared blast venturi at the breech, reinforcing bands
along the body, a sight rail on top and a flat mount pad underneath for grips
and saddles. Deliberately just the tube: grips live in weapon_grip.blend and
whatever ornament goes on the muzzle is the model's business, which is what
lets the same barrel serve a scavenged pipe launcher and a lacquered
ceremonial one.

Three variations, differing in silhouette rather than colour:

  Coll_LaunchTube_Banded  the hero — 0.95 m, five bands, deep venturi
  Coll_LaunchTube_Vented  0.78 m and fatter, with a ring of side vents behind
                          the muzzle; the short handy version
  Coll_LaunchTube_Twin    two 0.86 m tubes on a common yoke — a heavier
                          silhouette for a two-shot mod

Front is -Y, up is +Z. The origin sits on the bore axis at the BREECH face,
because that is the end whose position is fixed by the gunner's shoulder — a
tube grows forward from where it is held, not backward from its muzzle.

Generation script — historical record. The .blend is the source of truth;
never re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

from mathutils import Matrix, Vector  # noqa: E402

# Index 0 is the structural steel: on this part almost every bevelled edge is
# on a band, a rail or the mount pad, so a steel bevel is invisible and a gold
# one would wire-outline the whole barrel.
(STEEL, DARK, VERM, GOLD, BLACK, RUBBER) = range(6)
MATS = [
    "Mat_Metal_Steel_Worn",         # 0  bands, rail, mount pad — and bevels
    "Mat_Metal_Steel_Dark",         # 1  venturi, end rings, hardware
    "Mat_Paint_Lacquer_Vermilion",  # 2  the tube body
    "Mat_Metal_Gold_Leaf",          # 3  band inlay and muzzle ring
    "Mat_Neutral_Black_Matte",      # 4  the bore
    "Mat_Plastic_Rubber_Black",     # 5  the cheek rest and shoulder pad face
]

BEVEL_W = 0.003


def tube_body(p, y_breech, y_muzzle, r_out, r_bore, bands, seg=24):
    """The barrel proper: lacquered wall, black bore, gold muzzle ring."""
    length = y_breech - y_muzzle
    mid = (y_breech + y_muzzle) / 2.0

    p.tube((0, mid, 0), r_out, r_out - r_bore, length, 'Y', seg, VERM)
    # A separate darker inner wall, slightly under the bore radius so it never
    # z-fights the tube's own inner face. Without it the barrel looks solid
    # from the front, which is the one angle a launcher is always seen from.
    p.tube((0, mid, 0), r_bore - 0.001, 0.004, length * 0.98, 'Y', seg, BLACK)

    hard = []
    for i in range(bands):
        y = y_muzzle + length * (i + 0.7) / (bands + 0.4)
        hard += p.tube((0, y, 0), r_out + 0.008, 0.011, 0.026, 'Y', seg, STEEL)
        p.tube((0, y, 0), r_out + 0.010, 0.004, 0.008, 'Y', seg, GOLD)

    # Muzzle ring — the crown the ornament bolts onto.
    p.tube((0, y_muzzle + 0.012, 0), r_out + 0.006, 0.010, 0.024, 'Y', seg,
           GOLD)
    return hard


def venturi(p, y_breech, r_out, flare, depth, seg=24):
    """The blast cone at the breech. What makes it read as recoilless."""
    faces = p.cyl((0, y_breech + depth / 2.0, 0), r_out + 0.002, depth, 'Y',
                  seg, DARK, cap=False, radius_top=r_out + flare)
    p.tube((0, y_breech + depth, 0), r_out + flare, 0.012, 0.016, 'Y', seg,
           DARK)
    return faces


def rail(p, y_front, y_back, r_out, mat=STEEL):
    """Sight rail along the spine, with a notch block at each end.

    Seated 3 mm into the wall rather than resting on the nominal radius. A rail
    placed at exactly r_out floats: the tube is a 24-sided prism, so its flats
    fall below the radius everywhere except on a vertex, and the gap reads as a
    modelling error from every angle.
    """
    hard = p.box((0, (y_front + y_back) / 2.0, r_out + 0.004),
                 (0.030, abs(y_back - y_front), 0.016), mat)
    for y in (y_front + 0.030, y_back - 0.030):
        hard += p.box((0, y, r_out + 0.018), (0.020, 0.016, 0.026), mat)
    return hard


def mount_pad(p, y_front, y_back, r_out):
    """The flat underside a grip or saddle bolts to."""
    return p.box((0, (y_front + y_back) / 2.0, -(r_out + 0.008)),
                 (0.046, abs(y_back - y_front), 0.018), STEEL)


def marker(coll, name, at, mats, size=0.004):
    """A tiny cube carrying a coordinate across the FBX. See portal_gun.py."""
    p = Part(mats)
    p.box((0, 0, 0), (size, size, size), STEEL)
    obj = p.finish(name, coll)
    obj.location = at
    return obj


# --------------------------------------------------------------------------
# Variations
# --------------------------------------------------------------------------

def banded(coll, mats, name, length=0.95, r_out=0.055, r_bore=0.042, bands=5):
    p = Part(mats)
    y_muzzle = -length
    hard = tube_body(p, 0.0, y_muzzle, r_out, r_bore, bands)
    venturi(p, 0.0, r_out, 0.030, 0.090)
    hard += rail(p, y_muzzle + 0.120, -0.180, r_out)
    hard += mount_pad(p, y_muzzle + 0.300, -0.250, r_out)
    # Cheek rest: the one soft surface, on the side the gunner's face lands.
    hard += p.box((0.052, -0.120, 0.020), (0.014, 0.130, 0.052), RUBBER)
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll)


def vented(coll, mats, name, length=0.78, r_out=0.062, r_bore=0.048):
    p = Part(mats)
    y_muzzle = -length
    hard = tube_body(p, 0.0, y_muzzle, r_out, r_bore, 3)
    venturi(p, 0.0, r_out, 0.042, 0.110)
    hard += rail(p, y_muzzle + 0.100, -0.150, r_out)
    hard += mount_pad(p, y_muzzle + 0.240, -0.210, r_out)

    # Gas vents in a ring behind the muzzle: the short tube dumps pressure
    # sideways because it has no length left to do it down the bore.
    for i in range(8):
        a = 2 * math.pi * i / 8
        d = Vector((math.cos(a), 0.0, math.sin(a)))
        hard += p.box(d * (r_out + 0.004) + Vector((0, y_muzzle + 0.100, 0)),
                      (0.012, 0.070, 0.026), DARK,
                      rot=Matrix.Rotation(-a, 4, 'Y'))
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll)


def twin(coll, mats, name, length=0.86, r_out=0.040, r_bore=0.030,
         spacing=0.046):
    p = Part(mats)
    y_muzzle = -length
    hard = []
    for sx in (-1, 1):
        sub = Part(mats)
        tube_body(sub, 0.0, y_muzzle, r_out, r_bore, 4, seg=20)
        venturi(sub, 0.0, r_out, 0.024, 0.070, seg=20)
        # Built at the origin and shifted, so the two barrels cannot drift out
        # of line with each other the way two hand-typed copies would.
        import bmesh as _bm
        _bm.ops.translate(sub.bm, vec=Vector((sx * spacing, 0, 0)),
                          verts=sub.bm.verts)
        p.bm.from_mesh(_flush(sub))

    # The yoke clamping the pair together.
    for y in (y_muzzle + 0.120, -0.170):
        hard += p.box((0, y, 0), (spacing * 2 + r_out * 2, 0.030,
                                  r_out * 2 + 0.014), STEEL)
    hard += rail(p, y_muzzle + 0.140, -0.190, r_out + 0.010)
    hard += mount_pad(p, y_muzzle + 0.280, -0.230, r_out + 0.010)
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll)


def _flush(part):
    """Bake a scratch Part's bmesh to a mesh so another Part can absorb it.

    Needed only by the twin, whose two barrels are one geometry built twice at
    different offsets. Part has no public merge, and reaching into `_absorb`
    would need a bmesh the caller does not hold.
    """
    mesh = bpy.data.meshes.new("_scratch_flush")
    part.bm.to_mesh(mesh)
    part.bm.free()
    return mesh


# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    hero = collection("Coll_LaunchTube_Banded")
    banded(hero, mats, "Mesh_LaunchTube_Banded")
    # Where the ornament bolts on, where the blast leaves the breech, and the
    # centre of the underside mount pad. Read by DragonBazookaBuilder.
    marker(hero, "Marker_Muzzle", (0.0, -0.950, 0.0), mats)
    marker(hero, "Marker_Breech", (0.0, 0.090, 0.0), mats)
    marker(hero, "Marker_Mount", (0.0, -0.450, -0.072), mats)

    vented(collection("Coll_LaunchTube_Vented"), mats, "Mesh_LaunchTube_Vented")
    twin(collection("Coll_LaunchTube_Twin"), mats, "Mesh_LaunchTube_Twin")

    report()
    save(out)


main()
