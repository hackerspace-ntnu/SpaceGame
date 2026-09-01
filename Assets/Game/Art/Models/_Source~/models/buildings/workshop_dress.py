"""models/buildings/workshop_dress — dress the hand-built workshop tank into a settlement.

This is **not** a generator for `workshop.blend`. The tank and its annex were
modelled by hand and are the source of truth; this script opens that file and
*adds* to it. It is additive by construction:

- it never deletes, moves or reshapes existing geometry,
- everything it creates is uniquely named with a placement prefix,
- the only pre-existing things it touches are the two objects' **names** and
  **material slots**, which was explicitly authorised (`Cube` -> the tank body,
  `Cylinder` -> ... see `RENAMES` below — the default names were the wrong way
  round from what they model).

Components are **appended as loose objects, not linked as collection
instances.** An instance would be one un-editable empty per building; the
requirement here was that every wall, pole, plank and awning stay individually
selectable, so each placement brings in real objects and renames them under its
own prefix. That is why the file ends up with a few hundred objects.

Run it against the file, not over it:

    blender --background workshop.blend --python workshop_dress.py -- --save

Without `--save` it does everything and exits without writing, which is how to
check a layout change before committing it to the file.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))          # .../_Source~
COMP = os.path.join(LIB, "components")

# --- the site, measured off the hand-built geometry -------------------------
# Both existing objects bottom out at z = -0.858, so that is the ground plane.
# The barrel is centred on the world Z axis; the bounding box reaches x = 3.14
# only because of a base lug, not because the tank is off-centre.
GROUND = -0.858
TANK_R = 2.66             # barrel radius at ground level
RIM_HI = 6.05             # world z of the upper flange the growth spills off
RIM_LO = 4.59             # world z of the lower flange
CONE_TOP = 7.69           # world z of the conical roof's apex
CONE_R = 2.70             # barrel radius where that cone springs from RIM_HI


def cone_radius(z):
    """Radius of the hand-modelled conical roof at world height `z`.

    Anything sitting on that roof has to be placed off this rather than off a
    guessed radius: at 6.45 m the cone is already down to 2.0 m, so a clump
    dropped at the barrel's 2.7 m hangs in mid air.
    """
    return CONE_R * max(0.0, (CONE_TOP - z)) / (CONE_TOP - RIM_HI)

RENAMES = {
    # The default primitive names are misleading: the *Cylinder* is the tank
    # and the *Cube* is the annex shed leaning on its west side.
    "Cylinder": ("Mesh_Workshop_TankBody", "Mat_Paint_White_Arctic"),
    "Cube": ("Mesh_Workshop_Annex", "Mat_Paint_Blue_Station"),
}

SOURCES = {
    "cottage": os.path.join(COMP, "structural", "cottage_shell.blend"),
    "scaffold": os.path.join(COMP, "structural", "scaffold_bay.blend"),
    "awning": os.path.join(COMP, "structural", "facade_awning.blend"),
    "shade": os.path.join(COMP, "structural", "awning_shade.blend"),
    "paint": os.path.join(COMP, "props", "paint_station.blend"),
    "vine": os.path.join(COMP, "organic", "vine_drape.blend"),
    "crate": os.path.join(COMP, "props", "supply_crate.blend"),
    "barrel": os.path.join(COMP, "props", "fuel_barrel.blend"),
    "bench": os.path.join(COMP, "props", "field_bench.blend"),
}


# ---------------------------------------------------------------------------
# Placement maths
# ---------------------------------------------------------------------------

def xf(x, y, z=GROUND, yaw=0.0):
    return Matrix.Translation((x, y, z)) @ Matrix.Rotation(yaw, 4, 'Z')


def face_origin(x, y):
    """Yaw that turns a component's local -Y (its front) toward the tank.

    R_z(t) maps (0,-1,0) to (sin t, -cos t), so pointing that at the origin
    means sin t = -x, cos t = y once normalised.
    """
    return math.atan2(-x, y)


def on_tank(bearing, radius=None, z=GROUND):
    """Place a wall-mounted component against the barrel at `bearing`.

    Mount convention is `shanty_addon`'s: the mounting face is the plane x = 0
    and the thing projects into +X, so the yaw is the bearing itself and the
    radius is pulled in slightly to bury the wall plate in the curved skin.
    """
    r = TANK_R - 0.16 if radius is None else radius
    return xf(r * math.cos(bearing), r * math.sin(bearing), z, bearing)


def on_wall(host_xy, host_yaw, host_z, along=0.0, depth=1.50, z=0.0):
    """Place a wall-mounted component on a host's front (local -Y) wall.

    The awning's +X has to end up pointing along the host's -Y, which is a
    quarter turn back from the host's own yaw. Its width then runs along the
    host's X, i.e. along the wall, which is what makes `along` mean what it
    says.
    """
    yaw = host_yaw - math.pi / 2.0
    offset = Matrix.Rotation(host_yaw, 4, 'Z') @ Vector((along, -depth, 0.0))
    return xf(host_xy[0] + offset.x, host_xy[1] + offset.y, host_z + z, yaw)


# ---------------------------------------------------------------------------
# Appending
# ---------------------------------------------------------------------------

def group(name):
    coll = bpy.data.collections.get(name)
    if coll is None:
        coll = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(coll)
    return coll


def tag_of(name):
    parts = name.split("_")
    return parts[1] if len(parts) > 1 else ""


def rename(original, prefix, base):
    """`Mesh_CottageGable_WallFront` under prefix `HouseA` -> `HouseA_WallFront`.

    The variation tag is dropped because the prefix already says which building
    this is, and the trailing part is what a person is looking for in the
    outliner.

    `base` is the tag shared by most of the collection, and it exists because
    dropping the tag outright is lossy: `Coll_Cottage_Shed` holds both
    `Mesh_CottageShed_Door` and `Mesh_CottageShedUpper_Door`, which both
    collapse to `Door` and leave Blender to invent a `.001`. Whatever the tag
    carries beyond `base` is kept, so those become `Door` and `Upper_Door`.
    """
    parts = original.split("_")
    tag = tag_of(original)
    rest = "_".join(parts[2:]) if len(parts) > 2 else (tag or original)
    extra = tag[len(base):] if base and tag.startswith(base) and tag != base else ""
    tail = "%s_%s" % (extra, rest) if extra else rest
    return "%s_%s" % (prefix, tail)


def relink_palette():
    """Swap appended local materials for links back to `palette.blend`.

    Appending a collection makes everything local, materials included, which
    would leave this model holding private copies of palette colours — the one
    thing the shared palette exists to prevent. The local names have to be
    parked first, or the incoming linked datablocks arrive as `.001`.
    """
    pal = os.path.join(LIB, "palette.blend")
    locals_ = [m for m in bpy.data.materials if m.library is None]
    wanted = [m.name for m in locals_]
    for m in locals_:
        m.name = "_local_" + m.name

    with bpy.data.libraries.load(pal, link=True) as (src, dst):
        dst.materials = [n for n in wanted if n in src.materials]

    linked = {m.name: m for m in bpy.data.materials if m.library}
    swapped = 0
    for m in locals_:
        plain = m.name[len("_local_"):]
        target = linked.get(plain)
        if target is None:
            m.name = plain          # not a palette colour; leave it alone
            continue
        m.user_remap(target)
        bpy.data.materials.remove(m)
        swapped += 1
    print("  relinked %d material(s) to the palette" % swapped)


def place(source, variation, prefix, matrix, target):
    """Append one variation's objects, transform them, and file them away."""
    path = SOURCES[source]
    if not os.path.exists(path):
        raise SystemExit("Missing component source: %s" % path)

    with bpy.data.libraries.load(path, link=False) as (src, dst):
        if variation not in src.collections:
            raise SystemExit("%s has no collection %r (has: %s)"
                             % (os.path.basename(path), variation,
                                ", ".join(sorted(src.collections))))
        dst.collections = [variation]

    appended = dst.collections[0]
    objects = list(appended.all_objects)
    tags = [tag_of(o.name) for o in objects]
    base = max(set(tags), key=tags.count) if tags else ""
    for ob in objects:
        ob.name = rename(ob.name, prefix, base)
        if ob.data is not None:
            ob.data.name = ob.name
        ob.matrix_world = matrix @ ob.matrix_world
        target.objects.link(ob)
    # Drop the collection datablock itself: we keep the objects loose so every
    # one of them stays individually selectable.
    bpy.data.collections.remove(appended)
    return objects


