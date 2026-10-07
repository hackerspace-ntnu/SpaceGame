# The Striders' Walking City Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A third tribe, The Striders, whose one city — 4 crewed RigWalker houses, 2 digging DesertCrawler workers and 2 ridden crab outriders — roams between salvage sites as an `NpcWorldSim` caravan, its 24 crew stepping off at every stop and re-boarding before it walks on. *(Superseded 2026-09-24: 3 houses / 18 crew, user.)*

**Architecture:** The tribe is built the way the Sky Tribe was (faction → recoloured nomads → roster → war-party template, all from `RosterAuthoring`/`NomadPrefabBuilder`). The city is an ordinary seeded `NpcGroupTemplate`; the new pieces are a `crew` flag on member specs, a `CrewShift` component (with pure `CrewShiftLogic`) on each house that seats its crew on `VesselSeats` posts and runs the aboard → disembarking → ashore → recalling cycle, and a departure gate on `NpcTaskModule` so the leader never walks off while anyone is ashore. Houses are a builder-owned prefab *variant* of the hand-authored `RigWalker` with the player helm removed.

**Tech Stack:** Unity 6000.3.11f1, C#, Netcode for GameObjects (via `Network`/`NpcSeating`), Newtonsoft save records, NUnit EditMode, editor builders driven over the `unityMCP` bridge.

**Spec:** [docs/superpowers/specs/2026-09-23-striders-walking-city-design.md](../specs/2026-09-23-striders-walking-city-design.md) — read it in full first. Research notes behind this plan (exact signatures, verbatim excerpts) are summarised inline where used.

## Global Constraints

- Branch `Feat/create-factions`. **Commit only when the user authorises it** — every "Commit" step is conditional.
- Every `Nomad*`, crawler, crab and habitat prefab is **builder-owned**: change the builder, rebuild, read the prefab back. Never hand-edit a generated prefab.
- Builders end with `NetworkPrefabRegistrar.Sync(out _, out _)` → `SaveableWiring.TryWirePrefabs()` (→ `RagdollWiring.WirePrefabs()` for bodies with a ragdoll). **Never `SyncMenu()`** — it opens a modal dialog that blocks the MCP bridge.
- Modules added with `AddComponent` from editor code keep priority 0 (Unity does not run `Reset`) — **set every module's `priority` explicitly** via `SerializedFields`.
- `FormationModule.SetFormation("")` turns the empty id into `"caravan"`; never call it with an empty id. Builders write `formationId = ""` through `SerializedFields.SetString` instead.
- `LeggedDriver` clamps speed multipliers to ≤ 1: a follower machine can only keep up with a leader that travels slower than the follower's `moveSpeed`. The city leader travels at `CityTravelMultiplier = 0.45` × RigWalker 6 m/s = 2.7 m/s, under the crawler's 3.2 and the crab outrider's 3.2 (spike: the stock crab gait caps at 1.76 m/s; `CrabLocomotion.stepDuration` 0.22 lifts it to 3.2 cleanly). Keep the multiplier at 0.45 or lower — the crawler's catch-up headroom is thin.
- New `NpcGroup.Record` fields are **appended** with a dated comment stating what an older save reads.
- No magic numbers: tunables are `[SerializeField]` fields or named `const`s with a one-line *why*.
- Faction is not replicated: every prefab that must be a Strider on a client ships with `StriderFaction` baked on its `EntityFaction`.
- Verification commands: type-check `python3 tools/typecheck.py --editor` → `No errors.`; tests via the MCP `run_tests` tool (EditMode, filtered by class) or menu `Tools/Tests/Run EditMode Tests (headless)` then `cat Temp/headless_tests.txt` → `FAILED=0`; docs `python3 tools/docs_check.py --index` → `0 errors`. Use `python` if `python3` is missing.
- Unity editor bridge: MCP server `unityMCP`; tools are deferred — load with `ToolSearch` `select:mcp__unityMCP__execute_code,mcp__unityMCP__refresh_unity,mcp__unityMCP__read_console,mcp__unityMCP__run_tests,mcp__unityMCP__get_test_job`. Run menu items with `execute_code` → `EditorApplication.ExecuteMenuItem("...")` or call the static method. The "Scene(s) Have Been Modified" dialog blocks the bridge: start from a clean `Bootstrap` scene.

## Review Focus

1. **Save during a stop, then reload** — crew must come back on foot around their houses, and the leader must not walk off before they re-board (Task 5 record round-trip + Task 2 gate-in-Choosing test + Task 7 `CrewAshore` read-back test).
2. **A crew member dies ashore or while recalling** — the column must still leave (Task 4 `DeadCrewDoNotBlockTheRecall`).
3. **A crew member cannot reach the gangway** (stuck, off-mesh) — `recallTimeout` seats them anyway (Task 4 `TimeoutSeatsStragglers`), but never while anyone is fighting (Task 4 `AFightPausesTheRecallClock`).
4. **The city folds with crew seated** — seated crew are children of the house's `NetworkObject`; despawning the house first would strand them at the scene root. Members despawn in reverse spawn order (Task 7 `DespawnOrder_CrewBeforeTheirCarriers`).
5. **An old save with no `crewAshore`** reads `false` and the city spawns marching (Task 5 `Record_FromAnOlderSave_ReadsCrewAboard`).

---

### Task 1: Spike — can the machines lead, follow and be a variant? (throwaway)

**Files:** none kept. Work in a scratch scene; delete everything created. Record answers in this plan under "Spike findings" (end of file) before Task 2.

- [ ] **Step 1: Build a throwaway habitat variant in memory.** Via `execute_code`: `PrefabUtility.InstantiatePrefab` the `RigWalker` prefab (`Assets/Game/Prefabs/Agents/Vehicles/Ground/RigWalker.prefab`), then `Object.DestroyImmediate` in this order: `SteerModule`, `MountSaveable`, `MountNetworkSync`, the `DOOR_MountStation` child GameObject, `MountModule`. Save it with `PrefabUtility.SaveAsPrefabAsset(instance, "Assets/Scratch/SpikeHabitat.prefab")`. Answer in writing: did every destroy succeed; does the saved asset report `PrefabUtility.GetPrefabAssetType == Variant`; does it still carry `NetworkObject`, `WalkerPlatformCarrier`, `DesertCrawlerDriver`?
- [ ] **Step 2: Lead and follow.** In play mode on a NavMesh-baked chunk (e.g. near the RigWalker already in `Chunk_6_4`), spawn two spike habitats and one `DesertCrawler`. On all three add `FormationModule` (priority 15, same id `spike`, first `isLeader`); on the leader add `NpcTaskModule` + `GoalTravelModule` (priority 1) and give it one task (`targetSite = Ruin`, `travelSpeedMultiplier = 0.45`). Answer: does the leader walk to a site; do both followers hold slots with `FormationShape { Lanes = 2, RowSpacing = 30, LaneSpacing = 30 }`, follower `restRadius = 45`, `slotTolerance = 8`, `regroupDistance = 120`; do they collide; does the crawler dig (`CrawlerToolModule`) when the column stops?
- [ ] **Step 3: Crab speed.** Spawn `CrabWalker6`, set its `CrabDriver.moveSpeed = 3.6` and drive it with a `MoveIntent` (add `AgentController` + `GoalTravelModule` + `AgentGoal`, set a goal 100 m away). Answer: does the gait hold at 3.6 m/s without feet skating or the body tipping? If not, the highest speed that looks right is the crab's speed and `CityTravelMultiplier` must be lowered so `6 × multiplier` stays under it.
- [ ] **Step 4: Seat on a moving deck.** Add `VesselSeats` with 2 seat markers on the spike leader's deck and seat two sand nomads (`VesselSeats.Seat(npc)`) while it walks. Answer: do they ride without jitter; does `Unseat(seat, groundPoint)` put them on the NavMesh beside the legs?
- [ ] **Step 5: Stop or go.** If Step 1, 2 or 4 is a hard no, **stop and report to the user** — the spec's Risks table names the fallbacks. Otherwise write the numbers you actually used under "Spike findings", delete `Assets/Scratch/`, and continue.

---

### Task 2: `NpcTaskModule` departure gate

**Files:**
- Modify: `Assets/Game/Scripts/agents/Tasks/NpcTaskModule.cs` (fields near the top; `TickChoosing` ~:204; `TickDwelling` ~:285)
- Test: `Assets/Game/Editor/Tests/NpcTaskDepartureGateTests.cs` (new)

**Interfaces:**
- Produces: `public void SetDepartureGate(System.Func<bool> mayDepart)`; `public bool AtStop` (true while dwelling with time left).

- [ ] **Step 1: Write the failing tests**

```csharp
// The departure gate: something outside the task loop (a walking city's crew) can keep the NPC
// where it is -- both at the end of a stay and before it picks its next destination.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class NpcTaskDepartureGateTests
    {
        private GameObject go;
        private NpcTaskModule tasks;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("Leader");
            tasks = go.AddComponent<NpcTaskModule>();
            // EditMode AddComponent does not run Awake, and Awake is what finds the AgentGoal.
            typeof(NpcTaskModule).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(tasks, null);
            tasks.SetTasks(new[] { new NpcTask { targetSite = SiteKind.Ruin, dwellSeconds = new Vector2(1f, 1f) } });
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(go);

        private void Dwell(float timeLeft) =>
            tasks.RestoreTaskState(NpcTaskModule.Phase.Dwelling, 0, "site", "site-1", timeLeft, 0f, -1, true, Vector3.zero);

        private void Tick(float dt) => tasks.Tick(default, dt);

        [Test]
        public void AtStop_WhileDwellTimeRemains()
        {
            Dwell(5f);
            Assert.IsTrue(tasks.AtStop);
            Tick(6f);
            Assert.IsFalse(tasks.AtStop, "the stay is over even if the NPC has not left yet");
        }

        [Test]
        public void AClosedGate_HoldsTheDwellPastItsTime()
        {
            bool open = false;
            tasks.SetDepartureGate(() => open);
            Dwell(1f);

            Tick(5f);
            Assert.AreEqual(NpcTaskModule.Phase.Dwelling, tasks.CurrentPhase, "held while the gate is shut");

            open = true;
            Tick(0.1f);
            Assert.AreEqual(NpcTaskModule.Phase.Choosing, tasks.CurrentPhase);
        }

        [Test]
        public void AClosedGate_HoldsChoosing_SoARespawnedLeaderWaitsForItsCrew()
        {
            tasks.SetDepartureGate(() => false);
            tasks.SetTasks(new[] { new NpcTask { targetSite = SiteKind.Ruin } });

            Tick(1f);

            Assert.AreEqual(NpcTaskModule.Phase.Choosing, tasks.CurrentPhase);
            Assert.IsFalse(go.GetComponent<AgentGoal>().HasGoal, "no destination while held");
        }

        [Test]
        public void NoGate_BehavesAsBefore()
        {
            Dwell(1f);
            Tick(2f);
            Assert.AreEqual(NpcTaskModule.Phase.Choosing, tasks.CurrentPhase);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — type-check → errors: `SetDepartureGate`, `AtStop` do not exist.

- [ ] **Step 3: Implement.** Add beside the other private state:

```csharp
        // Who else must agree before this NPC sets off. A walking city's leader waits here for
        // its crew to be back aboard (CrewShift); null means nobody else has a say.
        private System.Func<bool> departureGate;

        /// <summary>Dwelling with time left on the stay. False once the stay is over, even while a
        /// departure gate is still holding the NPC where it is.</summary>
        public bool AtStop => CurrentPhase == Phase.Dwelling && phaseTimer > 0f;

        /// <summary>
        /// Something outside the task loop that can hold this NPC in place: consulted when a stay
        /// ends and before a new destination is chosen. Server-side state, like the rest of the loop.
        /// </summary>
        public void SetDepartureGate(System.Func<bool> mayDepart) => departureGate = mayDepart;

        private bool MayDepart => departureGate == null || departureGate();
```

At the top of `TickChoosing`, before the retry-delay check:

```csharp
            if (!MayDepart) return;
```

Replace the body of `TickDwelling` with:

```csharp
            phaseTimer = Mathf.Max(0f, phaseTimer - deltaTime);
            if (phaseTimer > 0f || !MayDepart) return;

            CollectYield();
            SetDwellFlag(null);
            CurrentPhase = Phase.Choosing;
            phaseTimer = 0f;
