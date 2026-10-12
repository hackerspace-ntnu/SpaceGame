# Simulation Distance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** NPCs and animals far from every player freeze in place, so nothing fights where nobody can see; machines (sky fleet, Strider houses, barges, craft) keep moving.

**Architecture:** A server-only `SimulationRange` on the `NpcWorldSim` GameObject ticks every 0.5 s: it advances a set of awake 125 m cells (wake ≤ `NpcWorldSim.SpawnRadius`, sleep > `DespawnRadius`) from player positions, then writes `AgentController.Dormant` on every opted-in `DistanceDormant` agent not covered by an exemption. `Dormant` is a second parking reason beside `Offstage`, sharing one `IsParked` state. The maths and the sleep decision are pure static functions in `SimulationRules`.

**Tech Stack:** Unity 6000.3, C#, Netcode for GameObjects (server-only logic, nothing replicated), NUnit EditMode tests, Unity MCP for test runs and prefab/scene edits.

**Spec:** `docs/superpowers/specs/2026-10-06-simulation-distance-design.md` — read it before starting.

## Global Constraints

- Distances: wake when a cell's rectangle is within `NpcWorldSim.SpawnRadius` (250) of a wake source; sleep once its nearest point is beyond `NpcWorldSim.DespawnRadius` (360 in the world scene). Read, never copied.
- `cellSize` 125 m, `tickInterval` 0.5 s, `woundedWakeSeconds` 30 s — all `[SerializeField]` on `SimulationRange`. No magic numbers.
- Distances are horizontal (XZ only).
- Cell coordinates are **unclamped** `floor((pos − origin) / cellSize)`. Never use `ChunkGrid.ToCoord` (it clamps).
- Server-or-offline only (`Network.Decides`). Nothing replicated, nothing saved, no new save keys.
- `Dormant`/`Offstage` never write `enabled`.
- Opt-in: only GameObjects carrying `DistanceDormant` ever sleep. Prefab scope (case-insensitive path match — git holds both `Prefabs/Agents/` and `Prefabs/agents/`): `prefabs/agents/characters/`, `prefabs/agents/robots/`, `prefabs/agents/creatures/`. Never `prefabs/agents/vehicles/` or `prefabs/environment/`. `prefabs/spikes/` ignored.
- Exemptions (a subject never sleeps while any holds): `GroupMembership` with a group; seated under an `IAirborneCarrier`; `NpcFlightModule` in flight, airborne or on sortie; dead; hurt within `woundedWakeSeconds`; `AgentTargeting.Target` is a player.
- The per-tick loop allocates nothing (reused collections).
- No dead code, no debug logs, no copy-paste, no empty catch (CLAUDE.md).
- **Never commit.** The user commits themselves. Each task ends with a checkpoint listing changed files.
- **Shared Unity editor.** Other Claude sessions use the same editor. Before any test run, refresh, prefab write or scene save: read `mcpforunity://editor/state`, refuse if `isPlaying`, and send a heads-up via `SendMessage` to the active peers (`ListAgents`). No Play Mode without telling them first. `NomadPrefabBuilder.cs`, `RosterAuthoring.cs`, `NpcWorldSim.cs`, `NpcFlightModule.cs`, `EntityLootTable.cs` belong to session `spacegame-38`'s current work: message it before editing them and build on whatever is there.

## Review Focus

1. **A resident who is `Offstage` (indoors) while its cell also sleeps** — when the player walks up, `Dormant` clears but the resident must stay parked until its routine brings it back. Pinned in Task 2 (`OffstageSurvivesDormantClearing`).
2. **A player inside an interior** (interiors load at world origin) — the settlement they walked in from must stay awake. Pinned in Task 4 (`APlayerInsideAnInteriorWakesTheDoorTheyCameIn`, via the `wakePosition` delegate).
3. **A player standing exactly on a cell corner, or two players far apart** — union of both neighbourhoods, no gap. Pinned in Task 1 (`TwoSourcesWakeTheUnion`, `CornerDistanceIsDiagonal`).
4. **A marker disabled or destroyed while its agent is dormant** (death, despawn, scene unload) — the agent must not be left parked forever. Pinned in Task 3 (`DisablingTheMarkerReleasesTheAgent`).
5. **A seated player targeted by an NPC** (target transform is the player, but the player is parented under a mount) — still counts as hunting a player. Pinned in Task 3 (`HuntingASeatedPlayerKeepsItAwake`).

---

## File Structure

| File | Responsibility |
| --- | --- |
| Create `Assets/Game/Scripts/agents/Simulation/SimulationRules.cs` | Pure maths: cell of a position, distance to a cell, advance the awake set, the sleep decision |
| Create `Assets/Game/Scripts/agents/Simulation/DistanceDormant.cs` | Opt-in marker; static registry; last-hurt stamp; reads its own exemption inputs |
| Create `Assets/Game/Scripts/agents/Simulation/SimulationRange.cs` | Server ticker on the `NpcWorldSim` GameObject |
| Modify `Assets/Game/Scripts/agents/controller/AgentController.cs` | `Dormant`, `IsParked`, one park refresh |
| Modify `Assets/Game/Scripts/agents/AI/Targeting/AgentTargeting.cs:514` | read `IsParked` |
| Modify `Assets/Game/Scripts/agents/perception/PerceptionModule.cs:127` | read `IsParked` |
| Modify `Assets/Game/Scripts/agents/entity/IAirborneCarrier.cs` | add `AirborneSeat.IsSeatedAloft(Transform)` (extracted from `EntityLootTable`) |
| Modify `Assets/Game/Scripts/agents/entity/EntityLootTable.cs:87` | use `AirborneSeat.IsSeatedAloft` |
| Modify `Assets/Game/Scripts/agents/Modules/Movement/NpcFlightModule.cs` | public `IsAirborne` |
| Create `Assets/Game/Editor/Agents/DistanceDormancyWiring.cs` | `IsSubjectPath`, `Ensure`, menu pass |
| Modify builders (Task 5 list) | call `DistanceDormancyWiring.Ensure(root)` |
| Create tests under `Assets/Game/Editor/Tests/` | `SimulationRulesTests`, `AgentParkingTests`, `DistanceDormantTests`, `SimulationRangeTests`, `DistanceDormancyPrefabTests` |
| Modify `Assets/Game/Scenes/world/persistentScene.unity` | `SimulationRange` on the `NpcWorldSim` GameObject |
| Create `docs/AI/systems/SimulationDistance.md`; modify `docs/Human/the-systems.md`, `docs/AI/systems/AgentSystem.md` | docs |

All runtime files: namespace `SpaceGame.Agents`, Assembly-CSharp (no asmdef in `agents/`). Tests: namespace `SpaceGame.EditorTools`, the Editor assembly, the same style as `Assets/Game/Editor/Tests/WarPartyRulesTests.cs`.

**Running tests:** read `mcpforunity://editor/state` (not playing), heads-up to peers, then `mcp__UnityMCP__run_tests` with `mode: "EditMode"` and `test_filter` = the test class name; poll `mcp__UnityMCP__get_test_job`. Fast compile check without the editor: `python3 tools/typecheck.py --editor` (re-globs sources; `No errors.` expected — see Testing.md Gotchas on when it can lie).

---

### Task 1: SimulationRules — cell maths and the sleep decision

