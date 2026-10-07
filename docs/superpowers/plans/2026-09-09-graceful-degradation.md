# Graceful Degradation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When one feature throws at runtime, that feature stops and the rest of the session keeps playing — instead of the whole game breaking for everyone.

**Architecture:** Four layers, built in order. (0) A dependency-free `SpaceGame.Diagnostics` assembly holding one fault primitive, a per-site fault budget, and a ring-buffer ledger. (1) Exception barriers at the existing fan-out seams where one code path invokes N plug-ins. (2) A guarded-coroutine wrapper, because a throwing coroutine dies permanently mid-way and that is what leaves a player frozen. (3) Outcome guards that measure a broken *state* and recover it — the doctrine already used by `UnderTerrainGuard` and `SpawnSyncGuard`: measure the outcome, never enumerate the causes. (4) Observability so these faults become reportable, since they are network races nobody can reproduce on demand.

**Tech Stack:** Unity 6000.3.11f1, C#, Unity Netcode for GameObjects, NUnit EditMode tests, `tools/typecheck.py`.

---

## Background reading (do this once, before Task 1)

Read these in full before writing any code:

- [docs/AI/INVARIANTS.md](../../AI/INVARIANTS.md) — especially "Statics outlive the world, the session and play mode", "Every handler fires more than once, and one of those times is a load", and "Bind in `OnEnable`".
- [docs/AI/systems/Testing.md](../../AI/systems/Testing.md) — how tests are run here and why `AddComponent` in a test raises no `Awake`.
- [Assets/Game/Scripts/World/Safety/Rules/UnderTerrainRule.cs](../../../Assets/Game/Scripts/World/Safety/Rules/UnderTerrainRule.cs) — the model every guard in Phase 3 copies: a pure `*Rule` that decides, and a component that only reads the world and applies the verdict.
- [Assets/Game/Scripts/Core/Multiplayer/Messaging/NetChannel.cs](../../../Assets/Game/Scripts/Core/Multiplayer/Messaging/NetChannel.cs) lines 128–165 — the barrier pattern already in the codebase.

### Facts about this repo you must not rediscover the hard way

- **An asmdef cannot reference `Assembly-CSharp`.** Most of `Assets/Game/Scripts` is `Assembly-CSharp`, but 19 folders carry their own `SpaceGame.*` asmdef (Jetpack, Wingsuit, Locomotion, Ornithopter, Crawler, DuneFoil, Persistence, …). A helper placed in `Assembly-CSharp` is unreachable from those. That is the whole reason Phase 0 creates its own assembly with an empty `references` list.
- **`SpaceGame.Diagnostics` therefore cannot call `ChatLog`, `GameplayMenuScope`, `PlayerController` or anything else in `Assembly-CSharp`.** It raises C# events; `Assembly-CSharp` subscribes. Task 5 builds that bridge.
- **In TDD here, "the type does not exist" is a compile error, not a red test, and a compile error makes the *whole* EditMode suite refuse to run.** That compile error naming your new type *is* the failing state. Do not expect a red assertion first.
- **Types inside a `SpaceGame.*` asmdef are tested from `Assets/Game/Tests/EditMode/`**, and that assembly must list the asmdef in its `references`. Types in `Assembly-CSharp` are tested from `Assets/Game/Editor/Tests/`. Putting a test in the wrong one produces `CS0246`.
- **Statics survive play-mode exit here** (enter-play-mode options are on). Every static in this plan gets a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` reset.
- After creating a new `.asmdef`, Unity must import it before anything compiles. Focus the Editor, or run `AssetDatabase.Refresh()`. `tools/typecheck.py` does **not** compile module assemblies, so it will not prove the new assembly is healthy — the Editor console is the authority for that one.

### How to run tests

```bash
python3 tools/typecheck.py --editor
```
Expected: `Assembly-CSharp: no errors.` then `Assembly-CSharp-Editor: no errors.`, exit 0.

Full suite — in the Unity Editor, menu `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then:
```bash
cat Temp/headless_tests.txt
```
Expected: a line `PASSED=… FAILED=0 SKIPPED=… INCONCLUSIVE=…`, then `DONE`. Absence of the file means the run has not finished — poll, never assume.

One fixture only (driven from the Editor or over MCP):
```
SpaceGame.EditorTools.HeadlessTestRunner.RunEditModeDeferred("FaultBudgetTests")
```

---

## File structure

**Phase 0 — new assembly** `Assets/Game/Scripts/Core/Diagnostics/` (assembly `SpaceGame.Diagnostics`, zero references, auto-referenced so every other assembly can see it):

| File | Responsibility |
| --- | --- |
| `SpaceGame.Diagnostics.asmdef` | The assembly boundary. Empty `references`. |
| `FaultRecord.cs` | One immutable description of one fault. |
| `FaultLedger.cs` | Ring buffer of the last N `FaultRecord`s on this machine. |
| `FaultBudget.cs` | Pure: how many faults per site per window before quarantine. No Unity types. |
| `IQuarantinable.cs` | Opt-in hook so a system can shed only its broken part. |
| `Fault.cs` | The API: `Run`, `Coroutine`, `IsQuarantined`, the two events. |

**Phase 0 bridge, in `Assembly-CSharp`:**

| File | Responsibility |
| --- | --- |
| `Assets/Game/Scripts/Core/Diagnostics/Bridge/FaultChatBridge.cs` | Subscribes to `Fault.Quarantined`, writes one line to `ChatLog`, registers `/faults`. |

> **Note:** `Bridge/` sits *inside* the `Diagnostics` folder but the asmdef only covers files under its own folder and below — so `Bridge/` would be swallowed by it. It must therefore live **outside**: `Assets/Game/Scripts/Core/DiagnosticsBridge/FaultChatBridge.cs`. Use that path.

**Phase 1–2 — modified only:**

| File | Change |
| --- | --- |
| `Assets/Game/Scripts/Agents/Controller/AgentController.cs` | Barriers on the four module loops. |
| `Assets/Game/Scripts/Items/Inventory/Core/UseChannel.cs` | Barriers around `TryUse` and `PlayUse`. |
| `Assets/Game/Scripts/Gameplay/Interaction/Core/Interactor.cs` | Barriers around `Interact` / `SecondaryInteract`. |
| `Assets/Game/Scripts/Items/Backpack/Focus/PackFocusSession.cs` | Guarded coroutine. |
| `Assets/Game/Scripts/Presentation/Cameras/FocusCamera.cs` | Guarded coroutines. |
| `Assets/Game/Scripts/Presentation/Cutscenes/UI/LetterboxOverlay.cs` | Guarded coroutines. |
| `Assets/Game/Scripts/Gameplay/Interaction/Core/NetLatch.cs` | Guarded coroutine. |
| `Assets/Game/Scripts/Presentation/UI/Widgets/GameplayMenuScope.cs` | Expose `Owners`. |
| `Assets/Game/Scripts/Agents/Modules/Riding/MountModule.Mounting.cs` | Expose `LocalRiderMount`. |

**Phase 3 — new, in `Assembly-CSharp`** `Assets/Game/Scripts/Core/Safety/`:

| File | Responsibility |
| --- | --- |
| `ISessionGuard.cs` | `string Name { get; }`, `void Check(float interval)`. |
| `SessionGuardRunner.cs` | Bootstraps itself, ticks every guard at a fixed interval inside `Fault.Run`. |
| `Rules/StuckScopeRule.cs` | Pure: is this scope owner abandoned? |
| `Rules/InputRestoreRule.cs` | Pure: should input be handed back? |
| `Rules/MountRecoveryRule.cs` | Pure: should the local rider be forced off? |
| `Rules/ViewRecoveryRule.cs` | Pure: has this machine gone blind? |
| `Guards/StuckScopeGuard.cs` | Reads `GameplayMenuScope.Owners`, applies `StuckScopeRule`. |
| `Guards/InputRestoreGuard.cs` | Reads the local player, applies `InputRestoreRule`. |
| `Guards/MountGuard.cs` | Reads `MountModule.LocalRiderMount`, applies `MountRecoveryRule`. |
| `Guards/ViewGuard.cs` | Reads `Camera.allCamerasCount`, applies `ViewRecoveryRule`. |

**Phase 4 — new:**

| File | Responsibility |
| --- | --- |
| `Assets/Game/Scripts/Core/DiagnosticsBridge/FaultLogSink.cs` | Hooks `Application.logMessageReceived`, feeds unhandled errors into the ledger with session context. |
| `Assets/Game/Scripts/Core/DiagnosticsBridge/FaultReport.cs` | Writes the ledger to a text file next to the save. |
| `Assets/Game/Scripts/Core/Multiplayer/Autotest/AutotestRunner.Faults.cs` | Fault-injection probe for the two-process run. |
| `docs/AI/systems/Diagnostics.md` | The system doc. |

**Tests:**

| File | Covers |
| --- | --- |
| `Assets/Game/Tests/EditMode/FaultBudgetTests.cs` | Quarantine arithmetic. |
| `Assets/Game/Tests/EditMode/FaultLedgerTests.cs` | Ring buffer. |
| `Assets/Game/Tests/EditMode/FaultCoroutineTests.cs` | Guarded coroutine. |
| `Assets/Game/Editor/Tests/FaultBarrierTests.cs` | `Fault.Run` against real MonoBehaviours. |
| `Assets/Game/Editor/Tests/AgentModuleBarrierTests.cs` | A throwing module does not stop the agent. |
| `Assets/Game/Editor/Tests/SessionGuardRuleTests.cs` | All four pure rules. |
| `Assets/Game/Editor/Tests/StuckScopeGuardTests.cs` | The scope guard against real components. |

---

## Phase 0 — the fault primitive

### Task 1: The `SpaceGame.Diagnostics` assembly and `FaultRecord`

**Files:**
- Create: `Assets/Game/Scripts/Core/Diagnostics/SpaceGame.Diagnostics.asmdef`
- Create: `Assets/Game/Scripts/Core/Diagnostics/FaultRecord.cs`
- Modify: `Assets/Game/Tests/EditMode/SpaceGame.Tests.EditMode.asmdef`

- [ ] **Step 1: Create the assembly definition**

Write `Assets/Game/Scripts/Core/Diagnostics/SpaceGame.Diagnostics.asmdef`:

