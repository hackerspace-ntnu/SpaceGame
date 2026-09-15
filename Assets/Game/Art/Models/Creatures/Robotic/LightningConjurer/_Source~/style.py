# Materials for the Lightning Conjurer.
#
# The body arrived with essentially no materials at all -- 37 of the 52 exported
# parts had no material slot and 11 had an empty one -- so this is additive work
# rather than a repaint. The only pre-existing assignments are the eye/eyelid and
# two cable curves; their original datablocks are kept in the file with a fake
# user so nothing is destroyed.
#
# Every material is LINKED from Assets/Game/Art/Models/_Source~/palette.blend,
# which is the convention every other model in the library follows.
#
# The read: a scavenged steel walker that carries a charge. Cold dark steel body,
# warm tarnished brass at every joint so the limbs articulate visibly, verdigris
# copper for the cable runs (the palette calls that one "coil windings", which is
# exactly what a lightning conjurer's cables are), and cyan Portal_Blue emissive
# for the three things that should look powered: the eye, the palm emitters, and
# the floating halo.
import bpy, os

PALETTE_REL = "//../../../_Source~/palette.blend"

STEEL_D  = "Mat_Metal_Steel_Dark"        # 3A3E42 machined mechanism metal
STEEL_W  = "Mat_Metal_Steel_Worn"        # 7A7D80 bare structural steel
BRASS    = "Mat_Metal_Brass_Tarnished"   # 9C7B3F bearing collars, joint pins
CHROME   = "Mat_Metal_Chrome_Scuffed"    # C9CDD2 bright trim
COPPER   = "Mat_Metal_Copper_Oxide"      # 4E8C7A verdigris coil windings
SLATE    = "Mat_Neutral_Slate_Dark"      # 1F2736 dark blue-black shell
BLACK    = "Mat_Neutral_Black_Matte"     # 272727 seals, shadow gaps
WHITE    = "Mat_Paint_White_Arctic"      # D6DAD9 cool off-white
GLOW     = "Mat_Emissive_Portal_Blue"    # 2FB8FF cold cyan light

NEEDED = [STEEL_D, STEEL_W, BRASS, CHROME, COPPER, SLATE, BLACK, WHITE, GLOW]

# object -> material per slot index
ASSIGN = {
    # --- head ---------------------------------------------------------------
    "Sphere":  [SLATE],                       # the hood / cranial shell
    "Eye":     [WHITE, GLOW, BLACK],          # sclera, glowing iris, pupil
    "Eyelid":  [SLATE, SLATE, BLACK, SLATE],
    "Neck":    [STEEL_D],
    "Cube":    [GLOW],                        # the floating halo, now clearly powered
    # --- torso / legs -------------------------------------------------------
    "Hips":          [STEEL_W],
    "LeftLeg":       [STEEL_W], "RightLeg":       [STEEL_W],
    "LeftKnee":      [BRASS],   "RightKnee":      [BRASS],
    "LeftLowerLeg":  [STEEL_W], "RightLowerLeg":  [STEEL_W],
    "LeftFoot":      [STEEL_D], "RightFoot":      [STEEL_D],
    # --- floating arms (R = -y, L = +y) -------------------------------------
    "Cylinder.001": [BRASS],   "Cylinder.004": [BRASS],    # shoulder ball
    "Cylinder.002": [BRASS],   "Cylinder.005": [BRASS],    # elbow ball
    "Cube.002":     [STEEL_W], "Cube.009":     [STEEL_W],  # upper-arm shroud
    "LowerArm.002": [STEEL_W], "LowerArm.003": [STEEL_W],
    "Cube.006":     [STEEL_D], "Cube.011":     [STEEL_D],  # wrist
    "Cube.007":     [GLOW],    "Cube.013":     [GLOW],     # palm emitter plate
}
# every finger part
FINGERS = [f"Hand.{i:03d}" for i in range(1, 40)]
for n in FINGERS:
    ASSIGN.setdefault(n, [CHROME])
# every cable run
for i in range(0, 12):
    ASSIGN.setdefault("BézierCurve" if i == 0 else f"BézierCurve.{i:03d}", [COPPER])

# ---------------------------------------------------------------- link palette
have = {m.name for m in bpy.data.materials if m.library}
want = [n for n in NEEDED if n not in have]
if want:
    path = bpy.path.abspath(PALETTE_REL)
    with bpy.data.libraries.load(path, link=True) as (src, dst):
        missing = [n for n in want if n not in src.materials]
        if missing:
            raise SystemExit(f"palette is missing: {missing}")
        dst.materials = want
    print(f"linked {len(want)} materials from palette.blend")

# keep the artist's original materials alive even once nothing points at them
for m in bpy.data.materials:
    if not m.library:
        m.use_fake_user = True

def mat(name):
    m = bpy.data.materials.get(name)
    if m is None:
        raise SystemExit(f"material not linked: {name}")
    return m

# ------------------------------------------------------------------- assign
# Meshes shared between objects would be assigned twice; check rather than assume.
seen_data = {}
applied, skipped = 0, []
for obj_name, slots in ASSIGN.items():
    o = bpy.data.objects.get(obj_name)
    if o is None or not hasattr(o.data, "materials"):
        skipped.append(obj_name); continue
    if o.data.name in seen_data and seen_data[o.data.name] != slots:
        print(f"  ! {obj_name} shares mesh '{o.data.name}' with "
              f"{seen_data[o.data.name]} - keeping the first assignment")
        continue
    seen_data[o.data.name] = slots
    while len(o.data.materials) < len(slots):
        o.data.materials.append(None)
    for i, name in enumerate(slots):
        o.data.materials[i] = mat(name)
    applied += 1

print(f"styled {applied} objects; not found: {len(skipped)}")
if skipped:
    print("  missing:", ", ".join(sorted(skipped)[:12]), "..." if len(skipped) > 12 else "")

# store the palette link as a relative path, like every other model in the library
bpy.ops.file.make_paths_relative()
bpy.ops.wm.save_mainfile()
print("SAVED")
