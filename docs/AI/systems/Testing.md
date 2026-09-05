---
system: Testing
layer: pipeline
summary: EditMode-only NUnit suite, a headless Roslyn type-check, and the two-process batch-mode multiplayer autotest
paths:
  - Assets/Game/Editor/Tests/
  - Assets/Game/Tests/EditMode/
  - Assets/Game/Tests/Editor/
  - tools/typecheck.py
  - Assets/Game/Scripts/Core/Multiplayer/Autotest/
symptoms:
  - "how do I run the tests or type-check the code without clicking around the Unity GUI"
  - "the Test Runner reports nothing at all instead of a failing test"
  - "my component is a bag of nulls in the test because Awake never ran"
  - "typecheck.py prints 'No errors.' but the Editor still shows compile errors"
  - "Temp/headless_tests.txt never appears and I cannot tell if the run started"
  - "how do I actually prove this works on a client and not just the host"
  - "a test fails with Expected: (0.00, 0.00) But was: (0.00, 0.00) and nothing says what differed"
  - "a probe that excludes one part of a prefab measures that part anyway"
  - "typecheck.py says a type I just added to an asmdef does not exist"
reads_with: [Multiplayer, Persistence, EditorTooling]
updated: 2026-09-05
---

# Testing

~1841 NUnit edit-mode assertions across 164 files, plus a headless Roslyn type-check and a two-process batch-mode autotest — none of which need the Unity Editor GUI to *start*, though the test runner still needs an Editor process alive.

**Scope:** [`Assets/Game/Editor/Tests/`](Assets/Game/Editor/Tests) (105 files), [`Assets/Game/Tests/EditMode/`](Assets/Game/Tests/EditMode) (45), [`Assets/Game/Tests/Editor/`](Assets/Game/Tests/Editor) (18), [`tools/typecheck.py`](tools/typecheck.py), [`Assets/Game/Scripts/Core/Multiplayer/Autotest/`](Assets/Game/Scripts/Core/Multiplayer/Autotest)
**Related:** [Multiplayer.md](Multiplayer.md) · [Persistence.md](Persistence.md) · [spacegame-multiplayer](.claude/skills/spacegame-multiplayer/SKILL.md) · [spacegame-persistence](.claude/skills/spacegame-persistence/SKILL.md)

## Model

- **Everything is EditMode. There are zero `[UnityTest]`s and zero play-mode assemblies.** No coroutines, no frames, no physics stepping. Tests that need time march a system manually in a `for` loop.
- Two different assemblies host tests, and they see different things:
  - [`Assets/Game/Tests/EditMode/`](Assets/Game/Tests/EditMode) has [`SpaceGame.Tests.EditMode.asmdef`](Assets/Game/Tests/EditMode/SpaceGame.Tests.EditMode.asmdef) — `autoReferenced: false`, `UNITY_INCLUDE_TESTS`, Editor-only. It references only the 15 modular `SpaceGame.*` asmdefs. **An asmdef cannot reference `Assembly-CSharp`**, so nothing here can touch a type that lives outside a module.
  - [`Assets/Game/Editor/Tests/`](Assets/Game/Editor/Tests) and [`Assets/Game/Tests/Editor/`](Assets/Game/Tests/Editor) have **no asmdef**. They fall into `Assembly-CSharp-Editor`, which auto-references `Assembly-CSharp`, `UnityEditor`, `nunit.framework` and both TestRunner assemblies. This is why the bulk of the suite lives there: it is the only place that can see MonoBehaviours, prefabs on disk and `AssetDatabase`.
