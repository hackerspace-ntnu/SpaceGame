# Weathering, written into the mesh as VERTEX COLOURS.
#
# STEP 5 OF 6:  restore_parts.py -> rig.py -> walkerize.py -> hands_rebuild.py
#               -> rustify.py -> anim.py -> export.py
# Safe to re-run.
#
# (The file keeps its old name for continuity with the build record and the
# pipeline order. What it paints is whatever RAMP is set to below; it began as
# four shades of rust and is now grey / green / light brown.)
#
# ---- why vertex colours, and what this replaces -------------------------------
#
# Two earlier versions assigned palette MATERIALS -- first one per object, then
# one per face from a warped noise field. Both are discrete by construction: a
# face gets exactly one material, so every transition is a hard step at a polygon
# edge. The brief is soft blends, one tone bleeding into the next the way paint
# and corrosion actually meet, and no amount of tuning a noise field gets a step
# function to do that.
#
# So the colour is no longer chosen from a list. The field is evaluated at every
# VERTEX and produces a continuous position along the ramp, which is then
# interpolated between the two palette colours it falls between. The result is
# baked into a colour attribute; the GPU interpolates it across each triangle for
# free, which is where the gradient comes from.
#
# Consequences worth knowing:
#
#   * The creature is back to ONE material over the whole body instead of the 70
#     submeshes the per-face version needed. Fewer draw calls, not more.
#   * Gradient smoothness is bounded by VERTEX density, not polygon count. Faces
#     here are a median 0.09 m, so blends are smooth at any distance you fight
#     this thing from. Detail finer than that is the shader's job -- see
#     ConjurerWeathered.shader, which adds triplanar grunge on top.
#   * The palette still owns the colours. This script reads them out of the linked
#     materials rather than hardcoding hexes, so a palette edit still propagates.
import os

import bpy
from mathutils import Vector, noise

PALETTE_REL = "//../../../_Source~/palette.blend"

# ---- the weathering ramp, light to dark --------------------------------------
#
# Two of the three already existed in the palette and are reused as-is; only the
# khaki was added. Ordered light to dark, which is also dry-to-damp: dusty khaki
# up top where the sun hits, bare grey through the body, verdigris green down at
# the feet where water sits.
RAMP = [
    "Mat_Metal_Patina_Khaki",      # BFA070  pale dust-brown, the light end
    "Mat_Metal_Steel_Worn",        # 7A7D80  neutral bare grey, the most common
    "Mat_Metal_Copper_Oxide",      # 4E8C7A  verdigris green, the damp end
]

GLOW = "Mat_Emissive_Portal_Blue"  # 2FB8FF the three things that are powered
BLACK = "Mat_Neutral_Black_Matte"  # 272727 pupil and seals

# The one material the whole weathered body wears. Its colour comes entirely from
# the vertex attribute, so it is a TECHNIQUE rather than a colour -- which is why
# it is defined here rather than added to the shared palette, where every entry is
# a hex and a surface. Unity swaps in ConjurerWeathered.shader by this name.
BLEND = "Mat_Weathered_Blend"

NEEDED = RAMP + [GLOW, BLACK]

# The colour attribute Unity reads. FBX carries exactly one set of vertex colours
# per mesh and Unity binds it to COLOR; the name matters only inside Blender.
ATTR = "Weathering"

# Everything the creature exports gets the blend material, EXCEPT these.
#
# The exemptions are not decoration. This attack is survivable because the player
# gets a telegraph, and the telegraph is the glow: the eye, the two palm emitter
# plates, and the staff's emitter lighting up. Weather those over and the wind-up
# is invisible until the bolt is already falling.
#
# The staff's SHAFT, MOUNT and FAN are deliberately not exempt. They are
# structure -- a rusted rod, a bearing collar and three steel blades -- and
# weathering them is what makes the emitter above them read as lit rather than as
# one uniformly bright object on a stick. Same call charger.py's housing got.
#
# Charger_Core/Rotor/Teeth used to be here. The chest ring is gone; staff.py
# removes it and the emitter at the top of the staff is what glows now.
EXCEPT = {
    "Eye":             [BLEND, GLOW, BLACK],  # weathered socket, live iris, dead pupil
    "Eyelid":          [BLEND, BLEND, BLACK, BLEND],
    "Cube":            [GLOW],                # the halo
    "Cube.007":        [GLOW],                # right palm emitter plate
    "Cube.013":        [GLOW],                # left palm emitter plate
    "Staff_Core":      [GLOW],                # the lens above the turbine
}

