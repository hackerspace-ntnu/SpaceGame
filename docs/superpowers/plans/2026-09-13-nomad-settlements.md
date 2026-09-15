# Nomad Settlements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Place nine nomad settlements — two large, three medium, four small — across the main world, each bound to one root GameObject, sitting flush on ground chosen for being flat, built from the forty nomad building prefabs and eight freestanding tents, and lived in by a population scaled to the tier that wanders, flocks and patrols.

**Architecture:** Three new files plus one editor command, in a new folder beside the existing settlement code. A pure seeded layout solver takes a ground-height function and returns placements; a `MonoBehaviour` generator turns solved placements into prefab instances under one `Generated` child and wires the behaviour modules; one editor command chooses all nine sites, writes each into its owning chunk scene, verifies it, and bakes the world NavMesh. The three wander behaviours are existing modules — no new AI code.

**Tech Stack:** Unity 6 / C#, Unity NGO, NUnit EditMode tests under `Assets/Game/Editor/Tests/`, `python3 tools/typecheck.py --editor` for headless compilation, `~/.unity/bin/unity test` for headless test runs (Editor must be CLOSED).

**Spec:** [docs/superpowers/specs/2026-09-13-nomad-settlements-design.md](../specs/2026-09-13-nomad-settlements-design.md)

---

## Constraint: the existing systems are frozen

**Do not modify any of these files.** Every one of them ships today and is out of scope:

```
Assets/Game/Scripts/World/ProceduralGeneration/Settlement/**   (all of it)
Assets/Game/Scripts/agents/Modules/Patrol/PatrolModule.cs
Assets/Game/Scripts/agents/Modules/Patrol/BasePatrolModule.cs
Assets/Game/Scripts/agents/Modules/Flocking/HerdModule.cs
Assets/Game/Scripts/agents/Faction/SettlementPopulation.cs
Assets/Game/Scripts/agents/Faction/SettlementAlarm.cs
Assets/Game/Scripts/World/Sites/WorldSiteMarker.cs
Assets/Game/Editor/Environment/ClankerSettlementBuilder.cs
```

Everything a settlement needs from those components is a private `[SerializeField]`, and the project's
answer to that is already `SerializedObject` — `NomadPrefabBuilder.ConfigureWatch` sets `WatchModule`'s
private `priority`, `requiredRelationship` and `detectRadius` exactly this way. So the generator wires
them the same way and nothing frozen is touched. This is not a workaround; it is the pattern.

Two consequences to be honest about:

1. **`NomadPlacementGeometry` duplicates three private helpers** inside `RobotSettlementGenerator`
   (`GetPrefabFootprint`, `BuildingClearanceRadius`, `IsTooCloseToOthers`). Extracting them into one
   shared file would be the right structure and is what the project's no-copy-paste rule asks for, but
   it means editing a frozen file. The duplication is deliberate, it is recorded in the new file's
   header and in `TerrainGeneration.md`, and collapsing it is a small, safe change the day the freeze
   lifts. Do **not** quietly "fix" it by editing the robot generator.
2. **Ground sampling is not duplicated.** `TerrainProbe.TryGetTerrainHeight` already answers exactly
   this question, is not frozen, and is used directly.

**Editable:** `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs` and the five Nomad prefabs it writes,
plus any new file.

---

## Before you start

Read these, in this order. They are short and they contain the traps this plan exists to avoid:

1. `docs/AI/systems/TerrainGeneration.md` — the whole Gotchas section.
2. `docs/AI/INVARIANTS.md`.
3. `Assets/Game/Editor/Environment/ClankerSettlementBuilder.cs` — read it, do not edit it. This plan builds the nomad equivalent and reuses its public site search.

Three facts that will otherwise cost you hours:

- **A mesa is invisible to an editor raycast.** Terrain-feature meshes are spawned at bake time and are not in the chunk scene, so `Physics.Raycast` and `Terrain.SampleHeight` both report the flat sand *under* a mesa. Keep-out boxes come from `TerrainFeatureSpawner.Area.ComputeLocalBounds()`.
- **Terrain and buildings share the `Default` layer**, so a ground raycast masked `~0` can hit a building you already placed. This plan never raycasts for ground; it reads the terrain heightmap.
- **A compile error over unity-mcp is invisible**, and another agent's broken file freezes your domain reload. Run `python3 tools/typecheck.py --editor` yourself; do not trust a silent Editor.

## File structure

Everything new lives in `NomadSettlement/`, a sibling of the frozen `Settlement/` folder, so the
boundary is visible in the tree rather than only in this document.

**Create:**

| Path | Responsibility |
| --- | --- |
| `Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement/NomadPlacementGeometry.cs` | Prefab footprint, clearance radius, spacing test, ground height. No randomness |
| `Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement/NomadSettlementLayout.cs` | The pure seeded solver. Settings + seed + a height function in, placements out. No scene, no prefabs |
| `Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement/NomadSettlementRecipe.cs` | `ScriptableObject` holding one tier's prefab arrays, layout numbers and population numbers |
| `Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement/NomadSettlementGenerator.cs` | The one object. Places a solved layout, emits patrol routes, wires every module |
| `Assets/Game/Editor/Environment/NomadSettlementPlacer.cs` | The editor command: site search across chunks, nine placements, verification, NavMesh bake |
| `Assets/Game/Editor/Tests/NomadSettlementTests.cs` | EditMode tests for the geometry, the solver, the recipe assets and the Nomad prefabs |

**Modify (the only files this plan changes):**

| Path | Change |
| --- | --- |
| `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs` | Swap `WanderModule` for `PatrolModule` in the agent stack, and configure it |
| `Assets/Game/Prefabs/agents/Characters/Nomad*.prefab` (5) | Rewritten by re-running that builder |

**Create as assets (by running the placer, not by hand):**

- `Assets/Game/ScriptableObjects/Settlements/NomadSettlement_Large.asset`
- `Assets/Game/ScriptableObjects/Settlements/NomadSettlement_Medium.asset`
- `Assets/Game/ScriptableObjects/Settlements/NomadSettlement_Small.asset`

---

## Task 1: Placement geometry

**Files:**
- Create: `Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement/NomadPlacementGeometry.cs`
- Test: `Assets/Game/Editor/Tests/NomadSettlementTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Game/Editor/Tests/NomadSettlementTests.cs`:

```csharp
// The placement geometry decides whether two buildings overlap and how far off the ground one sits.
// Both can be wrong without anything looking wrong in a screenshot, so they are checked against
// shapes whose answer is known exactly.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.World;

namespace SpaceGame.EditorTests
{
    public class NomadPlacementGeometryTests
    {
        [Test]
        public void FootprintOfAUnitCubeIsOneByOne()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                Vector2 footprint = NomadPlacementGeometry.MeasureFootprint(cube);
                Assert.AreEqual(1f, footprint.x, 1e-3f);
                Assert.AreEqual(1f, footprint.y, 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }

        [Test]
        public void FootprintFollowsAScaledChild()
        {
            // Every nomad building is an FBX whose mesh child sits at 100x with the exporter's -90 deg
            // X baked in. Reading mesh.bounds directly would report the unscaled mesh, so the measure
            // has to go through the child transform -- this is that case, in miniature.
            var parent = new GameObject("parent");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                cube.transform.SetParent(parent.transform, worldPositionStays: false);
                cube.transform.localScale = new Vector3(4f, 1f, 2f);

                Vector2 footprint = NomadPlacementGeometry.MeasureFootprint(parent);
                Assert.AreEqual(4f, footprint.x, 1e-3f);
                Assert.AreEqual(2f, footprint.y, 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void ClearanceIsHalfTheLongestSidePlusPadding()
        {
            // 4 x 2 footprint, 2 m padding: (4 + 2) / 2 = 3.
            float clearance = NomadPlacementGeometry.ClearanceRadius(new Vector2(4f, 2f),
                                                                     padding: 2f, minSpacing: 0f);
            Assert.AreEqual(3f, clearance, 1e-4f);
        }

        [Test]
        public void ClearanceNeverGoesBelowMinimumSpacing()
        {
            float clearance = NomadPlacementGeometry.ClearanceRadius(new Vector2(1f, 1f),
                                                                     padding: 0f, minSpacing: 10f);
            Assert.AreEqual(5f, clearance, 1e-4f);
        }

        [Test]
        public void OverlappingDiscsAreTooClose()
        {
            var placed = new List<NomadPlacementGeometry.Disc>
            {
                new NomadPlacementGeometry.Disc { Centre = Vector2.zero, Radius = 5f },
            };

            Assert.IsTrue(NomadPlacementGeometry.IsTooClose(new Vector2(6f, 0f), 5f, placed),
                          "6 m apart with radii 5 + 5 = 10 must collide");
            Assert.IsFalse(NomadPlacementGeometry.IsTooClose(new Vector2(11f, 0f), 5f, placed),
                           "11 m apart with radii 5 + 5 = 10 must not collide");
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Close the Unity Editor first — `unity test` refuses while an Editor has the project open.

Run:
```bash
~/.unity/bin/unity test . --mode EditMode --filter NomadPlacementGeometryTests --output /tmp/nomad-tests.xml
```
Expected: FAIL. The compile does not get that far — `NomadPlacementGeometry` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement/NomadPlacementGeometry.cs`:

```csharp
// Placement geometry for the nomad settlements: how big a prefab is on the ground, how much room it
// needs, whether a spot is already taken, and how high the terrain is under a point.
//
// DUPLICATION, AND WHY. The first three of those exist already as private helpers inside
// RobotSettlementGenerator (GetPrefabFootprint, BuildingClearanceRadius, IsTooCloseToOthers). The
// right structure is one shared file both generators call, and that is a small change -- but it means
// editing the robot generator, and the settlement system is frozen. So this is a deliberate second
// copy, not an oversight. When the freeze lifts: delete these three from here, move them into a
// shared type, and point both generators at it. Do not "fix" it by editing the frozen file.
//
// The ground sampling is NOT duplicated: TerrainProbe already answers exactly this question for the
// under-terrain guard, and is used directly.
//
// Free of random numbers on purpose. The layout solver owns the whole seeded sequence; a helper that
// consumed draws from it would make one town's layout depend on how often another helper was called.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.World.Safety;

namespace SpaceGame.World
{
    public static class NomadPlacementGeometry
    {
        /// <summary>A spot already taken, as a circle on the XZ plane.</summary>
        public struct Disc
        {
            public Vector2 Centre;
            public float Radius;
        }

        /// <summary>
        /// The prefab's XZ extent in root-local metres, measured over every mesh in its hierarchy.
        ///
        /// Mesh bounds are transformed corner by corner rather than read directly, because a prefab's
        /// meshes sit under children with their own scales and rotations -- an FBX child at 100x with
        /// the exporter's -90 deg X baked in is the normal case in this project, not the exception.
        /// </summary>
        public static Vector2 MeasureFootprint(GameObject prefab)
        {
            if (prefab == null) return Vector2.zero;

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            Transform root = prefab.transform;
            Bounds? combined = null;

            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null) continue;

                Bounds local = filter.sharedMesh.bounds;
                Vector3 centre = local.center;
                Vector3 extents = local.extents;

                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? -extents.x : extents.x,
                        (corner & 2) == 0 ? -extents.y : extents.y,
                        (corner & 4) == 0 ? -extents.z : extents.z);

                    Vector3 inRoot = root.InverseTransformPoint(filter.transform.TransformPoint(centre + offset));
                    if (combined == null) combined = new Bounds(inRoot, Vector3.zero);
                    else
                    {
                        Bounds grown = combined.Value;
                        grown.Encapsulate(inRoot);
                        combined = grown;
                    }
                }
            }

            return combined.HasValue
                ? new Vector2(combined.Value.size.x, combined.Value.size.z)
                : Vector2.zero;
        }

        /// <summary>
        /// How much clear ground a prefab of this footprint needs, as a radius.
        ///
        /// The LONGEST side, not the average: a building is yawed after it is placed, and a clearance
        /// measured off its narrow side would let that turn push it into its neighbour.
        /// </summary>
        public static float ClearanceRadius(Vector2 footprint, float padding, float minSpacing)
        {
            float longest = Mathf.Max(footprint.x, footprint.y);
            return Mathf.Max(longest + padding, minSpacing) * 0.5f;
        }

        /// <summary>True when a disc of <paramref name="radius"/> at <paramref name="centre"/> overlaps anything already placed.</summary>
        public static bool IsTooClose(Vector2 centre, float radius, IReadOnlyList<Disc> placed)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                float minimum = placed[i].Radius + radius;
                if ((placed[i].Centre - centre).sqrMagnitude < minimum * minimum) return true;
            }
            return false;
        }

        /// <summary>
        /// World-space ground height from the terrain HEIGHTMAP, not from a raycast.
        ///
        /// A raycast is the wrong tool here twice over. Terrain and buildings share the Default layer,
        /// so a mask cannot separate them and a later placement can land on an earlier building's roof.
        /// And a terrain feature's mesh is not in the scene at all at edit time -- it is spawned from a
        /// baked asset -- so a ray reports the flat sand under a mesa either way. Callers keep out of
        /// feature footprints explicitly instead.
        ///
        /// The sampling itself is TerrainProbe's, which already answers this for the under-terrain
        /// guard and already gets the out-of-bounds case right: SampleHeight clamps a coordinate off
        /// the side of a terrain to its border rather than admitting it has nothing. Only the shape
        /// differs -- a null here rather than a bool, because the solver takes ground as a Func
        /// returning float?, the same shape SettlementSiteScore takes.
        ///
        /// Null means the point is off every terrain. A site half off the edge of the world is not a
        /// site, and a caller must be able to tell that from "the ground is at zero".
        /// </summary>
        public static float? TerrainHeightAt(float worldX, float worldZ)
        {
            return TerrainProbe.TryGetTerrainHeight(new Vector3(worldX, 0f, worldZ), out float height)
                ? height
                : (float?)null;
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:
```bash
python3 tools/typecheck.py --editor
~/.unity/bin/unity test . --mode EditMode --filter NomadPlacementGeometryTests --output /tmp/nomad-tests.xml
```
Expected: typecheck exits 0; five tests pass.

- [ ] **Step 5: Confirm nothing frozen was touched**

Run:
```bash
git status --porcelain
```
Expected: only the two new files (plus their `.meta`) and the test file. If `RobotSettlementGenerator.cs` or anything else under `Settlement/` appears, revert it.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement \
        Assets/Game/Editor/Tests/NomadSettlementTests.cs \
        Assets/Game/Editor/Tests/NomadSettlementTests.cs.meta
git commit -m "feat(settlement): nomad placement geometry"
```

