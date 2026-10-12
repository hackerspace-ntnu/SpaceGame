"""Rig one of the four Strider humanoids in strider_characters.blend (the working copy of the user's
strider1.blend -- never run against strider1.blend itself).

    blender --background strider_characters.blend --python strider_characters_rig.py -- "<collection>" <Name>

One character per run (the file is large and the machine is shared). The run refuses a character
that already has its Char_<Name> collection, so it never re-rigs over a finished one.

The four humanoids share the "Body Male - Primitive (Realistic)" kit: one object per body segment
(GEO-pelvis_..., GEO-arm_upper_...L, ...) in an object-parent chain whose ORIGINS ARE THE JOINTS.
So the skeleton is read off those origins -- joints placed from the mesh, never by eye -- and each
segment is weighted 100 % to its bone. Everything else the user put on the body is either rigid
(a mask, a pouch, a buckle: most of its vertices sit nearest one segment) and goes 100 % to that
segment's bone, or a garment spanning joints (coats, ponchos) and gets soft weights from the
segments it lies over. Then the whole character is merged into ONE skinned mesh (one renderer,
one submesh per material) under a Unity-humanoid-named armature, and its duplicated palette
materials (Mat_X.005, ...) are pointed back at palette.blend's linked Mat_X.

Bone names are the Mecanim names Human_Rig (models/characters/drifters/human_sculpt_base_rigged.blend)
uses, so Unity's humanoid auto-mapper maps them; fingers and toes are left on the hand and foot.
"""

import os
import re
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

LIB_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
PALETTE = os.path.join(LIB_ROOT, "palette.blend")

# The kit's head, ears, eyes, nose and shoulders carry no material at all; they would import as
# Unity's default white. Most sit under a helmet or a poncho; what shows reads as skin.
UNSLOTTED_MATERIAL = "Mat_Hide_Dune_Tan"

# Subdivision on the kit's segments, capped: level 2 quadruples a body nobody sees under the cloth.
MAX_SUBDIVISION = 1

# A part whose vertices vote this share for one segment is rigid to that segment's bone.
RIGID_VOTE = 0.8
# Soft weights fade from a vertex's nearest segment to segments up to this much farther away (m).
BLEND_BAND = 0.06
MAX_INFLUENCES = 4

# Segment key (GEO-<key>_male_primitive_realistic<side>) -> bone, sides handled separately.
SEGMENT_BONES = {
    "pelvis": "Hips", "belly": "Spine", "chest": "Chest", "neck": "Neck", "head": "Head",
    "ear": "Head", "eye": "Head", "eyelid_upper": "Head", "eyelid_lower": "Head",
    "nose": "Head", "nose_bridge": "Head",
    "shoulder": "Shoulder", "arm_upper": "UpperArm", "arm_lower": "LowerArm", "hand": "Hand",
    "thumb": "Hand", "finger_index": "Hand", "finger_middle": "Hand", "finger_ring": "Hand",
    "finger_pinky": "Hand", "leg_upper": "UpperLeg", "leg_lower": "LowerLeg", "foot": "Foot",
}

# Garments are weighted to the torso and legs only: arms swing through a poncho rather than tearing it.
GARMENT_BONES_CENTRE = ("Hips", "Spine", "Chest", "Neck", "Head")
GARMENT_BONES_SIDED = ("Shoulder", "UpperLeg", "LowerLeg", "Foot")

# A part that sits mostly on one of these bones is gear worn there -- a helmet, a mask, a shoulder
# pad -- and rides that bone rigidly even when its vote is split: soft weights would bend a helmet
# at the neck. Only parts sitting mostly on the trunk or the legs are garments. "Mostly" is
# RIGID_WHEN_ON_VOTE: a poncho draped over everything votes ~0.2 for whichever arm it hangs nearest.
RIGID_WHEN_ON = ("Head", "Neck", "Shoulder", "UpperArm", "LowerArm", "Hand")
RIGID_WHEN_ON_VOTE = 0.45

