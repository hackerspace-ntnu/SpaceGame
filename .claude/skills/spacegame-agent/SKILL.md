---
name: spacegame-agent
description: Use when adding or changing a creature, NPC, enemy, animal, turret, mount, or AI behaviour in the SpaceGame Unity repo — a new AgentController prefab, a new IBehaviourModule / IFacingModule, faction and FactionRelationshipTable wiring, TargetingProfile or AgentTargeting tuning, PerceptionModule / AlertBroadcaster / NoiseEmitter sensing, ChaseModule / FleeModule / WanderModule / CloseCombatModule / AgentRangedCombatModule composition, an AgentAnimatorDriver walk cycle that skates, NavMeshAgentMotor versus LeggedDriver movement, peaceful-until-provoked creatures, NpcTaskModule / AgentGoal errands, or NpcWorldSim caravans and creature spawning.
---

# SpaceGame agents

## Overview

An agent is a **prefab plus a set of components**. Behaviour is composed by dropping
`IBehaviourModule` MonoBehaviours onto the prefab and arbitrated by priority — never by
subclassing `AgentController`, never by a behaviour tree, never by a per-creature state machine.

Three decisions each have exactly one owner: **who to fight** (`AgentTargeting`), **where to go**
(`AgentGoal`), **how to move** (`IMovementMotor`). A module that duplicates one of those three is
the bug this architecture exists to prevent.

Code: `Assets/Game/Scripts/agents/`, namespace `SpaceGame.Agents`, Assembly-CSharp (no asmdef, so a
new module needs no assembly wiring). Tick order, full interface members, complete module catalog,
motors and the animator contract are in **`reference.md`** beside this file.

## When to use

New creature / NPC / enemy / animal / turret / mountable; a new AI behaviour; tuning aggression,
perception, factions, herds, patrol, chatter, or NPC errands; a creature that animates wrong,
targets the wrong thing, or ignores the player.

## When NOT to use

- Mesh, rig, FBX export → **`blender-model`** skill.
- Save / load, `SaveableEntity`, savers, `SaveScope` → **`spacegame-persistence`**.
- `NetworkObject` registration, RPCs, `NetRelay` / `NetChannel`, damage replication →
  **`spacegame-multiplayer`**.
- The item an NPC carries and fires → **`spacegame-artifact`**.
- Player character logic (`PlayerController`, `PlayerMovement`) — not an agent.

## Decision guide: reuse a module, or write one

Writing a new module is the last resort. In order:

1. **Does an existing module do it?** Nearly thirty ship — see the table below and
   `reference.md` §3, and check `Assets/Game/Scripts/agents/Modules/` for anything newer.
2. **Does it only need different data?** Most "new behaviour" is a `TargetingProfile`, an
   `NpcTask[]`, a `FactionRelationshipTable` row, or a different priority number.
3. **Does it decide WHERE, not HOW?** Then it is not a movement module: give it
   `ClaimsMovement => false` and have it write `AgentGoal`. `GoalTravelModule` walks there.
   `NpcTaskModule` is the worked example.
4. **Does it only decide where the body POINTS?** Implement `IFacingModule` alongside — not a
   movement module returning `StopAndFace`, which starves everything below it.
5. **Only then** subclass `BehaviourModuleBase`.

A new module is justified when it produces a genuinely new *locomotion* answer — a new reason to
pick a destination. It is not justified for a new target rule (`TargetingProfile` +
`AgentTargeting`), a new weapon (`AgentWeaponDefinition` / `AgentFireProfile` / `AgentAimProfile`,
or an `InventoryItem` + `NpcItemUseModule`), or a new personality line (`ChatterModule`).

## End-to-end checklist: a new creature

Reference implementation to copy: `Assets/Game/Editor/Creatures/GolemBuilder.cs`
(`Tools/Creatures/Build Golem Prefab`). It assembles the whole stack in one place and is the
best template for a new creature builder.

1. **Mesh + rig** — `blender-model` skill. Export through the model's own export script.
2. **Import check (humanoid rigs only)** — confirm the generated avatar reports `isHuman = true`.
   A downgraded generic avatar leaves the character standing still with a **completely clean
   console**.
3. **Prefab** in `Assets/Game/Prefabs/agents/creatures/` (or `.../Robots/`, `.../Characters/`,
   `.../Caravan/`, `.../Vehicles/{Ground,Aircraft,Spacecraft}/`). Existing examples:
   `DuneRat.prefab`, `Golem.prefab`, `Ostrich.prefab`, `Vrescal.prefab`, `Nomad.prefab`,
   `PatrolRobot.prefab`, `DeathmatchBot.prefab`.
