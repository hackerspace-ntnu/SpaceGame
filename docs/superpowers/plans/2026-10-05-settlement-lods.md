# Settlement LODs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Strider city and the Sky fleet draw a generated, merged far level; every Strider city vehicle throws huge sparse dust at range; and the folded Strider city is drawn (in dust) from the edge of the loaded ground on every machine, handing over to the live city in the same slots.

**Architecture:** An editor baker (`SettlementLodBaker`) gives each prefab a `LODGroup` whose LOD0 is its own renderers and whose LOD1 is one rest-pose mesh per material carrying Unity 6.3 Mesh LODs; a runtime `MergedLod` component points at it. A new `FarDust` emitter on every Strider city vehicle crossfades in over the band its near dust fades out. A `DistantGroups` NetworkBehaviour on the session's NetworkObject replicates the opted-in group's `{hash, template, seed, position, yaw, spawned}`; a `DistantGroupSilhouette` on the same object draws each vehicle's merged mesh in the slots `GroupColumnLayout` gives the live spawn.

**Tech Stack:** Unity 6000.3.11f1, URP, Netcode for GameObjects 2.9.1 (embedded, patched), NUnit EditMode tests, `UnityEditor.MeshLodUtility`.

**Spec:** [docs/superpowers/specs/2026-10-05-settlement-lods-design.md](../specs/2026-10-05-settlement-lods-design.md) — read it in full before starting any task.

## Global Constraints

- **Never enter or stop Play Mode.** Never compile or run tests while the editor is playing (`rt.py` waits for that itself). Never run an unfiltered test run — package tests enter Play Mode — always pass a test class regex.
- **Commands** (run from the paths shown; `SCRATCH` = `C:/Users/tobia/AppData/Local/Temp/claude/C--Users-tobia-Documents-spaceGame-SpaceGame/ab2a1a7b-473e-4041-a764-18ee3b609e3d/scratchpad`):
  - EditMode tests: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py rt.py '<TestClassRegex>'`
  - C# in the editor: write the snippet to `$SCRATCH/<name>.cs` (a method body that `return`s a string), then `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py ux.py < <name>.cs`
  - Offline compile (no editor): `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/typecheck.py --editor`
  - Docs: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/docs_check.py --index`