**Files:**
- Create: `Assets/Game/Scripts/agents/Simulation/SimulationRules.cs`
- Test: `Assets/Game/Editor/Tests/SimulationRulesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `static Vector2Int SimulationRules.CellOf(Vector3 position, Vector3 origin, float cellSize)`
  - `static float SimulationRules.DistanceToCell(Vector2Int cell, Vector3 point, Vector3 origin, float cellSize)`
  - `static void SimulationRules.Advance(HashSet<Vector2Int> awake, IReadOnlyList<Vector3> sources, in SimulationCells cells, List<Vector2Int> scratch)`
  - `readonly struct SimulationCells { Vector3 Origin; float CellSize; float WakeRadius; float SleepRadius; }` with ctor `(Vector3 origin, float cellSize, float wakeRadius, float sleepRadius)`
  - `readonly struct DormancyInputs { bool CellAwake, InGroup, SeatedAloft, Flying, Dead, HuntsPlayer; float SecondsSinceHurt; }` with a ctor taking them in that order
  - `static bool SimulationRules.ShouldSleep(in DormancyInputs inputs, float woundedWakeSeconds)`

- [ ] **Step 1: Write the failing tests**

```csharp
// The simulation-distance maths from the 2026-10-06 spec, without a scene.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class SimulationRulesTests
    {
        private static readonly SimulationCells Cells = new(Vector3.zero, 125f, 250f, 360f);

        [Test]
        public void CellOf_FloorsAndNeverClamps()
        {
            Assert.AreEqual(new Vector2Int(0, 0), SimulationRules.CellOf(new Vector3(10f, 50f, 124.9f), Vector3.zero, 125f));
            Assert.AreEqual(new Vector2Int(-1, -1), SimulationRules.CellOf(new Vector3(-0.1f, 0f, -0.1f), Vector3.zero, 125f));
            Assert.AreEqual(new Vector2Int(132, 0), SimulationRules.CellOf(new Vector3(16500f, 0f, 0f), Vector3.zero, 125f),
                "the arena 16.5 km east is its own cell, not the world's corner");
        }

        [Test]
        public void CellOf_HonoursTheOrigin()
        {
            Assert.AreEqual(new Vector2Int(0, 0), SimulationRules.CellOf(new Vector3(0f, 0f, -1000f), new Vector3(0f, 0f, -1000f), 125f));
        }

        [Test]
        public void DistanceToCell_IsZeroInsideAndHorizontal()
        {
            Assert.AreEqual(0f, SimulationRules.DistanceToCell(Vector2Int.zero, new Vector3(60f, 900f, 60f), Vector3.zero, 125f));
            Assert.AreEqual(75f, SimulationRules.DistanceToCell(Vector2Int.zero, new Vector3(200f, 0f, 60f), Vector3.zero, 125f), 1e-4f);
        }

        [Test]
        public void CornerDistanceIsDiagonal()
        {
            Assert.AreEqual(5f, SimulationRules.DistanceToCell(Vector2Int.zero, new Vector3(128f, 0f, 129f), Vector3.zero, 125f), 1e-4f);
        }

        [Test]
        public void CellsWithinWakeRadiusWake_AndFartherOnesDoNot()
        {
            var awake = new HashSet<Vector2Int>();
            SimulationRules.Advance(awake, new[] { new Vector3(60f, 0f, 60f) }, Cells, new List<Vector2Int>());

            Assert.IsTrue(awake.Contains(new Vector2Int(0, 0)));
            Assert.IsTrue(awake.Contains(new Vector2Int(2, 0)), "nearest point 190 m away");
            Assert.IsFalse(awake.Contains(new Vector2Int(3, 0)), "nearest point 315 m away");
        }

        [Test]
        public void AnAwakeCellStaysAwakeUntilBeyondSleepRadius()
        {
            var awake = new HashSet<Vector2Int>();
            var scratch = new List<Vector2Int>();
            SimulationRules.Advance(awake, new[] { new Vector3(60f, 0f, 60f) }, Cells, scratch);
            Assert.IsTrue(awake.Contains(new Vector2Int(2, 0)));

            // Walk 100 m west: cell (2,0)'s nearest point is now 290 m away — between wake and sleep.
            SimulationRules.Advance(awake, new[] { new Vector3(-40f, 0f, 60f) }, Cells, scratch);
            Assert.IsTrue(awake.Contains(new Vector2Int(2, 0)), "hysteresis keeps it awake");

            // Another 100 m: 390 m — beyond sleep.
            SimulationRules.Advance(awake, new[] { new Vector3(-140f, 0f, 60f) }, Cells, scratch);
            Assert.IsFalse(awake.Contains(new Vector2Int(2, 0)));
        }

        [Test]
        public void TwoSourcesWakeTheUnion()
        {
            var awake = new HashSet<Vector2Int>();
            SimulationRules.Advance(awake, new[] { new Vector3(60f, 0f, 60f), new Vector3(2060f, 0f, 60f) }, Cells, new List<Vector2Int>());

            Assert.IsTrue(awake.Contains(new Vector2Int(0, 0)));
            Assert.IsTrue(awake.Contains(new Vector2Int(16, 0)));
            Assert.IsFalse(awake.Contains(new Vector2Int(8, 0)));
        }

        [Test]
        public void NoSourcesPutsEveryCellToSleep()
        {
            var awake = new HashSet<Vector2Int> { Vector2Int.zero, Vector2Int.one };
            SimulationRules.Advance(awake, new Vector3[0], Cells, new List<Vector2Int>());
            Assert.AreEqual(0, awake.Count);
        }

        [Test]
        public void SleepsOnlyInASleepingCellWithNoExemption()
        {
            Assert.IsTrue(SimulationRules.ShouldSleep(Inputs(), 30f));
            Assert.IsFalse(SimulationRules.ShouldSleep(Inputs(cellAwake: true), 30f));
            Assert.IsFalse(SimulationRules.ShouldSleep(Inputs(inGroup: true), 30f));
            Assert.IsFalse(SimulationRules.ShouldSleep(Inputs(seatedAloft: true), 30f));
            Assert.IsFalse(SimulationRules.ShouldSleep(Inputs(flying: true), 30f));
            Assert.IsFalse(SimulationRules.ShouldSleep(Inputs(dead: true), 30f));
            Assert.IsFalse(SimulationRules.ShouldSleep(Inputs(huntsPlayer: true), 30f));
            Assert.IsFalse(SimulationRules.ShouldSleep(Inputs(sinceHurt: 29f), 30f));
            Assert.IsTrue(SimulationRules.ShouldSleep(Inputs(sinceHurt: 30f), 30f));
        }

        private static DormancyInputs Inputs(bool cellAwake = false, bool inGroup = false, bool seatedAloft = false,
            bool flying = false, bool dead = false, bool huntsPlayer = false, float sinceHurt = float.PositiveInfinity) =>
            new(cellAwake, inGroup, seatedAloft, flying, dead, huntsPlayer, sinceHurt);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `python3 tools/typecheck.py --editor`
Expected: FAIL — `CS0246: The type or namespace name 'SimulationCells' could not be found`.

- [ ] **Step 3: Implement**

```csharp
// Simulation distance as pure maths: which cells around the players are awake, and whether one agent
// in one cell sleeps. No scene, no components — SimulationRange applies it, the tests run it bare.
//
// Cells are the ChunkGrid idea at a finer size (125 m against 500 m streaming chunks), but unbounded
// and UNCLAMPED: ChunkGrid.ToCoord clamps to the edge chunk, which would put the minigame arena
// 16.5 km east into the world's corner cell. Distances are horizontal, like NpcWorldSim's own.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    public readonly struct SimulationCells
    {
        public readonly Vector3 Origin;
        public readonly float CellSize;
        public readonly float WakeRadius;
        public readonly float SleepRadius;

        public SimulationCells(Vector3 origin, float cellSize, float wakeRadius, float sleepRadius)
        {
            Origin = origin;
            CellSize = cellSize;
            WakeRadius = wakeRadius;
            SleepRadius = sleepRadius;
        }
    }

    /// <summary>What one subject's sleep depends on, read by <see cref="DistanceDormant.Read"/>.</summary>
    public readonly struct DormancyInputs
    {
        public readonly bool CellAwake;
        public readonly bool InGroup;
        public readonly bool SeatedAloft;
        public readonly bool Flying;
        public readonly bool Dead;
        public readonly bool HuntsPlayer;
        public readonly float SecondsSinceHurt;

        public DormancyInputs(bool cellAwake, bool inGroup, bool seatedAloft, bool flying, bool dead,
            bool huntsPlayer, float secondsSinceHurt)
        {
            CellAwake = cellAwake;
            InGroup = inGroup;
            SeatedAloft = seatedAloft;
            Flying = flying;
            Dead = dead;
            HuntsPlayer = huntsPlayer;
            SecondsSinceHurt = secondsSinceHurt;
        }
    }

    public static class SimulationRules
    {
        public static Vector2Int CellOf(Vector3 position, Vector3 origin, float cellSize) =>
            new(Mathf.FloorToInt((position.x - origin.x) / cellSize),
                Mathf.FloorToInt((position.z - origin.z) / cellSize));

        /// <summary>Horizontal distance from <paramref name="point"/> to the nearest point of the cell; 0 inside it.</summary>
        public static float DistanceToCell(Vector2Int cell, Vector3 point, Vector3 origin, float cellSize)
        {
            float minX = origin.x + cell.x * cellSize;
            float minZ = origin.z + cell.y * cellSize;
            float dx = Mathf.Max(minX - point.x, 0f, point.x - (minX + cellSize));
            float dz = Mathf.Max(minZ - point.z, 0f, point.z - (minZ + cellSize));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Moves <paramref name="awake"/> on one tick: a cell whose nearest point is beyond SleepRadius of
        /// every source leaves, a cell within WakeRadius of any source joins. The gap between the two is
        /// what stops a cell flickering as somebody walks along its edge. <paramref name="scratch"/> is
        /// the caller's, so nothing is allocated.
        /// </summary>
        public static void Advance(HashSet<Vector2Int> awake, IReadOnlyList<Vector3> sources, in SimulationCells cells,
            List<Vector2Int> scratch)
        {
            scratch.Clear();
            foreach (Vector2Int cell in awake)
                if (!AnyWithin(cell, sources, cells, cells.SleepRadius))
                    scratch.Add(cell);

            for (int i = 0; i < scratch.Count; i++)
                awake.Remove(scratch[i]);

            int reach = Mathf.CeilToInt(cells.WakeRadius / cells.CellSize);
            for (int s = 0; s < sources.Count; s++)
            {
                Vector2Int centre = CellOf(sources[s], cells.Origin, cells.CellSize);
                for (int x = -reach; x <= reach; x++)
                for (int y = -reach; y <= reach; y++)
                {
                    var cell = new Vector2Int(centre.x + x, centre.y + y);
                    if (DistanceToCell(cell, sources[s], cells.Origin, cells.CellSize) <= cells.WakeRadius)
                        awake.Add(cell);
                }
            }
        }

        public static bool ShouldSleep(in DormancyInputs inputs, float woundedWakeSeconds) =>
            !inputs.CellAwake && !inputs.InGroup && !inputs.SeatedAloft && !inputs.Flying && !inputs.Dead &&
            !inputs.HuntsPlayer && inputs.SecondsSinceHurt >= woundedWakeSeconds;

        private static bool AnyWithin(Vector2Int cell, IReadOnlyList<Vector3> sources, in SimulationCells cells, float radius)
        {
            for (int i = 0; i < sources.Count; i++)
                if (DistanceToCell(cell, sources[i], cells.Origin, cells.CellSize) <= radius)
                    return true;

            return false;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: editor state check + heads-up, then `run_tests` EditMode, filter `SimulationRulesTests`.
Expected: 9 passed.

- [ ] **Step 5: Checkpoint** — list for the user: `SimulationRules.cs`, `SimulationRulesTests.cs` (+ `.meta`s Unity generates). Do not commit.

---

### Task 2: AgentController — two parking reasons, one parked state

**Files:**
- Modify: `Assets/Game/Scripts/agents/controller/AgentController.cs` (`Offstage` at :103-122, `Simulate` at :194, `SuspendSimulation`/`ResumeSimulation` at :295-303)
- Modify: `Assets/Game/Scripts/agents/AI/Targeting/AgentTargeting.cs:202,512-515`
- Modify: `Assets/Game/Scripts/agents/perception/PerceptionModule.cs:57,125-127`
- Test: `Assets/Game/Editor/Tests/AgentParkingTests.cs`

**Interfaces:**
- Produces: `bool AgentController.Dormant { get; set; }`, `bool AgentController.IsParked { get; }`. `Offstage` keeps its signature.

- [ ] **Step 1: Write the failing tests**

```csharp
// AgentController's two parking reasons (Offstage: the residents' routine; Dormant: simulation
// distance) share one parked state, and neither writer can release the other's park.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class AgentParkingTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        [Test]
        public void DormantParksTheMotorAndNeverTouchesEnabled()
        {
            (AgentController agent, FakeMotor motor) = NewAgent();

            agent.Dormant = true;
            Assert.IsTrue(agent.IsParked);
            Assert.IsTrue(motor.Suspended);
            Assert.IsTrue(agent.enabled);

            agent.Dormant = false;
            Assert.IsFalse(agent.IsParked);
            Assert.IsFalse(motor.Suspended);
        }

        [Test]
        public void OffstageSurvivesDormantClearing()
        {
            (AgentController agent, FakeMotor motor) = NewAgent();

            agent.Offstage = true;
            agent.Dormant = true;
            agent.Dormant = false;

            Assert.IsTrue(agent.IsParked, "the routine still has it indoors");
            Assert.IsTrue(motor.Suspended);
        }

        [Test]
        public void DormantSurvivesOffstageClearing()
        {
            (AgentController agent, FakeMotor motor) = NewAgent();

            agent.Dormant = true;
            agent.Offstage = true;
            agent.Offstage = false;

            Assert.IsTrue(agent.IsParked, "still out of every player's range");
            Assert.IsTrue(motor.Suspended);
        }

        private (AgentController, FakeMotor) NewAgent()
        {
            var go = new GameObject("agent");
            spawned.Add(go);
            FakeMotor motor = go.AddComponent<FakeMotor>();
            AgentController agent = go.AddComponent<AgentController>();
            typeof(AgentController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(agent, null);
            return (agent, motor);
        }

        private class FakeMotor : MonoBehaviour, IMovementMotor, ISelfDrivingMotor
        {
            public bool Suspended { get; private set; }

            public Vector3 Velocity => Vector3.zero;
            public float TopSpeed => 0f;
            public bool IsImmobile => true;
            public bool HasReachedDestination => true;
            public Vector3? CurrentDestination => null;

            public void Tick(in MoveIntent intent, float deltaTime) { }
            public void ForceStop() { }

            public void SuspendSelfDrive() => Suspended = true;
            public void ResumeSelfDrive() => Suspended = false;
        }
    }
}
```

(Before writing, check the `FakeMotor` member list against `IMovementMotor.cs` — copy `LassoTests.FakeMotor`'s exact members if the interface has changed.)

- [ ] **Step 2: Run to verify it fails**

Run: `python3 tools/typecheck.py --editor`
Expected: FAIL — `'AgentController' does not contain a definition for 'Dormant'`.

- [ ] **Step 3: Implement in `AgentController.cs`**

Replace the `Offstage` property and its backing field (lines 103-122) with:

```csharp
        public bool Offstage
        {
            get => offstage;
            set
            {
                if (offstage == value)
                    return;

                bool wasParked = IsParked;
                offstage = value;
                RefreshPark(wasParked);
            }
        }

        private bool offstage;

        /// <summary>
        /// Parked because no player is near: the second reason beside <see cref="Offstage"/>, written
        /// only by <see cref="SimulationRange"/> (SimulationDistance.md). Same starvation, same parked
        /// motor; kept apart from Offstage so the residents' routine and the range can never release
        /// each other's park. Server-side, not saved, not replicated — the range re-derives it every tick.
        /// </summary>
        public bool Dormant
        {
            get => dormant;
            set
            {
                if (dormant == value)
                    return;

                bool wasParked = IsParked;
                dormant = value;
                RefreshPark(wasParked);
            }
        }

        private bool dormant;

        /// <summary>Not acting for any reason: <see cref="Offstage"/> or <see cref="Dormant"/>.</summary>
        public bool IsParked => offstage || dormant;

        // The motor parks on the first reason and unparks only when the last one clears. A watcher's
        // motor is already parked; authority returning resumes it unless still parked.
        private void RefreshPark(bool wasParked)
        {
            if (IsParked == wasParked || !simulating)
                return;

            if (IsParked) ParkMotor();
            else UnparkMotor();
        }
