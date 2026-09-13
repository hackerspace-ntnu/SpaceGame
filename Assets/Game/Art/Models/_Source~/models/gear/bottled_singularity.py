"""Bottled singularity — a thrown flask with a knot of nothing in it.

Design: `docs/AI/systems/Artifacts/BottledSingularity.md`. Throw it, it lands,
the collar's iris opens, everything within 8 m is dragged in for 3 s, then it
lets go all at once. It does not know who threw it.

The read the model has to earn is **"that is the one that pulls"**, from across
a room and from a 256 px icon, distinguished from the storm flask which is the
same kit, the same manufacturer, and the same 0.2 m. Three things do that work,
in the order the eye takes them:

1. **Silhouette.** Squat and wide — a puck you palm — against the storm flask's
   tall shouldered bottle. Silhouette is the only axis that survives being small,
   dark, or in motion, which is why it is the axis the two bottles differ on
   first rather than the colour.
2. **The black core.** A flask this kit is issued with has a window; what is
   behind the window is the payload. A void reads as a hole punched in the
   object, and nothing else in the kit is a hole.
3. **Blue.** The colour code, last, because it is the channel that fails first —
   in a sandstorm, at night, and for the ~8% of players who cannot separate it
   from the storm flask's yellow (`GDC-L1-UX-0003`: never encode critical
   information in colour alone).

Assembled in **flask space**: the origin is the base of the bottle on its axis,
+Z up, the same frame `flask_body.py` authors in. Nothing is re-origined at the
end, because unlike a weapon this thing has no grip that the rest should be
measured from — it is a bottle, it stands on a table, and the base is where it
touches.

Moving parts, and why there is no armature: the **six iris leaves** each turn
about their own pin and carry that pin as their object origin, and the **core**
swells and snaps flat as one uniform scale about its own centre. Seven rigid
transforms about seven fixed axes is what an object pivot is for; an armature
would be a bone hierarchy storing the same seven numbers with a skinning
evaluation attached. Same call, for the same reason, as `dragon_bazooka.py`'s
jaw and `sucker_puncher.py`'s ram.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import os
import sys

from mathutils import Matrix

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, os.path.join(_LIB, "components", "props"))

from _buildlib import *  # noqa: E402,F403
from _tracked import TrackedPart  # noqa: E402
from flask_kit import marker, place, sphere  # noqa: E402

BODY_BLEND = os.path.join(_LIB, "components", "props", "flask_body.blend")
COLLAR_BLEND = os.path.join(_LIB, "components", "props", "flask_collar.blend")

BODY = ["Mesh_FlaskBody_Squat_Shell", "Mesh_FlaskBody_Squat_Cowl",
        "Mesh_FlaskBody_Squat_Window", "Mesh_FlaskBody_Squat_Bumper"]
COLLAR = ["Mesh_FlaskCollar_Iris_Ring"] + \
         ["Mesh_FlaskCollar_Iris_Leaf_%d" % i for i in range(1, 7)]

# Index 0 is a structural metal because `bmesh.ops.bevel` stamps material index
# 0 on every face it makes, and a forgotten `mat=` argument lands there too.
STEEL, VOID, BLUE, RIM, CHROME = range(5)
MATS = ["Mat_Metal_Steel_Worn",       # bevel guard, band substrate
        "Mat_Neutral_Black_Matte",    # the core: the darkest thing in the kit
        "Mat_Neutral_Slate_Dark",     # the colour code
        "Mat_Emissive_Portal_Blue",   # the rim light behind the glass
        "Mat_Metal_Chrome_Scuffed"]   # band edge

# The colour code is a near-black navy, not Mat_Paint_Blue_Station. The pale
# powder blue was tried first and washed out against the arctic-white shell at
# any distance: the two are 0.6 apart in value, so the band disappeared and the
# only blue left was the 2 mm rim light. Slate against Arctic is a value
# contrast, which is the half of the signal that survives being colour-blind,
# small, or in a sandstorm (`GDC-L1-UX-0003`). The storm flask's band is a warm
# light yellow for the same reason, mirrored: the two bottles differ in value
# even in greyscale, not only in hue.

SEAT_Z = 0.168                  # where Coll_FlaskBody_Squat presents its neck
WINDOW = (0.083, 0.124)         # the glass band on the squat body
CORE_Z = (WINDOW[0] + WINDOW[1]) / 2.0
CORE_R = 0.040                  # 17 mm of clear space inside the glass, so the
                                # core can swell before it touches anything


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    model = collection("Coll_BottledSingularity")

    # Palette materials are linked before the appends so the appended objects
    # bind to the same datablocks instead of dragging in a second copy.
    for obj in append_objects(BODY_BLEND, BODY, model):
        place(obj, Matrix.Identity(4))

    # The collar rides up to the neck. Each leaf keeps its own pin as its
    # origin through the move — `place(origin=...)` with the *moved* pivot, not
    # the component's, or the iris opens about a point 168 mm below itself.
    lift = Matrix.Translation((0.0, 0.0, SEAT_Z))
    for obj in append_objects(COLLAR_BLEND, COLLAR, model):
        place(obj, lift, origin=lift @ obj.matrix_world.to_translation())

    # ── The core ──
    # Its own object with its origin at its own centre, because the artifact
    # scales it: it swells through the inhale and snaps flat at the release,
    # which is the only warning anyone standing nearby gets that the fling is
    # coming (`GDC-L1-ANIM-0003` — the telegraph is a gameplay property).
    core = TrackedPart(mats)
    sphere(core, (0.0, 0.0, CORE_Z), CORE_R, VOID)
    core.restamp("core")
    core.finish("Mesh_BottledSingularity_Core", model,
                origin=(0.0, 0.0, CORE_Z))

    # ── Rim light ──
    # Two thin emissive rings, at the top and bottom edge of the glass band, so
    # the window is outlined from every angle a tumbling bottle presents. A
    # single ring reads as a stripe from half the compass and as nothing from
    # the other half.
    rim = TrackedPart(mats)
    rim.torus((0, 0, WINDOW[0] + 0.0025), 0.0606, 0.0022, 'Z', 28, 8, RIM)
    rim.torus((0, 0, WINDOW[1] - 0.0025), 0.0606, 0.0022, 'Z', 28, 8, RIM)
    rim.restamp("rim light")
    rim.finish("Mesh_BottledSingularity_RimLight", model, origin=(0, 0, CORE_Z))

    # ── Colour-code band ──
    # Embedded 2 mm into the shell and standing 1.2 mm proud of it. A band flush
    # with the surface it decorates is two coplanar faces, which flicker; a band
    # buried inside it renders as nothing at all.
    band = TrackedPart(mats)
    band.tube((0, 0, 0.017), 0.0672, 0.0032, 0.022, 'Z', 28, BLUE)
    band.torus((0, 0, 0.0268), 0.0668, 0.0012, 'Z', 28, 6, CHROME)
    band.torus((0, 0, 0.0072), 0.0668, 0.0012, 'Z', 28, 6, CHROME)
    band.restamp("band")
    band.finish("Mesh_BottledSingularity_Band", model, origin=(0, 0, 0.017))

    # ── Markers, in flask space ──
    # Read by the wave-2 prefab: where the hand closes, what the throw arc
    # rotates about, and where the closure is.
    marker(model, "Marker_Grip", (0.0, 0.0, 0.055))
    marker(model, "Marker_ThrowPivot", (0.0, 0.0, 0.090))
    marker(model, "Marker_Cap", (0.0, 0.0, SEAT_Z))

    report()
    save(out)


if __name__ == "__main__":
    main()
