---
system: LedgeClimbing
layer: characters
summary: "Jump climbs onto or over a ledge; the grapple hangs below an edge; holding Jump grabs ledges in mid-air"
paths:
  - Assets/Game/Scripts/Characters/Player/Movement/LedgeClimber.cs
  - Assets/Game/Scripts/Characters/Player/Movement/LedgeProbe.cs
  - Assets/Game/Scripts/Characters/Player/Movement/LedgeAirGrab.cs
  - Assets/Game/Scripts/Characters/Player/Movement/PlayerBodyShape.cs
symptoms:
  - "pressing Jump at a wall does an ordinary jump instead of climbing"
  - "I cannot climb or vault over a railing, only solid walls work"
  - "the grapple drops me at the top instead of letting me climb up"
  - "grappling to an edge from a distance never hangs me under it, I just get boosted off"
  - "the player is stuck unable to walk after a climb"
  - "after a climb and a mount the player walks on air after dismounting"
  - "[LedgeClimber] Climbing without a matching animation"
  - "climbing over an edge is slow and floaty"
  - "double-tapping Space next to a wall climbs instead of opening the wings"
  - "double-tapping Space while grappling climbs instead of opening the ornithopter"
  - "jumping next to a railing or off a deck backwards climbs instead of jumping"
  - "pressing Jump while strafing along a wall does not climb"
  - "pressing Jump next to another player or an NPC climbs onto their head"
  - "perched on the grapple under an edge, Jump does a plain jump instead of climbing"
  - "climbing onto a moving barge or the strider city stalls and drops me"
  - "knocked down mid-climb, the limp player's root is dynamic again"
reads_with: [PlayerCharacter, Ladders, Artifacts, BodyEquipment, HumanoidAnimation, Vehicles]
updated: 2026-10-06
---

# Ledge climbing

Jump at a wall too tall to jump onto and the player climbs it; the grappling hook ends a reel in a hang under an edge that Jump climbs; holding Jump in mid-air catches the first ledge in arms' reach. Design and rationale: [the spec](docs/superpowers/specs/2026-10-06-ledge-climbing-design.md).

**Scope:** [LedgeClimber.cs](Assets/Game/Scripts/Characters/Player/Movement/LedgeClimber.cs) · [LedgeProbe.cs](Assets/Game/Scripts/Characters/Player/Movement/LedgeProbe.cs) · [LedgeAirGrab.cs](Assets/Game/Scripts/Characters/Player/Movement/LedgeAirGrab.cs) · [PlayerBodyShape.cs](Assets/Game/Scripts/Characters/Player/Movement/PlayerBodyShape.cs) · [PlayerTraversalWiring.cs](Assets/Game/Editor/Traversal/PlayerTraversalWiring.cs)

## Model

