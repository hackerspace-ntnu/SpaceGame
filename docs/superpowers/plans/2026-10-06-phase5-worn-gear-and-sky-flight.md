# Phase 5: NPC Worn Gear and Sky Flight Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sky nomads wear a folded wing pack and fly long distances on the same flapping craft the player flies — off the Sky City and between ground sites. They shoot from the cradle, land on NavMesh near their goal and walk on. A pilot shot down drops its body and its pack, and the player can loot that pack and fly it. NPCs also fire opted-in worn gauntlets. Everything is server-decided and identical on host and clients.

**Architecture:** NPC flight is **simple, NPC-only and kinematic-style** (user decision, 2026-10-06). The player's `OrnithopterFlightMotor`, its energy model and `DuneOrnithopter.prefab` are **not touched**.
- **The craft.** A new unsaved `NpcOrnithopter` prefab is a variant of `DuneOrnithopter`, with three swaps:
  - the player's flight motor, mount and savers are removed;
  - the existing `FlyingRigidbodyMotor` (the Sky fleet's motor) flies it, with a new opt-in bank-into-turns option;
  - an `NpcOrnithopterWings` presenter feeds the wing animator and audio from measured motion.
- **The pilot.** `NpcAviator`, a behaviour module on the craft, seats the pilot (`VesselSeats`, one seat) and steers the motor with `MoveIntent`s. The intents come from a pure `NpcFlightPlan`: climb, cruise, approach a `LandingSiteFinder` spot, flare and touch down, or spiral in as a wreck when the pilot dies.
- **The nomad.** `EntityBodyEquipment` (server state, replicated as three item ids) wears the gear, and `NpcFlightModule` decides when to fly.
- **Shared code.** The launch-pose and teardown halves of `WingPackItem` move into a shared `CraftDeployment`.

**Tech Stack:** Unity 6000.3.11f1, URP, Netcode for GameObjects 2.9.1 (embedded), NUnit EditMode tests, Newtonsoft JSON saves.

**Spec:** [docs/superpowers/specs/2026-10-06-phase5-worn-gear-and-sky-flight-design.md](../specs/2026-10-06-phase5-worn-gear-and-sky-flight-design.md).
- Decisions D1–D9 are binding. D5 means no formation flying.
- The spec's "User decision (post-review)" note replaces the energy-model autopilot with this simple NPC motor.
- The evidence it rests on is `.superpowers/phase5-design-refresh.md`. Its §2 autopilot experiments now matter only as background.
- Read the spec in full before starting any task.

**Reuse decision: an existing motor, not a new one.** The NPC craft is flown by **`FlyingRigidbodyMotor`** (`Assets/Game/Scripts/agents/AI/Motors/FlyingRigidbodyMotor.cs`), not a new motor class.
- **Why it fits.** It is already the AI-driven 3D motor: `Tick(MoveIntent)` flies to a 3D point at `maxSpeed` with acceleration ramps. It faces its travel and is network-proven on the Sky fleet. Its header names gliders and drones as intended users, and on a dynamic body it gets real collisions, so a crash into a cliff is still detected.
- **What it lacks.** Only banking into turns and pitching along a climb are missing. Both are added as opt-in serialized fields with defaults off, and the default code path stays byte-identical, so the city and escorts are unaffected (Task 2).
- **Why not more.** Landing, wreck and the flight phases do not belong in a generic motor. They live in the pure `NpcFlightPlan`, which outputs ordinary `MoveIntent`s.
- **Why not a new motor.** That would duplicate the fleet motor's ramps, facing and rider-free AI path (CLAUDE.md: reuse what exists).
- **Why not `OrnithopterFlightMotor`.** It is the player's energy model and must stay untouched.

## Global Constraints

- **Never enter or stop Play Mode.** Never compile or run tests while the editor is playing (`rt.py` waits for that itself). Never run an unfiltered test run, because package tests enter Play Mode. Always pass a test-class regex.
- **Commands.** Run them from the paths shown. `SCRATCH` = `C:/Users/tobia/AppData/Local/Temp/claude/C--Users-tobia-Documents-spaceGame-SpaceGame/ab2a1a7b-473e-4041-a764-18ee3b609e3d/scratchpad`.
  - EditMode tests: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py rt.py '<TestClassRegex>'`
  - C# in the editor: write the snippet to `$SCRATCH/<name>.cs` as a method body that `return`s a string, then run `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py ux.py < <name>.cs`
  - Offline compile (red step, no editor): `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/typecheck.py --editor`
  - Docs: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/docs_check.py --index`
  - After adding a new `.cs` file, Unity may need `UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(); return "ok";` through `ux.py` before `rt.py` sees the new type.
- **The editor is shared** with other agents and a human. Never leave C# that does not compile on disk.
  - The "see it fail" step of every task is `typecheck.py --editor` reporting the missing type or member (the red). It is never a Unity refresh.
  - Keep the red window short. Write the test, typecheck, then write the implementation straight away.
  - Refresh Unity (through `rt.py`) only once `typecheck.py --editor` exits 0.
  - If typecheck fails in a file your task does not own, another agent is mid-task. Wait and re-run; never edit their file.
- **Prefab builds.** The editor ran out of memory baking several prefabs in one call.
  - Rebuild **one prefab per `ux.py` call**.
  - Builds and wiring passes may re-serialize unrelated prefabs. Before a build, save `git status --porcelain > "$SCRATCH/p5_status_before.txt"`. After it, restore with `git checkout -- <file>` **only** files that were clean before and that your task does not list. Never restore a file that was already modified before you started.
- **Git.** Make **no commits and no staging**; the user commits. Every task ends by reporting the exact files it changed or created, including `.meta` files of new assets and the regenerated `docs/AI/INDEX.md` / `ROUTING.md`.
  - Another session has uncommitted files: the Blender library, the satellite tower `Assets/Game/Art/...SatelliteTower...`, `_backups~`, and `docs/superpowers/specs/2026-10-06-phase5-…`. Never touch them.
  - Path casing: `Assets/Game/Scripts/agents/` is tracked as `Agents/` for some files. Use the path `git ls-files` prints when reporting.
- **Agent hygiene.** No Blender. No profiler. Keep tool calls short (agents have stalled on long calls); `rt.py` with a narrow regex is fast.
- **Docs are part of each task.** Each task names its governing doc(s). For each one:
  - Update the affected rows and delete what the change made untrue.
  - Add to `## Gotchas` anything non-obvious.
  - Add a `symptoms:` frontmatter entry for anything that cost real time, phrased as what you *saw*.
  - Bump `updated: 2026-10-06` (or the day you work).
  - Run `docs_check.py --index`.
  - Docs have a 150-body-line cap. Replace, don't append, where a row already says it.
  - No new system doc is created, so no new `docs/Human/the-systems.md` entry is required. Task 10 updates the Sky Tribe entry because the tribe's shape changes.
- **No code smells** (CLAUDE.md): no dead code, no debug logs, no magic numbers (serialize tunables or name constants), no silent `catch`, no copy-paste (extract instead).
- **Multiplayer (spec "Multiplayer", answered once).**
  - The server decides wearing, deploy, the flight (`NpcAviator` is a module, and `AgentController` ticks modules only where it simulates), landing, death and crash. Each check uses `Network.Simulates(this)`.
  - Worn gear replicates as three server-written `NetworkVariable<FixedString64Bytes>` item ids, and every machine seats the visual from them, late joiners included.
  - The NPC craft is a registered network prefab, spawned server-owned. The seated pilot is netcode-parented through `NpcSeating.Attach` (inside `VesselSeats`).
  - On clients `NetAuthority` switches the craft's `FlyingRigidbodyMotor` off and the replicated transform moves it. `NpcOrnithopterWings` derives the beating wings on every machine from that motion, so there is no launch message and no late-join gap. The pack hides on every machine from the replicated parenting.
  - **No new `NetMsg`.** Worn gauntlet presentation reuses `NetMsg.ItemUsed` with a body `UseSlotCode`.
- **Persistence (spec "Persistence", answered once).**
  - `npcWorn` (`EntityBodyEquipmentSaveable`) saves worn item ids per NPC.
  - Per D4, a flying nomad and its craft are not saved. The craft is excluded by `SaveablePolicy.NeedsSaving` returning false for any root with `NpcAviator`. A world-scope pilot is disowned for the flight (`SaveScopeHold`), and a group member is already External.
  - After a load the group record (ground-projected, Task 9) brings members back on foot.
  - Nothing else new is saved.
- **The player's flight is off limits** (user decision 2026-10-06): never edit `OrnithopterFlightMotor.cs`, `OrnithopterFlightMotor.Replication.cs`, the flight model/state/config/crash files, or `DuneOrnithopter.prefab`.
- **Known pre-existing failure, not this work:** `NetworkPrefabRegistrationTests.EveryRiderDrivenMount_SurvivesItsRiderDisconnecting` (Dunehorn). Ignore it when it is the only failure in that class.
- **EditMode facts every test author needs:**
  - EditMode `AddComponent` runs no `Awake`/`OnEnable`. Invoke them by reflection, as the `Wake` helper in `RemainsTests` does.
  - `Time.time` and `Time.frameCount` do not advance inside a test.
  - With no `NetworkManager`, `Network.Simulates(x)` is true and sends dispatch locally.
  - `Physics.Simulate` / `PhysicsScene.Simulate` need `Physics.simulationMode = SimulationMode.Script`. Set it and restore it, as `SkyFleetDriftTests` does.
  - Put physics fixtures far from the open scene (around `(150000, 6000, 150000)`) and call `Physics.SyncTransforms()` after moving colliders (`autoSyncTransforms` is off).

## Review Focus

1. **A pilot killed in the air must leave exactly one body and one pack**, on host and client. No second copy may appear when the save restores the corpse at zero health. Pinned by:
   - `NpcAviatorTests.ThePilotDying_WrecksTheCraft_AndDropsTheBodyStraightDown` (Task 4)
   - `EntityBodyEquipmentPersistenceTests.ADeadWearer_DropsItsPackOnce_AndARestoredCorpseDropsNothing` (Task 6)
   - `NpcFlightModuleTests.APilotKilledInFlight_IsSavedAgainAsACorpse` (Task 8)
2. **The group folds or the pilot is despawned mid-flight** (the player walks away). The craft must not hover on empty forever, and nothing may be left with `RidesAsPassenger` stuck. Pinned by `NpcAviatorTests.ACraftWhosePilotWasTakenAway_RetiresItself` (Task 4).
3. **A save made mid-flight** must hold neither the craft nor a world-scope pilot. Landing must never move a group member (External) into the world save, or it would load twice. Pinned by `NpcAviatorTests.TheCraft_IsNeverSaved` (Task 4) and `SaveScopeHoldTests.AGroupMember_IsNeverReclaimedIntoTheWorldSave` (Task 8).
4. **The player's own flight and the Sky fleet are unchanged.**
   - `OrnithopterFlightMotor*.cs`, the flight model and `DuneOrnithopter.prefab` must show an empty `git diff` (Task 10, Step 3).
   - `FlyingRigidbodyMotor`'s defaults keep the old rotation path: `FlyingRigidbodyMotorAttitudeTests.WithAttitudeOff_TheMotorNeverRollsOrPitches` (Task 2), plus the unchanged `SkyFleetDriftTests` / `SkyFleetPrefabTests`.
5. **No landing site, or ground not streamed in yet, or a goal on a cliff.** The craft still flies to the goal itself and sets down there, rather than hovering for ever. Pinned by `NpcAviatorTests.WithNoSiteFound_TheCraftStillFliesToTheGoal` (Task 4) and `NpcFlightPlanTests.ArrivingTooHigh_SpiralsDown_AndLandsGently` / `TerrainRisingUnderTheRoute_IsClimbedOver` (Task 1).

## Spec assumptions that are false in the code (and what this plan does)

| Spec / refresh says | Code says | Plan |
| --- | --- | --- |
| "An autopilot … driven through an AI channel on `OrnithopterFlightMotor`" | Overruled by the user after review: the player's motor stays untouched, and NPCs get a simple motor that "just flies" | The `NpcOrnithopter` variant swaps `OrnithopterFlightMotor` for the existing `FlyingRigidbodyMotor`, driven by `NpcAviator` + the pure `NpcFlightPlan`. There is no stall, glide ratio or stamina, and no AI channel on the player's motor (spec note added) |
| "Same model, motor, wing animator and audio" | `OrnithopterWingAnimator` / `OrnithopterAudio` bind `GetComponent<IOrnithopterFlightState>()` in `Awake`, and only the player's motor implements it | New `NpcOrnithopterWings : IOrnithopterFlightState` measures the craft's motion on every machine (Task 2). No player code path changes |
| An NPC craft prefab with no `SaveableEntity` is unsaved (D4) | `WorldService.Spawn` → `SaveablePolicy.EnsureSpawned` adds `SaveableEntity` + savers at runtime to anything with an `AgentController` (`IPersistentEntity`) or a non-kinematic `Rigidbody`. The craft has both | `NeedsSaving` returns false for a root carrying `NpcAviator`, exactly as it does for `VesselPilot` (Task 4) |
| A "builder-made `NpcOrnithopter` variant"; Ornithopter.md lists `OrnithopterBuilder` / `WingPackBuilder` | Neither builder exists. `DuneOrnithopter.prefab` and `WingPack.prefab` are hand-owned | New `NpcOrnithopterBuilder` makes a **Prefab Variant** (Task 4). `WingPack.prefab` is patched surgically (Task 9). The stale doc rows are deleted (Task 10) |
| "Seat via `NpcSeating` (one seat, no dismount on damage)" — a new seat component | `VesselSeats` is exactly that: it never dismounts on damage, a death detaches and restores (the corpse drops straight down), and presentation is per machine | Reuse `VesselSeats` with one marker (`SEAT_Cradle`) |
| Extract the `WingPackItem` spawn flow | `WingPackItem.LaunchPosition` reads the seat off `MountModule`, which the NPC craft does not have | `CraftDeployment.LaunchPosition(Transform root, Vector3 seatPoint, …)`. The pack forwards with its mount's seat; the nomad passes `VesselSeats.SeatPose(0)` (Task 3) |
| D3: the folded model on the back with an offset | `WornSeat.Apply(..., Form.Carried)` sizes to `WornFit.size` (2.64 m, the stowed wings) at the rail-fallback offset | `WornFit` gains `foldedSize` / `foldedLocalPosition` / `foldedLocalEuler`, plus `WornSeat.ApplyWithoutRig` (Task 5). The values are written onto `WingPack.prefab` (Task 9) |
| D8: "the craft falls as a wreck (existing crash detection)" | The craft has no wreck behaviour, and with the player's motor removed it has no touchdown detection either | `NpcFlightPlan.Wreck` spirals the craft down near the death point. `NpcAviator` ends it on reaching the ground or on any collision (`OnCollisionEnter`, using the dynamic body). The body (via `VesselSeats`) and the pack (via `EntityLootTable`) drop where the pilot died (Tasks 1, 4) |
| D2: a "Fly trip between sites" | No "Fly" task and no goal-setter exist on Sky nomads: no `NpcTaskModule`, `GoalTravelModule` or `FormationModule` | `NpcFlightModule` flies **any** far `AgentGoal`: its own, or for a follower its formation leader's (D5: alone, to the same goal). Content (Task 9): a seeded `sky-wing` NpcWorldSim group for ground-to-ground trips, and city residents occasionally fly a **sortie** off the moored Sky City to a ground site |
| D4: "its group record brings members back on foot" | The record's position is the members' centroid (`NpcWorldSim.CurrentPosition`), which is mid-air while they fly. A fold or a load would respawn them in the air | The record's position is ground-projected when it is off the NavMesh (Task 9) |
| D6/D9: NPCs fire worn gauntlets "the way `NpcItemUseModule` fires the hand item" | Gauntlets aim through `aimProvider` (player-only) and fall back to the item's own `transform.forward`, so an NPC's gauntlet would fire along its dangling forearm | New `INpcAim` + `UsableItem.HolderAimRay()`. The Repulsor, the one opted-in gauntlet, aims through it (Task 7) |
| `DisownToExternal` excludes a flier from the save | It is play-mode only: in EditMode it logs an error and does nothing | `SaveScopeHold` wraps it. The tests expect that log and assert the External/World bookkeeping. The JSON check is in the human checklist (Task 10) |

---

## File Structure

**Runtime, Ornithopter asmdef (`SpaceGame.Vehicles.Ornithopter`)**
- Create `Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/NpcFlightPlan.cs`: settings, phases and the pure steering (climb, cruise, approach, spiral, flare, wreck).
- Create `Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/NpcWings.cs`: the pure wing state derived from measured motion.
- Create `Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/NpcOrnithopterWings.cs`: a MonoBehaviour implementing `IOrnithopterFlightState` that feeds the wing animator and audio.
- Modify `Assets/Game/Scripts/Vehicles/Ornithopter/Flight/FlightLaunch.cs`: add `IsAirborne` / `HasDropAhead` / `HasLaunchRoom` (moved out of `WingPackItem`).

**Runtime, Assembly-CSharp**
- Modify `Assets/Game/Scripts/agents/AI/Motors/FlyingRigidbodyMotor.cs`: opt-in `bankPerTurnRate` / `maxBank` / `pitchAlongPath`.
- Create `Assets/Game/Scripts/Items/Equipped/CraftDeployment.cs`: the launch pose plus retiring a craft (shared).
- Modify `Assets/Game/Scripts/Items/Equipped/WingPackItem.cs`: forwards to `CraftDeployment` and `FlightLaunch`.
- Create `Assets/Game/Scripts/Vehicles/NpcAviation/NpcAviator.cs`: the behaviour module on the craft that flies one NPC.
- Modify `Assets/Game/Scripts/Core/Persistence/Runtime/SaveablePolicy.cs`:
  - T4: `NpcAviator` is never saved;
  - T6: the `npcWorn` clause;
  - T7: the item-use base type.
- Create `Assets/Game/Scripts/Items/Body/WornBones.cs` (bone-name hints shared by the player and NPC body controllers) and `Assets/Game/Scripts/Items/Body/IStowsTorsoGear.cs`.
- Create `Assets/Game/Scripts/Items/Core/INpcAim.cs`.
- Modify `Assets/Game/Scripts/Items/Body/BodyEquipmentController.cs`: hints from `WornBones`.
- Modify `Assets/Game/Scripts/Items/Equipped/WornFit.cs` and `WornSeat.cs`: the folded pose and `ApplyWithoutRig`.
- Create `Assets/Game/Scripts/agents/entity/EntityBodyEquipment.cs` and `Assets/Game/Scripts/Core/Persistence/Adapters/EntityBodyEquipmentSaveable.cs`.
- Modify `Assets/Game/Scripts/agents/entity/EntityLootTable.cs`: drops worn gear.
- Modify `Assets/Game/Scripts/Items/Core/InventoryItem.cs` (`npcUsable`) and `Assets/Game/Scripts/Items/Core/UsableItem.cs` (`HolderAimRay`).
- Create `Assets/Game/Scripts/agents/entity/NpcItemFire.cs`, `INpcItemUser.cs` and `WornGauntletUser.cs`.
- Create `Assets/Game/Scripts/agents/Modules/Combat/ItemUseModuleBase.cs`, and modify `NpcItemUseModule.cs`.
- Create `Assets/Game/Scripts/agents/Modules/Combat/NpcGauntletUseModule.cs`.
- Modify `Assets/Game/Scripts/agents/AI/Targeting/AgentTargeting.cs` and `Assets/Game/Scripts/Core/Persistence/Adapters/CombatCadenceSaveable.cs`.
- Modify `Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorGauntletArtifact.cs`.
- Create `Assets/Game/Scripts/Core/Persistence/Runtime/SaveScopeHold.cs`.
- Create `Assets/Game/Scripts/agents/Modules/Movement/NpcFlightModule.cs`.
- Modify `Assets/Game/Scripts/agents/World/NpcWorldSim.cs`: the ground-projected record position.

**Not modified, guarded instead:** `OrnithopterFlightMotor.cs`, `OrnithopterFlightMotor.Replication.cs`, everything under `Vehicles/Ornithopter/Flight/` except `FlightLaunch.cs`, and `DuneOrnithopter.prefab`.

**Editor**
- Create `Assets/Game/Editor/Vehicles/NpcOrnithopterBuilder.cs`.
- Modify `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs` and `Assets/Game/Editor/Agents/RosterAuthoring.cs`.

**Assets**
- Create `Assets/Game/Prefabs/Agents/Vehicles/Aircraft/NpcOrnithopter.prefab` (+ .meta).
- Modify `DefaultNetworkPrefabs.asset`, `WingPack.prefab` (the folded fit), `RepulsorGauntlet.asset` (`npcUsable`), `SkyNomad_{Umber,Tan,Maroon,StrawHat}.prefab` and `persistentScene.unity` (the `sky-wing` template).

**Tests**
- `Assets/Game/Tests/EditMode/NpcFlightPlanTests.cs` and `NpcWingsTests.cs`.
- `Assets/Game/Editor/Tests/`: `FlyingRigidbodyMotorAttitudeTests.cs`, `CraftDeploymentTests.cs`, `NpcAviatorTests.cs`, `NpcOrnithopterPrefabTests.cs`, `EntityBodyEquipmentTests.cs`, `EntityBodyEquipmentPersistenceTests.cs`, `NpcGauntletUseTests.cs`, `NpcFlightModuleTests.cs` (includes `SaveScopeHoldTests`) and `SkyFlightContentTests.cs`.
- Helpers, each in its own file so `Instantiate` keeps the script: `TestGauntletItem.cs`, `TestTorsoStower.cs`, `InstantiatingWorld.cs`.

## Dependencies and parallelism

| Task | Depends on | Can run alongside |
| --- | --- | --- |
| 1 `NpcFlightPlan` (pure) — **riskiest: does it land near the goal** | — | 2, 3, 5 |
| 2 Craft presentation: `FlyingRigidbodyMotor` attitude + `NpcOrnithopterWings` | — | 1, 3, 5 |
| 3 `CraftDeployment` + `FlightLaunch` extraction | — | 1, 2, 5 |
| 4 `NpcAviator` + `NpcOrnithopter` prefab (**riskiest-slice exit**) | 1, 2, 3 | 5, 6, 7 |
| 5 `EntityBodyEquipment` | — | 1–4 |
| 6 `npcWorn` saver + loot | 5 | 4, 7 |
| 7 NPC gauntlets | 5 | 4, 6 |
| 8 `NpcFlightModule` | 4, 5 | — |
| 9 Sky content (recipes, prefabs, sky-wing, record grounding) | 6, 7, 8 | — |
| 10 Docs consolidation + human checklist | 9 | — |

Parallel agents share one editor. Test runs serialize themselves in `rt.py`. Edits to the same doc must use targeted `Edit`s, never whole-file rewrites: Ornithopter.md is touched in Tasks 1, 2 and 4, BodyEquipment.md in 5–7, and Vehicles.md in 2 and 4. If `docs_check.py --index` races another agent, re-run it.

---

### Task 1: `NpcFlightPlan` — the pure NPC flight steering (riskiest assumption first)

The risk this task retires: can a simple planner reliably bring a craft down **within 15 m of a goal**, gently, from a ground take-off, from the Sky City's 280 m, and when it arrives too high? The planner knows nothing about the energy model. It outputs a 3D point to fly at and a speed fraction, which become a `MoveIntent` for `FlyingRigidbodyMotor` (Task 4). The tests fly it against a stand-in for that motor: velocity ramps toward `target × MaxSpeed × Speed` at `Acceleration`, using the values Task 4 writes onto the prefab.

**Files:**
- Create: `Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/NpcFlightPlan.cs`
- Test: `Assets/Game/Tests/EditMode/NpcFlightPlanTests.cs`
- Docs: `docs/AI/systems/Ornithopter.md`

**Interfaces:**
- Produces (namespace `SpaceGame.Vehicles.Ornithopter`):
  - `enum NpcFlightPhase { Idle, EnRoute, Approach, Landed, Wreck }`
  - `[Serializable] class NpcFlightSettings` (the public fields below)
  - `readonly struct NpcFlightStep { Vector3 Target; float Speed; bool Touchdown; }`
  - `sealed class NpcFlightPlan` with:
    - `NpcFlightPlan(NpcFlightSettings)`
    - `NpcFlightPhase Phase`
    - `void Begin()`
    - `void Wreck(Vector3 at)`
    - `NpcFlightStep Step(Vector3 position, float groundBelow, Vector3 landing, float cruiseHeight)`

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Game/Tests/EditMode/NpcFlightPlanTests.cs
using System;
using NUnit.Framework;
using SpaceGame.Vehicles.Ornithopter;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The NPC craft's flight plan, flown against a stand-in for FlyingRigidbodyMotor's AI path: velocity
    /// ramps toward (target - position).normalized * MaxSpeed * Speed at Acceleration. MaxSpeed and
    /// Acceleration are the values NpcOrnithopterBuilder writes onto the prefab (Task 4). Behaviour tests:
    /// what a watching player has to see. A retune of NpcFlightSettings must keep them passing.
    /// </summary>
    public class NpcFlightPlanTests
    {
        private const float Dt = 1f / 50f;
        private const float MaxSpeed = 25f;        // NpcOrnithopterBuilder.CruiseSpeed
        private const float Acceleration = 8f;     // NpcOrnithopterBuilder.Acceleration
        private const float Cruise = 60f;
        private const float LandingTolerance = 15f;   // spec: "land within ~15 m of the goal"
        private static readonly float SafeClosing = new OrnithopterCrashConfig().SafeClosingSpeed;

        private struct Flight
        {
            public bool Landed, HitTheGround;
            public Vector3 Touchdown;
            public float SinkAtTouchdown, Highest, LowestCruiseClearance, Seconds;
        }

        private static NpcFlightPlan Plan()
        {
            var plan = new NpcFlightPlan(new NpcFlightSettings());
            plan.Begin();
            return plan;
        }

        private static Flight Fly(NpcFlightPlan plan, Vector3 position, Vector3 velocity, Vector3 landing,
                                  Func<float, float, float> ground, float seconds)
        {
            var f = new Flight { Highest = position.y, LowestCruiseClearance = float.MaxValue };
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                float g = ground(position.x, position.z);
                NpcFlightStep step = plan.Step(position, g, landing, Cruise);
                if (step.Touchdown)
                {
                    f.Landed = true;
                    f.Touchdown = position;
                    f.SinkAtTouchdown = Mathf.Max(0f, -velocity.y);
                    f.Seconds = i * Dt;
                    return f;
                }

                Vector3 toTarget = step.Target - position;
                Vector3 desired = toTarget.sqrMagnitude > 0.04f ? toTarget.normalized * MaxSpeed * step.Speed : Vector3.zero;
                velocity = Vector3.MoveTowards(velocity, desired, Acceleration * Dt);
                position += velocity * Dt;

                f.Highest = Mathf.Max(f.Highest, position.y);
                if (plan.Phase == NpcFlightPhase.EnRoute && i * Dt > 20f)
                    f.LowestCruiseClearance = Mathf.Min(f.LowestCruiseClearance, position.y - g);
                if (position.y < g)
                {
                    f.HitTheGround = true;
                    return f;
                }
            }
            return f;
        }

        private static float Flat0(float x, float z) => 0f;
        private static float Miss(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        [Test]
        public void TakeOff_ClimbsToCruise_ThenLandsNearTheGoal()
        {
            var goal = new Vector3(600f, 0f, 0f);
            Flight f = Fly(Plan(), new Vector3(0f, 3f, 0f), Vector3.zero, goal, Flat0, 300f);

            Assert.IsFalse(f.HitTheGround, "flew into the ground");
            Assert.Greater(f.Highest, Cruise - 6f, "never climbed to cruise height");
            Assert.IsTrue(f.Landed, "never touched down");
            Assert.Less(Miss(f.Touchdown, goal), LandingTolerance, $"touched down {Miss(f.Touchdown, goal):F1} m from the goal");
            Assert.Less(f.SinkAtTouchdown, SafeClosing, $"came down at {f.SinkAtTouchdown:F1} m/s");
        }

        [Test]
        public void OffTheSkyCity_LandsNearAGoalTwoKilometresAway()
        {
            var goal = new Vector3(0f, 0f, 2000f);
            Flight f = Fly(Plan(), new Vector3(0f, 280f, 0f), new Vector3(0f, 0f, 14f), goal, Flat0, 400f);

            Assert.IsFalse(f.HitTheGround);
            Assert.IsTrue(f.Landed, "never touched down");
            Assert.Less(Miss(f.Touchdown, goal), LandingTolerance, $"touched down {Miss(f.Touchdown, goal):F1} m away");
            Assert.Less(f.SinkAtTouchdown, SafeClosing);
        }

        [Test]
        public void ArrivingTooHigh_SpiralsDown_AndLandsGently()
        {
            var goal = new Vector3(40f, 0f, 0f);
            Flight f = Fly(Plan(), new Vector3(0f, 120f, 0f), Vector3.zero, goal, Flat0, 300f);

            Assert.IsFalse(f.HitTheGround, "dived into the ground instead of spiralling down");
            Assert.IsTrue(f.Landed);
            Assert.Less(Miss(f.Touchdown, goal), LandingTolerance, $"touched down {Miss(f.Touchdown, goal):F1} m away");
            Assert.Less(f.SinkAtTouchdown, SafeClosing);
        }

        [Test]
        public void TerrainRisingUnderTheRoute_IsClimbedOver()
        {
            float Ridge(float x, float z) => Mathf.Clamp((x - 200f) * 0.15f, 0f, 150f);
            var goal = new Vector3(1500f, 150f, 0f);
            Flight f = Fly(Plan(), new Vector3(0f, 3f, 0f), Vector3.zero, goal, Ridge, 400f);

            Assert.IsFalse(f.HitTheGround, "flew into the rising ground");
            Assert.Greater(f.LowestCruiseClearance, 20f, $"cruised {f.LowestCruiseClearance:F1} m over the ground");
            Assert.IsTrue(f.Landed);
            Assert.Less(Miss(f.Touchdown, goal), LandingTolerance);
        }

        [Test]
        public void AWreck_SpiralsDownNearWhereItWasHit()
        {
            NpcFlightPlan plan = Plan();
            var hit = new Vector3(0f, 60f, 0f);
            plan.Wreck(hit);
            Flight f = Fly(plan, hit, new Vector3(25f, 0f, 0f), new Vector3(3000f, 0f, 0f), Flat0, 60f);

            Assert.IsTrue(f.Landed || f.HitTheGround, "the wreck never came down");
            Vector3 end = f.Landed ? f.Touchdown : hit;
            Assert.Less(Miss(end, hit), 3f * new NpcFlightSettings().SpiralRadius,
                        "a wreck flew off instead of coming down where the pilot died");
            Assert.Less(f.Seconds, 20f, "a wreck took too long to come down");
        }

        [Test]
        public void Begin_RestartsALandedOrWreckedPlan()
        {
            NpcFlightPlan plan = Plan();
            plan.Wreck(Vector3.zero);
            plan.Begin();
            Assert.AreEqual(NpcFlightPhase.EnRoute, plan.Phase);
        }
    }
}
```

- [ ] **Step 2: Run the typecheck to see it fail**

Run `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/typecheck.py --editor`.
Expected: `NpcFlightPlan`, `NpcFlightSettings`, `NpcFlightStep` and `NpcFlightPhase` are not found.

- [ ] **Step 3: Write the implementation**

```csharp
// Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/NpcFlightPlan.cs
// Where an NPC-flown ornithopter should be heading next: a point to fly at and how fast. Pure — no
// Transform, no clock, no physics — and deliberately NOT the energy model the player flies (user
// decision 2026-10-06: an NPC "just flies"). NpcAviator turns each step into a MoveIntent for
// FlyingRigidbodyMotor; NpcFlightPlanTests fly it against a stand-in for that motor.
//
// En route the craft climbs (or sinks) toward cruiseHeight over the ground under it, at most ClimbSlope
// up or DescentSlope down, aiming LookAhead metres along the line to the landing. When the landing is
// within FlareDistance, or the descent to it has become ApproachSlope, it approaches: straight at the
// touchdown point, slowing to FlareSpeed inside FlareDistance. If that line would be steeper than
// MaxApproachSlope it spirals down around the landing first. A wreck spirals down at WreckSlope around
// the point where its pilot died.
using System;
using UnityEngine;

