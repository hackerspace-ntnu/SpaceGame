"""`Coll_GrappleHarpoon` — a fourth, much larger head appended to grapple_dart.blend.

## Why this exists

`Coll_GrappleDart_Barbed` (0.400 m, 0.140 m spread) shipped as the hero and is
wired into the grappling hook. In play it is **too small to see** and it reads
as a dart — mostly head, with a stub of body behind it. This is the harpoon
answer to that: **0.900 m to the tip**, with the proportion inverted. A long
obvious shaft carries the mass, and the business end sits on the front of it.

The three separators from a dart, in the order they read at distance:

1. **The shaft.** 0.60 m of 34 mm steel is the silhouette. The barbed dart's
   body is 0.15 m of 28 mm; you cannot see it past the barbs.
2. **A lance blade, not a spike.** A 65 mm wide, 174 mm long lens-section leaf
   blade — a shape with a width, rather than a cone with a point.
3. **Three barbs, not four, and each one 2.5x the size.** 245 mm long, 46 mm
   at the root, reaching 172 mm off the axis. "Big, few and bold" — the whole
   complaint was legibility, and four smaller barbs is the shape that failed.

Three rather than two because a two-barbed harpoon viewed down its barb plane
has no barbs at all; three guarantees at least one near-side-on from any angle.
Three rather than four because at this blade size four start to occlude one
another and the count stops being readable.

**Nothing on this model is finer than about 8 mm** and nothing glows. Same art
direction as the darts: plain, physical, expendable salvage ironmongery.

## This script is ADDITIVE and it writes to an existing .blend

`grapple_dart.blend` is the source of truth and carries three shipped
variations. This script therefore does **not** call `start()`: it opens the
existing file, refuses if anything it is about to create already exists, adds
exactly one collection plus its three objects, and saves back in place. It
touches nothing that was already there.

    blender --background --python components/props/grapple_harpoon.py

Like every generator in this library it is historical record. Do not re-run it
against a file that already contains `Coll_GrappleHarpoon` — it will refuse,
which is the point.

## Axes and origin — unchanged, and load-bearing

Identical to the darts, because the Unity side is already written against them:

* **Tip down Blender −Y**, which `_exportlib`'s `axis_forward='-Z'`,
  `axis_up='Y'` maps onto **Unity +Z**, which is what
  `Quaternion.LookRotation(travelDirection)` produces.
* **Origin at the rope eye** — the centre of the eyelet hole, at `(0, 0, 0)`,
  so the `LineRenderer`'s last vertex is just `head.transform.position` and
  the seating maths is `anchor + normal * (tipOffset - embed)`.

`_y(f)` converts a forward distance into a Y exactly as `grapple_dart.py` does.
Every dimension below is written as a forward distance from the rope eye.

## The high-vis bands are deliberately at the BACK

Two orange bands sit on the rear third of the shaft (f 0.370-0.295 and
0.276-0.244), not near the head. A harpoon that has done its job is buried
tip-first in a wall, so paint near the tip is paint inside the wall. Everything
from f = 0.37 back stays outside for any plausible embed depth.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Matrix  # noqa: E402

BLEND = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                     "grapple_dart.blend")

COLL = "Coll_GrappleHarpoon"
MESH = "Mesh_GrappleHarpoon"
SUFFIX = "Harpoon"

# Same five palette entries as the darts, in the same order, and nothing added.
# STEEL stays index 0 because `bmesh.ops.bevel` stamps every face it creates
# with material index 0 — see project_buildlib_traps and grapple_dart_BUILD.md.
STEEL, DARK, BRASS, RUBBER, ORANGE = range(5)
MATS = ["Mat_Metal_Steel_Worn",       # shaft, collars, ferrule body, eyelet
        "Mat_Metal_Steel_Dark",       # lance blade and barbs
        "Mat_Metal_Brass_Tarnished",  # ferrule rings
        "Mat_Plastic_Rubber_Black",   # grip and damper bands
        "Mat_Paint_Safety_Orange"]    # the two rear high-vis bands

# 2.0 mm rather than the darts' 1.2 mm: the only bevelled piece here is the
# 20 mm eyelet lug, which is nearly half again the barbed dart's, and a 1.2 mm
# chamfer on it is not visible at the distance this thing is looked at. Still
# far under the library's 12 mm default, which would weld the lug shut.
BEVEL_W = 0.0020

F_TIP = 0.900          # forward distance to the point of the blade


class TrackedPart(Part):
    """`Part` with `_absorb` fixed to identify new faces by identity.

    Copied verbatim from `grapple_dart.py`, for the bug documented at length
    there and in `grapple_dart_BUILD.md`: `Part._absorb` slices
    `self.bm.faces[n_before:]` after `bm.from_mesh`, and `from_mesh` does not
    leave the existing faces in their old index slots. The slice is always the
    right *length*, so the model builds and the counts look plausible; the only
    symptom is a handful of faces wearing a neighbouring part's material.

    Not fixed in `_buildlib` itself — every component in the library imports
    it, and some have been tuned by eye against the wrong colours. Correcting
    it centrally is a separate job with its own render review.

    `restamp()` replays every (faces, index) pair before the bevel pass and
    prints how many faces it corrected, so a regression shows up as a number
    instead of as a mysteriously gold nose.
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
    """Forward distance `f` (metres from the rope eye toward the tip) as a Y.

    The sign flip lives in exactly one function, because a stray sign is the
    single most likely way to ship a harpoon that flies backwards.
    """
    return -f


