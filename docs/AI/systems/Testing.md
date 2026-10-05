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
  - "pressing Play shows only the skybox through a stray Main Camera instead of the game"
  - "runtime objects from a test turn up saved inside persistentScene"
  - "a renamed or newly added test never runs and the old one keeps failing with identical text"
  - "someone else's compile error makes my own unrelated test change invisible"
  - "a queued headless run leaves Unity on the modal Scene(s) Have Been Modified dialog over an Untitled scene and every MCP call stops answering"
  - "Temp/headless_tests.txt says ABORTED=dirty-scene and no test ran"
  - "RunEditModeDeferred was queued but the run does not start while another session's run is going"
  - "the Scene(s) Have Been Modified dialog appears in the middle of a full test run, during Netcode BuildTests.BasicBuildTest"
  - "the editor adds objects to my scene while I am doing nothing and no agent is running"
  - "headless_tests.txt says CANCELLED, or my queued test run was discarded as too old"
  - "an autotest moves its player and it is back where it was the next frame; stand-ins never spawn beside the observer"
reads_with: [Multiplayer, Persistence, EditorTooling]
updated: 2026-10-04
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
- **What is testable:** pure readers/builders/policies (`LocomotionPolicy`, `VersusRules`, `NetArg`, save codecs) directly; MonoBehaviours by `new GameObject().AddComponent<T>()` (214 sites) — but see Gotchas, `Awake`/`Start` do **not** run.
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
| All of this project's EditMode tests (assemblies under `Assets/`; package tests excluded), from the Editor | menu `Tools ▸ Tests ▸ Run EditMode Tests (headless)`, or `RunEditModeDeferred(null)` |
| One fixture, driven externally (MCP / script) | `SpaceGame.EditorTools.HeadlessTestRunner.RunEditModeDeferred("PrefabPersistenceTests")` |
| Read the verdict | `cat Temp/headless_tests.txt` — `PASSED=… FAILED=… SKIPPED=… INCONCLUSIVE=…`, then one line per failure, then `DONE`; or `ABORTED=dirty-scene <path>` + `DONE` when a saved scene had unsaved changes and nothing ran |

[`HeadlessTestRunner`](Assets/Game/Editor/Tests/HeadlessTestRunner.cs) deletes `Temp/headless_tests.txt` before starting, so **absence of the file means "still running", presence of `DONE` means finished**. Poll for it; never assume. `RunEditModeDeferred` survives the domain reload a code edit triggers (`SessionState`) and pumps on `EditorApplication.update` rather than `delayCall`, so it still fires when the Unity window is unfocused. It refuses to start in play mode and discards a pending request if play mode begins. A deferred request also **waits while any other Test Framework run is in flight** (another session's, the Test Runner window's). Before starting it settles the scene question itself so the framework never prompts: a dirty **untitled** scene is discarded by reopening `Bootstrap.unity` (`OpenSceneMode.Single`); a dirty **saved** scene makes it write `ABORTED=dirty-scene <path>` + `DONE` to the result file, `LogError`, and not run. After the run it reopens Bootstrap again if a dirty untitled scratch is left active. **Every request expires `HeadlessTestRunner.MaxRequestAge` (10 min) after it was made:** a pending request older than that is discarded with a warning instead of started, and a run still going that long after it started is cancelled through `TestRunnerApi.CancelTestRun` — the result file then reads `CANCELLED: …` followed by `DONE`. Re-request rather than waiting past the limit.

Two-process client verification (the only real proof of client-side netcode):

