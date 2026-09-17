---
system: Ladders
layer: characters
summary: "Ladder volumes, and the LadderClimber that climbs, slides down and steps off them"
paths:
  - Assets/Game/Scripts/Gameplay/Traversal/Ladder.cs
  - Assets/Game/Scripts/Characters/Player/Movement/LadderClimber.cs
  - Assets/Game/Editor/Traversal/LadderClimberWiring.cs
  - Assets/Game/Editor/Tests/LadderTests.cs
symptoms:
  - "walking into a ladder does nothing"
  - "the player stalls under the floor at the top of a ladder"
  - "the player falls down the gap at the top of a ladder instead of climbing down"
  - "jumping near the top of a ladder pulls me onto it"
  - "a ladder can be climbed from behind or grabbed in mid-air"
reads_with: [PlayerCharacter, ArtPipeline, Wingsuit]
updated: 2026-09-17
---

# Ladders

A ladder the player climbs by walking into it. Two components: `Ladder` on the ladder says where it is
and which volume counts as being at it; `LadderClimber` on the player does the climbing.

**Scope:** [Ladder.cs](Assets/Game/Scripts/Gameplay/Traversal/Ladder.cs) ·
[LadderClimber.cs](Assets/Game/Scripts/Characters/Player/Movement/LadderClimber.cs) ·
[LadderClimberWiring.cs](Assets/Game/Editor/Traversal/LadderClimberWiring.cs)
**Where ladders come from:** the Sky City's seven `LAD_SkyCity_##` markers —
[`SkyCityBuilder`](Assets/Game/Editor/Environment/SkyCityBuilder.cs) adds a `Ladder` to each, wired to
its `_Top` and `_Exit` children (see [ArtPipeline.md](ArtPipeline.md) and `sky_city_BUILD.md`).

## Model

- **The volume is numbers, not a trigger.** A trigger collider is found by every ray that does not
  ignore triggers (crosshair, placement aim, camera) and would eat clicks on whatever is behind the
  ladder. `Ladder.At(feet)` / `Ladder.AtTop(feet)` walk a static registry instead.
- **Which side you climb from comes from the exit, not the transform.** The exit floor is behind the
  rungs; `TowardClimber` points away from it. The markers arrive rotated by the FBX axis conversion
  and scaled x100, so their axes are not trusted.
- **Two regions per ladder.** `Contains`: in front of the rungs (`behind`..`reach`), within
  `halfWidth`, from `footMargin` below the foot up to the step-off height. `TopContains`: a band
  `topBand` either side of the step-off height, from `topReach` back over the exit floor out to the
  climber's side of the gap.
- **The climber owns the body the way a wing does.** `PlayerMovement.SetClimbing(true)` skips the
  velocity write, fall damage and the leg jump; the probe and animator keep running. Gravity is off
  and restored to what it was.

## Key types

| Type | Role |
| --- | --- |
| `Ladder` | `Foot`, `TopHeight`, `ExitPoint`, `TowardClimber`, `Contains`, `TopContains`, `At`, `AtTop`, `Configure(top, exit)` for builders |
| `LadderClimber` | On `PlayerCharacter.prefab` beside `PlayerMovement`. Speeds, grab alignment, standoff, `mantleReach`. `LetGo()` from every exit path, `ITeleportAware` |
| `PlayerMovement.SetClimbing` / `IsClimbing` / `BodyCapsule` | The hand-over flag; `BodyCapsule` is the one body capsule (the ragdoll adds others) |

## Flows

**Taking hold (bottom).** Feet in `Contains` and either forward input within `grabAlignment` of the
rungs, or a Jump press → climb (`GDC-L1-FEEL-0003`: the intent, not a button).

**Taking hold (top).** Feet in `TopContains` and either walking over the gap toward the climber's side,
or dropping (not grounded, falling, feet past the rung line) → the body is moved onto the climbing line
`topEntryDrop` below the top — or, if the body would be inside the top floor there, a body height plus
`mantleReach` below it.

**On the ladder, each physics step.** Forward or Jump held: up at `climbSpeed`. Back: down at
`descendSpeed`. Nothing: slide down at `slideSpeed`. Horizontal velocity pulls onto the line
`standoff` in front of the rungs. Then, in order:
1. Strafe past `letGoStrafe` with nothing forward/back → let go and fall.
2. Feet at the step-off height → **step off**: feet to `ExitPoint + stepOffLift`, velocity zero.
3. Climbing up, top within a body height + `mantleReach`, and the head's upward cast hits something
   above it → step off (climb over the floor's lip).
4. Descending and feet within `bottomTolerance` of the foot → let go.
5. Feet outside `Contains` (shoved off) → let go.

## Multiplayer

Owner only (`Network.Owns`), like the wingsuit and jetpack: the player's NetworkTransform is
owner-authoritative, so peers see the climb as the pose moving. Nothing climb-specific is replicated.
Ladders are static scene geometry on every machine. There is **no climbing animation**; peers and the
owner see the ordinary airborne blend.

## Persistence

None. Ladders are static; a climb is not saved — a player loaded mid-ladder stands (or falls) where
they were and takes hold again. A teleport lets go.

## Gotchas

- **The player is 3 m tall, not 2.** The body capsule is authored 2 m on a transform stretched 1.5 in Y.
  Anything measured off `capsule.height` without `lossyScale` is a third short — the first climb test
  did exactly that and passed ladders a real body gets stuck on. `LadderClimber.BodyHeight` and the test
  both use world size.
- **On a 3 m body the head meets the top floor a whole body height before the feet reach it.** Without
  the climb-over (rule 3) ladders 03, 04 and 07 in the Sky City stalled the climber under the lip. The
  upward cast ignores distance-0 hits, or the deck touching the feet at the bottom reads as "blocked
  above".
- **Ladder-top gaps are wider than the player** at the city's Scale 1.5, which is why `TopContains`
  exists: without it, walking off the exit floor over the gap is a 30 m fall.
- **A jump beside the top comes down inside `TopContains`.** The drop catch requires the feet to be past
  the rung line, or a hop on the exit floor pulls the player onto the ladder.
- **Ladder 05's exit starts 0.10 m inside Bag1's hull**; depenetration clears it in a step.
  `SkyCityPrefabTests` allows 0.15 m.

## Extending

**A ladder somewhere else** — add `Ladder` to an object at the ladder's foot (on the rung line), with a
`top` transform at the step-off height and an `exit` transform on the floor behind the rungs; or call
`Configure` from a builder, as `SkyCityBuilder.GatherLadders` does. Nothing registers it but enabling it.
Check it with a column test like `SkyCityPrefabTests.ThePlayerCanClimbEveryLadderAndStepOffAtTheTop`.

**The player prefab lost the climber** (rebuilt, reverted) — **Tools ▸ SpaceGame ▸ Player ▸ Wire Ladder
Climber**, idempotent.

**A climbing animation** — drive it from `PlayerMovement.IsClimbing` in `UpdateAnimatorParameters`; the
animator bool needs to be one `ClientNetworkAnimator` already replicates.