- **The editor is shared** (other agents, a human). Never leave C# that does not compile on disk: the "see it fail" step of every task is `typecheck.py --editor` reporting the missing type/member (the red), never a Unity refresh. Refresh Unity (via `rt.py`) only once `typecheck.py --editor` exits 0.
- **No profiler capture** unless more than 2.5 GB RAM is free: `powershell -c "[math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1MB,2)"` prints free GB. **No Blender.**
- **Git:** other agents and humans have uncommitted files (e.g. `Assets/Game/Art/Models/_Source~/…`, ThirdParty assets, `docs/AI/ArtPipeline.md`). Stage only the files your task lists (plus Unity's `.meta` for files you created, and the regenerated `docs/AI/INDEX.md`/`ROUTING.md`). Never `git add -A`/`.`, never commit others' work, never push, never amend. Every commit message ends with a blank line and `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.
- **Docs are part of each task:** update the governing doc(s) named in the task (rows, `## Gotchas`, `symptoms:`, bump `updated: 2026-10-05` or the day you work), run `docs_check.py --index`, and commit the doc with the code. A new system doc needs a `docs/Human/the-systems.md` entry (Task 1 adds it). Docs have a 150-body-line cap: replace, don't append, where a row already says it.
- **No code smells** (CLAUDE.md): no dead code, no debug logs, no magic numbers (serialize tunables or name constants), no silent `catch`, no copy-paste.
- **Spec values (verbatim):** near dust fades out `lodNear 80` → `lodFar 200` (FootfallDust/RollingDust); far puffs "~4× larger (~8–13 m), ~⅕ the rate, longer life"; replication "~2 Hz"; merged mesh asset `<Prefab>_LOD1_Merged.asset` beside the prefab, overwritten on every bake; menu `Tools/SpaceGame/Art/Bake Settlement LODs`; "Particles are never in a LOD level"; "Colliders, scripts and network components are untouched"; "nothing new saved"; opt-in `NpcGroupTemplate.showFromAfar` (default false), set on `strider-city` by `WireStriderCity`.
- **Multiplayer and persistence (CLAUDE.md non-negotiables), answered once for the whole plan:** LODs and dust are presentation computed on every machine from what it already sees — nothing sent, nothing saved. `DistantGroups` is server-written / everyone-read state; the host reads the same list as a client. **No new saved state:** the silhouette is rebuilt from the group record, whose `position` and `rosterSeed` are already saved (`NpcGroup.Record`).

## Review Focus

1. **A mirrored (negative-scale) part** in a model — common in Blender exports — must not come out inside-out in the merged level. `Mesh.CombineMeshes` transforms normals but not triangle winding. Pinned by `SettlementLodBakerTests.AMirroredPart_IsNotTurnedInsideOut` (Task 1).
2. **A wreck seen from afar** (a dead crab outrider or monowheel — the city vehicles that carry a `HealthComponent`, both on the root — lying where it fell) must not be drawn as its standing rest pose. `MergedLod` holds LOD0 while the root's `HealthComponent` is dead. Pinned by `SettlementLodBakerTests.AWreck_HoldsFullDetail…` (Task 1) and `SettlementLodPrefabTests.AVehicleThatCanDie_HasItsHealthOnTheRoot` (Task 5).
3. **Rebuilding a prefab twice** must neither leave a stale merged mesh nor mint a new asset GUID (which would orphan every reference). Pinned by `SettlementLodBakerTests.Rebaking_OverwritesTheSameMeshAsset…` (Task 1).
4. **No camera, or no ground under a slot** (a headless server, a test, the edge of the loaded chunks): nothing is drawn rather than a city floating over the void. Pinned by `DistantGroupSilhouetteTests.ShouldShow…` and `…GroundUnder_IsNaN_OffEveryLoadedTile` (Task 6).
5. **The silhouette's column must equal the live spawn's for every seed and heading**, or the hand-over jumps. Pinned by `DistantGroupSilhouetteTests.TheSilhouette_StandsInTheLiveSpawnsSlots…` over five seeds and a turned heading (Task 6), on top of `GroupColumnLayoutTests.TheColumnTurnsWithItsHeading…` (Task 3).

## Spec assumptions that are false in the code (and what this plan does)

| Spec says | Code says | Plan |
| --- | --- | --- |
| `DistantGroups` NetworkBehaviour "on the NpcWorldSim object" | The `NpcWorldSim` GameObject in `persistentScene` has no `NetworkObject` (NpcWorldSim, NpcWorldSaveable, FactionGoodwillLedger, WarPartyDirector, ExpeditionDirector, ExpeditionSaveable, SaveableEntity). Session-wide netcode rides `NetworkGameManager.prefab` (Multiplayer.md), as `PushableLedger`/`ObjectiveNetwork` do | `DistantGroups` + `DistantGroupSilhouette` go on `Assets/Game/Prefabs/Systems/NetworkGameManager.prefab`; the server reads `NpcWorldSim.Instance` |
| Near dust fades 80–200 on every vehicle | `MonowheelPresentation` fades 60–150 | `FarDust` takes the band from the vehicle's own near emitter through a new `IDustLodBand` interface |
| `SkyCityFleet` carries a `LODGroup` | The fleet root has none; its nested `SkyCity.prefab` carries it (`SkyCityBuilder`) | Bake `SkyCity.prefab` (the fleet inherits it); the baker refuses a nested `LODGroup` |
| Silhouette slots = `FormationMath.SlotOffset` over `CityShape` | `NpcWorldSim.Spawn` uses `FormationMath.SlotPosition(followerIndex, …, followerIndex * 7919, 0f)` — jitter of up to 2–3 m on top of `SlotOffset` | Extract `GroupColumnLayout.Places` and use it in both `Spawn` and the silhouette |
| Payload `{id hash, position, heading, moving, rosterSeed, spawned}` | A client needs the template to deal the column; `moving` is derivable from position change (Multiplayer.md: "Derivable from replicated state ⇒ do nothing") | Payload `{GroupHash, TemplateHash, RosterSeed, Position, Yaw, Spawned}`; far dust reads motion off the drawn transform |
| "Verify on an actual client" / "save/reload restores the city" (manual) | The two-process test player builder targets `StandaloneOSX` only and this machine has Windows build support only; agents may not enter Play Mode | Autotest report lines are added for a mac run; Task 7 ends with a human checklist (host + client, save/reload) |
| Today's Sky cull "at 2 %" | 2 % of a ~300 m city is tens of km away — effectively never culled on the 4 × 3 km map | Sky profile culls at 8000 m, beyond the map's diagonal, so the fleet stays visible everywhere |

Open risk (not falsified, unverified): whether a `MeshRenderer` that is a member of a `LODGroup` level still selects its Mesh LOD by screen size. Task 5 Step 9 checks it by render; record the answer in SettlementLods.md Gotchas either way.

---

## File Structure

**Runtime (Assembly-CSharp)**
- Create `Assets/Game/Scripts/Vehicles/Lod/MergedLod.cs` — on a baked prefab's root: its `LODGroup`, the merged mesh/materials, and "hold LOD0 while dead".
- Create `Assets/Game/Scripts/Vehicles/Dust/IDustLodBand.cs` — the near dust's fade band, read by far dust.
- Create `Assets/Game/Scripts/Vehicles/Dust/FarDust.cs` — the huge sparse speed-driven emitter and its crossfade maths.
- Modify `Assets/Game/Scripts/Vehicles/Dust/FootfallDust.cs`, `RollingDust.cs`, `Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentation.cs` — implement `IDustLodBand`.
- Create `Assets/Game/Scripts/Agents/World/GroupColumnLayout.cs` — where each planned member stands (shared by spawn and silhouette).
- Modify `Assets/Game/Scripts/Agents/World/NpcWorldSim.cs` — `Spawn` uses `GroupColumnLayout`; `FindTemplateByHash`.
- Modify `Assets/Game/Scripts/Agents/World/NpcGroup.cs` — `NpcGroupTemplate.showFromAfar`, `IdHash`, `HashOf`, `FindByIdHash`.
- Create `Assets/Game/Scripts/Agents/World/DistantGroupState.cs` — the wire struct.
- Create `Assets/Game/Scripts/Agents/World/DistantGroups.cs` — the NetworkBehaviour.
- Create `Assets/Game/Scripts/Agents/World/DistantGroupSilhouette.cs` — draws the folded group.
- Modify `Assets/Game/Scripts/Core/Multiplayer/Autotest/AutotestRunner.Client.cs`, `AutotestRunner.Host.cs` — report distant groups.

**Editor (Assembly-CSharp-Editor)**
- Create `Assets/Game/Editor/AssetPipeline/SettlementLodSettings.cs` + asset `Assets/Game/Settings/SettlementLodSettings.asset`.
- Create `Assets/Game/Editor/AssetPipeline/SettlementLodBaker.cs`.
- Modify `Assets/Game/Editor/Support/DustCloudRecipe.cs` — name the start-size constants.
- Modify `Assets/Game/Editor/Vehicles/VehicleDustWiring.cs` — `AddFarDust`.
- Modify builders: `Editor/Vehicles/StriderCityBuilder.cs`, `DesertCrawlerBuilder.cs`, `StriderBargeBuilder.cs`, `StriderMonowheelBuilder.cs`, `Editor/Creatures/StriderCrabOutriderBuilder.cs`, `Editor/Environment/SkyCityBuilder.cs`, `SkyFleetBuilder.cs`.
- Modify `Assets/Game/Editor/Agents/RosterAuthoring.cs` — `WireStriderCity` sets `showFromAfar`.

**Tests (`Assets/Game/Editor/Tests/`)**: create `SettlementLodBakerTests.cs`, `FarDustTests.cs`, `GroupColumnLayoutTests.cs`, `DistantGroupsTests.cs`, `SettlementLodPrefabTests.cs`, `DistantGroupSilhouetteTests.cs`; modify `StriderDustPrefabTests.cs`, `StriderCityTemplateTests.cs`.

**Docs**: create `docs/AI/systems/SettlementLods.md`; modify `VehicleDust.md`, `AgentSystem.md`, `Striders.md`, `SkyTribe.md`, `Multiplayer.md`, `Testing.md`, `docs/Human/the-systems.md`.

## Task order and parallelism

| Task | Depends on | Can run in parallel with |
| --- | --- | --- |
| 1 Baker + `MergedLod` | — | 2, 3 |
| 2 `FarDust` + `AddFarDust` | — | 1, 3 |
| 3 `GroupColumnLayout` + `showFromAfar` | — | 1, 2 |
| 4 `DistantGroups` replication | 3 | 1, 2, 5 |
| 5 Builders bake + far dust, rebuild prefabs | 1, 2 | 4 |
| 6 `DistantGroupSilhouette` | 3, 4, 5 | — |
| 7 Client reports, renders, verification | 6 | — |

Parallel subagents share one Unity editor: obey the "never leave non-compiling C# on disk" constraint, and run `docs_check.py --index` immediately before your own commit.

---

### Task 1: The settlement LOD baker and `MergedLod`

**Files:**
- Create: `Assets/Game/Scripts/Vehicles/Lod/MergedLod.cs`
- Create: `Assets/Game/Editor/AssetPipeline/SettlementLodSettings.cs`
- Create: `Assets/Game/Settings/SettlementLodSettings.asset` (generated by Step 6)
- Create: `Assets/Game/Editor/AssetPipeline/SettlementLodBaker.cs`
- Test: `Assets/Game/Editor/Tests/SettlementLodBakerTests.cs`
- Docs: create `docs/AI/systems/SettlementLods.md`; add an entry to `docs/Human/the-systems.md`

**Interfaces:**
- Consumes: builder path constants that already exist — `StriderCityBuilder.HabitatPath`, `DesertCrawlerBuilder.PrefabPath`, `StriderCrabOutriderBuilder.PrefabPath`, `StriderBargeBuilder.Barges` / `PrefabPath(string)`, `StriderMonowheelBuilder.AllPrefabPaths`, `SkyCityBuilder.PrefabPath`, `SkyFleetBuilder.Vessels[i].PrefabPath`; `NetworkObjectDefaults.KeepSceneMigrationSync(string)`.
- Produces:
  - `namespace SpaceGame.Vehicles { public sealed class MergedLod : MonoBehaviour }` with `public const string ChildName = "LOD1_Merged"`, `LODGroup Group`, `Mesh Mesh`, `Material[] Materials`, `MeshRenderer MergedRenderer`, `void Configure(LODGroup, MeshFilter, MeshRenderer)`, `static int ForcedLevel(bool alive)`.
  - `namespace SpaceGame.EditorTools { public sealed class SettlementLodSettings : ScriptableObject }` with `const string AssetPath`, `struct Profile { float mergedBeyondMetres; float cullBeyondMetres; int meshLodLimit; float referenceFovDegrees; }`, fields `Profile strider`, `Profile sky`, `static SettlementLodSettings Load()`.
  - `namespace SpaceGame.EditorTools { public static class SettlementLodBaker }` with `MergedLod Bake(GameObject root, string prefabPath, SettlementLodSettings.Profile profile)`, `string MergedMeshPath(string prefabPath)`, `float ScreenHeightAt(float worldSize, float distance, float fovDegrees, float lodBias)`, `IEnumerable<string> StriderPrefabPaths`, `IEnumerable<string> SkyPrefabPaths`, menu `BakeAll()`.

- [ ] **Step 1: Write the failing tests**

Create `Assets/Game/Editor/Tests/SettlementLodBakerTests.cs`:

```csharp
// The generated far level, baked over a scratch hierarchy: one merged mesh per material in the root's
// own space, the original renderers as LOD0, no particle in any level, mirrored parts the right way
// out, and a re-bake that overwrites its own asset instead of minting a new one.
using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Vehicles;
using Object = UnityEngine.Object;

namespace SpaceGame.EditorTools
{
    public class SettlementLodBakerTests
    {
        private const string Folder = "Assets/SettlementLodBakerTests_Scratch";
        private const string PrefabPath = Folder + "/Scratch.prefab";

        private static readonly SettlementLodSettings.Profile Profile = new SettlementLodSettings.Profile
        {
            mergedBeyondMetres = 100f,
            cullBeyondMetres = 1000f,
            meshLodLimit = 2,
            referenceFovDegrees = 60f,
        };

        private GameObject root;
        private Material red, blue;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "SettlementLodBakerTests_Scratch");
            red = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "red" };
            blue = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "blue" };
            root = new GameObject("Scratch");
            Part(PrimitiveType.Cube, red, new Vector3(0f, 1f, 0f), Vector3.one);
            Part(PrimitiveType.Sphere, red, new Vector3(3f, 1f, 0f), Vector3.one);
            Part(PrimitiveType.Cylinder, blue, new Vector3(-3f, 1f, 0f), Vector3.one);
            var smoke = new GameObject("FX_Smoke");
            smoke.transform.SetParent(root.transform, false);
            smoke.AddComponent<ParticleSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(red);
            Object.DestroyImmediate(blue);
            AssetDatabase.DeleteAsset(Folder);
        }

        private MeshRenderer Part(PrimitiveType type, Material material, Vector3 at, Vector3 scale)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = at;
            part.transform.localScale = scale;
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        private MeshRenderer[] Parts() =>
            root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name != MergedLod.ChildName).ToArray();

        [Test]
        public void TheMergedLevel_HasOneSubmeshPerMaterial_AndEveryVertexOfTheParts()
        {
            MergedLod lod = SettlementLodBaker.Bake(root, PrefabPath, Profile);

            Assert.AreEqual(2, lod.Mesh.subMeshCount, "one submesh (one draw) per distinct material");
            CollectionAssert.AreEqual(new[] { red, blue }, lod.Materials);
            Assert.AreEqual(Parts().Sum(r => r.GetComponent<MeshFilter>().sharedMesh.vertexCount), lod.Mesh.vertexCount);
        }

        [Test]
        public void LodZero_IsTheOriginalRenderers_AndNoParticleIsInAnyLevel()
        {
            MergedLod lod = SettlementLodBaker.Bake(root, PrefabPath, Profile);
            LOD[] lods = lod.Group.GetLODs();

            Assert.AreEqual(2, lods.Length);
            CollectionAssert.AreEquivalent(Parts(), lods[0].renderers, "LOD0 is the prefab's own renderers, untouched");
            CollectionAssert.AreEqual(new Renderer[] { lod.MergedRenderer }, lods[1].renderers);
            Assert.IsFalse(lods.SelectMany(l => l.renderers).Any(r => r is ParticleSystemRenderer),
                           "smoke and dust must run at every distance");
            Assert.AreSame(root.GetComponent<LODGroup>(), lod.Group);
        }

        [Test]
        public void TheTransitions_AreTheProfilesDistances_AsScreenHeights()
        {
            MergedLod lod = SettlementLodBaker.Bake(root, PrefabPath, Profile);
            LOD[] lods = lod.Group.GetLODs();
            float size = lod.Group.size;

            Assert.AreEqual(SettlementLodBaker.ScreenHeightAt(size, Profile.mergedBeyondMetres, Profile.referenceFovDegrees, QualitySettings.lodBias),
                            lods[0].screenRelativeTransitionHeight, 1e-5f);
            Assert.AreEqual(SettlementLodBaker.ScreenHeightAt(size, Profile.cullBeyondMetres, Profile.referenceFovDegrees, QualitySettings.lodBias),
                            lods[1].screenRelativeTransitionHeight, 1e-5f);
        }

        [Test]
        public void ScreenHeightAt_IsHalfTheSizeOverTheHalfFrustum_ScaledByTheLodBias()
        {
            float expected = 5f / (100f * Mathf.Tan(30f * Mathf.Deg2Rad));
            Assert.AreEqual(expected, SettlementLodBaker.ScreenHeightAt(10f, 100f, 60f, 1f), 1e-6f);
            Assert.AreEqual(2f * expected, SettlementLodBaker.ScreenHeightAt(10f, 100f, 60f, 2f), 1e-6f);
        }

        // Not rotated: the AABB of rotated parts' AABBs is looser than the AABB of the rotated merged mesh,
        // so the two would differ for a right answer. Translation and scale keep both exact.
        [Test]
        public void TheMergedLevel_SitsWhereThePartsDo_UnderAMovedScaledRoot()
        {
            root.transform.position = new Vector3(10f, 2f, -4f);
            root.transform.localScale = Vector3.one * 1.5f;
            MergedLod lod = SettlementLodBaker.Bake(root, PrefabPath, Profile);

            MeshRenderer[] parts = Parts();
            Bounds union = parts[0].bounds;
            foreach (MeshRenderer part in parts.Skip(1)) union.Encapsulate(part.bounds);
            Bounds merged = lod.MergedRenderer.bounds;
            Assert.Less(Vector3.Distance(union.center, merged.center), 1e-3f);
            Assert.Less(Vector3.Distance(union.size, merged.size), 1e-3f);
        }

        [Test]
        public void AMirroredPart_IsNotTurnedInsideOut()
        {
            foreach (MeshRenderer part in Parts()) Object.DestroyImmediate(part.gameObject);
            Part(PrimitiveType.Cube, red, Vector3.zero, Vector3.one);
            Part(PrimitiveType.Cube, red, new Vector3(4f, 0f, 0f), new Vector3(-1f, 1f, 1f));

            Mesh mesh = SettlementLodBaker.Bake(root, PrefabPath, Profile).Mesh;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.GetTriangles(0);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                Assert.Greater(Vector3.Dot(Vector3.Cross(b - a, c - a), normals[triangles[t]]), 0f,
                               $"triangle {t / 3} faces inward: its winding disagrees with its normal");
            }
        }

        [Test]
        public void Rebaking_OverwritesTheSameMeshAsset_AndLeavesOneMergedChild()
        {
            SettlementLodBaker.Bake(root, PrefabPath, Profile);
            string path = SettlementLodBaker.MergedMeshPath(PrefabPath);
            string guid = AssetDatabase.AssetPathToGUID(path);
            Part(PrimitiveType.Capsule, blue, new Vector3(0f, 1f, 3f), Vector3.one);

            MergedLod again = SettlementLodBaker.Bake(root, PrefabPath, Profile);

            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path), "a rebuild must keep the asset every reference resolves to");
            Assert.AreEqual(path, AssetDatabase.GetAssetPath(again.Mesh));
            Assert.AreEqual(1, root.transform.Cast<Transform>().Count(t => t.name == MergedLod.ChildName));
            Assert.AreEqual(Parts().Sum(r => r.GetComponent<MeshFilter>().sharedMesh.vertexCount), again.Mesh.vertexCount,
                            "the overwritten asset holds the new parts, not the old ones");
        }

        [Test]
        public void ANestedLodGroup_IsRefused()
        {
            root.transform.GetChild(0).gameObject.AddComponent<LODGroup>();
            Assert.Throws<InvalidOperationException>(() => SettlementLodBaker.Bake(root, PrefabPath, Profile));
        }

        [Test]
        public void AWreck_HoldsFullDetail_AndTheLivingLetTheGroupChoose()
        {
            Assert.AreEqual(0, MergedLod.ForcedLevel(alive: false), "a body lying where it fell is not its rest pose");
            Assert.AreEqual(-1, MergedLod.ForcedLevel(alive: true));
        }

        [Test]
        public void TheMergedMesh_IsSavedBesideItsPrefab() =>
            Assert.AreEqual("Assets/A/B/House_LOD1_Merged.asset", SettlementLodBaker.MergedMeshPath("Assets/A/B/House.prefab"));
    }
}
```

- [ ] **Step 2: Run the offline compile to see it fail**

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/typecheck.py --editor`
Expected: errors `CS0246`/`CS0103` for `SettlementLodSettings`, `SettlementLodBaker`, `MergedLod`. Do not refresh Unity yet.

- [ ] **Step 3: Write `MergedLod`**

Create `Assets/Game/Scripts/Vehicles/Lod/MergedLod.cs`:

```csharp
// The far level a settlement prefab carries (SettlementLodBaker, SettlementLods.md): its LODGroup, and
// the one merged mesh -- one submesh per material, in the prefab's rest pose -- that LOD1 draws. Read by
// DistantGroupSilhouette to draw the folded Strider city.
//
// The merged level is the rest pose. A body that died lies where it fell (a crab on its back, a barge
// on its side), so while the root's HealthComponent reads dead the group is held at LOD0 at every
// distance. Health replicates (RestoreHealth raises OnDeath/OnRevive on clients), so every machine
// holds the same level and nothing is sent or saved here.
using SpaceGame.Gameplay;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    [DisallowMultipleComponent]
    public sealed class MergedLod : MonoBehaviour
    {
        /// <summary>The child the baker puts the merged level on, at the root's identity pose.</summary>
        public const string ChildName = "LOD1_Merged";

        [SerializeField] private LODGroup group;
        [SerializeField] private MeshFilter mergedFilter;
        [SerializeField] private MeshRenderer mergedRenderer;

        private HealthComponent health;

        public LODGroup Group => group;
        public Mesh Mesh => mergedFilter.sharedMesh;
        public Material[] Materials => mergedRenderer.sharedMaterials;
        public MeshRenderer MergedRenderer => mergedRenderer;

        /// <summary>Baker only: the group and the merged level it switches to.</summary>
        public void Configure(LODGroup lodGroup, MeshFilter filter, MeshRenderer renderer)
        {
            group = lodGroup;
            mergedFilter = filter;
            mergedRenderer = renderer;
        }

        /// <summary>The level to force: LOD0 for a dead body, none (-1, the group chooses) for a living one.</summary>
        public static int ForcedLevel(bool alive) => alive ? -1 : 0;

        private void OnEnable()
        {
            health = GetComponent<HealthComponent>();
            if (health == null) return;

            health.OnDeath += Sync;
            health.OnRevive += Sync;
            health.OnRestored += Sync;
            Sync();
        }

        private void OnDisable()
        {
            if (health == null) return;

            health.OnDeath -= Sync;
            health.OnRevive -= Sync;
            health.OnRestored -= Sync;
        }

        private void Sync() => group.ForceLOD(ForcedLevel(health.Alive));
    }
}
```

- [ ] **Step 4: Write `SettlementLodSettings`**

Create `Assets/Game/Editor/AssetPipeline/SettlementLodSettings.cs`:

```csharp
// The tunables of the generated settlement LODs (SettlementLodBaker, SettlementLods.md), one profile per
// settlement, as distances: the baker turns them into screen heights for each prefab's own size, so a
// 2 m monowheel and a 21 m house switch at the same range. Created with these defaults the first time
// anything loads it; edit the asset, then re-run Tools/SpaceGame/Art/Bake Settlement LODs.
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    [CreateAssetMenu(fileName = "SettlementLodSettings", menuName = "SpaceGame/Art/Settlement LOD Settings")]
    public sealed class SettlementLodSettings : ScriptableObject
    {
        public const string AssetPath = "Assets/Game/Settings/SettlementLodSettings.asset";

        [System.Serializable]
        public struct Profile
        {
            [Tooltip("Camera distance (m) beyond which the merged level replaces the prefab's own renderers.")]
            [Min(1f)] public float mergedBeyondMetres;

            [Tooltip("Camera distance (m) beyond which the prefab is not drawn at all.")]
            [Min(1f)] public float cullBeyondMetres;

            [Tooltip("Most Mesh LOD levels generated inside the merged mesh (MeshLodUtility.GenerateMeshLods). " +
                     "Negative: keep simplifying until a level has about 64 indices.")]
            public int meshLodLimit;

            [Tooltip("Vertical field of view (degrees) the distances are converted with: the player camera's " +
                     "(GameSettings default 60).")]
            [Range(1f, 179f)] public float referenceFovDegrees;
        }

        [Tooltip("The Strider city's vehicles. Merged inside spawnRadius (250 m), so the live city arrives already " +
                 "drawing the level its silhouette drew; culled past the loaded ground (~1250 m).")]
        public Profile strider = new Profile
        {
            mergedBeyondMetres = 160f,
            cullBeyondMetres = 1500f,
            meshLodLimit = 4,
            referenceFovDegrees = 60f,
        };

        [Tooltip("The Sky fleet: the flagship city and its escorts. Culled beyond the 4 x 3 km map's diagonal, so " +
                 "the fleet stays in the sky from anywhere, as the old 2 % cull group did.")]
        public Profile sky = new Profile
        {
            mergedBeyondMetres = 400f,
            cullBeyondMetres = 8000f,
            meshLodLimit = 4,
            referenceFovDegrees = 60f,
        };

        /// <summary>The settings asset, created with the defaults above if it does not exist yet.</summary>
        public static SettlementLodSettings Load()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SettlementLodSettings>(AssetPath);
            if (settings != null) return settings;

            settings = CreateInstance<SettlementLodSettings>();
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }
    }
}
```

- [ ] **Step 5: Write `SettlementLodBaker`**

Create `Assets/Game/Editor/AssetPipeline/SettlementLodBaker.cs`:

```csharp
// Generated LODs for the settlements that move: the Strider city's vehicles and the Sky fleet
// (SettlementLods.md). Their cost is draw calls -- a walking house is 1024 renderers -- so the far level
// is every mesh renderer's mesh in the prefab's rest pose (skinned ones baked) combined into ONE mesh
// with one submesh per material, which then carries Unity's own Mesh LODs so it keeps simplifying as it
// recedes. LOD0 is the prefab's own renderers, untouched and animated. Particle systems, trails and lines
// are never in a level, so smoke and dust run at every distance. Colliders, scripts and network
// components are not touched: a LODGroup only switches renderers, on every machine, with nothing sent.
//
// Each builder bakes its scratch root just before saving it; Tools/SpaceGame/Art/Bake Settlement LODs
// re-bakes every prefab in place. The merged mesh lives beside the prefab and is overwritten in place,
// so a rebuild never keeps a stale level and never mints a new GUID.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceGame.Vehicles;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SpaceGame.EditorTools
{
    public static class SettlementLodBaker
    {
        public const string MergedSuffix = "_LOD1_Merged";

        /// <summary>A screen height of 1 fills the screen, and a transition above it is never reached.</summary>
        private const float LargestTransition = 0.999f;

        /// <summary>Every Strider city vehicle prefab (the player's monowheel too: it is built by the same pass).</summary>
        public static IEnumerable<string> StriderPrefabPaths =>
            new[] { StriderCityBuilder.HabitatPath, DesertCrawlerBuilder.PrefabPath, StriderCrabOutriderBuilder.PrefabPath }
                .Concat(StriderBargeBuilder.Barges.Select(b => StriderBargeBuilder.PrefabPath(b.Variant)))
                .Concat(StriderMonowheelBuilder.AllPrefabPaths);

        /// <summary>The Sky fleet: the city (nested in SkyCityFleet, which inherits its levels) and the escort hulls.</summary>
        public static IEnumerable<string> SkyPrefabPaths =>
            SkyFleetBuilder.Vessels.Select(v => v.PrefabPath).Prepend(SkyCityBuilder.PrefabPath);

        public static string MergedMeshPath(string prefabPath) =>
            $"{Path.GetDirectoryName(prefabPath).Replace('\\', '/')}/{Path.GetFileNameWithoutExtension(prefabPath)}{MergedSuffix}.asset";

        /// <summary>
        /// The LODGroup screen height at which something <paramref name="worldSize"/> metres across is
        /// <paramref name="distance"/> metres from a camera of vertical <paramref name="fovDegrees"/>.
        /// Unity multiplies an object's screen height by QualitySettings.lodBias before comparing, so the
        /// threshold carries it too and the switch lands at the distance asked for.
        /// </summary>
        public static float ScreenHeightAt(float worldSize, float distance, float fovDegrees, float lodBias) =>
            worldSize * 0.5f / (distance * Mathf.Tan(fovDegrees * 0.5f * Mathf.Deg2Rad)) * lodBias;

        [MenuItem("Tools/SpaceGame/Art/Bake Settlement LODs")]
        public static void BakeAll()
        {
            SettlementLodSettings settings = SettlementLodSettings.Load();
            var report = new System.Text.StringBuilder("[SettlementLodBaker]\n");
            foreach (string path in StriderPrefabPaths) report.AppendLine(BakeInPlace(path, settings.strider));
            foreach (string path in SkyPrefabPaths) report.AppendLine(BakeInPlace(path, settings.sky));
            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }

        private static string BakeInPlace(string prefabPath, SettlementLodSettings.Profile profile)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            string line;
            try
            {
                MergedLod lod = Bake(root, prefabPath, profile);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException($"The AssetDatabase refused {prefabPath}.");
                line = $"  {prefabPath}: {lod.Group.GetLODs()[0].renderers.Length} renderers -> " +
                       $"{lod.Mesh.subMeshCount} merged submeshes, {lod.Mesh.lodCount} mesh LODs";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            // A builder's last write on a networked prefab (Multiplayer.md, NetworkObjectDefaults).
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponent<NetworkObject>() != null)
                NetworkObjectDefaults.KeepSceneMigrationSync(prefabPath);
            return line;
        }

        /// <summary>
        /// Gives <paramref name="root"/> its generated levels: LOD0 its own mesh renderers, LOD1 one merged
        /// mesh (saved at <see cref="MergedMeshPath"/> beside <paramref name="prefabPath"/>), culled past
        /// the profile's distance. Replaces a previous bake and reuses the root's own LODGroup. Throws on
        /// a LODGroup below the root: a renderer in two groups draws twice.
        /// </summary>
        public static MergedLod Bake(GameObject root, string prefabPath, SettlementLodSettings.Profile profile)
        {
            if (profile.mergedBeyondMetres >= profile.cullBeyondMetres)
                throw new ArgumentException($"The merged level ({profile.mergedBeyondMetres} m) must start nearer than the cull " +
                                            $"({profile.cullBeyondMetres} m).");
            LODGroup nested = root.GetComponentsInChildren<LODGroup>(true).FirstOrDefault(g => g.gameObject != root);
            if (nested != null)
                throw new InvalidOperationException($"{root.name}: '{nested.name}' has its own LODGroup, and a renderer in two " +
                                                    "groups draws twice. Bake that prefab instead, or remove its group.");

            Transform previous = root.transform.Find(MergedLod.ChildName);
            if (previous != null) Object.DestroyImmediate(previous.gameObject);

            Renderer[] originals = root.GetComponentsInChildren<Renderer>(true)
                .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer)
                .ToArray();
            Mesh built = Merge(root.transform, originals, out Material[] materials);
            MeshLodUtility.GenerateMeshLods(built, profile.meshLodLimit);
            Mesh mesh = SaveMesh(built, MergedMeshPath(prefabPath));

            var child = new GameObject(MergedLod.ChildName) { layer = root.layer };
            child.transform.SetParent(root.transform, false);
            var filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;

            LODGroup group = root.GetComponent<LODGroup>();
            if (group == null) group = root.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            // Bounds first (RecalculateBounds reads the levels), then the heights that need them.
            group.SetLODs(Levels(originals, renderer, LargestTransition, LargestTransition * 0.5f));
            group.RecalculateBounds();
            Vector3 scale = root.transform.lossyScale;
            float size = group.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            float bias = QualitySettings.lodBias;
            group.SetLODs(Levels(originals, renderer,
                                 Mathf.Min(LargestTransition, ScreenHeightAt(size, profile.mergedBeyondMetres, profile.referenceFovDegrees, bias)),
                                 ScreenHeightAt(size, profile.cullBeyondMetres, profile.referenceFovDegrees, bias)));

            MergedLod lod = root.GetComponent<MergedLod>();
            if (lod == null) lod = root.AddComponent<MergedLod>();
            lod.Configure(group, filter, renderer);
            return lod;
        }

        private static LOD[] Levels(Renderer[] originals, Renderer merged, float mergedFrom, float culledFrom) =>
            new[] { new LOD(mergedFrom, originals), new LOD(culledFrom, new[] { merged }) };

        /// <summary>
        /// Every enabled, active mesh renderer under <paramref name="root"/> in the root's own space, one
        /// submesh per distinct material in first-seen order. A submesh with no material slot is not drawn
        /// by Unity and is left out here too.
        /// </summary>
        private static Mesh Merge(Transform root, IEnumerable<Renderer> renderers, out Material[] materials)
        {
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            var order = new List<Material>();
            var temporaries = new List<Mesh>();
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            try
            {
                foreach (Renderer renderer in renderers)
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    if (!TrySource(renderer, toRoot, temporaries, out Mesh source, out Matrix4x4 matrix)) continue;

                    Material[] slots = renderer.sharedMaterials;
                    for (int s = 0; s < source.subMeshCount; s++)
                    {
                        Material material = s < slots.Length ? slots[s] : null;
                        if (material == null) continue;

                        if (!byMaterial.TryGetValue(material, out List<CombineInstance> parts))
                        {
                            parts = new List<CombineInstance>();
                            byMaterial[material] = parts;
                            order.Add(material);
                        }
                        parts.Add(new CombineInstance { mesh = source, subMeshIndex = s, transform = matrix });
                    }
                }

                if (order.Count == 0)
                    throw new InvalidOperationException($"{root.name} has no enabled mesh renderer with a material to merge.");

                var perMaterial = new CombineInstance[order.Count];
                for (int i = 0; i < order.Count; i++)
                {
                    var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                    part.CombineMeshes(byMaterial[order[i]].ToArray(), mergeSubMeshes: true, useMatrices: true);
                    temporaries.Add(part);
                    perMaterial[i] = new CombineInstance { mesh = part, transform = Matrix4x4.identity };
                }

                var merged = new Mesh { name = root.name + MergedSuffix, indexFormat = IndexFormat.UInt32 };
                merged.CombineMeshes(perMaterial, mergeSubMeshes: false, useMatrices: false);
                merged.RecalculateBounds();
                materials = order.ToArray();
                return merged;
            }
            finally
            {
                foreach (Mesh temporary in temporaries) Object.DestroyImmediate(temporary);
            }
        }

        /// <summary>
        /// The mesh a renderer draws and the matrix into the root's space: a skinned one baked in its
        /// current (rest) pose, with its scale. A mirrored transform flips the triangles' winding, which
        /// CombineMeshes does not undo, so a mirrored mesh is merged from a copy wound the other way.
        /// </summary>
        private static bool TrySource(Renderer renderer, Matrix4x4 toRoot, List<Mesh> temporaries, out Mesh source, out Matrix4x4 matrix)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                source = null;
                matrix = default;
                if (skinned.sharedMesh == null) return false;

                var pose = new Mesh();
                skinned.BakeMesh(pose, true);   // with the renderer's scale; position and rotation come from the matrix
                temporaries.Add(pose);
                source = pose;
                matrix = toRoot * Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
            }
            else
            {
                var filter = renderer.GetComponent<MeshFilter>();
                source = filter != null ? filter.sharedMesh : null;
                matrix = toRoot * renderer.transform.localToWorldMatrix;
                if (source == null) return false;
            }

            if (matrix.determinant < 0f) source = Rewound(source, temporaries);
            return true;
        }

        private static Mesh Rewound(Mesh mesh, List<Mesh> temporaries)
        {
            Mesh copy = Object.Instantiate(mesh);
            for (int s = 0; s < copy.subMeshCount; s++)
            {
                int[] triangles = copy.GetTriangles(s);
                Array.Reverse(triangles);   // reversing the whole list reverses every triangle's winding
                copy.SetTriangles(triangles, s);
            }
            temporaries.Add(copy);
            return copy;
        }

        /// <summary>Saves <paramref name="mesh"/> at <paramref name="path"/>, over the asset already there so its GUID survives.</summary>
        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
```

- [ ] **Step 6: Compile, create the settings asset, run the tests**

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/typecheck.py --editor` — expected exit 0.

Write `$SCRATCH/lod_settings.cs`:
```csharp
var s = SpaceGame.EditorTools.SettlementLodSettings.Load();
return UnityEditor.AssetDatabase.GetAssetPath(s) + " strider " + s.strider.mergedBeyondMetres + "/" + s.strider.cullBeyondMetres
     + " sky " + s.sky.mergedBeyondMetres + "/" + s.sky.cullBeyondMetres;
```
Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py ux.py < lod_settings.cs`
Expected: `Assets/Game/Settings/SettlementLodSettings.asset strider 160/1500 sky 400/8000`

Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py rt.py 'SettlementLodBakerTests'`
Expected: 10 passed. If `AMirroredPart_IsNotTurnedInsideOut` fails on the *mirrored* cube's triangles (indices 12 onward), this Unity version's CombineMeshes already rewinds mirrored meshes and the baker's copy double-flips them: delete `Rewound` and its call, rerun, and record that in SettlementLods.md Gotchas instead of the "Mirrored parts" flow line.

