---
system: WildCreatures
layer: pipeline
summary: "Concept-art creatures: AI mesh on a donor rig in Blender, built into a wild-animal prefab"
paths:
  - Assets/Game/Editor/Creatures/WildCreatureBuilder.cs
  - Assets/Game/Editor/Creatures/WildCreatureRoster.cs
  - Assets/Game/ScriptableObjects/Creatures/
  - Assets/Game/Prefabs/agents/creatures/Wild/
  - Assets/Game/Art/Animations/Creatures/
  - Assets/Game/ScriptableObjects/Eyes/Creatures/
  - Assets/Game/Art/Models/_Source~/models/creatures/
symptoms:
  - "a wild creature glides with its legs barely moving, or its feet skate"
  - "a wild creature's prefab was not rebuilt after re-exporting its FBX"
  - "Build Wild Creatures says clips are not readable yet"
  - "a creature has no eyes in game, or its eyes never blink"
  - "a wild creature can be saddled or ridden when the roster says it should not be"
  - "the new animals are tiny next to the player"
  - "a big wild creature stops short and bites the air in front of me"
  - "the saddle floats above or sinks into a wild creature's back"
  - "a wild creature's legs cross, bend backwards or slide while it walks"
  - "a wild creature's clips play but its body stays frozen in the bind pose after a re-export"
  - "a wild creature's collider or NavMeshAgent is far bigger than its body"
  - "a galloping wild creature's feet race ahead of its body"
  - "a wild creature's feet slide sideways every time it turns on the spot"
  - "a creature changed size after its model was replaced"
reads_with: [AgentSystem, ArtPipeline, StylizedEyes, Persistence, Multiplayer, Combat]
updated: 2026-10-28
---

# Wild Creatures

How a creature drawn as concept art becomes a wandering, fleeing, fighting, blinking animal in the
game. The modelling half runs in Blender outside the project; this doc covers what arrives in the
project and the one editor pass that turns it into an agent.

**Scope:** [`WildCreatureBuilder`](Assets/Game/Editor/Creatures/WildCreatureBuilder.cs), the prefabs
it writes to `Assets/Game/Prefabs/agents/creatures/Wild/`, their override controllers and eye styles.
**Related:** [AgentSystem.md](AgentSystem.md) (the modules), [ArtPipeline.md](ArtPipeline.md) (the
`.blend` → FBX export), [StylizedEyes.md](StylizedEyes.md) (eyes and blinking).

## Model

- **A wild creature is the Esko, saddle optional.** `Esko.prefab` is the finished
  peaceful-until-provoked animal (perception, wander, flee-or-fight, provocation, ragdoll, savers,
  netcode). The builder copies its root components field for field onto the new body. The mount
  parts (`MountModule`, `SteerModule`, `SaddleSocket`, `SaddleQuickRelease`, `SaddleSaveable`,
  `MountSaveable`, `MountNetworkSync`, `MountedRiderPose`) are copied only when the roster marks the
  creature rideable. Retuning the Esko's behaviour therefore retunes every creature built after it —
  not the ones already built.
- **The roster owns the game's half.** [`WildCreatureRoster.asset`](Assets/Game/ScriptableObjects/Creatures/WildCreatureRoster.asset)
  gives each creature, by manifest name, its game `height` (ground to top of the rest pose — the
  player is **3 m**), `rideable` and `saddleBone`. The manifest's height is whatever the model was
  sculpted at; the first 24 came in at 0.6–2.6 m, sized for a 1.8 m person.
