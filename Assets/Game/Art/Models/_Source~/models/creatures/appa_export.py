"""Export appa.blend to the FBX Unity consumes.

    blender --background --python appa_export.py

Meant to be re-run. It never writes to the .blend it opens.

Two things it does that a plain export would not, and both exist because Appa is
a hand sculpt rather than a generated model -- the same reasoning as
`vrescal_export.py`, which is the file to read next if this one confuses you.

  * **Turns -X forward into -Y forward.** The sculpt faces -X because that is
    how it was modelled. The library convention is -Y, which the axis conversion
    below maps onto Unity's +Z. Without the yaw Appa arrives in Unity walking
    sideways.

  * **Moves the pivot to between the soles.** In the .blend the origin sits
    inside the ribcage with the soles at z = -1.76. Exported as-is the prefab
    would hover 1.76 m in the air, and anything that later places him on the
    ground -- a NavMeshAgent, a spawn point -- would bury him instead. `PIVOT`
    puts the origin on the sole plane, midway between the front and back feet.

Neither is applied to the .blend. Rescaling or yawing the source would move
every vertex of the author's model, and the .blend is the source of truth.

Both are applied by transforming `Arm_Appa` itself. After `appa_rig.py` the
armature is the only root object -- every mesh is either skinned to it or
parented to one of its bones -- so moving it moves the whole animal, and the
bone animation is in armature-local space and does not care.

Do **not** reintroduce a parent empty to carry this. Blender's FBX exporter
drops empties and the model arrives unrotated with no error anywhere to say so.

## Animation

Both actions become their own FBX take, named `Arm_Appa|Appa_Idle` and
`Arm_Appa|Appa_Walk`. `AppaBuilder` slices those takes into clips and sets the
loop flags, so the take names are the contract between the two files: rename an
action and the builder has to follow.

Clips are **in place** -- no forward root translation -- so the prefab keeps
`m_ApplyRootMotion: 0`.
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))

# Walk up to the Unity project root rather than counting parent directories.
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(
        os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)

SRC = os.path.join(HERE, "appa.blend")
DST = os.path.join(REPO, "Assets", "Game", "Art", "Models",
                   "Creatures", "Organic", "Appa", "appa.fbx")

ARM = "Arm_Appa"

# Meshes that exist in the sculpt but must not reach the game.
#
# `Cube` is a flat, lens-shaped blob sitting at belly height between the legs,
# on `Material.009` -- no texture, base colour (0.242, 0.254, 0.305), a dark
# blue-grey nothing else on the animal uses. It is interior filler, and it does
# not stay interior: 441 of its 1538 vertices (28.7%) sit OUTSIDE the body mesh
# in the rest pose, protruding up to 0.21 m. Untextured dark grey breaching a
# light tan hide reads in game as holes punched behind the front legs, worst
# where the flank is thinnest and symmetric because the sculpt is mirrored.
#
# `Cube.016` carries the whole animal -- torso, hump, tail and all six legs --
# and is closed (0 boundary edges), so dropping this loses nothing visible.
# The object stays in the .blend; this only stops it being exported.
EXCLUDE = {"Cube"}

# Where the front legs meet the body, and how hard to round that junction off.
#
# Reported twice as "holes behind both his front legs". There is no hole. The
# body mesh measures clean on every test that could produce one -- 0 boundary
# edges, 0 self-intersections, 0 winding-inconsistent edges, 0 enclosed
# see-through pixels when the silhouette is ray-cast, no narrow slots, and no
# dark faces in the texture. What is actually there is a deep, sharply hooked
# recess where the leg joins the torso: it takes almost no light, so URP's
# shadowing takes it to black and the eye reads a puncture.
#
# So this is a shading fix made out of geometry -- a few Laplacian passes
# weighted by distance from the junction, which fills the hook into a rounded
# fillet. It moves 549 vertices by a median of 7 mm (max 87 mm) and tapers to
# zero at the edge of the region, so there is no seam.
#
# Applied at export, never to the .blend: the sculpt is the author's and this is
# the game's problem, not the model's. Delete this constant to turn it off.
# The lower jaw and its teeth sit slightly proud of the muzzle, so a closed mouth
# reads as an underbite. Nudge that whole assembly back and up -- +X is toward the
# tail, since the head is at -X. Author request; applied at export so the sculpt is
# untouched, and set to (0, 0, 0) to turn it off.
JAW_SHIFT = (0.06, 0.0, 0.04)
JAW_PARTS = ("Cube.003", "Cube.025", "Cube.027", "Cube.028",
             "Cube.029", "Cube.030", "Cube.031", "Cube.032", "Cube.033")

JUNCTION_CENTRES = ((1.02, 0.86, -0.72), (1.02, -0.86, -0.72))
JUNCTION_RADIUS = 0.62
JUNCTION_PASSES = 6
JUNCTION_STRENGTH = 0.85

# The point in the .blend that becomes Unity's origin: on the sole plane,
# midway between the front and back feet. Must agree with where `root` sits in
# appa_rig.py, or the root bone is not at the origin.
PIVOT = Vector((1.44, 0.0, -1.76))

# -X forward in the sculpt -> -Y forward, the library convention.
YAW = math.radians(90.0)


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s" % SRC)

    bpy.ops.wm.open_mainfile(filepath=SRC)

    # Palette materials are linked from outside Assets/, where Unity can never
    # resolve them. Appa carries his own local materials so this is normally a
    # no-op -- it stays because a later edit could link one in and the failure
    # mode is a silently untextured model.
    localised = 0
    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()
            localised += 1

    # Before anything measures or rewrites the meshes, so the counts printed
    # below describe what actually ships.
    dropped = []
    for name in sorted(EXCLUDE):
        obj = bpy.data.objects.get(name)
        if obj is not None:
            bpy.data.objects.remove(obj, do_unlink=True)
            dropped.append(name)

    shifted = _shift_jaw()
    softened = _soften_leg_junction()

    written = _write_textures()
    collapsed = _collapse_duplicate_shaders()
    # Order matters: baking a negative scale into the mesh reverses its winding,
    # so normals have to be recalculated after, not before.
    unmirrored = _apply_object_transforms()
    consistent = _make_normals_consistent()

    roots = [o for o in bpy.data.objects if o.parent is None]
    if len(roots) != 1 or roots[0].name != ARM:
        raise SystemExit(
            "Expected %s to be the only root object, found %s. Anything still "
            "unparented would keep the sculpt's own placement and arrive in "
            "Unity detached from the rest of the animal."
            % (ARM, [o.name for o in roots]))

    # world' = yaw . (world - PIVOT). Right to left: slide PIVOT onto the
    # origin, then turn -X forward into -Y.
    place = Matrix.Rotation(YAW, 4, 'Z') @ Matrix.Translation(-PIVOT)
    roots[0].matrix_world = place @ roots[0].matrix_world

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    tris = sum(sum(max(0, len(p.vertices) - 2) for p in o.data.polygons) for o in meshes)
    takes = [a.name for a in bpy.data.actions]

    print("Exporting %d meshes, %d tris, %d bones, %d takes (%d materials localised)"
          % (len(meshes), tris, len(roots[0].data.bones), len(takes), localised))
    print("  takes: %s" % ", ".join("%s|%s" % (ARM, t) for t in takes))
    print("  excluded from export: %s" % (", ".join(dropped) if dropped else "none"))
    print("  jaw geometry shifted: %s" % (", ".join(shifted) if shifted else "none"))
    print("  leg junction softened: %d verts, max %.3f m" % softened)
    print("  textures written: %s" % (", ".join(written) if written else "none"))
    print("  duplicate shader chains collapsed: %s"
          % (", ".join(collapsed) if collapsed else "none"))
    print("  transforms baked, mirror removed from: %s"
          % (", ".join(unmirrored) if unmirrored else "none (none were mirrored)"))
    print("  normals made consistent on: %s"
          % (", ".join(consistent) if consistent else "none (all were already)"))

    os.makedirs(os.path.dirname(DST), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=DST,
        use_selection=False,
        object_types={'MESH', 'ARMATURE'},
        apply_scale_options='FBX_SCALE_NONE',
        # -Y forward in Blender becomes +Z forward in Unity, and Blender's +Z up
        # becomes Unity's +Y up. The yaw above is what puts Appa on -Y.
        axis_forward='-Z',
        axis_up='Y',
        mesh_smooth_type='FACE',
        use_mesh_modifiers=False,      # keep the armature modifier unapplied
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        armature_nodetype='NULL',
        bake_space_transform=False,
        path_mode='AUTO',
        embed_textures=False,
    )
    print("  wrote %s (%.1f MB)" % (DST, os.path.getsize(DST) / 1e6))
    # Deliberately no save_mainfile: the .blend is the source of truth.

    verify()



def _shift_jaw():
    """Move the lower-jaw assembly by JAW_SHIFT, in world metres.

    The mesh DATA is translated, not the objects. These parts are bone-parented to
    `jaw`, so moving the object would be undone by the bone it hangs off, and
    moving the bone would drag the hinge -- and the hinge is where it should be.
    """
    offset = Vector(JAW_SHIFT)
    if offset.length < 1e-6:
        return []

    moved = []
    for name in JAW_PARTS:
        obj = bpy.data.objects.get(name)
        if obj is None:
            continue
        local = obj.matrix_world.to_3x3().inverted() @ offset
        for vertex in obj.data.vertices:
            vertex.co += local
        obj.data.update()
        moved.append(name)
    return moved


def _soften_leg_junction():
    """Round off the front-leg/torso recess. See JUNCTION_CENTRES.

    Laplacian, not a sculpt: each vertex moves toward the average of its
    neighbours by a weight that smoothsteps to zero at JUNCTION_RADIUS. A concave
    crease is exactly the shape that fills under that, and a convex one barely
    moves, so the leg keeps its silhouette while the hook in its armpit rounds
    out. Returns (vertices touched, largest move in metres) for the report.
    """
    obj = bpy.data.objects.get("Cube.016")
    if obj is None or not JUNCTION_CENTRES:
        return 0, 0.0

    matrix = obj.matrix_world
    inverse = matrix.inverted()
    mesh = obj.data

    neighbours = [set() for _ in range(len(mesh.vertices))]
    for edge in mesh.edges:
        a, b = edge.vertices
        neighbours[a].add(b)
        neighbours[b].add(a)

    centres = [Vector(c) for c in JUNCTION_CENTRES]
    start = [matrix @ v.co for v in mesh.vertices]

    weights = []
    for point in start:
        near = min((point - c).length for c in centres)
        if near >= JUNCTION_RADIUS:
            weights.append(0.0)
            continue
        t = 1.0 - near / JUNCTION_RADIUS
        weights.append(t * t * (3.0 - 2.0 * t) * JUNCTION_STRENGTH)

    active = [i for i, w in enumerate(weights) if w > 0.001]
    if not active:
        return 0, 0.0

    current = list(start)
    for _ in range(JUNCTION_PASSES):
        nxt = list(current)
        for i in active:
            if not neighbours[i]:
                continue
            average = Vector((0.0, 0.0, 0.0))
            for j in neighbours[i]:
                average += current[j]
            average /= len(neighbours[i])
            nxt[i] = current[i].lerp(average, weights[i])
        current = nxt

    largest = 0.0
    for i in active:
        moved = (current[i] - start[i]).length
        largest = max(largest, moved)
        mesh.vertices[i].co = inverse @ current[i]
    mesh.update()

    return len(active), largest


def _write_textures():
    """Write the .blend's packed images out beside the FBX, inside `Assets/`.

    Every one of Appa's six textures is **packed into the .blend with an empty
    filepath**. An FBX cannot carry a reference to that: `embed_textures=False`
    writes a path, and the path of a packed image is the empty string, so the
    model arrives in Unity with its materials intact and every texture slot
    empty. That is the whole of "my texturing disappeared".

    Writing them into `<fbx dir>/Textures/` puts them where Unity can import
    them and where the FBX's own relative references resolve, so the materials
    bind on import with nothing to wire by hand.

    Done here rather than in the .blend on purpose: unpacking would rewrite the
    author's image datablocks, and the .blend is the source of truth. This
    script never saves it.
    """
    tex_dir = os.path.join(os.path.dirname(DST), "Textures")
    os.makedirs(tex_dir, exist_ok=True)

    written = []
    for img in bpy.data.images:
        # 'Render Result' and 'Viewer Node' are generated, have no pixels on
        # disk, and raise if you ask them to save.
        if img.type != 'IMAGE' or img.size[0] == 0:
            continue

        grown = _dilate(img)

        path = os.path.join(tex_dir, "%s.png" % img.name)
        img.filepath_raw = path
        img.file_format = 'PNG'
        img.save()

        # Setting `filepath` is NOT enough. A still-packed image makes the
        # exporter fall back to its embedded bytes and skip the reference
        # entirely -- it logs `Image "" not available. Keeping packed image` and
        # writes an FBX with no texture in it at all. The image has to actually
        # stop being packed, which is what unpack does; `packed_file` itself is
        # read-only and cannot be cleared directly.
        if img.packed_file is not None:
            img.unpack(method='REMOVE')

        img.filepath = path
        img.reload()

        if img.packed_file is not None:
            raise SystemExit("%s is still packed; the FBX would ship untextured." % img.name)

        written.append("%s(+%d px)" % (os.path.basename(path), grown))

    return written


def _dilate(img, passes=64):
    """Grow the painted pixels outward over the unpainted black background.

    Every one of Appa's textures is a UV layout painted on a black canvas, and
    the paint stops exactly at the island edge with no margin. That is fine at
    full resolution -- Blender's viewport samples mip 0 and the islands look
    clean -- and wrong at every other resolution. Unity builds mipmaps, and each
    level averages the island with the black beside it, so at any distance the
    black bleeds inward: the mane's thin strands go blotchy dark and the
    shoulder fur, whose islands are tiny, goes nearly black outright.

    Padding the islands is the standard fix. Each pass fills unpainted pixels
    from their painted neighbours, so the colour that bleeds into an island at
    low mip levels is the fur's own colour rather than the canvas.

    `BackHair` needs this most and benefits least honestly: it is **99.2%
    unpainted**, so what dilation gives it is a flood-fill of the 0.8% that was
    painted. It stops the shoulder fur rendering black, but the real fix is to
    paint or re-unwrap that map -- see appa_BUILD.md.

    Exact black is a safe test for "never painted" here, and that is measured
    rather than assumed: in all six images the share of *exactly* black pixels
    equals the share of near-black ones, so nothing painted is being eaten.
    """
    import numpy as np

    w, h = img.size
    buf = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    buf = buf.reshape(h, w, 4)

    rgb = buf[:, :, :3]
    painted = rgb.sum(axis=2) > 0.0

    if painted.all() or not painted.any():
        return 0

    filled = 0
    for _ in range(passes):
        if painted.all():
            break

        acc = np.zeros_like(rgb)
        cnt = np.zeros((h, w), dtype=np.float32)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                sp = np.roll(np.roll(painted, dy, axis=0), dx, axis=1)
                sc = np.roll(np.roll(rgb, dy, axis=0), dx, axis=1)
                acc += sc * sp[:, :, None]
                cnt += sp

        grow = (~painted) & (cnt > 0.0)
        if not grow.any():
            break

        rgb[grow] = acc[grow] / cnt[grow][:, None]
        painted |= grow
        filled += int(grow.sum())

    buf[:, :, :3] = rgb
    img.pixels.foreach_set(buf.ravel())
    return filled


def _collapse_duplicate_shaders():
    """Leave each material with exactly one Principled BSDF: the textured one.

    Five of Appa's materials carry **two** shader chains -- a flat-coloured
    `Principled BSDF` -> `Material Output`, and a textured
    `Principled BSDF.001` -> `Material Output.001`. The second is the one marked
    active, so Blender renders the texture and the file looks right.

    Blender's FBX exporter does not follow the active output. It takes the first
    Principled BSDF it finds, which is the flat one, and the texture never
    reaches the FBX. That is why `appaFace` survived the last export and the
    other five did not: `Material.010` has a single chain, and its one BSDF
    happens to be the textured one.

    Deleting the dead chain leaves the exporter no wrong node to pick. Done at
    export, so the author's material graphs are left exactly as built.
    """
    collapsed = []
    for mat in bpy.data.materials:
        if not mat.use_nodes or mat.node_tree is None:
            continue

        tree = mat.node_tree
        outputs = [n for n in tree.nodes if n.type == 'OUTPUT_MATERIAL']
        if len(outputs) < 2:
            continue

        active = next((o for o in outputs if o.is_active_output), outputs[0])
        surface = active.inputs.get("Surface")
        if surface is None or not surface.is_linked:
            continue

        # Names, not references. `nodes.remove()` reallocates the collection, so
        # any Python handle taken beforehand goes stale and an identity test
        # against it silently matches nothing -- which deleted BOTH shaders and
        # left materials with no BSDF at all, exporting them untextured. Names
        # survive the reallocation; the node list is re-queried after each pass.
        active_name = active.name
        keep_name = surface.links[0].from_node.name

        for node in [n for n in tree.nodes
                     if n.type == 'OUTPUT_MATERIAL' and n.name != active_name]:
            tree.nodes.remove(node)

        for node in [n for n in tree.nodes
                     if n.type == 'BSDF_PRINCIPLED' and n.name != keep_name]:
            tree.nodes.remove(node)

        if not any(n.type == 'BSDF_PRINCIPLED' for n in tree.nodes):
            raise SystemExit(
                "%s lost its shader during collapse -- it would export untextured."
                % mat.name)

        collapsed.append(mat.name)

    return collapsed


def _apply_object_transforms():
    """Bake every mesh's own rotation and scale into its vertices.

    Appa is built the way hand sculpts usually are: the author duplicated halves
    by negating a scale axis, so sixteen of the twenty-seven meshes carry a
    **negative scale determinant**. Three of those sixteen are also *skinned* --
    the legs (`Cube`), the shoulder fur (`Cube.015`) and the mane (`Cube.026`).
    Those three are the problem, and the distinction is the whole point:

      * A mirrored **rigid** prop is fine in Unity. It arrives as a MeshRenderer
        whose transform still carries the negative scale, and Unity reverses the
        culling mode on a negative-determinant renderer, which cancels the
        winding flip exactly.

      * A mirrored **skinned** mesh is not. A SkinnedMeshRenderer does not deform
        through its own transform -- it deforms through the bind poses and the
        bone hierarchy -- so there is no negative-determinant renderer transform
        for Unity to notice, and nothing reverses the culling. The mirror's
        winding flip survives into the game, the surface lights by a normal
        pointing into the mesh instead of out of it, and the mane comes out a
        mottled patchwork of dark and pale locks. Blender never shows it, because
        there the negative scale is still a live object transform and the
        viewport flips accordingly -- which is why re-importing the FBX into
        Blender to check it looks perfectly correct.

    Baking the transform into the vertex data removes the mirror outright, so no
    part of the chain has to compensate for one. It is geometry-preserving: every
    vertex stays exactly where it was in world space, which the check below
    enforces rather than assumes. `location` is deliberately left alone so each
    object keeps the origin the author gave it.

    This is what `dune_foil_rig.py` already does before its export, for the same
    reason. Do not confuse it with the deleted `_cancel_mirrored_winding`, which
    tried to reverse the winding of all sixteen mirrored meshes by hand: that
    broke the thirteen rigid ones, which never needed help, and reversing a
    correct normal is how the mane ended up a black dome lit from below.

    Returns the names whose mirror was removed.
    """
    before = {}
    for obj in bpy.data.objects:
        if obj.type != 'MESH' or not obj.data.vertices:
            continue
        # Mesh data shared between objects cannot be applied to, and silently
        # taking the first object's transform for both would move the other.
        if obj.data.users > 1:
            obj.data = obj.data.copy()
        before[obj.name] = (obj.matrix_world.determinant(),
                            obj.matrix_world @ obj.data.vertices[0].co)

    if bpy.context.object is not None and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')

    bpy.ops.object.select_all(action='DESELECT')
    meshes = [bpy.data.objects[n] for n in before]
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.context.view_layer.update()

    unmirrored = []
    for obj in meshes:
        det, anchor = before[obj.name]
        moved = (obj.matrix_world @ obj.data.vertices[0].co - anchor).length
        if moved > 1e-4:
            raise SystemExit(
                "%s moved %.4f m when its transform was baked. The bake must be "
                "geometry-preserving; anything else silently reshapes the "
                "author's model." % (obj.name, moved))
        if obj.matrix_world.determinant() < 0.0:
            raise SystemExit(
                "%s is still mirrored after baking its transform." % obj.name)
        if det < 0.0:
            unmirrored.append(obj.name)

    return unmirrored


def _make_normals_consistent():
    """Point every face outward before anything else touches the winding.

    Three of the author's meshes have faces wound inconsistently with their
    neighbours -- the mane (378 of 6618), the shoulder fur (378 of 7290) and the
    ears (96 of 192, an exact half). Blender never shows it, because it draws
    both sides of a face; Unity lights the side the normal points at, so those
    patches come out dark and inside-out while the rest of the same mesh looks
    fine. That is the "the fur on the head and shoulders is messed up" the
    author sees, and it is not a UV problem -- the UVs are clean and untouched
    by this.

    **This must run after `_apply_object_transforms`, and it is the only thing
    that should ever touch winding.** Baking a negative scale into the vertices
    reverses the winding of every mirrored mesh, so recalculating outward here is
    what puts them right; run in the other order it would be undone.

    Do not add a pass that reverses whole meshes to "cancel the mirror". One
    existed, it reversed all sixteen mirrored meshes by hand, and it broke the
    thirteen rigid ones -- those arrive as MeshRenderers that keep their negative
    scale, and Unity already cancels the winding flip for them by reversing the
    culling mode. Reversing a correct normal is how the mane ended up a black
    dome lit from underneath. Removing the mirror at the source, as
    `_apply_object_transforms` now does, is what that pass was groping for.

    Only winding changes. Vertex positions and UVs are untouched -- reversing a
    face's loops carries its loop data along, which was verified by comparing
    every (vertex, uv) pair before and after.
    """
    fixed = []
    for obj in bpy.data.objects:
        if obj.type != 'MESH' or not obj.data.polygons:
            continue

        bm = bmesh.new()
        bm.from_mesh(obj.data)
        before = [f.normal.copy() for f in bm.faces]
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.faces.ensure_lookup_table()
        changed = sum(1 for i, f in enumerate(bm.faces) if f.normal.dot(before[i]) < 0.0)

        if changed:
            bm.to_mesh(obj.data)
            obj.data.update()
            fixed.append("%s(%d)" % (obj.name, changed))
        bm.free()

    return fixed


def verify():
    """Re-import what was just written and check it is actually usable.

    Cheap insurance against the two failures that are invisible until Unity
    opens the file: a skeleton that lost its bones, and takes that baked out
    empty.
    """
    bpy.ops.wm.read_homefile(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=DST)

    rigs = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    if not rigs:
        raise SystemExit("VERIFY FAILED: no armature in the exported FBX.")

    bones = len(rigs[0].data.bones)
    actions = [a.name for a in bpy.data.actions]

    leaves = [b.name for b in rigs[0].data.bones if b.name.endswith("_end")]
    if leaves:
        raise SystemExit("VERIFY FAILED: leaf bones present: %s" % leaves)

    print("  verify: %d bones, %d action(s): %s" % (bones, len(actions), actions))
    if bones != 28:
        raise SystemExit("VERIFY FAILED: expected 28 bones, got %d" % bones)
    if len(actions) < 2:
        raise SystemExit("VERIFY FAILED: expected 2 takes, got %s" % actions)
    print("  verify: OK")


if __name__ == "__main__":
    main()
