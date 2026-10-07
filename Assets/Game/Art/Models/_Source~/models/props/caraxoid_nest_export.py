"""Export caraxoid_nest.blend to Props/caraxoid_nest.fbx.

    blender --background --python caraxoid_nest_export.py

The Caraxoid mother's nest: a 12 m ring of branches lined with hide, with a clutch of four eggs set
off-centre so they stay visible beside her. Static set dressing, no rig.
"""
import os
import sys

LIB = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
sys.path.insert(0, LIB)
from _exportlib import export, unity_path  # noqa: E402

export(os.path.join(LIB, "models", "props", "caraxoid_nest.blend"), unity_path("Props", "caraxoid_nest.fbx"))
