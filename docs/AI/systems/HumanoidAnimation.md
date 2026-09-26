---
system: HumanoidAnimation
layer: presentation
summary: "One generated controller for every humanoid, its action assets, and the cues and moments that trigger them"
paths:
  - Assets/Game/Scripts/Presentation/Animation
  - Assets/Game/Editor/Animation
  - Assets/Game/Art/Animations/Humanoid
  - Assets/Game/ScriptableObjects/Animation
  - Assets/Game/Resources/Animation
  - Assets/ThirdParty/Quaternius
  - Assets/ThirdParty/CMU
  - Assets/Game/Editor/AssetPipeline/CmuClipImporter.cs
  - Assets/Game/Scripts/Gameplay/Interaction/Core/IInteractionMoment.cs
symptoms:
  - "[CharacterActions] 'X' has no state for action 'Y' — it was added after the controller was built"
  - "an action plays on the owner's screen and never on anyone else's"
  - "a gesture restarts halfway through on a remote player's body"
  - "a Quaternius clip plays with the character turned round, facing away from where it walks"
  - "every NPC of one prefab stands in the same idle and steps off on the same foot"
  - "Creating missing CharacterActions component for PlayerEmotes in PlayerCharacterNetworked"
  - "a humanoid preview render stands in bind pose while the numbers say the bones moved"
  - "an NPC's punch never lands although the arm visibly reaches you"
  - "an NPC talks with its arms hanging still"
  - "a moment is raised but the body does nothing"
  - "every CMU clip shorter than a second comes out exactly one second long"
  - "every clip cut from one CMU take plays the same motion"
  - "the player bows or falls over when picking something up"
  - "every NPC throws the same jab and cross"
  - "an aimed arm snaps level on every shot"
  - "a player's punch shows BLOCKED and does no damage"
reads_with: [PlayerCharacter, AgentSystem, ArtPipeline, Multiplayer, Combat, InteractionSystem, TalkingMouth]
updated: 2026-09-25
---

# Humanoid Animation

The player and every humanoid NPC (drifters, nomads, sky nomads, patrol robots) wear ONE Animator
controller, generated from data. Actions — a wave, a strike, a flinch, the talking loop — are assets.
Gameplay rarely names an action: it reports a **moment** ("picked something up", "said a line") or
asks for a **cue** ("greet"), and data decides which of the tagged actions plays.

**Scope:** runtime [Presentation/Animation/](Assets/Game/Scripts/Presentation/Animation), builder
[Editor/Animation/](Assets/Game/Editor/Animation), controller and masks
[Art/Animations/Humanoid/](Assets/Game/Art/Animations/Humanoid), data
[ScriptableObjects/Animation/](Assets/Game/ScriptableObjects/Animation) (actions, cues) and
[Resources/Animation/](Assets/Game/Resources/Animation) (catalog, reaction table, emotes), importers
[QuaterniusClipImporter.cs](Assets/Game/Editor/AssetPipeline/QuaterniusClipImporter.cs) and
[CmuClipImporter.cs](Assets/Game/Editor/AssetPipeline/CmuClipImporter.cs).
**Related:** [PlayerCharacter.md](PlayerCharacter.md), [AgentSystem.md](AgentSystem.md), [Multiplayer.md](Multiplayer.md), [InteractionSystem.md](InteractionSystem.md), [TalkingMouth.md](TalkingMouth.md).

## Model

