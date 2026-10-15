"""Rock pack generator - 100 realistic rock building blocks, one palette material per rock.

Run:  blender --background --python rocks.py -- [--out=<file.blend>] [--thumbs=<dir>]
Writes a NEW rocks.blend next to this script and refuses to run if that file already exists.
The .blend is the source of truth: once it has been opened and edited by hand, never regenerate it.
Materials are linked from ../../palette.blend (run rocks_palette.py first).

How a rock is made: a dense sphere (20 480 triangles) is shaped by a radial function - a noisy ball
intersected with fracture planes (smooth-min, so edges can be crisp or weathered), scooped by a few
concave bites, squeezed in bedding layers, stretched to its real size - and then collapsed down to its
triangle budget, which keeps the fracture faces and ledges and throws away the flat interior."""
import bpy, bmesh, sys, os, math, random, json, uuid
import numpy as np
from math import pi, sin, cos, radians
from mathutils import Vector, Matrix, Euler

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.join(HERE, "rocks.blend")
THUMBS = None
ONLY = None
for a in argv:
    if a.startswith("--out="):
        OUT = a[6:]
    if a.startswith("--thumbs="):
        THUMBS = a[9:]
    if a.startswith("--only="):
        ONLY = a[7:].split(",")
PALETTE = os.path.normpath(os.path.join(HERE, "..", "..", "palette.blend"))
if os.path.exists(OUT) and ".agent-staging" not in OUT:
    raise SystemExit("REFUSED: %s exists. The .blend is the source of truth - edit it in Blender instead." % OUT)
PLAN = json.load(open(os.path.join(HERE, "rocks_materials.json")))
MATNAME, GROUP = PLAN["materials"], PLAN["groups"]
GROUP_ORDER = ["Yellow Desert", "Red Desert", "Grey Stone", "Pastel"]
TYPE_ORDER = ["Boulder", "Chunk", "Block", "Slab", "Strata", "Cliff", "Outcrop", "Pillar", "Wedge", "Ledge", "Rubble"]
POOL = {"Y": ["Gold", "Ochre", "Pale", "Tan", "Mustard"],
        "R": ["Terracotta", "Rust", "Crimson", "Salmon", "Mesa", "Umber"],
        "G": ["GraniteMid", "GraniteDark", "GranitePale", "Slate", "Warm", "Basalt", "Chalk", "Mud"],
        "P": ["Butter", "Pink", "Lilac", "Mint", "Sky", "Peach"]}
PATTERN = {12: "GYRGYRGPYRGP", 10: "GYRGYRGPYR", 8: "GYRGYRPG", 6: "GYRGPR"}
_pool_i = {k: 0 for k in POOL}

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.name = "Rocks"
scene.unit_settings.system = 'METRIC'
os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=OUT)
need = sorted(set(MATNAME.values()))
with bpy.data.libraries.load(PALETTE, link=True, relative=True) as (src, dst):
    missing = [n for n in need if n not in src.materials]
    assert not missing, missing
    dst.materials = need
MATS = {m.name: m for m in bpy.data.materials}

# ------------------------------------------------------------------------------------------------ noise
def _hash(ix, iy, iz, seed):
    n = ix * 374761393 + iy * 668265263 + iz * 2147483647 + seed * 144665
    n = (n ^ (n >> 13)) * 1274126177
    n = n ^ (n >> 16)
    return (n & 0xffff) / 32767.5 - 1.0

def vnoise(P, seed):
    i = np.floor(P).astype(np.int64)
    f = P - i
    u = f * f * f * (f * (f * 6 - 15) + 10)
    x, y, z = i[:, 0], i[:, 1], i[:, 2]
    def L(a, b, t):
        return a + (b - a) * t
    c = [[[_hash(x + dx, y + dy, z + dz, seed) for dz in (0, 1)] for dy in (0, 1)] for dx in (0, 1)]
    x0 = L(L(c[0][0][0], c[0][0][1], u[:, 2]), L(c[0][1][0], c[0][1][1], u[:, 2]), u[:, 1])
    x1 = L(L(c[1][0][0], c[1][0][1], u[:, 2]), L(c[1][1][0], c[1][1][1], u[:, 2]), u[:, 1])
    return L(x0, x1, u[:, 0])

def fbm(P, seed, octv=4, gain=0.5):
    s = np.zeros(len(P)); a = 1.0; t = 0.0; f = 1.0
    for o in range(octv):
        s += a * vnoise(P * f + 17.3 * o, seed + o * 101)
        t += a; a *= gain; f *= 2.03
    return s / t