```

Keep the existing XML summary above `Offstage` unchanged. In `Simulate` change `if (offstage)` to `if (IsParked)` and its comment to "Not in the scene's action at all (offstage or dormant).". In `SuspendSimulation`/`ResumeSimulation` change `if (!offstage)` to `if (!IsParked)` and the comment above to "An offstage or dormant body's motor is parked already.".

In `AgentTargeting.cs`: line 202 comment → `// Read for AgentController.IsParked: an agent that is not in the scene's action acquires no one.`; lines 512-514 → comment `// Parked (indoors, asleep, out of range): …` and `if (controller != null && controller.IsParked)`.

In `PerceptionModule.cs`: line 57 comment → `// Read for AgentController.IsParked (…)`; line 125-127 → comment `// Parked: a body being placed is not "moving".` and `if (agent != null && agent.IsParked)`.

Then `grep -rn "\.Offstage" Assets/Game/Scripts --include=*.cs` — every remaining reader other than `Resident.cs` (the writer) must be judged: a reader asking "is it acting" switches to `IsParked`; the residents' own `IsOffstage` stays.

- [ ] **Step 4: Run tests**

Run: `run_tests` EditMode, filter `AgentParkingTests` then `AgentSystemResidentFeatureTests` (regression for `Offstage`).
Expected: all pass.

