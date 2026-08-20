"""components/props/field_backpack — the astronaut's homemade stand-up cabinet pack.

One variation, deliberately: this is a specific piece of the player's kit rather
than set dressing, and Unity binds to its object names (see
`docs/superpowers/specs/2026-08-12-astronaut-backpack-design.md`). Everything
else in this folder is a family; this one is a contract.

Rustic but built for vacuum: a bent steel-tube exoframe carries the load, a
canvas body is lashed to it with cord through punched brass eyelets, and
scavenged rust-patched plate with deliberately unsquare edges is riveted over
both doors. The one part that reads scifi rather than 1970s expedition gear is
the pair of small amber lamps beside the latches.

It is a big pack on purpose — 0.70 x 0.45 x 1.15 m against a 1.4 m astronaut.
Worn it dominates the silhouette; set down it stands as tall as a crouching
player and opens into a field station.

Axes, which are the whole reason this file is fussy
----------------------------------------------------
Built in the library frame (+Z up, -Y forward).

  +Y  the pack's back, worn against the astronaut. The FRAME: exoframe, back
      plate, shoulder harness. It never moves, and it is what the doors hinge
      from — "opens from the back supported part".
  -Y  the pack's outer face. The two doors close over it.
  +Z  up, both when shouldered and when standing on the ground.

`PIVOT_Door_L` and `PIVOT_Door_R` sit on the two REAR vertical edges with their
local Z along the hinge, so Unity swings each door with one Euler angle about Z.
They open outward like a cabinet, splaying to roughly 135 degrees, which turns
both inner faces toward anyone standing in front — that is what makes the gear
visible on both halves at once.

Every `SOCK_*` empty obeys one rule: **local +Y points out of its own mouth.**
Unity parents an item at identity, so local +Y is the item's up.

  SOCK_Int_*  stow on the doors' INNER faces, so local +Y is +Y at rest (the
              items point into the closed cavity) and swings round to face the
              player as the doors open. Identity rotation.
  SOCK_Ext_*  hang off the OUTER surfaces, pointing -Y, so gear visibly hangs
              out behind the astronaut while the pack is worn. Flipped 180
              about X.

Get this wrong and nothing looks broken in Blender — every item just lies on its
face in game — so `dump_sockets()` asserts it, closed and open, rather than
trusting it.

Origin at the bottom centre of the footprint, matching the rest of this folder.

    blender --background --python field_backpack.py -- --out field_backpack.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, LIB)
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Fabric_Canvas_Faded",    # 0  the body, straps, stow loops
    "Mat_Metal_Steel_Worn",       # 1  the bent exoframe, welds, rivet heads
    "Mat_Metal_Rust_Heavy",       # 2  the scavenged door plates and skid plate
    "Mat_Metal_Brass_Tarnished",  # 3  latches, eyelets, lash hardware
    "Mat_Plastic_Rubber_Black",   # 4  strap pads, lashing cord, lamp cable
    "Mat_Emissive_Amber",         # 5  the latch lamps
]
CANVAS, STEEL, RUST, BRASS, RUBBER, AMBER = range(6)

# --- the body box ---------------------------------------------------------
W_HALF = 0.365        # canvas body half-width; the frame stands proud to 0.425
Y_BACK = 0.225        # inner face of the back wall, against the wearer
Y_SPLIT = 0.070       # where the doors meet the frame's carcass
Y_FRONT = -0.245      # outer face of the closed doors
Z_BOT = 0.070         # the canvas sits on the frame skids, not on the ground
Z_TOP = 1.330
T = 0.024             # frame carcass wall
TD = 0.019            # door wall

# Hinge axes: the two REAR vertical edges. Local Z is the hinge, so Unity drives
# each door with a single Z Euler.
HINGE_L = Vector((-W_HALF, Y_SPLIT, 0.0))
HINGE_R = Vector((W_HALF, Y_SPLIT, 0.0))

# --- where gear actually goes ---------------------------------------------
# Interior stow sits INSIDE THE CARCASS, standing on the shelves — not on the
# doors. Anchors on a door face have to seat their item along the door's normal,
# which means the moment the door swings open every item juts out sideways into
# thin air. Shelves are the only placement that stays natural through the whole
# swing, because the item is resting on something the entire time.
SHELF_Z = (0.094, 0.404, 0.714, 1.024)   # floor, then three shelves
INT_X = (-0.222, 0.0, 0.222)             # three across each tier
INT_Y = 0.150                            # mid-depth of the carcass

# Exterior gear lies FLAT under the cargo nets, hugging the surface.
NET_Z = (0.300, 0.640, 0.980)            # three courses of net per door
NET_X = (0.100, 0.250)                   # two columns per course, from the hinge

# Local +Y is the item's up, local +Z its forward. See the module docstring.
#   interior — +Y onto world +Z, so an item STANDS on its shelf.
#   exterior — +Y out of the surface (-Y) with +Z still up, so an item lies flat
#              against the door with its long axis vertical under the net.
INT_ROT = (math.pi / 2.0, 0.0, 0.0)
EXT_ROT = (0.0, 0.0, math.pi)

# Four anchors per door, staggered across the face rather than gridded, each one
# under a course of net.
EXT_DOOR_CELLS = ((0.105, 0.310), (0.255, 0.520), (0.105, 0.730), (0.255, 0.940))
EXT_DOOR_Y = -0.256      # just proud of the door skin, under the net

# Exterior anchors on the FRAME, which never move.
EXT_FRAME = (
    ("SOCK_Ext_8", (-0.412, -0.150, 0.520)),   # left outrigger loop
    ("SOCK_Ext_9", (0.412, -0.150, 0.520)),    # right outrigger loop
)

# Cut from something else, in each door's (x, z) plane. One long sheared edge
# where it came off the parent sheet, then corners knocked off. It covers only
# part of the face — a plate the size of the panel reads as a panel.
PLATE = [
    (-0.250, 0.250), (-0.020, 0.232), (0.012, 0.400), (-0.010, 0.700),
    (-0.096, 0.784), (-0.088, 0.900), (-0.230, 0.916), (-0.262, 0.760),
    (-0.276, 0.470),
]


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def bent_tube(p, pts, r, mat, seg=10, collar=True):
    """A tube following a polyline, with a weld collar at every kink.

    The exoframe is meant to read as hand-bent over a knee, not drawn through a
    mandrel, so it is a chain of straight runs with visible lumps at the joints
    rather than a swept curve.
    """
    pts = [Vector(q) for q in pts]
    for a, b in zip(pts, pts[1:]):
        d = b - a
        rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        p.cyl((a + b) / 2.0, r, d.length, seg=seg, mat=mat, rot=rot)
    if collar:
        for i in range(1, len(pts) - 1):
            d = pts[i + 1] - pts[i - 1]
            rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
            p.cyl(pts[i], r * 1.24, r * 1.5, seg=seg, mat=mat, rot=rot)


def ribbon(p, pts, width, thick, mat, flat='X'):
    """Webbing along a polyline — `width` lies along `flat`, `thick` across it.

    `flat` has to name an axis the run is never parallel to, or `Part.seam` has
    no plane to lie in. Straps run in YZ so they take the default; the haul
    handle runs along X and takes 'Y'.
    """
    for a, b in zip(pts, pts[1:]):
        p.seam(a, b, width=thick, depth=width, axis=flat, mat=mat)


def stitches(p, a, b, count, mat, size):
    """A dashed thread run. Cheapest thing that reads as sewn rather than moulded."""
    a, b = Vector(a), Vector(b)
    d = b - a
    for i in range(count):
        p.box(a + d * ((i + 0.5) / count), size, mat)


def loop_buckle(p, c, w, h, t, mat, bar=True):
    """A rectangular hardware loop with a centre bar — strap threads through it."""
    cx, cy, cz = c
    for sz in (-1, 1):
        p.box((cx, cy, cz + sz * (h / 2 - t / 2)), (w, t, t), mat)
    for sx in (-1, 1):
        p.box((cx + sx * (w / 2 - t / 2), cy, cz), (t, t, h), mat)
    if bar:
        p.box((cx, cy, cz), (w - 2 * t, t * 0.7, t * 0.7), mat)


def empty(name, loc, coll, rot=(0.0, 0.0, 0.0), size=0.06):
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_type = 'ARROWS'
    obj.empty_display_size = size
    obj.location = Vector(loc)
    obj.rotation_euler = rot
    coll.objects.link(obj)
    return obj


def attach(child, parent, parent_world):
    """Parent with a clean local transform.

    Blender's parent-inverse would hold the child in place while hiding the
    offset in a matrix the FBX flattens away; setting the local location against
    a known parent origin keeps the hierarchy readable on Unity's side. Only
    valid because every parent in this chain is an unrotated translation.
    """
    child.parent = parent
    child.matrix_parent_inverse = Matrix.Identity(4)
    child.location = Vector(child.location) - Vector(parent_world)
    return child


# ---------------------------------------------------------------------------
# Meshes
# ---------------------------------------------------------------------------

def frame(mats, coll):
    """Exoframe, back plate and shoulder harness.

    This is the "back supported part": load-bearing, worn against the astronaut,
    and the thing both doors hinge from. It never moves.
    """
    p = Part(mats)

    for sx in (-1, 1):
        # Main vertical, kinked on its way up and over into the top lash bar.
        bent_tube(p, [
            (sx * 0.402, 0.244, 0.036), (sx * 0.412, 0.234, 0.317),
            (sx * 0.402, 0.246, 0.780), (sx * 0.383, 0.227, 1.170),
            (sx * 0.341, 0.146, 1.295), (sx * 0.295, -0.058, 1.327),
            (sx * 0.268, -0.232, 1.285),
        ], 0.027, STEEL)
        # Skid: the tube turns forward at the floor so the pack stands on steel.
        bent_tube(p, [
            (sx * 0.402, 0.244, 0.036), (sx * 0.388, 0.049, 0.027),
            (sx * 0.356, -0.146, 0.041), (sx * 0.332, -0.210, 0.078),
        ], 0.024, STEEL)
        # Outrigger loop — a D of tube standing off the side, which is what
        # SOCK_Ext_8/9 clip onto.
        bent_tube(p, [
            (sx * 0.402, 0.244, 0.683), (sx * 0.414, 0.073, 0.629),
            (sx * 0.407, -0.156, 0.571), (sx * 0.383, -0.239, 0.507),
            (sx * 0.393, -0.051, 0.449), (sx * 0.402, 0.132, 0.412),
            (sx * 0.402, 0.244, 0.393),
        ], 0.022, STEEL)

    # Top lash bar, cambered so it is obviously not a straight length of pipe.
    bent_tube(p, [
        (-0.268, -0.232, 1.285), (-0.105, -0.256, 1.302),
        (0.105, -0.256, 1.302), (0.268, -0.232, 1.285),
    ], 0.022, STEEL)

    # Welded cross-braces: three ladders and one X across the back.
    for z in (0.293, 0.780, 1.170):
        bent_tube(p, [(-0.402, 0.244, z), (0.402, 0.244, z)], 0.020, STEEL)
        for sx in (-1, 1):
            p.cyl((sx * 0.392, 0.244, z), 0.037, 0.049, axis='X', seg=8, mat=STEEL)
    bent_tube(p, [(-0.397, 0.268, 0.312), (0.397, 0.268, 0.761)], 0.017, STEEL)
    bent_tube(p, [(0.397, 0.283, 0.312), (-0.397, 0.283, 0.761)], 0.017, STEEL)
    p.cyl((0.0, 0.276, 0.536), 0.041, 0.054, axis='Y', seg=8, mat=STEEL)

    # The carcass the doors close onto: back wall, floor, roof and side jambs.
    zm = (Z_BOT + Z_TOP) / 2.0
    zh = Z_TOP - Z_BOT
    ym = (Y_BACK + Y_SPLIT) / 2.0
    yd = Y_BACK - Y_SPLIT
    p.box((0, Y_BACK - T / 2, zm), (2 * W_HALF, T, zh), CANVAS)
    for sx in (-1, 1):
        p.box((sx * (W_HALF - T / 2), ym, zm), (T, yd, zh), CANVAS)
    p.box((0, ym, Z_BOT + T / 2), (2 * W_HALF, yd, T), CANVAS)
    p.box((0, ym, Z_TOP - T / 2), (2 * W_HALF, yd, T), CANVAS)

    # Three shelves. These are what the interior anchors stand on, so they are
    # load-bearing to the design and not just interior dressing — an item with
    # nothing under it is the whole bug this layout exists to avoid.
    for z in SHELF_Z[1:]:
        p.box((0, ym, z - 0.011), (2 * W_HALF - 2 * T, yd, 0.022), CANVAS)
        # A lip along the front edge so nothing slides out when the doors open.
        p.box((0, Y_SPLIT + 0.014, z + 0.014), (2 * W_HALF - 2 * T, 0.020, 0.030), CANVAS)

    # Rolled hem around the mouth the doors seal against.
    p.box((0, Y_SPLIT + 0.005, Z_TOP - 0.024), (0.755, 0.036, 0.049), CANVAS)
    p.box((0, Y_SPLIT + 0.005, Z_BOT + 0.024), (0.755, 0.036, 0.049), CANVAS)
    for sx in (-1, 1):
        p.box((sx * 0.353, Y_SPLIT + 0.005, zm), (0.049, 0.036, zh - 0.049), CANVAS)

    # Punched eyelets down both sides, and the cord that lashes the body to the
    # frame through them. This is the join the whole pack hangs on.
    for sx in (-1, 1):
        for z in (0.220, 0.463, 0.707, 0.951, 1.195):
            p.tube((sx * 0.369, 0.170, z), 0.022, 0.009, 0.020, axis='X',
                   seg=8, mat=BRASS)
            for sz in (-1, 1):
                p.seam((sx * 0.369, 0.170, z), (sx * 0.402, 0.244, z + sz * 0.117),
                       width=0.015, depth=0.015, axis='X', mat=RUBBER)

    # Riveted skid plate under the floor, where a pack dumped on rock wears out.
    p.box((0, ym, Z_BOT - 0.012), (0.706, 0.244, 0.027), RUST)
    for sy in (-1, 1):
        p.rivets((-0.321, ym + sy * 0.105, Z_BOT - 0.012),
                 (0.321, ym + sy * 0.105, Z_BOT - 0.012), 7,
                 radius=0.017, height=0.020, axis='Z', mat=STEEL)

    # Haul handle over the top back.
    ribbon(p, [(-0.122, 0.183, 1.295), (-0.051, 0.202, 1.345),
               (0.051, 0.202, 1.345), (0.122, 0.183, 1.295)],
           0.076, 0.029, CANVAS, flat='Y')

    # Shoulder harness.
    for sx in (-1, 1):
        path = [
            (sx * 0.173, 0.234, 1.243), (sx * 0.210, 0.263, 1.185),
            (sx * 0.241, 0.280, 1.036), (sx * 0.251, 0.273, 0.824),
            (sx * 0.236, 0.258, 0.610), (sx * 0.268, 0.251, 0.375),
            (sx * 0.324, 0.241, 0.156),
        ]
        ribbon(p, path, 0.122, 0.034, CANVAS)
        pad = [(x, y + 0.029, z) for x, y, z in path[1:4]]
        ribbon(p, pad, 0.144, 0.022, RUBBER)
        for a, b in zip(pad, pad[1:]):
            for sw in (-1, 1):
                stitches(p, (a[0] + sw * 0.066, a[1] + 0.012, a[2]),
                         (b[0] + sw * 0.066, b[1] + 0.012, b[2]),
                         5, CANVAS, (0.010, 0.007, 0.022))
        loop_buckle(p, (sx * 0.256, 0.276, 0.492), 0.136, 0.076, 0.017, BRASS)
        loop_buckle(p, (sx * 0.295, 0.263, 0.249), 0.122, 0.066, 0.015, BRASS)

    # A sewn-on repair square, because nothing on this pack was bought new.
    p.box((0.134, Y_BACK + 0.007, 0.868), (0.251, 0.015, 0.219), CANVAS)
    for z in (0.978, 0.758):
        stitches(p, (0.009, Y_BACK + 0.015, z), (0.259, Y_BACK + 0.015, z),
                 9, RUBBER, (0.015, 0.007, 0.007))

    p.bevel(width=0.006)
    return p.finish("Mesh_Backpack_Frame", coll)


def door(mats, coll, sx, name):
    """One half of the outer body, hinged on its own rear vertical edge.

    Modelled in world space and then re-origined onto its hinge, so Unity swings
    it with a single Z Euler about `PIVOT_Door_*`.
    """
    p = Part(mats)
    hinge_x = sx * W_HALF
    inner_x = 0.0
    xm = (hinge_x + inner_x) / 2.0
    xw = abs(hinge_x - inner_x)

    zm = (Z_BOT + Z_TOP) / 2.0
    zh = Z_TOP - Z_BOT
    ym = (Y_SPLIT + Y_FRONT) / 2.0
    yd = Y_SPLIT - Y_FRONT

    # Outer skin, plus a return lip all the way round so the door reads as a
    # tray rather than a flat board.
    p.box((xm, Y_FRONT + TD / 2, zm), (xw, TD, zh), CANVAS)
    p.box((xm, ym, Z_TOP - TD / 2), (xw, yd, TD), CANVAS)
    p.box((xm, ym, Z_BOT + TD / 2), (xw, yd, TD), CANVAS)
    p.box((hinge_x - sx * TD / 2, ym, zm), (TD, yd, zh), CANVAS)
    p.box((inner_x + sx * TD / 2, ym, zm), (TD, yd, zh), CANVAS)

    # Cargo net over the outer face: three courses of cord between lash rails,
    # each course covering one row of exterior anchors. This is what makes gear
    # on the outside read as lashed on rather than glued to a panel.
    ny = Y_FRONT - 0.030
    for cz in NET_Z:
        # Lash rails top and bottom of the course.
        for rz in (cz - 0.115, cz + 0.115):
            p.box(((hinge_x + inner_x) / 2.0, Y_FRONT - 0.012, rz),
                  (abs(hinge_x - inner_x) - 0.030, 0.020, 0.016), CANVAS)
        # The mesh itself: a diagonal lattice strung between the rails.
        for k in range(7):
            u = 0.055 + k * 0.048
            xa = hinge_x - sx * u
            xb = hinge_x - sx * (u + 0.058)
            p.seam((xa, ny, cz - 0.115), (xb, ny, cz + 0.115),
                   width=0.009, depth=0.009, axis='Y', mat=RUBBER)
            p.seam((xb, ny, cz - 0.115), (xa, ny, cz + 0.115),
                   width=0.009, depth=0.009, axis='Y', mat=RUBBER)
        # Brass hooks pulling the net down onto the load.
        for hx in (0.085, 0.235):
            p.tube((hinge_x - sx * hx, Y_FRONT - 0.008, cz - 0.126), 0.017, 0.006,
                   0.014, axis='Y', seg=8, mat=BRASS)

    # The scavenged plate, mirrored per door. A prism off a nine-point profile,
    # so the outline is visibly cut from something bigger rather than sheared.
    # It sits between net courses so the two do not fight for the same face.
    profile = [(sx * px * 1.22, pz * 1.22) for px, pz in PLATE]
    p.prism(profile, 0.019, axis='Y', mat=RUST, offset=(0, Y_FRONT + 0.005, 0))
    for a, b in zip(profile, profile[1:] + profile[:1]):
        n = max(2, int(math.dist(a, b) / 0.110))
        p.rivets((a[0], Y_FRONT - 0.005, a[1]), (b[0], Y_FRONT - 0.005, b[1]), n,
                 radius=0.017, height=0.019, axis='Y', mat=STEEL)

    # Hem stitching around the outer edge.
    for hz in (Z_BOT + 0.041, Z_TOP - 0.041):
        stitches(p, (hinge_x - sx * 0.036, Y_FRONT - 0.005, hz),
                 (inner_x + sx * 0.036, Y_FRONT - 0.005, hz), 14, RUBBER,
                 (0.017, 0.010, 0.007))

    p.bevel(width=0.006)
    hinge = HINGE_L if sx < 0 else HINGE_R
    return p.finish(name, coll, origin=hinge)


def latch(mats, coll, sx, name):
    """A brass draw latch where the two doors meet, with the pack's one scifi note.

    Modelled about the centre seam and re-origined onto the door's hinge so it
    parents cleanly and swings with its own door.
    """
    p = Part(mats)
    x = sx * 0.036
    z = 0.780
    p.box((x, Y_FRONT + 0.024, z), (0.034, 0.122, 0.183), BRASS)          # backplate
    p.box((x + sx * 0.017, Y_FRONT + 0.015, z), (0.027, 0.146, 0.073), BRASS)  # cam lever
    p.box((x + sx * 0.012, Y_FRONT + 0.085, z), (0.020, 0.110, 0.037), BRASS)  # hook arm
    p.cyl((x + sx * 0.012, Y_FRONT - 0.022, z), 0.017, 0.059, axis='X', seg=8, mat=BRASS)
    p.rivets((x, Y_FRONT + 0.005, z - 0.071), (x, Y_FRONT + 0.005, z + 0.071), 2,
             radius=0.013, height=0.017, axis='X', mat=STEEL)

    # Hooded amber lamp beside the latch, wired down the door.
    lz = z + 0.281
    p.cyl((x, Y_FRONT + 0.005, lz), 0.054, 0.034, axis='Y', seg=12, mat=STEEL)
    p.box((x, Y_FRONT - 0.002, lz + 0.044), (0.120, 0.054, 0.024), STEEL)
    p.cyl((x, Y_FRONT - 0.012, lz), 0.034, 0.024, axis='Y', seg=12, mat=AMBER)
    ribbon(p, [(x, Y_FRONT - 0.012, lz - 0.049), (x + sx * 0.034, Y_FRONT - 0.012, lz - 0.220),
               (x, Y_FRONT - 0.012, lz - 0.390)], 0.017, 0.017, RUBBER)

    p.bevel(width=0.004)
    hinge = HINGE_L if sx < 0 else HINGE_R
    return p.finish(name, coll, origin=hinge)


# ---------------------------------------------------------------------------
# Verification
# ---------------------------------------------------------------------------

def dump_sockets(open_deg=135.0):
    """Print and assert the socket convention, closed and open.

    Three invariants, all of which render identically in Blender when broken and
    only show up in game as items floating or lying on their faces:

      interior — +Y is world UP at all times, because the item is standing on a
                 shelf. It must NOT depend on the door angle: an anchor that
                 swings is an anchor on a door, which is the bug this layout
                 exists to avoid.
      interior — must sit INSIDE the carcass box, not proud of the mouth.
      exterior — +Y points out of the surface it is lashed to.
    """
    bpy.context.view_layer.update()
    pivots = {s: bpy.data.objects["PIVOT_Door_" + s] for s in ("L", "R")}

    socks = sorted((o for o in bpy.data.objects if o.name.startswith("SOCK_")),
                   key=lambda o: o.name)
    ext = [o for o in socks if o.name.startswith("SOCK_Ext_")]
    inte = [o for o in socks if o.name.startswith("SOCK_Int_")]
    if len(ext) != 10 or len(inte) != 12:
        raise SystemExit("Expected 10 exterior + 12 interior sockets, found %d + %d"
                         % (len(ext), len(inte)))

    up_world = Vector((0.0, 0.0, 1.0))

    for state, ang in (("closed", 0.0), ("open", open_deg)):
        # Left door swings -Z, right door +Z: both outward, away from the seam.
        pivots["L"].rotation_euler = (0.0, 0.0, math.radians(ang))
        pivots["R"].rotation_euler = (0.0, 0.0, math.radians(-ang))
        bpy.context.view_layer.update()

        print("  -- sockets, doors %s (%.0f deg) --" % (state, ang))
        for o in socks:
            m = o.matrix_world
            up = m.col[1].to_3d().normalized()
            print("  %-16s pos=(%7.4f,%7.4f,%7.4f) +Y=(%5.2f,%5.2f,%5.2f)"
                  % (o.name, *m.translation, *up))

        for o in inte:
            m = o.matrix_world
            up = m.col[1].to_3d().normalized()
            if up.dot(up_world) < 0.999:
                raise SystemExit(
                    "%s must point +Y at world up so its item stands on the shelf; "
                    "points %s with the doors %s. An anchor that moves with the "
                    "doors is on a door, not on a shelf."
                    % (o.name, tuple(round(v, 3) for v in up), state))
            q = m.translation
            if not (-W_HALF < q.x < W_HALF and Y_SPLIT < q.y < Y_BACK
                    and Z_BOT < q.z < Z_TOP):
                raise SystemExit(
                    "%s at %s is outside the carcass box; items must sit INSIDE "
                    "the pack." % (o.name, tuple(round(v, 3) for v in q)))

        for o in ext:
            up = o.matrix_world.col[1].to_3d().normalized()
            if abs(up.z) > 0.35:
                raise SystemExit(
                    "%s should point out of the surface it is lashed to, not up "
                    "or down; +Y is %s" % (o.name, tuple(round(v, 3) for v in up)))

    for s in pivots.values():
        s.rotation_euler = (0.0, 0.0, 0.0)
    bpy.context.view_layer.update()

    pts = [o.matrix_world @ Vector(c)
           for o in bpy.data.objects if o.type == 'MESH' for c in o.bound_box]
    lo = Vector((min(q.x for q in pts), min(q.y for q in pts), min(q.z for q in pts)))
    hi = Vector((max(q.x for q in pts), max(q.y for q in pts), max(q.z for q in pts)))
    print("  BOUNDS W=%.3f D=%.3f H=%.3f  (%.3f..%.3f, %.3f..%.3f, %.3f..%.3f)"
          % (hi.x - lo.x, hi.y - lo.y, hi.z - lo.z, lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_Backpack_Field")

    frame(mats, coll)

    pivots = {}
    for s, sx, hinge in (("L", -1, HINGE_L), ("R", 1, HINGE_R)):
        pivot = empty("PIVOT_Door_" + s, hinge, coll, size=0.12)
        pivots[s] = pivot
        attach(door(mats, coll, sx, "Mesh_Backpack_Door" + s), pivot, hinge)
        attach(latch(mats, coll, sx, "Mesh_Backpack_Latch_" + s), pivot, hinge)

    # Exterior: four per door under the cargo nets, so gear rides on the outside
    # and swings with the door it is lashed to, plus one on each outrigger loop.
    for i, (s, sx) in enumerate((("L", -1), ("R", 1))):
        hinge_x = sx * W_HALF
        for j, (cx, cz) in enumerate(EXT_DOOR_CELLS):
            loc = (hinge_x - sx * cx, EXT_DOOR_Y, cz)
            sock = empty("SOCK_Ext_%d" % (i * len(EXT_DOOR_CELLS) + j), loc, coll,
                         rot=EXT_ROT)
            attach(sock, pivots[s], HINGE_L if sx < 0 else HINGE_R)
    for name, loc in EXT_FRAME:
        empty(name, loc, coll, rot=EXT_ROT)

    # Interior: three across on each of four tiers, standing on the carcass floor
    # and shelves. Unparented — these must NOT move with the doors, or an item
    # ends up swinging through the air on the end of a panel.
    for tier, sz in enumerate(SHELF_Z):
        for col, sxx in enumerate(INT_X):
            empty("SOCK_Int_%d" % (tier * len(INT_X) + col),
                  (sxx, INT_Y, sz + 0.012), coll, rot=INT_ROT)

    report()
    dump_sockets()
    save(out)


main()