- [ ] **Step 7: Write the system doc**

Create `docs/AI/systems/SettlementLods.md`:

```markdown
---
system: SettlementLods
layer: vehicles
summary: "Generated far levels for the Strider city and Sky fleet: one merged mesh per material, Mesh LODs, culled"
paths:
  - Assets/Game/Scripts/Vehicles/Lod/
  - Assets/Game/Editor/AssetPipeline/SettlementLodBaker.cs
  - Assets/Game/Editor/AssetPipeline/SettlementLodSettings.cs
  - Assets/Game/Settings/SettlementLodSettings.asset
  - Assets/Game/Editor/Tests/SettlementLodBakerTests.cs
symptoms:
  - "the walking houses or the sky fleet cost hundreds of draw calls even far away"
  - "a merged far level shows parts of a machine inside out"
  - "a dead crab outrider or monowheel stands upright again when seen from a distance"
reads_with: [VehicleDust, Striders, SkyTribe, ArtPipeline]
updated: 2026-10-05
---

# Settlement LODs

The Strider city's vehicles and the Sky fleet cost **draw calls**, not triangles (a walking house: 1024 renderers, 1024 material slots). Each prefab gets a generated `LODGroup`: LOD0 its own renderers, LOD1 one rest-pose mesh with **one submesh per material** that carries Unity 6.3 **Mesh LODs**, culled past a distance. Nothing is modelled by hand.

## Model

- **LOD0** = every `MeshRenderer`/`SkinnedMeshRenderer` under the root, untouched and animated. **LOD1** = child `LOD1_Merged`: those renderers' meshes (enabled, active, with a material) in the root's space, skinned ones `BakeMesh`ed in the prefab's pose, one submesh per distinct material; `MeshLodUtility.GenerateMeshLods(mesh, meshLodLimit)` adds simplified levels inside the same mesh, picked by screen size by the renderer itself.
- **Never in a level:** particle systems, trails, lines — smoke and dust run at every distance. Colliders, scripts and network components are untouched.
- **Distances, not heights.** `SettlementLodSettings.asset` holds a profile per settlement (`strider` 160 m merged / 1500 m culled; `sky` 400 / 8000) in metres; the baker converts each to a screen height from the prefab's own `LODGroup.size` × lossy scale, the reference FOV (60, the player default) and `QualitySettings.lodBias` (2): `ScreenHeightAt = size·0.5 / (d·tan(fov/2)) · lodBias`.
- **A wreck keeps full detail.** `MergedLod` forces LOD0 while the root's `HealthComponent` is dead (`OnDeath`/`OnRevive`/`OnRestored`), so a body lying where it fell is never drawn as its standing rest pose.
- The merged mesh is saved beside the prefab as `<Prefab>_LOD1_Merged.asset` and **overwritten in place** (`CopySerialized`), so its GUID survives every rebuild.

## Key types

| Type | File | Role |
|---|---|---|
| `SettlementLodBaker` | [SettlementLodBaker.cs](Assets/Game/Editor/AssetPipeline/SettlementLodBaker.cs) | `Bake(root, prefabPath, profile)` (builders, before saving), `BakeAll` = `Tools/SpaceGame/Art/Bake Settlement LODs` (in place), `MergedMeshPath`, `ScreenHeightAt`, `StriderPrefabPaths`, `SkyPrefabPaths` |
| `SettlementLodSettings` | [SettlementLodSettings.cs](Assets/Game/Editor/AssetPipeline/SettlementLodSettings.cs) | `Profile { mergedBeyondMetres, cullBeyondMetres, meshLodLimit, referenceFovDegrees }`, `strider`, `sky`; `Load()` creates the asset with defaults |
| `MergedLod` | [MergedLod.cs](Assets/Game/Scripts/Vehicles/Lod/MergedLod.cs) | On the baked root: `Group`, `Mesh`, `Materials`, `MergedRenderer`; holds LOD0 while dead (`ForcedLevel`) |

## Flows

- **Bake:** refuse a `LODGroup` below the root → delete the old `LOD1_Merged` → merge → generate Mesh LODs → save over the old asset → new child → `LODGroup` (the root's own, reused) with LOD0/LOD1 → heights from the profile → `MergedLod.Configure`.
- **Mirrored parts:** a renderer whose matrix into the root has a negative determinant is merged from a copy with every triangle's winding reversed; `CombineMeshes` transforms normals but not winding.

## Multiplayer

N/A for the wire: a `LODGroup` switches renderers on each machine from its own camera. `MergedLod`'s dead/alive hold reads health, which already replicates.

## Persistence

N/A: generated assets and serialized prefab data only; no runtime state.

## Gotchas

- **A screen height above 1 is never reached.** A large prefab (the Sky city) can need a "height" over 1 at its merge distance; the baker clamps it to 0.999, which merges it a little *farther* out than asked, never nearer.
- **A renderer in two `LODGroup`s draws twice**, so the baker throws on a group below the root. Bake the nested prefab instead (the Sky fleet's city).

## Extending

- **Another prefab:** call `SettlementLodBaker.Bake(root, prefabPath, SettlementLodSettings.Load().<profile>)` as the builder's last step before `SaveAsPrefabAsset` (after anything that adds renderers), add its path to `StriderPrefabPaths`/`SkyPrefabPaths`, and it joins `SettlementLodPrefabTests`.
- **Retune:** edit `SettlementLodSettings.asset`, then run `Tools/SpaceGame/Art/Bake Settlement LODs`.
```

Add to `docs/Human/the-systems.md`, directly after the `### Dust off the walking city *(VehicleDust)*` entry (after its **Worth knowing** line):

```markdown
### Settlements seen from afar *(SettlementLods)*

The walking city's machines and the sky fleet each carry a second, cheap version of themselves that the game draws once they are far away: every piece of the machine fused into one mesh per kind of paint, which then simplifies itself further as it recedes. Nobody models these by hand; a tool makes them every time a machine is rebuilt. Far away the moving parts freeze in place.

**Worth knowing:** Smoke and dust are never part of the cheap version, so they keep running at every distance.
```

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/docs_check.py --index` — expected exit 0.

- [ ] **Step 8: Commit**

```bash
cd /c/Users/tobia/Documents/spaceGame/SpaceGame
git add Assets/Game/Scripts/Vehicles/Lod.meta Assets/Game/Scripts/Vehicles/Lod/MergedLod.cs Assets/Game/Scripts/Vehicles/Lod/MergedLod.cs.meta \
  Assets/Game/Editor/AssetPipeline/SettlementLodSettings.cs Assets/Game/Editor/AssetPipeline/SettlementLodSettings.cs.meta \
  Assets/Game/Editor/AssetPipeline/SettlementLodBaker.cs Assets/Game/Editor/AssetPipeline/SettlementLodBaker.cs.meta \
  Assets/Game/Settings/SettlementLodSettings.asset Assets/Game/Settings/SettlementLodSettings.asset.meta \
  Assets/Game/Editor/Tests/SettlementLodBakerTests.cs Assets/Game/Editor/Tests/SettlementLodBakerTests.cs.meta \
  docs/AI/systems/SettlementLods.md docs/Human/the-systems.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "feat(lods): bake a merged, self-simplifying far level for settlement prefabs

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Far dust

**Files:**
- Create: `Assets/Game/Scripts/Vehicles/Dust/IDustLodBand.cs`, `Assets/Game/Scripts/Vehicles/Dust/FarDust.cs`
- Modify: `Assets/Game/Scripts/Vehicles/Dust/FootfallDust.cs`, `Assets/Game/Scripts/Vehicles/Dust/RollingDust.cs`, `Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentation.cs` (implement `IDustLodBand`)
- Modify: `Assets/Game/Editor/Support/DustCloudRecipe.cs` (name the start sizes), `Assets/Game/Editor/Vehicles/VehicleDustWiring.cs` (`AddFarDust`)
- Test: `Assets/Game/Editor/Tests/FarDustTests.cs`
- Docs: `docs/AI/systems/VehicleDust.md`

**Interfaces:**
- Consumes: `MonowheelPresentationMath.LodFactor/SpeedFraction/Rate`, `GroundSpeedGauge`, `DustCloudRecipe.Cloud/CapFor/MinLife/MaxLife`, `VehicleDustWiring.SandMaterial()/SandTint`.
- Produces:
  - `namespace SpaceGame.Vehicles { public interface IDustLodBand { float LodNear { get; } float LodFar { get; } } }` implemented by `FootfallDust`, `RollingDust`, `MonowheelPresentation`.
  - `namespace SpaceGame.Vehicles { public sealed class FarDust : MonoBehaviour }` with `ParticleSystem Cloud`, `float FullSpeed`, `float RateAtFullSpeed`, `float FadeNear`, `float FadeFar`, `float CullDistance`, `void Configure(ParticleSystem puffs, float cruiseSpeed, float peakRate, IDustLodBand nearBand, float cull)`, `static float Fade(float cameraDistance, float near, float far, float cull)`, `float Present(float dt, float cameraDistance)`, `void ResetBaseline()`. It lives on its own child GameObject (with its `ParticleSystem`) so a copy of that GameObject works on its own (Task 6 instantiates it).
  - `VehicleDustWiring.AddFarDust(GameObject root, float nearPeakRate, float cruiseSpeed) : FarDust`; constants `FarCloudName = "FX_FarDust"`, `FarDustSizeMultiplier = 4f`, `FarDustLifeMultiplier = 1.5f`, `FarDustRateFraction = 0.2f`, `FarDustCullDistance = 1500f`.
  - `DustCloudRecipe.MinSize = 2f`, `DustCloudRecipe.MaxSize = 3.2f`.

- [ ] **Step 1: Write the failing tests**

Create `Assets/Game/Editor/Tests/FarDustTests.cs`:

```csharp
// The far dust: a huge, sparse cloud that fades in exactly as a vehicle's near dust fades out, billows
// while the vehicle moves and settles when it parks. Driven by hand; nothing here is networked or saved.
using System;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Monowheel;
using Object = UnityEngine.Object;

namespace SpaceGame.EditorTools
{
    public class FarDustTests
    {
        private const float Near = 80f, Far = 200f, Cull = 1500f;
        private const float Dt = 1f / 60f;

        private sealed class Band : IDustLodBand
        {
            public float LodNear => Near;
            public float LodFar => Far;
        }

        private GameObject subject;

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(subject);

        [Test]
        public void TheNearAndFarDust_AlwaysSumToOne_InsideTheCull()
        {
            for (float d = 0f; d < Cull; d += 5f)
            {
                float near = MonowheelPresentationMath.LodFactor(d, Near, Far);
                float far = FarDust.Fade(d, Near, Far, Cull);
                Assert.AreEqual(1f, near + far, 1e-5f, $"at {d} m");
                Assert.IsFalse(near > 0.99f && far > 0.99f, $"both full at {d} m");
            }
        }

        [Test]
        public void TheFarDust_IsOffNear_HalfwayInTheBand_FullBeyond_AndGonePastTheCull()
        {
            Assert.AreEqual(0f, FarDust.Fade(40f, Near, Far, Cull));
            Assert.AreEqual(0.5f, FarDust.Fade(140f, Near, Far, Cull), 1e-5f);
            Assert.AreEqual(1f, FarDust.Fade(900f, Near, Far, Cull));
            Assert.AreEqual(0f, FarDust.Fade(Cull, Near, Far, Cull));
            Assert.AreEqual(0f, FarDust.Fade(float.NaN, Near, Far, Cull), "no camera: the near dust is full, so the far is off");
        }

        private FarDust Rig()
        {
            subject = new GameObject("Vehicle");
            ParticleSystem cloud = DustCloudRecipe.Cloud(subject.transform, "FX_TestFarDust", Vector3.zero, Quaternion.LookRotation(Vector3.up),
                                                         new Material(Shader.Find(DustCloudRecipe.Shader)), Color.white,
                                                         cap: 500, shapeOffset: Vector3.zero);
            var dust = cloud.gameObject.AddComponent<FarDust>();
            dust.Configure(cloud, cruiseSpeed: 2.7f, peakRate: 4f, nearBand: new Band(), cull: Cull);
            dust.ResetBaseline();
            return dust;
        }

        private static float Drive(FarDust dust, float speed, float distance, int frames)
        {
            float rate = 0f;
            for (int i = 0; i < frames; i++)
            {
                dust.transform.position += Vector3.forward * (speed * Dt);
                rate = dust.Present(Dt, distance);
            }
            return rate;
        }

        [Test]
        public void AMarchingVehicle_BillowsAtRange_AndAParkedOneSettles()
        {
            FarDust dust = Rig();
            Assert.Greater(Drive(dust, 2.7f, 300f, 120), 3.6f, "marching at the city's pace, 300 m away");
            Assert.Less(Drive(dust, 0f, 300f, 120), 0.1f, "parked");
            Assert.AreEqual(0f, Drive(dust, 2.7f, 50f, 60), "close up the near dust has it");
        }

        [Test]
        public void AddFarDust_BuildsAHugeSparseCloud_InTheNearDustsOwnBand()
        {
            // An unscaled root, as every builder's is, with a 10 x 4 x 6 m hull under it.
            subject = new GameObject("Machine");
            GameObject hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hull.transform.SetParent(subject.transform, false);
            hull.transform.localScale = new Vector3(10f, 4f, 6f);
            var contact = new GameObject("Contact");
            contact.transform.SetParent(subject.transform, false);
            RollingDust rolling = VehicleDustWiring.AddRollingDust(subject, new[] { contact.transform }, 4f, 3f);

            FarDust far = VehicleDustWiring.AddFarDust(subject, nearPeakRate: 6f, cruiseSpeed: 2.7f);

            Assert.AreEqual(VehicleDustWiring.FarCloudName, far.name);
            Assert.AreSame(subject.transform, far.transform.parent);
            Assert.AreSame(far.GetComponent<ParticleSystem>(), far.Cloud);
            Assert.AreEqual(rolling.LodNear, far.FadeNear);
            Assert.AreEqual(rolling.LodFar, far.FadeFar);
            Assert.AreEqual(6f * VehicleDustWiring.FarDustRateFraction, far.RateAtFullSpeed, 1e-5f);
            Assert.AreEqual(2.7f, far.FullSpeed, 1e-5f);
            Assert.AreEqual(VehicleDustWiring.FarDustCullDistance, far.CullDistance);

            ParticleSystem.MainModule main = far.Cloud.main;
            Assert.AreEqual(DustCloudRecipe.MinSize * VehicleDustWiring.FarDustSizeMultiplier, main.startSize.constantMin, 1e-4f);
            Assert.AreEqual(DustCloudRecipe.MaxSize * VehicleDustWiring.FarDustSizeMultiplier, main.startSize.constantMax, 1e-4f);
            Assert.AreEqual(DustCloudRecipe.MinLife * VehicleDustWiring.FarDustLifeMultiplier, main.startLifetime.constantMin, 1e-4f);
            Assert.AreEqual(DustCloudRecipe.MaxLife * VehicleDustWiring.FarDustLifeMultiplier, main.startLifetime.constantMax, 1e-4f);
            Assert.AreEqual(Mathf.CeilToInt(far.RateAtFullSpeed * DustCloudRecipe.MaxLife * VehicleDustWiring.FarDustLifeMultiplier),
                            main.maxParticles);
            Assert.AreEqual(ParticleSystemSimulationSpace.World, main.simulationSpace);
            Assert.AreEqual(5f, far.Cloud.shape.radius, 1e-3f, "born across the hull's widest half-extent");
            Assert.AreEqual(VehicleDustWiring.SandMaterialPath,
                            UnityEditor.AssetDatabase.GetAssetPath(far.Cloud.GetComponent<ParticleSystemRenderer>().sharedMaterial));
        }

        [Test]
        public void AddFarDust_RefusesAMachineWithNoNearDust()
        {
            subject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Assert.Throws<InvalidOperationException>(() => VehicleDustWiring.AddFarDust(subject, 6f, 2.7f));
        }
    }
}
```

- [ ] **Step 2: Run the offline compile to see it fail**

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/typecheck.py --editor`
Expected: errors for `IDustLodBand`, `FarDust`, `AddFarDust`, `FarCloudName`, `MinSize`, `LodNear`.

- [ ] **Step 3: Add `IDustLodBand` and implement it**

Create `Assets/Game/Scripts/Vehicles/Dust/IDustLodBand.cs`:

```csharp
// The camera-distance band over which a vehicle's near dust fades out. FarDust fades in over the same
// band, so the two crossfade and are never both at full (VehicleDust.md). Each near emitter keeps its
// own band: the legged and tracked machines' 80-200 m, the monowheels' 60-150 m.
namespace SpaceGame.Vehicles
{
    public interface IDustLodBand
    {
        /// <summary>Full near dust up to this camera distance (m).</summary>
        float LodNear { get; }

        /// <summary>No near dust beyond this camera distance (m).</summary>
        float LodFar { get; }
    }
}
```

In `FootfallDust.cs` change `public sealed class FootfallDust : MonoBehaviour` to `public sealed class FootfallDust : MonoBehaviour, IDustLodBand` and add after `public float SizePerFootRadius => sizePerFootRadius;`:

```csharp
        public float LodNear => lodNear;
        public float LodFar => lodFar;
```

In `RollingDust.cs` change `public sealed class RollingDust : MonoBehaviour` to `public sealed class RollingDust : MonoBehaviour, IDustLodBand` and add after `public float RateAtFullSpeed => rateAtFullSpeed;`:

```csharp
        public float LodNear => lodNear;
        public float LodFar => lodFar;
```

In `MonowheelPresentation.cs` add `IDustLodBand` to the class's base list (`SpaceGame.Vehicles.Monowheel` is inside `SpaceGame.Vehicles`, so no `using` is needed) and add after `public float DustAtFullSpeed => dustAtFullSpeed;`:

```csharp
        public float LodNear => lodNear;
        public float LodFar => lodFar;
