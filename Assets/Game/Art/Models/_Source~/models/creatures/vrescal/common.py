"""Shared scaffolding for the Vrescal's per-part build scripts.

The animal is built one body part at a time -- `body.py`, then a neck, legs,
tail and rig alongside it -- so that each script stays small enough to reason
about on its own. Everything those scripts agree on lives here: the working
units, the ground plane, and the cross-section machinery.

## Working space

    +X   forward (the nose end)
    +Y   port
    +Z   up
    ground plane            z = -13.0
    1 metre                 3.6245 units

The odd scale is inherited and load-bearing: the author's head sculpt was
modelled at this size, and the export factor (0.2759) is chosen so the skull
ships at exactly the size it has always been. Nothing rescales the .blend.

## Why sections are not ellipses

The obvious way to loft a body is a stack of ellipses, and it is why every
earlier attempt at this animal read as a tube. An ellipse has one degree of
freedom per axis: you can make it taller or wider, and that is all. Real
cross-sections do not work like that -- a withers section is narrow at the crest
and broad low down, a chest section is deep and keeled, a haunch is a rounded
egg with its widest point high. Those are different *shapes*, not different
sizes of one shape.

`Section` therefore describes a cross-section as a half-width **profile down the
body**, given as a handful of control points that get interpolated: how wide it
is near the crest, where its widest point sits, how far it tucks in at the
belly. That is enough to state all three of the shapes above, and it is what
makes the loft stop reading as pipe.
"""

import math
import os
import sys

import bpy
from mathutils import Vector

UNITS_PER_M = 3.62450
GROUND = -13.0

HERE = os.path.dirname(os.path.abspath(__file__))

# Every part script writes here by default, so the work in progress is a file in
# the project that can simply be opened rather than something in /tmp. It is
# **generated output and is overwritten on every run** -- that is the opposite
# of the rule for `vrescal.blend`, which is a source of truth and may carry hand
# edits. Do not hand-edit this file; edit the part script and re-run it.
WIP = os.path.join(HERE, "vrescal_wip.blend")


def parse_out(default=WIP):
    """`--out <path>` from the args after `--`, defaulting to the WIP file."""
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--out" in argv:
        return argv[argv.index("--out") + 1]
    return default


def start(out_path):
    """Begin a build into a generated preview file.

    Deliberately *does* clobber, unlike `_buildlib.start`. That guard exists to
    protect hand-edited masters; this file is regenerated every time a part
    script runs and protecting it would just mean deleting it by hand between
    every iteration.
    """
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0
    return scene


def save(out_path):
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(out_path))
    print("\nOPEN THIS FILE:  %s" % os.path.abspath(out_path))

# Points around a lofted ring. Even, because the profile is built down one side
# and mirrored -- an odd count would leave the crest and the keel off-centre.
RING = 20

# The crest and keel keep a sliver of width rather than collapsing to a point.
# A zero-width ring end is a pole: `remove_doubles` merges it, subdivision
# pinches around it, and the shading goes to pieces along the spine.
END_W = 0.06


def m(metres):
    """Metres to working units."""
    return metres * UNITS_PER_M


def height(z):
    """Height above the sole plane, in metres -- for readable print-outs."""
    return (z - GROUND) / UNITS_PER_M


def _catmull(ts, vs, t):
    """Interpolate a 1-D control curve. Linear between points, but with the
    slope eased at each end so a profile does not develop a visible crease
    where two control spans meet."""
    if t <= ts[0]:
        return vs[0]
    if t >= ts[-1]:
        return vs[-1]
    for i in range(len(ts) - 1):
        if ts[i] <= t <= ts[i + 1]:
            span = ts[i + 1] - ts[i]
            u = (t - ts[i]) / span if span > 1e-9 else 0.0
            u = u * u * (3.0 - 2.0 * u)          # smoothstep
            return vs[i] + (vs[i + 1] - vs[i]) * u
    return vs[-1]


class Section:
    """One cross-section of the body, as a shaped closed profile.

    `top` and `bot` are absolute z. The width controls are half-widths, and
    `t_mid` says how far down the section the widest point sits -- 0 at the
    crest, 1 at the keel. A withers wants `t_mid` around 0.62 with a narrow
    `w_top`; a haunch wants it around 0.42 and much rounder.
    """

    def __init__(self, x, top, bot, w_mid, t_mid=0.50, w_top=None, w_bot=None,
                 lean=0.0):
        self.x = x
        self.top = top
        self.bot = bot
        self.w_mid = w_mid
        self.t_mid = t_mid
        self.w_top = w_mid * 0.62 if w_top is None else w_top
        self.w_bot = w_mid * 0.70 if w_bot is None else w_bot
        # `lean` slides the upper half fore or aft, which is how a shoulder mass
        # overhangs the leg beneath it instead of sitting square on top of it.
        self.lean = lean

    def half_width(self, t):
        ts = [0.0, 0.16, self.t_mid, 0.84, 1.0]
        vs = [self.w_mid * END_W, self.w_top, self.w_mid, self.w_bot,
              self.w_mid * END_W]
        # Guard the ordering: a caller asking for t_mid below 0.84 or above 0.16
        # would otherwise hand `_catmull` a non-monotonic knot vector.
        if not (0.16 < self.t_mid < 0.84):
            ts = [0.0, 0.16, 0.50, 0.84, 1.0]
        return _catmull(ts, vs, t)

    def ring(self, n=RING):
        """Closed ring of world points, starting at the crest and running port.

        Built down one side and mirrored, so the crest and keel land exactly on
        y = 0 and the animal is symmetric to the last decimal.
        """
        half = n // 2
        pts = []
        for k in range(half + 1):
            t = k / float(half)
            z = self.top + (self.bot - self.top) * t
            y = self.half_width(t)
            x = self.x + self.lean * (1.0 - t) ** 2
            pts.append(Vector((x, y, z)))
        # Mirror back up the starboard side, skipping the shared crest and keel.
        for k in range(half - 1, 0, -1):
            p = pts[k]
            pts.append(Vector((p.x, -p.y, p.z)))
        return pts


def loft(sections, n=RING):
    """Rings for a list of Sections, as parallel lists of world points."""
    return [s.ring(n) for s in sections]


def report(name, sections):
    """One line per station, in metres, for eyeballing a table without rendering."""
    print("%s: %d stations" % (name, len(sections)))
    for s in sections:
        print("   x %7.2f  top %5.2f m  bot %5.2f m  depth %5.2f m  width %5.2f m"
              % (s.x, height(s.top), height(s.bot),
                 (s.top - s.bot) / UNITS_PER_M, s.w_mid * 2.0 / UNITS_PER_M))
