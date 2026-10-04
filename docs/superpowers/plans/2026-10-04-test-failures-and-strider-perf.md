# Post-merge test failures and Strider/NPC performance: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix every EditMode failure left after merging `main` into `Feat/create-factions` (merge `f64a3906`). Then make the Strider city and NPC crowds cheaper, in the order the profiler ranks them.

**Architecture:** Part A is a set of independent fixes, one per root cause. Each one fixes the side that is wrong (test, builder, runtime, or prefab data rebuilt by its builder). Part B starts by adding `ProfilerMarker`s and a capture protocol, because nothing in the codebase is instrumented. Each optimisation task after that is gated on, and ordered by, measured cost (GDC-L1-PERF-0001: measure first. GDC-L1-PERF-0003: fix the top cost, then re-profile).

**Tech Stack:** Unity 6000.3, C#, NGO 2.9.1, NUnit EditMode tests, Unity Profiler (`Unity.Profiling.ProfilerMarker`, which is in CoreModule and needs no asmdef reference).

**Spec:** this file. The diagnosis below is the spec: it records each failure, its root cause and the evidence for it.

## Global Constraints

- CLAUDE.md applies to every task. Every behaviour change updates its governing `docs/AI/systems/*.md` in the same commit. After a doc edit, bump `updated:` and run `python3 tools/docs_check.py --index`. Never hand-edit `INDEX.md` or `ROUTING.md`.
- **Do NOT touch** (another session owns them): `Assets/Game/Scripts/Vehicles/Monowheel/*`, `Assets/Game/Editor/Vehicles/MonowheelPresentationBuilder.cs`, `Assets/Game/Editor/Tests/Monowheel*`, `Assets/Game/Art/Shaders/Effects/JetSmoke.shader`, `docs/AI/systems/Monowheel.md`, `docs/AI/systems/Vehicles.md`, `Assets/Game/Art/Models/_Source~/models/vehicles/strider1.blend`. Where a doc row really belongs in `Vehicles.md`, put it in the other governing doc named in the task. Leave a note in the commit message saying so.
- Edit the builder, never the asset it writes (INVARIANTS: "Edit the builder, never the asset it writes"). Prefab changes go in builders and are then rebuilt from the Tools menu. The one exception is `RigWalker.prefab`, which is hand-authored and has no builder.
- No magic numbers: every tunable is a `[SerializeField]` with a `[Tooltip]`. A new serialized field reaches existing prefabs only through the class initializer or a rebuild (INVARIANTS: "A serialized field keeps its old value"). Pin it with a test that reads the asset.
- Tooling, run from `C:\Users\tobia\AppData\Local\Temp\claude\C--Users-tobia-Documents-spaceGame-SpaceGame\ab2a1a7b-473e-4041-a764-18ee3b609e3d\scratchpad` after `export PYTHONIOENCODING=utf-8`:
  - `py rt.py <FixtureRegex>...` runs EditMode fixtures. It prints at most 25 failures, so run one fixture at a time when a fixture fails a lot.
  - `py ux.py < file.cs` runs a C# method body in the editor.
  - `python3 tools/typecheck.py --editor` compiles without Unity.
- **Before every test run**, check `EditorApplication.isPlaying` through `ux.py`. If the editor is in Play Mode, the user is testing: wait, and never stop Play Mode yourself. Run targeted fixtures only, never the whole 4850-test suite.
- Never write files from `execute_code`.
- Commits end with `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`. Do not commit unless the user has asked for commits for this plan.

## Diagnosis (Part A spec)

The full run's failure list was **capped at 25**, sorted alphabetically: it stops at `HogtieTests.ATieRefusesABodyThatIsStillOnItsFeet`. Chunked re-runs found more failures after that point. Chunks not yet re-run are listed in Task A0.