---

## Task 2: The seeded layout solver

The part that decides where every building and tent goes. Kept pure — no prefabs, no scene, no `Terrain` — so it can be tested against ground whose shape is known exactly, which is the only way to prove the slope rejection actually rejects slopes.

**Files:**
- Create: `Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement/NomadSettlementLayout.cs`
- Test: `Assets/Game/Editor/Tests/NomadSettlementTests.cs` (append)

- [ ] **Step 1: Write the failing tests**

Append to `Assets/Game/Editor/Tests/NomadSettlementTests.cs`, inside the same namespace:

```csharp
    // The solver is the one place a settlement can come out wrong in a way no screenshot shows: a
    // building half a metre into a dune reads as "the artist put it there". So it is run over ground
    // whose height is a formula, where the right answer is arithmetic.
    public class NomadSettlementLayoutTests
    {
        private static NomadLayoutSettings Settings() => new NomadLayoutSettings
        {
            largeCount = new Vector2Int(2, 2),
            mediumCount = new Vector2Int(3, 3),
            smallCount = new Vector2Int(2, 2),
            tentCount = new Vector2Int(4, 4),
            largePrefabs = 12,
            mediumPrefabs = 20,
            smallPrefabs = 8,
            tentPrefabs = 8,
            coreRadius = 18f,
            midRadius = 34f,
            outerRadius = 52f,
            tentRadius = 70f,
            minStructureSpacing = 6f,
            buildingPadding = 2f,
            maxGroundRange = 0.4f,
            placementAttempts = 40,
            footprintSamples = 3,
        };

        // Every nomad building measured 1.97-11.30 m across, so a flat 8 x 8 stands in for all of them.
        private static Vector2 EightByEight(NomadBuildingClass _, int __) => new Vector2(8f, 8f);

        [Test]
        public void FlatGroundPlacesEverythingAsked()
        {
            var placements = NomadSettlementLayout.Solve(Settings(), seed: 1701,
                                                          heightAt: (x, z) => 40f,
                                                          footprintOf: EightByEight);

            Assert.AreEqual(2, CountOf(placements, NomadBuildingClass.Large));
            Assert.AreEqual(3, CountOf(placements, NomadBuildingClass.Medium));
            Assert.AreEqual(2, CountOf(placements, NomadBuildingClass.Small));
            Assert.AreEqual(4, CountOf(placements, NomadBuildingClass.Tent));
        }

        [Test]
        public void SteepGroundPlacesNothing()
        {
            // A 30 % grade. Over an 8 m footprint the range is 2.4 m, six times the 0.4 m tolerance,
            // so no spot anywhere can pass and the solver must give up rather than tip a building.
            var placements = NomadSettlementLayout.Solve(Settings(), seed: 1701,
                                                          heightAt: (x, z) => x * 0.3f,
                                                          footprintOf: EightByEight);

            Assert.AreEqual(0, placements.Count);
        }

        [Test]
        public void NothingOverlapsAnythingElse()
        {
            var placements = NomadSettlementLayout.Solve(Settings(), seed: 99,
                                                          heightAt: (x, z) => 40f,
                                                          footprintOf: EightByEight);

            for (int a = 0; a < placements.Count; a++)
            for (int b = a + 1; b < placements.Count; b++)
            {
                float minimum = placements[a].ClearanceRadius + placements[b].ClearanceRadius;
                float actual = Vector2.Distance(placements[a].LocalXZ, placements[b].LocalXZ);
                Assert.GreaterOrEqual(actual, minimum - 1e-3f,
                    $"placements {a} and {b} are {actual:F2} m apart but need {minimum:F2} m");
            }
        }

        [Test]
        public void TheSameSeedGivesTheSameTown()
        {
            var first = NomadSettlementLayout.Solve(Settings(), 4242, (x, z) => 40f, EightByEight);
            var second = NomadSettlementLayout.Solve(Settings(), 4242, (x, z) => 40f, EightByEight);

            Assert.AreEqual(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].LocalXZ, second[i].LocalXZ);
                Assert.AreEqual(first[i].PrefabIndex, second[i].PrefabIndex);
                Assert.AreEqual(first[i].YawDegrees, second[i].YawDegrees, 1e-4f);
            }
        }

        [Test]
        public void GroundThatRunsOutIsNotBuiltOn()
        {
            // Ground exists only within 30 m of the centre. Everything outside must be refused, so
            // nothing can be placed past 30 m minus its own half-footprint.
            var placements = NomadSettlementLayout.Solve(Settings(), 7,
                heightAt: (x, z) => new Vector2(x, z).magnitude <= 30f ? 40f : (float?)null,
                footprintOf: EightByEight);

            foreach (NomadPlacement placement in placements)
                Assert.LessOrEqual(placement.LocalXZ.magnitude, 30f,
                    "a placement was seated on ground the height function said does not exist");
        }

        [Test]
        public void BaseHeightIsTheLowestSampleUnderTheFootprint()
        {
            // A shallow ramp inside tolerance: 4 % grade over 8 m is 0.32 m, under the 0.4 m limit.
            var placements = NomadSettlementLayout.Solve(Settings(), 11,
                                                          heightAt: (x, z) => 40f + x * 0.04f,
                                                          footprintOf: EightByEight);

            Assert.Greater(placements.Count, 0);
            foreach (NomadPlacement placement in placements)
            {
                float lowestCorner = 40f + (placement.LocalXZ.x - 4f) * 0.04f;
                Assert.LessOrEqual(placement.GroundY, lowestCorner + 1e-3f,
                    "the base must sit at or below the lowest ground under it, or a corner floats");
            }
        }

        private static int CountOf(List<NomadPlacement> placements, NomadBuildingClass kind)
        {
            int count = 0;
            foreach (NomadPlacement placement in placements)
                if (placement.Class == kind) count++;
            return count;
        }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```bash
~/.unity/bin/unity test . --mode EditMode --filter NomadSettlementLayoutTests --output /tmp/nomad-tests.xml
```
Expected: FAIL — `NomadSettlementLayout`, `NomadLayoutSettings`, `NomadPlacement` and `NomadBuildingClass` do not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement/NomadSettlementLayout.cs`:

```csharp
// Where every building and tent in a nomad settlement stands, as a pure function of a seed, a set of
// numbers and a ground-height function.
//
// Pure on purpose. The thing that can be wrong here -- a building seated on a slope, two buildings
// overlapping, a town that comes out different every time it is regenerated -- is invisible in a
// screenshot and obvious in arithmetic, so the solver takes its ground as a Func and its randomness
// as a seed, and the test suite runs it over ground whose shape is a formula. Nothing in this file
// touches a Terrain, a prefab or a scene.
//
// System.Random, never UnityEngine.Random: the whole point is that one seed is one town, and global
// Random is shared with everything else in the editor. RobotSettlementGenerator's use of it is the
// documented exception in TerrainGeneration.md, not a pattern.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>Which band of the settlement a thing belongs to, and which prefab array it comes from.</summary>
    public enum NomadBuildingClass { Large, Medium, Small, Tent }

    /// <summary>One solved placement, in settlement-local metres. Y is world height, not local.</summary>
    public readonly struct NomadPlacement
    {
        public readonly NomadBuildingClass Class;
        public readonly int PrefabIndex;
        public readonly Vector2 LocalXZ;
        public readonly float GroundY;
        public readonly float YawDegrees;
        public readonly float ClearanceRadius;

        public NomadPlacement(NomadBuildingClass kind, int prefabIndex, Vector2 localXZ,
                              float groundY, float yawDegrees, float clearanceRadius)
        {
            Class = kind;
            PrefabIndex = prefabIndex;
            LocalXZ = localXZ;
            GroundY = groundY;
            YawDegrees = yawDegrees;
            ClearanceRadius = clearanceRadius;
        }
    }

    /// <summary>Everything the solver needs. Serialized on the recipe, one asset per size tier.</summary>
    [Serializable]
    public struct NomadLayoutSettings
    {
        public Vector2Int largeCount;
        public Vector2Int mediumCount;
        public Vector2Int smallCount;
        public Vector2Int tentCount;

        public int largePrefabs;
        public int mediumPrefabs;
        public int smallPrefabs;
        public int tentPrefabs;

        public float coreRadius;
        public float midRadius;
        public float outerRadius;
        public float tentRadius;

        public float minStructureSpacing;
        public float buildingPadding;

        /// <summary>Metres of height range allowed across a footprint. Above this the spot is refused.</summary>
        public float maxGroundRange;

        public int placementAttempts;

        /// <summary>Samples per side of the grid taken across a footprint. 3 gives a 3 x 3 = 9 sample test.</summary>
        public int footprintSamples;
    }

    public static class NomadSettlementLayout
    {
        /// <summary>
        /// Solves one settlement.
        ///
        /// <paramref name="heightAt"/> returns null where there is no ground; a footprint with any
        /// null sample is refused outright rather than seated on a guess.
        /// <paramref name="footprintOf"/> gives a prefab's XZ extent in metres.
        /// </summary>
        public static List<NomadPlacement> Solve(NomadLayoutSettings settings, int seed,
                                                  Func<float, float, float?> heightAt,
                                                  Func<NomadBuildingClass, int, Vector2> footprintOf)
        {
            var random = new System.Random(seed);
            var placements = new List<NomadPlacement>();
            var taken = new List<NomadPlacementGeometry.Disc>();

            // Largest first. A large building needs the most clear ground, so letting the huts take
            // the middle of the town would leave the landmarks nowhere to stand.
            Place(NomadBuildingClass.Large, settings.largeCount, settings.largePrefabs,
                  0f, settings.coreRadius, faceCentre: true);
            Place(NomadBuildingClass.Medium, settings.mediumCount, settings.mediumPrefabs,
                  settings.coreRadius, settings.midRadius, faceCentre: false);
            Place(NomadBuildingClass.Small, settings.smallCount, settings.smallPrefabs,
                  settings.midRadius, settings.outerRadius, faceCentre: false);
            Place(NomadBuildingClass.Tent, settings.tentCount, settings.tentPrefabs,
                  settings.midRadius, settings.tentRadius, faceCentre: false);

            return placements;

            void Place(NomadBuildingClass kind, Vector2Int countRange, int prefabCount,
                       float minRadius, float maxRadius, bool faceCentre)
            {
                if (prefabCount <= 0) return;

                int wanted = RandRange(random, countRange);
                for (int i = 0; i < wanted; i++)
                {
                    int prefabIndex = random.Next(prefabCount);
                    Vector2 footprint = footprintOf(kind, prefabIndex);
                    float clearance = NomadPlacementGeometry.ClearanceRadius(
                        footprint, settings.buildingPadding, settings.minStructureSpacing);

                    for (int attempt = 0; attempt < Mathf.Max(1, settings.placementAttempts); attempt++)
                    {
                        Vector2 candidate = PointInAnnulus(random, minRadius, maxRadius);
                        if (NomadPlacementGeometry.IsTooClose(candidate, clearance, taken)) continue;
                        if (!TrySeat(candidate, footprint, settings, heightAt, out float groundY)) continue;

                        float yaw = faceCentre
                            ? Mathf.Atan2(-candidate.x, -candidate.y) * Mathf.Rad2Deg
                            : (float)(random.NextDouble() * 360.0);

                        placements.Add(new NomadPlacement(kind, prefabIndex, candidate, groundY, yaw, clearance));
                        taken.Add(new NomadPlacementGeometry.Disc { Centre = candidate, Radius = clearance });
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Reads the ground across the footprint and decides whether a building may stand there.
        ///
        /// The base is seated at the LOWEST sample, not the average: seated at the average, half the
        /// footprint hangs over air, and on this terrain that reads as a building floating at one
        /// corner. Embedding the low side instead is invisible.
        /// </summary>
        private static bool TrySeat(Vector2 centre, Vector2 footprint, NomadLayoutSettings settings,
                                    Func<float, float, float?> heightAt, out float groundY)
        {
            groundY = 0f;

            // The footprint is sampled as the square of its longest side, because the building has not
            // been yawed yet and a yaw would swing the long side over ground the narrow side never covered.
            float half = Mathf.Max(footprint.x, footprint.y) * 0.5f;
            int samples = Mathf.Max(2, settings.footprintSamples);

            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;

            for (int ix = 0; ix < samples; ix++)
            for (int iz = 0; iz < samples; iz++)
            {
                float u = ix / (float)(samples - 1) * 2f - 1f;
                float v = iz / (float)(samples - 1) * 2f - 1f;

                float? height = heightAt(centre.x + u * half, centre.y + v * half);
                if (height == null) return false;

                if (height.Value < min) min = height.Value;
                if (height.Value > max) max = height.Value;
            }

            if (max - min > settings.maxGroundRange) return false;

            groundY = min;
            return true;
        }

        private static int RandRange(System.Random random, Vector2Int range)
        {
            int min = Mathf.Min(range.x, range.y);
            int max = Mathf.Max(range.x, range.y);
            return random.Next(min, max + 1);
        }

        /// <summary>Uniform over the AREA of the ring, not over the radius — otherwise the middle crowds.</summary>
        private static Vector2 PointInAnnulus(System.Random random, float minRadius, float maxRadius)
        {
            float t = (float)random.NextDouble();
            float radius = Mathf.Sqrt(Mathf.Lerp(minRadius * minRadius, maxRadius * maxRadius, t));
            float angle = (float)(random.NextDouble() * Math.PI * 2.0);
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
python3 tools/typecheck.py --editor
~/.unity/bin/unity test . --mode EditMode --filter NomadSettlementLayoutTests --output /tmp/nomad-tests.xml
```
Expected: typecheck exits 0; six tests pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement \
        Assets/Game/Editor/Tests/NomadSettlementTests.cs