```

- [ ] **Step 4: Write `FarDust`**

Create `Assets/Game/Scripts/Vehicles/Dust/FarDust.cs`:

```csharp
// The Strider city's dust seen from afar: a second, huge and sparse DustCloudRecipe cloud per vehicle,
// puffs ~4x the near ones and a fifth the rate, rising round the hull while the vehicle moves. It fades
// in over exactly the band the vehicle's near dust fades out (IDustLodBand) and stays on to the
// vehicle's cull distance, so a merged far level (SettlementLods.md) -- whose legs and wheels are frozen
// -- marches inside a cloud.
//
// The rate follows how fast this transform is seen to move (GroundSpeedGauge), so the host, a client
// watching a replicated hull and the distant silhouette (DistantGroupSilhouette, which instantiates a
// copy of this GameObject) all present alike, with nothing sent or saved (GDC-L1-FEEL-0004). Overdraw is
// paid per covered pixel, and these clouds are, by construction, far (GDC-L1-TECH-0002).
using SpaceGame.Vehicles.Monowheel;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    public sealed class FarDust : MonoBehaviour
    {
        [Tooltip("The huge, sparse cloud, on this GameObject. World space, emission driven here.")]
        [SerializeField] private ParticleSystem cloud;

        [Header("Speed")]
        [Tooltip("Ground speed (m/s) at which the far dust reaches its full rate: the city's march.")]
        [SerializeField] private float fullSpeed = 2.7f;
        [Tooltip("Seconds over which the speed is smoothed, so a replicated or stepped pose does not flicker it.")]
        [SerializeField] private float speedSmoothing = 0.3f;
        [Tooltip("A frame's move implying more than this speed (m/s) is a snap (a load, a refold), not motion.")]
        [SerializeField] private float maxPlausibleSpeed = 50f;

        [Header("Emission (puffs/s)")]
        [SerializeField] private float rateAtFullSpeed = 4f;

        [Header("Distance crossfade")]
        [Tooltip("The near dust is full up to this camera distance (m), so the far dust is off.")]
        [SerializeField] private float fadeNear = 80f;
        [Tooltip("The near dust is gone beyond this camera distance (m), so the far dust is full.")]
        [SerializeField] private float fadeFar = 200f;
        [Tooltip("No far dust beyond this camera distance (m): where the vehicle itself is culled.")]
        [SerializeField] private float cullDistance = 1500f;

        private GroundSpeedGauge gauge;

        public ParticleSystem Cloud => cloud;
        public float FullSpeed => fullSpeed;
        public float RateAtFullSpeed => rateAtFullSpeed;
        public float FadeNear => fadeNear;
        public float FadeFar => fadeFar;
        public float CullDistance => cullDistance;

        /// <summary>Builder only: the cloud, the speed and rate it peaks at, the near dust it crossfades with, and its cull.</summary>
        public void Configure(ParticleSystem puffs, float cruiseSpeed, float peakRate, IDustLodBand nearBand, float cull)
        {
            cloud = puffs;
            fullSpeed = cruiseSpeed;
            rateAtFullSpeed = peakRate;
            fadeNear = nearBand.LodNear;
            fadeFar = nearBand.LodFar;
            cullDistance = cull;
        }

        /// <summary>
        /// How much far dust at <paramref name="cameraDistance"/>: 1 minus the near dust's LodFactor inside
        /// the cull, 0 beyond it and with no camera (NaN, where the near dust is full).
        /// </summary>
        public static float Fade(float cameraDistance, float near, float far, float cull)
        {
            if (float.IsNaN(cameraDistance) || cameraDistance >= cull) return 0f;
            return 1f - MonowheelPresentationMath.LodFactor(cameraDistance, near, far);
        }

        /// <summary>Forget where this was: after a spawn, a load or any snap into place.</summary>
        public void ResetBaseline() => gauge.Reset(transform.position);

        private void OnEnable() => ResetBaseline();

        private void OnValidate()
        {
            fullSpeed = Mathf.Max(0.01f, fullSpeed);
            speedSmoothing = Mathf.Max(0f, speedSmoothing);
            rateAtFullSpeed = Mathf.Max(0f, rateAtFullSpeed);
            fadeFar = Mathf.Max(fadeNear + 1f, fadeFar);
            cullDistance = Mathf.Max(fadeFar, cullDistance);
        }

        private void Update()
        {
            Camera cam = Camera.main;
            Present(Time.deltaTime, cam == null ? float.NaN : Vector3.Distance(cam.transform.position, transform.position));
        }

        /// <summary>One frame at a given camera distance (NaN = no camera: none). Returns the rate set (puffs/s).</summary>
        public float Present(float dt, float cameraDistance)
        {
            if (dt <= 0f) return cloud.emission.rateOverTime.constant;

            float speed = gauge.MeasureAlongStep(transform.position, dt, speedSmoothing, maxPlausibleSpeed);
            float rate = MonowheelPresentationMath.Rate(0f, rateAtFullSpeed, MonowheelPresentationMath.SpeedFraction(speed, fullSpeed))
                         * Fade(cameraDistance, fadeNear, fadeFar, cullDistance);
            ParticleSystem.EmissionModule emission = cloud.emission;
            emission.rateOverTime = rate;
            // A stopped system ignores its rate: one never played (a preview scene, an edit-mode test).
            if (rate > 0f && !cloud.isPlaying) cloud.Play();
            return rate;
        }
    }
}
```

- [ ] **Step 5: Name the recipe's start sizes and add `AddFarDust`**

In `DustCloudRecipe.cs`, replace `public const float MinLife = 8f, MaxLife = 12f;` with:

```csharp
        public const float MinLife = 8f, MaxLife = 12f;
        /// <summary>A puff's diameter at birth (m), before the recipe billows it about 4x.</summary>
        public const float MinSize = 2f, MaxSize = 3.2f;
```

and in `Cloud(...)` replace `main.startSize = new ParticleSystem.MinMaxCurve(2f, 3.2f);` with `main.startSize = new ParticleSystem.MinMaxCurve(MinSize, MaxSize);`.

In `VehicleDustWiring.cs`, add to the header comment's first paragraph the sentence `Every Strider city vehicle also gets far dust (FarDust): huge, sparse, fading in as the near dust fades out.`, add the constants after `public const string TrackCloudName = "FX_TrackDust";`:

```csharp
        public const string FarCloudName = "FX_FarDust";

        /// <summary>Far puffs are this many times the near ones across: the recipe's 2-3.2 m become 8-12.8 m.</summary>
        public const float FarDustSizeMultiplier = 4f;
        /// <summary>And live this many times as long (12-18 s), so a slow march leaves a standing wall.</summary>
        public const float FarDustLifeMultiplier = 1.5f;
        /// <summary>At this fraction of the machine's near dust's peak rate.</summary>
        public const float FarDustRateFraction = 0.2f;
        /// <summary>No far dust past the Strider vehicles' cull (SettlementLodSettings.strider.cullBeyondMetres; SettlementLodPrefabTests pins the two).</summary>
        public const float FarDustCullDistance = 1500f;
```

and add after `AddRollingDust`:

```csharp
        /// <summary>
        /// Far dust for the machine at <paramref name="root"/>: one huge, sparse cloud on its own child
        /// (<see cref="FarCloudName"/>), born across the hull's footprint at its lowest point and thrown up,
        /// fading in over the band its near dust fades out. <paramref name="nearPeakRate"/> is that near
        /// dust's peak (puffs/s); <paramref name="cruiseSpeed"/> the speed at which the far dust is full.
        /// Call after the near dust and after anything that measures renderer bounds.
        /// </summary>
        public static FarDust AddFarDust(GameObject root, float nearPeakRate, float cruiseSpeed)
        {
            var band = root.GetComponentInChildren<IDustLodBand>(true);
            if (band == null)
                throw new System.InvalidOperationException($"{root.name} has no near dust; far dust crossfades with it, so add that first.");

            Bounds hull = HullBounds(root);
            float rate = nearPeakRate * FarDustRateFraction;
            int cap = Mathf.CeilToInt(rate * DustCloudRecipe.MaxLife * FarDustLifeMultiplier);
            ParticleSystem cloud = DustCloudRecipe.Cloud(root.transform, FarCloudName, new Vector3(hull.center.x, hull.min.y, hull.center.z),
                                                         Quaternion.LookRotation(Vector3.up), SandMaterial(), SandTint, cap, Vector3.zero);
            ParticleSystem.MainModule main = cloud.main;
            main.startSize = new ParticleSystem.MinMaxCurve(DustCloudRecipe.MinSize * FarDustSizeMultiplier,
                                                            DustCloudRecipe.MaxSize * FarDustSizeMultiplier);
            main.startLifetime = new ParticleSystem.MinMaxCurve(DustCloudRecipe.MinLife * FarDustLifeMultiplier,
                                                                DustCloudRecipe.MaxLife * FarDustLifeMultiplier);
            ParticleSystem.ShapeModule shape = cloud.shape;
            shape.radius = Mathf.Max(hull.extents.x, hull.extents.z);

            var dust = cloud.gameObject.AddComponent<FarDust>();
            dust.Configure(cloud, cruiseSpeed, rate, band, FarDustCullDistance);
            return dust;
        }

        /// <summary>The bounds, in the root's own space, of every mesh renderer under it: particles are not hull.</summary>
        private static Bounds HullBounds(GameObject root)
        {
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
            Bounds hull = default;
            bool any = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;

                Bounds world = renderer.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 offset = Vector3.Scale(world.extents, new Vector3((corner & 1) == 0 ? -1f : 1f,
                                                                              (corner & 2) == 0 ? -1f : 1f,
                                                                              (corner & 4) == 0 ? -1f : 1f));
                    Vector3 local = toRoot.MultiplyPoint3x4(world.center + offset);
                    if (any) hull.Encapsulate(local);
                    else hull = new Bounds(local, Vector3.zero);
                    any = true;
                }
            }
            if (!any) throw new System.InvalidOperationException($"{root.name} has no mesh renderer to raise far dust round.");
            return hull;
        }
```

- [ ] **Step 6: Compile and run the tests**

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/typecheck.py --editor` — expected exit 0.
Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py rt.py 'FarDustTests|StriderDustEmitterTests|MonowheelPresentation'`
Expected: all pass (the existing emitter and monowheel tests prove the recipe and the three emitters are unchanged).

- [ ] **Step 7: Document**

In `docs/AI/systems/VehicleDust.md`:
- frontmatter `summary:` → `"Sand dust off every Strider machine: footfall and rolling clouds near, one huge sparse far cloud at range"`; add to `paths:` `  - Assets/Game/Editor/Tests/FarDustTests.cs`; add to `symptoms:` `  - "far away the walking city's legs are frozen and nothing hides it"`; add `SettlementLods` to `reads_with`.
- In `## Model`, after the **LOD** bullet, add: `- **Far dust** (\`FarDust\`, one cloud per vehicle, child \`FX_FarDust\`): the recipe's cloud with puffs 4× (8–12.8 m) and life 1.5× (12–18 s), born across the hull's footprint and thrown up, at \`FarDustRateFraction\` 0.2 of the near dust's peak × the vehicle's own measured speed over \`fullSpeed\` (the city's 2.7 m/s march). Fades **in** over the near dust's own band (\`IDustLodBand\`: 80–200 m legged/tracked, 60–150 m monowheels) — the two always sum to 1 — and is off past \`FarDustCullDistance\` 1500 m (the Strider LOD cull, [SettlementLods.md](SettlementLods.md)) and with no camera.`
- In `## Key types`, add a row: `| \`FarDust\` / \`IDustLodBand\` | [FarDust.cs](Assets/Game/Scripts/Vehicles/Dust/FarDust.cs) | \`Present(dt, cameraDistance)\` sets the rate from \`GroundSpeedGauge\` on its own transform × \`Fade(d, near, far, cull)\`; on its own GameObject so a copy works alone (the distant silhouette instantiates it) |` and extend the `VehicleDustWiring` row with `, \`AddFarDust(root, nearPeakRate, cruiseSpeed)\` (throws without near dust)`.
- Under the budget table add one line: `Far dust adds ⌈0.2 × peak × 18 s⌉ per vehicle (house 65, crawler 90, crab 72, barges 87/44, monowheels 72): ~1 200 very large puffs for the whole city, only ever drawn far away.`
- In `## Gotchas` add: `- **The crossfade band is the vehicle's, not a constant.** The monowheels' near dust fades over 60–150 m, the others over 80–200 m; \`AddFarDust\` reads the band from the machine's own near emitter, so call it after that emitter exists.`

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/docs_check.py --index` — expected exit 0.

- [ ] **Step 8: Commit**

```bash
cd /c/Users/tobia/Documents/spaceGame/SpaceGame
git add Assets/Game/Scripts/Vehicles/Dust/IDustLodBand.cs Assets/Game/Scripts/Vehicles/Dust/IDustLodBand.cs.meta \
  Assets/Game/Scripts/Vehicles/Dust/FarDust.cs Assets/Game/Scripts/Vehicles/Dust/FarDust.cs.meta \
  Assets/Game/Scripts/Vehicles/Dust/FootfallDust.cs Assets/Game/Scripts/Vehicles/Dust/RollingDust.cs \
  Assets/Game/Scripts/Vehicles/Monowheel/MonowheelPresentation.cs \
  Assets/Game/Editor/Support/DustCloudRecipe.cs Assets/Game/Editor/Vehicles/VehicleDustWiring.cs \
  Assets/Game/Editor/Tests/FarDustTests.cs Assets/Game/Editor/Tests/FarDustTests.cs.meta \
  docs/AI/systems/VehicleDust.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "feat(dust): far dust that fades in where a vehicle's near dust fades out

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: One column layout for the live spawn and the silhouette; `showFromAfar`

**Files:**
- Create: `Assets/Game/Scripts/Agents/World/GroupColumnLayout.cs`
- Modify: `Assets/Game/Scripts/Agents/World/NpcWorldSim.cs` (`Spawn`; add `FindTemplateByHash`)
- Modify: `Assets/Game/Scripts/Agents/World/NpcGroup.cs` (`NpcGroupTemplate`)
- Modify: `Assets/Game/Editor/Agents/RosterAuthoring.cs` (`WireStriderCity`)
- Modify (generated): `Assets/Game/Scenes/world/persistentScene.unity` (Step 7)
- Test: `Assets/Game/Editor/Tests/GroupColumnLayoutTests.cs`; modify `Assets/Game/Editor/Tests/StriderCityTemplateTests.cs`
- Docs: `docs/AI/systems/AgentSystem.md`, `docs/AI/systems/Striders.md`

**Interfaces:**
- Consumes: `PlannedMember(GameObject prefab, bool leads, bool crew = false)`, `FormationMath.SlotPosition(int, Vector3, Vector3, in FormationShape, int, float)`, `RosterDraw.StableHash(string)` (null-safe).
- Produces:
  - `namespace SpaceGame.Agents { public readonly struct ColumnPlace { int PlanIndex; bool Leads; Vector3 Position; } }`
  - `GroupColumnLayout.Places(IReadOnlyList<PlannedMember> plan, Vector3 origin, Vector3 heading, in FormationShape shape) : List<ColumnPlace>` — one entry per planned member with a prefab (crew included), in plan order.
  - `NpcGroupTemplate.showFromAfar` (bool, default false), `int IdHash`, `static int HashOf(string templateId)`, `static NpcGroupTemplate FindByIdHash(IEnumerable<NpcGroupTemplate>, int)`.
  - `NpcWorldSim.FindTemplateByHash(int idHash) : NpcGroupTemplate`.

- [ ] **Step 1: Write the failing tests**

Create `Assets/Game/Editor/Tests/GroupColumnLayoutTests.cs`:

```csharp
// Where each planned member of a group stands: the leader at the origin, every other member that
// spawns in the next follower slot, the whole column turning rigidly with its heading. The live spawn
// and the distant silhouette both stand members here, so the hand-over between them cannot jump.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class GroupColumnLayoutTests
    {
        private static readonly FormationShape Shape = new FormationShape
        {
            Lanes = 2, RowSpacing = 35f, LaneSpacing = 30f,
            LateralJitter = 2f, LongitudinalJitter = 3f, DriftAmplitude = 1f, DriftRate = 0.05f,
        };

        private GameObject prefab;

        [SetUp]
        public void SetUp() => prefab = new GameObject("Member");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(prefab);

        [Test]
        public void TheLeaderStandsAtTheOrigin_AndEveryOtherSpawningMemberInTheNextFollowerSlot()
        {
            var plan = new List<PlannedMember>
            {
                new PlannedMember(prefab, true),
                new PlannedMember(prefab, false),
                new PlannedMember(null, false),
                new PlannedMember(prefab, false, crew: true),
                new PlannedMember(prefab, false),
            };
            var origin = new Vector3(100f, 5f, -40f);
            Vector3 heading = new Vector3(1f, 0f, 1f).normalized;

            List<ColumnPlace> places = GroupColumnLayout.Places(plan, origin, heading, Shape);

            CollectionAssert.AreEqual(new[] { 0, 1, 3, 4 }, places.Select(p => p.PlanIndex), "nothing drawn takes no slot");
            Assert.IsTrue(places[0].Leads);
            Assert.AreEqual(origin, places[0].Position);
            for (int k = 0; k < 3; k++)
            {
                Assert.IsFalse(places[k + 1].Leads);
                Assert.AreEqual(FormationMath.SlotPosition(k, origin, heading, Shape, k * 7919, 0f), places[k + 1].Position,
                                $"follower {k}: the slot NpcWorldSim.Spawn always gave it");
            }
        }

        [Test]
        public void ASecondLeader_Follows()
        {
            var plan = new List<PlannedMember> { new PlannedMember(prefab, true), new PlannedMember(prefab, true) };
            List<ColumnPlace> places = GroupColumnLayout.Places(plan, Vector3.zero, Vector3.forward, Shape);
            Assert.IsTrue(places[0].Leads);
            Assert.IsFalse(places[1].Leads);
            Assert.AreEqual(FormationMath.SlotPosition(0, Vector3.zero, Vector3.forward, Shape, 0, 0f), places[1].Position);
        }

        [Test]
        public void TheColumnTurnsWithItsHeading_AsOneRigidShape([Values(0f, 37f, 90f, 200f, 315f)] float yaw)
        {
            var plan = new List<PlannedMember> { new PlannedMember(prefab, true) };
            for (int i = 0; i < 17; i++) plan.Add(new PlannedMember(prefab, false));
            var origin = new Vector3(-812f, 33f, 1530f);
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);

            List<ColumnPlace> local = GroupColumnLayout.Places(plan, Vector3.zero, Vector3.forward, Shape);
            List<ColumnPlace> world = GroupColumnLayout.Places(plan, origin, turn * Vector3.forward, Shape);

            for (int i = 0; i < plan.Count; i++)
                Assert.Less(Vector3.Distance(origin + turn * local[i].Position, world[i].Position), 1e-3f, $"member {i} at {yaw} deg");
        }

        [Test]
        public void ATemplate_IsFoundByTheHashOfItsId()
        {
            var city = new NpcGroupTemplate { id = "strider-city" };
            var nomads = new NpcGroupTemplate { id = "sand-nomads" };

            Assert.AreEqual(RosterDraw.StableHash("strider-city"), city.IdHash);
            Assert.AreEqual(NpcGroupTemplate.HashOf("sand-nomads"), nomads.IdHash);
            Assert.AreSame(nomads, NpcGroupTemplate.FindByIdHash(new[] { city, null, nomads }, nomads.IdHash));
            Assert.IsNull(NpcGroupTemplate.FindByIdHash(new[] { city, nomads }, NpcGroupTemplate.HashOf("no-such-group")));
            Assert.IsFalse(new NpcGroupTemplate().showFromAfar, "opt-in: no group is drawn from afar unless its template says so");
        }
    }
}
```

In `StriderCityTemplateTests.cs`, add after `TheElders_RideLast_AsStandingCrew_OneOrTwoByWeight`:

```csharp
        [Test]
        public void OnlyTheCity_IsSeenFromAfar()
        {
            NpcGroupTemplate[] templates = ReadTemplates();
            Assert.IsTrue(templates.Single(t => t.id == RosterAuthoring.StriderCityTemplateId).showFromAfar,
                          "the walking city is drawn beyond spawnRadius (re-run Wire Strider City)");
            CollectionAssert.IsEmpty(templates.Where(t => t.id != RosterAuthoring.StriderCityTemplateId && t.showFromAfar).Select(t => t.id));
        }
```

