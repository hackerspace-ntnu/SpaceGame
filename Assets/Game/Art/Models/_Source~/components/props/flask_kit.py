"""Shared helpers for the thrown-flask family and the shader-shell props.

Two jobs, both of which would otherwise be copied into five model scripts. Same
pattern as `components/props/_console_kit.py` and `models/gear/_gauntlet.py`: a
family kit sits beside its components and is imported, never copied.

**Assembly** — `place`, `marker` and `sphere` are what the two bottle models
(`models/gear/bottled_singularity.py`, `models/gear/storm_flask.py`) do to the
same two component files.

**Shader channels** — `emit` and `write_channels` are what the three props
(`models/props/foam_blob.py`, `frozen_statue_base.py`, `storm_cloud.py`) hand
their shaders. Those props are shells for shaders rather than detailed meshes,
so the per-vertex data they carry *is* most of their content, and it has to mean
the same thing on all three or the shader author has to learn three conventions.

Holds no geometry of its own and produces no .blend.
"""

import math

import bmesh
import bpy
from mathutils import Matrix, Vector


def place(obj, matrix, origin=None):
    """Apply `matrix` into the mesh data, leaving rotation 0 and scale 1.

    Transforms are applied by library convention, and it is not cosmetic: a
    rotated object exports with that rotation baked into the FBX node, and
    Unity then hands the game a Transform whose local axes are not the ones the
    code reasons about. `origin` re-seats the pivot at a chosen world point,
    which is how an iris leaf keeps its hinge through the move onto the bottle.

    Lifted verbatim from `models/gear/dragon_bazooka.py`, which carries the
    original and is left alone as the historical record of a shipped file.
    """
    world = matrix @ obj.matrix_world
    if origin is None:
        origin = world.to_translation()
    origin = Vector(origin)
    obj.data.transform(Matrix.Translation(-origin) @ world)
    obj.location = origin
    obj.rotation_euler = (0.0, 0.0, 0.0)
    obj.scale = (1.0, 1.0, 1.0)
    return obj


def marker(coll, name, at, size=0.012):
    """A named empty carrying a coordinate across the FBX.

    An empty, not the 4 mm cube `dragon_bazooka.py` uses. That model predates
    `_exportlib.export(keep_empties=True)` and had to smuggle its coordinates
    through as geometry, which leaves the Unity prefab with four tiny renderers
    to strip. An empty arrives as a plain Transform with nothing to draw —
    `ruin_scanner_export.py` ships its emitter the same way. The export script
    for each bottle passes `keep_empties=True`; without it these vanish
    silently and the effects all play from the prefab root.
    """
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_type = 'PLAIN_AXES'
    obj.empty_display_size = size
    obj.location = at
    coll.objects.link(obj)
    return obj


def sphere(part, center, radius, mat, rings=9, seg=24):
    """A sphere as a loft of latitude circles — the core inside a flask.

    `_buildlib.Part` has no sphere, and `bmesh.ops.create_uvsphere` would land
    through `_absorb`, whose face slice claims the wrong faces (trap 5). A loft
    of latitude rings goes through the same tracked path as everything else and
    comes out as clean quad bands, which is what a shader wanting a smooth
    normal field needs anyway.
    """
    cx, cy, cz = center
    sections = []
    for i in range(rings + 1):
        t = -1.0 + 2.0 * i / rings
        z = cz + radius * t
        r = radius * math.sqrt(max(0.0, 1.0 - t * t))
        r = max(r, radius * 0.02)      # poles kept off zero so the loft closes
        sections.append((z, [(cx + r * math.cos(2 * math.pi * k / seg),
                              cy + r * math.sin(2 * math.pi * k / seg))
                             for k in range(seg)]))
    faces = part.loft(sections, axis='Z', mat=mat)
    part.shade(faces, True)
    return faces


