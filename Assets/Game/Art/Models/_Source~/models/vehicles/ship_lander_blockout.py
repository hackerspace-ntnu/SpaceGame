"""Interior block-out for the lander rebuilt from the Tripo example hull.

The example (`models/example/futuristic+spacecraft+3d+model.fbx 3`) is a single
closed-ish shell with no interior, normalised to 1.0 unit long. This file
places box volumes for the rooms, doors, exterior cuts and keep-outs INSIDE a
scaled copy of that hull, so the hand-built replacement can be modelled around
volumes that are already proven to fit 3 m characters.

Everything below is authored in the example's normalised units (hull length
1.0, nose at -Y, ground at z = 0) and scaled once by SCALE at emit time, so a
different ship size is a one-number change. All box numbers were read off a
0.01-unit voxelisation of the hull (see `ship_lander_blockout_BUILD.md`).

    blender --background --python ship_lander_blockout.py -- \
        --out ship_lander_blockout.blend [--check grid.pkl]

`--check` samples every habitable box against a pickled voxel grid of the hull
and prints the fraction of samples that fall outside the fuselage; the
keep-outs and cuts are reported the other way round (how much hull they
contain), because those are the places the exterior has to change.
"""

import math
import os
import pickle
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
import _buildlib as B  # noqa: E402

# One normalised unit of the example hull, in metres. 30 puts the mid-body
# ceiling 4.8 m over the deck, the nose 3.6 m, and the ship at 30 x 22 x 11 m.
SCALE = 30.0
B.SCALE = SCALE

EXAMPLE_FBX = os.path.join(B.LIB_ROOT, "models", "example",
                           "futuristic+spacecraft+3d+model.fbx 3")

# Hull skin allowance kept between every room and the example surface.
SKIN = 0.01

# Deck levels (normalised). The cockpit sits on the low forward deck; the main
# room floor is raised over the mid-body underside, which rises aft.
DECK_FORE = 0.04
DECK_MAIN = 0.11

RAMP_ANGLE_DEG = 22.0
RAMP_HINGE_Y = 0.08

# name, lo(x,y,z), hi(x,y,z), material key, kind
#   room     habitable volume - must be entirely inside the hull
#   opening  door/hatch volume - marks where the wall is pierced
#   cut      exterior volume that has to be REMOVED from the example shape
#   keepout  exterior volume that has to stay EMPTY for a door to be usable
#   ref      reference geometry (3 m character)
LAYOUT = [
    ("Room_Cockpit_Nose",   (-0.08, -0.41, DECK_FORE), (0.08, -0.26, 0.16), "room", "room"),
    ("Room_Cockpit_Bridge", (-0.10, -0.26, DECK_FORE), (0.10, -0.14, 0.18), "room", "room"),
    ("Room_Main_Fore",      (-0.12, -0.14, DECK_MAIN), (0.12, -0.06, 0.25), "room", "room"),
    ("Room_Main_Aft",       (-0.12, -0.06, DECK_MAIN), (0.12,  0.08, 0.27), "room", "room"),

    ("Steps_Bridge_To_Main", (-0.03, -0.21, DECK_FORE), (0.03, -0.14, DECK_MAIN), "opening", "opening"),
    ("Door_Bulkhead",        (-0.03, -0.145, DECK_MAIN), (0.03, -0.135, 0.24), "opening", "opening"),
    ("Door_Side_Sliding",    (0.12, -0.10, DECK_MAIN), (0.135, 0.00, 0.23), "opening", "opening"),
    ("Door_Side_Pocket",     (0.125, 0.00, DECK_MAIN), (0.15, 0.10, 0.23), "opening", "opening"),
    ("Door_Rear_Baggage",    (-0.09, 0.075, DECK_MAIN), (0.09, 0.085, 0.25), "opening", "opening"),

    ("Cut_RampBay",          (-0.10, 0.08, 0.08), (0.10, 0.27, 0.27), "cut", "cut"),
    ("Keepout_RampHeadroom", (-0.10, 0.27, 0.0), (0.10, 0.36, 0.17), "keepout", "keepout"),
    ("Keepout_SideDoorApproach", (0.135, -0.10, 0.0), (0.22, 0.00, 0.23), "keepout", "keepout"),
]

# 3 m tall characters, 0.9 m across the shoulders, 0.6 m deep. Scaled to
# normalised units so they ride SCALE like everything else.
CHAR = Vector((0.9, 0.6, 3.0)) / SCALE
CHARACTERS = [
    ("Ref_Character_Main",    (0.0, -0.02, DECK_MAIN)),
    ("Ref_Character_Cockpit", (0.0, -0.34, DECK_FORE)),
    ("Ref_Character_Ramp",    (0.0, 0.30, 0.0)),
]

MATERIALS = {
    "room":    "Mat_Emissive_Green_CRT",
    "opening": "Mat_Emissive_Amber",
    "cut":     "Mat_Emissive_Red_Warn",
    "keepout": "Mat_Emissive_Portal_Orange",
    "ramp":    "Mat_Emissive_Cabin_Warm",
    "ref":     "Mat_Fabric_Flag_Bleached",
    "hull":    "Mat_Glass_Canopy_Tinted",
}


