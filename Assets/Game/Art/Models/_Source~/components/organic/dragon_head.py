"""Chinese dragon heads — cast ornamental muzzle pieces.

A lion-dog skull in the Chinese temple-guardian register rather than a western
wyrm: short blunt snout, heavy jowls, a domed brow over a bulging eye, branched
deer horns swept back over the neck, and long wire whiskers curling forward off
the nostrils. Vermilion lacquer over the scaled mass, gold leaf on every
ornament — horns, fangs, brow ridge, whisker wire and the flame mane at the
collar.

The mouth is a real hole. Each variation's throat is bored straight through on
the -Y axis so a launch tube can fire out of it, which is the whole reason this
is a component and not a decal: the head IS the muzzle.

Three variations, differing in silhouette rather than colour:

  Coll_DragonHead_Roaring   jaws wide, horns tall — the hero, ships as the
                            bazooka's muzzle
  Coll_DragonHead_Snarling  jaws barely parted, horns swept low and tight; a
                            closed, watchful read for a static fitting
  Coll_DragonHead_Whelp     half scale, one horn pair, no whiskers — cheap
                            enough to fly on a projectile

Front is -Y, up is +Z, per library convention. The origin sits at the collar
ring on the bore axis, so the head drops straight onto a tube muzzle without an
offset. Hero head is 0.26 m nose to collar.

The lower jaw is its OWN object with its origin on the hinge axis, and there is
deliberately no armature: the jaw is one rigid part turning about one axis, so
an object pivot carries it and the FBX ships a plain transform the game can
animate directly. Same reasoning as sucker_puncher.py's ram.

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

# Index 0 first and it must be the lacquer: `bmesh.ops.bevel` stamps every face
# it creates with material index 0, and on this part almost every bevelled edge
# is on the painted skull. Putting metal here (the gravel blaster's choice, and
# right for a gun) outlines the whole head in gold wire.
(VERM, GOLD, IVORY, BLACK, AMBER, JADE) = range(6)
MATS = [
    "Mat_Paint_Lacquer_Vermilion",  # 0  skull, jaw, nose — and every bevel
    "Mat_Metal_Gold_Leaf",          # 1  horns, fangs, brow, whiskers, mane
    "Mat_Hide_Ivory_Spine",         # 2  the tooth rows
    "Mat_Neutral_Black_Matte",      # 3  throat bore, nostril holes, pupils
    "Mat_Emissive_Amber",           # 4  the lit eye
    "Mat_Metal_Copper_Oxide",       # 5  jade scale banding at the collar
]

# Narrow, for the reason the gravel blaster documents: the whiskers are 4 mm
# wire, and a bevel wider than their radius makes finish()'s remove_doubles
# weld them into a blob.
BEVEL_W = 0.0016


# --------------------------------------------------------------------------
# Local helpers
# --------------------------------------------------------------------------

def blob(hw, hh, zc, n=12, squash=0.72):
    """A superelliptic profile in (x, z) — the head's cross-section.

    A plain ellipse reads as a pipe and a rectangle reads as a crate; a
    superellipse with a fat exponent is what gives the skull flat cheek planes
    that still turn a round corner. `squash` flattens the underside so the
    jowls sit heavier than the crown, which is most of what makes the silhouette
    read as an animal rather than a lozenge.
    """
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n
        cs, sn = math.cos(a), math.sin(a)
        u = hw * math.copysign(abs(cs) ** squash, cs)
        v = hh * math.copysign(abs(sn) ** squash, sn)
        pts.append((u, zc + v))
    return pts


def sweep(p, path, radii, mat, seg=8, cap=True):
    """A tapering tube swept along an arbitrary path.

    Frames come from the tangent plus a fixed world up, which is legitimate
    here and would not be in general: every swept part on this head — horns,
    whiskers, mane spikes — runs broadly fore-and-aft and never turns far
    enough toward vertical for the up-reference to degenerate. The helix in
    gravel_blaster.py needed analytic frames because it genuinely spins.
    """
    path = [Vector(q) for q in path]
    rings = []
    for i, centre in enumerate(path):
        if i == 0:
            tan = (path[1] - path[0])
        elif i == len(path) - 1:
            tan = (path[-1] - path[-2])
        else:
            tan = (path[i + 1] - path[i - 1])
        tan.normalize()

        up = Vector((0, 0, 1))
        if abs(tan.dot(up)) > 0.95:
            up = Vector((0, 1, 0))
        side = tan.cross(up).normalized()
        up = side.cross(tan).normalized()

        r = radii[i]
        rings.append([
            centre + side * (math.cos(2 * math.pi * k / seg) * r)
                   + up * (math.sin(2 * math.pi * k / seg) * r)
            for k in range(seg)])

    bm2 = bmesh.new()
    vrings = [[bm2.verts.new(tuple(c)) for c in ring] for ring in rings]
    for a, b in zip(vrings, vrings[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            bm2.faces.new((a[i], a[j], b[j], b[i]))
    if cap:
        bm2.faces.new(vrings[0])
        bm2.faces.new(list(reversed(vrings[-1])))

    faces = p._absorb(bm2, mat)
    for f in faces:
        f.smooth = True
    return faces


def arc(a, b, bulge, steps):
    """Points along a quadratic bend from `a` to `b`, bowed by `bulge`.

    Horns and whiskers are curves, and typing their control points as a list of
    hand-guessed triples is how the left one ends up not matching the right.
    """
    a, b, bulge = Vector(a), Vector(b), Vector(bulge)
    mid = (a + b) / 2.0 + bulge
    out = []
    for i in range(steps):
        t = i / (steps - 1)
        out.append((1 - t) ** 2 * a + 2 * (1 - t) * t * mid + t ** 2 * b)
    return out


def taper(r0, r1, n, power=1.0):
    return [r0 + (r1 - r0) * ((i / (n - 1)) ** power) for i in range(n)]


def marker(coll, name, at, mats, size=0.004):
    """A tiny cube whose only job is to carry a coordinate across the FBX.

    Empties are not exported (object_types={"MESH"}), so a named 4 mm mesh
    survives the trip; the Unity prefab builder reads its transform and strips
    the renderer. Same trick as portal_gun.py and gravel_blaster.py.
    """
    p = Part(mats)
    p.box((0, 0, 0), (size, size, size), GOLD)
    obj = p.finish(name, coll)
    obj.location = at
    return obj


def bored_cranium(mats, sections, radius, y_back, y_front, z):
    """The cranium loft with the gullet cut out of it, as a loose mesh.

    A genuine boolean, because the head IS the muzzle. The first pass inset a
    black cylinder inside the skull and it was invisible from every angle —
    the lofted palate simply closed over it — and a weapon whose barrel is
    painted on reads as an ornament.

    The cutter sits LOW on purpose: its underside falls below the palate, so
    the tunnel it removes is open downward into the mouth for its whole length
    as well as open at the back. That is what makes a dropped jaw show a deep
    dark throat instead of a solid red roof. Cutting on the snout's own axis
    instead would have been a nostril, and at any radius wide enough to pass a
    rocket it took the top off the nose.

    <b>Only the loft is cut, and that is the whole reason this is a separate
    function.</b> The exact solver wants a closed manifold, and the finished
    head is nothing of the sort — horns, teeth, eyes and mane are a dozen
    interpenetrating shells sharing one mesh. Handing it the whole head cost
    two thirds of its triangles: the ornaments came back silently deleted, with
    the boolean reporting success. So the shell is bored alone and the
    ornaments are merged in afterwards.

    Faces the cut creates inherit the cutter's material, which is why the
    cutter is built from the same palette list — the gullet walls come out
    black without a second lining object to keep aligned.

    Applied through the depsgraph rather than `bpy.ops.object.modifier_apply`,
    which needs an active object and a context a `--background` run does not
    reliably have.
    """
    scene_coll = bpy.context.scene.collection

    shell = Part(mats)
    shell.loft(sections, axis='Y', mat=VERM)
    target = shell.finish("_BoreTarget", scene_coll)

    cut = Part(mats)
    cut.cyl((0, (y_back + y_front) / 2.0, z), radius, abs(y_front - y_back),
            'Y', 24, BLACK)
    cutter = cut.finish("_BoreCutter", scene_coll)

    mod = target.modifiers.new("Bore", 'BOOLEAN')
    mod.object = cutter
    mod.operation = 'DIFFERENCE'
    mod.solver = 'EXACT'

    deps = bpy.context.evaluated_depsgraph_get()
    baked = bpy.data.meshes.new_from_object(target.evaluated_get(deps))

    for obj in (target, cutter):
        mesh = obj.data
        bpy.data.objects.remove(obj)
        bpy.data.meshes.remove(mesh)

    return baked


# --------------------------------------------------------------------------
# The head
# --------------------------------------------------------------------------

def skull(coll, mats, name, s, horn_sweep, horn_len, whiskers, mane_count,
          bore_r):
    """Everything above the jaw hinge, as one object.

    `s` scales the whole head off the hero's 0.26 m. Every dimension below is
    written at hero scale and multiplied, so a variation cannot drift out of
    proportion with itself.
    """
    p = Part(mats)

    # ── Cranium and snout ──
    # Stations run back-to-front. The widest is at the jowl just ahead of the
    # collar, and the bridge pinches behind the nose — that pinch is what stops
    # the profile reading as a traffic cone.
    sections = [
        (+0.020 * s, blob(0.060 * s, 0.036 * s, 0.034 * s)),
        (-0.015 * s, blob(0.076 * s, 0.046 * s, 0.038 * s)),
        (-0.055 * s, blob(0.072 * s, 0.045 * s, 0.042 * s)),
        (-0.100 * s, blob(0.053 * s, 0.031 * s, 0.032 * s)),
        (-0.150 * s, blob(0.049 * s, 0.028 * s, 0.030 * s)),
        (-0.200 * s, blob(0.053 * s, 0.031 * s, 0.032 * s)),
        (-0.232 * s, blob(0.040 * s, 0.024 * s, 0.030 * s)),
        (-0.244 * s, blob(0.022 * s, 0.014 * s, 0.028 * s)),
    ]
    # Bored on its own and merged straight into this part's bmesh — the shell
    # is the only closed manifold on the head, and it is the only thing the
    # boolean can safely be pointed at. Material indices survive the round trip
    # because both sides index the same MATS list.
    # Front of the cut stops at the nose flare rather than piercing the snout
    # tip: the tip station is only 28 mm deep, so a bore wide enough to pass a
    # rocket takes the whole nose off. Ending it here still leaves 175 mm of
    # dark channel above the tooth line, which is what the mouth actually shows.
    shell = bored_cranium(mats, sections, bore_r * s, 0.040 * s, -0.215 * s,
                          0.012 * s)
    p.bm.from_mesh(shell)
    bpy.data.meshes.remove(shell)

    # ── Brow ridges ──
    # Gold, heavy, angled, and SHORT: the single detail that decides whether
    # the head looks angry or surprised. The first pass ran these 70 mm down
    # the snout and they read as gold tape rather than bone — a brow has to sit
    # over the eye and stop.
    hard = []
    for sx in (-1, 1):
        hard += p.box((sx * 0.056 * s, -0.050 * s, 0.068 * s),
                      (0.040 * s, 0.040 * s, 0.026 * s), GOLD,
                      rot=Matrix.Rotation(math.radians(sx * -16), 4, 'Y')
                          @ Matrix.Rotation(math.radians(16), 4, 'X'))

    # ── Eyes ──
    # A domed cylinder rather than a sphere: _buildlib has no sphere, and a
    # tapered 12-sided disc smooth-shaded reads as one at this size. Sunk into
    # the cheek far enough to sit proud of it — at the jowl's widest the skull
    # is 76 mm out, so an eye at 56 mm was buried inside its own head.
    #
    # The taper has to be flipped per side. `cyl` measures radius_top toward
    # +X regardless of where the part sits, so one sign gives a dome bulging
    # outward and the other gives a funnel opening outward — which is exactly
    # what the left eye came out as, and it read as a black box glued to the
    # cheek. The pupil is recessed inside the dome's outer face for the same
    # reason: sitting proud of it, it was a cube, not a pupil.
    for sx in (-1, 1):
        wide, narrow = 0.024 * s, 0.011 * s
        p.cyl((sx * 0.062 * s, -0.048 * s, 0.046 * s),
              wide if sx > 0 else narrow, 0.028 * s, 'X', 12, AMBER,
              radius_top=narrow if sx > 0 else wide)
        p.cyl((sx * 0.068 * s, -0.049 * s, 0.046 * s), 0.008 * s, 0.011 * s,
              'X', 8, BLACK)

    # ── Nose and nostrils ──
    hard += p.box((0, -0.216 * s, 0.044 * s),
                  (0.070 * s, 0.030 * s, 0.024 * s), VERM,
                  rot=Matrix.Rotation(math.radians(-12), 4, 'X'))
    for sx in (-1, 1):
        p.cyl((sx * 0.021 * s, -0.228 * s, 0.048 * s), 0.008 * s, 0.014 * s,
              'Y', 8, BLACK)

    # ── Horns ──
    # Deer horns swept back over the neck, each with one forward-raked branch.
    # `horn_sweep` tips the whole pair back; the whelp gets a short stub pair.
    for sx in (-1, 1):
        root = Vector((sx * 0.040 * s, -0.030 * s, 0.070 * s))
        tip = root + Vector((sx * 0.030 * s,
                             (0.055 + horn_len) * s,
                             (0.075 + horn_len * 0.5) * s))
        path = arc(root, tip, (sx * 0.014 * s, -0.030 * s, horn_sweep * s), 9)
        sweep(p, path, taper(0.014 * s, 0.004 * s, 9, power=0.8), GOLD)

        if horn_len > 0.02 * s or horn_len > 0.0:
            branch_root = path[4]
            branch_tip = branch_root + Vector((sx * 0.030 * s, -0.030 * s,
                                               0.038 * s))
            sweep(p, arc(branch_root, branch_tip,
                         (sx * 0.006 * s, -0.010 * s, 0.008 * s), 6),
                  taper(0.008 * s, 0.003 * s, 6), GOLD)

    # ── Flame mane at the collar ──
    # Gold spikes fanned around the back of the skull. They read as the cast
    # collar that clamps the head onto whatever it is mounted to, and they hide
    # the join.
    for i in range(mane_count):
        a = math.pi * (i + 0.5) / mane_count
        radial = Vector((math.cos(a), 0.0, math.sin(a)))
        root = Vector((0, 0.014 * s, 0.030 * s)) + radial * (0.056 * s)
        tip = root + radial * (0.030 * s) + Vector((0, 0.052 * s, 0.006 * s))
        sweep(p, arc(root, tip, (0, 0.004 * s, 0.012 * s), 6),
              taper(0.011 * s, 0.002 * s, 6), GOLD)

    # ── Collar ring ──
    # Jade banding over the lacquer: the one cool note, and the seam that says
    # the head is a separate casting bolted on.
    p.tube((0, 0.016 * s, 0.030 * s), 0.062 * s, 0.008 * s, 0.014 * s, 'Y',
           16, JADE)

    # ── Whiskers ──
    # Out and UP rather than straight ahead. The first pass reached 150 mm past
    # the nose on a 260 mm head, which put half the part's bounding box in
    # front of the muzzle — on a shouldered weapon that is 150 mm of gold wire
    # hanging in the player's aim. They now flick forward, roll outboard and
    # curl back over the brow, which is the shape they are drawn in anyway.
    if whiskers:
        for sx in (-1, 1):
            root = Vector((sx * 0.030 * s, -0.226 * s, 0.040 * s))
            tip = root + Vector((sx * 0.062 * s, 0.006 * s, 0.062 * s))
            sweep(p, arc(root, tip, (sx * 0.030 * s, -0.052 * s, 0.006 * s), 12),
                  taper(0.0055 * s, 0.0018 * s, 12), GOLD, seg=6)

    # ── Upper tooth row ──
    for i, y in enumerate((-0.090, -0.128, -0.166, -0.202)):
        r = (0.011 if i == 0 else 0.007) * s
        for sx in (-1, 1):
            p.cyl((sx * 0.041 * s, y * s, 0.010 * s), r, 0.026 * s, 'Z', 6,
                  GOLD if i == 0 else IVORY, radius_top=0.0005 * s)

    p.bevel(hard, width=BEVEL_W * s, segments=2)
    return p.finish(name, coll)


def jaw(coll, mats, name, s, open_deg, hinge):
    """The lower jaw, built about its own hinge and rotated open.

    Geometry is authored in head space and then turned about the hinge axis, so
    the tooth row still meets the upper one when `open_deg` is zero. The object
    origin lands on the hinge, which is what lets the game animate a roar by
    setting one local rotation.
    """
    p = Part(mats)

    sections = [
        (+0.010 * s, blob(0.058 * s, 0.020 * s, -0.020 * s)),
        (-0.040 * s, blob(0.062 * s, 0.024 * s, -0.024 * s)),
        (-0.100 * s, blob(0.050 * s, 0.021 * s, -0.021 * s)),
        (-0.160 * s, blob(0.043 * s, 0.018 * s, -0.018 * s)),
        (-0.205 * s, blob(0.034 * s, 0.014 * s, -0.014 * s)),
        (-0.218 * s, blob(0.018 * s, 0.008 * s, -0.012 * s)),
    ]
    p.loft(sections, axis='Y', mat=VERM)

    # Chin beard: a small gold tuft under the jaw tip. Deliberately stubby —
    # the first pass swept it 86 mm down and back off a 218 mm jaw, and with
    # the jaw dropped open for a roar the tuft hung lower than the jaw itself
    # and read as a second mandible.
    sweep(p, arc((0, -0.202 * s, -0.018 * s), (0, -0.176 * s, -0.052 * s),
                 (0, -0.014 * s, -0.008 * s), 7),
          taper(0.010 * s, 0.003 * s, 7), GOLD, seg=8)

    # Lower tooth row, interleaving with the upper one.
    for i, y in enumerate((-0.072, -0.110, -0.148, -0.188)):
        r = (0.010 if i == 0 else 0.0065) * s
        for sx in (-1, 1):
            p.cyl((sx * 0.038 * s, y * s, -0.006 * s), r, 0.024 * s, 'Z', 6,
                  GOLD if i == 0 else IVORY, radius_top=0.0005 * s)

    # Turn the whole jaw about the hinge. Rotating about +X takes -Y onto -Z,
    # which drops the front of the jaw — the direction a mouth opens.
    hinge = Vector(hinge)
    turn = (Matrix.Translation(hinge)
            @ Matrix.Rotation(math.radians(open_deg), 4, 'X')
            @ Matrix.Translation(-hinge))
    bmesh.ops.transform(p.bm, matrix=turn, verts=p.bm.verts)

    p.bevel(width=BEVEL_W * s, segments=2, angle=60.0)
    return p.finish(name, coll, origin=hinge)


def head(coll, mats, tag, s=1.0, open_deg=30.0, horn_sweep=0.055,
         horn_len=0.030, whiskers=True, mane_count=7, bore_r=0.022):
    """One complete head: skull, bored throat, lining, and hinged jaw.

    The bore runs from behind the collar to just short of the snout tip, so a
    launch tube slides into the back and the rocket leaves through the teeth.
    """
    hinge = (0.0, -0.026 * s, 0.006 * s)
    skull(coll, mats, "Mesh_DragonHead_%s" % tag, s, horn_sweep, horn_len,
          whiskers, mane_count, bore_r)
    jaw(coll, mats, "Mesh_DragonJaw_%s" % tag, s, open_deg, hinge)
    return hinge


# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    # The hero: jaws wide, horns tall. This is the one that ships as the
    # bazooka's muzzle, so its bore is sized to the launch tube's 60 mm mouth.
    hero = collection("Coll_DragonHead_Roaring")
    hinge = head(hero, mats, "Roaring")
    # Where the rocket leaves the teeth, and where the head meets the tube.
    # Read by DragonBazookaBuilder on the Unity side.
    marker(hero, "Marker_Throat", (0.0, -0.196, 0.012), mats)
    marker(hero, "Marker_Collar", (0.0, 0.020, 0.012), mats)
    marker(hero, "Marker_JawHinge", hinge, mats)

    # Mouth almost shut, horns raked low and back, no flame mane: a watchful
    # head for a doorway boss or a shrine fitting rather than a muzzle.
    head(collection("Coll_DragonHead_Snarling"), mats, "Snarling",
         open_deg=7.0, horn_sweep=0.014, horn_len=0.010, mane_count=5)

    # Half scale, stub horns, no whiskers — light enough to fly on a rocket,
    # and the wire whiskers would be invisible at this size anyway.
    head(collection("Coll_DragonHead_Whelp"), mats, "Whelp", s=0.5,
         open_deg=34.0, horn_sweep=0.030, horn_len=0.0, whiskers=False,
         mane_count=5, bore_r=0.018)

    report()
    save(out)


main()
