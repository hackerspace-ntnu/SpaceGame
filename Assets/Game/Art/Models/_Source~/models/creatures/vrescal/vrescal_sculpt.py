"""Bring the image-to-3D Vrescal sculpt into the model library as a .blend.

    blender --background --python vrescal_sculpt.py -- \
        --src ~/Downloads/dinosaur+creature+3d+model/tripo_convert_*.fbx

Writes `vrescal_sculpt.blend`. This is a **one-shot import**, not a
re-runnable generator: once the .blend exists it is the source of truth and may
carry hand edits, so `save()` refuses to clobber it without `--overwrite`.

## What arrives, and what is wrong with it

The FBX is a single 50 k-triangle mesh with one Principled material and a
2048-square base-colour / normal / roughness / metallic set. No armature, no
vertex groups, no actions, no quads. Four things have to be fixed before it is
usable:

**It contains a human.** The conversion was run against the concept painting,
which has a scale silhouette standing beside the animal, and the reconstruction
dutifully rebuilt that too -- as a second loose shell 0.39 units tall at the
head end. It is deleted here.

**It is unit-scaled, not metre-scaled.** The creature arrives 0.857 units tall.

**The nose points along -X.** The rest of this creature's scripts -- and the
export that feeds Unity -- take +X as forward, so the mesh is turned 180
degrees about Z.

**Its textures live in ~/Downloads.** They are packed into the .blend, or the
file breaks the first time that folder is cleaned out.

## The scale figure is the calibration

Rather than guessing a scale factor, the human is measured before it is thrown
away: it is 0.390 units tall, and a concept-art scale figure is 1.75 m. That
fixes the factor at 4.49 and makes the animal 3.85 m at the front hump.

That number is worth trusting because it was arrived at twice, independently. A
reading of the concept art -- human silhouette 440 px against a 950 px animal,
so 1.75 m against 3.78 m -- had already put the front hump at 3.78 m before
this model existed. The two agree to within 2 %, so the animal really is a bit
under four metres at the shoulder-hump and a 1.75 m player cannot walk under
its belly.

## Materials

This is the one asset in the library that does **not** draw its colour from
`palette.blend`. Its entire appearance is in the baked 2048-square maps, and
swapping them for a flat palette colour would discard the model rather than
conform it. The material is renamed into the palette's scheme
(`Mat_Hide_Vrescal_Baked`) so it sorts with the other hide materials, and the
deviation is deliberate. Nothing else should copy it.
"""

import glob
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import anatomy as A          # noqa: E402

OUT = os.path.join(HERE, "vrescal_sculpt.blend")

# A concept-art scale figure. The single number the whole model's size hangs
# off; change it and everything downstream rescales proportionally.
HUMAN_H = 1.75

OBJ_NAME = "Mesh_Vrescal_Sculpt"
MAT_NAME = "Mat_Hide_Vrescal_Baked"
TEX_NAMES = {
    "basecolor": "Tex_Vrescal_BaseColor",
    "normal": "Tex_Vrescal_Normal",
    "roughness": "Tex_Vrescal_Roughness",
    "metallic": "Tex_Vrescal_Metallic",
    "rm": "Tex_Vrescal_RoughMetal",
}


def arg(flag, default=None):
    argv = A.parse_args()
    return argv[argv.index(flag) + 1] if flag in argv else default


def bounds(obj):
    """World extent, measured from vertices.

    Deliberately not `obj.bound_box`: that is cached against the last depsgraph
    evaluation, so straight after a `mesh.transform()` it still reports the
    pre-transform box. Reading it here made a correctly scaled 3.85 m animal
    report itself as 0.86 m tall, which is a convincing enough lie to send you
    debugging a transform that was never broken.
    """
    pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    return (Vector((min(p.x for p in pts), min(p.y for p in pts),
                    min(p.z for p in pts))),
            Vector((max(p.x for p in pts), max(p.y for p in pts),
                    max(p.z for p in pts))))


def underside(mesh, lo, hi, bins=14):
    """Lowest point along the centreline, per band down the body.

    A single "belly height" number is meaningless on a four-legged animal --
    sample anywhere near x = 0 and you measure a foot. This prints the whole
    underside profile so the belly can be read off between the limbs.
    """
    out = []
    for i in range(bins):
        a = A.lerp(lo.x, hi.x, i / float(bins))
        b = A.lerp(lo.x, hi.x, (i + 1) / float(bins))
        zs = [v.co.z for v in mesh.vertices
              if a <= v.co.x < b and abs(v.co.y) < 0.16]
        out.append((a, b, min(zs) if zs else None))
    return out