4. **Animator** — reuse the FBX's own `Animator`, never add a second one. Set
   `applyRootMotion = false` (the motor owns movement) and `cullingMode = AlwaysAnimate` for any
   rig built from many bone-parented renderers, or it freezes mid-stride when Unity thinks its
   bind-pose bounds are off screen.
5. **Physical presence** — `BoxCollider` or `CapsuleCollider` sized in **world** space, plus a
   kinematic `Rigidbody` with `useGravity = false` for a NavMesh creature.
6. **Movement**, one of:
   - NavMesh creature: `NavMeshAgent` + `NavMeshAgentMotor`. Set `NavMeshAgent.speed` to the
     **run** speed and set `walkSpeedMultiplier = walk / run`.
   - Procedural legged rig: a `LeggedDriver` subclass in `Assets/Game/Scripts/Creatures/Drivers/`
     (must stay in Assembly-CSharp) plus a `LeggedLocomotion` subclass in its own
     `SpaceGame.Creatures.<Name>` asmdef. No NavMeshAgent, no `AgentAnimatorDriver`.
   - Flyer: `FlyingRigidbodyMotor` + `AirWanderModule`. Vehicle: `RigidbodyMotor`.
7. **`AgentController`** on the root; assign `MotorComponent` and `animatorDriver`.
   `AgentTargeting` and `AgentGoal` are auto-added in `Awake`. Set `nearbyAgentScanRadius` and
   `nearbyAgentLayer` only if using `FlockingModule`.
8. **`EntityFaction`** — a `FactionDefinition` from
   `Assets/Game/ScriptableObjects/Factions/Core/` **and** the one relationship table,
   `Assets/Game/ScriptableObjects/Factions/Core/GlobalRelationships.asset`. Without this component
   the creature is invisible to every targeting module and can never acquire one, silently.
   Pick the temperament first, because it decides the faction:

   | Temperament | Faction | Rows in `GlobalRelationships.asset` | Extra |
   |---|---|---|---|
   | Attacks on sight | `RobotFaction`, `BountyHunterFaction`, or a new one | `Hostile` toward `PlayerFaction` | combat modules |
   | Peaceful until hurt | `FaunaFaction` (or a new empty one) | **none** — `FaunaFaction.asset` appears in zero rows; adding one "for completeness" makes every creature of that faction attack on sight | `ProvocationModule`, with `leashRange` ≤ `AgentTargeting.loseRange` |
   | Ambient wildlife | `WildlifeFaction` | already `Hostile` toward the player — change or reuse deliberately | — |
   | Afraid of the player | any | see below | `FleeModule` |

   `FleeModule` resolves its own threat by **relationship**, not by "the player": it uses
   `fleeFromRelationship` (default `Hostile`) against `EntityTargetRegistry`. For a creature that
   should flee the player and nothing else, give it its own `FactionDefinition` with a single
   `Hostile` row toward `PlayerFaction` and add **no** chase or combat module — `AgentTargeting`
   acquires the player, and with only `FleeModule` above `WanderModule` on the ladder the creature
   runs. Setting `fleeFromRelationship = Neutral` instead makes it flee every neutral entity in the
   world, including other creatures.
9. **Health** — `HealthComponent` + `HealthReactionModule` (death sound, despawn, animator
   triggers), plus `EntityLootTable` if it drops anything (a MonoBehaviour, authored per prefab —
   there are no loot-table assets).
10. **Behaviour modules** — at minimum one Fallback module (`WanderModule` or `PatrolModule`), then
    reactive/combat modules from the table. When adding components **from a script, set `priority`
    explicitly**: Unity does not call `Reset()` for `AddComponent`, so the module keeps the
    serialized default of `Fallback (0)` and ties with wander.
11. **Perception** — `PerceptionModule` for FOV/LoS; set `occlusionLayers` explicitly.
    `AlertBroadcaster` + `AlertReceiverModule` for pack alerts; `NoiseEmitter` +
    `NoiseReceiverModule` for hearing.
12. **Animation parameters** (NavMesh creatures) — a controller in
    `Assets/Game/Art/Animations/Creatures/` carrying exactly `SpeedX`, `SpeedY`, `FallSpeed`,
    `IsGrounded`, `IsImmobalized` *(sic)*, `IsAiming`, plus whatever triggers the combat and health
    modules are configured to fire. Then set `animatorSpeedScale = groundSpeed / strideSpeed` or
    the feet skate.
