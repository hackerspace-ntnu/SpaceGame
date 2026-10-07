# Strider characters: the user's models as the Striders

**Date:** 2026-10-04 · **Status:** design approved in conversation, awaiting spec review · **Branch:** Feat/create-factions

## Goal

Replace the Striders' borrowed bodies (the Sand-nomad body under a rust-red recolour) with the
five characters the user modelled in `Assets/Game/Art/Models/_Source~/models/vehicles/strider1.blend`:
four humanoid Striders and a four-legged **cyborg elder**, a Strider with robotic parts who leads the
tribe. Success: a walking city in play shows the four new humanoids as its crew, scouts and warriors,
and one to three cyborg elders standing on its houses' decks. All of them are animated and as tall as
the other NPCs, and they survive host/client and save/reload like the bodies they replace.

## What is in the file (measured 2026-10-04)

| Collection | Becomes | Parts | Triangles | Notes |
|---|---|---|---|---|
| `Collection` | `Strider_Horned` | 77 meshes | 36 k | poncho over long coat, horned helmet, gas mask |
| `warrior` | `Strider_Warrior` | 80 | 40 k | armoured shoulders, helmet |
| `Collection 3` | `Strider_Longcoat` | 59 | 24 k | long coat, bare masked head |
| `Collection 4` | `Strider_Beanie` | 72 | 30 k | poncho, beanie, gas mask |
| `cyborg` | `StriderElder` | 144 | 120 k | humanoid torso, head and arms on four robotic legs |

- **No rigs.** The only armature in the file is a robot-hand part's. Nothing is weighted.
- **Shared base body.** The four humanoids share a base body ("Body Male - Primitive (Realistic)") in an A-pose, 1.8 m tall. The cyborg is 1.58 m tall and 1.43 m across its legs.
- **Duplicated materials.** Each character carries 90–260 duplicated palette materials (`Mat_*.005`, …).

## Decisions (from the brainstorm)

1. **Cyborg is a real four-legged walker**, not a humanoid with stiff leg props.
2. **Elders are rare but plural:** 1–3 per walking city (usually 1, sometimes 2, rarely 3), drawn
   seeded like every other member so a reloaded city comes back with the same elders. Never in war
   parties or other groups.
3. **Elders ride the houses' decks**, at most one per house and the lead house first. At a stop they
   step down with their house's crew and stand among them, and the crew gate waits for them to re-board.
4. **Same height as the other NPCs: 3.0 m** (capsule and NavMesh agent), so the ×~1.67 scale from
   Blender, and every crew post, gangway and saddle sized for nomads, keeps fitting.
5. **The four humanoids take over the four existing Strider prefabs**, renamed with
   `AssetDatabase.MoveAsset` so GUIDs (and with them the city template, saves, roster and network
   prefab list) are kept. Old → new: `StriderNomad_Umber` → `Strider_Horned`, `_Maroon` → `Strider_Warrior`,
   `_Tan` → `Strider_Longcoat`, `_StrawHat` → `Strider_Beanie`. Roster roles (Scout, Warrior) are unchanged.
6. **Authored colours.** The models keep the user's palette materials; the Strider cloth recolour is dropped.
   The Sand nomads keep their bodies and recolours untouched.
7. **The cyborg's upper body has no motion clips.** Humanoid clips cannot drive it and authoring a set is
   separate art work: its legs step procedurally and its torso rides them with a slight sway.

## Design

### 1. Blender: rig, merge, export (one script per concern, never over the user's file)

- **Work on a copy.** `strider_characters.blend` is created from `strider1.blend` once and never
  regenerated over (ArtPipeline: the `.blend` is the only copy). The user's `strider1.blend` is opened read-only.
- **Humanoid rig.** Fit the shared humanoid skeleton (the `Human_Rig` of
  `models/characters/drifters/human_sculpt_base_rigged.blend`, whose bone names Unity's humanoid avatar
  already maps) to the base body by **geodesic level sets** (ArtPipeline gotcha: place joints from the mesh,
  not by eye). Then **transfer weights** from that rigged sculpt base onto the base body, rather than
  `ARMATURE_AUTO`, which proximity-binds coats to the wrong bones (ArtPipeline: never ship bone-heat weights
  unfiltered).
- **Parts.** Each rigid part (mask, pouch, buckle, shoulder plate) is weighted 100 % to its nearest bone.
  Cloth that spans joints (coats, ponchos) takes the body's transferred weights at each vertex. Then every
  part is **merged into one skinned mesh per material**, a handful per character instead of 59–80. This
  matters: the Strider performance review found today's nomads cost about 44 skinned renderers each.
- **Materials.** The duplicates are relinked to `palette.blend`. After relinking, `[m.name for m in
  bpy.data.materials if m.library is None]` must be empty (ArtPipeline gotcha).
- **Cyborg rig.** A generic armature: root, pelvis, spine, chest, neck, head, two arm chains, and four leg
  chains (hip, knee, ankle) matching the robotic legs' joints, each leg part rigid-weighted to its bone.
