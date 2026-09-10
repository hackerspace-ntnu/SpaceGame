---
artifact: CryoSprayer
status: implemented
authority: Server
continuous: true
uses: [StatusEffects, SurfaceCoat, SupplyCharge]
updated: 2026-09-09
---

# Cryo sprayer

Read [../Artifacts.md](../Artifacts.md) first.

Freezes what it hits, and everything it touches loses its grip. Bodies wear a film of frost from
the first contact and hold the pose they were caught in for ten seconds; liquid and wet ground
becomes a sheet of ice you can stand on; every other surface level enough to stand on takes the
film.

## What it does

Hold Use and a plume of vapour comes out, eighteen metres of it. What it lands on decides what
happens:

- **A creature, a player or a loose prop standing anywhere in the plume** — the whole 15° cone, not
  only the crosshair — takes the `Slick` status on the very first touch — no
  grip, and ropes, lassos and nets slide straight off it — then ices over for three quarters of a
  second of continuous spray and freezes: `Frozen`, no movement, no attacks, the pose held exactly
  as it was. Off the centre line it takes longer: the freezing rate falls from full on the
  crosshair to 0.35 of it at the rim, so a body at the edge of the plume freezes in about two
  seconds rather than in three quarters of one. The freeze thaws after 10 s and the film after 20, so a thaw hands the victim their
  body back still sliding. The freeze **deals no damage, cannot kill and never ragdolls the body**:
  ten seconds of not playing is the whole price, and the victim can see it coming (GDC-L1-MP-0002).
- **A liquid or wet surface** becomes `Ice`: a standable patch. That is a way across water and a way
  to make a slope no one can climb.
- **Any other ground level enough to stand on** becomes `Slick`: a film of frost, 20 s, no
  collider. Nothing that walks onto it can stop and a slope becomes impossible.
- **Walls take nothing, and say so** — the plume ends in a puff of blow-off rather than in frost.
  Not a rule about the material: a coat is a horizontal disc, so what the gate really asks is
  whether anything stands here at all.

## Numbers

| Knob | Value | Where |
| --- | --- | --- |
| Range | 18 m — **serialized on the prefab**, which is what ships | `CryoSprayerArtifact.range` |
| Visible plume reach | speed × lifetime ≈ 16.5–20.4 m | `CryoPlumeBuilder.CoreSpeedMin/CoreLifeMin` |
| Plume cone, drawn | 15° open, 3° shut | `CryoSprayerNozzle.openConeDegrees` |
| Plume cone, freezing | 15° half-angle — must match the drawn one | `CryoSprayerArtifact.coneHalfAngle` |
| Freezing rate at the rim of the cone | 0.35 of the rate on the crosshair | `CryoSprayerArtifact.edgeChill` |
| Freeze time | 0.75 s of continuous spray on one target — also serialized on the prefab | `CryoSprayerArtifact.freezeSeconds` |
| Thaw of the build-up | 0.5 fractions/s once nothing is spraying | `FrostLook.thawPerSecond` |
| Frozen duration | 10 s, never extended by being sprayed harder | `FrozenStatus` |
| Sweeps | 15 /s | `CryoSprayerArtifact.sweepsPerSecond` |
| Max ground angle for a coat | 50°, ice and frost alike | `CryoSprayerArtifact.maxGroundAngle` |
| Frost film duration | 20 s, ground and body alike | `SlickCoat`, `SlickStatus` |
| Grip on frost, and on ice | 0.03 of normal | `SlickCoat`, `IceCoat`, `SlickStatus` |
| Tank drain / refill | 0.15 /s held, 0.06 /s idle | `SupplyReservoir` on the prefab |

## How it is built

- `UseAuthority.Server`, `IsContinuous => true`, `WantsHold => false`. The aim RAY travels — origin
  in `NetArg.P`, rotation in `R` — on the press and on every hold tick, and nothing else.
- **Bodies come from the cone, the coat from the centre line.** Every body inside the drawn 15° cone
  chills — [`ConeSweep.Bodies`](Assets/Game/Scripts/Items/Artifacts/ConeSweep.cs), shared with the
  [flamethrower](Flamethrower.md), one entry per body however many colliders it puts in the cone,
  line of sight tested so nothing freezes through a rock. The COAT still comes from the single
  centre-line trace, because a coat is a disc laid at one point and a cone has no one point; a body
  in the way still shields the ground behind it, and takes no coat itself.