git commit -m "feat(settlement): seeded nomad layout solver with slope rejection"
```

---

## Task 3: Anchor the Nomad prefabs' wander

`WanderModule` picks destinations within `wanderRadius` of the agent's **current** position, so it is a random walk with no home. A nomad diffuses a few hundred metres over an hour, leaves `SettlementPopulation.countRadius`, stops counting toward the cap, and the town spawns a replacement — forever. A cap of fourteen robots hides that; thirty-eight nomads across nine towns will not.

`PatrolModule` in `RadiusBased` with no centre assigned anchors to the spawn position and is otherwise the same behaviour. Adding it *beside* `WanderModule` is not an option: both sit at priority 0, `AgentController` takes "highest priority first, first non-null wins", and a tie resolves by component order — the exact ambiguity `NomadPrefabBuilder` already documents at its `ConfigureWatch`.

The five prefabs are independent copies, not Unity prefab variants, so editing `Nomad.prefab` propagates nothing. They are all written by `NomadPrefabBuilder`, so the change goes in the builder and the builder is re-run. `NomadOstrich` has no `WanderModule` and is untouched.

Persistence needs no work: `SaveablePolicy` already adds `PatrolSaveable` for `PatrolModule`, `BasePatrolSaveable` for `BasePatrolModule` and `HerdMemberSaveable` for `HerdModule`.

**Files:**
- Modify: `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs`
- Test: `Assets/Game/Editor/Tests/NomadSettlementTests.cs` (append)

- [ ] **Step 1: Write the failing test**

Append to `Assets/Game/Editor/Tests/NomadSettlementTests.cs`, inside the same namespace:

```csharp
    // Read the ASSETS, not the builder. The builder having the right code proves nothing if nobody
    // re-ran it: the prefab on disk is what ships, and a stale one is the failure this catches.
    public class NomadPrefabModuleTests
    {
        private static readonly string[] NomadPrefabs =
        {
            "Assets/Game/Prefabs/agents/Characters/Nomad.prefab",
            "Assets/Game/Prefabs/agents/Characters/Nomad_Tan.prefab",
            "Assets/Game/Prefabs/agents/Characters/Nomad_Umber.prefab",
            "Assets/Game/Prefabs/agents/Characters/Nomad_Maroon.prefab",
            "Assets/Game/Prefabs/agents/Characters/Nomad_StrawHat.prefab",
        };

        [Test]
        public void EveryNomadPatrolsAndNoneWanders([ValueSource(nameof(NomadPrefabs))] string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"missing prefab: {path}");

            Assert.IsNotNull(prefab.GetComponent<PatrolModule>(),
                $"{path} has no PatrolModule; re-run Tools > SpaceGame > Agents > Build Sand Nomad NPCs");
            Assert.IsNull(prefab.GetComponent<WanderModule>(),
                $"{path} still has WanderModule. Two modules at priority 0 resolve by component " +
                "order, so which one drives is undefined.");
        }
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
~/.unity/bin/unity test . --mode EditMode --filter NomadPrefabModuleTests --output /tmp/nomad-tests.xml
```
Expected: FAIL on all five — `WanderModule` is still there and `PatrolModule` is absent.

- [ ] **Step 3: Swap the module in the builder**

In `Assets/Game/Editor/Agents/NomadPrefabBuilder.cs`, in `AddAgentStack`, replace the line

```csharp
                "SpaceGame.Agents.WanderModule",
```

with

```csharp
                // PatrolModule in RadiusBased, not WanderModule: WanderModule picks its next point
                // relative to where the agent IS, which is a random walk with no home, and a nomad
                // drifts out of his own settlement over an hour. PatrolModule anchors to a point, and
                // with no centre assigned that point is where he woke up. See ConfigurePatrol.
                "SpaceGame.Agents.PatrolModule",
