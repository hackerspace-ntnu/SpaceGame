"""Ship the jetpack to Unity — both of its models, out of one .blend.

The file holds two arrangements of the same hand-built pod, in two collections
(`jetpack_mirror.py` builds them and `jetpack_BUILD.md` records the numbers):

* `Coll_Jetpack_Worn` — a pod on each lash-rail tip, 1.785 m apart. The worn
  model, clipped to the expedition rig's bar.
* `Coll_Jetpack_Item` — the same pair pushed together, 0.03 m apart. The model
  carried in a hand and lying on the pack mat.

`keep_collection` is what makes one source file two FBXs. It resolves the filter
after the file is open, so a part the user adds to either collection ships
without anything being retyped here.

Three flags this export cannot lose:

**`keep_empties=True`.** `MOUNT_Jetpack_R` / `_L` (and `ITEM_Jetpack_R` / `_L`)
are the pod roots, and every part of a pod hangs under one. `JetpackNozzles`
rotates the vectoring parts inside each pod and needs that grouping — without
the empties the FBX is 32 loose meshes with no way to say which pod a nozzle
belongs to, and `JetpackBuilder` would have to guess from positions.

**`use_mesh_modifiers` (on inside `export`).** The two exhausts per pod are
geometry nodes and the whole model carries `BEVEL`. Unevaluated, the exhausts
export as bare planes.

**`bake_space_transform=False`** (also inside `export`, and shared by every
model here). The axis conversion rides on the NODE, so vertices keep Blender's
frame — which is why `JetpackNozzles` measures the pod's thrust axis off the
geometry rather than naming one, and why a yaw the user changes in Blender must
never become a constant in C#.

The printed numbers are what the Unity side binds to. `holdSize` is the item
pair's longest axis, `WornFit.size` the worn pair's, and both are typed onto the
prefab by `JetpackBuilder` — printing them here is what turns a re-export that
changed the scale into a number that disagrees rather than a pack that quietly
drifts off the rail.

Exports are meant to be re-run; this only ever reads the .blend.

    blender --background --python models/gear/jetpack_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import export, to_unity, unity_path  # noqa: E402

SRC = os.path.join(HERE, "jetpack.blend")

# Worn first, item second, so the file left open at the end — and therefore the
# bounds printed last — is the one whose numbers go on ItemGrip.
JOBS = [
    ("Coll_Jetpack_Worn", unity_path("Items", "jetpack_worn.fbx"), "WORN"),
    ("Coll_Jetpack_Item", unity_path("Items", "jetpack.fbx"), "ITEM"),
]

# The parts each pod vectors, by the role in their name. Printed so a rename in
# Blender shows up here as a missing part rather than as a nozzle that silently
# stops moving in game.
VECTORED = ("NozzleYoke", "NozzleInnerCone", "NozzleInnerCollar",
            "NozzleOuterCone", "NozzleOuterCollar")

# Where the flames are drawn. Two per pod, four in total.
EXHAUSTS = ("ExhaustInner", "ExhaustOuter")


def bounds(objs):
    """World-space min/max over evaluated geometry, in Blender's frame."""
    depsgraph = bpy.context.evaluated_depsgraph_get()

    lo = [1e9] * 3
    hi = [-1e9] * 3
    for obj in objs:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        for vertex in mesh.vertices:
            world = evaluated.matrix_world @ vertex.co
            for axis in range(3):
                lo[axis] = min(lo[axis], world[axis])
                hi[axis] = max(hi[axis], world[axis])
        evaluated.to_mesh_clear()

    return lo, hi


def report(label):
    """Everything the Unity side has to agree with, measured after the export."""
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    lo, hi = bounds(meshes)
    size = [hi[i] - lo[i] for i in range(3)]

    print("\n  %s ------------------------------------------------" % label)
    print("    blender bounds  %.4f x %.4f x %.4f m" % tuple(size))
    print("    unity   size    %.4f x %.4f x %.4f m"
          % tuple(abs(v) for v in to_unity(size)))
    print("    longest axis    %.4f m   <- holdSize / WornFit.size" % max(size))

    for obj in sorted((o for o in bpy.data.objects if o.type == 'EMPTY'),
                      key=lambda o: o.name):
        loc = obj.matrix_world.translation
        print("    pivot %-22s blender (%.4f, %.4f, %.4f)  unity (%.4f, %.4f, %.4f)"
              % ((obj.name,) + tuple(loc) + to_unity(loc)))

    names = {o.name for o in meshes}
    for role in VECTORED + EXHAUSTS:
        found = sorted(n for n in names if ("_%s_" % role) in n or n.endswith("_%s" % role)
                       or ("Jetpack_%s" % role) in n)
        print("    %-18s %d part(s)%s"
              % (role, len(found), "" if found else "   <-- MISSING, check the .blend"))


def main():
    for collection, destination, label in JOBS:
        export(SRC, destination, keep_armature=False, keep_empties=True,
               keep_collection=collection)
        report(label)


main()