namespace SpaceGame.Vehicles.Ornithopter
{
    public enum NpcFlightPhase { Idle, EnRoute, Approach, Landed, Wreck }

    [Serializable]
    public class NpcFlightSettings
    {
        [Tooltip("How far along the route the craft aims while en route, metres.")]
        [Min(1f)] public float LookAhead = 80f;

        [Tooltip("Steepest climb en route, degrees.")]
        [Range(1f, 60f)] public float ClimbSlope = 25f;

        [Tooltip("Steepest descent en route, degrees — off the Sky City it sinks to cruise height at this.")]
        [Range(1f, 60f)] public float DescentSlope = 15f;

        [Tooltip("Descent angle to the landing at which the approach begins, degrees.")]
        [Range(1f, 45f)] public float ApproachSlope = 12f;

        [Tooltip("Steepest straight approach, degrees. Steeper than this, it spirals down first.")]
        [Range(5f, 70f)] public float MaxApproachSlope = 35f;

        [Tooltip("Inside this distance of the touchdown point it slows to FlareSpeed, metres.")]
        [Min(1f)] public float FlareDistance = 45f;

        [Tooltip("Speed through the flare, 0..1 of cruise speed.")]
        [Range(0.05f, 1f)] public float FlareSpeed = 0.25f;

        [Tooltip("Speed while spiralling down to a landing, 0..1 of cruise speed.")]
        [Range(0.05f, 1f)] public float SpiralSpeed = 0.4f;

        [Tooltip("Radius of a spiral descent (a landing arrived at too high, and a wreck), metres.")]
        [Min(1f)] public float SpiralRadius = 25f;

        [Tooltip("How far round the spiral the craft aims ahead of where it is, degrees.")]
        [Range(5f, 90f)] public float SpiralLead = 40f;

        [Tooltip("Height of the craft's origin (the cradle) above the ground when it is down, metres — " +
                 "the player's craft's landing probe distance.")]
        [Min(0.1f)] public float TouchdownHeight = 1.4f;

        [Tooltip("Slack on TouchdownHeight before it counts as down, metres.")]
        [Min(0.05f)] public float TouchdownTolerance = 0.6f;

        [Tooltip("Flat distance from the landing within which reaching touchdown height counts as landed, metres.")]
        [Min(0.5f)] public float LandingTolerance = 5f;

        [Tooltip("How steeply a wreck spirals in, degrees.")]
        [Range(5f, 85f)] public float WreckSlope = 40f;
    }

    public readonly struct NpcFlightStep
    {
        public readonly Vector3 Target;
        public readonly float Speed;
        public readonly bool Touchdown;

        public NpcFlightStep(Vector3 target, float speed, bool touchdown)
        {
            Target = target;
            Speed = speed;
            Touchdown = touchdown;
        }
    }

    public sealed class NpcFlightPlan
    {
        // Below this a flat vector names no bearing.
        private const float MinFlat = 0.01f;

        private readonly NpcFlightSettings s;
        private Vector3 wreckCentre;

        public NpcFlightPlan(NpcFlightSettings settings)
        {
            s = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public NpcFlightPhase Phase { get; private set; }

        public void Begin() => Phase = NpcFlightPhase.EnRoute;

        /// <summary>Nobody is flying it any more: spiral in around <paramref name="at"/>.</summary>
        public void Wreck(Vector3 at)
        {
            wreckCentre = at;
            Phase = NpcFlightPhase.Wreck;
        }

        /// <param name="groundBelow">World height of the ground straight under the craft.</param>
        /// <param name="landing">Where to set down, on the ground.</param>
        public NpcFlightStep Step(Vector3 position, float groundBelow, Vector3 landing, float cruiseHeight)
        {
            if (Phase == NpcFlightPhase.Idle || Phase == NpcFlightPhase.Landed)
                return new NpcFlightStep(position, 0f, false);

            float aboveGround = position.y - groundBelow;
            bool down = aboveGround <= s.TouchdownHeight + s.TouchdownTolerance;

            if (Phase == NpcFlightPhase.Wreck)
                return down ? Land(position) : new NpcFlightStep(Spiral(wreckCentre, position, s.WreckSlope), 1f, false);

            Vector3 touchdown = landing + Vector3.up * s.TouchdownHeight;
            Vector3 toLanding = touchdown - position;
            toLanding.y = 0f;
            float flat = toLanding.magnitude;
            float above = position.y - touchdown.y;

            if (Phase == NpcFlightPhase.EnRoute &&
                (flat <= s.FlareDistance || (above > 0f && flat <= above / Tan(s.ApproachSlope))))
                Phase = NpcFlightPhase.Approach;

            if (Phase == NpcFlightPhase.EnRoute)
            {
                Vector3 ahead = flat > s.LookAhead
                    ? position + toLanding / flat * s.LookAhead
                    : new Vector3(touchdown.x, position.y, touchdown.z);
                float rise = groundBelow + cruiseHeight - position.y;
                ahead.y = position.y + Mathf.Clamp(rise, -Tan(s.DescentSlope) * s.LookAhead, Tan(s.ClimbSlope) * s.LookAhead);
                return new NpcFlightStep(ahead, 1f, false);
            }

            if (down || (above <= s.TouchdownTolerance && flat <= s.LandingTolerance))
                return Land(position);

            float slope = Mathf.Atan2(above, Mathf.Max(flat, MinFlat)) * Mathf.Rad2Deg;
            if (above > 0f && slope > s.MaxApproachSlope)
                return new NpcFlightStep(Spiral(touchdown, position, s.MaxApproachSlope), s.SpiralSpeed, false);

            float distance = Vector3.Distance(position, touchdown);
            return new NpcFlightStep(touchdown, distance <= s.FlareDistance ? s.FlareSpeed : 1f, false);
        }

        private NpcFlightStep Land(Vector3 position)
        {
            Phase = NpcFlightPhase.Landed;
            return new NpcFlightStep(position, 0f, true);
        }

        /// <summary>The next point round a circle of SpiralRadius about <paramref name="centre"/>, sunk by <paramref name="slopeDegrees"/>.</summary>
        private Vector3 Spiral(Vector3 centre, Vector3 position, float slopeDegrees)
        {
            Vector3 fromCentre = position - centre;
            fromCentre.y = 0f;
            float bearing = fromCentre.sqrMagnitude > MinFlat * MinFlat
                ? Mathf.Atan2(fromCentre.x, fromCentre.z) * Mathf.Rad2Deg
                : 0f;
            float next = (bearing + s.SpiralLead) * Mathf.Deg2Rad;

            Vector3 point = centre + new Vector3(Mathf.Sin(next), 0f, Mathf.Cos(next)) * s.SpiralRadius;
            Vector3 chord = point - position;
            chord.y = 0f;
            point.y = position.y - Tan(slopeDegrees) * chord.magnitude;
            return point;
        }

        private static float Tan(float degrees) => Mathf.Tan(degrees * Mathf.Deg2Rad);
    }
}
```

- [ ] **Step 4: Typecheck, then run the tests**

Run `py tools/typecheck.py --editor`, which must exit 0. Then run `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py rt.py 'NpcFlightPlanTests'`.

Expected: all pass. If a test fails, tune **only the defaults in `NpcFlightSettings`** and never edit a test. Typical levers:
- `FlareDistance` / `FlareSpeed` for the sink rate at touchdown;
- `SpiralRadius` / `SpiralSpeed` for the too-high and wreck cases (the stand-in turns at most `Acceleration`, so a tight fast spiral drifts outward);
- `LookAhead` / `ClimbSlope` for the ridge.

- [ ] **Step 5: Docs**

In `docs/AI/systems/Ornithopter.md`:
- Add `Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/` to `paths:`.
- Add a Key types row for `NpcFlightPlan` / `NpcFlightSettings`: the pure NPC flight steering. It is not the energy model. Its phases are EnRoute (climb or sink to cruise over the ground), Approach (straight, flare, or spiral when too steep) and Wreck (a spiral at `WreckSlope`). It outputs a point plus a speed fraction for `FlyingRigidbodyMotor`.
- Model: add one sentence, "NPC craft do not fly the energy model — user decision 2026-10-06; the player's motor is untouched by NPC flight".
- Bump `updated:` and run `py tools/docs_check.py --index`.

- [ ] **Step 6: Report the exact files changed**

Report:
- `NpcFlightPlan.cs` (+ `.meta`, and the new `NpcFlight` folder `.meta`)
- `NpcFlightPlanTests.cs` (+ `.meta`)
- `Ornithopter.md`, `INDEX.md`, `ROUTING.md`
- any tuned defaults, and the test results

---

### Task 2: Craft presentation — `FlyingRigidbodyMotor` banks into turns (opt-in), and `NpcOrnithopterWings` beats the wings

**Files:**
- Modify: `Assets/Game/Scripts/agents/AI/Motors/FlyingRigidbodyMotor.cs`
  - Add the fields beside `faceRotateSpeed` (`:36-39`).
  - Change `FaceDirection` (`:459-476`, the non-kinematic branch).
- Create: `Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/NpcWings.cs`
- Create: `Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/NpcOrnithopterWings.cs`
- Test: `Assets/Game/Editor/Tests/FlyingRigidbodyMotorAttitudeTests.cs`, `Assets/Game/Tests/EditMode/NpcWingsTests.cs`. Regression: `SkyFleetDriftTests`, `SkyFleetPrefabTests`, `OrnithopterWingAnimatorTests`.
- Docs: `docs/AI/systems/Vehicles.md` (FlyingRigidbodyMotor), `docs/AI/systems/Ornithopter.md`

**Interfaces:**
- Produces:
  - `FlyingRigidbodyMotor` serialized fields `bankPerTurnRate` (0), `maxBank` (0), `pitchAlongPath` (false) and `maxPitch` (45). With all defaults the old rotation code runs unchanged.
  - `[Serializable] class NpcWingSettings`
  - `struct NpcWingState`
  - `static NpcWingState NpcWings.Step(NpcWingState s, Vector3 velocity, float bankDegrees, float turnRateDegrees, float dt, NpcWingSettings cfg)`
  - `NpcOrnithopterWings : MonoBehaviour, IOrnithopterFlightState`

**The shared seam with the player's craft:** `IOrnithopterFlightState`. `OrnithopterWingAnimator` and `OrnithopterAudio` already bind it by `GetComponent` in `Awake`. On the NPC variant the presenter is the only implementer, because Task 4 removes `OrnithopterFlightMotor`. No player code path changes.

The presenter's derivation sits beside the player motor's "watching machine" measurement (`OrnithopterFlightMotor.Replication.cs: Update`) rather than sharing it, on purpose. Extracting it would edit the player's motor, which the user ruled out, and the inputs differ: the NPC presenter has no flight model, no stall and no stamina.

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Game/Editor/Tests/FlyingRigidbodyMotorAttitudeTests.cs
// FlyingRigidbodyMotor's opt-in attitude: a craft that banks into its turns and noses along its climb
// (the NPC ornithopter), while a blimp — every Sky fleet hull — keeps the exact old upright rotation.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.EditorTools;

namespace SpaceGame.Tests
{
    public class FlyingRigidbodyMotorAttitudeTests
    {
        private static readonly Vector3 FarAway = new Vector3(150000f, 6000f, 150000f);
        private const float Step = 0.02f;
        private SimulationMode originalMode;
        private GameObject craft;
        private FlyingRigidbodyMotor motor;

        [SetUp]
        public void SetUp()
        {
            originalMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            craft = new GameObject("Craft");
            craft.transform.position = FarAway;
            craft.AddComponent<Rigidbody>().useGravity = false;
            motor = craft.AddComponent<FlyingRigidbodyMotor>();
        }

        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = originalMode;
            Object.DestroyImmediate(craft);
        }

        private void Attitude(float bankPerTurnRate, float maxBank, bool pitch)
        {
            var so = new SerializedObject(motor);
            SerializedFields.SetFloat(so, "maxSpeed", 25f);
            SerializedFields.SetFloat(so, "acceleration", 8f);
            SerializedFields.SetBool(so, "altitudeHold", false);
            SerializedFields.SetFloat(so, "bankPerTurnRate", bankPerTurnRate);
            SerializedFields.SetFloat(so, "maxBank", maxBank);
            SerializedFields.SetBool(so, "pitchAlongPath", pitch);
            so.ApplyModifiedPropertiesWithoutUndo();
            typeof(FlyingRigidbodyMotor).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                        .Invoke(motor, null);
        }

        private void FlyToward(Vector3 offset, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                MoveIntent intent = MoveIntent.MoveTo(craft.transform.position + offset);
                motor.Tick(in intent, Step);
                motor.StepPhysics(Step);
                Physics.Simulate(Step);
            }
        }

        private float Roll => Mathf.DeltaAngle(0f, craft.transform.eulerAngles.z);
        private float Pitch => Mathf.DeltaAngle(0f, craft.transform.eulerAngles.x);

        [Test]
        public void WithAttitudeOff_TheMotorNeverRollsOrPitches()
        {
            Attitude(0f, 0f, pitch: false);
            FlyToward(new Vector3(300f, 200f, 50f), 100);

            Assert.AreEqual(0f, Roll, 0.01f, "a blimp rolled");
            Assert.AreEqual(0f, Pitch, 0.01f, "a blimp pitched");
        }

        [Test]
        public void ACraftTurningRight_BanksItsRightWingDown()
        {
            Attitude(0.5f, 35f, pitch: false);
            FlyToward(new Vector3(500f, 0f, 0f), 30);

            Assert.Less(Roll, -5f, $"roll {Roll:F1} deg: no bank into a right turn");
            Assert.GreaterOrEqual(Roll, -35.01f, "banked past maxBank");
        }

        [Test]
        public void ACraftClimbing_PointsItsNoseUp()
        {
            Attitude(0f, 0f, pitch: true);
            FlyToward(new Vector3(0f, 200f, 300f), 100);

            Assert.Less(Pitch, -5f, $"pitch {Pitch:F1} deg: the nose did not follow the climb");
        }
    }
}
```

```csharp
// Assets/Game/Tests/EditMode/NpcWingsTests.cs
using NUnit.Framework;
using SpaceGame.Vehicles.Ornithopter;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>The NPC craft's wings, derived from how it moves: what the wing animator and audio are fed.</summary>
    public class NpcWingsTests
    {
        private const float Dt = 1f / 60f;
        private static readonly NpcWingSettings Cfg = new NpcWingSettings();

        private static NpcWingState Run(Vector3 velocity, float seconds, float bank = 0f, float turnRate = 0f)
        {
            var s = new NpcWingState();
            for (float t = 0f; t < seconds; t += Dt) s = NpcWings.Step(s, velocity, bank, turnRate, Dt, Cfg);
            return s;
        }

        [Test]
        public void AParkedCraft_KeepsItsWingsShut()
        {
            Assert.AreEqual(0f, Run(Vector3.zero, 2f).WingSpread);
        }

        [Test]
        public void ACruisingCraft_OpensItsWingsAndBeatsThem()
        {
            NpcWingState s = Run(new Vector3(0f, 0f, 25f), 2f);
            Assert.AreEqual(1f, s.WingSpread, 1e-3f);
            Assert.Greater(s.FlapEffort, 0f);
            Assert.AreEqual(25f, s.Airspeed, 1e-3f);
        }

        [Test]
        public void Climbing_BeatsHarderThanLevelFlight()
        {
            Assert.Greater(Run(new Vector3(0f, 5f, 24f), 1f).FlapEffort, Run(new Vector3(0f, 0f, 25f), 1f).FlapEffort);
        }

        [Test]
        public void AWreckFallingFast_HalfFoldsItsWings_AndStopsBeating()
        {
            NpcWingState s = Run(new Vector3(10f, -20f, 0f), 3f);
            Assert.AreEqual(Cfg.WreckSpread, s.WingSpread, 1e-3f);
            Assert.AreEqual(0f, s.FlapEffort);
        }

        [Test]
        public void TurningRight_ReadsAsARightTurn_AndPassesTheBankThrough()
        {
            NpcWingState s = Run(new Vector3(0f, 0f, 25f), 0.5f, bank: 20f, turnRate: 30f);
            Assert.Greater(s.Turn, 0f);
            Assert.AreEqual(20f, s.Bank, 1e-3f);
        }
    }
}
```

- [ ] **Step 2: Typecheck to see it fail**

Expected: `NpcWings`, `NpcWingState` and `NpcWingSettings` are missing. The motor tests compile, because they set fields by name, but `SerializedFields.SetFloat` fails at run time until the fields exist.

- [ ] **Step 3: Extend `FlyingRigidbodyMotor`**

Update the header comment: add one paragraph, "Attitude is opt-in: a craft that should bank into its turns and pitch along its climb (the NPC ornithopter) sets `bankPerTurnRate`/`pitchAlongPath`; with both off — every Sky fleet hull — the rotation is the old upright yaw slerp, unchanged."

Fields, after `riderTurnSpeed`:

```csharp
        [Header("Attitude (opt-in; a blimp leaves these at zero)")]
        [Tooltip("Degrees of bank per degree/second of turn, on a dynamic body. 0 keeps the craft upright.")]
        [SerializeField, Min(0f)] private float bankPerTurnRate;

        [Tooltip("Steepest bank, degrees.")]
        [SerializeField, Range(0f, 80f)] private float maxBank;

        [Tooltip("Point the nose along the climb or descent, on a dynamic body.")]
        [SerializeField] private bool pitchAlongPath;

        [Tooltip("Steepest nose up or down when pitching along the path, degrees.")]
        [SerializeField, Range(0f, 80f)] private float maxPitch = 45f;

        // Below this horizontal speed there is no path to pitch along.
        private const float MinPitchSpeed = 1f;
```

In `FaceDirection`, replace the final non-kinematic statement

```csharp
            body.MoveRotation(Quaternion.Slerp(body.rotation, target, rotateSpeed * deltaTime));
```

with

```csharp
            body.MoveRotation(AttitudeEnabled
                ? Attitude(target, rotateSpeed, deltaTime)
                : Quaternion.Slerp(body.rotation, target, rotateSpeed * deltaTime));
```

and add:

```csharp
        private bool AttitudeEnabled => bankPerTurnRate > 0f || pitchAlongPath;

        /// <summary>
        /// The same yaw slerp toward <paramref name="targetYaw"/>, plus a bank into the turn that slerp
        /// makes and a pitch along the climb. Yaw is slerped on its own so the roll and pitch written last
        /// step are not slerped back out.
        /// </summary>
        private Quaternion Attitude(Quaternion targetYaw, float rotateSpeed, float deltaTime)
        {
            float yaw = body.rotation.eulerAngles.y;
            float newYaw = Quaternion.Slerp(Quaternion.Euler(0f, yaw, 0f), targetYaw, rotateSpeed * deltaTime).eulerAngles.y;
            float turnRate = deltaTime > 0f ? Mathf.DeltaAngle(yaw, newYaw) / deltaTime : 0f;
            float bank = Mathf.Clamp(turnRate * bankPerTurnRate, -maxBank, maxBank);

            float pitch = 0f;
            Vector3 v = LinearVelocity;
            float horizontal = new Vector2(v.x, v.z).magnitude;
            if (pitchAlongPath && horizontal >= MinPitchSpeed)
                pitch = Mathf.Clamp(Mathf.Atan2(v.y, horizontal) * Mathf.Rad2Deg, -maxPitch, maxPitch);

            // Unity's signs (OrnithopterFlightMotor.ApplyPose documents them): -X raises the nose, -Z
            // drops the right wing — a right turn banks right.
            return Quaternion.Euler(-pitch, newYaw, -bank);
        }
```

In `OnValidate`, add `maxBank = Mathf.Max(0f, maxBank);`.

- [ ] **Step 4: The wing state and its presenter**

```csharp
// Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/NpcWings.cs
// The NPC craft's wings, read off how it is moving — there is no flight model behind an NPC craft to ask
// (NpcFlightPlan + FlyingRigidbodyMotor fly it). Pure, so NpcWingsTests can drive it.
using System;
using UnityEngine;

namespace SpaceGame.Vehicles.Ornithopter
{
    [Serializable]
    public class NpcWingSettings
    {
        [Tooltip("Below this speed the craft is parked and its wings stay shut, m/s.")]
        [Min(0f)] public float SpreadSpeed = 3f;

        [Tooltip("Seconds for the wings to open or close.")]
        [Min(0.05f)] public float SpreadSeconds = 0.6f;

        [Tooltip("Wing beats per second gliding and at full effort.")]
        [Min(0.01f)] public float FlapHzIdle = 0.35f;
        [Min(0.01f)] public float FlapHzMax = 1.6f;

        [Tooltip("Beat effort in level flight, 0..1.")]
        [Range(0f, 1f)] public float GlideEffort = 0.15f;

        [Tooltip("Extra effort per m/s of climb.")]
        [Min(0f)] public float EffortPerClimbSpeed = 0.25f;

        [Tooltip("Sinking faster than this reads as a wreck: wings half shut, no beat, m/s.")]
        [Min(0f)] public float WreckSinkSpeed = 12f;

        [Tooltip("How far a wreck's wings stay open, 0..1.")]
        [Range(0f, 1f)] public float WreckSpread = 0.4f;

        [Tooltip("Turn rate that reads as full turn input, degrees per second.")]
        [Min(1f)] public float TurnRateForFullTurn = 45f;
    }

    public struct NpcWingState
    {
        public float Airspeed;
        public float FlapPhase;
        public float FlapEffort;
        public float WingSpread;
        public float Bank;
        public float Turn;
    }

    public static class NpcWings
    {
        public static NpcWingState Step(NpcWingState s, Vector3 velocity, float bankDegrees, float turnRateDegrees,
                                        float dt, NpcWingSettings cfg)
        {
            s.Airspeed = velocity.magnitude;
            s.Bank = bankDegrees;
            s.Turn = Mathf.Clamp(turnRateDegrees / cfg.TurnRateForFullTurn, -1f, 1f);

            bool wrecked = -velocity.y >= cfg.WreckSinkSpeed;
            float spreadTarget = s.Airspeed < cfg.SpreadSpeed ? 0f : wrecked ? cfg.WreckSpread : 1f;
            s.WingSpread = Mathf.MoveTowards(s.WingSpread, spreadTarget, dt / cfg.SpreadSeconds);

            s.FlapEffort = wrecked || s.WingSpread <= 0f
                ? 0f
                : Mathf.Clamp01(cfg.GlideEffort + velocity.y * cfg.EffortPerClimbSpeed);

            if (s.WingSpread > 0f)
            {
                float hz = cfg.FlapHzIdle + (cfg.FlapHzMax - cfg.FlapHzIdle) * s.FlapEffort;
                s.FlapPhase = Mathf.Repeat(s.FlapPhase + hz * dt, 1f);
            }

            return s;
        }
    }
}
```

```csharp
// Assets/Game/Scripts/Vehicles/Ornithopter/NpcFlight/NpcOrnithopterWings.cs
// Feeds the wing animator and the audio on an NPC-flown craft (IOrnithopterFlightState — the seam both
// bind in Awake). Measured from the transform on EVERY machine: the server's FlyingRigidbodyMotor moves
// it, clients see the replicated pose, and both derive the same wings with nothing sent.
using UnityEngine;

namespace SpaceGame.Vehicles.Ornithopter
{
    [DisallowMultipleComponent]
    public class NpcOrnithopterWings : MonoBehaviour, IOrnithopterFlightState
    {
        [SerializeField] private NpcWingSettings settings = new NpcWingSettings();

        private NpcWingState state;
        private Vector3 lastPosition;
        private float lastYaw;
        private bool sampled;

        public float Airspeed => state.Airspeed;
        public float FlapPhase => state.FlapPhase;
        public float FlapEffort => state.FlapEffort;
        public float WingSpread => state.WingSpread;
        public float BankAngle => state.Bank;
        public float PitchInput => 0f;
        public float TurnInput => state.Turn;
        public bool IsStalled => false;

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 position = transform.position;
            float yaw = transform.eulerAngles.y;
            if (!sampled)
            {
                lastPosition = position;
                lastYaw = yaw;
                sampled = true;
                return;
            }

            Vector3 velocity = (position - lastPosition) / dt;
            float turnRate = Mathf.DeltaAngle(lastYaw, yaw) / dt;
            lastPosition = position;
            lastYaw = yaw;

            // Positive bank is right wing down, as IOrnithopterFlightState.BankAngle reads it.
            float bank = -Mathf.DeltaAngle(0f, transform.eulerAngles.z);
            state = NpcWings.Step(state, velocity, bank, turnRate, dt, settings);
        }

        private void OnEnable() => sampled = false;
    }
}
```

- [ ] **Step 5: Typecheck and run**

Run `py rt.py 'FlyingRigidbodyMotorAttitudeTests|NpcWingsTests|SkyFleetDriftTests|SkyFleetPrefabTests|OrnithopterWingAnimatorTests'`.

Expected: all pass. The fleet tests prove the default path is unchanged.

- [ ] **Step 6: Docs**

- **Vehicles.md:** in the `FlyingRigidbodyMotor` row and tunables, add the opt-in attitude fields: "0/false on every fleet hull; the NPC ornithopter banks 0.5 deg per deg/s up to 35, pitching along its path".
- **Ornithopter.md:**
  - Key types: an `NpcOrnithopterWings` / `NpcWings` row (the measured `IOrnithopterFlightState` for NPC craft).
  - Gotchas: "the wing animator and audio bind `IOrnithopterFlightState` in `Awake`; a craft without the player's motor needs another implementer or its wings never move".
- Bump `updated:` in both and run `docs_check.py --index`.

- [ ] **Step 7: Report the exact files changed.**

---

### Task 3: `CraftDeployment` and `FlightLaunch.HasLaunchRoom`: extract the wing pack's shared halves

**Files:**
- Create: `Assets/Game/Scripts/Items/Equipped/CraftDeployment.cs`
- Modify: `Assets/Game/Scripts/Vehicles/Ornithopter/Flight/FlightLaunch.cs`
- Modify: `Assets/Game/Scripts/Items/Equipped/WingPackItem.cs` (`HasLaunchRoom` `:92-105`, `LaunchPosition` `:266-287`, `ReleaseCraft` despawn block `:401-412`)
- Test: `Assets/Game/Editor/Tests/CraftDeploymentTests.cs`; regression `WingPackLaunchTests`
- Docs: `docs/AI/systems/Ornithopter.md`

**Interfaces:**
- Produces:
  - `static Vector3 CraftDeployment.LaunchPosition(Transform craftRoot, Vector3 seatPoint, Vector3 pilotPosition, Quaternion facing, float lift)`
  - `static void CraftDeployment.Retire(GameObject craft)`
  - `static bool FlightLaunch.IsAirborne(Vector3 feet, float groundClearance, LayerMask groundMask)`
  - `static bool FlightLaunch.HasDropAhead(Vector3 feet, Vector3 forward, float probeForward, float minClearance, LayerMask groundMask)`
  - `static bool FlightLaunch.HasLaunchRoom(Vector3 feet, Vector3 forward, float groundClearance, float minClearance, float probeForward, LayerMask groundMask)`
- `WingPackItem.LaunchPosition(GameObject, Vector3, Quaternion, float)` keeps its signature, because `WingPackLaunchTests` pins it.

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Game/Editor/Tests/CraftDeploymentTests.cs
// The two halves of deploying a craft that the player's wing pack and an NPC's flight share: where to
// spawn it so its seat lands on the pilot, and how to retire it on whichever machine this is.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;
using SpaceGame.Vehicles.Ornithopter;

namespace SpaceGame.Tests
{
    public class CraftDeploymentTests
    {
        private static readonly Vector3 FarAway = new Vector3(150000f, 6000f, 150000f);
        private readonly List<Object> junk = new();
        private IWorldService previousWorld;
        private DespawnSpy world;

