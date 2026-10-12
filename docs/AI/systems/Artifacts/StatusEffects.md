---
system: StatusEffects
status: implemented
layer: items
summary: "Timed flags on a body — burning, frozen, slick, inflated, foamed, swallowed, shocked — owned by the server, presented everywhere"
consumers: [Flamethrower, CryoSprayer, InflatorNozzle, FoamGun, StormFlask, BottledSingularity, Artifacts]
updated: 2026-10-12
---

# Status effects

Read [../Artifacts.md](../Artifacts.md) first.

Seven of the artifacts leave a timed condition on whatever they hit. Without a shared system that is
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
  true for `Frozen`, `Foamed` and `Shocked`, and `StatusReceiver.Suppressed` is the one question everything
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
| `Shocked` | 3 s default (the Shock Harpoon applies 5 s), not extendable, 2 s immunity after | Helpless and on the floor: a player limp through `BodyHold.Take`, a creature through `AgentRagdoll.HoldDown` (the net's claim), both on every machine off the replicated flag, released the frame it clears. No damage of its own. Applied by the Shock Harpoon ([Artifacts.md](../Artifacts.md)). Drawn by `ShockedVisual`: the body flashes to an X-ray (a ghost of each renderer in `SpaceGame/XRayShell`) with a cartoon skeleton inside (`BodySkeleton`: a bone between every pair of the rig's joints, a skull with eye sockets, ribs), arcs jump along and across the bones, and a blue point light wavers over it. Tuned in `Resources/Effects/ShockLook.asset` |
| `Swallowed` | Named by the caller (the singularity's is ~3 s) | A flag and a clock, and nothing else — it does **not** suppress. The body has been moved into an interior by whatever ate it; this is how everything else knows. Never extended by a second source |

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
- **A stun must not be chainable.** `Shocked` refuses a refresh while it runs and refuses a new shock for `immunity` (2 s) after it ends, on the deciding machine (`CanApply`). Without it two shooters taking turns keep a victim on the floor for good (GDC-L1-BAL-0004). A creature's fall goes through `AgentRagdoll.HoldDown`/`ReleaseHold` from `OnApplied`/`OnCleared` — not `NetMsg.Knockdown` — so an early clear stands it up at once rather than when a knock-down timer runs out.
- **The shocked X-ray never touches the body's materials.** `XRayGhost` adds a second renderer bound to the same mesh and bones (marked `BodyAttachment`, so the ragdoll and the skeleton skip it) and hides the original with `Renderer.forceRenderingOff` — a flag nothing else in the game writes (the singularity's `BodyVeil` uses `enabled`, first person's head hide uses `shadowCastingMode`). Write it from another system and a shock that ends will show that body again behind its back. A `LODGroup` body is ghosted by LOD 0 only, with every level hidden; a ghost per level would draw them all at once, additively.
- **`BodyFrame` measures which way the face points while the body still stands.** The rigs here face +Z, -Y or whatever their .blend did, so a head bone's axes say nothing; the root's forward does, until the body is limp. The harpoon adds `ShockedVisual` in `Present`, a flight time before the shock lands, for exactly this. A future shock source that adds it to a body already on the floor gets the skull's eye sockets on whatever side the body faced as it lay.
- **The X-ray strobe is held under three flashes a second** (`ShockLook.xraySeconds` + `normalSeconds`, pinned by `ShockHarpoonTests.TheXRayStrobesUnderThePhotosensitivityLimit`). The game has no reduce-flashing setting, so a full-body flash faster than that would be unsafe as shipped (GDC-L1-FEEL-0006). The light wavers with noise instead of strobing.
- **A condition's default duration is serialized into every body that carries a receiver.** `StatusReceiver` holds each behaviour as a serialized field, so a prefab saved after `Shocked` existed (`Raider_GearThief`) keeps `seconds: 3` even if `ShockedStatus.DefaultDuration` changes. A source that needs a definite length passes it to `Apply` — the Shock Harpoon does (5 s).
- **`ShockLook` numbers live in the asset, not in code.** The field initialisers only seed a NEW asset; once `ShockLook.asset` exists, changing a default in `ShockLook.cs` changes nothing on screen.
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