13. **Streaming** — `SceneTracked` with `policy = Migrate`, `keepChunksLoaded = false` for anything
    that roams between chunks.
14. **Persistence** — `SaveableEntity` + `TransformSaveable` + `HealthSaveable`, and
    `AgentStateSaveable` for anything with an `AgentTargeting`. Add these **inside the builder**,
    then run `Tools/Save System/Wire Saveable Prefabs`. Details: **`spacegame-persistence`**.
    (`AgentController` implements `IPersistentEntity`, so every agent is save-eligible with no
    extra opt-in.)
15. **Netcode** — `NetworkObject`, `NetworkedHealthComponent`, `NetAuthority`, then
    `Tools/SpaceGame/Multiplayer/Sync Network Prefabs`. Details: **`spacegame-multiplayer`**.
16. **Get it into the world**, one of:
    - `NpcWorldSim.templates` on the `NpcWorldSim` object in
      `Assets/Game/Scenes/world/persistentScene.unity` — an `NpcGroupTemplate` with
      `NpcGroupMemberSpec { prefab, isLeader, count }`, inlined in the scene, not an asset.
    - `Assets/Game/ScriptableObjects/Settlements/SettlementConfig.asset` → `robotPrefabs`
      (settlement patrols).
    - `MatchManager.deathmatchBotPrefab` (arena).
    - A hand-placed instance in a chunk scene under `Assets/Game/Scenes/world/Chunks/`.
17. **Verify in play**: it wanders; it acquires only what it should; the feet do not slide; the
    walk/run blend matches the motor; it dies, drops loot once, and despawns.

## Module quick reference

Priorities: `Scripted 100 · Override 30 · MeleeAttack 23 · RangedAttack 22 · Reactive 20 ·
Social 15 · Ambient 10 · Personality 5 · Fallback 0`.

| Want | Module | Priority |
|---|---|---|
| Roam | `WanderModule` | Fallback |
| Roam in the air | `AirWanderModule` | Fallback |
| Waypoints / area patrol | `PatrolModule`, `BasePatrolModule` | Fallback |
| Run an errand | `NpcTaskModule` (writes goal) + `GoalTravelModule` (walks) | Fallback / Fallback+1 |
| Close on a target | `ChaseModule` | Reactive |
| Investigate where it lost you | `SearchModule` | Reactive−1 |
| Take cover | `CoverModule` | Reactive+1 |
| Run away | `FleeModule` | Override |
| Kite / keep its distance | `KeepDistanceModule` | Ambient |
| Walk up and talk | `ApproachModule` | Ambient |
| Stop and stare | `WatchModule`, `FacePlayerModule` | Ambient |
| Melee | `CloseCombatModule` | MeleeAttack |
| Built-in ranged weapon | `AgentRangedCombatModule` | RangedAttack |
| Fire a real lootable item | `NpcItemUseModule` | RangedAttack (side-effect) |
| Stationary gun | `TurretModule` | n/a |
| Move as a herd | `HerdModule` / `FlockingModule` | Social |
| Travel as a column | `FormationModule` | Social |
| Peaceful until hit | `ProvocationModule` | order −40 |
| React to allies / noise | `AlertReceiverModule`, `NoiseReceiverModule` | 19 / 18 |
| Say something | `ChatterModule` | Personality (side-effect) |
| Be rideable | `MountModule` + `SteerModule` | Fallback / Scripted |

What each module requires, its exact default priority and how it behaves: **`reference.md` §3**.

Assets that tune them: `Assets/Game/ScriptableObjects/Weapons/{WPN_RobotPistol, FIRE_PistolBurst,
AIM_GruntPoor}.asset`; `Assets > Create > Agents > {Weapon Definition, Fire Profile, Aim Profile,
Targeting Profile}`; `Assets > Create > Factions > {Faction Definition, Relationship Table}`.
`AgentTargeting` runs on its own inline fields unless a `TargetingProfile` asset is assigned, and
the project has leaned on the inline fields — so authoring the first profile for a creature is a
normal, expected step, not a sign something is missing.

## Writing a new behaviour module

Real, complete, compiles against the interfaces in this repo. Goes in
`Assets/Game/Scripts/agents/Modules/Movement/`.

