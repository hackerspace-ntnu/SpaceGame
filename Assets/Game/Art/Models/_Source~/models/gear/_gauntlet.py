"""The gauntlet family — one seating shared by every forearm-worn device.

**A gauntlet is the device alone.** Since 2026-09-04 the bracer is not part of
it: the player wears `components/props/gauntlet_base.blend`'s Mount variation
on each forearm permanently (shipped by `gauntlet_base_export.py`, seated by
Unity's `ForearmBracers`), and a gauntlet model is only what stands on that
bracer's hardpoint deck. So a device file contains no `Mesh_GauntletBase_*`
object at all, and `strip_bracer.py` is what took them out of the seven built
before that date.

Devices are still authored against the deck, so the `BASE_*` constants below
are the contract that keeps a device landing on it — see `gauntlet_base_BUILD.md`.
Unity seats every gauntlet with `GauntletFit` at scale 1 on the forearm bone,
the same frame and scale the bracer itself is seated in, which is what makes
"authored on the deck" and "lands on the deck" the same statement.

The cuff generation (`append_cuff`, `spine`, `clamp_band*`) is what
`grapple_bracer.py`, `item_scanner.py` and the first `leash_gauntlet.py`
were built on — `arm_cuff`'s webbing, a riveted spine and clamp bands. Those
scripts are historical records and the helpers stay so they still read; no
new gauntlet should use them.

This module is imported by generators; it never opens or writes a .blend itself.


## The frame

**Arm along Y, wrist at y = 0, elbow toward +Y, forward −Y, dorsal +Z.**

Forward is −Y because `_exportlib`'s FBX flags map Blender −Y onto Unity +Z,
the axis `ItemGrip` points an aimed item down. Dorsal +Z lands on Unity +Y.

The export also flips X (Blender `(x, y, z)` → Unity `(−x, z, −y)`, measured in
`grapple_bracer_BUILD.md`). So Blender **+X is the thumb side** of a right
forearm and **−X the little-finger side**. Verified against the rig's knuckle
flexion on 2026-09-02, which is also when `BodyEquipmentController`'s dorsal
was found inverted and fixed — before that fix every gauntlet's top sat on the
palm side and the doc above was wrong in practice.


## The deck a device is authored against

Nothing appends the base any more, but every device is still SHAPED by it. Its
hardpoint is a flat deck on the back of the arm at `BASE_DECK_Z`; a device
stands on that plane, sunk 2-3 mm, between `BASE_DECK_Y0` and `BASE_DECK_Y1`.
Nothing on the bracer crosses y < `BASE_WRIST_EDGE`, so the glove is the
device's to reach into.

These are the only numbers a device may assume about the arm, and they are
copied from `gauntlet_base.py` rather than measured — if they move, they move
there first, and every device is re-derived from them.

The one thing still appended out of the base file is `append_rails()`, and
those are the puncher's own hardware rather than a mount.


## The cuff (legacy)

`Mesh_ArmCuff_Webbing`, appended unchanged, through `cuff_matrix()`. It arrives
with its wrist end at y ≈ 0.012, its elbow end at y = 0.205, its mounting boss
on top at (0, 0.086..0.130, 0.040..0.056) and its buckles out on the −X flank.
`SPINE_Z0` is the boss's outer face: a spine floor laid at that height sits on
the boss, which is what the boss is for.

The wearer's suit is fatter than the sleeve the cuff was authored for, so on the
rig the cuff mostly disappears into the sleeve — the reason the base replaced
it. The clamp bands stand off it by `BAND_STANDOFF` for exactly that reason —
see `clamp_band`.
"""

import math
import os

import bpy
from mathutils import Matrix, Vector

_HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(_HERE))
PROPS = os.path.join(LIB, "components", "props")
CUFF = os.path.join(PROPS, "arm_cuff.blend")
CUFF_OBJECT = "Mesh_ArmCuff_Webbing"

BASE = os.path.join(PROPS, "gauntlet_base.blend")
BASE_VARIANTS = ("Plain", "Mount", "Rail")
BASE_PARTS = ("Undersleeve", "DorsalShell", "VentralShell", "Collar",
              "HingeFront", "HingeRear", "LatchFront", "LatchRear")
