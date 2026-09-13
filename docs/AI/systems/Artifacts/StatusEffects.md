---
system: StatusEffects
status: implemented
layer: items
summary: "Timed flags on a body — burning, frozen, slick, inflated, foamed, swallowed — owned by the server, presented everywhere"
consumers: [Flamethrower, CryoSprayer, InflatorNozzle, FoamGun, StormFlask, BottledSingularity]
updated: 2026-09-09
---

# Status effects

Read [../Artifacts.md](../Artifacts.md) first.

Six of the artifacts leave a timed condition on whatever they hit. Without a shared system that is
six copies of the same timer, the same replication, and the same "what does a creature do about it"
question. This is that system, written once.

## Model

- **One receiver per body.** `StatusReceiver` sits on anything that can carry a condition: the
  player character, an `AgentController` (which ensures its own in `Awake`, on its own GameObject),
  a loose Rigidbody prop. It holds a small list of active statuses and nothing else.
- **What counts as a body is one rule, `StatusReceiver.EnsureOnBody`** — anything with a
  `HealthComponent` or a `Rigidbody` in its parents, plus anything somebody authored a receiver
  onto; null for world geometry. Every continuous artifact reaches with a mask of `~0`, so without
  it the first sweep across a dune sets a whole terrain chunk alight as one body. `Ignition` and the
  cryo sprayer both go through it, because a flame and a plume that disagreed about what a body is
  would be two rules to keep in step.
- **A status is a kind plus an expiry.** `StatusKind` is an enum — `Burning`, `Frozen`, `Slick`,
  `Inflated`, `Foamed`. Reapplying a kind refreshes its expiry rather than stacking a second copy;
  a body is burning or it is not. A kind that must not be extended by the source holding it there
  refuses the refresh through `StatusBehaviour.CanApply` — `Burning` does, so a fire is five seconds
  whatever keeps announcing it.
- **The server owns the flag; every machine owns the look.** The server adds, ticks and expires.
  Presentation — flames, frost, a sheen, a wobble — is rebuilt locally from the replicated flag, so
  no visual effect travels on the wire.
- **Behaviour lives with the kind, not with the receiver.** Each kind gets a small class
  (`BurningStatus`, `FrozenStatus`, …) that says what applying, ticking and clearing do. The
  receiver never grows a switch over kinds, which is what would turn it into a god class.
- **Creature reactions hook in once.** A burning or slick creature reacting is an `AgentSystem`
  concern; the receiver raises `StatusChanged` and a single `StatusReactionModule` on the agent
  translates it into a flee, a stagger or nothing. Five artifacts do not each learn about AI.
- **"Cannot act" is the KIND's answer, not a per-creature row.** `StatusBehaviour.Suppresses` is
  true for `Frozen` and `Foamed`, and `StatusReceiver.Suppressed` is the one question everything
  else asks: `AgentController` refuses to run any module at all while it holds (handing the motor
  `MoveIntent.Idle()`), `StatusReactionModule` answers `IsHelpless` from it, and `BodyHold` takes a
  player's body with it. Derived every frame, written nowhere — a suppression stored on the agent is
  a world save capturing a switched-off brain.

## Kinds

| Kind | Duration | Effect |
| --- | --- | --- |
| `Burning` | 5 s, not extendable, 3 s before catching again | Damage over time. Creatures panic and flee. Cleared by rain or water |
| `Frozen` | 10 s | Helpless — no movement, no attacks, pose held. No damage, no kill, no ragdoll |
| `Slick` | 20 s | Grip 0.03 of normal. Ropes, lassos and nets slide off. Laid by the cryo sprayer on the first touch, ten seconds longer than the `Frozen` that follows |
| `Inflated` | While pumped, deflates when not | Scale and mass driven by one signed scalar |
| `Foamed` | 10 s | Held in place (ragdolled, unlike `Frozen`). Broken early by damage |
| `Swallowed` | Named by the caller (the singularity's is ~5 s) | A flag and a clock, and nothing else — it does **not** suppress. The body has been moved into an interior by whatever ate it; this is how everything else knows. Never extended by a second source |

**`Swallowed` is the odd one and worth reading before reusing it.** The other five change what a
body can *do*; this one changes where it *is*, and does so entirely outside the status system — the
mover is `InteriorManager`, and this is only the flag that says so. It is **not**
[Containment](../Containment.md), which serialises a body to a record and despawns it. That is right
for a captive that must survive a save and a rejoin inside a carried item, and wrong for something
seconds long: a despawn-and-rebuild hands the body a fresh identity, and loses it outright if the
session ends mid-hold. A hidden live body has neither problem.

## Multiplayer

- Server-authoritative in every case: these are contested world state, not the holder's own body.
- One message carries add and remove, addressed to the affected body. Nothing per tick — a client
  that knows the kind and the expiry can draw the whole thing without further traffic.
- Damage over time is billed on the server only. Clients draw flames; they never subtract health.
- Late joiners get the active list when the body's state is first sent.

## Persistence

Statuses are seconds long and deliberately not saved. A quicksave taken while burning loads a body
that is not burning, which is the honest outcome — restoring a fire the player was already escaping
would hand back a situation they had left. `Frozen` is the one worth a second look: a creature
frozen at the moment of a save reloads thawed and free, which is a small gift rather than a defect.

## Gotchas

- **Frozen and a save.** A frozen agent has a suppressed motor. If the suppression is written to the
  agent rather than derived from the status every frame, a load restores a creature that never moves
  again. Derive it; do not store it.
- **A hold is not always a ragdoll.** `BodyHold.Take` puts a player down limp (`Foamed`, the net,
  a hogtie); `BodyHold.TakeStanding` takes control and leaves them upright with the collider on and
  the camera in the helmet, which is what `Frozen` uses — a statue that collapses into a heap is not
  a statue. Both take a claim on the same `PlayerRagdoll` claim set, so two captors cannot free each
  other's captive.
- **A condition that must not be lethal must not be a `DamageWatchingStatus`.** `Frozen` used to
  watch damage and kill above a threshold; a victim who can see it coming and can do nothing once it
  lands is exactly the case that must not be an execution (GDC-L1-MP-0002).
- **Two authorities.** `Slick` on a *player* changes how their own movement resolves, and player
  movement is owner-authoritative. The server owns the flag, but the owner's movement code reads the
  flag and applies it — the server never writes the player's velocity. Same split as the rest of the
  codebase.
- **`Slick` reaches a mover only through `GroundGrip`.** The status registers an answer and waits to
  be asked. Every mover that can be slicked has to ask: the player in `Movement`, legged rigs in
  `LeggedLocomotion.ApplyGroundGrip`, NavMesh agents in `NavMeshAgentMotor.ApplyGroundGrip`, the
  dune foil in its own locomotion. One that does not ask is not a bug in the status
  ([SurfaceCoat](SurfaceCoat.md) has the same seam).