```json
{
    "name": "SpaceGame.Diagnostics",
    "rootNamespace": "SpaceGame.Diagnostics",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`references` is empty and stays empty. `autoReferenced: true` is what lets `Assembly-CSharp` and every `SpaceGame.*` module see it without editing 19 asmdefs.

- [ ] **Step 2: Add the assembly to the EditMode test assembly's references**

In `Assets/Game/Tests/EditMode/SpaceGame.Tests.EditMode.asmdef`, add `"SpaceGame.Diagnostics"` as the first entry of `references`:

```json
    "references": [
        "SpaceGame.Diagnostics",
        "SpaceGame.Persistence",
        "SpaceGame.Minigame.Core",
```

Leave every other line of that file untouched.

- [ ] **Step 3: Write `FaultRecord`**

Write `Assets/Game/Scripts/Core/Diagnostics/FaultRecord.cs`:

```csharp
// One fault, as it happened: what threw, where, and whether it has been switched off.
//
// A struct with no Unity object references on purpose. Records outlive the objects they describe —
// the ledger is read minutes later, from a chat command, after the GameObject is gone — so holding
// a Component here would either resurrect a destroyed reference or print "null" where the useful
// information used to be. The names are copied at the moment of the fault instead.
namespace SpaceGame.Diagnostics
{
    public readonly struct FaultRecord
    {
        /// <summary>Where in the code, e.g. "AgentController.Movement". Stable, never interpolated with an id.</summary>
        public readonly string Site;

        /// <summary>The GameObject or component name at the moment of the fault.</summary>
        public readonly string Owner;

        /// <summary>The exception, already formatted. Never null; "unknown" when nothing was supplied.</summary>
        public readonly string Detail;

        /// <summary>How many times this (owner, site) pair has thrown inside the current window.</summary>
        public readonly int Count;

        /// <summary>True on the record that tripped quarantine — the one that switched the thing off.</summary>
        public readonly bool Quarantined;

        /// <summary>Seconds since startup, as supplied by the caller. Tests pass their own clock.</summary>
        public readonly float Time;

        public FaultRecord(string site, string owner, string detail, int count, bool quarantined, float time)
        {
            Site = string.IsNullOrEmpty(site) ? "unknown" : site;
            Owner = string.IsNullOrEmpty(owner) ? "unknown" : owner;
            Detail = string.IsNullOrEmpty(detail) ? "unknown" : detail;
            Count = count;
            Quarantined = quarantined;
            Time = time;
        }

        public override string ToString() =>
            $"[{Time:F1}s] {Owner} · {Site} · x{Count}{(Quarantined ? " · QUARANTINED" : string.Empty)} · {Detail}";
    }
}
```

- [ ] **Step 4: Let Unity import the new assembly**

In the Unity Editor, focus the window (or run `AssetDatabase.Refresh()`), then confirm the console shows no compile errors and that `Library/ScriptAssemblies/SpaceGame.Diagnostics.dll` exists:

```bash
ls -la Library/ScriptAssemblies/SpaceGame.Diagnostics.dll
```
Expected: the file exists. If it does not, the asmdef has not been imported yet — do not continue, nothing below will compile.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Core/Diagnostics Assets/Game/Tests/EditMode/SpaceGame.Tests.EditMode.asmdef
git commit -m "feat: add SpaceGame.Diagnostics assembly and FaultRecord"
```

---

### Task 2: `FaultBudget` — the quarantine decision

Pure arithmetic, no Unity types, so it is testable directly. This is the same split as `UnderTerrainRule` versus `UnderTerrainGuard`.

**Files:**
- Create: `Assets/Game/Scripts/Core/Diagnostics/FaultBudget.cs`
- Test: `Assets/Game/Tests/EditMode/FaultBudgetTests.cs`

- [ ] **Step 1: Write the failing test**

Write `Assets/Game/Tests/EditMode/FaultBudgetTests.cs`:

```csharp
using NUnit.Framework;
using SpaceGame.Diagnostics;

namespace SpaceGame.Diagnostics.Tests
{
    /// <summary>
    /// The budget decides when a repeatedly-throwing thing gets switched off. Its whole contract is
    /// "loud but bounded": every fault is counted, but only one of them ever trips quarantine, and a
    /// thing that throws once an hour is never quarantined at all.
    /// </summary>
    public class FaultBudgetTests
    {
        [Test]
        public void FirstFaultDoesNotQuarantine()
        {
            var budget = new FaultBudget(maxFaults: 3, windowSeconds: 10f);

            Assert.IsFalse(budget.Record("a", 0f), "one fault is not a pattern");
            Assert.IsFalse(budget.IsQuarantined("a"));
        }

        [Test]
        public void QuarantinesOnTheNthFaultInsideTheWindow()
        {
            var budget = new FaultBudget(maxFaults: 3, windowSeconds: 10f);

            Assert.IsFalse(budget.Record("a", 0f));
            Assert.IsFalse(budget.Record("a", 1f));
            Assert.IsTrue(budget.Record("a", 2f), "the third fault inside the window trips it");
            Assert.IsTrue(budget.IsQuarantined("a"));
        }

        [Test]
        public void QuarantineTripsExactlyOnce()
        {
            var budget = new FaultBudget(maxFaults: 2, windowSeconds: 10f);

            budget.Record("a", 0f);
            Assert.IsTrue(budget.Record("a", 1f), "trips here");
            Assert.IsFalse(budget.Record("a", 2f), "and never again, or every frame logs a new quarantine");
            Assert.IsFalse(budget.Record("a", 3f));
        }

        [Test]
        public void AnOldFaultDoesNotCountTowardsANewWindow()
        {
            var budget = new FaultBudget(maxFaults: 2, windowSeconds: 10f);

            budget.Record("a", 0f);

            // 30 s later. A thing that throws once every half minute is annoying, not broken, and
            // switching it off would be a worse outcome than the fault itself.
            Assert.IsFalse(budget.Record("a", 30f));
            Assert.IsFalse(budget.IsQuarantined("a"));
        }

        [Test]
        public void SitesAreCountedSeparately()
        {
            var budget = new FaultBudget(maxFaults: 2, windowSeconds: 10f);

            budget.Record("a", 0f);
            Assert.IsFalse(budget.Record("b", 1f), "b has thrown once, not twice");
            Assert.IsFalse(budget.IsQuarantined("b"));
        }

        [Test]
        public void CountIsReportedForTheCurrentWindow()
        {
            var budget = new FaultBudget(maxFaults: 5, windowSeconds: 10f);

            budget.Record("a", 0f);
            budget.Record("a", 1f);

            Assert.AreEqual(2, budget.CountFor("a"));

            budget.Record("a", 100f);
            Assert.AreEqual(1, budget.CountFor("a"), "a new window starts a new count");
        }

        [Test]
        public void ClearForgetsEverything()
        {
            var budget = new FaultBudget(maxFaults: 2, windowSeconds: 10f);

            budget.Record("a", 0f);
            budget.Record("a", 1f);
            Assert.IsTrue(budget.IsQuarantined("a"));

            budget.Clear();

            Assert.IsFalse(budget.IsQuarantined("a"));
            Assert.AreEqual(0, budget.CountFor("a"));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

In the Unity Editor: `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then:
```bash
cat Temp/headless_tests.txt
```
Expected: the run does **not** report a failing assertion — it reports nothing at all, because `FaultBudget` does not exist and the compile fails. The Editor console shows `error CS0246: The type or namespace name 'FaultBudget' could not be found`. That is the failing state for this repo.

- [ ] **Step 3: Write the implementation**

Write `Assets/Game/Scripts/Core/Diagnostics/FaultBudget.cs`:

```csharp
// How many times a thing may throw before it is switched off, and over what span.
//
// Pure, and deliberately ignorant of Unity: the caller passes the clock in. That is what makes the
// decision testable without a frame loop, and it is the same split UnderTerrainRule makes against
// UnderTerrainGuard — the part that decides has no components in it.
//
// The window matters more than the count. A module that throws five times in one frame is broken and
// must stop; a module that throws once every thirty seconds is a bug worth fixing but not worth
// removing a creature's ability to walk over. Without the window the second case eventually reaches
// any threshold and gets quarantined for being slow, which is the guard causing the outage.
using System.Collections.Generic;

namespace SpaceGame.Diagnostics
{
    public sealed class FaultBudget
    {
        private struct Entry
        {
            public int Count;
            public float WindowStart;
            public bool Quarantined;
        }

        private readonly Dictionary<string, Entry> entries = new();

        public FaultBudget(int maxFaults, float windowSeconds)
        {
            // Clamped rather than trusted: a max of 0 would quarantine everything on its first fault
            // and a negative window would restart the window on every call, so both turn the guard
            // into the outage it exists to prevent.
            MaxFaults = maxFaults > 0 ? maxFaults : 1;
            WindowSeconds = windowSeconds > 0f ? windowSeconds : 0f;
        }

        public int MaxFaults { get; }

        public float WindowSeconds { get; }

        /// <summary>
        /// Counts one fault against <paramref name="key"/>.
        /// Returns true only on the call that trips quarantine — never again for that key, so a
        /// thing throwing every frame produces one quarantine line and not sixty a second.
        /// </summary>
        public bool Record(string key, float now)
        {
            if (string.IsNullOrEmpty(key)) return false;

            entries.TryGetValue(key, out Entry entry);

            if (entry.Count == 0 || now - entry.WindowStart > WindowSeconds)
            {
                entry.Count = 1;
                entry.WindowStart = now;
            }
            else
            {
                entry.Count++;
            }

            bool trips = !entry.Quarantined && entry.Count >= MaxFaults;
            if (trips) entry.Quarantined = true;

            entries[key] = entry;
            return trips;
        }

        public bool IsQuarantined(string key) =>
            !string.IsNullOrEmpty(key) && entries.TryGetValue(key, out Entry e) && e.Quarantined;

        /// <summary>Faults counted against <paramref name="key"/> in its current window.</summary>
        public int CountFor(string key) =>
            !string.IsNullOrEmpty(key) && entries.TryGetValue(key, out Entry e) ? e.Count : 0;

        public void Clear() => entries.Clear();
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

`Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then:
```bash
cat Temp/headless_tests.txt
```
Expected: `FAILED=0`, then `DONE`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Core/Diagnostics/FaultBudget.cs Assets/Game/Tests/EditMode/FaultBudgetTests.cs
git commit -m "feat: add FaultBudget quarantine arithmetic"
```

---

### Task 3: `FaultLedger` — the ring buffer

**Files:**
- Create: `Assets/Game/Scripts/Core/Diagnostics/FaultLedger.cs`
- Test: `Assets/Game/Tests/EditMode/FaultLedgerTests.cs`

- [ ] **Step 1: Write the failing test**

Write `Assets/Game/Tests/EditMode/FaultLedgerTests.cs`:

```csharp
using NUnit.Framework;
using SpaceGame.Diagnostics;

namespace SpaceGame.Diagnostics.Tests
{
    /// <summary>
    /// The ledger is what a player's bug report is made of. It has to be bounded — a fault every
    /// frame for an hour must not grow without limit — and it has to keep the newest, because the
    /// thing that just broke is the thing being reported.
    /// </summary>
    public class FaultLedgerTests
    {
        [SetUp]
        public void Reset() => FaultLedger.Clear();

        [TearDown]
        public void Cleanup() => FaultLedger.Clear();

        [Test]
        public void RecordsArriveOldestFirst()
        {
            FaultLedger.Add(new FaultRecord("s", "a", "d", 1, false, 0f));
            FaultLedger.Add(new FaultRecord("s", "b", "d", 1, false, 1f));

            Assert.AreEqual(2, FaultLedger.Recent.Count);
            Assert.AreEqual("a", FaultLedger.Recent[0].Owner);
            Assert.AreEqual("b", FaultLedger.Recent[1].Owner);
        }

        [Test]
        public void DropsTheOldestPastCapacity()
        {
            for (int i = 0; i < FaultLedger.Capacity + 5; i++)
                FaultLedger.Add(new FaultRecord("s", $"owner{i}", "d", 1, false, i));

            Assert.AreEqual(FaultLedger.Capacity, FaultLedger.Recent.Count);
            Assert.AreEqual("owner5", FaultLedger.Recent[0].Owner, "the first five should have been dropped");
        }

        [Test]
        public void CountsTotalFaultsBeyondWhatItKeeps()
        {
            for (int i = 0; i < FaultLedger.Capacity + 5; i++)
                FaultLedger.Add(new FaultRecord("s", "o", "d", 1, false, i));

            Assert.AreEqual(FaultLedger.Capacity + 5, FaultLedger.TotalFaults,
                            "the count must not be the buffer length — that would hide how bad a session got");
        }

        [Test]
        public void ClearEmptiesBothTheBufferAndTheCount()
        {
            FaultLedger.Add(new FaultRecord("s", "o", "d", 1, false, 0f));
            FaultLedger.Clear();

            Assert.AreEqual(0, FaultLedger.Recent.Count);
            Assert.AreEqual(0, FaultLedger.TotalFaults);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

`Tools ▸ Tests ▸ Run EditMode Tests (headless)`. Expected: compile error `CS0246: 'FaultLedger' could not be found`.

- [ ] **Step 3: Write the implementation**

Write `Assets/Game/Scripts/Core/Diagnostics/FaultLedger.cs`:

```csharp
// Every fault this machine has seen this session, newest last, bounded.
//
// Static and not a component, for the same reason ChatLog is: the ledger has to outlive scene loads,
// and the world streams chunk scenes in and out constantly. A buffer living on a scene object would
// be emptied by events the player did not cause, which is exactly when they most want to report one.
//
// A List used as a queue rather than a real ring buffer, matching ChatLog: at this size the shuffle
// is a few dozen pointer copies per fault, and in exchange a reader can index it in arrival order.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Diagnostics
{
    public static class FaultLedger
    {
        /// <summary>Records kept. Beyond this the oldest is dropped; <see cref="TotalFaults"/> still counts it.</summary>
        public const int Capacity = 64;

        private static readonly List<FaultRecord> records = new(Capacity);

        /// <summary>Oldest first. Never null.</summary>
        public static IReadOnlyList<FaultRecord> Recent => records;

        /// <summary>
        /// Every fault since the session began, including the ones the buffer has dropped.
        /// Reported separately because "64 faults" and "9000 faults" are very different sessions and
        /// the buffer length cannot tell them apart.
        /// </summary>
        public static int TotalFaults { get; private set; }

        /// <summary>
        /// Statics survive play-mode exit here — enter-play-mode options are on — so without this the
        /// second play session in an Editor starts holding the first one's faults.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            records.Clear();
            TotalFaults = 0;
        }

        public static void Add(in FaultRecord record)
        {
            records.Add(record);
            if (records.Count > Capacity) records.RemoveAt(0);
            TotalFaults++;
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

`Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then `cat Temp/headless_tests.txt`. Expected: `FAILED=0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Core/Diagnostics/FaultLedger.cs Assets/Game/Tests/EditMode/FaultLedgerTests.cs
git commit -m "feat: add FaultLedger session fault ring buffer"
```

---

### Task 4: `IQuarantinable` and `Fault.Run`

**Files:**
- Create: `Assets/Game/Scripts/Core/Diagnostics/IQuarantinable.cs`
- Create: `Assets/Game/Scripts/Core/Diagnostics/Fault.cs`
- Test: `Assets/Game/Editor/Tests/FaultBarrierTests.cs`

`Fault.Run` takes a `Component`, so its test needs real MonoBehaviours. Components from `Assembly-CSharp` are not involved, but the test fixture creates its own — either test assembly would work; it goes in `Assets/Game/Editor/Tests/` so it sits beside the barrier tests in Task 6 that *do* need `Assembly-CSharp`.

- [ ] **Step 1: Write the failing test**

Write `Assets/Game/Editor/Tests/FaultBarrierTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Diagnostics;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The barrier contract: a throwing body is contained, reported loudly, and — once it has proved
    /// it is not a one-off — switched off. Loudly matters as much as contained: a guard that hides
    /// the bug it caught is worse than no guard, because the bug then ships.
    /// </summary>
    public class FaultBarrierTests
    {
        private sealed class Thrower : MonoBehaviour
        {
            public int Calls;
            public void Boom() { Calls++; throw new InvalidOperationException("boom"); }
        }

        private sealed class Shedder : MonoBehaviour, IQuarantinable
        {
            public int Shed;
            public void OnQuarantined() => Shed++;
        }

        private readonly List<GameObject> spawned = new();

        private T Make<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            spawned.Add(go);
            return go.AddComponent<T>();
        }

        [SetUp]
        public void Reset() => Fault.ResetForPlaySession();

        [TearDown]
        public void Cleanup()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
            Fault.ResetForPlaySession();
        }

        [Test]
        public void ABodyThatDoesNotThrowReportsSuccess()
        {
            bool ran = false;
            Assert.IsTrue(Fault.Run(Make<Thrower>(), "site", () => ran = true));
            Assert.IsTrue(ran);
            Assert.AreEqual(0, FaultLedger.TotalFaults);
        }

        [Test]
        public void AThrowIsContainedAndReported()
        {
            Thrower t = Make<Thrower>();

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\].*site"));
            Assert.IsFalse(Fault.Run(t, "site", t.Boom), "a contained throw reports failure to the caller");

            Assert.AreEqual(1, t.Calls);
            Assert.AreEqual(1, FaultLedger.TotalFaults, "and it lands in the ledger");
        }

        [Test]
        public void RepeatedThrowsQuarantineTheBehaviour()
        {
            Thrower t = Make<Thrower>();

            for (int i = 0; i < Fault.MaxFaultsPerWindow; i++)
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
                Fault.Run(t, "site", t.Boom);
            }

            Assert.IsTrue(Fault.IsQuarantined(t, "site"));
            Assert.IsFalse(t.enabled, "a quarantined Behaviour is switched off so it stops being ticked");
        }

        [Test]
        public void AQuarantinedSiteIsNotRunAgain()
        {
            Thrower t = Make<Thrower>();

            for (int i = 0; i < Fault.MaxFaultsPerWindow; i++)
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
                Fault.Run(t, "site", t.Boom);
            }

            int callsAtQuarantine = t.Calls;
            Assert.IsFalse(Fault.Run(t, "site", t.Boom));
            Assert.AreEqual(callsAtQuarantine, t.Calls, "the body must not be entered once quarantined");
        }

        [Test]
        public void AQuarantinableShedsItsOwnPartInsteadOfBeingDisabled()
        {
            Shedder s = Make<Shedder>();

            for (int i = 0; i < Fault.MaxFaultsPerWindow; i++)
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
                Fault.Run(s, "site", () => throw new InvalidOperationException("boom"));
            }

            Assert.AreEqual(1, s.Shed, "OnQuarantined runs exactly once");
            Assert.IsTrue(s.enabled, "a component that sheds its own part keeps running the rest");
        }

        [Test]
        public void QuarantineRaisesTheEventOnce()
        {
            Thrower t = Make<Thrower>();
            int raised = 0;
            Fault.Quarantined += _ => raised++;

            try
            {
                for (int i = 0; i < Fault.MaxFaultsPerWindow + 3; i++)
                {
                    if (Fault.IsQuarantined(t, "site")) { Fault.Run(t, "site", t.Boom); continue; }
                    LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
                    Fault.Run(t, "site", t.Boom);
                }
            }
            finally
            {
                Fault.ResetForPlaySession();
            }

            Assert.AreEqual(1, raised);
        }

        [Test]
        public void TwoSitesOnOneComponentAreIndependent()
        {
            Thrower t = Make<Thrower>();

            for (int i = 0; i < Fault.MaxFaultsPerWindow; i++)
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
                Fault.Run(t, "one", t.Boom);
            }

            Assert.IsTrue(Fault.IsQuarantined(t, "one"));
            Assert.IsFalse(Fault.IsQuarantined(t, "two"),
                           "a broken Present must not take Use down with it");
        }

        [Test]
        public void ADestroyedOwnerIsNotTouched()
        {
            Thrower t = Make<Thrower>();
            UnityEngine.Object.DestroyImmediate(t.gameObject);

            Assert.IsFalse(Fault.Run(t, "site", () => { }), "nothing to run a body on");
            Assert.AreEqual(0, FaultLedger.TotalFaults, "and a dead owner is not a fault");
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

`Tools ▸ Tests ▸ Run EditMode Tests (headless)`. Expected: compile error `CS0246: 'Fault' could not be found` (and `IQuarantinable`).

- [ ] **Step 3: Write `IQuarantinable`**

Write `Assets/Game/Scripts/Core/Diagnostics/IQuarantinable.cs`:

```csharp
// Opt out of "quarantine means enabled = false", for a component whose broken part is smaller than
// the whole component.
//
// The default is deliberately blunt — switching the Behaviour off is the only thing that is correct
// for a component this code knows nothing about. But a component that owns several jobs can lose
// one and keep the others: a station that can no longer draw its readout can still be operated, and
// disabling it outright would take the seat away too.
namespace SpaceGame.Diagnostics
{
    public interface IQuarantinable
    {
        /// <summary>
        /// Shed the part that keeps throwing. Called once, on the fault that trips quarantine, and
        /// never again for that site. Implementing this means the component is NOT disabled — so an
        /// implementation that does nothing leaves the fault running forever.
        /// </summary>
        void OnQuarantined();
    }
}
```

- [ ] **Step 4: Write `Fault`**

Write `Assets/Game/Scripts/Core/Diagnostics/Fault.cs`:

```csharp
// The barrier. One broken feature stops; the rest of the session carries on.
//
// This is a generalisation of something the codebase already does in three places, not a new idea:
// NetChannel.Dispatch runs each handler in its own try/catch so one feature's bug cannot stop the
// message carrying somebody else's damage; UnderTerrainGuard and SpawnSyncGuard both measure an
// outcome rather than enumerating causes. What was missing was a single primitive, and — because an
// asmdef cannot reference Assembly-CSharp — a place to put it that all 19 module assemblies can see.
//
// Three rules it will not break:
//
//   * It is never silent. Every fault logs an error naming the owner and the site, and lands in the
//     FaultLedger. A guard that hides the bug it caught is worse than no guard: the bug then ships.
//
//   * It never swallows anything twice over. Once a site is quarantined its body is not entered at
//     all, so a thing throwing every frame costs one log line and not sixty a second.
//
//   * It knows nothing about the game. No chat, no HUD, no player. It raises events and something in
//     Assembly-CSharp decides what a player should be told. That is what keeps this assembly free of
//     references, which is what lets every module use it.
//
// Where NOT to use it: around a single decision whose failure must abort the whole action. Damage,
// ownership transfer and spawning must refuse rather than half-happen — a half-applied change is how
// a session diverges and stays diverged. Barriers belong at fan-out points, where one caller invokes
// N independent plug-ins and the others are entitled to run.
using System;
using System.Collections;
using UnityEngine;

namespace SpaceGame.Diagnostics
{
    public static class Fault
    {
        /// <summary>Faults at one site inside one window before it is switched off.</summary>
        public const int MaxFaultsPerWindow = 5;

        /// <summary>How long that window is. See <see cref="FaultBudget"/> for why it exists.</summary>
        public const float WindowSeconds = 10f;

        private static FaultBudget budget = new(MaxFaultsPerWindow, WindowSeconds);

        /// <summary>Raised for every fault, quarantining or not.</summary>
        public static event Action<FaultRecord> Raised;

        /// <summary>Raised once per site, on the fault that switched it off.</summary>
        public static event Action<FaultRecord> Quarantined;

        /// <summary>
        /// Statics survive play-mode exit here, and the events hold delegates pointing at objects
        /// from the previous play session. Both are cleared, for the same reason ChatLog clears its.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForPlaySession()
        {
            budget = new FaultBudget(MaxFaultsPerWindow, WindowSeconds);
            Raised = null;
            Quarantined = null;
            FaultLedger.Clear();
        }

        /// <summary>
        /// Runs <paramref name="body"/> behind the barrier. Returns true when it completed.
        ///
        /// <para>
        /// False means one of three things and the caller should treat them the same: the owner is
        /// gone, the site is quarantined, or the body threw. In every case the right response is to
        /// carry on with the next thing rather than to retry.
        /// </para>
        /// </summary>
        public static bool Run(Component owner, string site, Action body)
        {
            if (body == null) return false;

            // Unity's null: a destroyed object compares equal to null while the C# reference lives.
            // A body whose owner has gone is not a fault, it is a teardown — reporting it would fill
            // the ledger with noise on every scene unload.
            if (owner == null) return false;

            string key = Key(owner, site);
            if (budget.IsQuarantined(key)) return false;

            try
            {
                body();
                return true;
            }
            catch (Exception e)
            {
                Report(owner, site, key, e);
                return false;
            }
        }

        public static bool IsQuarantined(Component owner, string site) =>
            owner != null && budget.IsQuarantined(Key(owner, site));

        /// <summary>
        /// Wraps <paramref name="body"/> so a throw inside it ends the coroutine instead of killing
        /// it where it stands.
        ///
        /// <para>
        /// This is the single highest-value use of the barrier, because an unguarded coroutine that
        /// throws does not resume, does not run its own teardown, and leaves whatever it took —
        /// the cursor, the camera, the player's input, a menu scope — taken forever. The player's
        /// only way out is quitting.
        /// </para>
        /// <para>
        /// <paramref name="onFail"/> is the teardown the routine would have run. It runs behind its
        /// own barrier, so a broken cleanup cannot re-throw out of here.
        /// </para>
        /// </summary>
        public static IEnumerator Coroutine(Component owner, string site, IEnumerator body,
                                            Action onFail = null)
        {
            if (body == null) yield break;

            while (true)
            {
                object current;

                // MoveNext is inside the try and the yield is outside it, because C# forbids
                // yielding from a try that has a catch. This shape is the reason the wrapper is a
                // loop rather than a single "yield return body".
                try
                {
                    if (!body.MoveNext()) yield break;
                    current = body.Current;
                }
                catch (Exception e)
                {
                    if (owner != null) Report(owner, site, Key(owner, site), e);
                    if (onFail != null) Run(owner, site + ".teardown", onFail);
                    yield break;
                }

                yield return current;
            }
        }

        // ------------------------------------------------------------------ internals

        // The instance id rather than the name: two creatures off the same prefab share a name, and
        // quarantining one of them must not switch off the other. Ids are unique per object and
        // stable for its life, which is exactly the scope a budget should have.
        private static string Key(Component owner, string site) =>
            $"{owner.GetInstanceID()}:{site}";

        private static void Report(Component owner, string site, string key, Exception e)
        {
            bool trips = budget.Record(key, Time.realtimeSinceStartup);

            var record = new FaultRecord(site, owner.name, e.ToString(),
                                         budget.CountFor(key), trips, Time.realtimeSinceStartup);

            FaultLedger.Add(record);

            Debug.LogError($"[Fault] {owner.name} · {site} threw (x{record.Count})" +
                           $"{(trips ? " — QUARANTINED, this feature is now off" : string.Empty)}: {e}",
                           owner);

            Raise(Raised, record);

            if (!trips) return;

            Quarantine(owner);
            Raise(Quarantined, record);
        }

        private static void Quarantine(Component owner)
        {
            // A component that can shed one job keeps the others. Anything else is switched off
            // wholesale, which is the only thing that is correct for a component this code knows
            // nothing about.
            if (owner is IQuarantinable shedder)
            {
                try { shedder.OnQuarantined(); }
                catch (Exception e) { Debug.LogError($"[Fault] OnQuarantined on '{owner.name}' threw: {e}", owner); }
                return;
            }

            if (owner is Behaviour behaviour) behaviour.enabled = false;
        }

        // Subscribers are somebody else's code and are entitled to be broken too. A throwing
        // listener must not take the report down with it, or the fault that mattered is lost.
        private static void Raise(Action<FaultRecord> handlers, in FaultRecord record)
        {
            if (handlers == null) return;

            foreach (Delegate d in handlers.GetInvocationList())
            {
                try { ((Action<FaultRecord>)d)(record); }
                catch (Exception e) { Debug.LogError($"[Fault] a fault listener threw: {e}"); }
            }
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

`Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then `cat Temp/headless_tests.txt`. Expected: `FAILED=0`.

- [ ] **Step 6: Type-check**

```bash
python3 tools/typecheck.py --editor
```
Expected: `Assembly-CSharp: no errors.` and `Assembly-CSharp-Editor: no errors.`

- [ ] **Step 7: Commit**

```bash
git add Assets/Game/Scripts/Core/Diagnostics/Fault.cs Assets/Game/Scripts/Core/Diagnostics/IQuarantinable.cs Assets/Game/Editor/Tests/FaultBarrierTests.cs
git commit -m "feat: add Fault barrier with per-site quarantine"
```

---

### Task 5: Guarded-coroutine tests

`Fault.Coroutine` was written in Task 4; this task proves it, driven by hand rather than by Unity's coroutine runner (there are no play-mode tests in this project).

**Files:**
- Test: `Assets/Game/Tests/EditMode/FaultCoroutineTests.cs`

- [ ] **Step 1: Write the test**

Write `Assets/Game/Tests/EditMode/FaultCoroutineTests.cs`:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Diagnostics;

namespace SpaceGame.Diagnostics.Tests
{
    /// <summary>
    /// Driven by hand with MoveNext rather than by StartCoroutine: this project has no play-mode
    /// tests, and the wrapper is a plain IEnumerator, so marching it in a loop tests exactly the
    /// thing Unity would run.
    /// </summary>
    public class FaultCoroutineTests
    {
        private GameObject go;
        private Transform owner;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("owner");
            owner = go.transform;
            Fault.ResetForPlaySession();
        }

        [TearDown]
        public void TearDown()
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
            Fault.ResetForPlaySession();
        }

        private static int Drain(IEnumerator routine)
        {
            int steps = 0;
            while (routine.MoveNext()) steps++;
            return steps;
        }

        private static IEnumerator Counts(List<int> into, int n)
        {
            for (int i = 0; i < n; i++) { into.Add(i); yield return null; }
        }

        private static IEnumerator ThrowsAfter(List<int> into, int n)
        {
            for (int i = 0; i < n; i++) { into.Add(i); yield return null; }
            throw new InvalidOperationException("boom");
        }

        [Test]
        public void APassingRoutineRunsToCompletionUnchanged()
        {
            var seen = new List<int>();
            Drain(Fault.Coroutine(owner, "site", Counts(seen, 3)));

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, seen);
            Assert.AreEqual(0, FaultLedger.TotalFaults);
        }

        [Test]
        public void AThrowEndsTheRoutineInsteadOfEscaping()
        {
            var seen = new List<int>();

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
            Assert.DoesNotThrow(() => Drain(Fault.Coroutine(owner, "site", ThrowsAfter(seen, 2))));

            CollectionAssert.AreEqual(new[] { 0, 1 }, seen, "work before the throw still happened");
            Assert.AreEqual(1, FaultLedger.TotalFaults);
        }

        [Test]
        public void TheTeardownRunsWhenTheRoutineThrows()
        {
            bool tornDown = false;

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
            Drain(Fault.Coroutine(owner, "site", ThrowsAfter(new List<int>(), 1), () => tornDown = true));

            Assert.IsTrue(tornDown, "this is the whole point: a dead routine must still give back what it took");
        }

        [Test]
        public void TheTeardownDoesNotRunWhenTheRoutineFinishes()
        {
            bool tornDown = false;
            Drain(Fault.Coroutine(owner, "site", Counts(new List<int>(), 2), () => tornDown = true));

            Assert.IsFalse(tornDown, "a routine that ended normally has already cleaned up after itself");
        }

        [Test]
        public void ABrokenTeardownCannotEscapeEither()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));

            Assert.DoesNotThrow(() => Drain(Fault.Coroutine(
                owner, "site", ThrowsAfter(new List<int>(), 1),
                () => throw new InvalidOperationException("cleanup is broken too"))));
        }

        [Test]
        public void YieldedValuesArePassedThrough()
        {
            IEnumerator Yields() { yield return "first"; yield return "second"; }

            IEnumerator wrapped = Fault.Coroutine(owner, "site", Yields());

            Assert.IsTrue(wrapped.MoveNext());
            Assert.AreEqual("first", wrapped.Current, "a WaitForSeconds must reach Unity unchanged");
            Assert.IsTrue(wrapped.MoveNext());
            Assert.AreEqual("second", wrapped.Current);
            Assert.IsFalse(wrapped.MoveNext());
        }
    }
}
```

`LogAssert` lives in `UnityEngine.TestTools`; add `using UnityEngine.TestTools;` to the usings if the compiler asks for it.

- [ ] **Step 2: Run the test to verify it passes**

`Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then `cat Temp/headless_tests.txt`. Expected: `FAILED=0`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Tests/EditMode/FaultCoroutineTests.cs
git commit -m "test: cover the guarded coroutine wrapper"
```

---

### Task 6: The chat bridge and the `/faults` command

`SpaceGame.Diagnostics` cannot see `ChatLog`. This is the piece in `Assembly-CSharp` that connects them, and it is why `Fault` raises events instead of writing to a HUD.

**Files:**
- Create: `Assets/Game/Scripts/Core/DiagnosticsBridge/FaultChatBridge.cs`

- [ ] **Step 1: Write the bridge**

Write `Assets/Game/Scripts/Core/DiagnosticsBridge/FaultChatBridge.cs`:

```csharp
// What a player is told when something breaks, and how they report it.
//
// Separate from SpaceGame.Diagnostics because that assembly has an empty reference list — which is
// the only reason all 19 module assemblies can use it — and ChatLog lives in Assembly-CSharp. So the
// primitive raises events and this decides what they mean to a person.
//
// Chat rather than a new HUD widget: the channel already exists, already survives scene loads,
// already has scrollback, and already has a command table anything may register into. A second
// notification system would be the same thing again with its own bugs.
//
// Only quarantines are announced. A single fault is for the log; a quarantine means a feature the
// player was using has stopped, and saying nothing about that is how "the gun does nothing now"
// becomes an hour of somebody's evening.
using System.Text;
using UnityEngine;
using SpaceGame.Diagnostics;

namespace SpaceGame.Core
{
    public static class FaultChatBridge
    {
        /// <summary>How many recent faults /faults prints. More than this and it is a file, not a chat line.</summary>
        private const int PrintLimit = 10;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            // Unsubscribe first: statics survive play-mode exit here, so without this the second
            // play session in an Editor has two of these attached and announces everything twice.
            Fault.Quarantined -= Announce;
            Fault.Quarantined += Announce;

            // Register replaces by name, so running this again after a domain reload leaves one
            // entry rather than a duplicate. Same contract ChatBuiltinCommands relies on.
            ChatCommands.Register("faults", "/faults", "List the features that have faulted this session.",
                                  List, "errors");
        }

        private static void Announce(FaultRecord record) =>
            ChatLog.AddSystem($"{record.Site} on '{record.Owner}' faulted and has been switched off. " +
                              "The rest of the game keeps running. Type /faults for detail.");

        private static string List(ulong sender, string[] args)
        {
            if (FaultLedger.TotalFaults == 0) return "No faults this session.";

            var text = new StringBuilder();
            text.Append($"{FaultLedger.TotalFaults} fault(s) this session");

            int from = Mathf.Max(0, FaultLedger.Recent.Count - PrintLimit);
            if (from > 0 || FaultLedger.TotalFaults > FaultLedger.Recent.Count)
                text.Append($" — showing the last {FaultLedger.Recent.Count - from}");
            text.Append(':');

            for (int i = from; i < FaultLedger.Recent.Count; i++)
                text.Append('\n').Append(FaultLedger.Recent[i]);

            return text.ToString();
        }
    }
}
```

- [ ] **Step 2: Verify the command table accepts it**

Confirm the signature `ChatCommands.Register(name, usage, summary, handler, params aliases)` matches:

```bash
grep -n "public static void Register" Assets/Game/Scripts/Core/Multiplayer/Chat/ChatCommands.cs
```
Expected: a `Register` overload taking name, usage, summary, `ChatCommandHandler`, and a `params string[]` of aliases. If the parameter order differs, match the file — not this plan.

- [ ] **Step 3: Type-check**

```bash
python3 tools/typecheck.py --editor
```
Expected: no errors in either assembly.

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Scripts/Core/DiagnosticsBridge/FaultChatBridge.cs
git commit -m "feat: announce quarantines in chat and add /faults"
```

---

## Phase 1 — barriers at the fan-out seams

### Task 7: Barrier the agent module loops

Four loops in `AgentController` invoke N independent modules. Today one throwing module stops every module after it in the same list — a creature with a broken `ChaseModule` stops walking, stops fleeing and stops attacking. With the barrier the broken module drops out and the creature keeps behaving.

**Files:**
- Modify: `Assets/Game/Scripts/Agents/Controller/AgentController.cs` (four loops at lines 194, 267, 277, 308)
- Test: `Assets/Game/Editor/Tests/AgentModuleBarrierTests.cs`

- [ ] **Step 1: Write the failing test**

Write `Assets/Game/Editor/Tests/AgentModuleBarrierTests.cs`:

```csharp
using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Agents;
using SpaceGame.Diagnostics;

namespace SpaceGame.Tests
{
    /// <summary>
    /// A creature whose highest-priority module is broken must still walk on its next one. Before
    /// the barrier, the throw escaped EvaluateModules and the whole agent stopped — every module
    /// below the broken one starved, so a bug in chasing also removed fleeing and wandering.
    ///
    /// Modules are exercised through <see cref="AgentController.RunModule"/> rather than through
    /// Update, because AddComponent outside play mode raises no Awake and the controller never
    /// resolves its motor.
    /// </summary>
    public class AgentModuleBarrierTests
    {
        private sealed class ThrowingModule : BehaviourModuleBase
        {
            public int Calls;
            public override MoveIntent? Tick(in AgentContext context, float deltaTime)
            {
                Calls++;
                throw new InvalidOperationException("module is broken");
            }
        }

        private sealed class WalkingModule : BehaviourModuleBase
        {
            public int Calls;
            public override MoveIntent? Tick(in AgentContext context, float deltaTime)
            {
                Calls++;
                return MoveIntent.Idle();
            }
        }

        private GameObject agent;

        [SetUp]
        public void SetUp()
        {
            agent = new GameObject("agent");
            Fault.ResetForPlaySession();
        }

        [TearDown]
        public void TearDown()
        {
            if (agent != null) UnityEngine.Object.DestroyImmediate(agent);
            Fault.ResetForPlaySession();
        }

        [Test]
        public void AThrowingModuleReturnsNoIntentAndDoesNotEscape()
        {
            var broken = agent.AddComponent<ThrowingModule>();
            var context = new AgentContext { Self = agent.transform, Position = Vector3.zero };

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));

            MoveIntent? result = null;
            Assert.DoesNotThrow(() => result = AgentController.RunModule(broken, in context, 0.02f));

            Assert.IsNull(result, "a module that threw claimed nothing, so the next one must get the frame");
            Assert.AreEqual(1, broken.Calls);
        }

        [Test]
        public void ARepeatedlyThrowingModuleIsSwitchedOff()
        {
            var broken = agent.AddComponent<ThrowingModule>();
            var context = new AgentContext { Self = agent.transform, Position = Vector3.zero };

            for (int i = 0; i < Fault.MaxFaultsPerWindow; i++)
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
                AgentController.RunModule(broken, in context, 0.02f);
            }

            Assert.IsFalse(broken.enabled);
            Assert.IsFalse(broken.IsActive, "IsActive reads enabled, so the controller stops ticking it");
        }

        [Test]
        public void AHealthyModuleIsUnaffectedByABrokenSibling()
        {
            var broken = agent.AddComponent<ThrowingModule>();
            var healthy = agent.AddComponent<WalkingModule>();
            var context = new AgentContext { Self = agent.transform, Position = Vector3.zero };

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[Fault\\]"));
            AgentController.RunModule(broken, in context, 0.02f);

            MoveIntent? result = AgentController.RunModule(healthy, in context, 0.02f);

            Assert.IsNotNull(result, "the creature still moves");
            Assert.AreEqual(1, healthy.Calls);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

`Tools ▸ Tests ▸ Run EditMode Tests (headless)`. Expected: compile error `CS0117: 'AgentController' does not contain a definition for 'RunModule'`.

- [ ] **Step 3: Add `RunModule` to `AgentController`**

In `Assets/Game/Scripts/Agents/Controller/AgentController.cs`, add this method in the "Module evaluation" region, immediately above `private MoveIntent EvaluateModules(`:

```csharp
        /// <summary>
        /// Ticks one module behind the fault barrier, and reports what it claimed.
        ///
        /// <para>
        /// A module that throws returns null — the same answer as "I pass" — so the frame falls
        /// through to the next module rather than being lost. That is the whole degradation
        /// contract here: one broken behaviour costs the creature that behaviour, not its ability
        /// to move at all. Before this, the throw escaped the loop and every module below the
        /// broken one starved, which is why a bug in chasing also removed fleeing and wandering.
        /// </para>
        /// <para>
        /// Public and static so the barrier can be tested without a motor, a NavMesh or an Awake.
        /// Not <c>internal</c>: the tests live in <c>Assembly-CSharp-Editor</c>, which has no
        /// <c>InternalsVisibleTo</c> into <c>Assembly-CSharp</c> and would not see it.
        /// </para>
        /// </summary>
        public static MoveIntent? RunModule(IBehaviourModule module, in AgentContext context, float deltaTime)
        {
            if (module is not Component owner) return null;

            MoveIntent? result = null;
            AgentContext local = context;   // a lambda cannot capture an `in` parameter

            Fault.Run(owner, ModuleSite, () => result = module.Tick(in local, deltaTime));

            return result;
        }

        /// <summary>One site name for every module, so a creature's quarantines are per component.</summary>
        private const string ModuleSite = "AgentModule.Tick";
```

Add `using SpaceGame.Diagnostics;` to the file's usings.

- [ ] **Step 4: Route the four loops through it**

Replace the body of `TickPresentation`'s loop (around line 194):

```csharp
            foreach (IBehaviourModule module in presentationModules)
            {
                if (module.IsActive)
                    RunModule(module, in context, deltaTime);
            }
```

Replace the side-effect loop in `EvaluateModules` (around line 267):

```csharp
                foreach (IBehaviourModule module in sideEffectModules)
                {
                    if (module.IsActive)
                        RunModule(module, in context, deltaTime);
                }
```

Replace the movement loop in `EvaluateModules` (around line 277):

```csharp
                foreach (IBehaviourModule module in movementModules)
                {
                    if (!module.IsActive)
                        continue;

                    MoveIntent? result = RunModule(module, in context, deltaTime);
                    if (result.HasValue)
                    {
                        // Don't broadcast Idle — it would lock the whole herd in place.
                        if (result.Value.Type != AgentIntentType.Idle)
                            herdModule?.Publish(module.Priority, result.Value);
                        return result.Value;
                    }
                }
```

Replace the facing loop in `ApplyFacingOverride` (around line 308):

```csharp
            foreach (IFacingModule module in facingModules)
            {
                if (!module.IsActive)
                    continue;

                if (module is not Component owner) continue;

                bool wants = false;
                Vector3 facePosition = Vector3.zero;
                AgentContext local = context;

                Fault.Run(owner, "AgentModule.Facing",
                          () => wants = module.TryGetFacing(in local, out facePosition));

                if (!wants) continue;

                intent.FacePosition = facePosition;
                intent.OverrideFacing = true;
                return;
            }
```

- [ ] **Step 5: Run the test to verify it passes**

`Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then `cat Temp/headless_tests.txt`. Expected: `FAILED=0`.

- [ ] **Step 6: Type-check and commit**

```bash
python3 tools/typecheck.py --editor
git add Assets/Game/Scripts/Agents/Controller/AgentController.cs Assets/Game/Editor/Tests/AgentModuleBarrierTests.cs
git commit -m "feat: contain a throwing agent module so the creature keeps behaving"
```

---

### Task 8: Barrier the item use pipeline

`UseChannel` calls into artifact code four times. Today a throw in `PlayUse` (presentation) skips the `TryUse` (effect) below it, and a throw in either escapes into the caller — which, on the server side, is a `NetChannel` handler that has already been delivered. Separating the two sites means a broken muzzle flash cannot stop the shot, and a broken shot cannot stop the flash.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Inventory/Core/UseChannel.cs` (lines 116–125, 265, 277)

- [ ] **Step 1: Add the using and the site names**

At the top of `Assets/Game/Scripts/Items/Inventory/Core/UseChannel.cs` add:

```csharp
using SpaceGame.Diagnostics;
```

and inside the class, beside the other constants:

```csharp
        // Two sites, not one, and that is the point of this change. Presentation and effect fail
        // independently: a broken muzzle flash must not stop the shot, and a broken shot must not
        // stop everyone else seeing the flash. One shared site would quarantine both together.
        private const string PresentSite = "UseChannel.Present";
        private const string EffectSite  = "UseChannel.Effect";
```

- [ ] **Step 2: Barrier the owner path**

In `Press()`, replace:

```csharp
            usable.OnRequestUse(ref arg);

            // Presented immediately, always, so no item ever feels like it is waiting for a reply.
            usable.PlayUse(Holder, arg);
            Presented?.Invoke();

            // An owner-authoritative tool is ours to run, right now — its effect is this player's
            // own body, which already replicates through the transform they own.
            if (usable.Authority == UseAuthority.Owner)
                usable.TryUse(Holder, arg);
```

with:

```csharp
            // OnRequestUse is NOT barriered. It writes the aim into the message every other machine
            // will act on, so a half-filled NetArg is worse than no use at all — this one must
            // abort rather than degrade.
            usable.OnRequestUse(ref arg);

            // Presented immediately, always, so no item ever feels like it is waiting for a reply.
            NetArg presented = arg;
            if (Fault.Run(usable, PresentSite, () => usable.PlayUse(Holder, presented)))
                Presented?.Invoke();

            // An owner-authoritative tool is ours to run, right now — its effect is this player's
            // own body, which already replicates through the transform they own.
            if (usable.Authority == UseAuthority.Owner)
            {
                NetArg effect = arg;
                Fault.Run(usable, EffectSite, () => usable.TryUse(Holder, effect));
            }
```

The local copies exist because `NetArg arg` is written by `OnRequestUse(ref arg)` and a lambda cannot capture a `ref`/`in` parameter.

- [ ] **Step 3: Barrier the server path**

In `OnUseRequested`, replace:

```csharp
            if (usable.Authority == UseAuthority.Server)
                usable.TryUse(Holder, arg);
```

with:

```csharp
            if (usable.Authority == UseAuthority.Server)
            {
                NetArg effect = arg;
                Fault.Run(usable, EffectSite, () => usable.TryUse(Holder, effect));
            }
```

Note the broadcast below it is left where it is and still runs. That is deliberate: peers are told the item was used even when this machine's copy of the effect threw, because a peer that never hears the message diverges permanently while one that hears it and shows the animation does not.

- [ ] **Step 4: Barrier the peer path**

In `OnUsedElsewhere`, replace:

```csharp
            usable.PlayUse(Holder, arg);
            Presented?.Invoke();
```

with:

```csharp
            NetArg presented = arg;
            if (Fault.Run(usable, PresentSite, () => usable.PlayUse(Holder, presented)))
                Presented?.Invoke();
```

- [ ] **Step 5: Type-check**

```bash
python3 tools/typecheck.py --editor
```
Expected: no errors.

- [ ] **Step 6: Run the full suite**

`Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then `cat Temp/headless_tests.txt`. Expected: `FAILED=0` — the existing artifact suites (288 assertions) must be unaffected.

- [ ] **Step 7: Commit**

```bash
git add Assets/Game/Scripts/Items/Inventory/Core/UseChannel.cs
git commit -m "feat: fail item presentation and effect independently"
```

---

### Task 9: Barrier the interaction pipeline

`Interactor.Interact` and `SecondaryInteract` call into arbitrary `IInteractable` implementations. A throw there escapes into an input callback, and the player's right-click stops working on everything, not just the broken door.

**Files:**
- Modify: `Assets/Game/Scripts/Gameplay/Interaction/Core/Interactor.cs` (lines 161–172, 234–244)

- [ ] **Step 1: Add the using**

At the top of `Assets/Game/Scripts/Gameplay/Interaction/Core/Interactor.cs` add:

```csharp
using SpaceGame.Diagnostics;
```

- [ ] **Step 2: Barrier the primary interaction**

In `Interact()`, replace the call:

```csharp
                interactable.Interact(this);
```

with:

```csharp
                // Barriered on the TARGET, not on this Interactor: a broken door must cost the
                // player that door, not their ability to interact with anything ever again. Quarantine
                // therefore switches off the door, which is the thing that is actually broken.
                if (interactable is Component target)
                    Fault.Run(target, "Interactable.Interact", () => interactable.Interact(this));
                else
                    interactable.Interact(this);
```

- [ ] **Step 3: Barrier the secondary interaction**

In `SecondaryInteract()`, replace:

```csharp
            secondary.SecondaryInteract(this);
```

with:

```csharp
            if (secondary is Component target)
                Fault.Run(target, "Interactable.SecondaryInteract", () => secondary.SecondaryInteract(this));
            else
                secondary.SecondaryInteract(this);
```

- [ ] **Step 4: Type-check and run the suite**

```bash
python3 tools/typecheck.py --editor
```
`Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then `cat Temp/headless_tests.txt`. Expected: `FAILED=0` (the existing `Interactor` ray/hover tests must still pass).

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Gameplay/Interaction/Core/Interactor.cs
git commit -m "feat: contain a throwing interactable so interaction keeps working"
```

---

## Phase 2 — guarded coroutines

### Task 10: Guard the coroutines that hold the player's controls

These four are the "stuck forever" cases: each one takes something (the menu scope, the camera, the screen, a network wait) and gives it back at the end. A throw in the middle means it is never given back.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Backpack/Focus/PackFocusSession.cs:231`
- Modify: `Assets/Game/Scripts/Presentation/Cameras/FocusCamera.cs:192,235`
- Modify: `Assets/Game/Scripts/Presentation/Cutscenes/UI/LetterboxOverlay.cs:90,98,106,114,130`
- Modify: `Assets/Game/Scripts/Gameplay/Interaction/Core/NetLatch.cs:190`

- [ ] **Step 1: Guard `PackFocusSession.ReshoulderOnceLanded`**

Add `using SpaceGame.Diagnostics;` at the top of `PackFocusSession.cs`, then replace line 231:

```csharp
                    pendingStow = StartCoroutine(ReshoulderOnceLanded());
```

with:

```csharp
                    // Guarded, because this routine holds the menu scope open while it waits. If it
                    // dies mid-wait the player keeps a free cursor, loses gameplay input and has no
                    // way back except quitting — the failure this whole layer exists to end.
                    pendingStow = StartCoroutine(Fault.Coroutine(
                        this, "PackFocus.Reshoulder", ReshoulderOnceLanded(), Exit));
```

- [ ] **Step 2: Guard `FocusCamera`'s two flights**

Add `using SpaceGame.Diagnostics;` at the top of `FocusCamera.cs`, then replace line 192:

```csharp
            flight = StartCoroutine(FlyIn());
```

with:

```csharp
            // The teardown is FlyOut rather than nothing: a fly-in that dies halfway leaves the
            // camera detached from the player with no route home.
            flight = StartCoroutine(Fault.Coroutine(this, "FocusCamera.FlyIn", FlyIn(), FlyOut));
```

and line 235:

```csharp
            flight = StartCoroutine(FlyOutRoutine());
```

with:

```csharp
            // No teardown: this IS the teardown, and a second attempt at a fly-out that just threw
            // would throw again. The barrier's job here is only to stop the throw escaping.
            flight = StartCoroutine(Fault.Coroutine(this, "FocusCamera.FlyOut", FlyOutRoutine()));
```

Before writing this, confirm the exact names of the public entry points:

```bash
grep -n "public void FlyOut\|private IEnumerator FlyOutRoutine\|public void FlyIn" Assets/Game/Scripts/Presentation/Cameras/FocusCamera.cs
```
Use the names the file actually has. If the public fly-out is not called `FlyOut`, use whatever the file names it as the teardown action.

- [ ] **Step 3: Guard `LetterboxOverlay`'s five routines**

Add `using SpaceGame.Diagnostics;`, then wrap each of the five `StartCoroutine` calls. Lines 90, 98, 106, 114:

```csharp
            barsRoutine = StartCoroutine(Fault.Coroutine(
                this, "Letterbox.Bars", AnimateBars(true, duration, ++barsGeneration), HideInstantly));
```
```csharp
            barsRoutine = StartCoroutine(Fault.Coroutine(
                this, "Letterbox.Bars", AnimateBars(false, duration, ++barsGeneration), HideInstantly));
```
```csharp
            fadeRoutine = StartCoroutine(Fault.Coroutine(
                this, "Letterbox.Fade", AnimateFade(1f, duration, ++fadeGeneration), HideInstantly));
```
```csharp
            fadeRoutine = StartCoroutine(Fault.Coroutine(
                this, "Letterbox.Fade", AnimateFade(0f, duration, ++fadeGeneration), HideInstantly));
```

and line 130:

```csharp
            return StartCoroutine(Fault.Coroutine(
                this, "Letterbox.FadeOutIn",
                FadeOutInRoutine(duringBlack, fadeOutDur, holdDur, fadeInDur), HideInstantly));
```

Then add the teardown, beside the other private methods:

```csharp
        /// <summary>
        /// Put the screen back with no animation. The teardown for every routine in this file:
        /// a bar animation that dies half-open leaves the player looking at the game through a
        /// letterbox that nothing will ever close, and a fade that dies leaves a black screen.
        /// Both are indistinguishable from a hung game.
        /// </summary>
        private void HideInstantly()
        {
            barsGeneration++;
            fadeGeneration++;
            SetBars(0f);
            SetFade(0f);
        }
```

Before writing `HideInstantly`, read how the file sets its bar height and fade alpha:

```bash
grep -n "private void Set\|barHeight\|canvasGroup.alpha\|SetBars\|SetFade" Assets/Game/Scripts/Presentation/Cutscenes/UI/LetterboxOverlay.cs
```
Use the file's own setters. If it writes the values inline in the routines rather than through setters, extract those two writes into `SetBars(float)` and `SetFade(float)` first and call them from both places — do not duplicate the arithmetic.

- [ ] **Step 4: Guard `NetLatch.AskWhenConnected`**

Add `using SpaceGame.Diagnostics;` to `NetLatch.cs`, then replace line 190:

```csharp
                askRoutine = owner.StartCoroutine(AskWhenConnected());
```

with:

```csharp
                // No teardown: the latch's own state is already "not answered", which is the correct
                // reading after a failed ask. The barrier is here so a throw does not leave the
                // routine handle set on a coroutine that is never going to complete.
                askRoutine = owner.StartCoroutine(Fault.Coroutine(owner, "NetLatch.Ask", AskWhenConnected()));
```

Note `owner` rather than `this`: `NetLatch` may not be a `Component` itself. Confirm before writing:

```bash
grep -n "class NetLatch\|owner;" Assets/Game/Scripts/Gameplay/Interaction/Core/NetLatch.cs
```
If `NetLatch` is a plain class holding a `MonoBehaviour owner`, pass `owner`. If it is itself a `Component`, pass `this`.

- [ ] **Step 5: Type-check and run the suite**

```bash
python3 tools/typecheck.py --editor
```
`Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then `cat Temp/headless_tests.txt`. Expected: `FAILED=0`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/Items/Backpack/Focus/PackFocusSession.cs Assets/Game/Scripts/Presentation/Cameras/FocusCamera.cs Assets/Game/Scripts/Presentation/Cutscenes/UI/LetterboxOverlay.cs Assets/Game/Scripts/Gameplay/Interaction/Core/NetLatch.cs
git commit -m "feat: guard the coroutines that hold the player's controls"
```

---

### Task 11: Guard the remaining state-holding coroutines

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/Cutscenes/Core/CutsceneDirector.cs`
- Modify: `Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalDirector.cs`
- Modify: `Assets/Game/Scripts/Vehicles/Stations/VehicleStation.cs`
- Modify: `Assets/Game/Scripts/Core/SceneManagement/Transitions/Effects/FadeToBlackEffect.cs`

- [ ] **Step 1: Find every `StartCoroutine` in these four files**

```bash
grep -n "StartCoroutine" Assets/Game/Scripts/Presentation/Cutscenes/Core/CutsceneDirector.cs Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalDirector.cs Assets/Game/Scripts/Vehicles/Stations/VehicleStation.cs Assets/Game/Scripts/Core/SceneManagement/Transitions/Effects/FadeToBlackEffect.cs
```

- [ ] **Step 2: Wrap each one**

For every hit, apply the same transformation used in Task 10:

```csharp
StartCoroutine(SomeRoutine(args))
```
becomes
```csharp
StartCoroutine(Fault.Coroutine(this, "<Type>.<Routine>", SomeRoutine(args), <teardown>))
```

Pick the teardown by asking: **what does this routine give back at its end?** Use the method that already gives it back — do not write a second copy of it.

- `CutsceneDirector` — the routine ends by clearing `IsPlaying` and handing the player back. Teardown: the existing method that ends a cutscene (`grep -n "IsPlaying = false" Assets/Game/Scripts/Presentation/Cutscenes/Core/CutsceneDirector.cs` names it). A cutscene that dies mid-play leaves `IsPlaying` true forever, which stops `InputRestoreGuard` in Task 14 from ever firing — so this one is load-bearing for Phase 3.
- `ArrivalDirector` — teardown is whatever ends the arrival and gives the player their body. A dead arrival routine is a player who never gets out of the ship.
- `VehicleStation` — teardown is the existing release/unman method. A dead routine leaves a station claimed by a player who is not in it, and nobody else can use it for the rest of the session.
- `FadeToBlackEffect` — teardown is setting the fade to 0. A dead fade is a permanently black screen.

- [ ] **Step 3: Add the using to each file**

```csharp
using SpaceGame.Diagnostics;
```

- [ ] **Step 4: Type-check and run the suite**

```bash
python3 tools/typecheck.py --editor
```
`Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then `cat Temp/headless_tests.txt`. Expected: `FAILED=0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Presentation/Cutscenes/Core/CutsceneDirector.cs Assets/Game/Scripts/Gameplay/Arrival/Runtime/ArrivalDirector.cs Assets/Game/Scripts/Vehicles/Stations/VehicleStation.cs Assets/Game/Scripts/Core/SceneManagement/Transitions/Effects/FadeToBlackEffect.cs
git commit -m "feat: guard the cutscene, arrival, station and fade coroutines"
```

---

## Phase 3 — outcome guards

Every guard here follows `UnderTerrainGuard`: a pure `*Rule` decides, a class reads the world and applies the verdict, the recovery is bounded, and the first time it fires it logs an error — because a guard that hides the bug it caught is worse than no guard.

### Task 12: `ISessionGuard` and `SessionGuardRunner`

**Files:**
- Create: `Assets/Game/Scripts/Core/Safety/ISessionGuard.cs`
- Create: `Assets/Game/Scripts/Core/Safety/SessionGuardRunner.cs`

- [ ] **Step 1: Write the interface**

Write `Assets/Game/Scripts/Core/Safety/ISessionGuard.cs`:

```csharp
// One thing that can go wrong with a session, and how to notice it.
//
// A guard measures an OUTCOME. It does not know how the state it finds came about, and deliberately
// so — UnderTerrainGuard's header makes the argument in full: a failsafe that enumerated causes
// would only ever cover the ones already known about, and the whole value of these is catching the
// cause nobody has hit yet.
namespace SpaceGame.Core.Safety
{
    public interface ISessionGuard
    {
        /// <summary>Short, stable, used in the fault site and in the log. e.g. "StuckScope".</summary>
        string Name { get; }

        /// <summary>
        /// Look at the world and fix it if it is broken.
        /// <paramref name="interval"/> is the seconds since this guard was last checked — guards
        /// measure their own patience with it rather than reading Time directly, so a test can march
        /// one forward without a frame loop.
        /// </summary>
        void Check(float interval);
    }
}
```

- [ ] **Step 2: Write the runner**

Write `Assets/Game/Scripts/Core/Safety/SessionGuardRunner.cs`:

```csharp
// Runs every session guard on a slow tick.
//
// Bootstrapped from a static rather than placed in a scene, for the same reason SessionWatchdog and
// PauseMenuUI are: gameplay is spread over a persistent scene, streamed world chunks and an
// additively loaded arena, and a listener that must exist in all of them cannot be authored into
// one of them.
//
// Slow on purpose. None of these are physics: they answer "has this been wrong for a while", and
// asking twice a second is both enough to fix it before the player gives up and cheap enough to
// ignore. The same bargain UnderTerrainGuard's checkInterval makes.
//
// Each guard runs behind the fault barrier. A guard that throws is exactly the thing this system
// exists to survive, and one broken guard must not stop the others from recovering the session.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Diagnostics;

namespace SpaceGame.Core.Safety
{
    public class SessionGuardRunner : MonoBehaviour
    {
        /// <summary>Seconds between sweeps. See the header for why this is not per-frame.</summary>
        public const float CheckIntervalSeconds = 0.5f;

        private static SessionGuardRunner instance;

        private readonly List<ISessionGuard> guards = new();
        private float sinceLastCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var go = new GameObject(nameof(SessionGuardRunner));
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SessionGuardRunner>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            guards.Add(new StuckScopeGuard());
            guards.Add(new InputRestoreGuard());
            guards.Add(new MountGuard());
            guards.Add(new ViewGuard());
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            // Unscaled, because half of what these guards recover from is a menu scope that has
            // stopped the clock and cannot give it back. A guard measuring frozen time would wait
            // forever for exactly the failure it exists to end.
            sinceLastCheck += Time.unscaledDeltaTime;
            if (sinceLastCheck < CheckIntervalSeconds) return;

            float interval = sinceLastCheck;
            sinceLastCheck = 0f;

            foreach (ISessionGuard guard in guards)
            {
                ISessionGuard g = guard;
                Fault.Run(this, $"Guard.{g.Name}", () => g.Check(interval));
            }
        }
    }
}
```

- [ ] **Step 3: Type-check**

```bash
python3 tools/typecheck.py --editor
```
Expected: errors naming `StuckScopeGuard`, `InputRestoreGuard`, `MountGuard` and `ViewGuard` — those are written in Tasks 13–16. That is the expected intermediate state; the next four tasks close it.

- [ ] **Step 4: Do not commit yet**

This task leaves the tree uncompilable on purpose, and a compile error anywhere freezes the loaded domain for every assembly. Continue straight to Task 13 and commit at the end of Task 16.

---

### Task 13: `StuckScopeGuard` — the frozen-with-a-free-cursor bug

`GameplayMenuScope` holds a `HashSet<object> owners`. Seven screens put themselves in it. If any of them is destroyed, or disabled, without calling `Exit`, the player keeps a free cursor, loses gameplay input, and in a solo session the clock stays stopped. There is no way out but quitting.

**Files:**
- Modify: `Assets/Game/Scripts/Presentation/UI/Widgets/GameplayMenuScope.cs`
- Create: `Assets/Game/Scripts/Core/Safety/Rules/StuckScopeRule.cs`
- Create: `Assets/Game/Scripts/Core/Safety/Guards/StuckScopeGuard.cs`
- Test: `Assets/Game/Editor/Tests/SessionGuardRuleTests.cs`

- [ ] **Step 1: Write the failing test**

Write `Assets/Game/Editor/Tests/SessionGuardRuleTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core.Safety;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The four decisions the session guards make, tested without any of the world they read.
    /// Same split as UnderTerrainRule against UnderTerrainGuard: a decision inlined in Update() is a
    /// decision nothing can test.
    /// </summary>
    public class SessionGuardRuleTests
    {
        // ── StuckScopeRule ────────────────────────────────────────────────────

        [Test]
        public void ANullOwnerIsAbandoned()
        {
            Assert.IsTrue(StuckScopeRule.IsAbandoned(null));
        }

        [Test]
        public void ADestroyedComponentIsAbandoned()
        {
            var go = new GameObject("owner");
            var behaviour = go.AddComponent<Light>();
            Object.DestroyImmediate(go);

            Assert.IsTrue(StuckScopeRule.IsAbandoned(behaviour),
                          "a destroyed Unity object compares equal to null while the C# reference lives");
        }

        [Test]
        public void ADisabledComponentIsAbandoned()
        {
            var go = new GameObject("owner");
            var behaviour = go.AddComponent<Light>();
            behaviour.enabled = false;

            Assert.IsTrue(StuckScopeRule.IsAbandoned(behaviour));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void AnActiveComponentIsNotAbandoned()
        {
            var go = new GameObject("owner");
            var behaviour = go.AddComponent<Light>();

            Assert.IsFalse(StuckScopeRule.IsAbandoned(behaviour));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void APlainObjectIsNeverJudged()
        {
            Assert.IsFalse(StuckScopeRule.IsAbandoned(new object()),
                           "nothing can be read off a non-Unity owner, so it gets the benefit of the doubt");
        }

        // ── InputRestoreRule ──────────────────────────────────────────────────

        [Test]
        public void InputIsNotRestoredWhileItWorks()
        {
            Assert.IsFalse(InputRestoreRule.ShouldRestore(
                inputEnabled: true, menuActive: false, cutsceneRunning: false,
                isDead: false, mounted: false, stuckSeconds: 60f, timeoutSeconds: 5f));
        }

        [Test]
        public void InputIsNotRestoredWhileSomethingLegitimatelyHoldsIt()
        {
            Assert.IsFalse(InputRestoreRule.ShouldRestore(false, menuActive: true,  cutsceneRunning: false, isDead: false, mounted: false, stuckSeconds: 60f, timeoutSeconds: 5f), "a menu is up");
            Assert.IsFalse(InputRestoreRule.ShouldRestore(false, menuActive: false, cutsceneRunning: true,  isDead: false, mounted: false, stuckSeconds: 60f, timeoutSeconds: 5f), "a cutscene is playing");
            Assert.IsFalse(InputRestoreRule.ShouldRestore(false, menuActive: false, cutsceneRunning: false, isDead: true,  mounted: false, stuckSeconds: 60f, timeoutSeconds: 5f), "the player is dead");
            Assert.IsFalse(InputRestoreRule.ShouldRestore(false, menuActive: false, cutsceneRunning: false, isDead: false, mounted: true,  stuckSeconds: 60f, timeoutSeconds: 5f), "the player is riding");
        }

        [Test]
        public void InputIsRestoredOnlyAfterTheTimeout()
        {
            Assert.IsFalse(InputRestoreRule.ShouldRestore(false, false, false, false, false, stuckSeconds: 4.9f, timeoutSeconds: 5f),
                           "a frame of disabled input is normal during a handover");
            Assert.IsTrue(InputRestoreRule.ShouldRestore(false, false, false, false, false, stuckSeconds: 5f, timeoutSeconds: 5f));
        }

        // ── MountRecoveryRule ─────────────────────────────────────────────────

        [Test]
        public void NoMountMeansNothingToRecover()
        {
            Assert.IsFalse(MountRecoveryRule.ShouldDismount(
                haveMount: false, mountAlive: false, mountClaimsRider: false,
                brokenSeconds: 60f, timeoutSeconds: 3f));
        }

        [Test]
        public void AHealthyMountIsLeftAlone()
        {
            Assert.IsFalse(MountRecoveryRule.ShouldDismount(true, mountAlive: true, mountClaimsRider: true, brokenSeconds: 60f, timeoutSeconds: 3f));
        }

        [Test]
        public void ADeadMountForcesADismountAfterTheTimeout()
        {
            Assert.IsFalse(MountRecoveryRule.ShouldDismount(true, mountAlive: false, mountClaimsRider: false, brokenSeconds: 2f, timeoutSeconds: 3f));
            Assert.IsTrue(MountRecoveryRule.ShouldDismount(true, mountAlive: false, mountClaimsRider: false, brokenSeconds: 3f, timeoutSeconds: 3f));
        }

        [Test]
        public void AMountThatNoLongerClaimsTheRiderAlsoCounts()
        {
            Assert.IsTrue(MountRecoveryRule.ShouldDismount(true, mountAlive: true, mountClaimsRider: false, brokenSeconds: 5f, timeoutSeconds: 3f),
                          "the mount let go without the rider being told, which strands them seated on nothing");
        }

        // ── ViewRecoveryRule ──────────────────────────────────────────────────

        [Test]
        public void AVisibleGameIsNotRecovered()
        {
            Assert.IsFalse(ViewRecoveryRule.ShouldRestoreView(anyEnabledCamera: true, blindSeconds: 60f, timeoutSeconds: 3f));
        }

        [Test]
        public void ABlindMachineIsRecoveredOnlyAfterTheTimeout()
        {
            Assert.IsFalse(ViewRecoveryRule.ShouldRestoreView(false, blindSeconds: 2.9f, timeoutSeconds: 3f),
                           "a single-scene load legitimately has no camera for a moment");
            Assert.IsTrue(ViewRecoveryRule.ShouldRestoreView(false, blindSeconds: 3f, timeoutSeconds: 3f));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

`Tools ▸ Tests ▸ Run EditMode Tests (headless)`. Expected: compile errors naming all four rules.

- [ ] **Step 3: Write all four rules**

Write `Assets/Game/Scripts/Core/Safety/Rules/StuckScopeRule.cs`:

```csharp
// Whether a menu-scope owner has gone without giving the scope back.
//
// The judgement is deliberately narrow. Only two states are read as abandoned — destroyed, and
// disabled — because those are the two a screen cannot be in while it is on screen. Anything else
// gets the benefit of the doubt: a guard that guessed would close the pause menu under somebody
// reading it, which is a worse outcome than the freeze it prevents.
//
// A non-Unity owner is never judged at all. Nothing can be read off a plain object, and every owner
// in the project today is a MonoBehaviour — but that must not become an assumption this file makes.
using UnityEngine;

namespace SpaceGame.Core.Safety
{
    public static class StuckScopeRule
    {
        public static bool IsAbandoned(object owner)
        {
            if (owner == null) return true;

            // Unity's null: a destroyed object compares equal to null while the C# reference is
            // still alive, which is exactly the state a leaked scope owner is in.
            if (owner is Object unityObject && unityObject == null) return true;

            if (owner is Behaviour behaviour) return !behaviour.isActiveAndEnabled;

            return false;
        }
    }
}
```

Write `Assets/Game/Scripts/Core/Safety/Rules/InputRestoreRule.cs`:

```csharp
// Whether the player has lost their controls to nothing.
//
// Every "why can I not move" report has the same shape: PlayerController.Input is disabled and the
// thing that disabled it is gone. What makes this decidable is that the four legitimate holders are
// all askable — a menu is up, a cutscene is playing, the player is dead, the player is riding — so
// "none of those, for several seconds" is not a guess.
//
// cutsceneRunning is deliberately NOT PlayerController.InCutsceneMode. That flag is the SYMPTOM: it
// is how a menu, a cutscene and a leaked scope all express themselves, so reading it here would make
// the guard refuse to fire in exactly the case it exists for. Ask the cutscene director instead.
namespace SpaceGame.Core.Safety
{
    public static class InputRestoreRule
    {
        public static bool ShouldRestore(bool inputEnabled, bool menuActive, bool cutsceneRunning,
                                         bool isDead, bool mounted,
                                         float stuckSeconds, float timeoutSeconds)
        {
            if (inputEnabled) return false;
            if (menuActive || cutsceneRunning || isDead || mounted) return false;

            // Bounded rather than immediate: input is legitimately off for a frame or two during
            // every handover — mounting, a scene transition, a respawn — and a guard that fired on
            // that would fight the game instead of repairing it.
            return stuckSeconds >= timeoutSeconds;
        }
    }
}
```

Write `Assets/Game/Scripts/Core/Safety/Rules/MountRecoveryRule.cs`:

```csharp
// Whether a rider is still attached to something that still exists.
//
// Two ways this breaks, and they look identical to the player: the mount was destroyed while they
// were on it, or the mount let go without the rider being told. Either leaves a player seated on
// nothing, with their own movement, camera and interactor all switched off by the mount teardown
// that never ran.
namespace SpaceGame.Core.Safety
{
    public static class MountRecoveryRule
    {
        public static bool ShouldDismount(bool haveMount, bool mountAlive, bool mountClaimsRider,
                                          float brokenSeconds, float timeoutSeconds)
        {
            if (!haveMount) return false;
            if (mountAlive && mountClaimsRider) return false;

            // Bounded, because a mount despawning and a rider being released are two events on two
            // machines and they do not arrive in the same frame.
            return brokenSeconds >= timeoutSeconds;
        }
    }
}
```

Write `Assets/Game/Scripts/Core/Safety/Rules/ViewRecoveryRule.cs`:

```csharp
// Whether this machine has stopped drawing anything at all.
//
// A blunt measurement on purpose: not "is the right camera active" — which needs to know about
// mounts, spectators, focus cameras and the terminal — but "is there any enabled camera". Zero is
// unambiguous, and every legitimate state in the game has one: the menu has its own, the loading
// screen has its own, a mounted player has the mount's, a dead player has the spectator's.
//
// It is bounded because a Single scene load legitimately has none for a moment.
namespace SpaceGame.Core.Safety
{
    public static class ViewRecoveryRule
    {
        public static bool ShouldRestoreView(bool anyEnabledCamera, float blindSeconds, float timeoutSeconds)
        {
            if (anyEnabledCamera) return false;
            return blindSeconds >= timeoutSeconds;
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

`Tools ▸ Tests ▸ Run EditMode Tests (headless)`. `SessionGuardRuleTests` must pass; `SessionGuardRunner` still does not compile, so the run will not start until Task 16 — if so, park this verification and repeat it at Task 16 Step 4.

- [ ] **Step 5: Expose the scope's owners**

In `Assets/Game/Scripts/Presentation/UI/Widgets/GameplayMenuScope.cs`, add beside `public static bool IsActive`:

```csharp
        /// <summary>
        /// Everything currently holding this scope open.
        ///
        /// Exposed for <c>StuckScopeGuard</c>, which is the only reader: an owner that was destroyed
        /// or disabled without calling <see cref="Exit"/> leaves the player with a free cursor, no
        /// gameplay input and — solo — a stopped clock, with no way out but quitting. Read-only, and
        /// a caller that acts on it must copy first: <see cref="Exit"/> mutates this set.
        /// </summary>
        public static IReadOnlyCollection<object> Owners => owners;
```

`System.Collections.Generic` is already imported in that file.

- [ ] **Step 6: Write the guard**

Write `Assets/Game/Scripts/Core/Safety/Guards/StuckScopeGuard.cs`:

```csharp
// Gives the game back when a menu that took it has gone.
//
// Seven screens claim GameplayMenuScope, all by passing `this`. Every one of them gives it back in a
// paired call at the end of a method — and every one of those pairs is broken by a throw in between,
// by a destroy, or by a scene load that takes the screen with it. What the player sees is the game
// frozen behind a free cursor with nothing on screen to close.
//
// It measures the outcome, the same bargain UnderTerrainGuard makes: it does not care which of those
// happened, only that somebody is holding the scope who cannot possibly still be using it.
//
// The grace period is not politeness, it is correctness. A screen legitimately spends a frame or two
// disabled during its own open and close animations, and releasing on the first sighting would close
// menus out from under people.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Core.Safety
{
    public sealed class StuckScopeGuard : ISessionGuard
    {
        /// <summary>How long an owner must look abandoned before its claim is dropped.</summary>
        public const float GraceSeconds = 2f;

        private readonly Dictionary<object, float> abandonedFor = new();
        private readonly List<object> release = new();

        public string Name => "StuckScope";

        public void Check(float interval)
        {
            if (!GameplayMenuScope.IsActive)
            {
                abandonedFor.Clear();
                return;
            }

            release.Clear();

            foreach (object owner in GameplayMenuScope.Owners)
            {
                if (!StuckScopeRule.IsAbandoned(owner))
                {
                    abandonedFor.Remove(owner);
                    continue;
                }

                abandonedFor.TryGetValue(owner, out float elapsed);
                elapsed += interval;
                abandonedFor[owner] = elapsed;

                if (elapsed >= GraceSeconds) release.Add(owner);
            }

            // Collected first, released after: Exit mutates the set being walked.
            foreach (object owner in release)
            {
                // An error, not a warning. This guard repairing a session means a real bug happened
                // somewhere else, and the guard must never be the thing that hides it.
                Debug.LogError($"[StuckScopeGuard] '{Describe(owner)}' held the gameplay menu scope " +
                               $"for {GraceSeconds:F0}s after being destroyed or disabled. Releasing it " +
                               "so the player can move again — something failed to call " +
                               "GameplayMenuScope.Exit and that is the real bug.");

                GameplayMenuScope.Exit(owner);
                abandonedFor.Remove(owner);
            }

            release.Clear();
        }

        // The type name, because the instance's own name needs a live object to read it from and by
        // definition there may not be one.
        private static string Describe(object owner) =>
            owner == null ? "null" : owner.GetType().Name;
    }
}
```

- [ ] **Step 7: Continue to Task 14 without committing**

The tree still does not compile until all four guards exist.

---

### Task 14: `InputRestoreGuard` — the "I cannot move" bug

**Files:**
- Create: `Assets/Game/Scripts/Core/Safety/Guards/InputRestoreGuard.cs`
- Modify: `Assets/Game/Scripts/Agents/Modules/Riding/MountModule.Mounting.cs`

- [ ] **Step 1: Give the mount system a local-rider handle**

`InputRestoreGuard` and `MountGuard` both need one question answered: is this machine's player riding something? Nothing answers it today, and asking every `MountModule` in a streamed world twice a second is not affordable.

In `Assets/Game/Scripts/Agents/Modules/Riding/MountModule.Mounting.cs`, add:

```csharp
        /// <summary>
        /// The mount this machine's own player is riding, or null.
        ///
        /// <para>
        /// A static because there is exactly one local player and the alternative — sweeping every
        /// MountModule in a streamed world twice a second — is not affordable. Read by
        /// <c>InputRestoreGuard</c> (which must not hand controls back to a rider who is legitimately
        /// riding) and by <c>MountGuard</c> (which forces a dismount when this points at something
        /// that no longer exists).
        /// </para>
        /// <para>
        /// Deliberately NOT cleared on destroy. A mount destroyed under its rider leaves this holding
        /// a Unity-null, which is precisely the broken state <c>MountGuard</c> looks for; clearing it
        /// would hide the failure rather than fix it.
        /// </para>
        /// </summary>
        public static MountModule LocalRiderMount { get; private set; }

        /// <summary>
        /// Statics survive play-mode exit here, so without this the second play session starts
        /// believing the first session's player is still mounted.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLocalRiderMount() => LocalRiderMount = null;
```

Then set it. Find the point where a local mount completes and the point where a local dismount completes:

```bash
grep -n "RiderIsLocal\|mountedPlayer =" Assets/Game/Scripts/Agents/Modules/Riding/MountModule.Mounting.cs
```

At the end of the successful mount path, after `mountedPlayer` is assigned:

```csharp
            if (RiderIsLocal) LocalRiderMount = this;
```

In `Dismount()` and `DismountAt(Vector3)`, after the rider is released:

```csharp
            if (LocalRiderMount == this) LocalRiderMount = null;
```

- [ ] **Step 2: Write the guard**

Write `Assets/Game/Scripts/Core/Safety/Guards/InputRestoreGuard.cs`:

```csharp
// Hands the player their controls back when nothing is holding them.
//
// The last line of defence behind StuckScopeGuard, and it catches a different set of causes: a
// cutscene action that threw before its ExitCutsceneMode, a mount teardown that never ran, a death
// handler that disabled input and then failed to respawn. All of them look the same to the player —
// the world is there, the camera works, and nothing responds.
//
// It measures the outcome and asks the four legitimate holders directly. It never reads
// PlayerController.InCutsceneMode as the question, because that flag is how every one of those
// holders — including the broken one — expresses itself; reading it would make the guard refuse to
// fire in exactly the case it exists for. See InputRestoreRule.
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Presentation;

namespace SpaceGame.Core.Safety
{
    public sealed class InputRestoreGuard : ISessionGuard
    {
        /// <summary>
        /// How long the controls may be held by nothing before they are handed back.
        ///
        /// Generous on purpose. Input is legitimately off for a moment during every handover, and
        /// this is a repair for a session that is otherwise over — a few seconds of certainty costs
        /// far less than fighting a mount for its own rider.
        /// </summary>
        public const float TimeoutSeconds = 5f;

        private float stuckSeconds;

        public string Name => "InputRestore";

        public void Check(float interval)
        {
            PlayerController player = GameplayMenuScope.FindLocalPlayer();
            if (player == null || player.Input == null)
            {
                stuckSeconds = 0f;
                return;
            }

            bool cutsceneRunning = CutsceneDirector.Instance != null && CutsceneDirector.Instance.IsPlaying;
            bool mounted = MountModule.LocalRiderMount != null;

            stuckSeconds = player.Input.enabled ? 0f : stuckSeconds + interval;

            if (!InputRestoreRule.ShouldRestore(player.Input.enabled, GameplayMenuScope.IsActive,
                                                cutsceneRunning, player.IsDead, mounted,
                                                stuckSeconds, TimeoutSeconds))
                return;

            Debug.LogError($"[InputRestoreGuard] The local player has had no input for " +
                           $"{stuckSeconds:F1}s with no menu, cutscene, death or mount to explain it. " +
                           "Handing the controls back — something took them and did not give them " +
                           "back, and that is the real bug.", player);

            // Through ExitCutsceneMode rather than by writing Input.enabled: that method is the
            // project's existing primitive and it also restores look, movement and the cursor, which
            // a bare flag would leave in whatever state the failure left them.
            if (player.InCutsceneMode) player.ExitCutsceneMode();
            else player.Input.enabled = true;

            stuckSeconds = 0f;
        }
    }
}
```

Confirm the namespaces before writing:

```bash
grep -n "^namespace" Assets/Game/Scripts/Characters/Player/Core/PlayerController.cs Assets/Game/Scripts/Presentation/Cutscenes/Core/CutsceneDirector.cs Assets/Game/Scripts/Agents/Modules/Riding/MountModule.cs
```
Use the namespaces those files actually declare, not the ones guessed here.

- [ ] **Step 3: Continue to Task 15**

---

### Task 15: `MountGuard` — stranded on a mount that is gone

**Files:**
- Create: `Assets/Game/Scripts/Core/Safety/Guards/MountGuard.cs`

- [ ] **Step 1: Write the guard**

Write `Assets/Game/Scripts/Core/Safety/Guards/MountGuard.cs`:

```csharp
// Gets a rider off a mount that no longer exists.
//
// Mounting switches off the rider's movement, camera and interactor, and dismounting switches them
// back on. When the mount goes away without that second half running — despawned by the streamer,
// destroyed on the server, torn down during a Shutdown that NGO does not deparent through — the
// rider is left with everything switched off and nothing to ride.
//
// The recovery is deliberately the game's own Dismount and not a hand-written unwind. Dismount knows
// about the camera, the collision ignores, the pose and the drop point; a second copy of that
// knowledge here would rot the moment either changes.
//
// When the mount is gone entirely there is nothing to call Dismount on. That case falls through to
// InputRestoreGuard, which is why LocalRiderMount is cleared here: the mount reference is what stops
// that guard from firing.
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.Core.Safety
{
    public sealed class MountGuard : ISessionGuard
    {
        /// <summary>
        /// How long a broken attachment is tolerated. A mount despawning and a rider being released
        /// are two events on two machines; they do not arrive in the same frame.
        /// </summary>
        public const float TimeoutSeconds = 3f;

        private float brokenSeconds;

        public string Name => "Mount";

        public void Check(float interval)
        {
            MountModule mount = MountModule.LocalRiderMount;

            // ReferenceEquals for "we hold a reference at all" and Unity's == for "the object behind
            // it still exists". The two disagree exactly when a mount was destroyed under its rider,
            // which is the failure this guard is for.
            bool haveMount = !ReferenceEquals(mount, null);
            bool mountAlive = mount != null;
            bool claimsRider = mountAlive && mount.IsMounted;

            if (!haveMount || claimsRider)
            {
                brokenSeconds = 0f;
                return;
            }

            brokenSeconds += interval;

            if (!MountRecoveryRule.ShouldDismount(haveMount, mountAlive, claimsRider,
                                                  brokenSeconds, TimeoutSeconds))
                return;

            Debug.LogError($"[MountGuard] The local player has been attached to a mount that is " +
                           $"{(mountAlive ? "no longer claiming them" : "destroyed")} for " +
                           $"{brokenSeconds:F1}s. Forcing a dismount — a mount teardown did not run, " +
                           "and that is the real bug.");

            // The real Dismount when there is still an object to run it, so the camera, the
            // collision ignores and the pose are all unwound by the code that set them.
            if (mountAlive) mount.Dismount();

            // Cleared either way. With the mount destroyed there is nothing to unwind, and clearing
            // this is what lets InputRestoreGuard see an unmounted player and hand the controls back.
            MountModule.ClearLocalRiderMount();

            brokenSeconds = 0f;
        }
    }
}
```

- [ ] **Step 2: Add the clear method the guard needs**

`LocalRiderMount` has a private setter, so add beside it in `Assets/Game/Scripts/Agents/Modules/Riding/MountModule.Mounting.cs`:

```csharp
        /// <summary>
        /// Forgets the local rider's mount without unwinding anything.
        ///
        /// Only for <c>MountGuard</c>, and only for the case where the mount object is already
        /// destroyed — there is nothing left to dismount from, and holding the dead reference is what
        /// keeps <c>InputRestoreGuard</c> from handing the player their controls back.
        /// </summary>
        public static void ClearLocalRiderMount() => LocalRiderMount = null;
```

- [ ] **Step 3: Continue to Task 16**

---

### Task 16: `ViewGuard` — the black screen

**Files:**
- Create: `Assets/Game/Scripts/Core/Safety/Guards/ViewGuard.cs`
- Test: `Assets/Game/Editor/Tests/StuckScopeGuardTests.cs`

- [ ] **Step 1: Write the guard**

Write `Assets/Game/Scripts/Core/Safety/Guards/ViewGuard.cs`:

```csharp
// Turns a camera back on when this machine has stopped drawing anything.
//
// Cameras here are handed between owners constantly — the player's own, a mount's orbit camera, the
// spectator on death, the focus camera at a terminal — and every handover is a pair of calls. A
// throw between them leaves every camera off, which the player reads as the game having crashed.
//
// The measurement is Camera.allCamerasCount and nothing cleverer. "Is the right camera active"
// would need to know about mounts, spectators, focus cameras and the terminal, and would be wrong
// the first time somebody adds a fifth. Zero enabled cameras is unambiguous and needs no such list.
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Presentation;

namespace SpaceGame.Core.Safety
{
    public sealed class ViewGuard : ISessionGuard
    {
        /// <summary>How long a black screen is tolerated. A Single scene load legitimately has none.</summary>
        public const float TimeoutSeconds = 3f;

        private float blindSeconds;

        public string Name => "View";

        public void Check(float interval)
        {
            bool anyCamera = Camera.allCamerasCount > 0;

            blindSeconds = anyCamera ? 0f : blindSeconds + interval;

            if (!ViewRecoveryRule.ShouldRestoreView(anyCamera, blindSeconds, TimeoutSeconds))
                return;

            PlayerController player = GameplayMenuScope.FindLocalPlayer();
            if (player == null || player.PlayerCamera == null)
            {
                // Nothing to restore. Say so once per interval rather than going quiet, because a
                // black screen with no local player is a different bug and needs its own report.
                Debug.LogError($"[ViewGuard] No enabled camera anywhere for {blindSeconds:F1}s and no " +
                               "local player to restore one from.");
                blindSeconds = 0f;
                return;
            }

            Debug.LogError($"[ViewGuard] No enabled camera anywhere for {blindSeconds:F1}s. " +
                           "Re-enabling the player camera — a camera handover did not complete, and " +
                           "that is the real bug.", player);

            player.PlayerCamera.gameObject.SetActive(true);
            blindSeconds = 0f;
        }
    }
}
```

- [ ] **Step 2: Write the guard test**

Write `Assets/Game/Editor/Tests/StuckScopeGuardTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Core.Safety;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The scope guard's patience, exercised by marching its interval forward by hand — this project
    /// has no play-mode tests, and the guard reads its elapsed time from the caller for exactly that
    /// reason.
    ///
    /// The release path itself is not covered here: GameplayMenuScope.Enter refuses without a local
    /// player, and building one takes a three-scene rig (MultiplayerTestPlayerBuilder). What is
    /// covered is the part that decides, which is the part that can be wrong.
    /// </summary>
    public class StuckScopeGuardTests
    {
        [Test]
        public void AnIdleGuardDoesNothingWhenNoScopeIsHeld()
        {
            var guard = new StuckScopeGuard();

            Assert.DoesNotThrow(() => guard.Check(1f));
            Assert.DoesNotThrow(() => guard.Check(60f));
        }

        [Test]
        public void TheGraceIsLongerThanASingleSweep()
        {
            Assert.Greater(StuckScopeGuard.GraceSeconds, SessionGuardRunner.CheckIntervalSeconds,
                           "a grace shorter than one sweep would release on first sighting, which " +
                           "closes menus out from under people mid-animation");
        }

        [Test]
        public void EveryGuardTimeoutOutlastsASweep()
        {
            Assert.Greater(InputRestoreGuard.TimeoutSeconds, SessionGuardRunner.CheckIntervalSeconds);
            Assert.Greater(MountGuard.TimeoutSeconds, SessionGuardRunner.CheckIntervalSeconds);
            Assert.Greater(ViewGuard.TimeoutSeconds, SessionGuardRunner.CheckIntervalSeconds);
        }
    }
}
```

- [ ] **Step 3: Type-check**

```bash
python3 tools/typecheck.py --editor
```
Expected: `Assembly-CSharp: no errors.` and `Assembly-CSharp-Editor: no errors.` This is the first point since Task 12 where the tree compiles.

- [ ] **Step 4: Run the full suite**

`Tools ▸ Tests ▸ Run EditMode Tests (headless)`, then `cat Temp/headless_tests.txt`. Expected: `FAILED=0` — including `SessionGuardRuleTests` from Task 13.

- [ ] **Step 5: Commit the whole of Phase 3**

```bash
git add Assets/Game/Scripts/Core/Safety Assets/Game/Scripts/Presentation/UI/Widgets/GameplayMenuScope.cs Assets/Game/Scripts/Agents/Modules/Riding/MountModule.Mounting.cs Assets/Game/Editor/Tests/SessionGuardRuleTests.cs Assets/Game/Editor/Tests/StuckScopeGuardTests.cs
git commit -m "feat: add session guards for stuck scope, lost input, dead mount and black screen"
```

- [ ] **Step 6: Verify by hand, in the running game**

Press Play, enter a world, and check the console for `SessionGuardRunner` bootstrapping without errors. Then force each failure and confirm the recovery:

1. Open the backpack (focus mode), and from the Editor disable the `PackFocusSession` component while it is open. Within ~2.5 s the console logs `[StuckScopeGuard]` and the cursor re-locks.
2. With the game running, set `PlayerController.Input.enabled = false` from the Inspector. Within ~5.5 s the console logs `[InputRestoreGuard]` and movement returns.
3. Mount an animal, then delete the animal's GameObject from the Hierarchy. Within ~3.5 s the console logs `[MountGuard]`, then `[InputRestoreGuard]` hands control back.
4. Disable every camera in the scene. Within ~3.5 s the console logs `[ViewGuard]` and the picture returns.

All four must log an **error**, not a warning. If any recovers silently, fix that before continuing — a silent guard is how the underlying bug ships.

---

## Phase 4 — observability, proof, docs

### Task 17: Feed unhandled errors into the ledger

Barriers catch what they wrap. Everything else — a throw in an `Update`, in an `OnDestroy`, inside Netcode — still logs and is still invisible in a bug report. Nothing in `Assets/Game` hooks `Application.logMessageReceived` today.

**Files:**
- Create: `Assets/Game/Scripts/Core/DiagnosticsBridge/FaultLogSink.cs`

- [ ] **Step 1: Write the sink**

Write `Assets/Game/Scripts/Core/DiagnosticsBridge/FaultLogSink.cs`:

```csharp
// Everything that goes wrong outside a barrier, captured so it can be reported.
//
// The barriers only see what they wrap. A throw in an Update, an OnDestroy, or inside Netcode still
// happens, still breaks something, and still leaves nothing behind except a console the player
// cannot read. This puts those in the same ledger, so /faults answers the real question — "what went
// wrong in that session" — rather than "what went wrong in the places we thought to guard".
//
// Errors and exceptions only. Warnings are routine here (unregistered relays, unreplicated actions)
// and would bury the thing being looked for.
//
// It does not throw its own errors back into the sink. The handler is re-entrant by construction —
// Debug.LogError inside a log callback calls the callback again — so anything it might log would
// loop until the stack ran out.
using System;
using UnityEngine;
using SpaceGame.Diagnostics;

namespace SpaceGame.Core
{
    public static class FaultLogSink
    {
        /// <summary>Prefix on the messages the barrier itself writes; they are already in the ledger.</summary>
        private const string BarrierPrefix = "[Fault]";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            // Statics survive play-mode exit here, so a second play session would otherwise install
            // a second handler and record everything twice.
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;

            // The barrier has already written a richer record for these, with the owner and the site
            // on it. Recording them again would double every guarded fault in the ledger.
            if (condition != null && condition.StartsWith(BarrierPrefix, StringComparison.Ordinal)) return;

            FaultLedger.Add(new FaultRecord(
                site: type.ToString(),
                owner: SessionContext(),
                detail: Trim(condition) + " | " + Trim(stackTrace),
                count: 1,
                quarantined: false,
                time: Time.realtimeSinceStartup));
        }

        /// <summary>
        /// Who this machine was when the error happened. Host and client see different bugs — and
        /// most of what breaks a session here is a race between them — so a record that does not say
        /// which side it came from is half a report.
        /// </summary>
        private static string SessionContext()
        {
            if (!Network.IsNetworked) return "offline";
            return Network.Server ? $"host:{Network.LocalClientId}" : $"client:{Network.LocalClientId}";
        }

        // Bounded because a stack trace is unbounded, the ledger holds 64 of these, and this is
        // going into a chat line and a text file, not a debugger.
        private static string Trim(string text)
        {
            const int limit = 400;
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = text.Replace('\n', ' ').Replace('\r', ' ');
            return text.Length <= limit ? text : text.Substring(0, limit) + "…";
        }
    }
}
```

Confirm the network accessors before writing:

```bash
grep -n "public static bool Server\|public static bool IsNetworked\|public static ulong LocalClientId" Assets/Game/Scripts/Core/Multiplayer/Authority/Network.cs
```
Use the members that file actually exposes.

- [ ] **Step 2: Type-check and commit**

```bash
python3 tools/typecheck.py --editor
git add Assets/Game/Scripts/Core/DiagnosticsBridge/FaultLogSink.cs
git commit -m "feat: capture unhandled errors into the fault ledger with session context"
```

---

### Task 18: `/faults dump` — a one-command bug report

**Files:**
- Create: `Assets/Game/Scripts/Core/DiagnosticsBridge/FaultReport.cs`
- Modify: `Assets/Game/Scripts/Core/DiagnosticsBridge/FaultChatBridge.cs`

- [ ] **Step 1: Write the report writer**

Write `Assets/Game/Scripts/Core/DiagnosticsBridge/FaultReport.cs`:

```csharp
// Writes the session's faults to a file a player can attach to a bug report.
//
// This exists because the bugs this whole system is about are network races, and a race is not
// reproducible on demand. When you cannot reproduce, you invest in observability instead — which
// here means making the evidence a single command rather than "could you find your Player.log".
using System;
using System.IO;
using System.Text;
using UnityEngine;
using SpaceGame.Diagnostics;

namespace SpaceGame.Core
{
    public static class FaultReport
    {
        /// <summary>
        /// Writes the ledger to <c>Application.persistentDataPath</c> and returns the path, or an
        /// empty string when it could not be written.
        /// </summary>
        public static string Write(out string problem)
        {
            problem = null;

            var text = new StringBuilder();
            text.AppendLine("SpaceGame fault report");
            text.AppendLine($"written   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            text.AppendLine($"unity     {Application.unityVersion}");
            text.AppendLine($"platform  {Application.platform}");
            text.AppendLine($"faults    {FaultLedger.TotalFaults} total, {FaultLedger.Recent.Count} kept");
            text.AppendLine();

            foreach (FaultRecord record in FaultLedger.Recent)
                text.AppendLine(record.ToString());

            string path = Path.Combine(Application.persistentDataPath,
                                       $"faults-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

            try
            {
                File.WriteAllText(path, text.ToString());
                return path;
            }
            catch (IOException e)
            {
                problem = e.Message;
            }
            catch (UnauthorizedAccessException e)
            {
                problem = e.Message;
            }

            // No catch-all. A failure this does not name is a failure somebody needs to see, and
            // swallowing it here would leave a player believing a report exists that does not.
            return string.Empty;
        }
    }
}
```

- [ ] **Step 2: Wire it to the command**

In `Assets/Game/Scripts/Core/DiagnosticsBridge/FaultChatBridge.cs`, change the registration usage line:

```csharp
            ChatCommands.Register("faults", "/faults [dump]",
                                  "List the features that have faulted this session, or write a report file.",
                                  List, "errors");
```

and add this at the top of the `List` handler, before the `TotalFaults == 0` test:

```csharp
            if (args.Length > 0 && string.Equals(args[0], "dump", StringComparison.OrdinalIgnoreCase))
            {
                string path = FaultReport.Write(out string problem);
                return string.IsNullOrEmpty(path)
                    ? $"Could not write the report: {problem}"
                    : $"Wrote {FaultLedger.TotalFaults} fault(s) to {path}";
            }
```

Add `using System;` to that file's usings.

- [ ] **Step 3: Verify by hand**

Press Play, enter a world, open chat and type `/faults`. Expected: `No faults this session.` Then trigger a fault (disable a `PackFocusSession` while focus mode is open, as in Task 16 Step 6), and type `/faults` again. Expected: one line naming `StuckScope`. Then `/faults dump`, and:

```bash
ls -la ~/Library/Application\ Support/*/SpaceGame/faults-*.txt
```
Expected: the file the command named exists and contains the same line.

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Scripts/Core/DiagnosticsBridge
git commit -m "feat: write a fault report file from /faults dump"
```

---

### Task 19: Prove it across two machines

Host-only verification proves nothing in this project. The two-process batch-mode autotest is the only real proof, and the claim being proved here is precise: **a client keeps playing when the host's code throws.**

**Files:**
- Create: `Assets/Game/Scripts/Core/Multiplayer/Autotest/AutotestRunner.Faults.cs`

- [ ] **Step 1: Read how the existing probes report**

```bash
sed -n '1,60p' Assets/Game/Scripts/Core/Multiplayer/Autotest/AutotestRunner.Client.cs
grep -n "Report(" Assets/Game/Scripts/Core/Multiplayer/Autotest/AutotestRunner.Client.cs | head
```
Every probe writes one `[MPTEST]` line through `Report(key, value)`. Extend that; do not build a second harness.

- [ ] **Step 2: Write the fault probe**

Write `Assets/Game/Scripts/Core/Multiplayer/Autotest/AutotestRunner.Faults.cs`:

```csharp
// Proves the degradation contract across two machines: a host whose gameplay code throws must not
// take the client's session with it.
//
// Host-only verification proves nothing here — a perfect host and blind clients is the failure mode
// this whole project keeps hitting — so the claim is checked from both logs. The host deliberately
// breaks one thing; the client must still be spawned, still have its player object, and still be
// receiving messages afterwards.
using UnityEngine;
using SpaceGame.Diagnostics;

namespace SpaceGame.Core
{
    public partial class AutotestRunner
    {
        /// <summary>Faults injected before the check, so the numbers below are not accidental.</summary>
        private const int InjectedFaults = 3;

        /// <summary>
        /// Host side: throw from inside a barrier several times and report that the session is still
        /// alive afterwards.
        /// </summary>
        private void ProbeFaultContainment()
        {
            var victim = new GameObject("FaultProbeVictim");

            int contained = 0;
            for (int i = 0; i < InjectedFaults; i++)
                if (!Fault.Run(victim.transform, "Autotest.Inject",
                               () => throw new System.InvalidOperationException("injected")))
                    contained++;

            Report("HOST_FAULTS_CONTAINED", contained);
            Report("HOST_FAULTS_LEDGERED", FaultLedger.TotalFaults >= InjectedFaults);
            Report("HOST_ALIVE_AFTER_FAULTS", Network.IsNetworked && Network.Server);

            Object.Destroy(victim);
        }
    }
}
```

Match the partial class name, namespace and `Report` signature to `AutotestRunner.Client.cs` exactly — if `Report` takes `(string, object)` there, use that; if the class is `AutotestRunner` in a different namespace, use that one.

- [ ] **Step 3: Call it from the host sequence**

In `Assets/Game/Scripts/Core/Multiplayer/Autotest/AutotestRunner.Host.cs`, add `ProbeFaultContainment();` beside the other host probes, at the same point in the sequence.

- [ ] **Step 4: Build the test player and run both processes**

In the Unity Editor: `Tools ▸ Tests ▸ Build Multiplayer Test Player`, then `Tools ▸ Tests ▸ Print Multiplayer Test Commands` and use the paths it prints. Then:

```bash
"<app>/Contents/MacOS/SpaceGameMP" -batchmode -nographics -sgmode host   -logFile /tmp/mp_host.log &
"<app>/Contents/MacOS/SpaceGameMP" -batchmode -nographics -sgmode client -logFile /tmp/mp_client.log &
```

Wait for both to finish, then:

```bash
grep '\[MPTEST\]' /tmp/mp_host.log /tmp/mp_client.log
```

Expected, across **both** logs:

- `HOST_FAULTS_CONTAINED=3`
- `HOST_FAULTS_LEDGERED=True`
- `HOST_ALIVE_AFTER_FAULTS=True`
- and every pre-existing assertion still holding: `HOST_CLIENTS=2`, `CLIENT_SPAWNED > 0`, `CLIENT_PLAYER_OBJECT=True`, `CLIENT_SUPPRESSED == CLIENT_AUTHORITIES`, `CLIENT_HEALTH_SEEN == HOST_HEALTH_AFTER`, `HOST_RELAY_FROM_CLIENT=1`.

The last group is the actual proof: the client was unaffected by three faults on the host.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Core/Multiplayer/Autotest
git commit -m "test: prove a host-side fault does not break the client's session"
```

---

### Task 20: Documentation

Required by [CLAUDE.md](../../../CLAUDE.md): every change to behaviour updates its doc in the same commit, and a new system needs a doc plus a plain-language entry.

**Files:**
- Create: `docs/AI/systems/Diagnostics.md`
- Modify: `docs/AI/INVARIANTS.md`
- Modify: `docs/Human/the-systems.md`
- Modify: `docs/AI/systems/AgentSystem.md`, `Artifacts.md`, `InteractionSystem.md`, `Vehicles.md`, `UI.md`, `Cutscenes.md`

- [ ] **Step 1: Write the system doc**

Write `docs/AI/systems/Diagnostics.md` with this frontmatter, then the seven required sections in order (**Model → Key types → Flows → Multiplayer → Persistence → Gotchas → Extending**):

```yaml
---
system: Diagnostics
layer: core
summary: One fault barrier, per-site quarantine, and outcome guards that give a stuck session back
paths:
  - Assets/Game/Scripts/Core/Diagnostics/
  - Assets/Game/Scripts/Core/DiagnosticsBridge/
  - Assets/Game/Scripts/Core/Safety/
symptoms:
  - "a feature stopped working mid-session and the chat says it was switched off"
  - "the game is frozen behind a free cursor with no menu on screen"
  - "I cannot move but the camera still works and nothing is on screen"
  - "the screen is black and the game seems to have crashed but the audio is still running"
  - "I am stuck on a mount that is not there any more"
  - "one broken creature behaviour stopped the creature moving at all"
  - "how do I get a bug report out of a playtest"
reads_with: [Multiplayer, AgentSystem, UI, Testing]
updated: 2026-09-09
---
```

Content requirements, one paragraph each:

- **Model** — the four failure classes (per-frame throw, dead coroutine, leaked lock, divergence), and the two answers: barriers at fan-out seams, guards on outcomes.
- **Key types** — `Fault`, `FaultBudget`, `FaultLedger`, `FaultRecord`, `IQuarantinable`, `ISessionGuard`, `SessionGuardRunner`, and the four rule/guard pairs, each with its file path.
- **Flows** — a fault from throw to chat line; a coroutine death to its teardown; a leaked scope to its release.
- **Multiplayer** — barriers run on every machine; guards run on the machine that owns the thing (the same reason `UnderTerrainGuard` runs on the owner, not the server); and the standing rule that damage/ownership/spawn abort rather than degrade.
- **Persistence** — `N/A — the ledger is deliberately per-session and is never saved. A fault from three worlds ago is noise in a bug report, and a save file is not a log.`
- **Gotchas** — at minimum: `SpaceGame.Diagnostics` has an empty reference list on purpose and must never gain one; a barrier around a state change that must not half-happen is a bug, not a safety net; `InputRestoreRule` must not read `InCutsceneMode`; a guard that recovers without logging an error hides the real bug; every guard timeout must outlast one `SessionGuardRunner` sweep.
- **Extending** — how to add a barrier (find the fan-out point, one site name per independent failure), and how to add a guard (write the pure rule first, then the reader, then add it to `SessionGuardRunner.Awake`).

- [ ] **Step 2: Add the invariant**

Add a section to `docs/AI/INVARIANTS.md`, in the same shape as the others (**The rule / Why / How it fails / Where**):

```markdown
## Degrade at the seams, abort at the decisions

**The rule.** A loop that invokes N independent plug-ins wraps each one in `Fault.Run` and carries on
with the rest. A single decision whose failure would leave state half-applied does **not** — it aborts.
Every coroutine that takes something (input, the cursor, a camera, a menu scope) goes through
`Fault.Coroutine` with the teardown that gives it back.

**Why.** Unity's own per-message catch is not enough: it skips the rest of that method, so a throw
partway through a loop starves every plug-in below it, and a throw in a coroutine kills the coroutine
permanently, mid-way, with whatever it took still taken. But a barrier in the wrong place is worse
than none — a half-applied damage or ownership change is how a session diverges and stays diverged.

**How it fails.** One broken creature behaviour stops the creature moving at all; a broken muzzle
flash stops the shot; a cutscene that throws leaves the player with a free cursor, no input and no
way out but quitting.

**Where.** [Diagnostics](systems/Diagnostics.md) · [AgentSystem](systems/AgentSystem.md) · [Artifacts](systems/Artifacts.md) · [Cutscenes](systems/Cutscenes.md) · [NetChannel.cs](Assets/Game/Scripts/Core/Multiplayer/Messaging/NetChannel.cs)
```

- [ ] **Step 3: Add the plain-language entry**

Add a short entry for Diagnostics to `docs/Human/the-systems.md`, in the style of the entries already there. The validator fails without it. Roughly: *when something in the game breaks, the broken part switches itself off and says so in chat, instead of taking everyone's session down with it — and if the game ever leaves you frozen, unable to move, or staring at a black screen, it notices and gives itself back.*

- [ ] **Step 4: Update the docs of every system this plan touched**

For each of `AgentSystem.md`, `Artifacts.md`, `InteractionSystem.md`, `Vehicles.md`, `UI.md` and `Cutscenes.md`: add one `## Gotchas` line naming the new behaviour, add `Diagnostics` to `reads_with`, and bump `updated:` to `2026-09-09`. Examples of the line to add:

- `AgentSystem.md` — "A module that throws is quarantined after 5 faults in 10 s and its `enabled` is set false, so `IsActive` goes false and the controller stops ticking it. The creature keeps its other behaviours. See [Diagnostics](Diagnostics.md)."
- `Artifacts.md` — "`PlayUse` and `TryUse` fail independently. A broken presentation does not stop the effect and vice versa, and the server still broadcasts `ItemUsed` even when its own copy of the effect threw — a peer that never hears the message diverges permanently."
- `UI.md` — "`GameplayMenuScope.Owners` is public for `StuckScopeGuard`, which releases a claim whose owner has been destroyed or disabled for 2 s. Anything acting on that collection must copy it first: `Exit` mutates it."

- [ ] **Step 5: Regenerate and validate**

```bash
python3 tools/docs_check.py --index
```
Expected: it regenerates `INDEX.md` and `ROUTING.md`, then validates and reports no errors. Never hand-edit those two files.

- [ ] **Step 6: Commit**

```bash
git add docs
git commit -m "docs: document the diagnostics and session-guard system"
```

---

## Not in scope, and why

State these when handing the work over — they are deliberate omissions, not gaps:

- **The save path gains no new barriers.** A caught corruption that then persists is worse than a crash. `SaveFileStore` already fails loudly and keeps the previous file; that stays.
- **Damage, ownership and spawning are never barriered.** They abort instead. A half-applied authoritative change diverges the session permanently, which is worse than the throw.
- **No blanket try/catch anywhere.** [CLAUDE.md](../../../CLAUDE.md) bans silent catches, and indiscriminate barriers destroy the traceability that makes a fault fixable at all.
- **No telemetry leaves the machine.** `/faults dump` writes a local file the player chooses to send.

## Verification checklist for the whole plan

- [ ] `python3 tools/typecheck.py --editor` — no errors in either assembly.
- [ ] `Tools ▸ Tests ▸ Run EditMode Tests (headless)` → `Temp/headless_tests.txt` shows `FAILED=0`.
- [ ] The four hand-verifications in Task 16 Step 6 each log an **error** and each recover.
- [ ] The two-process run in Task 19 Step 4 reports `HOST_FAULTS_CONTAINED=3` and every pre-existing client assertion still holds.
- [ ] `python3 tools/docs_check.py --index` validates clean.
- [ ] `git status` is clean and every commit above exists.