def tris(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def split_off_the_human():
    """Separate loose shells; return (creature, [discarded]).

    The creature is identified by triangle count rather than by name, because
    `separate` assigns `.001` suffixes in no guaranteed order.
    """
    obj = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.mesh.separate(type='LOOSE')
    shells = sorted((o for o in bpy.data.objects if o.type == 'MESH'),
                    key=tris, reverse=True)
    for o in bpy.data.objects:
        o.select_set(False)
    return shells[0], shells[1:]


def close_holes(obj):
    """Fill the reconstruction's pinholes so the shell is watertight.

    The import arrives with four boundary edges -- one hole a few triangles
    across, invisible in a render and a nuisance in everything else: it breaks
    solidify, boolean and volume operations, and it is exactly the sort of
    thing that surfaces three weeks later as a rigging artefact. Cheap to fix
    now, so it is fixed now rather than documented as a known issue.
    """
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    holes = [e for e in bm.edges if e.is_boundary]
    if holes:
        centre = Vector((0, 0, 0))
        for e in holes:
            centre += (e.verts[0].co + e.verts[1].co) * 0.5
        centre /= len(holes)
        print("  closing %d boundary edge(s) around (%.2f, %.2f, %.2f)"
              % (len(holes), *centre))
        bmesh.ops.holes_fill(bm, edges=holes, sides=8)
        bmesh.ops.triangulate(
            bm, faces=[f for f in bm.faces if len(f.verts) > 3])
        left = sum(1 for e in bm.edges if e.is_boundary)
        if left:
            raise SystemExit("%d boundary edges survived the fill" % left)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def main():
    src = arg("--src")
    if not src:
        hits = glob.glob(os.path.expanduser(
            "~/Downloads/dinosaur+creature+3d+model/*.fbx"))
        if len(hits) != 1:
            raise SystemExit("Pass --src <file.fbx>; found %d candidates"
                             % len(hits))
        src = hits[0]
    src = os.path.abspath(os.path.expanduser(src))

    A.start()
    bpy.ops.import_scene.fbx(filepath=src)
    creature, extra = split_off_the_human()

    # -- calibrate off the scale figure, then discard it -------------------
    if not extra:
        raise SystemExit("No scale figure found; cannot calibrate the size.")
    human = max(extra, key=lambda o: bounds(o)[1].z - bounds(o)[0].z)
    h_lo, h_hi = bounds(human)
    human_units = h_hi.z - h_lo.z
    scale = HUMAN_H / human_units
    print("  scale figure %.4f units tall -> %.2f m gives factor %.4f"
          % (human_units, HUMAN_H, scale))
    for o in extra:
        print("  discarding reconstruction artefact %s (%d tris, %.2f units tall)"
              % (o.name, tris(o), bounds(o)[1].z - bounds(o)[0].z))
        bpy.data.meshes.remove(o.data)

    c_lo, c_hi = bounds(creature)
    if not (2.0 < (c_hi.z - c_lo.z) * scale < 6.0):
        raise SystemExit(
            "Calibration gives an implausible %.2f m animal -- the scale "
            "figure was probably misidentified."
            % ((c_hi.z - c_lo.z) * scale))

    # -- orient, scale and seat on the ground ------------------------------
    #
    # Applied to the mesh data rather than to the object, so the object's
    # transform stays identity and no compensating scale reaches Unity.
    mesh = creature.data
    mesh.transform(Matrix.Rotation(3.14159265358979, 4, 'Z'))   # nose -> +X
    mesh.transform(Matrix.Diagonal((scale, scale, scale, 1.0)))

    # Origin between the feet at ground level: the centre of the footprint,
    # measured from the lowest tenth of the animal rather than from the whole
    # bounding box, which the humps and the head would drag off-centre.
    zs = [v.co.z for v in mesh.vertices]
    floor, ceiling = min(zs), max(zs)
    band = floor + (ceiling - floor) * 0.10
    feet = [v.co for v in mesh.vertices if v.co.z <= band]
    cx = (min(p.x for p in feet) + max(p.x for p in feet)) * 0.5
    cy = (min(p.y for p in feet) + max(p.y for p in feet)) * 0.5
    mesh.transform(Matrix.Translation((-cx, -cy, -floor)))

    close_holes(creature)

    creature.name = OBJ_NAME
    mesh.name = OBJ_NAME
    coll = A.collection("Coll_Vrescal_Sculpt")
    bpy.context.scene.collection.objects.unlink(creature)
    coll.objects.link(creature)

    # -- material and textures ---------------------------------------------
    for m in bpy.data.materials:
        m.name = MAT_NAME
    for img in bpy.data.images:
        for key, name in TEX_NAMES.items():
            if key in img.name.lower():
                img.name = name
                break
        img.pack()            # or the file dies with ~/Downloads

    # -- report -------------------------------------------------------------
    lo, hi = bounds(creature)
    print("\n  %s" % OBJ_NAME)
    print("  tris     %d" % tris(creature))
    print("  verts    %d" % len(mesh.vertices))
    print("  size     %.2f long x %.2f wide x %.2f tall (m)"
          % (hi.x - lo.x, hi.y - lo.y, hi.z - lo.z))
    print("  extent   x %.2f..%.2f   y %.2f..%.2f   z %.2f..%.2f"
          % (lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))
    print("  underside profile (centreline low point per band):")
    for a, b, z in underside(mesh, lo, hi):
        print("      x %6.2f..%6.2f   z %s"
              % (a, b, "%.2f" % z if z is not None else "-"))
    print("  textures %s" % [i.name for i in bpy.data.images])
    print("  packed   %s" % all(i.packed_file for i in bpy.data.images))
    A.save(OUT)


if __name__ == "__main__":
    main()
