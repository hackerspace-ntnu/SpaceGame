---
system: Lasso
layer: items
summary: "Throwable loop: wind up to charge, release to throw a solved arc, catch, fight, tie off to a leash"
paths:
  - Assets/Game/Scripts/Items/Artifacts/Lasso
  - Assets/Game/Scripts/Core/Multiplayer/Messaging/Vocabulary/LassoVerb.cs
  - Assets/Game/Prefabs/Items/Artifacts/Gadgets/Lasso.prefab
  - Assets/Game/Resources/Items/Artifacts/Lasso.asset
  - Assets/Game/Editor/Tests/LassoTests.cs
symptoms:
  - "the thrown loop passes over the animal I aimed at and lands well behind it"
  - "a lasso throw goes where I point only if I aim at the creature's feet"
  - "a fast or lightly-charged throw passes straight through a thin target and reports a miss"
  - "I cannot tell how far a wind-up will reach until after I let go"
  - "the spinning loop clips through the camera or flickers at the top of the screen in first person"
  - "the rope is drawn to a point above a small creature, or around a big one's ankle"
  - "the collar is drawn in one place and the rope goes taut against another"
  - "a roped creature stops struggling after a few seconds and never fights again"
  - "a caught creature can never be let out again once it has been reeled in"
  - "there is nothing to do with a roped animal except drag it around"
  - "the lasso makes a generic hit sound when I press the button and is silent when it catches"
  - "the rope is drawn in a wood texture, smeared once along its whole length"
reads_with: [Artifacts, LeashSystem, Multiplayer, Persistence, AgentSystem]
updated: 2026-09-05
---

# Lasso

A throwable loop with its own Verlet rope. **Hold to twirl, release to throw** — the press starts the loop turning over the player's head and it opens as it winds; letting go throws it as far as it was wound. Catch an animal, fight it, and tie it off.

**Scope:** [`Assets/Game/Scripts/Items/Artifacts/Lasso/`](Assets/Game/Scripts/Items/Artifacts/Lasso) (9 files), [LassoVerb.cs](Assets/Game/Scripts/Core/Multiplayer/Messaging/Vocabulary/LassoVerb.cs), [Lasso.prefab](Assets/Game/Prefabs/Items/Artifacts/Gadgets/Lasso.prefab), [LassoTests.cs](Assets/Game/Editor/Tests/LassoTests.cs).
**Related:** [Artifacts.md](Artifacts.md) · [LeashSystem.md](LeashSystem.md) (where a catch is handed off to) · [Multiplayer.md](Multiplayer.md) · [Persistence.md](Persistence.md) · [AgentSystem.md](AgentSystem.md)

**Not this system:** the **leash** is a rope tied between any two things and is a separate artifact. The **net gun**'s `Snare*` files are a parallel capture system that shares `LassoTether.EstimateMassOf` and nothing else.

## Model

