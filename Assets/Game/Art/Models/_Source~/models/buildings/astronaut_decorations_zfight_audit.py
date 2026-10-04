"""Z-fight audit of the astronaut prop library: coplanar overlapping faces per prop, closed AND open poses.

    blender -b astronaut_decorations.blend --python astronaut_decorations_zfight_audit.py -- <out.json> [PropName ...]
    blender -b --python astronaut_decorations_zfight_audit.py -- <out.json> --fbx     (audits the SHIPPED FBX instead)

Expected result: "TOTAL visible 0 same-facing different-material 0". Anything else is a face that will z-fight in Unity
(or is one placement away from it): fix it in the prop's source, never by nudging the prefab.

For every prop, all mesh objects (Body + moving parts) are evaluated in prop space, posed CLOSED (as authored) and OPEN
(airlock leaves slid by the prefabs' AirlockHatch.openOffset, OuterHatch_Door swung +100 deg about Blender Z = its
openEuler (0,0,-100), beacon rotor turned 45 deg). Every triangle is tagged with its SHELL: the connected component it
belongs to inside its object (the props are unions of unwelded primitives, so one shell = one box / cylinder / plate).

A pair of shells is COPLANAR when two of their triangles have |normal dot| > 0.999, every vertex of each within 4.9 mm of
the other's plane, and their in-plane intersection summed over the pair (per plane) exceeds 0.5 cm^2.
  same      - both normals the same way: both faces render from the same side.
  back2back - opposite normals: with back-face culling they never meet in one pixel (counted, not a fight), unless a
              transparent material (Glass/Water) is involved, which is listed as visible.
A same-facing pair is VISIBLE when a sample point of its overlap, 0.5 mm in front of the outer of the two planes, lies
outside every CLOSED opaque shell of the prop (other than the two) - a plane test for convex shells, ray parity
(majority of three directions) for the rest. same_diffmat_all lists every same-facing pair of different materials
whether visible or buried. No plane is exempt, not even the floor or the wall a prop backs onto.
"""
import bpy, sys, os, glob, json, math, collections
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

args = sys.argv[sys.argv.index('--') + 1:]
OUT = args[0]
FBX_MODE = '--fbx' in args[1:]
ONLY = set(a for a in args[1:] if not a.startswith('--'))
MOUNTS = {}
# planes nearer than 4.9 mm count: the builder separates every pair by 5 mm, and a 3 mm cutoff let pairs sitting
# exactly 3.0 mm apart slip through on float noise (they do fight in Unity). DB must be >= DIST.
DOT, DIST, MIN_AREA = 0.999, 0.0049, 0.5e-4
NB, DB = 40, 0.005
SLIDE = {'AirlockBulkhead': 1.93, 'AirlockVestibule': 1.38}      # prefab AirlockHatch.openOffset (Unity x)
TRANSPARENT = ('Mat_Astro_Glass', 'Mat_Astro_Water')
RAY_DIRS = (Vector((0.5413, 0.3112, 0.7814)).normalized(), Vector((-0.4021, 0.7713, -0.4934)).normalized(),
            Vector((0.2711, -0.6619, 0.3141)).normalized())


def clip(poly, a, b):
    out = []
    n = len(poly)
    for i in range(n):
        p, q = poly[i], poly[(i + 1) % n]
        sp = (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0])
        sq = (b[0] - a[0]) * (q[1] - a[1]) - (b[1] - a[1]) * (q[0] - a[0])
        if sp >= 0:
            out.append(p)
        if (sp >= 0) != (sq >= 0):
            t = sp / (sp - sq)
            out.append((p[0] + t * (q[0] - p[0]), p[1] + t * (q[1] - p[1])))
    return out


def area2(poly):
    s = 0.0
    for i in range(len(poly)):
        x1, y1 = poly[i]
        x2, y2 = poly[(i + 1) % len(poly)]
        s += x1 * y2 - x2 * y1
    return s / 2


def overlap(t1, t2):
    if area2(t2) < 0:
        t2 = [t2[0], t2[2], t2[1]]
    poly = list(t1)
    for i in range(3):
        poly = clip(poly, t2[i], t2[(i + 1) % 3])
        if len(poly) < 3:
            return 0.0, None
    return abs(area2(poly)), poly


def basis(n):
    a = Vector((1, 0, 0)) if abs(n.x) < 0.9 else Vector((0, 1, 0))
    u = n.cross(a).normalized()
    return u, n.cross(u)