- **What is testable:** pure readers/builders/policies (`LocomotionPolicy`, `MatchRules`, `NetArg`, save codecs) directly; MonoBehaviours by `new GameObject().AddComponent<T>()` (214 sites) — but see Gotchas, `Awake`/`Start` do **not** run.
- **Wiring tests** are a first-class category here: `*WiringTests` load a prefab with `AssetDatabase.LoadAssetAtPath` and assert component/field wiring, catching the class of bug where the code is right and the asset is not.
- Shared fixtures are thin — three helpers, no base classes: [`PersistenceProbe`](Assets/Game/Editor/Tests/PersistenceProbe.cs) (`.For(prefabPath).Mutate(…).AssertSurvivesRoundTrip()` / `.AssertWiredCorrectly()`, oracle derived from the real `SaveablePolicy.Ensure`), [`WalkerTestRig`](Assets/Game/Tests/EditMode/WalkerTestRig.cs) (real limb proportions), [`MultiplayerTestPlayerBuilder`](Assets/Game/Editor/Tests/MultiplayerTestPlayerBuilder.cs) (3-scene player build). 57 `[SetUp]` / 90 `[TearDown]`, no `[Category]`, no `[Explicit]`.

## Suites

| Area | Path | ~Tests | Notes |
| --- | --- | --- | --- |
| Locomotion & walkers | `Tests/EditMode` (+ 3 in `Editor/Tests`) | 300 / 26 files | Densest suite. Pure math: IK chains, gait, hip budget, support planes. `WalkerTestRig` shared. |
| Items & artifacts | `Editor/Tests` | 288 / 22 files | Laser staff, lasso, net gun, sprayed portals, gravel blast, repulsor, grapple, leash, hold/grip poses. |
| Persistence / save | all three dirs | 202 / 17 files | Round-trips through real JSON text; `PrefabPersistenceTests` sweeps every world-entity prefab. |
| Vehicles, mounts, flight | `EditMode` + `Editor/Tests` | 197 / 15 files | Ornithopter flight model, foil aerodynamics, mount seating/teardown, rider pose. |
| Versus / minigame / arrival | `EditMode` + `Editor/Tests` | 172 / 15 files | Match rules, win evaluation, team assignment, ship spawn rings, crash-landing arrival. |
| World, streaming, spawning | `EditMode` + `Editor/Tests` | 161 / 15 files | Chunk activation/anchors, spawn reachability, under-terrain rule, day/night, sandstorms, teleport. |
| Lobby, session, menus, settings | `Editor/Tests` | 148 / 17 files | Lobby routes/roster/layout/error paths, menu stepper + busy state, session profiles. |
| Netcode & authority | `Editor/Tests` | 126 / 11 files | `NetMessagingTests`, `NetLatchTests`, `NetworkPrefabRegistrationTests`, `NetAuthorityAndDamageTests`. Static guards only. |
| Backpack & physical inventory | `Tests/Editor` | 122 / 14 files | Pack layout/shape/surface/size, deploy arc, stow/swap, item footprints, save codecs. |
| Interaction, UI, misc | `Editor/Tests` + `Tests/Editor` | 52 / 4 files | Interactor ray/hover, suit customization, chat sanitising. |
| Agents / NPC behaviour | `Editor/Tests` | 48 / 6 files | Task planner, world sim, provocation, hostile dialog, formation math, ragdoll skeletons. |
| Portals | `Editor/Tests` | 25 / 2 files | Lifecycle + traversal. |

## Coverage gaps

Blunt: these have **zero** tests. Grep of every test file finds no mention.

- **`World/ProceduralGeneration` (68 files) — nothing.** Terrain gen, settlements, facades, bridges. The single largest untested subsystem. (`Terrain` hits in tests are `UnderTerrainRuleTests`, a safety rule, not generation.)
- **Weapons — nothing.** `Weapons/Firearms`, `Weapons/BallLightning`, `Weapons/Projectiles`, `Weapons/Core`. Artifact-style items are well covered; conventional weapons are not.
- **Audio — effectively nothing.** 7 incidental mentions of `SfxId`; no test of `AudioCatalog`, FMOD wiring, or emitters.
- **Cutscenes (`Presentation/Cutscenes`, 12 files)** — one incidental mention; only `SceneTransitionEffectsTests` is adjacent.
- **`Presentation/UI` (71 files)** — covered only where it is lobby/menu/hotbar. HUD, nameplates, damage numbers, map: untested.
- **`Vehicles/Rover`, `Presentation/Cloth`, `agents/Perception`, `World/Caves`** — nothing.
- **Play-mode / runtime behaviour** — no play-mode suite exists at all. Anything requiring `Awake`, physics, NavMesh or a frame loop is verified by hand or by the batch-mode autotest.