# ---------------------------------------------------------------------------
# Head
# ---------------------------------------------------------------------------

def lance_blade(p, stations, phase=math.pi / 3.0, mat=DARK):
    """A three-lobed lance blade, built as one loft.

    `stations` is [(forward_distance, lobe_radius, valley_radius), ...]. Each
    becomes a six-point star: three fins at 120 degrees out to `lobe_radius`,
    three valleys between them at `valley_radius`.

    ## Why trilobed and not a flat leaf

    The first pass built the obvious thing — a flat leaf with a lens
    cross-section, wide in x and thin in z. It renders as a **needle** from
    every direction except one. A flat blade is only broad when you happen to
    be looking at its face, and a flying harpoon is never obliging about that;
    the whole complaint being answered here is legibility, so a shape with one
    good angle is the wrong shape.

    Three fins put a broad edge in front of the eye from any bearing, and they
    are the same 120 degree rhythm as the barbs, so the head reads as one
    designed object rather than as a blade with hooks bolted to it.

    `phase` offsets the fins 60 degrees from the barbs so the lobes sit in the
    gaps. Aligned, blade and barb vanish together edge-on and the silhouette
    thins from exactly three bearings; interleaved, something is always broad.

    `loft(axis='Y')` maps a profile (u, v) onto (x, z) and the station offset
    onto y. Written down rather than guessed: `_plane_point`'s mapping is a
    documented way to put a part in the wrong place.

    One loft, not a stack of cones — coincident rings between stacked solids
    are what `finish()`'s `remove_doubles` welds into non-manifold edges. The
    apex is a 2.2 mm star rather than a true point, which is invisible at any
    distance this is seen from and avoids a degenerate fan.

    Flat-shaded: on a blade the facets ARE the design.
    """
    sections = []
    for f, lobe, valley in stations:
        prof = []
        for i in range(6):
            a = phase + math.pi / 3.0 * i
            r = lobe if i % 2 == 0 else valley
            prof.append((r * math.cos(a), r * math.sin(a)))
        sections.append((_y(f), prof))
    faces = p.loft(sections, axis='Y', mat=mat, cap=True)
    p.shade(faces, False)
    return faces


def barb_swept(p, angle, f_root, f_tip, r_root, r_tip, half_w, thick,
               curve=1.15, steps=8, mat=DARK):
    """One rear-swept barb: a flat tapered blade lying in an axial plane.

    Copied from `grapple_dart.py` — same shape, four times the size. Built in
    the (axial, radial) plane and rotated about Y, which is what lets two,
    three or four barbs come off one function.

    `r_root` sits *inside* the shaft radius on purpose, so the blade emerges
    from the shaft rather than being a plate stuck to its side; a plate leaves
    an interior face exactly where the two surfaces meet.

    `curve` above 1.0 hugs the shaft before flaring — the shape that reads as
    "this went in easily and will not come out".
    """
    path = []
    for i in range(steps + 1):
        t = i / steps
        path.append((f_root + (f_tip - f_root) * t,
                     r_root + (r_tip - r_root) * (t ** curve)))

    upper, lower = [], []
    for i, (f, r) in enumerate(path):
        t = i / steps
        h = half_w * (1.0 - t) ** 0.55 + 0.0025
        if i == steps:
            h = 0.0020                      # the barb ends in a point
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


# ---------------------------------------------------------------------------
# Body vocabulary — same shapes as the darts, scaled up
# ---------------------------------------------------------------------------

def sleeve(p, f_front, f_back, radius, mat=STEEL, seg=16):
    """A plain cylindrical section of body: shaft, collar, ferrule or band.

    The darts split this into `shank` / `collar` / `band` purely so the calls
    read as what they are. At harpoon scale there are enough of them that one
    named function plus a comment per call is clearer than four aliases.
    """
    return p.cyl((0, _y((f_front + f_back) / 2), 0), radius,
                 f_front - f_back, axis='Y', seg=seg, mat=mat)


