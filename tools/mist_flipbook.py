"""Bakes the airlock mist's six-way lit smoke flipbook (ColonyInterior.md, "Airlock effects").

A billowing puff is modelled as a 3D density volume per frame, and each frame is rendered six times,
lit from +X, +Y, +Z, -X, -Y and -Z: the "six-way lighting" smoke technique (Unity's VFX Graph ships it,
https://unity.com/blog/engine-platform/realistic-smoke-with-6-way-lighting-in-vfx-graph). The shader
blends the six by the light's direction in the particle's own frame, so a flat billboard catches a
lamp from the side, from above or from behind like a volume would.

Lighting is single scattering with Beer-Lambert transmittance. Along a grid axis the transmittance
toward a light is one cumulative sum, so the whole bake is a few numpy array passes per frame.

Output (8 x 8 frames of 128 px, linear data, NOT colour):
    AirlockMist_Positive.png   R = lit from +X (right)  G = lit from +Y (top)     B = lit from -Z (behind)  A = coverage
    AirlockMist_Negative.png   R = lit from -X (left)   G = lit from -Y (bottom)  B = lit from +Z (front)   A = erosion detail

The viewer looks down -Z, so "+Z" is the camera's side. Every frame is faded to zero well inside its
cell: a puff that touched its cell edge would be cut off as a straight line, which is exactly the
"rectangular" look this replaces.

Run from the repo root:  python3 tools/mist_flipbook.py [output folder, default below]
"""
import sys
from multiprocessing import Pool
from pathlib import Path

import numpy as np
from PIL import Image

OUT_DIR = Path("Assets/Game/Art/Textures/Effects")

GRID = 8                 # frames per row and per column
FRAME_PX = 128           # one frame's side, in pixels
DEPTH = 64               # voxels along the view axis
SEED = 1729

EXTINCTION = 7.0         # optical depth per unit of density across the whole frame
LIGHT_EXTINCTION = 4.0   # softer toward the lights: a cheap stand-in for multiple scattering
AMBIENT_SCATTER = 0.18   # light that reaches every voxel whatever the shadowing, so cores never go black
CELL_FADE = (0.80, 0.97) # radius (in half-cells) where coverage starts and finishes fading to zero
LOBES = 7                # lumps the puff is built from
SMOOTH_UNION = 6.0       # sharpness of the smooth maximum joining the lumps; higher creases more
BODY_RADIUS = 0.86     # the ball's radius, in half-cells, before the warp bends it
EXPOSURE_PERCENTILE = 99.5


def value_noise(lattice, p):
    """Trilinear value noise on a tiling random lattice; p is (..., 3) in lattice cells."""
    n = lattice.shape[0]
    cell = np.floor(p).astype(np.int64)
    f = p - cell
    f = f * f * (3.0 - 2.0 * f)
    x0, y0, z0 = (cell[..., i] % n for i in range(3))
    x1, y1, z1 = ((cell[..., i] + 1) % n for i in range(3))
    fx, fy, fz = f[..., 0], f[..., 1], f[..., 2]

    def lerp(a, b, t):
        return a + (b - a) * t

    c00 = lerp(lattice[x0, y0, z0], lattice[x1, y0, z0], fx)
    c10 = lerp(lattice[x0, y1, z0], lattice[x1, y1, z0], fx)
    c01 = lerp(lattice[x0, y0, z1], lattice[x1, y0, z1], fx)
    c11 = lerp(lattice[x0, y1, z1], lattice[x1, y1, z1], fx)
    return lerp(lerp(c00, c10, fy), lerp(c01, c11, fy), fz)


def fbm(lattice, p, octaves, gain=0.5):
    total, amp, norm = 0.0, 1.0, 0.0
    for o in range(octaves):
        total = total + amp * value_noise(lattice, p * (2.0 ** o) + o * 17.3)
        norm += amp
        amp *= gain
    return total / norm