# The grafted hands arrive from components/mechanical/robot_hand.blend wearing
# plating over dark steel joints and chrome pins. Only the PLATING is weathered:
# the dark joints and bright pins are the only thing that reads the fingers as
# separate segments at 25 m.
HAND_PREFIX = "Hand_"

# Every material this script has ever painted, so a re-run after the ramp changed
# recognises its own previous output instead of preserving it as an authored
# special.
LEGACY = {
    "Mat_Metal_Rust_Pale", "Mat_Metal_Rust_Heavy",
    "Mat_Metal_HullRust_Orange", "Mat_Metal_Rust_Deep",
}
REPAINTABLE = set(RAMP) | LEGACY | {BLEND}


def log(m):
    print(f"[rustify] {m}")


# ============================================================ palette plumbing
def purge_foreign_palettes():
    """Drop links to any palette.blend that is not THE palette.

    restore_parts.py appends the halo and cable curves out of a donor copy of the
    committed model, typically a `git show` dump in a temp directory. Its own
    palette link is relative, so it resolves against the DONOR's location, and
    appending an object that wears a palette material drags a second, broken
    library in alongside the real one. The halo rendered black for a while because
    of exactly this.
    """
    want = os.path.normcase(os.path.abspath(bpy.path.abspath(PALETTE_REL)))

    strays = [lib for lib in bpy.data.libraries
              if os.path.basename(lib.filepath).lower() == "palette.blend"
              and os.path.normcase(os.path.abspath(bpy.path.abspath(lib.filepath)))
              != want]
    if not strays:
        return

    good = {m.name: m for m in bpy.data.materials
            if m.library is not None and m.library not in strays}

    remapped = 0
    for lib in strays:
        for m in [x for x in bpy.data.materials if x.library is lib]:
            target = good.get(m.name)
            if target is None:
                log(f"WARNING: {m.name} came from {lib.filepath} and the real "
                    "palette has no match; leaving it alone")
                continue
            m.user_remap(target)
            remapped += 1

    for lib in strays:
        log(f"dropping stale palette link {lib.filepath}")
        bpy.data.libraries.remove(lib)
    log(f"remapped {remapped} material user(s) onto the project palette")


# Before the link check below, which matches by NAME and would otherwise take a
# broken material as proof the real one is already present.
purge_foreign_palettes()

have = {m.name for m in bpy.data.materials if m.library}
want = [n for n in NEEDED if n not in have]
if want:
    with bpy.data.libraries.load(bpy.path.abspath(PALETTE_REL), link=True) as (src, dst):
        missing = [n for n in want if n not in src.materials]
        if missing:
            raise SystemExit(f"[rustify] palette is missing: {missing}")
        dst.materials = want
    log(f"linked {len(want)} material(s) from palette.blend")

# Keep the artist's own materials alive even once nothing points at them -- the
# procedural Rust above all, which is the one worth coming back for.
for m in bpy.data.materials:
    if not m.library:
        m.use_fake_user = True


def mat(name):
    m = bpy.data.materials.get(name)
    if m is None:
        raise SystemExit(f"[rustify] material not linked: {name}")
    return m


def base_colour(name):
    """A palette material's base colour, read rather than hardcoded."""
    m = mat(name)
    bsdf = m.node_tree.nodes.get("Principled BSDF") if m.use_nodes else None
    if bsdf is None:
        raise SystemExit(f"[rustify] {name} has no Principled BSDF to read")
    c = bsdf.inputs["Base Color"].default_value
    return Vector((c[0], c[1], c[2]))


COLOURS = [base_colour(n) for n in RAMP]


def blend_material():
    """The single body material: base colour driven by the colour attribute.

    Wired up in Blender too, not just declared, so the preview renders match what
    Unity will show. Unity replaces it wholesale with ConjurerWeathered.shader,
    matched by name through the FBX importer's material remap.
    """
    m = bpy.data.materials.get(BLEND)
    if m is None:
        m = bpy.data.materials.new(BLEND)
    m.use_nodes = True
    m.use_fake_user = True

    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = ATTR

    bsdf.inputs["Metallic"].default_value = 0.55
    bsdf.inputs["Roughness"].default_value = 0.80

    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])

    out.location = (300, 0)
    bsdf.location = (0, 0)
    attr.location = (-300, 0)
    return m


