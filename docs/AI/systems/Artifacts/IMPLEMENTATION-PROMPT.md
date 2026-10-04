# Implementation prompt — the nine artifacts

Paste the block below into a fresh Claude Code session at the repo root. It is written to be read by
an orchestrating agent that dispatches subagents; it is not a design document.

---

## The prompt

You are the orchestrator for a multi-agent implementation. You will **not** write gameplay code
yourself. You dispatch subagents, hold the sequencing, own the shared files, and verify.

### Goal

Implement nine artifacts and the three shared systems they rest on, fully: C# gameplay code, Blender
models, shaders, prefabs, `InventoryItem` assets, icons, network-prefab registration, documentation.
Design is already done and is not up for renegotiation — read it, do not redesign it.

Designs live in `docs/AI/systems/Artifacts/`:

| Shared system | Doc |
| --- | --- |
| Status effects | `StatusEffects.md` |
| Surface coats | `SurfaceCoat.md` |
| Containment | `Containment.md` |

| Artifact | Doc |
| --- | --- |
| Flamethrower | `Flamethrower.md` |
| Foam gun | `FoamGun.md` |
| Bottled singularity | `BottledSingularity.md` |
| Strap-on booster | `StrapOnBooster.md` |
| Vacuum canister | `VacuumCanister.md` |
| Inflator nozzle | `InflatorNozzle.md` |
| Cryo sprayer | `CryoSprayer.md` |
| Storm flask | `StormFlask.md` |

Governing reference for all of it: `docs/AI/systems/Artifacts.md`, plus the `spacegame-artifact`,
`spacegame-multiplayer` and `spacegame-persistence` skills.

### Read before dispatching anything

- `docs/AI/INDEX.md`, `docs/AI/INVARIANTS.md`
- `docs/AI/systems/Artifacts.md` in full — the `Use()` / `Present()` split, `UseAuthority`, the
  15 Hz hold stream, `NetArg`, `ItemState`, and its Gotchas section
- `docs/AI/systems/SupplyCharge.md` and `SupplyGauge.md` — the tanks are this, not a new system
- `docs/AI/systems/Multiplayer.md`, `Persistence.md`
- The nine design docs and three system docs above

### Hard constraints — violating these breaks other agents' work

1. **One writer per file, always.** Two subagents may never have the same `.cs`, `.prefab`, `.asset`
   or `.blend` in scope. Assign file ownership explicitly in every brief. If two agents need the same
   file, they do not run in the same wave.
2. **Compilation is a global shared resource.** One agent's broken file freezes every other agent's
   domain reload, and a compile error over unity-mcp is invisible — you get "Will not reload
   assemblies" and silence, and the DLL mtime lies. Subagents write code; **you** compile, once, at
   the end of each wave, and you do not start the next wave until it is green.
3. **`Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset` is yours alone.** No
   subagent edits it. Each agent that needs a runtime-spawned prefab registered reports the prefab
   path in its final message; you register them in one batch per wave. (The copy at
   `Assets/DefaultNetworkPrefabs.asset` is stale and unused — do not touch it.)
4. **`ProjectSettings/TagManager.asset` is yours alone**, and layers are added through the editor
   bridge, never by hand-editing the file — hand edits are invisible to a running Editor and get
   clobbered.
5. **The Blender library is append-only and shared.** Modelling agents must re-dump object names from
   the `.blend` immediately before appending, because a parallel session may have changed it since
   they last looked. Shipped `.blend` files carry hand edits a regenerating script destroys — check
   `docs/AI/systems/ArtPipeline.md` before regenerating anything that
   already exists.
6. **Every behaviour change updates its doc in the same commit**, then
   `python3 tools/docs_check.py --index`. `INDEX.md` and `ROUTING.md` are generated — never hand-edit.
7. **Design docs graduate on ship.** When a system or artifact is implemented, move its doc from
   `docs/AI/systems/Artifacts/` up to `docs/AI/systems/`, give it the standard frontmatter
   (`system`, `layer`, `summary`, real `paths`, `symptoms`, `reads_with`, `updated`), and add a
   plain-language entry to `docs/Human/the-systems.md` — the validator fails without it.

### Quality bar — every subagent brief must carry this verbatim

