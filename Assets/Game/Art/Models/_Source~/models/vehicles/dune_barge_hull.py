"""The dune barge's own hull — the geometry no other model shares.

Imported by `dune_barge.py` (the model generator); kept separate so the hull's layout constants
are one place both the assembly and the interior read from. Every piece is its own object.

The barge is a rusted tracked land-ship (concept: "Kl-modifisert" desert crawler reference):
a long riveted hull on two pairs of tracks, a rounded bridge perched on the front, an open roof
deck behind it. It is WALKABLE for the 3 m player: the main deck is 4.0 m floor to ceiling, the
bridge 3.4 m, and every doorway a 2.2 x 3.4 m clear opening.

Coordinates: metres, +Z up, -Y forward (library convention). y = -11 is the bow, +11 the stern.
"""

import math

from mathutils import Matrix, Vector

# ── Layout (shared with dune_barge.py) ───────────────────────────────────────
HALF_W = 3.9            # hull side walls' inner face, x = +-HALF_W
WALL_T = 0.16
BOW_Y, STERN_Y = -10.5, 10.5
FLOOR_Z = 3.0           # main deck floor top
CEIL_Z = 7.0            # main deck ceiling underside
DECK_T = 0.2            # floor / ceiling slab thickness
ROOF_Z = CEIL_Z + DECK_T                 # roof deck top = bridge floor top (7.2)
BRIDGE_REAR_Y = -2.0
BRIDGE_HALF_W = 3.7
BRIDGE_TOP = ROOF_Z + 4.2                # bridge ceiling underside (11.4): door frames are 4.0 tall
BELLY_Z = 1.6           # hull belly underside, between the tracks
BELLY_HALF_TOP = 2.55   # the belly is a narrow tub; the deck overhangs the tracks either side
RIVET_PITCH = 1.0

DOOR_W, DOOR_H = 2.2, 3.55               # hull_door frames: 2.2 x 3.4 clear above a 0.15 sill
BOARDING_Y = -1.1       # centre of the left-side boarding door
BULKHEAD_ENGINE_Y = 3.5
BULKHEAD_CARGO_Y = 8.2
RAMP_W, RAMP_H = 3.2, 4.0                # stern cargo opening

STAIRWELL = (1.35, 3.75, -7.4, 1.4)      # x0, x1, y0, y1 cut in the ceiling
ROOF_HATCH = (-1.6, 9.35)                # x, y of the roof hatch, over the cargo bay (engines fill the engine room)
HATCH_HOLE = 1.9

WINDOW_BAND = (5.3, 6.15)                # side window band z
SIDE_WINDOWS_Y = (-8.6, -6.2, -3.8, 1.8, 4.6, 7.0, 9.2)
SIDE_WINDOW_W = 1.2
BRIDGE_WINDOW_BAND = (9.0, 10.3)


def _gaps(lo, hi, holes):
    """Solid spans between [lo, hi] minus the (a, b) holes."""
    spans, cur = [], lo
    for a, b in sorted(holes):
        if a > cur:
            spans.append((cur, a))
        cur = max(cur, b)
    if cur < hi:
        spans.append((cur, hi))
    return spans


def side_wall(P, side, mats):
    """One side wall: plated bands, a window band with openings, the boarding door on the left."""
    x0 = side * HALF_W
    x1 = side * (HALF_W + WALL_T)
    lo_x, hi_x = min(x0, x1), max(x0, x1)
    door = [(BOARDING_Y - DOOR_W / 2, BOARDING_Y + DOOR_W / 2)] if side < 0 else []
    top_door = FLOOR_Z + DOOR_H
    # below the window band, around the door
    for a, b in _gaps(BOW_Y, STERN_Y, door):
        P.slab((lo_x, a, FLOOR_Z - DECK_T), (hi_x, b, WINDOW_BAND[0]), mats["hull"])
    if door:
        a, b = door[0]
        P.slab((lo_x, a, top_door), (hi_x, b, WINDOW_BAND[0]), mats["hull"])   # lintel
    # window band piers
    wins = [(y - SIDE_WINDOW_W / 2, y + SIDE_WINDOW_W / 2) for y in SIDE_WINDOWS_Y]
    for a, b in _gaps(BOW_Y, STERN_Y, wins):
        P.slab((lo_x, a, WINDOW_BAND[0]), (hi_x, b, WINDOW_BAND[1]), mats["hull"])
    # top band up to the roof edge
    P.slab((lo_x, BOW_Y, WINDOW_BAND[1]), (hi_x, STERN_Y, ROOF_Z), mats["hull"])


