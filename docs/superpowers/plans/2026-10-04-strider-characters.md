# Strider Characters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The user's five Strider models in `strider1.blend` become the Striders: four rigged humanoids take over the four Strider nomad prefabs, and a four-legged cyborg elder (1–3 per walking city) stands on the houses' decks and goes ashore with their crew.

**Architecture:** Blender scripts work on a one-time copy (`strider_characters.blend`) and export one FBX per character. The humanoids go through the existing `NomadPrefabBuilder` Strider recipes (new FBX, no cloth recolour) after their prefabs are renamed in place with `AssetDatabase.MoveAsset`. The elder is a rigid-part rig on the crab walker's legged stack (`CrabLocomotion` + `CrabDriver`, four legs), built by a new `StriderElderBuilder`. It rides a standing post: an extra `VesselSeats` seat on each house that `CrewShift` reserves for members carrying a `StandingRider`, which parks the legs while seated and resumes them on release.

**Tech Stack:** Unity 6 (C#, NGO, NUnit EditMode tests), Blender 5.2 headless Python.

**Spec:** [docs/superpowers/specs/2026-10-04-strider-characters-design.md](../specs/2026-10-04-strider-characters-design.md)

## Global Constraints

- Never write `strider1.blend`; `strider_characters.blend` is created from it once, beside it, and never regenerated over.
- Humanoids: Humanoid avatar (Create From This Model), `avatar.isHuman && avatar.isValid`; 3.0 m ± 0.1 tall; at most 6 skinned renderers; no local (unlinked) materials in Blender.
- Mapping: `Collection` → `Strider_Horned`, `warrior` → `Strider_Warrior`, `Collection 3` → `Strider_Longcoat`, `Collection 4` → `Strider_Beanie`, `cyborg` → `StriderElder`.
- Prefab renames by `AssetDatabase.MoveAsset` (GUIDs kept): `StriderNomad_Umber` → `Strider_Horned`, `_Maroon` → `Strider_Warrior`, `_Tan` → `Strider_Longcoat`, `_StrawHat` → `Strider_Beanie`.
- Elder count per city drawn from {1: 0.65, 2: 0.25, 3: 0.10}, serialized on the template, seeded by the group's `RosterSeed`; never in war parties.
- Same height as other NPCs: 3.0 m (capsule and agent).
- Template changes only through `RosterAuthoring.WireStriderCity`; keep start (2500, 106, 500), barges and 30 crew.
- Export through `_exportlib.export(..., keep_armature=True, triangulate=True)` into `Assets/Game/Art/Models/Characters/Striders/`.
- Shared editor: wait while `EditorApplication.isPlaying`; check free RAM ≥ 2.5 GB before each Blender run; one Blender process at a time, one character per run, never alongside a Unity builder/test run.
- Every behaviour change updates its doc in the same commit (Striders.md, ArtPipeline.md, EditorTooling.md, Locomotion.md, the-systems.md).

## Review Focus

1. **A city that unfolds mid-stop with elders ashore** — elders spawn on foot at the gangway and the gate waits for them; pinned by a `CrewShift` test that takes an elder ashore and checks `AllAboard` stays false until it is seated.
2. **More elders than standing posts** (a template drawing 3 elders for 3 houses is fine, a future 4th would not be) — the extra elder must not take a crew post; pinned by `CrewShiftTests.ACrewPost_IsNeverTakenByAStandingRider` and a `HasRoomFor` test.
3. **An elder unseated onto the gangway walks from there, not from where it boarded** — `StandingRider` resumes the legs from the transform; pinned by a test that parents/unparents and checks `LeggedLocomotion.BodyPosition` follows.
4. **A crew member never takes the standing post** when crew posts are full (recall seats by kind, not first-free); pinned by the same `CrewShift` test as 2.
5. **Weighted count with an empty weight list** behaves exactly as `count` (every existing template) — pinned by `NpcGroupCompositionTests.NoCountWeights_UsesCount`.

---

### Task 1: Working copy and humanoid rig (Blender)

**Files:**
- Create: `Assets/Game/Art/Models/_Source~/models/vehicles/strider_characters.blend` (copy of `strider1.blend`, once)
- Create: `Assets/Game/Art/Models/_Source~/models/vehicles/strider_characters_rig.py`

The base body is not a continuous sculpt: it is the segmented "Body Male - Primitive (Realistic)" kit, one object per segment (`GEO-pelvis_…`, `GEO-arm_upper_…L`, …) in an object-parent chain whose **origins are the joints**. So the skeleton is read off the segment origins (the kit's own pivots — joints from the mesh, not by eye), each segment is weighted 100 % to its bone, and garments get soft weights from the segments. This replaces the spec's weight transfer from `human_sculpt_base_rigged.blend`, whose joints do not match this body.

- [ ] **Step 1:** Copy once: `cp -n strider1.blend strider_characters.blend` (refuse if it exists).
- [ ] **Step 2:** Write `strider_characters_rig.py <collection> <name>`: refuses when `Char_<name>` already exists; then
  1. **Dedupe** exact duplicates (same vertex count and world bounds within 1 mm) — the cyborg holds two stacked copies of most of its torso.
  2. **Bake** every object: apply modifiers (Subdivision capped at level 1), clear parents keeping world matrix, apply the transform to the mesh data and recalculate normals where the determinant is negative (mirrored skinned meshes arrive inside out).
  3. **Materials:** objects with no material slot get the body material named in `UNSLOTTED_MATERIAL`; every `Mat_X.NNN` slot is pointed at the palette's linked `Mat_X` (linked from `palette.blend` with `bpy.data.libraries.load(link=True)`); materials missing from the palette are reported and the run fails.
  4. **Skeleton** (Unity humanoid names, a subset of `Human_Rig`): `Hips, Spine, Chest, Neck, Head, {Left,Right}{Shoulder,UpperArm,LowerArm,Hand,UpperLeg,LowerLeg,Foot}`, heads at the segment origins (`SEGMENT_BONES` maps segment base name → bone; tails at the child's head).
  5. **Weights:** segment parts → their bone, 100 %. Other parts: per vertex the nearest segment (BVH over each bone's segments) votes; ≥ `RIGID_VOTE` (0.8) of votes for one bone → the part is rigid to it; otherwise a garment: soft weights `w_b = (1 − (d_b − d_min)/BLEND_BAND)²` over bones within the band, top 4, normalised, with garment candidates limited to torso and leg bones (arms swing through a poncho rather than tearing it).
  6. **Merge** everything into one mesh `Strider_<name>` (multi-material: one skinned renderer), parent with an Armature modifier, put mesh + rig in a new collection `Char_<name>`, delete the consumed objects, save.
- [ ] **Step 3:** Run per collection, one Blender process each, checking RAM first. Verify: zero vertices with total weight 0; every bone in the list has weighted vertices; `[m.name for m in bpy.data.materials if m.library is None]` is empty for the character's materials.
- [ ] **Step 4:** Render each humanoid posed (arms down, one leg forward, elbow bent) with `strider/render_posed.py`; look at every image; fix weights if anything tears.
- [ ] **Step 5:** Commit `strider_characters.blend` + script: `feat(art): rig the four Strider humanoids in a working copy`.

### Task 2: Elder rig (Blender)

**Files:** Modify `strider_characters.blend`; create `Assets/Game/Art/Models/_Source~/models/vehicles/strider_elder_rig.py`

- [ ] **Step 1:** Write `strider_elder_rig.py`: dedupe and bake as Task 1 (shared helpers imported from `strider_characters_rig.py`); rigid-part rig `Rig_StriderElder`: `Root`, `Pelvis`, `Chest`, `Neck`, `Head`, `{Left,Right}{UpperArm,LowerArm,Hand}` from the segment origins; per leg id in `FL, FR, RL, RR` (by the leg's world quadrant) a chain `Coxa_<id>` (vertical, at the hip pivot = `Mesh_WalkerLeg_Raised_Upper` origin) → `Hip_<id>` → `Knee_<id>` (= `_Lower` origin) → `Ankle_<id>` (= `_Foot` origin) → `Foot_<id>` (under the ankle at the sole). The robot hand's `Rig_Hand_Splayed` is dropped (its parts become rigid to `RightHand`).
- [ ] **Step 2:** Bone-parent (rigid) every part: leg `_Upper` → `Hip_`, `_Lower` → `Knee_`, `_Foot` → `Foot_`, renamed `Mesh_ElderLeg_<id>_{Upper,Lower,Foot}` (the crab builder finds limb meshes by `_Upper`/`_Lower`/`_Foot`); torso parts → nearest torso bone. Parts sharing a bone are joined (one renderer per bone). Add `<Joint>Pin_<id>` cylinders (dark steel, bone-parented) at every leg joint, long axis on the hinge: vertical for `Coxa`, perpendicular to the leg plane for `Hip/Knee/Ankle`, along the leg's outward direction for `Foot` — `WalkerRig` measures hinges from pins.
- [ ] **Step 3:** Render rest and a posed frame (legs stepped, chest rolled); look at them.
- [ ] **Step 4:** Commit: `feat(art): rig the Strider elder as a four-legged rigid-part walker`.

### Task 3: Export, library index and assets

**Files:** Create `Assets/Game/Art/Models/_Source~/models/vehicles/strider_characters_export.py`; modify `_assets.py` (CHARACTERS gains per-collection entries); regenerate `LIBRARY.md`, `library_index.json`, `blender_assets.cats.txt`; create `Assets/Game/Art/Models/Characters/Striders/{strider_horned,strider_warrior,strider_longcoat,strider_beanie,strider_elder}.fbx`.

- [ ] **Step 1:** `strider_characters_export.py <name>`: `export(SRC, unity_path("Characters", "Striders", f"{snake}.fbx"), keep_armature=True, keep_collection=f"Char_{name}", scale_all=<humanoid>, triangulate=True)`. Humanoids `scale_all=True` (no bone scale 100); the elder `scale_all=False` like the crab (its pins are measured off bone-parented meshes, and the crab ships that way).
- [ ] **Step 2:** `_assets.py`: CHARACTERS values may be `(asset name, collection)`; a tuple marks that collection as the asset instead of building `Char_<Name>`, catalogue `Characters/Striders`.
- [ ] **Step 3:** Run the five exports (one Blender run each), `_index_library.py`, `_assets.py`.
- [ ] **Step 4:** Commit with ArtPipeline.md updated: `feat(art): export the Strider characters`.

### Task 4: Humanoid prefabs

**Files:** Modify `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs`; create `Assets/Game/Editor/Tests/StriderCharacterAssetTests.cs`; move the four prefabs.

**Interfaces:** Produces `NomadPrefabBuilder.StriderPeople` (replaces `StriderNomads`; index 0 is `Strider_Horned`, used by the crab outrider), `NomadRecipe.ClothPalette == null` meaning "authored colours, no recolour, no wind".

- [ ] **Step 1: Failing test** `StriderCharacterAssetTests`: for each Strider recipe, the FBX's avatar is valid and human; the prefab is 3.0 ± 0.1 m tall (renderer bounds of a fresh instance, as `NomadPrefabBuilder.TryMeasure`); ≤ 6 `SkinnedMeshRenderer`s; the prefab path is `Strider_<Variant>.prefab`.
- [ ] **Step 2:** Rename with `AssetDatabase.MoveAsset` (Umber→Horned, Maroon→Warrior, Tan→Longcoat, StrawHat→Beanie), via `ux.py`.
- [ ] **Step 3:** Recipes: `StriderPeople` = four explicit recipes (`Name`, `FbxPath` under `Characters/Striders`, `PrefabPath`, Strider faction/roster, `ClothPalette = null`, dialog lines, `RandomWeapon`, `TravelsToGoals`); `ApplyClothMaterial` returns without a warning when the palette is null; remove `StriderCloth`. Update references (`StriderCrabOutriderBuilder`, `StriderMonowheelBuilder`, tests) to `StriderPeople`.
- [ ] **Step 4:** Run `Build Strider Nomad NPCs` (renamed `Build Strider People`), then the crab outrider and monowheels (their riders). Tests pass: `StriderCharacterAssetTests`, `StriderRosterAssetTests`, `StriderNetworkPrefabTests`, `StriderCrabOutriderTests`, `StriderMonowheelPrefabTests`.
- [ ] **Step 5:** Render one posed in Unity (`render_pose.cs`). Commit with Striders.md: `feat(striders): the user's four humanoids become the Strider people`.

### Task 5: Standing posts in CrewShift

**Files:** Create `Assets/Game/Scripts/Vehicles/Crew/StandingRider.cs`; modify `CrewShift.cs`, `NpcWorldSim.cs` (one call), `LeggedLocomotion.Body.cs` (`ResumeFromCarry`); tests in `CrewShiftTests.cs`, new `StandingRiderTests.cs`.

**Interfaces:**
- `CrewShift.standingPosts` (serialized int): the last N `VesselSeats` seats are standing posts.
- `bool CrewShift.HasRoomFor(GameObject memberOrPrefab)`; `static CrewShift FirstWithRoom(IEnumerable<GameObject> members, GameObject memberOrPrefab)`.
- `StandingRider` (on the elder): `static bool Is(GameObject)`; parks `LeggedLocomotion` (`enabled = false`) while its parent chain holds a `VesselSeats`, and on release enables it and calls `LeggedLocomotion.ResumeFromCarry()`.
- `LeggedLocomotion.ResumeFromCarry()`: `ResetBodyState()`, then `SnapToGround()` when owning the body, `GroundFeet()` when followed.

- [ ] **Step 1: Failing tests** — `CrewShiftTests`: `AStandingRider_TakesOnlyAStandingPost`, `ACrewPost_IsNeverTakenByAStandingRider`, `HasRoomFor_CountsCrewAndStandingSeparately`, `FirstWithRoom_PassesOverAHouseWhoseStandingPostIsTaken`, `AnElderAshore_HoldsTheGate_UntilSeated`; `StandingRiderTests.Seated_ParksTheLegs_Released_ResumesFromTheTransform`.
- [ ] **Step 2:** Implement (code in the commit; `Seat` by kind: `SeatIndexFor(member)` picks the first free index in the member's range, `TickRecall` and `Take` use it).
- [ ] **Step 3:** Tests pass (`CrewShiftTests`, `CrewShiftLogicTests`, `RuntimeGroupTests`, `StandingRiderTests`). Commit with Striders.md + Locomotion.md: `feat(striders): standing posts for the walking city's elders`.

### Task 6: Weighted member count

**Files:** Modify `NpcGroup.cs` (`NpcGroupMemberSpec.countWeights`), `NpcGroupComposition.cs`; tests in `NpcGroupCompositionTests.cs`.

**Interfaces:** `[Serializable] struct WeightedCount { int count; float weight; }`; `int NpcGroupMemberSpec.DrawCount(int rosterSeed, int index)` — empty weights → `count`; otherwise `RosterDraw.PickWeighted` with `RosterDraw.Roll01(seed, index + CountSalt)`.

- [ ] **Step 1: Failing tests:** `NoCountWeights_UsesCount`, `CountWeights_StayInRange_AndAreDeterministic`, `CountWeights_MatchTheirWeights_OverManySeeds` (10 000 seeds, each share within 0.02).
- [ ] **Step 2:** Implement; `Resolve` uses `spec.DrawCount(group.RosterSeed, plan.Count)`.
- [ ] **Step 3:** Tests pass. Commit with AgentSystem.md (template field) + Striders.md: `feat(agents): a group member's count can be a seeded weighted draw`.

### Task 7: StriderElder prefab and torso sway

**Files:** Create `Assets/Game/Editor/Creatures/StriderElderBuilder.cs`, `Assets/Game/Scripts/Creatures/StriderElder/StriderElderSway.cs`, `StriderElderSwayMath.cs`, `Assets/Game/Editor/Tests/StriderElderTests.cs`; modify `CrabWalkerBuilder.cs` (share `DropModelOntoHips`, `AddLimbBox`, `WireLocomotion` as `internal static` with the parameters that differ), `EntityFactionWiring.Assignments` (elder → Striders).

- [ ] **Step 1: Failing tests** `StriderElderTests`: four legs (`Coxa_`… chains with `Foot_`), a pin under every leg joint; `CrabLocomotion` + `CrabDriver` (`lateralSteering` off), `AgentController`, `HealthComponent`, `EntityFaction` (Striders), `GoalTravelModule`, `StandingRider`, `StriderElderSway`, `NetworkObject`, `SaveableEntity`; registered in the network prefab list; 3.0 ± 0.15 m tall; `StriderElderSwayMath` is zero at rest and bounded by its amplitude.
- [ ] **Step 2:** Implement the builder (`Tools/Creatures/Build Strider Elder`): body = elder FBX unpacked under a root, model scaled to 3.0 m, origin dropped onto the hip plane, limb boxes, `CrabLocomotion` tuned for a slow upright walk (`slopeFollow` 0.5), `CrabDriver` (`moveSpeed` 1.6, no lateral steering), health, perception, targeting, provocation, faction, `GoalTravelModule`, `StandingRider`, sway, `SceneTracked` (Migrate), `UnderTerrainGuard`, netcode, saveable, then `NetworkPrefabRegistrar.Sync`, `SaveableWiring`, `RagdollWiring`, `KeepSceneMigrationSync`.
- [ ] **Step 3:** Build, tests pass, render it in Unity. Commit with Striders.md + EditorTooling.md + Locomotion.md: `feat(striders): the cyborg elder, a four-legged walker`.

### Task 8: Elder posts on the houses and the template

**Files:** Modify `StriderCityBuilder.cs` (an `ElderPost` seat appended per house, raised by the elder's hip height, front-centre of the deck ring), `CrewDeckWiring.AddCrewDeck` (optional standing posts → `CrewShift.standingPosts`), `RosterAuthoring.WireStriderCity` (elder spec last: prefab, `crew: true`, weights 1:0.65/2:0.25/3:0.10); tests in `StriderHabitatWalkerTests.cs`, `StriderCityTemplateTests.cs`.

- [ ] **Step 1: Failing tests:** each house has `CrewPosts + 1` seats and `standingPosts == 1`, the post is on the deck above the elder's ride height; the template's last member is the elder with the stated weights, crew, and nothing after it.
- [ ] **Step 2:** Implement, rebuild the habitat, re-run `Wire Strider City`, check `git diff` of `persistentScene.unity` touches only the strider-city template.
- [ ] **Step 3:** Tests pass (`StriderHabitatWalkerTests`, `StriderCityTemplateTests`, `StriderBargeTests`). Commit with Striders.md, the-systems.md: `feat(striders): elders ride standing posts on the walking city's houses`.

### Task 9: Docs, validation and play-check list

- [ ] Run `py tools/docs_check.py --index`; fix failures; report what needs the user's Play-Mode check (host + client: elders on decks, step off/on at a stop; save/reload mid-march and mid-stop, elder entries in the save JSON).
