"""Simplify the Vrescal sculpt's baked textures down to the project's art style.

    blender --background --python vrescal_stylise.py -- --overwrite

Reads `vrescal_sculpt.blend`, rewrites its four 2048-square photographic maps
into a flatter, lower-frequency set at 1024, and saves `vrescal_stylised.blend`
alongside it. The source file is never modified. The maps are also written out
as PNGs under `textures/` so they can be repainted by hand without going
through Blender.

## What "too detailed" actually means here

Measured off the source maps:

    base colour   53 054 distinct colours, per-pixel gradient energy 0.027
    normal        219 139 distinct colours -- pore-level micro-relief
    roughness     238 levels of speckle
    metallic      mean 0.027, max 0.42 -- noise around zero

Only the first two matter visually, and they fail in different ways. The base
colour carries photographic grain that reads as dirt at any distance. The
normal map carries skin-pore relief that is smaller than a screen pixel unless
the animal fills the frame, so it costs 4 MB to produce shimmer.

The four passes below each target one of those, and the ordering is: throw away
resolution, flatten the colour, soften the relief, then rebuild the padding.

## The padding is the whole difficulty

This is a generated UV atlas: hundreds of small islands scattered over a flat
light-grey background. **Any filter run over that image bleeds grey into every
island edge**, and because the islands are small and numerous the result is a
bright halo along every UV seam on the model -- dozens of them, all over the
animal, and they do not look like a texture problem when you see them, they
look like a lighting bug.

So nothing here filters the raw image. Instead:

1. The UV triangles are rasterised into a coverage mask, so island and padding
   are known exactly rather than guessed at by colour.
2. Downsampling is **mask-weighted** -- a destination pixel averages only the
   source pixels that were inside an island, so the 2:1 reduction cannot pull
   padding into an island edge.
3. The padding is then *replaced* by dilating island colour outward, which
   makes the image safe to filter and is strictly better than the flat grey it
   arrived with: it also stops the lowest mip levels bleeding grey into the
   model at distance.
4. After stylisation the padding is rebuilt once more, because quantisation
   moves the island colours it was derived from.

## Metallic is deleted, not simplified

The map is noise around zero on an animal made of hide and keratin. It is
dropped entirely and the shader's Metallic input pinned to 0.0 -- one less
2048-square texture to ship, and a strictly more correct material.
"""

import os
import sys

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import anatomy as A          # noqa: E402

SRC = os.path.join(HERE, "vrescal_sculpt.blend")
OUT = os.path.join(HERE, "vrescal_stylised.blend")
TEXDIR = os.path.join(HERE, "textures")

# ---- the tuning surface ---------------------------------------------------
#
# Everything art-directable is here. Re-running after an edit is one command.

OUT_RES = 1024        # 2048 -> 1024: the cheapest detail reduction there is
COLOURS = 26          # k-means clusters for the base colour
MEDIAN_R = 2          # edge-preserving pre-smooth radius, in pixels
MEDIAN_PASSES = 2     # repeated small medians beat one large one, and are
                      # far cheaper than a true bilateral filter
QUANT_MIX = 0.82      # how far to go toward flat cells. 1.0 is hard posterise,
                      # which bands the smooth belly-to-flank countershading;
                      # this keeps a trace of the original gradient underneath
CONTRAST = 1.14       # A median filter pulls every pixel toward its local
SATURATION = 1.18     # median, which is a contrast and saturation loss as well
                      # as a detail loss -- the animal comes out hazy and grey
                      # next to the original. These two put back only what the
                      # smoothing took; they are a correction, not a grade.
NORMAL_BLUR = 1.4     # radius, in pixels, at the output resolution
NORMAL_STRENGTH = 0.85  # scale on the tangent-space deviation from flat
                      # These two are a pair, and the first attempt got the
                      # balance wrong: blur 3.0 at strength 0.55 removed the
                      # skin pores *and* the cracked-scute edges, which are the
                      # animal's whole signature. A small blur kills detail
                      # below a screen pixel; the strength is what would flatten
                      # the plates, so it stays high.
ROUGH_LEVELS = 5      # quantisation steps for roughness
ROUGH_BLUR = 2.0

MAT_NAME = "Mat_Hide_Vrescal_Stylised"


# --------------------------------------------------------------------------
# Array plumbing
# --------------------------------------------------------------------------

