"""models/props/inventory_wall_scale — the 2026-09-01 uniform enlargement.

Second and last step of the wall pipeline, run IN PLACE on a generated file:

    blender -b --python inventory_wall.py -- --out <new>.blend
    blender -b <new>.blend --python inventory_wall_scale.py

Why this is a step of its own, and not a `SCALE` sprinkled through the generator
-------------------------------------------------------------------------------
The obvious route is to change `grid_panel.CELL` from 0.090 to 0.135 and let
every derived number follow. It is wrong here, and the audit that says so is
worth writing down so nobody re-proposes it.

Only the *positions* in these two files are derived from `CELL`. The *stock* is
not. `grid_panel.py` carries fifteen distinct lengths that are not multiples of
the cell — `FRAME_T` 0.030, `FRAME_D` 0.045, `FACE_T` 0.018, `TAPE_W` 0.024,
`TAPE_P` 0.008, the 0.011/0.016/0.008 corner bolt, the 0.014/0.005/0.020 eyelet,
the 0.017 pegboard boss, the 0.007 net cord — and `inventory_wall.py` carries
twenty-two more: `STILE_W` 0.120, `DEPTH` 0.180, `TRAY_D` 0.240, the 0.024 plate
thicknesses, the 0.014 rivets, the whole lamp housing, and both bevel widths.
Thirty-seven numbers that a change to `CELL` does not touch.

So route (a) is not a similarity transform at all. It would leave a 0.024 m
webbing tape and a 0.030 m frame stock wrapped around a bay that had grown from
0.54 to 0.81 m, and a 0.005 m bevel on a fitting half again as large. The wall
would not be bigger; it would be a differently-proportioned wall, and every
comment in both files relating one measurement to a neighbouring one would have
quietly become false.

Route (b) — this file — applies the enlargement afterwards as what it actually
is: **one similarity transform of the finished model**. Rotations, parenting,
material assignment, face indices, object names and object *scales* all come out
untouched; only lengths move, and all of them by the same factor. That is also
why it can be verified in one line — every bound and every location in the
result is exactly the applied factor times the one before it. It is the same route
`components/props/expedition_rig_scale.py` takes over the rig, for the same
reason, and the two models have to stay in the same frame as each other.

What the enlargement is for
-------------------------------------------------------------------------------
Unity's side of the physical inventory — `PackGrid.Cell`, `PackSurface`'s
rectangles, `InventoryWallBuilder.SurfaceSize` and the size every item is drawn
at — was enlarged by the same factor in the same change (`PackScale.Factor`).
This pass moves no COUNT: the bays are six cells wide before it and six cells
wide after it, every authored `PackShape` mask is still valid, and capacity is
exactly what the generator authored. What changes is how much of the room the
wall fills, and how big the gear on it reads from across the deck.

(The counts themselves were re-picked on 2026-09-01 — 30 x 22 = 660 cells over
five bays, down from 60 x 30 = 1800 over ten — because at 1.5x the old fitting
was 8.46 x 4.95 m and the lander's aft room, measured against the ship's baked
collision, offers 4.37 m of headroom over the fitting's footprint. That decision
lives in `inventory_wall.py`, which is where the counts are authored; this file
only multiplies.)

The model has to come along rather than being left at its old size, because the
decoration IS the grid. The bay dividers are exactly six cells apart; the webbing
tapes, the pegboard bosses and the net cords are all on `PITCH` = two cells.
Leave the model at 1x under a `TOTAL`x grid and every one of those lines stops
falling where the player is dropping gear — an item lands visibly between the
lines it is supposed to sit on — and the placement rectangle grows over a board
that did not, so a third of it hangs off the fitting into thin air.

Composes; refuses only to repeat itself
-------------------------------------------------------------------------------
`TOTAL` is the scale the shipped model is meant to be at, cumulatively, and
`scene["wall_scale"]` records the scale it is at now. The pass applies the
RESIDUAL between them and re-stamps, so raising `TOTAL` later enlarges the wall
by the difference and running the same script twice does nothing at all. It
refuses only when the file already carries `TOTAL`.

The earlier version refused any stamped file outright. That was the right guard
and the wrong shape: a second unconditional pass would leave a 2.25x wall that
no number on Unity's side agrees with, and nothing downstream would report it —
but so would re-typing `SCALE` under a refusal that has to be commented out to
get past. Composing keeps the protection and removes the temptation.

It composes DOWNWARD too, and has to. A second refusal used to reject any
`TOTAL` below the stamp — "shrinking is not what this pass is for" — which is
the same guard in the same wrong shape one step later: the only way past it was
to comment it out, and the file it was protecting is the one an over-scale has
to be taken back out of. That is exactly what 2026-09-02 needed, taking the wall
from a 1.8285 that stood through the arch rib back to 1.59. A residual under 1
divides the same hand-edited meshes the residual over 1 multiplied, so there is
nothing to protect that composing does not already protect.

Hand edits, and what the identity check is actually for
-------------------------------------------------------------------------------
This file is opened and edited by hand as well as generated into: it carries a
`Plane` backing panel and some reassigned materials that `inventory_wall.py`
knows nothing about, and two of its meshes are deliberately left at non-identity
object scale. So the pass touches mesh DATA and object LOCATIONS only, and never
object scales — a mesh scaled 1.7966 on Y before is scaled 1.7966 on Y after,
around geometry that has grown.

The identity check that follows is therefore about the EMPTIES and nothing else.
`SURF_WallGrid` must be identity-scaled, because Unity parents display copies
under it and a scaled empty would rescale every one of them; that is a hard
failure. A mesh at a non-identity scale is a modelling decision and is reported,
not refused — reported rather than dropped, because a mesh that picked one up by
accident looks exactly the same in this printout as one that meant it.
"""

