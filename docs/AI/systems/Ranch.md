---
system: Ranch
layer: world
summary: "Raxy ranch quest: tame, saddle and ride an esko, then rope a wild herd into a gated corral"
paths:
  - Assets/Game/Scripts/World/Ranch/
  - Assets/Game/Scripts/agents/Modules/Social/CreatureTrust.cs
  - Assets/Game/Scripts/Core/Persistence/Adapters/CreatureTrustSaveable.cs
  - Assets/Game/Scripts/agents/AI/Goals/ScriptedStand.cs
  - Assets/Game/Editor/World/RanchStockBuilder.cs
  - Assets/Game/Editor/Tests/RanchTests.cs
  - Assets/Game/Scenes/world/gameScene.unity
  - Assets/Game/ScriptableObjects/Ranch/RaxyRanchSite.asset
  - Assets/Game/ScriptableObjects/Objectives/Ranch/
  - Assets/Game/Prefabs/Environment/Structures/NomadSettlement/RanchCorral.prefab
  - Assets/Game/Prefabs/agents/creatures/Esko.prefab
  - Assets/Game/Art/Animations/Creatures/Esko.controller
  - Assets/Game/Prefabs/agents/creatures/Esko_Ranch.prefab
  - Assets/Game/Prefabs/agents/creatures/Esko_Wild.prefab
  - Assets/Game/Prefabs/agents/Characters/Raxy/Raxy_Wrangler.prefab
symptoms:
  - "there is no wrangler in the Raxy town / nobody offers the ranch quest"
  - "the esko will not take the saddle"
  - "I put every wild esko in the corral and the quest does not finish"
  - "the wild eskos walk out of the corral with the gate shut"
  - "a roped wild esko drags me across the desert"
  - "the wrangler walks off and leaves me behind / stands still and will not lead"
  - "the wild herd is gone, or there are two herds, after loading the world"
  - "the ranch quest's lines show LANDER instead of the wrangler, or his mouth does not move"
  - "I can't find the ranch mission / Tovak is not in the town"
  - "the esko's walk and gallop look slow-motion"
  - "the esko does a somersault and lands upside down when it jumps"
reads_with: [Objectives, Saddles, Lasso, AgentSystem, Residents, NavMeshSystem, Multiplayer, Persistence]
updated: 2026-10-21
---

# Ranch

Beyond the Raxy town's animal keep stands a **ranch** — in **both worlds** (table below): a 30 × 22 m corral with
a big gate, a yard with a tame **esko**, and **Tovak**, a Raxy drover. Talk to him in the town square and he gives
the quest **Breaking Eskos** (`raxy-ranch`): follow him to the yard → pet the esko until it trusts you → saddle
it → ride it (walk / gallop / jump checklist) → ride out with him to a wild herd ~170 m south → rope all three wild
eskos **from the saddle**, tow them into the corral, let go and shut the gate → meet him at the gate.

## Model

