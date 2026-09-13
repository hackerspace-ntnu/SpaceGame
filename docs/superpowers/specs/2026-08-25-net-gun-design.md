# Net Gun — design

Status: approved 2026-08-25. Implementation plan: [2026-08-25-net-gun.md](../plans/2026-08-25-net-gun.md).

A hotbar artifact that fires a weighted net. Whatever the net lands on is held for up to
30 seconds, fighting the whole time. The net is a simulated lattice that falls, drapes and
drags — not an animation played at a target.

## 1. Why a Verlet lattice

Three candidate solvers, one viable.

**Unity `Cloth` — rejected.** Collides only with sphere/capsule/conical-capsule colliders
that are explicitly registered on the component, and not with terrain at all. It fights
transform teleports, cannot be seeded, and exposes no handle to make it purse closed. It also
has no holes: a net made this way is a sheet with an alpha-cut texture.

**Rigidbody + ConfigurableJoint lattice — rejected.** Physically coupled to the world for
free, and unusable at this size: a 15x15 net is 225 rigidbodies and roughly 800 joints per
net. PhysX relaxes high-valence joint lattices poorly, so it stretches and jitters visibly,
and nothing about it agrees between two machines.

**Verlet particle grid (position-based dynamics) — chosen.** The model `LassoRope` already
uses, generalised from a 1-D chain to a 2-D lattice. Inextensible by construction, stable at
any stiffness, cheap, and deterministic on a fixed substep. Every lesson `LassoRope` paid for
transfers unchanged: alternating Gauss-Seidel passes, velocity living in `prev` rather than
`pos`, Laplacian de-kinking, and the taut-case straightening correction.

### The six choices that make it read as a net

1. **Shear diagonals are maximum-length-only.** Structural four-neighbour constraints alone
   make a trellis: a square grid of hinged corners has zero resistance to shear and collapses
   to a line. Hard diagonals make a rigid sheet. A real net deforms freely until the mesh
   locks, so a diagonal pulls in when it is too long and never pushes apart when it is short.
   One branch, and it is the most load-bearing line in the solver.
2. **Weighted rim.** Cast nets and net-gun rounds carry most of their mass in the hem. That
   mass blooms the net open in flight and, on impact, carries past and under the target so it
   purses closed. Implemented as per-node inverse mass, with the Jakobsen correction split by
   inverse mass rather than 50/50.
3. **Bloom by impulse, not keyframe.** Rim nodes get outward radial velocity at deploy and
   the constraints resolve it. A keyframed opening is identical in a vacuum and wrong the
   moment the net clips a rock.
4. **Almost no bend stiffness.** `LassoRope` needs `bendResistance = 0.3`; the net wants about
   0.02 — enough to kill the one-node zigzag, nowhere near enough to hold a shape.
5. **Face drag.** Drag proportional to (face normal · velocity) gives flutter and decelerates
   the bloom without a tuned curve.
6. **Collision is a capsule per captive plus one sampled ground height.** Pushing nodes out of a
   capsule is what the drape *is*. The ground is a single raycast under the net, re-sampled as it
   drags, not a raycast per node — 225 casts per substep is not a budget that exists, and across
   6 m of this game's terrain one height plus the captive capsules is within a hand's width of the
   truth everywhere the difference would show.

### Budget

15x15 nodes across 6 m is a 0.43 m mesh: about 810 constraints, 8 passes, a fixed 1/90 s
substep. Roughly 10k float operations per rendered frame per net. Concurrent nets are capped.

Rendered as a single procedural mesh of camera-facing ribbon strands rebuilt each frame — one
draw call, and it reads as cord at close range instead of a textured sheet.

## 2. Gameplay

Tap to fire. The net leaves the canister as a tight bundle, blooms over about 0.25 s, and
drapes over whatever it reaches.

**Capture is the server's.** On impact the server sweeps the footprint and binds every valid
body under it. Creatures bind through a `SnareTether`; players bind through a `SnaredBody` that
runs on the captive's *own* machine, because a player body is owner-authoritative and anything
the server writes to it is overwritten within a tick and silently lost. This is exactly the
split `LassoTether` / `LassoedBody` already established.

**Hobbling is a constraint, not an off-switch.** A captive is tethered to the net centre with
a short radius and a speed cap, so it shuffles, thrashes and drags the net with it. The net
deliberately never calls `SuspendSelfDrive()`: `LassoTether` documents the statue-on-a-string
version as the mistake it replaced, and leaving the AI's enabled flags untouched also keeps
the net clear of the save-freeze failure class, where a quit-time autosave captures a
component that a runtime effect had switched off.

