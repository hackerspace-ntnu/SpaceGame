---
system: WildCreatures
layer: pipeline
summary: "Concept-art creatures: AI mesh on a donor rig in Blender, built into a wild-animal prefab"
paths:
  - Assets/Game/Editor/Creatures/WildCreatureBuilder.cs
  - Assets/Game/Prefabs/agents/creatures/Wild/
  - Assets/Game/Art/Animations/Creatures/
  - Assets/Game/ScriptableObjects/Eyes/Creatures/
  - Assets/Game/Art/Models/_Source~/models/creatures/
symptoms:
  - "a wild creature glides with its legs barely moving, or its feet skate"
  - "a wild creature's prefab was not rebuilt after re-exporting its FBX"
  - "Build Wild Creatures says clips are not readable yet"
  - "a creature has no eyes in game, or its eyes never blink"
  - "a wild creature can be saddled or ridden"
reads_with: [AgentSystem, ArtPipeline, StylizedEyes, Persistence, Multiplayer, Combat]
updated: 2026-10-27
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

- **A wild creature is the Esko without the saddle.** `Esko.prefab` is the finished
  peaceful-until-provoked animal (perception, wander, flee-or-fight, provocation, ragdoll, savers,
  netcode). The builder copies its root components field for field onto the new body and leaves out
  the mount parts (`MountModule`, `SteerModule`, `SaddleSocket`, `SaddleQuickRelease`,
  `SaddleSaveable`, `MountSaveable`, `MountNetworkSync`, `MountedRiderPose`). Retuning the Esko's
  behaviour therefore retunes every creature built after it — not the ones already built.
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
- **Stride speed is measured, not tuned.** The clips are in place, so a planted foot slides
  backward under the body at the speed the body should travel; the Blender side takes the median
  of that slide over every foot. The prefab moves at exactly those speeds
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
| `<Name>.creature.json` | `Art/Models/Creatures/Organic/<Name>/` | Manifest: clips, strides, bounds, eyes |
| `<Name>.overrideController` | `Art/Animations/Creatures/` | The creature's clips on `Esko.controller` |

## Flows

1. **Blender** (outside the project): the AI mesh is cleaned, a donor animal's skeleton is laid
   into it and its clips transferred, the coat is painted and eyeballs added; the `.blend` goes to
   `_Source~/models/creatures/<snake>/`, the FBX (through `_exportlib`, armature + takes,
   triangulated) and its textures to `Art/Models/Creatures/Organic/<Name>/`, with the manifest.
2. **Build:** *Build Wild Creatures* → per manifest: import settings (Generic, avatar from the
   model, clip names = slots, loop flags, root locked, materials in the FBX found by texture name)
   → override controller → eye style + material → **if the prefab does not exist**: instantiate and
   unpack the FBX, copy the Esko's animator settings and root components, fresh `NetworkObject` and
   `SaveableEntity`, size the `BoxCollider` and `NavMeshAgent` from the body bounds, set the speeds,
   scale `RagdollRig.totalMass` with the body's volume, wire the eyes, `SaveablePolicy.Ensure`, save;
   then, for every creature, stamp the saveable prefab id (the prefab's GUID) into the file, and
   finally `NetworkPrefabRegistrar.Sync` (append-only) registers new prefabs.
3. **Verify:** *Verify Wild Creatures* checks per prefab: override complete, avatar valid,
   `NetworkObject` registered, a saveable prefab id, `EyeBlink` present, no mount component, no
   missing script, and that **every clip moves bones on that skeleton** (a clip bound to nothing
   reports "moves nothing").

## Multiplayer

Same as the Esko: server-authoritative agent, `ClientNetworkTransform`, `NetworkedHealthComponent`,
`NetRelay`. The `NetworkObject` is added fresh (a copied one would carry the template's
`GlobalObjectIdHash`) and `NetworkPrefabRegistrar.Sync` registers the prefab.

## Persistence

Same savers as the Esko (transform, health, agent state, flee, provocation, goal, faction, remains).
`SaveableEntity` is added fresh and `SaveableWiring.TryWirePrefabs` gives it its prefab id.

## Gotchas

- **Create-only.** An existing prefab is never rebuilt — prefabs are authored assets. Import
  settings, the override controller and the eye style ARE refreshed every run, so a re-exported FBX
  with the same bones reaches the prefab. Delete the prefab to rebuild it (its GUID changes).
- **Clips can be unreadable right after the import is changed** (ArtPipeline Gotchas): the build
  stops that creature with "clips … not readable yet. Run the build again."
- **Never run the project-wide wiring passes from this builder.** `RagdollWiring.WirePrefabs` and
  `SaveableWiring.TryWirePrefabs` re-serialise EVERY prefab in the project that lacks wiring: the
  first build rewrote 13 unrelated prefabs (Shellback, Workshop, the workshop gears, Oathbranch…),
  dropping obsolete fields and adding savers, and tried to save the SpriteHamlet prefabs, which have
  missing scripts. The creatures get their wiring directly instead: `AgentRagdoll`/`RagdollRig` and
  the savers come with the Esko stack, `SaveablePolicy.Ensure` fills gaps, `StampPrefabId` writes
  the id. Only `NetworkPrefabRegistrar.Sync`, which only appends list entries, stays project-wide.
- **`GetComponent(type) ?? AddComponent(type)` never adds in the Editor.** A missing component comes
  back as Unity's fake-null placeholder, which `==` treats as null but `??` does not, so the next
  `CopySerialized` threw `MissingComponentException ... Parameter name: dest`. Test with `== null`.
- **"NetworkPrefab (X) has a duplicate GlobalObjectIdHash source entry" on a fresh build is the
  editor-only root list.** Netcode auto-adds every networked prefab to `Assets/DefaultNetworkPrefabs.asset`,
  which NetworkManager does not read (the live list is `ScriptableObjects/Networking/DefaultNetworkPrefabs.asset`),
  so each creature sits in both. Checked 2026-10-27: the live list held 357 prefabs with 0 duplicate
  hashes, and each new prefab's on-disk `GlobalObjectIdHash` equals `NetworkObject.PrefabIdHash`.
- **A donor's "Idle" can be a held pose.** The mallard's is, so every bird-donor creature stood
  frozen. `ClipMap` candidates are taken only if they turn some bone by more than 0.5° between the
  first frame and the middle (`MovesSkeleton`), so Idle falls through to IdleTailWag/IdleRuffle, which
  `AlwaysLoop` then forces to loop. An Esko clip left un-overridden binds no bones on another skeleton,
  so every row ends in a clip the creature has.

## Extending

- Another clip for a creature: add the slot in Blender; to have a module play it, add the slot to
  `ClipMap` under the Esko clip that module triggers.
- A creature that should not behave like the Esko (a predator, a flyer): build it, then edit the
  prefab by hand — it is never overwritten.
