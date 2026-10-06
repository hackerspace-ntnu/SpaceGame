---
system: AnimationCatalog
layer: presentation
summary: "What each humanoid clip library became: cuts, actions, loops, measured locomotion speeds"
paths:
  - Assets/ThirdParty/MocapCentral
  - Assets/ThirdParty/Motion Cast-FREE01
  - Assets/ThirdParty/EEJANAI_Team
  - Assets/ThirdParty/ExplosiveLLC
  - Assets/Game/Editor/AssetPipeline/LibraryClipImporter.cs
  - Assets/Game/Editor/AssetPipeline/ClipCuts.cs
  - Assets/Game/Editor/Animation/ClipPoseSheet.cs
symptoms:
  - "an action loops but its clip is 'not imported with Loop Time'"
  - "which clip should an NPC use for farming, fishing, mining, cooking, serving, singing"
  - "a Mixamo take imports as one 27 second clip called mixamo.com"
  - "a pose sheet shows the same figure in every cell"
  - "an NPC with an injured, drunk or heavy walk skates or moonwalks"
reads_with: [HumanoidAnimation, AgentSystem, Residents, ArtPipeline, Stations]
updated: 2026-10-06
---

# Animation Catalog

[HumanoidAnimation.md](HumanoidAnimation.md) explains how a body plays an action. This doc is about
the **content**: which clip libraries the project owns, how a raw take becomes the clips an action
plays, what answers each cue today, and where the vocabulary is still thin. Numbers are from 2026-10-03. Per-action tables: [animation-action-inventory.md](animation-action-inventory.md). What is missing and how NPCs should use it: [NpcAnimationPlan.md](NpcAnimationPlan.md).

## Model

- **Seven libraries, 258 new clips imported as Humanoid, 0 of them generic.** All are played in place
  (see the root-motion gotcha in HumanoidAnimation.md).

| Library | Where | Files → clips | Loops | Minutes | What it is good for |
|---|---|---|---|---|---|
| Mixamo singles | `Art/Animations/Humanoid/*.fbx` | 38 → 51 | 13 | 3.4 | Get-ups, slips and stumbles, kneeling work, fishing cast, fax/keypad/device work, salute, bow, bartending, a lying-down sleep loop, a ladder climb cycle; and (2026-10-06, player-only) `Ledge Climb Low/Mid/High/Running` for the ledge climb ([LedgeClimbing.md](LedgeClimbing.md)) — root motion kept out of the pose; and (2026-10-06, player-only) `Lift Heavy` / `Set Down Heavy`, cut from `Lifting Object.fbx` (a crouched heave of something very heavy), played by name by `Liftable` ([Lifting.md](Lifting.md)) |
| Mocap Central sample | `ThirdParty/MocapCentral` (79 of 127 Unity-rig takes; full set unzipped, git-ignored, in `Art/Animations/_Packed~/MC_Sample`) | 79 → 84 | 23 | 6.1 | Dance start/loop/stop sets, injured and drunk and swagger and heavy-hammer walks, wounded collapse and get-up, piano, spellbook, vending machine, singing, conversation beats |
| Motion Cast FREE01 (No Root copies) | `ThirdParty/Motion Cast-FREE01/No Root Animations` | 16 → 37 | 8 | 4.6 | Acted emotions: crying, laughing, mad laugh, applause, exhaustion, disgust, hunger, waving, threats, six speech-gesture beats |
| EEJANAI cooking | `ThirdParty/EEJANAI_Team/CookingAnimations/FBX` | 18 → 18 | 8 | 1.6 | Chopping, washing, stirring, wok, pan, grill, blender, plating, pouring, seasoning, eating, drinking. Each is now its own station cue (`chop`, `wash`, `stir`, `cookpan`, `cookwok`, `grill`, `blend`, `plate`, `season`), see [Stations.md](Stations.md) |
| ExplosiveLLC crafter | `ThirdParty/ExplosiveLLC/.../Animations` | 8 → 8 | 4 | 0.2 | Carry-a-crate idle/walk/pickup/putdown/hand over/receive |
| Kevin Iglesias, male + female | `ThirdParty/Kevin Iglesias/.../Animations/{Male,Female}` | 60 → 60 | 31 | 1.3 | Farming, fishing (4 kinds), hammering L/R, mining ground/wall L/R, gathering, rifle aim, damage, death, spear and boomerang throws |
| (already present) CMU 288 cuts, Quaternius UAL1 42, Player/Mixamo 42 | see HumanoidAnimation.md | | | | |

