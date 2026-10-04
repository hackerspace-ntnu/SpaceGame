---
system: Ladders
layer: characters
summary: "Ladder volumes, the player's LadderClimber, and the NavMesh link NPCs climb by"
paths:
  - Assets/Game/Scripts/Gameplay/Traversal/Ladder.cs
  - Assets/Game/Scripts/Gameplay/Traversal/NavLinkAreas.cs
  - Assets/Game/Scripts/Characters/Player/Movement/LadderClimber.cs
  - Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.Links.cs
  - Assets/Game/Editor/Tests/LadderTests.cs
  - Assets/Game/Editor/Tests/LadderTraversalTests.cs
symptoms:
  - "walking into a ladder does nothing"
  - "the player stalls under the floor at the top of a ladder"
  - "the player falls down the gap at the top of a ladder instead of climbing down"
  - "jumping near the top of a ladder pulls me onto it"
  - "a ladder can be climbed from behind or grabbed in mid-air"
  - "an NPC walks to the foot of a ladder and stands there"
  - "an NPC slides up a ladder instead of climbing it"
  - "[Ladder] found no NavMesh within 1 m of its foot or exit"
  - "the project has no 'Ladder' NavMesh area"
reads_with: [PlayerCharacter, ArtPipeline, Wingsuit, NavMeshSystem, AgentSystem]
updated: 2026-10-02
---

# Ladders

A ladder the player climbs by walking into it, and NPCs climb by routing over it. `Ladder` on the ladder says
where it is, which volume counts as being at it, and registers the NavMesh link NPCs plan over; `LadderClimber`
on the player does the player's climbing; `NavMeshAgentMotor` (its `.Links.cs` partial) does the NPC's.

**Scope:** [Ladder.cs](Assets/Game/Scripts/Gameplay/Traversal/Ladder.cs) ·
[LadderClimber.cs](Assets/Game/Scripts/Characters/Player/Movement/LadderClimber.cs) ·
LadderClimberWiring.cs · [NavLinkAreas.cs](Assets/Game/Scripts/Gameplay/Traversal/NavLinkAreas.cs) ·
[NavMeshAgentMotor.Links.cs](Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.Links.cs)
**Where ladders come from:** the Sky City's seven `LAD_SkyCity_##` markers —
`SkyCityBuilder` adds a `Ladder` to each, wired to
its `_Top` and `_Exit` children (see [ArtPipeline.md](ArtPipeline.md) and `sky_city_BUILD.md`).
The three `Decorations/Watchtowers/Deco_Watchtower_{Wood,MetalLattice,MetalScaffold}` prefabs carry one each:
foot on the ground, `Top` 6 m up, `Exit` 0.9 m inside the deck edge through the rail gap (checked 2026-10-02 against
their colliders: the exit is on the deck, the foot clear of the brace colliders).

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
| `NavLinkAreas` | Area IDs by name: `Jump` (built-in 2) and `Ladder` (3, cost 3); `GroundMask` = every area but those two |
| `NavMeshAgentMotor` (`.Links.cs`) | Crosses links: `usesLadders`, `ladderClimbSpeed` / `ladderDescendSpeed` / `ladderStandoff` / `ladderStepSpeed`, `plainLinkSpeed`, `jumpLink*`; `IsRidingLink` |
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