- **The controller is output, never edited.** [`HumanoidControllerBuilder`](Assets/Game/Editor/Animation/HumanoidControllerBuilder.cs) writes [`Humanoid.controller`](Assets/Game/Art/Animations/Humanoid/Humanoid.controller) from the [profile](Assets/Game/ScriptableObjects/Animation/HumanoidAnimationProfile.asset) (locomotion set, hold poses, raises, glide, blend times) plus every [`CharacterAction`](Assets/Game/Scripts/Presentation/Animation/CharacterAction.cs) under `ScriptableObjects/Animation/Actions/` (302 today). Same GUID as the old `AstronautArmature.controller`, so every prefab and scene kept its reference.
- **Layers, bottom to top** ([`HumanoidLayers.Order`](Assets/Game/Scripts/Presentation/Animation/HumanoidLayers.cs)): `Base Layer` (Move/Crouch/Air/Land/Sit) · `Upper Body` (hold poses, raises — PlayerAimRig/HoldAnimator own its weight) · `Worn Left` · `Action Full` · `Action Upper` · `Action Left Arm` · `Action Right Arm` · `Action Additive` (blend mode Additive, upper-body mask: the gun recoil kicks on top of whatever pose is below, at any aim pitch) · `Glide`.
- **An action** = a slot (which action layer), a playback (OneShot / Loop / EnterLoopExit), variants (a clip, or a Down/Level/Up blend on `AimPitch`), fades, a speed range, phase marks (Contact, Release), **cues** it can express and optional **postures** it fits (none ticked = by slot: Full only standing still, the rest anywhere). One state per variant; **no triggers, no Any State transitions** — [`CharacterActions`](Assets/Game/Scripts/Presentation/Animation/CharacterActions.cs) crossfades straight to the state by name hash.
- **The vocabulary is three layers.** A [`CharacterCue`](Assets/Game/Scripts/Presentation/Animation/CharacterCue.cs) is a word (`greet`, `talk`, `gesture`, `hurt`, `pickup` … 54 of them in [Cues/](Assets/Game/ScriptableObjects/Animation/Cues), each with a meaning and an optional fallback cue); actions list the cues they answer, and the catalog indexes cue → actions at runtime (nothing to rebuild when tags change). A [`CharacterMoment`](Assets/Game/Scripts/Presentation/Animation/CharacterMoment.cs) is something gameplay reports; the [`MomentReactions`](Assets/Game/Scripts/Presentation/Animation/MomentReactions.cs) table ([default](Assets/Game/Resources/Animation/MomentReactions.asset), per-body override) maps each moment to a cue (or an exact action), a chance and a cooldown.
- **[`BodyLanguage`](Assets/Game/Scripts/Presentation/Animation/BodyLanguage.cs)** on every humanoid resolves them: `React(moment)` (table-driven one-shot), `Express(cue)` (one-shot), `Hold(cue)`/`Release(cue)` (a loop for as long as a state lasts), `Pick(cue)` (decide without playing). A pick keeps only actions that fit the body's [`BodyPosture`](Assets/Game/Scripts/Presentation/Animation/BodyPosture.cs) (read off `Seated`/`IsGrounded`/`IsGliding`/speed params), never a full-body action when `fullBody` is off (the player), loops only for `Hold`, and never the action it picked last for that cue while another fits.
- **Parameters** are listed once in [`HumanoidParams.Contract`](Assets/Game/Scripts/Presentation/Animation/HumanoidParams.cs); the builder writes exactly those and the tests check them.
- **Clips:** Mixamo locomotion and the Blender gestures in `Art/Animations/Player`, a Kevin Iglesias subset, Quaternius UAL1 (42 clips, CC0) and 288 cuts from 253 CMU mocap takes (251 actions under `Actions/CMU/<Category>/`, credit in `THIRD_PARTY_NOTICES.md` at the repo root). Every library clip is played by some action; Audit lists any that is not.

## Key types