- [ ] **Step 2: Run the offline compile to see it fail**

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/typecheck.py --editor`
Expected: errors for `ColumnPlace`, `GroupColumnLayout`, `IdHash`, `HashOf`, `FindByIdHash`, `showFromAfar`.

- [ ] **Step 3: Write `GroupColumnLayout`**

Create `Assets/Game/Scripts/Agents/World/GroupColumnLayout.cs`:

```csharp
// Where each planned member of a group stands in its column: the leader at the origin, every other
// member that spawns in the next follower slot, with the slot's own fixed jitter (FormationMath.SlotPosition,
// seeded by the slot, drift at time 0). One rule for the live spawn (NpcWorldSim.Spawn) and the distant
// city's silhouette (DistantGroupSilhouette), so the silhouette hands over to the live city in the same
// places. The column is rigid: turning the heading turns every place about the origin. Pure: tested
// without a scene.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>One planned member's place: its index in the plan, whether it leads, and where it stands.</summary>
    public readonly struct ColumnPlace
    {
        public readonly int PlanIndex;
        public readonly bool Leads;
        public readonly Vector3 Position;

        public ColumnPlace(int planIndex, bool leads, Vector3 position)
        {
            PlanIndex = planIndex;
            Leads = leads;
            Position = position;
        }
    }

    public static class GroupColumnLayout
    {
        /// <summary>Seeds each follower slot's fixed jitter and drift phase (FormationMath.SlotPosition's memberSeed).</summary>
        private const int SlotSeedStride = 7919;

        /// <summary>
        /// A place for every planned member with a prefab, in plan order: the first that leads at
        /// <paramref name="origin"/>, the rest in follower slots behind it along <paramref name="heading"/>.
        /// Crew take a slot too (NpcWorldSim seats them on a carrier instead), so every later member keeps
        /// the slot ColumnDeal dealt it (NpcGroupComposition.FollowerSlots counts the same way).
        /// </summary>
        public static List<ColumnPlace> Places(IReadOnlyList<PlannedMember> plan, Vector3 origin, Vector3 heading, in FormationShape shape)
        {
            var places = new List<ColumnPlace>(plan.Count);
            int follower = 0;
            bool leaderTaken = false;
            for (int i = 0; i < plan.Count; i++)
            {
                if (plan[i].Prefab == null) continue;

                bool leads = plan[i].Leads && !leaderTaken;
                leaderTaken |= leads;
                Vector3 at = leads
                    ? origin
                    : FormationMath.SlotPosition(follower, origin, heading, shape, follower * SlotSeedStride, 0f);
                if (!leads) follower++;
                places.Add(new ColumnPlace(i, leads, at));
            }
            return places;
        }
    }
}
```

- [ ] **Step 4: Use it in `NpcWorldSim.Spawn`**

In `NpcWorldSim.Spawn`, replace this block:

```csharp
            Vector3 heading = group.Heading;
            Quaternion facing = FacingAlong(heading);
            int followerIndex = 0;
            bool leaderTaken = false;

            for (int index = 0; index < plan.Count; index++)
            {
                PlannedMember planned = plan[index];
                if (planned.Prefab == null) continue;

                bool leads = planned.Leads && !leaderTaken;

                Vector3 slot = leads
                    ? origin
                    : FormationMath.SlotPosition(followerIndex, origin, heading,
                                                 template.formation, followerIndex * 7919, 0f);

                if (!leads) followerIndex++;
```

with:

```csharp
            Vector3 heading = group.Heading;
            Quaternion facing = FacingAlong(heading);
            bool leaderTaken = false;

            // The same places the distant silhouette draws the folded group in (GroupColumnLayout).
            foreach (ColumnPlace place in GroupColumnLayout.Places(plan, origin, heading, template.formation))
            {
                int index = place.PlanIndex;
                PlannedMember planned = plan[index];
                bool leads = place.Leads;
                Vector3 slot = place.Position;
```

Everything after it in the loop body (crew carrier, `pose`, `seated`, `memberIndex = index`, `SpawnMember`, `leaderTaken |= leads`, `Configure`) stays as it is. After the loop, `if (!leaderTaken && group.Live[0]…)` still makes the first spawned lead when the planned leader failed to spawn.

Add to `NpcWorldSim`, after `FindTemplate`:

```csharp
        /// <summary>
        /// The template whose id hashes to <paramref name="idHash"/> (<see cref="NpcGroupTemplate.IdHash"/>);
        /// null when none. Every machine holds the templates -- they are scene data -- so a client resolves a
        /// replicated group's template here (DistantGroupSilhouette).
        /// </summary>
        public NpcGroupTemplate FindTemplateByHash(int idHash) => NpcGroupTemplate.FindByIdHash(templatesById.Values, idHash);
```

- [ ] **Step 5: Add to `NpcGroupTemplate`**

In `NpcGroup.cs`, inside `NpcGroupTemplate`, add after `public bool bountyHunters;`:

```csharp
        [Tooltip("Drawn from beyond spawnRadius while folded: every machine draws its vehicles' merged far levels " +
                 "in its column, in dust (DistantGroups, DistantGroupSilhouette). For a group big enough to see from " +
                 "the edge of the loaded ground -- the Strider city.")]
        public bool showFromAfar;
```

and at the end of the class, after `formation`:

```csharp
        /// <summary>The id as every machine hashes it: how a replicated group names its template (DistantGroupState).</summary>
        public int IdHash => HashOf(id);

        public static int HashOf(string templateId) => RosterDraw.StableHash(templateId);

        /// <summary>The first of <paramref name="templates"/> whose <see cref="IdHash"/> is <paramref name="idHash"/>; null when none.</summary>
        public static NpcGroupTemplate FindByIdHash(IEnumerable<NpcGroupTemplate> templates, int idHash)
        {
            foreach (NpcGroupTemplate template in templates)
                if (template != null && template.IdHash == idHash) return template;
            return null;
        }
```

- [ ] **Step 6: Opt the city in**

In `RosterAuthoring.WireStriderCity`, after `t.FindPropertyRelative("bountyHunters").boolValue = false;` add:

```csharp
                // Seen marching from the edge of the loaded ground (DistantGroupSilhouette, SettlementLods.md).
                t.FindPropertyRelative("showFromAfar").boolValue = true;
```

- [ ] **Step 7: Compile, re-wire the city, run the tests**

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/typecheck.py --editor` — expected exit 0.

Check `git status --short Assets/Game/Scenes/world/persistentScene.unity` first; if someone else has uncommitted changes there, stop and report instead of re-wiring.

Write `$SCRATCH/wire_city.cs`:
```csharp
bool ran = UnityEditor.EditorApplication.ExecuteMenuItem("Tools/SpaceGame/Agents/Wire Strider City");
UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
return "wired " + ran;
```
Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py ux.py < wire_city.cs` — expected `wired True`.
Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && git diff --stat Assets/Game/Scenes/world/persistentScene.unity && git diff Assets/Game/Scenes/world/persistentScene.unity | grep '^[-+] ' | head -20`
Expected: only `+      showFromAfar: 1` (and `showFromAfar: 0` lines for the other templates, which Unity now serializes). If `startPosition` or any other field changed, `git checkout -- Assets/Game/Scenes/world/persistentScene.unity` and report the drift instead of committing it.

Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py rt.py 'GroupColumnLayoutTests|StriderCityTemplateTests|NpcWorldSimTests|ColumnDealTests|FormationMathTests'`
Expected: all pass.

- [ ] **Step 8: Document**

- `docs/AI/systems/AgentSystem.md`: in the `## Key types` table, add a row `| \`GroupColumnLayout\` | [GroupColumnLayout.cs](Assets/Game/Scripts/Agents/World/GroupColumnLayout.cs) | \`Places(plan, origin, heading, shape)\`: the leader at the origin, every other spawning member (crew included) in the next \`FormationMath.SlotPosition\` slot. \`NpcWorldSim.Spawn\` and \`DistantGroupSilhouette\` both stand members here |`. In `## Gotchas` add: `- **A failed leader spawn no longer promotes a later \`isLeader\` member.** Places are decided before anything spawns (\`GroupColumnLayout\`), so the leader slot stays empty and the first member spawned leads (the fallback after the loop). Every template has one leader, so nothing changes in practice.` Bump `updated:`.
- `docs/AI/systems/Striders.md`: in the `## Model` bullet **The city.**, append `It is \`showFromAfar\`: folded, it is drawn from afar on every machine ([SettlementLods.md](SettlementLods.md)).` In the `**Spawn.**` paragraph of `## Flows`, replace `walks the plan in order` with `walks the plan in order (places from \`GroupColumnLayout.Places\`, shared with the distant silhouette)`. Bump `updated:`.

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/docs_check.py --index` — expected exit 0. (If `SettlementLods.md` does not exist yet because Task 1 has not landed, link the Striders mention to `VehicleDust.md` instead and Task 6 repoints it.)

- [ ] **Step 9: Commit**

```bash
cd /c/Users/tobia/Documents/spaceGame/SpaceGame
git add Assets/Game/Scripts/Agents/World/GroupColumnLayout.cs Assets/Game/Scripts/Agents/World/GroupColumnLayout.cs.meta \
  Assets/Game/Scripts/Agents/World/NpcWorldSim.cs Assets/Game/Scripts/Agents/World/NpcGroup.cs \
  Assets/Game/Editor/Agents/RosterAuthoring.cs Assets/Game/Scenes/world/persistentScene.unity \
  Assets/Game/Editor/Tests/GroupColumnLayoutTests.cs Assets/Game/Editor/Tests/GroupColumnLayoutTests.cs.meta \
  Assets/Game/Editor/Tests/StriderCityTemplateTests.cs \
  docs/AI/systems/AgentSystem.md docs/AI/systems/Striders.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "feat(world-sim): one column layout for spawn and silhouette; the city opts in to showFromAfar

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: `DistantGroups` — where the opted-in groups are, on every machine

**Files:**
- Create: `Assets/Game/Scripts/Agents/World/DistantGroupState.cs`, `Assets/Game/Scripts/Agents/World/DistantGroups.cs`
- Modify (by Step 5's snippet): `Assets/Game/Prefabs/Systems/NetworkGameManager.prefab`
- Test: `Assets/Game/Editor/Tests/DistantGroupsTests.cs`
- Docs: `docs/AI/systems/AgentSystem.md`, `docs/AI/systems/Multiplayer.md`

**Interfaces:**
- Consumes (Task 3): `NpcGroupTemplate.showFromAfar`, `NpcGroupTemplate.HashOf(string)`; existing `NpcWorldSim.Instance`, `NpcWorldSim.Groups`, `NpcWorldSim.FindTemplate(string)`, `NpcGroup.Heading`, `Network.Simulates(Component)`.
- Produces:
  - `public struct DistantGroupState : INetworkSerializable, IEquatable<DistantGroupState>` with fields `int GroupHash, int TemplateHash, int RosterSeed, Vector3 Position, float Yaw, bool Spawned`, `static DistantGroupState Of(NpcGroup)`, `static float YawOf(Vector3 heading)`, `Vector3 Heading`.
  - `public sealed class DistantGroups : NetworkBehaviour` with `int Count` (0 until spawned), indexer `DistantGroupState this[int]`, `static void Collect(IReadOnlyList<NpcGroup>, Func<string, NpcGroupTemplate>, List<DistantGroupState>)`. On `NetworkGameManager.prefab`.

- [ ] **Step 1: Write the failing tests**

Create `Assets/Game/Editor/Tests/DistantGroupsTests.cs`:

```csharp
// Where every group seen from afar stands, on the wire: the state survives serialization bit for bit,
// is read straight off the group's record, and only opted-in groups still standing are published. The
// list rides the session's one NetworkObject.
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class DistantGroupsTests
    {
        private const string SessionPrefabPath = "Assets/Game/Prefabs/Systems/NetworkGameManager.prefab";

        [Test]
        public void AState_SurvivesTheWire()
        {
            var state = new DistantGroupState
            {
                GroupHash = -7, TemplateHash = 42, RosterSeed = 123456789,
                Position = new Vector3(1234.5f, 67.25f, -890.125f), Yaw = -135.5f, Spawned = true,
            };
            using var writer = new FastBufferWriter(64, Allocator.Temp);
            writer.WriteNetworkSerializable(state);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out DistantGroupState back);

            Assert.AreEqual(state, back);
        }

        [Test]
        public void AState_IsReadOffTheGroupsRecord()
        {
            var group = new NpcGroup
            {
                Id = "strider-city", TemplateId = "strider-city", RosterSeed = 99,
                Position = new Vector3(10f, 2f, 20f), GoalPosition = new Vector3(110f, 2f, 20f), HasGoal = true,
            };

            DistantGroupState state = DistantGroupState.Of(group);

            Assert.AreEqual(NpcGroupTemplate.HashOf("strider-city"), state.GroupHash);
            Assert.AreEqual(NpcGroupTemplate.HashOf("strider-city"), state.TemplateHash);
            Assert.AreEqual(99, state.RosterSeed);
            Assert.AreEqual(group.Position, state.Position);
            Assert.AreEqual(90f, state.Yaw, 1e-4f, "heading east");
            Assert.Less(Vector3.Distance(group.Heading, state.Heading), 1e-4f);
            Assert.IsFalse(state.Spawned);
        }

        [Test]
        public void OnlyOptedInGroups_StillStanding_ArePublished()
        {
            var city = new NpcGroupTemplate { id = "strider-city", showFromAfar = true };
            var nomads = new NpcGroupTemplate { id = "sand-nomads" };
            var templates = new Dictionary<string, NpcGroupTemplate> { [city.id] = city, [nomads.id] = nomads };
            var groups = new List<NpcGroup>
            {
                new NpcGroup { Id = "strider-city", TemplateId = city.id, RosterSeed = 1 },
                new NpcGroup { Id = "sand-nomads", TemplateId = nomads.id, RosterSeed = 2 },
                new NpcGroup { Id = "strider-city-2", TemplateId = city.id, RosterSeed = 3, WipedOut = true },
                new NpcGroup { Id = "orphan", TemplateId = "no-such-template", RosterSeed = 4 },
            };
            var published = new List<DistantGroupState> { default };

            DistantGroups.Collect(groups, id => templates.TryGetValue(id, out NpcGroupTemplate t) ? t : null, published);

            Assert.AreEqual(1, published.Count, "Collect replaces what the list held");
            Assert.AreEqual(NpcGroupTemplate.HashOf("strider-city"), published[0].GroupHash);
            Assert.AreEqual(1, published[0].RosterSeed);
        }

        [Test]
        public void TheSessionObject_CarriesDistantGroups()
        {
            var session = AssetDatabase.LoadAssetAtPath<GameObject>(SessionPrefabPath);
            Assert.IsNotNull(session.GetComponent<NetworkObject>());
            Assert.IsNotNull(session.GetComponent<DistantGroups>(),
                             "a NetworkBehaviour must be on the session prefab, never added at runtime");
        }
    }
}
```

- [ ] **Step 2: Run the offline compile to see it fail**

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/typecheck.py --editor`
Expected: errors for `DistantGroupState` and `DistantGroups`.

- [ ] **Step 3: Write `DistantGroupState`**

Create `Assets/Game/Scripts/Agents/World/DistantGroupState.cs`:

```csharp
// One group seen from afar, as it crosses the wire (DistantGroups): which group, which template (a client
// deals the column itself from the template and the seed -- ColumnDeal is deterministic), where it is and
// which way it faces, and whether it is live (then the real members are drawn instead). Unmanaged and
// fixed size, as a NetworkList needs. Whether it is moving is not sent: the drawn transform's own motion
// says so (Multiplayer.md: derivable from replicated state, send nothing).
using System;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Agents
{
    public struct DistantGroupState : INetworkSerializable, IEquatable<DistantGroupState>
    {
        /// <summary><see cref="NpcGroupTemplate.HashOf"/> of the group's id: the silhouette's key.</summary>
        public int GroupHash;
        /// <summary><see cref="NpcGroupTemplate.IdHash"/> of its template (NpcWorldSim.FindTemplateByHash).</summary>
        public int TemplateHash;
        /// <summary>The group's saved roster seed: the same seed deals the same column on every machine.</summary>
        public int RosterSeed;
        /// <summary>The group's position: its leader's slot.</summary>
        public Vector3 Position;
        /// <summary>Degrees about +Y of the heading the live spawn would face (NpcGroup.Heading).</summary>
        public float Yaw;
        /// <summary>Live: its members are real and the silhouette steps aside.</summary>
        public bool Spawned;

        public static DistantGroupState Of(NpcGroup group) => new DistantGroupState
        {
            GroupHash = NpcGroupTemplate.HashOf(group.Id),
            TemplateHash = NpcGroupTemplate.HashOf(group.TemplateId),
            RosterSeed = group.RosterSeed,
            Position = group.Position,
            Yaw = YawOf(group.Heading),
            Spawned = group.Spawned,
        };

        public static float YawOf(Vector3 heading) => Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;

        public Vector3 Heading => Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref GroupHash);
            serializer.SerializeValue(ref TemplateHash);
            serializer.SerializeValue(ref RosterSeed);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Yaw);
            serializer.SerializeValue(ref Spawned);
        }

        /// <summary>Required by NetworkList, which sends only a write that differs.</summary>
        public bool Equals(DistantGroupState other) =>
            GroupHash == other.GroupHash
            && TemplateHash == other.TemplateHash
            && RosterSeed == other.RosterSeed
            && Position.Equals(other.Position)
            && Yaw.Equals(other.Yaw)
            && Spawned == other.Spawned;

        public override bool Equals(object obj) => obj is DistantGroupState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(GroupHash, TemplateHash, RosterSeed, Position, Yaw, Spawned);
    }
}
```

- [ ] **Step 4: Write `DistantGroups`**

Create `Assets/Game/Scripts/Agents/World/DistantGroups.cs`:

```csharp
// Where every group seen from afar stands, on every machine (SettlementLods.md, "The distant city").
//
// NpcWorldSim decides on the server alone, and its GameObject carries no NetworkObject, so the groups a
// template marks showFromAfar are published here, on the session's one NetworkObject (NetworkGameManager
// in persistentScene): a NetworkList the server rewrites a couple of times a second -- only entries that
// changed go out -- and NGO hands a late joiner whole with the spawn. The host reads the same list a
// client does, so DistantGroupSilhouette has one code path. Nothing is saved here: the group's record
// (position, rosterSeed) already is.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public sealed class DistantGroups : NetworkBehaviour
    {
        [Tooltip("Seconds between the server's writes. A folded group steps once per NpcWorldSim tick (1 s), so " +
                 "twice a second never leaves the drawn city more than a tick behind.")]
        [Min(0.05f)]
        [SerializeField] private float publishInterval = 0.5f;

        // Built with the behaviour: NGO registers NetworkVariables when it initialises them.
        private NetworkList<DistantGroupState> states = new();
        private readonly List<DistantGroupState> desired = new();
        private float publishTimer;

        /// <summary>Groups published; 0 until this object has spawned.</summary>
        public int Count => IsSpawned ? states.Count : 0;

        public DistantGroupState this[int index] => states[index];

        /// <summary>Every group in <paramref name="groups"/> whose template is showFromAfar and that is not wiped out, into <paramref name="into"/> (cleared first).</summary>
        public static void Collect(IReadOnlyList<NpcGroup> groups, Func<string, NpcGroupTemplate> templateFor, List<DistantGroupState> into)
        {
            into.Clear();
            foreach (NpcGroup group in groups)
            {
                if (group == null || group.WipedOut) continue;

                NpcGroupTemplate template = templateFor(group.TemplateId);
                if (template == null || !template.showFromAfar) continue;

                into.Add(DistantGroupState.Of(group));
            }
        }

        private void Update()
        {
            if (!IsSpawned || !Network.Simulates(this)) return;

            publishTimer -= Time.deltaTime;
            if (publishTimer > 0f) return;
            publishTimer = publishInterval;

            NpcWorldSim sim = NpcWorldSim.Instance;
            if (sim == null) return;

            Collect(sim.Groups, sim.FindTemplate, desired);
            Publish();
        }

        /// <summary>Writes only the entries that differ, so an idle city sends nothing.</summary>
        private void Publish()
        {
            for (int i = 0; i < desired.Count; i++)
            {
                if (i >= states.Count) states.Add(desired[i]);
                else if (!states[i].Equals(desired[i])) states[i] = desired[i];
            }
            while (states.Count > desired.Count) states.RemoveAt(states.Count - 1);
        }
    }
}
```

- [ ] **Step 5: Compile and put it on the session prefab**

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/typecheck.py --editor` — expected exit 0.

Write `$SCRATCH/distant_groups_wire.cs`:
```csharp
const string path = "Assets/Game/Prefabs/Systems/NetworkGameManager.prefab";
if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == path) return "open in Prefab Mode: close it first";
var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
try {
  if (root.GetComponent<SpaceGame.Agents.DistantGroups>() == null) root.AddComponent<SpaceGame.Agents.DistantGroups>();
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
} finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
return "has DistantGroups: " + (UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<SpaceGame.Agents.DistantGroups>() != null);
```
Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py rt.py 'DistantGroupsTests'` once first (it refreshes and compiles; `TheSessionObject_CarriesDistantGroups` fails — the red for the wiring), then `py ux.py < distant_groups_wire.cs` — expected `has DistantGroups: True`.
Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && git diff --stat Assets/Game/Prefabs/Systems/NetworkGameManager.prefab` — expected one file, a small insertion (the component and its `m_Component` entry).

