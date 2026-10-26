---
system: SpriteHamletQuest
layer: world
summary: "The sprite hamlet's questline: a witch boss, a cursed guardian robot, an Oath-Chant and villager favours"
paths:
  - Assets/Game/Scripts/Gameplay/SpriteHamlet
  - Assets/Game/Scripts/Gameplay/Hazards
  - Assets/Game/Scripts/Gameplay/Objectives/Quest
  - Assets/Game/Scripts/Agents/Modules/Quest
  - Assets/Game/Scripts/Items/Artifacts/Oathbranch
  - Assets/Game/Scripts/Gameplay/ThornEruption.cs
  - Assets/Game/Scripts/Agents/Modules/Combat/BlinkModule.cs
  - Assets/Game/Scripts/Agents/Modules/Combat/BlinkArena.cs
  - Assets/Game/Scripts/Presentation/Objectives/QuestStepCutscene.cs
  - Assets/Game/Scripts/Presentation/Cutscenes/Actions/DialogueCutscene.cs
  - Assets/Game/Editor/World/QuestSiteBaker.cs
symptoms:
  - "talking to the elder does nothing for the quest on a client"
  - "the elder never offers the quest, he just repeats his ordinary lines"
  - "Merob wakes up as soon as I walk up to the hamlet gate"
  - "Merob dies instead of being freed"
  - "the witch takes no damage at all, every hit just says blocked"
  - "one freeze burned all three of the witch's herbs"
  - "the stasis bolt froze one of the keepers"
  - "the witch's strike lands with no warning on the ground"
  - "the keepers stand around and never chant"
  - "a quest waypoint never appears for a place in another chunk"
  - "the subdue step never finishes although Merob is already down"
  - "the thorn wall will not burn"
  - "I died during the witch's monologue, stuck in the dialogue while things attacked me"
reads_with: [WitchFight, Objectives, AgentSystem, Cutscenes, InteractionSystem, Multiplayer, Persistence, ArtPipeline]
updated: 2026-10-15
---

# Sprite Hamlet questline — "The Broken Oath"

A knee-to-waist-high fairy village whose guardian robot, Merob (the `LightningConjurer`), sleeps under a witch's curse. The elder sends the crew to find her hut, hands them the Oathbranch — the staff that held a stasis dome over the village — and three villagers carrying rue. The crew lead them to the hut and beat the witch — freezing her so the keepers' rue bombs can burn out the stolen herbs she lives on, while carrying the keepers out of her poison ([WitchFight.md](WitchFight.md)) — beat the woken Merob without destroying him, and guard the chant that frees him. Merob gives up his staff. Three villager favours pay out items on the side.

**Scope:** the quest's world pieces — [Gameplay/SpriteHamlet/](Assets/Game/Scripts/Gameplay/SpriteHamlet) (chant circle, the stasis dome, Merob's curse, the lightning omen), [Gameplay/Hazards/](Assets/Game/Scripts/Gameplay/Hazards) (burnable obstacles, spore haze), the reusable quest glue in [Gameplay/Objectives/Quest/](Assets/Game/Scripts/Gameplay/Objectives/Quest), the agent modules in [Agents/Modules/Quest/](Assets/Game/Scripts/Agents/Modules/Quest) plus `BlinkModule`, and the Oathbranch in [Items/Artifacts/Oathbranch/](Assets/Game/Scripts/Items/Artifacts/Oathbranch). The `Stasis` condition itself is [StatusEffects](Artifacts/StatusEffects.md); the chain machinery is [Objectives](Objectives.md).