# --------------------------------------------------------------------------
# Shader channels
#
# One convention, three props. Every prop mesh in this wave carries:
#
#   UV0 "UVMap"  the texturing unwrap, per prop — stated in each prop's script
#   UV1 "Data"   (u, v) = (core, up), as exact floats
#   Col "Col"    (r, g, b, a) = (core, up, lobe, 1)
#
#   core  1 deep inside the solid form, 0 at the edge where it should dissolve
#   up    0 at the bottom of the form, 1 at the top
#   lobe  a per-feature random in 0..1, smooth across the surface
#
# The data lives in **UV1 as well as** the colour attribute on purpose. A second
# UV set crosses FBX and Unity as exact float2 with no colour management
# anywhere in the path; a vertex colour crosses as 8-bit and Unity may or may
# not gamma-convert it depending on project colour space. UV1 is therefore the
# authoritative copy and the colour attribute is the convenience one — a shader
# that needs the value to be right reads UV1 (TEXCOORD1).
# --------------------------------------------------------------------------

def write_channels(mesh, uv0, data, wrap_u=False):
    """Write UV0, UV1 and the colour attribute from per-vertex tables.

    `uv0` and `data` are indexed by vertex index: `uv0[i]` is `(u, v)` and
    `data[i]` is `(core, up, lobe)`.

    `wrap_u` fixes the seam on a cylindrical or spherical unwrap. A single ring
    of vertices cannot hold both u=0.98 and u=0.02, so the faces that straddle
    the seam are detected per polygon (their u values span more than half the
    range) and their low corners pushed past 1.0. UVs are per *loop*, so this
    costs nothing and needs no split vertices — without it the seam face samples
    the entire texture backwards in one triangle, which reads as a bright scar
    down one side of the model.
    """
    lay = mesh.uv_layers.new(name="UVMap")
    for poly in mesh.polygons:
        idx = list(poly.loop_indices)
        us = [uv0[mesh.loops[li].vertex_index][0] for li in idx]
        straddles = wrap_u and (max(us) - min(us) > 0.5)
        for li in idx:
            u, v = uv0[mesh.loops[li].vertex_index]
            if straddles and u < 0.5:
                u += 1.0
            lay.data[li].uv = (u, v)

    dat = mesh.uv_layers.new(name="Data")
    for li, loop in enumerate(mesh.loops):
        core, up, _ = data[loop.vertex_index]
        dat.data[li].uv = (core, up)
    mesh.uv_layers.active = lay

    col = mesh.color_attributes.new(name="Col", type='BYTE_COLOR',
                                    domain='CORNER')
    # `color_srgb`, not `color`. A byte colour attribute's `color` property is
    # scene-linear and Blender encodes it on the way in, so writing 0.5 stores
    # the byte 188 rather than 128 — which silently bends every ramp in this
    # file. `color_srgb` stores the byte the value asks for.
    linear = not hasattr(col.data[0], "color_srgb")
    for li, loop in enumerate(mesh.loops):
        core, up, lobe = data[loop.vertex_index]
        rgba = (core, up, lobe, 1.0)
        if linear:
            col.data[li].color = rgba
        else:
            col.data[li].color_srgb = rgba
    mesh.color_attributes.active_color = col
    return lay, dat, col


def emit(name, bm, coll, materials, uv0=None, data=None, wrap_u=False,
         origin=(0.0, 0.0, 0.0)):
    """Turn a finished bmesh into a named object carrying the shader channels.

    `_buildlib.Part` is the right tool for a bevelled mechanical part and the
    wrong one here: these meshes are solved per vertex against an implicit
    surface and every vertex has to keep its identity so the channel tables line
    up with it, which is exactly what `Part`'s scratch-bmesh merging destroys.
    """
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()

    origin = Vector(origin)
    if origin.length_squared > 0:
        mesh.transform(Matrix.Translation(-origin))

    for m in materials:
        mesh.materials.append(m)
    if uv0 is not None:
        write_channels(mesh, uv0, data, wrap_u=wrap_u)

    obj = bpy.data.objects.new(name, mesh)
    obj.location = origin
    coll.objects.link(obj)
    return obj


def ramp(value, lo, hi):
    """`value` mapped into 0..1 across `lo`..`hi`, clamped. Flat range -> 0."""
    if hi - lo < 1e-9:
        return 0.0
    return min(1.0, max(0.0, (value - lo) / (hi - lo)))
