"""Re-rig the hand-edited Vrescal as the six-legged animal it actually is.

    blender --background --python vrescal_hexapod_rig.py -- --overwrite

Reads `vrescal_lowpoly_rigged.blend`, writes `vrescal_hexapod_rigged.blend`.
**Not one vertex of the mesh is touched** -- the input carries hand sculpting
that exists nowhere else. Only the armature is rebuilt, and only because the old
one was wrong about the animal.

## The animal has six legs and the rig had four

Measured off the mesh by walking its own edges below the belly (see
`limbs()` below), the six limbs sit at:

    FrontP  x +1.09  y +0.67      FrontS  x +1.09  y -0.66
    MidP    x +0.58  y +0.62      MidS    x +0.58  y -0.62
    RearP   x -0.56  y +0.29      RearS   x -1.17  y -0.22

Four at the front in two ranks, two at the back. `vrescal_rig.py` carries a
hardcoded four-entry `LIMBS` table inherited from the earlier *lofted*
quadruped, so the middle pair never got bones. Bone heat still weighted those
vertices -- to whatever chain happened to be nearest -- which is why the rig
verified as "0 unweighted vertices" while two whole legs were being dragged
along by the front pair instead of stepping.

**Spatial clustering cannot find this and was what hid it.** Single-link
clustering on vertex positions chain-merges two limbs whose surfaces pass within
the threshold, and the front and middle legs are 1.57 units apart with a gap of
about 0.57 between their surfaces. Connected components over the mesh's own
edges cannot chain across empty space, and it reports six at every cut height
from 1.43 m down.

## The neck is corrected in Y only

The head geometry runs off to port -- straight to x 6.4, then swinging to
y +1.51 at the skull and +2.12 at the snout (0.42 m and 0.59 m) -- while every
neck bone sat on y = 0. A bone that is not inside the geometry it drives makes
rotation swing the mesh through an arc instead of turning it in place, which is
the "head is at an angle" symptom.

Only Y is re-derived. X and Z were measured for this sculpt and are within half
a unit of their geometry; moving them as well would be re-rigging things that
were not broken. Centring the head *while animating* is a separate problem and
is solved in `vrescal_hexapod_anim.py`, not here -- the rest pose keeps the
sculpt's real shape.
"""

import os
import sys

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import anatomy as A          # noqa: E402

SRC = os.environ.get("VRESCAL_HEX_SRC",
                     os.path.join(HERE, "vrescal_lowpoly_rigged.blend"))
OUT = os.environ.get("VRESCAL_HEX_OUT",
                     os.path.join(HERE, "vrescal_hexapod_rigged.blend"))

UNITS_PER_M = 3.62450
GROUND = -13.0
ARM = "Arm_Vrescal"
MESH = "Mesh_Vrescal_Sculpt"
PIVOT_X_M = -0.05

# Joint heights in metres above the sole plane, shared by all six limbs. These
# are the heights vrescal_rig.py used and they still bracket every limb: all six
# feet bottom out within 0.02 m of the sole plane and all six separate from the
# body at 1.43 m.
JOINT_M = [2.12, 1.45, 0.93, 0.42]
SEGMENTS = ["Upper", "Lower", "Cannon"]

FOOT = {}          # filled by limbs(), consumed by the animation shim


def M(m):
    return GROUND + m * UNITS_PER_M


# --------------------------------------------------------------------------
# Measuring the mesh
# --------------------------------------------------------------------------

def mesh_arrays(obj):
    me = obj.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    ed = np.empty(len(me.edges) * 2, dtype=np.int64)
    me.edges.foreach_get("vertices", ed)
    return co.reshape(-1, 3), ed.reshape(-1, 2)


def components(co, ed, zcut):
    """Connected components of the surface below `zcut`, by edge walking."""
    keep = co[:, 2] < zcut
    idx = np.where(keep)[0]
    remap = -np.ones(len(co), dtype=np.int64)
    remap[idx] = np.arange(len(idx))
    adj = [[] for _ in idx]
    for a, b in ed:
        if keep[a] and keep[b]:
            adj[remap[a]].append(remap[b])
            adj[remap[b]].append(remap[a])
    lab = -np.ones(len(idx), dtype=int)
    n = 0
    for i in range(len(idx)):
        if lab[i] >= 0:
            continue
        stack, lab[i] = [i], n
        while stack:
            j = stack.pop()
            for k in adj[j]:
                if lab[k] < 0:
                    lab[k] = n
                    stack.append(k)
        n += 1
    return [idx[lab == g] for g in range(n) if (lab == g).sum() >= 12]