- [ ] **Step 6: Run the tests**

Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py rt.py 'DistantGroupsTests|NetworkPrefabRegistrationTests|PushableTests'`
Expected: all pass.

- [ ] **Step 7: Document**

- `docs/AI/systems/AgentSystem.md`, `## Multiplayer`: add the bullet `- **Folded groups seen from afar** are published by \`DistantGroups\` ([DistantGroups.cs](Assets/Game/Scripts/Agents/World/DistantGroups.cs)) on the NetworkGameManager prefab — NpcWorldSim's object has no NetworkObject. Server-written \`NetworkList<DistantGroupState>\` (\`GroupHash, TemplateHash, RosterSeed, Position, Yaw, Spawned\`) every \`publishInterval\` 0.5 s, only for \`showFromAfar\` templates, only changed entries.` Bump `updated:`.
- `docs/AI/systems/Multiplayer.md`, `## Model`, the NetworkGameManager bullet: change `(chat, sky anchor, join snapshot, spawn flow)` to `(chat, sky anchor, join snapshot, spawn flow, distant groups — [SettlementLods.md](SettlementLods.md))`. Bump `updated:`. (If Task 1's SettlementLods.md has not landed yet, link `AgentSystem.md` instead and Task 6 repoints it.)

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/docs_check.py --index` — expected exit 0.

- [ ] **Step 8: Commit**

```bash
cd /c/Users/tobia/Documents/spaceGame/SpaceGame
git add Assets/Game/Scripts/Agents/World/DistantGroupState.cs Assets/Game/Scripts/Agents/World/DistantGroupState.cs.meta \
  Assets/Game/Scripts/Agents/World/DistantGroups.cs Assets/Game/Scripts/Agents/World/DistantGroups.cs.meta \
  Assets/Game/Prefabs/Systems/NetworkGameManager.prefab \
  Assets/Game/Editor/Tests/DistantGroupsTests.cs Assets/Game/Editor/Tests/DistantGroupsTests.cs.meta \
  docs/AI/systems/AgentSystem.md docs/AI/systems/Multiplayer.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "feat(net): replicate folded showFromAfar groups on the session object

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Builders bake LODs and add far dust; rebuild the prefabs

**Files:**
- Modify: `Assets/Game/Editor/Vehicles/StriderCityBuilder.cs`, `Assets/Game/Editor/Vehicles/DesertCrawlerBuilder.cs`, `Assets/Game/Editor/Creatures/StriderCrabOutriderBuilder.cs`, `Assets/Game/Editor/Vehicles/StriderBargeBuilder.cs`, `Assets/Game/Editor/Vehicles/StriderMonowheelBuilder.cs`, `Assets/Game/Editor/Environment/SkyCityBuilder.cs`, `Assets/Game/Editor/Environment/SkyFleetBuilder.cs`
- Regenerated: the 11 Strider prefabs (`StriderPrefabPaths`), `SkyCity.prefab`, `SkyFreighter`/`SkySkiff`/`SkyTug` prefabs (paths from `SkyFleetBuilder.Vessels`), and one `<Prefab>_LOD1_Merged.asset` beside each
- Test: create `Assets/Game/Editor/Tests/SettlementLodPrefabTests.cs`; modify `Assets/Game/Editor/Tests/StriderDustPrefabTests.cs`
- Docs: `docs/AI/systems/SettlementLods.md`, `Striders.md`, `SkyTribe.md`, `VehicleDust.md`

**Interfaces:**
- Consumes (Task 1): `SettlementLodBaker.Bake(GameObject, string, SettlementLodSettings.Profile)`, `SettlementLodBaker.StriderPrefabPaths/SkyPrefabPaths/MergedMeshPath`, `SettlementLodSettings.Load().strider/.sky`, `MergedLod`. (Task 2): `VehicleDustWiring.AddFarDust(GameObject, float, float)`, `FarDust`, `IDustLodBand`, `VehicleDustWiring.FarCloudName/FarDustRateFraction/FarDustCullDistance`.
- Produces: every prefab in `StriderPrefabPaths` + `SkyPrefabPaths` carries `MergedLod` + `LODGroup`; every Strider city vehicle (habitat, crawler, crab, 3 barges, 5 Strider monowheels) carries a `FarDust`. Task 6 relies on both.

- [ ] **Step 1: Write the failing tests**

Create `Assets/Game/Editor/Tests/SettlementLodPrefabTests.cs`:

```csharp
// Every Strider city vehicle and every Sky fleet hull, read off disk: a LODGroup whose LOD0 is the
// prefab's own renderers and whose LOD1 is its merged mesh on disk beside it, carrying Mesh LODs and
// costing fewer draws than the original; no particle in any level; the fleet's one group on its city.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class SettlementLodPrefabTests
    {
        public static IEnumerable<string> Baked => SettlementLodBaker.StriderPrefabPaths.Concat(SettlementLodBaker.SkyPrefabPaths);

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"{path} is missing");
            return prefab;
        }

        [TestCaseSource(nameof(Baked))]
        public void EveryPrefab_DrawsItsOwnRenderersNear_AndOneMergedMeshFar(string path)
        {
            GameObject prefab = Load(path);
            var lod = prefab.GetComponent<MergedLod>();
            Assert.IsNotNull(lod, $"{prefab.name} has no merged level: run Tools/SpaceGame/Art/Bake Settlement LODs");
            Assert.AreSame(prefab.GetComponent<LODGroup>(), lod.Group);

            LOD[] lods = lod.Group.GetLODs();
            Assert.AreEqual(2, lods.Length);
            Renderer[] originals = prefab.GetComponentsInChildren<Renderer>(true)
                .Where(r => (r is MeshRenderer || r is SkinnedMeshRenderer) && r != lod.MergedRenderer).ToArray();
            CollectionAssert.AreEquivalent(originals, lods[0].renderers, "LOD0 is the prefab's own renderers");
            CollectionAssert.AreEqual(new Renderer[] { lod.MergedRenderer }, lods[1].renderers);
            Assert.IsFalse(lods.SelectMany(l => l.renderers).Any(r => r is ParticleSystemRenderer), "dust and smoke run at every distance");

            Assert.AreEqual(SettlementLodBaker.MergedMeshPath(path), AssetDatabase.GetAssetPath(lod.Mesh));
            Assert.Greater(lod.Mesh.lodCount, 1, "no Mesh LODs in the merged level");
            Assert.AreEqual(lod.Materials.Length, lod.Mesh.subMeshCount);
            Assert.Less(lod.Mesh.subMeshCount, originals.Sum(r => r.sharedMaterials.Length), "the merged level must cost fewer draws");
        }

        [TestCaseSource(nameof(Baked))]
        public void AVehicleThatCanDie_HasItsHealthOnTheRoot(string path)
        {
            GameObject prefab = Load(path);
            if (prefab.GetComponentInChildren<HealthComponent>(true) == null) return;
            Assert.IsNotNull(prefab.GetComponent<HealthComponent>(), "MergedLod holds a wreck at LOD0 by the root's health");
        }

        [Test]
        public void TheFleetsOnlyLodGroup_IsItsCitys()
        {
            GameObject fleet = Load(SkyFleetBuilder.FleetPrefabPath);
            LODGroup[] groups = fleet.GetComponentsInChildren<LODGroup>(true);
            Assert.AreEqual(1, groups.Length, "a renderer in two LODGroups draws twice");
            Assert.AreNotSame(fleet, groups[0].gameObject);
            Assert.IsNotNull(groups[0].GetComponent<MergedLod>());
        }

        [Test]
        public void TheFarDustStops_WhereTheStriderVehiclesAreCulled() =>
            Assert.AreEqual(SettlementLodSettings.Load().strider.cullBeyondMetres, VehicleDustWiring.FarDustCullDistance);
    }
}
```

In `StriderDustPrefabTests.cs`, add `using System.Linq;` and `using SpaceGame.Vehicles.Monowheel;` to the usings, and add after `LeggedMachines()`:

```csharp
        /// Every vehicle in the city and, for the legged ones, its near dust's peak (puffs/s); NaN: read off the prefab.
        public static IEnumerable<TestCaseData> CityVehicles()
        {
            yield return new TestCaseData(StriderCityBuilder.HabitatPath, StriderCityBuilder.PeakFootfallsPerSecond * StriderCityBuilder.PuffsPerFootfall);
            yield return new TestCaseData(DesertCrawlerBuilder.PrefabPath, DesertCrawlerBuilder.PeakFootfallsPerSecond * DesertCrawlerBuilder.PuffsPerFootfall);
            yield return new TestCaseData(StriderCrabOutriderBuilder.PrefabPath,
                                          StriderCrabOutriderBuilder.PeakFootfallsPerSecond * StriderCrabOutriderBuilder.PuffsPerFootfall);
            foreach ((string _, string variant) in StriderBargeBuilder.Barges)
                yield return new TestCaseData(StriderBargeBuilder.PrefabPath(variant), float.NaN);
            foreach (string variant in StriderMonowheelBuilder.Singles.Concat(StriderMonowheelBuilder.Doubles))
                yield return new TestCaseData(StriderMonowheelBuilder.PrefabPath(variant), float.NaN);
        }

        private static float NearPeak(GameObject prefab, float leggedPeak)
        {
            if (!float.IsNaN(leggedPeak)) return leggedPeak;
            if (prefab.TryGetComponent(out RollingDust rolling)) return rolling.ContactCount * rolling.RateAtFullSpeed;
            return prefab.GetComponentInChildren<MonowheelPresentation>(true).DustAtFullSpeed;
        }

        [TestCaseSource(nameof(CityVehicles))]
        public void ACityVehicle_ThrowsFarDust_InItsNearDustsBand(string path, float leggedPeak)
        {
            GameObject prefab = Load(path);
            FarDust far = prefab.GetComponentInChildren<FarDust>(true);
            Assert.IsNotNull(far, $"{prefab.name} has no far dust: rebuild it");
            Assert.AreEqual(VehicleDustWiring.FarCloudName, far.name);
            Assert.AreSame(prefab.transform, far.transform.parent);
            Assert.AreSame(far.GetComponent<ParticleSystem>(), far.Cloud);

            IDustLodBand band = prefab.GetComponentInChildren<IDustLodBand>(true);
            Assert.AreEqual(band.LodNear, far.FadeNear, "fades in where the near dust starts fading out");
            Assert.AreEqual(band.LodFar, far.FadeFar);
            Assert.AreEqual(NearPeak(prefab, leggedPeak) * VehicleDustWiring.FarDustRateFraction, far.RateAtFullSpeed, 1e-4f);
            Assert.AreEqual(StriderCityBuilder.CityLeaderSpeed, far.FullSpeed, 1e-4f, "full while marching with the city");
            Assert.AreEqual(VehicleDustWiring.FarDustCullDistance, far.CullDistance);
            Assert.AreEqual(DustCloudRecipe.MinSize * VehicleDustWiring.FarDustSizeMultiplier, far.Cloud.main.startSize.constantMin, 1e-4f);
            AssertSharedSand(far.Cloud);
        }
```

- [ ] **Step 2: Run them against today's prefabs to see them fail**

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/typecheck.py --editor` — expected exit 0 (Tasks 1 and 2 are in).
Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py rt.py 'SettlementLodPrefabTests|StriderDustPrefabTests'`
Expected: `EveryPrefab_…` and `ACityVehicle_ThrowsFarDust…` FAIL ("has no merged level" / "has no far dust"); `TheFarDustStops…` passes; the existing legged tests pass.

- [ ] **Step 3: Wire the Strider builders**

Every edit adds the far dust right after the vehicle's near dust and the bake right before its `SaveAsPrefabAsset` (after everything that adds renderers). Each builder file gets `using SpaceGame.Vehicles;` if it lacks it (for `FarDust` is not referenced by name, so only add it if the compiler asks).

`StriderCityBuilder.BuildHabitat`, replace:
```csharp
                VehicleDustWiring.AddFootfallDust(instance, PuffsPerFootfall, PeakFootfallsPerSecond);
                PrefabUtility.SaveAsPrefabAsset(instance, HabitatPath);
```
with:
```csharp
                VehicleDustWiring.AddFootfallDust(instance, PuffsPerFootfall, PeakFootfallsPerSecond);
                VehicleDustWiring.AddFarDust(instance, PeakFootfallsPerSecond * PuffsPerFootfall, CityLeaderSpeed);
                SettlementLodBaker.Bake(instance, HabitatPath, SettlementLodSettings.Load().strider);
                PrefabUtility.SaveAsPrefabAsset(instance, HabitatPath);
```

`DesertCrawlerBuilder.Build`, replace `VehicleDustWiring.AddFootfallDust(root, PuffsPerFootfall, PeakFootfallsPerSecond);` with:
```csharp
            VehicleDustWiring.AddFootfallDust(root, PuffsPerFootfall, PeakFootfallsPerSecond);
            VehicleDustWiring.AddFarDust(root, PeakFootfallsPerSecond * PuffsPerFootfall, StriderCityBuilder.CityLeaderSpeed);
```
and replace `PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);` with:
```csharp
            SettlementLodBaker.Bake(root, PrefabPath, SettlementLodSettings.Load().strider);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
```

`StriderCrabOutriderBuilder.Build`, replace `VehicleDustWiring.AddFootfallDust(root, PuffsPerFootfall, PeakFootfallsPerSecond);` with:
```csharp
            VehicleDustWiring.AddFootfallDust(root, PuffsPerFootfall, PeakFootfallsPerSecond);
            VehicleDustWiring.AddFarDust(root, PeakFootfallsPerSecond * PuffsPerFootfall, StriderCityBuilder.CityLeaderSpeed);
```
and replace `PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);` with:
```csharp
            SettlementLodBaker.Bake(root, PrefabPath, SettlementLodSettings.Load().strider);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
```

`StriderBargeBuilder.Build`, replace `VehicleDustWiring.AddRollingDust(root, markers, root.GetComponent<TrackedHullMotor>().TopSpeed, TrackDustPerContact);` with:
```csharp
                VehicleDustWiring.AddRollingDust(root, markers, root.GetComponent<TrackedHullMotor>().TopSpeed, TrackDustPerContact);
                VehicleDustWiring.AddFarDust(root, markers.Length * TrackDustPerContact, StriderCityBuilder.CityLeaderSpeed);
```
and replace `PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(variant), out bool saved);` with:
```csharp
                SettlementLodBaker.Bake(root, PrefabPath(variant), SettlementLodSettings.Load().strider);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(variant), out bool saved);
```

`StriderMonowheelBuilder.BuildStrider`, replace `return Finish(root, body, path, offsets.Strider);` with:
```csharp
            // Before Finish: its colliders measure renderers under Body only, and the far dust hangs off the root.
            VehicleDustWiring.AddFarDust(root, body.GetComponent<MonowheelPresentation>().DustAtFullSpeed, StriderCityBuilder.CityLeaderSpeed);
            return Finish(root, body, path, offsets.Strider);
```
and in `Finish`, replace `PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);` with:
```csharp
            SettlementLodBaker.Bake(root, path, SettlementLodSettings.Load().strider);
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
```
(The player's monowheel is baked too — it is built by the same pass and is in `StriderPrefabPaths` — but gets no far dust: it never marches with the city.)

- [ ] **Step 4: Wire the Sky builders**

`SkyCityBuilder.Build`: delete the `LodCullRatio` constant and its two-line comment above it (`// One cull level, as on the buildings -- …` / `// to hang a real chain off. …`), delete `StaticPropBuilder.BuildLodGroup(root, renderers, LodCullRatio);`, and replace `PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);` (inside the `try`) with:
```csharp
                // After the root's scale: the transition heights are worked out from the size it is drawn at.
                SettlementLodBaker.Bake(root, PrefabPath, SettlementLodSettings.Load().sky);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
```
Also update the file's header comment line that lists `LODGroup` among what is generated (`// LODGroup, static flags and the prefab -- is generated here rather than`) to say `// generated LODs (SettlementLodBaker), static flags and the prefab -- is generated here rather than`.

`SkyFleetBuilder.BuildVessel`: delete `StaticPropBuilder.BuildLodGroup(root, renderers, LodCullRatio);` and replace `PrefabUtility.SaveAsPrefabAsset(root, vessel.PrefabPath);` with:
```csharp
            SettlementLodBaker.Bake(root, vessel.PrefabPath, SettlementLodSettings.Load().sky);
            PrefabUtility.SaveAsPrefabAsset(root, vessel.PrefabPath);
```
Keep `SkyFleetBuilder.LodCullRatio`: `SkyVesselBuilder` (the war-party vessels, out of scope) still uses it.

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/typecheck.py --editor` — expected exit 0.

- [ ] **Step 5: Rebuild the Strider prefabs**

Record what is dirty before building: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && git status --short > "$SCRATCH/lods_status_before.txt"`.

Write `$SCRATCH/lods_build_striders.cs`:
```csharp
string[] menus = {
  "Tools/SpaceGame/Vehicles/Build Strider Habitat Walker",
  "Tools/Vehicles/Build Desert Crawler Prefab",
  "Tools/Creatures/Build Strider Crab Outrider",
  "Tools/SpaceGame/Vehicles/Build Strider Barges",
  "Tools/SpaceGame/Vehicles/Build Strider Monowheels",
};
var report = new System.Text.StringBuilder();
foreach (var m in menus) report.AppendLine(m + ": " + UnityEditor.EditorApplication.ExecuteMenuItem(m));
UnityEditor.AssetDatabase.SaveAssets();
return report.ToString();
```
Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py ux.py < lods_build_striders.cs` — expected five `: True` lines. Then read the console's errors with the Unity MCP `read_console` tool (load it with ToolSearch `select:mcp__UnityMCP__read_console`; call it with `types: ["error"]`, `count: 50`). Expected: no `[StriderCityBuilder]`/`[DesertCrawler]`/`[StriderCrabOutrider]`/`[StriderBargeBuilder]`/`[StriderMonowheel]` errors and no exception from `SettlementLodBaker` or `AddFarDust`.

- [ ] **Step 6: Bake the Sky prefabs in place**

The Sky builders also rewrite `persistentScene` placements (`SkyFleetPlacement.Place`), so the Sky prefabs are baked in place instead of rebuilt. `BakeAll` re-bakes the Strider prefabs too, which is a no-op in content.

Write `$SCRATCH/lods_bake_all.cs`:
```csharp
SpaceGame.EditorTools.SettlementLodBaker.BakeAll();
return "baked";
```
Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py ux.py < lods_bake_all.cs` — expected `baked`; the console's `[SettlementLodBaker]` report lists every prefab with its renderer count, merged submesh count and mesh LOD count. Copy that report into your notes for the doc (Step 10).

- [ ] **Step 7: Run the tests**

Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py rt.py 'SettlementLodPrefabTests|StriderDustPrefabTests|SkyFleetPrefabTests|StriderNetworkPrefabTests|StriderHabitatWalkerTests|StriderBargeTests|StriderMonowheelPrefabTests|StriderCrabOutriderTests|StriderCityTemplateTests|NetworkPrefabRegistrationTests'`
Expected: all pass. A failure in `StriderCityTemplateTests.EveryFollower_IsACardOfTheDeck_WithItsRulesAndFootprint` means a footprint measured from renderers changed — the merged child has the same bounds as the originals, so investigate the builder diff before re-running Wire Strider City.

- [ ] **Step 8: Check the diff is only what this task made**

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && git status --short | diff "$SCRATCH/lods_status_before.txt" - | grep '^>'`
Expected: the builder sources, the Strider and Sky prefabs, new `*_LOD1_Merged.asset` (+ `.meta`) beside each, possibly `DefaultNetworkPrefabs.asset` (registrar re-sync; must show no added/removed entries — `git diff Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset` empty or reordering only). Anything else (other prefabs touched by `SaveableWiring.TryWirePrefabs`, `RagdollWiring.WirePrefabs`) — inspect; do not commit files this task did not mean to change.

- [ ] **Step 9: Renders: LOD0, merged near, merged far; Mesh LOD selection inside a LODGroup**

Write `$SCRATCH/lods_render.cs` (renders each prefab at its LOD0, merged at 200 m, merged at 700 m; and the merged renderer at 700 m with `forceMeshLod` 0 vs the default, to see whether Mesh LOD selection runs inside a LODGroup):
```csharp
try {
var outDir = "C:/Users/tobia/AppData/Local/Temp/claude/C--Users-tobia-Documents-spaceGame-SpaceGame/ab2a1a7b-473e-4041-a764-18ee3b609e3d/scratchpad/";
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
var report = new System.Text.StringBuilder();
try {
  var lightGo = new GameObject("l"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene);
  var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; lightGo.transform.rotation = Quaternion.Euler(40, 30, 0);
  var camGo = new GameObject("c"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
  var cam = camGo.AddComponent<Camera>(); cam.scene = scene; cam.fieldOfView = 60f; cam.farClipPlane = 5000f;
  cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.80f, 0.72f, 0.60f);
  var paths = new[] { SpaceGame.EditorTools.StriderCityBuilder.HabitatPath, SpaceGame.EditorTools.DesertCrawlerBuilder.PrefabPath,
                      SpaceGame.EditorTools.StriderBargeBuilder.PrefabPath(SpaceGame.EditorTools.StriderBargeBuilder.Barges[0].Variant),
                      SpaceGame.EditorTools.SkyCityBuilder.PrefabPath };
  foreach (var path in paths) {
    var go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
    var lod = go.GetComponent<SpaceGame.Vehicles.MergedLod>();
    Bounds b = lod.MergedRenderer.bounds;
    foreach (var shot in new[] { ("lod0", 0, 1.2f), ("merged200", 1, 200f), ("merged700", 1, 700f), ("merged700_meshlod0", 1, 700f) }) {
      lod.Group.ForceLOD(shot.Item2);
      lod.MergedRenderer.forceMeshLod = shot.Item1 == "merged700_meshlod0" ? 0 : -1;
      float d = shot.Item1 == "lod0" ? b.extents.magnitude * 2.2f : shot.Item3;
      cam.transform.position = b.center + new Vector3(0.6f, 0.25f, -0.75f).normalized * d; cam.transform.LookAt(b.center);
      var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt; cam.Render();
      RenderTexture.active = rt; var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = null;
      System.IO.File.WriteAllBytes(outDir + "lods_" + go.name + "_" + shot.Item1 + ".png", tex.EncodeToPNG()); rt.Release();
    }
    report.AppendLine(go.name + " merged submeshes " + lod.Mesh.subMeshCount + " mesh lods " + lod.Mesh.lodCount);
    Object.DestroyImmediate(go);
  }
} finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
return report.ToString();
} catch (System.Exception e) { return "EXC " + e; }
```
Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py ux.py < lods_render.cs`, then open the PNGs with the Read tool. Check: merged levels match LOD0 in silhouette and colour (no missing parts, no inside-out faces, no T-posed limbs); `merged700` vs `merged700_meshlod0` — if they look identical in triangle density at the edges, Mesh LOD selection is either not running inside a LODGroup or not yet stepping down at 700 m; say which in the doc's Gotchas (Step 10) as "unverified" or "verified", with the image names.

Profiler draw-call counts: only if `powershell -c "[math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory/1MB,2)"` prints more than 2.5. Otherwise record in the doc that the draw-call proxy is the test's `subMeshCount < material slots` and the `[SettlementLodBaker]` report numbers.

- [ ] **Step 10: Document**

- `docs/AI/systems/SettlementLods.md`: add `  - Assets/Game/Editor/Tests/SettlementLodPrefabTests.cs` to `paths:`. In `## Model` add a bullet `- **Coverage:** every Strider city vehicle (\`StriderPrefabPaths\`: habitat, crawler, crab, three barges, five Strider monowheels and the player's) and the Sky fleet (\`SkyCity.prefab\` — nested in \`SkyCityFleet\`, whose root has no group — and the escort hulls). Measured: <paste the [SettlementLodBaker] report: renderers → merged submeshes per prefab>.` In `## Flows` add `- **Builders:** each Strider builder adds far dust ([VehicleDust.md](VehicleDust.md)) and bakes right before \`SaveAsPrefabAsset\`; \`SkyCityBuilder\` after setting the root's 1.5 scale; \`SkyFleetBuilder.BuildVessel\` in place of the old single-level cull group. The Sky prefabs were baked in place by \`BakeAll\` (rebuilding them re-places the fleet in \`persistentScene\`).` Add the Mesh-LOD-in-LODGroup finding from Step 9 to `## Gotchas`.
- `docs/AI/systems/SkyTribe.md`: wherever the fleet's cull `LODGroup` is described (grep `LOD` / `cull`), say the city and escorts now carry generated levels ([SettlementLods.md](SettlementLods.md)), sky profile 400 m merged / 8000 m culled. Bump `updated:`.
- `docs/AI/systems/Striders.md`: in `## Flows` **Build** (or the builder rows), add `then far dust (VehicleDust.md) and the bake (SettlementLods.md)`. Bump `updated:`.
- `docs/AI/systems/VehicleDust.md`: in `## Flows` **Build**, add that each Strider builder now calls `AddFarDust` right after its near dust (the monowheels in `BuildStrider`, before `Finish`).

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/docs_check.py --index` — expected exit 0.

- [ ] **Step 11: Commit**

```bash
cd /c/Users/tobia/Documents/spaceGame/SpaceGame
git add Assets/Game/Editor/Vehicles/StriderCityBuilder.cs Assets/Game/Editor/Vehicles/DesertCrawlerBuilder.cs \
  Assets/Game/Editor/Creatures/StriderCrabOutriderBuilder.cs Assets/Game/Editor/Vehicles/StriderBargeBuilder.cs \
  Assets/Game/Editor/Vehicles/StriderMonowheelBuilder.cs Assets/Game/Editor/Environment/SkyCityBuilder.cs \
  Assets/Game/Editor/Environment/SkyFleetBuilder.cs \
  Assets/Game/Editor/Tests/SettlementLodPrefabTests.cs Assets/Game/Editor/Tests/SettlementLodPrefabTests.cs.meta \
  Assets/Game/Editor/Tests/StriderDustPrefabTests.cs \
  docs/AI/systems/SettlementLods.md docs/AI/systems/SkyTribe.md docs/AI/systems/Striders.md docs/AI/systems/VehicleDust.md \
  docs/AI/INDEX.md docs/AI/ROUTING.md
# The regenerated prefabs and their merged meshes, by name (never a wildcard over the whole folder):
git add Assets/Game/Prefabs/Agents/Vehicles/Ground/StriderHabitatWalker.prefab Assets/Game/Prefabs/Agents/Vehicles/Ground/DesertCrawler.prefab \
  Assets/Game/Prefabs/Agents/Vehicles/Ground/StriderDuneBarge.prefab Assets/Game/Prefabs/Agents/Vehicles/Ground/StriderDuneBargeCompact.prefab \
  Assets/Game/Prefabs/Agents/Vehicles/Ground/StriderDuneBargeLookout.prefab \
  Assets/Game/Prefabs/Agents/Characters/Striders/StriderCrabOutrider.prefab \
  Assets/Game/Prefabs/Agents/Vehicles/Ground/Monowheels/StriderMonowheel_*.prefab Assets/Game/Prefabs/Agents/Vehicles/Ground/Monowheels/PlayerMonowheel.prefab \
  Assets/Game/Prefabs/Environment/Structures/SkyFleet/SkyCity.prefab \
  Assets/Game/Prefabs/Environment/Structures/SkyFleet/SkyFreighter.prefab \
  Assets/Game/Prefabs/Environment/Structures/SkyFleet/SkySkiff.prefab \
  Assets/Game/Prefabs/Environment/Structures/SkyFleet/SkyTug.prefab
# The new merged meshes and their metas (all new, all this task's):
git add $(git ls-files --others --exclude-standard | grep '_LOD1_Merged\.asset')
git status --short   # confirm nothing outside this task is staged before committing
git commit -m "feat(lods): Strider vehicles and Sky fleet carry generated LODs; Strider vehicles throw far dust

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: The distant city silhouette

**Files:**
- Create: `Assets/Game/Scripts/Agents/World/DistantGroupSilhouette.cs`
- Modify (by Step 5's snippet): `Assets/Game/Prefabs/Systems/NetworkGameManager.prefab`
- Test: `Assets/Game/Editor/Tests/DistantGroupSilhouetteTests.cs`
- Docs: `docs/AI/systems/SettlementLods.md`, `docs/AI/systems/Striders.md`, `docs/Human/the-systems.md`

**Interfaces:**
- Consumes: (Task 1) `MergedLod.Mesh/Materials`; (Task 2) `FarDust`, `FarDust.ResetBaseline()`; (Task 3) `GroupColumnLayout.Places`, `ColumnPlace`, `NpcWorldSim.FindTemplateByHash`; (Task 4) `DistantGroups.Count`, indexer, `DistantGroupState`; existing `NpcGroupComposition.Resolve(NpcGroup, NpcGroupTemplate)`, `NpcWorldSim.Instance/SpawnRadius`, `SettlementPlacementUtil.TerrainHeightAt(Terrain[], Vector2, float)`.
- Produces: `public sealed class DistantGroupSilhouette : MonoBehaviour` (`[RequireComponent(typeof(DistantGroups))]`) with `readonly struct Place { GameObject Prefab; Vector3 Local; }`, `static List<Place> Layout(NpcGroupTemplate, int rosterSeed)`, `static bool ShouldShow(bool spawned, float cameraDistance, float spawnRadius)`, `static Vector3 FollowPosition(Vector3, Vector3, float dt, float lag)`, `static float FollowYaw(float, float, float dt, float lag)`, `static float GroundUnder(Terrain[], Vector3)`.

- [ ] **Step 1: Write the failing tests**

Create `Assets/Game/Editor/Tests/DistantGroupSilhouetteTests.cs`:

```csharp
// The folded Strider city drawn from afar: in the live spawn's slots and dealt order for every seed and
// heading, every vehicle with a merged level and far dust, shown only while folded and beyond spawnRadius
// with a camera, nothing over unloaded ground, and gliding -- never stepping -- between the server's writes.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class DistantGroupSilhouetteTests
    {
        [Test]
        public void TheSilhouette_StandsInTheLiveSpawnsSlots_InTheSameDealtOrder([Values(1, 7, 1234, -99, 424242)] int seed)
        {
            NpcGroupTemplate city = StriderCityTemplateTests.ReadTemplate(RosterAuthoring.StriderCityTemplateId);
            var group = new NpcGroup { Id = city.id, TemplateId = city.id, RosterSeed = seed };
            List<PlannedMember> plan = NpcGroupComposition.Resolve(group, city);
            var origin = new Vector3(812f, 40f, -1530f);
            Quaternion turn = Quaternion.Euler(0f, 123f, 0f);
            List<ColumnPlace> live = GroupColumnLayout.Places(plan, origin, turn * Vector3.forward, city.formation)
                .Where(p => !plan[p.PlanIndex].Crew).ToList();

            List<DistantGroupSilhouette.Place> drawn = DistantGroupSilhouette.Layout(city, seed);

            Assert.AreEqual(RosterAuthoring.CityFollowers + 1, drawn.Count, "every vehicle and no crew");
            Assert.AreEqual(live.Count, drawn.Count);
            for (int i = 0; i < drawn.Count; i++)
            {
                Assert.AreSame(plan[live[i].PlanIndex].Prefab, drawn[i].Prefab, $"place {i}: not the vehicle ColumnDeal dealt there");
                Assert.Less(Vector3.Distance(live[i].Position, origin + turn * drawn[i].Local), 1e-3f, $"place {i}");
            }
        }

        [Test]
        public void EveryVehicleInTheCity_HasAMergedLevelAndFarDust()
        {
            NpcGroupTemplate city = StriderCityTemplateTests.ReadTemplate(RosterAuthoring.StriderCityTemplateId);
            foreach (GameObject vehicle in DistantGroupSilhouette.Layout(city, 1).Select(p => p.Prefab).Distinct())
            {
                var lod = vehicle.GetComponent<MergedLod>();
                Assert.IsNotNull(lod, $"{vehicle.name}: run Tools/SpaceGame/Art/Bake Settlement LODs");
                Assert.IsNotNull(lod.Mesh);
                Assert.IsNotNull(vehicle.GetComponentInChildren<FarDust>(true), $"{vehicle.name}: rebuild it for far dust");
            }
        }

        [Test]
        public void ItIsDrawn_OnlyFolded_BeyondSpawnRadius_WithACamera()
        {
            Assert.IsTrue(DistantGroupSilhouette.ShouldShow(spawned: false, cameraDistance: 600f, spawnRadius: 250f));
            Assert.IsFalse(DistantGroupSilhouette.ShouldShow(spawned: true, cameraDistance: 600f, spawnRadius: 250f), "the live city is drawn");
            Assert.IsFalse(DistantGroupSilhouette.ShouldShow(spawned: false, cameraDistance: 200f, spawnRadius: 250f), "about to spawn");
            Assert.IsFalse(DistantGroupSilhouette.ShouldShow(spawned: false, cameraDistance: float.NaN, spawnRadius: 250f), "no camera");
        }

        [Test]
        public void GroundUnder_IsNaN_OffEveryLoadedTile() =>
            Assert.IsNaN(DistantGroupSilhouette.GroundUnder(new Terrain[0], new Vector3(5f, 0f, 5f)));

        [Test]
        public void ItGlides_TowardWhereTheServerSaid_WithoutOvershooting()
        {
            var shown = Vector3.zero;
            var target = new Vector3(3f, 0f, 0f);
            float last = 0f;
            for (int i = 0; i < 300; i++)
            {
                shown = DistantGroupSilhouette.FollowPosition(shown, target, 1f / 60f, 1.5f);
                Assert.GreaterOrEqual(shown.x, last);
                Assert.LessOrEqual(shown.x, target.x);
                last = shown.x;
            }
            Assert.Less(target.x - shown.x, 0.1f, "most of the gap closed within a few lags");
            Assert.AreEqual(15f, Mathf.Repeat(DistantGroupSilhouette.FollowYaw(355f, 15f, 1f, 1e-3f), 360f), 1e-2f,
                            "turns the short way round, through 0");
            Assert.AreEqual(5f, Mathf.Repeat(DistantGroupSilhouette.FollowYaw(355f, 15f, 1.5f * Mathf.Log(2f), 1.5f), 360f), 1e-2f,
                            "halfway after one half-life is 5 degrees, not 185");
        }

        [Test]
        public void TheSessionObject_DrawsTheDistantGroups()
        {
            var session = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Systems/NetworkGameManager.prefab");
            Assert.IsNotNull(session.GetComponent<DistantGroupSilhouette>());
        }
    }
}
```

- [ ] **Step 2: Run the offline compile to see it fail**

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/typecheck.py --editor`
Expected: errors for `DistantGroupSilhouette`.

- [ ] **Step 3: Write `DistantGroupSilhouette`**

Create `Assets/Game/Scripts/Agents/World/DistantGroupSilhouette.cs`:

```csharp
// The Strider city seen from afar, drawn on every machine (SettlementLods.md, "The distant city").
//
// A folded group is a record with no GameObjects (NpcGroup), so beyond spawnRadius the city used to be
// invisible. This draws each opted-in group DistantGroups publishes: every vehicle's merged far level
// (MergedLod -- its Mesh LODs pick the coarse levels at this range) in the column the live spawn would
// use (GroupColumnLayout over NpcGroupComposition.Resolve, dealt from the replicated roster seed: the
// same order on every machine), each standing on the terrain under it, each with a copy of its far dust.
// Plain renderers, no NetworkObjects, nothing saved.
//
// Drawn only while the group is folded, the camera is beyond spawnRadius and there is loaded terrain
// under the slot -- so never over the void. When the group spawns the renderers go and the dust stays to
// settle: the live city arrives inside the cloud, in the same slots, drawing its own merged level.
using System.Collections.Generic;
using SpaceGame.Vehicles;
using SpaceGame.World;
using UnityEngine;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DistantGroups))]
    public sealed class DistantGroupSilhouette : MonoBehaviour
    {
        [Tooltip("Seconds the drawn city takes to close most of the gap to where the server last said it was. A " +
                 "folded group steps once per sim tick; this turns the steps into a march.")]
        [Min(0.01f)]
        [SerializeField] private float positionLag = 1.5f;

        [Tooltip("A jump farther than this (m) is a load, a refold or a teleport: snap instead of gliding.")]
        [Min(1f)]
        [SerializeField] private float snapDistance = 60f;

        /// <summary>A vehicle of the column and where it stands relative to the leader, heading +Z.</summary>
        public readonly struct Place
        {
            public readonly GameObject Prefab;
            public readonly Vector3 Local;

            public Place(GameObject prefab, Vector3 local)
            {
                Prefab = prefab;
                Local = local;
            }
        }

        private sealed class Vehicle
        {
            public Transform Body;
            public MeshRenderer Renderer;
            public FarDust Dust;
            public Vector3 Local;
            /// <summary>The merged mesh's lowest point in its own space: stood on the ground, not sunk into it.</summary>
            public float Sole;
        }

        private sealed class View
        {
            public int TemplateHash;
            public int RosterSeed;
            public GameObject Root;
            public readonly List<Vehicle> Vehicles = new();
            public Vector3 ShownPosition;
            public float ShownYaw;
            public bool Posed;
        }

        private DistantGroups source;
        private readonly Dictionary<int, View> views = new();
        private readonly HashSet<int> published = new();
        private readonly List<int> gone = new();

        /// <summary>
        /// The vehicles of a group with <paramref name="template"/> and <paramref name="rosterSeed"/>, in the
        /// order and places the live spawn gives them with the leader at the origin heading +Z. Crew are
        /// left out: they ride the carriers.
        /// </summary>
        public static List<Place> Layout(NpcGroupTemplate template, int rosterSeed)
        {
            var group = new NpcGroup { Id = template.id, TemplateId = template.id, RosterSeed = rosterSeed };
            List<PlannedMember> plan = NpcGroupComposition.Resolve(group, template);
            var places = new List<Place>();
            foreach (ColumnPlace place in GroupColumnLayout.Places(plan, Vector3.zero, Vector3.forward, template.formation))
            {
                PlannedMember member = plan[place.PlanIndex];
                if (!member.Crew) places.Add(new Place(member.Prefab, place.Position));
            }
            return places;
        }

        /// <summary>Folded, with a camera beyond <paramref name="spawnRadius"/>. NaN (no camera) compares false: not drawn.</summary>
        public static bool ShouldShow(bool spawned, float cameraDistance, float spawnRadius) =>
            !spawned && cameraDistance > spawnRadius;

        public static Vector3 FollowPosition(Vector3 shown, Vector3 target, float dt, float lag) =>
            Vector3.Lerp(shown, target, 1f - Mathf.Exp(-dt / lag));

        public static float FollowYaw(float shown, float target, float dt, float lag) =>
            Mathf.LerpAngle(shown, target, 1f - Mathf.Exp(-dt / lag));

        /// <summary>The loaded terrain's height under <paramref name="at"/>; NaN off every loaded tile.</summary>
        public static float GroundUnder(Terrain[] terrains, Vector3 at) =>
            SettlementPlacementUtil.TerrainHeightAt(terrains, new Vector2(at.x, at.z), float.NaN);

        private void Awake() => source = GetComponent<DistantGroups>();

        private void LateUpdate()
        {
            NpcWorldSim sim = NpcWorldSim.Instance;
            if (sim == null) return;

            Camera cam = Camera.main;
            Terrain[] terrains = Terrain.activeTerrains;
            published.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                DistantGroupState state = source[i];
                published.Add(state.GroupHash);
                View view = ViewFor(state, sim);
                bool snapped = Follow(view, state, Time.deltaTime);
                float distance = cam == null
                    ? float.NaN
                    : Vector2.Distance(new Vector2(cam.transform.position.x, cam.transform.position.z),
                                       new Vector2(view.ShownPosition.x, view.ShownPosition.z));
                Pose(view, terrains, ShouldShow(state.Spawned, distance, sim.SpawnRadius), snapped);
            }

            gone.Clear();
            foreach (int hash in views.Keys)
                if (!published.Contains(hash)) gone.Add(hash);
            foreach (int hash in gone)
            {
                Destroy(views[hash].Root);
                views.Remove(hash);
            }
        }

        private View ViewFor(DistantGroupState state, NpcWorldSim sim)
        {
            if (views.TryGetValue(state.GroupHash, out View view))
            {
                if (view.TemplateHash == state.TemplateHash && view.RosterSeed == state.RosterSeed) return view;
                Destroy(view.Root);
            }

            view = Build(state, sim.FindTemplateByHash(state.TemplateHash));
            views[state.GroupHash] = view;
            return view;
        }

        /// <summary>The group's vehicles as plain renderers under one root, each with a copy of its far dust.</summary>
        private View Build(DistantGroupState state, NpcGroupTemplate template)
        {
            var view = new View
            {
                TemplateHash = state.TemplateHash,
                RosterSeed = state.RosterSeed,
                Root = new GameObject($"Distant_{state.GroupHash}"),
            };
            view.Root.transform.SetParent(transform, false);
            if (template == null)
            {
                Debug.LogError($"[DistantGroupSilhouette] No template hashes to {state.TemplateHash}: this machine's NpcWorldSim " +
                               "lacks one the server has. Nothing is drawn for that group.", this);
                return view;
            }

            foreach (Place place in Layout(template, state.RosterSeed))
            {
                var merged = place.Prefab.GetComponent<MergedLod>();
                if (merged == null)
                {
                    Debug.LogError($"[DistantGroupSilhouette] {place.Prefab.name} has no merged far level; run " +
                                   "Tools/SpaceGame/Art/Bake Settlement LODs. It is left out of the distant city.", this);
                    continue;
                }

                var body = new GameObject(place.Prefab.name);
                body.transform.SetParent(view.Root.transform, false);
                body.AddComponent<MeshFilter>().sharedMesh = merged.Mesh;
                var renderer = body.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = merged.Materials;
                renderer.enabled = false;

                FarDust dust = null;
                FarDust farDust = place.Prefab.GetComponentInChildren<FarDust>(true);
                if (farDust != null) dust = Instantiate(farDust.gameObject, body.transform, false).GetComponent<FarDust>();

                view.Vehicles.Add(new Vehicle
                {
                    Body = body.transform, Renderer = renderer, Dust = dust,
                    Local = place.Local, Sole = merged.Mesh.bounds.min.y,
                });
            }
            return view;
        }

        /// <summary>Glides the shown pose toward the published one; snaps on the first pose and on a jump. True when it snapped.</summary>
        private bool Follow(View view, DistantGroupState state, float dt)
        {
            if (!view.Posed || Vector3.Distance(view.ShownPosition, state.Position) > snapDistance)
            {
                view.ShownPosition = state.Position;
                view.ShownYaw = state.Yaw;
                view.Posed = true;
                return true;
            }

            view.ShownPosition = FollowPosition(view.ShownPosition, state.Position, dt, positionLag);
            view.ShownYaw = FollowYaw(view.ShownYaw, state.Yaw, dt, positionLag);
            return false;
        }

        /// <summary>
        /// Stands every vehicle on the ground under its slot. A hidden one keeps its last place, so its far
        /// dust reads no motion and the cloud it left settles where the city was drawn.
        /// </summary>
        private static void Pose(View view, Terrain[] terrains, bool show, bool snapped)
        {
            Quaternion facing = Quaternion.Euler(0f, view.ShownYaw, 0f);
            foreach (Vehicle vehicle in view.Vehicles)
            {
                Vector3 at = view.ShownPosition + facing * vehicle.Local;
                float ground = GroundUnder(terrains, at);
                bool drawn = show && !float.IsNaN(ground);
                vehicle.Renderer.enabled = drawn;
                if (!drawn) continue;

                vehicle.Body.SetPositionAndRotation(new Vector3(at.x, ground - vehicle.Sole, at.z), facing);
                if (snapped && vehicle.Dust != null) vehicle.Dust.ResetBaseline();
            }
        }
    }
}
```

- [ ] **Step 4: Compile and run the tests (the session test is the red)**

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/typecheck.py --editor` — expected exit 0.
Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py rt.py 'DistantGroupSilhouetteTests'`
Expected: `TheSessionObject_DrawsTheDistantGroups` FAILS; every other test passes.

- [ ] **Step 5: Put it on the session prefab**

Write `$SCRATCH/distant_silhouette_wire.cs`:
```csharp
const string path = "Assets/Game/Prefabs/Systems/NetworkGameManager.prefab";
if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == path) return "open in Prefab Mode: close it first";
var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
try {
  if (root.GetComponent<SpaceGame.Agents.DistantGroupSilhouette>() == null) root.AddComponent<SpaceGame.Agents.DistantGroupSilhouette>();
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
} finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
return "has silhouette: " + (UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<SpaceGame.Agents.DistantGroupSilhouette>() != null);
```
Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py ux.py < distant_silhouette_wire.cs` — expected `has silhouette: True`.
Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py rt.py 'DistantGroupSilhouetteTests|DistantGroupsTests|GroupColumnLayoutTests|PushableTests'`
Expected: all pass.

- [ ] **Step 6: Render the distant city at 300 m and 700 m**

Write `$SCRATCH/distant_render.cs` — builds the silhouette's layout in a preview scene over a flat slab (the snippet stands the merged meshes itself, as `Pose` does, since a preview scene has no loaded terrain), simulates 20 s of marching for the far dust, and renders from 300 m and 700 m:
```csharp
try {
var outDir = "C:/Users/tobia/AppData/Local/Temp/claude/C--Users-tobia-Documents-spaceGame-SpaceGame/ab2a1a7b-473e-4041-a764-18ee3b609e3d/scratchpad/";
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
try {
  var city = SpaceGame.EditorTools.StriderCityTemplateTests.ReadTemplate(SpaceGame.EditorTools.RosterAuthoring.StriderCityTemplateId);
  var places = SpaceGame.Agents.DistantGroupSilhouette.Layout(city, 1234);
  var slab = GameObject.CreatePrimitive(PrimitiveType.Cube); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(slab, scene);
  slab.transform.localScale = new Vector3(4000f, 1f, 4000f); slab.transform.position = new Vector3(0f, -0.5f, 0f);
  var bodies = new System.Collections.Generic.List<(Transform t, Vector3 local, SpaceGame.Vehicles.FarDust dust)>();
  foreach (var p in places) {
    var lod = p.Prefab.GetComponent<SpaceGame.Vehicles.MergedLod>();
    var go = new GameObject(p.Prefab.name); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
    go.AddComponent<MeshFilter>().sharedMesh = lod.Mesh; go.AddComponent<MeshRenderer>().sharedMaterials = lod.Materials;
    var far = p.Prefab.GetComponentInChildren<SpaceGame.Vehicles.FarDust>(true);
    var dust = Object.Instantiate(far.gameObject, go.transform, false).GetComponent<SpaceGame.Vehicles.FarDust>();
    bodies.Add((go.transform, p.Local + Vector3.up * -lod.Mesh.bounds.min.y, dust));
  }
  const float dt = 1f / 30f;
  for (int f = 0; f < 600; f++) {
    var lead = new Vector3(0f, 0f, 2.7f * f * dt);
    foreach (var b in bodies) { b.t.position = lead + b.local; b.dust.Present(dt, 500f); b.dust.Cloud.Simulate(dt, true, false, false); }
  }
  var lightGo = new GameObject("l"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene);
  var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; lightGo.transform.rotation = Quaternion.Euler(35, 40, 0);
  var camGo = new GameObject("c"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
  var cam = camGo.AddComponent<Camera>(); cam.scene = scene; cam.fieldOfView = 60f; cam.farClipPlane = 5000f;
  cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.80f, 0.72f, 0.60f);
  var centre = new Vector3(0f, 10f, 2.7f * 600 * dt - 150f);
  foreach (var d in new[] { 300f, 700f }) {
    cam.transform.position = centre + new Vector3(0.8f, 0.12f, 0.6f).normalized * d; cam.transform.LookAt(centre);
    var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt; cam.Render();
    RenderTexture.active = rt; var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = null;
    System.IO.File.WriteAllBytes(outDir + "distant_city_" + (int)d + ".png", tex.EncodeToPNG()); rt.Release();
  }
  return places.Count + " vehicles drawn";
} finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
} catch (System.Exception e) { return "EXC " + e; }
```
Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py ux.py < distant_render.cs` — expected `17 vehicles drawn` (`RosterAuthoring.CityFollowers + 1`: 2 houses, 2 crawlers, 2 crabs, 3 barges, 8 monowheels). Open `distant_city_300.png` and `distant_city_700.png` with Read: the column must read as a city marching inside large dust clouds; no vehicle floating or sunk.