def side_straps(P, side, mats):
    """Riveted horizontal straps over the side plating — the image's banded hull. Stood proud 2 cm."""
    x = side * (HALF_W + WALL_T + 0.02)
    riveted = (FLOOR_Z + 0.35, ROOF_Z - 0.3)          # rivets on the outer straps only
    for z in (FLOOR_Z + 0.35, WINDOW_BAND[0] - 0.08, WINDOW_BAND[1] + 0.08, ROOF_Z - 0.3):
        for a, b in _gaps(BOW_Y, STERN_Y, [(BOARDING_Y - DOOR_W / 2 - 0.1, BOARDING_Y + DOOR_W / 2 + 0.1)]
                          if side < 0 and z < FLOOR_Z + DOOR_H else []):
            P.box((x, (a + b) / 2, z), (0.05, b - a, 0.14), mats["strap"])
            if z in riveted:
                P.rivets((x + side * 0.03, a + 0.1, z), (x + side * 0.03, b - 0.1, z),
                         max(2, int((b - a) / RIVET_PITCH)), radius=0.03, height=0.02, axis='X',
                         mat=mats["strap"])


def end_wall(P, y, mats, opening=None, bottom=FLOOR_Z - DECK_T):
    """A transverse wall spanning the hull at y, with an optional (x0, x1, top) opening."""
    y0, y1 = y - WALL_T / 2, y + WALL_T / 2
    holes = [(opening[0], opening[1])] if opening else []
    for a, b in _gaps(-HALF_W - WALL_T, HALF_W + WALL_T, holes):
        P.slab((a, y0, bottom), (b, y1, ROOF_Z), mats["hull"])
    if opening:
        P.slab((opening[0], y0, opening[2]), (opening[1], y1, ROOF_Z), mats["hull"])


def bulkhead(P, y, mats):
    end_wall(P, y, mats, opening=(-DOOR_W / 2, DOOR_W / 2, FLOOR_Z + DOOR_H), bottom=FLOOR_Z - 0.01)


def floor_slab(P, mats):
    P.slab((-HALF_W, BOW_Y, FLOOR_Z - DECK_T), (HALF_W, STERN_Y, FLOOR_Z), mats["deck"])


def ceiling_slab(P, mats):
    """Main deck ceiling = bridge floor + roof deck, with the stairwell and roof hatch cut out."""
    sx0, sx1, sy0, sy1 = STAIRWELL
    hx, hy = ROOF_HATCH
    h = HATCH_HOLE / 2
    z0, z1 = CEIL_Z, ROOF_Z
    # split along y into strips that avoid both holes
    ys = sorted({BOW_Y - 0.3, sy0, sy1, hy - h, hy + h, STERN_Y + 0.3})
    for a, b in zip(ys, ys[1:]):
        mid = (a + b) / 2
        holes = []
        if sy0 <= mid <= sy1:
            holes.append((sx0, sx1))
        if hy - h <= mid <= hy + h:
            holes.append((hx - h, hx + h))
        for c, d in _gaps(-HALF_W - WALL_T, HALF_W + WALL_T, holes):
            P.slab((c, a, z0), (d, b, z1), mats["roof"])


def belly(P, mats):
    """The lower hull between the tracks: a lofted tub, bow swept up into the prow."""
    sections = []
    for y, bot, half_bot in ((STERN_Y + 0.4, BELLY_Z + 0.5, 1.9), (8.5, BELLY_Z, 2.1),
                             (-5.5, BELLY_Z, 2.1), (-9.5, BELLY_Z + 0.4, 2.0),
                             (-11.4, FLOOR_Z - 0.3, 1.9)):
        top = FLOOR_Z - DECK_T + 0.03      # embedded 3 cm into the floor slab
        half_top = BELLY_HALF_TOP
        sections.append((y, [(-half_bot, bot), (half_bot, bot), (half_top, top), (-half_top, top)]))
    P.loft(sections, axis='Y', mat=mats["hull"])


def prow(P, mats):
    """The slanted bow plate over the front tracks, with the image's vertical ribs."""
    rows = []
    for i in range(6):
        t = i / 5
        z = FLOOR_Z - 0.3 + t * (ROOF_Z - FLOOR_Z + 0.3)
        y = -11.4 + t * 0.9                  # leans back as it rises
        rows.append([(x, y, z) for x in (-HALF_W - 0.15, -1.3, 1.3, HALF_W + 0.15)])
    P.sheet([[Vector(p) for p in r] for r in rows], 0.14, mat=mats["hull"], smooth=False)
    for x in (-3.0, -1.8, -0.6, 0.6, 1.8, 3.0):
        P.segment((x, -11.52, FLOOR_Z - 0.2), (x, -10.62, ROOF_Z - 0.05),
                  [(0.0, 0.07, 0.05), (1.0, 0.07, 0.05)], mat=mats["strap"], ring=6)