## Running tests

The Test Runner API is async and needs a live Editor. There is **no verified `unity -batchmode -runTests` path in this repo** — do not invent one.

| Goal | Command |
| --- | --- |
| All EditMode tests, from the Editor | menu `Tools ▸ Tests ▸ Run EditMode Tests (headless)` |
| One fixture, driven externally (MCP / script) | `SpaceGame.EditorTools.HeadlessTestRunner.RunEditModeDeferred("PrefabPersistenceTests")` |
| Read the verdict | `cat Temp/headless_tests.txt` — `PASSED=… FAILED=… SKIPPED=… INCONCLUSIVE=…`, then one line per failure, then `DONE` |

[`HeadlessTestRunner`](Assets/Game/Editor/Tests/HeadlessTestRunner.cs) deletes `Temp/headless_tests.txt` before starting, so **absence of the file means "still running", presence of `DONE` means finished**. Poll for it; never assume. `RunEditModeDeferred` survives the domain reload a code edit triggers (`SessionState`) and pumps on `EditorApplication.update` rather than `delayCall`, so it still fires when the Unity window is unfocused. It refuses to start in play mode and discards a pending request if play mode begins.

Two-process client verification (the only real proof of client-side netcode):

```
# menu: Tools ▸ Tests ▸ Build Multiplayer Test Player   (builds ../Build/MPTest/SpaceGameMP.app,
#       3 scenes only: Bootstrap, MainMenu, world/persistentScene)
# menu: Tools ▸ Tests ▸ Print Multiplayer Test Commands  → prints the exact paths + expected values
"<app>/Contents/MacOS/SpaceGameMP" -batchmode -nographics -sgmode host   -logFile /tmp/mp_host.log &
"<app>/Contents/MacOS/SpaceGameMP" -batchmode -nographics -sgmode client -logFile /tmp/mp_client.log &
grep '\[MPTEST\]' /tmp/mp_host.log /tmp/mp_client.log
"<app>/Contents/MacOS/SpaceGameMP" -batchmode -nographics -sgmode persist -logFile /tmp/mp_persist.log
```

Assert across **both** logs: `HOST_CLIENTS=2`, `CLIENT_SPAWNED > 0`, `CLIENT_PLAYER_OBJECT=True`, `CLIENT_SUPPRESSED == CLIENT_AUTHORITIES`, `CLIENT_HEALTH_SEEN == HOST_HEALTH_AFTER`, `HOST_RELAY_FROM_CLIENT=1`, and for the ship's terminal (`AutotestRunner.Terminal.cs`, see [Terminal.md](Terminal.md)) `CLIENT_TERMINAL_PAGE_SEEN == HOST_TERMINAL_PAGE == 2`, `HOST_TERMINAL_OCCUPIED=True` then `HOST_TERMINAL_RELEASED=True`. `persist` mode runs alone and checks save/quit/load (`PERSIST_CHARGES_AFTER_LOAD`, …). Extend [`AutotestRunner.Client.cs`](Assets/Game/Scripts/Core/Multiplayer/Autotest/AutotestRunner.Client.cs) with a `Report(key, value)` rather than building a second harness. To *play* a build against the Editor: `open "<app>" --args -sgprofile client` (without it both sign in as the same anonymous PlayerId and the lobby 409s).

Adjacent validation menus that are cheaper than a test run: `Tools ▸ Save System ▸ Validate Save Wiring`, `Tools ▸ SpaceGame ▸ Multiplayer ▸ Sync Network Prefabs`, `Tools ▸ SpaceGame ▸ Items ▸ Audit Held Item Poses` / `Audit Item Scale Ladder`, `Tools ▸ SpaceGame ▸ Ragdoll ▸ Audit Skeletons`.

## Headless verification