```

- [ ] **Step 4: Configure it**

In the same file, beside `ConfigureWatch`, add:

```csharp
        /// <summary>
        /// Where he mills about when nothing more urgent is happening.
        ///
        /// <para>
        /// No centre is assigned here on purpose. Left null, PatrolModule latches the position it woke
        /// up at, which is right for every nomad in the project without anyone wiring anything: a
        /// caravan member anchors to the road, a settlement inhabitant to the town it was spawned into.
        /// NomadSettlementGenerator overrides the centre on the residents it places, and only those.
        /// </para>
        /// <para>
        /// Priority is set explicitly because Unity does not call <c>Reset</c> for a component added
        /// from a script, and Fallback (0) is what the ladder reserves for wander and patrol.
        /// </para>
        /// </summary>
        private static void ConfigurePatrol(GameObject root)
        {
            var patrol = FindComponent(root, "SpaceGame.Agents.PatrolModule");
            if (patrol == null)
            {
                Debug.LogWarning("[NomadPrefabBuilder] No PatrolModule; he will stand still.");
                return;
            }

            var so = new SerializedObject(patrol);
            SetInt(so, "priority", 0);          // ModulePriority.Fallback
            SetEnum(so, "mode", 0);             // PatrolMode.RadiusBased
            SetFloat(so, "patrolRadius", 14f);  // a loose stand-around, not a march
            SetFloat(so, "minMoveDistance", 3f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
```

Call it from the same place `ConfigureWatch` is called.

- [ ] **Step 5: Re-run the builder**

Open the Unity Editor and run **Tools > SpaceGame > Agents > Build Sand Nomad NPCs**, then **Tools > SpaceGame > Agents > Build Nomad NPC**. Confirm the console logs `[NomadPrefabBuilder] Wrote ...` once per prefab and reports no errors.

If the console is silent, the assembly did not reload — check `python3 tools/typecheck.py --editor` first. A compile error over MCP is invisible.

- [ ] **Step 6: Run the test to verify it passes**

Close the Editor, then run:
```bash
python3 tools/typecheck.py --editor
~/.unity/bin/unity test . --mode EditMode --filter NomadPrefabModuleTests --output /tmp/nomad-tests.xml
```
Expected: five tests pass.

- [ ] **Step 7: Verify a caravan still travels**

This changes behaviour for every nomad, caravan members included. Their fallback rarely runs — `NpcWorldSim` and the formation sit far above priority 0 — but "rarely" is not "never" and this is a shipped prefab.

In the Editor, run **Tools > SpaceGame > Agents > Place Sand Nomad Caravan**, enter play mode, and watch for 60 seconds. Expected: the caravan still forms up and travels. Record what you saw in the commit message. If it stands still, stop and report — do not proceed.

- [ ] **Step 8: Commit**

```bash
git add Assets/Game/Editor/Agents/NomadPrefabBuilder.cs \
        Assets/Game/Prefabs/agents/Characters/Nomad.prefab \
        Assets/Game/Prefabs/agents/Characters/Nomad_Tan.prefab \
        Assets/Game/Prefabs/agents/Characters/Nomad_Umber.prefab \
        Assets/Game/Prefabs/agents/Characters/Nomad_Maroon.prefab \
        Assets/Game/Prefabs/agents/Characters/Nomad_StrawHat.prefab \
        Assets/Game/Editor/Tests/NomadSettlementTests.cs
git commit -m "fix(agents): anchor nomad wandering so a town cannot bleed its people"
```

---

## Task 4: The recipe

**Files:**
- Create: `Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement/NomadSettlementRecipe.cs`
- Test: `Assets/Game/Editor/Tests/NomadSettlementTests.cs` (append)

- [ ] **Step 1: Write the recipe**

Create `Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement/NomadSettlementRecipe.cs`:

```csharp
// One nomad settlement tier, as data. Three assets exist -- Large, Medium, Small -- and the only
// difference between a hamlet and a town is which one the generator is pointed at.
//
// Written by NomadSettlementPlacer rather than filled in by hand, for the reason ClankerSettlementBuilder
// records: a serialized slot keeps whatever was last saved into it, and a settlement generator's
// failure mode is an empty array it silently skips.
using UnityEngine;

namespace SpaceGame.World
{
    [CreateAssetMenu(fileName = "NomadSettlementRecipe", menuName = "Settlement/Nomad Settlement Recipe")]
    public class NomadSettlementRecipe : ScriptableObject
    {
        [Header("Buildings, by size class")]
        public GameObject[] largeBuildings;
        public GameObject[] mediumBuildings;
        public GameObject[] smallBuildings;

        [Tooltip("The freestanding shade sails. The wall-mounted ones are already hung on the " +
                 "buildings by NomadSettlementBuilder and must not appear here.")]
        public GameObject[] tents;

        [Header("Layout")]
        public NomadLayoutSettings layout = new NomadLayoutSettings
        {
            largeCount = new Vector2Int(2, 3),
            mediumCount = new Vector2Int(5, 6),
            smallCount = new Vector2Int(4, 5),
            tentCount = new Vector2Int(7, 7),
            coreRadius = 12f,
            midRadius = 24f,
            outerRadius = 38f,
            tentRadius = 52f,
            minStructureSpacing = 6f,
            buildingPadding = 2f,
            maxGroundRange = 0.45f,
            placementAttempts = 40,
            footprintSamples = 3,
        };

        [Tooltip("Radius of level ground the site search must find for this tier. Must clear tentRadius.")]
        public float siteRadius = 55f;

        [Header("Population")]
        [Tooltip("Residents wander a radius centred on the settlement root.")]
        public int residents = 7;
        [Tooltip("Flocks that move as one group, and how many nomads are in each.")]
        public int flocks = 2;
        public int flockSize = 3;
        [Tooltip("Patrol routes walked around the settlement, and how many nomads walk each.")]
        public int patrolRoutes = 1;
        public int patrolSize = 3;
        [Tooltip("Mounted nomads, placed on their own so a town fields a NUMBER of riders.")]
        public int mounted = 1;

        [Header("Population upkeep")]
        [Tooltip("The town stops spawning once this many of its own are alive inside countRadius. " +
                 "A mounted pair counts as two.")]
        public int populationCap = 20;
        public float spawnInterval = 60f;
        public int spawnsPerWave = 3;
        public float countRadius = 80f;
        public float spawnRingInner = 18f;
        public float spawnRingOuter = 50f;

        [Header("Site")]
        [Tooltip("Home for a town somebody lives in, Camp for a waypoint. Drives what NPC errands " +
                 "can name this place.")]
        public SiteKind siteKind = SiteKind.Home;
    }
}
```

- [ ] **Step 2: Write the failing test**

Append to `Assets/Game/Editor/Tests/NomadSettlementTests.cs`:

```csharp
    // Reads the ASSETS the placer writes, for the reason ClankerSettlementTests gives: an empty
    // prefab array is a settlement that generates nothing and logs nothing.
    public class NomadRecipeAssetTests
    {
        private static readonly string[] RecipePaths =
        {
            "Assets/Game/ScriptableObjects/Settlements/NomadSettlement_Large.asset",
            "Assets/Game/ScriptableObjects/Settlements/NomadSettlement_Medium.asset",
            "Assets/Game/ScriptableObjects/Settlements/NomadSettlement_Small.asset",
        };

        [Test]
        public void EveryTierHasItsPrefabs([ValueSource(nameof(RecipePaths))] string path)
        {
            var recipe = AssetDatabase.LoadAssetAtPath<NomadSettlementRecipe>(path);
            Assert.IsNotNull(recipe, $"missing recipe: {path}. Run Tools > SpaceGame > Settlements > Build Nomad Settlements");

            Assert.AreEqual(12, recipe.largeBuildings.Length, "large buildings");
            Assert.AreEqual(20, recipe.mediumBuildings.Length, "medium buildings");
            Assert.AreEqual(8, recipe.smallBuildings.Length, "small buildings");
            Assert.AreEqual(8, recipe.tents.Length, "freestanding tents");

            foreach (GameObject prefab in recipe.largeBuildings) Assert.IsNotNull(prefab);
            foreach (GameObject prefab in recipe.mediumBuildings) Assert.IsNotNull(prefab);
            foreach (GameObject prefab in recipe.smallBuildings) Assert.IsNotNull(prefab);
            foreach (GameObject prefab in recipe.tents) Assert.IsNotNull(prefab);
        }

        [Test]
        public void TheSiteRadiusCoversTheTown([ValueSource(nameof(RecipePaths))] string path)
        {
            var recipe = AssetDatabase.LoadAssetAtPath<NomadSettlementRecipe>(path);
            Assert.IsNotNull(recipe, $"missing recipe: {path}");

            Assert.GreaterOrEqual(recipe.siteRadius, recipe.layout.tentRadius,
                "the ground the site search levels must reach at least as far as the outermost tent");
        }
    }
```

- [ ] **Step 3: Run the test to verify it fails**

Run:
```bash
~/.unity/bin/unity test . --mode EditMode --filter NomadRecipeAssetTests --output /tmp/nomad-tests.xml
```
Expected: FAIL — the three assets do not exist yet. They are written by Task 6's placer. The test stays red until then, on purpose: it is the assertion that the placer actually wrote them.

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement \
        Assets/Game/Editor/Tests/NomadSettlementTests.cs
git commit -m "feat(settlement): nomad settlement recipe"
```

---

## Task 5: The generator — the one object

The object the whole feature hangs off. Deleting it deletes the settlement; right-clicking its header regenerates it.

Every frozen component it uses is wired through `SerializedObject`, inside `#if UNITY_EDITOR`. That is
the same mechanism `NomadPrefabBuilder` uses on `WatchModule`, and it is why nothing frozen needs a new
`Configure` method. The field names below were read off the component sources; if one is wrong the
wiring fails **silently**, which is why Task 6's `Verify` re-reads them back.

**Files:**
- Create: `Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement/NomadSettlementGenerator.cs`

- [ ] **Step 1: Write the generator**

Create `Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement/NomadSettlementGenerator.cs`:

```csharp
// One nomad settlement, bound to one GameObject.
//
// Everything it makes lives under a single "Generated" child, so Clear is one DestroyImmediate and
// deleting the root in the Hierarchy takes the whole town with it. The same contract as
// RobotSettlementGenerator, on purpose: this is the shape the project already knows for "a settlement
// you can throw away and roll again".
//
// Edit-time tool. Generate() is never called in play mode -- the output is committed to a chunk scene
// and streams in as ordinary scene content, identical bytes on every machine, so no netcode is involved
// in the buildings. The people are a different matter and are placed as networked, saveable prefabs.
//
// The layout itself is NOT decided here. NomadSettlementLayout solves it as a pure function of the seed
// and a ground-height function, so it can be tested against ground whose shape is known; this class
// turns solved placements into prefab instances and wires the behaviour modules.
//
// WIRING. PatrolModule, BasePatrolModule, HerdModule, SettlementPopulation, SettlementAlarm and
// WorldSiteMarker keep their settings in private [SerializeField] fields and are frozen -- no Configure
// method may be added to them. They are therefore wired with SerializedObject, which is how
// NomadPrefabBuilder already sets WatchModule's private fields. A field name that does not exist fails
// SILENTLY, so NomadSettlementPlacer.Verify reads the values back off the built town.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SpaceGame.World
{
    public class NomadSettlementGenerator : MonoBehaviour
    {
        [Header("Recipe")]
        public NomadSettlementRecipe recipe;

        [Header("Determinism")]
        [Tooltip("One seed is one town. Change it and the town changes.")]
        public int seed = 1;

        [Header("People")]
        public FactionDefinition ownerFaction;
        public FactionRelationshipTable relationships;
        [Tooltip("The nomads on foot, used in order. Must be registered network prefabs.")]
        public GameObject[] inhabitantPrefabs;
        [Tooltip("The mounted nomads. Must be a registered network prefab.")]
        public GameObject mountedPrefab;

        [Tooltip("Distinguishes this town's flocks from every other town's. HerdModule keys herds by " +
                 "a GLOBAL string, so two settlements sharing an id would share movement broadcasts " +
                 "across kilometres.")]
        public string herdPrefix = "nomad_s00";

        [Header("Site")]
        public string siteName = string.Empty;

        public const string GeneratedRootName = "Generated";
        public const string PatrolRootName = "Patrol";

        /// <summary>How many waypoints make up one perimeter route. Eight reads as a circuit, not a triangle.</summary>
        public const int WaypointsPerRoute = 8;

        [ContextMenu("Generate")]
        public void Generate()
        {
            if (recipe == null)
            {
                Debug.LogError("[NomadSettlementGenerator] No recipe assigned.", this);
                return;
            }

            Clear();

            Transform root = new GameObject(GeneratedRootName).transform;
            root.SetParent(transform, worldPositionStays: false);
            root.localPosition = Vector3.zero;

            NomadLayoutSettings settings = recipe.layout;
            settings.largePrefabs = Length(recipe.largeBuildings);
            settings.mediumPrefabs = Length(recipe.mediumBuildings);
            settings.smallPrefabs = Length(recipe.smallBuildings);
            settings.tentPrefabs = Length(recipe.tents);

            Vector3 origin = transform.position;

            List<NomadPlacement> placements = NomadSettlementLayout.Solve(
                settings, seed,
                heightAt: (x, z) => NomadPlacementGeometry.TerrainHeightAt(origin.x + x, origin.z + z),
                footprintOf: (kind, index) => NomadPlacementGeometry.MeasureFootprint(PrefabFor(kind, index)));

            foreach (NomadPlacement placement in placements)
            {
                GameObject prefab = PrefabFor(placement.Class, placement.PrefabIndex);
                if (prefab == null) continue;

                var position = new Vector3(origin.x + placement.LocalXZ.x,
                                           placement.GroundY,
                                           origin.z + placement.LocalXZ.y);
                Place(prefab, position, Quaternion.Euler(0f, placement.YawDegrees, 0f), root);
            }

            Transform patrolRoot = BuildPatrolRoutes(origin);
            Populate(root, patrolRoot, origin);
            WireUpkeep();
            WireSite();

            Debug.Log($"[NomadSettlementGenerator] {name}: {placements.Count} structures placed.", this);
        }

        [ContextMenu("Reroll (new seed + generate)")]
        public void Reroll()
        {
            seed = Random.Range(1, int.MaxValue);
            Generate();
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name != GeneratedRootName && child.name != PatrolRootName) continue;

                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        // ── structures ───────────────────────────────────────────────────────────

        private GameObject PrefabFor(NomadBuildingClass kind, int index)
        {
            GameObject[] source = kind switch
            {
                NomadBuildingClass.Large => recipe.largeBuildings,
                NomadBuildingClass.Medium => recipe.mediumBuildings,
                NomadBuildingClass.Small => recipe.smallBuildings,
                _ => recipe.tents,
            };
            return source != null && index >= 0 && index < source.Length ? source[index] : null;
        }

        private static int Length(GameObject[] array) => array?.Length ?? 0;

        /// <summary>
        /// Instantiates as a prefab LINK at edit time, so the town stays connected to its prefabs and a
        /// rebuilt building updates every copy of itself across nine settlements.
        ///
        /// Not named Instantiate: that would hide <c>Object.Instantiate</c> on a MonoBehaviour, and the
        /// next person to add a plain spawn here would silently get this one instead.
        /// </summary>
        private static GameObject Place(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                instance.transform.SetPositionAndRotation(position, rotation);
                return instance;
            }
#endif
            return Object.Instantiate(prefab, position, rotation, parent);
        }

        // ── patrol routes ────────────────────────────────────────────────────────

        /// <summary>
        /// A ring of empty waypoints per route, at the town edge and (for a town with two) through the core.
        ///
        /// Terrain-snapped, NOT NavMesh-snapped: on a first run the NavMesh covering this town does not
        /// exist yet -- the bake happens after the settlement is placed. PatrolModule samples the NavMesh
        /// itself at runtime, so terrain height is enough to put the waypoint in the right place.
        ///
        /// Plain GameObjects, deliberately, so a designer can drag one afterwards.
        /// </summary>
        private Transform BuildPatrolRoutes(Vector3 origin)
        {
            if (recipe.patrolRoutes <= 0) return null;

            Transform patrolRoot = new GameObject(PatrolRootName).transform;
            patrolRoot.SetParent(transform, worldPositionStays: false);
            patrolRoot.localPosition = Vector3.zero;

            for (int route = 0; route < recipe.patrolRoutes; route++)
            {
                // Route 0 walks the edge; a second route loops through the middle of the town.
                float radius = route == 0 ? recipe.layout.outerRadius : recipe.layout.coreRadius;
                float phase = route * Mathf.PI / WaypointsPerRoute;

                Transform routeRoot = new GameObject($"Route{route}").transform;
                routeRoot.SetParent(patrolRoot, worldPositionStays: false);
                routeRoot.localPosition = Vector3.zero;

                for (int i = 0; i < WaypointsPerRoute; i++)
                {
                    float angle = phase + i * Mathf.PI * 2f / WaypointsPerRoute;
                    float x = Mathf.Cos(angle) * radius;
                    float z = Mathf.Sin(angle) * radius;
                    float y = NomadPlacementGeometry.TerrainHeightAt(origin.x + x, origin.z + z) ?? origin.y;

                    var waypoint = new GameObject($"Waypoint{i}");
                    waypoint.transform.SetParent(routeRoot, worldPositionStays: false);
                    waypoint.transform.position = new Vector3(origin.x + x, y, origin.z + z);
                }
            }

            return patrolRoot;
        }

        // ── people ───────────────────────────────────────────────────────────────

        /// <summary>
        /// The town's starting population, placed once at build time.
        ///
        /// NOT left to SettlementPopulation to grow into. At three a wave every sixty seconds a
        /// twenty-person town takes seven minutes to fill, so a player walking in on a fresh world
        /// would find an empty village. The waves exist to replace losses, not to build the town.
        /// </summary>
        private void Populate(Transform root, Transform patrolRoot, Vector3 origin)
        {
            if (inhabitantPrefabs == null || inhabitantPrefabs.Length == 0) return;

            var random = new System.Random(seed ^ 0x5EED);
            int index = 0;

            // Residents: wander a radius centred on this settlement.
            for (int i = 0; i < recipe.residents; i++)
            {
                GameObject person = PlacePerson(root, origin, random, recipe.layout.outerRadius, ref index);
                if (person == null) continue;

                if (person.TryGetComponent(out PatrolModule patrol))
                    WireRadiusPatrol(patrol, transform, recipe.layout.outerRadius);
            }

            // Flocks: one herd each, moving as a group around their own anchor.
            for (int flock = 0; flock < recipe.flocks; flock++)
            {
                Vector2 offset = PointInCircle(random, recipe.layout.midRadius);
                float anchorY = NomadPlacementGeometry.TerrainHeightAt(origin.x + offset.x, origin.z + offset.y) ?? origin.y;

                var anchor = new GameObject($"FlockAnchor{flock}").transform;
                anchor.SetParent(root, worldPositionStays: false);
                anchor.position = new Vector3(origin.x + offset.x, anchorY, origin.z + offset.y);

                string herdId = $"{herdPrefix}_flock{flock}";

                for (int i = 0; i < recipe.flockSize; i++)
                {
                    GameObject person = PlacePerson(root, origin, random, recipe.layout.midRadius, ref index);
                    if (person == null) continue;

                    // The resident behaviour is switched off rather than removed: the component is the
                    // prefab's, and stripping it from an instance is an override that a prefab rebuild
                    // would have to reconcile.
                    if (person.TryGetComponent(out PatrolModule patrol)) patrol.enabled = false;

                    WireFlock(Add<BasePatrolModule>(person), anchor, recipe.layout.outerRadius);
                    WireHerd(Add<HerdModule>(person), herdId);
                }
            }

            // Patrols: walk a fixed route round the town.
            for (int route = 0; route < recipe.patrolRoutes && patrolRoot != null; route++)
            {
                Transform routeRoot = patrolRoot.Find($"Route{route}");
                if (routeRoot == null) continue;

                var waypoints = new Transform[routeRoot.childCount];
                for (int i = 0; i < routeRoot.childCount; i++) waypoints[i] = routeRoot.GetChild(i);

                for (int i = 0; i < recipe.patrolSize; i++)
                {
                    GameObject person = PlacePerson(root, origin, random, recipe.layout.outerRadius, ref index);
                    if (person == null) continue;

                    if (person.TryGetComponent(out PatrolModule patrol))
                        WireRoutePatrol(patrol, waypoints);
                }
            }

            // Mounted, last: drawing their numbers after everything else keeps an existing seed's town
            // exactly as it was when the count changes.
            for (int i = 0; i < recipe.mounted && mountedPrefab != null; i++)
            {
                Vector2 offset = PointInCircle(random, recipe.layout.outerRadius);
                float y = NomadPlacementGeometry.TerrainHeightAt(origin.x + offset.x, origin.z + offset.y) ?? origin.y;
                Place(mountedPrefab,
                      new Vector3(origin.x + offset.x, y, origin.z + offset.y),
                      Quaternion.Euler(0f, (float)(random.NextDouble() * 360.0), 0f),
                      root);
            }
        }

        private GameObject PlacePerson(Transform root, Vector3 origin, System.Random random,
                                       float radius, ref int index)
        {
            GameObject prefab = inhabitantPrefabs[index++ % inhabitantPrefabs.Length];
            if (prefab == null) return null;

            Vector2 offset = PointInCircle(random, radius);
            float y = NomadPlacementGeometry.TerrainHeightAt(origin.x + offset.x, origin.z + offset.y) ?? origin.y;

            return Place(prefab,
                         new Vector3(origin.x + offset.x, y, origin.z + offset.y),
                         Quaternion.Euler(0f, (float)(random.NextDouble() * 360.0), 0f),
                         root);
        }

        private static T Add<T>(GameObject target) where T : Component
        {
            T existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        private static Vector2 PointInCircle(System.Random random, float radius)
        {
            float r = radius * Mathf.Sqrt((float)random.NextDouble());
            float a = (float)(random.NextDouble() * Mathf.PI * 2f);
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }

        // ── wiring the frozen components ─────────────────────────────────────────
        //
        // Field names are read off the component sources. A name that does not exist is a no-op, not an
        // error, so every one of these is read back by NomadSettlementPlacer.Verify.

        private static void WireRadiusPatrol(PatrolModule patrol, Transform centre, float radius)
        {
#if UNITY_EDITOR
            var so = new SerializedObject(patrol);
            so.FindProperty("mode").enumValueIndex = 0;                 // PatrolMode.RadiusBased
            so.FindProperty("radiusCenter").objectReferenceValue = centre;
            so.FindProperty("patrolRadius").floatValue = radius;
            so.FindProperty("priority").intValue = 0;                   // ModulePriority.Fallback
            so.ApplyModifiedPropertiesWithoutUndo();
#endif
        }

        private static void WireRoutePatrol(PatrolModule patrol, Transform[] waypoints)
        {
#if UNITY_EDITOR
            var so = new SerializedObject(patrol);
            so.FindProperty("mode").enumValueIndex = 1;                 // PatrolMode.PatrolPoints
            so.FindProperty("selectionMode").enumValueIndex = 0;        // PatrolSelectionMode.SequentialLoop
            so.FindProperty("priority").intValue = 0;

            SerializedProperty points = so.FindProperty("patrolPoints");
            points.arraySize = waypoints.Length;
            for (int i = 0; i < waypoints.Length; i++)
                points.GetArrayElementAtIndex(i).objectReferenceValue = waypoints[i];

            so.ApplyModifiedPropertiesWithoutUndo();
#endif
        }

        private static void WireFlock(BasePatrolModule roam, Transform anchor, float radius)
        {
#if UNITY_EDITOR
            var so = new SerializedObject(roam);
            so.FindProperty("baseTransform").objectReferenceValue = anchor;
            so.FindProperty("patrolRadius").floatValue = radius;
            so.FindProperty("priority").intValue = 0;                   // ModulePriority.Fallback
            so.ApplyModifiedPropertiesWithoutUndo();
#endif
        }

        private static void WireHerd(HerdModule herd, string herdId)
        {
#if UNITY_EDITOR
            var so = new SerializedObject(herd);
            so.FindProperty("herdId").stringValue = herdId;
            so.FindProperty("priority").intValue = 15;                  // ModulePriority.Social
            so.ApplyModifiedPropertiesWithoutUndo();
#endif
        }

        /// <summary>
        /// The alarm and the population upkeep, both on the settlement root.
        ///
        /// SettlementPopulation's own header explains why it decides on the server rather than on
        /// Network.Simulates: a settlement root is chunk scenery with no NetworkObject, and Simulates
        /// would be true on every client. Nothing here needs to do anything about that; it only has to
        /// not break it.
        /// </summary>
        private void WireUpkeep()
        {
#if UNITY_EDITOR
            var alarmSo = new SerializedObject(Add<SettlementAlarm>(gameObject));
            alarmSo.FindProperty("owner").objectReferenceValue = ownerFaction;
            alarmSo.FindProperty("relationshipTable").objectReferenceValue = relationships;
            alarmSo.FindProperty("radius").floatValue = recipe.countRadius;
            alarmSo.ApplyModifiedPropertiesWithoutUndo();

            var populationSo = new SerializedObject(Add<SettlementPopulation>(gameObject));
            populationSo.FindProperty("owner").objectReferenceValue = ownerFaction;
            populationSo.FindProperty("relationshipTable").objectReferenceValue = relationships;
            populationSo.FindProperty("countRadius").floatValue = recipe.countRadius;
            populationSo.FindProperty("maxPopulation").intValue = recipe.populationCap;
            populationSo.FindProperty("spawnInterval").floatValue = recipe.spawnInterval;
            populationSo.FindProperty("spawnsPerWave").intValue = recipe.spawnsPerWave;
            populationSo.FindProperty("innerRadius").floatValue = recipe.spawnRingInner;
            populationSo.FindProperty("outerRadius").floatValue = recipe.spawnRingOuter;

            // Refills are residents: SettlementPopulation spawns a prefab and knows nothing about
            // flocks or routes, so a flock that loses members stays short-handed. Deliberate.
            var weighted = new List<(GameObject prefab, int weight)>();
            foreach (GameObject prefab in inhabitantPrefabs)
                if (prefab != null) weighted.Add((prefab, 3));
            if (mountedPrefab != null) weighted.Add((mountedPrefab, 1));

            SerializedProperty inhabitants = populationSo.FindProperty("inhabitants");
            inhabitants.arraySize = weighted.Count;
            for (int i = 0; i < weighted.Count; i++)
            {
                SerializedProperty entry = inhabitants.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("prefab").objectReferenceValue = weighted[i].prefab;
                entry.FindPropertyRelative("weight").intValue = weighted[i].weight;
            }

            populationSo.ApplyModifiedPropertiesWithoutUndo();
#endif
        }

        /// <summary>
        /// Makes the settlement somewhere an NPC can be sent.
        ///
        /// The marker's id is NOT set here. It is derived from the scene and hierarchy path by the
        /// marker's own OnValidate, which ApplyModifiedProperties triggers, and a saved NPC names that
        /// id as the place it was walking to. Minting a fresh one would orphan every saved reference.
        /// </summary>
        private void WireSite()
        {
#if UNITY_EDITOR
            var so = new SerializedObject(Add<WorldSiteMarker>(gameObject));
            so.FindProperty("kind").enumValueIndex = (int)recipe.siteKind;
            so.FindProperty("siteName").stringValue = siteName;
            so.FindProperty("radius").floatValue = recipe.layout.outerRadius;
            so.ApplyModifiedPropertiesWithoutUndo();
#endif
        }

        private void OnDrawGizmosSelected()
        {
            if (recipe == null) return;

            Gizmos.color = new Color(0.95f, 0.75f, 0.3f, 0.5f);
            DrawCircle(recipe.layout.coreRadius);
            Gizmos.color = new Color(0.9f, 0.55f, 0.25f, 0.4f);
            DrawCircle(recipe.layout.outerRadius);
            Gizmos.color = new Color(0.8f, 0.8f, 0.8f, 0.25f);
            DrawCircle(recipe.layout.tentRadius);
        }

        private void DrawCircle(float radius, int segments = 48)
        {
            Vector3 centre = transform.position;
            Vector3 previous = centre + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments * Mathf.PI * 2f;
                Vector3 next = centre + new Vector3(Mathf.Cos(t) * radius, 0f, Mathf.Sin(t) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
```

- [ ] **Step 2: Verify the serialized field names exist**

Every `FindProperty` above is a silent no-op if the name is wrong. Confirm all of them before trusting the wiring:

```bash
grep -n 'mode;\|radiusCenter\|patrolRadius\|patrolPoints\|selectionMode\|minMoveDistance' Assets/Game/Scripts/agents/Modules/Patrol/PatrolModule.cs
grep -n 'baseTransform\|patrolRadius' Assets/Game/Scripts/agents/Modules/Patrol/BasePatrolModule.cs
grep -n 'herdId' Assets/Game/Scripts/agents/Modules/Flocking/HerdModule.cs
grep -n 'owner;\|relationshipTable\|countRadius\|inhabitants\|maxPopulation\|spawnInterval\|spawnsPerWave\|innerRadius\|outerRadius' Assets/Game/Scripts/agents/Faction/SettlementPopulation.cs
grep -n 'owner\|relationshipTable\|radius' Assets/Game/Scripts/agents/Faction/SettlementAlarm.cs
grep -n 'kind\|siteName\|radius' Assets/Game/Scripts/World/Sites/WorldSiteMarker.cs
grep -n 'private int priority' Assets/Game/Scripts/agents/Modules/Core/BehaviourModuleBase.cs
grep -n 'public GameObject prefab\|public int weight' Assets/Game/Scripts/agents/Faction/SettlementPopulation.cs
```
Expected: every name appears as a `[SerializeField]` (or a public field, for the `Inhabitant` members). Fix any mismatch here rather than discovering it as a settlement full of nomads standing still.

- [ ] **Step 3: Verify it compiles**

Run:
```bash
python3 tools/typecheck.py --editor
```
Expected: exits 0.

- [ ] **Step 4: Confirm nothing frozen was touched**

Run:
```bash
git status --porcelain
```
Expected: only files under `NomadSettlement/`. Nothing under `Settlement/`, `Modules/`, `Faction/` or `Sites/`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement
git commit -m "feat(settlement): nomad settlement generator bound to one root"
```

---

## Task 6: The placer — nine sites, one command

Reuses `SettlementSiteScore` and the chunk-opening pattern from `ClankerSettlementBuilder`. Read that file's `TryChooseSite`, `OpenChunksAround`, `FeatureKeepOutBounds` and `PlaceGenerator` before writing this — this is the same shape with nine sites instead of one. `SettlementSiteScore` is a public static type in the same editor assembly, so it is used, not copied.

**Files:**
- Create: `Assets/Game/Editor/Environment/NomadSettlementPlacer.cs`

- [ ] **Step 1: Write the placer**

Create `Assets/Game/Editor/Environment/NomadSettlementPlacer.cs`:

```csharp
// Places the nine nomad settlements: two large, three medium, four small, across the main world.
//
// A builder rather than nine generators dropped into scenes by hand, for the reason
// ClankerSettlementBuilder records at length: a hand-placed generator sits on ground nobody can click
// again, and its layout depends on a sequence nobody wrote down. This command chooses every site from
// constants and a fixed seed, so running it twice gives the same nine towns.
//
// Site selection. Chunk terrain is binary on disk and can only be read with the chunk scenes open, so
// the placer opens every chunk with terrain, scores a grid of candidate centres by the height range
// across each tier's disc (SettlementSiteScore -- pure, tested, reused from the Clanker builder), and
// takes the flattest that clears everything it must keep away from: the spawn point, the Clanker
// settlement, every terrain feature's footprint, the edge of its own chunk, and every site already chosen.
//
// Two constraints that are not obvious:
//
//   * A settlement must not straddle a chunk boundary. Everything it makes lives in ONE chunk scene, so
//     a town whose far side reaches into the neighbouring chunk would have half its buildings appear
//     and vanish with a chunk the player is nowhere near.
//   * Two settlements must be far enough apart that the streaming window rarely holds both. Chunks are
//     500 m with loadRadius 1, so the window is 1500 m across; 800 m between towns keeps the common
//     case to one town live at a time, and also keeps each one a separate landmark
//     (GDC-L1-LEVEL-0002) rather than one continuous smear of adobe.
//
// Re-run from: Tools > SpaceGame > Settlements > Build Nomad Settlements
//              Tools > SpaceGame > Settlements > Build Nomad Settlements + Bake NavMesh
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.World;
using SpaceGame.World.NavMeshTools;

namespace SpaceGame.EditorTools
{
    public static class NomadSettlementPlacer
    {
        private const string RecipeDir = "Assets/Game/ScriptableObjects/Settlements";
        private const string BuildingDir = "Assets/Game/Prefabs/Environment/Structures/NomadSettlement";
        private const string CharacterDir = "Assets/Game/Prefabs/agents/Characters";
        private const string MountedPrefab = "Assets/Game/Prefabs/agents/Caravan/NomadOstrich.prefab";
        private const string OwnerFactionPath = "Assets/Game/ScriptableObjects/Factions/Core/NPCFaction.asset";
        private const string RelationshipsPath = "Assets/Game/ScriptableObjects/Factions/Core/GlobalRelationships.asset";

        /// <summary>Changing this moves every settlement. It is the whole world's layout in one number.</summary>
        private const int BaseSeed = 20260913;

        private const float CandidateStep = 50f;
        private const float FeatureMargin = 60f;
        private const float SpawnKeepOut = 300f;
        private const float ClankerKeepOut = 250f;

        /// <summary>Metres between any two settlements. See the header for why 800.</summary>
        private const float MinimumSeparation = 800f;

        /// <summary>The freestanding sails. The wall ones are already hung on the buildings.</summary>
        private static readonly string[] FreestandingTents =
        {
            "QuadSmall", "QuadLarge", "Tri", "TriTall", "Penta", "HexLow", "Ribbon", "Kite",
        };

        private static readonly string[] InhabitantPrefabs =
        {
            CharacterDir + "/Nomad_Tan.prefab",
            CharacterDir + "/Nomad_Umber.prefab",
            CharacterDir + "/Nomad_Maroon.prefab",
            CharacterDir + "/Nomad_StrawHat.prefab",
        };

        /// <summary>The towns, in the order they claim ground. Largest first: they need the most of it.</summary>
        private static readonly (string Tier, string Name)[] Settlements =
        {
            ("Large", "Ras Tamir"),
            ("Large", "Ghaf Wells"),
            ("Medium", "Sabkha"),
            ("Medium", "Khor Dune"),
            ("Medium", "Al Marrah"),
            ("Small", "Dry Stand"),
            ("Small", "Two Poles"),
            ("Small", "Latch Camp"),
            ("Small", "Windward"),
        };

        [MenuItem("Tools/SpaceGame/Settlements/Build Nomad Settlements")]
        public static void BuildMenu() => Build(bakeNavMesh: false);

        [MenuItem("Tools/SpaceGame/Settlements/Build Nomad Settlements + Bake NavMesh")]
        public static void BuildAndBakeMenu() => Build(bakeNavMesh: true);

        [MenuItem("Tools/SpaceGame/Settlements/Build Nomad Settlements", validate = true)]
        [MenuItem("Tools/SpaceGame/Settlements/Build Nomad Settlements + Bake NavMesh", validate = true)]
        private static bool CanBuild() => !EditorApplication.isPlaying;

        public static void Build(bool bakeNavMesh)
        {
            Dictionary<string, NomadSettlementRecipe> recipes = WriteRecipes();
            if (recipes == null) return;

            WorldStreamingConfig config = WorldNavMeshBaker.LoadConfig();
            if (config == null) return;

            if (!ClankerSettlementBuilder.TryFindSpawnPoint(out Vector3 spawn)) return;

            var opened = new List<Scene>();
            var report = new System.Text.StringBuilder("NomadSettlementPlacer\n");
            bool everythingPlaced = true;

            try
            {
                OpenTerrainChunks(config, opened);
                Physics.SyncTransforms();

                List<Bounds> keepOut = BuildKeepOut(spawn);
                var chosen = new List<Vector3>();

                for (int index = 0; index < Settlements.Length; index++)
                {
                    (string tier, string name) = Settlements[index];
                    NomadSettlementRecipe recipe = recipes[tier];

                    if (!TryChooseSite(config, recipe.siteRadius, keepOut, chosen,
                                       out Vector3 centre, out float heightRange))
                    {
                        Debug.LogError($"[NomadSettlementPlacer] No level ground of radius {recipe.siteRadius} m " +
                                       $"left for '{name}' ({tier}) at {MinimumSeparation} m from every other " +
                                       "settlement. Widen maxGroundRange, shrink the tier, or place fewer towns.");
                        everythingPlaced = false;
                        break;
                    }

                    chosen.Add(centre);

                    ChunkInfo? chunk = config.GetChunk(config.WorldToChunkCoord(centre));
                    if (chunk == null || string.IsNullOrEmpty(chunk.Value.scenePath))
                    {
                        Debug.LogError($"[NomadSettlementPlacer] {centre} is not inside any chunk scene.");
                        everythingPlaced = false;
                        break;
                    }

                    Scene target = SceneManager.GetSceneByPath(chunk.Value.scenePath);
                    if (!target.IsValid() || !target.isLoaded)
                    {
                        Debug.LogError($"[NomadSettlementPlacer] {chunk.Value.sceneName} is not open; the site " +
                                       "search should have opened it.");
                        everythingPlaced = false;
                        break;
                    }

                    NomadSettlementGenerator generator = PlaceGenerator(target, recipe, centre, index, name);
                    generator.Generate();

                    if (!Verify(generator, recipe, out string verdict))
                    {
                        Debug.LogError($"[NomadSettlementPlacer] '{name}': {verdict}", generator);
                        everythingPlaced = false;
                        break;
                    }

                    EditorSceneManager.MarkSceneDirty(target);
                    if (!EditorSceneManager.SaveScene(target))
                    {
                        Debug.LogError($"[NomadSettlementPlacer] Could not save {chunk.Value.sceneName}.");
                        everythingPlaced = false;
                        break;
                    }

                    report.AppendLine($"  {name} ({tier}) in {chunk.Value.sceneName} at " +
                                      $"{centre}, site range {heightRange:F2} m. {verdict}");
                }
            }
            finally
            {
                foreach (Scene scene in opened)
                    if (scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, removeScene: true);
            }

            Debug.Log(report.ToString());
            if (!everythingPlaced) return;

            if (!bakeNavMesh)
            {
                Debug.LogWarning("[NomadSettlementPlacer] The world NavMesh is now stale: run " +
                                 "World > Streaming > Bake World NavMesh before playing, or every " +
                                 "nomad stands still and the build check fails.");
                return;
            }

            Debug.Log(WorldNavMeshBaker.Bake(WorldNavMeshBaker.LoadConfig()));
        }

        // ── recipes ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates or updates the three tier assets in place. Updating rather than recreating keeps
        /// their GUIDs, which the placed generators reference.
        /// </summary>
        private static Dictionary<string, NomadSettlementRecipe> WriteRecipes()
        {
            bool ok = true;

            GameObject[] large = LoadFolder(BuildingDir + "/Large", ref ok);
            GameObject[] medium = LoadFolder(BuildingDir + "/Medium", ref ok);
            GameObject[] small = LoadFolder(BuildingDir + "/Small", ref ok);
            GameObject[] tents = FreestandingTents
                .Select(n => LoadRequired($"{BuildingDir}/Tents/NomadSail_{n}.prefab", ref ok))
                .ToArray();

            if (!ok)
            {
                Debug.LogError("[NomadSettlementPlacer] A nomad prefab is missing (see above). Run " +
                               "Tools > Environment > Build Nomad Settlement Prefabs first.");
                return null;
            }

            var result = new Dictionary<string, NomadSettlementRecipe>();

            result["Large"] = WriteRecipe("Large", large, medium, small, tents, recipe =>
            {
                recipe.layout.largeCount = new Vector2Int(6, 8);
                recipe.layout.mediumCount = new Vector2Int(10, 12);
                recipe.layout.smallCount = new Vector2Int(6, 8);
                recipe.layout.tentCount = new Vector2Int(12, 12);
                recipe.layout.coreRadius = 18f;
                recipe.layout.midRadius = 34f;
                recipe.layout.outerRadius = 52f;
                recipe.layout.tentRadius = 70f;
                recipe.siteRadius = 80f;
                recipe.residents = 12;
                recipe.flocks = 3;
                recipe.flockSize = 4;
                recipe.patrolRoutes = 2;
                recipe.patrolSize = 3;
                recipe.mounted = 2;
                recipe.populationCap = 38;
                recipe.spawnInterval = 45f;
                recipe.spawnsPerWave = 4;
                recipe.countRadius = 110f;
                recipe.spawnRingInner = 25f;
                recipe.spawnRingOuter = 75f;
                recipe.siteKind = SiteKind.Home;
            });

            result["Medium"] = WriteRecipe("Medium", large, medium, small, tents, recipe =>
            {
                recipe.layout.largeCount = new Vector2Int(2, 3);
                recipe.layout.mediumCount = new Vector2Int(5, 6);
                recipe.layout.smallCount = new Vector2Int(4, 5);
                recipe.layout.tentCount = new Vector2Int(7, 7);
                recipe.layout.coreRadius = 12f;
                recipe.layout.midRadius = 24f;
                recipe.layout.outerRadius = 38f;
                recipe.layout.tentRadius = 52f;
                recipe.siteRadius = 55f;
                recipe.residents = 7;
                recipe.flocks = 2;
                recipe.flockSize = 3;
                recipe.patrolRoutes = 1;
                recipe.patrolSize = 3;
                recipe.mounted = 1;
                recipe.populationCap = 20;
                recipe.spawnInterval = 60f;
                recipe.spawnsPerWave = 3;
                recipe.countRadius = 80f;
                recipe.spawnRingInner = 18f;
                recipe.spawnRingOuter = 50f;
                recipe.siteKind = SiteKind.Home;
            });

            result["Small"] = WriteRecipe("Small", large, medium, small, tents, recipe =>
            {
                recipe.layout.largeCount = new Vector2Int(0, 1);
                recipe.layout.mediumCount = new Vector2Int(2, 3);
                recipe.layout.smallCount = new Vector2Int(3, 4);
                recipe.layout.tentCount = new Vector2Int(4, 4);
                recipe.layout.coreRadius = 8f;
                recipe.layout.midRadius = 16f;
                recipe.layout.outerRadius = 26f;
                recipe.layout.tentRadius = 36f;
                recipe.siteRadius = 40f;
                recipe.residents = 4;
                recipe.flocks = 1;
                recipe.flockSize = 3;
                recipe.patrolRoutes = 0;
                recipe.patrolSize = 0;
                recipe.mounted = 0;
                recipe.populationCap = 9;
                recipe.spawnInterval = 75f;
                recipe.spawnsPerWave = 2;
                recipe.countRadius = 60f;
                recipe.spawnRingInner = 12f;
                recipe.spawnRingOuter = 34f;
                recipe.siteKind = SiteKind.Camp;
            });

            AssetDatabase.SaveAssets();

            // Read the writes back: a read-only AssetDatabase discards saves silently.
            foreach (string tier in result.Keys.ToList())
            {
                var reloaded = AssetDatabase.LoadAssetAtPath<NomadSettlementRecipe>($"{RecipeDir}/NomadSettlement_{tier}.asset");
                if (reloaded == null || reloaded.largeBuildings == null || reloaded.largeBuildings.Length == 0)
                {
                    Debug.LogError($"[NomadSettlementPlacer] {RecipeDir}/NomadSettlement_{tier}.asset did not save.");
                    return null;
                }
                result[tier] = reloaded;
            }

            return result;
        }

        private static NomadSettlementRecipe WriteRecipe(string tier, GameObject[] large, GameObject[] medium,
                                                          GameObject[] small, GameObject[] tents,
                                                          Action<NomadSettlementRecipe> tune)
        {
            string path = $"{RecipeDir}/NomadSettlement_{tier}.asset";
            var recipe = AssetDatabase.LoadAssetAtPath<NomadSettlementRecipe>(path);
            if (recipe == null)
            {
                recipe = ScriptableObject.CreateInstance<NomadSettlementRecipe>();
                AssetDatabase.CreateAsset(recipe, path);
            }

            recipe.largeBuildings = large;
            recipe.mediumBuildings = medium;
            recipe.smallBuildings = small;
            recipe.tents = tents;
            recipe.layout.minStructureSpacing = 6f;
            recipe.layout.buildingPadding = 2f;
            recipe.layout.maxGroundRange = 0.45f;
            recipe.layout.placementAttempts = 40;
            recipe.layout.footprintSamples = 3;
            tune(recipe);

            EditorUtility.SetDirty(recipe);
            return recipe;
        }

        private static GameObject[] LoadFolder(string folder, ref bool ok)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            if (guids.Length == 0)
            {
                Debug.LogError($"[NomadSettlementPlacer] No prefabs under {folder}.");
                ok = false;
                return Array.Empty<GameObject>();
            }

            return guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p, StringComparer.Ordinal)   // stable order, or the seed means nothing
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(p => p != null)
                .ToArray();
        }

        private static GameObject LoadRequired(string path, ref bool ok)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"[NomadSettlementPlacer] Missing prefab: {path}");
                ok = false;
            }
            return prefab;
        }

        // ── sites ────────────────────────────────────────────────────────────────

        private static void OpenTerrainChunks(WorldStreamingConfig config, List<Scene> opened)
        {
            foreach (ChunkInfo chunk in config.chunks)
            {
                if (!chunk.hasTerrain || string.IsNullOrEmpty(chunk.scenePath)) continue;

                Scene scene = SceneManager.GetSceneByPath(chunk.scenePath);
                if (scene.IsValid() && scene.isLoaded) continue;
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(chunk.scenePath) == null) continue;

                opened.Add(EditorSceneManager.OpenScene(chunk.scenePath, OpenSceneMode.Additive));
            }
        }

        private static List<Bounds> BuildKeepOut(Vector3 spawn)
        {
            var keepOut = new List<Bounds>
            {
                new Bounds(spawn, new Vector3(SpawnKeepOut * 2f, 10000f, SpawnKeepOut * 2f)),
            };

            // The Clanker town, wherever its own site search put it. If it has not been built yet there
            // is simply nothing to avoid.
            foreach (RobotSettlementGenerator robot in UnityEngine.Object.FindObjectsByType<RobotSettlementGenerator>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                keepOut.Add(new Bounds(robot.transform.position,
                                       new Vector3(ClankerKeepOut * 2f, 10000f, ClankerKeepOut * 2f)));
            }

            // Terrain features. Their meshes are NOT in the scene -- they are spawned from a baked asset
            // -- so the authored footprint is the only thing that says where a mesa will be.
            foreach (TerrainFeatureSpawner spawner in UnityEngine.Object.FindObjectsByType<TerrainFeatureSpawner>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Bounds local = spawner.Area.ComputeLocalBounds();
                Transform t = spawner.transform;
                Vector3 centre = t.TransformPoint(local.center);
                Vector3 size = Vector3.Scale(local.size, t.lossyScale);
                float diagonal = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z)) * 1.42f;
                var world = new Bounds(centre, new Vector3(diagonal, 10000f, diagonal));
                world.Expand(FeatureMargin * 2f);
                keepOut.Add(world);
            }

            return keepOut;
        }

        private static bool TryChooseSite(WorldStreamingConfig config, float siteRadius,
                                          List<Bounds> keepOut, List<Vector3> alreadyChosen,
                                          out Vector3 centre, out float bestRange)
        {
            float? HeightAt(float x, float z) => NomadPlacementGeometry.TerrainHeightAt(x, z);

            centre = default;
            bestRange = float.MaxValue;
            bool found = false;

            Bounds world = config.chunks[0].worldBounds;
            foreach (ChunkInfo chunk in config.chunks) world.Encapsulate(chunk.worldBounds);

            for (float x = world.min.x; x <= world.max.x; x += CandidateStep)
            for (float z = world.min.z; z <= world.max.z; z += CandidateStep)
            {
                var flat = new Vector2(x, z);

                if (!InsideOneChunk(config, flat, siteRadius)) continue;
                if (TooCloseToChosen(flat, alreadyChosen)) continue;
                if (DiscTouches(keepOut, flat, siteRadius)) continue;

                if (!SettlementSiteScore.TryEvaluate(HeightAt, flat, siteRadius,
                                                     out float range, out float centreHeight)) continue;

                if (range >= bestRange) continue;
                bestRange = range;
                centre = new Vector3(x, centreHeight, z);
                found = true;
            }

            return found;
        }

        /// <summary>
        /// True when the whole site disc lies inside ONE chunk. Everything a settlement makes goes into a
        /// single chunk scene, so a town reaching into its neighbour would have half its buildings stream
        /// in and out with a chunk nobody is near.
        /// </summary>
        private static bool InsideOneChunk(WorldStreamingConfig config, Vector2 flat, float siteRadius)
        {
            var centre = new Vector3(flat.x, 0f, flat.y);
            ChunkInfo? owner = config.GetChunk(config.WorldToChunkCoord(centre));
            if (owner == null || !owner.Value.hasTerrain) return false;

            Bounds bounds = owner.Value.worldBounds;
            return flat.x - siteRadius >= bounds.min.x && flat.x + siteRadius <= bounds.max.x
                && flat.y - siteRadius >= bounds.min.z && flat.y + siteRadius <= bounds.max.z;
        }

        private static bool TooCloseToChosen(Vector2 flat, List<Vector3> chosen)
        {
            foreach (Vector3 other in chosen)
            {
                float dx = other.x - flat.x;
                float dz = other.z - flat.y;
                if (dx * dx + dz * dz < MinimumSeparation * MinimumSeparation) return true;
            }
            return false;
        }

        private static bool DiscTouches(List<Bounds> keepOut, Vector2 flat, float siteRadius)
        {
            var disc = new Bounds(new Vector3(flat.x, 0f, flat.y),
                                  new Vector3(siteRadius * 2f, 10000f, siteRadius * 2f));
            foreach (Bounds bounds in keepOut)
                if (bounds.Intersects(disc)) return true;
            return false;
        }

        // ── scene ────────────────────────────────────────────────────────────────

        /// <summary>
        /// The root name is part of the save-id path of every person under it. Do not rename it once a
        /// world has been saved: their ids derive from scene + hierarchy path, and a rename orphans the lot.
        /// </summary>
        private static string RootName(int index, string name) =>
            $"NomadSettlement_{index:00}_{name.Replace(" ", string.Empty)}";

        private static NomadSettlementGenerator PlaceGenerator(Scene scene, NomadSettlementRecipe recipe,
                                                                Vector3 centre, int index, string name)
        {
            string rootName = RootName(index, name);
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == rootName);
            if (root == null)
            {
                root = new GameObject(rootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            root.transform.SetPositionAndRotation(centre, Quaternion.identity);

            if (!root.TryGetComponent(out NomadSettlementGenerator generator))
                generator = root.AddComponent<NomadSettlementGenerator>();

            bool ok = true;
            generator.recipe = recipe;
            generator.seed = BaseSeed + index;
            generator.ownerFaction = AssetDatabase.LoadAssetAtPath<FactionDefinition>(OwnerFactionPath);
            generator.relationships = AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(RelationshipsPath);
            generator.inhabitantPrefabs = InhabitantPrefabs.Select(p => LoadRequired(p, ref ok)).ToArray();
            generator.mountedPrefab = LoadRequired(MountedPrefab, ref ok);
            generator.herdPrefix = $"nomad_s{index:00}";
            generator.siteName = name;

            EditorUtility.SetDirty(generator);
            return generator;
        }

        /// <summary>
        /// Reads the built town back. The counts catch a site too rough to build on; the module checks
        /// catch the real hazard of wiring frozen components through SerializedObject -- a field name
        /// that does not exist writes nothing and reports nothing, and the settlement would look right
        /// in the Scene view while every nomad in it stood still.
        /// </summary>
        private static bool Verify(NomadSettlementGenerator generator, NomadSettlementRecipe recipe,
                                    out string report)
        {
            Transform generated = generator.transform.Find(NomadSettlementGenerator.GeneratedRootName);
            if (generated == null)
            {
                report = "Generate() produced no 'Generated' child.";
                return false;
            }

            int structures = 0, people = 0;
            foreach (Transform child in generated)
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                string path = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;

                if (path.StartsWith(BuildingDir, StringComparison.Ordinal)) structures++;
                else if (path.StartsWith(CharacterDir, StringComparison.Ordinal) || path == MountedPrefab) people++;
            }

            int wantedStructures = recipe.layout.largeCount.x + recipe.layout.mediumCount.x
                                 + recipe.layout.smallCount.x + recipe.layout.tentCount.x;
            int wantedPeople = recipe.residents + recipe.flocks * recipe.flockSize
                             + recipe.patrolRoutes * recipe.patrolSize + recipe.mounted;

            report = $"{structures} structures, {people} people.";

            if (structures < wantedStructures)
            {
                report = $"only {structures} of at least {wantedStructures} structures were placed -- " +
                         "is the chunk's terrain open, and is the ground flat enough? " + report;
                return false;
            }

            if (people < wantedPeople)
            {
                report = $"only {people} of {wantedPeople} people were placed. " + report;
                return false;
            }

            // The wiring actually landed.
            int residents = 0, patrollers = 0, flockers = 0;
            foreach (PatrolModule patrol in generated.GetComponentsInChildren<PatrolModule>(true))
            {
                if (!patrol.enabled) continue;
                var so = new SerializedObject(patrol);
                if (so.FindProperty("mode").enumValueIndex == 0
                    && so.FindProperty("radiusCenter").objectReferenceValue == generator.transform) residents++;
                else if (so.FindProperty("patrolPoints").arraySize > 0) patrollers++;
            }

            foreach (HerdModule herd in generated.GetComponentsInChildren<HerdModule>(true))
            {
                var so = new SerializedObject(herd);
                if (so.FindProperty("herdId").stringValue.StartsWith(generator.herdPrefix, StringComparison.Ordinal))
                    flockers++;
            }

            report += $" {residents} residents, {patrollers} patrollers, {flockers} flockers.";

            if (residents < recipe.residents)
            {
                report = "the resident wiring did not land -- check the PatrolModule field names. " + report;
                return false;
            }

            if (flockers < recipe.flocks * recipe.flockSize)
            {
                report = "the flock wiring did not land -- check the HerdModule field names. " + report;
                return false;
            }

            if (patrollers < recipe.patrolRoutes * recipe.patrolSize)
            {
                report = "the patrol wiring did not land -- check the PatrolModule field names. " + report;
                return false;
            }

            var population = generator.GetComponent<SettlementPopulation>();
            if (population == null)
            {
                report = "no SettlementPopulation on the root. " + report;
                return false;
            }

            var populationSo = new SerializedObject(population);
            if (populationSo.FindProperty("inhabitants").arraySize == 0
                || populationSo.FindProperty("maxPopulation").intValue != recipe.populationCap)
            {
                report = "the SettlementPopulation wiring did not land -- check its field names. " + report;
                return false;
            }

            // Overlap. The solver guarantees it, but the solver is given footprints measured here, and a
            // prefab whose meshes changed since would silently shrink its own clearance.
            var seen = new List<(Vector3 position, float radius)>();
            foreach (Transform child in generated)
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                if (source == null) continue;
                if (!AssetDatabase.GetAssetPath(source).StartsWith(BuildingDir, StringComparison.Ordinal)) continue;

                float radius = NomadPlacementGeometry.ClearanceRadius(
                    NomadPlacementGeometry.MeasureFootprint(source),
                    recipe.layout.buildingPadding, recipe.layout.minStructureSpacing);

                foreach ((Vector3 position, float otherRadius) in seen)
                {
                    float dx = position.x - child.position.x;
                    float dz = position.z - child.position.z;
                    float minimum = radius + otherRadius;
                    if (dx * dx + dz * dz < minimum * minimum - 1e-2f)
                    {
                        report = $"'{child.name}' overlaps another structure. " + report;
                        return false;
                    }
                }

                seen.Add((child.position, radius));
            }

            return true;
        }
    }
}
```

- [ ] **Step 2: Confirm `TryFindSpawnPoint` is reachable**

Run:
```bash
grep -n 'TryFindSpawnPoint' Assets/Game/Editor/Environment/ClankerSettlementBuilder.cs
```
Expected: `internal static bool TryFindSpawnPoint(out Vector3 position)`. Both files are in `Assembly-CSharp-Editor`, so `internal` is enough and **no change to that frozen file is needed**. If it turns out to be `private`, do not widen it — copy the eight-line spawn-point lookup into the placer instead and note the duplication beside the `NomadPlacementGeometry` one.

- [ ] **Step 3: Verify it compiles**

Run:
```bash
python3 tools/typecheck.py --editor
```
Expected: exits 0.

Both external names this file depends on were confirmed when the plan was written, so a failure here means something else: `ChunkInfo.hasTerrain` is at `Assets/Game/Scripts/World/Streaming/Core/WorldStreamingConfig.cs:132`, and the tent prefabs really are `NomadSail_QuadSmall.prefab` and friends under `.../NomadSettlement/Tents/`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Editor/Environment/NomadSettlementPlacer.cs \
        Assets/Game/Editor/Environment/NomadSettlementPlacer.cs.meta
git commit -m "feat(settlement): place nine nomad settlements from one command"
```