- **Three components, one rule.** `LedgeProbe` (serialized inside the climber, not a component) decides what is climbable; `LedgeClimber` runs the climb; `LedgeAirGrab` is an optional add-on that only asks the climber. Every way in goes through the one probe, so they agree.
- **Owner only, velocity-steered.** The climber takes the body off `PlayerMovement` with `SetClimbing(true)`, drops its weight, and writes `linearVelocity` each physics step toward two feet positions: `Hang` (against the wall, just above the lip), then `Landing` (on top, or past a vault's far edge). Move keys are ignored until it ends; look stays free.
- **The movement sets the pace, the animation keeps up.** Pull-up at `pullSpeed` (7 m/s), over at `overSpeed` (5 m/s), each at least `minPhaseSeconds`; the action is played through `CharacterActions.Play(..., speedScale)` at `secondsToGrab / pullSeconds` (clamped to `animationSpeedRange` 0.5–3×) so its `Grab` mark lands as the body reaches the lip. The other way round (clip clock drives the body, the first build) made every climb as slow as its clip — slow and floaty on a 1 m step. `maxClimbSpeed` (12) only bounds a phase that fell behind.
- **A ledge is an edge the hands can take** (user decision 2026-10-06): a walkable top with open space just above it (`handRoom`) for hands and head. Whether a whole body could stand up there is not asked — the climb goes over and the far side is whatever it is: a top at the landing spot makes it a climb up, nothing there makes it a vault. Grab-and-mantle games read edges the same way (detect from the hands, not the feet).

## Key types

| Type | Role |
| --- | --- |
| `LedgeProbe` / `Ledge` / `LedgeKind` | `Find(body, feet, facing, minHeight, reach)` → `Ledge { Kind (None/ClimbUp/Vault), Hang, Landing, Facing }`. `Find` (on foot): a capsule sweep over the whole climb height finds the outermost face, then `GrabEdge` walks `lipSearchDepth` past it in `sampleStep`s for the first walkable top between `minHeight` and `reach` with `handRoom` above it. `FindAtHook(body, anchor, facing)` (grapple): the same `GrabEdge`, from `hookSearchBack` short of the hook to `hookSearchAhead` past it, tops from `hookGrabBelow` under the hook to `hookGrabAbove` over it. A top with no hand room (a deck edge with a railing on it) is passed over for the next (the rail). The queries (`Sweep`, `Overlaps`, `TopAt`, the wall sweep) see only the world: `IsWorld` skips self and `IsCharacter` (a `RagdollRig` in the collider's parents) |
| `LedgeClimber` | On `PlayerCharacter.prefab`. `TryClimb()` (Jump's question: rope if offering, else grounded and not tethered), `TryClimbFrom(facing, minHeight, reach)` (checks `CanStart`, then probes from the current feet), `OfferRopeClimb(anchor, facing, onClimb)` / `WithdrawRopeClimb()`, `RopeLedgeInReach(anchor, facing)`, `IsClimbing`, `LetGo()`, `ITeleportAware`. Reach on foot: `minClimbHeight` 1 m, `maxReach` 4.5 m; a rope's edge is found at its hook |
| `LedgeAirGrab` | Each `FixedUpdate` while `JumpHeld`: airborne, not tethered, `afterJumpDelay` past the last `OnJumped`, then `TryClimbFrom(forward, minHeight, airReach 3.3)`. No public surface — nothing else asks it |
| `PlayerBodyShape` | `readonly struct` over the body capsule + rigidbody: world `Height`/`Radius`/`Feet` through `lossyScale`, `BlockedAt`, `CastClear`, `IsSelf`. Shared with [LadderClimber](Ladders.md) |
| `CharacterAction.Mark.Grab` | The frame the hands take the edge. Append-only enum, after `Contact`, `Release` |
| `CarriedBody` | [CarriedBody.cs](Assets/Game/Scripts/agents/Modules/Riding/CarriedBody.cs): `SuspendGravity(go, owner)` / `Release(go, owner)` own the body's gravity flag. A record only ever claimed by `SuspendGravity` gives back `useGravity` alone — no kinematic, interpolation or velocity restore |

## Flows

**Jump on the ground.** `PlayerMovement.OnJump` → returns early if bouncing/climbing/hauling → `LedgeClimber.TryClimb()` → if it climbs, the leg jump never runs. Crouching, gliding, bouncing, a dead/ragdolled/kinematic body, a non-owner, and any spot inside a `Ladder` volume refuse.

**A climb.** `Begin`: `CarriedBody.SuspendGravity`, velocity zeroed, `SetClimbing(true)`, play the action picked by `ActionFor` (below), then call the rope's `onClimb` so the rope lets go of a body already climbing. **Which action**: off a rope → `highClimbUpAction` (hang-and-pull-up) / `highVaultAction`; else by lip height above the feet — up to `midEdgeHeight` (2 m) the low clips, up to `highEdgeHeight` (3.3 m) `midClimbUpAction`, above it the high one; sprinting (`PlayerStance.IsSprinting`) from the ground → `runningClimbUpAction`. While the grapple holds the player perched, `SetHanging(true)` loops `hangAction`.

**Finish.** `LetGo` first, then set the exit velocity: zero for a climb up, `Facing * vaultExitSpeed` plus `CarryMomentum()` for a vault. Order matters: the release gives back gravity only, so whatever velocity is on the body after `LetGo` is the one it leaves with.

**Grapple.** While the owner is off the ground, or perched, the hook offers the rope climb every physics step (`OfferRopeClimb` with a cached callback); on the ground and not perched it withdraws it. On winch arrival with `RopeLedgeInReach` the winch stops, velocity is zeroed and the stall timer is skipped (`_perched`); with no edge, `ReleaseInto` and its boost as before. Jump while perched or swinging → `TryClimb` → `ClimbOffRope` (announce release, `StopGrapple`, no boost). Releasing the trigger while perched lets the hook's existing `holdTimeout` drop the rope. See [Artifacts.md](Artifacts.md).

**Mid-air hold.** `LedgeAirGrab.FixedUpdate` tries a grab while Jump is held.

**Double tap = wings, always** (user decision 2026-10-06, replacing "the ledge wins"). `BodyEquipmentController.OnBodyActivate` cancels a running ledge climb through `LetGo` and then deploys the back item; only a ladder or hatch (`PlayerMovement.IsClimbing` still true after that) refuses it. The first tap of a double tap often starts a climb itself — on a grapple rope every Space offers one, and the hook search is generous — so "refuse while climbing" left the wings nearly unreachable while grappling. See [BodyEquipment.md](BodyEquipment.md).

## Multiplayer

Nothing on the wire. Owner-only (`Network.Owns`); the player's `NetworkTransform` is owner-authoritative, so peers see the pose move, and the action rides the network animator. The rope's release is announced with `AnnounceRelease` as any grapple release is. `PlayerCharacterNetworked` inherits the components from the base prefab.

## Persistence

Nothing saved: a climb lasts about a second, and a player loaded mid-climb just drops or climbs again. Hanging on a rope is the grapple's saved rope state, not this system's.

## Gotchas

- **Weight goes through `CarriedBody`, not a private `useGravity` save.** A mount taken mid-climb would bank `useGravity = false` as the body's normal state and the player walks on air after dismounting. `LetGo` is the only exit and calls `CarriedBody.Release`.
- **`Release` does not touch velocity for the climber's claim.** A weight-only record (`SuspendGravity` and nothing else) restores `useGravity` alone, so the steering speed survives `LetGo`: `Finish` zeroes it for a climb up and replaces it with the vault exit, after the release. A stall, teleport or disable lets go with whatever the steer last wrote. Before this, the full restore ran under a ragdoll too: `PlayerRagdoll.SuspendLayers` makes the root kinematic directly, the next step `BodyIsOurs()` is false, and `LetGo` → `Release` set `isKinematic = false` and zeroed velocity under the limp bones.
- **A double tap mid-climb must end the climb BEFORE the back item opens.** `PlayerInputManager.HandleJump` raises `OnJumpPressed` then `OnBodyActivatePressed`; a jetpack/wingsuit/wing pack entered over a running climb banks its suspended `useGravity = false` as the body's own and writes it back after the climb restored it — a player walking on air. So `OnBodyActivate` calls `LedgeClimber.LetGo()` (which releases the climb's `CarriedBody` weight claim) first, and refuses outright only for a ladder or hatch, which keep holding the body.
- **Characters are not ledges.** A 3 m capsule in front reads as wall plus walkable crown, so Jump next to another player or an NPC climbed onto their head. The probe skips any collider with a `RagdollRig` in its parents — on the root of the player, every NPC and every creature (mounts included), and on no vehicle, building or sky-fleet hull. Not "skip dynamic bodies": remote players and agents are kinematic. `PlayerBodyShape.BlockedAt` / `CastClear` still see characters, so someone in the landing spot blocks the climb.
- **`OnJump` calls the climber directly** instead of a second `OnJump` subscriber: two subscribers fire in no defined order and the jump could leave the ground in the same step the climb took the body.
- **`SetClimbing` has three users: the ladder, the hatch and the ledge.** `LadderClimber.TryTakeHold` and `HatchCrawler.TryCrawl` refuse while `movement.IsClimbing`, and the ledge climber refuses then too and inside a ladder volume (`Ladder.At(Feet)`), so none takes a body another holds. The hatch keeps a private `gravityBefore`; stacked on a climb it banked the weightless state.
- **A grapple's edge is found at the hook, not from the feet.** Rediscovering it from the body failed one geometry at a time: the winch arrives 2.5 m from the anchor with the body 1.7–2.4 m off the face (past an on-foot wall reach), deck edges, railings and stairs stand proud of the wall below them, and upper decks overhang lower ones. Measured 2026-10-06 against the satellite tower prefab, grapples to real edges: ~0/118 from the feet, 54/118 after reach + stepped-top patches, 128/128 with `FindAtHook`. The hook point is the player's intent; the probe only asks whether there is an edge to grab near it.
- **Edge samples start just past the face (`lipInset` 0.1, the probe radius).** A railing's top rail is a few centimetres deep; at the original 0.3 m the downward sweep fell past it into the drop beyond, so every railing (the tower's 1.6 m deck rails) refused while solid boxes climbed fine.
- **The on-foot wall sweep covers the whole climb height** (a capsule from `minHeight/2` to `reach`), so the face is whatever stands out furthest. Swept at one height it found the recessed concrete under the tower's decks and the climb rose straight up through the deck edge, railing and stairs.
- **On foot, Jump climbs only when it means to** (`MeansToClimb`, user decision 2026-10-06): move keys within `climbIntentAngle` (60°) of facing, or no keys at all, climb; strafing along a wall or backing away is a plain jump. With edge-at-the-hands detection nearly everything beside a building is grabbable, so every hop along a railing and every jump backwards off a deck was being taken for a climb. Not applied to the rope (the hook already says where) or the mid-air hold.
- **Crouched is refused**: a crouched capsule is short and the hang and landing heights assume the standing one.
- **`TryClimbFrom` checks `CanStart` before reading `Feet`**, because `BodyCapsule` can be null on a body that is not the player's. That is why it takes no feet argument: a caller passing `climber.Feet` evaluated them first.
- **A grounded player with the rope out keeps today's jump**: the rope offers its climb only while airborne — or perched, because a body hung against stepped geometry can read `IsOnGround` and the offer was withdrawn under it.
- **Known limitation — moving decks.** `Hang` and `Landing` are world points captured at `Begin`. On a moving deck (a dune barge, the strider city) the ledge moves away from them, so the climb can stall and drop the player. Out of scope; a fix would express both in the deck's local space.
- **`Mark` is append-only**; assets store the numbers.
- **The player is 3 m tall**: every distance goes through `PlayerBodyShape` (`lossyScale`), never `capsule.height`.

## Extending

**A new rope-like holder** calls `OfferRopeClimb(anchor, facing, onClimb)` every physics step it holds the body and `WithdrawRopeClimb()` when it stops; `onClimb` must let go without boosting.

**New clips need a `Grab` mark** — the moment the hips reach the lip with the hands planted, as normalized time of the CUT. Assigned today (2026-10-06): Mixamo `Ledge Climb Low/Mid/High/Running` (`Art/Animations/Humanoid/Ledge Climb *.fbx`, rows in that folder's `cuts.json`; Low/Mid trimmed to the climb, High = Braced Hang To Crouch, Running = Sprint To Wall Climb) and CMU `Hang Swing` for the hang. Their rise and forward travel must stay OUT of the pose (root motion, discarded — the Animator applies none): baked in, the mesh rises on top of the capsule's own rise. The vault slots temporarily hold the Low/High climbs: the CMU `Ledge Vault Low`/`Ledge Vault` cuts exist but `CmuClipImporter` bakes height into the pose and ignores `travelsVertically`, so they bob 0.4–0.75 m above the body — swap in a Mixamo vault ("Jump Over") when there is one. Adding an action means **Rebuild Humanoid Controller** (shared by every character: give other sessions a heads-up).

**The player prefab lost the components** (rebuilt, reverted) — **Tools ▸ SpaceGame ▸ Player ▸ Wire Ledge Climbing**, idempotent.