```

- [ ] **Step 4: Run** `NpcTaskDepartureGateTests` (4 pass) and `NpcTaskPlannerTests` (still green).
- [ ] **Step 5: Commit** (only if authorised) — `feat(agents): NpcTaskModule departure gate`.

---

### Task 3: A machine with no health is not a fighter

**Files:**
- Modify: `Assets/Game/Scripts/agents/World/GroupMembership.cs` (`Stamp`, ~:39)
- Test: `Assets/Game/Editor/Tests/GroupMembershipTests.cs` (new)

Why: `Stamp` marks every member without an `NpcPassenger` a fighter. The city's houses and crawlers have no `HealthComponent`, so they would sit in `Group.Fighters` forever and `CountStanding` would count them standing.

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class GroupMembershipTests
    {
        [Test]
        public void AMemberWithNoHealth_IsNotAFighter()
        {
            var machine = new GameObject("Walker");
            var group = new NpcGroup { Id = "city" };
            try
            {
                GroupMembership m = GroupMembership.Stamp(machine, group, 0, null);
                Assert.IsFalse(m.IsFighter, "an indestructible carrier can never be beaten");
                CollectionAssert.DoesNotContain(group.Fighters, machine);
            }
            finally { Object.DestroyImmediate(machine); }
        }

        [Test]
        public void AMemberWithHealth_IsStillAFighter()
        {
            var person = new GameObject("Nomad");
            person.AddComponent<HealthComponent>();
            var tribe = ScriptableObject.CreateInstance<FactionDefinition>();   // Enlist applies the tribe
            var group = new NpcGroup { Id = "city" };
            try
            {
                Assert.IsTrue(GroupMembership.Stamp(person, group, 0, tribe).IsFighter);
                CollectionAssert.Contains(group.Fighters, person);
            }
            finally { Object.DestroyImmediate(person); Object.DestroyImmediate(tribe); }
        }
    }
}
```

- [ ] **Step 2: Run to verify** `AMemberWithNoHealth_IsNotAFighter` fails.
- [ ] **Step 3: Implement** — in `Stamp` replace the `IsFighter` line with:

```csharp
            // A mount is carried into the fight by its rider's standing, not its own; a machine with
            // no health (a walking city's houses and workers) can never fall, so neither is a fighter.
            membership.IsFighter = !member.TryGetComponent(out NpcPassenger _) &&
                                   member.TryGetComponent(out HealthComponent _);
```

- [ ] **Step 4: Run** `GroupMembershipTests` (2 pass), `RuntimeGroupTests`, `WarPartyDirectorTests`, `NpcGroupCompositionTests` (still green).
- [ ] **Step 5: Commit** (only if authorised).

---

### Task 4: `CrewShiftLogic` (pure)

**Files:**
- Create: `Assets/Game/Scripts/Vehicles/Crew/CrewShiftLogic.cs`
- Test: `Assets/Game/Editor/Tests/CrewShiftLogicTests.cs`

**Interfaces:**
- Produces: `enum CrewState { Aboard, Disembarking, Ashore, Recalling }`; `readonly struct CrewCensus(int living, int aboard, int fighting)`; `static CrewState CrewShiftLogic.Next(CrewState state, bool atStop, CrewCensus crew)`; `static float CrewShiftLogic.AdvanceRecallClock(float elapsed, float deltaTime, CrewCensus crew)`; `static bool CrewShiftLogic.ShouldForceBoard(float recallElapsed, float recallTimeout, CrewCensus crew)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class CrewShiftLogicTests
    {
        private static CrewCensus Crew(int living, int aboard, int fighting = 0) => new CrewCensus(living, aboard, fighting);

        [Test]
        public void Marching_StaysAboard() =>
            Assert.AreEqual(CrewState.Aboard, CrewShiftLogic.Next(CrewState.Aboard, atStop: false, Crew(6, 6)));

        [Test]
        public void AStop_StartsTheDisembark() =>
            Assert.AreEqual(CrewState.Disembarking, CrewShiftLogic.Next(CrewState.Aboard, atStop: true, Crew(6, 6)));

        [Test]
        public void Disembark_EndsAshore_WhenNobodyIsLeftAboard()
        {
            Assert.AreEqual(CrewState.Disembarking, CrewShiftLogic.Next(CrewState.Disembarking, true, Crew(6, 2)));
            Assert.AreEqual(CrewState.Ashore, CrewShiftLogic.Next(CrewState.Disembarking, true, Crew(6, 0)));
        }

        [Test]
        public void TheStayEnding_RecallsFromAshore_AndFromAHalfFinishedDisembark()
        {
            Assert.AreEqual(CrewState.Recalling, CrewShiftLogic.Next(CrewState.Ashore, atStop: false, Crew(6, 0)));
            Assert.AreEqual(CrewState.Recalling, CrewShiftLogic.Next(CrewState.Disembarking, atStop: false, Crew(6, 3)));
        }

        [Test]
        public void Recall_EndsAboard_OnlyWhenEveryLivingMemberIs()
        {
            Assert.AreEqual(CrewState.Recalling, CrewShiftLogic.Next(CrewState.Recalling, false, Crew(6, 5)));
            Assert.AreEqual(CrewState.Aboard, CrewShiftLogic.Next(CrewState.Recalling, false, Crew(6, 6)));
        }

        [Test]
        public void DeadCrewDoNotBlockTheRecall() =>
            Assert.AreEqual(CrewState.Aboard, CrewShiftLogic.Next(CrewState.Recalling, false, Crew(living: 4, aboard: 4)));

        [Test]
        public void AnEmptyHouse_IsAboard() =>
            Assert.AreEqual(CrewState.Aboard, CrewShiftLogic.Next(CrewState.Recalling, false, Crew(0, 0)));

        [Test]
        public void AFightPausesTheRecallClock()
        {
            Assert.AreEqual(10f, CrewShiftLogic.AdvanceRecallClock(10f, 1f, Crew(6, 2, fighting: 1)));
            Assert.AreEqual(11f, CrewShiftLogic.AdvanceRecallClock(10f, 1f, Crew(6, 2)));
        }

        [Test]
        public void TimeoutSeatsStragglers_ButNeverMidFight()
        {
            Assert.IsFalse(CrewShiftLogic.ShouldForceBoard(59f, 60f, Crew(6, 5)));
            Assert.IsTrue(CrewShiftLogic.ShouldForceBoard(60f, 60f, Crew(6, 5)));
            Assert.IsFalse(CrewShiftLogic.ShouldForceBoard(90f, 60f, Crew(6, 5, fighting: 1)));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** — type-check → `CrewState`, `CrewCensus`, `CrewShiftLogic` do not exist.
- [ ] **Step 3: Implement**

```csharp
// The decisions a walking city's house makes about its crew, with nothing in them but numbers:
// when to put them ashore, when to call them back, and when to stop waiting. CrewShift feeds it
// what it counts and does what it says; this file never touches a scene.
namespace SpaceGame.Vehicles
{
    public enum CrewState { Aboard, Disembarking, Ashore, Recalling }

    public readonly struct CrewCensus
    {
        public readonly int Living;
        public readonly int Aboard;
        public readonly int Fighting;

        public CrewCensus(int living, int aboard, int fighting)
        {
            Living = living;
            Aboard = aboard;
            Fighting = fighting;
        }

        public bool AllAboard => Aboard >= Living;
    }

    public static class CrewShiftLogic
    {
        /// <param name="atStop">The column's leader is dwelling with time left on its stay.</param>
        public static CrewState Next(CrewState state, bool atStop, CrewCensus crew)
        {
            switch (state)
            {
                case CrewState.Aboard:
                    return atStop && crew.Living > 0 ? CrewState.Disembarking : CrewState.Aboard;
                case CrewState.Disembarking:
                    if (!atStop) return CrewState.Recalling;
                    return crew.Aboard == 0 ? CrewState.Ashore : CrewState.Disembarking;
                case CrewState.Ashore:
                    return atStop ? CrewState.Ashore : CrewState.Recalling;
                default:
                    return crew.AllAboard ? CrewState.Aboard : CrewState.Recalling;
            }
        }

        /// <summary>The recall clock does not run while anyone is fighting: nobody is hurried aboard mid-fight.</summary>
        public static float AdvanceRecallClock(float elapsed, float deltaTime, CrewCensus crew) =>
            crew.Fighting > 0 ? elapsed : elapsed + deltaTime;

        /// <summary>Seat whoever is still ashore, rather than strand the city on a straggler.</summary>
        public static bool ShouldForceBoard(float recallElapsed, float recallTimeout, CrewCensus crew) =>
            crew.Fighting == 0 && recallElapsed >= recallTimeout;
    }
}
```

- [ ] **Step 4: Run** `CrewShiftLogicTests` → 9 pass.
- [ ] **Step 5: Commit** (only if authorised).

---

### Task 5: Group data — crew members and `crewAshore`

**Files:**
- Modify: `Assets/Game/Scripts/agents/World/NpcGroup.cs` (`NpcGroupMemberSpec` :18-35; `NpcGroup` fields :177+; `Record` :303-333; `ToRecord`/`ApplyRecord` :335-375)
- Modify: `Assets/Game/Scripts/agents/World/NpcGroupComposition.cs` (`PlannedMember`, `Resolve`)
- Test: append to `Assets/Game/Editor/Tests/GroupRecordTests.cs` and `NpcGroupCompositionTests.cs`

**Interfaces:**
- Produces: `NpcGroupMemberSpec.crew` (bool); `NpcGroup.CrewAshore` (bool, saved); `NpcGroup.Record.crewAshore`; `PlannedMember.Crew` (bool) and ctor `PlannedMember(GameObject prefab, bool leads, bool crew = false)`.

- [ ] **Step 1: Write the failing tests** — append to `GroupRecordTests`:

```csharp
        [Test]
        public void CrewAshore_RoundTripsThroughTheSaveSerializer()
        {
            var group = new NpcGroup { Id = "strider-city", TemplateId = "strider-city", CrewAshore = true };
            JObject json = JObject.FromObject(group.ToRecord(), SaveSerializer.Serializer);
            NpcGroup.Record back = json.ToObject<NpcGroup.Record>(SaveSerializer.Serializer);

            var restored = new NpcGroup { Id = back.id, TemplateId = back.templateId };
            restored.ApplyRecord(in back);
            Assert.IsTrue(restored.CrewAshore);
        }

        [Test]
        public void Record_FromAnOlderSave_ReadsCrewAboard()
        {
            var old = JObject.Parse("{\"id\":\"strider-city\",\"templateId\":\"strider-city\",\"taskIndex\":1}");
            NpcGroup.Record record = old.ToObject<NpcGroup.Record>(SaveSerializer.Serializer);
            var group = new NpcGroup { Id = record.id };
            group.ApplyRecord(in record);
            Assert.IsFalse(group.CrewAshore, "an old save spawns the city marching");
        }
```

Append to `NpcGroupCompositionTests`:

```csharp
        [Test]
        public void CrewSpecs_ArePlannedAsCrew_OthersAreNot()
        {
            var house = new GameObject("House");
            var person = new GameObject("Person");
            try
            {
                var template = new NpcGroupTemplate
                {
                    id = "city",
                    members = new[]
                    {
                        new NpcGroupMemberSpec { prefab = house, isLeader = true },
                        new NpcGroupMemberSpec { prefab = person, crew = true, count = 2 },
                    },
                };
                var plan = NpcGroupComposition.Resolve(new NpcGroup { Id = "city" }, template);
                Assert.AreEqual(3, plan.Count);
                Assert.IsFalse(plan[0].Crew);
                Assert.IsTrue(plan[1].Crew && plan[2].Crew);
            }
            finally { Object.DestroyImmediate(house); Object.DestroyImmediate(person); }
        }
```

- [ ] **Step 2: Run to verify it fails** (type-check: `CrewAshore`, `crew`, `Crew` missing).
- [ ] **Step 3: Implement.** In `NpcGroupMemberSpec` after `isLeader`:

```csharp
        [Tooltip("Rides one of the group's carriers (a member with a CrewShift) instead of walking: " +
                 "spawned seated on a free crew post while the group marches, on foot by its " +
                 "carrier's gangway while the group is stopped.")]
        public bool crew;
```

In `NpcGroup` beside `Delivered`:

```csharp
        public bool CrewAshore;               // saved (Record.crewAshore)
```

Append to `Record` after `delivered`:

```csharp
            // Appended 2026-09-24 (Striders walking city). Older saves read false: the group comes
            // back marching, its crew seated, which is what every group without crew already does.
            public bool crewAshore;
```

`ToRecord`: add `crewAshore = CrewAshore,`. `ApplyRecord`: add `CrewAshore = record.crewAshore;`.

`PlannedMember`:

```csharp
        public readonly bool Crew;
        public PlannedMember(GameObject prefab, bool leads, bool crew = false) { Prefab = prefab; Leads = leads; Crew = crew; }