- [ ] **Step 7: Document**

- `docs/AI/systems/SettlementLods.md`: `summary:` → `"Generated far levels for the Strider city and Sky fleet, and the folded Strider city drawn from afar"`. Add to `paths:` `  - Assets/Game/Scripts/Agents/World/DistantGroups.cs`, `  - Assets/Game/Scripts/Agents/World/DistantGroupState.cs`, `  - Assets/Game/Scripts/Agents/World/DistantGroupSilhouette.cs`, `  - Assets/Game/Editor/Tests/DistantGroupSilhouetteTests.cs`, `  - Assets/Game/Editor/Tests/DistantGroupsTests.cs`. Add to `symptoms:` `  - "the Strider city is invisible until I am right next to it"`, `  - "the walking city pops in or jumps sideways when I walk up to it"`. Add `Multiplayer, AgentSystem` to `reads_with`.
  - `## Model`, new bullet: `- **The distant city.** A \`showFromAfar\` template (only \`strider-city\`) is drawn while folded: \`DistantGroups\` on the NetworkGameManager prefab publishes \`{GroupHash, TemplateHash, RosterSeed, Position, Yaw, Spawned}\` at 2 Hz; \`DistantGroupSilhouette\` (same object, every machine, host included) deals the column from the template + seed (\`NpcGroupComposition.Resolve\`), places each vehicle by \`GroupColumnLayout.Places\` — the live spawn's own slots — rotated by \`Yaw\`, stands its \`MergedLod\` mesh's lowest point on \`SettlementPlacementUtil.TerrainHeightAt\`, and gives it a copy of its \`FarDust\`. Drawn while \`!Spawned\`, camera beyond \`NpcWorldSim.SpawnRadius\` and terrain loaded under the slot.`
  - `## Key types` rows for `DistantGroups` (`Collect`, `Count`, indexer; server writes changed entries only), `DistantGroupState` (wire struct; `Of(group)`), `DistantGroupSilhouette` (`Layout`, `ShouldShow`, `FollowPosition/FollowYaw` 1.5 s lag, snap past 60 m, `GroundUnder`).
  - `## Flows`: `- **Hand-over:** a player within spawnRadius → the server spawns the live city into the same places (\`GroupColumnLayout\`) → \`Spawned\` replicates → renderers off, the far dust copies stop moving and their clouds settle around the live city, which at 250 m is drawing its own merged level.`
  - `## Multiplayer`: replace the N/A line with: `LODs and dust: nothing sent. The distant city: server-written \`NetworkList\` on the session NetworkObject, read on every machine including the host (one path); a late joiner gets the whole list with the spawn. The column is dealt locally — \`ColumnDeal\` is deterministic for a template and seed, and templates are scene data on every machine.`
  - `## Persistence`: replace with: `Nothing new is saved. The distant city is rebuilt from the group record, whose \`position\` and \`rosterSeed\` are saved (\`NpcGroup.Record\`), so a reload draws the same column where it was.`
  - `## Gotchas`: `- **A parked city faces +Z.** \`NpcGroup.Heading\` is \`Vector3.forward\` while the group has no goal, and the live spawn faces it too, so the silhouette turns to +Z when the city stops — matching what spawns there, not where it was heading.` and `- **Seen from at most the loaded ground** (3×3 chunks of 500 m round each player): a slot with no terrain under it is not drawn, so the city appears at the edge of the loaded ground inside its dust, never over the void.`