# ------------------------------------------------------------------------------------------------ base sphere
_BASE = {}
def base(sub):
    if sub not in _BASE:
        bm = bmesh.new()
        bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=1.0)
        bm.verts.ensure_lookup_table()
        D = np.array([v.co[:] for v in bm.verts], dtype=np.float64)
        D /= np.linalg.norm(D, axis=1)[:, None]
        F = np.array([[v.index for v in f.verts] for f in bm.faces], dtype=np.int32)
        bm.free()
        _BASE[sub] = (D, F)
    return _BASE[sub]

def unit(v):
    v = np.asarray(v, dtype=np.float64)
    return v / np.linalg.norm(v)

def rand_dir(rnd):
    return unit([rnd.gauss(0, 1), rnd.gauss(0, 1), rnd.gauss(0, 1)])

def rotm(rx, ry, rz):
    return np.array(Euler((rx, ry, rz)).to_matrix())

# ---- fracture plane sets (normal, distance) in the unit-ball space the rock is shaped in ---------
def planes_random(rnd, n, t0, t1, flat=0.0):
    """Random fracture planes; flat>0 pulls the normals towards horizontal (vertical faces)."""
    out = []
    for _ in range(n):
        d = rand_dir(rnd)
        d[2] *= (1 - flat)
        out.append((unit(d), rnd.uniform(t0, t1)))
    return out

def frame(rnd, tilt=0.15, yaw=None):
    return rotm(rnd.uniform(-tilt, tilt), rnd.uniform(-tilt, tilt), rnd.uniform(0, pi) if yaw is None else yaw)

def planes_joints(rnd, R, t=(0.66, 0.66, 0.66), jit=0.1, tj=0.08, top=True, extra=1):
    """Three roughly perpendicular joint sets - what makes rock break into blocks."""
    out = []
    for ax in range(3):
        for sgn in (1, -1):
            if ax == 2 and sgn == 1 and not top:
                continue
            for j in range(1 + (extra if ax < 2 else 0)):
                e = np.zeros(3); e[ax] = sgn
                d = unit(R @ e + np.array([rnd.gauss(0, jit), rnd.gauss(0, jit), rnd.gauss(0, jit)]))
                out.append((d, t[ax] * (1 + rnd.uniform(-tj, tj) + (0.0 if j == 0 else rnd.uniform(0.03, 0.22)))))
    return out

def plane_bottom(t):
    return [(np.array([0.0, 0.0, -1.0]), t)]

def fracture(P, rnd, seed, R, spacing, strength=0.9, riser=0.22, patch=0.3):
    """Jointing: along each of the three joint directions the surface is snapped onto stepped planes.
    Faces turned towards a joint direction become flat fracture faces separated by crisp steps. The step
    levels drift slowly across the rock (patch, in step units) so no plane is perfectly straight.
    Spacing is in metres."""
    ext = float(np.abs(P).max())
    for ax in range(3):
        s = spacing[ax]
        if not s:
            continue
        a = R[:, ax]
        c = P @ a
        ph = patch * fbm(P / (ext * 0.9) + 31.7 * ax, seed + 57 * ax, 2)
        u = c / s + ph + rnd.random()
        fl = np.floor(u)
        fr = u - fl
        g = np.clip((fr - (0.5 - riser / 2)) / riser, 0, 1)
        g = g * g * (3 - 2 * g)
        P = P + np.outer(((fl + g) - u) * s * strength, a)
    return P