```

In `Resolve`'s template branch: `plan.Add(new PlannedMember(prefab, spec.isLeader, spec.crew));`.

- [ ] **Step 4: Run** `GroupRecordTests`, `NpcGroupCompositionTests` → green.
- [ ] **Step 5: Commit** (only if authorised).

---

### Task 6: `CrewShift` — the component on each house

**Files:**
- Create: `Assets/Game/Scripts/Vehicles/Crew/CrewShift.cs`
- Test: `Assets/Game/Editor/Tests/CrewShiftTests.cs`

**Interfaces:**
- Consumes: `CrewShiftLogic` (Task 4); `NpcTaskModule.SetDepartureGate`, `AtStop` (Task 2); `VesselSeats` (`Capacity`, `Seat(GameObject)`, `Seat(int, GameObject)`, `OccupantAt`, `Unseat(int, Vector3)`, `FirstOccupiedSeat`); `FormationModule` (`FormationId`, `IsLeader`, `static LeaderOf(string)`); `AgentGoal`; `AgentTargeting.HasTarget`; `HealthComponent.Alive`; `Network.Simulates`.
- Produces: `CrewShift.State`; `bool HasRoom`; `void Take(GameObject member, bool aboard)`; `Vector3 GangwayPoint`; `static bool AllAboard(string formationId)`; `static bool AnyAshore(IEnumerable<GameObject> members)`; `static CrewShift FirstWithRoom(IEnumerable<GameObject> members)`.

- [ ] **Step 1: Write the failing tests** (what can run without play mode: bookkeeping and the static queries)

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class CrewShiftTests
    {
        private readonly List<Object> junk = new();

        [TearDown]
        public void TearDown() { foreach (var o in junk) if (o != null) Object.DestroyImmediate(o); junk.Clear(); }

        private CrewShift House(int seats)
        {
            var go = new GameObject("House");
            junk.Add(go);
            var posts = new Transform[seats];
            for (int i = 0; i < seats; i++) { posts[i] = new GameObject($"Seat_{i}").transform; posts[i].SetParent(go.transform); }
            var vessel = go.AddComponent<VesselSeats>();
            SerializedFieldsForTests.Set(vessel, "seats", posts);
            return go.AddComponent<CrewShift>();
        }

        [Test]
        public void Take_Ashore_FillsTheRoster_AndMarksTheHouseAshore()
        {
            CrewShift house = House(2);
            var a = new GameObject("A"); junk.Add(a);
            house.Take(a, aboard: false);
            Assert.AreEqual(CrewState.Ashore, house.State);
            Assert.IsTrue(house.HasRoom);
            var b = new GameObject("B"); junk.Add(b);
            house.Take(b, aboard: false);
            Assert.IsFalse(house.HasRoom);
        }

        [Test]
        public void FirstWithRoom_SkipsFullHouses_AndNonHouses()
        {
            CrewShift full = House(1), free = House(1);
            var a = new GameObject("A"); junk.Add(a);
            full.Take(a, aboard: false);
            var plain = new GameObject("Crawler"); junk.Add(plain);

            Assert.AreSame(free, CrewShift.FirstWithRoom(new[] { plain, full.gameObject, free.gameObject }));
        }

        [Test]
        public void AnyAshore_ReadsEveryHouseInTheGroup()
        {
            CrewShift aboard = House(1), ashore = House(1);
            var a = new GameObject("A"); junk.Add(a);
            ashore.Take(a, aboard: false);
            Assert.IsTrue(CrewShift.AnyAshore(new[] { aboard.gameObject, ashore.gameObject }));
            Assert.IsFalse(CrewShift.AnyAshore(new[] { aboard.gameObject }));
        }
    }
}
```

`SerializedFieldsForTests.Set` — if no such helper exists in `Assets/Game/Editor/Tests/`, write the assignment inline: `var so = new UnityEditor.SerializedObject(vessel); var p = so.FindProperty("seats"); p.arraySize = posts.Length; for (int i = 0; i < posts.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = posts[i]; so.ApplyModifiedPropertiesWithoutUndo();` (grep for an existing helper first and reuse it).

- [ ] **Step 2: Run to verify it fails** (type-check: `CrewShift` missing).
- [ ] **Step 3: Implement**

```csharp
// One walking-city house's crew: seated on its VesselSeats posts while the column marches, put
// ashore at its gangway when the column stops, called back and seated again before it moves on.
// Server-side only (Network.Simulates); seating replicates through NpcSeating's parenting, so a
// client sees the result with no message of its own. The decisions are CrewShiftLogic's; this
// component counts, moves people and holds the leader.
//
// The leader waits for EVERY house: the leader house sets its NpcTaskModule's departure gate to
// AllAboard(formationId), so a stay can end but the column does not leave until the last crew
// member of the last house is seated (or seated by the recall timeout).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Vehicles
{
    [RequireComponent(typeof(VesselSeats))]
    [DisallowMultipleComponent]
    public class CrewShift : MonoBehaviour
    {
        [Tooltip("Ground-level marker beside the hull where crew get off and on. Must be within the " +
                 "VesselSeats navMeshReach of walkable ground.")]
        [SerializeField] private Transform gangway;

        [Tooltip("Seconds between one crew member stepping off and the next.")]
        [SerializeField] private float disembarkInterval = 0.5f;

        [Tooltip("How far from the gangway crew may wander while ashore before they are walked back.")]
        [SerializeField] private float ashoreRadius = 40f;

        [Tooltip("How close to the gangway a recalled crew member must get to be seated.")]
        [SerializeField] private float boardRadius = 4f;

        [Tooltip("Seconds of recall (not counting time spent fighting) before anyone still ashore is seated directly.")]
        [SerializeField] private float recallTimeout = 60f;

        [Tooltip("How far around the gangway marker to look for NavMesh.")]
        [SerializeField] private float gangwaySampleDistance = 8f;

        private static readonly Dictionary<string, List<CrewShift>> Houses = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Houses.Clear();

        private readonly List<GameObject> crew = new();
        private VesselSeats seats;
        private FormationModule formation;
        private string registeredId;
        private bool gateInstalled;
        private float disembarkTimer;
        private float recallElapsed;

        public CrewState State { get; private set; } = CrewState.Aboard;
        public bool HasRoom => crew.Count < Seats.Capacity;
        private VesselSeats Seats => seats != null ? seats : seats = GetComponent<VesselSeats>();

        public Vector3 GangwayPoint
        {
            get
            {
                Vector3 point = gangway != null ? gangway.position : transform.position;
                return NavMesh.SamplePosition(point, out NavMeshHit hit, gangwaySampleDistance, NavMesh.AllAreas)
                    ? hit.position : point;
            }
        }

        /// <summary>Add a crew member: seated on a free post, or left ashore by the gangway.</summary>
        public void Take(GameObject member, bool aboard)
        {
            if (member == null || !HasRoom) return;
            crew.Add(member);
            if (aboard) Seats.Seat(member);
            else State = CrewState.Ashore;
        }

        public static CrewShift FirstWithRoom(IEnumerable<GameObject> members)
        {
            foreach (GameObject member in members)
                if (member != null && member.TryGetComponent(out CrewShift house) && house.HasRoom)
                    return house;
            return null;
        }

        public static bool AnyAshore(IEnumerable<GameObject> members)
        {
            foreach (GameObject member in members)
                if (member != null && member.TryGetComponent(out CrewShift house) && house.State != CrewState.Aboard)
                    return true;
            return false;
        }

        public static bool AllAboard(string formationId)
        {
            if (string.IsNullOrEmpty(formationId) || !Houses.TryGetValue(formationId, out List<CrewShift> houses))
                return true;
            foreach (CrewShift house in houses)
                if (house != null && house.State != CrewState.Aboard) return false;
            return true;
        }

        private void OnDisable() => Unregister();

        private void Update()
        {
            if (!Network.Simulates(this)) return;

            if (formation == null) TryGetComponent(out formation);
            KeepRegistered();

            crew.RemoveAll(member => member == null || !IsAlive(member));
            CrewCensus census = Count();
            NpcTaskModule leaderTasks = LeaderTasks();
            bool atStop = leaderTasks != null && leaderTasks.AtStop;

            CrewState next = CrewShiftLogic.Next(State, atStop, census);
            if (next != State) Enter(next);

            switch (State)
            {
                case CrewState.Disembarking: TickDisembark(); break;
                case CrewState.Ashore:       TetherAshore(); break;
                case CrewState.Recalling:    TickRecall(census); break;
            }
        }

        private void Enter(CrewState next)
        {
            State = next;
            disembarkTimer = 0f;
            recallElapsed = 0f;
            if (next == CrewState.Aboard) ClearGoals();
        }

        private void TickDisembark()
        {
            disembarkTimer -= Time.deltaTime;
            if (disembarkTimer > 0f) return;
            disembarkTimer = disembarkInterval;

            int seat = Seats.FirstOccupiedSeat();
            if (seat >= 0) Seats.Unseat(seat, GangwayPoint);
        }

        private void TetherAshore()
        {
            Vector3 home = GangwayPoint;
            foreach (GameObject member in crew)
            {
                if (IsSeated(member)) continue;
                AgentGoal goal = AgentGoal.GetOrAdd(member);
                float distance = Flat(member.transform.position - home);
                if (distance > ashoreRadius) goal.Set(home, ashoreRadius * 0.5f, "back to the walker");
                else if (goal.HasGoal && goal.HasArrived) goal.Clear();
            }
        }

        private void TickRecall(CrewCensus census)
        {
            recallElapsed = CrewShiftLogic.AdvanceRecallClock(recallElapsed, Time.deltaTime, census);
            bool force = CrewShiftLogic.ShouldForceBoard(recallElapsed, recallTimeout, census);
            Vector3 gangwayPoint = GangwayPoint;

            foreach (GameObject member in crew)
            {
                if (IsSeated(member) || IsFighting(member)) continue;

                if (force || Flat(member.transform.position - gangwayPoint) <= boardRadius)
                {
                    AgentGoal.GetOrAdd(member).Clear();
                    Seats.Seat(member);
                }
                else
                {
                    AgentGoal.GetOrAdd(member).Set(gangwayPoint, boardRadius * 0.5f, "boarding the walker");
                }
            }
        }

        private CrewCensus Count()
        {
            int aboard = 0, fighting = 0;
            foreach (GameObject member in crew)
            {
                if (IsSeated(member)) aboard++;
                if (IsFighting(member)) fighting++;
            }
            return new CrewCensus(crew.Count, aboard, fighting);
        }

        private bool IsSeated(GameObject member)
        {
            for (int i = 0; i < Seats.Capacity; i++)
                if (Seats.OccupantAt(i) == member) return true;
            return false;
        }

        private static bool IsFighting(GameObject member) =>
            member.TryGetComponent(out AgentTargeting targeting) && targeting.HasTarget;

        private static bool IsAlive(GameObject member) =>
            !member.TryGetComponent(out HealthComponent health) || health.Alive;

        private void ClearGoals()
        {
            foreach (GameObject member in crew)
                if (member != null && member.TryGetComponent(out AgentGoal goal)) goal.Clear();
        }

        private NpcTaskModule LeaderTasks()
        {
            if (formation == null || string.IsNullOrEmpty(formation.FormationId)) return null;
            FormationModule leader = FormationModule.LeaderOf(formation.FormationId);
            return leader != null && leader.TryGetComponent(out NpcTaskModule tasks) ? tasks : null;
        }

        private void KeepRegistered()
        {
            string id = formation != null ? formation.FormationId : null;
            if (id != registeredId)
            {
                Unregister();
                if (!string.IsNullOrEmpty(id))
                {
                    if (!Houses.TryGetValue(id, out List<CrewShift> list)) Houses[id] = list = new List<CrewShift>();
                    list.Add(this);
                    registeredId = id;
                }
                gateInstalled = false;
            }

            if (!gateInstalled && formation != null && formation.IsLeader && TryGetComponent(out NpcTaskModule tasks))
            {
                string gateId = registeredId;
                tasks.SetDepartureGate(() => AllAboard(gateId));
                gateInstalled = true;
            }
        }

        private void Unregister()
        {
            if (registeredId != null && Houses.TryGetValue(registeredId, out List<CrewShift> list)) list.Remove(this);
            registeredId = null;
        }

        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        private void OnValidate()
        {
            disembarkInterval = Mathf.Max(0.05f, disembarkInterval);
            ashoreRadius = Mathf.Max(boardRadius + 1f, ashoreRadius);
            boardRadius = Mathf.Max(0.5f, boardRadius);
            recallTimeout = Mathf.Max(1f, recallTimeout);
        }
    }
}
```

Check before compiling: `HealthComponent`'s namespace (`grep -rn "class HealthComponent" Assets/Game/Scripts`) and `Network`'s (`Scripts/Core/Multiplayer/Authority/Network.cs`); fix the `using`s to match. `VesselSeats.Seat(GameObject)` returns `-1` when not authority — `Update` already returns early off-authority.

- [ ] **Step 4: Run** `CrewShiftTests` (3 pass), `CrewShiftLogicTests` (still 9).
- [ ] **Step 5: Commit** (only if authorised).

---

### Task 7: `NpcWorldSim` spawns crew onto carriers, reads the crew back, folds crew first

**Files:**
- Modify: `Assets/Game/Scripts/agents/World/NpcWorldSim.cs` (`Spawn` :526-598; `Despawn` :768-801; `CaptureRecords` :1085; `DespawnMembers` :893-906)
- Test: append to `Assets/Game/Editor/Tests/RuntimeGroupTests.cs`

**Interfaces:**
- Consumes: `PlannedMember.Crew`, `NpcGroup.CrewAshore` (Task 5); `CrewShift.FirstWithRoom`, `Take`, `GangwayPoint`, `AnyAshore` (Task 6).

- [ ] **Step 1: Write the failing tests** — append to `RuntimeGroupTests` (same reflection pattern as the fixture):

