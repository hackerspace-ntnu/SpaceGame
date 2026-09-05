---
system: LeashSystem
layer: items
summary: "A rope tied between any two things; every machine draws its own copy and resolves only the ends it owns"
paths:
  - Assets/Game/Scripts/Items/Artifacts/Leash
  - Assets/Game/Scripts/Core/Persistence/Adapters/LeashSaveable.cs
  - Assets/Game/Editor/Tests/LeashConstraintTests.cs
  - Assets/Game/Prefabs/Items/Artifacts/Gadgets/Leash.prefab
symptoms:
  - "the rope holds a host-ridden animal but is inert against a client-ridden one"
  - "a leashed creature teleports or jitters instead of straining at the rope"
  - "the rope tows the player like a grappling hook and launches them"
  - "the leash applies no force to the player at all"
  - "the rope hums, or the two ends accelerate together and collide"
  - "the rope sinks into a hillside, or clicking it to untie never registers"
  - "ropes from the previous world are still hanging in the newly loaded one"
  - "a rope tied to a dropped item slides it along without ever turning it, or moves it only for the host"
  - "a rope tied to a vehicle or a mounted player does nothing at all"
  - "the rope snaps every time I try to drag something heavy"
  - "two ropes on one object hold it far more firmly than one does"
  - "a rope pulls straight through a wall, a pillar or a hillside"
  - "a rope is a different shape on the host and on the client"
  - "a rope fills up with bends and its measured length collapses"
  - "a rope goes onto a slot and can never come off it"
  - "the rope snaps the instant I try to haul a crate or a dropped item anywhere"
  - "a rope tied to a walking animal moves it not at all, as if it were bolted down"
reads_with: [Artifacts, Lasso, Multiplayer, Persistence, PlayerCharacter, Locomotion]
updated: 2026-09-05
---

# Leash System

A rope tied between any two things in the world — creature to post, player to crate, anything to a moving vehicle.
**Scope:** [`Assets/Game/Scripts/Items/Artifacts/Leash/`](Assets/Game/Scripts/Items/Artifacts/Leash) (7 files), [LeashSaveable.cs](Assets/Game/Scripts/Core/Persistence/Adapters/LeashSaveable.cs), [LeashConstraintTests.cs](Assets/Game/Editor/Tests/LeashConstraintTests.cs).
**Related:** [Artifacts.md](Artifacts.md) · [Multiplayer.md](Multiplayer.md) · [Persistence.md](Persistence.md) · [PlayerCharacter.md](PlayerCharacter.md) · [Portals.md](Portals.md) (same "not a NetworkObject, every machine builds its own" pattern)

## Model