def to_array(img):
    """Image datablock -> (h, w, 4) float32, row 0 at the bottom (as Blender)."""
    a = np.empty(len(img.pixels), dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)


def new_image(name, arr, colorspace):
    h, w = arr.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=False, float_buffer=False)
    img.colorspace_settings.name = colorspace
    img.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
    img.update()
    return img


# --------------------------------------------------------------------------
# UV coverage
# --------------------------------------------------------------------------

def uv_mask(obj, size):
    """Rasterise the mesh's UV triangles into a boolean coverage mask.

    Barycentric, with a small negative tolerance so a pixel straddling a
    triangle edge counts as covered -- island interiors must not end up with
    pinholes, or the padding fill leaks inward.
    """
    mesh = obj.data
    mesh.calc_loop_triangles()
    uvs = np.empty(len(mesh.loops) * 2, dtype=np.float32)
    mesh.uv_layers.active.data.foreach_get("uv", uvs)
    uvs = uvs.reshape(-1, 2)
    idx = np.empty(len(mesh.loop_triangles) * 3, dtype=np.int32)
    mesh.loop_triangles.foreach_get("loops", idx)
    tri = uvs[idx.reshape(-1, 3)] * size

    mask = np.zeros((size, size), dtype=bool)
    for t in range(len(tri)):
        p = tri[t]
        x0, y0 = np.floor(p.min(axis=0)).astype(int) - 1
        x1, y1 = np.ceil(p.max(axis=0)).astype(int) + 1
        x0, y0 = max(0, x0), max(0, y0)
        x1, y1 = min(size, x1), min(size, y1)
        if x1 <= x0 or y1 <= y0:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1) + 0.5,
                             np.arange(y0, y1) + 0.5)
        (ax, ay), (bx, by), (cx, cy) = p
        det = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
        if abs(det) < 1e-12:
            continue
        l1 = ((by - cy) * (gx - cx) + (cx - bx) * (gy - cy)) / det
        l2 = ((cy - ay) * (gx - cx) + (ax - cx) * (gy - cy)) / det
        mask[y0:y1, x0:x1] |= (l1 >= -0.03) & (l2 >= -0.03) & (l1 + l2 <= 1.03)
    return mask


def downsample(arr, mask, factor):
    """Mask-weighted box reduction: padding contributes nothing.

    A plain box filter would average the grey background into every island
    edge pixel, which is precisely the seam halo this module exists to avoid.
    """
    h, w = mask.shape
    h2, w2 = h // factor, w // factor
    m = mask[:h2 * factor, :w2 * factor].reshape(h2, factor, w2, factor)
    weight = m.sum(axis=(1, 3)).astype(np.float32)
    a = arr[:h2 * factor, :w2 * factor, :3].reshape(h2, factor, w2, factor, 3)
    acc = (a * m[..., None]).sum(axis=(1, 3))
    out = np.where(weight[..., None] > 0,
                   acc / np.maximum(weight, 1)[..., None],
                   a.mean(axis=(1, 3)))
    return out.astype(np.float32), weight > 0