---

## Task 7: Run it and prove it

Everything so far compiles and is tested in isolation. This task is the part that can only be done in the Editor, and none of it may be claimed without the evidence named beside it.

**Files:** chunk scenes under `Assets/Game/Scenes/World/Chunks/`, the three recipe assets, the world NavMesh asset.

- [ ] **Step 1: Build the settlements**

Open the Unity Editor. Run **Tools > SpaceGame > Settlements > Build Nomad Settlements + Bake NavMesh**.

Expected console: one `NomadSettlementPlacer` block naming nine settlements, each with its chunk, centre, site height range, structure and people counts, and the residents/patrollers/flockers breakdown; then the NavMesh bake report. No errors.

If it reports a shortfall — "No level ground of radius N left for ..." — **stop and report the number**. The levers are `maxGroundRange`, `MinimumSeparation`, and the tier's `siteRadius`, and which to pull is a design call, not yours.

If it reports "the … wiring did not land", a `SerializedObject` field name is wrong. Fix the name in `NomadSettlementGenerator`, re-run. Do not work around it by editing a frozen component.

- [ ] **Step 2: Verify the recipe assets were written**

Close the Editor. Run:
```bash
~/.unity/bin/unity test . --mode EditMode --filter NomadRecipeAssetTests --output /tmp/nomad-tests.xml
```
Expected: the tests from Task 4 that were red now pass — the three assets exist and every prefab slot is filled.