        [SetUp]
        public void SetUp()
        {
            previousWorld = GameServices.World;
            world = new DespawnSpy();
            GameServices.World = world;
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.World = previousWorld;
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        [Test]
        public void LaunchPosition_PutsTheSeatOnTheLiftedPilot_ForEveryHeading()
        {
            var root = new GameObject("Craft");
            junk.Add(root);
            var seat = new GameObject("SEAT").transform;
            seat.SetParent(root.transform, false);
            seat.localPosition = new Vector3(0f, -0.3f, 1.5f);
            Vector3 pilot = new Vector3(10f, 2f, -4f);

            foreach (float yaw in new[] { 0f, 90f, 217f })
            {
                Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
                Vector3 craftAt = CraftDeployment.LaunchPosition(root.transform, seat.position, pilot, facing, 1.2f);
                Vector3 seatAfter = craftAt + facing * root.transform.InverseTransformPoint(seat.position);

                Assert.Less(Vector3.Distance(seatAfter, pilot + Vector3.up * 1.2f), 1e-4f, $"yaw {yaw}");
            }
        }

        [Test]
        public void Retire_Offline_DespawnsThroughTheWorldService()
        {
            var craft = new GameObject("Craft");
            junk.Add(craft);
            CraftDeployment.Retire(craft);
            CollectionAssert.Contains(world.Despawned, craft);
        }

        [Test]
        public void LaunchRoom_StandingOnGround_IsNone_ButAtAnEdge_ItIs()
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            junk.Add(block);
            block.transform.position = FarAway;
            block.transform.localScale = new Vector3(10f, 1f, 10f);
            Physics.SyncTransforms();
            Vector3 middle = FarAway + Vector3.up * 0.5f;
            Vector3 edge = FarAway + new Vector3(0f, 0.5f, 4.9f);

            Assert.IsFalse(FlightLaunch.HasLaunchRoom(middle, Vector3.forward, 0.6f, 6f, 1.5f, ~0), "mid-block is not a launch");
            Assert.IsTrue(FlightLaunch.HasLaunchRoom(edge, Vector3.forward, 0.6f, 6f, 1.5f, ~0), "facing off the edge is a launch");
            Assert.IsTrue(FlightLaunch.IsAirborne(FarAway + Vector3.up * 20f, 0.6f, ~0), "20 m up is airborne");
        }

        private sealed class DespawnSpy : IWorldService
        {
            public readonly List<GameObject> Despawned = new();
            public void Despawn(GameObject gameObject) => Despawned.Add(gameObject);
            public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation,
                                    ulong ownerClientId = NetworkSpawn.NoOwner) => null;
        }
    }
}
```

- [ ] **Step 2: Typecheck to see it fail** (`CraftDeployment`, `FlightLaunch.HasLaunchRoom` and `IsAirborne` missing).

- [ ] **Step 3: Implement `CraftDeployment`**

```csharp
// Assets/Game/Scripts/Items/Equipped/CraftDeployment.cs
// Deploying a craft for a pilot, the two halves shared by the player's wing pack (WingPackItem) and a
// Sky nomad's flight (NpcFlightModule / NpcAviator), so neither copies the other.
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    public static class CraftDeployment
    {
        /// <summary>
        /// Where the craft's root has to be spawned for its SEAT to land on the pilot, lifted by
        /// <paramref name="lift"/>. <paramref name="seatPoint"/> is the seat in the same space as
        /// <paramref name="craftRoot"/> — read off the PREFAB, before the craft exists, because the
        /// spawn pose is the only pose that replicates (Ornithopter.md Gotchas).
        /// </summary>
        public static Vector3 LaunchPosition(Transform craftRoot, Vector3 seatPoint, Vector3 pilotPosition,
                                             Quaternion facing, float lift)
        {
            Vector3 lifted = pilotPosition + Vector3.up * lift;
            if (craftRoot == null) return lifted;

            // The seat in the root's own frame, turned to the launch heading: the craft is spawned
            // rotated, so an offset measured in the prefab's frame has to turn with it.
            Vector3 seatLocal = craftRoot.InverseTransformPoint(seatPoint);
            return lifted - facing * seatLocal;
        }

        /// <summary>
        /// Take a craft out of the world for everybody. Only the server may retire a networked object;
        /// on a client the authoritative despawn arrives from the server, so nothing is destroyed out
        /// from under it here.
        /// </summary>
        public static void Retire(GameObject craft)
        {
            if (craft == null) return;

            if (Network.IsNetworked && !Network.Server &&
                craft.TryGetComponent(out NetworkObject netObj) && netObj.IsSpawned)
                return;

            GameServices.World.Despawn(craft);
        }
    }
}
```

- [ ] **Step 4: Extend `FlightLaunch`.** Add inside the class:

```csharp
        // The probes start a hand's width above the feet: a ray that starts AT the sole starts inside
        // the ground it is standing on and finds nothing.
        private const float ProbeLift = 0.1f;

        /// <summary>No ground within <paramref name="groundClearance"/> straight down: already falling.</summary>
        public static bool IsAirborne(Vector3 feet, float groundClearance, LayerMask groundMask) =>
            !Physics.Raycast(feet + Vector3.up * ProbeLift, Vector3.down, groundClearance,
                             groundMask, QueryTriggerInteraction.Ignore);

        /// <summary>
        /// A drop worth jumping into, <paramref name="probeForward"/> ahead. This is what makes a cliff
        /// EDGE work: the ray straight down hits the ledge being stood on; the one that matters is cast
        /// out over the drop.
        /// </summary>
        public static bool HasDropAhead(Vector3 feet, Vector3 forward, float probeForward, float minClearance,
                                        LayerMask groundMask) =>
            !Physics.Raycast(feet + Vector3.up * ProbeLift + forward * probeForward, Vector3.down, minClearance,
                             groundMask, QueryTriggerInteraction.Ignore);

        /// <summary>Air under the wings in either sense: already falling, or at the top of a drop.</summary>
        public static bool HasLaunchRoom(Vector3 feet, Vector3 forward, float groundClearance, float minClearance,
                                         float probeForward, LayerMask groundMask) =>
            IsAirborne(feet, groundClearance, groundMask) ||
            HasDropAhead(feet, forward, probeForward, minClearance, groundMask);
```

- [ ] **Step 5: Make `WingPackItem` forward**

- Replace the body of `HasLaunchRoom(Transform player)` with:
  `return FlightLaunch.HasLaunchRoom(player.position, player.forward, groundClearance, minLaunchClearance, ledgeProbeForward, groundMask);`
  Keep its doc comment.
- Replace the body of `LaunchPosition(GameObject craftPrefab, …)` (keep the doc comments) with:

```csharp
            var prefabMount = craftPrefab != null ? craftPrefab.GetComponent<MountModule>() : null;
            Transform seat = prefabMount != null ? prefabMount.ActiveSeatPoint : null;
            if (seat == null)
                return pilotPosition + Vector3.up * lift;

            return CraftDeployment.LaunchPosition(craftPrefab.transform, seat.TransformPoint(prefabMount.SeatOffset),
                                                  pilotPosition, facing, lift);
```

- In `ReleaseCraft`, replace everything from `// Despawn through the world service …` down to the closing `SetHeldVisible(true);` with:

```csharp
            // For every player, not just whoever was flying it; a client leaves it to the server.
            CraftDeployment.Retire(doomed);
            SetHeldVisible(true);
```

  Then remove `using Unity.Netcode;` if nothing else in the file uses it. Typecheck tells you.

- [ ] **Step 6: Typecheck and run the tests**

`py rt.py 'CraftDeploymentTests|WingPackLaunchTests|WingPackStowTests'`. Expected: all pass.

- [ ] **Step 7: Docs.** In Ornithopter.md:
  - Add a Key types row for `CraftDeployment` (launch pose from any seat point; `Retire` = the server-only despawn).
  - Change the `WingPackItem` row to "forwards to `CraftDeployment` / `FlightLaunch.HasLaunchRoom`".
  - Flows step 1: `HasLaunchRoom` now lives in `FlightLaunch`.
  - Bump `updated:` and run `docs_check.py --index`.

- [ ] **Step 8: Report the exact files changed**

---

### Task 4: `NpcAviator` and the unsaved `NpcOrnithopter` prefab (riskiest-slice exit)

**Files:**
- Create: `Assets/Game/Scripts/Vehicles/NpcAviation/NpcAviator.cs`
- Modify: `Assets/Game/Scripts/Core/Persistence/Runtime/SaveablePolicy.cs` (`NeedsSaving`, beside the `VesselPilot` clause `:83`)
- Create: `Assets/Game/Editor/Vehicles/NpcOrnithopterBuilder.cs`
- Create (by the builder): `Assets/Game/Prefabs/Agents/Vehicles/Aircraft/NpcOrnithopter.prefab`
- Modify (by the builder): `Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset`
- Test: `Assets/Game/Editor/Tests/NpcAviatorTests.cs`, `NpcOrnithopterPrefabTests.cs`, `NpcOrnithopterFlightTests.cs`, and the helper `InstantiatingWorld.cs`
- Docs: `docs/AI/systems/Ornithopter.md`, `docs/AI/systems/Persistence.md`, `docs/AI/systems/Vehicles.md` (`VesselSeats` row)

**Interfaces:**
- Consumes:
  - Task 1: `NpcFlightPlan`, `NpcFlightSettings`, `NpcFlightStep`, `NpcFlightPhase`.
  - Task 2: `FlyingRigidbodyMotor` attitude fields and `NpcOrnithopterWings`.
  - Task 3: `CraftDeployment.Retire`.
  - Existing: `VesselSeats.Seat(int, GameObject)`, `Unseat(int, Vector3)`, `OccupantAt(int)`, `SeatPose(int)`; `LandingSiteFinder.Find`; `PhysicsGroundProbe.TryGround` / `TryGroundBelow`; `OrnithopterCrash.ClosingSpeed` / `ImpactDamage`; `OrnithopterCrashConfig`.
- Produces:
  - `NpcAviator : BehaviourModuleBase` (namespace `SpaceGame.Vehicles`; Task 8 adds `IStowsTorsoGear`), with:
    - `bool Fly(GameObject pilot, Vector3 destination, float cruiseHeight, float landingSampleDistance)`
    - `GameObject Pilot`, `bool HasSite`, `bool Wrecked`, `Vector3 Goal`, `Vector3 LandingPoint`, `NpcFlightPhase Phase`
    - `event Action<GameObject, bool> PilotReleased` (npc, alive)
    - `static LandingSettings CraftLanding`
  - `NpcOrnithopterBuilder.PrefabPath`, `CruiseSpeed` (25) and `Acceleration` (8), plus `Build()` and `Verify()`.

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Game/Editor/Tests/InstantiatingWorld.cs
// An IWorldService for tests that need what was spawned to be real: Spawn instantiates the prefab
// (a scene object built by the test is a fine "prefab") and Despawn only records.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Tests
{
    public sealed class InstantiatingWorld : IWorldService
    {
        public readonly List<GameObject> Spawned = new();
        public readonly List<GameObject> Despawned = new();
        private readonly List<Object> junk;

        public InstantiatingWorld(List<Object> junk) => this.junk = junk;

        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation,
                                ulong ownerClientId = NetworkSpawn.NoOwner)
        {
            GameObject go = Object.Instantiate(prefab, position, rotation);
            junk.Add(go);
            Spawned.Add(go);
            return go;
        }

        public void Despawn(GameObject gameObject) => Despawned.Add(gameObject);
    }
}
```

```csharp
// Assets/Game/Editor/Tests/NpcAviatorTests.cs
// An NPC flown on the wing pack's craft: seated in the cradle, steered by the flight plan through
// FlyingRigidbodyMotor, set down on the ground at touchdown and the craft retired; killed in the air,
// its body drops and the craft spirals in as a wreck.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Ornithopter;

namespace SpaceGame.Tests
{
    public class NpcAviatorTests
    {
        private static readonly Vector3 FarAway = new Vector3(150000f, 6000f, 150000f);
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<Object> junk = new();
        private IWorldService previousWorld;
        private InstantiatingWorld world;

        private GameObject craft;
        private NpcAviator aviator;
        private GameObject pilot;

        [SetUp]
        public void SetUp()
        {
            previousWorld = GameServices.World;
            world = new InstantiatingWorld(junk);
            GameServices.World = world;
            craft = Craft(junk);
            aviator = craft.GetComponent<NpcAviator>();
            pilot = Pilot(junk);
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.World = previousWorld;
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        /// <summary>The NpcOrnithopter's flying parts, built in code: body, FlyingRigidbodyMotor, one-seat VesselSeats, NpcAviator.</summary>
        internal static GameObject Craft(List<Object> junk)
        {
            var go = new GameObject("NpcCraft");
            junk.Add(go);
            go.transform.position = FarAway;
            go.AddComponent<Rigidbody>().useGravity = false;
            var seat = new GameObject("SEAT_Cradle").transform;
            seat.SetParent(go.transform, false);
            var motor = go.AddComponent<FlyingRigidbodyMotor>();
            var motorSo = new UnityEditor.SerializedObject(motor);
            motorSo.FindProperty("altitudeHold").boolValue = false;
            motorSo.ApplyModifiedPropertiesWithoutUndo();
            typeof(FlyingRigidbodyMotor).GetMethod("Awake", Private).Invoke(motor, null);
            var seats = go.AddComponent<VesselSeats>();
            var so = new UnityEditor.SerializedObject(seats);
            UnityEditor.SerializedProperty list = so.FindProperty("seats");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = seat;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.AddComponent<NpcAviator>();
            return go;
        }

        internal static GameObject Pilot(List<Object> junk)
        {
            var go = new GameObject("Pilot");
            junk.Add(go);
            go.transform.position = FarAway;
            go.AddComponent<HealthComponent>();
            go.AddComponent<AgentController>();
            return go;
        }

        private Vector3 GoalPoint => FarAway + Vector3.forward * 2000f;
        private bool Fly() => aviator.Fly(pilot, GoalPoint, 60f, 6f);
        private MoveIntent? Tick() => aviator.Tick(new AgentContext { Self = craft.transform, Position = craft.transform.position }, 0.02f);
        private void Touchdown(float closingSpeed) =>
            typeof(NpcAviator).GetMethod("Touchdown", Private).Invoke(aviator, new object[] { craft.transform.position, closingSpeed });

        [Test]
        public void Fly_SeatsThePilot_AndStartsTheFlight()
        {
            Assert.IsTrue(Fly());
            Assert.AreEqual(pilot, aviator.Pilot);
            Assert.IsTrue(pilot.transform.IsChildOf(craft.transform), "the pilot is not in the cradle");
            Assert.IsTrue(pilot.GetComponent<AgentController>().RidesAsPassenger, "the pilot kept its feet");
            Assert.AreEqual(NpcFlightPhase.EnRoute, aviator.Phase);
        }

        [Test]
        public void Fly_RefusesWhileAlreadyFlying()
        {
            Assert.IsTrue(Fly());
            Assert.IsFalse(aviator.Fly(Pilot(junk), FarAway, 60f, 6f));
        }

        [Test]
        public void Tick_SteersTowardTheGoal()
        {
            Fly();
            MoveIntent? intent = Tick();

            Assert.IsTrue(intent.HasValue);
            Assert.AreEqual(AgentIntentType.MoveToPosition, intent.Value.Type);
            Assert.Greater(intent.Value.TargetPosition.z, craft.transform.position.z, "not heading for a goal due north");
        }

        [Test]
        public void Touchdown_PutsThePilotDown_FreesIt_AndRetiresTheCraft()
        {
            GameObject released = null;
            bool alive = false;
            aviator.PilotReleased += (npc, a) => { released = npc; alive = a; };
            Fly();

            Touchdown(0f);

            Assert.IsNull(pilot.transform.parent, "the pilot is still in the cradle");
            Assert.IsFalse(pilot.GetComponent<AgentController>().RidesAsPassenger);
            CollectionAssert.Contains(world.Despawned, craft);
            Assert.AreEqual(pilot, released);
            Assert.IsTrue(alive);
        }

        [Test]
        public void AGentleLanding_IsFree_AndAHardArrivalHurts()
        {
            var health = pilot.GetComponent<HealthComponent>();
            int full = health.GetHealth;
            Fly();
            Touchdown(2f);
            Assert.AreEqual(full, health.GetHealth, "a gentle landing cost health");

            GameObject craft2 = Craft(junk);
            craft2.GetComponent<NpcAviator>().Fly(pilot, GoalPoint, 60f, 6f);
            typeof(NpcAviator).GetMethod("Touchdown", Private)
                .Invoke(craft2.GetComponent<NpcAviator>(), new object[] { craft2.transform.position, 25f });
            Assert.Less(health.GetHealth, full, "flying into the ground at 25 m/s cost nothing");
        }

        [Test]
        public void ACrashIntoAWall_EndsTheFlight()
        {
            Fly();
            typeof(NpcAviator).GetField("launchedAt", Private).SetValue(aviator, -999f);
            typeof(NpcAviator).GetField("lastVelocity", Private).SetValue(aviator, new Vector3(0f, 0f, 25f));

            typeof(NpcAviator).GetMethod("Crash", Private)
                .Invoke(aviator, new object[] { craft.transform.position, Vector3.back });

            Assert.IsNull(pilot.transform.parent);
            CollectionAssert.Contains(world.Despawned, craft);
        }

        [Test]
        public void ThePilotDying_WrecksTheCraft_AndDropsTheBodyStraightDown()
        {
            bool releasedDead = false;
            aviator.PilotReleased += (npc, a) => releasedDead = npc == pilot && !a;
            Fly();

            pilot.GetComponent<HealthComponent>().Damage(999);

            Assert.IsNull(pilot.transform.parent, "the body rides on in the cradle");
            Assert.IsTrue(aviator.Wrecked);
            Assert.AreEqual(NpcFlightPhase.Wreck, aviator.Phase, "a pilotless craft flew on");
            Assert.IsTrue(releasedDead);
            CollectionAssert.DoesNotContain(world.Despawned, craft, "the wreck vanished in mid-air");
        }

        [Test]
        public void AWreckReachingTheGround_IsRetired()
        {
            Fly();
            pilot.GetComponent<HealthComponent>().Damage(999);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            junk.Add(ground);
            ground.transform.position = FarAway + Vector3.down * 1.5f;
            ground.transform.localScale = new Vector3(40f, 1f, 40f);
            Physics.SyncTransforms();

            Tick();

            CollectionAssert.Contains(world.Despawned, craft);
        }

        [Test]
        public void ACraftWhosePilotWasTakenAway_RetiresItself()
        {
            Fly();
            Object.DestroyImmediate(pilot);   // its group folded: NpcSpawn.Remove under the craft

            Tick();

            CollectionAssert.Contains(world.Despawned, craft);
        }

        [Test]
        public void WithNoSiteFound_TheCraftStillFliesToTheGoal()
        {
            Fly();
            Tick();

            Assert.IsFalse(aviator.HasSite, "a site was found over empty space");
            Assert.AreEqual(GoalPoint, aviator.LandingPoint, "with no ground to project onto, the goal itself is the landing");
        }

        [Test]
        public void TheCraft_IsNeverSaved()
        {
            Assert.IsFalse(SaveablePolicy.NeedsSaving(craft, out string why),
                           $"an NPC craft would be saved ({why}) and come back pilotless after a load");
        }
    }
}
```

```csharp
// Assets/Game/Editor/Tests/NpcOrnithopterPrefabTests.cs
// The NPC craft as written to disk by NpcOrnithopterBuilder: the player's craft with its flight motor,
// mount and every saver taken off, flown by FlyingRigidbodyMotor + NpcAviator, its wings fed by
// NpcOrnithopterWings, and a registered network prefab of its own.
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.EditorTools;
using SpaceGame.Persistence;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Ornithopter;

namespace SpaceGame.Tests
{
    public class NpcOrnithopterPrefabTests
    {
        private const string PlayerCraftPath = "Assets/Game/Prefabs/Agents/Vehicles/Aircraft/DuneOrnithopter.prefab";

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"{path} is missing — run Tools/SpaceGame/Vehicles/Build NPC Ornithopter");
            return prefab;
        }

        [Test]
        public void TheNpcCraft_FliesOnTheSimpleMotor_WithNoMountAndNoSavers()
        {
            GameObject craft = Load(NpcOrnithopterBuilder.PrefabPath);

            Assert.IsNull(craft.GetComponent<OrnithopterFlightMotor>(), "NPCs never fly the player's energy model");
            Assert.IsNotNull(craft.GetComponent<FlyingRigidbodyMotor>());
            Assert.IsNull(craft.GetComponent<MountModule>(), "a player could take the NPC's craft (D7)");
            Assert.IsNull(craft.GetComponent<MountNetworkSync>());
            Assert.IsNull(craft.GetComponent<SteerModule>());
            Assert.IsNull(craft.GetComponent<SaveableEntity>(), "an NPC craft must never be saved (D4)");
            Assert.IsEmpty(craft.GetComponents<ISaveable>(), "a saver survived on the NPC craft");
            Assert.IsNotNull(craft.GetComponent<NpcAviator>());
            Assert.AreEqual(1, craft.GetComponent<VesselSeats>().Capacity);
            Assert.IsFalse(SaveablePolicy.NeedsSaving(craft, out _));
        }

        [Test]
        public void TheNpcCraftsWings_AreFedByThePresenter()
        {
            GameObject craft = Load(NpcOrnithopterBuilder.PrefabPath);
            Assert.IsNotNull(craft.GetComponent<OrnithopterWingAnimator>());
            Assert.IsInstanceOf<NpcOrnithopterWings>(craft.GetComponent<IOrnithopterFlightState>(),
                                                    "the wing animator would bind nothing and the wings never beat");
        }

        [Test]
        public void TheNpcCraft_IsARegisteredNetworkPrefab_DistinctFromThePlayersCraft()
        {
            uint npc = Load(NpcOrnithopterBuilder.PrefabPath).GetComponent<NetworkObject>().PrefabIdHash;
            uint player = Load(PlayerCraftPath).GetComponent<NetworkObject>().PrefabIdHash;

            Assert.AreNotEqual(0u, npc, "a zero hash is dropped by NGO for every peer but the host");
            Assert.AreNotEqual(player, npc);
            Assert.IsTrue(NetworkPrefabRegistrar.IsRegistered(NpcOrnithopterBuilder.PrefabPath));
        }
    }
}
```

If typecheck says `PrefabIdHash` is not public on this embedded NGO, read the serialized field instead: `new SerializedObject(…GetComponent<NetworkObject>()).FindProperty("GlobalObjectIdHash").uintValue`.

```csharp
// Assets/Game/Editor/Tests/NpcOrnithopterFlightTests.cs
// The riskiest slice end to end, on the REAL prefab (no Play Mode): a pilot seated in NpcOrnithopter
// takes off from the ground — or launches off Sky City height — flies to a goal, sets down within the
// spec's ~15 m, steps off unhurt, and the craft is retired. The craft lives in a preview scene with its
// own physics; the ground lives in the default scene, which is where PhysicsGroundProbe looks.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.EditorTools;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;

namespace SpaceGame.Tests
{
    public class NpcOrnithopterFlightTests
    {
        private static readonly Vector3 Ground0 = new Vector3(200000f, 0f, 200000f);
        private const float Dt = 0.02f;
        private const float LandingTolerance = 15f;
        private readonly List<Object> junk = new();
        private IWorldService previousWorld;
        private InstantiatingWorld world;
        private SimulationMode originalMode;
        private UnityEngine.SceneManagement.Scene scene;

        [SetUp]
        public void SetUp()
        {
            previousWorld = GameServices.World;
            world = new InstantiatingWorld(junk);
            GameServices.World = world;
            originalMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.hideFlags = HideFlags.HideAndDontSave;
            junk.Add(ground);
            ground.transform.position = Ground0 + Vector3.down * 0.5f;
            ground.transform.localScale = new Vector3(6000f, 1f, 6000f);
            Physics.SyncTransforms();
            scene = EditorSceneManager.NewPreviewScene();
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.ClosePreviewScene(scene);
            Physics.simulationMode = originalMode;
            GameServices.World = previousWorld;
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        [TestCase(3f, 600f)]
        [TestCase(280f, 2000f)]
        public void TheRealCraft_FliesToItsGoal_AndSetsThePilotDownNearIt(float startHeight, float goalDistance)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcOrnithopterBuilder.PrefabPath);
            Assert.IsNotNull(prefab, "build the NPC craft first");
            var craft = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            junk.Add(craft);
            craft.transform.position = Ground0 + Vector3.up * startHeight;
            var motor = craft.GetComponent<FlyingRigidbodyMotor>();
            typeof(FlyingRigidbodyMotor).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(motor, null);
            var aviator = craft.GetComponent<NpcAviator>();
            GameObject pilot = NpcAviatorTests.Pilot(junk);
            pilot.transform.position = craft.transform.position;
            Vector3 goal = Ground0 + Vector3.right * goalDistance;
            Assert.IsTrue(aviator.Fly(pilot, goal, 60f, 6f));