- **Cut, not copied.** A long performance is cut into the beats gameplay can ask for. The cut table is
  a `cuts.json` per library (take = FBX file name, seconds, loop flag); boundaries were snapped to the
  quietest frame (one-shots) or the best-closing pair (loops) by scoring muscle-space distance, and
  checked on pose sheets. A take with no entries imports whole, named after its file.
- **Loop twins.** A clip that starts and ends on the identical pose (closure 0.00) cannot be held by
  `Hold` unless it ships Loop-flagged, so the cooking machine cycles, the drill and the piano cycle
  exist only as loop twins. A `Loop` action refuses a clip imported without Loop Time.
- **Male and female variants** of every Kevin Iglesias work clip sit in the same action as separate
  variants, picked by the body's seeded random: a crowd mining does not mine in unison or as one
  person. Left/right-handed variants (hammering, mining) are variants too.
- **Cue vocabulary: 63 → 89 words, then 101 with the station cues** (stir, cookpan, cookwok, grill, chop, wash, blend, plate, season, wipe, weave, rummage), every one answered (Audit lists none unanswered).
  New: serve, operate, farm, fish, mine, gather, salute, bow, draw, applaud, confused, disgust, cough,
  hungry, exhausted, cry, collapse, stumble, sing, music, read, chant, drunk, prone, receive, restrain.
- **Locomotion archetypes.** Six new `LocomotionSet`s (Injured, Drunk, Swagger, Stroll, Heavy, Carry)
  join Formal and Wander; the builder generated their override controllers. **None is assigned to a
  prefab.** Measured ground speed of each clip at `Animator.speed = 1` (Blender, from the
  root-motion originals) and what it implies — the default walk is a 1.03 s in-place Mixamo cycle,
  taken as roughly 1.4 m/s for the ratios (an estimate; check against a measured NPC):

| Set | Forward clip | Cycle | Ground speed | Agent speed to keep feet planted |
|---|---|---|---|---|
| Injured | InjuredBelly_Loco_Walk_Fwd | 5.7 s | 0.67 m/s | ~0.5 × normal walk |
| Drunk | Drunk_Loco_Walk_01 | 4.2 s | 0.64 m/s | ~0.45 × |
| Swagger | Loco_Walk_Swagger | 2.5 s | 0.97 m/s | ~0.7 × |
| Stroll (Mixamo Walking) | Walking Loop | 2.9 s | 0.98 m/s | ~0.7 × |
| Heavy (orc hammer) | OrcHammer_Loco_Walk_Fwd/Bwd/Left/Right | 1.07 s | 1.48 / 1.24 / 1.42 / 1.21 m/s | ~1.05 × fwd, 0.9 × strafe |
| Carry (Crafter) | Carry-WalkForward | 0.8 s | ~2.7 m/s on a taller rig (about 2.1 in our units) | ~1.5 × — brisk, a hauler's pace |
| Crawl (Mocap Central, action only) | ProneCrawl_CrawlStartStop_Fwd | 6.7 s | 0.41 m/s | n/a |

## Key types

| Type | File | Role |
|---|---|---|
| `LibraryClipImporter` | [LibraryClipImporter.cs](Assets/Game/Editor/AssetPipeline/LibraryClipImporter.cs) | One importer for the five single-take libraries; a `Libraries` table of (folder, take sub-folders); Humanoid, avatar from each take's own skeleton, cuts from the library's `cuts.json`, in place, loop-posed cycles, vendor events stripped |
| `ClipCut` / `ClipCutList` | [ClipCuts.cs](Assets/Game/Editor/AssetPipeline/ClipCuts.cs) | The shared `cuts.json` row: take, name, start, end (seconds), loop, `travelsVertically` (a ladder climb). Also read by `CmuClipImporter` |
| `ClipPoseSheet` | [ClipPoseSheet.cs](Assets/Game/Editor/Animation/ClipPoseSheet.cs) | Stick-figure contact sheet of any humanoid clip (front + side, seconds in the corner), **Tools ▸ SpaceGame ▸ Animation ▸ Pose Sheet From Selected Clip** → `Library/PoseSheets/` |

