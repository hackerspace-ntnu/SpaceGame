"""Gravel blasters — handmade spring-driven pipe shotguns.

The design brief is a concept sheet of handmade shotguns; the model is its
main figure: two thick rusted pipe barrels side by side, a pair of exposed
coil springs on guide rods driving the breech, a dark forged fork clamping
the whole assembly onto a wooden stock with a cloth-wrapped wrist. The
mechanism the sheet describes — slide the grip back to compress the springs,
drop shells into the exposed barrels, pull the trigger, BAM — is why the
springs and the open pipe muzzles are the two loudest details.

Three variations, straight off the sheet's own "3 and 4 barrel mods
available" note, differing in silhouette rather than colour:

  Coll_GravelBlaster_Twin    the hero — two barrels level, ships to Unity
  Coll_GravelBlaster_Triple  two low, one stacked on top: a taller profile
  Coll_GravelBlaster_Quad    2x2 block of pipes: the cartoonishly heavy mod

Built at real longarm scale: about 1.05 m from muzzle to butt plate. Front
is -Y, up is +Z, matching the library convention. The origin sits at the
stock wrist where the trigger hand closes; Marker_Muzzle and Marker_Grip
cubes carry the exact firing point and hand point across the FBX for the
Unity prefab builder to adopt (see portal_gun.py for why markers, not
empties).

Generation script — historical record. The .blend is the source of truth;
never re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

# Index 0 first and structural: `bmesh.ops.bevel` stamps every face it creates
# with material index 0, so whatever sits here is the colour of every chamfered
# edge in the file.
(STEEL, RUSTO, RUSTH, DARK, BRASS, WOOD, CANVAS, BLACK, RUBBER) = range(9)
MATS = [
    "Mat_Metal_Steel_Worn",       # 0  clamps, bands, rods — and every bevel
    "Mat_Metal_HullRust_Orange",  # 1  the pipe barrels
    "Mat_Metal_Rust_Heavy",       # 2  muzzle mouths, corrosion bands
    "Mat_Metal_Steel_Dark",       # 3  receiver, fork, trigger, hammer
    "Mat_Metal_Brass_Tarnished",  # 4  drive springs, shell rims
    "Mat_Wood_Ply_Worn",          # 5  the stock
    "Mat_Fabric_Canvas_Faded",    # 6  tape wraps on barrels and wrist
    "Mat_Neutral_Black_Matte",    # 7  bore shadow discs
    "Mat_Plastic_Rubber_Black",   # 8  trigger pad
]

# Bevel only the boxy faces, and narrowly: a whole-part bevel at this scale
# exceeds half the wire radius of the springs, and finish()'s remove_doubles
# then welds the over-bevelled coils into a blob.
BEVEL_W = 0.0018


# --------------------------------------------------------------------------
# Local helpers
# --------------------------------------------------------------------------

def coil(p, x, z, y0, y1, coil_r, wire_r, turns, mat, seg_per_turn=14,
         wire_seg=8):
    """A helical drive spring along +Y, centred on (x, z).

    Analytic frames rather than parallel transport (portal_gun's sweep):
    a helix has a closed-form tangent, so the cross-section ring can be
    placed exactly at every station and the coil cannot twist.
    """
    length = y1 - y0
    n = int(turns * seg_per_turn) + 1
    pitch = length / (2 * math.pi * turns)

    rings = []
    for i in range(n):
        t = i / (n - 1)
        a = 2 * math.pi * turns * t
        centre = Vector((x + coil_r * math.cos(a), y0 + length * t,
                         z + coil_r * math.sin(a)))
        radial = Vector((math.cos(a), 0.0, math.sin(a)))
        tangent = Vector((-coil_r * math.sin(a), pitch,
                          coil_r * math.cos(a))).normalized()
        binormal = tangent.cross(radial).normalized()
        rings.append([
            centre + radial * (math.cos(2 * math.pi * k / wire_seg) * wire_r)
                   + binormal * (math.sin(2 * math.pi * k / wire_seg) * wire_r)
            for k in range(wire_seg)])

    bm2 = bmesh.new()
    vrings = [[bm2.verts.new(tuple(c)) for c in ring] for ring in rings]
    for a_ring, b_ring in zip(vrings, vrings[1:]):
        for i in range(wire_seg):
            j = (i + 1) % wire_seg
            bm2.faces.new((a_ring[i], a_ring[j], b_ring[j], b_ring[i]))
    bm2.faces.new(vrings[0])
    bm2.faces.new(list(reversed(vrings[-1])))

    faces = p._absorb(bm2, mat)
    for f in faces:
        f.smooth = True
    return faces


def strut(p, a, b, size, mat):
    """A box run between two points — the fork arms and guard limbs.

    Length and rotation are derived from the endpoints instead of typed,
    because every arm on this gun runs at a compound angle and a guessed
    Euler triple is exactly the kind of number that goes silently wrong.
    """
    a, b = Vector(a), Vector(b)
    mid = (a + b) / 2.0
    d = b - a
    length = d.length
    rot = Vector((0, 1, 0)).rotation_difference(d.normalized()).to_matrix() \
                           .to_4x4()
    return p.box(mid, (size[0], length, size[1]), mat, rot=rot)


def octagon(zc, hw, hh, c=0.35):
    """A chamfered-rectangle profile in (x, z) for lofting the stock.

    `c` is how much of each corner is cut; a plain rectangle read as a
    plank, and a full ellipse read as machined — the stock is neither.
    """
    cw, ch = hw * c, hh * c
    return [(-hw + cw, zc - hh), (hw - cw, zc - hh), (hw, zc - hh + ch),
            (hw, zc + hh - ch), (hw - cw, zc + hh), (-hw + cw, zc + hh),
            (-hw, zc + hh - ch), (-hw, zc - hh + ch)]


def marker(coll, name, at, mats, size=0.004):
    """A tiny cube whose only job is to carry a coordinate across the FBX.

    Empties are not exported (object_types={"MESH"}), so a named 4 mm mesh
    survives the trip; the Unity prefab builder reads its transform and
    strips the renderer.
    """
    p = Part(mats)
    p.box((0, 0, 0), (size, size, size), STEEL)
    obj = p.finish(name, coll)
    obj.location = at
    return obj


# --------------------------------------------------------------------------
# The gun, parameterised by barrel layout
# --------------------------------------------------------------------------

def barrel(p, x, z, y_muzzle, y_breech, r):
    """One pipe barrel along Y: rusted body, open hollow mouth, wrap bands."""
    body_len = (y_breech - y_muzzle) - 0.060
    p.cyl((x, y_muzzle + 0.060 + body_len / 2.0, z), r, body_len, 'Y', 18,
          RUSTO)
    # The mouth is a hollow tube so the muzzle reads as an open pipe — the
    # single strongest cue on the concept sheet that these are scavenged
    # plumbing rather than gun barrels.
    p.tube((x, y_muzzle + 0.030, z), r, 0.008, 0.060, 'Y', 18, RUSTH)
    p.cyl((x, y_muzzle + 0.058, z), r - 0.009, 0.004, 'Y', 18, BLACK)
    # Reinforcing collar where mouth meets body.
    p.torus((x, y_muzzle + 0.062, z), r + 0.002, 0.005, 'Y', 18, 8, RUSTH)


def wraps(p, positions, r):
    """Pale tape wraps around a barrel — the hand-repaired look."""
    for x, z, y, w in positions:
        p.cyl((x, y, z), r + 0.0022, w, 'Y', 18, CANVAS, cap=False)


def blaster(coll, mats, name, layout, y_muzzle, spring_z):
    """One gravel blaster.

    `layout` is the list of (x, z) barrel centres; `y_muzzle` the front of
    the pipes (more barrels get a longer or shorter cluster so the three
    variations differ in profile, not just count); `spring_z` where the two
    drive springs sit.
    """
    p = Part(mats)
    hard = []

    r = 0.036          # pipe radius
    y_breech = -0.095  # rear of the pipes, where the receiver swallows them

    xs = sorted({x for x, _ in layout})
    zs = sorted({z for _, z in layout})
    span_x = (xs[-1] - xs[0]) / 2.0 + r
    lo_z, hi_z = zs[0] - r, zs[-1] + r

    for x, z in layout:
        barrel(p, x, z, y_muzzle, y_breech, r)

    # Tape wraps, staggered per barrel so the repairs read as individual.
    band_ys = [y_muzzle + 0.155, y_muzzle + 0.345]
    wraps(p, [(x, z, band_ys[i % 2] + 0.020 * (i % 3), 0.048)
              for i, (x, z) in enumerate(layout)], r)

    # Front clamp: one strap over the whole cluster, riveted.
    clamp_y = y_muzzle + 0.130
    hard += p.box((0, clamp_y, (lo_z + hi_z) / 2.0),
                  (span_x * 2 + 0.014, 0.040, hi_z - lo_z + 0.014), STEEL)
    p.rivets((-span_x - 0.002, clamp_y, hi_z + 0.004),
             (span_x + 0.002, clamp_y, hi_z + 0.004), 2,
             radius=0.0045, height=0.0035, axis='Z', mat=STEEL)

    # Receiver: the forged block the pipes seat into.
    rec_z = (lo_z + hi_z) / 2.0
    rec_h = hi_z - lo_z + 0.030
    hard += p.box((0, -0.048, rec_z), (span_x * 2 + 0.026, 0.096, rec_h),
                  DARK)
    hard += p.box((0, 0.004, rec_z), (span_x * 2 + 0.010, 0.014, rec_h + 0.014),
                  STEEL)
    # Shell rims peeking out of the open breech — "load shells into exposed
    # barrels" is the whole reload fiction, so the brass has to be visible.
    for x, z in layout:
        p.cyl((x, 0.013, z), r - 0.006, 0.006, 'Y', 14, BRASS)
        p.cyl((x, 0.016, z), r - 0.020, 0.004, 'Y', 10, BLACK)

    # Drive springs on guide rods, riding above the receiver: the mechanism,
    # and deliberately the most detailed metal on the gun.
    spr_y0, spr_y1 = 0.012, 0.150
    for sx in (-1, 1):
        x = sx * 0.043
        p.cyl((x, (spr_y0 + spr_y1) / 2.0, spring_z), 0.0072,
              spr_y1 - spr_y0 + 0.030, 'Y', 10, STEEL)
        coil(p, x, spring_z, spr_y0, spr_y1, 0.0225, 0.0068, 6.0, BRASS)
        for y in (spr_y0 - 0.006, spr_y1 + 0.006):
            p.cyl((x, y, spring_z), 0.0265, 0.009, 'Y', 14, DARK)

    # Central spine rail the springs ride along, running from the receiver
    # back to the anchor — the guide the "slide back to compress" fiction
    # needs, and what visually carries the spring assembly.
    hard += p.box((0, 0.085, spring_z), (0.024, 0.170, 0.030), DARK)

    # Spring anchor: a vertical web dropping from the rod ends down into the
    # stock wrist, so the whole mechanism is visibly held by something.
    wrist = Vector((0.0, 0.205, -0.012))
    hard += p.box((0, 0.172, (spring_z + wrist.z) / 2.0 + 0.010),
                  (0.030, 0.026, spring_z - wrist.z + 0.050), DARK)

    # The fork: two dark arms from the receiver's flanks converging on the
    # wrist — the concept's single most recognisable structural shape.
    for sx in (-1, 1):
        hard += strut(p, (sx * (span_x + 0.002), 0.010, rec_z - 0.010),
                      (sx * 0.014, wrist.y - 0.010, wrist.z + 0.006),
                      (0.020, 0.034), DARK)

    # Hammer block seated on the anchor's crown, with a cocking pin.
    hard += p.box((0, 0.180, spring_z + 0.032), (0.034, 0.044, 0.034), DARK)
    p.cyl((0, 0.180, spring_z + 0.048), 0.0055, 0.052, 'X', 10, STEEL)

    # Trigger housing under the fork junction, so the blade and guard hang
    # off metal rather than floating in the gap behind the receiver.
    hard += p.box((0, 0.135, -0.020), (0.026, 0.044, 0.030), DARK)
    p.box((0, 0.138, -0.048), (0.009, 0.014, 0.038), STEEL,
          rot=Matrix.Rotation(math.radians(14), 4, 'X'))
    p.box((0, 0.136, -0.043), (0.012, 0.010, 0.016), RUBBER,
          rot=Matrix.Rotation(math.radians(14), 4, 'X'))
    strut(p, (0, 0.098, -0.028), (0, 0.106, -0.072), (0.010, 0.006), STEEL)
    strut(p, (0, 0.106, -0.072), (0, 0.172, -0.076), (0.010, 0.006), STEEL)
    strut(p, (0, 0.172, -0.076), (0, 0.196, -0.038), (0.010, 0.006), STEEL)

    # The stock: one loft from wrist to butt, dropping as it runs back.
    p.loft([
        (0.178, octagon(-0.010, 0.017, 0.032)),
        (0.240, octagon(-0.022, 0.019, 0.034)),
        (0.330, octagon(-0.048, 0.021, 0.044)),
        (0.430, octagon(-0.072, 0.024, 0.062)),
        (0.500, octagon(-0.078, 0.025, 0.072)),
    ], axis='Y', mat=WOOD)
    # Butt plate, proud of the wood by a hair.
    p.loft([
        (0.500, octagon(-0.078, 0.026, 0.074)),
        (0.516, octagon(-0.078, 0.024, 0.070)),
    ], axis='Y', mat=STEEL)

    # Cloth wrap where the trigger hand closes on the wrist.
    for i in range(5):
        y = 0.202 + i * 0.0135
        p.cyl((0, y, -0.012 - i * 0.0028), 0.0245 + (i % 2) * 0.0012, 0.011,
              'Y', 12, CANVAS)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll)


# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    # The hero: two pipes level, exactly the sheet's main figure.
    hero = collection("Coll_GravelBlaster_Twin")
    blaster(hero, mats, "Mesh_GravelBlaster_Twin",
            [(-0.043, 0.030), (0.043, 0.030)], y_muzzle=-0.700,
            spring_z=0.096)
    # Where the blast leaves the pipes, and where the trigger hand closes.
    # Read by GravelBlasterBuilder on the Unity side.
    marker(hero, "Marker_Muzzle", (0.0, -0.700, 0.030), mats)
    marker(hero, "Marker_Grip", (0.0, 0.205, -0.014), mats)

    # Three pipes in a triangle: taller silhouette, slightly shorter pipes —
    # the mod trades reach for another shot of gravel.
    blaster(collection("Coll_GravelBlaster_Triple"), mats,
            "Mesh_GravelBlaster_Triple",
            [(-0.043, 0.008), (0.043, 0.008), (0.0, 0.082)],
            y_muzzle=-0.640, spring_z=0.148)

    # Four pipes in a 2x2 block: the heavy mod, longest and squarest.
    blaster(collection("Coll_GravelBlaster_Quad"), mats,
            "Mesh_GravelBlaster_Quad",
            [(-0.043, 0.008), (0.043, 0.008), (-0.043, 0.082), (0.043, 0.082)],
            y_muzzle=-0.730, spring_z=0.148)

    report()
    save(out)


main()