# ------------------------------------------------------------------------------------------------ the rock
def shape(rnd, size, planes, k=40.0, rbase=1.0, warp=0.14, wfreq=1.3, fine=0.02, ffreq=5.0, bites=0, bite_depth=0.14,
          R=None, joints=None, jstrength=0.9, patch=0.3, taper=0.0, shear=(0.0, 0.0), undercut=None, rot=(0, 0, 0), sub=5):
    D, F = base(sub)
    seed = rnd.randrange(1, 10 ** 6)
    off = np.array([rnd.uniform(-40, 40) for _ in range(3)])
    r0 = rbase * (1 + warp * fbm(D * wfreq + off, seed, 3))
    N = np.array([p[0] for p in planes]); T = np.array([p[1] for p in planes])
    dn = D @ N.T
    with np.errstate(divide="ignore", invalid="ignore"):
        rk = np.where(dn > 1e-3, T[None, :] / dn, np.inf)
    allr = np.concatenate([r0[:, None], rk], axis=1)
    mn = allr.min(axis=1)
    r = mn - np.log(np.exp(-k * (allr - mn[:, None])).sum(axis=1)) / k       # smooth minimum
    for _ in range(bites):
        b = rand_dir(rnd)
        ang = np.arccos(np.clip(D @ b, -1, 1))
        r -= rnd.uniform(0.5, 1.0) * bite_depth * np.exp(-(ang / rnd.uniform(0.3, 0.6)) ** 2)
    P = D * r[:, None]
    if undercut:                      # (level, amount, direction): below `level` the rock recedes on one side only
        lvl, amt, ang = undercut
        u = np.array([cos(ang), sin(ang), 0.0])
        w = np.clip((lvl - P[:, 2]) / 0.12, 0, 1)
        w = w * w * (3 - 2 * w)
        P = P - np.outer(amt * w * np.clip(P @ u + 0.15, 0, None), u)
    if taper:
        zt = np.clip((P[:, 2] + 1) / 2, 0, 1.2)
        P[:, :2] *= (1 - taper * zt ** 1.3)[:, None]
    P = P * np.array(size)[None, :]
    if joints:
        P = fracture(P, rnd, seed, R if R is not None else np.eye(3), joints, jstrength, patch=patch)
    if fine:
        m = float(min(size))
        nrm = P / np.linalg.norm(P, axis=1)[:, None]
        P = P + nrm * (fine * m * fbm(P / m * ffreq + off, seed + 7, 3))[:, None]
    P[:, 0] += shear[0] * P[:, 2]
    P[:, 1] += shear[1] * P[:, 2]
    if any(rot):
        P = P @ rotm(*rot).T
    return P, F

ROCKS = []
TOTALS = {"Boulder": 10, "Chunk": 10, "Block": 12, "Slab": 10, "Strata": 12, "Cliff": 12, "Outcrop": 8, "Pillar": 8, "Wedge": 6, "Ledge": 6, "Rubble": 6}
def add(typ, P, F, tris, sharp=28.0, desc=""):
    n = sum(1 for e in ROCKS if e["typ"] == typ)
    g = PATTERN[TOTALS[typ]][n]
    key = POOL[g][_pool_i[g] % len(POOL[g])]
    _pool_i[g] += 1
    ROCKS.append(dict(typ=typ, name="Rock_%s_%02d" % (typ, n + 1), mat=key, P=P, F=F, tris=tris, sharp=sharp, desc=desc))

def R_(typ, i):
    return random.Random("v3-%s-%d" % (typ, i))

def want(typ):
    return not ONLY or typ in ONLY

# ---- Boulders: weathered, edges worn but still broken stone -------------------------------------
for i, sz in enumerate([(3.0, 2.6, 2.2), (4.5, 3.5, 2.6), (2.6, 2.3, 2.5), (6.0, 4.5, 3.5), (3.6, 2.5, 1.7), (5.0, 4.4, 4.2), (7.0, 5.0, 3.2), (2.4, 1.9, 1.6), (4.2, 3.8, 2.3), (8.0, 6.5, 5.0)]):
    r = R_("Boulder", i); R = frame(r, 0.5)
    m = min(sz)
    pl = planes_random(r, r.randint(16, 24), 0.66, 0.98) + plane_bottom(r.uniform(0.55, 0.75))
    P, F = shape(r, sz, pl, k=r.uniform(18, 30), warp=0.2, wfreq=1.3, fine=0.02, bites=r.randint(1, 3), bite_depth=0.12,
                 R=R, joints=(m * r.uniform(0.75, 1.1), m * r.uniform(0.75, 1.1), m * r.uniform(0.7, 1.0)), jstrength=r.uniform(0.25, 0.4))
    add("Boulder", P, F, 620 + 40 * i, 36, "Weathered boulder with worn edges")