- [ ] **Step 5: Checkpoint** — list `AgentController.cs`, `AgentTargeting.cs`, `PerceptionModule.cs`, `AgentParkingTests.cs`.

---

### Task 3: DistanceDormant — the opt-in marker and its exemption inputs

**Files:**
- Create: `Assets/Game/Scripts/agents/Simulation/DistanceDormant.cs`
- Modify: `Assets/Game/Scripts/agents/entity/IAirborneCarrier.cs` (message spacegame-38 first)
- Modify: `Assets/Game/Scripts/agents/entity/EntityLootTable.cs:87` (message spacegame-38 first)
- Modify: `Assets/Game/Scripts/agents/Modules/Movement/NpcFlightModule.cs` (message spacegame-38 first)
- Test: `Assets/Game/Editor/Tests/DistanceDormantTests.cs`

**Interfaces:**
- Consumes: `DormancyInputs` (Task 1), `AgentController.Dormant` (Task 2).
- Produces:
  - `static IReadOnlyList<DistanceDormant> DistanceDormant.All`
  - `AgentController DistanceDormant.Agent { get; }`
  - `DormancyInputs DistanceDormant.Read(bool cellAwake, HashSet<Transform> players, float now)`
  - `static bool AirborneSeat.IsSeatedAloft(Transform body)`
  - `bool NpcFlightModule.IsAirborne { get; }`

- [ ] **Step 1: Write the failing tests**

```csharp
// What a DistanceDormant reads off its own body for the sleep decision.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class DistanceDormantTests
    {
        private readonly List<GameObject> spawned = new();
        private static readonly HashSet<Transform> NoPlayers = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        [Test]
        public void AFreshSubjectReadsAsUnhurtAndExemptFromNothing()
        {
            DistanceDormant subject = NewSubject();
            DormancyInputs inputs = subject.Read(false, NoPlayers, 100f);

            Assert.IsFalse(inputs.InGroup || inputs.SeatedAloft || inputs.Flying || inputs.Dead || inputs.HuntsPlayer);
            Assert.IsTrue(float.IsPositiveInfinity(inputs.SecondsSinceHurt));
        }

        [Test]
        public void HuntingASeatedPlayerKeepsItAwake()
        {
            DistanceDormant subject = NewSubject();
            var player = NewObject("player").transform;
            var seat = NewObject("seat").transform;
            seat.SetParent(NewObject("mount").transform);
            var body = NewObject("player body").transform;
            body.SetParent(seat);

            AgentTargeting.GetOrAdd(subject.gameObject).ForceTarget(body);
            var players = new HashSet<Transform> { body, player };

            Assert.IsTrue(subject.Read(false, players, 0f).HuntsPlayer);
        }

        [Test]
        public void HuntingAnotherNpcDoesNotKeepItAwake()
        {
            DistanceDormant subject = NewSubject();
            AgentTargeting.GetOrAdd(subject.gameObject).ForceTarget(NewObject("clanker").transform);

            Assert.IsFalse(subject.Read(false, NoPlayers, 0f).HuntsPlayer);
        }

        [Test]
        public void SeatedUnderAnAirborneCarrierIsAloft()
        {
            DistanceDormant subject = NewSubject();
            GameObject craft = NewObject("craft");
            craft.AddComponent<FakeCarrier>();
            subject.transform.SetParent(craft.transform);

            Assert.IsTrue(subject.Read(false, NoPlayers, 0f).SeatedAloft);
        }

        [Test]
        public void DisablingTheMarkerReleasesTheAgent()
        {
            DistanceDormant subject = NewSubject();
            subject.Agent.Dormant = true;

            subject.enabled = false;

            Assert.IsFalse(subject.Agent.Dormant);
            CollectionAssert.DoesNotContain((ICollection<DistanceDormant>)DistanceDormant.All, subject);
        }

        private DistanceDormant NewSubject()
        {
            GameObject go = NewObject("subject");
            AgentController agent = go.AddComponent<AgentController>();
            typeof(AgentController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(agent, null);
            DistanceDormant subject = go.AddComponent<DistanceDormant>();
            typeof(DistanceDormant).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(subject, null);
            return subject;
        }

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        private class FakeCarrier : MonoBehaviour, IAirborneCarrier { }
    }
}
```

