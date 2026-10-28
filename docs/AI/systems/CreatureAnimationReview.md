---
system: CreatureAnimationReview
layer: pipeline
summary: "Edit-mode window + scene that plays every creature clip on a treadmill and logs Good/Fix/Broken verdicts"
paths:
  - Assets/Game/Editor/Creatures/CreatureAnimationReviewWindow.cs
  - Assets/Game/Editor/Creatures/CreatureAnimationCatalog.cs
  - Assets/Game/Editor/Creatures/CreatureAnimationStage.cs
  - Assets/Game/Editor/Creatures/CreatureAnimationReviewLog.cs
  - Assets/Game/Editor/Creatures/ClipMotion.cs
  - Assets/Game/Scenes/Tests/CreatureAnimationReview.unity
symptoms:
  - "pressing Play in the animation review scene lands in the main menu"
  - "a creature is missing from the animation review list"
  - "the review window flags half the clips as pops"
  - "every loop seam measures 0 although a loop visibly snaps"
reads_with: [WildCreatures, AgentSystem, ArtPipeline]
updated: 2026-10-27
---

# Creature Animation Review

A tool for judging creature clips by eye and recording the verdict. *Tools ▸ SpaceGame ▸ Creatures
▸ Animation Review*. Nothing here ships or runs in a build.

## Model

- **Edit mode only.** [`Bootstrapper`](Assets/Game/Scripts/Core/SceneManagement/Core/Bootstrapper.cs)
  sends Play in any scene outside the build to the main menu, so the window samples clips itself
  from `EditorApplication.update` (`AnimationClip.SampleAnimation`). No Animator state machine
  runs: this reviews clips, not controller transitions.
- **Bodies are model instances, not agents.** The stage instantiates the model file and copies the
  prefab's renderer materials and meshes onto it by transform path, so eyes and coats look as in
  game while no AI, `NetworkObject` or saver wakes up. Everything is `HideFlags.DontSave`.
- **Treadmill.** Clips are in place. Each lane's floor grid (0.5 m tiles) scrolls backward at the
  lane's speed; a planted foot that slides against the grid is foot skating. The default speed is
  what the game moves the body at while that clip plays (`ReviewClip.GameSpeed`): run clip →
  `NavMeshAgent.speed`, walk clip → `speed × walkSpeedMultiplier`, else 0.
- **Two views.** *One creature*: all its clips, one lane. *Lineup*: one clip name played on every
  creature that has it, lanes side by side along +X, all facing +X.
- **Verdicts** (`Good`/`Fix`/`Broken` + note) live in
  `Assets/Game/Art/Animations/Creatures/CreatureAnimationReview.json` (created on the first verdict), keyed
  creature → clip, written on every change. *Copy report* puts the Fix/Broken list on the
  clipboard as Markdown, with each clip's in-game role.

## Key types

| Type | File | Role |
|---|---|---|
| `CreatureAnimationReviewWindow` | [CreatureAnimationReviewWindow.cs](Assets/Game/Editor/Creatures/CreatureAnimationReviewWindow.cs) | UI, playback clock, hotkeys, scene labels, framing |
| `CreatureAnimationCatalog` | [CreatureAnimationCatalog.cs](Assets/Game/Editor/Creatures/CreatureAnimationCatalog.cs) | Finds bodies + clips + roles + game speeds; `Measure` flags held poses and pops |
| `CreatureAnimationStage` | [CreatureAnimationStage.cs](Assets/Game/Editor/Creatures/CreatureAnimationStage.cs) | Lanes: body instance, scrolling floor, sampling |
| `CreatureAnimationReviewLog` | [CreatureAnimationReviewLog.cs](Assets/Game/Editor/Creatures/CreatureAnimationReviewLog.cs) | JSON verdict store + Markdown report |
| `ClipMotion` | [ClipMotion.cs](Assets/Game/Editor/Creatures/ClipMotion.cs) | Bone-turn measurement, shared with `WildCreatureBuilder` |

## Flows

1. **Catalog:** every prefab under `Prefabs/Agents/creatures` (recursive) with an `Animator`. Model
   = the avatar's asset, or — no avatar (Esko, Caraxoids, Glintback, Sporeling, Vrescal) — the
   model file most of the controller's clips come from. Prefabs sharing a model are one body
   (Esko/Esko_Ranch/Esko_Wild → Esko; first path wins). Clips = the model's takes ∪ the
   controller's clips. Roles = override keys (`Esko_Walk` → `Walk`) or plain controller clip names.
2. **Stage:** built lazily when the active scene is the review scene and the creature set changed;
   cleared on `sceneClosing` and `OnDisable` (domain reload included).
3. **Measure** (first time a clip is on stage): samples every frame. *Held pose* = no bone turns
   ≥ 0.5° between frame 0 and the middle. *Pop* = largest one-frame turn (loop wrap included) vs the
   median frame; warned at ≥ 8× and ≥ 5°.
4. **Judge:** `1` Good (advances), `2` Fix, `3` Broken, `0` clear; the note saves per keystroke.

## Multiplayer

N/A — editor tool; nothing networked is instantiated.

## Persistence

N/A for game saves. The review log is a project file, not save data.

## Gotchas

- **`SampleAnimation` can move the model root.** A clip with curves on path "" carries the body out
  of its lane; `Sample` re-applies the lane pose after every sample.
- **A loop's seam is often one frame BEFORE the end.** Exports that snap the last frame onto the first
  measure 0° at the wrap while the last-but-one → last step jumps (Barbelwalk Run: 80°). Comparing
  only last ↔ first frame flagged nothing; `Measure` therefore scans every step.
- **Pop ratio is noisy below ~8×.** Strikes, falls and turns legitimately reach 3–7× their median
  frame; at 3× the check flagged 295 of 650 clips. Treat flags as "look here", not verdicts.
- **Turn-in-place loops wrap 90°** (ShellBear/SpikeJerbo TurnL/R) because the root turn is baked
  into the hips — flagged as a seam pop, real in the review loop, blended in game.
- **The note field must drop focus when the row changes** (`GUIUtility.keyboardControl = 0`), or an
  open edit writes the previous clip's text onto the next one.
- **Opening the review scene `Single` prompts to save a dirty scene.** To drive it from a tool or
  agent without that modal, open it `Additive` and `SetActiveScene` — the window only needs it active.

## Extending

- More checks: add a field to `ReviewClip`, fill it in `CreatureAnimationCatalog.Measure`, surface it
  in the window's `Warning`.
- Another source of bodies: widen `PrefabRoot` or the model lookup in `ModelPath`.