def fill_padding(rgb, mask, iters=64):
    """Replace padding with island colour dilated outward.

    Leaves no flat grey anywhere, so later filtering cannot drag it into an
    island and the bottom mip levels stay on-model.
    """
    out = rgb.copy()
    m = mask.copy()
    for _ in range(iters):
        if m.all():
            break
        acc = np.zeros_like(out)
        cnt = np.zeros(m.shape, dtype=np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            acc += np.roll(out, (dy, dx), axis=(0, 1)) * \
                np.roll(m, (dy, dx), axis=(0, 1)).astype(np.float32)[..., None]
            cnt += np.roll(m, (dy, dx), axis=(0, 1)).astype(np.float32)
        grew = (cnt > 0) & ~m
        out[grew] = acc[grew] / cnt[grew][..., None]
        m |= cnt > 0
    if not m.all():
        out[~m] = out[mask].mean(axis=0)
    return out


# --------------------------------------------------------------------------
# Filters
# --------------------------------------------------------------------------

def box_blur(rgb, radius, passes=3):
    """Separable box blur, repeated -- three passes approximate a gaussian."""
    out = rgb.astype(np.float32)
    k = int(max(1, round(radius)))
    for _ in range(passes):
        for axis in (0, 1):
            acc = np.zeros_like(out)
            for s in range(-k, k + 1):
                acc += np.roll(out, s, axis=axis)
            out = acc / (2 * k + 1)
    return out


def median_filter(rgb, radius):
    """True median over a square window. Removes speckle, keeps hard edges.

    An edge-preserving smooth is the pass that matters: a gaussian of the same
    strength would soften the boundaries between the armour plates, which are
    the one piece of high-frequency detail worth keeping.
    """
    stack = [np.roll(np.roll(rgb, dy, axis=0), dx, axis=1)
             for dy in range(-radius, radius + 1)
             for dx in range(-radius, radius + 1)]
    return np.median(np.stack(stack, axis=0), axis=0).astype(np.float32)


def kmeans(pixels, k, iters=16, seed=7):
    """Lloyd's algorithm on a subsample. Deterministic for a fixed seed."""
    rng = np.random.default_rng(seed)
    sample = pixels[rng.choice(len(pixels), min(90000, len(pixels)),
                               replace=False)]
    centres = sample[rng.choice(len(sample), k, replace=False)].copy()
    for _ in range(iters):
        d = ((sample[:, None, :] - centres[None, :, :]) ** 2).sum(axis=2)
        lab = d.argmin(axis=1)
        for i in range(k):
            hit = sample[lab == i]
            if len(hit):
                centres[i] = hit.mean(axis=0)
    return centres


def quantise(rgb, mask, k, mix):
    """Snap colours onto a k-means palette derived from the island pixels only.

    Fitting the palette to the padding as well would spend several of the k
    clusters describing background grey, and the animal would lose that many
    of its actual tones.
    """
    flat = rgb.reshape(-1, 3)
    centres = kmeans(rgb[mask].reshape(-1, 3), k)
    out = np.empty_like(flat)
    step = 200000
    for i in range(0, len(flat), step):
        chunk = flat[i:i + step]
        d = ((chunk[:, None, :] - centres[None, :, :]) ** 2).sum(axis=2)
        out[i:i + step] = centres[d.argmin(axis=1)]
    out = out.reshape(rgb.shape)
    return (out * mix + rgb * (1.0 - mix)).astype(np.float32), centres


def punch(rgb, mask, contrast, saturation):
    """Restore contrast and saturation lost to the median pass.

    Both are measured and applied about the *island* mean rather than the whole
    image, so the padding -- which is about to be regenerated anyway -- cannot
    drag the pivot point around.
    """
    pivot = rgb[mask].mean()
    out = (rgb - pivot) * contrast + pivot
    grey = out.mean(axis=2, keepdims=True)
    return np.clip(grey + (out - grey) * saturation, 0.0, 1.0).astype(np.float32)


def flatten_normal(rgb, blur, strength):
    """Blur the tangent-space relief, then pull it toward flat and renormalise.

    Blurring alone leaves the map still claiming the same slope magnitudes over
    a wider area, which reads as a lumpy, inflated surface. Scaling XY toward
    zero is what actually removes the relief; the renormalise keeps the vector
    unit length so the shading stays energy-correct.
    """
    v = box_blur(rgb, blur) * 2.0 - 1.0
    v[..., 0] *= strength
    v[..., 1] *= strength
    v[..., 2] = np.maximum(v[..., 2], 0.05)
    n = np.linalg.norm(v, axis=2, keepdims=True)
    return ((v / np.maximum(n, 1e-6)) * 0.5 + 0.5).astype(np.float32)


def posterise(rgb, mask, levels, blur):
    """Quantise onto a ladder spanning the map's own range, not 0..1.

    Rounding onto a global 0/¼/½/¾/1 ladder looks harmless and is not: this
    animal's roughness averages 0.68, and a large part of its surface sits just
    below the midpoint, so a global ladder rounds it down to 0.33 and the whole
    creature comes out wet-looking. Fitting the ladder between the 2nd and 98th
    percentiles of the actual data preserves the mean, and the animal stays as
    matte as it arrived.
    """
    v = box_blur(rgb, blur)
    lo, hi = np.percentile(v[mask], (2.0, 98.0))
    if hi - lo < 1e-4:
        return v.astype(np.float32)
    t = np.clip((v - lo) / (hi - lo), 0.0, 1.0)
    t = np.round(t * (levels - 1)) / (levels - 1)
    return (lo + t * (hi - lo)).astype(np.float32)


# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

def rewire(mat, base, normal, rough):
    """Repoint the material at the new maps and pin Metallic to zero."""
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    wanted = {"Base Color": base, "Roughness": rough}

    for node in list(nt.nodes):
        if node.type != 'TEX_IMAGE':
            continue
        targets = [l.to_socket.name for o in node.outputs for l in o.links]
        if "Color" in targets:                      # feeds the normal map node
            node.image = normal
            node.label = normal.name
            continue
        hit = next((t for t in targets if t in wanted), None)
        if hit:
            node.image = wanted[hit]
            node.label = wanted[hit].name
        else:                                       # the metallic map
            nt.nodes.remove(node)

    for link in list(bsdf.inputs['Metallic'].links):
        nt.links.remove(link)
    bsdf.inputs['Metallic'].default_value = 0.0
    mat.name = MAT_NAME


def main():
    A.start()
    with bpy.data.libraries.load(SRC) as (src, dst):
        dst.objects = list(src.objects)
    coll = A.collection("Coll_Vrescal_Stylised")
    for o in dst.objects:
        coll.objects.link(o)
    obj = bpy.data.objects["Mesh_Vrescal_Sculpt"]

    src_res = bpy.data.images[0].size[0]
    factor = src_res // OUT_RES
    print("  rasterising UV coverage at %d..." % src_res)
    mask_hi = uv_mask(obj, src_res)
    print("  UV coverage %.1f%% of the atlas" % (100.0 * mask_hi.mean()))

    maps = {i.name: i for i in bpy.data.images}
    made = {}

    for key, name, space in (("base", "Tex_Vrescal_BaseColor", 'sRGB'),
                             ("normal", "Tex_Vrescal_Normal", 'Non-Color'),
                             ("rough", "Tex_Vrescal_Roughness", 'Non-Color')):
        rgb, mask = downsample(to_array(maps[name]), mask_hi, factor)
        rgb = fill_padding(rgb, mask)

        if key == "base":
            for _ in range(MEDIAN_PASSES):
                rgb = median_filter(rgb, MEDIAN_R)
            rgb, centres = quantise(rgb, mask, COLOURS, QUANT_MIX)
            rgb = punch(rgb, mask, CONTRAST, SATURATION)
            print("  base colour -> %d clusters, %d distinct 8-bit colours"
                  % (len(centres),
                     len(np.unique((rgb.reshape(-1, 3) * 255).astype(np.uint8),
                                   axis=0))))
        elif key == "normal":
            rgb = flatten_normal(rgb, NORMAL_BLUR, NORMAL_STRENGTH)
        else:
            before = rgb[mask].mean()
            rgb = posterise(rgb, mask, ROUGH_LEVELS, ROUGH_BLUR)
            print("  roughness mean %.3f -> %.3f (must not drop, or the "
                  "animal turns glossy)" % (before, rgb[mask].mean()))

        rgb = fill_padding(np.clip(rgb, 0.0, 1.0), mask)
        rgba = np.concatenate(
            [rgb, np.ones(rgb.shape[:2] + (1,), dtype=np.float32)], axis=2)

        for old in [i for i in bpy.data.images if i.name == name]:
            old.name = name + "_src"
        made[key] = new_image(name.replace("Tex_", "Tex_Styl_"), rgba, space)
        grad = (np.abs(np.diff(rgb, axis=0)).mean()
                + np.abs(np.diff(rgb, axis=1)).mean())
        print("  %-8s %dx%d  gradient energy %.4f" % (key, OUT_RES, OUT_RES,
                                                      grad))

    rewire(bpy.data.materials[0], made["base"], made["normal"], made["rough"])

    os.makedirs(TEXDIR, exist_ok=True)
    for img in made.values():
        img.filepath_raw = os.path.join(TEXDIR, "%s.png" % img.name)
        img.file_format = 'PNG'
        img.save()
        img.pack()
        print("  wrote %s" % img.filepath_raw)

    # Drop every source map, including the deleted metallic one. Blender keeps
    # an unlinked image datablock alive until the file is reloaded, so without
    # this the "removed" 2048-square metallic map still ships inside the .blend.
    keep = set(made.values())
    for img in [i for i in bpy.data.images if i not in keep]:
        print("  dropping source map %s (%dx%d)" % (img.name, *img.size))
        bpy.data.images.remove(img)

    print("  images in file: %s" % [i.name for i in bpy.data.images])
    print("  material: %s" % bpy.data.materials[0].name)
    A.save(OUT)


if __name__ == "__main__":
    main()