**Assets (2026-10-07):** two site prefabs in `Prefabs/Environment/SpriteHamlet/` — `SpriteHamlet` (Tree of Life + stash lid, hedge ring of 20 m with the gate at −Z, the `OathbranchSocket` before the Tree with the planted staff and the 22 m `StasisDome`, homes, villagers, elder, keepers, favour villagers, the fallen log, the fungus stump with the basket, the well, `Merob` 33 m out with the `merob-oath` circle) and `WitchGlade` (hut with its door at +Z, Oath Stones as scenery, blink arena, monologue cutscene, lightning omen, thorn-wall cache, spore puffs, a trail of herbs and effigies along +Z). Characters in `Prefabs/Agents/Characters/SpriteHamlet/`, `Sporeling` and `Merob` in `creatures/`; four chains in `ScriptableObjects/Objectives/SpriteHamlet/`; models: [ArtPipeline](ArtPipeline.md). **Playable in `Tests/Marius test scene`** (hamlet at (−30,0,−120) yawed 180° so the gate faces the spawn, glade at (−30,0,−340), a `QuestNavMesh` surface over both, a `QuestSystems` object standing in for persistentScene with `SandboxEmptyMainChain`, and the scene switched to host-of-one). Not in any world chunk yet; see Extending.

## Model

- **Everything the world does follows the chain's saved progress.** Merob's curse (`MerobQuestState`), the keepers following (`QuestStateGate` → `EscortFollowModule`), the chant circle (`ChantCircle.runsDuring`), the witch holding still for her monologue (`QuestHoldModule`), the dome and the planted staff (`QuestStateGate`), the stash lid (`QuestStateGate`) and what each villager says (`QuestDialog`) are all **derived** from a `QuestStepWindow` over replicated, saved progress. None of them saves or sends anything.
- **Characters and places are named, not referenced.** Step assets name a `QuestActor` id ("hamlet-elder") or a `QuestSite` id ("witch-hut") because steps are assets and characters live in chunk scenes or are spawned. A site in an unloaded chunk answers from the baked `QuestSiteTable`.
- **Happenings reach steps as quest marks.** `QuestMarks.Raise(mark, id)` on the server — Talked, Dead, Subdued, Chanted, Burned — is recorded against every unfinished quest's current step (`QuestStepMemory`); a step reads `world.HasMark`. A mark counts only in the step it lands in.
- **The witch fight is its own system — [WitchFight.md](WitchFight.md).** In short: her life is bound to three stolen herbs (`HerbBoundLife`, only `Rue` in `Stasis`, one herb per stasis, the last plucked in a finisher); she prowls unseen and spins out poison spirals; the keepers hold posts and lob rue bombs that only land on a frozen witch; the player carries keepers out of the poison; three balls on any keeper fail and reset the fight.
- **The Oath-Chant frees Merob.** Three keepers at three stones advance a verse only while all are in place and on their feet; around Merob the full chant frees him. It is no longer part of the witch fight (changed 2026-10-12: a chant lowering an invisible ward read as unmotivated).
- **The dome is the hamlet's defence while Merob sleeps** (`StasisDome`): any creature not of the hamlet's faction that touches its skin is held in stasis. It stands only while the Oathbranch is in its socket — taking the staff drops it, and the player keeps the staff after the fight.
- **Nobody on the hamlet's side can die.** Keepers are stunned instead (`StunOnHitModule`; poison is counted, not stunned — [WitchFight.md](WitchFight.md)), Merob stops at a health floor (`SubduableModule`). A keeper falling to the witch's poison fails and resets that fight; it does not kill anyone.

## Key types

