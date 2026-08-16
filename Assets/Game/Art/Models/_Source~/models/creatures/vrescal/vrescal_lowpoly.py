"""Cut the Vrescal down to a low-poly, flat-shaded, flat-coloured game asset.

    blender --background --python vrescal_lowpoly.py -- --overwrite

Reads `vrescal_stylised.blend`, writes `vrescal_lowpoly.blend`. The source is
never modified, and neither is `vrescal_sculpt.blend` behind it.

    verts    22 470 -> ~3 000      (7.5x fewer)
    tris     44 944 -> ~6 000
    maps     3 x 1024 -> 1 x 512   base colour only
    colours  3 099 -> 12           hard flat regions, saturation-boosted
    shading  smooth -> flat

## Why the UV atlas is rebuilt rather than carried over

The obvious cheap route is to decimate and keep the existing UVs. It does not
survive contact with this mesh. The atlas has 99 islands and **12.5 % of all
vertices sit on a UV seam**; a collapse that ignores seams smears texture across
island boundaries, and a collapse that protects them (zero-weight vertex group,
which Decimate does support) cannot go below 2 811 verts and spends its whole
budget on dense island borders wrapped around sparse interiors. Both are worse
than the third option.

So the mesh is decimated freely, given a fresh Smart-UV atlas sized for its
actual triangle count, and the colour is **baked back off the high-poly**. That
is the standard retopo-and-bake trade and it is the only one of the three that
gets clean topology *and* clean UVs.

## The colour pass is deliberately not the stylise pass

`vrescal_stylise.py` had to protect a fragile generated atlas from its own
filters -- hundreds of small islands on flat grey, where any blur produces seam
halos. That machinery is reused here (`uv_mask`, `fill_padding`, `median_filter`,
`kmeans`) because the problem is identical on the new atlas.

What differs is the target. Stylise aimed for *fewer* colours while keeping the
countershading gradient underneath, so it mixed the quantised result back at
82 %. This aims for **flat regions**, so the assignment is hard: every pixel
becomes exactly one of `COLOURS` palette entries, with no gradient left. That is
what makes it read as low-poly art rather than as a blurred photograph.

Saturation is applied **to the palette entries, not to the image**. Boosting a
continuous image and then clustering lets the clustering average the boost back
out; boosting the K centres after they are found guarantees the shipped colours
are exactly the K saturated tones and nothing in between. It is also done in
gamma space through HSV, so hue is preserved exactly -- "the same general
colours, more saturated" is a hue-preserving operation and doing it in the
linear space Blender hands over would shift the tans toward green.

## Flat shading is a Unity vertex-count trade

Faceted shading splits a vertex per adjacent face, so Unity renders roughly
3 verts per triangle regardless of how few mesh vertices there are. The mesh
vertex count is what drops 7.5x; the GPU vertex count drops about 1.4x. The
triangle count -- which is what actually costs fill and skinning -- drops the
full 7.5x. This is stated plainly because the two numbers disagree and the
mesh-vertex figure is the flattering one.
"""

import colorsys
import os
import sys

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import anatomy as A                 # noqa: E402
import vrescal_stylise as ST        # noqa: E402  -- main() is guarded, safe

SRC = os.path.join(HERE, "vrescal_stylised.blend")
OUT = os.path.join(HERE, "vrescal_lowpoly.blend")
TEXDIR = os.path.join(HERE, "textures")

# ---- the tuning surface ---------------------------------------------------

TARGET_VERTS = 3000   # 22 470 / 3 000 = 7.5x, mid-range of the 5-10x asked for
VERT_TOLERANCE = 0.02  # bisection stops inside +-2 %

UV_ANGLE = 74.0       # Smart UV Project seam angle, degrees. Above the 66
                      # default: on a flat-shaded model a little UV stretch is
                      # invisible, and fewer, larger islands pack far better
UV_MARGIN = 0.005     # ~2.5 px at 512, applied by the repack rather than by
                      # the projection -- see unwrap()

BAKE_RES = 512        # 6 000 tris over 512^2 is ~44 texels per triangle, which
                      # is generous for flat colour and leaves headroom to drop
                      # to 256 if the art wants it
