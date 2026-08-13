"""components/mechanical/walker_leg — the mech leg, promoted into the library.

These legs are not new geometry. They were hand-built in
`Assets/Prefabs/agents/vehicle/walker_legs.blend`, which holds four of them in
collections `Leg_01`..`Leg_04` as 60-80 loose parts each, hung off a chain of
empties (`LEG_Root -> LEG_JNT_Hip -> LEG_JNT_Knee -> LEG_JNT_Ankle`). That file
is the art; this script imports it and never writes back to it.

What this adds on top of the source:

  * **One mesh per limb segment.** The parts are baked to world space and joined
    per joint, so a leg is three objects (Upper / Lower / Foot) instead of
    seventy. A walker needs six legs, and 400-odd loose transforms per machine
    is a cost paid every frame for nothing — the parts never move relative to
    their own joint.
  * **Palette materials.** The source carries its own `LEG_Hull` / `LEG_DarkMetal`
    / `LEG_Piston` / `LEG_Accent`, which is how a library ends up with eleven
    greys. They are remapped onto the shared palette on the way in.
  * **Origins on the joints.** Each segment's origin sits on the axle it rotates
    about, so an assembly can parent it straight to a bone and pose it.

The four variations differ in real linkage geometry, not decoration — the build
prints the segment lengths each one gives.

    blender --background --python walker_leg.py -- --out walker_leg.blend
"""

import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import REPO_ROOT, collection, link_materials, parse_out, report, save, start  # noqa: E402

REPO = REPO_ROOT
SOURCE = os.path.join(REPO, "Assets", "Prefabs", "agents", "vehicle",
                      "walker_legs.blend")

# source collection, variant name, prefix of that leg's empties
VARIANTS = [
    ("Leg_01", "Heavy", "LEG"),
    ("Leg_02", "Compact", "LEG2"),
    ("Leg_03", "Raised", "LEG3"),
    ("Leg_04", "Long", "LEG4"),
]

SEGMENTS = [("JNT_Hip", "Upper"), ("JNT_Knee", "Lower"), ("JNT_Ankle", "Foot")]

# The source's four painted metals, mapped onto the shared palette. LEG_Hull is
# the leg's body colour and is what Mat_Paint_Hull_Bleached was added for.
MATERIAL_MAP = {
    "LEG_Hull": "Mat_Paint_Hull_Bleached",
    "LEG_DarkMetal": "Mat_Metal_Steel_Dark",
    "LEG_Piston": "Mat_Metal_Chrome_Scuffed",
    "LEG_Accent": "Mat_Metal_Rust_Heavy",
}
# Everything else in the source is an unnamed default grey on a single part.
FALLBACK = "Mat_Metal_Steel_Worn"

PALETTE_ORDER = [
    "Mat_Paint_Hull_Bleached",
    "Mat_Metal_Steel_Dark",
    "Mat_Metal_Chrome_Scuffed",
    "Mat_Metal_Rust_Heavy",
    "Mat_Metal_Steel_Worn",
]


def append_source():
    """Bring the four leg collections in and put them in the view layer.

    They have to be in the layer: `matrix_world` and `evaluated_get` are both
    depsgraph-backed, and an appended object no scene references has neither.
    """
    names = [src for src, _, _ in VARIANTS]
    with bpy.data.libraries.load(SOURCE, link=False) as (src, dst):
        missing = [n for n in names if n not in set(src.collections)]
        if missing:
            raise SystemExit("%s has no collection(s) %s" % (SOURCE, missing))
        # A copy: Blender rewrites the list it is handed in place, swapping the
        # names for datablocks, and `names` is needed as names afterwards.
        dst.collections = list(names)
    for name in names:
        bpy.context.scene.collection.children.link(bpy.data.collections[name])
    bpy.context.view_layer.update()