BASE_MOUNT_PARTS = ("Deck", "Bosses")
BASE_RAIL_PARTS = ("RailLeft", "RailRight")

# The base's hardpoint contract, copied from `gauntlet_base.py` so a device
# script can be read on its own. If these move, they move there first.
BASE_DECK_Z = 0.250                  # flat deck top, the plane devices stand on
BASE_DECK_HX = 0.070                 # deck half-width
BASE_DECK_Y0, BASE_DECK_Y1 = 0.100, 0.320
BASE_BOSS_INSET = 0.014              # bolt bosses at the deck corners, 4 mm proud
BASE_RAIL_X, BASE_RAIL_Z = 0.048, 0.272
BASE_RAIL_Y0, BASE_RAIL_Y1 = 0.090, 0.330
BASE_WRIST_EDGE = 0.030              # the collar's wrist rim; nothing of the base is nearer the hand
BASE_ELBOW_EDGE = 0.352
BASE_TOP = 0.236                     # the dorsal shell's flat back line

# Index 0 is a structural metal because `bmesh.ops.bevel` stamps every face it
# creates with material index 0 — see `_buildlib` trap notes.
STEEL, DARK, PALE, ORANGE, CHROME, RUBBER, BLACK, BRASS, AMBER, WARN = range(10)
MATS = ["Mat_Metal_Steel_Worn",        # spine, clamps, plates
        "Mat_Metal_Steel_Dark",        # machined collars, cradles
        "Mat_Paint_Hull_Bleached",     # painted panels on the spine
        "Mat_Paint_Safety_Orange",     # high-vis bands
        "Mat_Metal_Chrome_Scuffed",    # fasteners, cable
        "Mat_Plastic_Rubber_Black",    # pads under the clamps
        "Mat_Neutral_Black_Matte",     # shadow gaps
        "Mat_Metal_Brass_Tarnished",   # bearing collars
        "Mat_Emissive_Amber",          # ready lamps
        "Mat_Paint_Warn_Red"]          # arming stripes

BEVEL_W = 0.0016

Y_ELBOW = 0.2050        # back of the cuff
Y_WRIST = 0.0060        # front of the cuff — the arm axis crosses it here
SPINE_Z0, SPINE_Z1 = 0.0560, 0.0760     # bracer channel floor and rail tops

# Where a spine clamps onto the cuff: (station y, half-extent in x,
# half-extent in z), read off `arm_cuff.SLEEVE`. The cuff tapers, so a band
# written at a fixed radius is off the sleeve at one end and inside it at the
# other — the same trap `arm_cuff._at` exists to dodge.
#
# Two stations, not the sleeve's three: the cuff already carries three canvas
# bands of its own, and a steel clamp over every one of them turns the sleeve
# into a barcode with no arm visible between the rings.
#
# The rear station is 0.150 rather than the sleeve's last band at 0.192: the
# suit balloons into an elbow pad over the last third of the forearm — 0.17 m
# of radius against 0.11 at mid-arm — and a band placed back there is inside
# the pad no matter how far it stands off the cuff.
CLAMPS = [(0.0300, 0.0320, 0.0390),
          (0.1500, 0.0429, 0.0519)]

# How far the clamp bands stand off the cuff, and the reason they stand off it
# at all: **the cuff is mostly invisible on the wearer.** The astronaut's suit
# sleeve is fatter than the sleeve `arm_cuff` was authored for, so the webbing
# sinks into it and what should read as the device's arm frame reads as
# nothing. The bands are sized off the cuff rather than being part of it, and
# 20 mm here is 42 mm on the worn device — enough that two steel hoops sit
# visibly around the suit and carry the read the cuff cannot.
BAND_STANDOFF = 0.0200


# ---------------------------------------------------------------------------
# Appending components
# ---------------------------------------------------------------------------