- No dead code, commented-out blocks, or leftover debug logs.
- No copy-paste. If it exists in the codebase, reuse it; if it now exists twice, extract it.
- No god classes. `StatusReceiver` never grows a switch over kinds; behaviour lives with the kind.
- No magic numbers. Every value in a design doc's "numbers to start from" table is a serialized field
  on the component, tunable in the Inspector.
- No empty or silent `catch`.
- Names say what the thing is.
- Match the surrounding code's idiom, comment density and naming.

### The traps that will actually bite

Put the relevant ones in each brief:

- **A held item's `NetworkObject` is dormant, so `Network.Simulates(this)` is true everywhere.** Ask
  the owner: `OwnerIsLocal()`, or `Network.Owns(owner.transform)` in an `EffectItem`.
- **`NetArg.A` is reserved** for the slot code on presses *and* hold ticks. Item flags go in the
  upper bits of `B`; `EquipmentController` owns `B`'s low bit on hold ticks.
- **Aim comes from `AimProvider.GetAimRay()`**, never `AimTransform.forward` and never a hand-rolled
  `Physics.Raycast`. Both opt out of mounted aiming and of filtering the holder's own body.
- **A mounted rider's body is kinematic.** Velocity written to it is discarded in silence. Anything
  that must move a rider goes through the vehicle via `ITowable`.
- **A player's own movement is owner-authoritative.** A server-applied force on a player is
  overwritten within a tick with nothing in the console. The server owns the *flag*; the owner's
  movement code reads it.
- **The final hold tick arrives with `active == false`** and must stop the effect, even on a machine
  that never saw the preceding ticks. `holdTimeout` must stay above the 0.2 s keepalive.
- **Disabling a collider removes a body from every physics query.** Despawn properly; do not hide.
- **Gravity here is 18, not 9.81.**
- **Unregistered network prefabs fail only on clients.** Single-player runs as a host of one, so
  solo testing proves nothing.

### Wave plan

Run the waves in order. Within a wave, dispatch the agents in a single message so they run
concurrently. Compile and verify between waves.

**Wave 0 — foundations (2 agents, parallel)**

- `status-core`: `StatusEffects.md`. `StatusReceiver`, `StatusKind`, the per-kind behaviour classes,
  the replication, the `StatusChanged` event, **and the one movement grip seam** that `Slick` needs —
  it owns that hook so nothing else has to touch player movement later. Also the
  `StatusReactionModule` on the agent side, so no artifact learns about AI.
- `containment-core`: `Containment.md`. Capture-to-record, release-from-record, the full-container
  item identity, and `SnareStruggleMeter` as a third consumer — extract, do not copy.

**Wave 0.5 — surface coats (1 agent)**