| Type | File | Role |
|---|---|---|
| `CharacterAction` | [CharacterAction.cs](Assets/Game/Scripts/Presentation/Animation/CharacterAction.cs) | The action asset; `Cues`, `Postures`/`Fits`, `SecondsTo(mark)` for authority timers |
| `CharacterActions` | [CharacterActions.cs](Assets/Game/Scripts/Presentation/Animation/CharacterActions.cs) | `Play/Stop/IsPlaying/PlayingOn`, `PlayEverywhere` (server → `NetMsg.CharacterActed`), layer weight from state |
| `CharacterActionCatalog` | [CharacterActionCatalog.cs](Assets/Game/Scripts/Presentation/Animation/CharacterActionCatalog.cs) | Builder-written list; index = wire id within a build; `TryFind` by name, `ActionsFor(cue)`, `Cues`, `TryFindCue` |
| `CharacterCue` | [CharacterCue.cs](Assets/Game/Scripts/Presentation/Animation/CharacterCue.cs) | A vocabulary word; `Meaning`, `Fallback` |
| `CharacterMoment` | [CharacterMoment.cs](Assets/Game/Scripts/Presentation/Animation/CharacterMoment.cs) | Append-only enum of what gameplay raises; a value exists only where something raises it |
| `MomentReactions` | [MomentReactions.cs](Assets/Game/Scripts/Presentation/Animation/MomentReactions.cs) | Moment → cue/action, chance, cooldown. A row with neither silences the moment |
| `BodyLanguage` | [BodyLanguage.cs](Assets/Game/Scripts/Presentation/Animation/BodyLanguage.cs) | `React`, `ReactEverywhere`, `Express`, `Hold`/`Release`, `Pick`; static `Choose` is the pick rule |
| `SpeechGestures` | [SpeechGestures.cs](Assets/Game/Scripts/Presentation/Animation/SpeechGestures.cs) | NPCs: holds `talking` while the popup types their line, raises the speech moments |
| `AnimatorAuthority` | [AnimatorAuthority.cs](Assets/Game/Scripts/Presentation/Animation/AnimatorAuthority.cs) | Who writes a body's actions |
| `IdleVariation` | [IdleVariation.cs](Assets/Game/Scripts/Presentation/Animation/IdleVariation.cs) | `IdleIndex` from seed + server clock; per-body `CycleOffset`; `IdleFidget` once per clock bucket |
| `HurtReaction` | [HurtReaction.cs](Assets/Game/Scripts/Presentation/Animation/HurtReaction.cs) | Server: `HealthComponent.OnDamage` → `ReactEverywhere(Hurt)` |
| `IInteractionMoment` | [IInteractionMoment.cs](Assets/Game/Scripts/Gameplay/Interaction/Core/IInteractionMoment.cs) | What an interactable's press shows on the presser; default `Interacted` |
| `HumanoidControllerBuilder` | [HumanoidControllerBuilder.cs](Assets/Game/Editor/Animation/HumanoidControllerBuilder.cs) | Rebuild / Audit (also lists unanswered cues); `CollectActions`, `CollectCues` |
| `CharacterActionWiring` | [CharacterActionWiring.cs](Assets/Game/Editor/Animation/CharacterActionWiring.cs) | Adds CharacterActions, BodyLanguage (full body off on the player), IdleVariation, HurtReaction, and SpeechGestures on NPCs |
| `AnimationLibraryWindow` | [AnimationLibraryWindow.cs](Assets/Game/Editor/Animation/AnimationLibraryWindow.cs) | Browse actions/cues/moments, filter by cue/slot/playback, play any of them on the selected body in play mode |
| `EmoteCatalog` | [EmoteCatalog.cs](Assets/Game/Scripts/Characters/Player/EmoteCatalog.cs) | The player's emotes (chat words + the emote wheel, hold V), 46 today; each references an action that must be in this catalog (`EmoteCatalogAssetTests`) — see [PlayerCharacter.md](PlayerCharacter.md) |
| `CharacterActionAuthoring` | [CharacterActionAuthoring.cs](Assets/Game/Editor/Animation/CharacterActionAuthoring.cs) | One action per selected clip, skipping clips an action already plays |

Menus: `Tools/SpaceGame/Animation/` **Generate Recoil Clips**, **Rebuild Humanoid Controller**, **Audit Humanoid Controller** (writes nothing but a scratch copy), **Wire Humanoid Prefabs**, **Create Actions From Selected Clips**, **Animation Library**. In game: **`/act <action or cue>`** plays any action by name or any cue on your body (`/act` alone lists the cues).

## Flows

