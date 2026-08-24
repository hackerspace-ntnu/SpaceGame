"""Generate the leash's braid albedo and normal map.

A LineRenderer is a camera-facing ribbon: U runs along the rope, V across its width. So the
texture's X axis is length (and must tile) and its Y axis is the cross-section. Nearly all the
cylinder-ness comes from the NORMAL map, because the geometry is flat — that is the trick that
makes a flat strip read as round from every angle.
"""
import numpy as np
from PIL import Image

W, H = 256, 64          # x = along the rope and tiles; y = across the width
STRANDS = 3             # a real rope is three strands laid together
LAY = 1.6               # how many times a strand wraps across the width per repeat

xs = np.linspace(0.0, 1.0, W, endpoint=False)[None, :]
ys = np.linspace(0.0, 1.0, H, endpoint=False)[:, None]

# Where in its own strand each texel sits. The +ys term is the lay: it shears the bands into the
# diagonal that makes rope look like rope rather than like a ribbed hose.
phase = (xs * STRANDS + ys * LAY) % 1.0

# A strand is round in cross-section too, so the seam between two of them is a valley.
strand = np.cos((phase - 0.5) * 2.0 * np.pi) * 0.5 + 0.5      # 1 at the crown, 0 at the seam

# The rope's own cross-section: dark at both silhouette edges, bright along the centre line.
across = np.cos((ys - 0.5) * np.pi)                            # 1 centre, 0 at the edges
across = np.repeat(across, W, axis=1)

rng = np.random.default_rng(7)
fibre = rng.normal(0.0, 1.0, (H, W))
fibre = (fibre + np.roll(fibre, 1, axis=1) + np.roll(fibre, 1, axis=0)) / 3.0

# ── Albedo ────────────────────────────────────────────────────────────────────
# Only a light touch of shading is baked in — an ambient-occlusion hint in the seams and at the
# silhouette. The rest is left to the normal map and the actual lights, or the rope would read as
# lit from a direction it is not.
light = np.array([0.68, 0.57, 0.39])
dark = np.array([0.34, 0.27, 0.17])

# strand is raised to a power so the seam between two strands is a narrow dark line rather than
# a broad gradient. A rope read at ten metres is mostly its seams.
t = 0.18 + 0.55 * (strand ** 0.65) + 0.27 * (across ** 1.4)
t = np.clip(t + fibre * 0.05, 0.0, 1.0)

albedo = dark + (light - dark) * t[:, :, None]
albedo = np.clip(albedo, 0.0, 1.0)
Image.fromarray((albedo * 255).astype(np.uint8)).save("rope_braid_albedo.png")

# ── Normal ────────────────────────────────────────────────────────────────────
# Two shapes summed: the rope's whole cross-section curving across Y, and each strand's own
# smaller curve running along its diagonal.
ny_rope = np.repeat(np.sin((ys - 0.5) * np.pi), W, axis=1) * 0.90

# The strand curve is kept well under the rope curve. Summed at full strength the two saturate,
# the square root below clamps, and the result is a FLATTER rope than either shape alone.
groove = np.sin((phase - 0.5) * 2.0 * np.pi)
gx, gy = STRANDS, LAY
gl = np.hypot(gx, gy)
nx = groove * (gx / gl) * 0.30
ny = ny_rope * (1.0 - 0.30 * np.abs(groove)) + groove * (gy / gl) * 0.30

nx = nx + fibre * 0.04
ny = ny + fibre * 0.04

nz = np.sqrt(np.clip(1.0 - nx * nx - ny * ny, 0.02, 1.0))
length = np.sqrt(nx * nx + ny * ny + nz * nz)

normal = np.stack([nx / length, ny / length, nz / length], axis=-1)
normal = (normal * 0.5 + 0.5)
Image.fromarray((normal * 255).astype(np.uint8)).save("rope_braid_normal.png")

print("wrote rope_braid_albedo.png and rope_braid_normal.png at %dx%d" % (W, H))