# Parts placed by name, by base-name prefix: a bedroll strapped at the hip hangs from the hips, not
# from the thigh it happens to lie nearest.
PART_BONES = {"Mesh_CargoBundle_": "Hips"}

SEGMENT = re.compile(r"^GEO-(?P<key>[a-z_]+?)(?:\.\d{3})?_male_primitive_realistic(?P<side>\.[LR])?(?:\.\d{3})?$")


def strip_copy_suffix(name):
    return re.sub(r"\.\d{3}$", "", name)


def segment_bone(obj):
    """The bone a kit segment belongs to, or None for anything that is not a segment."""
    m = SEGMENT.match(obj.name)
    if not m:
        return None
    bone = SEGMENT_BONES.get(m.group("key"))
    if bone is None:
        raise SystemExit("Unknown kit segment %r: add it to SEGMENT_BONES" % obj.name)
    side = m.group("side")
    if bone in GARMENT_BONES_CENTRE:
        return bone
    if side is None:
        raise SystemExit("Sided segment %r has no .L/.R" % obj.name)
    return ("Left" if side == ".L" else "Right") + bone


def collection(name):
    coll = bpy.data.collections.get(name)
    if coll is None:
        raise SystemExit("No collection %r" % name)
    return coll


def world_bounds(obj):
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    return (tuple(round(min(p[i] for p in pts), 3) for i in range(3)),
            tuple(round(max(p[i] for p in pts), 3) for i in range(3)))


def dedupe(objs):
    """Drop exact duplicates stacked in place (same vertex count and world bounds to the mm).

    Deleting an object orphans its children, and an orphaned child keeps its LOCAL transform -- it
    jumps (the cyborg's forearms landed 6 m away). So every survivor's world matrix is put back.
    """
    worlds = {o.name: o.matrix_world.copy() for o in objs}
    seen, kept, dropped = set(), [], []
    for obj in sorted(objs, key=lambda o: o.name):
        if obj.type != 'MESH':
            kept.append(obj)
            continue
        key = (len(obj.data.vertices), world_bounds(obj))
        if key in seen:
            dropped.append(obj.name)
            bpy.data.objects.remove(obj, do_unlink=True)
            continue
        seen.add(key)
        kept.append(obj)
    for obj in kept:
        obj.matrix_world = worlds[obj.name]
    if dropped:
        print("  dropped %d stacked duplicate(s): %s" % (len(dropped), ", ".join(dropped)))
    return kept


def bake(meshes):
    """Apply modifiers and world transforms into the mesh data, unparent, identity transforms.

    Read every world matrix first: clearing one parent must not move a child not yet baked. A
    mirrored (negative-determinant) object gets its winding flipped back after the bake, or the
    skinned mesh renders inside out in Unity (ArtPipeline gotcha).
    """
    for obj in meshes:
        for mod in obj.modifiers:
            if mod.type == 'SUBSURF':
                mod.levels = min(mod.levels, MAX_SUBDIVISION)
                mod.render_levels = min(mod.render_levels, MAX_SUBDIVISION)
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    worlds = {o.name: o.matrix_world.copy() for o in meshes}
    baked = {}
    for obj in meshes:
        me = bpy.data.meshes.new_from_object(obj.evaluated_get(dg), preserve_all_data_layers=True,
                                             depsgraph=dg)
        world = worlds[obj.name]
        me.transform(world)
        if world.to_3x3().determinant() < 0.0:
            bm = bmesh.new()
            bm.from_mesh(me)
            bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
            bm.to_mesh(me)
            bm.free()
        baked[obj.name] = me
    for obj in meshes:
        obj.parent = None
        obj.modifiers.clear()
        obj.data = baked[obj.name]
        obj.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()