```
# menu: Tools ▸ Tests ▸ Build Multiplayer Test Player   (builds ../Build/MPTest/SpaceGameMP.app,
#       Bootstrap, MainMenu, world/persistentScene and 11 chunk scenes: the settlement's Chunk_6_3 with its
#       neighbours, and the chunks around the spawn in Chunk_7_5)
# menu: Tools ▸ Tests ▸ Print Multiplayer Test Commands  → prints the exact paths + expected values
"<app>/Contents/MacOS/SpaceGameMP" -batchmode -nographics -sgmode host   -logFile /tmp/mp_host.log &
"<app>/Contents/MacOS/SpaceGameMP" -batchmode -nographics -sgmode client -logFile /tmp/mp_client.log &
grep '\[MPTEST\]' /tmp/mp_host.log /tmp/mp_client.log
"<app>/Contents/MacOS/SpaceGameMP" -batchmode -nographics -sgmode persist -logFile /tmp/mp_persist.log
# the nomad settlement: host and client, then one process through a save and reload
"<app>/Contents/MacOS/SpaceGameMP" -batchmode -nographics -sgmode settlement-host   -logFile /tmp/mp_settlement_host.log &
"<app>/Contents/MacOS/SpaceGameMP" -batchmode -nographics -sgmode settlement-client -logFile /tmp/mp_settlement_client.log &
"<app>/Contents/MacOS/SpaceGameMP" -batchmode -nographics -sgmode settlement-persist -logFile /tmp/mp_settlement_persist.log
# the settlement's expedition band: host and client, then one process through a save and reload
"<app>/Contents/MacOS/SpaceGameMP" -batchmode -nographics -sgmode expedition-host   -logFile /tmp/mp_expedition_host.log &
"<app>/Contents/MacOS/SpaceGameMP" -batchmode -nographics -sgmode expedition-client -logFile /tmp/mp_expedition_client.log &
"<app>/Contents/MacOS/SpaceGameMP" -batchmode -nographics -sgmode expedition-persist -logFile /tmp/mp_expedition_persist.log
```

The player's executable is `SpaceGame` inside `SpaceGameMP.app/Contents/MacOS` (the product name), not `SpaceGameMP` as the older command lines above say. A player build compiles every script itself and refuses with "Type ... has an extra field ... can't be serialized" while another session is half-way through changing a serialized type: wait and rebuild.

The settlement modes also log, per machine, `HOST_BODIES` / `CLIENT_BODIES` (every 5 s for 70 s after the settle, and at each census): one line led by the clock minute with, per resident, the replicated activity, place, seat and cart ids, whether the seat and the cart really are held (`+`/`-`), the tool in the hand, the `HoldStyle` and the Full/Upper loops playing (`AutotestProbes.TakeBodyLine`). Pair the host's and the client's lines by minute: a resident that differs is a body the client derives differently. `settlement-persist` also leaves a cart off its authored pose before saving (`PERSIST_CART_MOVED_METRES`, `PERSIST_SAVE_CART_RECORDS`) and checks it after the reload (`PERSIST_CART_RESTORED=True`). **Written 2026-10-03 and compiled, not yet run: the player build was attempted and did not finish (the editor recompiled mid-build), so the animation features have still not been seen on a real client or through a reload.**

The settlement modes (`AutotestRunner.Settlement.cs`) put the host's player in the settlement, because no chunk loads unless somebody stands near it, and count with `AutotestProbes.TakeSettlementCensus`: residents (and how many are hidden indoors, hold a place, stand within 0.15 m of its stand point, climb), penned stock and how much of it is still inside a pen, gates and how many are open. The client never leaves the spawn and must count the same as the host (`CLIENT_RESIDENTS == HOST_RESIDENTS`, `CLIENT_STOCK == HOST_STOCK`) and see the gate the host opened (`CLIENT_GATE_OPEN_SEEN=True`). `settlement-persist` opens a gate, saves, reloads the world and counts again: every `PERSIST_AFTER_LOAD_*` must equal its `PERSIST_BEFORE_SAVE_*`. `AutotestProbes.FindSettlement` is the loaded settlement **with residents**: the culture-less astronaut colony loads in the same 3x3 and has no society.

