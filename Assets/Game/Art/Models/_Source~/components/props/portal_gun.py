"""Portal guns — handheld aperture emitters built on a fire-extinguisher chassis.

The design brief was a reference photo of a 5 litre foam extinguisher: chromed
bottle, swan-neck carry handle, squeeze lever, yellow safety pin and ring, side
clamp bracket and a black discharge horn. Everything about that silhouette is
kept; the only substitution is what the bottle holds. Instead of foam it carries
two reservoirs of portal fluid, orange and blue, shown through sight tubes on
the flanks so the charge state reads at a glance — the one detail that turns a
piece of safety equipment into a weapon without changing its shape.

Four variations, differing in silhouette rather than colour, because silhouette
is what survives a 256 px inventory icon:

  Coll_PortalGun_Extinguisher  the hero — upright bottle, horn as the barrel
  Coll_PortalGun_Twin          squat horizontal sidearm, two short reservoirs
  Coll_PortalGun_Sprayer       pressure-sprayer chassis with a long lance
  Coll_PortalGun_Spent         the hero, dented and drained: a world prop

Built at real extinguisher scale for a compact 2 kg bottle: 0.36 m tall overall,
0.116 m across the tank. Origin sits at the centre of the base ring, so each
variation stands on a surface without a Z nudge, matching the convention the
rest of components/props/ uses.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
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
# edge in the file. See components/props/item_devices_BUILD.md.
(STEEL, CHROME, DARK, RUBBER, YELLOW, BRASS, GLASS, PBLUE, PORANGE,
 LABEL, WARN, AMBER) = range(12)
MATS = [
    "Mat_Metal_Steel_Worn",         # 0  structural, and every bevel face
    "Mat_Metal_Chrome_Scuffed",     # 1  the bottle skin
    "Mat_Metal_Steel_Dark",         # 2  machined fittings, contrast panels
    "Mat_Plastic_Rubber_Black",     # 3  horn, hose, hand grips
    "Mat_Plastic_Safety_Yellow",    # 4  safety pin and pull ring
    "Mat_Metal_Brass_Tarnished",    # 5  valve body, collars
    "Mat_Glass_Canopy_Tinted",      # 6  sight tubes over the reservoirs
    "Mat_Emissive_Portal_Blue",     # 7  blue charge
    "Mat_Emissive_Portal_Orange",   # 8  orange charge
    "Mat_Paint_White_Arctic",       # 9  label wrap
    "Mat_Paint_Warn_Red",           # 10 hazard band
    "Mat_Emissive_Amber",           # 11 pressure gauge face
]

# Bevel only the boxy faces, and narrowly. At this scale a whole-part bevel
# exceeds half the radius of the thin swept tubing, and finish()'s
# remove_doubles then welds the over-bevelled ends into a blob.
BEVEL_W = 0.0018


# --------------------------------------------------------------------------
# Local helpers
# --------------------------------------------------------------------------

def sweep(p, pts, radius, mat, seg=8, radii=None):
    """A swept tube through a polyline, using parallel-transport frames.

    _buildlib has no sweep of its own, and a chain of overlapping `cyl` calls
    pinches visibly at the tight bends this model needs — the carry handle
    turns through 180 degrees over 8 cm. Transporting the normal from ring to
    ring instead of rebuilding it per segment is what stops the tube twisting
    where the path leaves the vertical plane.
    """
    pts = [Vector(q) for q in pts]
    n_pts = len(pts)

    tangents = []
    for i in range(n_pts):
        if i == 0:
            t = pts[1] - pts[0]
        elif i == n_pts - 1:
            t = pts[-1] - pts[-2]
        else:
            t = pts[i + 1] - pts[i - 1]
        tangents.append(t.normalized())

    # Seed the frame with any axis not parallel to the first tangent.
    seed = Vector((0.0, 0.0, 1.0))
    if abs(tangents[0].dot(seed)) > 0.95:
        seed = Vector((0.0, 1.0, 0.0))
    normal = (seed - tangents[0] * seed.dot(tangents[0])).normalized()

    rings = []
    for i, (q, t) in enumerate(zip(pts, tangents)):
        if i > 0:
            prev = tangents[i - 1]
            axis = prev.cross(t)
            if axis.length > 1e-6:
                normal = (Matrix.Rotation(prev.angle(t), 4, axis.normalized())
                          @ normal)
            normal = (normal - t * normal.dot(t)).normalized()
        binormal = t.cross(normal).normalized()
        r = radius if radii is None else radii[i]
        rings.append([
            q + normal * (math.cos(2 * math.pi * k / seg) * r)
              + binormal * (math.sin(2 * math.pi * k / seg) * r)
            for k in range(seg)])

    bm2 = bmesh.new()
    vrings = [[bm2.verts.new(tuple(c)) for c in ring] for ring in rings]
    for a, b in zip(vrings, vrings[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            bm2.faces.new((a[i], a[j], b[j], b[i]))
    bm2.faces.new(vrings[0])
    bm2.faces.new(list(reversed(vrings[-1])))

    faces = p._absorb(bm2, mat)
    for f in faces:
        f.smooth = True
    return faces


def arc(centre_y, centre_z, radius_y, radius_z, start_deg, end_deg, n=16):
    """Points along an elliptical arc in the YZ plane, at x = 0.

    The carry handle is swept along this rather than along a handful of
    hand-typed waypoints. Eight waypoints round a 200 degree bend put the
    sweep's rings far enough apart that the tube visibly facets, and the
    fatter rubber sleeve over it turned into a row of lumps — which is the
    exact defect this model was rebuilt to get rid of. Sampling a curve gives
    as many rings as the bend needs and keeps them evenly spaced.
    """
    return [(0.0,
             centre_y + radius_y * math.cos(math.radians(
                 start_deg + (end_deg - start_deg) * i / (n - 1))),
             centre_z + radius_z * math.sin(math.radians(
                 start_deg + (end_deg - start_deg) * i / (n - 1))))
            for i in range(n)]


def cone_forward(p, centre, base_r, tip_r, depth, mat, seg=20):
    """A cone along -Y whose WIDE end faces forward.

    `Part.cyl`'s `radius_top` is the +Z end of its local frame, and for
    `axis='Y'` that frame puts local +Z on world +Y — the back of this model.
    So the obvious spelling, `cyl(..., radius_top=mouth)`, builds a flare that
    opens towards the bottle. It did, on all three variations, and the muzzle
    looked like a funnel pointing the wrong way. Naming the direction here
    means no call site has to remember which end `radius_top` is.
    """
    return p.cyl(centre, base_r, depth, 'Y', seg, mat, radius_top=tip_r)


def circle(r, n=20, cx=0.0, cy=0.0, squash=1.0):
    """A closed circular profile for `loft`, in the plane perpendicular to Z."""
    return [(cx + math.cos(2 * math.pi * i / n) * r,
             cy + math.sin(2 * math.pi * i / n) * r * squash)
            for i in range(n)]


def bottle(p, sections, mat=CHROME, n=20):
    """Loft a stack of (z, radius) stations into a bottle shell.

    `loft` with axis='Z' maps a profile's (u, v) straight to (x, y) and the
    station offset to z, so the sections can be written as real heights.
    """
    return p.loft([(z, circle(r, n)) for z, r in sections], axis='Z', mat=mat)


def sight_tube(p, base, top, angle_deg, tank_r, fluid, level=1.0,
               radius=0.0125, drained=False):
    """One reservoir: a column of portal fluid behind a guard cage.

    The obvious construction — a hollow glass tube with a fluid cylinder inside
    it — was built first and is wrong, because a glass material is opaque to
    every renderer that is not doing refraction, and both EEVEE previews and
    Unity's own opaque queue simply showed a grey pipe. The fluid is the single
    detail that makes this a portal gun rather than an extinguisher, so it is
    the surface that must be visible: the column is exposed, and three chrome
    rods around it stand in for the tube. That is also a real level-gauge
    design, and it survives being a 256 px icon, which the glass never did.

    `level` is how full it is — the charge readout, and the reason a spent gun
    looks spent.
    """
    a = math.radians(angle_deg)
    # Front of the gun is -Y, so 0 degrees points down -Y and the two tubes
    # splay symmetrically around it.
    d = Vector((math.sin(a), -math.cos(a), 0.0))
    c = d * (tank_r * 0.95)
    mid = (base + top) / 2.0
    depth = top - base

    if not drained and level > 0.02:
        fluid_depth = depth * level
        p.cyl((c.x, c.y, base + fluid_depth / 2.0),
              radius - 0.0025, fluid_depth, 'Z', 16, fluid)
    # The unfilled head of the column, dark so the level line reads.
    if level < 0.98:
        empty_depth = depth * (1.0 - level)
        p.cyl((c.x, c.y, top - empty_depth / 2.0),
              radius - 0.0035, empty_depth, 'Z', 16, DARK)

    # Guard rods: three around the column, the front one thinner so it does not
    # cut the fluid in half from the angle the item is actually looked at.
    for k in range(3):
        ra = a + math.pi + 2 * math.pi * k / 3
        p.cyl((c.x + math.cos(ra) * radius, c.y + math.sin(ra) * radius, mid),
              0.0022 if k == 0 else 0.0026, depth, 'Z', 6, CHROME)

    for z in (base, top):
        p.cyl((c.x, c.y, z), radius + 0.0030, 0.011, 'Z', 16, CHROME)
        p.cyl((c.x, c.y, z), radius * 0.50, 0.015, 'Z', 10, BRASS)
    # Strap back to the tank wall, mid-height, so it is visibly held on.
    p.box((c.x * 0.60, c.y * 0.60, mid), (0.010, 0.010, 0.008), STEEL,
          rot=Matrix.Rotation(a, 4, 'Z'))


def gauge(p, centre, facing='Y', r=0.017, mat_face=AMBER):
    """A pressure gauge: chrome bezel, glass, glowing face, needle."""
    cx, cy, cz = centre
    p.cyl(centre, r, 0.012, facing, 16, CHROME)
    p.cyl((cx, cy - 0.005, cz) if facing == 'Y' else (cx - 0.005, cy, cz),
          r * 0.78, 0.006, facing, 16, mat_face)
    # The one place tinted glass belongs on this model: a flat cover over a lit
    # face, where an opaque fallback still reads as a gauge crystal.
    p.cyl((cx, cy - 0.008, cz) if facing == 'Y' else (cx - 0.008, cy, cz),
          r * 0.80, 0.002, facing, 16, GLASS)
    p.box((cx, cy - 0.009, cz + r * 0.28), (0.0016, 0.004, r * 0.55), DARK,
          rot=Matrix.Rotation(math.radians(24), 4, 'Y'))


def horn(p, root, drained=False):
    """The discharge horn — and the model's single, unmistakable output.

    Rebuilt after the first pass. Two things were wrong with it: the barrel was
    too short to dominate anything, and the top of the gun carried so many
    competing shapes that no one of them was obviously the business end. It now
    runs 0.17 m forward of the valve, making it the longest feature on the
    model, and there is nothing else on the gun it could be mistaken for.

    Four sections, back to front, each a different diameter so the taper reads
    at a glance: throat collar, knurled fore-grip, barrel, flared mouth. The
    fore-grip is the reference photograph's knurled black sleeve, and it is the
    second place a hand goes — the carry handle takes the weight, this one aims.

    The iris across the mouth is the only part that is not extinguisher: two
    concentric emissive discs, orange outside and blue inside, so both charges
    are readable down one muzzle instead of needing a second nozzle.
    """
    x, y, z = root

    # Throat: chrome collar clamping the horn to the valve.
    p.cyl((x, y - 0.013, z), 0.020, 0.026, 'Y', 16, CHROME)
    p.torus((x, y - 0.027, z), 0.019, 0.0035, 'Y', 18, 8, CHROME)

    # Knurled fore-grip. Ridges rather than a smooth tube: a plain cylinder
    # here reads as pipework, and the whole job of this section is to look held.
    grip_mid = y - 0.058
    p.cyl((x, grip_mid, z), 0.0175, 0.054, 'Y', 18, RUBBER)
    for i in range(9):
        p.box((x, grip_mid, z), (0.0030, 0.048, 0.0375), DARK,
              rot=Matrix.Rotation(math.pi * i / 9, 4, 'Y'))
    for ring_y in (y - 0.033, y - 0.083):
        p.cyl((x, ring_y, z), 0.0196, 0.006, 'Y', 18, CHROME)

    # Barrel, thinner than the grip so the silhouette steps down.
    p.cyl((x, y - 0.111, z), 0.0135, 0.052, 'Y', 16, DARK)
    p.cyl((x, y - 0.101, z), 0.0158, 0.008, 'Y', 16, STEEL)

    # Flare, wide enough that the eye lands here and nowhere else. Wide end
    # forward — see cone_forward for why that is not the obvious spelling.
    cone_forward(p, (x, y - 0.152, z), 0.030, 0.0135, 0.036, DARK)

    # The iris. Set flush with the mouth rather than recessed inside the bell:
    # sunk 3 mm back it fell into the cone's own shadow and the orange read as
    # grey, which cost the muzzle the one cue that says which end fires.
    mouth = y - 0.170
    p.torus((x, mouth, z), 0.0275, 0.0040, 'Y', 22, 8, CHROME)
    if not drained:
        p.cyl((x, mouth - 0.001, z), 0.0262, 0.004, 'Y', 22, PORANGE)
        p.torus((x, mouth - 0.003, z), 0.0205, 0.0032, 'Y', 20, 8, PORANGE)
        p.cyl((x, mouth - 0.004, z), 0.0120, 0.006, 'Y', 16, PBLUE)
    else:
        p.cyl((x, mouth - 0.001, z), 0.0262, 0.004, 'Y', 22, DARK)

    return mouth


def marker(coll, name, at, mats, size=0.004):
    """A tiny cube whose only job is to carry a coordinate across the FBX.

    Blender empties are not exported (the export is `object_types={"MESH"}`),
    and the FBX arrives in Unity with a -90 degree X rotation and a scale-100
    root, so hand-deriving a muzzle position on the Unity side means composing
    two conversions and hoping. A named 4 mm mesh survives the trip and the
    prefab builder reads its transform and strips the renderer, which is exact
    and needs nobody to be right about axis conventions.
    """
    p = Part(mats)
    p.box((0, 0, 0), (size, size, size), STEEL)
    obj = p.finish(name, coll)
    obj.location = at
    return obj


# --------------------------------------------------------------------------
# Variation 1 — the hero
# --------------------------------------------------------------------------

def extinguisher(coll, mats, name="Mesh_PortalGun_Extinguisher",
                 drained=False, dent=False):
    """Upright bottle, swan-neck handle, horn forward along -Y.

    Also builds the spent variation, because the two differ only in charge
    level and a few dents — rebuilding the whole chassis a second time to
    change that would be two files' worth of drift waiting to happen.
    """
    p = Part(mats)
    hard = []

    tank_r = 0.058
    shoulder_z = 0.268
    neck_z = 0.318

    # Bottle: domed base, straight barrel, shoulder taper, neck.
    bottle(p, [
        (0.000, 0.0180), (0.006, 0.0400), (0.016, 0.0520), (0.032, 0.0570),
        (0.050, tank_r), (shoulder_z, tank_r), (shoulder_z + 0.022, 0.0480),
        (shoulder_z + 0.038, 0.0330), (neck_z, 0.0250), (neck_z + 0.014, 0.0245),
    ])

    # Base ring the bottle actually stands on.
    p.cyl((0, 0, 0.006), 0.0505, 0.012, 'Z', 20, RUBBER)
    p.torus((0, 0, 0.012), 0.0495, 0.0035, 'Z', 20, 8, DARK)

    # Label wrap and hazard band, floated a hair off the skin so they read as
    # printed film rather than z-fighting with the chrome.
    p.cyl((0, 0, 0.170), tank_r + 0.0009, 0.150, 'Z', 20, LABEL, cap=False)
    p.cyl((0, 0, 0.084), tank_r + 0.0007, 0.020, 'Z', 20, WARN, cap=False)
    p.cyl((0, 0, 0.247), tank_r + 0.0007, 0.009, 'Z', 20, WARN, cap=False)

    # The two reservoirs. Orange left, blue right, matching which trigger fires
    # which — the left button throws the orange aperture.
    sight_tube(p, 0.092, 0.240, -52.0, tank_r, PORANGE,
               level=0.10 if drained else 0.86, drained=drained)
    sight_tube(p, 0.092, 0.240, 52.0, tank_r, PBLUE,
               level=0.06 if drained else 0.72, drained=drained)

    # Valve head.
    hard += p.box((0, 0.002, neck_z + 0.030), (0.052, 0.062, 0.044), BRASS)
    p.cyl((0, 0, neck_z + 0.006), 0.030, 0.016, 'Z', 16, BRASS)
    for i in range(10):
        a = 2 * math.pi * i / 10
        p.box((math.cos(a) * 0.029, math.sin(a) * 0.029, neck_z + 0.006),
              (0.005, 0.005, 0.014), BRASS, rot=Matrix.Rotation(a, 4, 'Z'))
    hard += p.box((0, 0.004, neck_z + 0.058), (0.040, 0.046, 0.014), DARK)

    # ── The way to hold it ────────────────────────────────────────────────
    #
    # Rebuilt after the first pass. That version drew the loop as a thin steel
    # tube and then stuck five separate rubber blocks along its top as a "palm
    # section", which at any distance read as a row of black lumps rather than
    # as a handle — and, standing next to the bent-rod lever and the feed hose,
    # turned the top of the bottle into a tangle of arms. It is now ONE
    # continuous arc with ONE continuous sleeve over the part a hand closes on,
    # and it is the tallest thing on the gun, which is what says "hold here".

    handle = arc(-0.002, neck_z + 0.050, 0.044, 0.064, -28.0, 208.0, n=18)
    sweep(p, handle, 0.0105, STEEL, seg=14)

    # One sleeve, not five blocks — and sampled off the SAME curve, so it sits
    # on the loop rather than beside it. Kept to the top third and only a
    # little fatter than the tube: the reference handle is bare metal, and a
    # sleeve over most of the arc turned the whole loop into a black croissant
    # that read as mass rather than as something to put a hand through.
    sweep(p, handle[6:13], 0.0132, RUBBER, seg=14)

    # Squeeze lever, hinged at the back of the valve and standing in the gap
    # under the handle. A flat solid bar rather than a bent rod: a lever you can
    # see is a lever you know to pull.
    lever_tilt = Matrix.Rotation(math.radians(-7), 4, 'X')
    hard += p.box((0, -0.004, neck_z + 0.062), (0.032, 0.086, 0.010), DARK,
                  rot=lever_tilt)
    p.box((0, -0.008, neck_z + 0.069), (0.034, 0.062, 0.005), RUBBER,
          rot=lever_tilt)
    p.cyl((0, 0.040, neck_z + 0.056), 0.0062, 0.040, 'X', 12, CHROME)

    # Safety pin through the lever, with its pull ring hanging off the side.
    p.cyl((0, 0.022, neck_z + 0.058), 0.0044, 0.086, 'X', 10, YELLOW)
    p.torus((0.050, 0.022, neck_z + 0.058), 0.026, 0.0052, 'X', 18, 8, YELLOW)
    if not drained:
        p.cyl((-0.046, 0.022, neck_z + 0.058), 0.0058, 0.008, 'X', 10, WARN)

    # Side clamp bracket. Flattened against the valve rather than standing off
    # it on a pin, which was a third shape competing for the top of the
    # silhouette with the two that have a job there.
    hard += p.box((0.048, 0.014, neck_z + 0.014), (0.014, 0.038, 0.044), STEEL)
    p.rivets((0.056, 0.028, neck_z + 0.000), (0.056, 0.028, neck_z + 0.028), 2,
             radius=0.0035, height=0.0026, axis='X', mat=STEEL)

    # Pressure gauge, tucked onto the flank of the valve and made smaller, so it
    # is a detail on the body rather than another silhouette element.
    gauge(p, (-0.040, -0.006, neck_z + 0.022), facing='Y', r=0.013,
          mat_face=DARK if drained else AMBER)

    # The one output. Built last and deliberately the longest thing here.
    horn(p, (0, -0.030, neck_z + 0.028), drained=drained)

    if dent:
        # Condition, not decoration: three inward dents in the bottle wall and
        # a bent bracket. Boxes pushed through the skin rather than displaced
        # geometry, which is all that survives at icon size anyway.
        for i, (a, z, s) in enumerate([(0.6, 0.115, 0.026), (2.6, 0.196, 0.020),
                                       (4.4, 0.146, 0.031)]):
            p.box((math.cos(a) * (tank_r + 0.004),
                   math.sin(a) * (tank_r + 0.004), z),
                  (s, s, s * 0.7), DARK, rot=Matrix.Rotation(a, 4, 'Z'))
        p.box((0.062, 0.034, neck_z - 0.020), (0.020, 0.014, 0.016), STEEL,
              rot=Matrix.Rotation(math.radians(18), 4, 'Y'))

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll)


# --------------------------------------------------------------------------
# Variation 2 — squat horizontal sidearm
# --------------------------------------------------------------------------

def twin(coll, mats):
    """Two short reservoirs lying on their sides over a pistol grip.

    Where the hero is a tall vertical bottle, this is a wide horizontal block —
    the strongest silhouette contrast available without leaving the family, and
    the one that still reads at thumbnail size.
    """
    p = Part(mats)
    hard = []

    grip_tilt = Matrix.Rotation(math.radians(-14), 4, 'X')

    # Pistol grip.
    hard += p.box((0, 0.014, 0.052), (0.038, 0.048, 0.104), DARK, rot=grip_tilt)
    for i in range(5):
        p.box((0, 0.014 + i * 0.0032, 0.018 + i * 0.019),
              (0.041, 0.052, 0.009), RUBBER, rot=grip_tilt)
    hard += p.box((0, 0.020, 0.005), (0.044, 0.056, 0.012), STEEL)

    body_z = 0.132

    # Receiver.
    hard += p.loft([(-0.030, [(-0.052, body_z - 0.028), (0.058, body_z - 0.034),
                              (0.058, body_z + 0.028), (-0.052, body_z + 0.022)]),
                    (0.030, [(-0.052, body_z - 0.028), (0.058, body_z - 0.034),
                             (0.058, body_z + 0.028), (-0.052, body_z + 0.022)])],
                   axis='X', mat=CHROME)
    hard += p.box((0, 0.020, body_z + 0.020), (0.056, 0.058, 0.016), DARK)
    hard += p.box((0, 0.030, body_z - 0.012), (0.050, 0.036, 0.020), WARN)

    # Trigger and guard.
    p.box((0, -0.016, body_z - 0.036), (0.010, 0.014, 0.026), STEEL,
          rot=Matrix.Rotation(math.radians(10), 4, 'X'))
    sweep(p, [(0, 0.004, body_z - 0.030), (0, -0.026, body_z - 0.038),
              (0, -0.034, body_z - 0.016), (0, -0.024, body_z + 0.000)],
          0.0048, STEEL, seg=8)

    # The two reservoirs, lying along the barrel axis either side of the body.
    for sx, fluid, level in ((-1, PORANGE, 0.80), (1, PBLUE, 0.64)):
        cx = sx * 0.050
        # Exposed column with a rod cage, not a glass sleeve — see sight_tube.
        p.cyl((cx, -0.004 + (0.096 * (1 - level)) / 2.0, body_z + 0.004),
              0.0158, 0.096 * level, 'Y', 16, fluid)
        p.cyl((cx, 0.044 - (0.096 * (1 - level)) / 2.0, body_z + 0.004),
              0.0150, 0.096 * (1 - level), 'Y', 16, DARK)
        for k in range(3):
            ra = math.pi / 2 + 2 * math.pi * k / 3
            p.cyl((cx + math.cos(ra) * 0.0182, -0.004,
                   body_z + 0.004 + math.sin(ra) * 0.0182),
                  0.0024, 0.096, 'Y', 6, CHROME)
        for y in (-0.052, 0.044):
            p.cyl((cx, y, body_z + 0.004), 0.0205, 0.010, 'Y', 16, CHROME)
        p.cyl((cx, 0.052, body_z + 0.004), 0.010, 0.014, 'Y', 10, BRASS)

    # Manifold bridging the two reservoirs into the muzzle.
    hard += p.box((0, -0.048, body_z + 0.004), (0.118, 0.020, 0.030), STEEL)
    p.cyl((0, -0.048, body_z + 0.004), 0.014, 0.124, 'X', 12, BRASS)

    # Stubby emitter.
    cone_forward(p, (0, -0.072, body_z + 0.004), 0.026, 0.020, 0.030, DARK, 18)
    p.torus((0, -0.088, body_z + 0.004), 0.023, 0.0035, 'Y', 20, 8, CHROME)
    p.cyl((0, -0.086, body_z + 0.004), 0.019, 0.004, 'Y', 20, PORANGE)
    p.cyl((0, -0.087, body_z + 0.004), 0.009, 0.006, 'Y', 16, PBLUE)

    gauge(p, (0, 0.052, body_z + 0.026), facing='Z', r=0.014)
    p.rivets((-0.040, 0.048, body_z + 0.028), (0.040, 0.048, body_z + 0.028), 4,
             radius=0.0034, height=0.0026, mat=STEEL)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_PortalGun_Twin", coll)


# --------------------------------------------------------------------------
# Variation 3 — pressure sprayer with a lance
# --------------------------------------------------------------------------

def sprayer(coll, mats):
    """Garden-sprayer chassis: pump plunger on top, long lance off a hose.

    Third silhouette: neither a bottle held by its handle nor a pistol, but a
    tank that stands on the ground with a separate wand. Useful as a workshop
    prop even where nobody is holding it.
    """
    p = Part(mats)
    hard = []

    tank_r = 0.070

    bottle(p, [
        (0.000, 0.030), (0.008, 0.058), (0.020, 0.068), (0.034, tank_r),
        (0.212, tank_r), (0.226, 0.066), (0.236, 0.046), (0.244, 0.040),
    ], mat=YELLOW)

    p.cyl((0, 0, 0.008), 0.062, 0.016, 'Z', 20, DARK)
    p.cyl((0, 0, 0.126), tank_r + 0.0009, 0.084, 'Z', 20, LABEL, cap=False)
    p.cyl((0, 0, 0.196), tank_r + 0.0007, 0.014, 'Z', 20, WARN, cap=False)

    # Pump collar and plunger handle.
    p.cyl((0, 0, 0.252), 0.042, 0.020, 'Z', 18, DARK)
    p.cyl((0, 0, 0.292), 0.013, 0.064, 'Z', 12, CHROME)
    sweep(p, [(-0.034, 0, 0.330), (0, 0, 0.342), (0.034, 0, 0.330)],
          0.0095, DARK, seg=10)

    # A single wide sight window on the front instead of two tubes: the fluids
    # are mixed in this chassis, which is why its lance fires one colour at a
    # time and the tank shows a two-layer separation.
    p.cyl((0, -tank_r * 0.86, 0.116), 0.0186, 0.076, 'Z', 16, PORANGE)
    p.cyl((0, -tank_r * 0.86, 0.176), 0.0186, 0.044, 'Z', 16, PBLUE)
    for k in range(3):
        ra = math.pi / 2 + 2 * math.pi * k / 3
        p.cyl((math.cos(ra) * 0.0212, -tank_r * 0.86 + math.sin(ra) * 0.0212,
               0.140), 0.0026, 0.130, 'Z', 6, CHROME)
    for z in (0.076, 0.204):
        p.cyl((0, -tank_r * 0.86, z), 0.0232, 0.011, 'Z', 16, CHROME)

    # Shoulder strap lugs.
    for sx in (-1, 1):
        hard += p.box((sx * (tank_r - 0.004), 0.020, 0.206),
                      (0.016, 0.026, 0.014), STEEL)

    # Hose out of the tank shoulder, curving forward to the lance.
    sweep(p, [(0.044, 0.030, 0.232), (0.078, -0.010, 0.212),
              (0.086, -0.062, 0.176), (0.062, -0.098, 0.146),
              (0.028, -0.106, 0.132)], 0.0072, RUBBER, seg=8)

    # Lance: trigger handle, long tube, nozzle.
    hard += p.box((0.006, -0.116, 0.132), (0.030, 0.058, 0.034), DARK,
                  rot=Matrix.Rotation(math.radians(-8), 4, 'X'))
    for i in range(4):
        p.box((0.006, -0.116 - 0.004 + i * 0.0028, 0.116 + i * 0.011),
              (0.033, 0.062, 0.008), RUBBER)
    p.box((0.006, -0.140, 0.112), (0.009, 0.013, 0.022), STEEL,
          rot=Matrix.Rotation(math.radians(12), 4, 'X'))
    sweep(p, [(0.006, -0.128, 0.150), (0.006, -0.150, 0.158),
              (0.006, -0.158, 0.140), (0.006, -0.148, 0.126)],
          0.0044, STEEL, seg=8)

    p.cyl((0.006, -0.150, 0.156), 0.0115, 0.150, 'Y', 14, CHROME)
    p.cyl((0.006, -0.150, 0.156), 0.0150, 0.014, 'Y', 14, BRASS)
    lance_tip = -0.150 - 0.075
    cone_forward(p, (0.006, lance_tip - 0.016, 0.156), 0.0210, 0.0115, 0.032,
                 DARK, 16)
    p.torus((0.006, lance_tip - 0.033, 0.156), 0.0185, 0.0032, 'Y', 18, 8,
            CHROME)
    p.cyl((0.006, lance_tip - 0.031, 0.156), 0.0155, 0.004, 'Y', 18, PORANGE)
    p.cyl((0.006, lance_tip - 0.032, 0.156), 0.0072, 0.006, 'Y', 14, PBLUE)

    gauge(p, (0, 0.052, 0.238), facing='Z', r=0.015)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_PortalGun_Sprayer", coll)


# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    hero = collection("Coll_PortalGun_Extinguisher")
    extinguisher(hero, mats)
    # Where the aperture leaves the horn, and where a hand closes on the loop.
    # Both are read by the Unity prefab builder; see portal_gun_BUILD.md.
    marker(hero, "Marker_Muzzle", (0.0, -0.204, 0.346), mats)
    marker(hero, "Marker_Grip", (0.0, 0.006, 0.429), mats)

    twin(collection("Coll_PortalGun_Twin"), mats)
    sprayer(collection("Coll_PortalGun_Sprayer"), mats)

    spent = collection("Coll_PortalGun_Spent")
    extinguisher(spent, mats, name="Mesh_PortalGun_Spent",
                 drained=True, dent=True)

    report()
    save(out)


main()