- **A dropped item is a legitimate far end.** Its `LeashEnd.Kind` is `Object` with a live, dynamic body, so it takes the `AddForceAtPosition` branch and a crate roped by one corner turns to face the pull. That only became true on 2026-09-03: item prefabs used to freeze themselves kinematic on landing, which put them on the `MovePosition` branch — dragged flat, with no tumble and no resistance — and they carried no `NetworkTransform`, so the pull moved them on the simulating machine and nowhere else. See [Inventory.md](Inventory.md).
- **Hook, then hook.** One button (Use). Empty hands + a thing → rope runs from it to your hand. Holding a rope + anything solid → tied, and the rope is now a world object. Click nothing → let go. Empty hands + **a rope** → untie. One held rope at a time; unequip drops it, tied ropes stay.
- **No second key.** `dropAction` was deleted: an `InputActionReference` read in `Update` runs on *every* copy of the artifact on this machine (including remote players' hands) and bypasses `Use`/`Present`.
- A [`Leash`](Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs) is a bare runtime `GameObject` — no prefab, no `NetworkObject`, `DontDestroyOnLoad` (it outlives the chunk either end streamed in from). Every machine builds and draws its own copy because `Present` runs everywhere.
- **Each machine resolves the ends it owns** — `LeashEnd.ResolvedHere` is `Network.Owns(Body ?? Anchor)`, nothing else. Rope length is fixed and both endpoints replicate, so both machines compute the same overshoot and apply only their own share.
- **A rope is a polyline, not a chord** ([LeashPath.cs](Assets/Game/Scripts/Items/Artifacts/Leash/LeashPath.cs)). It bends on static geometry, the constraint measures the whole path, and each end pulls toward its own nearest bend. **Rope spent going round a corner is rope the far end no longer has** — which is why walking away from a corner draws a load in, with no winch anywhere in this system and without a player being able to shorten their *own* free segment by moving.
- **A `LeashRail` is a slot the rope rides and slides along.** Its bend is the point on the segment making the total rope shortest — closed form, so two machines agree with nothing on the wire. Rails are *preferred wrap points*, not a parallel system: everything downstream treats one exactly as a bend on a rock.
- The constraint is a **distance limit, not a spring**. Below `Length` it does nothing.
- **It is a two-way force contest.** Each end has a `PullStrength` of `mass × topSpeed` (`Leash.PullOf`), both figures off the prefab so every machine derives the same number with nothing on the wire. **Rigidbody mass is therefore a gameplay figure here, not just a physics one** — it decides who wins a tug of war and how much of the correction each end absorbs, and it is read straight off the prefab even when the body is kinematic. Until 2026-09-05 the player and every creature were all `1 kg`, which made every contest a coin toss and every rope a bungee against anything heavy. The scale now: player 80, DuneRat 40, Ostrich 60, Nomad/BountyHunter 80, PatrolRobots · DeathmatchBot · CrabWalker6 · DesertCrawler 100, HumanoidRobot · Vrescal 120, DuneOrnithopter 150, **Golem 200 — which out-pulls a sprinting player and tows them instead**. `BlastPush` reads the same masses to scale knockback by `1/mass`, so a heavier creature is also shoved less. The end that is out-pulled is **towed**, at `netPull / mass` m/s (`Leash.TowCap`) — heavy things move slowly. A static anchor scores **zero** pull: it resists everything and tows nothing.
- **Resistance and strength are different questions.** `ShareOf` still splits the correction by *inverse mass* — a wall has no pull, so sharing by pull would give a player roped to one a share of zero and the rope would stop restraining them.
- **Ropes sum per body.** Every other rope on the same body is projected onto this one's direction (`Leash.CombinedPull`), so three crews hauling a hull add and two hauling it apart cancel.
- **Nothing breaks on stretch.** A tow holds a permanent overstretch by design. The only exit is `ResistStrain`, built from movement input pointing away from the knot.
- Rope length is **fixed**. A tie across a wider gap pays out **once**, to `min(distance + payOutMargin, maxPaidOutLength)`, and the paid-out value travels on the wire.

## Key types

| Type | File | Role |
|---|---|---|
| `Leash` | [Leash.cs](Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs) | One rope. `A`/`B`, `Settings`, static `All`/`Create`/`Aimed`/`Nearest`, the constraint (`ShareOf`, `ArrestSpeed`, `CorrectionDistance`, `PullOf`, `TowCap`, `CombinedPull`), resist (`ResistSeconds`, `ResistStrain`, `StrainOn`), `Tension01`/`IsTaut`, `Snap`/`Dispose` |
| `LeashEnd` | [LeashEnd.cs](Assets/Game/Scripts/Items/Artifacts/Leash/LeashEnd.cs) | One knot: `Kind`, `Anchor`, `LocalOffset`, `Mass`, `TopSpeed`, `PullStrength`, `Towable`, `ResolvedHere`, `Pull` |
| `LeashArtifact` | [LeashArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs) | The equipped `ToolItem`. `UseAuthority.Owner`. Aims, encodes verb+length, owns the tuning |
| `LeashedBody` | [LeashedBody.cs](Assets/Game/Scripts/Items/Artifacts/Leash/LeashedBody.cs) | `[DefaultExecutionOrder(200)]`, added on demand. The player half of the constraint |
| `LeashRope` | [LeashRope.cs](Assets/Game/Scripts/Items/Artifacts/Leash/LeashRope.cs) | `[Serializable]` drawing + tuning, folded into the artifact Inspector. Also `Aimed`/`RayToSegment` |
| `LeashPath` | [LeashPath.cs](Assets/Game/Scripts/Items/Artifacts/Leash/LeashPath.cs) | The rope's shape. `PolylineLength`, `TryMake`, `PointsBetween`, `TotalLength`, `DirectionFrom`, `Step` (drop-dead → unwrap → wrap → slide rails) |
| `LeashWrap` | [LeashWrap.cs](Assets/Game/Scripts/Items/Artifacts/Leash/LeashWrap.cs) | One bend: `Position`, `Normal`, `Surface`, and an optional `Rail` |
| `LeashWorldCast` | [LeashPath.cs](Assets/Game/Scripts/Items/Artifacts/Leash/LeashPath.cs) | The only Physics here. Owns its buffer; skips both anchors; grows rather than trusting a full `NonAlloc` result |
| `LeashRail` | [LeashRail.cs](Assets/Game/Scripts/Items/Artifacts/Leash/LeashRail.cs) | An authored slot. `ClosestBend` (closed form), `BendFor`, `AtEnd`, `HandOverAt`, static `Capturing` registry |
| `LeashAttachable` | [LeashAttachable.cs](Assets/Game/Scripts/Items/Artifacts/Leash/LeashAttachable.cs) | Marker added on demand; answers "what is tied to me" |
| `LeashSaveable` | [LeashSaveable.cs](Assets/Game/Scripts/Core/Persistence/Adapters/LeashSaveable.cs) | Global saver on [persistentScene.unity](Assets/Game/Scenes/world/persistentScene.unity). Key `leashes` |

Assets: [Leash.prefab](Assets/Game/Prefabs/Items/Artifacts/Gadgets/Leash.prefab) · [Leash.asset](Assets/Game/Resources/Items/Artifacts/Leash.asset) · [Rope_Leash.mat](Assets/Game/Art/Materials/Items/Rope_Leash.mat) · [rope_braid_albedo.png](Assets/Game/Art/Textures/Items/rope_braid_albedo.png) + `_normal`, generated by [rope_braid.py](Assets/Game/Art/Textures/Items/_Source~/rope_braid.py) (256×64, needs numpy + Pillow; run from that dir).

## Flows

1. **Aim** (`OnRequestUse`, owner only — the one machine with the camera). Raycast `maxRange`/`leashableLayers`, reject anything under `owner`. `arg.Target` = the root's network id, `arg.P` = the knot **in the target's local space**, `arg.B` = `verb | (paidOutCm << 8)`.
2. **Verbs**: `Miss` (0) drop held · `Hit` (1) tie · `Untie` (2) · `HitLocal` (3) tie to something with no network id — presented **only on the clicking machine**.
3. **Present** (every machine) builds/ties/unties. `Use()` is empty — nothing is server-only here.
4. **Untie** carries the clicked point relative to the **nearer anchor**, so it rides a moving target across the relay; each machine runs `Leash.Nearest(anchor, point, untieTolerance)` on its own copy.
5. **`FixedUpdate`** (`Leash`): `RefreshChannel` → `LeashPath.Step` → `MeasureStretch` (along the path) → `UpdateTension` → `ResolveEnd` for each **non-player** end.
6. **Player ends** are skipped there and driven by `LeashedBody.FixedUpdate` instead, which calls the same `Leash.ResolveEnd` and then `Struggle` to accumulate resist strain.
7. **Draw** (`LateUpdate`, every machine): `TiedBetween(A.Anchor, B.Anchor)` restated each frame (an end can be *replaced*), then `Draw(a, b, Length, Tension01)`.

### The constraint, as `ResolveEnd` applies it

```
pull    = mass * topSpeed                             // static anchor ⇒ 0, not infinity
share   = (1/selfMass) / (1/selfMass + 1/otherMass)   // immovable ⇒ infinite mass ⇒ 0
netPull = otherPull - selfPull + Σ(other ropes on this body, projected onto `toward`)
towCap  = netPull > 0 ? netPull / selfMass : ∞        // only the LOSING end is capped
```
`toward` is the direction to that end's **nearest bend**, and `separationRate` is each end's own
contribution — `dot(selfV, -toward) + dot(otherV, -otherToward)` — because with a bend in it the ends
no longer share one axis. With no bend it reduces exactly to the relative velocity it replaces, which
`SeparationRate_WithNoWraps_MatchesRelativeVelocity` pins.

`arrest = min(max(0, separationRate) * share, maxCorrectionSpeed)` takes velocity off; `step = min(stretch * share * correction, maxCorrectionStep, towCap * fixedDeltaTime)` repays the position error as a **position**. Repaying it as velocity does not converge — the added velocity survives into the next step, so the ends accelerate together and collide. Both terms carry `share`, or two ends each cancelling the *full* relative speed remove it twice and the rope hums. `towCap` is `∞` for an end that is winning or evenly matched, so two passive crates (both scoring zero pull) still close their rope rather than freezing at a cap of nothing.

| End | How `LeashEnd.Pull` applies it |
|---|---|
| `ITowable` | `RequestTow(corrected position)`. Asked, not pushed — a seated rider's body is kinematic and parented, so writing velocity to it does nothing. Returning false ends the tow. Taken **first**, before the kinematic branch. Implemented by `OrnithopterFlightMotor` and by `LeggedDriver`, which hands the pull to `LeggedLocomotion.Drag` capped at the machine's own `TopSpeed` |
| dynamic Rigidbody | `AddForceAtPosition` **at the knot** (torque is what turns a corner-roped crate to face the pull) + direct `position +=` |
| player | `linearVelocity += toward * arrest` + `position +=`. **No torque** — spinning an upright capsule tips the camera. No clamp on the result: a rope may now tow a standing player, bounded by `towCap` |
| kinematic + `NavMeshAgent` | `Agent.Move(step)`. **Never `Warp`** — it re-projects and resets navigation; that was the leashed-creature teleport jitter |
| plain kinematic | `MovePosition` |
| static (`Body == null`) | nothing; it anchors |

### Resist — the only way out

`LeashedBody.Struggle` runs on the struggling player's own machine, because the movement input it reads is local. Strain builds while the rope is **taut**, decays at `strainDecay` when it is not, and is clamped to `[0, 1]`; at 1 the rope snaps.

**Towing and struggling are the same input, and only the result tells them apart.** Both are a player holding a movement key that points away from a taut rope, so the input alone cannot distinguish "haul this crate" from "tear this rope off me". Strain is therefore charged for `wishAway × Leash.HeldBackFraction(wishAway, actualAway, topSpeed)` — the part of the movement the rope actually **cancelled**. A load that comes along with you is not restraining you and earns no strain however hard you pull; a post that will not move restrains you completely. Before this, hauling anything tore the rope off in 0.2 s (see Gotchas). `Leash.ResistSeconds(theirPull, myPull, baseSeconds)` scales the base figure by the captor's pull as a **ratio**, so tearing free of the lander is proportionally harder than tearing free of another player and tearing free of a crate is quick without being instant. Leaning sideways earns nothing — it is the component along the rope that counts.

## Multiplayer

- **Aim** is owner-authoritative; **simulation** is per end by `Network.Owns`.
- **Not "the server runs it".** A player's `NetworkTransform` is owner-authoritative, so server writes are silently overwritten within a tick. `NetMsg.RopeTug` (62) is retired — its receiver was never installed, so no rope ever pulled anyone.
- **Ownership, not `Network.Server`.** A ridden mount belongs to its **rider** (`MountNetworkSync` transfers ownership), so a `Network.Server` test made the rope hold a host-ridden animal and be inert against a client-ridden one.
- **Breaking is the resisting end's *owner's* verdict**, not the server's. It used to be the server's because the verdict was stretch and every machine could measure it; resist is accumulated from movement input, which only the struggling player's machine has. `Snap` sends when `!Network.IsNetworked || Network.Owns(listening) || Network.Server` (`Network.Owns` takes a `Component`, and `listening` is a `Transform`, so it is passed directly). The snap is broadcast as [`NetMsg.LeashSnap`](Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs) (85) on the channel of whichever **anchor** has a network id, addressed by point exactly as an untie is. Peers reach `Snap` *from* the announcement and must not re-broadcast — the `disposed` flag set before the send is what guarantees that.
- **Late joiners** get live ropes through the join snapshot — [SnapshotCapture.cs](Assets/Game/Scripts/Core/Multiplayer/Joining/SnapshotCapture.cs) / [SnapshotRestore.cs](Assets/Game/Scripts/Core/Multiplayer/Joining/SnapshotRestore.cs), which rebuild via `LeashArtifact.TryResolveSettings`, not via the saver.
- **Rope shape is derived, never sent.** Every machine rebuilds the path from the two replicated
  endpoints. That only holds because `wrapLayers` is **static geometry only** — two machines agree
  about where a wall is and do not agree about where a rolling barrel was 40 ms ago. A dynamic
  collider in that mask is the one change that puts the rope in a different place on every screen.
- A rope to an unnetworked dynamic prop stays **local** (`HitLocal`) — its physics already differs per machine.

## Persistence

**Wrap points are not saved.** The path rebuilds from the two endpoints on load, so a rope wound the
long way round a pillar can come back wound the short way. Saving the list would key it to collider
instance ids that do not survive a reload, and a path that disagrees with the world it loaded into
is worse than one that is merely re-derived.

**Resist strain is deliberately transient** — it is not saved, not sent, and resets to zero on load. A struggle is a thing you are doing right now, not a property of the rope; banking it across a quit would let a player tear free of a fresh rope instantly.

`LeashSaveable`, **global** (a rope belongs to neither end: filing it under one loses it when that one unloads, under both restores two). Format `{ a: {anchor: SaveRef, offset, point, held}, b: {…}, maxLength }`. Its own 60 s retry loop, because global savers get no `IDeferredSaveable` pass and both endpoints may still be streaming. `RestoreState` disposes every live rope first — leashes are `DontDestroyOnLoad`, so a previous world's ropes hang in the next one. An endpoint with no `SaveRef` degrades to its world point.

## Gotchas

- **`[DefaultExecutionOrder(200)]` on `LeashedBody` is load-bearing.** `PlayerMovement.FixedUpdate` assigns `rb.linearVelocity` outright (`Lerp(current, desired, 1)` while grounded), so a pull applied before it is *deleted*, not reduced.
- **A rope may now tow, but never launch.** `Restrain` is gone, so the constraint can add speed to the end losing the pull contest — that is the feature. What keeps it from being a second grappling hook is structural rather than a clamp: there is no winch anywhere in this system, so a player cannot pull *themselves* along a rope. Something else has to do the dragging. `LeashConstraintTests` still pins the absence of a `SetTethered` call at the source.
- **Nothing breaks under load.** A tow holds a permanent overstretch by design, so a stretch threshold that survives hauling a crate is one that never fires. The only exit is `ResistStrain`, accumulated from movement input pointing away from the knot — which is why the break verdict now belongs to the resisting end's **owner** rather than to the server.
- **Strain must be charged for movement the rope STOPPED, not movement the player asked for.** Reading the raw input made every tow an escape attempt, and the numbers made it fatal: an item has no `IMovementMotor`, so `TopSpeed` is 0, so `PullOf` is 0, so **every** dropped item hit the ratio floor in `ResistSeconds` at exactly `0.2 s` — measured identical across all 35 item prefabs. Hauling anything tore the rope off in a fifth of a second, before the load had moved, which read in play as "you cannot tow items and the ropes snap constantly". `HeldBackFraction` is the gate; delete it and towing dies again.
- **`ResistSeconds` is keyed on the captor's *pull*, and `PullOf` is 0 for anything without an engine** — a wall, a rock, the lander, a crate. So the floor still applies to a **static anchor**: roped to terrain and walking away, you are genuinely held back and the rope parts in 0.2 s. The doc's own example ("tearing free of the lander is proportionally harder") is inverted by that, and it is **not fixed** — see [DEFECTS.md](../DEFECTS.md).
- **A legged machine cannot be pushed, only asked.** `LeggedLocomotion` is the single author of its body transform (Invariant I4 in [Locomotion.md](Locomotion.md)), so the `MovePosition` branch is overwritten from `pathPos` on the next `LateUpdate` and a towed ostrich never moves at all, silently. `LeggedDriver` implements `ITowable` and calls `LeggedLocomotion.Drag`, which moves the path and **not** the footholds — leaving them is what makes the legs step instead of skate. `ITowable` lives in the default assembly, so the locomotion itself cannot implement it; the driver is the seam.
- **Strain only builds while the rope is taut** (`Leash.IsTaut`). Without that gate, walking away from a knot you are standing next to would tear the rope off after `resistSeconds` without it ever having gone tight.
- **`PullOf` returns zero for an infinite mass.** A static anchor resists everything and tows nothing, and the early return also keeps `Infinity * 0f` from producing a `NaN` that would poison every clamp downstream.
- **`ResistBaseSeconds` is a property, `ResistSeconds` a static method.** They cannot share a name in one C# type; the property is the authored figure and the method scales it by the captor's pull.
- **The per-body pull sum's scratch list is per rope, not static.** `Snap` re-enters this class inline on the host, and a buffer shared across a re-entrant call is the same trap `NetChannel` re-entrancy already cost this codebase once.
- **The knot is a local offset, not a world point.** A world point re-projected a relay later against a moving target lands on a different part of the animal on every machine — and shape, standing stretch and break verdict all follow from it, permanently.
- **The paid-out length travels** in `arg.B` (centimetres, the `CraftLaunch` convention). Measured per machine, a tie across anything moving settles a metre apart and stays that way.
- **`Leash.Snap` sets `disposed` before the send.** `NetTo.All` dispatches inline on the host, and the handler resolves this same rope and calls `Snap` again — flag-after-send is unbounded recursion.
- **A kinematic end reports no velocity**, so nothing is arrested on that side and the position term does it all. That converges to a **standing overstretch** (~0.5 m for a creature walking off at 4 m/s), not to zero. That is the intended "straining at the leash" look.
- **`everTied`** exists because a rope is built untied and its ends attached one at a time; a physics step landing between the two calls would read a half-built rope as "the thing is gone" and dispose it.
- **Clicking a rope is analytic** — no collider, and it is not getting one. `LeashRope.Aimed` runs `RayToSegment` against the points *actually drawn*, so a rope sagging on the ground is grabbed where it lies. Two clamps matter: solved as infinite lines, a rope **behind** you is as pickable as one in front; past a segment's end the nearest point is its **endpoint**. The `grabRadius` slack is what lets a rope resting on the ground win against the ground it rests on.
- **`LeashGround` is gone.** A downward height probe is not how a rope meets the world: it *drew* the rope draped on a hillside the constraint still measured straight through. The bends are real contacts now, so drawing them is just drawing where the rope is.
- **Unwrap runs before wrap, within one step.** The other order re-tests a waypoint inserted this same step against a neighbour it has not met yet and removes it again immediately.
- **A wrap dies on line of sight between its neighbours**, not on a sign-of-cross-product turn test. The turn direction at a bend over a curved surface is ambiguous in 3D, and a waypoint that cannot decide sticks forever.
- **`ResolveEnd` pulls toward the adjacent bend.** Reverting that one line to `aToB` silently turns every wrap in the game back into decoration — the rope will still *look* bent and will drag its load straight through the pillar.
- **`wrapLayers` must exclude everything dynamic**, and the `TryResolveSettings` fallback deliberately uses a mask of **0**: with the leash item missing from the build, a straight rope is right and a guessed mask is a desync.
- **A wrap is refused if it would land within two clearances of its own neighbour.** A rope lying along a flat wall contacts it everywhere; without the refusal the list fills in one step and the measured length collapses.
- **Sag can still dip a slack rope slightly below a surface between two bends**, bounded by `maxSag`. Slack is shared out per segment in proportion to span, so a rope pinned round a corner droops in both halves rather than dumping it all into one.
- **A rail's `connections` are one-way unless set both ways.** A rope gets onto that rail and can never come off it.
- **Rail capture has no hysteresis yet.** A player standing exactly where the straight line to the load grazes the slot mouth may see the bend form and clear on alternating steps. Not seen in play yet — if it shows, the fix is a dead zone on capture, not a smaller `captureRadius`.
- **Sag is `0.5·L·√(1 − (d/L)²)`**, from true slack. The predecessor used `1 − dist/maxLength`, a *ratio*, so droop depended on rope length rather than on spare rope.
- **AI is not told it has been leashed.** A creature is pulled; its brain keeps pathing. The visible lean is the intent.

## Extending

1. Tunables live on [LeashArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs) and flow out through `RopeSettings`; add the field there and to `Leash.Settings`.
2. `LeashArtifact.TryResolveSettings` finds the leash prefab through `Registry<InventoryItem>`, so save- and snapshot-rebuilt ropes pick the change up with nothing to wire.
3. A new kind of end: extend `LeashEndKind`, resolve it in `LeashEnd.TieTo`, and add a branch to `LeashEnd.Pull`. Whatever it is must answer `Network.Owns` correctly or it will never be resolved. A **vehicle** needs no new branch — implement `ITowable` and it takes the first one.
4. A new mover must implement `IMovementMotor.TopSpeed` off a **stable prefab figure**, never the current speed: both machines derive a rope's pull from it independently and have to agree without a message. The interface carries it so the compiler names anyone who still owes an answer.
5. Pure functions (`ShareOf`, `ArrestSpeed`, `CorrectionDistance`, `PullOf`, `TowCap`, `CombinedPull`, `ResistSeconds`, `ResistStrain`, `HeldBackFraction`, `SagDepth`, `RayToSegment`) are `static` so they are testable without a scene — add to [LeashConstraintTests.cs](Assets/Game/Editor/Tests/LeashConstraintTests.cs). `Awake`/`Start` do not run on an `AddComponent`'d MonoBehaviour in EditMode and `Time.time` starts at 0 there, which is why `ResistStrain` takes its own `dt`.
6. A new **rail**: place a `LeashRail`, give it two end transforms, and wire `connections` **both
   ways**. Nothing else is needed — a rail-bound bend is an ordinary `LeashWrap` from there on.
7. Anything new on the wire must fit `NetArg`: `A` is the hotbar slot, `P` carries the knot, `B` is already verb + packed length.

**Not this system:** the **Lasso** ([Lasso.md](Lasso.md)) is a separate throwable-loop artifact with its own Verlet rope and twirl-charge hold stream.

**A leash is not always made by the leash.** Since 2026-09-05 the lasso *ties its catch off* into one: `LassoHitch.TieOff` calls `LeashArtifact.TryResolveSettings` and `Leash.Create` from the lasso's own `Present`, on every machine, then drops the lasso. Such a rope is indistinguishable from a hand-tied one from here on — same constraint, same break rules, same `LeashSaveable` capture off `Leash.All` — which is the whole reason the hand-off produces a real `Leash` rather than a second rope subsystem. `TryResolveSettings` is therefore load-bearing for a caller outside this folder: it is what supplies the rope MATERIAL, which no save file and no other system can.