# ---- Chunks: freshly broken, sharp fracture faces ------------------------------------------------
for i, sz in enumerate([(3.2, 2.4, 2.0), (4.8, 3.0, 2.4), (2.6, 2.6, 3.2), (6.5, 4.0, 3.0), (3.8, 3.4, 1.8), (5.5, 3.6, 4.4), (7.5, 4.6, 3.6), (2.8, 1.8, 1.9), (4.4, 4.0, 3.0), (9.0, 6.0, 4.5)]):
    r = R_("Chunk", i); R = frame(r, 0.6)
    m = min(sz)
    pl = planes_random(r, r.randint(18, 28), 0.48, 0.96) + plane_bottom(r.uniform(0.5, 0.75))
    P, F = shape(r, sz, pl, k=r.uniform(50, 80), rbase=1.12, warp=0.12, fine=0.012, bites=r.randint(0, 1), bite_depth=0.1,
                 R=R, joints=(m * r.uniform(0.65, 0.95), m * r.uniform(0.65, 0.95), m * r.uniform(0.6, 0.85)), jstrength=r.uniform(0.28, 0.45))
    add("Chunk", P, F, 560 + 40 * i, 26, "Angular broken rock with sharp fracture faces")

# ---- Blocks: jointed, roughly box-like masses ----------------------------------------------------
for i, sz in enumerate([(6, 5, 4), (9, 6, 5), (5, 5, 7), (12, 7, 5), (8, 8, 6), (10, 5, 8), (7, 4.5, 3.5), (14, 9, 7), (6, 6, 9), (11, 8, 4.5), (9, 7, 7), (16, 10, 8)]):
    r = R_("Block", i); R = frame(r, 0.18)
    m = min(sz)
    pl = planes_joints(r, R, (r.uniform(0.6, 0.72), r.uniform(0.6, 0.72), r.uniform(0.6, 0.72))) + planes_random(r, r.randint(14, 22), 0.8, 1.08)
    P, F = shape(r, sz, pl, k=r.uniform(45, 70), rbase=1.3, warp=0.1, fine=0.012, bites=r.randint(1, 2), bite_depth=0.1,
                 R=R, joints=(m * r.uniform(0.5, 0.75), m * r.uniform(0.5, 0.75), m * r.uniform(0.45, 0.7)), jstrength=r.uniform(0.5, 0.72), sub=6)
    add("Block", P, F, 760 + 40 * i, 26, "Jointed block, stack several for a broken rock face")

# ---- Slabs: thick plates and tilted beds ---------------------------------------------------------
for i, sz in enumerate([(10, 7, 1.7), (14, 9, 2.3), (8, 6, 1.4), (18, 10, 3.0), (12, 11, 2.2), (20, 8, 2.6), (9, 5, 1.9), (16, 13, 3.6), (11, 7, 2.8), (24, 14, 4.2)]):
    r = R_("Slab", i); R = frame(r, 0.05)
    pl = planes_joints(r, R, (r.uniform(0.6, 0.75), r.uniform(0.6, 0.75), r.uniform(0.62, 0.72)), jit=0.14, extra=2) + planes_random(r, r.randint(10, 16), 0.78, 1.05, flat=0.6)
    tilt = (r.uniform(-0.25, 0.25), r.uniform(-0.2, 0.2), 0) if i % 3 == 2 else (0, 0, 0)
    P, F = shape(r, sz, pl, k=r.uniform(45, 70), rbase=1.3, warp=0.1, fine=0.012, bites=1, bite_depth=0.07,
                 R=R, joints=(sz[0] * r.uniform(0.3, 0.45), sz[1] * r.uniform(0.35, 0.5), sz[2] * r.uniform(0.6, 0.9)), jstrength=r.uniform(0.55, 0.8), rot=tilt)
    add("Slab", P, F, 560 + 40 * i, 26, "Thick rock plate - a ledge, a floor, or a tilted bed")

# ---- Strata: layered sedimentary masses ----------------------------------------------------------
for i, sz in enumerate([(8, 7, 6), (12, 9, 7), (7, 6, 9), (15, 10, 6), (10, 10, 10), (9, 6, 12), (18, 12, 8), (11, 8, 5), (14, 13, 11), (8, 5.5, 7), (20, 11, 9), (13, 9, 14)]):
    r = R_("Strata", i); R = frame(r, 0.12)
    pl = planes_random(r, r.randint(14, 20), 0.62, 1.0, flat=0.8) + [(np.array([0.0, 0.0, 1.0]), r.uniform(0.72, 0.9))] + plane_bottom(r.uniform(0.7, 0.85))
    bed = r.uniform(1.5, 2.6)
    P, F = shape(r, sz, pl, k=r.uniform(35, 60), rbase=1.2, warp=0.16, wfreq=1.3, fine=0.012, bites=r.randint(1, 3), bite_depth=0.12,
                 R=R, joints=(min(sz[:2]) * r.uniform(0.6, 0.9), min(sz[:2]) * r.uniform(0.6, 0.9), bed), jstrength=r.uniform(0.7, 0.88), sub=6)
    add("Strata", P, F, 1000 + 40 * i, 26, "Layered sedimentary rock with stepped beds")