```csharp
// Walks to the nearest piece of loose salvage and stops on top of it, so a scavenger picks over
// what a fight left behind instead of stepping around it.
//
// Ambient priority: below chase and flee, above wander. A scavenger that is being shot at has
// something better to do, and one with nothing to loot falls through to roaming.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    public class ScavengeModule : BehaviourModuleBase
    {
        [Header("Search")]
        [Tooltip("How far this creature notices loose salvage.")]
        [SerializeField] private float searchRadius = 25f;
        [Tooltip("Seconds between registry sweeps. Not per frame — every agent pays this.")]
        [SerializeField] private float rescanInterval = 1.5f;
        [Tooltip("How far off the NavMesh a piece of salvage may lie and still be reachable.")]
        [SerializeField] private float navMeshSampleDistance = 4f;

        [Header("Movement")]
        [SerializeField] private float stopDistance = 0.6f;
        [SerializeField] private float speedMultiplier = 1f;

        // Instance-level, not static: two scavengers ticking in the same frame would otherwise
        // read each other's results out of one shared list.
        private readonly List<ScanContact> contacts = new List<ScanContact>(16);

        private float rescanTimer;
        private bool hasDestination;
        private Vector3 destination;

        private void Reset() => SetPriorityDefault(ModulePriority.Ambient);

        private void OnEnable()
        {
            rescanTimer = 0f;
            hasDestination = false;
        }

        public override string ModuleDescription =>
            "Walks to the nearest loose item and stops on it. Yields to combat and flee.\n\n" +
            "• searchRadius — how far it notices salvage\n" +
            "• rescanInterval — seconds between registry sweeps\n" +
            "• stopDistance — how close counts as arrived";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            // Nothing to scavenge while something is trying to kill us. Returning null rather
            // than Idle hands the frame down instead of starving wander.
            if (context.Targeting != null && context.Targeting.HasTarget)
            {
                hasDestination = false;
                return null;
            }

            if (hasDestination && context.HasReachedDestination)
                hasDestination = false;

            rescanTimer -= deltaTime;
            if (!hasDestination && rescanTimer <= 0f)
            {
                rescanTimer = rescanInterval;
                hasDestination = TryFindSalvage(context.Position, out destination);
            }

            if (!hasDestination)
                return null;

            return MoveIntent.MoveTo(destination, stopDistance, speedMultiplier);
        }

        // A registry query, not Physics.OverlapSphere — see ScannerRegistry for why.
        private bool TryFindSalvage(Vector3 origin, out Vector3 result)
        {
            ScannerRegistry.Collect(origin, searchRadius, contacts, 8);

            for (int i = 0; i < contacts.Count; i++)
            {
                if (contacts[i].Class != ScanClass.Item)
                    continue;

                if (NavMesh.SamplePosition(contacts[i].Position, out NavMeshHit hit,
                                           navMeshSampleDistance, NavMesh.AllAreas))
                {
                    result = hit.position;
                    return true;
                }
            }

            result = origin;
            return false;
        }

        protected override void OnValidate()
        {
            searchRadius = Mathf.Max(1f, searchRadius);
            rescanInterval = Mathf.Max(0.1f, rescanInterval);
            stopDistance = Mathf.Max(0.1f, stopDistance);
            speedMultiplier = Mathf.Max(0.01f, speedMultiplier);
        }
    }
}
```

Rules the example follows, all load-bearing:

- `null` = pass. `MoveIntent.Idle()` **claims the frame** and starves every lower module — use it
  only when standing still *is* the behaviour.
- Read the target from `context.Targeting`. Never query `EntityTargetRegistry` for a hostile
  yourself. For a non-hostile candidate use `TargetResolution.Refresh(...)`, never a bare
  `if (target) return;` — a corpse stays non-null.
- Do the expensive query on an interval, not per frame.
- Reset per-module state in `OnEnable`; a creature is re-enabled by respawn, streaming and loads.
- No authority check inside a module. `AgentController` already gated the whole tick.
- Attack-style modules need enter/exit hysteresis (`rangeExitFactor`), or the winner flips every
  frame at the range boundary, the NavMesh path is thrown away and re-requested, and the agent
  visibly stutters.

**Acting on the world from a module.** The example only walks to the item. There is no existing
"NPC picks up a `PickupableItem`" path — `PickupableItem.Interact` takes an `Interactor` and routes
to the player's inventory. To have a creature actually take it, add an
`EntityInventoryComponent` to the prefab, call `TryAddItem(InventoryItem)` on arrival, and despawn
the world object. Do it inside `Tick`, which is already authority-gated (a server-owned creature
means `Tick` only runs on the server), and despawn through the netcode path rather than
`Destroy` — see **`spacegame-multiplayer`**.