- [ ] **Step 3: Verify no two towns share a herd**

`HerdModule` keys herds by a global string, so two settlements sharing a prefix would share movement broadcasts across kilometres — every flock in both towns walking to the same place. The prefix is unique by construction (`nomad_s{index:00}`), but "by construction" is what every silent collision was before it happened. Check the saved scenes directly:

```bash
grep -rho 'herdId: nomad_s[0-9]*_flock[0-9]*' Assets/Game/Scenes/World/Chunks | sort | uniq -c | sort -rn | head
```
Expected: every count is `1`. A count above 1 means two flocks share an id — report which settlements rather than renaming one by hand.

- [ ] **Step 4: Verify the whole suite**

Run:
```bash
~/.unity/bin/unity test . --mode EditMode --output /tmp/all-tests.xml
```
Expected: no failures. Quote the summary line in your report.

- [ ] **Step 5: Confirm the freeze held**

Run:
```bash
git diff --name-only main... | grep -E 'ProceduralGeneration/Settlement/|Modules/Patrol/|Modules/Flocking/|Faction/Settlement|World/Sites/|ClankerSettlementBuilder'
```
Expected: **no output**. Any line here is a frozen file that was modified; revert it and find another way.

- [ ] **Step 6: Look at one, in play mode**

Open the Editor, enter play mode, and travel to the nearest large settlement.