# ---- Cliffs: tall wall masses with vertical faces ------------------------------------------------
for i, sz in enumerate([(14, 6, 16), (20, 7, 18), (10, 6, 22), (24, 8, 14), (16, 9, 26), (12, 5.5, 12), (28, 9, 20), (9, 7, 18), (18, 6.5, 24), (22, 10, 16), (13, 8, 30), (30, 10, 22)]):
    r = R_("Cliff", i); R = frame(r, 0.08)
    pl = planes_random(r, r.randint(18, 26), 0.55, 0.98, flat=0.93) + [(unit([r.gauss(0, 0.15), r.gauss(0, 0.15), 1.0]), r.uniform(0.75, 0.92))] + plane_bottom(0.9)
    P, F = shape(r, sz, pl, k=r.uniform(45, 70), rbase=1.35, warp=0.12, wfreq=1.6, fine=0.012, bites=r.randint(2, 4), bite_depth=0.1,
                 R=R, joints=(r.uniform(5.0, 9.0), r.uniform(4.0, 7.0), r.uniform(3.5, 6.5)), jstrength=r.uniform(0.7, 0.88), sub=6)
    add("Cliff", P, F, 1100 + 35 * i, 26, "Tall cliff mass - overlap several for a rock wall")

# ---- Outcrops: long low bedrock spines with dipping beds -----------------------------------------
for i, sz in enumerate([(18, 6, 4), (25, 8, 6), (14, 7, 3.5), (30, 9, 7), (20, 10, 5), (16, 5, 5), (35, 10, 8), (22, 7, 4.5)]):
    r = R_("Outcrop", i)
    R = rotm(r.uniform(-0.1, 0.1), r.choice((-1, 1)) * r.uniform(0.25, 0.5), r.uniform(-0.3, 0.3))     # beds dip along the length
    pl = planes_random(r, r.randint(14, 20), 0.55, 0.98) + plane_bottom(r.uniform(0.35, 0.55))
    P, F = shape(r, sz, pl, k=r.uniform(35, 60), rbase=1.12, warp=0.18, wfreq=1.7, fine=0.012, bites=r.randint(1, 3), bite_depth=0.14,
                 R=R, joints=(sz[2] * r.uniform(1.1, 1.6), sz[1] * r.uniform(0.55, 0.85), sz[2] * r.uniform(0.45, 0.7)), jstrength=r.uniform(0.65, 0.85), sub=6)
    add("Outcrop", P, F, 860 + 45 * i, 26, "Long bedrock spine with dipping layers")

# ---- Pillars: stout free-standing stacks ---------------------------------------------------------
for i, sz in enumerate([(6, 5.5, 12), (8, 7, 17), (5, 5, 9), (10, 8, 20), (7, 6, 14), (9, 7.5, 16), (6, 5, 11), (11, 9, 24)]):
    r = R_("Pillar", i); R = frame(r, 0.08)
    pl = planes_random(r, r.randint(16, 22), 0.6, 0.95, flat=0.95) + [(unit([r.gauss(0, 0.25), r.gauss(0, 0.25), 1.0]), r.uniform(0.8, 0.95))] + plane_bottom(0.92)
    P, F = shape(r, sz, pl, k=r.uniform(40, 60), rbase=1.3, warp=0.14, wfreq=1.5, fine=0.012, bites=r.randint(1, 3), bite_depth=0.1,
                 R=R, joints=(min(sz[:2]) * r.uniform(0.65, 0.95), min(sz[:2]) * r.uniform(0.65, 0.95), r.uniform(2.6, 4.6)), jstrength=r.uniform(0.65, 0.85),
                 taper=r.uniform(0.1, 0.3), shear=(r.uniform(-0.06, 0.06), r.uniform(-0.06, 0.06)), sub=6)
    add("Pillar", P, F, 940 + 45 * i, 26, "Free-standing rock stack")

