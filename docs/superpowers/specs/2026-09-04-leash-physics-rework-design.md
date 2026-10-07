# The leash as a force contest — design

2026-09-04. Supersedes the constraint half of
[2026-08-23-leash-rework-design.md](2026-08-23-leash-rework-design.md); the per-end ownership model,
the drawing and the saver from that spec all stand. Reference:
[docs/AI/systems/LeashSystem.md](../../AI/systems/LeashSystem.md).

## The problem

The leash is a distance limit that may only ever *remove* velocity. `LeashEnd.Restrain` caps an end's
post-pull speed at the speed it already had, so nothing the rope is tied to can ever be made to move
by it. Three consequences, all of them things a player will try in the first minute:

- **It cannot tow.** A standing player roped to a fleeing animal contributes no separating velocity of
  their own, so the arrest term does nothing and `Restrain` clamps the position correction back to
  their prior speed — which is zero. The rope goes taut and the player does not move.
- **It is inert against most large things.** `LeashedBody.FixedUpdate` returns on `body.isKinematic`,
  and a mounted rider's body is kinematic, so roping a mounted player achieves nothing — while roping
  a mounted *NPC* yanks them out of the saddle via `NpcPassenger.UnseatRider`. Two answers to one
  gesture. The dune foil has no `Rigidbody` on its root at all and the desert crawler is
  `IExternallyPosed`, so both classify as `Static` and become fence posts.
- **The thing you most want to drag is the thing that disconnects.** Towing anything heavy holds a
  standing overstretch by design, and `breakStretch` / `breakTime` snap the rope when overstretch
  persists. Hauling therefore ends in a broken rope.

## What it becomes

A **two-way force contest**. Each end publishes a pull; the larger pull wins; mass decides how slowly
the loser is moved. Multiple ropes on one body sum, so several players out-pull one ship. Nothing
snaps under load — the *only* way out is a deliberate resist. Terrain is no longer an anchor: aiming
at it drops the rope.

The never-accelerate rule is **deliberately retired**. It existed so the leash could not become a
second grappling hook, and that risk is now covered structurally instead: there is still no winch, so
a player has no way to pull *themselves* along a rope. Being dragged requires something else to do
the dragging.

## Decisions