def append_objects(blend, names, into):
    """Append named objects from a component file into `into`.

    Appended, not linked: an export has to carry real mesh data, and a linked
    object arrives as a proxy the FBX writer skips silently.

    Ends with a depsgraph update because `matrix_world` reads identity right
    after `libraries.load` + `objects.link`, however `location` reads, and a
    pivot derived from it lands on the origin.
    """
    with bpy.data.libraries.load(blend, link=False) as (src, dst):
        missing = [n for n in names if n not in set(src.objects)]
        if missing:
            raise SystemExit("Not in %s: %s" % (blend, ", ".join(missing)))
        dst.objects = list(names)
    out = []
    for name in names:
        obj = bpy.data.objects[name]
        into.objects.link(obj)
        out.append(obj)
    bpy.context.view_layer.update()
    return out


def place(obj, matrix):
    """Apply `matrix` into the mesh data and the object's origin, leaving the
    object at rotation 0 and scale 1.

    The library's convention is that transforms are applied, and it is not
    cosmetic here: a rotated object exports with that rotation baked into the
    FBX node, and Unity then hands the game a Transform whose local axes are not
    the ones the code reasons about. The origin still carries the meaning —
    it moves with the matrix, the mesh rotates about it.
    """
    obj.data.transform(matrix.to_3x3().to_4x4())
    obj.location = matrix @ obj.location
    return obj


def seat(obj, at, rotation=None, scale=1.0):
    """`place` for a component whose origin must land exactly at `at`.

    `place` sends the object's own origin through the matrix, which is right
    for a cuff authored at the world origin and wrong for a knob authored with
    its pivot out at its socket: the knob would land wherever the socket's
    coordinates map to, not where it is wanted. This composes the un-offset in,
    so the mesh is rotated and scaled about its own pivot and the pivot lands
    at `at`.
    """
    m = (Matrix.Translation(Vector(at))
         @ (rotation or Matrix.Identity(4))
         @ Matrix.Diagonal(Vector((scale,) * 3)).to_4x4()
         @ Matrix.Translation(-obj.location))
    return place(obj, m)


# ---------------------------------------------------------------------------
# The cuff
# ---------------------------------------------------------------------------

def cuff_matrix():
    """Cuff local (wrist at origin, running up +Z) into the family frame.

    `R_z(-90)` first, so the cuff's mounting boss ends up on top under the
    spine and its buckles out on the -X flank; then `R_x(-90)` to lay the
    sleeve down the arm. Applied in that order — swapping them puts the
    buckles under the spine, where they are both invisible and intersecting.
    """
    return (Matrix.Rotation(math.radians(-90), 4, 'X')
            @ Matrix.Rotation(math.radians(-90), 4, 'Z'))


def append_cuff(coll):
    """The webbing cuff, seated. (Legacy — see the module doc.)"""
    obj, = append_objects(CUFF, [CUFF_OBJECT], coll)
    return place(obj, cuff_matrix())


def base_object_names(variant):
    """The objects that make up one base variation, in the base file's names."""
    if variant not in BASE_VARIANTS:
        raise SystemExit("No gauntlet base variation %r; one of %s"
                         % (variant, ", ".join(BASE_VARIANTS)))
    parts = list(BASE_PARTS)
    if variant in ("Mount", "Rail"):
        parts += BASE_MOUNT_PARTS
    if variant == "Rail":
        parts += BASE_RAIL_PARTS
    return ["Mesh_GauntletBase_%s_%s" % (part, variant) for part in parts]


def append_rails(coll):
    """The Rail variation's two rails — device hardware, not a mount.

    Only the Sucker Puncher has ever used them: they are the track its sled
    rides, and they happen to live in the base file because the base once came
    in a Rail variation for exactly that one gauntlet. They arrive under the
    device's own names, because a `Mesh_GauntletBase_` name in a device file
    now means the bracer leaked into it — see `strip_bracer.py`.

    There is deliberately no `append_base()` beside this: a gauntlet is the
    device alone, and the bracer under it is worn separately and permanently.
    """
    names = ["Mesh_GauntletBase_%s_Rail" % part for part in BASE_RAIL_PARTS]
    objs = append_objects(BASE, names, coll)
    for obj, part in zip(objs, BASE_RAIL_PARTS):
        obj.name = "Mesh_SuckerPuncher_%s" % part
        obj.data.name = "Data_SuckerPuncher_%s" % part
    return objs


