---
system: TalkingMouth
layer: presentation
summary: "A character's jaw opens and shuts in step with the speech popup typing out the line it said"
paths:
  - Assets/Game/Scripts/Presentation/Appearance/TalkingMouth.cs
  - Assets/Game/Editor/Agents/MouthWiring.cs
symptoms:
  - "an NPC talks with its mouth shut"
  - "a Raxy stands with its lower lip pushed up into its upper lip"
  - "the jaw pops at the start and end of every line"
  - "the mouth keeps flapping after the player skipped to the end of the line"
  - "rotating a bone between two renders in one editor command changes nothing in either picture"
reads_with: [ArtPipeline, UI, AgentSystem, InteractionSystem, StylizedEyes, HumanoidAnimation]
updated: 2026-09-25
---

# Talking Mouth

A character with a `Jaw` bone opens and shuts its mouth while the dialog popup types out a line it said, letter by letter.

**Scope:** [TalkingMouth.cs](Assets/Game/Scripts/Presentation/Appearance/TalkingMouth.cs), [MouthWiring.cs](Assets/Game/Editor/Agents/MouthWiring.cs), the speaker half of [NpcDialogPopupUI.cs](Assets/Game/Scripts/Presentation/UI/Dialog/NpcDialogPopupUI.cs). Wired by [SculptCharacterBuilder.cs](Assets/Game/Editor/Agents/SculptCharacterBuilder.cs).
**Related:** [ArtPipeline.md](ArtPipeline.md) (the jaw rig) · [UI.md](UI.md) (the popup) · [AgentSystem.md](AgentSystem.md) (who speaks) · [StylizedEyes.md](StylizedEyes.md) (the blink, its sibling)

## Model

- **Talking is whatever the popup is typing for this character.** Every NPC line goes through `NpcDialogPopupUI.Show` / `ShowQuestion`, which now takes the speaker's `Transform`: `ChatterModule.Speak` (idle chatter, warnings, war cries), `DialogInteraction.SpeakLine` and both `ShowQuestion` calls, and `TraderInteraction`'s sold-out and decline lines. `TryGetSpokenCharacter(who, out char)` answers with the letter the typewriter revealed last, while it is still typing that speaker's line.
- **The letter picks the opening:** `a`/`o` wide (`wideOpenDegrees`, 20°), `e i u y` at `narrowVowelOpen` (0.6), other letters and digits at `consonantOpen` (0.25), and `m b p`, spaces and punctuation shut. `SmoothDamp` over `response` (0.06 s, longer than one 28 chars/s letter) blurs letters into syllables. So the jaw pauses where the typewriter pauses on a comma, and stops the moment the player skips the line.
- **The rig:** Raxy's `Jaw` is a child of `Head`, hinged 4 cm behind the mouth slot, with the lower lip, chin and under-jaw weighted to it and a dark `raxy_mouth` pocket behind the lips. See [ArtPipeline.md](ArtPipeline.md). It carries Unity's Humanoid spelling, so the avatar maps it as the Jaw.
- **The open axis is measured, not typed:** `MouthWiring` stores the character's right as the jaw sees it at rest (`(-1, 0, 0)` on Raxy). A positive turn about it pitches the chin down.
- Dead (`HealthComponent.Alive` false) → target shut. Disabled → jaw back to rest.
- **The hands follow the same signal.** `SpeechGestures` reads the popup's `Speaker`, `Line` and `LineNumber` to hold a talking loop and gesture on the beats while the line types — see [HumanoidAnimation.md](HumanoidAnimation.md).

## Key types