| Decision | Why | Rejected |
| --- | --- | --- |
| `Pull = Mass × TopSpeed`, **derived** | Nothing in the game publishes a force — every mover is authored in velocity, and `IMovementMotor` exposes only `Velocity`. Deriving means every creature, vehicle and player participates the day it lands, with no value to forget. Both factors come off the prefab, so every machine computes the same number and nothing goes on the wire. | An authored `TowStrength` per entity (full control, but anything unset silently cannot pull). Mass alone (already shipped, but strength and weight collapse into one thing, so a small strong animal could never drag a heavier player). |
| **Delete `LeashEnd.Restrain`** | This one deletion is the whole feature. It is the clamp that makes every pull incapable of adding speed, and with it gone the position correction already does the towing. | Adding a separate "tow" term beside the restraint — two systems fighting over the same velocity, with the clamp still winning. |
| **Keep `ArrestSpeed`** | It removes only *separating* velocity, which is exactly what makes heavy cargo hold you back: walk at 6 m/s dragging a crate that can only manage 1.2 and your own speed is arrested down to the crate's. Deleting it alongside `Restrain` would let you tow a mountain at a sprint. | Removing both, and re-implementing drag as an explicit speed penalty. |
| **Keep `CorrectionDistance`** | It converges, it is pure, and `LeashConstraintTests` proves it without a physics scene. Repaying a position error as velocity does not converge — that was the "rope hums and the ends collide" bug. Not reopening it. | Re-deriving the correction as an impulse. |
| **`ShareOf` is left alone** — still inverse mass | Pull and mass answer two different questions, and conflating them breaks the anchor. Mass is *resistance to being moved*: a wall is infinitely heavy, so the player does all the work, which is what already ships and is already tested. Pull is *ability to tow*, and a wall has none — sharing by pull would give a player roped to a wall a share of zero and stop the rope restraining anything. | Splitting by inverse pull (drafted first, and wrong for exactly this reason). |
| A per-end **tow cap** of `netPull / mass` replaces `Restrain` at the same call site | The only place pull is consulted. "Largest force wins" and "heavy stuff is moved slowly" both fall out of it, and it is the same shape of clamp the code already had, so the call site and its test move across rather than being rewritten. | An acceleration-integrating model — physically truer, but it fights every velocity-assigning mover in the game and would have to agree across machines. |
| **Sum every rope on a body, resolve once** | Required for "multiple things can drag one thing". It also fixes a latent bug: today two ropes on one body each cancel the *full* relative speed independently, removing it twice. The gather uses `LeashAttachable.Leashes`, a public API that currently has no consumers. | Leaving ropes independent and letting the corrections stack by luck. |
| **Delete `breakStretch` / `breakTime` outright** | Explicitly asked for: dragging something must never end in the rope letting go. With permanent overstretch now the normal state of a tow, a stretch threshold is not tunable — any value that survives hauling a crate is a value that never fires. | Raising the threshold; exempting "towing" from the break test, which needs a definition of towing that the constraint does not have. |
| **Resist is movement input away from the knot** | Costs no binding, so the leash keeps its one-button rule and the `dropAction` trap stays shut. Creatures get it for free — `FleeModule` and `AgentGoal` already produce an "away" vector, so a strong animal tears loose from a weak captor with no AI work at all. | Repeated gauntlet presses (collides with attach/drop on the same button). A dedicated key — `Sprint`, `Previous` and `Next` are genuinely unsubscribed, but it is one more thing to teach. |
| Terrain is `hit.collider is TerrainCollider` | Exact, and needs no layer or tag setup — chunk ground really is Unity `Terrain`. A layer mask would have to be kept in step with the streaming config, which already has a documented casing drift defect. | A `leashableLayers` exclusion; a `NotLeashable` marker component on terrain. |
| Rocks, walls and structures **still anchor** | Only terrain was asked to drop the rope. `PinTo` and the `LeashAnchor` stand-in survive for everything else, so tying a creature to a building still works and the world needs no new content to make the leash usable. | Rigidbodies-only (would have left nothing in the world to tether to). |
| Break authority moves to the **resisting end's owner** | Strain accumulates from movement input, which only that machine has. Sending the input to let the server decide would put a round trip inside a struggle. | Server-decided breaking, as the stretch test used. |
| **No tow speed cap, full fall damage** | Tying yourself to a moving vehicle is meant to be dangerous, not a way to travel. It is also the least code: no special case anywhere. | Capping a player's tow speed at a multiple of sprint; suppressing fall damage above own top speed (which is how the grapple's tether behaves, and re-introduces a mobility exploit through the back door). |
| Inert bodies have **pull 0 and resist by mass** | A crate cannot win a contest but its mass still divides every acceleration, and `ArrestSpeed` charges the hauler for it. Two players roping one crate in opposite directions each receive a correction that cancels at the crate, so it sits still and both are held — a tug-of-war with no "which side is it on" rule to write. | Assigning inert bodies to a side, which needs a rule for a crate roped by three people. |

## The contest, as it resolves

Per body, per physics step, once the gap exceeds `Length`:

```
pull    = mass * topSpeed                  // 0 for inert bodies AND for static anchors
share   = ShareOf(selfMass, otherMass)     // UNCHANGED: inverse mass, i.e. resistance
netPull = pullOther - pullSelf             // > 0 means this end is losing the contest
towCap  = netPull > 0 ? netPull / mass : infinity

arrest  = max(0, separationRate) * share   // unchanged: only ever removes opening speed
step    = stretch * share * correction     // unchanged: error repaid as a position
step    = min(step, towCap * dt)           // replaces Restrain at the same call site
```

`towCap` returns infinity — no clamp — whenever this end is *not* being out-pulled, which keeps two
passive bodies (two crates roped together, both pull 0) closing normally instead of freezing. Still
clamped by `maxCorrectionSpeed` (25 m/s) and `maxCorrectionStep` (0.5 m), which remain the guard
against a streamed-in or teleported endpoint slingshotting its partner.

Worked through, on the cases that motivated the change:

| Case | Result |
| --- | --- |
| Player (80 kg, pull 480) hauls a crate (400 kg, pull 0) | Player `share` 0.83 — the rope holds the *hauler* back, so heavy cargo slows you. Crate `share` 0.17 and no cap, so it follows slowly. Both halves of "heavy stuff is moved slowly". |
| Player (480) roped to a fleeing ostrich (120 kg, pull 1080) | Player `share` 0.6, `netPull` +600, `towCap` 7.5 m/s. The player is dragged — which `Restrain` used to forbid outright. |
| Player (480) roped to a fleeing rat (30 kg, pull 240) | Rat `share` 0.73, `netPull` +240, `towCap` 8 m/s. The player out-pulls it and the rat comes along. |
| Player roped to a wall | `share` 1.0 as it is today, `netPull` negative so no cap. Fully restrained, unchanged. |
| Two players rope one crate in opposite directions | Each rope hands the crate a correction; they cancel and it sits still, while `ArrestSpeed` holds both players at the rope. A third player joining one side breaks the deadlock. No "which side is it on" rule needed. |