`python3 tools/typecheck.py` — **verified working on this machine**: prints `Unity 6000.3.11f1` then
`Assembly-CSharp: <n> sources | rsp <dag>` and `Assembly-CSharp: no errors.`, exit 0.

`python3 tools/typecheck.py --editor` additionally compiles **`Assembly-CSharp-Editor`** — every
prefab builder and **every test file** — against the `Assembly-CSharp` the same run just built. Use
it for any change to a runtime API. Added 2026-09-05, when a rename of `OxygenGenerator.RestoreDock`
left seven test files and four builders uncompilable and the plain run reported `No errors.`

How it works: it takes the newest `Library/Bee/artifacts/*/Assembly-CSharp.rsp` (Unity's own last
compile — exact defines, ~400 references, langversion), strips `-out:`/`-refout:` and the stale
source list, re-globs the sources, and runs Unity's bundled Roslyn
(`<UnityRoot>/Unity.app/Contents/Resources/Scripting/NetCoreRuntime/dotnet` +
`DotNetSdkRoslyn/csc.dll`). `--editor` then repeats that with `Assembly-CSharp-Editor.rsp`, **rewriting
its `-r:` for `Assembly-CSharp.ref.dll` to point at the fresh dll**.

Limits you must know before trusting a green result:

- **It does not type-check the 15 `SpaceGame.*` module assemblies.** Directories containing an
  `.asmdef` are excluded from both passes.
- Without `--editor` it skips every path with an `Editor` segment, so **no test file is compiled** —
  a test naming a type that no longer exists still prints `No errors.`
- It requires the Editor to have compiled at least once (no rsp → it exits with an explanatory
  message).
- It deliberately skips `Library/VP` MPPM clone caches, because a clone can hold a stale domain.

## Gotchas

- **`AddComponent` outside play mode raises no `Awake`, `Start` or `OnEnable`.** A component that initialises in `Awake` is a bag of nulls in a test. Initialise explicitly, or test the pure class behind the MonoBehaviour.
- **A failing run and a run that never started look identical.** Always delete `Temp/headless_tests.txt` first (the runner does) and wait for `DONE`.
- **Queue a headless run only from a CLEAN scene.** A `Unity_RunCommand` that instantiates a prefab and destroys it again (the ship placement probes do) leaves the open scene dirty, and the test framework's first step, `SaveCurrentModifiedScenesIfUserWantsTo`, then asks with a MODAL dialog — which blocks the editor loop, every bridge call after it (a `ReadConsole` sat for 30 minutes on 2026-09-05), and the run itself, until someone at the keyboard answers. Nothing is logged. Save or revert the scene in the same command before `RunEditModeDeferred`, or run the probes on a `PrefabUtility.LoadPrefabContents` copy that never touches the scene.
- **A missing type is a compile error, not a red test.** The whole EditMode suite refuses to run — the Test Runner reports nothing at all. In TDD here, "compile error naming the type" *is* the failing state.
- **The `--editor` pass MUST link the freshly built `Assembly-CSharp`, not Bee's cached `Assembly-CSharp.ref.dll`.** Against the cache, every call a runtime change just broke still resolves and the check reports success over a project that cannot compile — which is exactly what it did on its first run. The reference is named `Assembly-CSharp.ref.dll`, not `.dll`; the script now **exits** rather than warns if it finds no reference to redirect.
- **`typecheck.py` RED can also be a lie, and this one costs an hour.** It compiles Assembly-CSharp against Bee's *cached* module dlls, so a type you have just added inside an asmdef (`SpaceGame.Locomotion`, say) is reported `CS0246: could not be found` by every file that uses it until the Editor rebuilds that module — which it will not do while play mode blocks the asset refresh. The code is fine; the reference is stale. Confirm by comparing `Library/ScriptAssemblies/<module>.dll`'s mtime against the new source before believing it, or rebuild the module from its own `Library/Bee/artifacts/*/<module>.rsp` first.
- **`typecheck.py` green ≠ the project builds.** Without `--editor` it skips tests and editor code, and it always skips every module assembly (see above). It also cannot catch a player-build-only failure: `MultiplayerTestPlayerBuilder` warns that player builds compile scripts separately from the Editor.
- **MPPM clones can run a stale domain** and silently import nothing; check `Application.dataPath` before believing MCP results. `typecheck.py` excludes their rsp for the same reason.
- **`SpaceGame.Tests.EditMode` cannot reach `Assembly-CSharp`.** If the type under test is not inside a `SpaceGame.*` asmdef, the test belongs in `Assets/Game/Editor/Tests/`, not `Assets/Game/Tests/EditMode/`.
- **Host-only verification proves nothing.** The server instantiates prefabs directly and never consults the network prefab list — an unregistered prefab yields a perfect host and blank clients. `NetworkPrefabRegistrationTests` is the static guard; the two-process run is the real one.
- **A persistence round-trip must use real JSON text and a *different* instance.** Restoring onto the object you captured from passes even when the saver restores nothing; object-level round-trips hide the `Vector3`/`Quaternion` converter stack overflow.
- **`Assert.AreEqual` on a `Vector2`/`Vector3` is BITWISE, and its failure message rounds both sides to two decimals.** The same rectangle written `30 * PackGrid.Cell` and `4.05` need not be the same float, so a 1e-8 m difference fails and prints `(0.00, 0.00)` against `(0.00, 0.00)` — naming neither the axis nor the amount. Assert the components as floats with a delta and put the value in the message. Same trap the other way round: a boundary built by hand, `(1f + band) - 1f`, is one ulp *outside* a band the code tests with `<=`, so the test fails on IEEE rounding and says nothing about the system.
- **`RaycastHit.transform` is the RIGIDBODY's transform, not the collider's.** Over a prefab with one body on its root — `PlayerShip`, every vehicle — every hit anywhere reports that root, so `hit.transform.IsChildOf(part)` never matches and a filter written to *exclude* a part silently keeps it. That is how the gear wall's headroom probe came to measure the wall against its own collider. Ask `hit.collider.transform`; `Collider.transform` (from `OverlapBox`) is already right.
- The commit-block hook fires on `$(…)`, backticks and `$((`, including inside heredocs — write throwaway analysis in a Python file rather than retrying an inline shell one-liner.