| # | Failing tests | Root cause (evidence) | Class |
|---|---|---|---|
| 1 | `CharacterActionsPlaybackTests` ×4 (NRE) | The test calls `CharacterActions.Update` by reflection: `GetMethod("Update", NonPublic)`. On **main**, commit `8e7ef62a` renamed it to `LateUpdate` (`CharacterActions.cs:297`), so `GetMethod` returns null and `.Invoke` throws. The NRE comes straight from the test, not wrapped in a `TargetInvocationException`. The test and runtime are identical on main and HEAD. | (b) pre-existing on main; (c) the test is out of date |
| 2 | `ClankerPrefabTests.CarriesExactlyTheClankersComponents` ×3 (59 vs 65) | The merge added `CharacterActionWiring.Ensure(root)` to `ClankerStack.Apply` (`ClankerStack.cs:303`). It adds six components to bodies that wear `Humanoid.controller`: CharacterActions, BodyLanguage, IdleVariation, HurtReaction, SpeechGestures, MeleeDefense. Checked in the editor: PatrolRobot 1 `WearsHumanoidController=True` with 65 components; RPR `Clanker.prefab` `=False` with 59. The test compares against the RPR Clanker, which correctly gets no wiring. **The prefab is right; the test predates the merge.** | (a) merge, test side. Combined behaviour: Clanker stack plus humanoid wiring when humanoid |
| 3 | `HogtieTests` ×13 (all 17 in the fixture minus the refusals that pass anyway), `NetGunTests` ×25+ | `RagdollController.cs:69-72`: `// TEMPORARY (2026-09-25): every ragdoll switched off to find out whether ragdolls cause the current lag … RagdollsDisabled = true`. It makes `HoldDown`, `KnockHere`, `OnKnockdown` and the death ragdoll return early. Committed on the branch in `94275fc6`; not on main. **Proof:** with the flag set to false by reflection in the editor, HogtieTests 17/17 passed and NetGunTests plus every fixture matching `Ragdoll`/`Knock` passed. | Branch-side debug leftover (pre-merge, not main). Needs a user decision (Task A3) |
| 4 | `AgentCarryTests.Boost_TwoSecondsOfThrust…`, `Drop_MarkingAfterTheFall…` | The **test helpers** are wrong; `AgentCarry.Fall` is right. `Drop(markBeforeTheFall:false)` returns `(y - lastY)/Dt` right after `lastY = y`, so it is always 0. `Boost` assigns the burnout speed to `velocity` at `i == burnSteps-1`, then keeps looping to `burnSteps*8` and overwrites it, returning terminal velocity (-30). Both files are unchanged since 2026-09-13 and identical on main. | (b) pre-existing on main; test bug |
| 5 | `ArmAimTests.TheSwingStopsAtTheShoulderLimit` (102 vs <41) | `ArmAim.Point` (`ArmAim.cs:74-84`) clamps **each stage** to `maxDegrees`. The result is exactly 102 = shoulder 0.55×40 (22) + elbow pass 40 + elbow pass 40. The limit is never applied to the total swing. Code and test are identical on main since 2026-09-13. | (b) pre-existing on main; runtime bug (an arm can swing through the chest) |
| 6 | `DuneBargePrefabTests.EverySetDownSpotHasRoomForThePlayer` ×3 | `DuneBargeBuilder.AddLeafCollider` (`:284`) fits an **oriented box** to each door or lid leaf. The Blender exporter (`dune_barge_export.py`, `Obstacles`, `:516-575`) slides the set-down spots clear of each lid's **convex hull**, and the box is bigger than the hull. Two ladder exits and two hatch outer marks per variant overlap a shut lid's box by a few cm. **Proof:** in a preview scene, replacing every `LeafCollider` box with a convex `MeshCollider` of the leaf mesh left 0 of 8 spots blocked in all three variants. The builder, prefabs and test all come from branch commit `94275fc6`; the merge changed none of them. | Branch-side, pre-merge (not on main) |
| 7 | `SkyTransportPrefabTests.AFlownVesselIsNotARagdollBody` | The merge combined two fixes for the same bug in `RagdollWiring`. The branch added `IsBody` (excludes `VesselPilot`); main extended `IsVehicle` with `/Prefabs/Vehicles/`. The test's counter-case path `Assets/Game/Prefabs/Vehicles/Sky/Transport.prefab` is now excluded by the folder rule, so "health alone is a body" is false. Both guards are wanted. | (a) merge; test side |
| 8 | `SkyTransportPrefabTests.TheVesselCarriesNoSaversAndNoRagdoll` ×2 | The merge took main's `SkySkiffTransport`/`SkyFreighterTransport.prefab`. Main commit `9c5c2c73` (2026-09-21) had run a saver wiring pass that added `SaveableEntity`, `TransformSaveable`, `HealthSaveable` and `EntityFactionSaveable`. The branch's `SaveablePolicy.NeedsSaving` now refuses `VesselPilot` hulls (`SaveablePolicy.cs:83`), so a rebuild produces clean prefabs. Merge-base had no savers; P1 had none. | (a) merge; data side (main's prefab YAML won) |
| 9 | `HumanoidWiringAssetTests.EveryAnimatorStringOnAPrefabNamesAParameterItsControllerHas` | Main added this test and ran `Wire Humanoid Prefabs` over main's prefabs. The branch-only prefabs were never wired: `StriderNomad_*` keep `hurtAnimTrigger: Hurt` / `dieAnimTrigger: Death` and have 0 `CharacterActions`. `NomadPrefabBuilder` never calls `CharacterActionWiring.Ensure`, unlike `ClankerStack` and `SculptCharacterBuilder:1258`. A rebuild would also strip the wiring from the Sand and Sky nomads. The full list of failing prefabs is pending Task A0. Candidates by grep: the 4 `StriderNomad_*` and `StriderCrabOutrider`. | (a) merge; builder side |
| 10 | `LassoTests` ×3: "Unhandled log message … [FMOD] RuntimeManager accessed outside of runtime" | `Sfx.Play` (`Audio/Sfx.cs:126-152`) reaches `RuntimeManager` in EditMode. FMOD logs an **error** there, and NUnit fails on an unexpected error log. Sfx's `catch` only turns the follow-on exception into a warning. `Sfx.cs`, `AudioCatalog.asset`, `LassoTests.cs` and every `Lasso/*.cs` are identical on main. Main's `LassoArtifact.cs` won the merge. | (b) pre-existing on main (inferred: every input is identical; main was not run) |
| 11 | `LassoTests.RopeStaysSmoothWhileBeingThrown` (64.95° vs <8°) | Pure `LassoRope` maths, last changed 2026-09-07, identical on main. **Root cause not yet found.** The test comment says it measured 4.6° with bend resistance on and 11.2° with it off, so 65° means the rope is folding outright, not that bend resistance is missing. | (b) pre-existing on main; investigate (Task A10) |

Not failures, but worth knowing:
- During diagnosis, `RagdollsDisabled` was flipped by reflection and then flipped back. Mono had already JIT-folded the `static readonly` value, so the editor kept behaving as if ragdolls were on until it was restarted. The editor restarted cleanly at 17:40, so its state now matches the code again. Chunks `EditorTools.N` and `EditorTools.[O-R]` ran in the contaminated domain and must be re-run (Task A0).
- After that restart the MCP HTTP server did not come back (connection refused on `localhost:8080`). Task A0 needs it running.

## Review Focus

1. **A ragdoll that is switched back on is a perf regression the user cannot see in tests.** The flag was a perf experiment. Task A3 must not remove it until Task B1's capture has measured ragdoll cost with the flag on and off.
2. **Seated crew who can suddenly see and shoot.** Task B4 skips the carrier's colliders, so crew who were blind on their own house will start firing from it. That is a gameplay change and must be checked in Play Mode, not just in tests.
3. **Clients:** every presentation-side change (ground-conform park, network-transform space, renderer settings) has to work on a client. NPC seating runs on the server, but `VesselSeats.RefreshPresented` runs on every machine. Verify on an MPPM client (INVARIANTS: "Verify on a client").
4. **Rebuilt prefabs losing hand state.** A3/A6/A7/A8/B8/B9 rebuild prefabs. After each rebuild, re-read from disk that the GUID is unchanged, that the `NetworkObject` hash is non-zero, and that `SaveableEntity.PrefabId` is present where the design wants it (INVARIANTS: "Assume nothing throws — read the write back").
5. **NavPathFollower stale routes.** Task B6 lengthens repath for still targets. An agent pushed off its route (a ragdoll, or a walker stepping on it) must still recover. B6 includes that test.

---

# Part A: test failures

**Parallelism:** A1, A2, A4, A5, A7, A8 and A9 touch disjoint files and can run as parallel subagents. A3 and A10 need a user decision or investigation first. A6 shares `NomadPrefabBuilder.cs` with B8/B9, so run A6 before those. A0 only runs tests. Run it first, because it may add tasks.

### Task A0: Finish the failure census

**Files:** none (read-only; results go into this plan's Diagnosis table).

- [ ] **Step 1:** Make sure the MCP server is up: `py ux.py < chk.cs` should print `playing=False`. `chk.cs` is `return "playing=" + UnityEditor.EditorApplication.isPlaying;`. If you get "connection refused", ask the user to start the MCP server in the editor.
- [ ] **Step 2:** Run the chunks the capped list never reached, plus the two that ran in a contaminated domain. `rt4.py` in the scratchpad checks Play Mode before each chunk and reports `failures_capped`:
  ```bash
  py rt4.py '^SpaceGame\.EditorTools\.N' '^SpaceGame\.EditorTools\.[O-R]' '^SpaceGame\.EditorTools\.S[l-z]' \
            '^SpaceGame\.EditorTools\.[T-Z]' '^SpaceGame\.Tests\.[A-L]' '^SpaceGame\.Tests\.[M-Z]' '^SpaceGame\.World' > chunks2.txt
  ```
  If a chunk prints `capped True`, split it by letter and run it again.
- [ ] **Step 3:** Get the full `HumanoidWiringAssetTests` list. The test prints one line per prefab, but the runner truncates the message. Run `c6.cs` from the scratchpad, which re-implements the test's loop and prints every offending prefab.
- [ ] **Step 4:** For each new failure, record its root cause and class here and add a task in the same shape as the ones below. Expected: NetGunTests fails again in `N` (root cause #3).

### Task A1: CharacterActionsPlaybackTests calls the method that exists

**Files:**
- Modify: `Assets/Game/Editor/Tests/CharacterActionsPlaybackTests.cs:142-153` (`Advance`, `Call`)

- [ ] **Step 1: Confirm the failure.** `py rt.py CharacterActionsPlaybackTests` → 4 FAIL with NRE.
- [ ] **Step 2: Fix the test.** `Advance` steps the Animator and then calls the component. That is the real frame order: the Animator evaluates between Update and LateUpdate. Name the method by its compile-time name, so the next rename fails to compile instead of throwing at runtime. The method is private, so use a constant plus a guard:
  ```csharp
  private const string PerFrameMethod = "LateUpdate";   // CharacterActions reads the Animator after it evaluates

  private void Advance(float seconds)
  {
      for (float t = 0f; t < seconds; t += Step)
      {
          animator.Update(Step);
          Call(PerFrameMethod);
      }
  }

  private void Call(string method)
  {
      MethodInfo info = typeof(CharacterActions).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.IsNotNull(info, $"CharacterActions has no {method} any more; this fixture steps it by hand and must follow the rename");
      info.Invoke(actions, null);
  }
  ```
- [ ] **Step 3:** `py rt.py CharacterActionsPlaybackTests` → 6/6 pass. If one still fails, it is a real `LateUpdate` behaviour regression: debug it, and do not adjust the expected value.
- [ ] **Step 4: Doc.** Test-only change, so no system doc changes. Commit `test(animation): step CharacterActions through LateUpdate`.

### Task A2: The Clanker stack test expects the humanoid wiring on humanoid bodies

**Files:**
- Modify: `Assets/Game/Editor/Animation/CharacterActionWiring.cs` (expose the component list `Ensure` adds)
- Modify: `Assets/Game/Editor/Tests/ClankerPrefabTests.cs:50-69`
- Doc: `docs/AI/systems/AgentSystem.md` (the Clanker section: humanoid bodies carry the action wiring) and `docs/AI/systems/HumanoidAnimation.md` (the wiring list now has one source)

**Interfaces:** Produces `CharacterActionWiring.NpcComponents : IReadOnlyList<System.Type>` (NPC set, in the order `Ensure` adds them).

- [ ] **Step 1: Failing test first.** Change the assertion so a humanoid body expects the Clanker stack plus the wiring set:
  ```csharp
  string[] expected = Names(clanker)
      .Concat(CharacterActionWiring.WearsHumanoidController(prefab)
                  ? CharacterActionWiring.NpcComponents.Select(t => t.Name)
                  : Enumerable.Empty<string>())
      .OrderBy(n => n).ToArray();
  CollectionAssert.AreEqual(expected, Names(prefab),
      "a body variant carries the RPR Clanker's stack plus, when it wears Humanoid.controller, the action wiring");
  ```
  Run `py rt.py ClankerPrefabTests`. It must fail to compile (no `NpcComponents`) or fail. Either proves the test is live.
- [ ] **Step 2: Implement.** In `CharacterActionWiring` add one source of truth and make `Ensure` use it, so the list cannot drift. The player set stays the first four.
  ```csharp
  /// <summary>What Ensure adds to an NPC that wears the humanoid controller, in order.</summary>
  public static readonly IReadOnlyList<System.Type> NpcComponents = new[]
  {
      typeof(CharacterActions), typeof(BodyLanguage), typeof(IdleVariation), typeof(HurtReaction),
      typeof(SpeechGestures), typeof(MeleeDefense),
  };
  ```
  Keep `Ensure`'s typed `AddIfMissing<T>` calls, which it needs for configuring `BodyLanguage` and `SpeechGestures`. Add an EditMode assertion in the same fixture: `Ensure` on a fresh humanoid NPC adds exactly `NpcComponents`. Use `Drifter_Human.prefab`, instantiated into a preview scene with the added components removed first. Mirror the `CharacterActionsPlaybackTests` SetUp.
- [ ] **Step 3:** `py rt.py ClankerPrefabTests HumanoidWiringAssetTests` → ClankerPrefabTests all pass.
- [ ] **Step 4:** Update the docs (bump `updated:`) and run `python3 tools/docs_check.py --index`. Commit.

### Task A3: Turn ragdolls back on (decision required)

**Needs:** a **user decision** and the **Task B1 Play Mode capture**. The flag exists to find out whether ragdolls cause the lag; B1's protocol measures that.

**Files:**
- Modify: `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollController.cs:69-72, 215, 334, 404, 457`
- Doc: `docs/AI/systems/Combat.md` (ragdoll section). If the capture shows a cost, add it under Gotchas.

- [ ] **Step 1:** `py rt.py HogtieTests` → 13 fail. `py rt.py NetGunTests` → many fail (the "refused" messages).
- [ ] **Step 2:** Delete the `RagdollsDisabled` field and its four early returns. Do not replace it with a serialized toggle. If B1 shows ragdolls are expensive, fix the cost: `RagdollBudget` already exists for this.
- [ ] **Step 3:** `py rt.py HogtieTests` → 17/17. `py rt.py NetGunTests` → all pass. `py rt.py Ragdoll Knock` → all pass.
- [ ] **Step 4:** Play Mode check by the user: knock a nomad down, net a creature, and tie a downed player, on host and on an MPPM client.
- [ ] **Step 5:** Doc and commit `fix(ragdoll): remove the temporary kill switch`.

### Task A4: AgentCarryTests helpers measure what their names say

**Files:**
- Modify: `Assets/Game/Editor/Tests/AgentCarryTests.cs:189-207` (`Drop`), `:248-271` (`Boost`)

- [ ] **Step 1:** `py rt.py AgentCarryTests` → the 2 known failures.
- [ ] **Step 2: Fix `Drop`.** Return the velocity the last step produced, which is what the motor would carry into the next step. With the mark before the fall this is still terminal speed, so `Drop_ReachesTerminalSpeed` is unaffected.
  ```csharp
  private static float Drop(bool markBeforeTheFall, int steps = 100)
  {
      float y = 100f, lastY = y, velocity = 0f;
      for (int i = 0; i < steps; i++)
      {
          float measured = (y - lastY) / Dt;
          if (markBeforeTheFall) lastY = y;
          velocity = AgentCarry.Fall(new Vector3(0f, measured, 0f), Gravity, Dt, TerminalSpeed).y;
          y += velocity * Dt;
          if (!markBeforeTheFall) lastY = y;
      }
      return velocity;
  }
  ```
- [ ] **Step 3: Fix `Boost`.** Keep the burnout speed in its own variable:
  ```csharp
  float speedAtBurnout = 0f;
  // ...inside the loop, after the thrust is applied:
  if (i == burnSteps - 1) speedAtBurnout = (y - lastY) / Dt;
  // ...
  return (peak, speedAtBurnout);
  ```
  Expected values: net 22 m/s² for 2 s gives ≈44 m/s, and the peak is ≈44 m + 44²/36 ≈ 98 m. Both fall inside the existing bands.
- [ ] **Step 4:** `py rt.py AgentCarryTests` → all pass. Mutation check: set `markBeforeTheFall: true` in `Drop_MarkingAfterTheFall…` locally and confirm the test now fails, then revert.
- [ ] **Step 5:** Test-only, no doc change. Commit.

### Task A5: ArmAim limits the whole swing, not each stage

**Files:**
- Modify: `Assets/Game/Scripts/Characters/Player/Combat/ArmAim.cs:74-89`
- Test: `Assets/Game/Editor/Tests/ArmAimTests.cs` (the failing test already exists; add one for the shared budget)
- Doc: `docs/AI/systems/Flashlight.md` (ArmAim rows plus a Gotcha: "the limit is on the pointer's total swing")

- [ ] **Step 1:** Add a second failing test, so the fix cannot pass by over-clamping the shoulder alone:
  ```csharp
  [Test]
  public void ALimitedSwingStillUsesTheWholeLimit()
  {
      Vector3 target = upper.position - Vector3.forward * 5f;
      Quaternion before = pointer.rotation;
      ArmAim.Point(upper, lower, pointer, target, shoulderShare: 0.55f, maxDegrees: 40f, weight: 1f, elbowPasses: 2);
      Assert.Greater(Quaternion.Angle(before, pointer.rotation), 38f, "a clamp that stops far short wastes the reach the limit allows");
  }
  ```
  `py rt.py ArmAimTests` → `TheSwingStopsAtTheShoulderLimit` fails (102).
- [ ] **Step 2: Implement.** Read the clamped aim **direction** once, before anything moves. When the target is beyond the limit, every stage pursues that fixed direction. A direction has no parallax, so the elbow passes converge on exactly the limit. Within the limit, keep the current point pursuit, which `TheDeviceEndsUpFacingTheTarget` relies on.
  ```csharp
  public static void Point(Transform upper, Transform lower, Transform pointer, Vector3 target,
                           float shoulderShare, float maxDegrees, float weight, int elbowPasses)
  {
      if (upper == null || lower == null || pointer == null) return;

      Vector3 wanted = target - pointer.position;
      bool beyondLimit = Vector3.Angle(pointer.forward, wanted) > Mathf.Max(0f, maxDegrees);
      // Past the limit the arm reaches for the furthest direction it may, fixed now: re-reading the
      // target after the shoulder moved would hand every stage its own full limit.
      Vector3 limitedDirection = Swing(pointer.forward, wanted, maxDegrees) * pointer.forward;

      Quaternion Next() => beyondLimit
          ? HeadAim.Share(Quaternion.FromToRotation(pointer.forward, limitedDirection), Mathf.Clamp01(weight))
          : Remaining(pointer, target, maxDegrees, weight);

      upper.rotation = HeadAim.Share(Next(), shoulderShare) * upper.rotation;
      for (int pass = 0; pass < Mathf.Max(1, elbowPasses); pass++)
          lower.rotation = Next() * lower.rotation;
  }
  ```
  A local function that captures locals allocates a closure only if it is converted to a delegate. Here it is called directly, so it does not allocate.
- [ ] **Step 3:** `py rt.py ArmAimTests` → 8/8 pass.
- [ ] **Step 4: Play Mode (user).** With the flashlight or a worn device, look over each shoulder. The arm must stop at the limit and not pass through the chest. This applies on host and client, because the aim is presentation and runs on every machine.
- [ ] **Step 5:** Doc and commit.

### Task A6: Nomad builders wire the humanoid actions

**Files:**
- Modify: `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs` (in `BuildOne`, after the agent stack is added and before `SaveAsPrefabAsset`, around `:525-549`)
- Modify (only if A0 lists it): `Assets/Game/Editor/Creatures/StriderCrabOutriderBuilder.cs`
- Rebuild: Strider, Sand and Sky nomads (Tools menu entries at `NomadPrefabBuilder.cs:355-432`; follow the `spacegame-tribe` skill's rebuild order)
- Doc: `docs/AI/systems/Striders.md`, `docs/AI/systems/SkyTribe.md` (build steps), `docs/AI/systems/HumanoidAnimation.md` (which builders call `Ensure`)

**Interfaces:** Consumes `CharacterActionWiring.Ensure(GameObject) : bool` (unchanged).

- [ ] **Step 1:** `py rt.py HumanoidWiringAssetTests` fails, listing `StriderNomad_*`.
- [ ] **Step 2:** Add a builder-level test to `StriderTribeAssetTests` (or a new `NomadWiringTests`). For every nomad recipe prefab on disk, assert that `GetComponent<CharacterActions>() != null` and that `HealthReactionModule.hurtAnimTrigger` is empty. Run it and see it fail for the Strider nomads.
- [ ] **Step 3:** Call `CharacterActionWiring.Ensure(root);` in `BuildOne`, placed where `ClankerStack.Apply` calls it, after the modules exist. Do the same in `StriderCrabOutriderBuilder` if A0 lists the outrider.
- [ ] **Step 4:** Rebuild the nomads from the Tools menu. Re-read each prefab from disk: the GUID is unchanged and the components are present.
- [ ] **Step 5:** `py rt.py HumanoidWiringAssetTests StriderTribeAssetTests StriderRosterAssetTests StriderNetworkPrefabTests RosterAssetTests` → all pass.
- [ ] **Step 6:** Doc and commit. Coordinate with the session replacing the Strider bodies: it rebuilds through the same builder, so it inherits this fix.

### Task A7: Door and lid colliders match what the exporter cleared

**Decision (default chosen, user may override):** **Option 1:** the Unity collider becomes the leaf's convex hull, which is exactly what `dune_barge_export.py` checks against, so there is one model of the lid in both tools. **Option 2:** teach the exporter about oriented boxes and re-export in Blender. That is more work and needs Blender. Option 1 was verified in the editor: 0/24 spots blocked.

**Files:**
- Modify: `Assets/Game/Editor/Vehicles/DuneBargeBuilder.cs:177-218, 244-256, 280-302` (`AddLeafCollider` returns `Collider`; `FreerSide` takes `Collider own`)
- Rebuild: `Tools/Vehicles/Build Dune Barge Prefabs`
- Doc: `docs/AI/systems/DuneBarge.md`. Replace the Gotcha "Leaf and lid colliders are oriented boxes on a `LeafCollider` child" with: they are convex `MeshCollider`s of the leaf mesh, the same hull the exporter's `Obstacles` clears set-down spots against, and a box is bigger than the hull and overlapped two ladder exits and a hatch mark per variant. Add a `symptoms:` entry: "DuneBargePrefabTests says a ladder exit or hatch mark is inside LeafCollider".

- [ ] **Step 1:** `py rt.py DuneBargePrefabTests` → 3 failures (already failing).
- [ ] **Step 2: Implement.** Keep the `LeafCollider` child, which the Interactor and the tests resolve, at the leaf's identity pose:
  ```csharp
  private static Collider AddLeafCollider(Transform leaf)
  {
      var go = new GameObject(LeafColliderName);
      go.transform.SetParent(leaf, false);
      // The leaf's own convex hull: exactly the shape dune_barge_export.py's Obstacles clears every
      // set-down spot against. An oriented box around a curved lid is larger and overlapped them.
      var hull = go.AddComponent<MeshCollider>();
      hull.sharedMesh = leaf.GetComponent<MeshFilter>().sharedMesh;
      hull.convex = true;
      Physics.SyncTransforms();
      return hull;
  }
  ```
  Remove the `hinge` parameter and the across-axis check that only the box needed. Keep the `InvalidOperationException` for a leaf centred on its hinge, but move it into `SwingSign`/`FreerSide`, which still use the leaf centre. Change `FreerSide(..., Collider own, ...)`.
- [ ] **Step 3:** Rebuild the three barge prefabs. Re-read them: the GUIDs are unchanged and each `LeafCollider` has a convex `MeshCollider` with a mesh.
- [ ] **Step 4:** `py rt.py DuneBargePrefabTests` → all pass, including `DoorsLidsAndHatchesCanBeUsed`.
- [ ] **Step 5: Play Mode (user).** Open and close every door and hatch, climb each ladder, and crawl through each hatch, on host and client. Doors are replicated and saved through `ArticulatedPartInteraction`, which is unchanged.
- [ ] **Step 6:** Doc, regenerate the index, and commit.

### Task A8: Sky transports: both vehicle guards stand, and the prefabs are rebuilt clean

**Files:**
- Modify: `Assets/Game/Editor/Tests/SkyTransportPrefabTests.cs:47-60`
- Rebuild: `Tools/SpaceGame/Vehicles/Build Sky Transports` (`SkyVesselBuilder.cs:110`)
- Doc: `docs/AI/systems/SkyTribe.md`. Gotcha at `:94`: add that `RagdollWiring.IsVehicle` now also matches `/Prefabs/Vehicles/` (main's half of the fix), and that a merge can bring back saver-laden transport YAML from a branch that predates `SaveablePolicy`'s `VesselPilot` rule, which a rebuild cleans.

- [ ] **Step 1: Fix the counter-case.** It must use a path that neither guard excludes, so it still proves that `VesselPilot` **alone** excludes a body. Add a second assertion for the folder guard:
  ```csharp
  const string creaturePath = "Assets/Game/Prefabs/Agents/creatures/Transport.prefab";
  const string vehiclePath  = "Assets/Game/Prefabs/Vehicles/Sky/Transport.prefab";

  Assert.IsTrue(RagdollWiring.IsBody(hull, creaturePath), "counter-case: health alone is a body");
  Assert.IsFalse(RagdollWiring.IsBody(hull, vehiclePath), "anything under Prefabs/Vehicles is a machine by folder");

  hull.AddComponent<VesselPilot>();
  Assert.IsFalse(RagdollWiring.IsBody(hull, creaturePath), "a piloted hull must not ragdoll, wherever it lives");
  ```
- [ ] **Step 2:** `py rt.py SkyTransportPrefabTests` → `AFlownVesselIsNotARagdollBody` passes; `TheVesselCarriesNoSaversAndNoRagdoll` ×2 still fail (data).
- [ ] **Step 3:** Run `Build Sky Transports`. Re-read both prefabs from disk and check: no `SaveableEntity`, no `ISaveable`, no `AgentRagdoll`/`RagdollRig`, `NetworkObject` hash unchanged, GUID unchanged.
- [ ] **Step 4:** `py rt.py SkyTransportPrefabTests RosterAssetTests` → all pass.
- [ ] **Step 5:** Persistence check (user): start a Sky war party, save, quit and load. The party's group record rebuilds the vessel, and no duplicate hull appears. Doc and commit.

### Task A9: Sound stays silent in EditMode without an error

**Files:**
- Modify: `Assets/Game/Scripts/Audio/Sfx.cs` (top of the play path that reaches `RuntimeManager`, before `:126`)
- Doc: `docs/AI/systems/audio.md`. Gotcha: FMOD's `RuntimeManager` logs an **error** outside Play Mode; every EditMode test that triggers a sound fails on it.

- [ ] **Step 1:** `py rt.py LassoTests` → the 3 FMOD failures.
- [ ] **Step 2: Failing test.** Create `Assets/Game/Editor/Tests/SfxEditModeTests.cs`:
  ```csharp
  [Test]
  public void PlayingASoundInEditModeLogsNoError()
  {
      LogAssert.NoUnexpectedReceived();
      Sfx.Play(SfxId.RopeThrow, Vector3.zero, 0);
  }
  ```
  Use the `Sfx.Play` overload `LassoArtifact` calls (`Sfx.Play(SfxId, Vector3, int)`). Run it and see it fail on the FMOD error.
- [ ] **Step 3: Implement.** Return before touching FMOD when there is no runtime:
  ```csharp
  // FMOD's RuntimeManager exists only in Play Mode and LOGS AN ERROR when reached outside it, which
  // fails every EditMode test whose action makes a sound. Edit mode has no listener anyway.
  if (!Application.isPlaying) return;
  ```
  Put it after the `chosen.IsNull` warning, so the missing-catalog-entry warning still reports in tests, and before the cooldown and distance logic.
- [ ] **Step 4:** `py rt.py SfxEditModeTests LassoTests` → the 3 FMOD failures are gone (`RopeStaysSmoothWhileBeingThrown` remains: Task A10). Also run `py rt.py Audio Sfx` for any fixture that relied on reaching FMOD.
- [ ] **Step 5:** Doc and commit.

### Task A10: Investigate the lasso rope zigzag

**Needs:** systematic debugging before any fix (root cause unknown).

**Files:** `Assets/Game/Scripts/Items/Artifacts/Lasso/LassoRope.cs` (read), `Assets/Game/Editor/Tests/LassoTests.cs:86-125`

- [ ] **Step 1:** Reproduce: `py rt.py LassoTests` → `RopeStaysSmoothWhileBeingThrown` 64.95°.
- [ ] **Step 2: Instrument in a scratch copy of the test.** Do not commit this. Per frame, log the node count, `MeanTurnAngle`, and the frame where the angle first exceeds 8°. Check whether the rope node count changes on `Show` versus `Simulate` (a re-subdivision every frame would read as a fold), and whether the 65° appears on frame 1, which would mean `Show` with a 0.5 m span builds a degenerate chain.
- [ ] **Step 3:** Form one hypothesis, test it minimally, then write the fix with the existing test as the failing test. Doc: `docs/AI/systems/Lasso.md` Gotchas, with a `symptoms:` entry "the lasso rope folds into stairs while thrown".
- [ ] **Step 4:** `py rt.py LassoTests` → all pass. Commit.

---

# Part B: Strider/NPC performance

Principles: **GDC-L1-PERF-0001** (objective, confidence 5): measure, don't guess; keep before/after numbers. **GDC-L1-PERF-0003** (objective, 4): fix the current top cost, then re-profile. It also notes two exceptions that apply here: the per-frame closures are a *many-small-costs* pattern that gets a structural fix, and `NpcWorldSim.Spawn` is a *spike* that must be measured as a max frame time, not an average. **GDC-L1-PERF-0004**: decide whether a capture is CPU-bound or GPU-bound before choosing between the renderer tasks (B9) and the script tasks.

**Order:** B1 first. B2–B10 are listed in the investigation's suspected order, but **re-order them by B1's numbers** before starting, and drop any task whose marker costs under 0.1 ms/frame in the city capture.

**Parallel-safe groups** (no shared files):
- {B2}, {B3}, {B5}, {B6}, {B7}, {B10a}
- B4 must run before B7, because B4 moves perception onto `hit.collider.transform`, and B7's root Rigidbody changes `RaycastHit.transform` for every walker collider.
- B8 and B9 share `NomadPrefabBuilder.cs` with A6. Run them sequentially: A6 → B8 → B9.

**Needs a Play Mode capture from the user:** B1 (baseline), and a re-capture after each task. Tasks B3, B4, B8 and B9 also need a client check.

### Task B1: Profiler markers and a capture protocol

**Files:**
- Modify (add `static readonly ProfilerMarker` fields and `using (Marker.Auto())` around the hot body, no logic change):
  - `Assets/Game/Scripts/agents/controller/AgentController.cs` (`Update` total; `EvaluateModules`; `ApplyFacingOverride`)
  - `Assets/Game/Scripts/agents/AI/Targeting/AgentTargeting.cs` (`Update`; `RefreshTargetState`)
  - `Assets/Game/Scripts/agents/perception/PerceptionModule.cs` (`IsUnobstructed`)
  - `Assets/Game/Scripts/agents/animation/AgentGroundConform.cs` (`Conform`)
  - `Assets/Game/Scripts/agents/AI/Motors/NavPathFollower.cs` (the repath branch of `SteerTarget`)
  - `Assets/Game/Scripts/agents/Modules/Combat/MenaceSensor.cs` (scan), `NpcItemUseModule.cs` (`HasLineOfSight`)
  - `Assets/Game/Scripts/agents/Modules/Formation/FormationModule.cs` (`Tick`)
  - `Assets/Game/Scripts/agents/World/NpcWorldSim.cs` (`Spawn`)
  - `Assets/Game/Scripts/Vehicles/Systems/WalkerPlatformCarrier.cs` (`FixedUpdate`)
  - `Assets/Game/Scripts/agents/Tasks/NpcTaskPlanner.cs` (`ResolveDestination`)
  - `Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs` (limp build / physics tick, whichever it has)
- Create: `Assets/Game/Editor/Tests/ProfilerMarkerNamingTests.cs`
- Doc: `docs/AI/systems/Diagnostics.md`. New section "Profiling": the marker naming rule, the marker list, and the capture protocol below. Add a `symptoms:` entry: "the Strider city drops the frame rate and nobody knows which system".

**Interfaces:** Produces the marker names `SpaceGame.<System>.<Method>`, e.g. `SpaceGame.Agent.Update`, `SpaceGame.Agent.Modules`, `SpaceGame.Agent.Facing`, `SpaceGame.Targeting.Refresh`, `SpaceGame.Perception.LineOfSight`, `SpaceGame.GroundConform.Probe`, `SpaceGame.NavPath.Repath`, `SpaceGame.Menace.Scan`, `SpaceGame.ItemUse.LineOfSight`, `SpaceGame.Formation.Tick`, `SpaceGame.NpcWorldSim.Spawn`, `SpaceGame.WalkerCarrier.Fixed`, `SpaceGame.NpcTask.Resolve`, `SpaceGame.Ragdoll.Rig`. Later tasks quote them.

**Sensing and walker markers (added):** `SpaceGame.Perception.LineOfSight` (`PerceptionModule.IsUnobstructed`, every sight and muzzle ray), `SpaceGame.Targeting.Reevaluate` (`AgentTargeting.Reevaluate`, the candidate scoring on the interval), `SpaceGame.Targeting.Refresh` (`AgentTargeting.RefreshTargetState`, every frame), `SpaceGame.Menace.Scan` (`MenaceSensor.FindAimer`), `SpaceGame.GroundConform.Probe` (`AgentGroundConform.Conform`), `SpaceGame.WalkerCarrier.Fixed` (`WalkerPlatformCarrier.FixedUpdate`), `SpaceGame.ItemUse.LineOfSight` (`NpcItemUseModule.HasLineOfSight`). `Perception.LineOfSight` nests inside `Targeting.*` and `ItemUse.LineOfSight`, so read its self ms, not its total.

**Agent tick and route markers (added):** `SpaceGame.Agent.Update` (`AgentController.Update`, the whole brain), `SpaceGame.Agent.Modules` (every module loop: side-effect + movement arbitration, a passenger's side-effect tick, a watcher's presentation tick), `SpaceGame.Agent.Facing` (`ApplyFacingOverride`), `SpaceGame.Fault.Run` (one sample per barrier-guarded body, so its *calls* column is the module ticks per frame), `SpaceGame.NavPath.Repath` (the `CornerSource` fetch in `NavPathFollower.SteerTarget`: `NavMesh.SamplePosition` ×2 + `CalculatePath`), `SpaceGame.Formation.Tick`, `SpaceGame.NpcWorldSim.Spawn` (a spike: read its max, not its average), `SpaceGame.NpcTask.Resolve`. Nesting: `Agent.Update` ⊃ `Agent.Modules` ⊃ `Fault.Run` ⊃ `Formation.Tick` and the other module markers. `NavPath.Repath` sits under `Agent.Update` but outside `Agent.Modules` for `LeggedDriver` (its route is fetched from `Motor.Tick`), and under `FixedUpdate` for `TrackedHullMotor` and `MonowheelMotor`. `SpaceGame.Ragdoll.Rig` is **not** added: ragdoll code belongs to another session (A3).

- [ ] **Step 1: Test the convention first.** Reflect over `Assembly-CSharp` for static `ProfilerMarker` fields and assert that each marker's name starts with `SpaceGame.` and is unique. `ProfilerMarker` has no name getter, so keep the names in a `const string` beside each field and reflect over the consts named `*MarkerName`. Run it: it fails, because no markers exist yet.
- [ ] **Step 2:** Add the markers using this shape:
  ```csharp
  private const string UpdateMarkerName = "SpaceGame.Agent.Update";
  private static readonly ProfilerMarker UpdateMarker = new ProfilerMarker(UpdateMarkerName);
  // in Update():
  using (UpdateMarker.Auto()) { /* existing body */ }
  ```
  `ProfilerMarker` is compiled out of non-development builds, so it is not dead code.
- [ ] **Step 3:** `python3 tools/typecheck.py --editor`; `py rt.py ProfilerMarkerNamingTests AgentModuleBarrierTests FaultBarrierTests` → pass.
- [ ] **Step 4: Capture protocol (the user runs it; write it into Diagnostics.md).**
  1. A development build is preferable; the editor is acceptable if it is noted. Disable VSync. Close the Scene view.
  2. Scene: the world. Teleport next to the Strider city: 3 walking houses with 18 seated crew, 2 crawlers, 2 crabs, 8 scouts. Also run a second capture next to a Sky war party.
  3. Profiler → CPU module, **not** Deep Profile. Record 600 frames after the city has been on screen for 10 s. Separately record the frame range spanning a city spawn (`NpcWorldSim.Spawn`).
  4. Write down: median and max frame ms; the self ms of each `SpaceGame.*` marker (Hierarchy view, search `SpaceGame.`); `Physics.Processing`/`Physics.Simulate`; `PostLateUpdate.UpdateAllSkinnedMeshes` and `Render.OpaqueGeometry`/shadow passes; `GC.Alloc` per frame; Rendering stats (batches, SetPass, shadow casters, visible skinned meshes); whether the GPU module shows GPU-bound (GDC-L1-PERF-0004).
  5. Repeat steps 3-4 once more with `RagdollsDisabled` set back to `false`. This is a one-line local edit and is not committed. The comparison answers the question that flag was added for (Task A3).
  6. Paste the numbers into this plan under "Baseline". Re-order B2–B10 by them.
- [ ] **Step 5:** Doc, index, and commit `perf: profiler markers on the agent and walker hot paths`.

**Capture protocol, ready to run (GDC-L1-PERF-0001: keep the before numbers; GDC-L1-PERF-0004: CPU or GPU first):**
1. Prefer a Development Build with *Autoconnect Profiler*; the editor works but note "editor" beside every number (it adds editor overhead and runs its own GC). VSync off (Quality → VSync Count: Don't Sync), Game view only, Scene view closed, no Inspector on an NPC.
2. Load the world, teleport beside the Strider city and wait 10 s with it on screen.
3. Window → Analysis → Profiler, CPU Usage module, Deep Profile **off**, Record. Capture 600 frames. Stop.
4. Timeline or Hierarchy view, search `SpaceGame.`. For each marker write down: **total ms** and **self ms** per frame (median over the 600 frames: select a typical frame, then check the worst one), **calls**, and **GC Alloc**. Also: median and max frame ms, `PlayerLoop` total, `Physics.Processing`/`Physics.Simulate`, `PostLateUpdate.UpdateAllSkinnedMeshes`, `Render.OpaqueGeometry` and the shadow passes, total `GC.Alloc` per frame, and the Rendering module's batches / SetPass / shadow casters / visible skinned meshes.
5. GPU or CPU: add the GPU Usage module (or read `Gfx.WaitForPresent*` in the CPU timeline). Long `WaitForPresent`/`WaitForTargetFPS` with a short main thread = GPU-bound → B9 first; otherwise the script tasks.
6. Spike capture: clear, Record, trigger a city or war-party spawn (`NpcWorldSim`), Stop, and note the **max** frame and `SpaceGame.NpcWorldSim.Spawn`'s ms in that frame (GDC-L1-PERF-0003: a spike is judged by its max, not an average).
7. Repeat 2-6 beside a Sky war party, then once more with `RagdollsDisabled = false` (local, uncommitted) for A3.
8. Paste the table below and reorder B2-B10: drop any task whose marker is under 0.1 ms/frame in the city capture.

**Baseline:** *(user's numbers go here)*

### Task B2: Allocation-free fault barrier for module ticks

**Files:**
- Modify: `Assets/Game/Scripts/Core/Diagnostics/Fault.cs:69-94, 144-166`, `Assets/Game/Scripts/Core/Diagnostics/FaultBudget.cs`
- Modify: `Assets/Game/Scripts/agents/controller/AgentController.cs:326-336, 387-412`
- Test: `Assets/Game/Editor/Tests/FaultBarrierTests.cs`, `Assets/Game/Editor/Tests/AgentModuleBarrierTests.cs`
- Doc: `docs/AI/systems/Diagnostics.md` (the new overload and the "nothing quarantined" fast path), `docs/AI/systems/AgentSystem.md` (module tick)

**Interfaces:** Produces `public delegate void RefAction<TState>(ref TState state);` and `public static bool Fault.Run<TState>(Component owner, string site, ref TState state, RefAction<TState> body)`. Also `FaultBudget.AnyQuarantined : bool`. `RunModule`'s signature is unchanged.

- [ ] **Step 1: Failing test** in `AgentModuleBarrierTests`:
  ```csharp
  [Test]
  public void TickingAModuleAllocatesNothing()
  {
      var module = new GameObject("m").AddComponent<StubModule>();   // the fixture's existing stub
      var context = default(AgentContext);
      AgentController.RunModule(module, in context, 0.02f);          // warm up: JIT, statics
      long before = System.GC.GetAllocatedBytesForCurrentThread();
      for (int i = 0; i < 1000; i++) AgentController.RunModule(module, in context, 0.02f);
      Assert.AreEqual(0, System.GC.GetAllocatedBytesForCurrentThread() - before, "a closure or a string key per module per frame");
      Object.DestroyImmediate(module.gameObject);
  }
  ```
  Run `py rt.py AgentModuleBarrierTests`: it fails with closure and key bytes.
- [ ] **Step 2: Implement `Fault`:**
  ```csharp
  public delegate void RefAction<TState>(ref TState state);

  public static bool Run<TState>(Component owner, string site, ref TState state, RefAction<TState> body)
  {
      if (body == null || owner == null) return false;
      // The key is built only when something is quarantined at all, which is almost never.
      if (budget.AnyQuarantined && budget.IsQuarantined(Key(owner, site))) return false;
      try { body(ref state); return true; }
      catch (Exception e) { Report(owner, site, Key(owner, site), e); return false; }
  }
  ```
  Give the existing `Run(Component, string, Action)` and `IsQuarantined` the same `AnyQuarantined` early-out. In `FaultBudget`, keep an `int quarantinedCount`: increment it where `Entry.Quarantined` flips to true and reset it in `Clear()`. Then add `public bool AnyQuarantined => quarantinedCount > 0;`. Add a FaultBudget unit test: count 0 → record past the limit → `AnyQuarantined` → `Clear` → false.
- [ ] **Step 3: Implement `AgentController`:**
  ```csharp
  private struct ModuleCall { public IBehaviourModule Module; public AgentContext Context; public float DeltaTime; public MoveIntent? Result; }
  private static readonly Fault.RefAction<ModuleCall> TickModule = (ref ModuleCall c) => c.Result = c.Module.Tick(in c.Context, c.DeltaTime);

  public static MoveIntent? RunModule(IBehaviourModule module, in AgentContext context, float deltaTime)
  {
      if (module is not Component owner) return null;
      var call = new ModuleCall { Module = module, Context = context, DeltaTime = deltaTime };
      Fault.Run(owner, ModuleSite, ref call, TickModule);
      return call.Result;
  }
  ```
  Do the same for facing, with a `FacingCall` struct carrying `Wants` and `FacePosition`. Move the `"AgentModule.Facing"` literal into a `const string FacingSite`.
- [ ] **Step 4:** `py rt.py AgentModuleBarrierTests FaultBarrierTests FaultCoroutineTests` → all pass, including the existing quarantine tests: a throwing module is still quarantined after 5 faults in 10 s.
- [ ] **Step 5:** Multiplayer: no change; the barrier runs wherever modules tick. Persistence: none. User re-captures: `GC.Alloc` per frame under `SpaceGame.Agent.Modules` should be ≈0. Doc and commit.

### Task B3: Seated NPCs stop probing the ground, on every machine

**Files:**
- Modify: `Assets/Game/Scripts/agents/Modules/Riding/NpcSeating.cs:40-68` (server: `Suppress` parks the conform, `GiveBack` restores it)
- Modify: `Assets/Game/Scripts/Vehicles/SkyVessel/VesselSeats.cs:227-257` (every machine: `RefreshPresented` parks, `Release` restores)
- Test: `Assets/Game/Editor/Tests/AgentGroundConformTests.cs`, `Assets/Game/Editor/Tests/NpcPassengerTests.cs`
- Doc: `docs/AI/systems/SkyTribe.md` and `docs/AI/systems/Striders.md` (seated crew are parked), `docs/AI/systems/AgentSystem.md` (AgentGroundConform: who disables it). Do not edit `Vehicles.md`.

**Interfaces:** Produces `public static void NpcSeating.ParkPresentation(GameObject npc, bool parked)`, which enables or disables every `AgentGroundConform` under `npc`. It is the one helper both callers use.

- [ ] **Step 1: Failing tests.** (a) `NpcSeating.Suppress(npc)` leaves the NPC's `AgentGroundConform.enabled == false`, and `GiveBack` restores it. (b) `NpcSeating.ParkPresentation(npc, true)` disables it, and `OnDisable` resets `bodyRoot.localRotation` to rest. That is the existing clean park at `AgentGroundConform.cs:150-159`. Build the NPC as `AgentGroundConformTests` already does. Run → fail.
- [ ] **Step 2: Implement.** In `Suppress`: `foreach (AgentGroundConform c in npc.GetComponentsInChildren<AgentGroundConform>(true)) Disable(c);`. This reuses the recorded-and-restored `Disable` pattern, so `GiveBack` restores it for free. Add `ParkPresentation` and call it from `VesselSeats.RefreshPresented` for each NPC in `CollectSeatedNpcs`, and from `Release` with `false`. The `NpcPassenger` caravan seat (`NpcPassenger.cs:163-164`) gets the same call.
- [ ] **Step 3:** `py rt.py AgentGroundConformTests NpcPassengerTests CrewShiftTests StriderHabitatWalkerTests PassengerSeatTests` → all pass.
- [ ] **Step 4: Client check (user, MPPM).** Seated Strider crew on a walking house sit still on the client, without leaning or jittering. When unseated they conform to the ground again. Persistence: none (`RidesAsPassenger` is deliberately not saved). Doc and commit.

### Task B4: Line of sight: non-allocating, carrier-blind, throttled

**Files:**
- Modify: `Assets/Game/Scripts/agents/perception/PerceptionModule.cs:167-255`
- Modify: `Assets/Game/Scripts/agents/AI/Targeting/AgentTargeting.cs:489-528`
- Modify: `Assets/Game/Scripts/agents/Modules/Combat/NpcItemUseModule.cs:86, 400-412`
- Test: `Assets/Game/Editor/Tests/PerceptionLineOfSightTests.cs`, `Assets/Game/Editor/Tests/PerceptionAimTests.cs`, `Assets/Game/Editor/Tests/PassengerSeatTests.cs`
- Doc: `docs/AI/systems/AgentSystem.md` (perception: carrier skip, re-check interval). Gotcha: perception filters on `hit.collider.transform`, per INVARIANTS "A query does not know what the solver was told".

**Decision for the user (gameplay):** today seated crew are blinded by their own house, so in practice they rarely shoot from it. After this task they will. Confirm that this is the intended design before merging: GDC MP/combat feel, and whether crew are meant to fire from walking houses.

**Interfaces:** Adds serialized `PerceptionModule.sightRecheckInterval` (seconds, `[Tooltip]`, default 0.2) and a randomly phased timer. Produces `PerceptionModule.CanSeeCached(Transform target) : bool`, which `AgentTargeting` uses.

- [ ] **Step 1: Failing tests** in `PerceptionLineOfSightTests`:
  - (a) An NPC parented under a "house" with a BoxCollider between its eye and the target, and no other wall, can see the target. This is the carrier skip.
  - (b) The same layout, but the wall is a separate object not in the NPC's parent chain: blocked.
  - (c) Allocation: 1000 `CanSee` calls allocate 0 bytes (same `GC.GetAllocatedBytesForCurrentThread` pattern as B2).
  - (d) A target behind a wall whose collider sits on a child of a kinematic Rigidbody root is still reported as blocked by the wall, not as the target. This pins `hit.collider.transform`.

  Run → (a) and (c) fail.
- [ ] **Step 2: Implement `IsUnobstructed`.** Copy the grow-and-recast shape from `WalkerGround.Ray` (`Locomotion/Ground/WalkerGround.cs:67-101`): an instance buffer starting at 16, doubling up to a cap. Put the cap in a `const` named for its purpose; the precedent is `MaxBuffer = 512`. Filter on `hits[i].collider.transform`. Skip `t.IsChildOf(transform)` and, while seated, `t.IsChildOf(carrierRoot)`. `carrierRoot` is read when `AgentController.RidesAsPassenger` is true, as `transform.parent != null ? transform.parent.root : null`. The NPC is network-parented under the carrier's NetworkObject (`NpcSeating.Attach`).
- [ ] **Step 3: Throttle.** `AgentTargeting.RefreshTargetState` calls `perception.CanSeeCached(Target)`. That re-casts only when its timer elapses or the target changed. The timer starts at `Random.Range(0, sightRecheckInterval)` in `OnEnable`, which is the `AgentController.speedVariationPhase` precedent (`AgentController.cs:120`).
- [ ] **Step 4: `NpcItemUseModule.HasLineOfSight`.** Default `lineOfSightBlockers` to `PerceptionModule.SolidGeometryLayers` instead of `~0`. That field is serialized, so rebuild the nomads (after A6 and B8, sequentially) so the prefabs pick it up, and pin it with an asset test. Route the cast through the same self/carrier/target filter: call `PerceptionModule.HasLineOfSightFrom` when the NPC has a perception module, so there is no second copy.
- [ ] **Step 5:** `py rt.py PerceptionLineOfSightTests PerceptionAimTests PassengerSeatTests AgentAuthorityTests AlertChainTests HostileDialogTests MountedGunnersTests` → all pass.
- [ ] **Step 6:** Multiplayer: authority-only, no client change. Persistence: perception memory is still restored by `RestoreMemory`; the cache is runtime-only. User re-captures `SpaceGame.Perception.LineOfSight`, and play-tests crew firing from a house. Doc and commit.

### Task B5: Desynchronise re-evaluation timers

**Files:**
- Modify: `Assets/Game/Scripts/agents/AI/Targeting/AgentTargeting.cs:268, 299` (and `stormSampleTimer` `:466-471`)
- Modify: `Assets/Game/Scripts/agents/Modules/Combat/MenaceSensor.cs:90`
- Test: new `Assets/Game/Editor/Tests/AgentTimerPhaseTests.cs`
- Doc: `docs/AI/systems/AgentSystem.md`

- [ ] **Step 1: Failing test.** Enable 20 `AgentTargeting` components with the same profile and assert that their first re-evaluation timers are not all equal (read the private field by reflection, the way `CharacterActionsPlaybackTests` does). Do the same for `MenaceSensor.scanTimer`. Run → fail (all 0).
- [ ] **Step 2: Implement.** In `OnEnable`, after `EnsureSettings()`: `reevaluateTimer = Random.Range(0f, settings.reevaluateInterval);`, and the same for `stormSampleTimer`. `ApplyProfile` re-phases the same way. In MenaceSensor: `scanTimer = Random.Range(0f, Mathf.Max(MinScanInterval, scanInterval));`. Promote the inline `0.05f` at `:116-120` to a named const while you are there, since it is now used twice. `ForceTarget` keeps its full interval.
- [ ] **Step 3:** `py rt.py AgentTimerPhaseTests AgentAuthorityTests AlertChainTests` → pass.
- [ ] **Step 4:** Authority-only; timers are not saved (same reasoning as `AgentController.cs:95-100`). Re-capture: the per-frame spikes in `SpaceGame.Targeting.Refresh` should flatten. Doc and commit.

### Task B6: Do not rebuild a route to a target that has not moved

**Files:**
- Modify: `Assets/Game/Scripts/agents/AI/Motors/NavPathFollowerSettings.cs`, `Assets/Game/Scripts/agents/AI/Motors/NavPathFollower.cs:20-89`
- Modify: `Assets/Game/Scripts/agents/AI/Motors/LeggedDriver.cs:57-58`, `Assets/Game/Scripts/Vehicles/Motors/TrackedHullMotor.cs:44-45` (initialisers only). **Do not touch `MonowheelMotor.cs`:** it keeps the 4-argument constructor, which preserves today's behaviour.
- Test: `Assets/Game/Editor/Tests/NavPathFollowerTests.cs`
- Doc: `docs/AI/systems/Locomotion.md` (NavPathFollower tunables)

**Interfaces:** Adds `NavPathFollowerSettings.stillTargetRepathInterval` (float, `[Tooltip("Seconds between route rebuilds while the destination has not moved past repathTolerance. Long routes are expensive to rebuild; a route only goes stale when the body is pushed off it.")]`). Adds a 5-arg constructor. The existing 4-arg constructor chains with `stillTargetRepathInterval = repathInterval`. Adds `NavPathFollower.RequestRepath()` if it is not already public (it exists at `:101`).

- [ ] **Step 1: Failing tests.** Use the `CornerSource` constructor with a counting stub:
  - (a) With the target still for 10 s at dt 0.1, interval 0.5 and still-interval 5, the corner source is called ≤ 3 times, not 20.
  - (b) A target that moves more than the tolerance repaths on the same frame.
  - (c) After `RequestRepath()` (what a motor calls when the body is knocked off its route), the next `SteerTarget` repaths.

  Run → (a) fails.
- [ ] **Step 2: Implement.** In `SteerTarget`, re-arm with `repathTimer = targetMoved ? repathInterval : stillTargetRepathInterval;`. Floor it in `Validate()` with a `MinStillTargetRepathInterval` const.
- [ ] **Step 3:** Set the initialisers: LeggedDriver `stillTargetRepathInterval: 5f`, TrackedHullMotor `5f`. Prefabs that already serialize the struct will deserialize the new field from the class initializer. Add an asset test that reads `RigWalker.prefab`'s LeggedDriver value and asserts it is greater than `repathInterval`.
- [ ] **Step 4:** `py rt.py NavPathFollowerTests StriderHabitatWalkerTests SpiderWalkerGroundingTests` → pass.
- [ ] **Step 5:** Authority-only; a route is rebuilt rather than saved. Re-capture `SpaceGame.NavPath.Repath`. Doc and commit.

### Task B7: RigWalker gets a kinematic body so its moving colliders are not static

**Precondition:** B4 is merged (perception filters on `collider.transform`).

**Files:**
- Modify: `Assets/Game/Prefabs/Agents/Vehicles/Ground/RigWalker.prefab`. Hand-authored, no builder. Add the Rigidbody through an editor menu step or `ux.py` read-only inspection plus a manual Inspector edit. Do not write the file from `execute_code`. Prefer adding a small `RigWalkerPhysicsWiring` editor method that mirrors `DesertCrawlerBuilder.cs:440-450`, so the change is reproducible.
- Modify: `Assets/Game/Scripts/Vehicles/Systems/WalkerPlatformCarrier.cs:158-181` (census guard)
- Test: `Assets/Game/Editor/Tests/WalkerPlatformCarrierTests.cs`, `Assets/Game/Editor/Tests/StriderHabitatWalkerTests.cs`
- Doc: `docs/AI/systems/Striders.md` (the walker has a kinematic root body, and why). Note in the commit that the `Vehicles.md` row is pending the other session.

- [ ] **Step 1: Failing tests.**
  - (a) Asset test: `RigWalker.prefab` and `StriderHabitatWalker.prefab` roots have a `Rigidbody` with `isKinematic`, `useGravity == false` and interpolation `Interpolate`.
  - (b) Census: two `WalkerPlatformCarrier`s overlapping, where the neighbour has a kinematic Rigidbody this machine does not own. The neighbour is not counted as a rider. Guard: skip any `rb` whose GameObject has its own `WalkerPlatformCarrier` or `AgentController`.

  Run → both fail.
- [ ] **Step 2: Implement.** Add the census guard in `CollectRiders`. Add the Rigidbody with mass as a serialized value in the wiring step: copy the crawler's `BodyMass` approach, with a named const. Rebuild the variant through `StriderCityBuilder` and re-read it from disk.
- [ ] **Step 3:** `py rt.py WalkerPlatformCarrierTests StriderHabitatWalkerTests SpiderWalkerGroundingTests DuneFoilCraftTests CrewShiftTests` → pass.
- [ ] **Step 4: Client check (user).** Ride a walking house as a client. Players and crates on the deck are carried, and the walker still counts as ground for NPC ground probes (`WalkerGround.IsLooseBody` is false for kinematic bodies). Re-capture `Physics.Processing`. Doc and commit.

### Task B8: Nomads replicate in local space while seated, and skip scale

**Precondition:** A6 is done (same builder).

**Files:**
- Modify: `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs` (after `AddByName("SpaceGame.Core.ClientNetworkTransform")` at `:1045`; use the existing `Set*` SerializedObject helpers at `:1872-1910`)
- Rebuild: all nomads
- Test: `Assets/Game/Editor/Tests/StriderNetworkPrefabTests.cs`
- Doc: `docs/AI/systems/Multiplayer.md` (NPC network transform settings) and `docs/AI/systems/Striders.md`

**Interfaces:** Adds serialized fields on the builder's recipe or a shared settings asset for `SwitchTransformSpaceWhenParented` (true), `SyncScaleX/Y/Z` (false) and `UseHalfFloatPrecision` (true/false: a user tunable). Copy the explicit-configuration precedent in `SkyVesselBuilder.cs:261-273`.

- [ ] **Step 1: Failing asset test.** For every nomad prefab: `ClientNetworkTransform.SwitchTransformSpaceWhenParented == true`, and `SyncScaleX/Y/Z == false`. Run → fail.
- [ ] **Step 2:** Configure these in the builder, then rebuild. `SwitchTransformSpaceWhenParented` requires the parent to be a NetworkObject with a NetworkTransform. RigWalker and the sky hulls have one; check that the seat-marker fallback (`NpcSeating.cs:150-153`, plain parent, `AutoObjectParentSync = false`) still behaves.
- [ ] **Step 3:** `py rt.py StriderNetworkPrefabTests StriderTribeAssetTests RosterAssetTests HumanoidWiringAssetTests` → pass.
- [ ] **Step 4: Client check (user, MPPM, required).** Seated crew stay glued to their seats on the client while the house walks. Unseated nomads move normally. Nobody snaps at the moment of seating or unseating (watch NGO's interpolation reset on the parent change). Watch the Network Profiler bytes per tick for the city before and after. Persistence: none; group records own positions. Doc and commit.

### Task B9: Cheaper nomad rendering (shared builder path)

**Precondition:** A6 and B8 are done (same builder). Run it only if B1 shows rendering or skinning among the top costs (GDC-L1-PERF-0004).

**Scope:** New Strider character models are about to replace the Strider nomad bodies (another session). Everything here goes in `NomadPrefabBuilder.BuildOne`, so it applies **now** to Sand (`Nomad`, `Nomad_*`) and Sky (`SkyNomad_*`) nomads, and to the new Strider bodies automatically once they are built through the same path.

**Files:**
- Modify: `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs:490-576` (a renderer step after `ApplyClothMaterial`, before `SaveAsPrefabAsset`)
- Create: `Assets/Game/Editor/Agents/NomadRenderSettings.cs` (a serialized `ScriptableObject` or `[Serializable]` recipe block holding the tunables below. This keeps NomadPrefabBuilder from growing further.)
- Test: new `Assets/Game/Editor/Tests/NomadRenderBudgetTests.cs`
- Doc: `docs/AI/systems/ArtPipeline.md` (nomad renderer budget), `docs/AI/systems/Striders.md`, `docs/AI/systems/SkyTribe.md`

**Tunables (serialized):** `shadowCasterMinSize` (m; smaller parts do not cast), `skinnedMotionVectors` (bool, default false), `lodCullScreenFraction` (fraction of screen height at which the body is culled), and optionally `lod1ScreenFraction` together with a list of accessory name prefixes dropped at LOD1 (`_Ring_`, `_Band_`, `_Buckle_`, `_Pouch_`).

- [ ] **Step 1: Failing asset tests** for each Sand and Sky nomad prefab:
  - (a) Every SMR has `skinnedMotionVectors == false`.
  - (b) Every SMR whose `localBounds` max extent is under `shadowCasterMinSize` has `shadowCastingMode == Off`.
  - (c) The root has a `LODGroup` whose last LOD's `screenRelativeTransitionHeight == lodCullScreenFraction`.
  - (d) Every SMR has a valid `rootBone`. `AgentGroundConform.ResolveBodyRoot` reads the first SMR's `rootBone`.

  Run → fail.
- [ ] **Step 2:** Implement the renderer step: shadows, motion vectors, and a `LODGroup` with LOD0 = all renderers, LOD1 = without the small accessories, then cull. Rebuild the Sand and Sky nomads.
- [ ] **Step 3:** `py rt.py NomadRenderBudgetTests StriderTribeAssetTests RosterAssetTests HumanoidWiringAssetTests` → pass.
- [ ] **Step 4: Deferred: merging accessories per material into one skinned mesh.** Do it only if the re-capture still shows `UpdateAllSkinnedMeshes` or batches as the top cost. There is no mesh-combining utility in the repo, so this is new code. `StriderNomad_Maroon` uses 29 distinct materials across its 43 SMRs, so a merge means submeshes, not one draw call, unless the textures are atlased. `Cloth_*` meshes must stay separate: ClothWind anchors in object space with one material per mesh (see the builder doc at `:730-736`). Write this as its own plan when needed.
- [ ] **Step 5:** Presentation only: every machine renders the same prefab, and nothing is saved. The user checks visually that the Sand and Sky nomads look right at near, mid and far range, and re-captures rendering stats. Doc and commit.

### Task B10: Secondary costs: do these only if their marker shows up

Each item is a separate small task with the same test-first shape, and each runs only if B1 puts its marker above 0.1 ms:
- **B10a: `WalkerPlatformCarrier.Overlap`** (`:200-208`) passes `~0`. Add serialized `LayerMask riderLayers`. Test: a collider on an excluded layer inside the carry volume is not collected. Doc: `Striders.md`.
- **B10b: `FormationModule.Tick`** (`:350`) samples the NavMesh every frame per follower out of slot. Cache the sampled slot and re-sample only when the slot moves past a serialized tolerance. Test: a still leader gives one sample per follower. Doc: `AgentSystem.md`.
- **B10c: `NpcWorldSim.Spawn`** (`:531-622`) spawns about 43 NetworkObjects in one frame, which is a spike (GDC-L1-PERF-0003 exception). Spread the members over frames with a serialized `membersPerFrame`. **Persistence risk:** a save taken mid-spawn must not lose the members still pending. The group record must list the plan, not the spawned set; verify with save/quit/load during a spawn. Doc: `AgentSystem.md`, `Striders.md`.
- **B10d: `MenaceSensor`** (`:194`) calls `GetComponentInChildren<EquipmentController>()` per candidate per scan. Cache it per entity in a dictionary cleared on registry removal. Doc: `AgentSystem.md`.
- **B10e: `NpcTaskPlanner` level-ground search on the spawn frame** (`NpcTaskModule.cs:125` sets `phaseTimer = 0`). Start with a random phase, as in B5. Doc: `AgentSystem.md`.

---

## Self-review notes

- Every row of the Diagnosis table maps to a task: #1→A1, #2→A2, #3→A3, #4→A4, #5→A5, #6→A7, #7–8→A8, #9→A6, #10→A9, #11→A10. Failures still unfound → A0.
- Investigation findings that did not hold up, corrected here:
  - `AgentRangedCombatModule` does **not** call `GetComponentInParent` per frame. It does it per shot, and the module does not tick for seated passengers. The real per-frame cast for seated crew is `NpcItemUseModule.HasLineOfSight`, which also has no self or carrier filter (B4).
  - `NavPathFollower` **does** have a target-moved tolerance. The cost is the timer OR (B6).
  - Modules do not all tick every frame: movement stops at the first winner (B2 still applies to side-effect and winning modules).
  - `PerceptionModule` reads `hits[i].transform`, which is the rigidbody's transform. That makes B4 a prerequisite for B7.