| Type | Role |
| --- | --- |
| [`QuestActor`](Assets/Game/Scripts/Gameplay/Objectives/Quest/QuestActor.cs) | Authored, readable id on a character or a thing; static `TryFind`; raises Dead (never on a restore) and `ReportSubdued`; `TalkRange` for the server's distance check |
| [`QuestSite`](Assets/Game/Scripts/Gameplay/Objectives/Quest/QuestSite.cs) / [`QuestSiteTable`](Assets/Game/Scripts/Gameplay/Objectives/Quest/QuestSiteTable.cs) | Named place + radius; live registry first, then `Resources/Objectives/QuestSites.asset` baked by **Tools ▸ SpaceGame ▸ Objectives ▸ Bake Quest Sites** |
| [`QuestStepWindow`](Assets/Game/Scripts/Gameplay/Objectives/Quest/QuestStepWindow.cs) | `chain` + `from` / `until` step ids; positions −1 (not taken on: the director has no `Quest` for it yet) … n (complete); `#complete` token; `IsInside` / `TryGetPosition` |
| [`QuestStateGate`](Assets/Game/Scripts/Gameplay/Objectives/Quest/QuestStateGate.cs) | Toggles GameObjects / Behaviours by a window, every machine |
| [`QuestDialog`](Assets/Game/Scripts/Gameplay/Objectives/Quest/QuestDialog.cs) | Per-window lines on a character, asked first by `DialogInteraction.Interact`; may offer a chain; finishing an entry reports Talked |
| [`QuestRequests`](Assets/Game/Scripts/Gameplay/Objectives/Quest/QuestRequests.cs) | Talked / Accepted, local → server (`QuestRequestNetwork.ReportTalkedRpc` / `RequestAcceptRpc`, beside `ObjectiveNetwork` on `NetworkGameManager.prefab`); the server checks the SENDER's body is within `TalkRange` |
| [`QuestStepMemory`](Assets/Game/Scripts/Gameplay/Objectives/Quest/QuestStepMemory.cs) | What a hamlet step remembers beyond `ObjectiveWorld`: `QuestMarks.Raise` (SERVER) records into every unfinished quest's current step; `world.HasMark(mark, id)`, `world.StepSeconds()`. Keyed by the quest's `ObjectiveWorld`, wiped when its step index moves |
| [`QuestRequestNetwork`](Assets/Game/Scripts/Gameplay/Objectives/Quest/QuestRequestNetwork.cs) | Beside `ObjectiveNetwork` on `NetworkGameManager.prefab`: a client's "finished talking to" / "accepted the quest of" a `QuestActor`, judged by the server against the sender's body (`QuestRequests`) |
| `QuestMarks` / `QuestMark` | Server event hub; the director records marks per run |
| [`StepArrival`](Assets/Game/Scripts/Gameplay/Objectives/Quest/StepArrival.cs) | "Just reached this step", once per live arrival — for presentation |
| Steps | `TalkToActorStep`, `ReachSiteStep` (`revealWaypointAfterSeconds`), `EscortStep`, `SpawnActorStep` (+ `holdSeconds`), `DefeatActorStep` (Killed / Subdued; status from the actor's `IQuestStatus`), `ChantStep`, `BurnStep`, `MoveAwayStep`, `DeliverItemStep`, `RewardStep` |
| [`ChantCircle`](Assets/Game/Scripts/Gameplay/SpriteHamlet/ChantCircle.cs) | NetworkBehaviour on the site root: keepers ↔ stone offsets, optional `centreActorId`, verses, rest, `loop`; replicated `Progress`; `VerseCompletedOnServer`; raises Chanted when a non-looping chant ends. Not saved |
| [`HerbBoundLife`](Assets/Game/Scripts/Agents/Modules/Quest/HerbBoundLife.cs) · [`RueStrikeModule`](Assets/Game/Scripts/Agents/Modules/Quest/RueStrikeModule.cs) | The witch's life and the keepers' bombs — see [WitchFight.md](WitchFight.md). `HerbBoundLife` is also the `IQuestStatus` the defeat step shows |
| [`StasisDome`](Assets/Game/Scripts/Gameplay/SpriteHamlet/StasisDome.cs) | The hamlet's dome: every `scanInterval`, living bodies straddling the skin (`radius` ± `skinThickness`) that its `StasisPass` does not pass (players + `HamletsFaction` pass) get `Stasis`. Scans on every machine so receivers exist everywhere; only the deciding machine's `Apply` acts |
| [`OathbranchArtifact`](Assets/Game/Scripts/Items/Artifacts/Oathbranch/OathbranchArtifact.cs) + [`StasisBolt`](Assets/Game/Scripts/Items/Artifacts/Oathbranch/StasisBolt.cs) | The staff: a server-authority `ToolItem` whose bolt flies (35 m/s, 45 m) on every machine, the authority's copy applying `Stasis`; 10 s recharge carried in the hotbar slot. See [Artifacts](Artifacts.md) |
| [`ThornEruption`](Assets/Game/Scripts/Gameplay/ThornEruption.cs) | The witch's rare thorn strike, an `IStrikeEffect` spawned on every machine by `LightningStrike.Present`: `spikeCount` thorns (`Prefabs/VisualEffects/Thorns/ThornSpike`) burst across the blast radius it is handed, hold, sink and self-destruct. Layout seeded from the strike point, so every machine draws the same thorns |
| [`MerobQuestState`](Assets/Game/Scripts/Gameplay/SpriteHamlet/MerobQuestState.cs) | Cursed asleep (can't wake, immune) → awake → freed (faction swap, target cleared, `Revive`) |
| [`QuestLightningOmen`](Assets/Game/Scripts/Gameplay/SpriteHamlet/QuestLightningOmen.cs) | `LightningStrike.Present` over a site on a live step arrival, every machine |
| `EscortFollowModule` (Social) · `ChantStoneModule` (Override) · `StunOnHitModule` (Scripted) · `SubduableModule` (Scripted) · `QuestHoldModule` (Scripted, `IsHolding` readable everywhere) · `BlinkModule` (side effect, hops ≤ `maxHop` to a `BlinkArena` point) + `BlinkArena` | The agent half; see [AgentSystem](AgentSystem.md) |
| [`DialogueCutscene`](Assets/Game/Scripts/Presentation/Cutscenes/Actions/DialogueCutscene.cs) + [`QuestStepCutscene`](Assets/Game/Scripts/Presentation/Objectives/QuestStepCutscene.cs) | The monologue: look at a `QuestActor`, type lines, look back; played per machine to a player near the site on a live arrival |
| [`BurnAway`](Assets/Game/Scripts/Gameplay/Hazards/BurnAway.cs) · [`SporeCloud`](Assets/Game/Scripts/Gameplay/Hazards/SporeCloud.cs) | Fire removes it (despawn + tombstone, optional Burned mark, optional burnable window) · constant spore haze hurting players inside |

## Flows

**Accept.** Elder's `QuestDialog` entry (window: before acceptance) plays its lines, then `DialogInteraction.AskQuestion` → Yes → `QuestRequests.Accepted` → server: actor loaded, sender in range, actor `Offers(chain)` → `ObjectiveDirector.StartQuest` (main's quest catalog: the quest begins, joins every machine's quest log and becomes the active quest).

**Report.** Finishing an entry's last line → `QuestRequests.Talked` → server → `QuestMark.Talked` → a `TalkToActorStep` naming that actor is met.

**Staff.** The Elder's report (`TalkToActorStep`) → `hamlet-take-staff` (`RewardStep`, actor `oathbranch-socket`) puts the Oathbranch on the ground before the Tree and is met at once; the socket's `QuestStateGate` (window: until `hamlet-take-staff`) hides the planted staff and the dome on every machine.

**Witch.** `EscortStep` met (crew + all keepers at `witch-hut`) → `SpawnActorStep` spawns Morwen and holds `holdSeconds` (28 s; the monologue runs ~23); every machine near the hut plays the `DialogueCutscene`; `QuestHoldModule` keeps her still and switches off her blink → `DefeatActorStep` (Killed): the fight itself is [WitchFight.md](WitchFight.md). The keepers' `EscortFollowModule` stays on (window until `hamlet-reforge-oath`) but their `RueStrikeModule` (Override) outranks it while she is alive and not held, so they hold their posts instead of trailing the player. Her death → `QuestLightningOmen` strikes the hamlet gate.

**Stash.** After the elder's last talk, `hamlet-stash` (`RewardStep`, actor `tree-stash` — a marker at the hollow) puts the GrapplingHook and FlameGauntlet in front of the Tree, and a `QuestStateGate` hides the lid. Spawned rather than authored behind the lid, because pickups inside the trunk's collider get shoved out by physics.

**Merob.** `MerobQuestState` wakes him once the chain leaves the cursed window (or on load) → `DefeatActorStep` (Subdued) reads the mark **or** his live `ISubduable` → `ChantStep` (circle centred on Merob) → Chanted → `RewardStep` puts `ConjurerStaff` at his feet; `MerobQuestState` frees him; a `QuestStateGate` hides the staff meshes in his hand.

## Multiplayer

| What | Carrier | Authority |
| --- | --- | --- |
| Chain progress | `ObjectiveNetwork` `NetworkList` | Server |
| Talked / accepted | `ReportTalkedRpc` / `RequestAcceptRpc`, judged on the sender's body | Server |
| Merob mode, gates, keepers' escort, circle running, hold, dome up | none — derived from progress | Every machine |
| Chant progress | `ChantCircle` `NetworkVariable<float>` | Server |
| Stasis on a body | `StatusReceiver` status message | Server (receiver's `Decides`) |
| Herbs burned, hat herbs shown | none — derived from replicated health | Every machine |
| Bolt flight | none — each machine flies its own from the use's aim ray | Owner aims, server decides |
| Witch, rewards (the staff) | registered prefabs via `World.Spawn` | Server |
| Stun, subdue floor, herb filter, stagger | damage filters / modules — run where damage is decided | Server |
| Burned obstacle gone | `World.Despawn` of an in-scene `NetworkObject` | Server (receiver's `Decides`) |
| Cutscene, lightning, dialogue lines | none | Local |

## Persistence

Only the chain is saved (`objectives`), plus what already saves itself: the witch as a runtime entity (her `Health`, and so the herbs burned), Merob's `Health` and `EntityFaction`, reward pickups (the Oathbranch, wherever it lies or whoever holds it, its recharge in the hotbar slot), tombstones for burned obstacles, the moved log's transform. The dome is derived from the step. **Not saved, on purpose:** chant progress (a reload restarts the verse), stasis (a reload thaws her), stuns and staggers, the `revealWaypointAfterSeconds` and `holdSeconds` clocks (restart after a reload). **Not yet verified by a real save/reload** — the hamlet is in no world chunk, and the test scene is a disposable session.

## Gotchas

- **An unaccepted quest has no `Quest` at all.** `Find` is null until it begins, and a begun quest raises `Started`, not `Changed` — a client adopting a fresh quest (step 0, not begun) hears only `Started`. Anything following a quest's position (`QuestStepWindow` consumers, `StepArrival`) must listen to both.
- **`QuestStepMemory.StepSeconds` counts from the first time this machine asks** about the step, not from the step change; a waypoint revealed after N seconds (`ReachSiteStep`) times from when it is first drawn.
- **`ConjurerCastModule` draws its strike from the `ConjurerStruck` broadcast alone.** `NetToAll` always dispatches locally too (the host's own RPC returns to it; offline the channel runs the handler), so the direct `PresentStrike` that `Commit` used to make as well drew every strike twice on the host — two bolts for Merob, two thorn clusters for the witch (fixed 2026-10-13). Its `strikeVFXPrefab` (formerly `lightningVFXPrefab`) takes any prefab carrying an `IStrikeEffect`; `LightningStrike.Present` hands it both ends and the blast radius.
- **A mark lands only in the current step**, so anything that can happen early is also read live (`ISubduable`, the log's position) or gated (`BurnAway.burnableDuring`), or the step waits forever.
- **`QuestActor` ids are authored and unique** (a prefab's id is shared by every instance). **Talk is judged on the server's copy of the talker's body** — keep `TalkRange` a few metres above the interaction distance, or a lagging client is "too far away to be believed".
- **`QuestStateGate` must never toggle a GameObject holding a spawned `NetworkObject`.** Gate behaviours on the character, not the character.
- **A `ChantCircle`'s keeper ids must match `QuestActor` ids, one stone each** — a missing keeper stops the chant silently ("keepers stand around and never chant"). `SpriteHamletWiringTests` checks it.
- **One stasis burns one herb, never more.** The floor is fixed when the stasis STARTS (`HerbBoundLife.OnStatusChanged`), so a herb boundary reached mid-stasis does not open the next one. The last floor is 1 HP, not 0 — the last herb is plucked ([WitchFight.md](WitchFight.md)).
- **The bolt and the dome pass the same people** (`StasisPass`: players + `HamletsFaction`). Without the faction the bolt would freeze a keeper in the line of fire; the hamlets are NEUTRAL to the crew in the global table, so a relationship check would not pass them. `SpriteHamletWiringTests` checks the lists.
- **Taking the staff drops the dome for good.** The player keeps the Oathbranch; Merob, freed, is the hamlet's guardian again. A staff dropped and lost makes the witch unbeatable — nothing respawns it.
- **Priority does not stop side-effect modules** — the controller ticks them whoever wins movement, so a Scripted hold once left her summoning at a player locked in the monologue. List them in `QuestHoldModule.silencedWhileHeld` (now her `BlinkModule`); it flips `BehaviourModuleBase.Active`, not `enabled`.
- **Her thorns must keep a ground warning.** They come up under the player, so there is no cover — the `telegraphPrefab` (since 2026-10-14 `ThornSproutWarning`, the earth heaving rather than a ring) is the whole counterplay. Until 2026-10-13 she had none and her strike was undodgeable damage on a timer. `SpriteHamletWiringTests.TheWitchsStrikeIsWarnedAndDrawn` checks it.
- **A stagger delays her cast, it does not cancel it** — `ConjurerCastModule` pauses while a higher module wins the frame and resumes after; a stasis likewise only pauses it.
- **A burnable needs an authored `StatusReceiver`** — scenery is not a body; the flame skips it.
- **A primitive collider on an imported FBX root is ×100 and turned −90° X** (ArtPipeline's FBX scale gotcha): a hedge `BoxCollider` became 460 m long and blocked the whole NavMesh. Put box/sphere colliders on an upright child whose scale cancels the root's; mesh colliders are unaffected.
- **The quest's crew is `PlayerIdentity.All` — networked players only.** Offline, reach and escort never finish.
- **The table answers only after a bake** — run *Bake Quest Sites* after moving a site prefab.

## Extending

1. **A favour:** a chain asset (new permanent id, speaker = the villager's name) with a `title` and `summary` for the quest log (J), added to the director's `quests` catalog in `persistentScene` (and in the test scene's `QuestSystems`); a `QuestDialog` entry with `offerChain`; a doing step (`MoveAwayStep`, `DeliverItemStep`, `BurnStep`) and a `RewardStep`.
2. **A new kind of quest happening:** add a `QuestMark`, raise it on the server, add a step reading it.
3. **Verify** as [Objectives](Objectives.md) says, and per stage on a client: accept from a client, watch the cutscene on a client, and the witch fight's own list in [WitchFight.md](WitchFight.md).
4. **Placing the sites:** drop both prefabs into chunk scenes (the glade's +Z trail should point at the hamlet's gate side, a few hundred metres apart), re-bake the world NavMesh, run *Bake Quest Sites*, and re-save the scenes so the in-scene `NetworkObject`s and `SaveableEntity` ids are written.
5. **Every line the characters say** is in [the story bible](../../Human/sprite-hamlet-story.md); change both together.
6. **Jumping to a step while testing:** in play mode, `ObjectiveDirector.Instance.Restore(new[] { new SavedQuest { Id = "sprite-hamlet", Order = 1, Step = "<step>", Begun = begun } })` puts the quest there (the story quest restarts unless it is in the list too). Restore to `hamlet-confront-witch` (begun false) to spawn her, then to `hamlet-defeat-witch` (begun true).
