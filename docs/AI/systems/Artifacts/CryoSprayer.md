---
artifact: CryoSprayer
status: implemented
authority: Server
continuous: true
uses: [StatusEffects, SupplyCharge]
symptoms:
  - "the cryo sprayer leaves no frost or ice on the ground any more"
  - "vapour washes over a creature at the edge of the plume and freezes it as fast as one dead ahead"
updated: 2026-09-13
---

# Cryo sprayer

Read [../Artifacts.md](../Artifacts.md) first.

Freezes bodies, and nothing else. Anything the plume washes over wears a film of frost from the
first touch and holds the pose it was caught in for ten seconds. **The ground takes nothing** — no
frost discs, no ice.

## What it does

Hold Use and a plume of vapour comes out, eighteen metres of it, opening to a 22° cone.

- **A creature, a player or a loose prop standing anywhere in that cone** — not only on the
  crosshair — takes the `Slick` status on the very first touch: no grip, and ropes, lassos and nets
  slide straight off it. Three quarters of a second of continuous spray later it freezes: `Frozen`,
  no movement, no attacks, the pose held exactly as it was. **Where in the plume it stands buys it
  nothing** — the rim freezes as fast as the centre line. The freeze thaws after 10 s and the film
  after 20, so a thaw hands the victim their body back still sliding. The freeze **deals no damage,
  cannot kill and never ragdolls the body**: ten seconds of not playing is the whole price, and the
  victim can see it coming (GDC-L1-MP-0002).
- **Every surface takes nothing at all** and says so: the plume ends in a puff of blow-off, on sand,
  on water and on a wall alike. The landing burst bites only where the centre line ends on a BODY.

## Numbers

| Knob | Value | Where |
| --- | --- | --- |
| Range | 18 m — **serialized on the prefab**, which is what ships | `CryoSprayerArtifact.range` |
| Visible plume reach | speed × lifetime ≈ 16.5–20.4 m | `CryoPlumeBuilder.CoreSpeedMin/CoreLifeMin` |
| Plume cone, drawn | 22° open, 3° shut | `CryoSprayerNozzle.openConeDegrees` |
| Plume cone, freezing | 22° half-angle — must match the drawn one | `CryoSprayerArtifact.coneHalfAngle` |
| Freeze time | 0.75 s of continuous spray on one target, anywhere in the cone | `CryoSprayerArtifact.freezeSeconds` |
| Thaw of the build-up | 0.5 fractions/s once nothing is spraying | `FrostLook.thawPerSecond` |
| Frozen duration | 10 s, never extended by being sprayed harder | `FrozenStatus` |
| Sweeps | 15 /s | `CryoSprayerArtifact.sweepsPerSecond` |
| Frost film on a body | 20 s, 0.03 grip | `SlickStatus` |
| Tank drain / refill | 0.15 /s held, 0.06 /s idle | `SupplyReservoir` on the prefab |

## How it is built

- `UseAuthority.Server`, `IsContinuous => true`, `WantsHold => false`. The aim RAY travels — origin
  in `NetArg.P`, rotation in `R` — on the press and on every hold tick, and nothing else.
- **Bodies come from the cone.** Every body inside the drawn 22° cone chills —
  [`ConeSweep.Bodies`](Assets/Game/Scripts/Items/Artifacts/ConeSweep.cs), shared with the
  [flamethrower](Flamethrower.md), one entry per body however many colliders it puts in the cone,
  line of sight tested so nothing freezes through a rock.
- **The centre-line trace decides only where the landing burst is drawn**: frost biting into a body,
  or vapour blowing off anything else.
- **The cold does NOT fall off across the cone.** It used to — full on the crosshair, 0.35 at the
  rim — and vapour a player could see covering a creature took three times as long to do anything to
  it, which reads as a broken gun (GDC-L1-FEEL-0003). The cost is known and accepted: a held plume
  swept across a group is a crowd freeze at a range the flamethrower cannot reach
  (GDC-L1-BAL-0004). What keeps that affordable is that the freeze deals no damage and cannot kill.
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
- **The film lands before the freeze, deliberately.** A grazed body is visibly slithering, which is
  the warning it has three quarters of a second to act on before the pose locks (GDC-L1-MP-0002).