def ring(p, f, radius, minor=0.0050, seg=16, mat=BRASS):
    """A raised ferrule ring. A torus, not a box, so `bevel` skips it.

    16 x 6 rather than the tempting 24 x 10. There are seven of these and a
    torus is the most expensive shape per unit of silhouette in the library: at
    24 x 10 they cost 2 240 triangles, more than the rest of the harpoon put
    together, for a 5 mm bead that is one pixel wide at any distance this is
    seen from. Smooth-shaded, so the facets do not show.
    """
    return p.torus((0, _y(f), 0), radius, minor, axis='Y', maj_seg=seg,
                   min_seg=6, mat=mat)


def eyelet(p, major, minor, lug_f0, lug_f1, lug_x, lug_z, cheek_t,
           mat=STEEL):
    """The rope eye, centred on the object's origin.

    `torus(axis='X')` puts the ring in the Y-Z plane with its hole running
    along X, so the rope threads through sideways and trails back along +Y.
    The hole's centre is `(0, 0, 0)` — which is the entire reason the origin is
    here and not at the tip.

    Two cheek washers make the eye read as forged rather than bent from rod.
    Their thickness is a parameter here (the darts hard-code 4 mm) because at
    harpoon scale 4 mm is below the 8 mm floor this model is built to.

    Returns the boxy lug faces for the caller's bevel list. The ring itself is
    never bevelled: it is round already, and a chamfer at 11 mm minor radius
    would eat it.
    """
    p.torus((0, 0, 0), major, minor, axis='X', maj_seg=20, min_seg=8,
            mat=mat)
    hard = p.slab((-lug_x / 2, _y(lug_f0), -lug_z / 2),
                  (lug_x / 2, _y(lug_f1), lug_z / 2), mat)
    for sx in (-1, 1):
        p.cyl((sx * (minor + cheek_t / 2 + 0.0008), 0, 0), major * 0.74,
              cheek_t, axis='X', seg=16, mat=mat)
    return hard


def markers(coll, mats, suffix, f_tip):
    """Two 4 mm cubes carrying coordinates across the FBX.

    Geometry rather than empties because `object_types={'MESH'}` does not
    export empties. `Marker_RopeAnchor_*` is redundant by construction — it
    sits on the origin — and exists so the anchor is named and visible in the
    Unity hierarchy instead of being a fact you have to read a document to
    learn. `Marker_Tip_*` carries the embed offset and is not redundant.

    Both renderers are disabled on the Unity side; a 4 mm cube is small but it
    is not invisible, and `Marker_Tip` sits half outside the blade.
    """
    for name, loc in (("Marker_RopeAnchor_" + suffix, (0.0, 0.0, 0.0)),
                      ("Marker_Tip_" + suffix, (0.0, _y(f_tip), 0.0))):
        m = Part(mats)
        m.box(loc, (0.004, 0.004, 0.004), STEEL)
        m.finish(name, coll, origin=loc)


# ---------------------------------------------------------------------------
# The variation
# ---------------------------------------------------------------------------

