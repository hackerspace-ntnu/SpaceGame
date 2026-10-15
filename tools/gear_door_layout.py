"""Seeded search for a gear door's puzzle (docs/AI/systems/GearDoor.md): the workshop's gate, or its vault.

    python3 tools/gear_door_layout.py Assets/Game/Editor/Workshop/GearDoorLayout.json
    python3 tools/gear_door_layout.py vault Assets/Game/Editor/Workshop/VaultDoorLayout.json

Lays a chain of gears from the drive to the lock on the shut door, adds lure pegs (one exact spacing off a chain gear,
leading nowhere) and filler pegs, and keeps the first layout where EVERY way of carrying the drive to the lock with the
pile's gears uses the whole chain. Some chain gears start on their pegs (`preinstalled`); the rest, and nothing else,
lie in the pile. Deterministic: the same constants write the same file. Rebuild Workshop Prefab afterwards;
GearDoorLayoutTests checks the result again.

Door-plane coordinates: u along the shut door (the player's right facing it), v up from the floor.
"""
import json, math, random, sys

PER_TOOTH = 0.03          # metres of centre distance per tooth: kit module 0.04 at gear scale 1.5, halved
ADDENDUM = 0.06           # outer radius = pitch radius + this
CLEAR = 0.06              # two gears that do not mesh must stand this far apart, tip to tip
TOL = 0.02                # how far off the exact spacing two gears still mesh
FIXED_CLEAR = 0.3         # no peg this close to the drive's or the lock's rim
WANDER = 1.25             # radians the chain may turn off the line to the lock at each gear


def outer(t): return t * PER_TOOTH + ADDENDUM
def mesh_dist(a, b): return (a + b) * PER_TOOTH


class Gate:
    """The workshop's front gate: two 14.2 m leaves, nine chain gears, the pile on the sand in front."""
    KINDS = {'XS': 10, 'S': 14, 'M': 20, 'L': 28, 'XL': 40}
    DRIVER = ('Skeleton', 48, (-7.2, 2.9))
    OUTPUT = ('Flywheel', 56, (6.9, 3.4))
    CHAIN = 9                 # gears between the drive and the lock
    PREINSTALLED = (3, 5, 7)  # chain gears already on their pegs at the start (0 is next to the drive); not in the pile
    LURES = 9
    PEGS = 46
    PEG_SPACING = 0.7
    U_RANGE, V_LO, V_HI = (-12.6, 12.6), 0.25, 6.4     # gear discs stay above the floor and below 6.4 m
    FILLER_U, FILLER_V_LO = 12.0, 1.0
    CENTRE_V_MAX = 5.6        # centres a 3 m player can reach with a 4.5 m placement range
    SEAM_CLEAR = 0.6          # the shut leaves leave a 0.7 m gap at the seam: no peg stands in it
    HINGE_U, LEAF = 14.2, 14.2
    PILE_OUT, PILE_U, PILE_SWEEP_CLEAR, PILE_GAP = (3.0, 15.0), 7.0, 0.6, 0.25

    def fits(self, c, t):
        u, v = c
        return (self.U_RANGE[0] + outer(t) <= u <= self.U_RANGE[1] - outer(t) and v - outer(t) >= self.V_LO
                and v + outer(t) <= self.V_HI and v <= self.CENTRE_V_MAX and abs(u) >= self.SEAM_CLEAR)

    def filler(self, rng):
        p = (rng.uniform(-self.FILLER_U, self.FILLER_U), rng.uniform(self.FILLER_V_LO, self.CENTRE_V_MAX))
        return p if abs(p[0]) >= self.SEAM_CLEAR else None

    def pile(self, sizes):
        """Where the loose gears lie: out of both leaves' sweep, in front of the gate."""
        rng = random.Random(7)
        spots = []
        for name in sizes:
            r = outer(self.KINDS[name])
            for _ in range(5000):
                x, u = rng.uniform(*self.PILE_OUT), rng.uniform(-self.PILE_U, self.PILE_U)
                if min(math.hypot(x, self.HINGE_U - u), math.hypot(x, self.HINGE_U + u)) < self.LEAF + self.PILE_SWEEP_CLEAR + r:
                    continue
                if any(math.hypot(x - sx, u - su) < r + sr + self.PILE_GAP for sx, su, sr, _ in spots):
                    continue
                spots.append((x, u, r, name))
                break
            else:
                raise SystemExit('no room in the pile for ' + name)
        return [{'kind': n, 'out': round(x, 3), 'u': round(u, 3), 'yaw': round(rng.uniform(0, 360), 1)} for x, u, _, n in spots]


