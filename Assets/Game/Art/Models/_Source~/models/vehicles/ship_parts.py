"""The lander's removable hull modules, named once for everything that consumes them.

`player_ship_export.py` renames these meshes in the exported ship so PlayerShipBuilder can find
them, and `ship_parts_export.py` exports each kind again on its own as the item the player carries.
Both need the same list, and a copy in each would drift the first time the user renames a mesh.

The suffix is DERIVED, not authored: a mirrored pair is sorted on X and gets `_A` (lower X) and
`_B`. The names already in the .blend cannot be trusted for this — `Turbine_Ducted_Stbd.001` is a
*long* turbine sitting on the opposite flank from `Turbine_Long_Stbd`, and both carry "Stbd".
"""

ROLE_PREFIX = "Part_"

# kind -> the raw .blend object names that are that kind. A kind with two entries is a mirrored
# pair sharing one socket type, so one carried item fits either mount.
PART_KINDS = [
    ("AntiGravity",  ["anti_gravity"]),
    ("NuclearMotor", ["nuclear_motor", "nuclear_motor.001"]),
    ("ReactorCore",  ["reactor_core", "reactor_core.001"]),
    ("SmallMotor",   ["small_turbine", "Turbine_Stub_BellyPort"]),
    ("AirIntake",    ["air_intake"]),
    ("LongTurbine",  ["Turbine_Long_Stbd", "Turbine_Ducted_Stbd.001"]),
    ("Gun",          ["gun"]),
]

SIDES = "ABCDEFGH"


def raw_names():
    """Every .blend object name the parts feature depends on."""
    return [raw for _, raws in PART_KINDS for raw in raws]


def role_names(objects):
    """Map raw .blend name -> `Part_<Kind>_<Side>`, given a name -> object lookup.

    Raises if a listed mesh is absent: the .blend is the user's hand-built file and it does change,
    so a missing part must stop the export rather than quietly ship a ship with no sockets.
    """
    roles = {}
    for kind, raws in PART_KINDS:
        missing = [raw for raw in raws if raw not in objects]
        if missing:
            raise SystemExit(
                "ship_parts: the .blend has no mesh named %s (kind '%s'). It was renamed or "
                "removed — update PART_KINDS in ship_parts.py." % (", ".join(missing), kind))

        for side, raw in enumerate(sorted(raws, key=lambda n: objects[n].location.x)):
            roles[raw] = "%s%s_%s" % (ROLE_PREFIX, kind, SIDES[side])
    return roles


def item_source(objects, raws):
    """Which of a kind's meshes to export as the carried item.

    A mirrored copy has a negative-determinant world matrix; exported on its own its winding is
    inside out, which is the same flaw that made two of this hull's belly tracks invisible from one
    side. Prefer an unmirrored twin when there is one.
    """
    unmirrored = [n for n in raws if objects[n].matrix_world.determinant() > 0]
    return sorted(unmirrored or raws)[0]