BLEND_MAT = blend_material()

# ============================================================ the noise field
#
# ---- why the field is DOMAIN WARPED ------------------------------------------
#
# Plain fractal noise gives round blobs. Corrosion does not look like that -- it
# creeps along seams and runs, it necks and branches, and its edges wander. The
# standard way to get that shape is to distort the space before sampling it:
# offset each sample position by a second noise field, then sample the first at
# the offset position. Straight features come out as tendrils, and blob edges come
# out with the ragged branching that rust actually has.
FBM_OCTAVES = 3
WARP_FREQ, WARP_AMP = 0.26, 3.2
FIELD_FREQ = 0.78
FIELD_AMP = 1.60

# ---- blooms ------------------------------------------------------------------
#
# The warped field alone drifts smoothly between neighbouring tones, which reads
# as a gradient but not as damage. Real corrosion has ISOLATED patches: a bloom
# starts somewhere and eats outward. A second warped field is thresholded, and
# inside it the tone is pushed hard toward the damp end. Because the thresholded
# field is itself warped, its edge necks and strands off instead of being a
# circle. SOFTNESS is what keeps that edge a blend rather than a cut.
BLOOM_FREQ = 0.42
BLOOM_THRESHOLD = 0.16
BLOOM_DEPTH = 0.90
BLOOM_SOFTNESS = 0.22

# Blooms only ever push toward the damp end, so they drag the whole distribution
# with them -- roughly half the surface is inside one, which shifts the mean by
# about half BLOOM_DEPTH. Without this the creature comes out overwhelmingly
# verdigris and the grey it is supposed to mostly be never appears.
BLOOM_BIAS = -0.42

# Fine speckle, at a scale near the vertex spacing. Anything finer than this
# cannot be carried by vertex colours and is left to the shader's triplanar
# grunge, which is not bounded by mesh density.
SPECKLE_FREQ, SPECKLE_AMP = 3.1, 0.28

_WARP_OFFSETS = (Vector((5.2, 1.3, 0.0)),
                 Vector((0.0, 4.7, 9.2)),
                 Vector((3.1, 0.0, 2.8)))
_BLOOM_OFFSET = Vector((17.4, 8.1, 23.9))
_SPECKLE_OFFSET = Vector((31.7, 12.4, 5.6))


def fbm(p, octaves=FBM_OCTAVES):
    """Fractal Brownian motion: octaves of noise at doubling frequency."""
    total, amp, freq = 0.0, 1.0, 1.0
    for _ in range(octaves):
        total += noise.noise(p * freq) * amp
        freq *= 2.0
        amp *= 0.5
    return total


def smoothstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a))) if b != a else float(x >= b)
    return t * t * (3.0 - 2.0 * t)


def field(world_co, base):
    """Continuous position along the ramp at a point. NOT rounded.

    Returning a float rather than an index is the whole difference between this
    version and the last one: the fractional part is what the colour lerp uses,
    and it is where the gradient lives.
    """
    p = world_co * WARP_FREQ
    warp = Vector((fbm(p + _WARP_OFFSETS[0], 2),
                   fbm(p + _WARP_OFFSETS[1], 2),
                   fbm(p + _WARP_OFFSETS[2], 2))) * WARP_AMP

    v = base + fbm(world_co * FIELD_FREQ + warp) * FIELD_AMP
    v += fbm(world_co * SPECKLE_FREQ + _SPECKLE_OFFSET, 2) * SPECKLE_AMP

    # Blooms, faded in across SOFTNESS rather than switched on at a threshold.
    bloom = fbm(world_co * BLOOM_FREQ + warp + _BLOOM_OFFSET, 2)
    v += BLOOM_BIAS + BLOOM_DEPTH * smoothstep(
        BLOOM_THRESHOLD, BLOOM_THRESHOLD + BLOOM_SOFTNESS, bloom)

    return min(len(RAMP) - 1.0, max(0.0, v))