import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

# The generator's own numbers, so this script can print the rectangle in the
# frame Unity has to carry. Imported rather than re-typed: the whole point of
# the printout is that it is derived from the same source the model is.
from inventory_wall import (  # noqa: E402
    BAYS, BAY_W, CELL, DEPTH, GRID_H, GRID_W, GRID_Z0, GRID_Z1, HEADER_Z1,
    STILE_W, STILE_X, TRAY_D,
)

# The two enlargements this model carries, and the product of them is what the
# file on disk is meant to measure. There is no way to share a number across the
# two languages, so each is stated in both places:
#
#   PACK   `PackScale.Factor` — the 2026-09-01 enlargement of the whole physical
#          inventory, cell and all. The pack's surface tests notice a drift: a
#          mismatch shows up as a surface rectangle that is no longer a whole
#          multiple of the cell.
#   WALL   the wall alone, drawn larger than it reasons. It moves NO count and
#          no cell: Unity's side applies the same number to the uv -> world
#          mapping only, so the board and the lattice gear is dropped onto grow
#          together while the 30 x 22 cells, the 4.05 x 2.97 m of stored uv and
#          every save byte stay exactly as they were.
#
# TOTAL is what this FILE is baked at, and since 2026-09-02 that is
# `PackScale.WallModel`, not `PackScale.WallDrawn`: the wall's on-screen size
# moved to 1.2x this (`WallDrawn = WallModel * 1.2`, the user's 20% enlargement)
# and the residual is a transform scale `InventoryWallBuilder` puts on the
# prefab root, precisely so this hand-edited file never has to be re-run for a
# resize. Keep TOTAL equal to `PackScale.WallModel`; re-run this pass only when
# the BAKED scale itself is meant to change, and change `WallModel` with it.
TOTAL = 1.59
PACK_SCALE = 1.05
WALL_DISPLAY = TOTAL / PACK_SCALE

# Two scales are "the same" if they agree to this. `TOTAL` is a product of
# decimal literals neither language can represent exactly, so an equality test
# on it would make the refusal below depend on the last bit.
EPSILON = 1e-6

STAMP = "wall_scale"