- **The gesture is the item.** `IsContinuous => true`, `WantsHold => false`: the press starts a twirl, the **release** throws. A lasso that fires on the press is a rope gun.
- **Loft is flight time, never extra upward speed.** [`LassoThrow.SolveVelocity`](Assets/Game/Scripts/Items/Artifacts/Lasso/LassoThrow.cs) picks an apex, derives the flight time from it, and solves the launch so the loop passes **through** the aim point on the way down. `throwSpeed` is a speed *cap*, not the pace-setter: when a throw would exceed it the flight is lengthened and the arc **re-solved**.
- **The drawn radius is the catch radius.** [`LassoLoop.Radius`](Assets/Game/Scripts/Items/Artifacts/Lasso/LassoLoop.cs) is what the arc is tested against and what the aim guide draws, so a fully wound loop genuinely has a wider mouth than a flicked one.
- **The catch is swept, not sampled** — a `SphereCast` between the head's last and current position, plus an overlap at the destination for the case of arriving already inside a collider.
- **The thrower sees the arc; everyone else sees the loop.** [`LassoAim`](Assets/Game/Scripts/Items/Artifacts/Lasso/LassoAim.cs) draws an owner-only arc-and-ring guide from the same solver the throw uses. The twirling loop stays overhead for observers.
- **The catch cinches, then the animal fights.** [`LassoTether`](Assets/Game/Scripts/Items/Artifacts/Lasso/LassoTether.cs) takes the creature's legs off its AI with `SuspendSelfDrive()` — **never** `agent.enabled = false` — and drives them itself.
- **The heavier end wins.** `LassoArtifact.PlayerPullShare(targetMass, playerMass = 80)` is `public static` and a pure function of two masses, because the two ends of the rope run on two different machines and must agree without a message.
- **The rope is a contest with two opposing loops** ([`LassoTension`](Assets/Game/Scripts/Items/Artifacts/Lasso/LassoTension.cs)). Straining pays line out and tires the animal; slack winds line back within reach and lets the animal recover. Held under strain long enough the rope **wears through and parts**.
- **A catch ends somewhere.** Pressing Use while roped and aiming at a hitchable surface within `hitchRange` builds a real [`Leash`](Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs) between the creature and that anchor and drops the lasso. Aiming at nothing still means "let go" — the same shape `LeashArtifact` gives the gesture.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `LassoArtifact` | [LassoArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Lasso/LassoArtifact.cs) | `ToolItem`, **Owner** authority, `IsContinuous`, `IItemDeferredRestore`, `[DefaultExecutionOrder(200)]`. Gestures, the arc, the wire, save/restore, dallying |
| `LassoThrow` | [LassoThrow.cs](Assets/Game/Scripts/Items/Artifacts/Lasso/LassoThrow.cs) | Pure static ballistics. `ApexFor`, `SolveVelocity`, `PointAt` |
| `LassoRope` | [LassoRope.cs](Assets/Game/Scripts/Items/Artifacts/Lasso/LassoRope.cs) | Verlet cable. `Straighten` (closed form when taut), `Unkink` (Laplacian bend resistance), `Snap` (the tension crack) |
| `LassoLoop` | [LassoLoop.cs](Assets/Game/Scripts/Items/Artifacts/Lasso/LassoLoop.cs) | The honda, four states: coil → twirl → fly → cinch/collar |
| `LassoAim` | [LassoAim.cs](Assets/Game/Scripts/Items/Artifacts/Lasso/LassoAim.cs) | Owner-only arc + landing-ring guide. Its own runtime `GameObject`; never networked |
| `LassoTension` | [LassoTension.cs](Assets/Game/Scripts/Items/Artifacts/Lasso/LassoTension.cs) | Serialized tuning + pure static `Strain01` / `Wear` |
| `LassoStruggle` | [LassoStruggle.cs](Assets/Game/Scripts/Items/Artifacts/Lasso/LassoStruggle.cs) | Serialized tuning for the caught creature, handed to the tether on the catch |
| `LassoTether` | [LassoTether.cs](Assets/Game/Scripts/Items/Artifacts/Lasso/LassoTether.cs) | The caught **creature**, on the machine that owns it. Also `AttachHeightFor` and `EstimateMassOf`, both static and shared |
| `LassoedBody` | [LassoedBody.cs](Assets/Game/Scripts/Items/Artifacts/Lasso/LassoedBody.cs) | The caught **player**, on their own machine |
| `LassoHitch` | [LassoHitch.cs](Assets/Game/Scripts/Items/Artifacts/Lasso/LassoHitch.cs) | Hands the catch to a `Leash`. `TieOff`, `IsHitchable`, `EncodeKnot` |
| `LassoVerb` | [LassoVerb.cs](Assets/Game/Scripts/Core/Multiplayer/Messaging/Vocabulary/LassoVerb.cs) | `Caught` `ReelOn` `ReelOff` `StrainOn` `StrainOff` `Snapped` `Hitched`. **Append only** |

## Flows

**Wind up and throw**
1. Press → `OnRequestUse` sets `B = ThrowVerb` → `Present` on every machine → `BeginTwirl` (loop shown, `RopeTwirl` played).
2. Every frame, on every machine: `TickTwirl` accumulates `_twirlCharge` locally — every machine saw the press, and what the charge decides is already baked into the aimed point. The owner additionally draws `LassoAim`.
3. Release → owner's `OnRequestHold(active: false)` writes the aim point in `P` and the direction in `R` (`B` is unavailable — `EquipmentController` owns it on hold ticks as the active flag).
4. `PresentHold` on every machine → `ThrowRoutine` integrates the solved arc and pays the rope out behind it.
5. Only `OwnerIsLocal()` decides what was caught → `Attach` → `SendRope(Caught)`.