def ramp_box():
    """The lowered baggage ramp: hinged at the rear door sill, laid down until
    it meets the ground. Returns (center, size, rotation) in normalised units."""
    a = math.radians(RAMP_ANGLE_DEG)
    length = DECK_MAIN / math.sin(a)
    center = Vector((0.0, RAMP_HINGE_Y + 0.5 * length * math.cos(a),
                     DECK_MAIN - 0.5 * length * math.sin(a)))
    rot = Matrix.Rotation(a, 4, 'X')
    return center, Vector((0.18, length, 0.01)), rot, length


def build(out_path):
    B.start(out_path)
    mats = B.link_materials(list(MATERIALS.values()))
    by_key = dict(zip(MATERIALS.keys(), mats))

    coll_ref = B.collection("Coll_Ref_ExampleHull")
    coll_rooms = B.collection("Coll_Blockout_Rooms")
    coll_open = B.collection("Coll_Blockout_Openings")
    coll_ext = B.collection("Coll_Blockout_ExteriorChanges")
    coll_char = B.collection("Coll_Ref_Characters")
    colls = {"room": coll_rooms, "opening": coll_open,
             "cut": coll_ext, "keepout": coll_ext}

    for name, lo, hi, mkey, kind in LAYOUT:
        part = B.Part([by_key[mkey]])
        part.slab(lo, hi)
        obj = part.finish(name, colls[kind])
        obj.display_type = 'WIRE' if kind in ("cut", "keepout") else 'SOLID'
        obj.color = (*by_key[mkey].diffuse_color[:3], 1.0)

    center, size, rot, length = ramp_box()
    part = B.Part([by_key["ramp"]])
    part.box(center, size, rot=rot)
    part.finish("Ramp_Baggage_Lowered", coll_open)

    for name, foot in CHARACTERS:
        part = B.Part([by_key["ref"]])
        lo = Vector(foot) - Vector((CHAR.x / 2, CHAR.y / 2, 0.0))
        part.slab(lo, lo + CHAR)
        part.finish(name, coll_char)

    # Reference hull: the example scaled up, wireframe, on its own collection
    # so it can be hidden or swapped for the hand-built hull later.
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=EXAMPLE_FBX)
    hull = [o for o in bpy.data.objects if o not in before and o.type == 'MESH'][0]
    for c in list(hull.users_collection):
        c.objects.unlink(hull)
    coll_ref.objects.link(hull)
    hull.name = "Ref_ExampleHull"
    hull.data.name = "Ref_ExampleHull"
    hull.data.transform(Matrix.Diagonal((SCALE, SCALE, SCALE, 1.0)))
    hull.data.materials.clear()
    hull.data.materials.append(by_key["hull"])
    hull.display_type = 'WIRE'
    for m in list(bpy.data.materials):
        if m.name.startswith("tripo_"):
            bpy.data.materials.remove(m)

    B.save(out_path)
    report(length)


def report(ramp_length):
    print("\nDIMENSIONS at SCALE = %.0f (metres)" % SCALE)
    print("%-26s %8s %8s %8s   %s" % ("volume", "width", "length", "height", "y-range / deck"))
    for name, lo, hi, mkey, kind in LAYOUT:
        w, l, h = [(hi[i] - lo[i]) * SCALE for i in range(3)]
        print("%-26s %8.2f %8.2f %8.2f   y %+.1f..%+.1f  floor z %.2f"
              % (name, w, l, h, lo[1] * SCALE, hi[1] * SCALE, lo[2] * SCALE))
    print("%-26s ramp %.2f m long at %.0f deg, sill %.2f m above ground, foot at y %+.2f"
          % ("Ramp_Baggage_Lowered", ramp_length * SCALE, RAMP_ANGLE_DEG,
             DECK_MAIN * SCALE, (RAMP_HINGE_Y + ramp_length * math.cos(math.radians(RAMP_ANGLE_DEG))) * SCALE))


def check(grid_path):
    with open(grid_path, "rb") as f:
        g = pickle.load(f)
    grid, R = g["grid"], g["R"]

    def inside(x, y, z):
        key = (round(round(x / R) * R, 3), round(round(y / R) * R, 3), round(round(z / R) * R, 3))
        return grid.get(key, False)

    def samples(lo, hi, step=0.01):
        pts = []
        def axis(a, b):
            n = max(1, int(round((b - a) / step)))
            return [a + (b - a) * (i + 0.5) / n for i in range(n)]
        for x in axis(lo[0], hi[0]):
            for y in axis(lo[1], hi[1]):
                for z in axis(lo[2], hi[2]):
                    pts.append((x, y, z))
        return pts

    print("\nCHECK against %s" % grid_path)
    for name, lo, hi, mkey, kind in LAYOUT:
        pts = samples(lo, hi)
        ins = sum(1 for p in pts if inside(*p))
        frac = ins / len(pts)
        if kind in ("room", "opening"):
            bad = [p for p in pts if not inside(*p)]
            ys = sorted(set(round(p[1], 2) for p in bad))
            zs = sorted(set(round(p[2], 2) for p in bad))
            print("%-26s inside hull %5.1f%%  outside at y=%s z=%s" % (name, 100 * frac,
                  ys[:8] if ys else "-", zs[:8] if zs else "-"))
        else:
            print("%-26s hull occupies %5.1f%% of this %s volume" % (name, 100 * frac, kind))


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--check" in argv:
        check(argv[argv.index("--check") + 1])
    else:
        build(B.parse_out())
