"""components/props/expedition_rig_scale — the 2026-09-01 uniform enlargement.

Third and last step of the rig pipeline, run IN PLACE on a generated-and-dressed
file:

    blender -b expedition_rig.blend --python expedition_rig.py -- --out <new>
    blender -b <new> --python expedition_rig_dress.py
    blender -b <new> --python expedition_rig_scale.py

Why this is a step of its own, and not a `SCALE` sprinkled through the generator
-------------------------------------------------------------------------------
`expedition_rig.py` is 1400 lines of measurements — hinge offsets, cloth
thicknesses, cord radii, net sag, grommet pitch — and every one of them is
justified against a neighbouring one in a comment. Multiplying them individually
would mean editing several hundred numbers and invalidating every comment that
relates two of them, to express a transform that is a single factor. Worse, the
next person to add a part would have to remember to multiply theirs.

So the generator keeps authoring in the frame its own prose describes, and the
enlargement is applied afterwards as what it actually is: **one similarity
transform of the finished model**. Rotations, parenting, material assignment,
face indices, object names and object *scales* all come out untouched; only
lengths move, and all of them by the same factor. That is also why it can be
verified in one line — every bound, every location and every edge length in the
result is exactly `SCALE` times the one before it.

What the enlargement is for
-------------------------------------------------------------------------------
Unity's side of the pack — `PackGrid.Cell`, `ExpeditionRigWiring.SurfaceTable`,
`InventoryWallBuilder.SurfaceSize` and the size every item is drawn at on the mat
— was enlarged by the same factor in the same change (`PackScale.Factor`). Cell
COUNTS did not move: the rig still carries 255 cells, every authored `PackShape`
mask is still valid, and capacity is exactly what it was. What changed is how
much of the screen the pack fills in focus mode, which is the whole point — the
gear is easier to tell apart and easier to aim at.

The webbing ladder is the reason the model has to come along rather than being
left at its old size. Its rungs ARE the grid: `PackGrid`'s cell is one rung. Leave
the model at 1x under a 1.5x grid and the stitching, the grommet field and the
lash rail all stop lining up with the cells the player is dropping gear onto, and
the mat rectangle grows past the physical board so gear lands over sand.

Composes; refuses only to repeat itself
-------------------------------------------------------------------------------
`SCALE` is the scale the shipped model is meant to be at, cumulatively, and
`scene["rig_scale"]` records the scale it is at now. The pass applies the
RESIDUAL between them and re-stamps, so changing `SCALE` later resizes the rig by
the difference and running the same script twice does nothing at all. It refuses
only when the file already carries `SCALE`.

The first version refused any stamped file outright. That was the right guard and
the wrong shape: a second unconditional pass would leave a 2.25x rig that no
number on Unity's side agrees with, and nothing downstream would report it — the
pack would simply be enormous. But so would re-typing `SCALE` under a refusal
that has to be commented out to get past, which is what a shrink needs. Composing
keeps the protection and removes the temptation, and is what
`inventory_wall_scale.py` already did.
"""

import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

# The generator's own surface table, so this script can print the rectangles in
# the frame Unity has to carry. Imported rather than re-typed: the whole point of
# the printout is that it is derived from the same source the model is.
from expedition_rig import SURFACES  # noqa: E402

# Must equal `PackScale.Factor` in
# Assets/Game/Scripts/Items/Backpack/Placement/PackScale.cs. There is no way to
# share the number across the two languages, so it is stated in both places and
# `PackSurfaceTests.SurfaceTable_MatchesTheRigsCellCounts` is what notices when
# they drift: a mismatch shows up as surface rectangles that are no longer whole
# multiples of the cell.
SCALE = 1.05

# Two scales are "the same" if they agree to this. The residual below is a
# quotient of decimal literals neither language can represent exactly, so an
# equality test on it would make the refusal depend on the last bit.
EPSILON = 1e-6

STAMP = "rig_scale"


