"""Grapple harpoon heads — the dart on the end of the grappling hook's rope.

The grappling hook ships with a bare `LineRenderer` and nothing on the end of
it. This file is the missing object: a spear dart that flies off the launcher,
buries itself in a surface and holds the rope.

Deliberately a harpoon rather than a claw grapnel. A grapnel reads as three or
four thin hooks, and thin hooks disappear at the distance this thing is
actually looked at — the far end of a 30 m rope. A dart has one solid mass and
a barb ring, which still reads as a shape at 50 m.

Aesthetic brief: the launcher it pairs with is a retro sci-fi blaster, so this
is salvaged spacer hardware — machined steel, a brass ferrule, one high-vis
paint band. **Nothing on it glows.** No emissive material appears in `MATS`
and none should be added; the whole point of the item is that it is plain,
physical, expendable ironmongery.

## Axes, and why they are what they are

The library builds −Y forward / +Z up, and `_exportlib`'s FBX flags
(`axis_forward='-Z'`, `axis_up='Y'`) map Blender `(x, y, z)` onto Unity
`(x, z, −y)`. So Blender **−Y lands on Unity +Z**, which is exactly what
`Quaternion.LookRotation(travelDirection)` wants.

Therefore the tip points down **−Y** in Blender. Nothing in this file may be
built along +Y "because it looked right in the viewport" — that would ship a
dart that flies backwards.

## Origin

At the **rope anchor**: the centre of the eyelet hole, at `(0, 0, 0)`. That is
this object's one real connection point, which is the library's origin rule,
and it makes the Unity side trivial — the `LineRenderer`'s last position is the
head's `transform.position`, with no marker lookup and no offset to get wrong.

The consequence, which the Unity code has to know: the model extends into
**negative Y only**, so the tip is at −length, and a dart whose tip should sit
exactly on a raycast hit point is placed at `hit.point − dir * tipOffset`.

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

# Index 0 first: `bmesh.ops.bevel` stamps every face it creates with material
# index 0, so a structural metal has to sit there or every chamfer in the file
# comes out the colour of an accent. STEEL is also the dominant surface.
STEEL, DARK, BRASS, RUBBER, ORANGE = range(5)
MATS = ["Mat_Metal_Steel_Worn",       # collar, shank, eyelet — the body
        "Mat_Metal_Steel_Dark",       # hardened tip and barbs
        "Mat_Metal_Brass_Tarnished",  # ferrule rings, the one warm note
        "Mat_Plastic_Rubber_Black",   # damper band behind the collar
        "Mat_Paint_Safety_Orange"]    # one high-vis band, so it is findable

# 1.2 mm. Everything here is 10-30 mm thick; the library's 12 mm default would
# exceed half the radius of the shank and `finish()`'s remove_doubles would
# weld the over-bevelled ends into a lump (see project_buildlib_traps).
BEVEL_W = 0.0012


class TrackedPart(Part):
    """A `Part` that identifies absorbed geometry by identity, not by index.

    ## The bug this exists for

    `Part._absorb` — the path every `torus`, `tube`, `prism` and `loft` goes
    through — records `n_before = len(self.bm.faces)`, calls
    `bm.from_mesh(scratch)`, and then claims `self.bm.faces[n_before:]` as the
    faces it just made. That assumption is false: `from_mesh` does **not**
    leave the existing faces in their old index slots. Measured on Blender
    5.1.1, a Part holding a 6-face cone and an 8-face cone, absorbing a 72-face
    torus:

        torus's returned list overlaps the second cone by 6 faces
        final material counts {DARK: 8, STEEL: 6, BRASS: 72}

    — six faces of the cone were stamped with the torus's material, and six
    faces of the torus were left on index 0. The slice has the right *length*
    every time, which is exactly why nobody has caught it: the model builds,
    the counts look plausible, and the only symptom is a handful of faces
    wearing the wrong material.

    It cost a build here. The first pass shipped a harpoon whose shoulder cone
    was brass instead of hardened steel, because the ferrule torus behind it
    landed on the cone's side faces. Nothing errored; the preview render just
    had a gold nose.

    Fixed locally rather than in `_buildlib`, because `_buildlib` is imported
    by every component in the library and some of them will have been tuned by
    eye against the wrong colours — correcting it centrally would silently
    restyle models nobody asked to change. That is a deliberate, separate job.

    ## The fix

    `_absorb` is overridden to diff the face set before and after, which is
    exact regardless of what `from_mesh` does to the ordering. Face references
    survive `from_mesh` intact (documented in `project_buildlib_traps`), so the
    diff is safe.

    `restamp()` then replays every (faces, index) pair in creation order as a
    belt-and-braces pass before `bevel()`. It should be a no-op now, and it
    prints how many faces it had to correct precisely so that a future Blender
    reordering shows up as a number instead of as a mysterious gold nose.
    Bevel's own new faces are not in the log, so they keep index 0 — which is
    why MATS[0] has to stay a structural metal.
    """

    def __init__(self, materials):
        super().__init__(materials)
        self._stamps = []

    def _tag(self, faces, mat):
        faces = list(faces)
        self._stamps.append((faces, mat))
        return super()._tag(faces, mat)

    def _absorb(self, bm2, mat):
        before = set(self.bm.faces)
        n_log = len(self._stamps)
        super()._absorb(bm2, mat)
        del self._stamps[n_log:]        # drop the bogus index-slice stamp
        new = [f for f in self.bm.faces if f not in before]
        return self._tag(new, mat)

    def restamp(self):
        n = 0
        for faces, mat in self._stamps:
            for f in faces:
                if f.is_valid and f.material_index != mat:
                    f.material_index = mat
                    n += 1
        return n


def _y(f):
    """Forward distance `f` (metres from the origin toward the tip) as a Y.

    Every dimension in this file is written as a forward distance because that
    is how the thing is actually measured — "the barbs start 27 cm up the
    shaft" — and the sign flip is the single most likely place to ship a
    backwards model. Doing it in one function means it is done once.
    """
    return -f


# ---------------------------------------------------------------------------
# Components. Each is a shape a different dart can reuse: two kinds of tip, two
# kinds of barb, a collar, a ferrule and an eyelet.
# ---------------------------------------------------------------------------

def _ring(r, n):
    """A closed n-gon profile of radius `r` in the plane `loft` will place it."""
    return [(r * math.cos(2 * math.pi * i / n), r * math.sin(2 * math.pi * i / n))
            for i in range(n)]


def tip_point(p, f_tip, f_base, base_r, seg=6, waist=0.55, mat=DARK):
    """A faceted penetrator point: one loft with an ogive break part-way up.

    A single cone is a party hat. The break between a long fine forward stage
    and a shorter, fatter shoulder stage is what makes it read as a machined
    penetrator, and it is visible in silhouette — which a surface detail at
    this scale would not be.

    Built as one `loft` rather than as two stacked `cyl` cones. The first pass
    did stack them, and where the two rings coincided exactly `finish()`'s
    remove_doubles welded them into six non-manifold edges with the second
    cone's cap sealed inside. One loft has no seam to weld.

    `loft(axis='Y')` maps a profile (u, v) onto (x, z) and the station offset
    onto y — worth writing down, because guessing `_plane_point`'s mapping is a
    documented way to put a part in the wrong place.

    The apex is a 0.6 mm ring rather than a true point: invisible at any
    distance this is seen from, and it keeps the mesh watertight and free of
    the degenerate fan a zero-radius cone produces.

    Flat-shaded on purpose. `loft` smooth-shades its barrel, which is right for
    a pipe and wrong here: the facets ARE the design.
    """
    f_mid = f_base + (f_tip - f_base) * waist
    faces = p.loft([(_y(f_tip), _ring(0.0006, seg)),
                    (_y(f_mid), _ring(base_r * 0.5, seg)),
                    (_y(f_base), _ring(base_r, seg))],
                   axis='Y', mat=mat, cap=True)
    p.shade(faces, False)
    return faces


def tip_chisel(p, f_tip, f_base, half_x, half_z, edge_x=0.0012, mat=DARK):
    """A broad chisel instead of a point — a rock piton, not a spear.

    Built as a prism so the cutting edge is a real straight line rather than a
    very blunt cone. `prism(axis='Z')` maps profile (u, v) onto (x, y) and
    extrudes along Z, so the profile is the side view and the extrusion is the
    width of the blade — write it down, because guessing `_plane_point`'s
    mapping is a documented way to misplace a part by centimetres.
    """
    f_shoulder = f_base + (f_tip - f_base) * 0.62
    profile = [
        (-edge_x, _y(f_tip)),
        (edge_x, _y(f_tip)),
        (half_x, _y(f_shoulder)),
        (half_x, _y(f_base)),
        (-half_x, _y(f_base)),
        (-half_x, _y(f_shoulder)),
    ]
    faces = p.prism(profile, half_z * 2.0, axis='Z', mat=mat)
    p.shade(faces, False)
    return faces


def barb_swept(p, angle, f_root, f_tip, r_root, r_tip, half_w, thick,
               curve=1.15, steps=8, mat=DARK):
    """One swept-back barb: a flat tapered blade lying in an axial plane.

    Built in the (axial, radial) plane and then rotated about Y, which is what
    lets two, three or four of them come off the same code. `r_root` is
    deliberately *inside* the shank radius so the blade emerges from the shaft
    instead of being a plate stuck to its side — a plate leaves an interior
    face where it meets the cylinder.

    The blade is a single planar n-gon extruded once, so it stays watertight
    and cheap. Curvature lives in `curve`: 1.0 is a straight spike, above 1.0
    hugs the shaft before flaring, which is the shape that reads as "this went
    in easily and will not come out".
    """
    path = []
    for i in range(steps + 1):
        t = i / steps
        path.append((f_root + (f_tip - f_root) * t,
                     r_root + (r_tip - r_root) * (t ** curve)))

    upper, lower = [], []
    for i, (f, r) in enumerate(path):
        t = i / steps
        h = half_w * (1.0 - t) ** 0.55 + 0.0010
        if i == steps:
            h = 0.0009                      # the barb ends in a point
        j0 = max(0, i - 1)
        j1 = min(steps, i + 1)
        dy = _y(path[j1][0]) - _y(path[j0][0])
        dz = path[j1][1] - path[j0][1]
        L = math.hypot(dy, dz) or 1.0
        ny, nz = -dz / L, dy / L            # normal in the (y, z) plane
        y = _y(f)
        upper.append((y + ny * h, r + nz * h))
        lower.append((y - ny * h, r - nz * h))

    # prism(axis='X') maps profile (u, v) onto (y, z) and extrudes along X.
    profile = upper + list(reversed(lower))
    faces = p.prism(profile, thick, axis='X', mat=mat)

    verts = list({v for f in faces for v in f.verts})
    bmesh.ops.rotate(p.bm, cent=(0, 0, 0), verts=verts,
                     matrix=Matrix.Rotation(angle, 3, 'Y'))
    p.shade(faces, False)
    return faces


def barb_ring(p, f_front, f_back, r_front, r_back, seg=14, spikes=6,
              spike_len=0.026, mat=DARK):
    """A single wide flared collar that opens backwards, with spikes on its rim.

    The piton's answer to four separate barbs: one continuous cone of metal
    rather than blades. Reads as heavier and more industrial in silhouette, and
    it is one shape instead of four, which is why the piton can afford the ribs
    on its body.
    """
    faces = p.cyl((0, _y((f_front + f_back) / 2), 0), r_front,
                  f_front - f_back, axis='Y', seg=seg, mat=mat,
                  radius_top=r_back, cap=True)
    for i in range(spikes):
        a = 2 * math.pi * (i + 0.5) / spikes
        cx, cz = math.cos(a), math.sin(a)
        # Each spike leans outward as well as back, so the rim is not a flat
        # crown of pins standing parallel to the shaft.
        lean = Matrix.Rotation(math.radians(24), 4, (cz, 0.0, -cx))
        mid = f_back - spike_len * 0.5
        c = (cx * r_back * 0.94, _y(mid), cz * r_back * 0.94)
        faces += p.cyl(c, 0.0055, spike_len, axis='Y', seg=5, mat=mat,
                       radius_top=0.0, rot=lean)
    return faces


def collar(p, f_front, f_back, radius, seg=16, mat=STEEL):
    """The body behind the barbs — the mass the rope pulls against."""
    return p.cyl((0, _y((f_front + f_back) / 2), 0), radius,
                 f_front - f_back, axis='Y', seg=seg, mat=mat)


def ferrule(p, f, radius, minor=0.0035, seg=16, mat=BRASS):
    """A raised ring round the body. A torus, not a box, so `bevel` skips it."""
    return p.torus((0, _y(f), 0), radius, minor, axis='Y', maj_seg=seg,
                   min_seg=6, mat=mat)


def band(p, f_front, f_back, radius, mat, seg=16):
    """A short sleeve of a contrasting material — paint stripe or rubber damper."""
    return p.cyl((0, _y((f_front + f_back) / 2), 0), radius,
                 f_front - f_back, axis='Y', seg=seg, mat=mat)


def shank(p, f_front, f_back, radius, seg=12, mat=STEEL):
    return p.cyl((0, _y((f_front + f_back) / 2), 0), radius,
                 f_front - f_back, axis='Y', seg=seg, mat=mat)


def eyelet(p, major, minor, lug_f0, lug_f1, lug_x, lug_z, cheeks=False,
           mat_ring=STEEL, mat_lug=STEEL):
    """The rope eye, centred on the object's origin.

    `torus(axis='X')` puts the ring in the Y-Z plane with its hole running
    along X, so the rope threads through sideways and then trails back along
    +Y. The hole's centre is `(0, 0, 0)` — which is the whole reason the origin
    is here rather than at the tip.

    Returns the boxy faces it made, for the caller's bevel list. The ring
    itself is never bevelled: it is round already and a chamfer at 8 mm minor
    radius would eat it.
    """
    p.torus((0, 0, 0), major, minor, axis='X', maj_seg=20, min_seg=8,
            mat=mat_ring)
    hard = p.slab((-lug_x / 2, _y(lug_f0), -lug_z / 2),
                  (lug_x / 2, _y(lug_f1), lug_z / 2), mat_lug)
    if cheeks:
        # Two washers either side of the eye. Cheap, and they are what makes
        # the heavy variant's eye look forged rather than bent from rod.
        for sx in (-1, 1):
            p.cyl((sx * (minor + 0.0035), 0, 0), major * 0.72, 0.004,
                  axis='X', seg=12, mat=mat_ring)
    return hard


def markers(coll, mats, suffix, f_tip):
    """Two 4 mm cubes carrying coordinates across the FBX.

    Blender empties are not exported (`object_types={'MESH'}`), so a marker has
    to be geometry. `Marker_RopeAnchor_*` sits on the origin and is redundant
    by construction — it is here so the anchor is visible and named in the
    Unity hierarchy rather than being a fact you have to read a document to
    learn. `Marker_Tip_*` is not redundant: it is the offset needed to bury the
    tip in a surface.

    Both renderers must be disabled on the Unity side (see the BUILD record);
    a 4 mm cube is small but it is not invisible.
    """
    for name, loc in (("Marker_RopeAnchor_" + suffix, (0.0, 0.0, 0.0)),
                      ("Marker_Tip_" + suffix, (0.0, _y(f_tip), 0.0))):
        m = Part(mats)
        m.box(loc, (0.004, 0.004, 0.004), STEEL)
        m.finish(name, coll, origin=loc)


# ---------------------------------------------------------------------------
# Variations. Silhouette first: a needle, a four-barbed harpoon and a chisel
# piton are three different outlines, not one dart at three lengths.
# ---------------------------------------------------------------------------

def build_light(p):
    """Light — slim, two short barbs, almost no collar. 0.340 m overall.

    The cheap expendable round. Everything about it is thinner than the hero:
    a needle point rather than a faceted spike, two barbs instead of four
    (spread 0.084 against the hero's 0.140), a single thin ferrule where the
    hero has a machined collar.
    """
    hard = []
    tip_point(p, 0.319, 0.248, 0.0125, seg=8, waist=0.62)
    shank(p, 0.252, 0.098, 0.0100, seg=10)
    for i in range(2):
        barb_swept(p, math.pi * i, f_root=0.248, f_tip=0.146,
                   r_root=0.0050, r_tip=0.0420, half_w=0.0058, thick=0.0050,
                   curve=1.30)
    ferrule(p, 0.238, 0.0125, minor=0.0026, seg=12)
    # The whole "collar" is one short sleeve and two rings. Anything more and
    # it stops being the light variant.
    collar(p, 0.112, 0.074, 0.0165, seg=14)
    band(p, 0.104, 0.092, 0.0175, ORANGE, seg=14)
    ferrule(p, 0.074, 0.0165, minor=0.0028, seg=14)
    shank(p, 0.078, 0.020, 0.0105, seg=10)
    hard += eyelet(p, major=0.0155, minor=0.0050,
                   lug_f0=0.050, lug_f1=0.014, lug_x=0.0090, lug_z=0.0210)
    return hard, 0.319


def build_barbed(p):
    """Barbed — the hero. Four pronounced swept barbs. 0.400 m overall.

    Proportions are set by what survives at 50 m: barb spread 0.140 against
    0.400 of length, so the barbs are 35% of the body length across and the
    outline is unmistakably a harpoon rather than a bolt. Everything finer than
    about 4 mm was deliberately left off.
    """
    hard = []
    tip_point(p, 0.370, 0.296, 0.0200, seg=6, waist=0.56)
    ferrule(p, 0.292, 0.0205, minor=0.0032, seg=12)
    shank(p, 0.296, 0.152, 0.0140, seg=12)
    for i in range(4):
        barb_swept(p, math.pi / 2 * i, f_root=0.288, f_tip=0.142,
                   r_root=0.0060, r_tip=0.0690, half_w=0.0085, thick=0.0070,
                   curve=1.45)
    # Barb root boss: the barbs have to look bolted through something.
    collar(p, 0.286, 0.262, 0.0195, seg=14)

    collar(p, 0.152, 0.072, 0.0260, seg=16)
    ferrule(p, 0.146, 0.0265, minor=0.0038, seg=16)
    band(p, 0.132, 0.116, 0.0272, ORANGE, seg=16)
    band(p, 0.100, 0.086, 0.0272, RUBBER, seg=16)
    ferrule(p, 0.076, 0.0265, minor=0.0038, seg=16)
    shank(p, 0.076, 0.026, 0.0150, seg=12)
    hard += eyelet(p, major=0.0220, minor=0.0080,
                   lug_f0=0.068, lug_f1=0.020, lug_x=0.0140, lug_z=0.0300,
                   cheeks=True)
    return hard, 0.370


def build_piton(p):
    """Piton — industrial. Chisel tip, one flared barb ring, ribbed body.

    0.360 m overall, spread 0.114. The only member of the family whose front
    end is a straight edge rather than a point, which is the difference that
    reads first; the rib stack is the second. Mining and salvage gear: it is
    meant to look like it was made to be hammered into rock and left there.
    """
    hard = []
    hard += tip_chisel(p, 0.332, 0.262, half_x=0.0105, half_z=0.0215)
    shank(p, 0.266, 0.150, 0.0135, seg=12)
    barb_ring(p, f_front=0.246, f_back=0.196, r_front=0.0150, r_back=0.0500,
              seg=16, spikes=6, spike_len=0.028)
    ferrule(p, 0.250, 0.0155, minor=0.0030, seg=12)

    collar(p, 0.156, 0.062, 0.0225, seg=16)
    ferrule(p, 0.153, 0.0230, minor=0.0036, seg=16)
    # Four ribs rather than one collar band: a ribbed body is the industrial
    # read, and tori cost nothing and are skipped by the bevel pass. Only one
    # of them is brass — the first pass alternated and the body came out a gold
    # barrel, which read as steampunk rather than as mining gear.
    for i in range(4):
        ferrule(p, 0.144 - i * 0.0230, 0.0232, minor=0.0042, seg=16,
                mat=BRASS if i == 1 else STEEL)
    shank(p, 0.066, 0.024, 0.0140, seg=12)
    # The high-vis band goes on the bare rear shank, not on the collar: between
    # four ribs it was invisible, which is the one thing a high-vis band cannot
    # be.
    band(p, 0.058, 0.044, 0.0150, ORANGE, seg=12)
    hard += eyelet(p, major=0.0195, minor=0.0068,
                   lug_f0=0.060, lug_f1=0.018, lug_x=0.0170, lug_z=0.0290,
                   mat_lug=STEEL)
    return hard, 0.332


VARIATIONS = [
    ("Coll_GrappleDart_Light", "Light", build_light),
    ("Coll_GrappleDart_Barbed", "Barbed", build_barbed),
    ("Coll_GrappleDart_Piton", "Piton", build_piton),
]


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for coll_name, suffix, builder in VARIATIONS:
        coll = collection(coll_name)
        p = TrackedPart(mats)
        hard, f_tip = builder(p)
        # Expected to print 0. Anything else means Blender has changed how
        # `bm.from_mesh` orders faces again — see TrackedPart.
        print("  %s: %d face(s) needed re-stamping" % (coll_name, p.restamp()))
        # Only the boxy faces — never a whole-part bevel. At this scale a
        # global pass welds the barb blades and the shank into a blob.
        if hard:
            p.bevel(hard, width=BEVEL_W, segments=2)
        p.finish("Mesh_GrappleDart_" + suffix, coll)
        markers(coll, mats, suffix, f_tip)

    save(out)
    report()


main()
