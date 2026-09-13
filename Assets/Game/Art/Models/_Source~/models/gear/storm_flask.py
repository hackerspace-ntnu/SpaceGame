"""Storm flask — uncork it and a thundercloud parks itself over the target.

Design: `docs/AI/systems/Artifacts/StormFlask.md`. A cloud forms 15 m above the
aimed point, rains for 30 s, puts fires out, wets the ground, and every 2.5 s
hits the tallest body under it with a bolt. It has no idea who you are.

Same kit, same manufacturer and the same 0.2 m as the bottled singularity, and
the whole job of this model is to not be mistaken for it:

1. **Silhouette.** Tall and shouldered against the singularity's squat puck —
   a bottle that stands up, so it reads as different at the size a hotbar slot
   draws it. Silhouette first, because it is the channel that survives motion
   and darkness.
2. **A stopper on top.** The singularity's closure is a flush iris disc; this
   one has a plug and a pull ring standing 30 mm proud. That is an affordance
   the player already knows how to read — a thing with a ring on it is a thing
   you pull (`GDC-L1-UX-0004`) — and it is visible in profile, which colour
   never is.
3. **Warm light against cold.** The colour code is a light warm yellow band and
   the light inside the glass is amber, where the singularity is a near-black
   navy band and cold cyan. The pair differ in *value* as well as hue, so they
   stay distinguishable in greyscale and for a colour-blind player
   (`GDC-L1-UX-0003`).

Assembled in **flask space**: origin at the base of the bottle on its axis, +Z
up, the frame `flask_body.py` authors in.

Moving parts, and why there is no armature: the **stopper** pops straight up and
its origin sits on the seat so that is one local Z translation, the **pull ring**
rides with it on the same origin, and the **core** shrinks as the cloud forms,
about its own centre. Three rigid transforms about fixed axes — an object pivot's
job, not a skeleton's. Same call as `dragon_bazooka.py`'s jaw.

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

BODY = ["Mesh_FlaskBody_Shouldered_Shell", "Mesh_FlaskBody_Shouldered_Cowl",
        "Mesh_FlaskBody_Shouldered_Window", "Mesh_FlaskBody_Shouldered_Bumper"]
COLLAR = ["Mesh_FlaskCollar_Stopper_Ring", "Mesh_FlaskCollar_Stopper_Plug",
          "Mesh_FlaskCollar_Stopper_PullRing"]

# Index 0 is a structural metal because `bmesh.ops.bevel` stamps material index
# 0 on every face it makes, and a forgotten `mat=` argument lands there too.
STEEL, VAPOUR, YELLOW, ARC, CHROME = range(5)
MATS = ["Mat_Metal_Steel_Worn",       # bevel guard, band substrate
        "Mat_Neutral_Panel_Grey",     # the churning core behind the glass
        "Mat_Paint_Hazard_Yellow",    # the colour code
        "Mat_Emissive_Amber",         # the arc filament threading the core
        "Mat_Metal_Chrome_Scuffed"]   # band edge

SEAT_Z = 0.138                  # where Coll_FlaskBody_Shouldered presents its neck
WINDOW = (0.075, 0.113)         # the glass band on the shouldered body
CORE_Z = (WINDOW[0] + WINDOW[1]) / 2.0
CORE_R = 0.029                  # inside the glass bore (0.040) with room to drain


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    model = collection("Coll_StormFlask")

    # Palette materials are linked before the appends so the appended objects
    # bind to the same datablocks instead of dragging in a second copy.
    for obj in append_objects(BODY_BLEND, BODY, model):
        place(obj, Matrix.Identity(4))

    # The closure rides up to the neck. The plug and the pull ring both keep the
    # seat as their origin through the move, which is what leaves the pop as one
    # local +Z translation applied to two objects.
    lift = Matrix.Translation((0.0, 0.0, SEAT_Z))
    for obj in append_objects(COLLAR_BLEND, COLLAR, model):
        place(obj, lift, origin=lift @ obj.matrix_world.to_translation())

    # ── The core ──
    # Its own object with its origin at its own centre, because the artifact
    # scales it: the flask visibly empties as the cloud forms, which is the one
    # piece of feedback that says the charge is spent without a HUD element
    # (`GDC-L1-ANIM-0003`).
    core = TrackedPart(mats)
    sphere(core, (0.0, 0.0, CORE_Z), CORE_R, VAPOUR)
    core.restamp("core")
    core.finish("Mesh_StormFlask_Core", model, origin=(0.0, 0.0, CORE_Z))

    # ── Arc filament ──
    # Two crossed emissive rings in the gap between the core and the glass, so
    # there is light moving inside the bottle from every angle. Separate from the
    # core because it must not shrink with it — the storm drains, the lightning
    # does not dim until it is gone.
    arc = TrackedPart(mats)
    arc.torus((0, 0, CORE_Z), 0.0340, 0.0015, 'X', 24, 6, ARC)
    arc.torus((0, 0, CORE_Z), 0.0340, 0.0015, 'Y', 24, 6, ARC)
    arc.restamp("arc")
    arc.finish("Mesh_StormFlask_Arc", model, origin=(0, 0, CORE_Z))

    # ── Colour-code band ──
    # Embedded 2 mm into the shell and standing 1.2 mm proud of it. Flush is two
    # coplanar faces and flickers; buried renders as nothing at all.
    band = TrackedPart(mats)
    band.tube((0, 0, 0.016), 0.0502, 0.0032, 0.020, 'Z', 28, YELLOW)
    band.torus((0, 0, 0.0252), 0.0498, 0.0012, 'Z', 28, 6, CHROME)
    band.torus((0, 0, 0.0068), 0.0498, 0.0012, 'Z', 28, 6, CHROME)
    band.restamp("band")
    band.finish("Mesh_StormFlask_Band", model, origin=(0, 0, 0.016))

    # ── Markers, in flask space ──
    marker(model, "Marker_Grip", (0.0, 0.0, 0.045))
    marker(model, "Marker_ThrowPivot", (0.0, 0.0, 0.080))
    marker(model, "Marker_Cap", (0.0, 0.0, SEAT_Z))

    report()
    save(out)


if __name__ == "__main__":
    main()