class Vault:
    """The Shellbacks' vault: one round door, a small drive and lock, three small cogs between them, one already in
    place, and the two missing ones lying on the bench beside the door: five cogs in all. Every gear stays on the door's clear face
    (vault_build.py keeps a disc of FACE_R round the door's centre free of everything proud of the face)."""
    KINDS = {'XS': 10, 'S': 14, 'M': 20}
    CENTRE = (0.0, 2.3)       # the door's centre on its face, measured off the model
    FACE_R = 1.8              # every gear's rim stays this close to the centre
    DRIVER = ('Fixed_S', 14, (-0.85, 2.3))
    OUTPUT = ('Fixed_M', 20, (0.75, 2.3))
    CHAIN = 3
    PREINSTALLED = (1,)
    LURES = 3
    PEGS = 10
    PEG_SPACING = 0.45
    TRAY_GAP = 0.02           # the loose cogs lie flat in a row along the bench beside the door, this far apart

    def fits(self, c, t):
        return math.dist(c, self.CENTRE) + outer(t) <= self.FACE_R

    def filler(self, rng):
        a, r = rng.uniform(0, 2 * math.pi), self.FACE_R * math.sqrt(rng.uniform(0, 1))
        return (self.CENTRE[0] + r * math.cos(a), self.CENTRE[1] + r * math.sin(a))

    def pile(self, sizes):
        """In a row along the bench, its middle at the bench's centre: `u` along it, `out` unused."""
        widths = [2 * outer(self.KINDS[n]) for n in sizes]
        total = sum(widths) + self.TRAY_GAP * (len(widths) - 1)
        at, spots, rng = -0.5 * total, [], random.Random(7)
        for name, w in zip(sizes, widths):
            spots.append({'kind': name, 'out': 0.0, 'u': round(at + 0.5 * w, 3), 'yaw': round(rng.uniform(0, 360), 1)})
            at += w + self.TRAY_GAP
        return spots


def relation(c1, t1, c2, t2):
    d = math.dist(c1, c2)
    if abs(d - mesh_dist(t1, t2)) <= TOL:
        return 'mesh'
    if d < outer(t1) + outer(t2) + CLEAR:
        return 'hit'
    return 'free'


def near_fixed(door, p):
    return (math.dist(p, door.DRIVER[2]) < outer(door.DRIVER[1]) + FIXED_CLEAR or
            math.dist(p, door.OUTPUT[2]) < outer(door.OUTPUT[1]) + FIXED_CLEAR)


def chain(door, rng, sizes):
    """Centres for a chain drive -> sizes... -> lock, or None. The last gear sits where two circles cross."""
    drive, lock = (door.DRIVER[2], door.DRIVER[1]), (door.OUTPUT[2], door.OUTPUT[1])
    placed, at, prev_t = [], door.DRIVER[2], door.DRIVER[1]
    for i, t in enumerate(sizes):
        last = i == len(sizes) - 1
        r = mesh_dist(prev_t, t)
        if last:
            r2 = mesh_dist(t, door.OUTPUT[1])
            d = math.dist(at, lock[0])
            if d > r + r2 or d < abs(r - r2):
                return None
            a = (r * r - r2 * r2 + d * d) / (2 * d)
            h = math.sqrt(max(0.0, r * r - a * a))
            ex = ((lock[0][0] - at[0]) / d, (lock[0][1] - at[1]) / d)
            mid = (at[0] + a * ex[0], at[1] + a * ex[1])
            cands = [(mid[0] - h * ex[1], mid[1] + h * ex[0]), (mid[0] + h * ex[1], mid[1] - h * ex[0])]
            rng.shuffle(cands)
        else:
            base = math.atan2(lock[0][1] - at[1], lock[0][0] - at[0])
            cands = [(at[0] + r * math.cos(ang), at[1] + r * math.sin(ang))
                     for ang in (base + rng.uniform(-WANDER, WANDER) for _ in range(30))]
        chosen = None
        for c in cands:
            if not door.fits(c, t):
                continue
            # free of everything but the gear it meshes with (and, for the last, the lock)
            others = placed[:-1] + ([drive] if placed else []) + ([] if last else [lock])
            if all(relation(c, t, oc, ot) == 'free' for oc, ot in others):
                chosen = c
                break
        if chosen is None:
            return None
        placed.append((chosen, t))
        at, prev_t = chosen, t
    return placed