            PhysicsScene physics = scene.GetPhysicsScene();
            for (int i = 0; i < 25000 && !world.Despawned.Contains(craft); i++)
            {
                MoveIntent? intent = aviator.Tick(new AgentContext { Self = craft.transform, Position = craft.transform.position }, Dt);
                MoveIntent applied = intent ?? MoveIntent.Idle();
                motor.Tick(in applied, Dt);
                motor.StepPhysics(Dt);
                physics.Simulate(Dt);
            }

            Assert.Contains(craft, world.Despawned, "the craft never landed");
            Assert.IsNull(pilot.transform.parent, "the pilot was never set down");
            var flat = new Vector2(pilot.transform.position.x - goal.x, pilot.transform.position.z - goal.z);
            Assert.Less(flat.magnitude, LandingTolerance, $"set down {flat.magnitude:F1} m from the goal");
            var health = pilot.GetComponent<HealthComponent>();
            Assert.AreEqual(health.GetMaxHealth, health.GetHealth, "the landing hurt the pilot");
        }
    }
}
```

- [ ] **Step 2: Typecheck to see it fail**

Expected: `NpcAviator` and `NpcOrnithopterBuilder` are missing.

- [ ] **Step 3: Implement `NpcAviator`**

```csharp
// Assets/Game/Scripts/Vehicles/NpcAviation/NpcAviator.cs
// Flies one NPC on the craft the wing pack deploys and puts it down where it was going — the NPC-side
// counterpart of WingPackItem's flight, on NpcOrnithopter.prefab. NOT the player's energy model: the
// craft is flown by FlyingRigidbodyMotor, steered by this module through MoveIntents taken from the
// pure NpcFlightPlan (user decision 2026-10-06: NPC flight is simple; the player's flight is untouched).
//
// Seating is VesselSeats' (one marker, SEAT_Cradle): feet off, brain on, so a pilot shoots from the
// cradle (D6), and a dead pilot is dropped straight down. LandingSiteFinder picks reachable ground near
// the goal once that ground has streamed in; until then (or if there is none) the craft lands at the
// goal itself, projected onto the ground. This class owns the three endings:
//   • touchdown (the plan says down) or a crash (any collision past the launch grace): price the
//     arrival, stand the pilot on the NavMesh, retire the craft;
//   • the pilot dies: the plan spirals the craft in as a wreck (D8), retired when it reaches the ground;
//   • the pilot is taken away underneath it (its group folded): retire the craft at once.
// Server-decided throughout (AgentController only ticks modules where it simulates); every peer sees the
// spawn, the parenting and the despawn. Never saved: SaveablePolicy.NeedsSaving refuses any root
// carrying this (D4).
using System;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Vehicles.Ornithopter;
using SpaceGame.World;

namespace SpaceGame.Vehicles
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VesselSeats))]
    [RequireComponent(typeof(FlyingRigidbodyMotor))]
    public class NpcAviator : BehaviourModuleBase
    {
        private const int PilotSeat = 0;

        // MoveIntent.MoveTo's own default: the plan already slows the craft for its touchdown.
        private const float ArriveStopDistance = 0.2f;

        [Header("Flight")]
        [SerializeField] private NpcFlightSettings flight = new NpcFlightSettings();

        [Tooltip("Seconds after launch during which a collision is ignored — the deck or ledge just left.")]
        [SerializeField, Min(0f)] private float launchGraceSeconds = 0.5f;

        [Tooltip("With no ground found below the craft, the ground counts as this far down, metres.")]
        [SerializeField, Min(1f)] private float noGroundDepth = 300f;

        [Header("Landing")]
        [Tooltip("Half-width of the ground the craft sets down on, metres.")]
        [SerializeField, Min(0.5f)] private float footprintRadius = 6f;

        [Tooltip("Where around the goal a landing site may be: tighter rings than a vessel's, because the " +
                 "spec wants the pilot down within about 15 m of where it was going.")]
        [SerializeField] private LandingSettings landing = CraftLanding;

        [SerializeField] private PhysicsGroundProbe probe = new PhysicsGroundProbe();

        [Tooltip("Seconds between looks for a landing site while none is chosen.")]
        [SerializeField, Min(0.1f)] private float siteCheckInterval = 1f;

        [Header("Crash")]
        [Tooltip("What an arrival costs the pilot, by closing speed — the player's craft's own curve.")]
        [SerializeField] private OrnithopterCrashConfig crash = new OrnithopterCrashConfig();

        public static LandingSettings CraftLanding => new LandingSettings
        {
            ringMin = 3f,
            ringMax = 14f,
            candidatesPerRing = 12,
            rings = 3,
            maxSlopeDegrees = 18f,
            maxHeightSpread = 2f,
            clearanceHeight = 15f,
            hoverHeight = 0f,
            navMeshReach = 4f,
            farSidePenalty = 10f,
        };

        private VesselSeats seats;
        private FlyingRigidbodyMotor motor;
        private NpcFlightPlan plan;
        private WorldStreamer streamer;
        private HealthComponent pilotHealth;
        private Vector3 goal;
        private Vector3 landingPoint;
        private Vector3 lastVelocity;
        private float cruiseHeight;
        private float landingReach;
        private float launchedAt;
        private float nextSiteCheck;
        private bool flying;

        public event Action<GameObject, bool> PilotReleased;

        // Lazy, not cached in Awake: EditMode tests run no Awake.
        private VesselSeats Seats => seats != null ? seats : seats = GetComponent<VesselSeats>();
        private FlyingRigidbodyMotor Motor => motor != null ? motor : motor = GetComponent<FlyingRigidbodyMotor>();
        private NpcFlightPlan Plan => plan ??= new NpcFlightPlan(flight);

        public GameObject Pilot => Seats.OccupantAt(PilotSeat);
        public bool HasSite { get; private set; }
        public bool Wrecked { get; private set; }
        public Vector3 Goal => goal;
        public Vector3 LandingPoint => landingPoint;
        public NpcFlightPhase Phase => Plan.Phase;

        public override string ModuleDescription =>
            "Flies the seated NPC pilot to its goal on FlyingRigidbodyMotor (NpcFlightPlan: climb, cruise, " +
            "approach, spiral, flare), lands it on NavMesh and retires the craft; spirals in as a wreck if the pilot dies.";

        private void Reset() => SetPriorityDefault(ModulePriority.Override);

        /// <summary>Seat <paramref name="pilot"/> and fly it to <paramref name="destination"/>. Server only.</summary>
        public bool Fly(GameObject pilot, Vector3 destination, float cruiseHeight, float landingSampleDistance)
        {
            if (pilot == null || flying || !Network.Simulates(this)) return false;
            if (!Seats.Seat(PilotSeat, pilot)) return false;

            probe.IgnoreHierarchy(transform);
            goal = destination;
            landingPoint = probe.TryGround(destination, out Vector3 ground, out _) ? ground : destination;
            this.cruiseHeight = cruiseHeight;
            landingReach = landingSampleDistance;
            HasSite = false;
            Wrecked = false;
            flying = true;
            launchedAt = Time.time;
            nextSiteCheck = 0f;
            streamer = FindFirstObjectByType<WorldStreamer>();

            WatchPilot(pilot);
            Plan.Begin();
            return true;
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (!flying) return null;

            // Taken away underneath us — its group folded and NpcSpawn.Remove despawned it. Nothing is
            // aboard and nobody is watching (a fold happens beyond despawnRadius).
            if (!Wrecked && Pilot == null)
            {
                Retire();
                return MoveIntent.Idle();
            }

            if (!Wrecked && !HasSite && Time.time >= nextSiteCheck)
            {
                nextSiteCheck = Time.time + siteCheckInterval;
                TryChooseSite();
            }

            Vector3 position = transform.position;
            lastVelocity = Motor.Velocity;
            float groundBelow = probe.TryGroundBelow(position, out Vector3 below) ? below.y : position.y - noGroundDepth;

            NpcFlightStep step = Plan.Step(position, groundBelow, landingPoint, cruiseHeight);
            if (step.Touchdown)
            {
                Touchdown(new Vector3(position.x, groundBelow, position.z), Mathf.Max(0f, -lastVelocity.y));
                return MoveIntent.Idle();
            }

            return MoveIntent.MoveTo(step.Target, ArriveStopDistance, step.Speed);
        }

        private void TryChooseSite()
        {
            // The rule VesselPilot learned the hard way: ground that has not streamed in has no site to
            // find, and "no site" there is not an answer.
            if (streamer != null && !streamer.IsGroundLoadedAround(goal, landing.ringMax + footprintRadius)) return;

            DropSite? site = LandingSiteFinder.Find(goal, transform.position, footprintRadius, landing, probe);
            if (!site.HasValue || site.Value.Mode != DropMode.Land) return;

            HasSite = true;
            landingPoint = site.Value.Point;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.contactCount == 0) return;
            ContactPoint contact = collision.GetContact(0);
            Crash(contact.point, contact.normal);
        }

        /// <summary>Flew into something — a cliff, a rock, another craft. Priced on the velocity from before the contact.</summary>
        private void Crash(Vector3 point, Vector3 normal)
        {
            if (!flying || !Network.Simulates(this) || Time.time - launchedAt < launchGraceSeconds) return;

            Vector3 ground = probe.TryGround(point, out Vector3 hit, out _) ? hit : point;
            Touchdown(ground, OrnithopterCrash.ClosingSpeed(lastVelocity, normal));
        }

        private void Touchdown(Vector3 ground, float closingSpeed)
        {
            flying = false;

            GameObject pilot = Wrecked ? null : Pilot;
            if (pilot != null)
            {
                // Priced first, the hit landing last with the pilot standing — WingPackItem.HandleLanded's
                // order, for the same reason: a fatal arrival leaves the body at the craft.
                int damage = OrnithopterCrash.ImpactDamage(closingSpeed, crash);
                Vector3 stand = NavMesh.SamplePosition(ground, out NavMeshHit hit, landingReach, NavMesh.AllAreas)
                    ? hit.position
                    : ground;

                UnwatchPilot();
                Seats.Unseat(PilotSeat, stand);
                if (damage > 0) NetDamage.Apply(pilot, damage);

                bool alive = !pilot.TryGetComponent(out HealthComponent health) || health.Alive;
                PilotReleased?.Invoke(pilot, alive);
            }

            Retire();
        }

        private void WatchPilot(GameObject pilot)
        {
            pilotHealth = pilot.GetComponent<HealthComponent>();
            if (pilotHealth != null) pilotHealth.OnDeath += OnPilotDied;
        }

        private void UnwatchPilot()
        {
            if (pilotHealth != null) pilotHealth.OnDeath -= OnPilotDied;
            pilotHealth = null;
        }

        /// <summary>
        /// Killed in the air (D8). VesselSeats has already let the body go (it subscribed first, at Seat),
        /// so it falls where it died and EntityLootTable drops the pack there; the craft spirals in close by.
        /// </summary>
        private void OnPilotDied()
        {
            if (pilotHealth == null || pilotHealth.IsRestoring || !Network.Simulates(this)) return;

            GameObject corpse = pilotHealth.gameObject;
            UnwatchPilot();
            Wrecked = true;
            Plan.Wreck(transform.position);
            PilotReleased?.Invoke(corpse, false);
        }

        private void Retire()
        {
            flying = false;
            CraftDeployment.Retire(gameObject);
        }

        private void OnDestroy() => UnwatchPilot();
    }
}
```

Remove `using SpaceGame.Items;` if typecheck reports it unused; `CraftDeployment` is in `SpaceGame.Items`, so it is needed.

- [ ] **Step 4: Never save it.** In `SaveablePolicy.NeedsSaving`, directly after the `VesselPilot` line, add:

```csharp
            // An NPC-flown craft is a vehicle for one flight: the flier's group record (or nothing, for a
            // sortie) is what comes back after a load (D4). Its non-kinematic body and its AgentController
            // each qualify on their own, and WorldService.Spawn runs EnsureSpawned on every runtime spawn —
            // without this the craft came back pilotless and hung in the air.
            if (go.GetComponent<NpcAviator>() != null) return false;
```

- [ ] **Step 5: Typecheck and run `NpcAviatorTests`**

Run `py rt.py 'NpcAviatorTests'`. All pass. The prefab and flight tests stay red until Step 7.

- [ ] **Step 6: Write the builder**

```csharp
// Assets/Game/Editor/Vehicles/NpcOrnithopterBuilder.cs
// Builds the NPC craft as a Prefab Variant of the player's DuneOrnithopter: the same model, wing rig,
// wing animator and audio, with the player's flight motor, mount, steering and every saver removed,
// and the simple NPC flight added — FlyingRigidbodyMotor (banking on), NpcOrnithopterWings (feeds the
// wing animator), one-seat VesselSeats and NpcAviator. Re-runnable: it rebuilds from the base every time.
// DuneOrnithopter.prefab itself is hand-owned and never written here.
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Ornithopter;

namespace SpaceGame.EditorTools
{
    public static class NpcOrnithopterBuilder
    {
        public const string BasePath = "Assets/Game/Prefabs/Agents/Vehicles/Aircraft/DuneOrnithopter.prefab";
        public const string PrefabPath = "Assets/Game/Prefabs/Agents/Vehicles/Aircraft/NpcOrnithopter.prefab";
        private const string SeatName = "SEAT_Cradle";

        // The NPC craft's flight (NpcFlightPlanTests fly the plan against exactly these two).
        public const float CruiseSpeed = 25f;     // Spike 5.2a E4 cruised the player's craft at 22-28 m/s
        public const float Acceleration = 8f;
        private const float FaceRotateSpeed = 1.5f;
        private const float BankPerTurnRate = 0.5f;
        private const float MaxBank = 35f;
        private const float MaxPitch = 40f;

        // A dead pilot's body and a landed pilot are both put on NavMesh within this; matches
        // NpcPassenger.dismountSampleDistance.
        private const float SeatNavMeshReach = 6f;

        // Dependents before what they depend on: Unity refuses to remove a component another requires.
        // OrnithopterSaveable requires the flight motor, and MountNetworkSync/SteerModule require MountModule.
        private static readonly Type[] Removed =
        {
            typeof(MountNetworkSync), typeof(SteerModule), typeof(MountModule), typeof(OrnithopterFlightMotor),
        };