# ---------------------------------------------------------------------------
# Spine and clamps — the frame that holds a device onto the cuff
# ---------------------------------------------------------------------------

def spine(p, y0, y1, z0=SPINE_Z0, z1=SPINE_Z1, panel=None, rivets=None):
    """A steel channel down the back of the cuff: floor, two side rails, and
    optionally a painted inset panel with a row of rivets.

    `panel` is `(y_a, y_b)`, `rivets` is `(y_a, y_b, count)`. Returns the boxy
    faces for bevelling.
    """
    hard = []
    floor_top = z0 + 0.0050
    hard += p.slab((-0.0180, y0, z0), (0.0180, y1, floor_top), STEEL)
    for sx in (-1, 1):
        hard += p.slab((sx * 0.0130, y0, z0), (sx * 0.0180, y1, z1), STEEL)
    if panel is not None:
        # Inset, so a long channel is not one bare surface.
        hard += p.slab((-0.0120, panel[0], floor_top),
                       (0.0120, panel[1], floor_top + 0.0025), PALE)
    if rivets is not None:
        p.rivets((0.0, rivets[0], floor_top + 0.0030),
                 (0.0, rivets[1], floor_top + 0.0030), rivets[2],
                 radius=0.0022, height=0.0022, axis='Z', mat=CHROME)
    return hard


def clamp_band(p, y, hx, hz, arc0=60.0, arc1=-240.0, count=15):
    """A steel band round the cuff at one station, open at the top.

    It runs from 60 degrees the long way round the bottom to -240, so both
    ends finish beside the spine's side rails and the 60-degree gap is the arc
    the channel already occupies. Closing the ring instead buries a sixth of
    the band inside the spine, which costs triangles and leaves interior faces
    exactly where two parts meet.

    Each segment is laid by `R_y(90 - a)`, which is the rotation that sends the
    box's local +Z along the radius and its local +X along the tangent — so
    `size` reads (arc step, band width, band thickness). Using `atan2(z, x)`
    instead, which is the obvious thing to write, sends the tangent along the
    radius: the band comes out as a ring of splayed teeth pointing away from
    the arm, and it looks like a modelling accident because it is one.
    """
    # The segments are deliberately NOT returned for bevelling. Thirty
    # 4.4 mm blocks at two segments each is the single largest line in a
    # gauntlet's triangle budget, and a 1.6 mm chamfer on a band that reads as
    # one continuous ring is invisible.
    hard = []
    step = abs(math.radians((arc1 - arc0) / (count - 1)))
    for i in range(count):
        a = math.radians(arc0 + (arc1 - arc0) * i / (count - 1))
        # Elliptical, not circular: the cuff is 0.091 across and 0.110 deep at
        # the elbow, and a circular band fits neither dimension.
        x, z = math.cos(a) * (hx + BAND_STANDOFF), math.sin(a) * (hz + BAND_STANDOFF)
        p.box((x, y, z), (max(hx, hz) * step * 1.55, 0.0145, 0.0055), STEEL,
              rot=Matrix.Rotation(math.pi / 2 - a, 4, 'Y'))
    # Lugs bolting each end of the band to the spine's side rails.
    for sx in (-1, 1):
        hard += p.box((sx * 0.0175, y, SPINE_Z0 + 0.0050),
                      (0.0060, 0.0150, 0.0140), STEEL)
        p.cyl((sx * 0.0210, y, SPINE_Z0 + 0.0050), 0.0028, 0.0040, 'X', 8,
              CHROME)
    return hard


def clamp_bands(p, pad_top=SPINE_Z0 - 0.0005):
    """Both clamp stations, each with a rubber pad between spine and cuff.

    `pad_top` is where the pad's upper face lands. The bracer left it 0.5 mm
    shy of its spine floor; a spine whose floor is embedded into the boss
    should pass a value 0.5 mm *inside* the floor instead, so the two never
    share a plane.
    """
    hard = []
    for y, hx, hz in CLAMPS:
        hard += clamp_band(p, y, hx, hz)
        hard += p.box((0.0, y, pad_top - 0.0020), (0.0420, 0.0150, 0.0040),
                      RUBBER)
    return hard