def bake_frame(index):
    t = index / (GRID * GRID - 1)
    rng = np.random.default_rng(SEED)
    lattice = rng.random((32, 32, 32)).astype(np.float32)
    lobe_dirs = rng.normal(size=(LOBES, 3))
    lobe_dirs /= np.linalg.norm(lobe_dirs, axis=1, keepdims=True)
    lobe_sizes = rng.uniform(0.32, 0.46, LOBES)

    xs = np.linspace(-1.0, 1.0, FRAME_PX, dtype=np.float32)
    zs = np.linspace(-1.0, 1.0, DEPTH, dtype=np.float32)
    # Image rows run top to bottom, so Y is flipped: row 0 is +Y.
    y, x, z = np.meshgrid(-xs, xs, zs, indexing="ij")
    p = np.stack([x, y, z], axis=-1)

    # The puff swells and its lumps drift apart as it ages; the noise domain is pulled along with it
    # (divided by the growth) so the billows read as the same matter rolling outward, not a new texture.
    growth = 1.0 + 0.25 * t
    q = p / growth

    # Domain warp: the turbulent roll. Time moves the warp field so the lumps churn frame to frame.
    swirl = np.stack([fbm(lattice, q * 1.8 + [t * 1.2, 3.1, 0.0], 3),
                      fbm(lattice, q * 1.8 + [7.7, t * 1.2, 1.3], 3),
                      fbm(lattice, q * 1.8 + [2.9, 5.3, t * 1.2], 3)], axis=-1) - 0.5
    warped = q + swirl * (0.35 + 0.35 * t)

    # The body: a soft ball plus a ring of lumps, all bent by the same warp so they read as one mass.
    # Joined with a smooth maximum: a hard max leaves straight creases between lumps, which is the
    # polygonal silhouette the old look was faulted for.
    union = np.exp(SMOOTH_UNION * (1.0 - np.linalg.norm(warped, axis=-1) / BODY_RADIUS))
    lobe_dirs -= lobe_dirs.mean(axis=0)
    for d, s in zip(lobe_dirs, lobe_sizes):
        centre = d * (0.40 + 0.12 * t)
        r = np.linalg.norm(warped - centre, axis=-1)
        union += np.exp(SMOOTH_UNION * (0.85 * (1.0 - r / s)))
    field = np.clip(np.log(union) / SMOOTH_UNION, 0.0, 1.0)

    # Billows: high-frequency fbm carved out of the body, deepest at the edge, so the rim breaks into
    # curls while the core stays dense.
    billow = fbm(lattice, warped * 4.0 + [0.0, -t * 2.0, 0.0], 5)
    carve = (1.0 - billow) * (0.75 + 0.5 * t)

    # Ageing: the threshold rises, so the puff thins from its edges and wisps apart into nothing.
    threshold = 0.05 + 0.22 * t ** 1.3
    density = np.clip((field * 1.7 - carve - threshold) * 2.2, 0.0, 1.0)
    density *= 1.0 - 0.6 * t

    dz = 2.0 / DEPTH
    dxy = 2.0 / FRAME_PX
    sigma = EXTINCTION / 2.0          # density 1 across the full frame = EXTINCTION optical depth
    sigma_l = LIGHT_EXTINCTION / 2.0

    def toward(axis, positive):
        """Transmittance from each voxel to a light far along +axis (positive) or -axis."""
        step = dxy if axis < 2 else dz
        d = density if positive else np.flip(density, axis=axis)
        # Exclusive cumulative sum: the light travels from the far side up to (not through) the voxel.
        tau = (np.flip(np.cumsum(np.flip(d, axis=axis), axis=axis), axis=axis) - d) * sigma_l * step
        trans = np.exp(-tau)
        return trans if positive else np.flip(trans, axis=axis)

    # Viewer at +Z: transmittance from each voxel to the eye.
    view_tau = (np.flip(np.cumsum(np.flip(density, axis=2), axis=2), axis=2) - density) * sigma * dz
    view_trans = np.exp(-view_tau)
    weight = density * view_trans * sigma * dz           # what each voxel contributes to the pixel
    coverage = 1.0 - np.exp(-np.sum(density, axis=2) * sigma * dz)

    # Image axis 0 runs top to bottom (row 0 is +Y), axis 1 is +X, axis 2 is +Z toward the viewer.
    lights = {}
    for name, axis, positive in [("+x", 1, True), ("-x", 1, False), ("+z", 2, True), ("-z", 2, False)]:
        trans = toward(axis, positive)
        lights[name] = np.sum(weight * (AMBIENT_SCATTER + (1 - AMBIENT_SCATTER) * trans), axis=2)
    # Row 0 is the top: a light from +Y arrives at row 0 first, i.e. along -axis0.
    up_trans = toward(0, False)
    down_trans = toward(0, True)
    lights["+y"] = np.sum(weight * (AMBIENT_SCATTER + (1 - AMBIENT_SCATTER) * up_trans), axis=2)
    lights["-y"] = np.sum(weight * (AMBIENT_SCATTER + (1 - AMBIENT_SCATTER) * down_trans), axis=2)

    # Unpremultiply: the shader blends with SrcAlpha, so a lightmap holds the radiance, not radiance x alpha.
    safe = np.maximum(coverage, 1e-3)
    radiance = {k: v / safe for k, v in lights.items()}

    r = np.sqrt(xs[None, :] ** 2 + xs[:, None] ** 2)
    fade = 1.0 - np.clip((r - CELL_FADE[0]) / (CELL_FADE[1] - CELL_FADE[0]), 0.0, 1.0)
    fade = fade * fade * (3 - 2 * fade)
    coverage = coverage * fade

    # Erosion detail: fine projected noise the shader eats into as the particle ages.
    plane = np.stack([x[..., 0] * 5.0, y[..., 0] * 5.0, np.full_like(x[..., 0], t * 2.0)], axis=-1)
    detail = fbm(lattice, plane / growth, 3)
    detail = (detail - detail.min()) / max(1e-6, detail.max() - detail.min())
    return index, radiance, coverage, detail


def main(out_dir):
    with Pool() as pool:
        frames = pool.map(bake_frame, range(GRID * GRID))

    # One exposure for every frame and direction, so the six maps stay comparable to each other.
    covered = np.concatenate([np.stack(list(f[1].values()))[:, f[2] > 0.05].ravel() for f in frames])
    exposure = 1.0 / max(1e-6, np.percentile(covered, EXPOSURE_PERCENTILE))

    size = GRID * FRAME_PX
    pos = np.zeros((size, size, 4), np.float32)
    neg = np.zeros((size, size, 4), np.float32)
    for index, radiance, coverage, detail in frames:
        row, col = divmod(index, GRID)
        sl = (slice(row * FRAME_PX, (row + 1) * FRAME_PX), slice(col * FRAME_PX, (col + 1) * FRAME_PX))
        pos[sl] = np.stack([radiance["+x"], radiance["+y"], radiance["-z"], coverage], axis=-1)
        neg[sl] = np.stack([radiance["-x"], radiance["-y"], radiance["+z"], detail], axis=-1)
    pos[..., :3] *= exposure
    neg[..., :3] *= exposure

    out_dir.mkdir(parents=True, exist_ok=True)
    for name, img in [("AirlockMist_Positive.png", pos), ("AirlockMist_Negative.png", neg)]:
        data = (np.clip(img, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)
        Image.fromarray(data, "RGBA").save(out_dir / name)
        print(out_dir / name, data.shape)


if __name__ == "__main__":
    main(Path(sys.argv[1]) if len(sys.argv) > 1 else OUT_DIR)