def canon(n):
    """(n, 1) or (-n, -1), signed so the DOMINANT component is positive: a near-axis normal never flips bins on a
    1e-4 tilt (taking the first non-zero component did, and hid a 0.7 mm pair)."""
    c = max((n.x, n.y, n.z), key=abs)
    return (n, 1) if c >= 0 else (-n, -1)


def pose_matrix(prop, oname, pose):
    if pose == 'open':
        if prop in SLIDE and oname.endswith('_LeafA'):
            return Matrix.Translation((-SLIDE[prop], 0, 0))
        if prop in SLIDE and oname.endswith('_LeafB'):
            return Matrix.Translation((SLIDE[prop], 0, 0))
    return None


def gather(coll, pose):
    off = coll.instance_offset
    prop = coll.name[len('Coll_AstroDeco_'):]
    dg = bpy.context.evaluated_depsgraph_get()
    tris = []           # (shell, mat, n, d, verts)
    shells = []         # (object, closed, bbmin, bbmax, mats)
    allv, allt, tri_shell = [], [], []
    for o in sorted(coll.all_objects, key=lambda o: o.name):
        if o.type != 'MESH':
            continue
        mw = Matrix.Translation(-off) @ o.matrix_world
        if pose == 'open' and o.name == 'OuterHatch_Door':
            loc = mw.to_translation()
            mw = Matrix.Translation(loc) @ Matrix.Rotation(math.radians(100), 4, 'Z') @ Matrix.Translation(-loc) @ mw
        if pose == 'open' and o.name.endswith('_BeaconRotor'):
            mw = mw @ Matrix.Rotation(math.radians(45), 4, 'Z')
        pm = pose_matrix(prop, o.name, pose)
        if pm is not None:
            mw = pm @ mw
        me = o.evaluated_get(dg).to_mesh()
        me.calc_loop_triangles()
        ws = [mw @ v.co for v in me.vertices]
        # shells: union-find over polygon vertices
        parent = list(range(len(me.vertices)))

        def find(i):
            while parent[i] != i:
                parent[i] = parent[parent[i]]
                i = parent[i]
            return i
        for p in me.polygons:
            vs = p.vertices
            r0 = find(vs[0])
            for v in vs[1:]:
                r = find(v)
                if r != r0:
                    parent[r] = r0
        edge_use = collections.Counter()
        for p in me.polygons:
            vs = list(p.vertices)
            for i in range(len(vs)):
                a, b = vs[i], vs[(i + 1) % len(vs)]
                edge_use[(min(a, b), max(a, b))] += 1
        sid = {}
        open_roots = set(find(a) for (a, b), c in edge_use.items() if c != 2)
        mats = [s.material.name if s.material else '-' for s in o.material_slots]
        base = len(allv)
        allv.extend(ws)
        for lt in me.loop_triangles:
            root = find(lt.vertices[0])
            if root not in sid:
                sid[root] = len(shells)
                shells.append({'object': o.name, 'closed': root not in open_roots, 'lo': Vector((1e9,) * 3),
                               'hi': Vector((-1e9,) * 3), 'mats': set(), 'planes': [], 'pts': []})
            s = sid[root]
            vs = [ws[i] for i in lt.vertices]
            sh = shells[s]
            for v in vs:
                sh['lo'] = Vector(map(min, sh['lo'], v))
                sh['hi'] = Vector(map(max, sh['hi'], v))
            m = mats[lt.material_index] if lt.material_index < len(mats) else '-'
            sh['mats'].add(m)
            allt.append([base + i for i in lt.vertices])
            tri_shell.append(s)
            nn = (vs[1] - vs[0]).cross(vs[2] - vs[0])
            if nn.length < 1e-9:
                continue
            nn.normalize()
            sh['planes'].append((nn, nn.dot(vs[0])))
            sh['pts'].extend(vs)
            tris.append((s, m, nn, nn.dot(vs[0]), vs))
        o.evaluated_get(dg).to_mesh_clear()
    for sh in shells:
        sh['convex'] = sh['closed'] and all(n.dot(v) - d < 1e-6 for n, d in sh['planes'] for v in sh['pts'][::3])
    bvh = BVHTree.FromPolygons(allv, allt, all_triangles=True, epsilon=0.0)
    return tris, shells, bvh, tri_shell