Check and record each:
- Buildings sit **on** the ground — no floating corner, nothing sunk to its windows.
- Tents are scattered around the buildings, not stacked.
- Nomads are walking, not standing in a heap or sliding.
- A flock moves as a group and fans out when it stops.
- A patrol walks its perimeter route and comes back round.
- Walking into the settlement does not make anyone hostile.

- [ ] **Step 7: Verify on an actual client**

A feature seen working only on the host is not finished, and the buildings are scene content while the people are networked prefabs — different failure modes.

Start a host and join a second client (see `docs/AI/systems/Multiplayer.md`). On the **client**, travel to the same settlement.

Expected: the same buildings in the same places, and the nomads present and moving. A missing nomad on the client means a prefab is not registered in `DefaultNetworkPrefabs.asset`; the console says `[WorldService] Prefab 'X' has no NetworkObject` or similar.

- [ ] **Step 8: Verify save and reload**

Stand in a settlement, kill one nomad, save, quit to menu, load the world.

Expected: the buildings are unchanged (they are scene content and save nothing, by design); the dead nomad is still dead; the survivors are where they were, not back at their authored spots; and within one `spawnInterval` the town spawns a replacement. Confirm nomads appear in the save JSON.

- [ ] **Step 9: Measure the agent load**

Open the Profiler, stand in the largest settlement, and record the frame time and the `NavMeshAgent`/`AgentController` cost. This is the number the spec flagged as unknown.