**Resist**, on the end being held:

```
strain += dot(moveInput, awayFromKnot) * dt / resistTime(pullOther)
strain -= strainDecay * dt                     // when not pulling away
snap when strain >= 1
```

`resistTime` scales with the captor's pull, so tearing free of a ship takes visibly longer than
tearing free of a player. Strain is owner-local and never sent; only the resulting snap is.

## Shape

**`Leash`**
- `ShareOf` unchanged, and its tests with it.
- `PullOf(mass, topSpeed)` and `TowCap(netPull, mass)` — new, pure, static, tested without a scene
  alongside the existing three. `PullOf` returns 0 for an infinite mass, so a static anchor tows
  nothing (and `Mathf.Infinity * 0f` is never evaluated into a `NaN` that would poison the clamp).
- `HasBroken` / `breakStretch` / `breakTime` deleted. `Snap` and `NetMsg.LeashSnap` (85) stay; they
  are now raised by resist rather than by stretch.
- Resolution moves from per-rope to **per-body**: gather this body's ropes, sum the pull vectors,
  apply once.

**`LeashEnd`**
- `Pull { get; }` — `Mass * TopSpeed`. Zero for inert bodies and for static anchors.
- `TopSpeed` resolution, in order: `LeggedLocomotion.MaxSpeed` (already public) → `NavMeshAgent`
  motor's `defaultSpeed` → the rigidbody motors' `maxSpeed` → `PlayerMovement` sprint speed → 0.
- `Restrain` deleted. Mass estimation for bodiless ends reuses the lasso's existing estimator rather
  than adding a second one.

**`LeashedBody`**
- Drops the `body.isKinematic` early-out in favour of routing a kinematic end through its vehicle,
  so a mounted player is towed by the machine under them instead of silently doing nothing.
- Accumulates and decays resist strain; raises the snap at 1.0.

**`LeashArtifact`**
- `TerrainCollider` test in `OnRequestUse` → the existing `Miss` verb. No new verb code, no wire
  change.
- `breakStretch` / `breakTime` removed from the tuning block and from `RopeSettings`; `resistSeconds`
  (how long a struggle against an equal pull takes) and `strainDecay` added to both, so resist is
  tunable in the Inspector and reaches save- and snapshot-rebuilt ropes through
  `TryResolveSettings` with nothing to wire.

**Motors** — `TopSpeed` promoted to a public read on `NavMeshAgentMotor`, `RigidbodyMotor`,
`HoverRigidbodyMotor` and `FlyingRigidbodyMotor`. `LeggedLocomotion` already exposes it.

**`ITowable`** — the leash gains the branch the grappling hook already has, so the ornithopter is
towable by rope and the interface stops having exactly one consumer.

## Multiplayer

Unchanged in shape, and no new message. `Pull` is `mass × topSpeed` with both factors off the prefab,
so it agrees on every machine for the same reason mass already does — the current design's whole
trick. Rope sets are replicated, so the per-body sums agree too.

The one change: breaking is announced by the **resisting end's owner** rather than by the server,
because strain is accumulated from local movement input. `NetMsg.LeashSnap` is already addressed by
world point rather than by object id, so it carries this without modification.

## Persistence

No format change. `LeashSaveable` stores endpoints and `maxLength`, none of which move. Resist strain
is deliberately transient — a rope you were halfway through fighting out of is whole again after a
reload, which is the forgiving direction.

## Known gaps

- **Aiming while mounted is broken, and it blocks half of this.** Recorded in
  [DEFECTS.md](../../AI/DEFECTS.md): items hook straight down instead of where they are pointed while
  riding. "A ship tows something" is unreachable from a cockpit until that is fixed. It is a separate
  defect in a shared system, not leash work.
- **`LeashConstraintTests` changes meaning.** It currently asserts that a `PlayerMovement.SetTethered`
  call is *absent from the source*, as the guard against the leash becoming a grappling hook. That
  assertion should stay — the new design still never claims the body — but the tests pinning
  `Restrain`'s never-gain-speed rule are deleted along with it, and want replacing with contest tests:
  strongest wins, heavy moves slowly, ropes sum, nothing breaks under load.
- **Being towed at vehicle speed will kill people**, by explicit decision. If playtests hate it, the
  cheapest lever is a tow cap on players only; the design deliberately does not ship one.
- **No creature is told it is leashed.** Unchanged from today, and out of scope here: resist gives
  creatures a way *out* of a rope, not an opinion about being on one.
- **`TopSpeed` on a legged rig is the locomotion's, not the driver's.** A crawler being ridden may
  report a different figure than one walking itself. Worth measuring before tuning.