        [MenuItem("Tools/SpaceGame/Vehicles/Build NPC Ornithopter")]
        public static void Build()
        {
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePath);
            if (basePrefab == null)
            {
                Debug.LogError($"[NpcOrnithopterBuilder] No base craft at {BasePath}.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            try
            {
                foreach (MonoBehaviour saver in instance.GetComponents<MonoBehaviour>().Where(c => c is ISaveable).ToArray())
                    UnityEngine.Object.DestroyImmediate(saver);
                if (instance.TryGetComponent(out SaveableEntity entity)) UnityEngine.Object.DestroyImmediate(entity);
                foreach (Type type in Removed)
                    if (instance.TryGetComponent(type, out Component c)) UnityEngine.Object.DestroyImmediate(c);

                AddMotor(instance);
                instance.AddComponent<NpcOrnithopterWings>();
                AddSeat(instance);
                instance.AddComponent<NpcAviator>();
                PointControllerAtMotor(instance);

                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            // The worker, never SyncMenu (a modal dialog parks the chain). Then re-serialize so the
            // NetworkObject's hash reaches the YAML, as SkyFleetBuilder does.
            Debug.Log(NetworkPrefabRegistrar.Sync(out _, out _));
            AssetDatabase.ForceReserializeAssets(new[] { PrefabPath });
            Verify();
        }

        private static void AddMotor(GameObject craft)
        {
            var motor = craft.AddComponent<FlyingRigidbodyMotor>();
            var so = new SerializedObject(motor);
            SerializedFields.Set(so, "body", craft.GetComponent<Rigidbody>());
            SerializedFields.SetFloat(so, "maxSpeed", CruiseSpeed);
            SerializedFields.SetFloat(so, "acceleration", Acceleration);
            SerializedFields.SetFloat(so, "deceleration", Acceleration);
            SerializedFields.SetFloat(so, "faceRotateSpeed", FaceRotateSpeed);
            SerializedFields.SetBool(so, "kinematicHull", false);      // a dynamic body: collisions end a flight
            SerializedFields.SetBool(so, "altitudeHold", false);       // NpcAviator owns the height
            SerializedFields.SetBool(so, "gravityWhenIdle", false);
            SerializedFields.SetFloat(so, "bankPerTurnRate", BankPerTurnRate);
            SerializedFields.SetFloat(so, "maxBank", MaxBank);
            SerializedFields.SetBool(so, "pitchAlongPath", true);
            SerializedFields.SetFloat(so, "maxPitch", MaxPitch);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddSeat(GameObject craft)
        {
            Transform seat = craft.GetComponentsInChildren<Transform>(true).First(t => t.name == SeatName);
            var seats = craft.AddComponent<VesselSeats>();
            var so = new SerializedObject(seats);
            SerializedProperty list = so.FindProperty("seats");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = seat;
            SerializedFields.SetVector3(so, "seatOffset", Vector3.zero);
            SerializedFields.SetFloat(so, "navMeshReach", SeatNavMeshReach);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // The base leaves MotorComponent empty (auto-resolved); named explicitly so the NPC craft can
        // never resolve anything but the simple motor.
        private static void PointControllerAtMotor(GameObject craft)
        {
            var so = new SerializedObject(craft.GetComponent<AgentController>());
            SerializedFields.Set(so, "MotorComponent", craft.GetComponent<FlyingRigidbodyMotor>());
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Read the written prefab back off disk and fail loudly on anything the build did not stick.</summary>
        public static void Verify()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new InvalidOperationException($"{PrefabPath} was not written.");
            if (prefab.GetComponent<OrnithopterFlightMotor>() != null || prefab.GetComponent<MountModule>() != null ||
                prefab.GetComponent<SaveableEntity>() != null || prefab.GetComponents<MonoBehaviour>().Any(c => c is ISaveable))
                throw new InvalidOperationException($"{PrefabPath} still carries the player's motor, mount or a saver: the removed-component overrides did not stick.");
            if (prefab.GetComponent<NpcAviator>() == null || prefab.GetComponent<FlyingRigidbodyMotor>() == null ||
                !(prefab.GetComponent<IOrnithopterFlightState>() is NpcOrnithopterWings) ||
                prefab.GetComponent<VesselSeats>()?.Capacity != 1)
                throw new InvalidOperationException($"{PrefabPath} is missing its motor, wings presenter, aviator or its one seat.");
            if (!NetworkPrefabRegistrar.IsRegistered(PrefabPath))
                throw new InvalidOperationException($"{PrefabPath} is not in the network prefab list.");
        }
    }
}
```

**If `Verify` throws "removed-component overrides did not stick"**, the editor refused to record removals on a variant. Make it a standalone prefab instead: insert
`PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);`
directly after `InstantiatePrefab`, and record in Ornithopter.md Gotchas that the NPC craft no longer inherits model or rig edits from `DuneOrnithopter`. Check `SerializedFields.Set`'s exact signature in `Assets/Game/Editor/Support/SerializedFields.cs:35` before relying on it.

- [ ] **Step 7: Build the prefab (one call), then run the slice exit**

```bash
cd /c/Users/tobia/Documents/spaceGame/SpaceGame && git status --porcelain > "$SCRATCH/p5_status_before.txt"
```

Write `$SCRATCH/p5_build_npc_craft.cs` as:

```csharp
SpaceGame.EditorTools.NpcOrnithopterBuilder.Build();
return "built " + SpaceGame.EditorTools.NpcOrnithopterBuilder.PrefabPath;
```

Then:
1. Run `py ux.py < p5_build_npc_craft.cs`.
2. Diff `git status --porcelain` against the snapshot. Restore unrelated churn, but only on files that were clean before.
3. `git diff Assets/Game/Prefabs/Agents/Vehicles/Aircraft/DuneOrnithopter.prefab` must be empty.
4. Run `py rt.py 'NpcOrnithopterPrefabTests|NpcOrnithopterFlightTests|NpcAviatorTests|NetworkPrefabRegistrationTests|SaveWiringOnDiskTests|OrnithopterRigWiringTests|OrnithopterWingAnimatorTests'`.

Expected: all pass, apart from the known Dunehorn failure. **`NpcOrnithopterFlightTests` is the riskiest-slice exit**: the real prefab flies off the ground and from 280 m, and sets its pilot down unhurt within 15 m.
- If the flight test fails while `NpcFlightPlanTests` pass, the real motor disagrees with the stand-in. Compare the two: the motor's facing turn rate, and that it decelerates inside `ArriveStopDistance`. Tune `NpcFlightSettings` defaults so that both test classes pass.
- Report the numbers if they cannot be made to agree.

The NavMesh landing site, the seat pose and the client view are human checklist items (Task 10).

- [ ] **Step 8: Docs**

- **Ornithopter.md:**
  - Add `NpcAviation/NpcAviator.cs` and `NpcOrnithopter.prefab` to `paths`.
  - Model: "NPC pilots ride `NpcOrnithopter` — a variant of the player's craft with the player's motor, mount and savers removed, flown by `FlyingRigidbodyMotor` + `NpcAviator`/`NpcFlightPlan`; the player's motor and energy model are untouched".
  - Key types: `NpcAviator`, `NpcOrnithopterBuilder`.
  - Flows: add an "NPC flight" flow: `Fly` → seat → plan steps (climb, cruise, approach or spiral, flare) → touchdown or crash → `Unseat` + price → `Retire`; the pilot dies → wreck spiral → `Retire` at the ground.
  - Multiplayer: "server-owned; FRM disabled on clients by `NetAuthority`, which follow the replicated transform; wings derived per machine by `NpcOrnithopterWings`; no message".
  - Persistence: "never saved: `NeedsSaving` refuses `NpcAviator`".
  - Gotchas: "`WorldService.Spawn` runs `EnsureSpawned`; an unsaved prefab is not enough".
  - `symptoms:` add `"an NPC's ornithopter comes back after a load with nobody in it"`, `"a Sky nomad's craft keeps flying after its pilot was shot"` and `"an NPC ornithopter's wings never beat"`.
- **Persistence.md:** add `NpcAviator` to the `NeedsSaving` exclusion row.
- **Vehicles.md:** add to the `VesselSeats` row: "also the NPC ornithopter's one-seat cradle".
- Bump `updated:` and run `docs_check.py --index`.

- [ ] **Step 9: Report the exact files changed**

Include:
- the new prefab and its `.meta`
- `DefaultNetworkPrefabs.asset`
- confirmation that `DuneOrnithopter.prefab` is unchanged
- any churn you restored

---

### Task 5: `EntityBodyEquipment`: NPC worn gear (folded pack on the spine, gauntlets on the forearms)

**Files:**
- Create: `Assets/Game/Scripts/Items/Body/WornBones.cs`, `Assets/Game/Scripts/Items/Body/IStowsTorsoGear.cs`, `Assets/Game/Scripts/Items/Core/INpcAim.cs`
- Modify: `Assets/Game/Scripts/Items/Body/BodyEquipmentController.cs` (`backBoneNameHints` `:34`, `LeftForearmHints`/`RightForearmHints` `:74-75`, uses `:183-184`)
- Modify: `Assets/Game/Scripts/Items/Equipped/WornFit.cs`, `Assets/Game/Scripts/Items/Equipped/WornSeat.cs`
- Create: `Assets/Game/Scripts/agents/entity/EntityBodyEquipment.cs`
- Test: `Assets/Game/Editor/Tests/EntityBodyEquipmentTests.cs`, helper `Assets/Game/Editor/Tests/TestTorsoStower.cs`; regression `WornSeatTests`, `WornAnchorTests`
- Docs: `docs/AI/systems/BodyEquipment.md`

**Interfaces:**
- Produces:
  - `interface INpcAim { bool HasAimPoint { get; } Vector3 AimPoint { get; } }` (namespace `SpaceGame.Items`)
  - `interface IStowsTorsoGear { }` (namespace `SpaceGame.Items`)
  - `static class WornBones` with `BackHints`, `LeftForearmHints`, `RightForearmHints`, `LeftHandHints`, `RightHandHints`
  - `WornFit`: `bool HasFoldedPose`, `float FoldedSize`, `Vector3 FoldedLocalPosition`, `Quaternion FoldedLocalRotation`; `SizeFor(Form.Carried)` returns `foldedSize` when set
  - `static void WornSeat.ApplyWithoutRig(GameObject instance, Transform bone, WornFit fit)`
  - `EntityBodyEquipment : NetworkBehaviour, INpcAim`:
    - `bool TryWear(InventoryItem item)`, `bool TryWear(InventoryItem item, BodySlot slot)`, `InventoryItem Remove(BodySlot slot)`
    - `InventoryItem ItemIn(BodySlot)`, `GameObject InstanceIn(BodySlot)`, `UsableItem UsableIn(BodySlot)`
    - `List<InventoryItem> TakeAllWorn()`, `void RestoreWorn(IReadOnlyList<InventoryItem> items)`
    - `void SetTorsoShown(bool)`, `bool TorsoShown`, `void RefreshStowed()`
    - `bool IsNpcUsable(BodySlot)`, `Vector3 FireOrigin(BodySlot)`, `void AimAt(Vector3)`, `void ClearAim()`, `bool TryUseWornAt(BodySlot slot, Vector3 aimPoint)` (the last added in Task 7)
    - `event Action<BodySlot> WornChanged`

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Game/Editor/Tests/TestTorsoStower.cs
// A stand-in carrier that puts its rider's torso gear away (NpcAviator implements the interface for real,
// Task 8). In its own file so Unity keeps the script on the component.
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    public class TestTorsoStower : MonoBehaviour, IStowsTorsoGear { }
}
```

```csharp
// Assets/Game/Editor/Tests/EntityBodyEquipmentTests.cs
// NPC worn gear: the same three slots and rules as the player's body, the wing pack worn FOLDED on the
// spine (D3, NPCs have no lash rail), gauntlets strapped to the forearms, and the pack put away while
// its wearer rides a carrier that stows it.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    public class EntityBodyEquipmentTests
    {
        internal const string WingPackPath = "Assets/Game/Resources/Items/Artifacts/WingPack.asset";
        internal const string RepulsorPath = "Assets/Game/Resources/Items/Artifacts/RepulsorGauntlet.asset";
        internal const string GunPath = "Assets/Game/Resources/Items/Artifacts/basicgun.asset";

        private readonly List<Object> junk = new();
        private GameObject npc;
        private EntityBodyEquipment body;

        [SetUp]
        public void SetUp()
        {
            npc = Npc(junk);
            body = npc.GetComponent<EntityBodyEquipment>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        internal static T Asset<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, path);
            return asset;
        }

        /// <summary>A body with the bone names BoneResolver falls back to (no Animator), and worn gear.</summary>
        internal static GameObject Npc(List<Object> junk)
        {
            var go = new GameObject("Npc");
            junk.Add(go);
            Transform Bone(string name, Transform parent, Vector3 local)
            {
                var t = new GameObject(name).transform;
                t.SetParent(parent, false);
                t.localPosition = local;
                return t;
            }
            Transform hips = Bone("Hips", go.transform, new Vector3(0f, 1.5f, 0f));
            Transform spine = Bone("Spine", hips, new Vector3(0f, 0.3f, 0f));
            Transform leftArm = Bone("LeftForeArm", spine, new Vector3(-0.6f, 0.4f, 0f));
            Bone("LeftHand", leftArm, new Vector3(-0.35f, 0f, 0f));
            Transform rightArm = Bone("RightForeArm", spine, new Vector3(0.6f, 0.4f, 0f));
            Bone("RightHand", rightArm, new Vector3(0.35f, 0f, 0f));
            go.AddComponent<EntityBodyEquipment>();
            return go;
        }

        [Test]
        public void TheWingPack_GoesInTheTorso_OnTheSpine_Folded()
        {
            var pack = Asset<InventoryItem>(WingPackPath);
            Assert.IsTrue(body.TryWear(pack));

            Assert.AreEqual(pack, body.ItemIn(BodySlot.Torso));
            GameObject worn = body.InstanceIn(BodySlot.Torso);
            Assert.IsNotNull(worn);
            Assert.AreEqual("Spine", worn.transform.parent.name);
            Transform stowedWings = WornVisual.Of(worn);
            Assert.IsTrue(stowedWings == null || !stowedWings.gameObject.activeSelf,
                          "an NPC wears the folded bundle, not the stowed wings (D3)");
        }

        [Test]
        public void AHandItem_IsNeverWorn()
        {
            Assert.IsFalse(body.TryWear(Asset<InventoryItem>(GunPath)));
            for (int i = 0; i < 3; i++) Assert.IsNull(body.ItemIn((BodySlot)i));
        }

        [Test]
        public void ASecondTorsoItem_IsRefused_WhileTheFirstIsWorn()
        {
            var pack = Asset<InventoryItem>(WingPackPath);
            Assert.IsTrue(body.TryWear(pack));
            Assert.IsFalse(body.TryWear(pack));
        }

        [Test]
        public void AGauntlet_GoesOnTheFirstFreeForearm()
        {
            Assert.IsTrue(body.TryWear(Asset<InventoryItem>(RepulsorPath)));
            Assert.AreEqual("LeftForeArm", body.InstanceIn(BodySlot.LeftGauntlet).transform.parent.name);
            Assert.IsTrue(body.TryWear(Asset<InventoryItem>(RepulsorPath)));
            Assert.AreEqual("RightForeArm", body.InstanceIn(BodySlot.RightGauntlet).transform.parent.name);
        }

        [Test]
        public void Remove_HandsTheItemBack_AndTakesTheVisualOff()
        {
            var pack = Asset<InventoryItem>(WingPackPath);
            body.TryWear(pack);
            Assert.AreEqual(pack, body.Remove(BodySlot.Torso));
            Assert.IsNull(body.ItemIn(BodySlot.Torso));
            Assert.IsNull(body.InstanceIn(BodySlot.Torso));
        }

        [Test]
        public void RidingACarrierThatStowsIt_HidesThePack_AndGettingOffShowsIt()
        {
            body.TryWear(Asset<InventoryItem>(WingPackPath));
            var carrier = new GameObject("Craft");
            junk.Add(carrier);
            carrier.AddComponent<TestTorsoStower>();

            npc.transform.SetParent(carrier.transform, true);
            body.RefreshStowed();
            Assert.IsFalse(body.TorsoShown);
            foreach (Renderer r in body.InstanceIn(BodySlot.Torso).GetComponentsInChildren<Renderer>(true))
                Assert.IsFalse(r.enabled, "the folded pack still shows while the craft it IS is deployed");

            npc.transform.SetParent(null, true);
            body.RefreshStowed();
            Assert.IsTrue(body.TorsoShown);
        }

        [Test]
        public void StartingGear_IsWornOffline_AndARestoredEmptyBodyStaysEmpty()
        {
            var so = new SerializedObject(body);
            so.FindProperty("startingWorn").GetArrayElementAtIndex(0).objectReferenceValue = Asset<InventoryItem>(WingPackPath);
            so.ApplyModifiedPropertiesWithoutUndo();

            body.RestoreWorn(new InventoryItem[] { null, null, null });   // a save says "nothing"
            typeof(EntityBodyEquipment).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(body, null);

            Assert.IsNull(body.ItemIn(BodySlot.Torso), "the prefab's starting pack overrode a save that said empty");
        }

        [Test]
        public void StartingGear_IsWornOffline_WhenNothingWasRestored()
        {
            var so = new SerializedObject(body);
            so.FindProperty("startingWorn").GetArrayElementAtIndex(0).objectReferenceValue = Asset<InventoryItem>(WingPackPath);
            so.ApplyModifiedPropertiesWithoutUndo();

            typeof(EntityBodyEquipment).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(body, null);

            Assert.IsNotNull(body.ItemIn(BodySlot.Torso));
        }
    }
}
```

- [ ] **Step 2: Typecheck to see it fail.**

- [ ] **Step 3: Small shared pieces**

```csharp
// Assets/Game/Scripts/Items/Body/WornBones.cs
// Bone-name hints for a rig the humanoid lookup cannot read, shared by every body that wears gear —
// the player's BodyEquipmentController and an NPC's EntityBodyEquipment — so the two never disagree
// about which bone a gauntlet straps to.
namespace SpaceGame.Items
{
    public static class WornBones
    {
        public static readonly string[] BackHints = { "Spine", "Chest", "Torso" };
        public static readonly string[] LeftForearmHints = { "LeftForeArm", "ForeArm_L", "L_ForeArm", "forearm.L" };
        public static readonly string[] RightForearmHints = { "RightForeArm", "ForeArm_R", "R_ForeArm", "forearm.R" };
        public static readonly string[] LeftHandHints = { "LeftHand", "Hand_L", "L_Hand", "hand.L" };
        public static readonly string[] RightHandHints = { "RightHand", "Hand_R", "R_Hand", "hand.R" };
    }
}
```

```csharp
// Assets/Game/Scripts/Items/Body/IStowsTorsoGear.cs
namespace SpaceGame.Items
{
    /// <summary>
    /// A carrier whose rider's worn torso gear is put away while they ride it — the ornithopter an NPC's
    /// wing pack deploys IS the pack, so the folded one on the back must not show at the same time.
    /// Asked of the hierarchy (the rider is netcode-parented under it), so every machine, late joiners
    /// included, gets the same answer with nothing sent.
    /// </summary>
    public interface IStowsTorsoGear { }
}
```

```csharp
// Assets/Game/Scripts/Items/Core/INpcAim.cs
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Where an NPC holder is aiming, for items that would otherwise ask a player's AimProvider. Read by
    /// UsableItem.HolderAimRay.
    /// </summary>
    public interface INpcAim
    {
        bool HasAimPoint { get; }
        Vector3 AimPoint { get; }
    }
}
```

In `BodyEquipmentController`:
- Change the `backBoneNameHints` initializer to `= WornBones.BackHints;`. The serialized value on the prefab is unchanged; only the default moves.
- Delete the two private `LeftForearmHints` / `RightForearmHints` arrays.
- Use `WornBones.LeftForearmHints` / `WornBones.RightForearmHints` at the two `BoneResolver.Resolve` calls in `Start`.

- [ ] **Step 4: The folded pose.** In `WornFit`, add after `inspectSize`:

```csharp
        [Header("Folded, on a body with no expedition rig (NPCs)")]
        [Tooltip("Offset from the spine bone, in the bone's frame, metres, for the item's CARRIED (folded) " +
                 "model worn by a body with no lash rail. Only used when Folded Size is set.")]
        [SerializeField] private Vector3 foldedLocalPosition;

        [Tooltip("Rotation of the folded model relative to the spine bone, degrees.")]
        [SerializeField] private Vector3 foldedLocalEuler;

        [Tooltip("Longest-axis size of the folded model when worn that way, metres. 0 = this item is never " +
                 "worn folded; a body with no rig then wears its ordinary worn model at Local Position.")]
        [SerializeField, Min(0f)] private float foldedSize;

        public bool HasFoldedPose => foldedSize > 0f;
        public float FoldedSize => foldedSize;
        public Vector3 FoldedLocalPosition => foldedLocalPosition;
        public Quaternion FoldedLocalRotation => Quaternion.Euler(foldedLocalEuler);
```

Change `SizeFor` to:

```csharp
        public float SizeFor(WornVisual.Form form) => form switch
        {
            WornVisual.Form.Inspected when inspectSize > 0f => inspectSize,
            WornVisual.Form.Carried when foldedSize > 0f => foldedSize,
            _ => size,
        };
```

In `WornSeat`, add after `Apply`:

```csharp
        /// <summary>
        /// Seat torso gear on a body with no expedition rig — an NPC. An item with a folded pose is worn
        /// as its folded (carried) model at that pose (D3: the wing pack's worn wings are authored onto
        /// the rail's bar tips and hang off nothing without one); any other item wears its ordinary worn
        /// model at the fit's offset. The caller pins it (WornAnchor.Pin): with no rail there is no
        /// live mount to re-derive from.
        /// </summary>
        public static void ApplyWithoutRig(GameObject instance, Transform bone, WornFit fit)
        {
            if (fit == null || !fit.HasFoldedPose)
            {
                Apply(instance, bone, fit);
                return;
            }

            Apply(instance, bone, fit, mount: null, WornVisual.Form.Carried);
            instance.transform.SetLocalPositionAndRotation(fit.FoldedLocalPosition, fit.FoldedLocalRotation);
        }
```

- [ ] **Step 5: `EntityBodyEquipment`**

```csharp
// Assets/Game/Scripts/agents/entity/EntityBodyEquipment.cs
// An NPC's worn gear: the same three BodySlots and BodySlotRules as the player's body, decided on the
// server and replicated as item ids, so every machine — late joiners included — seats the same gear
// itself. Torso gear is worn on the spine without a rig (WornSeat.ApplyWithoutRig: the wing pack
// folded, D3); gauntlets are strapped to the forearms by the same ForearmSeat the player's use.
//
// Why not BodyEquipmentController: that is the player's — owner RPCs, an input map, a backpack's lash
// rail, a gear screen. This shares the SEATING half only (WornSeat/ForearmSeat/WornAnchor/WornBones),
// never the network half.
//
// Persistence: EntityBodyEquipmentSaveable ("npcWorn"). A restore wins over the prefab's starting gear,
// including a restore that says "nothing" — a looted pack must not grow back on reload.
// Loot: EntityLootTable takes everything worn on death (TakeAllWorn) and drops it with the bag.
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class EntityBodyEquipment : NetworkBehaviour, INpcAim
    {
        private const int Torso = (int)BodySlot.Torso;

        [Tooltip("Worn from the start, by body slot: Torso, LeftGauntlet, RightGauntlet. An entry of the " +
                 "wrong kind for its slot is skipped with a warning. A save that says otherwise wins.")]
        [SerializeField] private InventoryItem[] startingWorn = new InventoryItem[GearRef.BodySlotCount];

        private readonly InventoryItem[] worn = new InventoryItem[GearRef.BodySlotCount];
        private readonly GameObject[] instances = new GameObject[GearRef.BodySlotCount];

        // Server-written, everyone-read: one item id per slot, empty for nothing. Three fields rather
        // than a list because NGO discovers NetworkVariables as fields; NpcRandomLoadout's pattern.
        private readonly NetworkVariable<FixedString64Bytes> torsoId = new(
            writePerm: NetworkVariableWritePermission.Server, readPerm: NetworkVariableReadPermission.Everyone);
        private readonly NetworkVariable<FixedString64Bytes> leftId = new(
            writePerm: NetworkVariableWritePermission.Server, readPerm: NetworkVariableReadPermission.Everyone);
        private readonly NetworkVariable<FixedString64Bytes> rightId = new(
            writePerm: NetworkVariableWritePermission.Server, readPerm: NetworkVariableReadPermission.Everyone);

        private Transform spine;
        private readonly Transform[] forearms = new Transform[2];
        private readonly EquipItemSocket[] hands = new EquipItemSocket[2];
        private bool rigResolved;

        // Set once anything (a restore, a TryWear) has written a slot: the prefab's starting gear is
        // only for a body nothing has spoken for.
        private bool written;
        private bool torsoShown = true;
        private bool hasAimPoint;
        private Vector3 aimPoint;

        public event Action<BodySlot> WornChanged;

        public bool HasAimPoint => hasAimPoint;
        public Vector3 AimPoint => aimPoint;
        public bool TorsoShown => torsoShown;

        public InventoryItem ItemIn(BodySlot slot) => worn[(int)slot];
        public GameObject InstanceIn(BodySlot slot) => instances[(int)slot];
        public UsableItem UsableIn(BodySlot slot) =>
            instances[(int)slot] != null ? instances[(int)slot].GetComponent<UsableItem>() : null;

        /// <summary>A worn gauntlet this NPC may fire: opted in on its asset (D9) and actually usable.</summary>
        public bool IsNpcUsable(BodySlot slot) => false;   // replaced in Task 7, when InventoryItem.npcUsable exists

        // ── Lifecycle ──────────────────────────────────────────────────────────

        // Offline there is no spawn; the starting gear still has to go on.
        private void Start()
        {
            if (!Network.IsNetworked) WearStarting();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                WearStarting();
                for (int i = 0; i < worn.Length; i++) Publish(i);
                return;
            }

            torsoId.OnValueChanged += OnTorsoChanged;
            leftId.OnValueChanged += OnLeftChanged;
            rightId.OnValueChanged += OnRightChanged;
            // A late joiner gets the values with the spawn and never sees a change event for them.
            Mirror(Torso, torsoId.Value);
            Mirror((int)BodySlot.LeftGauntlet, leftId.Value);
            Mirror((int)BodySlot.RightGauntlet, rightId.Value);
        }

        public override void OnNetworkDespawn()
        {
            torsoId.OnValueChanged -= OnTorsoChanged;
            leftId.OnValueChanged -= OnLeftChanged;
            rightId.OnValueChanged -= OnRightChanged;
        }

        private void OnEnable() => this.NetOn(NetMsg.ItemUsed, OnItemUsedElsewhere);
        private void OnDisable() => this.NetOff(NetMsg.ItemUsed, OnItemUsedElsewhere);

        // A rider is netcode-parented under its carrier on every machine; the parent change is the cue.
        private void OnTransformParentChanged() => RefreshStowed();

        private void WearStarting()
        {
            if (written || startingWorn == null) return;

            for (int i = 0; i < startingWorn.Length && i < worn.Length; i++)
            {
                InventoryItem item = startingWorn[i];
                if (item == null) continue;
                if (!BodySlotRules.Accepts((BodySlot)i, item.equipKind))
                {
                    Debug.LogWarning($"[EntityBody] Starting item '{item.itemName}' is a {item.equipKind} and does not fit the {(BodySlot)i} slot — skipped.", this);
                    continue;
                }
                Write(i, item);
            }
        }

        // ── Server writes ──────────────────────────────────────────────────────

        public bool TryWear(InventoryItem item)
        {
            if (item == null) return false;
            for (int i = 0; i < worn.Length; i++)
                if (worn[i] == null && BodySlotRules.Accepts((BodySlot)i, item.equipKind))
                    return TryWear(item, (BodySlot)i);
            return false;
        }

        public bool TryWear(InventoryItem item, BodySlot slot)
        {
            if (item == null || !Network.Simulates(this)) return false;
            if (worn[(int)slot] != null || !BodySlotRules.Accepts(slot, item.equipKind)) return false;

            Write((int)slot, item);
            return true;
        }

        public InventoryItem Remove(BodySlot slot)
        {
            if (!Network.Simulates(this)) return null;
            InventoryItem item = worn[(int)slot];
            if (item != null) Write((int)slot, null);
            return item;
        }

        /// <summary>Everything worn, taken off — for the loot table on death. Server only.</summary>
        public List<InventoryItem> TakeAllWorn()
        {
            var taken = new List<InventoryItem>();
            for (int i = 0; i < worn.Length; i++)
            {
                InventoryItem item = Remove((BodySlot)i);
                if (item != null) taken.Add(item);
            }
            return taken;
        }

        /// <summary>A save's view of what is worn, positional by slot. Server only; wins over starting gear.</summary>
        public void RestoreWorn(IReadOnlyList<InventoryItem> items)
        {
            if (!Network.Simulates(this))
            {
                Debug.LogWarning("[Save] EntityBody RestoreWorn ignored on a client — worn gear is server state.", this);
                return;
            }

            for (int i = 0; i < worn.Length; i++)
            {
                InventoryItem item = items != null && i < items.Count ? items[i] : null;
                if (item != null && !BodySlotRules.Accepts((BodySlot)i, item.equipKind))
                {
                    Debug.LogWarning($"[Save] '{item.itemName}' does not fit the {(BodySlot)i} slot it was saved in — left empty.", this);
                    item = null;
                }
                Write(i, item);
            }
        }

        private void Write(int index, InventoryItem item)
        {
            written = true;
            worn[index] = item;
            Publish(index);
            Rebuild(index);
            WornChanged?.Invoke((BodySlot)index);
        }

        private void Publish(int index)
        {
            if (!IsSpawned || !IsServer) return;
            InventoryItem item = worn[index];
            var id = new FixedString64Bytes(item != null && !string.IsNullOrEmpty(item.ID) ? item.ID : string.Empty);
            IdOf(index).Value = id;
        }

        private NetworkVariable<FixedString64Bytes> IdOf(int index) => index switch
        {
            Torso => torsoId,
            (int)BodySlot.LeftGauntlet => leftId,
            _ => rightId,
        };

        // ── Every machine: mirror and seat ─────────────────────────────────────

        private void OnTorsoChanged(FixedString64Bytes previous, FixedString64Bytes next) => Mirror(Torso, next);
        private void OnLeftChanged(FixedString64Bytes previous, FixedString64Bytes next) => Mirror((int)BodySlot.LeftGauntlet, next);
        private void OnRightChanged(FixedString64Bytes previous, FixedString64Bytes next) => Mirror((int)BodySlot.RightGauntlet, next);

        private void Mirror(int index, FixedString64Bytes value)
        {
            string id = value.ToString();
            InventoryItem item = string.IsNullOrEmpty(id) ? null : Registry<InventoryItem>.Get(id);
            if (!string.IsNullOrEmpty(id) && item == null)
                Debug.LogWarning($"[EntityBody] '{name}' wears '{id}' on the server but this machine has no such item registered.", this);
            if (worn[index] == item && (item == null || instances[index] != null)) return;

            worn[index] = item;
            Rebuild(index);
            WornChanged?.Invoke((BodySlot)index);
        }

        private void Rebuild(int index)
        {
            Strip(index);
            InventoryItem item = worn[index];
            if (item == null) return;
            if (item.itemPrefab == null)
            {
                Debug.LogError($"[EntityBody] '{item.itemName}' has no prefab.", this);
                return;
            }

            ResolveRig();
            GameObject instance = index == Torso ? WearOnSpine(item.itemPrefab) : WearOnForearm(item.itemPrefab, index);
            if (instance == null)
            {
                Debug.LogWarning($"[EntityBody] '{name}' has nowhere to wear '{item.itemName}' ({(BodySlot)index}).", this);
                return;
            }

            instances[index] = instance;
            if (index == Torso) ApplyTorsoShown();

            if (instance.TryGetComponent(out UsableItem usable))
            {
                usable.Worn = true;
                usable.WornOn = index == (int)BodySlot.LeftGauntlet ? ItemGrip.Hand.Left : ItemGrip.Hand.Right;
                usable.OnEquipped(gameObject);
            }
        }

        private GameObject WearOnSpine(GameObject prefab)
        {
            if (spine == null) return null;
            GameObject instance = Instantiate(prefab, spine);
            EquipItemSocket.Sanitize(instance);
            WornSeat.ApplyWithoutRig(instance, spine, instance.GetComponent<WornFit>());
            WornAnchor.Pin(instance, spine);
            return instance;
        }

        private GameObject WearOnForearm(GameObject prefab, int index)
        {
            int side = index == (int)BodySlot.LeftGauntlet ? 0 : 1;
            Transform forearm = forearms[side];
            EquipItemSocket hand = hands[side];
            var fit = prefab.GetComponent<GauntletFit>();
            if (forearm == null || hand == null || fit == null) return null;

            GameObject instance = Instantiate(prefab, forearm);
            EquipItemSocket.Sanitize(instance);
            ForearmSeat.Apply(instance, forearm, hand.Socket, hand.GripRotation, side == 0, instance.GetComponent<GauntletFit>());
            WornAnchor.Pin(instance, forearm);
            return instance;
        }

        private void Strip(int index)
        {
            GameObject instance = instances[index];
            instances[index] = null;
            if (instance == null) return;

            if (instance.TryGetComponent(out UsableItem usable)) usable.OnUnequipped(gameObject);
            // DestroyImmediate outside play mode: an EditMode test and an editor build both strip gear,
            // and Destroy there is an error.
            if (Application.isPlaying) Destroy(instance);
            else DestroyImmediate(instance);
        }

        private void ResolveRig()
        {
            if (rigResolved) return;
            rigResolved = true;

            Animator animator = GetComponentInChildren<Animator>(true);
            spine = BoneResolver.Resolve(animator, transform, HumanBodyBones.Spine, WornBones.BackHints);
            forearms[0] = BoneResolver.Resolve(animator, transform, HumanBodyBones.LeftLowerArm, WornBones.LeftForearmHints);
            forearms[1] = BoneResolver.Resolve(animator, transform, HumanBodyBones.RightLowerArm, WornBones.RightForearmHints);
            hands[0] = Socket(animator, BoneResolver.Resolve(animator, transform, HumanBodyBones.LeftHand, WornBones.LeftHandHints), right: false);
            hands[1] = Socket(animator, BoneResolver.Resolve(animator, transform, HumanBodyBones.RightHand, WornBones.RightHandHints), right: true);
        }

        // The hand's grip frame is read for its thumb side only (ForearmSeat); nothing is parented to it.
        private static EquipItemSocket Socket(Animator animator, Transform hand, bool right) =>
            hand != null ? new EquipItemSocket(hand, HandGripFrame.Derive(animator, hand, right)) : null;

        // ── Stowed while riding ────────────────────────────────────────────────

        /// <summary>Hide or show the worn torso item. Presentation; every machine.</summary>
        public void SetTorsoShown(bool shown)
        {
            torsoShown = shown;
            ApplyTorsoShown();
        }

        /// <summary>Re-read whether a carrier above this body stows its torso gear (IStowsTorsoGear).</summary>
        public void RefreshStowed() =>
            SetTorsoShown(transform.parent == null || transform.parent.GetComponentInParent<IStowsTorsoGear>() == null);

        private void ApplyTorsoShown()
        {
            GameObject instance = instances[Torso];
            if (instance == null) return;
            foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true)) r.enabled = torsoShown;
        }

        // ── Aim (INpcAim) ──────────────────────────────────────────────────────

        public void AimAt(Vector3 point)
        {
            aimPoint = point;
            hasAimPoint = true;
        }

        public void ClearAim() => hasAimPoint = false;

        public Vector3 FireOrigin(BodySlot slot) =>
            instances[(int)slot] != null ? instances[(int)slot].transform.position : transform.position;

        // ── Peers: presentation of a worn use ──────────────────────────────────

        private void OnItemUsedElsewhere(in NetArg arg, ulong sender)
        {
            if (Network.Simulates(this)) return;
            GearRef slot = UseSlotCode.Decode(arg.A);
            if (!slot.IsBody) return;   // the hand's, EntityEquipmentController's
            UsableIn(slot.Slot)?.PlayUse(gameObject, arg);
        }

        public override void OnDestroy()
        {
            for (int i = 0; i < instances.Length; i++) Strip(i);
            // Disposes the NetworkVariables and deregisters the behaviour (BodyEquipmentNetwork.OnDestroy).
            base.OnDestroy();
        }
    }
}
```

`Strip` in `OnDestroy` is there for `OnUnequipped`, not for the destroy: the child instances would go with the GameObject anyway, but each worn item must hear that it was taken off. Typecheck may flag `NetMsg` / `NetOn` / `UseSlotCode` namespaces: `NetMsg` and `NetOn` are in `SpaceGame.Core`, `UseSlotCode` is in `SpaceGame.Items`.

`IsNpcUsable` is a stub (`=> false`) so Task 5 compiles without Task 7's `InventoryItem.npcUsable`. Task 7 Step 4 replaces the whole line, comment included.

- [ ] **Step 6: Typecheck and run**

`py rt.py 'EntityBodyEquipmentTests|WornSeatTests|WornAnchorTests|BodyEquipment'`. Expected: all pass.

- [ ] **Step 7: Docs.** In BodyEquipment.md:
  - Add `Assets/Game/Scripts/agents/entity/EntityBodyEquipment.cs`, `Items/Body/WornBones.cs` and `Items/Body/IStowsTorsoGear.cs` to `paths`.
  - Model: an "NPC counterpart" paragraph: server-decided, replicated as three item ids, seats with the shared seat half, torso folded (D3) through `WornFit` folded fields + `WornSeat.ApplyWithoutRig`, hidden under an `IStowsTorsoGear` carrier.
  - Key types rows for `EntityBodyEquipment`, `WornBones` and `IStowsTorsoGear`.
  - Gotchas:
    - "A save that says a slot is empty must win over `startingWorn`, or a looted pack grows back on reload: `written` guards `WearStarting`".
    - "`WornAnchor.Pin`, not `Follow`, on an NPC: there is no live rail to re-derive from".
  - Bump `updated:` and run `docs_check.py --index`.

- [ ] **Step 8: Report the exact files changed.**

---

### Task 6: `npcWorn` saver and worn gear dropped on death

**Files:**
- Create: `Assets/Game/Scripts/Core/Persistence/Adapters/EntityBodyEquipmentSaveable.cs`
- Modify: `Assets/Game/Scripts/Core/Persistence/Runtime/SaveablePolicy.cs` (`EnsureAgentCombat`, beside the `EntityEquipmentSaveable` clause `:452-457`)
- Modify: `Assets/Game/Scripts/agents/entity/EntityLootTable.cs` (`Drop` `:65-81`)
- Test: `Assets/Game/Editor/Tests/EntityBodyEquipmentPersistenceTests.cs`; regression `RemainsTests`, `EntityPersistenceTests`
- Docs: `docs/AI/systems/BodyEquipment.md`, `docs/AI/systems/Persistence.md`, `docs/AI/systems/Inventory.md`

**Interfaces:**
- Consumes (Task 5): `ItemIn`, `RestoreWorn`, `TakeAllWorn`.
- Produces: `EntityBodyEquipmentSaveable.Key = "npcWorn"`, with state `{ items: [id, id, id] }` (an empty string means nothing worn).

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Game/Editor/Tests/EntityBodyEquipmentPersistenceTests.cs
// What an NPC wears survives a save, and comes off with the bag when it dies — once.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Tests
{
    public class EntityBodyEquipmentPersistenceTests
    {
        private readonly List<Object> junk = new();
        private IWorldService previousWorld;
        private IItemDropService previousDrops;
        private InstantiatingWorld world;

        [SetUp]
        public void SetUp()
        {
            previousWorld = GameServices.World;
            previousDrops = GameServices.ItemDropService;
            world = new InstantiatingWorld(junk);
            GameServices.World = world;
            GameServices.ItemDropService = new PlayerDropService();
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.World = previousWorld;
            GameServices.ItemDropService = previousDrops;
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private static InventoryItem Pack => EntityBodyEquipmentTests.Asset<InventoryItem>(EntityBodyEquipmentTests.WingPackPath);

        [Test]
        public void WornGear_RoundTripsThroughTheSave()
        {
            GameObject a = EntityBodyEquipmentTests.Npc(junk);
            a.GetComponent<EntityBodyEquipment>().TryWear(Pack);
            var saverA = a.AddComponent<EntityBodyEquipmentSaveable>();
            JObject saved = JObject.FromObject(saverA.CaptureState(), SaveSerializer.Serializer);

            GameObject b = EntityBodyEquipmentTests.Npc(junk);
            b.AddComponent<EntityBodyEquipmentSaveable>().RestoreState(saved);

            Assert.AreEqual(EntityBodyEquipmentSaveable.Key, saverA.SaveKey);
            Assert.AreEqual(Pack, b.GetComponent<EntityBodyEquipment>().ItemIn(BodySlot.Torso));
        }

        [Test]
        public void AnEmptySave_StripsTheBody()
        {
            GameObject a = EntityBodyEquipmentTests.Npc(junk);
            JObject saved = JObject.FromObject(a.AddComponent<EntityBodyEquipmentSaveable>().CaptureState(), SaveSerializer.Serializer);

            GameObject b = EntityBodyEquipmentTests.Npc(junk);
            b.GetComponent<EntityBodyEquipment>().TryWear(Pack);
            b.AddComponent<EntityBodyEquipmentSaveable>().RestoreState(saved);

            Assert.IsNull(b.GetComponent<EntityBodyEquipment>().ItemIn(BodySlot.Torso));
        }

        [Test]
        public void ThePolicy_GivesAWearerItsSaver()
        {
            GameObject npc = EntityBodyEquipmentTests.Npc(junk);
            npc.AddComponent<HealthComponent>();
            SaveablePolicy.Ensure(npc, out _);
            Assert.IsNotNull(npc.GetComponent<EntityBodyEquipmentSaveable>());
        }

        [Test]
        public void ADeadWearer_DropsItsPackOnce_AndARestoredCorpseDropsNothing()
        {
            GameObject npc = EntityBodyEquipmentTests.Npc(junk);
            var health = npc.AddComponent<HealthComponent>();
            var loot = npc.AddComponent<EntityLootTable>();
            foreach (string m in new[] { "Awake", "OnEnable" })
                typeof(EntityLootTable).GetMethod(m, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(loot, null);
            var body = npc.GetComponent<EntityBodyEquipment>();
            body.TryWear(Pack);

            health.Damage(999);
            int drops = world.Spawned.Count(go => go.name.StartsWith(Pack.itemPrefab.name));
            Assert.AreEqual(1, drops, "the wing pack did not drop exactly once");
            Assert.IsNull(body.ItemIn(BodySlot.Torso), "the corpse still wears the pack it dropped");

            body.TryWear(Pack);                // as a restore would put it back on the corpse
            health.RestoreHealth(0);           // a load restoring the body at zero health raises OnDeath
            Assert.AreEqual(1, world.Spawned.Count(go => go.name.StartsWith(Pack.itemPrefab.name)),
                            "a reload dropped the pack a second time");
        }
    }
}
```

`PlayerDropService` drops through `GameServices.World.Spawn(item.itemPrefab, …)`, so the spawned instance is named `<prefab>(Clone)`, hence `StartsWith`. If `DropItem` names its spawn differently, assert on `world.Spawned.Count` before and after instead.

- [ ] **Step 2: Typecheck to see it fail.**

- [ ] **Step 3: The saver**

```csharp
// Assets/Game/Scripts/Core/Persistence/Adapters/EntityBodyEquipmentSaveable.cs
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// What an NPC wears, positional by BodySlot (append-only, like the slots themselves). Item ids only:
    /// no per-item state, because the one worn item with state — a deployed wing pack — belongs to a
    /// flier, and a flier is never saved (D4).
    /// </summary>
    public class EntityBodyEquipmentSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "npcWorn";     // written into save files — NEVER rename
        public string SaveKey => Key;

        public struct State
        {
            public List<string> items;
        }

        private EntityBodyEquipment body;

        // Lazy, not cached in Awake: EditMode tests never run Awake.
        private EntityBodyEquipment Body => body != null ? body : body = GetComponent<EntityBodyEquipment>();

        public object CaptureState()
        {
            if (Body == null) return null;

            var ids = new List<string>(GearRef.BodySlotCount);
            for (int i = 0; i < GearRef.BodySlotCount; i++)
            {
                InventoryItem item = Body.ItemIn((BodySlot)i);
                ids.Add(item != null ? item.ID : string.Empty);
            }
            return new State { items = ids };
        }

        public void RestoreState(JObject state)
        {
            // Null: nothing saved for this NPC, so the prefab's starting gear stands.
            if (state == null || Body == null) return;
            Body.RestoreWorn(GearSaveCodec.ReadItems(state["items"] as JArray, this));
        }
    }
}
```

- [ ] **Step 4: The policy clause.** In `SaveablePolicy.EnsureAgentCombat`, after the `EntityEquipmentSaveable` clause:

```csharp
            if (go.GetComponent<EntityBodyEquipmentSaveable>() == null &&
                go.GetComponent<EntityBodyEquipment>() != null)
            {
                go.AddComponent<EntityBodyEquipmentSaveable>();
                parts.Add(nameof(EntityBodyEquipmentSaveable));
            }
```

Read the surrounding method first and match its `parts` variable name.

- [ ] **Step 5: Loot.** In `EntityLootTable.Drop`, replace

```csharp
            if (dropInventoryContents && entityInventory != null) DropBag();
```

with

```csharp
            if (dropInventoryContents)
            {
                if (entityInventory != null) DropBag();
                DropWorn();
            }
```

and add:

```csharp
        /// <summary>
        /// What it was wearing, under the same guards and lifetime as the bag: a Sky nomad's wing pack is
        /// how a player gets to fly (D7). Taken off the body as it drops, so a corpse never holds a copy
        /// of what is lying beside it.
        /// </summary>
        private void DropWorn()
        {
            if (!TryGetComponent(out EntityBodyEquipment body)) return;
            foreach (InventoryItem item in body.TakeAllWorn()) DropOne(item);
        }
```

Update the `dropInventoryContents` tooltip to say "the bag's contents and everything worn".

- [ ] **Step 6: Typecheck and run**

`py rt.py 'EntityBodyEquipmentPersistenceTests|RemainsTests|EntityPersistenceTests|GearSaveCodecTests'`. Expected: all pass.

- [ ] **Step 7: Docs.**
  - BodyEquipment.md Persistence: the `npcWorn` row.
  - Persistence.md: add `EntityBodyEquipmentSaveable (npcWorn)` to the "Vitals & kit" savers row.
  - Inventory.md: in the EntityLootTable flow, "drops worn gear too (`TakeAllWorn`)". Add the symptom `"a Sky nomad I shot dropped its gun but not its wing pack"`.
  - Bump `updated:` and run `docs_check.py --index`.

- [ ] **Step 8: Report the exact files changed.**

---

### Task 7: NPCs fire opted-in worn gauntlets (D9)

**Files:**
- Modify: `Assets/Game/Scripts/Items/Core/InventoryItem.cs` (add `npcUsable` after `menacing` `:78`)
- Modify: `Assets/Game/Scripts/Items/Core/UsableItem.cs` (add `HolderAimRay()` after `aimProvider` `:84-85`)
- Create: `Assets/Game/Scripts/agents/entity/NpcItemFire.cs`, `INpcItemUser.cs`, `WornGauntletUser.cs`
- Modify: `Assets/Game/Scripts/agents/entity/EntityEquipmentController.cs` (`: INpcItemUser`; `TryUseAt` `:375-412` calls `NpcItemFire.Fire`)
- Create: `Assets/Game/Scripts/agents/Modules/Combat/ItemUseModuleBase.cs`
- Modify: `Assets/Game/Scripts/agents/Modules/Combat/NpcItemUseModule.cs`
- Create: `Assets/Game/Scripts/agents/Modules/Combat/NpcGauntletUseModule.cs`
- Modify: `Assets/Game/Scripts/agents/AI/Targeting/AgentTargeting.cs` (`:255`), `Assets/Game/Scripts/Core/Persistence/Adapters/CombatCadenceSaveable.cs` (`:85-93` and its loops), `SaveablePolicy.cs` (`:433-439`)
- Modify: `Assets/Game/Scripts/agents/entity/EntityBodyEquipment.cs` (real `IsNpcUsable`, `TryUseWornAt`)
- Modify: `Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorGauntletArtifact.cs` (`OnRequestUse` `:235-244`)
- Modify (asset): `Assets/Game/Resources/Items/Artifacts/RepulsorGauntlet.asset` (`npcUsable: 1`)
- Test: `Assets/Game/Editor/Tests/NpcGauntletUseTests.cs`, helper `Assets/Game/Editor/Tests/TestGauntletItem.cs`; regression `ClankerPrefabTests`, `RobotHorsePrefabTests`, and any class matching `CombatCadence|ItemUse|AgentTargeting`
- Docs: `docs/AI/systems/BodyEquipment.md`, `docs/AI/systems/AgentSystem.md`, `docs/AI/systems/Artifacts.md`

**Interfaces:**
- Produces:
  - `InventoryItem.npcUsable` (bool)
  - `protected Ray UsableItem.HolderAimRay()`
  - `static void NpcItemFire.Fire(Component host, UsableItem item, NetArg arg)`
  - `interface INpcItemUser { Vector3 FireOrigin { get; } void AimAt(Vector3); void ClearAim(); bool TryUseAt(Vector3); bool TryUseForward(); bool TryUseOnSelf(); }`
  - `abstract class ItemUseModuleBase : BehaviourModuleBase, IFacingModule`, whose public surface is everything `NpcItemUseModule` exposes today except `slotIndex` / `equipBeforeUse`
  - `NpcGauntletUseModule : ItemUseModuleBase` (serialized `gauntletSlot`)
  - `WornGauntletUser(EntityBodyEquipment, BodySlot)` with `IsReady`
  - `EntityBodyEquipment.TryUseWornAt(BodySlot slot, Vector3 aimPoint)`

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Game/Editor/Tests/TestGauntletItem.cs
// A gauntlet that records how it was used, for NpcGauntletUseTests. Aims like a real gadget —
// through HolderAimRay — so the test proves an NPC's aim reaches the item. Own file, so Instantiate
// keeps the script.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    public class TestGauntletItem : ToolItem
    {
        public int Uses;
        public Vector3 LastDirection;

        public override void OnRequestUse(ref NetArg arg)
        {
            Ray aim = HolderAimRay();
            arg.P = aim.origin;
            arg.R = Quaternion.LookRotation(aim.direction);
        }

        protected override void Use()
        {
            Uses++;
            LastDirection = UseArg.R * Vector3.forward;
        }
    }
}
```

```csharp
// Assets/Game/Editor/Tests/NpcGauntletUseTests.cs
// D9: an NPC fires a worn gauntlet only when the gauntlet's asset opts in, and fires it where it is
// aiming rather than along its own forearm.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    public class NpcGauntletUseTests
    {
        private readonly List<Object> junk = new();
        private GameObject npc;
        private EntityBodyEquipment body;

        [SetUp]
        public void SetUp()
        {
            npc = EntityBodyEquipmentTests.Npc(junk);
            body = npc.GetComponent<EntityBodyEquipment>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private InventoryItem Gauntlet(bool npcUsable)
        {
            var prefab = new GameObject("TestGauntlet");
            junk.Add(prefab);
            prefab.SetActive(false);
            prefab.AddComponent<GauntletFit>();
            prefab.AddComponent<TestGauntletItem>();
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            junk.Add(item);
            item.itemName = "Test Gauntlet";
            item.itemPrefab = prefab;
            item.equipKind = EquipKind.Gauntlet;
            item.npcUsable = npcUsable;
            return item;
        }

        [Test]
        public void AGauntletThatDidNotOptIn_IsNotReady()
        {
            body.TryWear(Gauntlet(npcUsable: false), BodySlot.RightGauntlet);
            Assert.IsFalse(new WornGauntletUser(body, BodySlot.RightGauntlet).IsReady);
        }

        [Test]
        public void AnOptedInGauntlet_FiresAtWhereTheNpcAims()
        {
            body.TryWear(Gauntlet(npcUsable: true), BodySlot.RightGauntlet);
            var user = new WornGauntletUser(body, BodySlot.RightGauntlet);
            Assert.IsTrue(user.IsReady);

            Vector3 target = user.FireOrigin + new Vector3(10f, 0f, 10f);
            Assert.IsTrue(user.TryUseAt(target));

            var item = body.InstanceIn(BodySlot.RightGauntlet).GetComponent<TestGauntletItem>();
            Assert.AreEqual(1, item.Uses);
            Assert.Greater(Vector3.Dot(item.LastDirection, (target - user.FireOrigin).normalized), 0.99f,
                           "the gauntlet fired along its forearm instead of at the NPC's aim");
        }

        [Test]
        public void TheGauntletModule_CountsTowardTheNpcsReach_AndSavesItsCadence()
        {
            var module = npc.AddComponent<NpcGauntletUseModule>();
            Assert.IsInstanceOf<ItemUseModuleBase>(module);
            Assert.IsFalse(module.ClaimsMovement, "a gauntlet trigger must never take the NPC's feet");
        }

        [Test]
        public void TheRepulsor_IsTheOptedInGauntlet()
        {
            Assert.IsTrue(EntityBodyEquipmentTests.Asset<InventoryItem>(EntityBodyEquipmentTests.RepulsorPath).npcUsable);
        }
    }
}
```

- [ ] **Step 2: Typecheck to see it fail.**

- [ ] **Step 3: Item data and aim**

In `InventoryItem`, after `menacing`:

```csharp
        [Tooltip("An NPC wearing this gauntlet fires it at its target (NpcGauntletUseModule). Off by default: " +
                 "most gadgets assume a player's camera, so each one opts in after it has been checked to " +
                 "work from an NPC's aim (UsableItem.HolderAimRay).")]
        public bool npcUsable;
```

In `UsableItem`, after the `aimProvider` property:

```csharp
        /// <summary>
        /// Where the holder is pointing, whoever the holder is: a player's AimProvider, else an NPC's
        /// INpcAim (EntityBodyEquipment aims its gauntlets), else this item's own forward — the old
        /// fallback, which for a worn gauntlet is the line of a dangling forearm.
        /// </summary>
        protected Ray HolderAimRay()
        {
            if (aimProvider != null) return aimProvider.GetAimRay();

            if (owner != null && owner.TryGetComponent(out INpcAim npc) && npc.HasAimPoint)
            {
                Vector3 toAim = npc.AimPoint - transform.position;
                if (toAim.sqrMagnitude > MinAimDistanceSqr) return new Ray(transform.position, toAim.normalized);
            }

            return new Ray(transform.position, transform.forward);
        }

        // An aim point closer than a centimetre names no direction.
        private const float MinAimDistanceSqr = 1e-4f;
```

In `RepulsorGauntletArtifact.OnRequestUse`, replace

```csharp
            Ray aim = aimProvider != null
                ? aimProvider.GetAimRay()
                : new Ray(transform.position, transform.forward);
```

with `Ray aim = HolderAimRay();`.

- [ ] **Step 4: The shared fire sequence**

```csharp
// Assets/Game/Scripts/agents/entity/NpcItemFire.cs
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    /// <summary>
    /// The one way an NPC uses an item, held or worn: owner hook, presentation, effect, then the peers.
    /// Moved out of EntityEquipmentController.TryUseAt when worn gauntlets needed the identical sequence.
    /// Server only (callers gate on Network.Simulates).
    /// </summary>
    public static class NpcItemFire
    {
        public static void Fire(Component host, UsableItem item, NetArg arg)
        {
            // Owner-side hook first, exactly as the player path does it: an item that describes its own
            // use (a grapple reporting where it hooks, a gauntlet reading HolderAimRay) overrides the aim.
            item.OnRequestUse(ref arg);

            // Presentation before effect, matching EquipmentController: Weapon.Present() returns early on
            // the simulating machine after its report, so this puts no second bullet in the air.
            item.PlayUse(host.gameObject, arg);
            item.TryUse(host.gameObject, arg);

            // Peers. Nothing is excluded: no other machine has presented an NPC's use locally.
            host.NetToOthers(NetMsg.ItemUsed, arg);
        }
    }
}
```

In `EntityEquipmentController.TryUseAt`, replace the four statements from `equippedUsable.OnRequestUse(ref arg);` through `this.NetToOthers(NetMsg.ItemUsed, arg);` (comments included) with `NpcItemFire.Fire(this, equippedUsable, arg);`. Change the class line to `public class EntityEquipmentController : MonoBehaviour, INpcItemUser`.

```csharp
// Assets/Game/Scripts/agents/entity/INpcItemUser.cs
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>
    /// What an item-use module drives: the NPC's hand (EntityEquipmentController) or one worn gauntlet
    /// (WornGauntletUser). The module decides WHEN; this decides how.
    /// </summary>
    public interface INpcItemUser
    {
        Vector3 FireOrigin { get; }
        void AimAt(Vector3 worldPoint);
        void ClearAim();
        bool TryUseAt(Vector3 worldAimPoint);
        bool TryUseForward();
        bool TryUseOnSelf();
    }
}
```

```csharp
// Assets/Game/Scripts/agents/entity/WornGauntletUser.cs
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    /// <summary>One worn gauntlet, as something an item-use module can fire.</summary>
    public sealed class WornGauntletUser : INpcItemUser
    {
        // A point "straight ahead" far enough that any aimed gadget reads it as a direction.
        private const float ForwardReach = 100f;

        // Below the feet, so a self-targeted use never hits whatever stands in front (EntityEquipmentController).
        private const float SelfAimDrop = 0.5f;

        private readonly EntityBodyEquipment body;
        private readonly BodySlot slot;

        public WornGauntletUser(EntityBodyEquipment body, BodySlot slot)
        {
            this.body = body;
            this.slot = slot;
        }

        public bool IsReady => body != null && body.IsNpcUsable(slot);
        public Vector3 FireOrigin => body.FireOrigin(slot);
        public void AimAt(Vector3 worldPoint) => body.AimAt(worldPoint);
        public void ClearAim() => body.ClearAim();
        public bool TryUseAt(Vector3 worldAimPoint) => body.TryUseWornAt(slot, worldAimPoint);
        public bool TryUseForward() => TryUseAt(FireOrigin + body.transform.forward * ForwardReach);
        public bool TryUseOnSelf() => TryUseAt(body.transform.position + Vector3.down * SelfAimDrop);
    }
}
```

In `EntityBodyEquipment`, replace the whole `IsNpcUsable` stub line with:

```csharp
        public bool IsNpcUsable(BodySlot slot) =>
            slot != BodySlot.Torso && worn[(int)slot] != null && worn[(int)slot].npcUsable && UsableIn(slot) != null;
```

Then add:

```csharp
        /// <summary>Fire a worn item at <paramref name="aimPoint"/>. Server only; peers see it through ItemUsed.</summary>
        public bool TryUseWornAt(BodySlot slot, Vector3 aimPoint)
        {
            UsableItem usable = UsableIn(slot);
            if (usable == null || !Network.Simulates(this)) return false;

            AimAt(aimPoint);
            Vector3 origin = FireOrigin(slot);
            Vector3 direction = aimPoint - origin;
            var arg = new NetArg
            {
                A = UseSlotCode.Encode(GearRef.Body(slot)),   // never a hand-slot number: EntityEquipmentController ignores it
                P = origin,
                R = direction.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(direction.normalized, Vector3.up) : transform.rotation,
            };
            NpcItemFire.Fire(this, usable, arg);
            return true;
        }
```

The `1e-4f` matches `EntityEquipmentController.TryUseAt`'s degenerate-direction check. Name it `MinAimDistanceSqr` as a private const in `EntityBodyEquipment`.

- [ ] **Step 5: Extract `ItemUseModuleBase` (a mechanical move)**

Create `Assets/Game/Scripts/agents/Modules/Combat/ItemUseModuleBase.cs`: `public abstract class ItemUseModuleBase : BehaviourModuleBase, IFacingModule`, header comment "Decides WHEN an NPC uses an item; a subclass says WHICH (the hand, a worn gauntlet)". **Move verbatim** from `NpcItemUseModule.cs` into it:
- the `Trigger` enum
- every `[SerializeField]` from `trigger` to `facingPriority`, **except** `slotIndex` and `equipBeforeUse`
- `ClaimsMovement`, `FacingPriority`, `MaxRange`
- the `health` / `perception` fields and every cadence and aim field (`cooldownTimer` … `facingPoint`) with `cadenceRestored`
- the profiler marker
- `Reset`, `OnEnable`, the restore getters, `RestoreCadence`, `RestoreAimTracking`
- `Tick`, the three `Tick*Trigger` methods, `BeginBurst`, `TickBurst`, `TrackTargetVelocity`, `PredictAimPoint`, `ApplySpread`, `HasLineOfSight`, `TryGetFacing`
- `OnValidate`, minus the `slotIndex` line

Then, inside the moved code:
- Replace every `equipment.` with `User.`.
- Replace `if (equipment == null) return null;` with `if (User == null) return null;`.
- Replace `Equip()` calls with `PrepareUse()`.
- Replace `equipment.HasItem` uses in the triggers with `PrepareUse()` (they already call `Equip()`).

Add:

```csharp
        /// <summary>What this module fires. Null means nothing to use; the module then does nothing.</summary>
        protected abstract INpcItemUser User { get; }

        /// <summary>Make the item ready to use now (draw it, check it is worn and opted in); false if there is nothing.</summary>
        protected abstract bool PrepareUse();

        protected virtual void Awake()
        {
            health = GetComponent<HealthComponent>();
            perception = GetComponent<PerceptionModule>();
        }
```

`NpcItemUseModule.cs` keeps:
- its header comment;
- `public class NpcItemUseModule : ItemUseModuleBase`;
- `slotIndex` and `equipBeforeUse` (unchanged names, so prefab values survive);
- `private EntityEquipmentController equipment;` and `protected override INpcItemUser User => equipment;`;
- `protected override void Awake()`, which calls `base.Awake();`, then `equipment = GetComponent<EntityEquipmentController>();` and the existing null warning;
- `protected override bool PrepareUse()`, the old `Equip()` body;
- the `ModuleDescription` override;
- `protected override void OnValidate()`, which calls `base.OnValidate();` and then clamps `slotIndex`.

```csharp
// Assets/Game/Scripts/agents/Modules/Combat/NpcGauntletUseModule.cs
// Fires a worn gauntlet the way NpcItemUseModule fires the hand item (D9): the same triggers, cadence,
// aim and facing (ItemUseModuleBase), aimed at the target through EntityBodyEquipment — and only for a
// gauntlet whose asset opts in (InventoryItem.npcUsable). Side effect: never claims movement.
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    public class NpcGauntletUseModule : ItemUseModuleBase
    {
        [Tooltip("Which forearm's gauntlet this fires.")]
        [SerializeField] private BodySlot gauntletSlot = BodySlot.RightGauntlet;

        private WornGauntletUser user;

        // Lazy as well as in Awake, so an EditMode test and a module added at runtime both have one.
        protected override INpcItemUser User => user ??= Resolve();

        public override string ModuleDescription =>
            "Fires the gauntlet worn on gauntletSlot (EntityBodyEquipment) at the target — only if its " +
            "InventoryItem has npcUsable set. Same triggers, ranges and cadence as NpcItemUseModule.";

        protected override void Awake()
        {
            base.Awake();
            user = Resolve();
            if (user == null)
                Debug.LogWarning($"{name}: NpcGauntletUseModule needs an EntityBodyEquipment to fire anything. It will do nothing.", this);
        }

        private WornGauntletUser Resolve() =>
            TryGetComponent(out EntityBodyEquipment body) ? new WornGauntletUser(body, gauntletSlot) : null;

        protected override bool PrepareUse() => user != null && user.IsReady;

        protected override void OnValidate()
        {
            base.OnValidate();
            if (gauntletSlot == BodySlot.Torso) gauntletSlot = BodySlot.RightGauntlet;
        }
    }
}
```

Note: `PrepareUse` reads the field `user`, not `User`, so it is null-safe before `Awake`. That mirrors how the base uses `User` lazily.

- [ ] **Step 6: Readers of the old type.** Change:
  - `AgentTargeting.RecomputeEffectiveRanges`: `GetComponents<NpcItemUseModule>()` → `GetComponents<ItemUseModuleBase>()`.
  - `CombatCadenceSaveable`: the field type and lazy getter → `ItemUseModuleBase[]` / `GetComponents<ItemUseModuleBase>()`; `NpcItemUseModule m` → `ItemUseModuleBase m` in its loops. The order stays positional by component order, so an NPC with no gauntlet module saves exactly as before.
  - `SaveablePolicy`'s cadence clause: `go.GetComponent<NpcItemUseModule>()` → `go.GetComponent<ItemUseModuleBase>()`.
  - The Repulsor asset: set `npcUsable` through a one-line `ux.py` snippet, `p5_repulsor.cs`:

```csharp
var item = UnityEditor.AssetDatabase.LoadAssetAtPath<SpaceGame.Items.InventoryItem>("Assets/Game/Resources/Items/Artifacts/RepulsorGauntlet.asset");
item.npcUsable = true; UnityEditor.EditorUtility.SetDirty(item); UnityEditor.AssetDatabase.SaveAssets();
return "npcUsable=" + UnityEditor.AssetDatabase.LoadAssetAtPath<SpaceGame.Items.InventoryItem>("Assets/Game/Resources/Items/Artifacts/RepulsorGauntlet.asset").npcUsable;
```

- [ ] **Step 7: Typecheck and run**

`py rt.py 'NpcGauntletUseTests|ClankerPrefabTests|RobotHorsePrefabTests|CombatCadence|EntityPersistenceTests|EntityBodyEquipmentTests|Remains'`. Expected: all pass. A Clanker or Nomad prefab must still show its `NpcItemUseModule` values in YAML (`git diff` on prefabs must be empty: moved fields keep their names).

- [ ] **Step 8: Docs.**
  - BodyEquipment.md: an "NPC gauntlet use" row, plus a Gotcha: "a gadget's no-camera fallback is its own forward, so a worn gauntlet fires along the forearm; use `HolderAimRay`".
  - AgentSystem.md:
    - Key types: `ItemUseModuleBase` / `NpcItemUseModule` / `NpcGauntletUseModule`.
    - Extending: "a new kind of thing an NPC fires is a new `INpcItemUser`, not a new module".
  - Artifacts.md: in the "make an item work for NPCs" guidance, add "aim through `HolderAimRay`, then set `npcUsable` on the asset".
  - Bump `updated:` and run `docs_check.py --index`.

- [ ] **Step 9: Report the exact files changed.**

---

### Task 8: `NpcFlightModule`: when a nomad flies, and keeping fliers out of the save

**Files:**
- Create: `Assets/Game/Scripts/Core/Persistence/Runtime/SaveScopeHold.cs`
- Create: `Assets/Game/Scripts/agents/Modules/Movement/NpcFlightModule.cs`
- Modify: `Assets/Game/Scripts/Vehicles/NpcAviation/NpcAviator.cs` (class line: `, IStowsTorsoGear`)
- Test: `Assets/Game/Editor/Tests/NpcFlightModuleTests.cs` (contains `NpcFlightModuleTests` and `SaveScopeHoldTests`)
- Docs: `docs/AI/systems/AgentSystem.md`, `docs/AI/systems/Ornithopter.md`, `docs/AI/systems/Persistence.md`

**Interfaces:**
- Consumes:
  - Task 3: `CraftDeployment.LaunchPosition`, `CraftDeployment.Retire`, `FlightLaunch.IsAirborne`.
  - Task 4: `NpcAviator.Fly(GameObject pilot, Vector3 destination, float cruiseHeight, float landingSampleDistance)`, `PilotReleased`.
  - Task 5: `EntityBodyEquipment.ItemIn(BodySlot.Torso)`.
  - Existing: `AgentGoal`, `FormationModule.LeaderOf(string)`, `NpcTaskPlanner.ResolveDestination`, `WorldSiteRegistry.TryFindNearest(..., includeAirborne: true)`, `UnseenRemoval.IsDue`, `NpcSpawn.Remove`.
- Produces:
  - `sealed class SaveScopeHold { bool Held; void Hold(GameObject); void Release(); }`
  - `NpcFlightModule : BehaviourModuleBase`, Override priority, `bool InFlight`, `bool OnSortie`, `NpcAviator Aviator`
  - Serialized fields (written by Task 9's builder): `craftPrefab`, `minFlightDistance`, `cruiseHeight`, `landingSampleDistance`, `minLaunchClearance`, `takeoffLift`, `sortieTask`, `sortieChance`

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Game/Editor/Tests/NpcFlightModuleTests.cs
// When a Sky nomad flies: a goal too far to walk and a wing pack on its back (D2), never in a fight (D2),
// alone to the shared goal (D5); a nomad that finds itself in mid-air deploys; a flier is kept out of the
// save (D4) without ever pulling a group member into it.
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Vehicles;
using SpaceGame.World;

namespace SpaceGame.Tests
{
    public class NpcFlightModuleTests
    {
        private static readonly Vector3 FarAway = new Vector3(150000f, 6000f, 150000f);
        private readonly List<Object> junk = new();
        private readonly List<string> sites = new();
        private IWorldService previousWorld;
        private InstantiatingWorld world;
        private GameObject nomad;
        private NpcFlightModule flight;
        private AgentGoal goal;

        [SetUp]
        public void SetUp()
        {
            previousWorld = GameServices.World;
            world = new InstantiatingWorld(junk);
            GameServices.World = world;

            GameObject craftPrefab = NpcAviatorTests.Craft(junk);
            craftPrefab.transform.position = FarAway + Vector3.down * 3000f;   // out of the way; only copied

            nomad = EntityBodyEquipmentTests.Npc(junk);
            nomad.transform.position = FarAway;
            nomad.AddComponent<HealthComponent>();
            nomad.AddComponent<AgentController>();
            goal = nomad.AddComponent<AgentGoal>();
            nomad.GetComponent<EntityBodyEquipment>().TryWear(EntityBodyEquipmentTests.Asset<InventoryItem>(EntityBodyEquipmentTests.WingPackPath));
            flight = nomad.AddComponent<NpcFlightModule>();
            var so = new SerializedObject(flight);
            so.FindProperty("craftPrefab").objectReferenceValue = craftPrefab;
            so.FindProperty("sortieChance").floatValue = 1f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.World = previousWorld;
            foreach (string id in sites) WorldSiteRegistry.Unregister(id);
            sites.Clear();
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private AgentContext Context(AgentTargeting targeting = null) => new AgentContext
        {
            Self = nomad.transform, Position = nomad.transform.position, Goal = goal, Targeting = targeting,
        };

        private void Ground()
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            junk.Add(block);
            block.transform.position = FarAway + Vector3.down * 0.5f;
            block.transform.localScale = new Vector3(40f, 1f, 40f);
            Physics.SyncTransforms();
        }

        // In EditMode a world-scope pilot's disown is refused with this error (SaveableEntity guard).
        private static void ExpectEditModeDisownRefusal() =>
            LogAssert.Expect(LogType.Error, new Regex("DisownToExternal\\(\\) was called .* outside play mode"));

        [Test]
        public void AGoalTooFarToWalk_DeploysTheCraft_AndSeatsTheNomad()
        {
            goal.Set(FarAway + Vector3.right * 2000f, 10f);

            MoveIntent? intent = flight.Tick(Context(), 0.02f);

            Assert.IsTrue(intent.HasValue, "the deploy frame should be claimed");
            Assert.AreEqual(1, world.Spawned.Count, "no craft was deployed");
            Assert.AreEqual(nomad, world.Spawned[0].GetComponent<NpcAviator>().Pilot);
            Assert.IsTrue(flight.InFlight);
        }

        [Test]
        public void AGoalWithinWalkingDistance_IsWalked()
        {
            Ground();
            goal.Set(FarAway + Vector3.right * 50f, 5f);

            Assert.IsFalse(flight.Tick(Context(), 0.02f).HasValue);
            Assert.IsEmpty(world.Spawned);
        }

        [Test]
        public void WithoutAWingPack_ItWalks()
        {
            nomad.GetComponent<EntityBodyEquipment>().Remove(BodySlot.Torso);
            goal.Set(FarAway + Vector3.right * 2000f, 10f);

            Assert.IsFalse(flight.Tick(Context(), 0.02f).HasValue);
            Assert.IsEmpty(world.Spawned);
        }

        [Test]
        public void InAFight_ItDoesNotTakeOff()
        {
            var enemy = new GameObject("Enemy");
            junk.Add(enemy);
            enemy.transform.position = FarAway + Vector3.forward * 10f;
            enemy.AddComponent<HealthComponent>();
            var targeting = nomad.AddComponent<AgentTargeting>();
            targeting.ForceTarget(enemy.transform);
            Assume.That(targeting.Target, Is.Not.Null, "the fixture could not give the nomad a target");
            goal.Set(FarAway + Vector3.right * 2000f, 10f);

            Assert.IsFalse(flight.Tick(Context(targeting), 0.02f).HasValue, "took off when threatened (D2 says it fights)");
            Assert.IsEmpty(world.Spawned);
        }

        [Test]
        public void ANomadInMidAir_WithNowhereToGo_DeploysToLand()
        {
            Assert.IsTrue(flight.Tick(Context(), 0.02f).HasValue);
            Assert.AreEqual(1, world.Spawned.Count);
        }

        [Test]
        public void AFollower_FliesAloneToItsLeadersGoal()
        {
            GameObject leader = EntityBodyEquipmentTests.Npc(junk);
            var leaderFormation = leader.AddComponent<FormationModule>();
            var leaderGoal = leader.AddComponent<AgentGoal>();
            leaderFormation.SetFormation("sky-wing-test", true);
            leaderGoal.Set(FarAway + Vector3.right * 2000f, 10f);
            var followerFormation = nomad.AddComponent<FormationModule>();
            followerFormation.SetFormation("sky-wing-test", false);
            // The formation registry is filled in OnEnable, which EditMode never runs.
            foreach (FormationModule f in new[] { leaderFormation, followerFormation })
                typeof(FormationModule).GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(f, null);
            Assume.That(FormationModule.LeaderOf("sky-wing-test"), Is.EqualTo(leaderFormation),
                        "the formation registry did not take the fixture's leader in EditMode");
            Ground();

            flight.Tick(Context(), 0.02f);

            Assert.AreEqual(1, world.Spawned.Count, "the follower walked a 2 km leg");
            Assert.AreEqual(leaderGoal.Position, world.Spawned[0].GetComponent<NpcAviator>().Goal);
        }

        [Test]
        public void AfterLanding_ItWaitsBeforeFlyingAgain()
        {
            goal.Set(FarAway + Vector3.right * 2000f, 10f);
            flight.Tick(Context(), 0.02f);
            NpcAviator aviator = flight.Aviator;
            typeof(NpcAviator).GetMethod("Touchdown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(aviator, new object[] { aviator.transform.position, 0f });

            Assert.IsFalse(flight.InFlight);
            Assert.IsFalse(flight.Tick(Context(), 0.02f).HasValue, "re-launched straight after landing");
            Assert.AreEqual(1, world.Spawned.Count);
        }

        [Test]
        public void APilotKilledInFlight_IsSavedAgainAsACorpse()
        {
            var entity = nomad.AddComponent<SaveableEntity>();
            ExpectEditModeDisownRefusal();
            goal.Set(FarAway + Vector3.right * 2000f, 10f);
            flight.Tick(Context(), 0.02f);

            nomad.GetComponent<HealthComponent>().Damage(999);

            Assert.IsFalse(flight.InFlight);
            Assert.IsTrue(entity.BelongsToWorld, "the corpse was left out of the save");
        }

        [Test]
        public void OnTheSkyCity_ASortiePicksAGroundSite_AndFlies()
        {
            sites.Add(WorldSiteRegistry.Register(SiteKind.Home, FarAway, 100f, WorldSite.SkyCityName, airborne: true));
            Vector3 ruin = FarAway + new Vector3(1200f, -6000f, 0f);
            sites.Add(WorldSiteRegistry.Register(SiteKind.Ruin, ruin, 20f, "Test Ruin"));
            var so = new SerializedObject(flight);
            so.FindProperty("sortieTask").FindPropertyRelative("targetSite").enumValueIndex = (int)SiteKind.Ruin;
            so.FindProperty("sortieTask").FindPropertyRelative("searchRadius").floatValue = 10000f;
            so.ApplyModifiedPropertiesWithoutUndo();

            flight.Tick(Context(), 0.02f);

            Assert.IsTrue(flight.OnSortie);
            Assert.Less(Vector2.Distance(new Vector2(goal.Position.x, goal.Position.z), new Vector2(ruin.x, ruin.z)), 25f);
            Assert.AreEqual(1, world.Spawned.Count);
        }
    }

    public class SaveScopeHoldTests
    {
        private readonly List<Object> junk = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private SaveableEntity Entity(bool world)
        {
            var go = new GameObject("Flier");
            junk.Add(go);
            var entity = go.AddComponent<SaveableEntity>();
            if (!world)
            {
                var so = new SerializedObject(entity);
                so.FindProperty("scope").enumValueIndex = (int)SaveScope.External;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return entity;
        }

        [Test]
        public void AGroupMember_IsNeverReclaimedIntoTheWorldSave()
        {
            SaveableEntity member = Entity(world: false);
            var hold = new SaveScopeHold();

            hold.Hold(member.gameObject);
            hold.Release();

            Assert.IsFalse(hold.Held);
            Assert.IsFalse(member.BelongsToWorld, "landing put a group member into the world save: it would load twice");
        }

        [Test]
        public void AWorldFlier_IsHeld_AndGivenBackOnRelease()
        {
            SaveableEntity flier = Entity(world: true);
            var hold = new SaveScopeHold();
            LogAssert.Expect(LogType.Error, new Regex("DisownToExternal\\(\\) was called .* outside play mode"));

            hold.Hold(flier.gameObject);
            Assert.IsTrue(hold.Held);
            hold.Release();

            Assert.IsFalse(hold.Held);
            Assert.IsTrue(flier.BelongsToWorld);
        }
    }
}
```

- [ ] **Step 2: Typecheck to see it fail.**

- [ ] **Step 3: `SaveScopeHold`**

```csharp
// Assets/Game/Scripts/Core/Persistence/Runtime/SaveScopeHold.cs
using UnityEngine;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keep a world-saved object out of the save for a while, and give it back exactly as it was. For a
    /// Sky nomad's flight (D4: a flier is not restored). Only an object that WAS world-scope is disowned
    /// and later reclaimed: a group member is External already — its group record owns it — and
    /// reclaiming it would save it twice and load it twice.
    /// </summary>
    public sealed class SaveScopeHold
    {
        private SaveableEntity held;

        public bool Held => held != null;

        public void Hold(GameObject target)
        {
            if (held != null || target == null) return;
            if (!target.TryGetComponent(out SaveableEntity entity) || !entity.BelongsToWorld) return;

            entity.DisownToExternal();
            held = entity;
        }

        public void Release()
        {
            if (held != null) held.ReclaimForWorld();
            held = null;
        }
    }
}
```

- [ ] **Step 4: `NpcFlightModule`**

```csharp
// Assets/Game/Scripts/agents/Modules/Movement/NpcFlightModule.cs
// Flies a Sky nomad on its wing pack. When where it is going is too far to walk — its own goal, or for a
// formation follower its leader's (D5: each pilot flies alone to the shared goal) — it deploys the
// same craft the player flies (NpcOrnithopter), rides it there and steps off; GoalTravelModule walks the
// last metres. The craft does the flying (NpcAviator); this module only decides and launches, and stops
// ticking by itself once seated (AgentController.RidesAsPassenger runs side-effect modules only).
//
// When (D2): a goal further than minFlightDistance, a wing pack worn, and room to launch — already in the
// air, or minLaunchClearance of empty sky above it (the NPC craft just climbs away: NpcFlightPlan, no
// energy model, so no ledge is needed). Never in a fight: a nomad with a target fights on foot. A nomad
// that finds itself in mid-air deploys to land.
// Off the Sky City (an airborne site) a resident now and then flies a SORTIE to a ground site; a sortie
// flier is never saved and is taken away once unseen after sortieLifetime (UnseenRemoval), so the city's
// refills cannot pile people up on the ground.
//
// Persistence (D4): a world-scope flier is disowned for the flight (SaveScopeHold) and reclaimed on
// landing; a dead pilot is reclaimed as a corpse; a group member is External throughout.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Ornithopter;
using SpaceGame.World;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class NpcFlightModule : BehaviourModuleBase
    {
        [Header("Craft")]
        [Tooltip("What is deployed: NpcOrnithopter.prefab (NpcAviator + one-seat VesselSeats).")]
        [SerializeField] private GameObject craftPrefab;

        [Header("When to fly")]
        [Tooltip("Goals nearer than this, flat metres, are walked.")]
        [SerializeField, Min(10f)] private float minFlightDistance = 250f;

        [Tooltip("Seconds after stepping off (or a refused launch) before flying again.")]
        [SerializeField, Min(0f)] private float relaunchCooldown = 20f;

        [Tooltip("Seconds between launch attempts while there is no room to launch.")]
        [SerializeField, Min(0.1f)] private float retryInterval = 2f;

        [Header("Flight")]
        [Tooltip("Height above the ground to cruise at, metres.")]
        [SerializeField, Min(5f)] private float cruiseHeight = 60f;

        [Tooltip("How far from the touchdown point the nomad may be put onto the NavMesh, metres.")]
        [SerializeField, Min(0.5f)] private float landingSampleDistance = 6f;

        [Header("Launch")]
        [Tooltip("Ground nearer than this straight down counts as standing on it, metres.")]
        [SerializeField, Min(0.1f)] private float groundClearance = 0.6f;

        [Tooltip("Craft spawned this far above the feet, metres — clear of the ground (or the city deck) it climbs away from.")]
        [SerializeField, Min(0f)] private float takeoffLift = 3f;

        [Tooltip("Height of the empty sky needed above a take-off from the ground, metres.")]
        [SerializeField, Min(1f)] private float minLaunchClearance = 12f;

        [Tooltip("Half-width of that empty sky, metres — about half the craft's 10 m span.")]
        [SerializeField, Min(1f)] private float takeoffClearRadius = 6f;

        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private PhysicsGroundProbe takeoffProbe = new PhysicsGroundProbe();

        [Header("Sorties off an airborne site (the Sky City)")]
        [Tooltip("Where a sortie goes: resolved like a task (NpcTaskPlanner), airborne sites excluded.")]
        [SerializeField] private NpcTask sortieTask = new NpcTask();

        [Tooltip("Chance per check that a resident standing on an airborne site flies a sortie.")]
        [SerializeField, Range(0f, 1f)] private float sortieChance = 0.05f;

        [SerializeField, Min(1f)] private float sortieCheckInterval = 30f;

        [Tooltip("Seconds a landed sortie flier stays before it may be taken away unseen.")]
        [SerializeField, Min(0f)] private float sortieLifetime = 300f;

        [Tooltip("No player within this, flat metres, counts as unseen.")]
        [SerializeField, Min(1f)] private float sortieUnseenDistance = 350f;

        [Tooltip("How far to look for the airborne site a resident stands on, metres.")]
        [SerializeField, Min(1f)] private float airborneSiteSearch = 300f;

        private readonly SaveScopeHold saveHold = new SaveScopeHold();
        private readonly List<Vector3> players = new();
        private EntityBodyEquipment body;
        private float nextAttempt;
        private float nextSortieCheck;
        private bool landedFromSortie;
        private float sortieRemaining;

        public NpcAviator Aviator { get; private set; }
        public bool InFlight => Aviator != null;
        public bool OnSortie { get; private set; }

        public override string ModuleDescription =>
            "Flies a far AgentGoal on the worn wing pack (NpcOrnithopter); never in a fight; deploys in mid-air; " +
            "sorties off an airborne site. Override priority, claims only the deploy frame.";

        private void Reset() => SetPriorityDefault(ModulePriority.Override);

        // Lazy: EditMode tests run no Awake.
        private EntityBodyEquipment Body => body != null ? body : body = GetComponent<EntityBodyEquipment>();

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (landedFromSortie && TickSortieExpiry(deltaTime)) return null;
            if (InFlight || Time.time < nextAttempt || !WearsWingPack()) return null;
            if (context.Targeting != null && context.Targeting.Target != null) return null;   // D2: fights on foot

            MaybeStartSortie(context.Goal);

            Vector3 feet = transform.position;
            bool airborne = FlightLaunch.IsAirborne(feet, groundClearance, groundMask);
            AgentGoal goal = GoalOf(context.Goal);
            bool far = goal != null && FlatDistance(feet, goal.Position) >= minFlightDistance;
            if (!far && !airborne) return null;

            Vector3 destination = goal != null ? goal.Position : feet;
            Vector3 heading = Flat(destination - feet, transform.forward);
            if (!TryLaunch(feet, heading, airborne, destination))
            {
                nextAttempt = Time.time + retryInterval;
                return null;
            }

            return MoveIntent.Idle();
        }

        private bool WearsWingPack()
        {
            if (craftPrefab == null || Body == null) return false;
            InventoryItem item = Body.ItemIn(BodySlot.Torso);
            return item != null && item.itemPrefab != null && item.itemPrefab.GetComponent<WingPackItem>() != null;
        }

        /// <summary>Own goal, else — for a formation follower — its leader's (D5).</summary>
        private AgentGoal GoalOf(AgentGoal own)
        {
            if (own != null && own.HasGoal) return own;
            if (!TryGetComponent(out FormationModule formation) || formation.IsLeader) return null;

            FormationModule leader = FormationModule.LeaderOf(formation.FormationId);
            return leader != null && leader.TryGetComponent(out AgentGoal theirs) && theirs.HasGoal ? theirs : null;
        }

        private bool TryLaunch(Vector3 feet, Vector3 heading, bool airborne, Vector3 destination)
        {
            if (!airborne)
            {
                takeoffProbe.IgnoreHierarchy(transform);
                if (!takeoffProbe.IsClear(feet + Vector3.up * takeoffLift, takeoffClearRadius, minLaunchClearance)) return false;
            }

            Quaternion facing = Quaternion.LookRotation(heading, Vector3.up);
            Vector3 seat = craftPrefab.GetComponent<VesselSeats>().SeatPose(0).position;
            Vector3 at = CraftDeployment.LaunchPosition(craftPrefab.transform, seat, feet, facing, takeoffLift);

            GameObject craft = GameServices.World.Spawn(craftPrefab, at, facing);
            if (craft == null) return false;
            if (!craft.TryGetComponent(out NpcAviator aviator))
            {
                Debug.LogError($"{name}: craftPrefab '{craftPrefab.name}' has no NpcAviator.", this);
                CraftDeployment.Retire(craft);
                return false;
            }

            saveHold.Hold(gameObject);
            if (!aviator.Fly(gameObject, destination, cruiseHeight, landingSampleDistance))
            {
                saveHold.Release();
                CraftDeployment.Retire(craft);
                return false;
            }

            Aviator = aviator;
            aviator.PilotReleased += OnPilotReleased;
            return true;
        }

        private void OnPilotReleased(GameObject npc, bool alive)
        {
            if (Aviator != null) Aviator.PilotReleased -= OnPilotReleased;
            Aviator = null;
            nextAttempt = Time.time + relaunchCooldown;

            // A sortie flier stays out of the save for good and is taken away unseen; anyone else is
            // given back to whatever saved them before — including a corpse, so its Remains count.
            if (OnSortie && alive)
            {
                landedFromSortie = true;
                sortieRemaining = sortieLifetime;
                return;
            }

            saveHold.Release();
        }

        private void MaybeStartSortie(AgentGoal own)
        {
            if (OnSortie || own == null || Time.time < nextSortieCheck) return;
            nextSortieCheck = Time.time + sortieCheckInterval;
            if (!OnAirborneSite() || Random.value > sortieChance) return;
            if (!NpcTaskPlanner.ResolveDestination(sortieTask, transform.position, null,
                                                   out Vector3 destination, out float arriveRadius, out string siteId, out _))
                return;

            own.Set(destination, arriveRadius, sortieTask.label, siteId);
            OnSortie = true;
        }

        private bool OnAirborneSite() =>
            WorldSiteRegistry.TryFindNearest(SiteKind.Home, transform.position, airborneSiteSearch, out WorldSite site,
                                             includeAirborne: true) &&
            site.Airborne && site.FlatDistanceTo(transform.position) <= site.Radius;

        /// <returns>True when the flier was taken away.</returns>
        private bool TickSortieExpiry(float deltaTime)
        {
            sortieRemaining -= deltaTime;
            if (!UnseenRemoval.IsDue(sortieRemaining, transform.position, sortieUnseenDistance, players)) return false;

            NpcSpawn.Remove(gameObject);
            return true;
        }

        private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static Vector3 Flat(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > MinHeadingSqr) return direction.normalized;
            fallback.y = 0f;
            return fallback.sqrMagnitude > MinHeadingSqr ? fallback.normalized : Vector3.forward;
        }

        // Below this a flat vector names no heading.
        private const float MinHeadingSqr = 1e-4f;

        private void OnDestroy()
        {
            if (Aviator != null) Aviator.PilotReleased -= OnPilotReleased;
        }
    }
}
```

On `NpcAviator`, change the class line to `public class NpcAviator : BehaviourModuleBase, IStowsTorsoGear`. `using SpaceGame.Items;` is already there for `CraftDeployment`. The pilot's folded pack now hides on every machine while it flies (Task 5's `RefreshStowed`).

- [ ] **Step 5: Typecheck and run**

`py rt.py 'NpcFlightModuleTests|SaveScopeHoldTests|NpcAviatorTests|EntityBodyEquipmentTests'`. Expected: all pass.

Two tests depend on the editor:
- `ANomadInMidAir_…` assumes the fixture is airborne at `FarAway`. If something in the open scene sits under `FarAway`, move the constant further out.
- `InAFight_…` uses `Assume`. If it reports inconclusive, give the enemy whatever `TargetResolution.IsViable` requires: read that method and add the missing component, never weaken the assertion.

- [ ] **Step 6: Docs.**
  - AgentSystem.md:
    - Key types: `NpcFlightModule`.
    - Flows: an "NPC flight" decision flow covering far goal, take-off room, not in combat, mid-air deploy and sortie.
    - Gotchas:
      - "A seated flier runs side-effect modules only, so flight-time logic lives on the craft (`NpcAviator`), never on the nomad".
      - "A sortie flier is unsaved for good and removed unseen".
    - Symptoms: `"a Sky nomad walks a two-kilometre leg instead of flying"`, `"Sky nomads pile up on the ground under the city"`.
  - Ornithopter.md: link `NpcFlightModule` from the NPC flight flow.
  - Persistence.md: a `SaveScopeHold` row ("disown for a while; never reclaims what was External").
  - Bump `updated:` and run `docs_check.py --index`.

- [ ] **Step 7: Report the exact files changed.**

---

### Task 9: Sky content — recipes, prefabs, the folded fit, the `sky-wing` group, and grounded records

**Files:**
- Modify: `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs` (`NomadRecipe` `:42-90`, `SkyNomads` `:211-219`, `SkySoldier` `:224-236`, `AddAgentStack` `:1019-1036` and the NetworkBehaviour section after `NetworkObject`, `BuildPrefab` `:485-577`)
- Modify: `Assets/Game/Editor/Agents/RosterAuthoring.cs` (new `WireSkyWing`)
- Modify: `Assets/Game/Scripts/agents/World/NpcWorldSim.cs` (`CurrentPosition` `:1144-1145`, new serialized `groundProbe`, `GroundedPosition`)
- Modify (assets): `WingPack.prefab` (`WornFit` folded fields), `SkyNomad_{Umber,Tan,Maroon,StrawHat}.prefab`, `persistentScene.unity`
- Test: `Assets/Game/Editor/Tests/SkyFlightContentTests.cs`; regression `RosterAssetTests`, `SkyFleetPrefabTests`, `StriderCityTemplateTests`, `NetworkPrefabRegistrationTests`, `SaveWiringOnDiskTests`, `WingPackStowTests`
- Docs: `docs/AI/systems/SkyTribe.md`, `docs/AI/systems/AgentSystem.md` (NpcWorldSim row)

**Interfaces:**
- Consumes:
  - `NpcOrnithopterBuilder.PrefabPath` (T4), `EntityBodyEquipment.startingWorn` (T5), `NpcGauntletUseModule.gauntletSlot` (T7).
  - `NpcFlightModule` fields (T8).
  - `StriderCityTemplateTests.ReadTemplate(id)` (existing, internal).
- Produces:
  - `NomadRecipe.FliesWithWingPack`, `NomadRecipe.JoinsGroups`, `NomadRecipe.WornGauntlet` (`InventoryItem` path or null)
  - `NomadPrefabBuilder.BuildSkyNomadAt(int index)`, `NomadPrefabBuilder.RegisterSkyNomads()`
  - `RosterAuthoring.SkyWingTemplateId = "sky-wing"`, `RosterAuthoring.WireSkyWing()`
  - `public static Vector3 NpcWorldSim.GroundedPosition(Vector3 point, float navMeshReach, PhysicsGroundProbe probe)`

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Game/Editor/Tests/SkyFlightContentTests.cs
// The Sky tribe as built: every nomad wears a folded wing pack and can fly it, the StrawHat wears the
// opted-in Repulsor, a sky-wing group hops between ground sites, and a flying group's record is put
// back on the ground (D4: members come back on foot).
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.EditorTools;
using SpaceGame.Items;
using SpaceGame.Vehicles;
using SpaceGame.World;

namespace SpaceGame.Tests
{
    public class SkyFlightContentTests
    {
        private static readonly Vector3 FarAway = new Vector3(150000f, 6000f, 150000f);

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path);
            return prefab;
        }

        [Test]
        public void EverySkyNomad_WearsTheWingPack_AndCanFlyIt()
        {
            foreach (NomadPrefabBuilder.NomadRecipe recipe in NomadPrefabBuilder.SkyNomads)
            {
                GameObject nomad = Load(recipe.PrefabPath);
                var body = nomad.GetComponent<EntityBodyEquipment>();
                Assert.IsNotNull(body, recipe.Name);
                var starting = (InventoryItem[])typeof(EntityBodyEquipment)
                    .GetField("startingWorn", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(body);
                Assert.AreEqual(EntityBodyEquipmentTests.WingPackPath, AssetDatabase.GetAssetPath(starting[0]), recipe.Name);

                var flight = nomad.GetComponent<NpcFlightModule>();
                Assert.IsNotNull(flight, recipe.Name);
                Assert.AreEqual(ModulePriority.Override, flight.Priority, recipe.Name);
                var so = new SerializedObject(flight);
                Assert.AreEqual(NpcOrnithopterBuilder.PrefabPath,
                                AssetDatabase.GetAssetPath(so.FindProperty("craftPrefab").objectReferenceValue), recipe.Name);

                Assert.IsNotNull(nomad.GetComponent<GoalTravelModule>(), $"{recipe.Name} cannot walk the last metres");
                Assert.IsNotNull(nomad.GetComponent<NpcTaskModule>(), $"{recipe.Name} cannot lead a sky-wing");
                Assert.IsNotNull(nomad.GetComponent<FormationModule>(), $"{recipe.Name} cannot follow a sky-wing");
                Assert.IsNotNull(nomad.GetComponent<SpaceGame.Core.Persistence.EntityBodyEquipmentSaveable>(),
                                 $"{recipe.Name}'s worn gear would not survive a reload");
            }
        }

        [Test]
        public void TheStrawHat_WearsTheRepulsor_AndFiresIt()
        {
            GameObject nomad = Load(NomadPrefabBuilder.SkyNomads.Single(r => r.Name.EndsWith("StrawHat")).PrefabPath);
            var body = nomad.GetComponent<EntityBodyEquipment>();
            var starting = (InventoryItem[])typeof(EntityBodyEquipment)
                .GetField("startingWorn", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(body);
            Assert.AreEqual(EntityBodyEquipmentTests.RepulsorPath, AssetDatabase.GetAssetPath(starting[(int)BodySlot.RightGauntlet]));
            Assert.IsNotNull(nomad.GetComponent<NpcGauntletUseModule>());
        }

        [Test]
        public void TheWingPack_HasAFoldedPoseForBodiesWithoutARig()
        {
            var fit = Load("Assets/Game/Prefabs/Items/Equipment/WingPack.prefab").GetComponent<WornFit>();
            Assert.IsTrue(fit.HasFoldedPose, "NPCs would wear the stowed wings hanging off nothing (D3)");
            Assert.Less(fit.FoldedSize, fit.Size, "the folded bundle is drawn at the stowed wings' size");
        }

        [Test]
        public void TheSkyWing_IsASeededGroupOfScouts_HoppingGroundSites()
        {
            NpcGroupTemplate wing = StriderCityTemplateTests.ReadTemplate(RosterAuthoring.SkyWingTemplateId);

            Assert.IsFalse(wing.runtimeOnly, "seeded at startup");
            Assert.AreEqual(RosterAuthoring.SkyFactionPath, AssetDatabase.GetAssetPath(wing.tribe));
            Assert.AreEqual(1, wing.members.Count(m => m.isLeader));
            Assert.IsTrue(wing.tasks.All(t => t.targetSite != SiteKind.Home), "a sky-wing cannot fly back up to the city");
            Assert.IsNull(wing.transport.smallVessel, "it flies on wing packs, not a vessel");
        }

        [Test]
        public void AFlyingGroupsRecord_IsPutBackOnTheGround()
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                block.transform.position = FarAway;
                block.transform.localScale = new Vector3(40f, 1f, 40f);
                Physics.SyncTransforms();

                Vector3 grounded = NpcWorldSim.GroundedPosition(FarAway + Vector3.up * 60f, 25f, new PhysicsGroundProbe());

                Assert.AreEqual(FarAway.y + 0.5f, grounded.y, 0.1f, "a fold mid-flight would respawn the group in the air");
            }
            finally { Object.DestroyImmediate(block); }
        }
    }
}
```

Make `NomadRecipe` public if it is not (`public sealed class NomadRecipe` is nested in a public static class, so it is reachable as `NomadPrefabBuilder.NomadRecipe`). `ModulePriority.Override` is 30.

- [ ] **Step 2: Typecheck to see it fail.**

- [ ] **Step 3: Ground the record.** In `NpcWorldSim`:
  - Add `[Tooltip("Finds the ground under a group whose members were in the air when it was read back (a Sky wing folded mid-flight), so it comes back on foot (D4).")] [SerializeField] private PhysicsGroundProbe groundProbe = new PhysicsGroundProbe();`.
  - Make `CurrentPosition` an instance method:

```csharp
        private Vector3 CurrentPosition(NpcGroup group) =>
            !group.Delivered && group.Transport != null
                ? group.Transport.transform.position
                : GroundedPosition(Centroid(group), spawnSampleDistance, groundProbe);

        /// <summary>
        /// <paramref name="point"/>, or the ground straight under it when there is no NavMesh within
        /// <paramref name="navMeshReach"/> — the members' centroid is mid-air while they fly.
        /// </summary>
        public static Vector3 GroundedPosition(Vector3 point, float navMeshReach, PhysicsGroundProbe probe)
        {
            if (NavMesh.SamplePosition(point, out _, navMeshReach, NavMesh.AllAreas)) return point;
            return probe != null && probe.TryGroundBelow(point, out Vector3 ground) ? ground : point;
        }
```