If it is unacceptable, the lever is the population numbers in `NomadSettlementPlacer.WriteRecipes`, and dropping them is a one-line change plus a re-run. **Report the number either way** — do not decide it is fine without measuring.

- [ ] **Step 10: Commit the world**

```bash
git add Assets/Game/Scenes/World/Chunks \
        Assets/Game/ScriptableObjects/Settlements \
        Assets/Game/Settings
git commit -m "feat(world): nine nomad settlements placed across the main world"
```

---

## Task 8: Documentation

Every change to behaviour updates its doc in the same commit. A doc describing code that no longer exists is worse than no doc, because the next agent trusts it.

**Files:**
- Modify: `docs/AI/systems/TerrainGeneration.md`
- Modify: `docs/AI/systems/AgentSystem.md`
- Modify: `docs/Human/the-systems.md`

- [ ] **Step 1: Update `TerrainGeneration.md`**

- **Model:** add a bullet for the nomad settlements beside the Clanker one — nine towns in three tiers, placed by `NomadSettlementPlacer`, solved by `NomadSettlementLayout`, one root per town, in `NomadSettlement/` beside the older `Settlement/` tree.
- **Key types:** add rows for `NomadPlacementGeometry`, `NomadSettlementLayout`, `NomadSettlementRecipe`, `NomadSettlementGenerator`, `NomadSettlementPlacer`.
- **Flows:** add the nomad settlement flow — `WriteRecipes` → `OpenTerrainChunks` → `TryChooseSite` per tier → `PlaceGenerator` → `Generate` → `Verify` → save → one NavMesh bake.
- **Editor entry points:** add both menu items.
- **Gotchas:** add these five, in the file's existing voice:
  - Ground for a nomad settlement comes from the terrain **heightmap**, never a raycast — terrain and buildings share the `Default` layer, so a masked ray can hit a building already placed.
  - A settlement's site disc must lie inside **one** chunk. Everything it makes goes into a single chunk scene, so a town straddling a boundary has half its buildings stream with a chunk nobody is near.
  - `HerdModule` keys herds by a **global** string. Two settlements sharing a `herdPrefix` share movement broadcasts across kilometres.
  - The settlement root name is part of the save-id path of every person under it. Renaming `NomadSettlement_NN_Name` or its `Generated` child after a world save orphans them all.
  - **`NomadPlacementGeometry` knowingly duplicates three private helpers in `RobotSettlementGenerator`.** The settlement system was frozen when the nomad towns were built, so extracting them was not available. Collapse the two when the freeze lifts; do not add a third copy in the meantime.
- **`symptoms:`** frontmatter — add whichever of these actually cost you time, phrased as what you *saw*.
- Bump `updated:` to the date you finish.

- [ ] **Step 2: Update `AgentSystem.md`**

Add the three settlement archetypes (resident / flocker / patroller) and which modules make each. Record that the generator wires those frozen modules through `SerializedObject` rather than a `Configure` method, and that a wrong field name fails **silently** — which is why the placer reads the values back. Record the `WanderModule` → `PatrolModule` swap on the five Nomad prefabs and **why**: `WanderModule` is a random walk relative to the agent's current position, so a nomad drifts out of his own town and the town refills for ever.

- [ ] **Step 3: Update `docs/Human/the-systems.md`**

One short plain-language paragraph on the nomad settlements. The validator fails without an entry for a new system.

- [ ] **Step 4: Regenerate and validate**

Run:
```bash
python3 tools/docs_check.py --index
```
Expected: exits 0. `INDEX.md` and `ROUTING.md` are generated — never hand-edit them.

- [ ] **Step 5: Commit**

```bash
git add docs/
git commit -m "docs: nomad settlements and the nomad wander anchor"
```

---

## Definition of done

Do not report this complete until every line here is true and you have the output that proves it:

- [ ] `python3 tools/typecheck.py --editor` exits 0.
- [ ] `~/.unity/bin/unity test . --mode EditMode` reports no failures.
- [ ] **No frozen file was modified** — the Task 7 Step 5 grep prints nothing.
- [ ] Nine settlements exist in the world, in three tiers, each under one root object.
- [ ] Deleting a root deletes exactly that settlement; right-clicking > Generate rebuilds it identically.
- [ ] No building floats or is sunk; none overlaps another.
- [ ] No two settlements share a `herdId`.
- [ ] Residents, flocks and patrols each behave as described, seen in play mode.
- [ ] A joining **client** sees the same nine towns with the same people.
- [ ] A save/reload keeps the dead dead and the living where they stood.
- [ ] A caravan still travels after the Nomad prefab change.
- [ ] The profiler number for a large settlement is recorded and reported.
- [ ] `python3 tools/docs_check.py --index` exits 0.