**Struggle drains one shared integrity pool.** Every captive's struggle drains the same pool;
when it empties the net tears and everyone gets out. So netting three creatures holds them for
meaningfully less than 30 seconds, and the wide net is not strictly better than a careful
single shot — a real tradeoff rather than a dominant option (GDC-L1-DESIGN-0002). Struggle
strength scales with the captive's estimated mass, reusing `LassoTether`'s bounds-density
estimate for the many creatures that have no Rigidbody.

**Ammo.** Three charges, one recharging every 12 s. This reuses `UsableItem`'s persisted
`uses` counter and needs two small changes to that base class: a `protected RefundUse()`,
because the counter is private and monotonic today, and an `OnMaxUsesReached()` override —
`EquipmentController.ItemDepleted` deletes the item from the inventory, which must never
happen to a gun that recharges.

**Expiry.** At 30 s, or when the pool empties, the net goes slack and dissolves over about
1 s and every captive is released.

### Control loss, deliberately bounded

`GDC-L1-FEEL-0001` (objective, confidence 5) defines game feel as real-time control of a
virtual body; a hard 30 s stun deletes that layer outright, and `GDC-L1-MP-0002` treats
consequence-free ways to ruin another player's time as a design outcome rather than bad luck.
The mitigation is not a shorter timer but giving the captive something to do
(`GDC-L1-DESIGN-0006`): struggle is continuous, drains a visible pool, and a heavy or
determined captive tears out early. Against creatures the full 30 s is the hunting verb
working as intended.

## 3. Multiplayer

The shot rides `UseItem` / `ItemUsed` like every other artifact. The owner writes the muzzle
origin into `NetArg.P`, aim into `R`, and a rolled seed into `B`; every machine then presents
the identical seeded ballistic flight, which is the Dragon Bazooka's closed-form agreement
precedent. That seed doubles as the net's id, because a counter would be advanced in a different
order on a host (which presents before it uses) than on a peer, and would then name a different
net on each machine.

Capture cannot ride the flight, for the reason the `LassoRoped` comment gives: two machines
integrating one arc at different frame rates can pick different creatures out of a crowd. So
the server decides and says so. `NetArg` has no list field, so one message is sent per
captive, grouped by a net id.

    Snared      88  server -> everyone, on the SHOOTER's channel.
                    Target = the captive, A = net id.
    SnareFreed  89  server -> everyone, on the SHOOTER's channel.
                    A = net id; Target = 0 for the whole net.

Both are broadcast to All and both handlers act only when the state differs, so a machine that
missed one is corrected by the next — the same idempotence rule `NetLatch.Apply` documents.

The flying net is presentation, built locally on every machine, so per this project's network
prefab rule it **must not** be registered in the network prefab list.

## 4. Persistence

The gun's charge count and recharge timer persist through `ItemState`, keyed `uses` (already
owned by `UsableItem`) and a new `netgun.recharge`.

**Netted-ness is deliberately not persisted.** It is at most 30 s of state, and the net writes
nothing that any saver captures: no saveable reads `NavMeshAgent.speed`, and the net never
touches an agent's enabled flag. A world loaded mid-capture has free captives, which is
correct rather than merely tolerable. A live net releases every captive in `OnDisable`, so a
chunk unloading under a net cannot leave a creature hobbled forever.

## 5. Model

Built from the supplied concept: a chunky receiver with orange diagonal striping, a tall optic
riser with a round green lens, an angled grip wrapped in cloth and cord, a blue bracket with a
red/blue hose loop, and a small roller under the drum. The large two-tone canister becomes the
net canister: its front bore is framed by four splayed petals with the bundled net visible
inside, so loaded and spent read at a glance with no animation. That two-variant approach
follows the existing `portal_gun` / `portal_gun_spent` precedent.

The concept sheet's "TONY BOY" lettering is the illustrator's signature and is not reproduced.

## 6. Testing

EditMode tests beside `LassoTests.cs`, covering solver invariants that are cheap to state and
expensive to lose: strands stay inextensible under load, a sheared lattice locks rather than
collapsing, the inverse-mass split moves the light node further than the heavy one, diagonals
never push apart, and the capture lifecycle binds, drains and releases exactly once.

Verification beyond tests, per this repo's two non-negotiables: fire on an actual client and
confirm the net draws and holds there, not only on the host; and save, quit, reload while a
creature is netted and confirm it comes back free and able to move.

## 7. Naming

The trap family is `Snare*`, not `Net*`. This codebase already owns the `Net` prefix for netcode —
`NetMsg`, `NetArg`, `NetRelay`, `NetChannel`, `NetAuthority`, `NetLatch`, `NetMessaging`, `NetTo` —
and a restraint component called `NetTether` sitting beside `NetRelay` reads as networking. Only the
artifact itself keeps the `NetGun` name, where it means the object rather than the layer.
