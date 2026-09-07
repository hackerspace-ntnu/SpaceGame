"""Shared numbers for the issued-equipment device family.

The family is nine hand devices built across several sessions — vacuum canister,
inflator nozzle, strap-on booster, and six more by other hands. They are meant to
read as *one kit*: pale moulded shells, one saturated colour per function, and a
`SupplyGauge` face wherever the thing holds a reservoir.

This module holds the two things that must not diverge between them: the material
list every part is built against, and the bevel width that decides how moulded a
shell looks. A second copy of either is how a family ends up with two whites and
four bevels (`GDC-L1-CONTENT-0003`).

Import it the way the component scripts import `_buildlib` — by path, since these
run under `blender --background`:

    sys.path.insert(0, "<lib>/components/props")
    from device_kit import MATS, SHELL, BEVEL_W

**Index 0 must stay a structural metal.** `bmesh.ops.bevel` stamps material index
0 onto every face it creates, so whatever sits at 0 is what every bevelled edge in
the family wears.
"""

# One list shared by every device component and every device model, so a part
# appended from one file into another lands on the same slot it was built with.
# Append only: inserting in the middle silently recolours everything downstream.
(STEEL, DARK, RUBBER, CHROME, SHELL, GREY, BLACK, SLATE, CRT, AMBER,
 REDLAMP, BLUE, YELLOW, RED, HAZARD, BRASS, GLASS, CYAN, RUST, CANVAS) = range(20)

MATS = [
    "Mat_Metal_Steel_Worn",         # 0  STEEL   — and every bevelled edge
    "Mat_Metal_Steel_Dark",         # 1  DARK
    "Mat_Plastic_Rubber_Black",     # 2  RUBBER
    "Mat_Metal_Chrome_Scuffed",     # 3  CHROME
    "Mat_Paint_White_Arctic",       # 4  SHELL   — the family's moulded shell
    "Mat_Neutral_Panel_Grey",       # 5  GREY
    "Mat_Neutral_Black_Matte",      # 6  BLACK
    "Mat_Neutral_Slate_Dark",       # 7  SLATE
    "Mat_Emissive_Green_CRT",       # 8  CRT     — the SupplyGauge fill strip
    "Mat_Emissive_Amber",           # 9  AMBER
    "Mat_Emissive_Red_Warn",        # 10 REDLAMP
    "Mat_Paint_Blue_Station",       # 11 BLUE    — vacuum canister: containment
    "Mat_Plastic_Safety_Yellow",    # 12 YELLOW  — inflator nozzle: pressure
    "Mat_Paint_Warn_Red",           # 13 RED     — strap-on booster: propellant
    "Mat_Paint_Hazard_Yellow",      # 14 HAZARD  — the booster's striped band
    "Mat_Metal_Brass_Tarnished",    # 15 BRASS   — the inflator's nozzle collar
    "Mat_Glass_Canopy_Tinted",      # 16 GLASS   — the canister window
    "Mat_Emissive_Portal_Blue",     # 17 CYAN    — a live containment field
    "Mat_Metal_Rust_Heavy",         # 18 RUST    — scorch on a spent booster
    "Mat_Fabric_Canvas_Faded",      # 19 CANVAS  — webbing straps
]

# Wide enough to read as a moulding at arm's length rather than as an edge break,
# and small enough that a 20 mm fitting does not swell into its neighbour. The
# oxygen bottle's 5 mm is tuned for a 0.54 m vessel; these are hand devices at
# roughly half that, so the chamfer halves with them.
BEVEL_W = 0.0025

# Segments per bevel. Two is what the whole library uses; a third buys nothing at
# the distance a held item is seen from and doubles the edge cost.
BEVEL_SEG = 2

# Barrel resolution for anything the player holds close. Sixteen is the library's
# default and reads faceted on a 40 mm tube in first person; twenty does not.
SEG = 20