def limbs(co, ed):
    """Six limb chains, each as {name, joints[4], toe}, measured from geometry.

    The cut height is searched downward for the highest one that still yields
    exactly six components -- the higher it separates, the more of each limb is
    measured directly rather than extrapolated from its top slice.
    """
    found = None
    for zc in np.arange(-6.6, -9.01, -0.1):
        c = components(co, ed, zc)
        if len(c) == 6:
            found = (zc, c)
            break
    if found is None:
        raise SystemExit(
            "Could not separate six limbs at any cut height. The mesh is not "
            "the six-legged Vrescal this script is for.")
    zcut, comps = found

    entries = []
    for c in comps:
        p = co[c]
        entries.append(dict(idx=c, cx=p[:, 0].mean(), cy=p[:, 1].mean()))
    entries.sort(key=lambda d: -d["cx"])
    for i, d in enumerate(entries):
        d["name"] = ["Front", "Front", "Mid", "Mid", "Rear", "Rear"][i] \
            + ("P" if d["cy"] > 0 else "S")

    out = {}
    print("  limbs separate at z < %.2f (%.2f m); six found:"
          % (zcut, (zcut - GROUND) / UNITS_PER_M))
    for d in entries:
        p = co[d["idx"]]
        joints = []
        for m in JOINT_M:
            z = M(m)
            band = p[np.abs(p[:, 2] - z) < 0.55]
            if len(band) < 4:
                # Above where this limb separates from the body. Its own top
                # slice is the best available estimate, and the rule this rig
                # follows -- y barely changes down a limb, or it reads as a
                # trestle -- makes that a good one.
                band = p[p[:, 2] > p[:, 2].max() - 1.0]
            joints.append((float(band[:, 0].mean()), float(band[:, 1].mean()), z))
        sole = p[p[:, 2] < p[:, 2].min() + 0.55]
        toe = sole[sole[:, 0].argmax()]
        reach = float(np.percentile(
            np.hypot(sole[:, 0] - sole[:, 0].mean(),
                     sole[:, 1] - sole[:, 1].mean()), 90))
        out[d["name"]] = dict(joints=joints,
                              toe=(float(toe[0]), float(toe[1]), float(toe[2])))
        FOOT[d["name"]] = dict(sole=round(reach / UNITS_PER_M, 3),
                               height=round((M(JOINT_M[-1]) - GROUND)
                                            / UNITS_PER_M, 3))
        print("    %-7s hip (%+6.2f,%+6.2f)  ankle (%+6.2f,%+6.2f)  "
              "toe (%+6.2f,%+6.2f)  %3d verts  sole %.2f m"
              % (d["name"], joints[0][0], joints[0][1], joints[3][0],
                 joints[3][1], toe[0], toe[1], len(p), FOOT[d["name"]]["sole"]))
    return out


def neck_lateral(co):
    """y(x) along the neck and head, sampled off the mesh.

    Everything forward of the shoulders and above the brisket. The jaw is
    included deliberately: it hangs off the skull and swings with it, so its
    vertices belong to the same lateral curve.
    """
    sel = co[(co[:, 0] > 4.5) & (co[:, 2] > -6.5)]
    xs, ys = [], []
    for x in np.arange(4.6, sel[:, 0].max() + 0.01, 0.4):
        band = sel[np.abs(sel[:, 0] - x) < 0.35]
        if len(band) >= 4:
            xs.append(x)
            ys.append(float(band[:, 1].mean()))
    print("  neck lateral curve, %d samples, y %.2f .. %.2f units "
          "(%.2f .. %.2f m)"
          % (len(xs), min(ys), max(ys), min(ys) / UNITS_PER_M,
             max(ys) / UNITS_PER_M))
    return np.array(xs), np.array(ys)


# --------------------------------------------------------------------------
# Building
# --------------------------------------------------------------------------

def existing_table(arm):
    """(head, tail) per bone off the armature already in the file."""
    return {b.name: (Vector(b.head_local), Vector(b.tail_local))
            for b in arm.data.bones}