| Idea | Mechanism |
|---|---|
| The quest is an ordinary catalog quest | `RaxyRanchQuest.asset` in `ObjectiveDirector.quests` (persistentScene), speaker `TOVAK`, no trigger: the wrangler's talk `Give`s it ([Objectives.md](Objectives.md)) |
| Steps are about one ranch | `RanchStep` (abstract `ObjectiveStep`): `site` (a [`RanchSite`](Assets/Game/Scripts/World/Ranch/RanchSite.cs) asset — wrangler's name, recorded yard/range positions) and `onBegin` (`RanchCue` None/Saddle/Lassos/Herd, laid once in `TryBegin`; waits until the ranch is loaded on the server) |
| One ranch per world | **Story**: `Ranch` in `Chunk_6_3` at (3005, 115, 418), yaw 0, herd at (3005, 250), quest in `persistentScene`'s catalog. **Spillexo**: `Ranch` in `gameScene` (the Spillexo world's scene) at (1748, 78, 829), yaw 239 (gate towards the herd at (1893, 50, 917)), quest in `gameScene`'s own `ObjectiveDirector`. Same quest, steps, site and prefabs |
| The ranch is a scene object | [`Ranch`](Assets/Game/Scripts/World/Ranch/Ranch.cs) on the `Ranch` root: `NetworkObject` + `NetRelay` (the gate's latch channel, as `SettlementNetworking.Wrap` does), the `RanchCorral` instance, marks (`WranglerHome` in town, `YardMark`, `GearRest`, `RangeMark`, `HerdRange`, `PenMark`, `DoneMark`) and one **beat** per quest step: where Tovak stands, `lead`, `run`, talk `hints` |
| Animals are found, never referenced | [`RanchStock`](Assets/Game/Scripts/World/Ranch/RanchStock.cs) (`Tame` / `Wild`, static registry). `Ranch.Tame` = nearest live Tame within `stockReach` of the yard; the herd = live Wild within `stockReach` of the corral. Creatures follow the streaming grid between chunk scenes, so a serialized scene reference would break |
| Trust gates the saddle | [`CreatureTrust`](Assets/Game/Scripts/agents/Modules/Social/CreatureTrust.cs): pets counted by the owner off `PettableModule.Petted`; `SaddleSocket.AcceptsSaddle` refuses until `IsTrusting` ([Saddles.md](Saddles.md)). Tame 3 pets, wild 6 |
| The corral is a `SettlementPen` with no stock | [`RanchCorral.prefab`](Assets/Game/Prefabs/Environment/Structures/NomadSettlement/RanchCorral.prefab): 31 `Deco_Fence_Straight` (×1.6, their box colliders are baked into the world NavMesh) and a `NomadFenceGate` (×2.2, carving leaf). Shut, the floor is a NavMesh island; `Contains` + `Gate` are what the ranch counts with |
| Roping from the saddle is what works | The esko variants weigh 700 kg (`Rigidbody.mass`; the base said 1, which the lasso read as 2 kg). On foot a roped one tows the player; seated, the rope's holder weighs the mount (`LassoArtifact.HolderMass`, [Lasso.md](Lasso.md)) and the wild one is held |
| The Esko's gaits | `Esko.prefab` `NavMeshAgent.speed` 15 (the gallop), walk 3.3 (`walkSpeedMultiplier` 0.22), acceleration 17.4 — 1.5× the Appa numbers it was cloned with, at the user's request (2026-10-21). `Esko.controller`'s Locomotion tree is foot-matched to them: Idle 0 · Walk 1.6 (timeScale 0.778) · Walk 3.3 (1.604) · Run 15 (0.971); timeScale = threshold ÷ the clip's own ground speed (Walk 2.06 m/s, Run 15.44 m/s at root scale 2.2). The second Walk sample keeps a ~1.5 m/s wander out of the Idle blend, which syncs every child to Idle's 6.7 s cycle. Flee is ×1 (above the last anchor the feet skate); Wild flees ×0.7 = 10.5 m/s, so a galloping rider closes |
| The Esko's jump | `Jump` plays `Esko_Run` held near its stretched phase (cycle offset 0.07, speed 0.12): a flying gallop for the 0.55 s the motor lifts it. The `Esko_Jump` take is the bear pack's `CanonBall`, a full 360° roll 1.1 s long that the jump cut off upside-down; it is unused now. `IsGrounded` defaults true, so a fresh Esko cannot flash into `Jump` on its first frame |
| Wild ones are skittish | `Esko_Wild`: `FleeModule` from **Neutral** (any person) inside 16 m, back to calm at 34 m, at 0.7 × speed so a galloping rider closes; `WanderModule.homeRadius` 30 keeps the herd on its range. Penned (inside, gate shut) the server re-homes it on the corral (`RanchStock.KeepIn`) |
| Tovak keeps the crew's pace | He leads by `AgentGoal` like every NPC, so he walks at a player's walking pace and runs at his run/walk ratio above it ([AgentSystem.md](AgentSystem.md) Gotchas) |
| The wrangler is rebuilt, not saved | [`RanchWrangler`](Assets/Game/Scripts/World/Ranch/RanchWrangler.cs) on `Raxy_Wrangler` (variant of `Raxy_Job_Drover`): `NpcSpawn` by the server when the chunk loads, `PutAt` the current beat's mark, despawned with the ranch. Outside any `Settlement`, so his resident parts stand down |
| His lines are the quest's | `QuestVoice` on the prefab: `ObjectiveBriefing` says every briefing through his `Speaker` (jaw, bubbles). Talk (`IDialogResponder`): before the quest the `offer` + `Give`; during it the beat's `hints` in turn; after it `afterwards`. `callOut` once, unasked, when this machine's player first comes within 14 m |

## Key types

| Type | Role |
|---|---|
| `Ranch` | SERVER think (`thinkSeconds`): spawn/direct the wrangler (`Direct`: to the beat's mark; leading, he stops and `Wait`s when nobody is within `leadRadius`, resumes inside `leadRadius × resumeShare`), `KeepPennedStock`. `Lay(cue, world)`: saddle on `GearRest`, one lasso per crew at the wrangler's feet (`ObjectiveWorld.Spawn`), `herdCount` wild via `GameServices.World.Spawn` round `HerdRange` (`SetKeepChunksLoaded(false)`). EVERY machine: `Of(site)`, `Tame`, `GateShut`, `CountHerd`, `HintsFor`; SERVER `GuideStandsFor(step)` |
| `FollowWranglerStep` | Met when he stands at this step's beat mark and a crew member is within `joinRadius`; waypoint pinned on him |
| `WinTrustStep` / `SaddleTameStep` | Met on `Tame.IsTrusting` / its `SaddleSocket.IsSaddled`; status from replicated trust / `CrewGear.AnyoneCarries(saddle)` |
| `RideTameStep` | A `LearnControlsStep` counted only while `MountModule.LocalRiderMount` is the tame animal (`CanPractise`); met on `ObjectiveWorld.AnyoneFinished` (one animal, one rider) |
| `PenHerdStep` | Met when the gate is shut and every LIVING herd animal is in the corral, at least one. Waypoint: the stray nearest this player, else the gate |
| `RanchStockBuilder` (editor) | *Tools ▸ SpaceGame ▸ World ▸ Build Ranch Stock*: the three variants, stamped (`CarriedGearItems.StampOnDisk`) and registered (`NetworkPrefabRegistrar.Sync`) |
| `ScriptedStand` | `Stand` / `PutAt` / `QuietStockModules` for a story NPC; shared with `WorkshopShellback` |

## Flows

1. **Offer.** Talk to Tovak at `WranglerHome` → `offer` line → `director.Give` (a client's goes as `GiveQuestRpc`).
2. **Yard.** Beat 1 leads (walk) to `YardMark`. Pet (RMB on its head) ×3 → saddle laid on `GearRest` → fit it (LMB) → mount
   (RMB) → move, gallop, jump in the saddle.
3. **Range.** Beat 5 (`onBegin` Herd) turns three wild ones loose round `HerdRange` and leads at a run to `RangeMark`.
4. **Pen.** Beat 6 (`onBegin` Lassos) drops a lasso per player at his feet; he runs back to `PenMark` by the gate. Rope,
   tow, release inside (LMB at nothing), shut the gate (RMB).
5. **Report.** He walks to `DoneMark`; reach him and the quest ends.

## Multiplayer

Nothing ranch-specific on the wire except `NetMsg.TrustSet` (142, animal's relay, absolute pet count, re-sent to late
joiners). Steps, waypoints and counts read replicated state: quest progress (`ObjectiveNetwork`), positions
(NetworkTransforms), the gate (`NetLatch` on the `Ranch` root), the saddle (`SaddleSocket`), seats (`MountNetworkSync`).
Talk is answered on the presser's machine. **Not yet run on a client.**

## Persistence

No ranch saver. Quest progress and `Begun` (so nothing is laid twice) are the director's; the laid saddle/lassos are
saved pickups; the wild herd are saved runtime creatures (prefab ids stamped); the tame esko is an authored scene
entity in `Chunk_6_3`; trust is `CreatureTrustSaveable` (key `trust`); the gate is `DoorSaveable`; the wrangler is
rebuilt from the quest. Penned homing is re-derived each think. **Not yet verified through a save file and a reload.**

## Gotchas

- **The gate leaf is modelled ajar and counter-rotated** (`LeftDoors` yaw −28° in `NomadFenceGate`): that IS shut. Do not
  zero it — the carving obstacle then leaves a diagonal gap animals path through.
- **The world NavMesh bake skips the carving leaf, so edit-mode paths go through the gateway**; only in play is the
  shut corral an island. After moving the corral, re-bake (`World ▸ Streaming ▸ Bake World NavMesh`, ~3 min; it refuses
  while chunk scenes are open).
- **Killing the tame esko before it is saddled stalls the quest** (it is authored once, never re-laid). Dead wild
  ones simply stop counting.
- **Never put a NetworkObject under the `Ranch` root**: it is the gate's channel; the tame esko stands beside it.
- `HerdRange`/`Yard` positions are also recorded in `RaxyRanchSite` (`Record`) for waypoints while the chunk is unloaded;
  they are the **Story** world's. Spillexo shares the site but never unloads its ranch, so it never reads them.
- **A quest exists only in the worlds whose `ObjectiveDirector` lists it**: Story and Spillexo each have their own
  catalog (persistentScene / gameScene). A ranch placed without its world's catalog entry is scenery with nobody to talk to.
- **With the Spillexo scene open in the editor, its NavMesh looks cut at the corral gate** — the leaf's carving obstacle
  carves the edit-time mesh too. The bake itself has the gateway; test paths with the leaf switched off.
- Re-saving `gameScene` stales Spillexo's NavMesh stamp: re-bake (`World ▸ Bake Open World Scene NavMesh`, ~2 min).
- Creating temporary GameObjects from an editor command lands them in the ACTIVE scene and dirties it even after
  `DestroyImmediate`; reload that scene from disk afterwards.

## Extending

- **Another ranch:** new `RanchSite`, a `Ranch` + `RanchCorral` + tame `RanchStock` in its chunk, its own quest of
  `RanchStep`s (add it to the director's catalog), one beat per step, re-bake the NavMesh.
- **This ranch in another world:** a `Ranch` root (rotate it: the gate is local −Z, the yard +Z) with the same site,
  quest and beats, a scene `Esko_Ranch` in its yard, the quest in that world's director catalog, `StampSceneIdentity` on
  the new savers, re-bake; add the world to `RanchTests.TheRanchQuestCanBeginInEveryWorld`.
- **Another kind of stock:** a variant with `RanchStock`, `CreatureTrust` (+ saver), a real `Rigidbody.mass`, and a
  `SaddleSocket` if it should be ridden; register it (`RanchStockBuilder` is the template).
- **Lines:** briefings on the step assets; hints on the `Ranch` beats; offer/call-out/afterwards on `Raxy_Wrangler`.