- `docs/AI/systems/Striders.md`: if Task 3 linked the city's `showFromAfar` sentence to `VehicleDust.md`, repoint it to `SettlementLods.md`. Same for `Multiplayer.md` if Task 4 linked `AgentSystem.md` in place of `SettlementLods.md`.
- `docs/Human/the-systems.md`, `### Settlements seen from afar *(SettlementLods)*`: append to the paragraph `The walking city can also be seen marching in its dust from the edge of the loaded ground, long before it is close enough to come to life; when you reach it, the real city takes over in the same places.`

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/docs_check.py --index` — expected exit 0.

- [ ] **Step 8: Commit**

```bash
cd /c/Users/tobia/Documents/spaceGame/SpaceGame
git add Assets/Game/Scripts/Agents/World/DistantGroupSilhouette.cs Assets/Game/Scripts/Agents/World/DistantGroupSilhouette.cs.meta \
  Assets/Game/Prefabs/Systems/NetworkGameManager.prefab \
  Assets/Game/Editor/Tests/DistantGroupSilhouetteTests.cs Assets/Game/Editor/Tests/DistantGroupSilhouetteTests.cs.meta \
  docs/AI/systems/SettlementLods.md docs/AI/systems/Striders.md docs/AI/systems/Multiplayer.md docs/Human/the-systems.md \
  docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "feat(striders): the folded walking city is drawn from afar in its dust

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: Client reports and the verification an agent cannot do

**Files:**
- Modify: `Assets/Game/Scripts/Core/Multiplayer/Autotest/AutotestRunner.Client.cs`, `Assets/Game/Scripts/Core/Multiplayer/Autotest/AutotestRunner.Host.cs`
- Docs: `docs/AI/systems/Testing.md`, `docs/AI/systems/SettlementLods.md`

**Interfaces:**
- Consumes (Task 4): `DistantGroups.Count`.
- Produces: `[MPTEST] HOST_DISTANT_GROUPS=<n>` and `[MPTEST] CLIENT_DISTANT_GROUPS=<n>`; expected `1` on both (the Strider city) once the world has loaded.

- [ ] **Step 1: Add the reports**

In `AutotestRunner.Client.cs`, after `Report("CLIENT_SPAWNED", NetworkManager.Singleton.SpawnManager.SpawnedObjects.Count);` add:

```csharp
            // The walking city seen from afar replicates as state on the session object (DistantGroups):
            // a client that has the list draws the city exactly where the host does.
            DistantGroups distant = FindAnyObjectByType<DistantGroups>();
            Report("CLIENT_DISTANT_GROUPS", distant != null ? distant.Count : -1);
```

In `AutotestRunner.Host.cs`, after `Report("HOST_CLIENTS", NetworkManager.Singleton.ConnectedClientsIds.Count);` add:

```csharp
            DistantGroups distant = FindAnyObjectByType<DistantGroups>();
            Report("HOST_DISTANT_GROUPS", distant != null ? distant.Count : -1);
```

Add `using SpaceGame.Agents;` to each file if not already present.

- [ ] **Step 2: Compile**

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/typecheck.py --editor` — expected exit 0.
Run: `cd "$SCRATCH" && export PYTHONIOENCODING=utf-8; py rt.py 'NetMessagingTests|DistantGroupsTests'` — expected pass (this proves the editor compiles them; the runners themselves only run in a built player).

- [ ] **Step 3: Document**

- `docs/AI/systems/Testing.md`: where the `[MPTEST]` keys are listed (grep `CLIENT_SPAWNED`), add `HOST_DISTANT_GROUPS` / `CLIENT_DISTANT_GROUPS` (expected 1: the Strider city). Note in the same place that `MultiplayerTestPlayerBuilder` targets `StandaloneOSX` only, so the two-process run needs a mac (or a Windows target added there). Bump `updated:`.
- `docs/AI/systems/SettlementLods.md` `## Gotchas`: add `- **Not yet seen on a real client by an agent (2026-10-05).** The test player builds for macOS only and agents may not enter Play Mode; the checklist in docs/superpowers/plans/2026-10-05-settlement-lods.md Task 7 is how a person proves it.` — remove this line once a person has run the checklist.

Run: `cd /c/Users/tobia/Documents/spaceGame/SpaceGame && py tools/docs_check.py --index` — expected exit 0.

- [ ] **Step 4: Commit**

```bash
cd /c/Users/tobia/Documents/spaceGame/SpaceGame
git add Assets/Game/Scripts/Core/Multiplayer/Autotest/AutotestRunner.Client.cs Assets/Game/Scripts/Core/Multiplayer/Autotest/AutotestRunner.Host.cs \
  docs/AI/systems/Testing.md docs/AI/systems/SettlementLods.md docs/AI/INDEX.md docs/AI/ROUTING.md
git commit -m "test(net): report the replicated distant groups on host and client

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 5: Hand the human checklist to the user (do not run it yourself)**

Report this checklist to the user verbatim and stop; it needs Play Mode and two instances (MPPM, or `-sgprofile client` on a second instance), which agents may not drive:

1. Host a world; a second instance joins as client.
2. On both, read the city's position: in the host's Scene view the `NpcWorldSim` gizmo, or `strider-city` in the save JSON. On the client, `/tp` to ~600 m from it.
3. **Both machines:** the city is visible as a column of vehicles marching inside large dust clouds, at the same place and in the same order on host and client (compare screenshots).
4. Walk toward it. Between ~200 m and ~80 m the far dust fades out as footfall/track dust fades in. At 250 m (spawnRadius) the live city replaces the silhouette with no sideways jump; its legs start moving inside the dust.
5. Walk to ~160 m from a single house on the client: it switches from the merged level to its animated LOD0 (and back when walking away) identically on host and client.
6. Kill a crab outrider and walk 300 m away: its wreck stays drawn as it lies, never standing.
7. Save, quit to menu, load the world: the distant city is where it was, in the same column order (same `rosterSeed` in the save JSON before and after).
8. Sky fleet: fly/teleport 1–3 km from it; it stays visible, and its engine smoke runs at every distance.

---

## Self-review (done while writing)

- **Spec coverage.** Part 1 (baker, LOD0/LOD1, `BakeMesh`, one submesh per material, Mesh LODs, cull, asset beside prefab overwritten, settings asset, particles excluded, Sky `LODGroup` replaced, builders re-run): Tasks 1 and 5. Part 2 (far dust on every Strider city vehicle, recipe, ×4 size, ⅕ rate, longer life, round the hull, speed via `GroundSpeedGauge`, crossfade on the near band, on to the cull, Sky smoke unchanged, tunables serialized, nothing sent or saved): Tasks 2 and 5. Part 3 (`showFromAfar`, `WireStriderCity`, replicated state ~2 Hz, one code path for host and client, merged meshes in formation slots in the dealt order, terrain height, far dust, visibility rules, hand-over, nothing saved): Tasks 3, 4, 6. Testing section: baker tests (Task 1), prefab tests (Task 5), crossfade maths (Task 2), payload round-trip (Task 4), slot layout equals live spawn (Tasks 3 and 6), renders (Tasks 5 and 6), profiler conditional (Task 5 Step 9), manual host + client + reload (Task 7 Step 5).
- **Placeholder scan.** One deliberate fill-in: Task 5 Step 10 pastes the measured `[SettlementLodBaker]` report into the doc — the value only exists after the bake. Likewise the Mesh-LOD-inside-a-LODGroup finding (Task 5 Step 9) is recorded as measured.
- **Type consistency.** `MergedLod.ChildName/Group/Mesh/Materials/MergedRenderer/Configure/ForcedLevel`; `SettlementLodSettings.Profile` fields `mergedBeyondMetres/cullBeyondMetres/meshLodLimit/referenceFovDegrees`; `SettlementLodBaker.Bake/MergedMeshPath/ScreenHeightAt/StriderPrefabPaths/SkyPrefabPaths/BakeAll`; `FarDust.Configure(ParticleSystem, float, float, IDustLodBand, float)`/`Fade/Present/ResetBaseline/Cloud/FullSpeed/RateAtFullSpeed/FadeNear/FadeFar/CullDistance`; `VehicleDustWiring.AddFarDust(GameObject, float, float)` and its five constants; `DustCloudRecipe.MinSize/MaxSize`; `ColumnPlace(PlanIndex, Leads, Position)`; `GroupColumnLayout.Places(IReadOnlyList<PlannedMember>, Vector3, Vector3, in FormationShape)`; `NpcGroupTemplate.showFromAfar/IdHash/HashOf/FindByIdHash`; `NpcWorldSim.FindTemplateByHash`; `DistantGroupState` fields and `Of/YawOf/Heading`; `DistantGroups.Count/this[]/Collect`; `DistantGroupSilhouette.Place/Layout/ShouldShow/FollowPosition/FollowYaw/GroundUnder` — used with the same names in every later task.
- **Review Focus.** Each of the five lines has a named test in its owning task (Tasks 1, 3, 5, 6).