def bone_table(old, limb, xs, ys):
    """(name, parent, head, tail, connect) for all 40 bones."""
    def lat(x):
        return float(np.interp(x, xs, ys))

    def bend(v):
        """Existing bone point, with only its Y re-derived from the mesh."""
        return (v.x, lat(v.x), v.z)

    out = [("Bone_Root", None,
            (PIVOT_X_M * UNITS_PER_M, 0.0, GROUND),
            (PIVOT_X_M * UNITS_PER_M + 1.63, 0.0, GROUND), False)]

    # Spine and pelvis: kept verbatim. They were measured for this sculpt and
    # the complaint was never about them.
    chain = ["Bone_Pelvis", "Bone_Spine_01", "Bone_Spine_02", "Bone_Spine_03"]
    prev = "Bone_Root"
    for name in chain:
        h, t = old[name]
        out.append((name, prev, tuple(h), tuple(t), prev != "Bone_Root"))
        prev = name

    # Neck, head and jaw: X and Z kept, Y taken from the mesh.
    for name in ["Bone_Neck_01", "Bone_Neck_02", "Bone_Neck_03",
                 "Bone_Neck_04", "Bone_Head"]:
        h, t = old[name]
        out.append((name, prev, bend(h), bend(t), True))
        prev = name
    h, t = old["Bone_Jaw"]
    out.append(("Bone_Jaw", "Bone_Head", bend(h), bend(t), False))

    prev = "Bone_Pelvis"
    for i in range(1, 6):
        name = "Bone_Tail_%02d" % i
        h, t = old[name]
        out.append((name, prev, tuple(h), tuple(t), i > 1))
        prev = name

    # The six limbs, on measured geometry. Middle legs hang off Spine_02:
    # their hips sit at x ~+1.9 and Spine_02 spans 0.18..2.36.
    parent_of = {"Front": "Bone_Spine_03", "Mid": "Bone_Spine_02",
                 "Rear": "Bone_Pelvis"}
    for leg in ["FrontP", "FrontS", "MidP", "MidS", "RearP", "RearS"]:
        spec = limb[leg]
        j = spec["joints"]
        parent = parent_of[leg[:-1]]
        for i, seg in enumerate(SEGMENTS):
            out.append(("Bone_%s_%s" % (leg, seg), parent, j[i], j[i + 1],
                        i > 0))
            parent = "Bone_%s_%s" % (leg, seg)
        out.append(("Bone_%s_Foot" % leg, parent, j[-1], spec["toe"], True))
    return out