# ---- Wedges: tilted, pointed masses --------------------------------------------------------------
for i, sz in enumerate([(10, 5, 7), (14, 6, 9), (8, 4.5, 6), (16, 8, 10), (12, 5.5, 12), (9, 6, 8)]):
    r = R_("Wedge", i)
    R = rotm(r.choice((-1, 1)) * r.uniform(0.35, 0.7), r.uniform(-0.3, 0.3), r.uniform(0, pi))
    pl = planes_joints(r, R, (r.uniform(0.55, 0.7), r.uniform(0.5, 0.65), r.uniform(0.55, 0.7)), jit=0.15) + planes_random(r, r.randint(10, 16), 0.7, 1.05) + plane_bottom(r.uniform(0.6, 0.8))
    m = min(sz)
    P, F = shape(r, sz, pl, k=r.uniform(45, 70), rbase=1.3, warp=0.12, fine=0.012, bites=r.randint(0, 2), bite_depth=0.1,
                 R=R, joints=(m * r.uniform(0.55, 0.8), m * r.uniform(0.55, 0.8), m * r.uniform(0.4, 0.6)), jstrength=r.uniform(0.45, 0.65), sub=6)
    add("Wedge", P, F, 700 + 50 * i, 26, "Tilted wedge of rock thrusting out of the ground")

# ---- Ledges: wall pieces that overhang on one side -----------------------------------------------
for i, sz in enumerate([(10, 8, 7), (14, 10, 8), (8, 7, 6), (18, 12, 9), (12, 9, 11), (16, 13, 7)]):
    r = R_("Ledge", i); R = frame(r, 0.06)
    pl = planes_random(r, r.randint(14, 20), 0.62, 1.0, flat=0.85) + [(np.array([0.0, 0.0, 1.0]), r.uniform(0.6, 0.8))] + plane_bottom(0.85)
    P, F = shape(r, sz, pl, k=r.uniform(40, 60), rbase=1.25, warp=0.14, fine=0.012, bites=r.randint(1, 2), bite_depth=0.1,
                 R=R, joints=(min(sz[:2]) * r.uniform(0.5, 0.75), min(sz[:2]) * r.uniform(0.5, 0.75), r.uniform(1.9, 3.2)), jstrength=r.uniform(0.65, 0.85),
                 undercut=(r.uniform(0.0, 0.3), r.uniform(0.45, 0.7), r.uniform(0, 2 * pi)), sub=6)
    add("Ledge", P, F, 900 + 50 * i, 26, "Rock mass with a hard cap overhanging on one side")

# ---- Rubble: small rocks for talus and scatter ---------------------------------------------------
for i, sz in enumerate([(1.2, 1.0, 0.8), (1.8, 1.3, 1.0), (0.9, 0.8, 0.7), (2.4, 1.8, 1.3), (1.5, 1.4, 0.7), (3.0, 2.2, 1.6)]):
    r = R_("Rubble", i); R = frame(r, 0.6)
    m = min(sz)
    pl = planes_random(r, r.randint(12, 18), 0.5, 0.95)
    P, F = shape(r, sz, pl, k=r.uniform(40, 70), rbase=1.1, warp=0.15, fine=0.01, R=R, joints=(m * 0.9, m * 0.9, m * 0.8), jstrength=0.3, sub=4)
    add("Rubble", P, F, 200 + 28 * i, 28, "Small loose rock for talus and scatter")

assert len(ROCKS) == 100, len(ROCKS)

# ------------------------------------------------------------------------------------------------ build file
def cat_uuid(c):
    return str(uuid.uuid5(uuid.NAMESPACE_DNS, "spacegame-rocks/" + c))

colls, report = {}, []
for t in TYPE_ORDER:
    c = bpy.data.collections.new("Coll_Rock_" + t)
    scene.collection.children.link(c)
    colls[t] = c