def build_harpoon(p):
    """0.900 m to the tip, 0.31 m across the barbs, 35 mm shaft.

    Read the numbers as a layout from the tip backwards:

        0.900 - 0.694   trilobe lance blade, 87 mm across the fins
        0.730 - 0.688   barb boss: the barbs bolt through something
        0.722 - 0.560   three barbs, out to 172 mm off the axis
        0.712 - 0.130   the shaft, 35 mm diameter, 582 mm of it
        0.500 - 0.400   foregrip collar with a rubber grip band
        0.360 - 0.240   two safety-orange bands (rear third, see the module doc)
        0.225 - 0.105   rope ferrule with a rubber damper band
        0.125 - 0.032   rear shank
        origin          the eye

    Two things here were set by looking at a render rather than by arithmetic:

    * **The collars are 56-58 mm against a 35 mm shaft.** At the 48 mm the
      first pass used, a black band over the top swallowed the step entirely
      and the shaft read as one unbroken 0.58 m pipe with stripes painted on
      it. A collar has to be visibly fatter than what it collars.
    * **Five ferrule rings, not seven.** Seven turned the rear half into a
      barcode. The rings are punctuation; past about five they stop separating
      anything.
    """
    hard = []

    # --- head -------------------------------------------------------------
    # Fins at 60/180/300 degrees, barbs at 0/120/240 — see `lance_blade`.
    lance_blade(p, [
        (0.900, 0.0022, 0.0012),   # tip: a 2.2 mm star, not a degenerate point
        (0.878, 0.0170, 0.0060),
        (0.852, 0.0320, 0.0100),
        (0.822, 0.0450, 0.0130),
        (0.795, 0.0500, 0.0145),   # widest — 87 mm across the fins
        (0.768, 0.0430, 0.0150),
        (0.742, 0.0300, 0.0150),
        (0.716, 0.0200, 0.0140),   # neck
        (0.694, 0.0180, 0.0130),   # into the socket, inside the shaft
    ])
    sleeve(p, 0.730, 0.688, 0.0290)        # barb boss
    ring(p, 0.692, 0.0296, minor=0.0055)

    # Three, at 120 degrees. Each is 245 mm of blade, 60 mm wide at the root
    # and a 16 mm plate — heavy enough to read as a barb rather than a scythe.
    for i in range(3):
        barb_swept(p, 2 * math.pi * i / 3, f_root=0.722, f_tip=0.560,
                   r_root=0.0090, r_tip=0.1720, half_w=0.0290, thick=0.0160,
                   curve=1.25)

    # --- shaft ------------------------------------------------------------
    # The single most important piece on the model: this is what makes it read
    # as a harpoon rather than as a large dart.
    sleeve(p, 0.712, 0.130, 0.0175)        # 35 mm diameter

    # Foregrip collar, a third of the way along. Two jobs: it is whaling
    # hardware, and it breaks 0.58 m of unbroken tube into two lengths so the
    # shaft reads as a made object rather than as a pipe.
    sleeve(p, 0.500, 0.400, 0.0280)
    ring(p, 0.502, 0.0288)
    sleeve(p, 0.478, 0.424, 0.0292, mat=RUBBER)   # steel shoulders either side
    ring(p, 0.398, 0.0288)

    # --- high-vis, deliberately at the back -------------------------------
    sleeve(p, 0.360, 0.285, 0.0190, mat=ORANGE)   # 75 mm band
    sleeve(p, 0.268, 0.240, 0.0190, mat=ORANGE)   # 28 mm band

    # --- rope ferrule and eye ---------------------------------------------
    sleeve(p, 0.225, 0.105, 0.0290)
    ring(p, 0.220, 0.0298, minor=0.0052)
    sleeve(p, 0.200, 0.168, 0.0300, mat=RUBBER)   # damper under the lashing
    ring(p, 0.112, 0.0298, minor=0.0052)
    sleeve(p, 0.125, 0.032, 0.0200, seg=14)

    hard += eyelet(p, major=0.0320, minor=0.0115,
                   lug_f0=0.105, lug_f1=0.026, lug_x=0.0210, lug_z=0.0470,
                   cheek_t=0.0090)
    return hard


def main():
    if not os.path.exists(BLEND):
        raise SystemExit("No component at %s" % BLEND)
    bpy.ops.wm.open_mainfile(filepath=BLEND)

    # Additive and defensive. The .blend is shared and may have been edited by
    # hand or by a concurrent session since this script was written, so refuse
    # rather than clobber, and never touch anything already in the file.
    wanted = [COLL, MESH,
              "Marker_RopeAnchor_" + SUFFIX, "Marker_Tip_" + SUFFIX]
    clash = [n for n in wanted
             if n in bpy.data.collections or n in bpy.data.objects]
    if clash:
        raise SystemExit(
            "Already present in %s: %s\nThe .blend is the source of truth; "
            "edit it in place rather than re-running this." %
            (os.path.basename(BLEND), ", ".join(clash)))
    print("Preserving %d existing object(s): %s"
          % (len(bpy.data.objects),
             ", ".join(sorted(o.name for o in bpy.data.objects))))

    mats = link_materials(MATS)
    coll = collection(COLL)

    p = TrackedPart(mats)
    hard = build_harpoon(p)
    # Expected to print 0. Anything else means Blender has changed how
    # `bm.from_mesh` orders faces again — see TrackedPart.
    print("  %s: %d face(s) needed re-stamping" % (COLL, p.restamp()))
    # Only the eyelet lug. A whole-part bevel at this scale welds the barb
    # blades into the shaft, which is the trap `grapple_dart.py` documents.
    p.bevel(hard, width=BEVEL_W, segments=2)
    p.finish(MESH, coll)
    markers(coll, mats, SUFFIX, F_TIP)

    for o in bpy.data.objects:
        if len(o.name) > 4 and o.name[-4] == '.' and o.name[-3:].isdigit():
            raise SystemExit("Auto-suffixed object name reached save: %s"
                             % o.name)
        if o.data is not None and hasattr(o.data, "name"):
            o.data.name = o.name
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
    print("Wrote %s" % BLEND)


main()