- **The cold falls off across the cone**, from full on the crosshair to `edgeChill` at the rim
  ([`ConeSweep.Falloff`](Assets/Game/Scripts/Items/Artifacts/ConeSweep.cs)). Without it an
  eighteen-metre cone — nine metres across at its far end — makes the sprayer a longer-ranged
  flamethrower that freezes a crowd as fast as one aimed body (GDC-L1-BAL-0004). The `Slick` film is
  **not** scaled with it: a body the vapour touched at all is slithering immediately, which is the
  graze warning.
- **What counts as a body is [`StatusReceiver.EnsureOnBody`](Assets/Game/Scripts/Gameplay/Status/StatusReceiver.cs)**
  — anything with a `HealthComponent` or a `Rigidbody` in its parents, plus anything somebody
  authored a receiver onto. It is the same rule [`Ignition`](Flamethrower.md) uses for fire, and it
  is called on **every** machine: a receiver the server invented alone is a body that freezes for the
  server and nobody else.
- **The build-up is derived, not replicated.** Every machine traces the same ray and reaches the same
  fraction within a frame. [`FrozenBody`](Assets/Game/Scripts/Items/Artifacts/CryoSprayer/FrozenBody.cs)
  holds the fraction, draws it and removes itself once the rime is gone.
- **A statue is not a prefab swap.** `FrostShell` builds a coincident copy of the body's meshes
  sharing its bones, shaded with `Mat_FrozenStatue` whose `_Freeze` runs 0→1, and `FrozenBody`
  freezes the Animator's speed so the pose holds. Nothing is despawned, so a rider strapped into a
  seat is not a special case.
- **Helplessness is derived from the condition, never written to the body.** `FrozenStatus.Suppresses`
  is true; `AgentController` reads `StatusReceiver.Suppressed` every frame and refuses to run any
  module at all — movement, side-effect, presentation — handing the motor `MoveIntent.Idle()`
  instead. A player is held by `BodyHold.TakeStanding` → `PlayerRagdoll.HoldStanding`, which takes
  input, look and movement but leaves the body upright, its collider on and its camera in the helmet.
- **`CoatFor` picks the kind, and ice wins where it can.** The ITEM answers "is this level enough
  to stand a horizontal disc on", which is true of every kind; the KIND answers "is this liquid or
  wet ground", which only `Ice` cares about. Ice is geometry as well as grip, so frost laid over a
  pool would be a film on water nobody can stand on.
- **The film lands before the freeze, deliberately.** A grazed body is visibly slithering, which is
  the warning it has three quarters of a second to act on before the pose locks (GDC-L1-MP-0002).
- **This gun absorbed the slick can.** That item sprayed the same film over four metres and did
  nothing else — a strictly narrower version of a job this plume already covered at eighteen, which
  is the case GDC-L1-SYS-0005 says to merge rather than differentiate. The merge is not a straight
  buff: the frost is symmetric, so ground denied to somebody else is ground the holder cannot brake
  on either, and the film that slides a lasso off a creature slides it off one they were trying to
  catch (GDC-L1-BAL-0004).
- Coats are [SurfaceCoat](SurfaceCoat.md); the tank is [SupplyCharge](../SupplyCharge.md).

## Multiplayer

Server-authoritative: only the deciding machine applies `Slick` and `Frozen`, and only it lays a
coat. Everything
else — the rime creeping over the victim, the plume, the landing burst, the hiss — is derived on each
machine from the hold stream, so nothing of the sprayer's own goes on the wire.

## Persistence

Neither `Frozen` nor `Slick` is saved: a frozen creature reloads thawed and a slicked one reloads
with grip, which is the right trade against a creature that reloads permanently stuck. Frost patches
are not saved either — `SlickCoat.Saved` is false, and twenty seconds is not a change to the world. The tank rides the hotbar slot's charge byte through `SupplyCharge`.
`Ice` patches are saved by [SurfaceCoat](SurfaceCoat.md), not by this item.

## Art

Model: `cryo_sprayer.fbx`, about 0.5 m, one-handed, with rime frosting the last few centimetres of
the barrel — that rime is the same `_Freeze` property as the ice on the target, driven by
`CryoSprayerNozzle` and never fully cleared (`restRime`, so the gun reads as cold at rest).