**The catch**
1. `LassoVerb.Caught` → server → `NetTo.All` → every machine runs `ApplyRope` → `Attach`, idempotent.
2. `Attach` resolves the knot height from the creature's own size, binds `LassoTether` (owner of the creature) or `LassoedBody` (the roped player's own machine), cinches the loop, cracks the rope and plays `RopeCatch`.

**The contest**
1. The **authority** measures the overshoot each `FixedUpdate`, wears the rope, and publishes `StrainOn`/`StrainOff` as edges.
2. Every machine integrates the rope length from those edges at the authored rate — reeling shortens, straining pays out — so all of them agree without a per-tick message.
3. Past the wear budget the authority sends `Snapped`; every machine plays `RopeSnap` and releases.

**Tie off**
1. Press while roped → `TryAimAtHitch` (owner) → `B = HitchVerb`, anchor in `Target`, knot in `P`, `BareAnchorFlag` when the anchor has no networked identity.
2. `Present` on every machine → `LassoHitch.TieOff` builds a `Leash` → `Release`. The leash is then saved and constrained by [LeashSystem](LeashSystem.md).

## Multiplayer

- **`UseAuthority.Owner`**, and `Use()` is deliberately **empty**. Everything is built by `Present` on every machine; the constraint decides for itself which machine may run it.
- **Messages ride the THROWER's channel, never this item's.** The prefab carries a `NetworkObject` (it must — dropping routes through `World.Spawn`) but that object is never spawned while the item is in a hand, so a send from here resolves to a dormant relay and runs locally only. `NetMessaging.NetSendTo(owner, …)`.
- **`IsAuthority` (`!Network.IsNetworked || Network.Server`), not `Network.Simulates(this)`.** An equipped artifact is instantiated into a hand and never spawned, so `Simulates` answers "yes" on every machine at once.
- **`OwnsTarget()` asks ownership of the TARGET, not of the item.** A loose creature is owned by the server, a ridden mount by its **rider**.
- **Late joiners** get `Caught` re-announced from `OnPeerJoined` — it is absolute state, not an edge, so it costs a joiner one `Attach` and everyone else one no-op.
- **Nothing spawned here is a network prefab.** The rope, the loop, the aim guide and the hitched leash are all local `Instantiate`/`new GameObject` on every machine.

## Persistence

- `IItemDeferredRestore`. `CaptureItemState` writes the target `SaveRef`, the attach offset and the rope length; an unreferenceable target writes **nothing** rather than a rope attached to nowhere.
- `TryCompleteRestore` is idempotent and stays pending until the creature's chunk streams in.
- Restore order is load-bearing: `Attach` first (it writes its own length from the current gap), then the saved offset and length, then `SendRope` — announcing earlier publishes the guess instead of the saved value.
- **A hitched creature needs nothing from this system.** Once tied off it is a `Leash`, and `LeashSaveable` captures it off `Leash.All`.

## Gotchas