- **Export.** One FBX per character through `_exportlib.export(..., keep_armature=True, triangulate=True)`
  into `Assets/Game/Art/Models/Characters/Striders/`.
- **Library.** Re-run `_index_library.py` and `_assets.py`. The four humanoids and the elder join
  `CHARACTERS` in `_assets.py` under `Characters/Striders`.

### 2. Unity: the four humanoids

- **Import.** Humanoid avatar, Create From This Model. Verified by `avatar.isHuman && avatar.isValid` with
  every required bone mapped. A human bone with no weighted vertices is silently unmapped (ArtPipeline gotcha).
- **Build.** The four Strider recipes in `NomadPrefabBuilder` point at the new FBX and drop the cloth
  recolour. The rest of the existing Strider stack (faction, roster role, `GoalTravelModule`, crew-post
  seating, weapons, ragdoll, savers, netcode, `CharacterActionWiring`) is reused unchanged. The model is
  scaled to the recipe's 3.0 m height, measured off the skinned bounds in the bind pose, never in the
  edit-mode animator pose.
- **Rename** the four prefabs first, with `MoveAsset`, so the builder writes over the renamed assets.

### 3. Unity: the cyborg elder

- **`StriderElderBuilder`** (Editor/Creatures), built the way `StriderCrabOutriderBuilder` builds on
  `CrabWalkerBuilder.BuildBody`. It uses the legged locomotion stack (the same IK foot placement and
  stepping) with four legs and the elder's own stride. On top go the Strider faction, health,
  `AgentController`, savers, netcode and network prefab registration, and ragdoll wiring only if the
  existing wiring accepts a legged rig. It gets no combat module of its own: it defends itself through
  the faction's provocation, as the crew do.
- **Torso sway.** A small serialized sway on the chest and head bones, driven from the body's speed, so it
  does not read as a statue on legs.

### 4. The walking city

- **Template.** The `strider-city` caravan gains an elder member spec: count drawn from {1: 0.65, 2: 0.25,
  3: 0.10}, serialized on the template and seeded through the group's existing `RosterDraw` seed.
- **Standing posts.** Each house prefab (`StriderHabitatWalker`) gains one `ElderPost` on its deck, an
  anchor the elder is parented to like a crew post. The elder's locomotion is parked in a standing pose
  through the same suppress/give-back path that seats crew (`NpcSeating`), so it rides the house with no
  legged physics on a moving deck. Elders fill posts lead house first.
- **Stops.** `CrewShift` treats an elder like its house's crew: it goes ashore with them, stands among them,
  and the departure gate counts it when it waits for everyone aboard.

## Multiplayer

The server owns the group: `NpcWorldSim` spawns elders like any member, and each has a `NetworkObject`
registered in the network prefab list (via the builder's existing registration step). Posts and
parenting go through the same server path as crew seating, so clients see what the crew already show.
The procedural legs and torso sway run on every machine from the replicated transform, with no messages.
**Verify on a client:** elders visible on the decks, stepping off and back on at a stop.

## Persistence

Elders are group members, saved and folded with the group exactly as crew are: their count and identities
come back from the seed, and whether they are aboard comes back with the crew's ashore state
(`NpcGroup.CrewAshore`). The humanoids keep their prefab GUIDs, so existing saves resolve.
**Verify by reloading** mid-march and mid-stop, checking the elder entries in the save JSON.

## Testing

- **Asset tests.** For each humanoid: the avatar is valid and humanoid; it is 3.0 m ± 0.1 tall; it has at
  most 6 skinned renderers; it has no local (unlinked) materials, checked in Blender.
- **Elder prefab test.** Four legs with feet; the components a group member needs; registered; a save id.
- **Template test.** The elder count draw stays in 1–3 with the stated weights, and is deterministic for a seed.
- **`CrewShift` tests.** An elder boards, goes ashore and re-boards with its house, and the gate waits for it.
- **Roster tests.** `StriderRosterAssetTests` still pass with the renamed prefabs.
- **Play check.** The user plays on host and client and saves/reloads.

## Docs

Update Striders.md (people, the elder, the template, posts) and ArtPipeline.md (the rigging pipeline and its
gotchas). Add EditorTooling rows for the new builder. Add a line to `docs/Human/the-systems.md` if the
elder counts as a new system.

## Out of scope

- arm and upper-body animation for the elder
- any elder-specific behaviour, dialogue or goodwill weight
- new crew-post layouts beyond one elder post per house
- the performance plan's nomad renderer merge for the Sand and Sky nomads, which is tracked in the performance plan

## Risks

- **Coats over legs.** Rigid long coats over walking legs may clip. Mitigation: coat panels take the leg
  weights below the hip line. Judge it in play before tuning.
- **Rig fit.** The base body is not the sculpt base the weights come from. If the transfer is poor, fall
  back to fitting joints geodesically and smoothing the transferred weights. Pose it before believing it
  (ArtPipeline gotcha).
- **Ragdoll on a legged rig.** If `RagdollWiring` refuses the elder's skeleton, it ships without a ragdoll.
  It dies the way the crab outrider dies, and this spec's tests record that.