def bake_segment(parts, palette, origin, name, coll):
    """Join `parts` into one mesh, in world space, pivoted on `origin`.

    Modifiers are applied first: the source uses BEVEL, and a bevel width
    re-evaluates against whatever scale it is baked into, so applying afterwards
    would quietly change the art.
    """
    index_of = {m.name: i for i, m in enumerate(palette)}
    depsgraph = bpy.context.evaluated_depsgraph_get()

    verts, faces, face_mats, face_smooth = [], [], [], []

    for part in parts:
        evaluated = part.evaluated_get(depsgraph)
        try:
            mesh = evaluated.to_mesh()
        except RuntimeError:
            continue                       # anything without geometry
        if mesh is None or not mesh.polygons:
            evaluated.to_mesh_clear()
            continue

        # Bake to world space, then onto the joint: the object transform is
        # discarded, so nothing depends on the source's parenting any more.
        world = Matrix.Translation(-origin) @ part.matrix_world
        base = len(verts)
        verts.extend([world @ v.co for v in mesh.vertices])

        slots = [(s.material.name if s.material else None)
                 for s in part.material_slots]
        for poly in mesh.polygons:
            faces.append([base + i for i in poly.vertices])
            source = (slots[poly.material_index]
                      if poly.material_index < len(slots) else None)
            face_mats.append(index_of[MATERIAL_MAP.get(source, FALLBACK)])
            face_smooth.append(poly.use_smooth)

        evaluated.to_mesh_clear()

    merged = bpy.data.meshes.new(name)
    merged.from_pydata([tuple(v) for v in verts], [], faces)
    merged.validate()
    for m in palette:
        merged.materials.append(m)
    # Carry the source's own shading through rather than re-deriving it by
    # angle: the artist already decided which barrels read as round and which
    # facets stay crisp, and an angle threshold would overrule that.
    for poly, index, smooth in zip(merged.polygons, face_mats, face_smooth):
        poly.material_index = index
        poly.use_smooth = smooth

    obj = bpy.data.objects.new(name, merged)
    obj.location = origin
    coll.objects.link(obj)
    return obj


def build():
    out = parse_out()
    start(out)
    palette = link_materials(PALETTE_ORDER)
    append_source()

    geometry = {}

    for source_name, variant, prefix in VARIANTS:
        coll = collection("Coll_WalkerLeg_%s" % variant)
        source = bpy.data.collections[source_name]

        root = bpy.data.objects["%s_Root" % prefix]
        root_world = root.matrix_world.translation.copy()

        joints = {}
        for joint_suffix, _ in SEGMENTS:
            joint = bpy.data.objects["%s_%s" % (prefix, joint_suffix)]
            joints[joint_suffix] = (joint.matrix_world.translation
                                    - root_world)

        # Every part hangs off exactly one joint empty; group them by which.
        by_joint = {suffix: [] for suffix, _ in SEGMENTS}
        for obj in source.all_objects:
            if obj.parent is None:
                continue
            suffix = obj.parent.name[len(prefix) + 1:]
            if suffix in by_joint:
                by_joint[suffix].append(obj)

        made = []
        for joint_suffix, segment in SEGMENTS:
            parts = by_joint[joint_suffix]
            if not parts:
                raise SystemExit("%s has no parts on %s"
                                 % (variant, joint_suffix))
            made.append(bake_segment(
                parts, palette, Vector(joints[joint_suffix]),
                "Mesh_WalkerLeg_%s_%s" % (variant, segment), coll))

        # The world-space bake left everything at the source's root offset; put
        # the whole variant back on the origin so a leg is authored in its own
        # space rather than wherever it happened to sit in the source file.
        for obj in made:
            obj.data.transform(Matrix.Translation(-root_world))

        # Measured off the vertices, not `bound_box`: bounds are cached and
        # stay stale after the mesh under them has just been rewritten, which
        # reports a sole one foot-length below where it actually is.
        sole = min((obj.location + v.co).z for obj in made
                   for v in obj.data.vertices)
        geometry[variant] = {
            "hip": Vector(joints["JNT_Hip"]),
            "knee": Vector(joints["JNT_Knee"]),
            "ankle": Vector(joints["JNT_Ankle"]),
            "sole": sole,
        }

    # Discard the imported source once everything is baked out of it.
    for source_name, _, _ in VARIANTS:
        source = bpy.data.collections[source_name]
        for obj in list(source.all_objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.collections.remove(source)
    for mat in list(bpy.data.materials):
        if mat.library is None and mat.name not in PALETTE_ORDER:
            bpy.data.materials.remove(mat)

    print("\nLinkage geometry (leg-local, root at origin, +X is the knee side):")
    for variant, g in geometry.items():
        upper = (g["knee"] - g["hip"]).length
        lower = (g["ankle"] - g["knee"]).length
        foot = g["ankle"].z - g["sole"]
        print("  %-8s hip=(%.2f, %.2f, %.2f) knee=(%.2f, %.2f, %.2f) "
              "ankle=(%.2f, %.2f, %.2f)" % (variant, *g["hip"], *g["knee"],
                                            *g["ankle"]))
        print("           upper=%.2f lower=%.2f foot=%.2f reach=%.2f sole_z=%.2f"
              % (upper, lower, foot, upper + lower + foot, g["sole"]))

    report()
    save(out)


build()
