"""Export one rigged Strider character from strider_characters.blend into
Assets/Game/Art/Models/Characters/Striders/.

    blender --background --python strider_characters_export.py -- <Name>

<Name> is Horned, Warrior, Longcoat, Beanie or StriderElder. One character per run.

The humanoids ship `scale_all=True` like the drifters: with FBX_SCALE_NONE every bone imports at
scale 100, which anything parented to a hand inherits. The elder ships `FBX_SCALE_NONE` like the
crab walkers it shares a locomotion stack with: its parts hang off bones rather than being skinned.
All ship triangles only (`triangulate=True`): the kit's n-gons, baked through a subdivision, are
exactly the kind Unity discards as self-intersecting (ArtPipeline gotcha).
"""

import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")))
from _exportlib import export, unity_path  # noqa: E402

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "strider_characters.blend")

# Name -> (collection, FBX file, humanoid?)
CHARACTERS = {
    "Horned": ("Char_Horned", "strider_horned.fbx", True),
    "Warrior": ("Char_Warrior", "strider_warrior.fbx", True),
    "Longcoat": ("Char_Longcoat", "strider_longcoat.fbx", True),
    "Beanie": ("Char_Beanie", "strider_beanie.fbx", True),
    "StriderElder": ("Char_StriderElder", "strider_elder.fbx", False),
}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if len(argv) != 1 or argv[0] not in CHARACTERS:
        raise SystemExit("usage: ... -- <%s>" % "|".join(CHARACTERS))
    collection, fbx, humanoid = CHARACTERS[argv[0]]
    export(SRC, unity_path("Characters", "Striders", fbx), keep_armature=True, keep_collection=collection,
           scale_all=humanoid, triangulate=True)


if __name__ == "__main__":
    main()
