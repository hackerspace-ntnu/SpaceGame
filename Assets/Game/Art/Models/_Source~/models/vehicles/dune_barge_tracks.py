"""Track geometry for the dune barge: where the wheels go and where every link lies.

Imported by `dune_barge.py`. Pure maths over (y, z) circles in a track's side plane, so the
loop can be checked without Blender: a track belt is the convex hull of its wheels, walked at
the link pitch.

A UNIT is one track assembly: its wheels (drive sprocket, idler, road wheels, return rollers)
as circles, plus the lateral centre x. Links sit with their INNER face on the belt line and
their outer face (the cleats) facing out of the loop.
"""

import math

LINK_PITCH = 0.36       # track_link.blend's pitch — links must fill the loop at exactly this spacing
LINK_INNER = 0.18       # ground to the inner face of a link on the bottom run (plate + cleat)

# Wheel radii, from the library parts (track_wheel.blend, road_wheel.blend Hub).
R_SPROCKET = 0.6803     # the link SEAT radius (track_link inner face); hinge pins sit at 0.752
R_IDLER = 0.65
R_ROAD = 0.65           # road wheels are track_wheel Idlers (spoked twin discs, 1.3 m)
R_ROLLER = 0.225

# (kind, y, z) for each wheel; z is the axle height. The bottom run rests on the road wheels.
ROAD_Z = LINK_INNER + R_ROAD
MAIN_WHEELS = (
    [("Idler", -3.6, 1.05), ("Sprocket", 9.7, 1.45)]
    + [("Road", y, ROAD_Z) for y in (-2.0, 0.0, 2.0, 4.0, 6.0, 8.0)]
    + [("Roller", y, 2.0) for y in (-0.9, 3.0, 6.9)]
)
POD_WHEELS = (
    [("Idler", -11.05, 1.1), ("Sprocket", -7.45, 1.25)]
    + [("Road", y, ROAD_Z) for y in (-9.85, -8.65)]
    + [("Roller", -9.25, 1.9)]
)
RADIUS = {"Sprocket": R_SPROCKET, "Idler": R_IDLER, "Road": R_ROAD, "Roller": R_ROLLER}

MAIN_X = 3.45           # lateral centre of the main tracks (+-)
POD_X = 3.35            # lateral centre of the front steering pods (+-)
POD_PIVOT_Y = -9.25     # the pods steer about a vertical axis here


def _hull(points):
    """Andrew's monotone chain; returns the convex hull counter-clockwise in (y, z)."""
    pts = sorted(set(points))
    if len(pts) < 3:
        return pts

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    lower, upper = [], []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


def belt(wheels, samples=48):
    """The belt's inner line: the convex hull round every wheel's rim, counter-clockwise."""
    pts = []
    for kind, y, z in wheels:
        r = RADIUS[kind]
        for i in range(samples):
            a = 2 * math.pi * i / samples
            pts.append((y + r * math.cos(a), z + r * math.sin(a)))
    return _hull(pts)


def links(wheels, pitch=LINK_PITCH):
    """Link poses along the belt: [(y, z, tangent_angle)], evenly spaced so the loop closes.

    The pitch is adjusted by a fraction of a millimetre so a whole number of links fits — a
    gap or an overlap at the seam is the first thing an eye finds on a track.
    """
    loop = belt(wheels)
    seg = []
    for a, b in zip(loop, loop[1:] + loop[:1]):
        seg.append((a, b, math.hypot(b[0] - a[0], b[1] - a[1])))
    total = sum(s[2] for s in seg)
    n = max(3, round(total / pitch))
    step = total / n
    out, d, i, acc = [], 0.0, 0, 0.0
    for k in range(n):
        target = k * step
        while acc + seg[i][2] < target:
            acc += seg[i][2]
            i += 1
        (ay, az), (by, bz), length = seg[i]
        t = (target - acc) / length
        out.append((ay + (by - ay) * t, az + (bz - az) * t, math.atan2(bz - az, by - ay)))
    return out, step, total