```csharp
        [Test]
        public void DespawnOrder_CrewBeforeTheirCarriers()
        {
            var order = new List<string>();
            var group = new NpcGroup { Id = "city" };
            var carrier = new GameObject("Carrier"); var crewman = new GameObject("Crew");
            junk.Add(carrier); junk.Add(crewman);
            group.Live.Add(carrier);
            group.Live.Add(crewman);

            var members = (List<GameObject>)typeof(NpcWorldSim)
                .GetMethod("DespawnOrder", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { group });

            CollectionAssert.AreEqual(new[] { crewman, carrier }, members,
                "a carrier despawned first would strand its seated crew at the scene root");
        }

        [Test]
        public void ReadBackCrew_MarksTheGroupAshore_WhenAnyHouseIs()
        {
            var group = new NpcGroup { Id = "city" };
            var house = new GameObject("House"); junk.Add(house);
            var seat = new GameObject("Seat_0").transform; seat.SetParent(house.transform);
            var seats = house.AddComponent<VesselSeats>();
            var so = new UnityEditor.SerializedObject(seats);
            so.FindProperty("seats").arraySize = 1;
            so.FindProperty("seats").GetArrayElementAtIndex(0).objectReferenceValue = seat;
            so.ApplyModifiedPropertiesWithoutUndo();
            var shift = house.AddComponent<CrewShift>();
            var person = new GameObject("Crew"); junk.Add(person);
            shift.Take(person, aboard: false);
            group.Live.Add(house);

            typeof(NpcWorldSim).GetMethod("ReadBackCrew", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { group });

            Assert.IsTrue(group.CrewAshore);
        }
```

(Add `using SpaceGame.Vehicles;` and `using System.Collections.Generic;` to the fixture if missing.)

- [ ] **Step 2: Run to verify it fails** — `DespawnOrder`, `ReadBackCrew` do not exist.
- [ ] **Step 3: Implement.**

In `Spawn`, inside the member loop, replace the `seated` computation and spawn with:

```csharp
                // Crew ride a carrier already spawned earlier in the plan (templates list carriers
                // first). Marching, they wake seated; stopped, they wake on foot at its gangway.
                CrewShift carrier = planned.Crew ? CrewShift.FirstWithRoom(group.Live) : null;
                bool crewAboard = carrier != null && !group.CrewAshore;
                if (carrier != null) slot = crewAboard ? carrier.transform.position : carrier.GangwayPoint;

                bool seated = crewAboard ||
                              (riders != null && NpcGroupComposition.Rides(planned) && riders.Count < seats);
```

and after `group.Live.Add(member);`:

```csharp
                if (carrier != null) carrier.Take(member, crewAboard);
                else if (planned.Crew)
                    Debug.LogWarning($"[NpcWorldSim] '{template.id}' has more crew than crew posts; " +
                                     $"'{member.name}' walks.", this);
```

Keep `if (seated) riders.Add(member);` but guard it: `if (seated && riders != null && carrier == null) riders.Add(member);`.

Add, next to `Despawn`:

```csharp
        /// <summary>
        /// Whether any of the group's carriers still has crew off it — read before the group folds or
        /// saves, so it comes back on foot where it left them rather than seated mid-stop.
        /// </summary>
        private static void ReadBackCrew(NpcGroup group) => group.CrewAshore = CrewShift.AnyAshore(group.Live);

        /// <summary>
        /// Members in reverse spawn order. Crew are spawned after the carriers they sit on and are
        /// parented under the carrier's NetworkObject, so they must go first or netcode lifts them to
        /// the scene root with nothing left to release them.
        /// </summary>
        private static List<GameObject> DespawnOrder(NpcGroup group)
        {
            var order = new List<GameObject>(group.Live);
            order.Reverse();
            return order;
        }
```

In `Despawn`, before `DespawnTransport`: `ReadBackCrew(group);`. In `CaptureRecords`, inside `if (groups[i].Spawned)`: add `ReadBackCrew(groups[i]);`. In `DespawnMembers`, replace `foreach (GameObject member in group.Live)` with `foreach (GameObject member in DespawnOrder(group))`.

Add `using SpaceGame.Vehicles;` to `NpcWorldSim.cs` if it is not there.

- [ ] **Step 4: Run** `RuntimeGroupTests`, `NpcWorldSimTests`, `WarPartyPersistenceTests`, `GroupRecordTests` → green.
- [ ] **Step 5: Commit** (only if authorised).

---

### Task 8: The Striders faction and people

**Files:**
- Modify: `Assets/Game/Editor/Agents/RosterAuthoring.cs` (constants :22-86; new `AuthorStriderFaction` beside `AuthorSkyTribeFaction` :180-229)
- Modify: `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs` (palettes :121-143; dialog lines :158-168; recipes :186-222; `NomadRecipe` :42-84; menu methods :352-391; module wiring near the `WanderModule` block ~:1467)
- Test: `Assets/Game/Editor/Tests/StriderTribeAssetTests.cs`

**Interfaces:**
- Produces: `RosterAuthoring.StriderFactionPath`, `RosterAuthoring.StriderRosterPath`; `NomadPrefabBuilder.StriderNomads` (`NomadRecipe[]`); `NomadRecipe.TravelsToGoals`; menus `Tools/SpaceGame/Agents/Author Strider Faction`, `Tools/SpaceGame/Agents/Build Strider Nomad NPCs`.

- [ ] **Step 1: Write the failing tests** — mirror `SkyTribeAssetTests`:

```csharp
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class StriderTribeAssetTests
    {
        private static FactionDefinition Striders() => AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.StriderFactionPath);

        [Test]
        public void StriderFaction_ExistsWithNeutralDefaultAndAnId()
        {
            FactionDefinition striders = Striders();
            Assert.IsNotNull(striders, "run Tools/SpaceGame/Agents/Author Strider Faction");
            Assert.AreEqual("The Striders", striders.factionName);
            Assert.AreEqual(FactionRelationship.Neutral, striders.defaultStance,
                "a Hostile-default faction is never tracked by the goodwill ledger (spacegame-tribe §1)");
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(RosterAuthoring.StriderFactionPath), striders.ID);
        }

        [Test]
        public void EveryStriderNomad_IsBakedOnTheStriders_AndCanWalkToAGoal()
        {
            foreach (var recipe in NomadPrefabBuilder.StriderNomads)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.PrefabPath);
                Assert.IsNotNull(prefab, $"{recipe.PrefabPath} — run Tools/SpaceGame/Agents/Build Strider Nomad NPCs");
                var baked = new SerializedObject(prefab.GetComponent<EntityFaction>()).FindProperty("faction").objectReferenceValue;
                Assert.AreEqual(Striders(), baked, $"{prefab.name}: faction is not replicated, it must be baked");
                Assert.IsNotNull(prefab.GetComponent<GoalTravelModule>(), $"{prefab.name}: crew walk back to the gangway by goal");
                Assert.IsNotNull(prefab.GetComponent<Unity.Netcode.NetworkObject>());
            }
        }

        [Test]
        public void StriderCloth_IsItsOwnMaterials_NotSandsOrSkys()
        {
            string[] prefixes = { "NomadCloth_", "SkyNomadCloth_" };
            foreach (var recipe in NomadPrefabBuilder.StriderNomads)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.PrefabPath);
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials.Where(m => m != null))
                    Assert.IsFalse(prefixes.Any(p => material.name.StartsWith(p)),
                        $"{prefab.name} wears {material.name}: one tribe's build would recolour another's");
            }
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** (type-check: `StriderFactionPath`, `StriderNomads` missing).
- [ ] **Step 3: Implement `RosterAuthoring`.** Constants:

```csharp
        public const string StriderFactionPath = "Assets/Game/ScriptableObjects/Factions/Core/StriderFaction.asset";
        public const string StriderRosterPath = RosterDir + "/Striders.asset";
        private const string StriderHostileLinesPath = RosterDir + "/StriderHostileLines.asset";
        public const string StriderWarPartyTemplateId = "strider-war-party";
        public const string StriderCityTemplateId = "strider-city";
        // Rust red: the colour the whole column reads as from across a dune (spec §2).
        private static readonly Color StriderHudColor = new Color(0.72f, 0.22f, 0.12f);
```

Refactor `AuthorSkyTribeFaction` so its body becomes a private `AuthorTribeFaction(string path, string displayName, Color hudColor)` returning the `FactionDefinition` (the same code: `LoadOrCreate`, name, `Neutral`, colour, ID stamp, `CopyRelationshipRows(sand, faction)`, `SaveAssets`, `WithWorldSim` ledger append), and make `AuthorSkyTribeFaction` call it with `(SkyFactionPath, "Sky Tribe", SkyTribeHudColor)`. Then:

```csharp
        [MenuItem("Tools/SpaceGame/Agents/Author Strider Faction")]
        public static void AuthorStriderFaction() =>
            AuthorTribeFaction(StriderFactionPath, "The Striders", StriderHudColor);
```

(Mirroring Sand's rows is the Sky decision too; Sand has none today, so Striders get none, matching spec §2.) Re-run `SkyTribeAssetTests` after the refactor — it must stay green.

- [ ] **Step 4: Implement `NomadPrefabBuilder`.** Add a field to `NomadRecipe`:

```csharp
            /// Walks to an AgentGoal someone else sets (GoalTravelModule). A walking city's crew are
            /// sent back to their gangway this way; nobody else needs it.
            public bool TravelsToGoals;
```

Palette and lines after `SkySoldierCloth` / `SkyDialogLines` (static initialisers run in textual order — they must precede the recipes):

```csharp
        // Heavy rust red with iron-dark scarves: the Striders read as a red column from across a
        // dune (spec §2, GDC-L1-LEVEL-0001).
        private static readonly ClothPalette StriderCloth = new ClothPalette
        {
            MaterialPrefix = "StriderNomadCloth",
            Cloth = new Color(0.62f, 0.16f, 0.10f),
            Accents = new[] { ("_Scarf_Long", new Color(0.22f, 0.20f, 0.19f)) },
        };

        private static readonly string[] StriderDialogLines =
        {
            "Mind the legs. They don't stop for anyone.",
            "Everything out here is parts, if you look long enough.",
            "We were walking before your ship fell. We'll be walking after.",
            "Bring us scrap and we'll talk.",
            "The houses carry us. We carry the houses. Fair trade.",
            "Hear that grinding? That's the crawlers eating a hill.",
            "Don't touch the rig. It bites.",
            "Rust is just the metal remembering where it came from.",
        };
```

Recipes after `SkyTribePeople`:

```csharp
        private const string StriderCharacterFolder = CharacterFolder + "/Striders";

        // The same four bodies, dyed rust red and sworn to the Striders. They walk back to their
        // walking house by goal when it calls them (CrewShift).
        public static readonly NomadRecipe[] StriderNomads = ArmedNomadVariants
            .Select(variant =>
            {
                NomadRecipe recipe = ArmedNomad(variant, "StriderNomad_", StriderCharacterFolder,
                                                RosterAuthoring.StriderFactionPath, RosterAuthoring.StriderRosterPath,
                                                StriderCloth, StriderDialogLines);
                recipe.TravelsToGoals = true;
                return recipe;
            })
            .ToArray();
```

Menu method after `BuildSkyNomads`:

```csharp
        /// The four Strider nomads. Run twice on a fresh project, around Author Strider Roster: the
        /// roster validates the prefabs' baked faction, and the prefabs bake the roster's hand items.
        [MenuItem("Tools/SpaceGame/Agents/Build Strider Nomad NPCs")]
        public static void BuildStriderNomads()
        {
            var prefabs = BuildArmedNomads(StriderNomads);
            if (prefabs.Count == 0) return;
            RegisterBuiltNomads();
            Debug.Log($"[NomadPrefabBuilder] Built {prefabs.Count} Strider nomad(s), registered them " +
                      "as network prefabs, wired their savers and ragdolls.");
        }