| Type | File | Role |
|---|---|---|
| `TalkingMouth` | [TalkingMouth.cs](Assets/Game/Scripts/Presentation/Appearance/TalkingMouth.cs) | On the character root. `jaw`, `openAxis`, the three openings, `response`. `LateUpdate` writes the jaw |
| `NpcDialogPopupUI` | [NpcDialogPopupUI.cs](Assets/Game/Scripts/Presentation/UI/Dialog/NpcDialogPopupUI.cs) | Holds `speaker`, `typedMessage`, `typedIndex`; `TryGetSpokenCharacter`. `Hide` clears the speaker |
| `MouthWiring` | [MouthWiring.cs](Assets/Game/Editor/Agents/MouthWiring.cs) | `Ensure(root)`: finds a bone named `Jaw`, adds/points `TalkingMouth`, measures the axis. No jaw → nothing added. `IsWired` for *Verify* |

## Flows

1. **Wire.** `SculptCharacterBuilder.ApplyBehaviour` → `MouthWiring.Ensure(root)`, on a new build and on *Update Drifter Behaviour*. *Verify Drifter NPCs* fails a prefab whose rig has a Jaw and no wired mouth.
2. **Speak.** A call site passes its own `transform` to `Show` → the popup's coroutine sets `typedIndex` as each letter appears.
3. **Move.** `TalkingMouth.LateUpdate` (after the Animator) → `TryGetSpokenCharacter(transform, …)` → `Openness(letter)` → `jaw.localRotation = rest * AngleAxis(open * wideOpenDegrees, openAxis)`.

## Multiplayer

Local presentation, like the blink: the mouth follows the popup on the machine that shows it. Chatter runs on every machine as an `IPresentationModule`, and war cries are relayed, so those lines move the jaw for every nearby player. A dialog line is local to the player who pressed interact (see [InteractionSystem.md](InteractionSystem.md)), so only that player sees the mouth move — the same rule as the text. Nothing is sent.

## Persistence

None. The mouth is transient; `rest` is re-read from the prefab on every spawn.

## Gotchas

- **A Humanoid avatar that maps the jaw drives it EVERY frame, and not to its rest pose.** Unity auto-mapped Raxy's `Jaw` as the Humanoid Jaw. No clip here animates the jaw muscle, so the Animator writes the muscle's centre each frame — measured **10.3° clenched** past rest after sampling `walking.fbx`, the lower lip pushed into the upper. `TalkingMouth.Start` detects `animator.GetBoneTransform(HumanBodyBones.Jaw) == jaw` and then holds the rest pose every frame, opening from it. On a rig the avatar does not map, it writes only while the mouth moves. Read the bone after a clip sample before trusting any jaw rest.
- **Capture `rest` in `Awake`, before the Animator's first update.** Read later on a mapped jaw, it is the clenched muscle-centre pose.
- **The popup is one singleton with no position.** A line shown without a speaker moves no mouth, and a second speaker's line takes the popup over, which also moves the mouth to them.
- **Skinning runs once per editor frame, not once per `Camera.Render`.** Several renders inside one `Unity_RunCommand`, with a bone moved between them, all show the first pose. The jaw looked dead that way when `BakeMesh` proved 1987 vertices moving 2.8 cm. Verify with `BakeMesh` into a `MeshRenderer` copy (scaled by the renderer's `lossyScale` — `BakeMesh(useScale: true)` leaves it out), or one pose per command.

## Extending

1. **Another character talks** once its rig has a bone named `Jaw` under `Head` with the lower face weighted to it, and a mouth that can open (Raxy's was a closed groove until the slot was cut — see [ArtPipeline.md](ArtPipeline.md)). Then *Update Drifter Behaviour*, or `MouthWiring.Ensure` from its own builder.
2. **A new speaking call site** passes the speaking character's `transform` to `Show`/`ShowQuestion`. Omit it only for a line nobody in the world says.
3. **Shape keys (visemes) instead of a jaw** would slot into `Openness` → `SetBlendShapeWeight`. The export keeps shape keys, because its triangulation is bmesh on the data, not a modifier.
4. **Other players seeing a dialog line's mouth** needs the line broadcast first. That belongs to the dialog system, not here.