- **Size is the FBX import scale** (`globalScale = roster height / the baked mesh's height at
  scale 1`), not a root scale. The manifest's `height` is not used for it: it disagrees with the
  mesh by up to 1.6x (Coilhorn). The prefab root stays at 1, so bounds, collider, `NavMeshAgent`, bones, ragdoll mass and the
  saddle are all measured off a body that is already in game metres, and none of the
  "does not follow root scale" exceptions in [Saddles.md](Saddles.md) apply. What the scale does not
  reach is multiplied in by hand: the manifest's strides (Blender metres), and the Esko's
  centre-to-centre distances `attackRange` / `chaseStopDistance` (× collider length / Esko's).
- **Rideable = the Esko's saddle stack fitted to this back.** `FitSaddle` puts the Sandloper saddle
  on the centreline over `saddleBone`, at the highest vertex whose strongest weight is that bone (the
  back, or the shell/dome on it). `seatOffset` keeps the Esko's seat-above-saddle offset; camera
  distance and spill radius grow with length, spill height with back height. Mounted jump/leap stay
  on only if the creature has a `Jump` take. Rideable 2026-10-27: Drovecamel, FrillBoar,
  GoldRunner, Plumeneck, SailCrest, ShellBear, SpineGrazer, Tewe, WingWalker (DomeBack was
  tried: its saddle disappeared inside the moss dome, whose skin is not on the spine bones). Like the Esko, a bare
  animal is not rideable — fit a saddle item first.
- **Every creature plays its own clips on `Esko.controller`** through an
  `AnimatorOverrideController` (`Animations/Creatures/<Name>.overrideController`). The Esko
  controller already has every parameter and trigger the copied modules fire (`Hurt`, `Death`,
  `Roar`, `Ram`, `Happy`, `IsGrazing`, `TurnSpeed`), so no creature needs its own state machine.
  `ClipMap` in the builder decides which of the creature's slots fills each Esko clip, best first
  (Esko_Ram ← Attack, Esko_Graze ← Graze/Eat/Drink, Esko_Roar ← Roar/Bark/Howl…).
- **The manifest is the contract** with the Blender side: `<Name>.creature.json` beside the FBX
  holds the clip takes (slot, loop), the stride speeds, the body bounds, every eyeball (object name,
  centre, gaze, radius) and the eye colours, all in **Blender axes**; the builder converts
  `(x, y, z)` → Unity `(-x, z, -y)`.
- **Stride speed is the clip's own ground speed, not tuned.** The clips are in place, so a planted
  foot slides backward under the body at the speed the body should travel. The manifest takes the
  speed each gait was authored at (the clean-room lab's `showcase.json` `v`); a clip without one
  falls back to the median measured foot slide. The prefab moves at exactly those speeds
  (`NavMeshAgent.speed` = run, `walkSpeedMultiplier` = walk / run) with `animatorSpeedScale` = 1,
  and `animationSpeedMultiplier` / `walkAnimBoost` land the run on Esko.controller's Run anchor
  (SpeedY 15) and the walk inside its Walk band (2.4). A stride under 0.2 m/s (a hop whose feet
  never slide) falls back to a Froude-scaled speed for the body's height.
- **Eyes are real meshes.** Each eyeball in the FBX gets the creature's own `EyeStyle`
  (`ScriptableObjects/Eyes/Creatures/<Name>.asset`, baked by `StylizedEyeBuilder.Build`), a fresh
  equirectangular unwrap about its gaze (`EnsureEyeMesh`) and `EyelidWiring.Ensure` on the root,
  so the `StylizedEye` shader paints the lids and `EyeBlink` blinks them like a drifter's.

## Key types

| Type | File | Role |
|---|---|---|
| `WildCreatureBuilder` | [WildCreatureBuilder.cs](Assets/Game/Editor/Creatures/WildCreatureBuilder.cs) | *Tools ▸ SpaceGame ▸ Creatures ▸ Build Wild Creatures* / *Verify Wild Creatures* |
| `WildCreatureRoster` | [WildCreatureRoster.cs](Assets/Game/Editor/Creatures/WildCreatureRoster.cs) | Editor-only table: game height, rideable, saddle bone per creature |
| `<Name>.creature.json` | `Art/Models/Creatures/Organic/<Name>/` | Manifest: clips, strides, bounds, eyes |
| `<Name>.overrideController` | `Art/Animations/Creatures/` | The creature's clips on `Esko.controller` |

## Flows

1. **Blender** (outside the project): mesh, coat, eyes and the **rig_v2** skeleton
   (`~/Documents/creature-models/pipeline/rig_v2/`); since 2026-10-28 every clip is authored from
   scratch in the clean-room lab (`~/Documents/creature-models/animation_lab/README.md`) — no donor
   clip is left in the game. `animation_lab/tools/publish_to_game.py <backup> <snake>…` writes the
   `.blend` to `_Source~/models/creatures/<snake>/`, the `_exportlib` FBX + textures + manifest to
   `Art/Models/Creatures/Organic/<Name>/` (`.meta` kept, GUIDs survive); then `patch_manifest_speeds.py`.
2. **Build:** *Build Wild Creatures* → per manifest: import settings (Generic, avatar from the
   model, clip names = slots, loop flags, root locked, materials in the FBX found by texture name)
   → override controller → eye style + material → **if the prefab does not exist**: instantiate and
   unpack the FBX, copy the Esko's animator settings and root components, fresh `NetworkObject` and
   `SaveableEntity`, size the `BoxCollider` and `NavMeshAgent` from the body bounds, set the speeds,
   scale `RagdollRig.totalMass` with the body's volume, wire the eyes, `SaveablePolicy.Ensure`, save;
   then, for every creature, stamp the saveable prefab id (the prefab's GUID) into the file, and
   finally `NetworkPrefabRegistrar.Sync` (append-only) registers new prefabs.
3. **Rebuild:** *Rebuild Wild Creatures (overwrite prefabs)* runs the same build over every existing
   prefab. Build alone rebuilds only a prefab whose roster height changed or whose model has a bone
   the prefab lacks (a re-rigged FBX).
4. **Verify:** *Verify Wild Creatures* checks per prefab: override complete, avatar valid,
   `NetworkObject` registered, a saveable prefab id, `EyeBlink` present, mount parts present exactly
   when the roster says rideable (and the saddle bone exists), height within 5 % of the roster, no
   missing script, and that **every clip moves bones on that skeleton** (a clip bound to nothing
   reports "moves nothing").

## Multiplayer

Same as the Esko: server-authoritative agent, `ClientNetworkTransform`, `NetworkedHealthComponent`,
`NetRelay`. The `NetworkObject` is added fresh (a copied one would carry the template's
`GlobalObjectIdHash`) and `NetworkPrefabRegistrar.Sync` registers the prefab. A rideable creature carries the Esko's
`MountNetworkSync` and the saddle's one-bool `NetMsg.SaddleFit`/`SaddleSet` on its own relay
([Saddles.md](Saddles.md)).

## Persistence

Same savers as the Esko (transform, health, agent state, flee, provocation, goal, faction, remains).
`SaveableEntity` is added fresh and `StampPrefabId` gives it its prefab id. A rideable creature
also saves `SaddleSaveable` (saddled or not) and `MountSaveable`, copied from the Esko.

## Gotchas

- **Create-only, except for size and skeleton.** Import, override controller and eye style refresh
  every run; the prefab is rebuilt only for a new import scale (unpacked bones no longer match the bind
  poses) or a changed skeleton (`SkeletonChanged`: clips would bind to nothing). Other roster changes
  need *Rebuild*, which keeps the GUID and `GlobalObjectIdHash` (AgentSystem Gotchas).
- **A skinned renderer's `bounds` lie about these bodies** — imported `localBounds`, up to 1.75x the
  real height, which oversized every collider and agent. `BodyBounds` bakes the mesh
  (`ClankerBodyBuilder.SkinnedBounds`), as the Clanker bodies do (AgentSystem Gotchas).
- **Donor skeletons:** Kaval, Skarn and FrillBoar keep pre-rig_v2 skeletons (FrillBoar's clean-room
  clips use `LeftUpLeg`, `Spine`…`Spine4`; the user chose to keep it). Never copy WORLD rotations
  between rigs whose rest poses differ: legs cross and feet slide.
- **The measured stride under-reads gallops:** a foot planted 2–3 frames barely registers
  (WingWalker Run read 4.6 m/s vs 8.9 authored). Always run `patch_manifest_speeds.py`.
- **The origin must sit between the planted feet in `Stand`** — the agent turns about it; donor
  origins 0.1–1.1 m off slid the feet on every turn. Moving it moves eyes and bind pose, so FBX and
  manifest are regenerated together.
- **A new bind pose changes size** (roster height ÷ baked rest height): NeedleWader's folded wings
  took it 2.40 → 1.95 m. The 2026-10-28 swap scaled the roster for every rest change over 2 %.
- **Review verdicts are keyed by clip name**: clear a re-animated creature's review-log entries.
- **Saddle bones are `Spine2`** on every rideable creature; renaming the spine needs a roster edit.
- **Clips can be unreadable right after an import change** (ArtPipeline Gotchas): run the build again.
- **Never run the project-wide wiring passes from this builder.** `RagdollWiring.WirePrefabs` /
  `SaveableWiring.TryWirePrefabs` re-serialise EVERY unwired prefab (13 unrelated ones once). Only
  the append-only `NetworkPrefabRegistrar.Sync` stays project-wide.
- **`GetComponent(type) ?? AddComponent(type)` never adds in the Editor** — fake-null; test `== null`.
- **"duplicate GlobalObjectIdHash source entry" is the editor-only root list**; the live list
  (`ScriptableObjects/Networking/`) had 0 duplicates on 2026-10-27.
- **A held-pose "Idle" freezes the creature.** `ClipMap` takes a candidate only if it turns a bone
  > 0.5° (`MovesSkeleton`); an un-overridden Esko clip binds nothing, so every row ends in a clip the
  creature has.

## Extending

- Another clip for a creature: add the slot in Blender; to have a module play it, add the slot to
  `ClipMap` under the Esko clip that module triggers.
- A creature that should not behave like the Esko (a predator, a flyer): build it, then edit the
  prefab by hand — it is never overwritten.