def build_armature(table):
    data = bpy.data.armatures.new(ARM)
    arm = bpy.data.objects.new(ARM, data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    for name, parent, head, tail, connect in table:
        eb = data.edit_bones.new(name)
        eb.head, eb.tail = Vector(head), Vector(tail)
        eb.roll = 0.0
        if parent:
            eb.parent = data.edit_bones[parent]
            eb.use_connect = connect
        if (eb.tail - eb.head).length < 1e-4:
            raise SystemExit("zero-length bone: %s" % name)
    bpy.ops.object.mode_set(mode='OBJECT')
    arm.data.bones["Bone_Root"].use_deform = False
    print("  %d bones" % len(data.bones))
    return arm


def verify(mesh_obj, arm, limb, co):
    """Every vertex driven, every bone used, and each limb on its own chain."""
    deform = {b.name for b in arm.data.bones if b.use_deform}
    groups = {g.index: g.name for g in mesh_obj.vertex_groups}
    unweighted, totals = 0, {}
    for v in mesh_obj.data.vertices:
        w = [g for g in v.groups if g.weight > 1e-4
             and groups.get(g.group) in deform]
        if not w:
            unweighted += 1
        for g in w:
            totals[groups[g.group]] = totals.get(groups[g.group], 0.0) + g.weight
    print("  unweighted vertices: %d" % unweighted)
    missing = sorted(deform - set(totals))
    if missing:
        print("  BONES WITH NO INFLUENCE: %s" % ", ".join(missing))
    n = len(mesh_obj.data.vertices)
    for name, tot in sorted(totals.items(), key=lambda kv: -kv[1])[:4]:
        print("      %-22s drives %5.1f%% of total weight" % (name, 100 * tot / n))

    # The point of the whole exercise: each limb's own vertices must be driven
    # mostly by its own chain, not by a neighbour's.
    print("  per-limb ownership (share of each limb's weight on its own bones):")
    ok = True
    ed = np.empty(len(mesh_obj.data.edges) * 2, dtype=np.int64)
    mesh_obj.data.edges.foreach_get("vertices", ed)
    for leg, spec in limb.items():
        own = {"Bone_%s_%s" % (leg, s)
               for s in SEGMENTS + ["Foot"]}
        ax, ay, _ = spec["joints"][3]
        sel = [v for v in mesh_obj.data.vertices
               if v.co.z < M(1.30) and abs(v.co.x - ax) < 1.2
               and abs(v.co.y - ay) < 1.2]
        tot = sum(g.weight for v in sel for g in v.groups)
        mine = sum(g.weight for v in sel for g in v.groups
                   if groups.get(g.group) in own)
        share = mine / tot if tot else 0.0
        flag = "" if share > 0.80 else "   <-- TOO LOW"
        if share <= 0.80:
            ok = False
        print("    %-7s %3d verts   %.1f%% own%s" % (leg, len(sel), 100 * share, flag))
    if unweighted:
        raise SystemExit("%d vertices are not driven by any bone" % unweighted)
    if not ok:
        raise SystemExit("a limb is mostly driven by another limb's bones")
    return missing


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No input at %s" % SRC)
    bpy.ops.wm.open_mainfile(filepath=SRC)

    mesh_obj = bpy.data.objects[MESH]
    old_arm = bpy.data.objects.get(ARM)
    if old_arm is None:
        raise SystemExit("No %s in %s" % (ARM, SRC))
    old = existing_table(old_arm)

    import bmesh
    bm = bmesh.new()
    bm.from_mesh(mesh_obj.data)
    boundary = sum(1 for e in bm.edges if len(e.link_faces) < 2)
    nonmani = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    bm.free()
    print("  mesh %d verts, boundary %d, non-manifold %d"
          % (len(mesh_obj.data.vertices), boundary, nonmani))
    if boundary or nonmani:
        raise SystemExit(
            "Mesh is not closed (boundary %d, non-manifold %d). Bone heat needs "
            "a closed surface and silently drops bones without one."
            % (boundary, nonmani))

    co, ed = mesh_arrays(mesh_obj)
    before = co.copy()
    limb = limbs(co, ed)
    xs, ys = neck_lateral(co)

    # Detach and drop the old armature. The mesh object itself is kept, with
    # its transform, its UVs and its material untouched.
    mesh_obj.parent = None
    mesh_obj.matrix_world = mesh_obj.matrix_world.copy()
    for m in [m for m in mesh_obj.modifiers if m.type == 'ARMATURE']:
        mesh_obj.modifiers.remove(m)
    for g in list(mesh_obj.vertex_groups):
        mesh_obj.vertex_groups.remove(g)
    bpy.data.objects.remove(old_arm, do_unlink=True)

    arm = build_armature(bone_table(old, limb, xs, ys))

    bpy.ops.object.select_all(action='DESELECT')
    mesh_obj.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    print("  skinned: %d vertex groups" % len(mesh_obj.vertex_groups))

    # `parent_set` appends the Armature modifier to the END of the stack, which
    # would put it after the Subdivision the user added by hand. That order is
    # wrong and it is not what was in the input file: subdividing first and
    # deforming the dense result is both more expensive and worse-looking than
    # deforming the cage and smoothing the deformation. Restore Armature first.
    names = [m.name for m in mesh_obj.modifiers]
    arm_mod = next(m for m in mesh_obj.modifiers if m.type == 'ARMATURE')
    if names.index(arm_mod.name) != 0:
        bpy.context.view_layer.objects.active = mesh_obj
        bpy.ops.object.modifier_move_to_index(modifier=arm_mod.name, index=0)
        print("  modifier order %s -> %s"
              % (names, [m.name for m in mesh_obj.modifiers]))
    kept = [(m.type, getattr(m, "levels", None), getattr(m, "render_levels", None))
            for m in mesh_obj.modifiers if m.type != 'ARMATURE']
    if kept:
        print("  hand-added modifiers preserved: %s" % kept)

    verify(mesh_obj, arm, limb, co)

    after, _ = mesh_arrays(mesh_obj)
    moved = float(np.abs(after - before).max())
    print("  mesh vertex drift through rigging: %.9f  (must be 0)" % moved)
    if moved > 1e-9:
        raise SystemExit("the mesh moved -- hand-sculpted work would be lost")

    print("  foot pads: %s" % FOOT)
    A.save(OUT)


if __name__ == "__main__":
    main()