## Flows

1. **Add a library:** put the FBX under `ThirdParty/<Pack>/`; add one row to `LibraryClipImporter.Libraries` (folder, which sub-folders hold takes) and an empty `cuts.json` (`{"cuts":[]}`) in it; reimport; check the clip names and that none imported generic (the importer logs an error for a generic one).
2. **Cut a long take:** render a pose sheet (step 0.3-1 s), find the rest frames (muscle-space speed near zero), write the proposed cuts, let the snapper move each boundary within ±0.4 s (loops: ±0.6-1.2 s, searching for the best-closing pair), write `cuts.json`. A cut inside a continuous performance has no rest frame — give it a longer fade.
3. **Make it playable:** one `CharacterAction` per role, tagged with cues (see [HumanoidAnimation.md](HumanoidAnimation.md) → Extending), then **Rebuild Humanoid Controller**, then **Audit**.
4. **Prove it is in place:** evaluate every clip through a PlayableGraph on an Animator with `applyRootMotion` off and read the hips; anything over ~0.45 m is travel. All 198 new clips measure under that; CMU went from 136 of 288 over to 5 (real flips).
5. **Pick an archetype for a prefab:** assign `Variants/Humanoid_Locomotion_<Set>.overrideController`, multiply the NPC's walk speed by the table above, then look at the feet.

## Multiplayer

Nothing here is networked. NPC bodies play actions from replicated events on every machine; the
player's actions replay through its NetworkAnimator (one writer per body). Because every new action
is a plain `CharacterAction`, it inherits that. **No play-mode or host+client run of any of the new
content has been made** — everything above is editor-time verified (import, drift measurement,
controller rebuild, EditMode tests, pose sheets).

## Persistence

None. Clips and actions are assets; no runtime state is added.

## Gotchas

- **Names are identities.** An FBX clip's fileID derives from its name and every action holds it, so a cut is never renamed after an action uses it. Action names must be unique across the whole project (case-insensitive): `Walk Drunk` collided with the CMU action and the builder refused to rebuild.
- **The Motion Cast "Various Attitudes" folder is the same sixteen takes with root motion**, and the demo scene, prefab, controllers and scripts in the pack are unused. Only `No Root Animations` is a take folder.
- **The cooking takes cannot copy the pack robot's avatar** ("Copied Avatar Rig Configuration mis-match": their root node differs); each builds its own. One file in the pack (`stiring (coffe or juice)`) happened to match, which hid the problem for a minute.
- **The Crafter FBX are version 6100** — Unity reads them, Blender cannot (≥ 7100 only). Measure their stride in Unity.
- **`MC_Sample_SourceFiles.zip` (450 MB) sits in `Art/Animations/Humanoid/`.** Unity ignores it, git does not: it should leave the repo (the 127 FBX that matter are already unzipped, git-ignored, in `_Packed~/MC_Sample`).
- **Pose sheets use `AnimationMode.SampleAnimationClip`.** A PlayableGraph over the same model left one rig (Motion Cast) frozen on its first pose in every cell while the curves moved.
- **Licences:** Kevin Iglesias ships a royalty-free, commercial-use, no-attribution licence. Mixamo is Adobe's royalty-free terms. **Mocap Central, Motion Cast, EEJANAI and ExplosiveLLC ship no licence text in the project** — confirm the terms before release and add them to `THIRD_PARTY_NOTICES.md`.
- **`Serve` had no loop** until a bartending cycle was cut for it; `Hold(serve)` on a one-shot-only cue plays nothing. Check the "Of which loop" column before binding a cue to a standing state.

## Extending

- **Another clip for an existing role:** import it, then add it as a variant of the action that already answers the cue (variants are picked by seeded weight; they are the cheapest way to add variety).
- **A new role:** a cue (Inspector, so the meaning is quoted — see the YAML gotcha), an action tagged with it, Rebuild, Audit.
- **A new NPC situation:** read [NpcAnimationPlan.md](NpcAnimationPlan.md) first; most of it is data (`SpotUse.holdCue`, a per-body `MomentReactions` table, an override controller on a prefab), not code.