# ---------------------------------------------------------------------------
# The site plan
# ---------------------------------------------------------------------------

# Four houses ringing the tank at 8-9 m, each turned to face it. They sat at
# 6 m first and it was wrong: the houses crowded the barrel and the tank
# stopped reading as the thing the settlement is built around. The extra two
# metres buy a yard, and a yard is what makes the tank the centrepiece.
#   id, cottage variation, (x, y), scaffold variation, lift, extra yaw
HOUSES = [
    ("HouseA", "Coll_Cottage_Gable",      (7.60, -4.60),
     "Coll_Scaffold_Undercroft", 1.00, 0.0),
    ("HouseB", "Coll_Cottage_Shed",       (-1.80, 8.10),
     "Coll_Scaffold_Stilts",     1.55, math.radians(-12)),
    ("HouseC", "Coll_Cottage_Corner",     (-8.30, -4.30),
     "Coll_Scaffold_Undercroft", 1.00, math.radians(-38)),
    ("HouseD", "Coll_Cottage_Glasshouse", (7.10, 5.20),
     "Coll_Scaffold_Undercroft", 1.00, math.radians(14)),
]


def dress():
    scene = bpy.context.scene

    # --- authorised edits to the hand-built geometry ------------------------
    for old, (new, mat_name) in RENAMES.items():
        ob = bpy.data.objects.get(old)
        if ob is None:
            print("  note: %r not present, skipping rename" % old)
            continue
        ob.name = new
        ob.data.name = new
        mat = bpy.data.materials.get(mat_name)
        if mat is None:
            with bpy.data.libraries.load(os.path.join(LIB, "palette.blend"),
                                         link=True) as (src, dst):
                if mat_name not in src.materials:
                    raise SystemExit("Not in the palette: %s" % mat_name)
                dst.materials = [mat_name]
            mat = bpy.data.materials[mat_name]
        # Geometry is untouched: only what the existing slots point at changes.
        for slot in ob.material_slots:
            slot.material = mat
        print("  renamed %-9s -> %-26s (%s)" % (old, new, mat_name))

    houses = group("Workshop_Houses")
    scaffold = group("Workshop_Scaffold")
    awnings = group("Workshop_Awnings")
    yard = group("Workshop_Yard")
    growth = group("Workshop_Growth")

    # --- houses, each on its own scaffolding --------------------------------
    placed = {}
    for tag, variation, (x, y), scaff, lift, extra in HOUSES:
        yaw = face_origin(x, y) + extra
        place("scaffold", scaff, tag + "Base", xf(x, y, GROUND, yaw), scaffold)
        place("cottage", variation, tag, xf(x, y, GROUND + lift, yaw), houses)
        placed[tag] = ((x, y), yaw, GROUND + lift)
        print("  %s  %-26s yaw=%6.1f deg  lift=%.2f"
              % (tag, variation, math.degrees(yaw), lift))

    # --- cloth: on the houses, and on the tank ------------------------------
    # The two pole-less variations go on house facades, where there is nothing
    # for a leg to stand on above the scaffold deck. The ground-supported ones
    # go against the barrel, which meets the ground everywhere.
    for tag, variation, along in (("HouseA", "Coll_FacadeAwning_Shop", 0.0),
                                  ("HouseD", "Coll_FacadeAwning_Shop", 0.10),
                                  ("HouseC", "Coll_FacadeAwning_Sail", -0.30)):
        (x, y), yaw, z = placed[tag]
        place("awning", variation, tag + "Awning",
              on_wall((x, y), yaw, z, along=along), awnings)

    for variation, bearing in (("Coll_FacadeAwning_PolePorch", math.radians(-108)),
                               ("Coll_FacadeAwning_Stall", math.radians(-24)),
                               ("Coll_FacadeAwning_Strip", math.radians(78))):
        # The Strip fixes 3.30 m up, which on this tank is a recessed band
        # about half a metre narrower than the skirt the other two sit on,
        # so it has to be pulled in or its wall plate floats.
        r = 2.15 if variation.endswith("Strip") else None
        place("awning", variation, "Tank" + variation.split("_")[-1],
              on_tank(bearing, radius=r), awnings)

    # One big free-standing shade over the yard, reused from the library rather
    # than rebuilt at this scale — `facade_awning` deliberately does not cover
    # spans this wide.
    place("shade", "Coll_Awning_Sagging", "YardShade",
          xf(0.40, -8.10, GROUND, math.radians(8)), awnings)

    # --- scaffolding climbing the tank --------------------------------------
    # Tangential, so it wraps the barrel rather than pointing at it.
    for variation, bearing, prefix in (
            ("Coll_Scaffold_Bay_Double", math.radians(208), "TankScaffold"),
            ("Coll_Scaffold_Bay_Single", math.radians(150), "TankScaffoldB")):
        r = TANK_R + 0.72
        place("scaffold", variation, prefix,
              xf(r * math.cos(bearing), r * math.sin(bearing), GROUND,
                 bearing + math.pi / 2.0), scaffold)

    ladder_b = math.radians(178)
    place("scaffold", "Coll_Scaffold_Ladder", "TankLadder",
          xf((TANK_R + 1.95) * math.cos(ladder_b),
             (TANK_R + 1.95) * math.sin(ladder_b), GROUND,
             ladder_b + math.pi), scaffold)

    # --- the yard -----------------------------------------------------------
    for variation, x, y, deg in (
            ("Coll_PaintStation_Trestle", 3.40, -6.05, 18),
            ("Coll_PaintStation_PotStack", 1.60, -6.60, -40),
            ("Coll_PaintStation_SwatchBoard", -2.45, -6.20, 152),
            ("Coll_PaintStation_DripSheet", 5.00, -7.00, 26),
            ("Coll_PaintStation_SprayRig", -4.35, 4.65, -66)):
        place("paint", variation, "Yard" + variation.split("_")[-1],
              xf(x, y, GROUND, math.radians(deg)), yard)

    for source, variation, x, y, deg in (
            ("crate", "Coll_Crate_Stack", -4.65, -6.55, 24),
            ("crate", "Coll_Crate_Pallet", 2.95, 6.35, -18),
            ("crate", "Coll_Crate_Open", -6.15, 2.35, 62),
            ("barrel", "Coll_Barrel_Drum", 5.35, 1.30, 0),
            ("barrel", "Coll_Barrel_Jerrican", -3.35, -7.15, -34),
            ("bench", "Coll_FieldBench_Sawhorse", 0.80, -7.45, 8),
            ("bench", "Coll_FieldBench_ToolRack", -6.75, 0.60, 96)):
        place(source, variation, "Yard" + variation.split("_")[-1],
              xf(x, y, GROUND, math.radians(deg)), yard)

    # --- growth -------------------------------------------------------------
    # Drapes hang from the two flanges. Their local +Y has to point outward, so
    # the yaw is a quarter turn *back* from the bearing, not forward.
    for bearing_deg, z, variation in ((28, RIM_HI, "Coll_Vine_DrapeLong"),
                                      (96, RIM_HI, "Coll_Vine_DrapeShort"),
                                      (168, RIM_HI, "Coll_Vine_DrapeLong"),
                                      (250, RIM_HI, "Coll_Vine_DrapeShort"),
                                      (312, RIM_HI, "Coll_Vine_DrapeLong"),
                                      (58, RIM_LO, "Coll_Vine_DrapeShort"),
                                      (200, RIM_LO, "Coll_Vine_DrapeShort"),
                                      (286, RIM_LO, "Coll_Vine_DrapeLong")):
        b = math.radians(bearing_deg)
        r = 2.62
        place("vine", variation, "Growth%03d_%s" % (bearing_deg,
                                                    variation.split("_")[-1]),
              xf(r * math.cos(b), r * math.sin(b), z, b - math.pi / 2.0), growth)

    # Tufts on the cone, which no flat mat would sit on, plus three on the
    # ground. Each is set at 85% of the cone's radius at its own height and
    # dropped 0.14 m, so it beds into the slope instead of perching on it.
    for bearing_deg, z in ((12, 6.45), (128, 6.95), (232, 6.25), (300, 7.15)):
        b = math.radians(bearing_deg)
        r = cone_radius(z) * 0.85
        place("vine", "Coll_Vine_Tuft", "GrowthCone%03d" % bearing_deg,
              xf(r * math.cos(b), r * math.sin(b), z - 0.14, b), growth)

    for x, y, deg in ((-4.75, -2.25, 40), (4.20, 2.95, -120), (-1.05, 5.35, 200)):
        place("vine", "Coll_Vine_Tuft", "GrowthGround%d" % abs(deg),
              xf(x, y, GROUND, math.radians(deg)), growth)

    # Planters on the two houses with somewhere to put one.
    (bx, by), byaw, bz = placed["HouseB"]
    place("vine", "Coll_Vine_Planter", "HouseBPlanter",
          on_wall((bx, by), byaw, bz, along=0.55, depth=1.44, z=2.24), growth)
    (dx, dy), dyaw, dz = placed["HouseD"]
    place("vine", "Coll_Vine_Planter", "HouseDPlanter",
          on_wall((dx, dy), dyaw, dz, along=-0.70, depth=1.62, z=0.34), growth)

    relink_palette()

    n = len([o for o in scene.collection.all_objects if o.type == 'MESH'])
    print("  scene now holds %d mesh objects" % n)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    dress()
    if "--save" in argv:
        bpy.ops.wm.save_mainfile()
        print("Saved %s" % bpy.data.filepath)
    else:
        print("Dry run — nothing written. Pass --save to commit.")


if __name__ == "__main__":
    main()