  If a caller of `CurrentPosition` is static, typecheck will say so. Make that caller an instance method too: all three are inside `NpcWorldSim`.

- [ ] **Step 4: The recipe.** In `NomadRecipe`, add:

```csharp
            /// <summary>Wears the wing pack (EntityBodyEquipment) and flies far goals on it (NpcFlightModule).</summary>
            public bool FliesWithWingPack;

            /// <summary>Can lead or follow an NpcWorldSim group: NpcTaskModule + FormationModule.</summary>
            public bool JoinsGroups;

            /// <summary>An InventoryItem asset path worn on the right forearm and fired by NpcGauntletUseModule; null for none.</summary>
            public string WornGauntlet;
```

In the `SkyNomads` Select, after `recipe.WanderReachableOnly = true;`:

```csharp
                recipe.TravelsToGoals = true;
                recipe.JoinsGroups = true;
                recipe.FliesWithWingPack = true;
                // One variant in four wears the one opted-in gauntlet: enough to meet it, not so many that
                // every Sky corpse is a Repulsor (GDC-L1-ECON-0001 — see Task 9 Step 9).
                if (variant == "StrawHat") recipe.WornGauntlet = RepulsorAssetPath;
```

Set the same three flags in `SkySoldier`'s initializer, without a gauntlet. Add the constants:
- `private const string WingPackAssetPath = "Assets/Game/Resources/Items/Artifacts/WingPack.asset";`
- `private const string RepulsorAssetPath = "Assets/Game/Resources/Items/Artifacts/RepulsorGauntlet.asset";`

In `AddAgentStack`, before `"SpaceGame.Agents.AgentController"` is added (next to the `TravelsToGoals` block):

```csharp
            if (recipe.JoinsGroups)
            {
                components.Add("SpaceGame.Agents.NpcTaskModule");
                components.Add("SpaceGame.Agents.FormationModule");
            }
            if (recipe.FliesWithWingPack) components.Add("SpaceGame.Agents.NpcFlightModule");
            if (recipe.WornGauntlet != null) components.Add("SpaceGame.Agents.NpcGauntletUseModule");
```

After `if (recipe.RandomWeapon) components.Add("SpaceGame.Agents.NpcRandomLoadout");`:

```csharp
            // A NetworkBehaviour, so after the NetworkObject it rides on.
            if (recipe.FliesWithWingPack || recipe.WornGauntlet != null) components.Add("SpaceGame.Agents.EntityBodyEquipment");
```

Add `ConfigureFlight` and call it in `BuildPrefab` right after `ConfigureGoalTravel(root, recipe);`:

```csharp
        // The ground sites a resident's sortie off the Sky City may fly to.
        private const SiteKind SortieSite = SiteKind.Ruin;
        private const float SortieSearchRadius = 1500f;
        private const float SortieArriveRadius = 12f;

        private static void ConfigureFlight(GameObject root, NomadRecipe recipe)
        {
            if (recipe.JoinsGroups)
            {
                SetPriority(root, "SpaceGame.Agents.NpcTaskModule", ModulePriority.Fallback);
                SetPriority(root, "SpaceGame.Agents.FormationModule", ModulePriority.Social);
            }

            var body = FindComponent(root, "SpaceGame.Agents.EntityBodyEquipment");
            if (body != null)
            {
                var so = new SerializedObject(body);
                SerializedProperty worn = so.FindProperty("startingWorn");
                worn.arraySize = GearRef.BodySlotCount;
                worn.GetArrayElementAtIndex((int)BodySlot.Torso).objectReferenceValue =
                    recipe.FliesWithWingPack ? AssetDatabase.LoadAssetAtPath<InventoryItem>(WingPackAssetPath) : null;
                worn.GetArrayElementAtIndex((int)BodySlot.RightGauntlet).objectReferenceValue =
                    recipe.WornGauntlet != null ? AssetDatabase.LoadAssetAtPath<InventoryItem>(recipe.WornGauntlet) : null;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (recipe.WornGauntlet != null)
            {
                var gauntlet = FindComponent(root, "SpaceGame.Agents.NpcGauntletUseModule");
                var so = new SerializedObject(gauntlet);
                SetInt(so, "priority", ModulePriority.RangedAttack);
                SetEnum(so, "gauntletSlot", (int)BodySlot.RightGauntlet);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (!recipe.FliesWithWingPack) return;
            var flight = FindComponent(root, "SpaceGame.Agents.NpcFlightModule");
            var fso = new SerializedObject(flight);
            SetInt(fso, "priority", ModulePriority.Override);
            SetObject(fso, "craftPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(NpcOrnithopterBuilder.PrefabPath));
            SerializedProperty task = fso.FindProperty("sortieTask");
            task.FindPropertyRelative("label").stringValue = "flying down to look around";
            task.FindPropertyRelative("targetSite").enumValueIndex = (int)SortieSite;
            task.FindPropertyRelative("searchRadius").floatValue = SortieSearchRadius;
            task.FindPropertyRelative("arriveRadius").floatValue = SortieArriveRadius;
            task.FindPropertyRelative("weight").floatValue = 1f;
            fso.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetPriority(GameObject root, string typeName, int priority)
        {
            var so = new SerializedObject(FindComponent(root, typeName));
            SetInt(so, "priority", priority);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
```

`SetInt` / `SetEnum` / `SetObject` / `FindComponent` exist at `:1875-1911`. Check their exact signatures and adapt the calls (e.g. whether `SetEnum` takes an int or an enum).

Add the per-prefab entry points (the existing menu items stay as they are):

```csharp
        /// <summary>
        /// Build ONE Sky nomad — for an agent that must not bake four prefabs in one editor call (the
        /// editor ran out of memory doing that). Follow the last one with <see cref="RegisterSkyNomads"/>.
        /// </summary>
        public static GameObject BuildSkyNomadAt(int index)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            NomadRecipe recipe = SkyNomads[index];
            return EnsureHumanoidImport(recipe.FbxPath) ? BuildPrefab(recipe) : null;
        }

        /// <summary>The registrations every freshly built nomad needs: network list, savers, ragdolls.</summary>
        public static void RegisterSkyNomads() => RegisterBuiltNomads();
```

- [ ] **Step 5: The folded fit on `WingPack.prefab` (surgical, never a builder).** Starting values:
  - `foldedSize = 1.26`, the in-hand `holdSize`, so the bundle on the back is the same object as the one in the hand;
  - `foldedLocalPosition = (0, 0.1, -0.3)`, behind the spine bone, clear of the 3 m body;
  - `foldedLocalEuler = (0, 0, 90)`, standing on edge along the spine.

