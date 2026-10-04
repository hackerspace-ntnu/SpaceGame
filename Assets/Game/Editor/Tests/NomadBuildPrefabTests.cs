// The nomad settlement's build prefabs, checked the way a resident would meet them: every decoration stands
// upright, every spot a resident is sent to is upright, ordinary-sized and on a floor, and no collider has a
// negative size. These prefabs came out of Blender with an Rx(-90) axis conversion on every nested decoration;
// kept on the instance, it laid each one on its side and threw the spots that rode on it metres into the air.
//
// Colliders are held to the same standard: no collider is a solid its object is not (the hand-edited mesh copies
// once carried one bounding box each, so a watchtower was a 5.6 x 13.9 x 6.8 m block), every hard object has a
// collider that reaches it, every Ladder can be climbed, a gate leaf is a panel, and the keep's two fences hold
// an animal until their gates are open.
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class NomadBuildPrefabTests
    {
        private const string Folder = "Assets/Game/Prefabs/Environment/Structures/NomadSettlement";
        private const float UprightDot = 0.99f;

        // A spot may stand under a hand-scaled decoration (up to about 2.2x), never under a Blender-axis x100 mesh node.
        private const float MaxSpotScale = 3f;
        private const float FloorTolerance = 0.4f;

        // A collider further below a spot than this is a foundation under the hillside, not the floor it stands on.
        private const float FloorReach = 3f;

        private static readonly string[] SubFolders = { "/Props/", "/Streets/", "/Terrace/", "/Tents/", "/sections/", "/Door/" };

        private readonly Dictionary<string, GameObject> contents = new();

        [OneTimeSetUp]
        public void LoadBuilds()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (SubFolders.Any(path.Contains)) continue;
                contents[path] = PrefabUtility.LoadPrefabContents(path);
            }
            Physics.SyncTransforms();
        }

        [OneTimeTearDown]
        public void UnloadBuilds()
        {
            foreach (GameObject root in contents.Values) PrefabUtility.UnloadPrefabContents(root);
            contents.Clear();
            SurfaceCells.Clear();
        }

        [Test]
        public void TheBuildsAreFound() => Assert.Greater(contents.Count, 20, "the nomad build prefabs were not found under " + Folder);

        [Test]
        public void EveryNestedDecorationStandsUpright()
        {
            var failures = new List<string>();
            foreach ((string path, GameObject root) in contents)
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!IsNestedDecoration(t.gameObject)) continue;
                    float up = Vector3.Dot(t.up, Vector3.up);
                    if (up < UprightDot) failures.Add($"{Describe(path, root, t)}: up·Y = {up:0.00}");
                }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void EverySpotIsUprightAndOrdinarySized()
        {
            var failures = new List<string>();
            foreach ((string path, GameObject root) in contents)
                foreach (SettlementSpot spot in root.GetComponentsInChildren<SettlementSpot>(true))
                {
                    float up = Vector3.Dot(spot.transform.up, Vector3.up);
                    float scale = spot.transform.lossyScale.x;
                    if (up < UprightDot || scale > MaxSpotScale)
                        failures.Add($"{Describe(path, root, spot.transform)}: up·Y = {up:0.00}, lossyScale = {scale:0.0}");
                }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // A prefab's yards stand on the world's terrain, which is not in the prefab, so a spot with nothing below
        // it is fine. What this catches is a spot hanging in the air over the prefab's own floor.
        [Test]
        public void NoSpotHangsAboveTheFloorBelowIt()
        {
            var failures = new List<string>();
            foreach ((string path, GameObject root) in contents)
            {
                PhysicsScene physics = root.scene.GetPhysicsScene();
                foreach (SettlementSpot spot in root.GetComponentsInChildren<SettlementSpot>(true))
                {
                    Vector3 from = spot.Position + Vector3.up * FloorTolerance;
                    if (!physics.Raycast(from, Vector3.down, out RaycastHit hit, FloorReach + FloorTolerance)) continue;
                    float drop = hit.distance - FloorTolerance;
                    if (drop > FloorTolerance)
                        failures.Add($"{Describe(path, root, spot.transform)}: {drop:0.00} m above {hit.collider.name}");
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void NoColliderHasANegativeOrEmptySize()
        {
            var failures = new List<string>();
            foreach ((string path, GameObject root) in contents)
            {
                foreach (BoxCollider box in root.GetComponentsInChildren<BoxCollider>(true))
                    if (box.size.x <= 0f || box.size.y <= 0f || box.size.z <= 0f)
                        failures.Add($"{Describe(path, root, box.transform)}: BoxCollider size {box.size}");
                foreach (SphereCollider sphere in root.GetComponentsInChildren<SphereCollider>(true))
                    if (sphere.radius <= 0f) failures.Add($"{Describe(path, root, sphere.transform)}: SphereCollider radius {sphere.radius}");
                foreach (CapsuleCollider capsule in root.GetComponentsInChildren<CapsuleCollider>(true))
                    if (capsule.radius <= 0f || capsule.height <= 0f)
                        failures.Add($"{Describe(path, root, capsule.transform)}: CapsuleCollider radius {capsule.radius}, height {capsule.height}");
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // ---- Colliders. A hard object has a collider that matches its surface, and no collider is a solid the object is not.

        private const string DecorationFolder = "Assets/Game/Prefabs/Environment/Decorations";

        // A hard object is at least this big in two of its own axes. Smaller clutter needs no collider.
        private const float HardObjectSize = 0.5f;

        // A vertex counts as covered when a primitive collider reaches within this distance of it.
        private const float CoverageReach = 0.35f;

        // The share of a hard renderer's vertices that must be covered. Open frames (racks, hide frames) are mostly
        // thin poles that are skipped on purpose, so this is a floor against "no collider at all", not a fit measure.
        private const float CoverageNeeded = 0.1f;

        // A collider whose bounds hold more than this many times the volume of the renderers it belongs to is a phantom
        // solid. Colliders under one cubic metre are ignored: small slack there is invisible.
        private const float PhantomRatio = 3f;
        private const float PhantomMinVolume = 1f;

        // A box this big must be mostly occupied: at least this share of its volume lies within SupportReach of the
        // surface of the renderers it belongs to. This is a coarse net for gross cases (a box around a derrick scored 0.15);
        // a bounding box around a densely braced tower still scores ~0.3, which is why MeshCopiesCarryNoBoundingBox exists. A wall, a deck or a post scores near one. Boxes under PhantomSupportMinVolume are ignored.
        private const float SupportReach = 0.2f;
        private const float SupportCell = 0.2f;
        private const float PhantomSupport = 0.2f;
        private const float PhantomSupportMinVolume = 4f;
        private const int MaxTrianglesSampled = 60000;

        // The thickest a gate leaf's collider may be, in metres: the leaf is a panel, not a block.
        private const float MaxGatePanelThickness = 0.4f;

        // Parts that are cloth, foliage, light, water or loose rope: never a hard object, however large.
        private static readonly Regex SoftPartName = new Regex(
            "(" +
            "Lantern|Hanging|Bunting|Pennant|Banner|Flag|Rope|Cord|Chain|Garland|Chime|Awning|Canopy|Sail|Tarp|Cloth|Drape|" +
            "Curtain|Leaves|Foliage|Fern|Flame|Embers|Glow|Water|Algae|Smoke|Steam|Decal|Rug|Carpet|Blanket|Bedding|Cushion|" +
            "Hay|Straw|Fur|Hide|Tassel|Bulb|Vine|Moss|Grass|Bush|Canvas|Reed|Succulent|Plant|Fern|Flower|Streamer|Tuft|" +
            "Tree|Pitchfork" +
            ")s?\\d*$", RegexOptions.IgnoreCase);

        // A raised roof, awning or canopy may have no collider: the player is 3 m tall and walks under it, and a decoration is
        // scaled up to 2x on an instance, so no fixed height tells a low roof from a high one (ArtPipeline.md, decoration colliders).
        // A roof high enough to be out of reach is given one when the decoration is fitted, but is not required here.
        private const float GroundSlack = 0.35f;
        private static readonly Regex OverheadPartName = new Regex("(Roof|Awning|Canopy|Lintel|Shade|Cover|Cladding|Sheets|Boards|Shingles)\\d*$", RegexOptions.IgnoreCase);

        private static readonly string[] SoftMaterialKinds = { "Fabric", "Fibre", "Plant", "Food", "Hide", "Leather", "Paper", "Glow", "Water", "Fur" };

        [Test]
        public void NoColliderIsAPhantomSolid()
        {
            var failures = new List<string>();
            foreach ((string path, GameObject root) in contents)
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                {
                    if (collider is MeshCollider || collider.isTrigger) continue;
                    List<Renderer> owned = RenderersOf(collider.transform, root.transform);
                    if (owned.Count == 0) continue;
                    Bounds rendered = owned[0].bounds;
                    foreach (Renderer renderer in owned) rendered.Encapsulate(renderer.bounds);
                    float colliderVolume = Volume(collider.bounds);
                    if (colliderVolume >= PhantomMinVolume && colliderVolume > PhantomRatio * Volume(rendered))
                        failures.Add($"{Describe(path, root, collider.transform)}: {collider.GetType().Name} {collider.bounds.size:0.0} holds " +
                                     $"{colliderVolume / Mathf.Max(Volume(rendered), 0.001f):0}x the {rendered.size:0.0} it covers");
                    else if (collider is BoxCollider box && colliderVolume >= PhantomSupportMinVolume && SupportedShare(box, owned) < PhantomSupport)
                        failures.Add($"{Describe(path, root, collider.transform)}: BoxCollider {collider.bounds.size:0.0} is mostly empty air " +
                                     $"({SupportedShare(box, owned):P0} of it near the surface it stands for)");
                }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // The hand-edited mesh copies once got one BoxCollider each, fitted to the mesh bounds. That is the shape of every
        // phantom solid here, so a mesh copy's colliders must live on a scale-1 'Colliders' child fitted part by part.
        [Test]
        public void MeshCopiesCarryNoBoundingBox()
        {
            var failures = new List<string>();
            foreach ((string path, GameObject root) in contents)
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    Transform t = filter.transform;
                    if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject) || t.parent == root.transform) continue;
                    foreach (Collider collider in t.GetComponents<Collider>())
                        failures.Add($"{Describe(path, root, t)}: a {collider.GetType().Name} sits on the mesh copy itself; fit it on a 'Colliders' child");
                }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void EveryHardRendererHasColliderCoverage()
        {
            var failures = new List<string>();
            foreach ((string path, GameObject root) in contents)
            {
                var solid = new List<Collider>();
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                    if (!collider.isTrigger && !(collider is MeshCollider)) solid.Add(collider);

                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    // The build's own Model is covered by its baked Collision shell, which is checked against it by eye in the audit.
                    if (renderer.name == "Model" && renderer.transform.parent == root.transform) continue;
                    if (!IsHardObject(renderer)) continue;
                    float covered = CoveredShare(renderer, solid);
                    if (covered < CoverageNeeded)
                        failures.Add($"{Describe(path, root, renderer.transform)}: {Dimensions(SizeOf(renderer))} m, {covered:P0} of it within {CoverageReach} m of a collider");
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void EveryLadderHasATopAndAnExit()
        {
            var failures = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Folder, DecorationFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;
                foreach (Ladder ladder in asset.GetComponentsInChildren<Ladder>(true))
                    if (!ladder.IsValid) failures.Add($"{path}: Ladder '{ladder.name}' has no Top or Exit, so nobody can climb it");
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // The watchtowers are hand-edited mesh copies: they have no Ladder of their own, so each one needs the component placed.
        [Test]
        public void EveryTowerMeshCopyHasALadderBesideIt()
        {
            var failures = new List<string>();
            foreach ((string path, GameObject root) in contents)
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    Transform t = filter.transform;
                    if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject) || !(t.name.StartsWith("BellFrame") || t.name.StartsWith("Watchtower"))) continue;
                    if (t.parent.Find(t.name + "_Ladder") == null) failures.Add($"{Describe(path, root, t)}: a tower with no Ladder");
                }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void NoGateLeafColliderIsABlock()
        {
            var failures = new List<string>();
            var gates = new List<(string, GameObject)>();
            foreach ((string path, GameObject root) in contents) gates.Add((path, root));
            string gatePath = Folder + "/Props/NomadFenceGate.prefab";
            GameObject gate = PrefabUtility.LoadPrefabContents(gatePath);
            try
            {
                gates.Add((gatePath, gate));
                foreach ((string path, GameObject root) in gates)
                    foreach (DoorInteraction door in root.GetComponentsInChildren<DoorInteraction>(true))
                        foreach (BoxCollider box in door.GetComponentsInChildren<BoxCollider>(true))
                        {
                            Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);
                            float thickness = Mathf.Min(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                            if (thickness > MaxGatePanelThickness) failures.Add($"{Describe(path, root, box.transform)}: BoxCollider {size:0.00} is {thickness:0.00} m thick");
                        }
            }
            finally { PrefabUtility.UnloadPrefabContents(gate); }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // The keep's two rectangular fences are the pens for trapped animals: shut, a small body cannot get out of either;
        // with the gates swung open, it can. Seeds are a point inside each pen, in prefab space.
        [Test]
        public void TheKeepPensHoldAnimalsUntilTheirGatesOpen()
        {
            GameObject keep = contents[Folder + "/NomadAnimalKeep.prefab"];
            Vector2[] pens = { new Vector2(-3f, 10f), new Vector2(-1f, 0f) };
            foreach (Vector2 pen in pens)
                Assert.IsFalse(Leaks(keep, pen), $"the pen at {pen} lets an animal out with its gate shut");

            var swung = new List<(Transform leaf, Quaternion shut)>();
            foreach (DoorInteraction door in keep.GetComponentsInChildren<DoorInteraction>(true))
                foreach (Transform t in door.GetComponentsInChildren<Transform>(true))
                    if (t.name == "LeftDoors") { swung.Add((t, t.localRotation)); t.localRotation *= Quaternion.Euler(0f, -90f, 0f); }
            try
            {
                Physics.SyncTransforms();
                foreach (Vector2 pen in pens)
                    Assert.IsTrue(Leaks(keep, pen), $"the pen at {pen} has no way out when its gate is open");
            }
            finally
            {
                foreach ((Transform leaf, Quaternion shut) in swung) leaf.localRotation = shut;
                Physics.SyncTransforms();
            }
        }

        private const float AnimalRadius = 0.2f;
        private const float AnimalHeight = 0.3f;
        private const float FloorHeight = 0.05f;
        private const float FloodCell = 0.1f;
        private static readonly Vector2 FloodMin = new Vector2(-16f, -9f);
        private static readonly Vector2 FloodMax = new Vector2(12f, 18f);

        // Whether a body of the animal's size can walk from the seed to the edge of the area around the keep.
        private static bool Leaks(GameObject root, Vector2 seed)
        {
            PhysicsScene physics = root.scene.GetPhysicsScene();
            int nx = (int)((FloodMax.x - FloodMin.x) / FloodCell), nz = (int)((FloodMax.y - FloodMin.y) / FloodCell);
            var blocked = new bool[nx, nz];
            var hits = new Collider[1];
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                    blocked[i, k] = physics.OverlapSphere(new Vector3(FloodMin.x + (i + 0.5f) * FloodCell, FloorHeight + AnimalHeight, FloodMin.y + (k + 0.5f) * FloodCell),
                                                          AnimalRadius, hits, ~0, QueryTriggerInteraction.Ignore) > 0;
            var start = new Vector2Int((int)((seed.x - FloodMin.x) / FloodCell), (int)((seed.y - FloodMin.y) / FloodCell));
            Assert.IsFalse(blocked[start.x, start.y], $"the seed {seed} stands inside a collider");

            var reached = new bool[nx, nz];
            var queue = new Queue<Vector2Int>();
            reached[start.x, start.y] = true;
            queue.Enqueue(start);
            Vector2Int[] steps = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            while (queue.Count > 0)
            {
                Vector2Int c = queue.Dequeue();
                if (c.x == 0 || c.y == 0 || c.x == nx - 1 || c.y == nz - 1) return true;
                foreach (Vector2Int step in steps)
                {
                    Vector2Int n = c + step;
                    if (blocked[n.x, n.y] || reached[n.x, n.y]) continue;
                    reached[n.x, n.y] = true;
                    queue.Enqueue(n);
                }
            }
            return false;
        }

        private static float Volume(Bounds b) => b.size.x * b.size.y * b.size.z;

        // The renderers the collider belongs to: those under it, or under the nearest ancestor that has any.
        public static List<Renderer> RenderersOf(Transform collider, Transform root)
        {
            var owned = new List<Renderer>();
            for (Transform scope = collider; scope != null; scope = scope.parent)
            {
                foreach (Renderer renderer in scope.GetComponentsInChildren<Renderer>(true))
                    if (!(renderer is ParticleSystemRenderer)) owned.Add(renderer);
                if (owned.Count > 0 || scope == root) break;
            }
            return owned;
        }

        // The cells a renderer's surface touches. Many colliders share one renderer scope, so each renderer is sampled once.
        private static readonly Dictionary<Renderer, HashSet<Vector3Int>> SurfaceCells = new();

        private static HashSet<Vector3Int> SurfaceOf(Renderer renderer)
        {
            if (SurfaceCells.TryGetValue(renderer, out HashSet<Vector3Int> cells)) return cells;
            cells = new HashSet<Vector3Int>();
            SurfaceCells[renderer] = cells;
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return cells;
            int[] triangles = filter.sharedMesh.triangles;
            if (triangles.Length / 3 > MaxTrianglesSampled) return cells;
            Vector3[] vertices = filter.sharedMesh.vertices;
            Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = toWorld.MultiplyPoint3x4(vertices[triangles[i]]), b = toWorld.MultiplyPoint3x4(vertices[triangles[i + 1]]), c = toWorld.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                float longest = Mathf.Max((b - a).magnitude, (c - b).magnitude, (a - c).magnitude);
                int n = Mathf.Max(1, Mathf.CeilToInt(longest / (SupportCell * 0.6f)));
                for (int u = 0; u <= n; u++)
                    for (int v = 0; v <= n - u; v++)
                        cells.Add(CellOf(a + (b - a) * ((float)u / n) + (c - a) * ((float)v / n)));
            }
            return cells;
        }

        // The share of the box's volume that lies near the surface of the given renderers (see SupportReach).
        public static float SupportedShare(BoxCollider box, List<Renderer> renderers)
        {
            List<HashSet<Vector3Int>> surfaces = renderers.Select(SurfaceOf).Where(cells => cells.Count > 0).ToList();
            if (surfaces.Count == 0) return 1f;

            Vector3 half = box.size * 0.5f;
            Vector3 scale = box.transform.lossyScale;
            int nx = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(box.size.x * scale.x) / SupportCell)), ny = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(box.size.y * scale.y) / SupportCell)), nz = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(box.size.z * scale.z) / SupportCell));
            int reach = Mathf.CeilToInt(SupportReach / SupportCell), supported = 0, total = 0;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        Vector3 local = box.center + new Vector3(Mathf.Lerp(-half.x, half.x, (i + 0.5f) / nx), Mathf.Lerp(-half.y, half.y, (j + 0.5f) / ny), Mathf.Lerp(-half.z, half.z, (k + 0.5f) / nz));
                        Vector3Int cell = CellOf(box.transform.TransformPoint(local));
                        total++;
                        if (surfaces.Any(surface => HasNeighbour(surface, cell, reach))) supported++;
                    }
            return (float)supported / total;
        }

        private static Vector3Int CellOf(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / SupportCell), Mathf.FloorToInt(p.y / SupportCell), Mathf.FloorToInt(p.z / SupportCell));

        private static bool HasNeighbour(HashSet<Vector3Int> surface, Vector3Int cell, int reach)
        {
            for (int x = -reach; x <= reach; x++)
                for (int y = -reach; y <= reach; y++)
                    for (int z = -reach; z <= reach; z++)
                        if (surface.Contains(cell + new Vector3Int(x, y, z))) return true;
            return false;
        }

        // The renderer's real size, sorted: its mesh bounds scaled by its node, so a leaning pole is not inflated by its lean.
        private static float[] SizeOf(Renderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            Vector3 size = filter != null && filter.sharedMesh != null ? Vector3.Scale(filter.sharedMesh.bounds.size, renderer.transform.lossyScale) : renderer.bounds.size;
            float[] sorted = { Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z) };
            System.Array.Sort(sorted);
            return sorted;
        }

        private static string Dimensions(float[] sorted) => $"{sorted[0]:0.0}x{sorted[1]:0.0}x{sorted[2]:0.0}";

        private static bool IsHardObject(Renderer renderer)
        {
            if (SizeOf(renderer)[1] < HardObjectSize) return false;
            string[] words = renderer.name.Split('_');
            if (SoftPartName.IsMatch(words[words.Length - 1])) return false;
            float underside = renderer.bounds.min.y;
            if (OverheadPartName.IsMatch(words[words.Length - 1]) && underside > GroundSlack) return false;
            Material[] materials = renderer.sharedMaterials;
            if (materials.Length == 0 || materials[0] == null) return true;
            string kind = materials[0].name.Replace("Mat_Deco_", "").Replace("Mat_", "");
            foreach (string soft in SoftMaterialKinds)
                if (kind.StartsWith(soft)) return false;
            return true;
        }

        private static float CoveredShare(MeshRenderer renderer, List<Collider> solid)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return 1f;
            Vector3[] vertices = filter.sharedMesh.vertices;
            if (vertices.Length == 0) return 1f;
            Bounds reach = renderer.bounds;
            reach.Expand(2f * CoverageReach);
            var near = new List<Collider>();
            foreach (Collider collider in solid)
                if (collider.bounds.Intersects(reach)) near.Add(collider);

            int stride = Mathf.Max(1, vertices.Length / 160), sampled = 0, covered = 0;
            Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
            for (int i = 0; i < vertices.Length; i += stride)
            {
                sampled++;
                Vector3 world = toWorld.MultiplyPoint3x4(vertices[i]);
                foreach (Collider collider in near)
                    if ((collider.ClosestPoint(world) - world).sqrMagnitude <= CoverageReach * CoverageReach) { covered++; break; }
            }
            return (float)covered / sampled;
        }

        // A decoration is a prefab instance (not the FBX shell) that is not itself inside another instance.
        private static bool IsNestedDecoration(GameObject go)
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go) || PrefabUtility.GetOutermostPrefabInstanceRoot(go) != go) return false;
            PrefabAssetType type = PrefabUtility.GetPrefabAssetType(go);
            return type == PrefabAssetType.Regular || type == PrefabAssetType.Variant;
        }

        private static string Describe(string path, GameObject root, Transform t)
        {
            var names = new List<string>();
            for (Transform n = t; n != null && n != root.transform; n = n.parent) names.Add(n.name);
            names.Reverse();
            return $"{System.IO.Path.GetFileNameWithoutExtension(path)}/{string.Join("/", names)}";
        }
    }
}