def colour_at(world_co, base):
    """The blended colour and a 0..1 weathering amount at a point."""
    v = field(world_co, base)
    i = min(len(COLOURS) - 2, int(v))
    f = v - i
    c = COLOURS[i].lerp(COLOURS[i + 1], f)
    return c, v / (len(RAMP) - 1.0)


# ============================================================ apply
arm = bpy.data.objects["ConjurerRig"]
targets = []


def walk(o):
    for c in o.children:
        targets.append(c)
        walk(c)


walk(arm)


def part_z(obj):
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    return sum(c.z for c in corners) / len(corners)


# Ranked by height rather than scaled between the lowest and highest point: the
# halo floats ten metres clear above the body, and raw height would compress every
# real part into the middle of the range.
_meshes = sorted((o for o in targets if o.type == 'MESH'), key=part_z)
RANK = {o.name: (i / max(1, len(_meshes) - 1)) for i, o in enumerate(_meshes)}


def base_tone(obj):
    return (1.0 - RANK.get(obj.name, 0.5)) * (len(RAMP) - 1)


def paint_vertices(obj):
    """Bake the field into the mesh's colour attribute, one value per vertex.

    POINT domain rather than CORNER: a value per vertex is what gets interpolated
    smoothly across a triangle, and it is a third of the data. Corner colours
    would let neighbouring faces disagree at a shared vertex, which is precisely
    the hard edge this whole change exists to remove.
    """
    mesh = obj.data
    attr = mesh.color_attributes.get(ATTR)
    if attr is None:
        attr = mesh.color_attributes.new(name=ATTR, type='FLOAT_COLOR',
                                         domain='POINT')
    mesh.color_attributes.active_color = attr
    mesh.color_attributes.render_color_index = list(mesh.color_attributes).index(attr)

    M = obj.matrix_world
    base = base_tone(obj)
    for i, v in enumerate(mesh.vertices):
        c, amount = colour_at(M @ v.co, base)
        # Alpha carries the weathering amount so the shader can rough up and
        # de-metal the corroded end without a second attribute.
        attr.data[i].color = (c.x, c.y, c.z, amount)
    return len(mesh.vertices)


seen = set()
painted = handled = skipped = 0
verts = 0

for o in targets:
    if o.type != 'MESH' or not hasattr(o.data, "materials"):
        skipped += 1
        continue
    if o.data.name in seen:
        continue
    seen.add(o.data.name)

    verts += paint_vertices(o)

    if o.name.startswith(HAND_PREFIX):
        # Plating only, identified by SLOT rather than by material name.
        # robot_hand.py builds every hand mesh with its material list in a fixed
        # order and plating at index 0. Name matching cannot work here: the
        # component authored its plating as Mat_Metal_Rust_Heavy, which the ramp
        # no longer contains, while the ramp DOES contain Mat_Metal_Steel_Worn --
        # which the component uses for the knuckle blocks. Matching by name would
        # skip the plating and repaint the knuckles, exactly backwards.
        if not o.data.materials:
            continue
        if not (o.data.materials[0] and o.data.materials[0].name in REPAINTABLE):
            log(f"WARNING: {o.name} slot 0 is "
                f"{o.data.materials[0].name if o.data.materials[0] else 'empty'}, "
                "not plating; leaving the hand alone")
            continue
        o.data.materials[0] = BLEND_MAT
        handled += 1
        continue

    slots = EXCEPT.get(o.name) or [BLEND] * max(1, len(o.data.materials))

    while len(o.data.materials) < len(slots):
        o.data.materials.append(None)
    # Trim any submeshes left over from the per-face version; one material now.
    while len(o.data.materials) > len(slots):
        o.data.materials.pop()
    for i, name in enumerate(slots):
        o.data.materials[i] = BLEND_MAT if name == BLEND else mat(name)
    for poly in o.data.polygons:
        if poly.material_index >= len(slots):
            poly.material_index = 0
    painted += 1

log(f"painted {painted} body part(s) and {handled} hand part(s); "
    f"{verts} vertices carry the field")
log(f"one material ({BLEND}) over the body, not one per shade")

bpy.ops.file.make_paths_relative()
bpy.ops.wm.save_mainfile()
log("SAVED")