```

In `BuildPrefab`, right after the `WanderModule` configuration block (~:1467-1476), add:

```csharp
            if (recipe.TravelsToGoals)
            {
                // Fallback + 1: above wander, so a goal someone set wins; below everything reactive,
                // so a fight still wins over walking home.
                var travel = FindComponent(root, "SpaceGame.Agents.GoalTravelModule") ??
                             root.AddComponent(System.Type.GetType("SpaceGame.Agents.GoalTravelModule, Assembly-CSharp"));
                var so = new SerializedObject(travel);
                SetInt(so, "priority", ModulePriority.Fallback + 1);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
```

Before writing this, read how the builder adds other modules around :1440-1500 (it uses `FindComponent(root, "<full name>")` and its own `SetInt(so, …)`); use the exact same helpers and style — if the file adds modules by `root.AddComponent<T>()` directly, do that instead.

- [ ] **Step 5: Run the tools in the editor** (via `execute_code`): `RosterAuthoring.AuthorStriderFaction()`, then `NomadPrefabBuilder.BuildStriderNomads()` (pass 1 — no roster yet; it logs "No roster at …Striders.asset" for the hand items, expected until Task 10). Read back: 4 prefabs under `Prefabs/Agents/Characters/Striders/`, `persistentScene.unity` ledger `tribes` now lists 3 factions.
- [ ] **Step 6: Run** `StriderTribeAssetTests` (3 pass), `SkyTribeAssetTests`, `SkyRosterAssetTests`, `RosterAssetTests`, `FactionAssetTests` → green.
- [ ] **Step 7: Commit** (only if authorised).

---

### Task 9: The crab outrider

**Files:**
- Modify: `Assets/Game/Editor/Creatures/CrabWalkerBuilder.cs` (split `Build(int)` :60-115 into `BuildBody(int)` + save)
- Modify: `Assets/Game/Editor/Creatures/RobotHorseBuilder.cs` (`AddOutriderBehaviour` :605 → `internal static`)
- Create: `Assets/Game/Editor/Creatures/StriderCrabOutriderBuilder.cs`
- Test: `Assets/Game/Editor/Tests/StriderCrabOutriderTests.cs`

**Interfaces:**
- Consumes: `NomadPrefabBuilder.StriderNomads[0].PrefabPath` (Task 8); `AgentNetworkWiring.Ensure(GameObject)`; `SerializedFields`.
- Produces: `StriderCrabOutriderBuilder.PrefabPath = "Assets/Game/Prefabs/Agents/Characters/Striders/StriderCrabOutrider.prefab"`; `CrabWalkerBuilder.BuildBody(int legCount)` returning an unsaved root (or null).

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class StriderCrabOutriderTests
    {
        private static GameObject Prefab()
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(StriderCrabOutriderBuilder.PrefabPath);
            Assert.IsNotNull(p, "run Tools/Creatures/Build Strider Crab Outrider");
            return p;
        }

        [Test]
        public void IsAStriderAgent_ThatReplicatesAndSaves()
        {
            GameObject crab = Prefab();
            Assert.IsNotNull(crab.GetComponent<AgentController>());
            Assert.IsNotNull(crab.GetComponent<HealthComponent>());
            var net = crab.GetComponent<Unity.Netcode.NetworkObject>();
            Assert.IsNotNull(net); Assert.AreNotEqual(0u, net.PrefabIdHash);
            Assert.IsFalse(string.IsNullOrEmpty(crab.GetComponent<SpaceGame.Core.Persistence.SaveableEntity>().PrefabId));
            var faction = new SerializedObject(crab.GetComponent<EntityFaction>()).FindProperty("faction").objectReferenceValue;
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.StriderFactionPath), faction);
        }

        [Test]
        public void CarriesAStriderRider_AndHasNoAttack()
        {
            GameObject crab = Prefab();
            var passenger = crab.GetComponent<NpcPassenger>();
            Assert.IsNotNull(passenger);
            var rider = new SerializedObject(passenger).FindProperty("riderPrefab").objectReferenceValue as GameObject;
            Assert.IsNotNull(rider);
            StringAssert.StartsWith("StriderNomad_", rider.name, "faction is not replicated: the rider must ship as a Strider");
            Assert.IsNull(crab.GetComponent<CloseCombatModule>(), "the mount carries, the rider shoots");
            Assert.IsNull(crab.GetComponent<AgentRangedCombatModule>());
        }

        [Test]
        public void KeepsUpWithTheCity()
        {
            float speed = new SerializedObject(Prefab().GetComponent<SpaceGame.Creatures.CrabDriver>()).FindProperty("moveSpeed").floatValue;
            Assert.GreaterOrEqual(speed, StriderCityBuilder.CityLeaderSpeed,
                "LeggedDriver cannot exceed its moveSpeed; a slower crab falls behind the column for good");
            float step = new SerializedObject(Prefab().GetComponent<SpaceGame.Creatures.Crab.CrabLocomotion>()).FindProperty("stepDuration").floatValue;
            Assert.LessOrEqual(step, 0.22f + 1e-4f, "the stock 0.4 s gait caps the crab at 1.76 m/s whatever moveSpeed says (spike)");
        }
    }
}
```

(`StriderCityBuilder.CityLeaderSpeed` is produced in Task 12; until then this one test does not compile — write Task 12's constant first if running this task alone: `public const float CityLeaderSpeed = RigWalkerMoveSpeed * CityTravelMultiplier;` with `RigWalkerMoveSpeed = 6f`, `CityTravelMultiplier = 0.45f`, using the Spike findings' numbers.)

- [ ] **Step 2: Run to verify it fails.**
- [ ] **Step 3: Split `CrabWalkerBuilder.Build`.** Everything up to and including `WireLocomotion(root, armature);` moves into:

```csharp
        /// <summary>A crab walker body with its locomotion wired and nothing else, not yet saved.
        /// Null when the model is missing. The caller owns (and destroys) the returned root.</summary>
        public static GameObject BuildBody(int legCount)
```

and `Build(int legCount)` becomes `var root = BuildBody(legCount); if (root == null) return;` followed by its existing save/log lines. Rebuild `CrabWalker6` once (`Tools/Creatures/Build Crab Walker Prefabs`) and diff the prefab — only irrelevant ordering noise is acceptable; if the wiring passes' components (savers, faction) are gone, that is the pre-existing builder behaviour, re-run `Tools/SpaceGame/Agents/Wire Entity Factions` and `Tools > Save System > Wire Saveable Prefabs`.

- [ ] **Step 4: Make `RobotHorseBuilder.AddOutriderBehaviour` `internal static`** (no body change).
- [ ] **Step 5: Write the builder**

```csharp
// The Striders' crab outrider: the six-legged crab walker with a Strider on its back, flanking the
// walking city and serving as the tribe's war-party mount (roster role Rider). The mount carries,
// the rider shoots -- the crab has no attack (AgentSystem.md).
//
// Re-run from: Tools > Creatures > Build Strider Crab Outrider
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public static class StriderCrabOutriderBuilder
    {
        public const string PrefabPath = "Assets/Game/Prefabs/Agents/Characters/Striders/StriderCrabOutrider.prefab";
        private const int Legs = 6;

        /// <summary>Faster than the wild crab's 1.6 m/s so it keeps up with the city's 2.7 (Spike findings).</summary>
        private const float OutriderMoveSpeed = 3.2f;
        /// <summary>The stock gait (stepDuration 0.4) caps the crab at 1.76 m/s whatever moveSpeed says;
        /// 0.22 s steps reach 3.2 m/s with the feet still planted (spike: slip 0.016 m/m, tilt 0 deg).</summary>
        private const float OutriderStepDuration = 0.22f;
        private const int Health = 180;
        /// <summary>Rider's feet above the shell's top, in metres.</summary>
        private const float SeatRise = 0.2f;

        [MenuItem("Tools/Creatures/Build Strider Crab Outrider")]
        public static void Build()
        {
            GameObject root = CrabWalkerBuilder.BuildBody(Legs);
            if (root == null) return;
            root.name = "StriderCrabOutrider";

            var driver = root.GetComponent<SpaceGame.Creatures.CrabDriver>();
            Set(driver, so => SerializedFields.SetFloat(so, "moveSpeed", OutriderMoveSpeed));
            Set(root.GetComponent<SpaceGame.Creatures.Crab.CrabLocomotion>(),
                so => SerializedFields.SetFloat(so, "stepDuration", OutriderStepDuration));

            var brain = root.AddComponent<AgentController>();
            Set(brain, so => SerializedFields.Set(so, "MotorComponent", driver));

            var health = root.AddComponent<HealthComponent>();
            Set(health, so => { SerializedFields.SetInt(so, "maxHealth", Health); SerializedFields.SetInt(so, "currentHealth", Health); });
            root.AddComponent<HealthReactionModule>();

            var faction = root.AddComponent<EntityFaction>();
            Set(faction, so =>
            {
                SerializedFields.Set(so, "faction", AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.StriderFactionPath));
                SerializedFields.Set(so, "relationshipTable", AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(RosterAuthoring.GlobalRelationshipsPath));
            });

            root.AddComponent<PerceptionModule>();
            root.AddComponent<AgentTargeting>();
            RobotHorseBuilder.AddOutriderBehaviour(root);

            var formation = root.AddComponent<FormationModule>();
            Set(formation, so =>
            {
                SerializedFields.SetInt(so, "priority", ModulePriority.Social);
                SerializedFields.SetString(so, "formationId", string.Empty);
            });
            var travel = root.AddComponent<GoalTravelModule>();
            Set(travel, so => SerializedFields.SetInt(so, "priority", ModulePriority.Fallback + 1));

            AttachRider(root);

            var tracked = root.AddComponent<SpaceGame.World.SceneTracked>();
            Set(tracked, so => SerializedFields.SetEnumByName(so, "policy", nameof(SpaceGame.World.SceneTracked.UnloadPolicy.Migrate)));
            AgentNetworkWiring.Ensure(root);
            root.AddComponent<SaveableEntity>();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            Debug.Log(NetworkPrefabRegistrar.Sync(out _, out _));
            if (!SaveableWiring.TryWirePrefabs())
                Debug.LogError("[StriderCrabOutrider] Save wiring failed; run Tools > Save System > Wire Saveable Prefabs.");
            RagdollWiring.WirePrefabs();
            Debug.Log($"[StriderCrabOutrider] Built {PrefabPath}.");
        }

        private static void AttachRider(GameObject root)
        {
            Bounds shell = new Bounds(root.transform.position, Vector3.zero);
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) shell.Encapsulate(r.bounds);

            var seat = new GameObject("SeatPoint");
            seat.transform.SetParent(root.transform, false);
            seat.transform.position = new Vector3(shell.center.x, shell.max.y + SeatRise, shell.center.z);

            var rider = AssetDatabase.LoadAssetAtPath<GameObject>(NomadPrefabBuilder.StriderNomads[0].PrefabPath);
            if (rider == null) Debug.LogError("[StriderCrabOutrider] Build the Strider nomads first; the crab rides empty.");

            var passenger = root.AddComponent<NpcPassenger>();
            Set(passenger, so =>
            {
                SerializedFields.Set(so, "riderPrefab", rider);
                SerializedFields.Set(so, "seatPoint", seat.transform);
                SerializedFields.SetVector3(so, "seatOffset", Vector3.zero);
                SerializedFields.SetBool(so, "spawnOnStart", true);
            });
        }

        private static void Set(Object target, System.Action<SerializedObject> edit)
        {
            var so = new SerializedObject(target);
            edit(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
```

Before compiling: confirm each type's namespace with `grep -rn "class <Type>\b" Assets/Game/Scripts` (`HealthReactionModule`, `SceneTracked`, `CrabDriver`, `NetworkPrefabRegistrar`, `SaveableWiring`, `RagdollWiring`) and the exact `SerializedFields` method names in `Assets/Game/Editor/Support/SerializedFields.cs`; adjust `using`s/calls to match. Do not add a second copy of helpers that exist.

- [ ] **Step 6: Run the builder** in the editor; read the prefab back.
- [ ] **Step 7: Run** `StriderCrabOutriderTests` (3 pass; `KeepsUpWithTheCity` once Task 12's constant exists), `RobotHorsePrefabTests` (still green).
- [ ] **Step 8: Commit** (only if authorised).

---

### Task 10: The Striders roster and war party

**Files:**
- Modify: `Assets/Game/Editor/Agents/RosterAuthoring.cs` (`AuthorRoster` :139-169 gains a hand-items parameter; new `AuthorStriderRoster`; `WireWorldSim` :291-361 adds the Strider war party)
- Test: `Assets/Game/Editor/Tests/StriderRosterAssetTests.cs`

**Interfaces:**
- Consumes: `StriderNomads` (Task 8), `StriderCrabOutriderBuilder.PrefabPath` (Task 9).
- Produces: `Rosters/Striders.asset`, `StriderHostileLines.asset`, a `strider-war-party` template.

- [ ] **Step 1: Write the failing tests** (mirror `SkyRosterAssetTests`):

```csharp
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class StriderRosterAssetTests
    {
        private static FactionRoster Roster => AssetDatabase.LoadAssetAtPath<FactionRoster>(RosterAuthoring.StriderRosterPath);

        [Test]
        public void Roster_Exists_AndValidates()
        {
            Assert.IsNotNull(Roster, "run Tools/SpaceGame/Agents/Author Strider Roster");
            var problems = RosterValidation.Problems(Roster);
            Assert.IsEmpty(problems, string.Join("\n", problems));
            Assert.AreSame(Roster, Roster.faction.roster, "faction ↔ roster back-reference");
        }

        [Test]
        public void Roster_HasTheSpecTiers()
        {
            AssertTier(0, (RosterRole.Rider, 2));
            AssertTier(1, (RosterRole.Rider, 2), (RosterRole.Warrior, 2));
            AssertTier(2, (RosterRole.Rider, 3), (RosterRole.Warrior, 4));
        }

        [Test]
        public void HandItems_AreTheThree_AndBakedOnEveryStriderNomad()
        {
            CollectionAssert.AreEquivalent(new[] { "basicgun", "GravelBlaster", "NetGun" }, Roster.handItems.Select(i => i.name));
            foreach (var recipe in NomadPrefabBuilder.StriderNomads)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(recipe.PrefabPath);
                var candidates = new SerializedObject(prefab.GetComponent<NpcRandomLoadout>()).FindProperty("candidates");
                var baked = Enumerable.Range(0, candidates.arraySize).Select(i => candidates.GetArrayElementAtIndex(i).objectReferenceValue as InventoryItem);
                CollectionAssert.AreEquivalent(Roster.handItems, baked, $"{prefab.name}: rebuild after authoring the roster");
            }
        }

        [Test]
        public void Riders_AreTheCrabOutrider()
        {
            var riders = Roster.members.Where(m => m.role == RosterRole.Rider).Select(m => AssetDatabase.GetAssetPath(m.prefab)).ToArray();
            CollectionAssert.AreEqual(new[] { StriderCrabOutriderBuilder.PrefabPath }, riders);
        }

        [Test]
        public void HostileLines_AreAuthored() => Assert.AreEqual(8, Roster.hostileLines.lines.Length);

        private static void AssertTier(int index, params (RosterRole role, int count)[] expected) =>
            CollectionAssert.AreEqual(expected, Roster.warPartyTiers[index].roles.Select(r => (r.role, r.count)).ToArray(), $"tier {index}");
    }
}
```

- [ ] **Step 2: Run to verify it fails.**
- [ ] **Step 3: Give `AuthorRoster` the hand items.** Add parameter `string[] handItemPaths` after `members` (before `tiers`), replace `NomadHandItemPaths.Select(...)` with `handItemPaths.Select(Load<InventoryItem>).Where(i => i != null).ToArray()`, and pass `NomadHandItemPaths` from `AuthorSandRoster` and `AuthorSkyRoster`.
- [ ] **Step 4: Add the Strider roster**

```csharp
        // The Striders carry the three salvage guns (design §3.6 named the first two).
        private static readonly string[] StriderHandItemPaths =
        {
            "Assets/Game/Resources/Items/Artifacts/basicgun.asset",
            "Assets/Game/Resources/Items/Artifacts/GravelBlaster.asset",
            "Assets/Game/Resources/Items/Artifacts/NetGun.asset",
        };

        // Shouted on a war party's first sight of its quarry. People who live on walking machines.
        private static readonly string[] StriderHostileLines =
        {
            "You'll make good scrap.",
            "Strip it down to the frame!",
            "The city remembers what you did.",
            "Nothing runs forever. Neither will you.",
            "Grind them under!",
            "We walked a thousand miles. We'll walk one more for you.",
            "Every bolt you carry is ours now.",
            "Rust takes everything. We just take it first.",
        };

        [MenuItem("Tools/SpaceGame/Agents/Author Strider Roster")]
        public static void AuthorStriderRoster()
        {
            GameObject[] people = NomadPrefabBuilder.StriderNomads
                .Select(recipe => Load<GameObject>(recipe.PrefabPath)).Where(p => p != null).ToArray();
            GameObject crab = Load<GameObject>(StriderCrabOutriderBuilder.PrefabPath);

            RosterMember[] members = people.Select(p => Member(RosterRole.Scout, p))
                .Concat(people.Select(p => Member(RosterRole.Warrior, p)))
                .Concat(crab != null ? new[] { Member(RosterRole.Rider, crab) } : Array.Empty<RosterMember>())
                .ToArray();

            AuthorRoster("Striders", StriderFactionPath, StriderRosterPath, StriderHostileLinesPath, StriderHostileLines,
                members, StriderHandItemPaths, new[]
                {
                    Tier((RosterRole.Rider, 2)),
                    Tier((RosterRole.Rider, 2), (RosterRole.Warrior, 2)),
                    Tier((RosterRole.Rider, 3), (RosterRole.Warrior, 4)),
                });
        }
```

- [ ] **Step 5: The war-party template.** In `WireWorldSim`, add `var striders = Load<FactionDefinition>(StriderFactionPath);` to the null checks; track `striderParty` in the id loop (`if (id == StriderWarPartyTemplateId) striderParty = i;`); after the Sky party block:

```csharp
            if (striderParty < 0)
            {
                templates.GetArrayElementAtIndex(warParty).DuplicateCommand();
                striderParty = warParty + 1;
                if (skyParty > warParty) skyParty++;   // the duplicate was inserted before it
            }
            SetWarParty(templates.GetArrayElementAtIndex(striderParty), StriderWarPartyTemplateId, "Strider War Party", striders);
```

(Recompute `skyParty`'s index before the Sky block if the Strider block runs first — keep the Strider block **after** the Sky block so no index already used shifts.)

- [ ] **Step 6: Run in the editor**, in order: `AuthorStriderRoster` → `NomadPrefabBuilder.BuildStriderNomads` (pass 2, bakes the three hand items) → `StriderCrabOutriderBuilder.Build` (re-seats a rider carrying the baked guns) → `WireWorldSim`. Read back `persistentScene.unity`: a `strider-war-party` template with tribe Striders, `runtimeOnly: 1`, `bountyHunters: 1`.
- [ ] **Step 7: Run** `StriderRosterAssetTests` (5 pass), `RosterAssetTests`, `SkyRosterAssetTests`, `WarPartyDirectorTests` → green.
- [ ] **Step 8: Commit** (only if authorised).

---

### Task 11: The worker crawler

**Files:**
- Modify: `Assets/Game/Editor/Vehicles/DesertCrawlerBuilder.cs` (`WireBrain` :444-476; `Build` :146-161)
- Modify: `Assets/Game/Editor/Agents/EntityFactionWiring.cs` (`Assignments` :43-50 + its comment :38-40)
- Test: `Assets/Game/Editor/Tests/DesertCrawlerWorkerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class DesertCrawlerWorkerTests
    {
        private static GameObject Crawler() => AssetDatabase.LoadAssetAtPath<GameObject>(DesertCrawlerBuilder.PrefabPath);

        [Test]
        public void BelongsToTheStriders()
        {
            var faction = new SerializedObject(Crawler().GetComponent<EntityFaction>()).FindProperty("faction").objectReferenceValue;
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.StriderFactionPath), faction);
        }

        [Test]
        public void FollowsAColumn_AndWorksAWideRingAtAStop()
        {
            var formation = Crawler().GetComponent<FormationModule>();
            Assert.IsNotNull(formation);
            var so = new SerializedObject(formation);
            Assert.AreEqual(ModulePriority.Social, so.FindProperty("priority").intValue);
            Assert.IsEmpty(so.FindProperty("formationId").stringValue, "inert until the city names the band");
            Assert.AreEqual(DesertCrawlerBuilder.WorkRadius, so.FindProperty("restRadius").floatValue, 0.01f);
            Assert.IsNotNull(Crawler().GetComponent<SpaceGame.Vehicles.Tools.CrawlerToolModule>(), "the tool rig is the rock collecting");
        }

        [Test]
        public void StillReplicatesAndSaves_AfterAWholesaleRebuild()
        {
            var net = Crawler().GetComponent<Unity.Netcode.NetworkObject>();
            Assert.IsNotNull(net); Assert.AreNotEqual(0u, net.PrefabIdHash);
            Assert.IsFalse(string.IsNullOrEmpty(Crawler().GetComponent<SpaceGame.Core.Persistence.SaveableEntity>().PrefabId));
        }
    }
}
```

(Confirm `CrawlerToolModule`'s namespace by grep and fix the test's qualifier.)

- [ ] **Step 2: Run to verify it fails.**
- [ ] **Step 3: Implement.** `EntityFactionWiring.Assignments`: `("DesertCrawler", "StriderFaction")`; update its comment (the crawler is a Strider worker now). `DesertCrawlerBuilder`:

```csharp
        /// <summary>How far from the column's leader a worker digs while the city is stopped: the
        /// formation rest ring, so the crawler wanders the site and its tools run at every pause.</summary>
        public const float WorkRadius = 60f;
        // Machine-sized formation tolerances: a 21 m crawler cannot hold a person-sized slot.
        private const float SlotTolerance = 8f;
        private const float RegroupDistance = 150f;
        private const float FormationNavSample = 20f;
```

At the end of `WireBrain`:

```csharp
            // A Strider worker: follows the walking city's column, and at a stop the wide rest ring
            // hands the frame to WanderModule so the crawler roams the site and digs.
            var formation = Ensure<FormationModule>(root);
            SetInt(formation, "priority", ModulePriority.Social);
            Set(formation, "formationId", string.Empty);   // use the file's string setter; grep its helpers
            SetFloat(formation, "restRadius", WorkRadius);
            SetFloat(formation, "slotTolerance", SlotTolerance);
            SetFloat(formation, "regroupDistance", RegroupDistance);
            SetFloat(formation, "navSampleDistance", FormationNavSample);
```

In `Build`, before `SaveAsPrefabAsset`: `AgentNetworkWiring.Ensure(root);` — the builder rewrites the prefab wholesale and has always relied on that pass being re-run; calling it here means a rebuild no longer drops the netcode. After the save: `Debug.Log(NetworkPrefabRegistrar.Sync(out _, out _)); if (!SaveableWiring.TryWirePrefabs()) Debug.LogError("[DesertCrawlerBuilder] Save wiring failed.");`.

- [ ] **Step 4: Rebuild** (`Tools/Vehicles/Build Desert Crawler Prefab`). Then open `Chunk_7_4.unity` and check the placed crawler instance is intact (no missing scripts, faction now Striders); save the scene only if Unity marked it changed by the rebuild.
- [ ] **Step 5: Run** `DesertCrawlerWorkerTests` (3 pass) and any existing crawler tests (`grep -l DesertCrawler Assets/Game/Editor/Tests`).
- [ ] **Step 6: Commit** (only if authorised).

---

### Task 12: The habitat walker variant

**Files:**
- Create: `Assets/Game/Editor/Vehicles/StriderCityBuilder.cs`
- Test: `Assets/Game/Editor/Tests/StriderHabitatWalkerTests.cs`

**Interfaces:**
- Consumes: `CrewShift` (Task 6), `RosterAuthoring.StriderFactionPath`.
- Produces: `StriderCityBuilder.HabitatPath = "Assets/Game/Prefabs/Agents/Vehicles/Ground/StriderHabitatWalker.prefab"`, `StriderCityBuilder.CrewPosts = 6`, `StriderCityBuilder.CityLeaderSpeed`, `StriderCityBuilder.CityTravelMultiplier`.

- [ ] **Step 0: `FormationModule.holdSlotAtRest`** (spike: parked followers bunched into the rest ring and two houses clipped at 17–19 m; user 2026-09-24: **nothing parks** — machines travel with the city and keep their place beside it at stops). In `Assets/Game/Scripts/agents/Modules/Formation/FormationModule.cs` add `[Tooltip("While the leader is stopped, hold this follower's marching slot instead of joining the rest ring. Machines that must not bunch up turn this on.")] [SerializeField] private bool holdSlotAtRest;` (default false keeps every existing prefab's behaviour) and choose the target through a new pure `FormationMath.UseMarchSlot(bool leaderMoving, bool holdSlotAtRest) => leaderMoving || holdSlotAtRest;` — slot = `UseMarchSlot(...) ? SlotPosition(...) : RestPosition(...)`, tolerance = `UseMarchSlot(...) ? slotTolerance : restRadius`. At rest the march slot is computed from the leader's last smoothed heading, so a stopped column keeps its shape. Tests appended to `FormationMathTests`: `UseMarchSlot(true,false)`, `UseMarchSlot(false,true)` are true; `UseMarchSlot(false,false)` is false. Run `FormationMathTests`.

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class StriderHabitatWalkerTests
    {
        private const string RigWalkerPath = "Assets/Game/Prefabs/Agents/Vehicles/Ground/RigWalker.prefab";
        private static GameObject Habitat() => AssetDatabase.LoadAssetAtPath<GameObject>(StriderCityBuilder.HabitatPath);

        [Test]
        public void IsAVariantOfTheRigWalker_WithTheHelmRemoved()
        {
            GameObject habitat = Habitat();
            Assert.IsNotNull(habitat, "run Tools/SpaceGame/Vehicles/Build Strider Habitat Walker");
            Assert.AreEqual(PrefabAssetType.Variant, PrefabUtility.GetPrefabAssetType(habitat));
            Assert.IsNull(habitat.GetComponent<MountModule>(), "nobody takes the helm of a Strider house");
            Assert.IsNull(habitat.GetComponent<SteerModule>());
            Assert.IsNull(habitat.GetComponentInChildren<MountStation>(true));
            Assert.IsNotNull(habitat.GetComponent<WalkerPlatformCarrier>(), "players can still ride the deck");
            Assert.IsNull(habitat.GetComponent<WanderModule>(), "a parked house must not wander into its neighbour (spike)");
            Assert.IsTrue(new SerializedObject(habitat.GetComponent<FormationModule>()).FindProperty("holdSlotAtRest").boolValue,
                "a house keeps its place in the column at a stop -- nothing parks (user 2026-09-24)");
        }

        [Test]
        public void TheRigWalkerItself_IsStillAPlayerVehicle()
        {
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigWalkerPath);
            Assert.IsNotNull(rig.GetComponent<MountModule>());
            Assert.IsNull(rig.GetComponent<CrewShift>());
        }

        [Test]
        public void HasSixCrewPosts_AGangwayOnTheGround_AndACrewShift()
        {
            GameObject habitat = Habitat();
            var seats = habitat.GetComponent<VesselSeats>();
            Assert.AreEqual(StriderCityBuilder.CrewPosts, seats.Capacity);
            var shift = habitat.GetComponent<CrewShift>();
            Assert.IsNotNull(shift);
            var gangway = new SerializedObject(shift).FindProperty("gangway").objectReferenceValue as Transform;
            Assert.IsNotNull(gangway);
            Assert.AreEqual(0f, gangway.localPosition.y, 0.01f, "the gangway is at the walker's feet, not on the deck");
        }

        [Test]
        public void CanLeadOrFollowTheCity_AsAStrider()
        {
            GameObject habitat = Habitat();
            Assert.IsNotNull(habitat.GetComponent<NpcTaskModule>());
            Assert.IsNotNull(habitat.GetComponent<GoalTravelModule>());
            var formation = new SerializedObject(habitat.GetComponent<FormationModule>());
            Assert.AreEqual(ModulePriority.Social, formation.FindProperty("priority").intValue);
            Assert.IsEmpty(formation.FindProperty("formationId").stringValue);
            var faction = new SerializedObject(habitat.GetComponent<EntityFaction>()).FindProperty("faction").objectReferenceValue;
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.StriderFactionPath), faction);
            var net = habitat.GetComponent<Unity.Netcode.NetworkObject>();
            Assert.AreNotEqual(0u, net.PrefabIdHash);
            Assert.AreNotEqual(AssetDatabase.LoadAssetAtPath<GameObject>(RigWalkerPath).GetComponent<Unity.Netcode.NetworkObject>().PrefabIdHash,
                net.PrefabIdHash, "a variant needs its own network identity");
        }
    }
}
```

(Confirm namespaces of `MountModule`, `SteerModule`, `MountStation`, `WalkerPlatformCarrier` by grep and adjust `using`s.)

- [ ] **Step 2: Run to verify it fails.**
- [ ] **Step 3: Write the builder**

```csharp
// The Striders' walking houses: a prefab VARIANT of the hand-authored RigWalker, so the house on
// its deck and every leg stay the RigWalker's, with the player's helm taken out (a Strider house is
// nobody's to steer) and a crew added: six posts on the deck, a gangway at its feet, and a
// CrewShift that puts the crew ashore at every stop and calls them back.
//
// The RigWalker itself is untouched: players still pilot their own.
//
// Re-run from: Tools > SpaceGame > Vehicles > Build Strider Habitat Walker
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public static class StriderCityBuilder
    {
        private const string RigWalkerPath = "Assets/Game/Prefabs/Agents/Vehicles/Ground/RigWalker.prefab";
        public const string HabitatPath = "Assets/Game/Prefabs/Agents/Vehicles/Ground/StriderHabitatWalker.prefab";

        public const int CrewPosts = 6;

        /// <summary>The RigWalker's DesertCrawlerDriver.moveSpeed.</summary>
        public const float RigWalkerMoveSpeed = 6f;
        /// <summary>The leader walks at this fraction of its top speed so every follower -- the
        /// crawler at 3.2 m/s, the crab at 3.6 -- can keep up: LeggedDriver never exceeds 1x.</summary>
        public const float CityTravelMultiplier = 0.45f;
        public const float CityLeaderSpeed = RigWalkerMoveSpeed * CityTravelMultiplier;

        // Machine-sized formation tolerances (Spike findings).
        private const float RestRadius = 45f;
        private const float SlotTolerance = 8f;
        private const float RegroupDistance = 120f;
        private const float FormationNavSample = 20f;

        /// <summary>How far inside the deck's edge a post stands, clear of the 1 m rails.</summary>
        private const float PostInset = 1.5f;
        /// <summary>How far out from the hull's side the gangway is, clear of the legs' swing.</summary>
        private const float GangwayStandoff = 6f;
        /// <summary>Unseat reach from the gangway to NavMesh (VesselSeats default 6 is for a vessel's ramp).</summary>
        private const float GangwayNavMeshReach = 8f;
        private const string DeckColliderName = "COL_Deck";

        [MenuItem("Tools/SpaceGame/Vehicles/Build Strider Habitat Walker")]
        public static void BuildHabitat()
        {
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigWalkerPath);
            if (rig == null) { Debug.LogError($"[StriderCityBuilder] No RigWalker at {RigWalkerPath}."); return; }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(rig);
            instance.transform.position = Vector3.zero;
            try
            {
                if (!RemoveHelm(instance)) return;
                AddBrain(instance);
                AddCrew(instance);
                PrefabUtility.SaveAsPrefabAsset(instance, HabitatPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            Debug.Log(NetworkPrefabRegistrar.Sync(out _, out _));
            if (!SaveableWiring.TryWirePrefabs())
                Debug.LogError("[StriderCityBuilder] Save wiring failed; run Tools > Save System > Wire Saveable Prefabs.");
            Debug.Log($"[StriderCityBuilder] Built {HabitatPath}.");
        }

        /// Components that [RequireComponent(MountModule)] go first, or Unity refuses to remove it.
        private static bool RemoveHelm(GameObject root)
        {
            DestroyAll<SteerModule>(root);
            DestroyAll<MountSaveable>(root);
            DestroyAll<MountNetworkSync>(root);
            foreach (MountStation station in root.GetComponentsInChildren<MountStation>(true))
                Object.DestroyImmediate(station.gameObject);
            DestroyAll<MountModule>(root);
            DestroyAll<WanderSaveable>(root);   // requires WanderModule, so it goes first
            DestroyAll<WanderModule>(root);     // a parked house must stay on its rest point

            if (root.GetComponent<MountModule>() == null) return true;
            Debug.LogError("[StriderCityBuilder] Could not remove the RigWalker's MountModule; see Spike findings.");
            return false;
        }

        private static void AddBrain(GameObject root)
        {
            var faction = root.AddComponent<EntityFaction>();
            Set(faction, so =>
            {
                SerializedFields.Set(so, "faction", AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.StriderFactionPath));
                SerializedFields.Set(so, "relationshipTable", AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(RosterAuthoring.GlobalRelationshipsPath));
            });

            Set(root.AddComponent<NpcTaskModule>(), so => SerializedFields.SetInt(so, "priority", ModulePriority.Fallback));
            Set(root.AddComponent<GoalTravelModule>(), so => SerializedFields.SetInt(so, "priority", ModulePriority.Fallback + 1));

            Set(root.AddComponent<FormationModule>(), so =>
            {
                SerializedFields.SetInt(so, "priority", ModulePriority.Social);
                SerializedFields.SetString(so, "formationId", string.Empty);
                SerializedFields.SetFloat(so, "restRadius", RestRadius);
                SerializedFields.SetBool(so, "holdSlotAtRest", true);
                SerializedFields.SetFloat(so, "slotTolerance", SlotTolerance);
                SerializedFields.SetFloat(so, "regroupDistance", RegroupDistance);
                SerializedFields.SetFloat(so, "navSampleDistance", FormationNavSample);
            });
        }

        private static void AddCrew(GameObject root)
        {
            Transform deck = FindDeep(root.transform, DeckColliderName);
            if (deck == null || !deck.TryGetComponent(out Collider deckCollider))
            {
                Debug.LogError($"[StriderCityBuilder] No {DeckColliderName} collider on the RigWalker; crew posts cannot be placed.");
                return;
            }

            // Deck bounds in the root's own (scaled) space, so posts ride the deck wherever it is.
            Bounds world = deckCollider.bounds;
            Vector3 min = root.transform.InverseTransformPoint(world.min);
            Vector3 max = root.transform.InverseTransformPoint(world.max);
            Vector3 lo = Vector3.Min(min, max), hi = Vector3.Max(min, max);
            float inset = PostInset / root.transform.lossyScale.x;
            float top = hi.y;

            var seatsRoot = new GameObject("CrewPosts").transform;
            seatsRoot.SetParent(root.transform, false);
            Vector3[] corners =
            {
                new(lo.x + inset, top, lo.z + inset), new(hi.x - inset, top, lo.z + inset),
                new(lo.x + inset, top, hi.z - inset), new(hi.x - inset, top, hi.z - inset),
                new(lo.x + inset, top, (lo.z + hi.z) * 0.5f), new(hi.x - inset, top, (lo.z + hi.z) * 0.5f),
            };
            var posts = new Transform[CrewPosts];
            for (int i = 0; i < CrewPosts; i++)
            {
                var post = new GameObject($"Post_{i}").transform;
                post.SetParent(seatsRoot, false);
                post.localPosition = corners[i];
                Vector3 outward = corners[i] - new Vector3((lo.x + hi.x) * 0.5f, top, (lo.z + hi.z) * 0.5f);
                outward.y = 0f;
                post.localRotation = Quaternion.LookRotation(outward.normalized, Vector3.up);   // lookouts face out
                posts[i] = post;
            }

            var gangway = new GameObject("Gangway").transform;
            gangway.SetParent(root.transform, false);
            gangway.localPosition = new Vector3(hi.x + GangwayStandoff / root.transform.lossyScale.x, 0f, (lo.z + hi.z) * 0.5f);

            var chair = root.AddComponent<ChairPose>();
            var seats = root.AddComponent<VesselSeats>();
            Set(seats, so =>
            {
                SerializedProperty array = so.FindProperty("seats");
                array.arraySize = posts.Length;
                for (int i = 0; i < posts.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = posts[i];
                SerializedFields.SetFloat(so, "navMeshReach", GangwayNavMeshReach);
                SerializedFields.Set(so, "chairPose", chair);
            });

            var shift = root.AddComponent<CrewShift>();
            Set(shift, so => SerializedFields.Set(so, "gangway", gangway));
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static void DestroyAll<T>(GameObject root) where T : Component
        {
            foreach (T c in root.GetComponentsInChildren<T>(true)) Object.DestroyImmediate(c);
        }

        private static void Set(Object target, System.Action<SerializedObject> edit)
        {
            var so = new SerializedObject(target);
            edit(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
```

Notes for the implementer: `ChairPose` poses every NPC `CollectSeatedNpcs` finds; with the `MountModule` gone it no longer subscribes to mount events (research note). If the Spike found the posts' outward facing looks wrong, make the seated two face the house instead — record which in Gotchas. If `StriderCrabOutriderTests.KeepsUpWithTheCity` was written against a temporary constant in Task 9, delete that duplicate now so only this file defines `CityLeaderSpeed`.

- [ ] **Step 4: Run the builder**; read the variant back (Variant type, no helm, 6 posts, gangway at y 0).
- [ ] **Step 5: Run** `StriderHabitatWalkerTests` (4 pass), `StriderCrabOutriderTests` (3 pass now), `SpiderWalkerGroundingTests` (the RigWalker still grounds).
- [ ] **Step 6: Commit** (only if authorised).

---

### Task 13: The `strider-city` template

**Files:**
- Modify: `Assets/Game/Editor/Agents/RosterAuthoring.cs` (new `WireStriderCity`)
- Test: `Assets/Game/Editor/Tests/StriderCityTemplateTests.cs`

- [ ] **Step 1: Write the failing test** — reads the scene template back:

```csharp
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class StriderCityTemplateTests
    {
        private const string ScenePath = "Assets/Game/Scenes/world/persistentScene.unity";

        [Test]
        public void TheCity_IsFourHousesTwoWorkersTwoCrabsAndTwentyFourCrew()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                NpcWorldSim sim = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<NpcWorldSim>(true)).Single();
                var templates = (NpcGroupTemplate[])typeof(NpcWorldSim)
                    .GetField("templates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(sim);
                NpcGroupTemplate city = templates.Single(t => t.id == RosterAuthoring.StriderCityTemplateId);

                Assert.IsFalse(city.runtimeOnly, "the city is seeded at startup");
                Assert.AreEqual(RosterAuthoring.StriderFactionPath, AssetDatabase.GetAssetPath(city.tribe));

                int Count(System.Func<NpcGroupMemberSpec, bool> which) => city.members.Where(which).Sum(m => m.count);
                Assert.AreEqual(4, Count(m => AssetDatabase.GetAssetPath(m.prefab) == StriderCityBuilder.HabitatPath));
                Assert.AreEqual(1, city.members.Count(m => m.isLeader));
                Assert.AreEqual(StriderCityBuilder.HabitatPath, AssetDatabase.GetAssetPath(city.members[0].prefab), "carriers before crew");
                Assert.AreEqual(2, Count(m => AssetDatabase.GetAssetPath(m.prefab) == DesertCrawlerBuilder.PrefabPath));
                Assert.AreEqual(2, Count(m => m.prefab == null && m.role == RosterRole.Rider && !m.crew));
                Assert.AreEqual(4 * StriderCityBuilder.CrewPosts, Count(m => m.crew));

                Assert.IsTrue(city.tasks.All(t => t.targetSite == SiteKind.Ruin || t.targetSite == SiteKind.ScrapField));
                Assert.IsTrue(city.tasks.All(t => Mathf.Approximately(t.travelSpeedMultiplier, StriderCityBuilder.CityTravelMultiplier)));
                Assert.AreEqual(StriderCityBuilder.CityLeaderSpeed, city.travelSpeed, 0.01f, "folded and live speeds agree");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails.**
- [ ] **Step 3: Implement `WireStriderCity`** in `RosterAuthoring`:

```csharp
        // The city's stops: salvage, where the workers dig and the crew work the ground.
        private static readonly (string label, SiteKind site, string[] chatter)[] StriderCityStops =
        {
            ("picking over a ruin", SiteKind.Ruin, new[] { "Old metal. Good metal.", "Set the legs down, we're working." }),
            ("working a scrap field", SiteKind.ScrapField, new[] { "Crawlers out! Everything that isn't sand.", "Load the houses." }),
        };
        private const float StriderCitySearchRadius = 2500f;
        private static readonly Vector2 StriderCityDwell = new Vector2(90f, 180f);
        // A 21 m hull stops well short of a site marker; a person-sized arrive radius never arrives.
        private const float StriderCityArriveRadius = 25f;

        // Superseded 2026-09-24: 3 houses / 18 crew (user); the shipped code reads StriderCityHouses.
        /// The Striders' one walking city: four houses (the first leads), two worker crawlers, two
        /// crab outriders and twenty-four crew who ride the houses. Carriers are listed before the
        /// crew because NpcWorldSim seats each crew member on a carrier already spawned.
        [MenuItem("Tools/SpaceGame/Agents/Wire Strider City")]
        public static void WireStriderCity()
        {
            var striders = Load<FactionDefinition>(StriderFactionPath);
            var habitat = Load<GameObject>(StriderCityBuilder.HabitatPath);
            var crawler = Load<GameObject>(DesertCrawlerBuilder.PrefabPath);
            if (striders == null || habitat == null || crawler == null) return;

            WithWorldSim(sim =>
            {
                var so = new SerializedObject(sim);
                SerializedProperty templates = so.FindProperty("templates");

                int city = -1, source = -1;
                for (int i = 0; i < templates.arraySize; i++)
                {
                    string id = templates.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue;
                    if (id == StriderCityTemplateId) city = i;
                    if (id == "sand-nomads") source = i;
                }
                if (city < 0)
                {
                    if (source < 0) { Debug.LogError("[RosterAuthoring] No 'sand-nomads' template to copy."); return; }
                    templates.GetArrayElementAtIndex(source).DuplicateCommand();
                    city = source + 1;
                }

                SerializedProperty t = templates.GetArrayElementAtIndex(city);
                t.FindPropertyRelative("id").stringValue = StriderCityTemplateId;
                t.FindPropertyRelative("displayName").stringValue = "Strider City";
                t.FindPropertyRelative("tribe").objectReferenceValue = striders;
                t.FindPropertyRelative("runtimeOnly").boolValue = false;
                t.FindPropertyRelative("bountyHunters").boolValue = false;
                t.FindPropertyRelative("useStartPosition").boolValue = false;
                t.FindPropertyRelative("startNearSite").enumValueIndex = (int)SiteKind.Ruin;
                t.FindPropertyRelative("travelSpeed").floatValue = StriderCityBuilder.CityLeaderSpeed;

                SerializedProperty members = t.FindPropertyRelative("members");
                members.arraySize = 0;
                AddMember(members, habitat, RosterRole.Scout, 1, leader: true, crew: false);
                AddMember(members, habitat, RosterRole.Scout, 3, leader: false, crew: false);
                AddMember(members, crawler, RosterRole.Scout, 2, leader: false, crew: false);
                AddMember(members, null, RosterRole.Rider, 2, leader: false, crew: false);
                int crewPerRole = 4 * StriderCityBuilder.CrewPosts / 2;
                AddMember(members, null, RosterRole.Warrior, crewPerRole, leader: false, crew: true);
                AddMember(members, null, RosterRole.Scout, crewPerRole, leader: false, crew: true);

                SerializedProperty tasks = t.FindPropertyRelative("tasks");
                tasks.arraySize = StriderCityStops.Length;
                for (int i = 0; i < StriderCityStops.Length; i++)
                {
                    SerializedProperty task = tasks.GetArrayElementAtIndex(i);
                    task.FindPropertyRelative("label").stringValue = StriderCityStops[i].label;
                    task.FindPropertyRelative("targetSite").enumValueIndex = (int)StriderCityStops[i].site;
                    task.FindPropertyRelative("searchRadius").floatValue = StriderCitySearchRadius;
                    task.FindPropertyRelative("searchFromHome").boolValue = false;
                    task.FindPropertyRelative("dwellSeconds").vector2Value = StriderCityDwell;
                    task.FindPropertyRelative("weight").floatValue = 1f;
                    task.FindPropertyRelative("arriveRadius").floatValue = StriderCityArriveRadius;
                    task.FindPropertyRelative("travelSpeedMultiplier").floatValue = StriderCityBuilder.CityTravelMultiplier;
                    SerializedProperty chatter = task.FindPropertyRelative("chatter");
                    chatter.arraySize = StriderCityStops[i].chatter.Length;
                    for (int c = 0; c < chatter.arraySize; c++) chatter.GetArrayElementAtIndex(c).stringValue = StriderCityStops[i].chatter[c];
                }

                SerializedProperty shape = t.FindPropertyRelative("formation");
                shape.FindPropertyRelative("Lanes").intValue = 2;
                shape.FindPropertyRelative("RowSpacing").floatValue = 30f;
                shape.FindPropertyRelative("LaneSpacing").floatValue = 30f;
                shape.FindPropertyRelative("LateralJitter").floatValue = 2f;
                shape.FindPropertyRelative("LongitudinalJitter").floatValue = 3f;
                shape.FindPropertyRelative("DriftAmplitude").floatValue = 1f;
                shape.FindPropertyRelative("DriftRate").floatValue = 0.05f;

                so.ApplyModifiedPropertiesWithoutUndo();
            });
        }

        private static void AddMember(SerializedProperty members, GameObject prefab, RosterRole role, int count, bool leader, bool crew)
        {
            int i = members.arraySize;
            members.InsertArrayElementAtIndex(i);
            SerializedProperty m = members.GetArrayElementAtIndex(i);
            m.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            m.FindPropertyRelative("role").enumValueIndex = (int)role;
            m.FindPropertyRelative("count").intValue = count;
            m.FindPropertyRelative("isLeader").boolValue = leader;
            m.FindPropertyRelative("crew").boolValue = crew;
        }
```

Move the formation numbers into named `const`s beside `StriderCityStops` (e.g. `CityLanes`, `CityRowSpacing`, …) with a one-line why each — the spike's measured values. Add `using SpaceGame.World;` if `SiteKind` needs it.

- [ ] **Step 4: Run** `WireStriderCity` in the editor; read the template back.
- [ ] **Step 5: Run** `StriderCityTemplateTests` (1 pass), `RuntimeGroupTests`, `NpcWorldSimTests`.
- [ ] **Step 6: Commit** (only if authorised).

---

### Task 14: Documentation

**Files:**
- Create: `docs/AI/systems/Striders.md`
- Modify: `docs/Human/the-systems.md`, `docs/AI/systems/AgentSystem.md`, `docs/AI/systems/Vehicles.md`, `.claude/skills/spacegame-tribe/SKILL.md`, `docs/superpowers/plans/2026-09-07-faction-system.md` (status table, Phase 4 row), `docs/superpowers/specs/2026-09-23-striders-walking-city-design.md` (§4.2: no seating helper was extracted — both paths call `VesselSeats.Seat`; the shared piece was already `NpcSpawn.Create(seated: true)`. §4.3: the leader's gate waits for the houses' crew only; the worker crawlers need no gate because the leader's 2.7 m/s is under their 3.2 and formation's regroup brings them back), `docs/AI/GLOSSARY.md` (Strider, walking city, crew post, gangway)

- [ ] **Step 1: Read the governing docs' shape** — `SkyTribe.md` (model for the new doc), `docs/AI/CONTRIBUTING.md`.
- [ ] **Step 2: Write `Striders.md`.** Frontmatter: `system: Striders`, `layer: characters`, `summary:` ≤110 chars ("The Striders end to end — faction, roster, and the walking city of crewed RigWalker houses"), `paths:` (every file created in Tasks 2–13 that exists, plus `Assets/Game/Prefabs/Agents/Characters/Striders/`, `Rosters/Striders.asset`, `Core/StriderFaction.asset`), `symptoms:` (at least: "the walking city leaves while its crew are still ashore", "crew stand frozen at the scene root after the city folds", "a Strider house can be piloted by a player", "the crawlers fall behind the column and never catch up", "the crab outriders fall behind the city", "after loading mid-stop the crew are seated and the city walks off at once", plus anything real from the Spike and play checks), `reads_with: [AgentSystem, Vehicles, SkyTribe, Persistence, Multiplayer]`, `updated:` today. Body ≤150 lines: Model → Key types → Flows (author/build order: Author Strider Faction → Build Strider Nomad NPCs → Build Strider Crab Outrider → Author Strider Roster → Build Strider Nomad NPCs → Build Strider Crab Outrider → Build Desert Crawler Prefab → Build Strider Habitat Walker → Wire War Party Templates → Wire Strider City; the crew cycle) → Multiplayer → Persistence → Gotchas (the Global Constraints' speed clamp, `SetFormation("")`, reverse despawn order, `ReadBackCrew` before fold/save, departure gate in Choosing, crew listed after carriers) → Extending.
- [ ] **Step 3: `the-systems.md` entry** `### The tribe that lives on walking machines *(Striders)*` — 2–4 sentences + one **Worth knowing** line.
- [ ] **Step 4: Update** `AgentSystem.md` (departure gate on `NpcTaskModule`; `GroupMembership` no-health rule; `crew` member specs + `CrewAshore`), `Vehicles.md` (habitat variant vs the player RigWalker; crew posts, gangway, `CrewShift`; DesertCrawler now a Strider worker with `FormationModule`), the tribe skill (§5/§9: a tribe whose home is a moving carrier — point at `Striders.md`), the faction plan's Phase 4 row (4.3 Mechanics half → Striders, done with date and test fixture names, carrying the client/save verification until Task 15 runs). Delete anything these changes made untrue (e.g. EntityFactionWiring comment about the crawler waiting on Sand).
- [ ] **Step 5: Run** `python3 tools/docs_check.py --index` → `0 errors`.
- [ ] **Step 6: Commit** (only if authorised).

---

### Task 15: Verification — full suite, host **and** client, save/reload, profile

- [ ] **Step 1: Type-check** → `No errors.` for both assemblies.
- [ ] **Step 2: Full EditMode suite** → `FAILED=0`, or the only failures are ones proven pre-existing (`git stash`-free check: run the failing fixture on `main`'s version of the file via `git show main:<path>` diff reasoning, and name them in the report). Known pre-existing at plan time: `NetGunTests`, `PlayerShipTests` — re-confirm.
- [ ] **Step 3: Find the city in play (host).** Log `NpcWorldSim.FindGroup("strider-city").Position` via `execute_code` in play mode; teleport near it. Check: 4 houses in formation with crew on the decks, 2 crawlers following, 2 crabs with riders on the flanks; the column walks to a Ruin/ScrapField; at the stop the crew step off one by one at the gangways and wander; the crawlers spread out and dig; when the stay ends the crew walk back and are seated; the column leaves only when all are aboard. *(Superseded 2026-09-24: 3 houses / 18 crew, user.)*
- [ ] **Step 4: Edge cases in play.** Shoot one crew member ashore → the others turn hostile per the aggression rules and the recall waits for the fight to end; kill one → the column still leaves with an empty post. Stand on a house deck while it walks → you ride along; try to interact with the helm → nothing.
- [ ] **Step 5: Client (MPPM clone or the batch autotest).** Repeat Step 3's checks from the client: seated crew posed (not T-posed or standing on air), step-off and re-board seen, crab riders seated. Join late mid-stop → crew ashore. A client player can ride a deck.
- [ ] **Step 6: Save/reload.** Save mid-march, reload → crew seated. Save mid-stop, reload → crew on foot by the gangways; the leader does not leave until they re-board. Grep the save JSON: `"npcworld"` has the `strider-city` record with `"crewAshore": true` for the mid-stop save; after hurting a Strider, `"factionGoodwill"` has the Striders' `factionId`.
- [ ] **Step 7: Profile.** Unity Profiler, 60 s beside the stopped city and 60 s beside the marching city: record CPU ms for `AgentController`/`NpcWorldSim`/`CrewShift` and total frame time in `Striders.md` Gotchas. If the frame is over budget, lower the crew count (Task 13 `crewPerRole`) rather than tune code blind (GDC-L1-PERF-0001).
- [ ] **Step 8: Update the faction plan status row** with what was actually verified; report to the user with results, including anything not verified.
- [ ] **Step 9: Commit** (only if authorised).

---

## Spike findings

Run 2026-09-24 (full report: `.superpowers/sdd/2026-09-24-striders-walking-city/task-1-report.md`).

- **Variant:** removal order SteerModule → MountSaveable → MountNetworkSync → DOOR_MountStation → MountModule works; `PrefabAssetType.Variant`; keeps NetworkObject / WalkerPlatformCarrier / DesertCrawlerDriver; own GlobalObjectIdHash and prefabId; no errors. Saving it auto-adds it to `Assets/DefaultNetworkPrefabs.asset` (expected diff).
- **Lead/follow (marching):** leader 2.65–2.95 m/s at multiplier 0.45; followers held slots (~(−17,−29), (+14,−31)) at 2.7–3.0 m/s; min separation 31 m; lanes 2 / 30 / 30, restRadius 45, slotTolerance 8, regroupDistance 120. Crawler digs when stopped.
- **Parked:** failed as planned — followers stop anywhere within restRadius of their rest point; two houses parked 17–19 m apart with decks clipping. Fixed by Task 12 Step 0 (`holdSlotAtRest`: houses keep their marching slots, ≥31 m apart, at stops — the user does not want anything to park) and by removing WanderModule from the house variant.
- **Crab:** the stock gait caps it at 1.76 m/s whatever moveSpeed says; `stepDuration` 0.22 → 3.2 m/s clean (slip 0.016 m/m, tilt 0°); 0.20 → 3.5; `swingLegs` 3 hops. Cadence is 1.8× authored — eyeball in Task 15.
- **Deck seating:** seated nomads held (0,0,0) relative to their markers over ~70 m of walking; after `Unseat` both `isOnNavMesh`. Unnetworked only — client check in Task 15.