def scale_world(factor):
    """Multiply every length in the file by `factor`, about the world origin.

    Mesh data is scaled in place and object *locations* are multiplied, so every
    object comes out identity-scaled — which `expedition_rig.dump_surfaces`
    asserts of the `SURF_` empties, and which Unity needs of them anyway, since a
    scaled empty would rescale every item parented under it.

    Valid only because `attach()` leaves `matrix_parent_inverse` at identity and
    every parent is an unrotated translation: with those two, a child's world
    position is `parent_world + R * local`, so multiplying every local location
    by `factor` multiplies every world position by it too, recursively.
    """
    matrix = Matrix.Scale(factor, 4)
    scaled_meshes = set()

    for obj in bpy.data.objects:
        # Meshes can in principle be shared between objects; transforming one
        # twice would cube the scale on it alone, which is the kind of thing
        # that looks like a modelling mistake rather than a script one.
        if obj.type == 'MESH' and obj.data is not None:
            if obj.data.name not in scaled_meshes:
                obj.data.transform(matrix)
                scaled_meshes.add(obj.data.name)

        obj.location = Vector(obj.location) * factor

        if obj.type == 'EMPTY':
            # So the arrows still read as a marker on the part they mark rather
            # than as a speck. Display only; nothing downstream reads it.
            obj.empty_display_size *= factor

    return len(scaled_meshes)


def report():
    print("expedition_rig_scale  cumulative x%.3f" % SCALE)
    print("  --- surface rectangles, in the frame Unity carries ---")
    print("      these must equal ExpeditionRigWiring.SurfaceTable exactly")

    for name, _parent, _loc, _rot, w, d in SURFACES:
        obj = bpy.data.objects.get(name)
        loc = obj.matrix_world.to_translation() if obj is not None else Vector()
        print("    %-15s %.3f x %.3f m   at (%6.3f,%6.3f,%6.3f)"
              % (name, w * SCALE, d * SCALE, loc.x, loc.y, loc.z))

    pts = [o.matrix_world @ Vector(c)
           for o in bpy.data.objects if o.type == 'MESH' for c in o.bound_box]
    lo = Vector((min(q.x for q in pts), min(q.y for q in pts), min(q.z for q in pts)))
    hi = Vector((max(q.x for q in pts), max(q.y for q in pts), max(q.z for q in pts)))
    print("  BOUNDS W=%.3f D=%.3f H=%.3f  (%.3f..%.3f, %.3f..%.3f, %.3f..%.3f)"
          % (hi.x - lo.x, hi.y - lo.y, hi.z - lo.z, lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))


def main():
    scene = bpy.context.scene

    applied = float(scene.get(STAMP, 1.0))

    if abs(applied - SCALE) <= EPSILON:
        raise SystemExit(
            "Already at x%.4f, which is SCALE. Nothing to do — a second pass at "
            "the same number leaves a rig no number on Unity's side agrees with, "
            "and nothing downstream would say so." % applied)

    residual = SCALE / applied
    print("expedition_rig_scale  x%.4f -> x%.4f  (residual x%.6f)"
          % (applied, SCALE, residual))

    meshes = scale_world(residual)
    scene[STAMP] = SCALE

    # `matrix_world` is cached and only recomputed when the dependency graph is
    # evaluated, so without this every position `report` prints is the one from
    # before the scale — a printout that says the enlargement did not happen
    # while the file on disk says it did.
    bpy.context.view_layer.update()

    report()

    # Identity-scaled objects are a hard requirement of the surface convention,
    # and the cheapest possible thing to get wrong here.
    bad = [o.name for o in bpy.data.objects
           if (Vector(o.scale) - Vector((1.0, 1.0, 1.0))).length > 1e-6]
    if bad:
        raise SystemExit("Objects left non-identity-scaled: " + ", ".join(bad))

    bpy.ops.wm.save_mainfile()
    print("  scaled %d meshes, %d objects; saved %s"
          % (meshes, len(bpy.data.objects), bpy.data.filepath))


main()