Notes for the implementer: `DistanceDormant.All` returns the backing `List<DistanceDormant>` as `IReadOnlyList`, so the cast to `ICollection` holds. If `AgentTargeting.ForceTarget` has preconditions in EditMode (check its signature in `AgentTargeting.cs` — it may take extra args), adapt the call, not the assertion. In EditMode `OnEnable` is not run by `AddComponent`, which is why the test invokes it; the `enabled = false` in `DisablingTheMarkerReleasesTheAgent` likewise may not run `OnDisable` outside play mode — if so, invoke `OnDisable` by reflection after setting `enabled`, the same way.

- [ ] **Step 2: Run to verify it fails**

Run: `python3 tools/typecheck.py --editor`
Expected: FAIL — `DistanceDormant` not found.

- [ ] **Step 3: Message spacegame-38, then extract `AirborneSeat` and add `IsAirborne`**

`SendMessage` to `spacegame-38`: "Simulation distance Task 3: adding `AirborneSeat.IsSeatedAloft` to IAirborneCarrier.cs (extracted from EntityLootTable:87, which will call it), and a read-only `public bool IsAirborne => airborneFor > 0f;` to NpcFlightModule. No behaviour change. OK?" — wait for the reply.

`IAirborneCarrier.cs` becomes:

```csharp
// A carrier whose seat is in the air — the NPC craft. A rider killed in it falls, so EntityLootTable holds
// that body's loot until the body is down (LootAwaitingGround) rather than dropping it at the kill point,
// and simulation distance never puts it to sleep (SimulationDistance.md).
using UnityEngine;

namespace SpaceGame.Agents
{
    public interface IAirborneCarrier { }

    public static class AirborneSeat
    {
        /// <summary>Is <paramref name="body"/> seated under an <see cref="IAirborneCarrier"/>?</summary>
        public static bool IsSeatedAloft(Transform body) =>
            body.parent != null && body.parent.GetComponentInParent<IAirborneCarrier>() != null;
    }
}
```

In `EntityLootTable.cs` replace the expression at line 87 (`transform.parent != null && transform.parent.GetComponentInParent<IAirborneCarrier>() != null`) with `AirborneSeat.IsSeatedAloft(transform)`.

In `NpcFlightModule.cs`, beside `InFlight` (line 104):

```csharp
        /// <summary>Off the ground right now — a fall it may be about to deploy from. Read by simulation distance.</summary>
        public bool IsAirborne => airborneFor > 0f;
```

- [ ] **Step 4: Implement `DistanceDormant.cs`**

```csharp
// The opt-in that lets simulation distance put this NPC or animal to sleep. Only people and animals
// carry it (DistanceDormancyWiring puts it on every prefab under Agents/Characters, Robots and
// creatures); a machine never does, so the sky fleet, the Strider houses and the craft keep moving.
//
// The decision is SimulationRange's and the rule SimulationRules'; this component only reads its own
// body for the inputs, and stamps when it was last hurt so a long-range shot wakes it.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AgentController))]
    public class DistanceDormant : MonoBehaviour
    {
        private static readonly List<DistanceDormant> registered = new();

        public static IReadOnlyList<DistanceDormant> All => registered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => registered.Clear();

        public AgentController Agent { get; private set; }

        private HealthComponent health;
        private NpcFlightModule flight;
        private AgentTargeting targeting;
        private GroupMembership membership;
        private float lastHurtTime = float.NegativeInfinity;

        private void OnEnable()
        {
            Agent = GetComponent<AgentController>();
            health = GetComponent<HealthComponent>();
            flight = GetComponentInChildren<NpcFlightModule>(true);
            targeting = GetComponent<AgentTargeting>();

            if (!registered.Contains(this)) registered.Add(this);
            if (health) health.OnDamage += OnDamaged;
        }

        // Whoever turns the marker off — death, despawn, a chunk unloading — must not leave the agent
        // parked with nobody left to unpark it.
        private void OnDisable()
        {
            registered.Remove(this);
            if (health) health.OnDamage -= OnDamaged;
            if (Agent) Agent.Dormant = false;
        }

        private void OnDamaged(int amount) => lastHurtTime = Time.time;

        public DormancyInputs Read(bool cellAwake, HashSet<Transform> players, float now)
        {
            // Stamped by NpcWorldSim after this object woke (NpcSpawn's beforeSpawn), so looked up late.
            if (!membership) TryGetComponent(out membership);
            if (!targeting) TryGetComponent(out targeting);

            return new DormancyInputs(
                cellAwake,
                membership && membership.Group != null,
                AirborneSeat.IsSeatedAloft(transform),
                flight && (flight.InFlight || flight.IsAirborne || flight.OnSortie),
                health && !health.Alive,
                targeting && IsPlayer(targeting.Target, players),
                now - lastHurtTime);
        }

        // A seated player is a child of its seat, and a target may be a child collider: walk up.
        private static bool IsPlayer(Transform target, HashSet<Transform> players)
        {
            for (Transform t = target; t != null; t = t.parent)
                if (players.Contains(t))
                    return true;

            return false;
        }
    }
}
```

(Check `GroupMembership.Group`'s setter: if `Leave()` nulls it, `Group != null` is the right membership test. Check `health.OnDamage`'s delegate type is `Action<int>` — it is at `HealthComponent.cs:49`.)

- [ ] **Step 5: Run tests**

Run: `run_tests` EditMode, filters `DistanceDormantTests`, then `SimulationRulesTests`, `AgentParkingTests`, and every test class whose name contains `Loot` or `NpcFlight` (regression for the extraction).
Expected: all pass.

- [ ] **Step 6: Checkpoint** — list `DistanceDormant.cs`, `IAirborneCarrier.cs`, `EntityLootTable.cs`, `NpcFlightModule.cs`, `DistanceDormantTests.cs`.

---

### Task 4: SimulationRange — the server ticker, wired into the world

**Files:**
- Create: `Assets/Game/Scripts/agents/Simulation/SimulationRange.cs`
- Modify: `Assets/Game/Scenes/world/persistentScene.unity` (add the component to the `NpcWorldSim` GameObject)
- Test: `Assets/Game/Editor/Tests/SimulationRangeTests.cs`

**Interfaces:**
- Consumes: `SimulationRules`, `SimulationCells` (Task 1); `DistanceDormant.All`, `.Agent`, `.Read` (Task 3); `NpcWorldSim.SpawnRadius`/`DespawnRadius`; `SessionPlayers.Collect`; `InteriorManager.Instance.TryGetVisit` → `InteriorVisit.ReturnPosition`; `WorldStreamer.Config.worldOrigin`.
- Produces: `public void SimulationRange.Tick(IReadOnlyList<Transform> players, float now)`; `public System.Func<Transform, Vector3> SimulationRange.WakePosition` (defaults to interior-aware lookup; tests replace it).

- [ ] **Step 1: Write the failing tests**