def inside_shells(bvh, tri_shell, shells, p):
    """Closed, opaque shells containing p (ray parity, majority of three directions)."""
    inside = set()
    for i, sh in enumerate(shells):
        if sh['convex'] and not (sh['mats'] & set(TRANSPARENT)) and all(sh['lo'][k] - 1e-6 <= p[k] <= sh['hi'][k] + 1e-6 for k in range(3)):
            if max(n.dot(p) - d for n, d in sh['planes']) < -1e-5:
                inside.add(i)
    votes = collections.Counter()
    for d in RAY_DIRS:
        cnt = collections.Counter()
        o = Vector(p)
        for _ in range(400):
            loc, nrm, idx, dist = bvh.ray_cast(o, d)
            if loc is None:
                break
            cnt[tri_shell[idx]] += 1
            o = loc + d * 1e-5
        for s, c in cnt.items():
            if c % 2 == 1 and shells[s]['closed'] and not shells[s]['convex'] and not (shells[s]['mats'] & set(TRANSPARENT)):
                votes[s] += 1
    return inside | set(s for s, v in votes.items() if v >= 2)


def on_mount(n, pt, lo, hi, mounts):
    for m in mounts:
        i = 'XYZ'.index(m[1])
        sgn = 1 if m[0] == '+' else -1
        if n[i] * sgn > 0.999:
            ext = hi[i] if sgn > 0 else lo[i]
            if abs(pt[i] - ext) < 0.004 and (m != '-Z' or lo.z < 0.03):
                return True
    return False


def audit(coll, pose, mounts):
    tris, shells, bvh, tri_shell = gather(coll, pose)
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for sh in shells:
        lo = Vector(map(min, lo, sh['lo']))
        hi = Vector(map(max, hi, sh['hi']))
    bins = collections.defaultdict(list)
    for i, (s, m, n, d, vs) in enumerate(tris):
        cn, sg = canon(n)
        bins[(round(cn.x * NB), round(cn.y * NB), round(cn.z * NB), math.floor(d * sg / DB))].append(i)
    groups = {}     # (sa, sb, kind) -> dict
    keys = sorted(bins)
    for key in keys:
        nx, ny, nz, dk = key
        cand = [key]
        for ddx in (-1, 0, 1):
            for ddy in (-1, 0, 1):
                for ddz in (-1, 0, 1):
                    for dd in (-1, 0, 1):
                        k2 = (nx + ddx, ny + ddy, nz + ddz, dk + dd)
                        if k2 != key and k2 in bins and k2 > key:
                            cand.append(k2)
        idx_a = bins[key]
        for k2 in cand:
            idx_b = bins[k2]
            for ia in idx_a:
                sa, ma, na, da, va = tris[ia]
                u, v = basis(na)
                ta = None
                for ib in idx_b:
                    if k2 == key and ib <= ia:
                        continue
                    sb, mb, nb, db, vb = tris[ib]
                    if sa == sb:
                        continue
                    dot = na.dot(nb)
                    if abs(dot) < DOT:
                        continue
                    if max(abs(na.dot(p) - da) for p in vb) > DIST or max(abs(nb.dot(p) - db) for p in va) > DIST:
                        continue
                    if ta is None:
                        ta = [(p.dot(u), p.dot(v)) for p in va]
                    tb = [(p.dot(u), p.dot(v)) for p in vb]
                    ar, poly = overlap(ta, tb)
                    if ar <= 1e-7:
                        continue
                    kind = 'same' if dot > 0 else 'back2back'
                    nk = na if (sa < sb or dot > 0) else -na
                    gk = (min(sa, sb), max(sa, sb), kind) + tuple(round(c * 20) for c in nk)
                    g = groups.setdefault(gk, {'area': 0.0, 'polys': [], 'n': na.copy(), 'd': max(da, db) if dot > 0 else da,
                                               'mats': set(), 'u': u, 'v': v, 'tri_pairs': 0})
                    g['area'] += ar
                    g['tri_pairs'] += 1
                    g['mats'].update((ma, mb))
                    if len(g['polys']) < 6:
                        g['polys'].append((ar, poly, u, v, na.copy(), max(na.dot(p) for p in va + vb)))
    out = {'visible': [], 'mount': 0, 'hidden_same': 0, 'back2back': 0, 'same_diffmat_all': []}
    for (sa, sb, kind, *_nk), g in groups.items():
        if g['area'] < MIN_AREA:
            continue
        rec = {'kind': kind, 'area_cm2': round(g['area'] * 1e4, 2), 'materials': sorted(g['mats']),
               'normal_blender': [round(c, 3) for c in g['n']],
               'shells': [{'object': shells[s]['object'], 'mats': sorted(shells[s]['mats']),
                           'centre': [round((shells[s]['lo'][i] + shells[s]['hi'][i]) / 2, 4) for i in range(3)],
                           'size': [round(shells[s]['hi'][i] - shells[s]['lo'][i], 4) for i in range(3)]} for s in (sa, sb)]}
        if kind == 'back2back':
            if any(m in TRANSPARENT for m in g['mats']):
                g_pt = g['polys'][0]
                rec['at_blender'] = [round(c, 4) for c in _pt(g_pt)]
                out['visible'].append(rec)
            else:
                out['back2back'] += 1
            continue
        n = g['n']
        if len(g['mats']) > 1:
            out['same_diffmat_all'].append({'area_cm2': rec['area_cm2'], 'materials': rec['materials'], 'normal_blender': rec['normal_blender'],
                                            'shell_centres': [x['centre'] for x in rec['shells']]})
        # mount planes: the prop's bottom (floor) and, when wall-backed, its +Y back plane
        pt0 = _pt(g['polys'][0])
        if on_mount(n, pt0, lo, hi, mounts):
            out['mount'] += 1
            continue
        vis = False
        for ar, poly, u, v, nn, dmax in g['polys']:
            c = [sum(p[0] for p in poly) / len(poly), sum(p[1] for p in poly) / len(poly)]
            samples = [c] + [(c[0] + 0.7 * (p[0] - c[0]), c[1] + 0.7 * (p[1] - c[1])) for p in poly[:6]]
            for s2 in samples:
                p3 = u * s2[0] + v * s2[1] + nn * (dmax + 0.0005)
                ins = inside_shells(bvh, tri_shell, shells, p3) - {sa, sb}
                if not ins:
                    vis = True
                    rec['at_blender'] = [round(x, 4) for x in p3]
                    break
            if vis:
                break
        if vis:
            out['visible'].append(rec)
        else:
            out['hidden_same'] += 1
    out['visible'].sort(key=lambda r: -r['area_cm2'])
    return out