dg = bpy.context.evaluated_depsgraph_get
for e in ROCKS:
    if ONLY and e["name"] not in ONLY and e["typ"] not in ONLY:
        continue
    P, F = e["P"], e["F"]
    P = P - np.array([(P[:, 0].min() + P[:, 0].max()) / 2, (P[:, 1].min() + P[:, 1].max()) / 2, P[:, 2].min()])
    hi = bpy.data.meshes.new("tmp_hi")
    hi.from_pydata(P.tolist(), [], F.tolist())
    hi.update()
    tmp = bpy.data.objects.new("tmp_hi", hi)
    scene.collection.objects.link(tmp)
    md = tmp.modifiers.new("Decimate", 'DECIMATE')
    md.ratio = e["tris"] / len(F)
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg()))
    bpy.data.objects.remove(tmp)
    bpy.data.meshes.remove(hi)
    me.name = "Mesh_" + e["name"]
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-4)
    bmesh.ops.dissolve_degenerate(bm, dist=1e-5, edges=bm.edges[:])
    for _ in range(3):                       # pull in needle-like spike vertices left over from the collapse
        fix = []
        for v in bm.verts:
            if sum(l.calc_angle() for l in v.link_loops) < radians(170):
                nb = [ed.other_vert(v).co for ed in v.link_edges]
                fix.append((v, sum(nb, Vector()) / len(nb)))
        for v, c in fix:
            v.co = v.co.lerp(c, 0.85)
        if not fix:
            break
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    zmin = min(v.co.z for v in bm.verts)
    for v in bm.verts:
        v.co.z -= zmin
    ang = radians(e["sharp"])
    for f in bm.faces:
        f.smooth = True
    for ed in bm.edges:
        ed.smooth = not (len(ed.link_faces) == 2 and ed.calc_face_angle(0.0) > ang)
    nt = sum(len(f.verts) - 2 for f in bm.faces)
    bad = sum(1 for ed in bm.edges if len(ed.link_faces) != 2)
    co = [v.co.copy() for v in bm.verts]
    e["dims"] = tuple(max(c[a] for c in co) - min(c[a] for c in co) for a in range(3))
    bm.to_mesh(me)
    bm.free()
    me.materials.clear()
    me.materials.append(MATS[MATNAME[e["mat"]]])
    ob = bpy.data.objects.new(e["name"], me)
    colls[e["typ"]].objects.link(ob)
    e.update(ob=ob, tris=nt, bad=bad, group=GROUP[e["mat"]])
    del e["P"], e["F"]
    report.append("ROCK %-18s %-14s %-28s tris=%4d nonmanifold=%d size=%.0fx%.0fx%.0f m" % (e["name"], e["group"], MATNAME[e["mat"]], nt, bad, *e["dims"]))
BUILT = [e for e in ROCKS if "ob" in e]

# ---- layout: one row per colour group, sorted by type ------------------------------------------
y = 0.0
for g in GROUP_ORDER:
    row = sorted([e for e in BUILT if e["group"] == g], key=lambda e: (TYPE_ORDER.index(e["typ"]), e["name"]))
    if not row:
        continue
    depth = max(e["dims"][1] for e in row)
    y -= depth / 2 + 12
    x = 0.0
    for e in row:
        x += e["dims"][0] / 2
        e["ob"].location = (x, y, 0)
        x += e["dims"][0] / 2 + 6
    y -= depth / 2 + 12
bpy.context.view_layer.update()

# ---- assets ------------------------------------------------------------------------------------
with open(os.path.join(os.path.dirname(OUT), "blender_assets.cats.txt"), "w") as f:
    f.write("# This is an Asset Catalog Definition file for Blender.\n#\n# Empty lines and lines starting with `#` will be ignored.\n"
            "# The first non-ignored line should be the version indicator.\n"
            "# Other lines are of the format \"UUID:catalog/path/for/assets:simple catalog name\"\n\nVERSION 1\n\n")
    f.write("%s:Rocks:Rocks\n" % cat_uuid("__root__"))
    for g in GROUP_ORDER:
        f.write("%s:Rocks/%s:Rocks-%s\n" % (cat_uuid(g), g, g))
for e in BUILT:
    ob = e["ob"]
    ob.asset_mark()
    ad = ob.asset_data
    ad.catalog_id = cat_uuid(e["group"])
    ad.description = "%s. %d triangles, %.0f x %.0f x %.0f m. %s." % (e["desc"], e["tris"], *e["dims"], MATNAME[e["mat"]])
    ad.author = "Claude for Ferdinand"
    for t in ("Rock", e["typ"], e["group"], MATNAME[e["mat"]].split("_", 2)[2].replace("_", " ")):
        ad.tags.new(t, skip_if_exists=True)

w = bpy.data.worlds.new("World_Rocks")
w.use_nodes = True
bg = w.node_tree.nodes["Background"]
bg.inputs[0].default_value = (0.75, 0.83, 0.95, 1)
bg.inputs[1].default_value = 0.9
scene.world = w