BAKE_EXTRUSION = 0.08  # metres. The low-poly deviates from the high-poly by up
BAKE_RAY = 0.15        # to ~3 cm after a 7.5x cut; these clear that comfortably

COLOURS = 12          # flat regions in the shipped map
MEDIAN_R = 1          # denoise the bake before clustering
SATURATION = 2.00     # applied to the palette entries, HSV, hue preserved
VALUE_GAIN = 1.14     # K-means puts every centre at the *mean* of its cluster,
                      # so a hard 12-colour assignment throws away both ends of
                      # the range and the animal comes back darker than the map
                      # it was cut from -- the sand tones land olive. This lifts
                      # the palette back toward the source's own brightness;
                      # it is a correction for the clustering, not a grade.
ROUGHNESS = 0.68      # scalar, replacing the deleted roughness map

MAT_NAME = "Mat_Hide_Vrescal_LowPoly"
MESH = "Mesh_Vrescal_Sculpt"       # the name vrescal_rig.py looks for
HIGH = "Mesh_Vrescal_HighPoly"
TEX_NAME = "Tex_Low_Vrescal_BaseColor"


# --------------------------------------------------------------------------
# Geometry
# --------------------------------------------------------------------------

def manifold_report(obj, label):
    """Boundary and non-manifold edge counts.

    This is not decoration. `vrescal_rig.py` skins with bone heat
    (`ARMATURE_AUTO`), which needs a closed surface to have a boundary condition
    to solve against -- the build record for the sculpt records bones being
    dropped outright on the pre-repair mesh, which had a single four-edge hole.
    A decimate that punches the shell open would fail there, several steps
    later, as a rigging bug.
    """
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    boundary = sum(1 for e in bm.edges if len(e.link_faces) < 2)
    nonmanifold = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    loose = sum(1 for v in bm.verts if not v.link_edges)
    bm.free()
    print("    %-10s verts %6d  tris %6d  boundary %d  non-manifold %d  loose %d"
          % (label, len(obj.data.vertices), len(obj.data.polygons),
             boundary, nonmanifold, loose))
    return boundary, nonmanifold, loose


def decimate_to(obj, target):
    """Collapse-decimate until the vertex count lands on `target`.

    Decimate's `ratio` is a fraction of *faces*, and the face-to-vertex
    relationship after collapse is only approximately the Euler one, so the
    ratio that hits a given vertex count is solved for rather than computed.
    """
    md = obj.modifiers.new("Decimate", 'DECIMATE')
    md.decimate_type = 'COLLAPSE'
    md.use_collapse_triangulate = True

    dg = bpy.context.evaluated_depsgraph_get()

    def verts_at(ratio):
        md.ratio = ratio
        dg.update()
        return len(obj.evaluated_get(dg).data.vertices)

    lo, hi = 0.01, 1.0
    best = None
    for _ in range(24):
        mid = (lo + hi) * 0.5
        n = verts_at(mid)
        if best is None or abs(n - target) < abs(best[1] - target):
            best = (mid, n)
        if abs(n - target) <= target * VERT_TOLERANCE:
            break
        if n > target:
            hi = mid
        else:
            lo = mid
    md.ratio = best[0]
    print("    decimate ratio %.5f -> %d verts (target %d)"
          % (best[0], best[1], target))

    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=md.name)


def unwrap(obj):
    """Smart-project, then repack tightly.

    Smart UV Project's own packing leaves a lot on the table: at
    `island_margin` 0.012 it filled 17 % of the atlas, because the margin is an
    absolute UV distance and these islands are small enough that a 6 px border
    on every side costs more area than the island encloses. Projecting with a
    near-zero margin and repacking afterwards separates the two decisions --
    seam placement, then spacing -- and roughly triples the coverage.
    """
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    for uv in list(obj.data.uv_layers):
        obj.data.uv_layers.remove(uv)
    obj.data.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=np.radians(UV_ANGLE),
                             island_margin=0.0,
                             area_weight=0.0,
                             correct_aspect=True,
                             scale_to_bounds=False)
    bpy.ops.uv.select_all(action='SELECT')
    try:
        bpy.ops.uv.pack_islands(rotate=True, margin=UV_MARGIN,
                                shape_method='CONCAVE', scale=True)
    except TypeError:                       # older signature
        bpy.ops.uv.pack_islands(rotate=True, margin=UV_MARGIN)
    bpy.ops.object.mode_set(mode='OBJECT')


