"""Ship BOTH worn wing packs to Unity.

The item has two worn shapes and the player sees a different one in each place:

| .blend | .fbx | pose | where |
| --- | --- | --- | --- |
| `ornithopter_worn` | `ornithopter_worn.fbx` | stowed, 1.97 m | out in the world |
| `ornithopter_worn_on_person` | `ornithopter_worn_on_person.fbx` | open, 5.51 m | the gear screen |

The names read backwards — the *first* is the one worn in ordinary play — and
that is a historical artefact of the second file's name, not a mistake here.
`WingPackBuilder` nests them as `WornModel` and `InspectModel` respectively.

Twelve static objects each, no armature: the worn form never articulates. It is
a display of equipment, and the thing that moves under it is the wearer's spine.

The origin is load-bearing and is why this prints its bounds. `WornSeat` puts a
back item's origin on the expedition rig's lash rail, and both models are
authored with their origin exactly there and their two shoulder pivots reaching
out to the rail's two protruding bar tips at x = ±0.885 m. That shared mount is
what lets the two be swapped for one another with nothing moving. So each FBX
must arrive at TRUE WEARER SCALE and must not be rescaled in Unity — the sizes
printed below are what the prefab's `WornFit.size` and `WornFit.inspectSize` are
pinned to, so a re-export that changes a span is caught by a number disagreeing
rather than by the wings quietly drifting off the bar.

Exports are meant to be re-run; this only ever reads the .blend files.

    blender --background --python models/vehicles/ornithopter_worn_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import export, to_unity, unity_path  # noqa: E402

# (source stem, which WornFit field its span is pinned to, what it is)
MODELS = [
    ("ornithopter_worn", "WornFit.size", "STOWED — worn in ordinary play"),
    ("ornithopter_worn_on_person", "WornFit.inspectSize", "OPEN — worn on the gear screen"),
]


def ship(stem, field, what):
    src = os.path.join(HERE, "%s.blend" % stem)
    dst = unity_path("Vehicles", "Ornithopter", "%s.fbx" % stem)
    print("\n=== %s  (%s)" % (stem, what))
    export(src, dst, keep_armature=False)

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']

    lo = [1e9, 1e9, 1e9]
    hi = [-1e9, -1e9, -1e9]
    for obj in meshes:
        m = obj.matrix_world
        for v in obj.data.vertices:
            w = m @ v.co
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])
    size = [hi[i] - lo[i] for i in range(3)]
    print("  SPAN  %.4f m  (depth %.4f, height %.4f)" % (size[0], size[1], size[2]))
    print("  BOUNDS blender lo (%.4f, %.4f, %.4f) hi (%.4f, %.4f, %.4f)" % (*lo, *hi))
    print("  -> pin %s to %.2f on WingPack.prefab" % (field, size[0]))

    # The mount, and the reason the two models are interchangeable. Every part's
    # origin is its own side's rail tip in BOTH files; if these ever disagree
    # between the two, swapping one for the other shifts the wings off the bar.
    roots = sorted({round(abs(o.location.x), 4) for o in meshes})
    print("  ORIGINS on x = ±%s  (the rail tips; ±0.885 with the clamps at ±0.83)"
          % ", ±".join("%.4f" % r for r in roots))
    for obj in sorted(meshes, key=lambda o: o.name):
        loc = obj.location
        print("  ORIGIN %-34s blender (%.4f, %.4f, %.4f)  unity (%.4f, %.4f, %.4f)"
              % (obj.name, loc.x, loc.y, loc.z, *to_unity(loc)))

    # Handedness: a negative-determinant object exports as a negative scale and
    # Unity renders it inside-out with a clean console. The .blend was built with
    # the winding already repaired (`ornithopter_worn.py: flatten`), so this is a
    # guard rather than a fix — if it ever prints, the fix belongs upstream.
    bad = [o.name for o in meshes if o.matrix_world.to_3x3().determinant() < 0.0]
    if bad:
        raise SystemExit("Inside-out after export: %s" % ", ".join(sorted(bad)))
    print("  handedness: %d object(s), all positive-determinant" % len(meshes))
    return len(meshes)


def main():
    counts = [ship(*m) for m in MODELS]

    # The two are one machine in two poses, so they must carry the same parts. A
    # mismatch means one of them was rebuilt from a changed cull and the other
    # was not, and the symptom in game is a part that appears and disappears
    # when the gear screen opens.
    if len(set(counts)) != 1:
        raise SystemExit("The two worn models disagree on part count: %s" % counts)
    print("\nBoth worn models shipped: %d objects each." % counts[0])


main()