def base_colour(mat):
    """A material's Principled base colour (node looked up by type, never by its localised name)."""
    if mat.use_nodes and mat.node_tree is not None:
        for node in mat.node_tree.nodes:
            if node.type == 'BSDF_PRINCIPLED':
                return Vector(node.inputs[0].default_value[:3])
    return Vector(mat.diffuse_color[:3])


def link_palette(meshes, extra=()):
    """Point every Mat_X.NNN slot at palette.blend's linked Mat_X; give unslotted parts skin.

    A slot whose material is in no palette entry at all (Blender's default "Material", a one-off
    the user made) takes the palette material nearest its base colour, and the run says so: the
    character must ship with no local material (ArtPipeline gotcha). Returns the linked materials
    by name, `extra` names included."""
    with bpy.data.libraries.load(PALETTE, link=True) as (src, dst):
        dst.materials = list(src.materials)
    palette = {m.name: m for m in bpy.data.materials
               if m.library is not None and os.path.samefile(bpy.path.abspath(m.library.filepath), PALETTE)}
    for name in (UNSLOTTED_MATERIAL, *extra):
        if name not in palette:
            raise SystemExit("%s is not in palette.blend" % name)

    def palette_for(mat):
        base = strip_copy_suffix(mat.name)
        if base in palette:
            return palette[base]
        colour = base_colour(mat)
        nearest = min(palette.values(), key=lambda p: (base_colour(p) - colour).length)
        print("  %s is in no palette entry: using %s, the nearest colour" % (mat.name, nearest.name))
        return nearest

    resolved = {}
    for obj in meshes:
        if not obj.material_slots:
            obj.data.materials.append(palette[UNSLOTTED_MATERIAL])
            continue
        for slot in obj.material_slots:
            if slot.material is None:
                slot.material = palette[UNSLOTTED_MATERIAL]
            elif slot.material.library is None:
                key = slot.material.name
                if key not in resolved:
                    resolved[key] = palette_for(slot.material)
                slot.material = resolved[key]
    return palette


def nearest_bone_trees(segments):
    """One BVH per bone over all of that bone's segments, in world space (meshes are baked)."""
    by_bone = {}
    for obj, bone in segments:
        bm = by_bone.setdefault(bone, bmesh.new())
        bm.from_mesh(obj.data)
    trees = {bone: BVHTree.FromBMesh(bm) for bone, bm in by_bone.items()}
    for bm in by_bone.values():
        bm.free()
    return trees


def distances(trees, co, bones):
    out = {}
    for bone in bones:
        hit = trees[bone].find_nearest(co)
        if hit[0] is not None:
            out[bone] = hit[3]
    return out


def soft_weights(dist):
    near = min(dist.values())
    ws = {b: (1.0 - (d - near) / BLEND_BAND) ** 2 for b, d in dist.items() if d - near < BLEND_BAND}
    top = sorted(ws.items(), key=lambda kv: -kv[1])[:MAX_INFLUENCES]
    total = sum(w for _, w in top)
    return {b: w / total for b, w in top}


def assign(obj, weights_per_vertex):
    groups = {}
    for i, weights in enumerate(weights_per_vertex):
        for bone, w in weights.items():
            vg = groups.get(bone) or obj.vertex_groups.get(bone) or obj.vertex_groups.new(name=bone)
            groups[bone] = vg
            vg.add([i], w, 'REPLACE')