## Common mistakes

| Symptom | Cause | Fix |
|---|---|---|
| Creature never notices anything, no errors | No `EntityFaction`, or no relationship table assigned | Add both; `EntityFaction.Ensure(go, faction, table)` on spawn paths |
| Every "peaceful" creature attacks on sight | A relationship row was added for its faction | Peaceful = **zero rows** + `ProvocationModule`; keep `leashRange` ≤ `AgentTargeting.loseRange` |
| Creature chases A, shoots B, backs away from C | A module resolved its own target | Read `context.Targeting` |
| Everything below one module never runs | That module returns `MoveIntent.Idle()` while merely waiting | Return `null` |
| A script-added module is ignored | `Reset()` is not called for `AddComponent`; priority stayed 0 and tied with wander | Set `priority` explicitly |
| Feet skate; walk looks sluggish or sped up | Only `animationSpeedMultiplier` / `walkAnimBoost` were tuned — those pick the *clip*, not the *rate* | Set `AgentAnimatorDriver.animatorSpeedScale = groundSpeed / strideSpeed`. It is global, so the attack speed changes with it |
| Provoked NPC closes at a walking pace | `NavMeshAgent.speed` was set to the walk | Set it to the run; scale `walkSpeedMultiplier` down from it |
| Character stands still after an FBX re-export, clean console | Unity downgraded the avatar: `isValid = true`, `isHuman = false` | Re-export via the model's export script (single armature, `add_leaf_bones=False`), reimport, re-check `isHuman` |
| Creature freezes mid-stride when off screen | Bone-parented renderers give the Animator bind-pose bounds | `animator.cullingMode = AnimatorCullingMode.AlwaysAnimate` |
| Death animation never plays | `AgentAnimatorDriver.TriggerDie` fires `"Die"`; `HealthReactionModule.dieAnimTrigger` defaults to `"Death"` | Match the controller. `CloseCombatModule` defaults to `"Meele"` and `AgentRangedCombatModule` to `"AssualtShoot"` — both misspellings are real, and `Golem.controller` carries `Die` *and* `Death` |
| Every NPC swings its gun to follow the host's head | `Weapon.UpdateWeaponRotation` aims at `Camera.main` for the owner, and the server owns every NPC | `EntityEquipmentController` sets `Weapon.ExternallyAimed`; keep `aimHeldItem` on |
| Agent shoots through walls, intermittently | `PerceptionModule.occlusionLayers` left at `Nothing` (it warns and falls back) | Set the mask explicitly on the prefab |
| Remote clients see it slide with still feet | `LeggedLocomotion` was left in `NetAuthority.simulationDrivers` | Remove it — the legs must keep solving against the replicated body |
| A rebuild silently drops hand-added components | `*Builder` scripts in `Assets/Game/Editor/` overwrite the prefab wholesale, with no warning | Put every component in the builder. `GolemBuilder` lost the Golem's `SaveableEntity` exactly this way |
| Loot duplicates every time the world loads | A death reaction ran during a save restore | Check `HealthComponent.IsRestoring` |
| A spawner's group duplicates on load | Its members were also captured by the world save | `SaveableEntity.DisownToExternal()` — see `spacegame-persistence` |
| Vehicle carrying the creature climbs into the sky | Its ground probe hit the non-kinematic rider | Skip hits whose `attachedRigidbody` is non-kinematic; layer masks do not work here (the player is on layer 0) |
| An agent with two `AgentTargeting` components | `[RequireComponent]` already added one before the builder did | Guard with `GetComponent<AgentTargeting>() == null` |

## Cross-references

- `reference.md` (beside this file) — tick order, execution orders, verbatim interface members,
  full module catalog, motors, targeting/faction API, animator contract.
- `Assets/Game/Editor/Creatures/GolemBuilder.cs` — the reference creature builder.
- `Assets/Game/Scripts/agents/Profiles/EntitySystemSetup.cs` — in-repo setup notes
  (documentation only; some of its paths are stale).
- Skills: `blender-model` (mesh/rig), `spacegame-persistence` (save/load),
  `spacegame-multiplayer` (netcode, network prefabs, damage replication),
  `spacegame-artifact` (items an NPC carries and fires).