```csharp
// SimulationRange applying the rule to real components, without play mode.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class SimulationRangeTests
    {
        private readonly List<GameObject> spawned = new();
        private SimulationRange range;

        [SetUp]
        public void SetUp()
        {
            GameObject host = NewObject("world sim");
            host.AddComponent<NpcWorldSim>();               // spawnRadius 250, despawnRadius 350 by default
            range = host.AddComponent<SimulationRange>();
            range.WakePosition = t => t.position;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        [Test]
        public void AnNpcFarFromEveryPlayerSleeps_AndANearOneDoesNot()
        {
            DistanceDormant far = NewSubject(new Vector3(800f, 0f, 0f));
            DistanceDormant near = NewSubject(new Vector3(100f, 0f, 0f));

            range.Tick(new[] { NewObject("player").transform }, 0f);

            Assert.IsTrue(far.Agent.Dormant);
            Assert.IsFalse(near.Agent.Dormant);
        }

        [Test]
        public void WalkingUpWakesIt()
        {
            DistanceDormant npc = NewSubject(new Vector3(800f, 0f, 0f));
            Transform player = NewObject("player").transform;

            range.Tick(new[] { player }, 0f);
            Assert.IsTrue(npc.Agent.Dormant);

            player.position = new Vector3(600f, 0f, 0f);
            range.Tick(new[] { player }, 0.5f);
            Assert.IsFalse(npc.Agent.Dormant);
        }

        [Test]
        public void APlayerInsideAnInteriorWakesTheDoorTheyCameIn()
        {
            DistanceDormant resident = NewSubject(new Vector3(800f, 0f, 0f));
            Transform player = NewObject("player").transform;   // standing at the interior's world-origin coordinates
            range.WakePosition = t => new Vector3(790f, 0f, 0f); // the door they walked in by

            range.Tick(new[] { player }, 0f);

            Assert.IsFalse(resident.Agent.Dormant);
        }

        [Test]
        public void NoPlayersPutsEverySubjectToSleep()
        {
            DistanceDormant npc = NewSubject(Vector3.zero);
            range.Tick(new Transform[0], 0f);
            Assert.IsTrue(npc.Agent.Dormant);
        }

        private DistanceDormant NewSubject(Vector3 position)
        {
            GameObject go = NewObject("npc");
            go.transform.position = position;
            AgentController agent = go.AddComponent<AgentController>();
            typeof(AgentController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(agent, null);
            DistanceDormant subject = go.AddComponent<DistanceDormant>();
            typeof(DistanceDormant).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(subject, null);
            return subject;
        }

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }
    }
}
```

(Teardown destroys the subjects; `DestroyImmediate` in EditMode does not run `OnDisable`, so also invoke `DistanceDormant.OnDisable` by reflection on each subject before destroying, or clear the registry via the `ResetStatics` method by reflection in `TearDown`, so tests do not leak subjects into each other.)

- [ ] **Step 2: Run to verify it fails**

Run: `python3 tools/typecheck.py --editor`
Expected: FAIL — `SimulationRange` not found.

- [ ] **Step 3: Implement `SimulationRange.cs`**

```csharp
// Simulation distance: NPCs and animals no player is near stop doing anything, so tribes, Clanker
// towns and wildlife do not fight where nobody can see (SimulationDistance.md).
//
// Lives beside NpcWorldSim and borrows its distances — wake at SpawnRadius, sleep beyond
// DespawnRadius — so a chunk-resident NPC and a caravan member fall asleep and wake at the same line.
// Server-or-offline, like NpcWorldSim; the clients run no agent modules anyway. Nothing is saved:
// every sleeper is re-derived on the first tick after a load.
using System;
using System.Collections.Generic;
using SpaceGame.Core;
using SpaceGame.World.Streaming;
using UnityEngine;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(NpcWorldSim))]
    public class SimulationRange : MonoBehaviour
    {
        [Tooltip("Side of one simulation cell, metres. Four to a 500 m streaming chunk.")]
        [SerializeField, Min(10f)] private float cellSize = 125f;

        [Tooltip("Seconds between decisions.")]
        [SerializeField, Min(0.05f)] private float tickInterval = 0.5f;

        [Tooltip("A sleeper that is hurt stays awake this long after the last hit, so a long-range shot " +
                 "is answered instead of landing on a statue.")]
        [SerializeField, Min(0f)] private float woundedWakeSeconds = 30f;

        /// <summary>Where a player counts from. Interior-aware by default; tests replace it.</summary>
        public Func<Transform, Vector3> WakePosition = InteriorAwarePosition;

        private readonly HashSet<Vector2Int> awake = new();
        private readonly List<Vector2Int> scratch = new();
        private readonly List<Transform> players = new();
        private readonly HashSet<Transform> playerSet = new();
        private readonly List<Vector3> sources = new();

        private NpcWorldSim sim;
        private Vector3 origin;
        private float nextTick;

        private void Start()
        {
            WorldStreamer streamer = FindFirstObjectByType<WorldStreamer>();
            origin = streamer != null && streamer.Config != null ? streamer.Config.worldOrigin : Vector3.zero;
        }

        private void Update()
        {
            if (!Network.Decides || Time.time < nextTick)
                return;

            nextTick = Time.time + tickInterval;
            SessionPlayers.Collect(players);
            Tick(players, Time.time);
        }

        /// <summary>One decision for every subject. Called by Update; public for the tests.</summary>
        public void Tick(IReadOnlyList<Transform> playerBodies, float now)
        {
            if (!sim) sim = GetComponent<NpcWorldSim>();

            playerSet.Clear();
            sources.Clear();
            for (int i = 0; i < playerBodies.Count; i++)
            {
                if (playerBodies[i] == null) continue;
                playerSet.Add(playerBodies[i]);
                sources.Add(WakePosition(playerBodies[i]));
            }

            var cells = new SimulationCells(origin, cellSize, sim.SpawnRadius, sim.DespawnRadius);
            SimulationRules.Advance(awake, sources, cells, scratch);

            IReadOnlyList<DistanceDormant> subjects = DistanceDormant.All;
            for (int i = 0; i < subjects.Count; i++)
            {
                DistanceDormant subject = subjects[i];
                bool cellAwake = awake.Contains(SimulationRules.CellOf(subject.transform.position, origin, cellSize));
                subject.Agent.Dormant = SimulationRules.ShouldSleep(subject.Read(cellAwake, playerSet, now), woundedWakeSeconds);
            }
        }

        // Interiors load at world origin: a player inside one counts at the door they came in by, so the
        // settlement whose house they are visiting stays awake for the visit.
        private static Vector3 InteriorAwarePosition(Transform player)
        {
            InteriorManager interiors = InteriorManager.Instance;
            return interiors != null && interiors.TryGetVisit(player.gameObject, out InteriorManager.InteriorVisit visit)
                ? visit.ReturnPosition
                : player.position;
        }
    }
}
```

Implementer checks: the namespaces of `Network` (`Core/Multiplayer/Authority/Network.cs`), `WorldStreamer` and `InteriorManager` — fix the `using`s to match. `Start` runs after `WorldStreamer.Awake`, and the tests never call `Start` (origin stays zero, which the tests assume).

- [ ] **Step 4: Run tests**

Run: `run_tests` EditMode, filter `SimulationRangeTests`.
Expected: 4 passed.

- [ ] **Step 5: Wire into the world scene**