def weight_parts(segments, others, trees, garment_bones, overrides):
    all_bones = sorted(trees)
    report = []
    for obj, bone in segments:
        assign(obj, [{bone: 1.0}] * len(obj.data.vertices))
    for obj in others:
        forced = next((bone for prefix, bone in overrides.items() if obj.name.startswith(prefix)), None)
        if forced:
            assign(obj, [{forced: 1.0}] * len(obj.data.vertices))
            report.append((obj.name, "rigid (override)", forced, 1.0))
            continue
        votes = {}
        for v in obj.data.vertices:
            d = distances(trees, v.co, all_bones)
            best = min(d, key=d.get)
            votes[best] = votes.get(best, 0) + 1
        top, n = max(votes.items(), key=lambda kv: kv[1])
        share = n / max(1, len(obj.data.vertices))
        if share >= RIGID_VOTE or (share >= RIGID_WHEN_ON_VOTE and any(top.endswith(b) for b in RIGID_WHEN_ON)):
            assign(obj, [{top: 1.0}] * len(obj.data.vertices))
            report.append((obj.name, "rigid", top, share))
        else:
            per_vertex = [soft_weights(distances(trees, v.co, garment_bones)) for v in obj.data.vertices]
            assign(obj, per_vertex)
            report.append((obj.name, "garment", top, share))
    for name, kind, bone, share in sorted(report, key=lambda r: (r[1], r[2], r[0])):
        print("  %-48s %-16s %-14s %.2f" % (name[:48], kind, bone, share))


def origins(segments):
    """World origin of each segment's object, by bone (the kit's pivots), read before baking."""
    out = {}
    for obj, bone in segments:
        key = SEGMENT.match(obj.name).group("key")
        out.setdefault((bone, key), obj.matrix_world.translation.copy())
    return out


def skeleton_layout(pivots, foot_bounds):
    """(bone, parent, head, tail) for the humanoid skeleton, from the kit's joint pivots."""
    def p(bone, key):
        try:
            return pivots[(bone, key)]
        except KeyError:
            raise SystemExit("The body has no %s segment for %s" % (key, bone))

    hip_l, hip_r = p("LeftUpperLeg", "leg_upper"), p("RightUpperLeg", "leg_upper")
    pelvis = p("Hips", "pelvis")
    hips = Vector((pelvis.x, pelvis.y, (hip_l.z + hip_r.z) * 0.5))
    spine, chest, neck, head = p("Spine", "belly"), p("Chest", "chest"), p("Neck", "neck"), p("Head", "head")
    if spine.z - hips.z < 0.02:
        spine = spine + Vector((0, 0, 0.05))
    bones = [
        ("Hips", None, hips, spine),
        ("Spine", "Hips", spine, chest),
        ("Chest", "Spine", chest, neck),
        ("Neck", "Chest", neck, head),
        ("Head", "Neck", head, head + Vector((0.0, 0.0, 0.2))),
    ]
    for side in ("Left", "Right"):
        shoulder = p(side + "Shoulder", "shoulder")
        upper = p(side + "UpperArm", "arm_upper")
        lower = p(side + "LowerArm", "arm_lower")
        hand = p(side + "Hand", "hand")
        fingers = [v for (b, k), v in pivots.items() if b == side + "Hand" and k.startswith("finger")]
        fingertip = sum(fingers, Vector()) / len(fingers) if fingers else hand + (hand - lower) * 0.4
        knee = p(side + "LowerLeg", "leg_lower")
        ankle = p(side + "Foot", "foot")
        lo, hi = foot_bounds[side]
        toe = Vector((ankle.x, lo.y + 0.25 * (hi.y - lo.y), lo.z + 0.03))
        bones += [
            (side + "Shoulder", "Chest", shoulder, upper),
            (side + "UpperArm", side + "Shoulder", upper, lower),
            (side + "LowerArm", side + "UpperArm", lower, hand),
            (side + "Hand", side + "LowerArm", hand, fingertip),
            (side + "UpperLeg", "Hips", p(side + "UpperLeg", "leg_upper"), knee),
            (side + "LowerLeg", side + "UpperLeg", knee, ankle),
            (side + "Foot", side + "LowerLeg", ankle, toe),
        ]
    return bones


def build_armature(name, layout, coll):
    data = bpy.data.armatures.new("Rig_" + name)
    rig = bpy.data.objects.new("Rig_" + name, data)
    coll.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    edit = {}
    for bone, parent, head, tail in layout:
        eb = data.edit_bones.new(bone)
        eb.head, eb.tail = head, tail
        if parent:
            eb.parent = edit[parent]
        edit[bone] = eb
    bpy.ops.object.mode_set(mode='OBJECT')
    return rig