def _pt(entry):
    ar, poly, u, v, nn, dmax = entry
    c = [sum(p[0] for p in poly) / len(poly), sum(p[1] for p in poly) / len(poly)]
    return u * c[0] + v * c[1] + nn * dmax


if FBX_MODE:
    here = os.path.dirname(os.path.abspath(__file__))
    root = here
    while root != os.path.dirname(root) and not os.path.isdir(os.path.join(root, 'ProjectSettings')):
        root = os.path.dirname(root)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for f in sorted(glob.glob(os.path.join(root, 'Assets/Game/Art/Models/Environment/Decorations/Astronaut/astro_*.fbx'))):
        tmp = bpy.data.collections.new('import')
        bpy.context.scene.collection.children.link(tmp)
        bpy.context.view_layer.active_layer_collection = bpy.context.view_layer.layer_collection.children[tmp.name]
        bpy.ops.import_scene.fbx(filepath=f)
        tmp.name = 'Coll_AstroDeco_' + sorted(o.name for o in tmp.objects)[0].split('_')[0]   # nodes are <Name>_<Part>
    bpy.context.view_layer.update()

result = {}
for coll in sorted(bpy.data.collections, key=lambda c: c.name):
    if not coll.name.startswith('Coll_AstroDeco_'):
        continue
    prop = coll.name[len('Coll_AstroDeco_'):]
    if ONLY and prop not in ONLY:
        continue
    desc = coll.asset_data.description if coll.asset_data else ''
    poses = ['closed']
    names = [o.name for o in coll.all_objects]
    if any(n.endswith(('_LeafA', '_LeafB', '_BeaconRotor')) or n == 'OuterHatch_Door' for n in names):
        poses.append('open')
    result[prop] = {}
    for pose in poses:
        result[prop][pose] = audit(coll, pose, MOUNTS.get(prop, []))
    print('AUDIT', prop, {p: (len(r['visible']), len(r['same_diffmat_all'])) for p, r in result[prop].items()}, flush=True)
json.dump(result, open(OUT, 'w'), indent=1)
tot = sum(len(r['visible']) for p in result.values() for r in p.values())
tot2 = sum(len(r['same_diffmat_all']) for p in result.values() for r in p.values())
print('TOTAL visible', tot, 'same-facing different-material (incl. hidden/mount)', tot2)