  Write `$SCRATCH/p5_wingpack_fold.cs`:

```csharp
const string path = "Assets/Game/Prefabs/Items/Equipment/WingPack.prefab";
var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
try {
  var fit = root.GetComponent<SpaceGame.Items.WornFit>();
  var so = new UnityEditor.SerializedObject(fit);
  so.FindProperty("foldedSize").floatValue = 1.26f;
  so.FindProperty("foldedLocalPosition").vector3Value = new UnityEngine.Vector3(0f, 0.1f, -0.3f);
  so.FindProperty("foldedLocalEuler").vector3Value = new UnityEngine.Vector3(0f, 0f, 90f);
  so.ApplyModifiedPropertiesWithoutUndo();
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
} finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
var back = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path).GetComponent<SpaceGame.Items.WornFit>();
return $"folded={back.HasFoldedPose} size={back.FoldedSize} pos={back.FoldedLocalPosition}";
```

  Then `git diff Assets/Game/Prefabs/Items/Equipment/WingPack.prefab` must show **only** the three new fields. Ornithopter.md warns that `prefabId`, `GlobalObjectIdHash`, `confinedToSurfaces` and `RigidbodySaveable` were lost by rebuilds. If anything else changed, `git checkout` the file and report.

- [ ] **Step 6: Build the four prefabs, one call each.** Snapshot `git status --porcelain > "$SCRATCH/p5_status_before.txt"`. For `i` in 0..3, write `$SCRATCH/p5_build_sky_i.cs` containing
  `var p = SpaceGame.EditorTools.NomadPrefabBuilder.BuildSkyNomadAt(<i>); return p != null ? p.name : "FAILED";`
  and run each with `py ux.py`, one at a time. Then one call:
  `SpaceGame.EditorTools.NomadPrefabBuilder.RegisterSkyNomads(); return "registered";`
  Diff against the snapshot and restore unrelated churn on files that were clean before.

- [ ] **Step 7: Look at the folded pack on a nomad (D3 visual check, one render).** Write `$SCRATCH/p5_render_fold.cs`:

```csharp
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Game/Prefabs/Agents/Characters/SkyTribe/SkyNomad_Maroon.prefab");
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
var go = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, scene);
var body = go.GetComponent<SpaceGame.Agents.EntityBodyEquipment>();
typeof(SpaceGame.Agents.EntityBodyEquipment).GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(body, null);
var camGo = new UnityEngine.GameObject("Cam"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
var cam = camGo.AddComponent<UnityEngine.Camera>(); cam.scene = scene; cam.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
cam.backgroundColor = UnityEngine.Color.gray; cam.fieldOfView = 40f;
var lightGo = new UnityEngine.GameObject("Light"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene);
var light = lightGo.AddComponent<UnityEngine.Light>(); light.type = UnityEngine.LightType.Directional; lightGo.transform.rotation = UnityEngine.Quaternion.Euler(40, 150, 0);
var rt = new UnityEngine.RenderTexture(768, 768, 24); cam.targetTexture = rt;
var outDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "claude", "C--Users-tobia-Documents-spaceGame-SpaceGame", "ab2a1a7b-473e-4041-a764-18ee3b609e3d", "scratchpad");
foreach (var (name, pos) in new[] { ("back", new UnityEngine.Vector3(0, 2.2f, -6f)), ("side", new UnityEngine.Vector3(6f, 2.2f, 0)) }) {
  cam.transform.position = pos; cam.transform.LookAt(new UnityEngine.Vector3(0, 1.6f, 0)); cam.Render();
  UnityEngine.RenderTexture.active = rt; var tex = new UnityEngine.Texture2D(768, 768); tex.ReadPixels(new UnityEngine.Rect(0, 0, 768, 768), 0, 0); tex.Apply();
  System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, $"p5_fold_{name}.png"), tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
}
UnityEngine.RenderTexture.active = null; cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt);
UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
return "rendered";
```