# ---- previews + contact sheets -----------------------------------------------------------------
def render_previews(thumbs):
    os.makedirs(thumbs, exist_ok=True)
    ps = bpy.data.scenes.new("Rocks_PreviewTmp")
    pw = bpy.data.worlds.new('World_RocksPreview'); pw.use_nodes = True
    pw.node_tree.nodes['Background'].inputs[0].default_value = (0.7, 0.8, 1.0, 1); pw.node_tree.nodes['Background'].inputs[1].default_value = 0.38
    ps.world = pw
    ps.render.engine = 'CYCLES'
    ps.cycles.samples = 32
    ps.cycles.use_denoising = False
    ps.cycles.max_bounces = 2
    ps.render.resolution_x = ps.render.resolution_y = 256
    ps.render.film_transparent = True
    ps.render.image_settings.file_format = 'PNG'
    ps.render.image_settings.color_mode = 'RGBA'
    ps.view_settings.view_transform = 'Standard'
    cd = bpy.data.cameras.new("Rocks_PrevCam"); cd.type = 'ORTHO'
    cam = bpy.data.objects.new("Rocks_PrevCam", cd); ps.collection.objects.link(cam); ps.camera = cam
    sd = bpy.data.lights.new("Rocks_PrevSun", 'SUN'); sd.energy = 4.2; sd.angle = 0.06
    sun = bpy.data.objects.new("Rocks_PrevSun", sd); ps.collection.objects.link(sun)
    sun.rotation_euler = (1.0, 0.0, 1.5)
    d = Vector((0.8, -1.0, 0.42)).normalized()
    cam.rotation_euler = d.to_track_quat('Z', 'Y').to_euler()
    rot = cam.rotation_euler.to_matrix()
    rx, ry = rot @ Vector((1, 0, 0)), rot @ Vector((0, 1, 0))
    tiles = []
    for e in BUILT:
        ob = e["ob"]
        ps.collection.objects.link(ob)
        pts = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
        ctr = sum(pts, Vector()) / 8
        ext = max(max(abs((p - ctr).dot(rx)), abs((p - ctr).dot(ry))) for p in pts)
        cd.ortho_scale = ext * 2.1
        size = max((p - ctr).length for p in pts)
        cam.location = ctr + d * (size * 4 + 20)
        cd.clip_end = size * 10 + 100
        path = os.path.join(thumbs, ob.name + ".png")
        ps.render.filepath = path
        bpy.ops.render.render(write_still=True, scene=ps.name)
        ps.collection.objects.unlink(ob)
        img = bpy.data.images.load(path)
        px = np.empty(256 * 256 * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        pv = ob.preview_ensure()
        pv.image_size = (256, 256)
        pv.image_pixels_float.foreach_set(px)
        bpy.data.images.remove(img)
        tiles.append(px.reshape(256, 256, 4))
    bpy.data.objects.remove(cam); bpy.data.objects.remove(sun)
    bpy.data.cameras.remove(cd); bpy.data.lights.remove(sd)
    bpy.data.scenes.remove(ps); bpy.data.worlds.remove(pw)
    order = sorted(range(len(BUILT)), key=lambda i: (TYPE_ORDER.index(BUILT[i]["typ"]), BUILT[i]["name"]))
    for s in range(0, len(order), 20):
        sheet = np.zeros((4 * 256, 5 * 256, 4), dtype=np.float32)
        sheet[..., :3] = 0.42; sheet[..., 3] = 1.0
        for k_, i in enumerate(order[s:s + 20]):
            t = tiles[i]; row, col = k_ // 5, k_ % 5
            y0 = (3 - row) * 256; x0 = col * 256
            a = t[..., 3:4]
            sheet[y0:y0 + 256, x0:x0 + 256, :3] = t[..., :3] * a + sheet[y0:y0 + 256, x0:x0 + 256, :3] * (1 - a)
            print("SHEET %d cell r%d c%d = %s" % (s // 20 + 1, row + 1, col + 1, BUILT[i]["ob"].name))
        im = bpy.data.images.new("sheet", 5 * 256, 4 * 256, alpha=True)
        im.pixels.foreach_set(sheet.ravel())
        im.filepath_raw = os.path.join(thumbs, "_sheet_%d.png" % (s // 20 + 1))
        im.file_format = 'PNG'
        im.save()
        bpy.data.images.remove(im)

if THUMBS:
    render_previews(THUMBS)

for l in report:
    print(l)
ts = [e["tris"] for e in BUILT]
print("TOTAL rocks %d  tris min %d max %d sum %d  nonmanifold %d  materials %d" % (
    len(BUILT), min(ts), max(ts), sum(ts), sum(e["bad"] for e in BUILT), len(set(MATNAME[e["mat"]] for e in BUILT))))
bpy.ops.wm.save_as_mainfile(filepath=OUT, compress=True)
print("SAVED", OUT)
