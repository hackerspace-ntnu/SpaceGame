"""Export the gear kit pieces the game uses: one FBX per collection, each on its own origin.

    blender --background --python gear_kit_export.py

Writes to Assets/Game/Art/Models/Props/Gears/. Every gear lands with its origin on the centre of its back face (the
seat) and turns about Unity's Z, its front facing +Z; the peg's origin is on the face it is bolted to. Read only:
the .blend is never saved.
"""
import os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.abspath(os.path.join(HERE, '..', '..')))
import _exportlib

SRC = os.path.join(HERE, 'gear_kit.blend')
OUT = _exportlib.unity_path('Props', 'Gears')

# collection -> fbx name. Carryable gears are brass and copper; what is fixed to a machine is iron or rust.
JOBS = (
    ('Coll_Gear_Puzzle_XS', 'gear_xs'),
    ('Coll_Gear_Puzzle_S', 'gear_s'),
    ('Coll_Gear_Puzzle_M', 'gear_m'),
    ('Coll_Gear_Puzzle_L', 'gear_l'),
    ('Coll_Gear_Puzzle_XL', 'gear_xl'),
    ('Coll_Gear_Deco_Flywheel', 'gear_flywheel'),
    ('Coll_Gear_Deco_Skeleton', 'gear_skeleton'),
    ('Coll_Gear_Fixed_S', 'gear_fixed_s'),
    ('Coll_Gear_Fixed_M', 'gear_fixed_m'),
    ('Coll_Gear_Peg', 'gear_peg'),
)

_exportlib.export_collections(SRC, [(coll, os.path.join(OUT, name + '.fbx')) for coll, name in JOBS])