def ways_to_the_lock(door, pegs, inventory, limit):
    """The length of every way to carry the drive to the lock with these pegs and this multiset of gears."""
    found = []
    drive, lock = (door.DRIVER[2], door.DRIVER[1]), (door.OUTPUT[2], door.OUTPUT[1])
    counts = dict(inventory)

    def dfs(at, at_t, path, used):
        if len(path) > limit:
            return
        if path and relation(at, at_t, *lock) == 'mesh':
            found.append(len(path))
            return
        for i, p in enumerate(pegs):
            if i in used:
                continue
            for name, n in counts.items():
                t = door.KINDS[name]
                if n == 0 or relation(at, at_t, p, t) != 'mesh' or not door.fits(p, t):
                    continue
                if any(relation(p, t, pegs[j], tj) == 'hit' for j, tj in path[:-1]):
                    continue
                if (path and relation(p, t, *drive) == 'hit') or relation(p, t, *lock) == 'hit':
                    continue
                counts[name] -= 1
                dfs(p, t, path + [(i, t)], used | {i})
                counts[name] += 1

    dfs(drive[0], drive[1], [], frozenset())
    return found


def generate(door, seed):
    rng = random.Random(seed)
    names = list(door.KINDS)
    for attempt in range(4000):
        sizes = [rng.choice(names) for _ in range(door.CHAIN)]
        if len(set(sizes)) < min(len(door.KINDS), door.CHAIN):
            continue
        placed = chain(door, rng, [door.KINDS[s] for s in sizes])
        if placed is None:
            continue
        sol = [(c, s) for (c, _), s in zip(placed, sizes)]
        pegs = [c for c, _ in sol]
        anchors = [(door.DRIVER[2], door.DRIVER[1])] + [(c, door.KINDS[s]) for c, s in sol]
        lures = 0
        for _ in range(600):
            if lures >= door.LURES:
                break
            ac, at = rng.choice(anchors)
            t = door.KINDS[rng.choice(names)]
            ang = rng.uniform(0, 2 * math.pi)
            p = (ac[0] + mesh_dist(at, t) * math.cos(ang), ac[1] + mesh_dist(at, t) * math.sin(ang))
            if not door.fits(p, t) or any(math.dist(p, q) < door.PEG_SPACING for q in pegs) or near_fixed(door, p):
                continue
            pegs.append(p)
            lures += 1
        for _ in range(4000):
            if len(pegs) >= door.PEGS:
                break
            p = door.filler(rng)
            if p is None or any(math.dist(p, q) < door.PEG_SPACING * 1.25 for q in pegs) or near_fixed(door, p):
                continue
            pegs.append(p)
        inventory = {s: sizes.count(s) for s in set(sizes)}
        ways = ways_to_the_lock(door, pegs, inventory, door.CHAIN + 3)
        if ways and all(n == door.CHAIN for n in ways):
            return sizes, sol, pegs, ways, attempt
    return None


if __name__ == '__main__':
    door = Vault() if sys.argv[1] == 'vault' else Gate()
    out = sys.argv[-1]
    for seed in range(1, 400):
        res = generate(door, seed)
        if res is None:
            continue
        sizes, sol, pegs, ways, attempt = res
        order = list(range(len(pegs)))
        random.Random(seed * 31).shuffle(order)          # the solution's pegs are not the first ones
        index = {o: n for n, o in enumerate(order)}
        doc = {
            'seed': seed, 'perTooth': PER_TOOTH, 'addendum': ADDENDUM, 'clearance': CLEAR, 'tolerance': TOL,
            'pegs': [[round(u, 4), round(v, 4)] for u, v in (pegs[i] for i in order)],
            'driver': {'kind': door.DRIVER[0], 'teeth': door.DRIVER[1], 'at': list(door.DRIVER[2])},
            'output': {'kind': door.OUTPUT[0], 'teeth': door.OUTPUT[1], 'at': list(door.OUTPUT[2])},
            'solution': [{'peg': index[i], 'kind': s} for i, (_, s) in enumerate(sol)],
            'preinstalled': [{'peg': index[i], 'kind': sol[i][1]} for i in door.PREINSTALLED],
            'pile': door.pile([s for i, s in enumerate(sizes) if i not in door.PREINSTALLED]),
            'solutions_found': len(ways),
        }
        with open(out, 'w') as fh:
            json.dump(doc, fh, indent=1)
        print('seed', seed, 'attempt', attempt, 'sizes', sizes, 'pegs', len(pegs), 'ways to the lock', len(ways))
        break
    else:
        raise SystemExit('no layout found: loosen the constants')