def join(meshes, name, coll):
    target = meshes[0]
    if len(meshes) > 1:
        with bpy.context.temp_override(active_object=target, selected_editable_objects=meshes,
                                       selected_objects=meshes):
            bpy.ops.object.join()
    target.name = name
    target.data.name = name
    for c in list(target.users_collection):
        c.objects.unlink(target)
    coll.objects.link(target)
    return target


def check(mesh, rig):
    """Nothing collapses to the origin and every bone deforms something (an unweighted human bone is
    silently unmapped by Unity's avatar: ArtPipeline gotcha)."""
    weighted = {vg.index: 0 for vg in mesh.vertex_groups}
    zero = 0
    for v in mesh.data.vertices:
        total = sum(g.weight for g in v.groups)
        if total < 1e-4:
            zero += 1
        for g in v.groups:
            if g.weight > 0:
                weighted[g.group] += 1
    counts = {mesh.vertex_groups[i].name: n for i, n in weighted.items()}
    empty = [b.name for b in rig.data.bones if counts.get(b.name, 0) == 0]
    print("  vertices %d, unweighted %d, per bone %s" % (len(mesh.data.vertices), zero, counts))
    if zero or empty:
        raise SystemExit("Weights incomplete: %d unweighted vertices, bones with no vertices %s" % (zero, empty))


def rig_humanoid(coll_name, name, overrides=PART_BONES):
    out_name = "Char_" + name
    if bpy.data.collections.get(out_name) is not None:
        raise SystemExit("%s already exists: this character is rigged. Never re-rig over it." % out_name)
    src = collection(coll_name)
    objs = dedupe(list(src.all_objects))
    meshes = [o for o in objs if o.type == 'MESH']

    segments = [(o, segment_bone(o)) for o in meshes if segment_bone(o)]
    others = [o for o in meshes if not segment_bone(o)]
    pivots = origins(segments)
    foot_bounds = {}
    for side in ("Left", "Right"):
        feet = [o for o, b in segments if b == side + "Foot"]
        pts = [o.matrix_world @ v.co for o in feet for v in o.data.vertices]
        foot_bounds[side] = (Vector([min(p[i] for p in pts) for i in range(3)]),
                             Vector([max(p[i] for p in pts) for i in range(3)]))

    bake(meshes)
    link_palette(meshes)
    trees = nearest_bone_trees(segments)
    garment = [b for b in trees if b in GARMENT_BONES_CENTRE or any(b.endswith(s) for s in GARMENT_BONES_SIDED)]
    weight_parts(segments, others, trees, garment, overrides)

    out = bpy.data.collections.new(out_name)
    bpy.context.scene.collection.children.link(out)
    rig = build_armature(name, skeleton_layout(pivots, foot_bounds), out)
    mesh = join(meshes, "Strider_" + name, out)
    mesh.parent = rig
    mod = mesh.modifiers.new("Armature", 'ARMATURE')
    mod.object = rig
    check(mesh, rig)

    # The source collection is now empty (its objects were joined) apart from its child collections.
    for child in list(src.children_recursive):
        bpy.data.collections.remove(child)
    bpy.data.collections.remove(src)
    bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=False, do_recursive=True)
    print("  %s: %d tris, %d materials" % (mesh.name, sum(len(p.vertices) - 2 for p in mesh.data.polygons),
                                           len(mesh.data.materials)))
    return rig, mesh


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if len(argv) != 2:
        raise SystemExit('usage: ... --python strider_characters_rig.py -- "<collection>" <Name>')
    if not bpy.data.filepath.endswith("strider_characters.blend"):
        raise SystemExit("Run against strider_characters.blend only, never the user's strider1.blend.")
    rig_humanoid(argv[0], argv[1])
    bpy.ops.wm.save_mainfile()
    print("  saved %s" % bpy.data.filepath)


if __name__ == "__main__":
    main()