def scale_world(factor):
    """Multiply every length in the file by `factor`, about the world origin.

    Mesh data is scaled in place and object *locations* are multiplied, so every
    object comes out identity-scaled — which `SURF_WallGrid` needs anyway, since
    a scaled empty would rescale every item Unity parents under it.

    Valid because nothing in this file is parented and every mesh sits at the
    origin with its geometry in world coordinates: `p.finish(..., origin=(0,0,0))`
    is what the generator asks for on all five parts. The location multiply is
    therefore only doing real work on `SURF_WallGrid`, and doing it in the same
    loop is what keeps this correct if a part is ever moved off the origin.
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
            # So the arrows still read as a marker on the face they mark rather
            # than as a speck. Display only; nothing downstream reads it.
            obj.empty_display_size *= factor

    return len(scaled_meshes)


def report():
    print("inventory_wall_scale  total x%.4f  (pack %.3f x wall display %.3f)"
          % (TOTAL, PACK_SCALE, WALL_DISPLAY))
    print("  --- the placement face, as the model DRAWS it ---")
    print("      this is InventoryWallBuilder.SurfaceSize times PackScale.WallDisplay.")
    print("      SurfaceSize itself does NOT move: it stays the LOGICAL rectangle,")
    print("      30 x 22 cells of PackGrid.Cell = 4.050 x 2.970 m, which is what every")
    print("      placement, save byte and wire message is written in. Only the mapping")
    print("      from a uv to a point on screen carries the display scale, so the board")
    print("      below and the numbers Unity reasons with are deliberately different.")

    surf = bpy.data.objects.get("SURF_WallGrid")
    loc = surf.matrix_world.to_translation() if surf is not None else Vector()
    print("    SURF_WallGrid   %.3f x %.3f m   (%d x %d cells of %.3f)   at (%.3f,%.3f,%.3f)"
          % (GRID_W * TOTAL, GRID_H * TOTAL,
             round(GRID_W / CELL), round(GRID_H / CELL), CELL * TOTAL,
             loc.x, loc.y, loc.z))
    print("    grid band z %.3f .. %.3f    bay pitch %.3f (%d cells) x %d bays"
          % (GRID_Z0 * TOTAL, GRID_Z1 * TOTAL, BAY_W * TOTAL,
             round(BAY_W / CELL), BAYS))
    print("    fitting %.3f W x %.3f H x %.3f D    tray reach %.3f"
          % (2 * (STILE_X + STILE_W) * TOTAL, HEADER_Z1 * TOTAL,
             DEPTH * TOTAL, TRAY_D * TOTAL))
    pts = [o.matrix_world @ Vector(c)
           for o in bpy.data.objects if o.type == 'MESH' for c in o.bound_box]
    lo = Vector((min(q.x for q in pts), min(q.y for q in pts), min(q.z for q in pts)))
    hi = Vector((max(q.x for q in pts), max(q.y for q in pts), max(q.z for q in pts)))

    # MEASURED, not derived from TRAY_D. The face sits at y = 0 with the whole
    # fitting behind it on +y, and what stands furthest back is whatever is
    # deepest — which since the 2026-09-01 hand edits is the surround, not the
    # tray. `WallDepth` is the number the ship subtracts from its half-width to
    # keep the fitting off the hull, so it has to be the real back of the
    # fitting; printing TRAY_D there is what let 0.287 m of plinth end up inside
    # the hull skin's baked collision with nothing to say so.
    print("    PlayerShipBuilder.WallGridCentreHeight must be %.4f, WallDepth %.4f"
          % ((GRID_Z0 + GRID_Z1) / 2.0 * TOTAL, hi.y))
    print("      (WallDepth is the MEASURED back of the fitting; TRAY_D alone "
          "would say %.4f)" % (TRAY_D * TOTAL))
    print("  BOUNDS W=%.3f D=%.3f H=%.3f  (%.3f..%.3f, %.3f..%.3f, %.3f..%.3f)"
          % (hi.x - lo.x, hi.y - lo.y, hi.z - lo.z, lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))


def main():
    scene = bpy.context.scene

    applied = float(scene.get(STAMP, 1.0))

    if abs(applied - TOTAL) <= EPSILON:
        raise SystemExit(
            "Already at x%.4f, which is TOTAL. Nothing to do — a second pass at "
            "the same scale is the one that leaves a wall no number on Unity's "
            "side agrees with, and nothing downstream would say so." % applied)

    residual = TOTAL / applied
    print("  at x%.4f, going to x%.4f — applying the residual x%.4f"
          % (applied, TOTAL, residual))

    meshes = scale_world(residual)
    scene[STAMP] = TOTAL

    # `matrix_world` is cached and only recomputed when the dependency graph is
    # evaluated, so without this every position `report` prints is the one from
    # before the scale — a printout that says the enlargement did not happen
    # while the file on disk says it did.
    bpy.context.view_layer.update()

    report()

    # An identity-scaled `SURF_WallGrid` is a hard requirement of the surface
    # convention — Unity parents every display copy under that empty, and a
    # scaled one would rescale all of them — and it is the cheapest possible
    # thing to get wrong here. Empties are refused.
    stretched = [o for o in bpy.data.objects
                 if (Vector(o.scale) - Vector((1.0, 1.0, 1.0))).length > 1e-6]

    empties = [o.name for o in stretched if o.type == 'EMPTY']
    if empties:
        raise SystemExit("Empties left non-identity-scaled: " + ", ".join(empties))

    # Meshes are a modelling decision — this file is hand-edited as well as
    # generated into — so they are named rather than refused. Named, and not
    # simply ignored, because a mesh that picked up a scale by accident is
    # indistinguishable from one that meant it except by somebody reading this.
    for obj in stretched:
        print("  hand scale  %-20s (%.4f, %.4f, %.4f) — preserved"
              % (obj.name, obj.scale.x, obj.scale.y, obj.scale.z))

    bpy.ops.wm.save_mainfile()
    print("  scaled %d meshes, %d objects; saved %s"
          % (meshes, len(bpy.data.objects), bpy.data.filepath))


main()
