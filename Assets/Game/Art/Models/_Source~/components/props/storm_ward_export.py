"""Ship the storm ward to Unity.

    blender --background --python components/props/storm_ward_export.py

No armature: nothing on it articulates. The `FX_Emitter` and `LIGHT_Core` empties
are exported because `StormWardBuilder` reads them to place the shockwave and the
core's light.

Exports are meant to be re-run; this only ever reads the .blend.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import export, unity_path  # noqa: E402

export(os.path.join(HERE, "storm_ward.blend"),
       unity_path("Items", "storm_ward.fbx"),
       keep_armature=False, keep_empties=True)