# --------------------------------------------------------------------------
# Bake
# --------------------------------------------------------------------------

def bake_material(obj, image):
    """A throwaway material whose only job is to own the bake target node."""
    mat = bpy.data.materials.new(MAT_NAME)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Metallic'].default_value = 0.0
    bsdf.inputs['Roughness'].default_value = ROUGHNESS
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = image
    tex.label = image.name
    tex.location = (-360, 240)
    nt.nodes.active = tex                  # this is what `bake` writes into
    # Deliberately *not* linked to Base Color yet. Feeding the bake target back
    # into the shader of an object taking part in the bake makes Blender warn
    # about a circular dependency; the link is made once the bake is done.
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    return mat, tex


def bake_colour(high, low, image):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = 4
    sc.cycles.use_denoising = False

    bake = sc.render.bake
    bake.use_selected_to_active = True
    bake.cage_extrusion = BAKE_EXTRUSION
    bake.max_ray_distance = BAKE_RAY
    bake.use_pass_direct = False
    bake.use_pass_indirect = False
    bake.use_pass_color = True
    bake.margin = 8
    bake.use_clear = True

    bpy.ops.object.select_all(action='DESELECT')
    high.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    print("    baking %d tris -> %d^2 ..." % (len(high.data.polygons), BAKE_RES))
    bpy.ops.object.bake(type='DIFFUSE')


# --------------------------------------------------------------------------
# Colour
# --------------------------------------------------------------------------

def saturate_palette(centres, saturation, value_gain):
    """Push the K palette entries in HSV, hue untouched.

    No colour-space conversion happens here, and that is deliberate. Blender's
    `Image.pixels` returns the *stored* buffer, and for a byte image that buffer
    is already gamma-encoded -- it is not linearised on the way out, whatever
    the datablock's colorspace is set to. So these values are sRGB already and
    an HSV saturation on them is exactly the slider-drag it looks like.

    An earlier version converted to gamma and back around this function. The
    two conversions cancelled, so the shipped image was roughly right, but the
    saturation multiplier was acting on doubly-encoded S values -- it applied
    perhaps half the boost it claimed, and the palette printout (which gamma'd
    a third time) reported washed-out hexes that did not match the file.
    """
    out = np.empty_like(centres)
    for i, c in enumerate(centres):
        h, s, v = colorsys.rgb_to_hsv(*np.clip(c, 0.0, 1.0))
        s = min(1.0, s * saturation)
        v = min(1.0, v * value_gain)
        out[i] = colorsys.hsv_to_rgb(h, s, v)
    return out.astype(np.float32)


def flatten_colour(rgb, mask):
    """Median -> K-means -> saturate the centres -> hard assign.

    The assignment is hard on purpose. `vrescal_stylise.py` mixed its quantised
    result back at 82 % to keep the belly-to-flank countershading from banding;
    here banding *is* the goal, so every pixel gets exactly one palette entry.
    """
    for _ in range(2):
        rgb = ST.median_filter(rgb, MEDIAN_R)

    centres = ST.kmeans(rgb[mask].reshape(-1, 3), COLOURS)
    palette = saturate_palette(centres, SATURATION, VALUE_GAIN)

    flat = rgb.reshape(-1, 3)
    out = np.empty_like(flat)
    step = 200000
    for i in range(0, len(flat), step):
        chunk = flat[i:i + step]
        d = ((chunk[:, None, :] - centres[None, :, :]) ** 2).sum(axis=2)
        out[i:i + step] = palette[d.argmin(axis=1)]
    out = out.reshape(rgb.shape)

    before = np.array([colorsys.rgb_to_hsv(*np.clip(c, 0, 1))[1]
                       for c in centres])
    after = np.array([colorsys.rgb_to_hsv(*np.clip(c, 0, 1))[1]
                      for c in palette])
    srgb = (np.clip(palette, 0, 1) * 255).round().astype(int)
    print("    palette (%d entries), mean saturation %.2f -> %.2f:"
          % (COLOURS, before.mean(), after.mean()))
    for i in np.argsort(-srgb.sum(axis=1)):
        r, g, b = srgb[i]
        h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
        print("      #%02X%02X%02X   H %3.0f  S %.2f  V %.2f"
              % (r, g, b, h * 360, s, v))
    return np.clip(out, 0.0, 1.0).astype(np.float32)


# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

def main():
    A.start()
    with bpy.data.libraries.load(SRC) as (src, dst):
        dst.objects = list(src.objects)
    coll = A.collection("Coll_Vrescal_LowPoly")
    for o in dst.objects:
        coll.objects.link(o)

    high = bpy.data.objects[MESH]
    high.name = HIGH
    high.data.name = HIGH
    manifold_report(high, "high-poly")

    low = high.copy()
    low.data = high.data.copy()
    low.name = MESH
    low.data.name = MESH
    coll.objects.link(low)

    print("  decimating:")
    decimate_to(low, TARGET_VERTS)
    boundary, nonmanifold, loose = manifold_report(low, "low-poly")
    if boundary or nonmanifold or loose:
        raise SystemExit(
            "Decimation opened the shell (boundary %d, non-manifold %d, loose "
            "%d).\nBone heat in vrescal_rig.py needs a closed surface and will "
            "drop bones on this mesh." % (boundary, nonmanifold, loose))

    print("  unwrapping:")
    unwrap(low)
    print("    %d UV loops over %d tris"
          % (len(low.data.uv_layers[0].data), len(low.data.polygons)))

    image = bpy.data.images.new(TEX_NAME, BAKE_RES, BAKE_RES,
                                alpha=False, float_buffer=False)
    image.colorspace_settings.name = 'sRGB'
    mat, tex = bake_material(low, image)

    print("  baking:")
    bake_colour(high, low, image)

    print("  flattening colour:")
    mask = ST.uv_mask(low, BAKE_RES)
    print("    UV coverage %.1f%% of the atlas" % (100.0 * mask.mean()))
    rgb = ST.to_array(image)[..., :3]
    rgb = flatten_colour(rgb, mask)
    rgb = ST.fill_padding(rgb, mask)

    rgba = np.concatenate(
        [rgb, np.ones(rgb.shape[:2] + (1,), dtype=np.float32)], axis=2)
    bpy.data.images.remove(image)
    image = ST.new_image(TEX_NAME, rgba, 'sRGB')
    tex.image = image
    tex.label = image.name
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])

    # Counted over the island pixels only. The padding is a dilation of island
    # colour, so it holds blends that never appear on the model -- counting the
    # whole atlas would report thousands and hide the fact that the surface
    # itself is exactly COLOURS flat tones.
    as8 = (rgb.reshape(-1, 3) * 255).astype(np.uint8)
    on_model = len(np.unique(as8[mask.reshape(-1)], axis=0))
    grad = (np.abs(np.diff(rgb, axis=0)).mean()
            + np.abs(np.diff(rgb, axis=1)).mean())
    print("    %d distinct colours on the model (%d over the whole atlas), "
          "gradient energy %.4f"
          % (on_model, len(np.unique(as8, axis=0)), grad))

    # Faceted shading, as chosen. Blender 4.1+ dropped `use_auto_smooth`; a
    # plain flat shade on every polygon is what "no smoothing" means now.
    low.data.polygons.foreach_set("use_smooth",
                                  [False] * len(low.data.polygons))
    low.data.update()

    bpy.data.objects.remove(high, do_unlink=True)
    for m in [m for m in bpy.data.materials if m.name != MAT_NAME]:
        bpy.data.materials.remove(m)
    for i in [i for i in bpy.data.images if i is not image]:
        print("    dropping %s (%dx%d)" % (i.name, *i.size))
        bpy.data.images.remove(i)

    os.makedirs(TEXDIR, exist_ok=True)
    image.filepath_raw = os.path.join(TEXDIR, "%s.png" % image.name)
    image.file_format = 'PNG'
    image.save()
    image.pack()
    print("  wrote %s" % image.filepath_raw)

    d = low.dimensions
    print("  final: %d verts, %d tris, %.2f x %.2f x %.2f m"
          % (len(low.data.vertices), len(low.data.polygons), d.x, d.y, d.z))
    print("  images: %s" % [i.name for i in bpy.data.images])
    print("  materials: %s" % [m.name for m in bpy.data.materials])
    A.save(OUT)


if __name__ == "__main__":
    main()