The plume is authored by **`Tools/SpaceGame/Items/Build Cryo Plume`**
([CryoPlumeBuilder](Assets/Game/Editor/Items/CryoPlumeBuilder.cs)) — three particle systems on the
prefab (`Jet` with `Shards` and `Mist` under it, plus `Bite` and `Blowoff`) and three materials off
one shader, [`SpaceGame/Effects/CryoVapour`](Assets/Game/Art/Shaders/Effects/CryoVapour.shader).
That shader is `FlameBillboard`'s opposite number: same quantized bands and world-space noise, but
the field scrolls *up* so the plume sags, the blend is ordinary transparency rather than additive
because frost is matter, and the noise is folded into hard facet steps with a sparse sparkle layer —
which is what makes it read as ice rather than as blue smoke.

## Gotchas

- **`FrozenBody.Chill` takes the sweep's SPAN and the rate as two arguments, and only the rate is
  scaled.** `chilledUntil` is what tells the body it is still standing in vapour; folding the
  falloff into the span instead shortens that window below the sweep interval, and the thaw then
  runs between sweeps on the very body being sprayed.
- **The cone that freezes and the cone that is drawn are two numbers.** `CryoSprayerArtifact.coneHalfAngle`
  is what freezes and `CryoSprayerNozzle.openConeDegrees` is what the player sees; vapour visibly
  washing over a creature and doing nothing to it is those two having drifted apart. `OnValidate`
  warns and `CryoFreezeTests.TheConeThatFreezesIsTheConeThatIsDrawn` fails when they do — and both
  are `[SerializeField]`s **on the prefab**, so editing a class default alone changes nothing.
- **A frost patch that never appears on dry ground is the coat gate, not the plume.** `CoatFor`
  returns `Slick` for everything level enough to stand on and `Ice` only where the ground can
  freeze; a surface steeper than `maxGroundAngle` takes nothing at all and shows blow-off instead.
  A wall reading as "the gun is broken" is that gate doing its job.
- **`Slick` on an NPC does nothing unless its motor asks.** `NavMeshAgentMotor` reads `GroundGrip`
  and scales its `acceleration`; a mover that never asks walks over frost as if it were sand. That
  is why the film is a grip SOURCE and never a push — see [SurfaceCoat](SurfaceCoat.md).
- **Tuning the plume by hand does not survive.** `CryoPlumeBuilder` replaces `Jet`, `Bite` and
  `Blowoff` whole on every run. Numbers belong in its constants.
- **The reach is two numbers that must agree, and neither of them is the one you edited.**
  `CryoSprayerArtifact.range` is what freezes, `CoreSpeed × CoreLife` in the builder is what the
  player sees — and `range` and `freezeSeconds` are `[SerializeField]`s **stored on the prefab**, so
  editing the class default alone changes nothing in the game. The sprayer shipped freezing at 5 m
  while its own source said 9 for exactly that reason. `CryoPlumeBuilder.CheckReach` warns when the
  prefab's range falls outside the built plume's reach, and `CryoFreezeTests` fails when the
  prefab's `range`/`freezeSeconds` drift from the class defaults.
- **A material freezes the shader defaults it was born with.** Retuning a default in
  `CryoVapour.shader` changes nothing on `CryoVapourSpray.mat`; the builder writes every property
  explicitly for that reason.
- **`Bite` and `Blowoff` must simulate in world space.** Both are moved to the hit point fifteen
  times a second; in local space the particles already in the air are dragged with the system and
  the frost smears across the ground.
- **A freeze that only worked on some creatures was a receiver problem, not a freeze problem.** The
  sprayer used to require an authored `StatusReceiver`, which in practice meant "prefabs with a
  `StatusReactionModule` on them". Agents now ensure their own receiver in `AgentController.Awake`,
  on their own GameObject — not through `StatusReceiver.Ensure`, which resolves to the networked
  root and would hand a mounted rider its mount's conditions.
- **`Frozen` no longer watches damage.** It used to shatter and kill above a damage threshold; that
  is gone, and `FrozenStatus` is a plain `StatusBehaviour` again. `FoamedStatus` is still a
  `DamageWatchingStatus` — a hit breaks foam early, which is its counterplay.