Heads-up to peers ("adding SimulationRange to NpcWorldSim's GameObject in persistentScene, then saving it"); confirm nobody else has `persistentScene.unity` open with unsaved changes (it currently shows `M` in git status — someone else's change: do not discard it). With Unity MCP: open `Assets/Game/Scenes/world/persistentScene.unity`, find the GameObject carrying `NpcWorldSim` (`find_gameobjects` by component), `manage_components` add `SimulationRange` with defaults, save the scene. Check `Assets/Game/Scenes/Tests/Ferdinand_Test_world.unity` for an `NpcWorldSim`; if it has one, add `SimulationRange` there too.

Verify with `git diff Assets/Game/Scenes/world/persistentScene.unity` that the only new hunk from this task is one `MonoBehaviour` block (`cellSize: 125`, `tickInterval: 0.5`, `woundedWakeSeconds: 30`) and its component reference.

- [ ] **Step 6: Checkpoint** — list `SimulationRange.cs`, `SimulationRangeTests.cs`, `persistentScene.unity` (shared with other work — tell the user which hunk is ours).

---

### Task 5: Prefab classification — wiring pass, builders, folder test

**Files:**
- Create: `Assets/Game/Editor/Agents/DistanceDormancyWiring.cs`
- Modify builders, each **immediately after** the named line:
  - `Assets/Game/Editor/Agents/ClankerStack.cs:299` (`AgentGroundConformWiring.Ensure(root);`)
  - `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs:586` (same call — **message spacegame-38 first**)
  - `Assets/Game/Editor/Agents/SculptCharacterBuilder.cs:1259` (same call)
  - `Assets/Game/Editor/Creatures/RobotHorseBuilder.cs:567` (same call)
  - `Assets/Game/Editor/Creatures/StriderCrabOutriderBuilder.cs:57` (`EntityFactionWiring.Ensure(...)`)
  - `Assets/Game/Editor/Creatures/StriderElderBuilder.cs:158` (`EntityFactionWiring.Ensure(...)`)
  - `Assets/Game/Editor/Creatures/AppaHerdAuthoring.cs` — before each `SaveAsPrefabAsset` at :159 and :234, only if the saved root has an `AgentController` (Ensure checks that itself)
  - `Assets/Game/Editor/Agents/DuneRatPennedBuilder.cs` — before the saves at :61/:77/:115
  (Line numbers are from 2026-10-06; re-grep `AgentGroundConformWiring.Ensure\|EntityFactionWiring.Ensure\|SaveAsPrefabAsset` in each file before editing.)
- Test: `Assets/Game/Editor/Tests/DistanceDormancyPrefabTests.cs`

**Interfaces:**
- Consumes: `DistanceDormant` (Task 3).
- Produces: `static bool DistanceDormancyWiring.IsSubjectPath(string assetPath)`, `static bool DistanceDormancyWiring.Ensure(GameObject root)`, menu `Tools/SpaceGame/Agents/Wire Distance Dormancy`.

- [ ] **Step 1: Write the failing tests**

```csharp
// Every person and animal can be put to sleep by simulation distance; no machine ever can.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class DistanceDormancyPrefabTests
    {
        [TestCase("Assets/Game/Prefabs/Agents/Characters/Nomad.prefab", true)]
        [TestCase("Assets/Game/Prefabs/agents/creatures/CrabWalker6.prefab", true)]
        [TestCase("Assets/Game/Prefabs/Agents/Robots/Clanker.prefab", true)]
        [TestCase("Assets/Game/Prefabs/Agents/Vehicles/Ground/RigWalker.prefab", false)]
        [TestCase("Assets/Game/Prefabs/Environment/Structures/SkyFleet/SkyCityFleet.prefab", false)]
        [TestCase("Assets/Game/Prefabs/Spikes/Raxy_Gunner.prefab", false)]
        public void SubjectPathsAreThePeopleAndAnimalFolders(string path, bool subject)
        {
            Assert.AreEqual(subject, DistanceDormancyWiring.IsSubjectPath(path));
        }

        [Test]
        public void EveryAgentPrefabIsClassifiedByItsFolder()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.ToLowerInvariant().Contains("/prefabs/spikes/")) continue;

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                bool subject = DistanceDormancyWiring.IsSubjectPath(path);

                foreach (AgentController agent in prefab.GetComponentsInChildren<AgentController>(true))
                {
                    bool marked = agent.GetComponent<DistanceDormant>() != null;
                    Assert.AreEqual(subject, marked,
                        subject ? $"{path}: a person or animal without DistanceDormant (run Tools/SpaceGame/Agents/Wire Distance Dormancy)"
                                : $"{path}: a machine or structure with DistanceDormant would freeze in the distance");
                }
            }
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `python3 tools/typecheck.py --editor`
Expected: FAIL — `DistanceDormancyWiring` not found.

- [ ] **Step 3: Implement `DistanceDormancyWiring.cs`**

```csharp
// Getting DistanceDormant onto every person and animal prefab, and keeping it there and off every
// machine (SimulationDistance.md). Called from BOTH ends, like AgentGroundConformWiring: the menu item
// fixes every prefab nobody generates, and each builder calls Ensure so a rebuild cannot drop it.
//
// Scope is by folder because the folders already sort the agents: people, robots and animals under
// Agents/Characters, Robots and creatures; vehicles, ships and the sky fleet elsewhere. Matched without
// case — git holds both Prefabs/Agents/ and Prefabs/agents/.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public static class DistanceDormancyWiring
    {
        private const string PrefabRoot = "Assets/Game/Prefabs";

        private static readonly string[] SubjectFolders =
        {
            "/prefabs/agents/characters/",
            "/prefabs/agents/robots/",
            "/prefabs/agents/creatures/",
        };

        public static bool IsSubjectPath(string assetPath)
        {
            string lower = assetPath.Replace('\\', '/').ToLowerInvariant();
            return SubjectFolders.Any(lower.Contains);
        }

        /// <summary>Puts a DistanceDormant beside every AgentController under <paramref name="root"/>. True if anything was added.</summary>
        public static bool Ensure(GameObject root)
        {
            bool changed = false;
            foreach (AgentController agent in root.GetComponentsInChildren<AgentController>(true))
            {
                if (agent.GetComponent<DistanceDormant>() != null) continue;
                agent.gameObject.AddComponent<DistanceDormant>();
                changed = true;
            }

            return changed;
        }

        private static bool Strip(GameObject root)
        {
            bool changed = false;
            foreach (DistanceDormant marker in root.GetComponentsInChildren<DistanceDormant>(true))
            {
                Object.DestroyImmediate(marker, true);
                changed = true;
            }

            return changed;
        }

        [MenuItem("Tools/SpaceGame/Agents/Wire Distance Dormancy")]
        public static void WireAll()
        {
            // Bases before variants: a variant visited first would get its own copy, and then inherit a
            // second from its base.
            IEnumerable<string> paths = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.ToLowerInvariant().Contains("/prefabs/spikes/"))
                .OrderBy(VariantDepth);

            int added = 0, removed = 0;
            foreach (string path in paths)
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset.GetComponentInChildren<AgentController>(true) == null) continue;

                bool subject = IsSubjectPath(path);
                bool marked = asset.GetComponentInChildren<DistanceDormant>(true) != null;
                if (subject == marked && (!subject || AllMarked(asset))) continue;

                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (subject ? Ensure(contents) : Strip(contents))
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        if (subject) added++; else removed++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            Debug.Log($"[DistanceDormancyWiring] Marked {added} prefab(s), stripped {removed}.");
        }

        private static bool AllMarked(GameObject asset) =>
            asset.GetComponentsInChildren<AgentController>(true).All(a => a.GetComponent<DistanceDormant>() != null);

        private static int VariantDepth(string path)
        {
            int depth = 0;
            Object source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            while ((source = PrefabUtility.GetCorrespondingObjectFromSource(source)) != null)
                depth++;

            return depth;
        }
    }
}
```

(The single `Debug.Log` is the menu command's result report, matching the other wiring menus — not a leftover debug log. If `Strip` meets a marker inherited from a base prefab in scope, `DestroyImmediate` fails loudly; that would mean a vehicle is a variant of a person, which the folder test then reports — do not catch it.)

- [ ] **Step 4: Add `DistanceDormancyWiring.Ensure(root);` to each builder listed under Files**, one line at the named spot, matching the neighbouring call's style. For `NomadPrefabBuilder.cs`, message spacegame-38 first and add the line after its `AgentGroundConformWiring.Ensure(root);` as it stands in their current version.

- [ ] **Step 5: Run the wiring pass**

Heads-up to peers ("running Wire Distance Dormancy: rewrites ~130 agent prefabs under Prefabs/Agents/{Characters,Robots,creatures}, adding one component each"). Check `isPlaying` is false. `mcp__UnityMCP__execute_menu_item` `Tools/SpaceGame/Agents/Wire Distance Dormancy`. Read the console: expect `Marked N prefab(s), stripped 0.` with N ≈ 130 minus variants that inherit.

Spot-check with `git diff --stat Assets/Game/Prefabs` that only `Agents/Characters`, `Agents/Robots`, `Agents/creatures` prefabs changed, and one diff (e.g. `Nomad.prefab`) adds a single `DistanceDormant` MonoBehaviour block.

- [ ] **Step 6: Run tests**

Run: `run_tests` EditMode, filter `DistanceDormancyPrefabTests`, then the prefab regression suites: `ClankerPrefabTests`, `RosterAssetTests`, `SkyTransportPrefabTests`, `StriderHabitatWalkerTests`.
Expected: all pass. (`ClankerPrefabTests` compares root component sets across Clanker bodies — all four get the marker, so the sets still match.)

- [ ] **Step 7: Checkpoint** — list `DistanceDormancyWiring.cs`, the builder files, `DistanceDormancyPrefabTests.cs`, and the prefab set from `git diff --stat`.

---

### Task 6: Documentation

**Files:**
- Create: `docs/AI/systems/SimulationDistance.md`
- Modify: `docs/Human/the-systems.md`, `docs/AI/systems/AgentSystem.md`
- Regenerated: `docs/AI/INDEX.md`, `docs/AI/ROUTING.md` (never hand-edit)

- [ ] **Step 1: Write `docs/AI/systems/SimulationDistance.md`** in the standard shape. Copy the frontmatter keys from `docs/AI/systems/EntitySystem.md` (`system`, `layer: characters`, `summary`, `paths`, `symptoms`, `reads_with`, `updated: 2026-10-06`). `paths`: the three `agents/Simulation/` files, `Assets/Game/Editor/Agents/DistanceDormancyWiring.cs`, the five test files. `symptoms` (phrased as what one sees):
  - "NPCs and creatures fight each other far away where no player is"
  - "an NPC far away stands frozen in place and does nothing"
  - "a villager stands still until I walk within about 250 m"
  - "a creature I shot from very far away does not react"
  - "the sky city or a Strider house stopped moving in the distance"
  - "a pack hunting me stops chasing when I run far enough"
  `reads_with: [AgentSystem, EntitySystem, WorldStreaming, Residents]`.

  Body sections — fill each from the spec: **Model** (cells, wake/sleep from NpcWorldSim, interior return position, opt-in), **Key types** (table of the five types incl. `IsParked`), **Flows** (the tick), **Multiplayer** (server-only, not replicated), **Persistence** ("holds no state worth persisting; re-derived on the first tick"), **Gotchas**:
  - Never use `ChunkGrid.ToCoord` for cells — it clamps.
  - `Offstage` and `Dormant` are two reasons; any reader asking "is it acting" reads `IsParked`.
  - A machine must never carry `DistanceDormant`; the folder test guards it. A new agent prefab outside the three folders is never put to sleep.
  - Exemptions table (copy from the spec), including why group members are exempt (war parties stage beyond `spawnRadius`).
  - A marker disabled while dormant releases its agent (`OnDisable`).
  **Extending**: a new player-facing event stays live as an `NpcWorldSim` group or by giving its members a player target; a new NPC/animal prefab goes in one of the three folders and its builder calls `DistanceDormancyWiring.Ensure`.

- [ ] **Step 2: Add a plain-language entry to `docs/Human/the-systems.md`** under the characters/agents heading, matching its neighbours' length: people and animals far from every player stand still until someone comes within about 250 m; machines and travelling groups keep going; a shot from afar wakes the target.

- [ ] **Step 3: Update `docs/AI/systems/AgentSystem.md`**: in Flows step 1 replace "Then `Offstage` →" with "Then `IsParked` (`Offstage` or `Dormant`) →" and add one sentence after the `Offstage` description: "`Dormant` is the second parking reason, written only by `SimulationRange` ([SimulationDistance](SimulationDistance.md)); the two never release each other." Add `SimulationDistance` to `reads_with`. Bump `updated: 2026-10-06`. In the Gotchas paragraph about tribes fighting with no player near ("Tribes and Clankers fight each other with no player near…"), delete what is no longer true and replace with: "Beyond `NpcWorldSim.DespawnRadius` of every player they now sleep ([SimulationDistance](SimulationDistance.md)); fights still happen inside it and among group members."

- [ ] **Step 4: Regenerate and validate**

Run: `python3 tools/docs_check.py --index`
Expected: regenerates INDEX.md + ROUTING.md and reports no errors.

- [ ] **Step 5: Checkpoint** — list the four doc files plus the two regenerated ones.

---

### Task 7: Verification in the real game (host, client, reload)

No new code; this is the spec's acceptance. Any failure → `superpowers:systematic-debugging`, fix in the owning task's files, re-run that task's tests.

- [ ] **Step 1: Heads-up** to every peer from `ListAgents`: "Entering Play Mode in the shared editor for ~15 min (simulation distance verification). Please hold refreshes/compiles."

- [ ] **Step 2: Host, distant town.** Play the main world. Teleport or walk the player ~600 m from the Clanker town nearest the Strider city start. Via `execute_code`, every 5 s for 2 minutes, log the count of `DistanceDormant.All` with `Agent.Dormant` true, and the town's living Clanker count. Expected: the town's Clankers are dormant and its living count does not drop.

- [ ] **Step 3: Host, walking in.** Move the player toward the town; record the distance at which its Clankers' `Dormant` turns false. Expected: by 250 m (at most one cell, ~125 m, earlier).

- [ ] **Step 4: Wounded wake.** From ~400 m, apply damage to one dormant Clanker through `GameServices`' damage path via `execute_code` (or fire a long-range weapon). Expected: it wakes at once and sleeps again ~30 s after the last hit if no player came near.

- [ ] **Step 5: Machines.** Watch the sky city flagship and the Strider lead house from beyond 360 m for 30 s: their positions change. Sky residents on the flagship are dormant (unless exempt) and stay on the deck.

- [ ] **Step 6: Client.** Run the two-process autotest (Testing.md / Multiplayer.md: `Assets/Game/Scripts/Core/Multiplayer/Autotest/`). Expected: the client sees the same still bodies, its console has no new errors or warnings, and a dormant NPC near the edge wakes when the *client's* player walks up.

- [ ] **Step 7: Save/reload.** Save the world, quit to menu, load it. Expected: `git diff`-style comparison of the save JSON before/after this feature shows no new keys (search the save for `dormant`/`Dormant`: zero hits); the distant town is dormant again within one tick of loading.

- [ ] **Step 8: Exit Play Mode, tell the peers, and report** results to the user with the logged numbers. Leave all work uncommitted; give the user the full file list from every checkpoint and suggest committing now.