- `surface-core`: `SurfaceCoat.md`. Patches, kinds, expiry, and consumption of the grip seam
  `status-core` built. Depends on Wave 0; do not parallelise it with Wave 0. (Historical: `Ice` and
  its collider were built here and have since been removed with the cryo sprayer's ground coats.)

**Wave 1 — art and shaders (4 agents, parallel)**

Modelling agents follow `docs/AI/systems/ArtPipeline.md`. Family: clean issued equipment — moulded shells,
colour coded, a `SupplyGauge` on every tank. Sizes bracketed against the Dragon Bazooka's 1.25 m:
sprayers ~0.5 m one-handed, Flamethrower ~0.9 m two-handed, thrown bottles ~0.2 m.

- `model-sprayers`: Flamethrower, Foam gun, Cryo sprayer
- `model-devices`: Vacuum canister, Inflator nozzle, Strap-on booster
- `model-bottles-and-props`: Bottled singularity, Storm flask, plus the world props — foam blob,
  frozen statue base, storm cloud
- `shaders`: four shader graphs — foam surface (translucent, rough, merges as one substance), frost
  sheen (grazing-angle gloss with a faint rainbow so a patch is *visible*), frozen statue (pale blue
  translucent, silhouette intact), storm cloud. Check each against `PastelQuantize.shader` so they
  survive the post pass. Owns `Assets/Game/Art/Shaders/` only.

Modelling agents append to the shared library one at a time — sequence their appends by having each
re-dump names immediately before writing.

**Waves 2a / 2b / 2c — the artifacts (3 artifacts per wave, 3 agents each, 9 concurrent)**

Ordered so each wave only depends on what is already green.

- **2a**: Flamethrower, Foam gun
- **2b**: Cryo sprayer, Inflator nozzle, Storm flask
- **2c**: Vacuum canister, Bottled singularity, Strap-on booster

Each artifact gets three agents with disjoint file ownership:

1. `<artifact>-code` — the `UsableItem` subclass and its support types under
   `Assets/Game/Scripts/Items/Artifacts/<Name>/`. Owns C# only. Reports which prefabs need network
   registration. Never edits a prefab or an asset.
2. `<artifact>-assets` — FBX export from the library, the item prefab under
   `Assets/Game/Prefabs/Items/Artifacts/Gadgets/`, the `InventoryItem` SO under
   `Assets/Game/Resources/Items/Artifacts/`, the icon, `ItemGrip` (hand, hold size, offsets, pack
   size), and hold pose. Owns assets only. Never edits C#.
3. `<artifact>-verify` — reads both, reviews against the design doc and the quality bar, checks the
   multiplayer split and the persistence claim item by item, and writes the doc graduation (move up,
   real frontmatter, `the-systems.md` entry, regenerate). Owns the doc only. Reports defects; does
   not fix code itself.

The code and assets agents in one artifact may run concurrently because their file sets are
disjoint. The verify agent runs after both return.

**Wave 3 — integration (you, plus 2 agents)**

- You: batch-register every reported prefab in `DefaultNetworkPrefabs.asset`, add any layers through
  the bridge, compile, run `python3 tools/docs_check.py --index`.
- `scale-audit`: run the item scale audit (`Tools/SpaceGame/Items`), check every new item against the
  ladder, check hold poses render correctly, check pack placement.
- `cross-review`: review the whole diff for duplication across the nine — the six tank items in
  particular. Six copies of a drain-and-refill policy is the most likely smell in this work, and the
  correct outcome is one shared component.

### Verification — evidence, not assertions

- **Type-check headlessly** rather than guessing: Bee's `Assembly-CSharp.rsp` plus Unity's Roslyn.
  See `docs/AI/systems/Testing.md`. The Unity CLI needs the Editor closed; `pipeline list` is the
  free out-of-band check.
- **A compile error over unity-mcp is invisible.** After any MCP-driven build step, refresh,
  recompile, then *measure* the result — a builder run over MCP can execute stale code and log
  success.
- **Client, not just host.** Every artifact must be seen working from an actual client. An
  unregistered prefab and a misplaced effect are both invisible solo.
- **Reload, not just save.** Every artifact with state — the tank fractions, the vacuum canister's
  captive — must be reloaded and the value found in the save JSON.
- For the artifacts that hold no state worth saving, say so explicitly in the doc rather than
  skipping the question.

### Design questions still open

These are recorded in the design docs and must be *decided and written down*, not silently resolved:

- Bottled singularity: what a pull does to a mounted rider — ignore, or route through `ITowable`.
- Inflator nozzle: whether a skinned rig scales fully (colliders, NavMesh agent radius, stride) or
  visual-plus-proxy only.
- Cryo sprayer: whether a 22° cone that freezes at one rate everywhere inside it is too strong
  against a group. The falloff that used to price that in was removed for legibility, and there is
  no playtest evidence either way yet.

Anything touching game feel, balance or player-facing behaviour consults
`docs/game-development-constitution/` — pick 1–5 principles, read them in full, cite the IDs.

### Report back

After each wave: what landed, what compiled, what is registered, what each verify agent found, and
which of the open questions got decided and how. Do not report an artifact as done until it has been
seen on a client and survived a reload.

---

## Notes for whoever runs this

- 9 concurrent agents in waves 2a–2c is the ceiling this repo tolerates. More agents do not go
  faster; they collide on compilation and on `DefaultNetworkPrefabs.asset`.
- The single largest risk is not any one artifact. It is six tank items growing six copies of the
  same drain-and-refill policy. `cross-review` in Wave 3 exists for exactly that, but the cheaper fix
  is to have `status-core` or Wave 1 land a shared `SupplyTankRegen` component that all six consume.
- Wave 0 is the only truly serial part. If time is short, cut artifacts, never cut Wave 0 — every
  one of the nine leans on it.