The expedition modes (`AutotestRunner.Expeditions.cs`, probes in `AutotestProbes.Expeditions.cs`) send one band of the nomad settlement out and home, see [Expeditions.md](Expeditions.md). They **drive** time instead of waiting: the clock is set to the departure hour and jumped over the muster, and jumped past `walkLimitMinutes` only when the walk-out or walk-in did not finish by itself (`*_HANDOFF_BY` / `*_HOMECOMING_BY` say which rule fired). The host's player is the observer and is moved to where each rule needs it — after it has stood up from the crash-landed ship (`*_ARRIVAL_SEAT`, Gotchas), and every move is checked to have stuck (`*_<STEP>_OBSERVER_PLACED`: within 10 m, on loaded ground): behind the muster spot, `handoffObserveRadius` + 50 m out, while the band walks out (unobserved, so the hand-off is the 150 m one and the band folds); 120 m from the folded band to make its stand-ins real; back behind the settlement so it folds for `SimulateDays(2)`; and 40 m from the hand-off point for `SendHome`, so the walk-in swaps stand-ins for residents. Each mode ends `HOST_EXP_PASS` / `CLIENT_EXP_PASS` / `PERSIST_EXP_PASS`, plus `*_FAILED=` naming every failed check with what it saw. Across the two logs `CLIENT_EXP_STAND_INS == HOST_EXP_STAND_INS` and `CLIENT_EXP_STAND_IN_LINE == HOST_EXP_STAND_IN_LINE` (key, name and the item in hand per stand-in). `expedition-persist` checks the save text itself (`entries.expeditions` holds the band and its members, its `npcworld` group record has `owner: expedition`) and, after the reload, the same band on the same stage, no second band past the hand-off, and its residents still away and hidden. **Run 2026-10-04: all three passed; after the settlement was regenerated, host and client failed `STAND_INS=0` until the observer stood up from its arrival seat first.** They need the Phase 0–1 editor steps done first (director wired into `persistentScene`, site catalog baked, muster spot placed, resident prefabs prepared): a missing piece fails the first check that needs it (`*_DIRECTOR`, `*_BAND_RAISED`, `*_MUSTERED_WITH_KIT`, `*_STAND_INS`).

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
- **`Scene(s) Have Been Modified` is a MODAL dialog, and a headless run used to raise it.** The Test Framework's first task, `SaveModifiedSceneTask`, calls `EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo` whenever any loaded scene is dirty. Modal = the editor loop, the run and every MCP session sharing the editor stop until someone clicks; nothing is logged (a `ReadConsole` sat 30 minutes on 2026-09-05; four agent tasks blocked on 2026-09-24). Two ways to meet it: a probe/builder left the open scene dirty, or **a second run started while another was in flight** — an EditMode run lives inside an untitled scratch scene (`CreateBootstrapSceneTask`) its tests dirty, and `TestRunnerApi.Execute` happily starts a second job beside it (Editor.log 2026-09-24: `Executing IPrebuildSetup` at line 613640 while the run begun at 604737 was still logging test output, which then continued after the second run's `IPostBuildCleanup`). [`HeadlessTestRunner`](Assets/Game/Editor/Tests/HeadlessTestRunner.cs) now closes both: a deferred request waits for `TestRunnerApi.IsRunActive()` (internal — read by reflection; it logs an error if a package upgrade removes it) to go false, then **discards a dirty untitled scene silently** (reopening Bootstrap — untitled scratch never holds work) and **refuses to run over a dirty saved scene**, writing `ABORTED=dirty-scene <path>` then `DONE` so a poller sees why nothing ran. Save or revert that scene and re-queue. It does not guard runs started some other way (the Test Runner window, MCP `run_tests`) — those still prompt over a dirty scene. The post-run cleanup waits for the framework's own `RestoreSceneSetupTask`; `RunFinished` fires *before* it, while the scratch scene is still active.
- **Package tests can raise `Scene(s) Have Been Modified` mid-run, where no pre-run check reaches.** `com.unity.netcode.gameobjects` is an *embedded* package (`Packages/`), so the Test Framework includes its tests — among them `Unity.Netcode.EditorTests.BuildTests.BasicBuildTest`, a real `BuildPipeline.BuildPlayer` (to `Builds/BuildTests`) executed while the run's dirty untitled scratch scene is active. On 2026-09-24 at 19:58:52 the editor froze right after that build's `IPreprocessBuildWithReport` callbacks (last log line `ProjectUnlinkBuildWarning.cs Line: 29`, stack through `BuildTests.cs:27`) with the dialog up over `Untitled*`. So a `HeadlessTestRunner` run **with no group filter is narrowed to the assemblies compiled from `Assets/`** (`IsProjectAssembly`, via `CompilationPipeline.GetAssemblies`): the full suite then runs ~3280 tests in about two minutes with no player build. An explicit group filter still reaches package tests — do not name a package fixture from an agent in a shared editor. The Test Runner window's *Run All* is not narrowed.
- **An interrupted run leaves you sitting IN the tests' scratch scene, and the next Play runs that.** The Test Runner opens an untitled scene for the run and puts the old scene setup back afterwards through `RestoreSceneSetupTask`. That task calls `EditorSceneManager.NewScene`, which throws `InvalidOperationException: This cannot be used during play mode` if anything has entered play mode meanwhile — so the restore never happens and the scratch scene stays open, dirty and unnamed. Pressing Play then runs a scene whose only camera is Unity's default `Main Camera` at (0, 1, −10): **the game appears to boot into empty skybox through a camera nobody can find**, because it is not your game at all. The give-away in the Hierarchy is `Main Camera` + `Directional Light` beside leftover test objects (`player`/`eye`, `Portal Primary (Player)`, `LobbyPreviewAnchor (temporary)`), and in the console `TestRunner: Unexpected assembly reload happened while running tests`. Recover by reopening the real entry scene — `EditorSceneManager.OpenScene("Assets/Game/Scenes/Core/Bootstrap.unity", OpenSceneMode.Single)`, which discards the scratch scene without a modal prompt. **Never save while that scene is open**: this is how five `Portal Primary (Player)` objects and, before them, four `*_MountThirdPersonCamera` objects came to be committed inside `persistentScene.unity` — the tests all tear down correctly, the run just never reached their `[TearDown]`.
- **An unattended test run writes into YOUR scene.** EditMode tests build their fixtures (`player`, `eye`, `mount`, `Rider`, the netcode package's `GetBehaviourIndexOne` with two missing-script components) in the open scene setup, and the Test Runner resumes a run on its own after a domain reload. So a run an agent queued and then cut short with a script edit could come back much later, with nobody watching, and whatever it left behind got saved with the scene — `Bootstrap.unity` held a `GetBehaviourIndexOne` object and `player`/`eye` pairs for several commits. That is why requests and runs now expire after `MaxRequestAge`; do not raise it to cover a slow suite — run a narrower fixture instead.
- **A missing type is a compile error, not a red test.** The whole EditMode suite refuses to run — the Test Runner reports nothing at all. In TDD here, "compile error naming the type" *is* the failing state.
- **The `--editor` pass MUST link the freshly built `Assembly-CSharp`, not Bee's cached `Assembly-CSharp.ref.dll`.** Against the cache, every call a runtime change just broke still resolves and the check reports success over a project that cannot compile — which is exactly what it did on its first run. The reference is named `Assembly-CSharp.ref.dll`, not `.dll`; the script now **exits** rather than warns if it finds no reference to redirect.
- **`typecheck.py` RED can also be a lie, and this one costs an hour.** It compiles Assembly-CSharp against Bee's *cached* module dlls, so a type you have just added inside an asmdef (`SpaceGame.Locomotion`, say) is reported `CS0246: could not be found` by every file that uses it until the Editor rebuilds that module — which it will not do while play mode blocks the asset refresh. The code is fine; the reference is stale. Confirm by comparing `Library/ScriptAssemblies/<module>.dll`'s mtime against the new source before believing it, or rebuild the module from its own `Library/Bee/artifacts/*/<module>.rsp` first.
- **`typecheck.py` green ≠ the project builds.** Without `--editor` it skips tests and editor code, and it always skips every module assembly (see above). It also cannot catch a player-build-only failure: `MultiplayerTestPlayerBuilder` warns that player builds compile scripts separately from the Editor. **And a player build beside the Editor's snapshot used to make it RED for nothing.** `newest_rsp` took the newest `Assembly-CSharp.rsp` under `Library/Bee/artifacts`, and a player build writes one that defines **no `UNITY_EDITOR`** — so Assembly-CSharp came out missing every member behind an `#if UNITY_EDITOR`, and the `--editor` pass then reported the builders calling them as errors in files nobody had touched (2026-09-08: `StructureAmbientMotion.SetHandles`, `TerrainFeatureSpawnerVisuals.LoadPresetMaterial`, both of which exist). The give-away is the `rsp <dag>` the run prints: the two passes named DIFFERENT dags. It now prefers a snapshot that defines `UNITY_EDITOR` and breaks ties by mtime. **If a red names a member you can `grep` in `Assets/`, check that line before believing it.**
- **One broken assembly freezes the loaded domain for EVERY assembly, and your own test then keeps running in its pre-edit form.** A compile error anywhere — in this project's case `SpaceGame.Tests.EditMode`, broken by a signature change in a *different* agent's work — makes Unity log `Editor compiler errors found. Will not reload assemblies.` and refuse the swap. Your assembly compiled fine and `Library/ScriptAssemblies/Assembly-CSharp-Editor.dll` on disk *does* contain your new test (`strings <dll> | grep <TestName>`), but the `AppDomain` still holds the old one, so a renamed or newly added test never runs and a deleted one keeps failing with **byte-identical text across every run**. `ImportAsset(ForceUpdate)`, `RequestScriptCompilation(CleanBuildCache)` and `EditorUtility.RequestScriptReload()` all recompile and none of them help. **Check `grep 'Will not reload assemblies' Editor.log` and then `grep 'error CS' Editor.log | tail` before believing any red test, and before concluding anything about your own change** — the errors may name a file you have never opened. Do not chase the `Hotreload:` lines in the log; they are routine and were a red herring here. Same family as the MPPM stale domain below: the assembly on disk is right and the one answering questions is not.
- **MPPM clones can run a stale domain** and silently import nothing; check `Application.dataPath` before believing MCP results. `typecheck.py` excludes their rsp for the same reason.
- **`SpaceGame.Tests.EditMode` cannot reach `Assembly-CSharp`.** If the type under test is not inside a `SpaceGame.*` asmdef, the test belongs in `Assets/Game/Editor/Tests/`, not `Assets/Game/Tests/EditMode/`.
- **A new world straps every player into the crash-landed ship, and a seat puts its rider back every frame.** `SeatedRider.HoldSeats` writes an owned body onto its chair in each `LateUpdate` until the player stands up (Q) or `ArrivalDirector`'s `strandedSeatTimeout` (180 s) turfs everyone out, so an autotest's `NetworkedTeleport.Move` of its player is undone on the next frame with nothing logged. A mode that stages no world, or `StageNew` without `disposable`, flies the arrival. The expedition modes waited 4 minutes by luck on the old settlement layout (the band walked its whole walk-out) and then lost the race on the new one (it skipped to its hand-off unseen, the watch began inside the 180 s): the "observer" never reached the band and no stand-in spawned. Stand up first (`SeatedRider.RequestLocalRelease` once `LocalPlayerMayLeave`) or stage a disposable world, and check a move took.
- **A client that joined mid-stream can hold two copies of a chunk** (Multiplayer.md Gotchas). `expedition-client` then read the never-spawned copy: `SEEN_TWICE (4)`, `RESIDENT_BODIES (144 bodies, 70 residents + 4 stand-ins)`, `MEMBERS_BACK` at once, so it ran minutes ahead of the host and counted the stand-ins still on the road as `STAND_INS_AFTER_SWAP (4)`. It now stops at `CLIENT_EXP_SETTLEMENT_COPIES` instead. And `LeaveArrivalSeat` must wait for the arrival to be over (`ArrivalDirector.IsPending` false) before asking whether the player is seated: the body exists before the seat takes it (`PERSIST_EXP_ARRIVAL_SEAT=not seated`, then `WALKOUT_OBSERVER_PLACED` 425.9 m off).
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