## Extending

1. Decide the assembly. Type lives inside a `SpaceGame.*` asmdef → `Assets/Game/Tests/EditMode/` (and add that asmdef to the `references` list in [`SpaceGame.Tests.EditMode.asmdef`](Assets/Game/Tests/EditMode/SpaceGame.Tests.EditMode.asmdef)). Otherwise → `Assets/Game/Editor/Tests/`. Backpack/inventory work by convention goes in `Assets/Game/Tests/Editor/`.
2. Name the file `<Thing>Tests.cs`, namespace `SpaceGame.Tests`, plain `public class` (no base class, no `[TestFixture]` needed). Write the fixture *first* and confirm it fails — for a new type that means a compile error naming it.
3. Prefer testing a pure class. If the logic only exists inside a MonoBehaviour, extract the arithmetic into a plain class (as `Locomotion/Policy` did) instead of fighting `Awake`.
4. Networked? Add assertions to `NetworkPrefabRegistrationTests` / `NetMessagingTests` if it introduces a prefab or a message id, then add a `Report(...)` line to `AutotestRunner.Client.cs` and run the two-process check.
5. Holds runtime state? Add three lines to [`PrefabPersistenceTests.cs`](Assets/Game/Editor/Tests/PrefabPersistenceTests.cs) using `PersistenceProbe.For(path).Mutate(…).AssertSurvivesRoundTrip()`. `Mutate` must reach a state a *player* could reach or the test passes vacuously.
6. Run `python3 tools/typecheck.py --editor` (catches breakage in `Assembly-CSharp` *and* in every builder and test; the bare form checks the runtime assembly only), then `Tools ▸ Tests ▸ Run EditMode Tests (headless)` and read `Temp/headless_tests.txt` for `FAILED=0`.