1. **Rebuild:** content check (refuses null or non-humanoid clips, duplicate names, half-aimed variants, loops on one-shot clips, empty cue slots) → catalog → masks → scratch controller → `AnimatorDescription` compare → if different, the scratch FILE is copied over the live one (`.meta` and GUID kept) → override controllers per locomotion variant → rebake the player's NetworkAnimator → `Verify()` from disk. Unchanged content writes nothing.
2. **Play:** `Play(action, arm?, variant?)` → writer check → `HasState` (error naming Rebuild if missing) → slot mirror/speed params → weight 1 → `CrossFadeInFixedTime`. A running Loop asked again is a no-op, so gameplay may re-assert every frame. A Full action stops the other slots. One-shots exit to `Empty` by themselves; `Update` drops the weight to 0 once the layer rests there.
3. **A moment:** `BodyLanguage.React(component, moment)` → the body's row (own table, else default) → cooldown → seeded chance roll → cue resolved to a fitting one-shot (following fallbacks) → `Play` with a picked arm and variant. Raised today by: `HurtReaction` (Hurt, server), `SpeechGestures` (ConversationStarted, QuestionAsked, Exclaimed, SpeechBeat), `IdleVariation` (IdleFidget), `Interactor` (Interacted / PickedUp / whatever `IInteractionMoment` says; mounts, seats, terminals, dialog and petting say None), `ChatterModule.PresentWarCry` (WarCry), `NpcTaskModule` dwell end (TaskFinished, server).
4. **Talking:** `SpeechGestures.Update` → the popup is typing a line whose `Speaker` is this body → first line after `newConversationAfter` s of silence raises ConversationStarted (greet), else a `?`/`!` line raises QuestionAsked/Exclaimed → `Hold(talking)` every frame (standing: the Talk loops, full or upper; moving: upper talk loops; seated: Talk Seated / upper loops) → a SpeechBeat every `beatEvery` s (16 CMU talk beats + Talk Story, mirrored at random) → typing stops → `Release`.
5. **NPC melee** ([CloseCombatModule.cs](Assets/Game/Scripts/agents/Modules/Combat/CloseCombatModule.cs)): the authority picks a move from `attackCue` (`brawl` on unarmed humanoids: Unarmed Strike, Punch Combo, Punch Combo Left, Boxing Flurry, Kick Front, Knee Strike; `swordplay` on the sword robot: Sword Strike, Overhead Chop, Two Hand Swing) with `BodyLanguage.Pick` — posture-fitted, no immediate repeat, else `attackAction` — and its variant, broadcasts both in `AgentActed.B = variant | (catalogIndex + 1) << 8` (0 in the high bits = the receiver's own `attackAction`), and lands the blow at that variant's contact (`Variant.contactAt`, measured from the mocap per variant; 0 = the action's mark) on its own clock (`Blow`: wait → land if in reach → drop if dodged or more than `contactGrace` late). The commit covers the whole move. Creatures without `attackAction` keep their trigger and immediate damage.
6. **Block / dodge** ([MeleeDefense.cs](Assets/Game/Scripts/agents/Modules/Combat/MeleeDefense.cs), server): an `IDamageFilter` on each humanoid NPC's `HealthComponent`. A `DamageKind.Melee` hit from inside its facing cone, while alive, upright, not mid-swing and off cooldown, rolls block (12%, 25% still gets through) or dodge (8%, nothing lands) → `ReactEverywhere(Blocked/Dodged)` plays Block Left/Right or Duck/Dodge Back everywhere, and the attacker sees a "BLOCKED"/"DODGED" number. A defended hit does not flinch. See [Combat.md](Combat.md).
7. **Items:** `UsableItem.useAction` plays in `PlayUse` (every machine); gauntlets/lasso call `PlayOnHolder` from `Present`. The firearms (`Gun`, `GravelBlaster`) and the patrol robots' `shootAction` play **Pistol Recoil** on the additive layer; robots aim with Aim Pistol, and watchers hold an NPC's aim for the fire cooldown plus `watcherAimGrace` after each shot they receive.
8. **Recoil clips** are generated, like the controller: *Generate Recoil Clips* reads [RecoilProfile.asset](Assets/Game/ScriptableObjects/Animation/RecoilProfile.asset), writes `Art/Animations/Humanoid/Recoil/{Pistol,Rifle} Recoil.anim` (muscle curves: frame 0 neutral = the additive reference, a kick, a settle back to neutral), updates the two Recoil actions and rebuilds.
9. **CMU import:** curated takes live in `Assets/ThirdParty/CMU/Takes/` (TimeMode patched, see Gotchas) with [cuts.json](Assets/ThirdParty/CMU/cuts.json) naming each clip's take, start/end seconds and loop flag → `CmuClipImporter` cuts them, Humanoid, root motion baked in place → one action per clip group. The 2,548-take pack stays Unity-invisible at `Art/Animations/_Packed~/`.

## Multiplayer

- **One writer per body.** The player's `ClientNetworkAnimator` replays layer **states and weights** on watchers and late joiners (`NetworkAnimator.CheckForStateChange`), so only its owner plays actions; NPCs have no NetworkAnimator, so every machine plays from the replicated event (`AgentActed`, `Present`, band changes, `NetMsg.CharacterActed`). Callers never check — `CharacterActions` does.
- **Moments follow the same rule.** Raise one where it happens: seen on every machine (chatter, war cry, the shared-clock fidget) or only by the owner (the player's interact) → `React`; decided by the server alone (damage, a task ending) → `ReactEverywhere`, which picks there and sends the action through `PlayEverywhere`. A local roll is seeded from the body's seed and how many times it has had that moment (or an explicit salt — the fidget's clock bucket), so machines that saw the same moments pick the same thing; a late joiner may differ, cosmetically.
- **Dialog gestures are local to the talker**, like the text and the jaw ([TalkingMouth.md](TalkingMouth.md)): only the player who pressed interact sees the NPC gesture. Chatter plays on every nearby machine; beat timing is per machine.
- **Variant choice** travels when a message carries it (melee `B`, `CharacterActed` `B = variant | arm<<8`); otherwise a per-body `System.Random` seeded from `NetworkObjectId`. Speed picks are seeded the same way, so contact timers match the clip everywhere.
- **Relaying a client's request:** `PlayEverywhere(..., except: client)` does NOT play locally — the host would then play it a second time from its own `ToOthersRpc`.
- **`/act` runs on the server** and reaches the owner through `NetMsg.Emote` with `B = 1` (A = action catalog index), like a typed emote.

## Persistence

None. Everything is seconds long or re-asserted by gameplay state that already saves (seated, band, task). A melee blow in flight is dropped by a save, in the victim's favour. Cooldowns and pick memory start fresh.

## Gotchas

- **Never hand-edit the controller** — run Audit first to see what a rebuild would erase.
- **`CharacterActions` has no `Awake`, on purpose.** Other modules call it from their own `OnEnable` during `Instantiate`, and Unity raises Awake/OnEnable per component in list order; the wiring adds it LAST, so `AggressionTelegraphModule.OnEnable` reached it first and threw a NullReferenceException on every nomad spawn. Its tracks are built by a field initializer and everything else resolves on first use — keep it that way.
- **Authority is `IsServerAuthoritative() ? IsServer : IsOwner`, never `HasAuthority`** (that means IsServer in client-server mode and hands the host every client's body).
- **A tag is a promise about when the action may play.** Moments pick among everything tagged: an action tagged `hurt` plays on every hit, `greet` on every conversation, `pickup` on every pickup. The CMU cutter first tagged limps and a stunt-dive knockdown `hurt`; they became `injured`/`fall`. Partner actions (high five, handshake) are not `greet`.
- **The player's `BodyLanguage.fullBody` is off.** A full-body pickup or bow would freeze its legs under its own input (GDC-L1-ANIM-0002). The wiring sets it only on a freshly added component.
- **A `Hold` waits while its slot is busy** (a greeting bow on Full clears the Upper talk loop; the hold comes back after `holdRetrySeconds`). It swaps to another loop when the posture no longer fits — a full-body Talk stops when the speaker walks off.
- **The CMU pack's FBX say TimeMode 7 (30 fps drop-frame), which Unity reads as 1 fps**: every cut under a second came out exactly 1 s long and curves were sampled at 1 Hz. The copied takes have that one int rewritten to 6; `CmuClipImporter` refuses a take still reporting < 10 fps. A new take needs the same patch.
- **`ModelImporterClipAnimation` is a class.** Reusing `defaultClipAnimations[0]` for every cut left all clips of a many-cut take with the LAST cut's range; take a fresh `defaultClipAnimations[0]` per cut.
- **UAL's root faces backwards.** "Based upon Original" rotation played every full-body UAL clip facing away; the importer uses Body Orientation (like Mixamo here). Bump `GetVersion()` whenever an import rule changes, or already-imported clips keep the old one.
- **UAL names some held poses without `_Loop`** (`Sword_Idle`, `Pistol_Aim_*`); the importer loops those too.
- **A Loop action on a ≤2-frame clip is a held pose** (the Point clips) and is allowed; any longer non-looping clip in a Loop freezes on its last frame and is refused.
- **An override controller swaps a clip everywhere**, so a locomotion variant may not replace a clip an action plays — the builder throws.
- **`[RequireComponent(CharacterActions)]` on `PlayerEmotes` auto-adds one into the networked VARIANT** whenever a rebake loads it and the base lacks one. Wire the base prefab (`PlayerCharacter`) first; a variant-added copy means two writers. `BodyLanguage`, `HurtReaction` and `SpeechGestures` deliberately have no `RequireComponent`.
- **Action speed params default to 1** — at 0 every action freezes on frame one.
- **Preview renders:** drifters and nomads cull transform updates, so a preview-scene Animator must be set to AlwaysAnimate; and a skinned mesh rendered several times in one editor frame may keep its first pose (Raxy did). `Time.time` does not advance inside one editor command, so cooldowns and hold retries cannot be exercised there.
- **Never give a gun a normal shot clip as its use action.** `Pistol_Shoot` and the rifle shot are level-only and snap an aimed arm level on every shot; recoil is additive for that reason. An additive clip must end where it starts (the content check refuses one that does not), and its frame 0 is the reference pose.
- **The additive slot is outside the stop rules:** a Full action does not stop a running recoil and a recoil stops nothing.
- **Recoil sign conventions are from memory** (Hand Down-Up positive = up, Front-Back negative = back). If a kick goes the wrong way, flip it in the profile and regenerate — never hand-edit the .anim.
- **Every CMU melee move is Full-slot**, so a moving NPC falls back to `attackAction`. Kick Front is 1.9 s and commits the NPC for all of it.
- **The humanoid controller has no `Hurt`, `Death`, `Meele`, `IsAiming` or `Hold`.** `AgentAnimatorDriver.TriggerHurt` is deliberately nothing on a humanoid; creatures keep their own triggers.
- **CMU data quirks the cuts cannot fix:** the 140_xx get-ups start 0.25–0.5 m above the floor (a mat); Fall Forward is a stunt dive; Backflip/Side Flip have one-frame foot flips mid-air.

## Extending

- **An animation:** select clips → *Create Actions From Selected Clips* (or create a `CharacterAction` by hand) → set slot/playback/marks → **tag its cues** → *Rebuild*. Tagged, it already plays wherever gameplay asks for those cues; to play it by name, reference the asset and call `CharacterActions.Play`. Check it in the Animation Library or with `/act <name>`.
- **A word:** create a `CharacterCue` in `Cues/` with a meaning (and a fallback if a broader word should stand in) → tag actions. Audit lists words nothing answers yet.
- **A trigger ("when X happens, the body does Y"):** if X is already a moment, edit its row in the reaction table (or a per-body table on that prefab's `BodyLanguage`). If not, append a `CharacterMoment`, raise it at X with `BodyLanguage.React(this, moment)` (or `ReactEverywhere` from the server), and add its row — `BodyLanguageAssetTests` fails until the row can play something.
- **A state that holds a loop** (working, carrying): `Hold(cue)` every frame while it lasts, `Release(cue)` when it ends, as `SpeechGestures` does with `talking`. For an NPC state only the server knows, add an Everywhere variant rather than holding on one machine.
- **An interactable that should show something else:** implement `IInteractionMoment` (return `None` for nothing).
- **A new clip library:** give it an import rule like `QuaterniusClipImporter`/`CmuClipImporter` (Humanoid, loop naming, Body Orientation, no events), then the steps above.
- **A hold style:** append to `ItemGrip.HoldStyle` and add its pose to the profile (`EveryHoldStyleHasAPose`).
- **A locomotion archetype:** a `LocomotionSet` with only the swapped clips → profile variants → Rebuild → assign `Variants/Humanoid_<set>.overrideController` to the prefab's Animator; re-tune that prefab's `animatorSpeedScale` against the new stride. The CMU walks (Casual, March, Robot, Elderly …) are the candidates.
- Tests: `HumanoidControllerAssetTests`, `CharacterActionAssetTests`, `HumanoidWiringAssetTests`, `CharacterActionRuleTests`, `CharacterActionsPlaybackTests`, `BodyLanguageRuleTests`, `BodyLanguageAssetTests`, `MeleeMovesetRuleTests`, `MeleeDefenseTests`, `RecoilClipAssetTests`.
