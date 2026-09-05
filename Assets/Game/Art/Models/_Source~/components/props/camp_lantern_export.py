"""Ship the camp lantern to Unity.

    blender --background --python components/props/camp_lantern_export.py

No armature: nothing on it articulates. The `LIGHT_Flame` empty is exported
because the builder reads it to place the Light -- guessing that offset in C#
would put the glow outside the glass the first time the model changed.

Exports are meant to be re-run; this only ever reads the .blend.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import export, unity_path  # noqa: E402

export(os.path.join(HERE, "camp_lantern.blend"),
       unity_path("Items", "camp_lantern.fbx"),
       keep_armature=False)