- **This gun no longer coats the ground.** It used to lay `Slick` frost on anything level enough to
  stand on and `Ice` over liquid — a standable bridge. Both went: the film is symmetric, so the
  discs took the holder's own footing away as readily as the target's, and the ground effect was a
  second weapon nobody reached for the gun to get (GDC-L1-BAL-0004). Both coat kinds were deleted
  with it — see [SurfaceCoat](SurfaceCoat.md), which is now the Storm Flask's rain alone.
- The tank is [SupplyCharge](../SupplyCharge.md).

## Multiplayer

Server-authoritative: only the deciding machine applies `Slick` and `Frozen`. Everything else — the
rime creeping over the victim, the plume, the landing burst, the hiss — is derived on each machine
from the hold stream, so nothing of the sprayer's own goes on the wire.

## Persistence

Neither `Frozen` nor `Slick` is saved: a frozen creature reloads thawed and a slicked one reloads
with grip, which is the right trade against a creature that reloads permanently stuck. The gun
leaves nothing in the world behind it. The tank rides the hotbar slot's charge byte through
`SupplyCharge`.

## Art

Model: `cryo_sprayer.fbx`, about 0.5 m, one-handed, with rime frosting the last few centimetres of
the barrel — that rime is the same `_Freeze` property as the ice on the target, driven by
`CryoSprayerNozzle` and never fully cleared (`restRime`, so the gun reads as cold at rest).

The plume is authored by **`Tools/SpaceGame/Items/Build Cryo Plume`**
(CryoPlumeBuilder) — three particle systems on the
prefab (`Jet` with `Shards` and `Mist` under it, plus `Bite` and `Blowoff`) and three materials off
one shader, [`SpaceGame/Effects/CryoVapour`](Assets/Game/Art/Shaders/Effects/CryoVapour.shader).
That shader is `FlameBillboard`'s opposite number: same quantized bands and world-space noise, but
the field scrolls *up* so the plume sags, the blend is ordinary transparency rather than additive
because frost is matter, and the noise is folded into hard facet steps with a sparse sparkle layer —
which is what makes it read as ice rather than as blue smoke.

## Gotchas

- **The cone that freezes and the cone that is drawn are two numbers.** `CryoSprayerArtifact.coneHalfAngle`
  is what freezes and `CryoSprayerNozzle.openConeDegrees` is what the player sees; vapour visibly
  washing over a creature and doing nothing to it is those two having drifted apart. `OnValidate`
  warns and `CryoFreezeTests.TheConeThatFreezesIsTheConeThatIsDrawn` fails when they do — and both
  are `[SerializeField]`s **on the prefab**, so editing a class default alone changes nothing.
- **Widening the cone is a balance change, not a feel tweak.** Everything inside it now freezes at
  one rate, so the angle IS the gun's accuracy: at 18 m, 22° is already 14 m across at the far end.
- **`FrozenBody.Chill` takes the sweep's SPAN.** `chilledUntil` is what tells the body it is still
  standing in vapour; a window shorter than the sweep interval lets the thaw run between sweeps on
  the very body being sprayed.
- **Tuning the plume by hand does not survive.** `CryoPlumeBuilder` replaces `Jet`, `Bite` and
  `Blowoff` whole on every run. Numbers belong in its constants.
- **The reach is two numbers that must agree, and neither of them is the one you edited.**
  `CryoSprayerArtifact.range` is what freezes, `CoreSpeed × CoreLife` in the builder is what the
  player sees — and `range`, `coneHalfAngle` and `freezeSeconds` are `[SerializeField]`s **stored on
  the prefab**, so editing the class default alone changes nothing in the game. The sprayer shipped
  freezing at 5 m while its own source said 9 for exactly that reason. `CryoPlumeBuilder.CheckReach`
  warns when the prefab's range falls outside the built plume's reach, and `CryoFreezeTests` fails
  when the prefab's values drift from the class defaults.
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