def bridge_plan(n=14):
    """Plan outline of the bridge: straight sides aft, a semicircular front."""
    cy = -10.8 + BRIDGE_HALF_W
    pts = []
    for i in range(n + 1):
        a = math.pi * i / n                   # 0 = +x side, pi = -x side, round through -y
        pts.append((BRIDGE_HALF_W * math.cos(a), cy - BRIDGE_HALF_W * math.sin(a)))
    return [(BRIDGE_HALF_W, BRIDGE_REAR_Y)] + pts + [(-BRIDGE_HALF_W, BRIDGE_REAR_Y)]


def bridge_walls(P, mats, rear_door_x=-1.5):
    """Wall segments along the bridge outline, with a window band round the front and sides."""
    outline = bridge_plan()
    z0, z1 = ROOF_Z - 0.01, BRIDGE_TOP + 0.01
    wb0, wb1 = BRIDGE_WINDOW_BAND
    for (xa, ya), (xb, yb) in zip(outline, outline[1:]):
        a, b = Vector((xa, ya, 0)), Vector((xb, yb, 0))
        d = b - a
        length = d.length
        mid = (a + b) / 2
        rot = Matrix.Rotation(math.atan2(d.y, d.x), 4, 'Z')
        # outward = rotate the segment direction by -90 deg
        out = Vector((d.y, -d.x, 0)).normalized()
        c = mid + out * (WALL_T / 2)
        windowed = length < 2.0 or abs(d.x) < 0.1      # the arc and the straight sides
        pier = 0.12 if windowed else 0.0
        if windowed and length > 3.0:                   # straight sides: two windows
            pieces = [(-length / 2, -length / 2 + 0.6), (-0.3, 0.3), (length / 2 - 0.6, length / 2)]
        elif windowed:
            pieces = [(-length / 2, -length / 2 + pier), (length / 2 - pier, length / 2)]
        else:
            pieces = [(-length / 2, length / 2)]
        for s0, s1 in pieces:                           # window-band piers
            P.box(c + (d.normalized() * ((s0 + s1) / 2)) + Vector((0, 0, (wb0 + wb1) / 2)),
                  (s1 - s0, WALL_T, wb1 - wb0), mats["hull"], rot=rot)
        P.box(c + Vector((0, 0, (z0 + wb0) / 2)), (length + 0.02, WALL_T, wb0 - z0), mats["hull"], rot=rot)
        P.box(c + Vector((0, 0, (wb1 + z1) / 2)), (length + 0.02, WALL_T, z1 - wb1), mats["hull"], rot=rot)
    # rear wall with the door to the roof deck
    y = BRIDGE_REAR_Y + WALL_T / 2
    door = (rear_door_x - DOOR_W / 2, rear_door_x + DOOR_W / 2)
    for a, b in _gaps(-BRIDGE_HALF_W, BRIDGE_HALF_W, [door]):
        P.slab((a, y - WALL_T / 2, z0), (b, y + WALL_T / 2, z1), mats["hull"])
    P.slab((door[0], y - WALL_T / 2, ROOF_Z + DOOR_H), (door[1], y + WALL_T / 2, z1), mats["hull"])


def bridge_roof(P, mats):
    outline = bridge_plan(20)
    pts = [(x * 1.05, y - 0.1 if y < -3 else y) for x, y in outline]
    P.prism([(x, y) for x, y in pts], 0.22, axis='Z', mat=mats["roof"],
            offset=(0, 0, BRIDGE_TOP + 0.11))


def bridge_glass(P, mats):
    """One glazing ring set just inside the window band — a single pane object per bridge."""
    outline = bridge_plan()
    wb0, wb1 = BRIDGE_WINDOW_BAND
    for (xa, ya), (xb, yb) in zip(outline, outline[1:]):
        a, b = Vector((xa, ya, 0)), Vector((xb, yb, 0))
        d = b - a
        if not (d.length < 2.0 or abs(d.x) < 0.1):
            continue
        rot = Matrix.Rotation(math.atan2(d.y, d.x), 4, 'Z')
        out = Vector((d.y, -d.x, 0)).normalized()
        c = (a + b) / 2 + out * (WALL_T * 0.5) + Vector((0, 0, (wb0 + wb1) / 2))
        P.box(c, (d.length, 0.03, wb1 - wb0 + 0.04), mats["glass"], rot=rot)