  Read `$SCRATCH/p5_fold_back.png` and `p5_fold_side.png`. The bundle must sit on the upper back, not sink into the body, and not float more than a hand's width off it.
  - If it is wrong, adjust only the three folded values (Step 5 snippet) and re-render.
  - Stop after **three** adjustments and report the images to the user: placement is an art call (D3).

- [ ] **Step 8: Wire the `sky-wing` group.** In `RosterAuthoring`, add constants and a menu:

```csharp
        public const string SkyWingTemplateId = "sky-wing";

        // Folded, the record travels at the craft's cruise ground speed: Spike 5.2a E4 cruised 22.7–28.3 m/s.
        private const float SkyWingFoldedSpeed = 22f;
        private const float SkyWingSearchRadius = 1500f;
        private const float SkyWingArriveRadius = 12f;
        private static readonly Vector2 SkyWingDwell = new Vector2(60f, 180f);
        private static readonly (string label, SiteKind site, string[] chatter)[] SkyWingStops =
        {
            ("looking over a ruin", SiteKind.Ruin, new[] { "Saw this from the city. Worth a look." }),
            ("picking through a scrap field", SiteKind.ScrapField, new[] { "Light pieces only. We have to fly it home." }),
            ("visiting a camp", SiteKind.Camp, new[] { "Ground folk. Keep your wings folded and your hands empty." }),
        };

        /// <summary>
        /// A pair of Sky scouts that hop between ground sites on their wing packs — the ground-to-ground
        /// flights of D2. Seeded at startup at a Ruin; each leg too long to walk is flown (NpcFlightModule),
        /// each pilot alone to the shared goal (D5). Idempotent: rewrites its template by id.
        /// </summary>
        [MenuItem("Tools/SpaceGame/Agents/Wire Sky Wing")]
        public static void WireSkyWing()
        {
            var sky = Load<FactionDefinition>(SkyFactionPath);
            if (sky == null) return;

            WithWorldSim(sim =>
            {
                var so = new SerializedObject(sim);
                SerializedProperty templates = so.FindProperty("templates");
                int wing = -1, source = -1;
                for (int i = 0; i < templates.arraySize; i++)
                {
                    string id = templates.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue;
                    if (id == SkyWingTemplateId) wing = i;
                    if (id == "sand-nomads") source = i;
                }

                if (wing < 0)
                {
                    if (source < 0)
                    {
                        Debug.LogError("[RosterAuthoring] No 'sand-nomads' template to copy.");
                        return;
                    }
                    templates.GetArrayElementAtIndex(source).DuplicateCommand();
                    wing = source + 1;
                }

                SerializedProperty t = templates.GetArrayElementAtIndex(wing);
                t.FindPropertyRelative("id").stringValue = SkyWingTemplateId;
                t.FindPropertyRelative("displayName").stringValue = "Sky Wing";
                t.FindPropertyRelative("tribe").objectReferenceValue = sky;
                t.FindPropertyRelative("runtimeOnly").boolValue = false;
                t.FindPropertyRelative("bountyHunters").boolValue = false;
                t.FindPropertyRelative("showFromAfar").boolValue = false;
                t.FindPropertyRelative("useStartPosition").boolValue = false;
                t.FindPropertyRelative("startNearSite").enumValueIndex = (int)SiteKind.Ruin;
                t.FindPropertyRelative("initialStaySeconds").floatValue = 0f;
                t.FindPropertyRelative("travelSpeed").floatValue = SkyWingFoldedSpeed;
                SerializedProperty transport = t.FindPropertyRelative("transport");
                transport.FindPropertyRelative("smallVessel").objectReferenceValue = null;
                transport.FindPropertyRelative("largeVessel").objectReferenceValue = null;

                SerializedProperty members = t.FindPropertyRelative("members");
                members.arraySize = 0;
                AddMember(members, null, RosterRole.Scout, 1, leader: true, crew: false);
                AddMember(members, null, RosterRole.Scout, 1, leader: false, crew: false);

                SerializedProperty tasks = t.FindPropertyRelative("tasks");
                tasks.arraySize = SkyWingStops.Length;
                for (int i = 0; i < SkyWingStops.Length; i++)
                {
                    SerializedProperty task = tasks.GetArrayElementAtIndex(i);
                    task.FindPropertyRelative("label").stringValue = SkyWingStops[i].label;
                    task.FindPropertyRelative("targetSite").enumValueIndex = (int)SkyWingStops[i].site;
                    task.FindPropertyRelative("searchRadius").floatValue = SkyWingSearchRadius;
                    task.FindPropertyRelative("searchFromHome").boolValue = false;
                    task.FindPropertyRelative("dwellSeconds").vector2Value = SkyWingDwell;
                    task.FindPropertyRelative("yields").objectReferenceValue = null;
                    task.FindPropertyRelative("yieldChance").floatValue = 1f;
                    task.FindPropertyRelative("dwellFlag").stringValue = string.Empty;
                    task.FindPropertyRelative("weight").floatValue = 1f;
                    task.FindPropertyRelative("arriveRadius").floatValue = SkyWingArriveRadius;
                    task.FindPropertyRelative("travelSpeedMultiplier").floatValue = 1f;
                    SerializedProperty chatter = task.FindPropertyRelative("chatter");
                    chatter.arraySize = SkyWingStops[i].chatter.Length;
                    for (int c = 0; c < chatter.arraySize; c++)
                        chatter.GetArrayElementAtIndex(c).stringValue = SkyWingStops[i].chatter[c];
                }

                so.ApplyModifiedPropertiesWithoutUndo();
            });
        }
```

`levelGround` is copied from `sand-nomads` by the duplicate. Leave it, since nomads need no level ground. Run it with a one-line `ux.py` call: `SpaceGame.EditorTools.RosterAuthoring.WireSkyWing(); return "wired";`. Check `git diff --stat Assets/Game/Scenes/world/persistentScene.unity` and confirm only the template block changed.

- [ ] **Step 9: Design check (CLAUDE.md: economy and loot).** Read in full:
  - `docs/game-development-constitution/principles/` for `GDC-L1-ECON-0001` (sources and sinks), or whichever ECON principle INDEX.md names for loot sources;
  - `GDC-L1-SYS-0006` (legibility).

  Confirm the choices against them and write two lines in SkyTribe.md citing the IDs:
  - one Repulsor variant in four, and two sky-wing scouts;
  - every Sky corpse drops a wing pack (D7 makes the pack the only route to flight, so it must be obtainable).

  If ECON argues against a wing pack on *every* corpse, say so in the report. Do not change D7.

- [ ] **Step 10: Run the tests**

`py rt.py 'SkyFlightContentTests|RosterAssetTests|SkyFleetPrefabTests|StriderCityTemplateTests|NetworkPrefabRegistrationTests|SaveWiringOnDiskTests|WingPackStowTests|EntityPersistenceTests'`. Expected: all pass, except the known Dunehorn failure.

- [ ] **Step 11: Docs.**
  - SkyTribe.md:
    - People: "wear a folded wing pack; StrawHat wears the Repulsor".
    - A new Flows block "Flight": sortie off the moored city, the `sky-wing` group's ground hops, and "never takes off in a fight".
    - Key types: `NpcFlightModule`, `sky-wing`.
    - Persistence: "fliers are not saved; a sortie flier is gone after a load; a sky-wing comes back on foot (grounded record)".
    - Gotchas: "a group's centroid is mid-air while it flies: ground it or a fold respawns it in the sky"; "the city's NavMesh is withdrawn under way, so residents launch only while moored".
    - Extending: "another flying tribe: `FliesWithWingPack` on its recipe".
    - Symptoms: `"a Sky wing respawns falling out of the sky after I walk away and come back"`.
  - AgentSystem.md: in the NpcWorldSim row, add "record position is ground-projected (`GroundedPosition`)".
  - Bump `updated:` and run `docs_check.py --index`.

- [ ] **Step 12: Report the exact files changed**, including the four prefabs, `WingPack.prefab`, `persistentScene.unity`, the render images you looked at (scratchpad only, not repo), and any churn you restored.

---

### Task 10: Docs consolidation, stale references, and the human Play Mode checklist

> **Open, pending the user's decision (recorded 2026-10-06, Task 10):** D7 is contradicted by content that predates this phase — `PlayerCharacterNetworked.prefab` starts wearing a `WingPack`, and a container in `persistentScene.unity` holds three more. Nothing in this phase changed them; remove them or amend D7.

**Files:**
- Modify: `docs/AI/systems/Ornithopter.md` (delete the stale `OrnithopterBuilder` / `WingPackBuilder` rows and Gotchas that describe a builder which no longer exists: the "Builders" Key types row, "`WingPackBuilder` writes the `WornFit` now", "`WingPackBuilder` is STILL lossy", and the "Prefab lives under `Prefabs/agents/…`" line if it still names a builder. Replace them with one Gotcha: "`DuneOrnithopter.prefab` and `WingPack.prefab` are hand-owned (no builder): patch them through `PrefabUtility.LoadPrefabContents` and diff `prefabId` / `GlobalObjectIdHash` / `confinedToSurfaces` / `RigidbodySaveable` against git")
- Modify: `docs/superpowers/specs/2026-09-07-faction-system-design.md` §3.8 (one line: "Superseded by 2026-10-06 phase 5 design; `DuneOrnithopter` has an `AgentController`")
- Modify: `Assets/Game/Scripts/agents/Modules/Movement/WanderSaveable.cs` (stale `AirWanderSaveable` mention, `:18`), `Assets/Game/Scripts/agents/AI/Behaviours/ProvocationModule.cs` (stale `AgentRangedCombatModule` mention, `:6` → `NpcItemUseModule`). Locate both with `git ls-files | grep -i 'WanderSaveable.cs\|ProvocationModule.cs'`
- Modify: `docs/Human/the-systems.md` (the Sky Tribe entry, `:233`; one short paragraph)
- Modify: `docs/AI/DEFECTS.md`, only if its late-join folded-wings entry implies NPC craft: add one clause saying NPC craft are not affected, because their wings are derived from motion

- [ ] **Step 1: Stale code comments.** Fix the two comments named above (comment-only edits). Run `py tools/typecheck.py --editor`.

- [ ] **Step 2: Docs edits** as listed. In `the-systems.md`, under "The tribe that lives in the sky", add in plain language: Sky nomads wear a folded wing pack; now and then one steps off the moored city and flies down; a pair of scouts hops between ruins and camps by air; they shoot from the cradle; shoot one down and its pack drops and is yours to fly. Bump `updated:` on every touched system doc and run `py tools/docs_check.py --index`, which must exit 0.

- [ ] **Step 3: Final targeted regression** (never unfiltered):

`py rt.py 'NpcFlightPlanTests|NpcWingsTests|FlyingRigidbodyMotorAttitudeTests|SkyFleetDriftTests|OrnithopterFlightModelTests|OrnithopterCrashTests|OrnithopterWingAnimatorTests|OrnithopterRigWiringTests|CraftDeploymentTests|WingPackLaunchTests|WingPackStowTests|NpcAviatorTests|NpcOrnithopterPrefabTests|NpcOrnithopterFlightTests|EntityBodyEquipmentTests|EntityBodyEquipmentPersistenceTests|NpcGauntletUseTests|NpcFlightModuleTests|SaveScopeHoldTests|SkyFlightContentTests|RemainsTests|EntityPersistenceTests|ClankerPrefabTests|RosterAssetTests|SkyFleetPrefabTests|NetworkPrefabRegistrationTests|SaveWiringOnDiskTests|WornSeatTests|WornAnchorTests|NpcPassengerTests|VesselMissionTests|LandingSiteFinderTests'`

Expected: all pass, except the known `EveryRiderDrivenMount_SurvivesItsRiderDisconnecting` (Dunehorn).

Then prove the player's flight is untouched:

```bash
git diff --stat -- Assets/Game/Scripts/agents/AI/Motors/OrnithopterFlightMotor.cs Assets/Game/Scripts/agents/AI/Motors/OrnithopterFlightMotor.Replication.cs Assets/Game/Scripts/Vehicles/Ornithopter/Flight/OrnithopterFlightModel.cs Assets/Game/Scripts/Vehicles/Ornithopter/Flight/OrnithopterFlightState.cs Assets/Game/Scripts/Vehicles/Ornithopter/Flight/OrnithopterFlightConfig.cs Assets/Game/Scripts/Vehicles/Ornithopter/Flight/OrnithopterCrash.cs Assets/Game/Prefabs/Agents/Vehicles/Aircraft/DuneOrnithopter.prefab
```

Expected: no output.

- [ ] **Step 4: Hand the human Play Mode checklist to the user.** Agents cannot enter Play Mode. Paste this list into the final report verbatim. Every item must be checked on the **host and an MPPM client**, with a **late joiner** where marked:
  1. **Worn look:** Sky nomads on the city and at a sky-wing stop wear the folded pack on their backs (D3), on host and client. The StrawHat has a Repulsor on its right forearm.
  2. **Sortie off the city:** wait at the moored city (or raise `sortieChance` on a resident in the Inspector). A resident takes off from the promenade, climbing straight up and away without a ledge. On the client the wings beat and the craft banks into its turns. It lands within ~15 m of a ruin and walks. Check the pilot lies prone in the cradle on the client, not standing or face-up (the seat pose was never seen in EditMode).
  3. **Ground hop:** follow the sky-wing pair. On a long leg each takes off from flat ground and lands near the next site. They fly separately, with no formation (D5).
  4. **Shoots from the cradle (D6):** anger a pilot in flight. It fires its gun from the craft, and its shots do not hit its own craft.
  5. **Shot down (D8):** kill a pilot in flight. The body ragdolls down, and exactly one wing pack lands near it (check on the client too). The craft spirals in close by with its wings half shut, and disappears when it reaches the ground. Pick up the pack, wear it, double-tap Space off a ledge, and fly it (D7).
  6. **No hijack (D7):** a landed NPC craft cannot be mounted (it disappears at touchdown anyway).
  7. **Repulsor (D9):** provoke the StrawHat at close range. It blasts with the Repulsor toward you, not along its arm.
  8. **Save mid-flight (D4):** save while a pilot flies, then grep the world JSON. There must be no `ornithopter` record for an NPC craft, and no record for a sortie flier. Reload: the flier and its craft are gone and the sky-wing comes back on foot on the ground. A landed nomad keeps its pack (`npcWorn` present in JSON for city residents).
  9. **Save, reload, kill:** after a reload, kill a pack-wearing nomad. The pack drops once.
  10. **Late joiner:** join while a pilot is airborne. The joiner sees the craft with its wings beating (derived from motion, so there is no late-join gap), the nomad inside, and no folded pack on its back. Packs on standing nomads show for the late joiner.
  11. **Walk away mid-flight:** leave a flying sky-wing beyond 350 m and come back. No empty craft is left flying, and the pair is on the ground.
  12. **Player flight unchanged:** wear your own wing pack and fly as before. It still stalls, glides and flaps on stamina exactly as it did, because none of the player's flight code changed.
  13. **Crash:** watch a pilot whose goal is behind a cliff. If it clips terrain, the craft ends there and the pilot is set down hurt rather than flying through the rock.

- [ ] **Step 5: Report** the exact files changed in this task, plus the consolidated list of every file changed by Tasks 1–10, ready for the user to commit.

---


## Self-review (done while writing; recorded for the executor)

- **User decision applied.** NPC flight is the existing `FlyingRigidbodyMotor` (opt-in bank/pitch) steered by `NpcAviator` + the pure `NpcFlightPlan`. It has no energy model and adds no AI channel. The player's motor, flight model and `DuneOrnithopter.prefab` are never edited (Global Constraints; guarded in Task 10 Step 3).
- **Spec coverage:**
  - Design 1 → Tasks 5, 6, 9.
  - Design 2 (D9) → Tasks 7, 9.
  - Design 3:
    - flying → Tasks 1, 2, 4;
    - deploy and land → Tasks 3, 4, 8;
    - the decision to fly (D2) → Tasks 8, 9;
    - shooting (D6) needs no code, because side-effect modules already tick for passengers; it is checklist item 4;
    - killed in flight (D8) → Task 1 (`Wreck`), Tasks 4 and 6;
    - no hijack (D7) → Task 4 (no `MountModule`, pinned by `NpcOrnithopterPrefabTests`);
    - Sky City launch → the Task 8 sortie and Task 9.
  - Multiplayer → Global Constraints, Tasks 2, 4 and 5, and checklist items 2 and 10.
  - Persistence → Tasks 4, 6, 8 and 9, and checklist items 8 and 9.
- **Placeholders.**
  - The `IsNpcUsable` stub in Task 5 is deliberate and is replaced in Task 7 Step 4.
  - Steps that say "check the signature" name the file and line to read: `SerializedFields.Set`, the `NomadPrefabBuilder` helpers and `PrefabIdHash`.
- **Type consistency, as used across tasks:**
  - `NpcAviator.Fly(GameObject, Vector3, float, float)`
  - `PilotReleased(GameObject, bool)`
  - `NpcAviator` private `Touchdown(Vector3, float)` and `Crash(Vector3, Vector3)`, both reflected in tests
  - `NpcFlightPlan.Step(Vector3, float, Vector3, float)`
  - `CraftDeployment.LaunchPosition(Transform, Vector3, Vector3, Quaternion, float)`
  - `EntityBodyEquipment.TryUseWornAt(BodySlot, Vector3)`
  - `SaveScopeHold.Hold/Release/Held`
  - `NpcWorldSim.GroundedPosition(Vector3, float, PhysicsGroundProbe)`
  - `RosterAuthoring.SkyWingTemplateId`
- **Review Focus:** all five lines have a named test or check in their owning task.