**NPCs.** On enable (`navigable`, default on) a ladder registers a bidirectional `NavMesh.AddLink` in the
Ladder area from `linkApproach` (1 m) in front of the rungs, on the ground, to `ExitPoint` on the deck,
each end snapped to the mesh within `linkSnapDistance`; the link's owner is the `Ladder`, and it is removed
on disable. World positions, not a `NavMeshLink` component, because the Sky City markers are scaled x100. If
no NavMesh is under an end yet (a streamed world's mesh can arrive after the ladder) it retries every
`linkRetryInterval` for `linkRetryTimeout`, then warns. `NavMeshAgentMotor` sets `autoTraverseOffMeshLink`
false, and the first tick an agent stands on a link whose owner is a `Ladder` it takes the body by hand
(`updatePosition` off) through three legs: onto the climbing line `ladderStandoff` in front of the rungs,
along it at `ladderClimbSpeed` / `ladderDescendSpeed`, off onto the far end; facing the rungs throughout.
Then `agent.Warp` onto the mesh and the module's next `MoveTo` re-plans. Any NavMeshAgent can use it;
`usesLadders = false` removes the Ladder area from the agent's `areaMask` for anything that cannot climb.
The settlement's tower ladders: the two watchtowers and NomadBuilding_B20's BellFrame link. Three `BellFrame__01_Ladder`s are `navigable = false` (NomadBuilding_B18 and NomadBuilding_B29: a foot sunk or buried below the ground; NomadMarketPlaza: its landing deck is too small for the mesh) and NomadGuardPost's has `linkSnapDistance` 1.5 (its landing is 1.3 m from the mesh). They stay climbable by the player; no resident has a post on them. In play an override-sent resident climbed the wooden watchtower's ladder to its deck.

## Multiplayer

Owner only (`Network.Owns`), like the wingsuit and jetpack: the player's NetworkTransform is
owner-authoritative, so peers see the climb as the pose moving. Nothing climb-specific is replicated.
Ladders are static scene geometry on every machine. There is **no player climbing animation**; peers and the
owner see the ordinary airborne blend. NPC climbs are server-authoritative: the motor ticks only where the
agent is simulated and the `NetworkTransform` replicates the pose; every machine registers its own copy of the
link (harmless where nothing paths). The NPC also has no climbing pose -- it slides up in its idle. The `climb`
cue and a looping "Climb Ladder" CMU action exist in the humanoid catalogue and are not wired
([HumanoidAnimation.md](HumanoidAnimation.md)).

## Persistence

None. Ladders are static; a climb is not saved — a player loaded mid-ladder stands (or falls) where
they were and takes hold again. A teleport lets go. An NPC's ladder ride is not saved either: loaded
mid-climb it is put on the nearest mesh by `DeferredNavMeshWarp` and plans again. (A jump link in flight is
a leap and comes back through the leap's saved state.) A teleport or disabling the motor abandons a ride.

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

- **A ladder whose ends are not on a NavMesh has no link, and says so only after `linkRetryTimeout`.**
  Agents just never route over it. Check the foot is within `linkSnapDistance` of walkable mesh (the world
  bake sits ~0.26 m above the ground) and the exit floor is baked, and that the `Ladder` area still exists.
- **Links are registered at their enable position.** A ladder on something that moves (a flying ship) keeps
  its link where it was; the Sky City's NavMesh has the same assumption (`StaticNavMeshData`).
- **A link end that is not on the mesh made an agent's path stay pending forever** in the test world (the
  agent stood still, clean console). `Ladder` snaps its ends; do the same for any link registered by hand.
- **Legged rigs plan over `NavLinkAreas.GroundMask`.** They follow corners in straight lines and cannot
  cross a link, so `LeggedDriver` leaves the Jump and Ladder areas out of its path queries.

## Extending

**A ladder somewhere else** — add `Ladder` to an object at the ladder's foot (on the rung line), with a
`top` transform at the step-off height and an `exit` transform on the floor behind the rungs; or call
`Configure` from a builder, as `SkyCityBuilder.GatherLadders` does. Nothing registers it but enabling it.
Check it with a column test like `SkyCityPrefabTests.ThePlayerCanClimbEveryLadderAndStepOffAtTheTop`.

**The player prefab lost the climber** (rebuilt, reverted) — **Tools ▸ SpaceGame ▸ Player ▸ Wire Ladder
Climber**, idempotent.

**A climbing animation** — drive it from `PlayerMovement.IsClimbing` in `UpdateAnimatorParameters`; the
animator bool needs to be one `ClientNetworkAnimator` already replicates.