- **Loft added to a solved arc is a miss, not a lob.** `throwArcHeight` used to be added straight onto the vertical component of a correct ballistic solution. That is a throw at a *different* target: at the prefab's shipped numbers the loop passed **1.6 m over** a creature aimed at from 12 m and **4.0 m over** one at 30 m, and crossed the target's own altitude a constant **13 m behind it** (`2·arc·speed/g`, which is why the error did not vary with range) — against a catch radius of 0.22–0.8 m. Nothing failed; the arc was real ballistics and the rope drew beautifully, and the item simply did not go where the crosshair was. `TheThrowLandsOnThePointItWasAimedAt` pins it, and `TheThrowArrivesOnTheWayDownAndActuallyArcs` pins the half that a flat rifle shot would otherwise satisfy.
- **A point-sampled catch tunnels.** A single `OverlapSphere` at the head's position samples a thing moving up to 22 m/s against a 0.22 m mouth. The sweep and the destination overlap are both needed: a `SphereCast` reports nothing for a collider it *started* inside, which is exactly the case of a loop arriving on top of an animal.
- **The first-person eye is at root + 1.45 m, and the twirl loop is at root + `twirlHeight`.** At the authored 2.1 m that put the loop 0.65 m *directly above the camera* — out of frame for the only person who needed to read the charge off it, with its lower arc skimming the near plane. `twirlForward` carries the sweep ahead of the body; the thrower reads the charge off `LassoAim` instead. **Do not "fix" a loop the thrower cannot see by lowering `twirlHeight`** — that puts it back on their ear, which is the defect the 2.1 m was chosen to fix.
- **The knot is one number, asked for in one place.** It used to be two: `LassoArtifact.npcAttachHeightOffset` drew the rope and `LassoStruggle.attachHeight` constrained it, both 1.2 m on the same prefab with nothing keeping them equal. It is now a **fraction of the creature's own height** (`LassoTether.AttachHeightFor`) — a flat metre value hangs the collar in mid-air above anything small and around the shin of a habitat-sized walker.
- **Judge strain on one machine and publish the edge.** Every machine measuring its own overshoot against its own interpolated copy of two moving ends gives every one of them a different rope length within seconds — permanently, because the length is what the constraint and the break verdict are both measured against.
- **`Sfx` is played at the moment, not at the button.** `UsableItem.PlayUse` plays `useSound` inside `Present`, which for this item is the **press** — so the item's one sound fired at the start of the wind-up and again on the press that dropped the rope, while the throw, the catch, the crack and the coil-back were silent. `useSound` on the prefab is now empty and the six `SfxId.Rope*` entries are played where they happen.
- **A LineRenderer's texture is Stretched by default.** The rope and loop were drawn in `Custom_Wood` — a surface material off a prop — fitted once across up to 26 m of cable. Both now take `Rope_Leash.mat` (the braid the leash already uses) with `LineTextureMode.Tile`, and neither casts shadows: a view-aligned ribbon casts the shadow of a flat strip that changes shape as the player turns their head.
- **`Show(start, start)` stacks every node on one point** with zero-length segments the solver cannot give a direction to. Seed along the aim.
- **Slack, not span, is the shape of a rope.** `FlightSlack` must stay well outside `Straighten`'s 0.9–1.0 band or the cable is snapped onto the chord every substep and the throw draws as a straight line. `ThrownRopeTrailsInACurve` and `RopeStaysSmoothWhileBeingThrown` pin both halves; either is trivial to satisfy alone by breaking the other.
- **A release with no orientation is a cancel, not a throw.** `EndHold(send: false)` delivers a `default` NetArg on unequip, disable and death; `arg.HasOrientation` is the test.
- **Never `agent.enabled = false`.** `SuspendSelfDrive()`/`ResumeSelfDrive()` *record* whether the agent was enabled, which matters because `Awake` parks an agent that wakes before a NavMesh exists under it.
- **`AgentController.Motor` is null outside play mode** (resolved in `Awake`, which `AddComponent` does not raise in edit mode). `LassoTether.Bind` falls back to `GetComponentInParent<ISelfDrivingMotor>()` or every EditMode test silently fails to take hold.
- **Drive the animator from `LateUpdate`.** `AgentController.Update` feeds `AgentAnimatorDriver` `Motor.Velocity`, which with the agent suspended is zero — so a struggling creature slides with an idle animation.
- **Right mouse is shared.** `UI/RightClick` drives the reel and stays enabled during play alongside interact — see [InteractionSystem.md](InteractionSystem.md).
- Prefab field names that must survive any rewrite or `Lasso.prefab` silently nulls them: `lineRenderer`, `loopRenderer`, `muzzle`, `lassoModel`, `reelInAction`.

## Extending

- **A new rope state** is a `LassoVerb` (append only — retired ids still travel between builds) plus a case in `ApplyRope`. Make it absolute state rather than an edge if a late joiner has to learn it.
- **A new thing to do with a catch** should follow `LassoHitch`: decide owner-side in `OnRequestUse`, encode into `B` above `BareAnchorFlag`, act in `Present` on every machine, and end in `Release()` unconditionally so a machine that could not act still drops the rope.
- **Tuning** lives on the prefab in `LassoRope` / `LassoLoop` / `LassoStruggle` / `LassoTension` / `LassoAim` foldouts. Adding a knob means adding it to the serializable class, not to `LassoArtifact`.
- **Anything worth a test** goes in `Assets/Game/Editor/Tests/LassoTests.cs` (Editor/, not the asmdef'd EditMode folder — these touch `Assembly-CSharp`). Prefer the pure statics: `LassoThrow` and `LassoTension.Wear` are testable without a scene precisely so the two things that were silently wrong can be pinned.
