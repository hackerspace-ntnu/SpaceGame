// Z-fighting in the walk-in colony is a layout bug that no other check sees: two pieces placed so that a face of one
// lies in the plane of a face of the other look fine in every number and flicker on screen. The user reported it three
// times (a gear board laid over the airlock bulkhead's lamp panel, cabinets standing inside the gear wall, pipe runs
// overlapping by 6 mm) before anything measured it. This test measures it: every visible triangle of every Colony_*
// prefab, with each airlock hatch shut and then open, and fails on any pair of coplanar, same-facing, overlapping
// triangles that belong to two DIFFERENT placed pieces (the hull and a prop, or two props).
//
// Coplanar faces INSIDE one model (a label flush with its panel) are the model's own business and are left to the art
// pipeline; so are back-to-back faces, which back-face culling never draws together, unless a material is two-sided.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.EditorTools
{
    public class ColonyZFightTests
    {
        private const string ColonyFolder = "Assets/Game/Prefabs/Environment/Structures/AstronautSettlement";

        // Faces this close to parallel and to one plane fight in the depth buffer; 3 mm is far below anything an eye
        // tells apart in the colony and far above the depth buffer's resolution at any range a player stands at.
        private const float ParallelDot = 0.999f;
        private const float PlaneTolerance = 0.003f;
        private const float MinOverlapArea = 0.00005f;     // 0.5 cm2: a slit smaller than this is a pixel or two at most

        // Spatial hash of the triangles' planes: normal quantised to 1/NormalBins, plane offset to OffsetBin metres.
        private const float NormalBins = 40f;
        private const float OffsetBin = 0.004f;

        private struct Tri
        {
            public Vector3 A, B, C, Normal;
            public float Offset;
            public int Renderer, Owner, SubMesh;
        }

        [Test]
        public void NoTwoPlacedPiecesShareAVisibleSurface()
        {
            var failures = new List<string>();
            string[] colonies = AssetDatabase.FindAssets("Colony_ t:Prefab", new[] { ColonyFolder })
                                             .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            Assert.IsNotEmpty(colonies, "no Colony_* prefabs under " + ColonyFolder);

            foreach (string path in colonies)
            {
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var colony = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                    colony.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    failures.AddRange(PlacementFights(colony).Select(f => $"{colony.name} (hatches shut): {f}"));
                    OpenEveryHatch(colony);
                    failures.AddRange(PlacementFights(colony).Select(f => $"{colony.name} (hatches open): {f}"));
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            Assert.IsEmpty(failures, $"{failures.Count} coplanar overlaps between placed pieces (offset one of each " +
                                     "pair by at least 3 mm or move it):\n" + string.Join("\n", failures.Take(40)));
        }

        /// <summary>Every hatch in the pose <see cref="AirlockHatch"/> drives it to when fully open.</summary>
        private static void OpenEveryHatch(GameObject colony)
        {
            foreach (var hatch in colony.GetComponentsInChildren<AirlockHatch>(true))
            {
                var so = new SerializedObject(hatch);
                hatch.transform.localPosition += so.FindProperty("openOffset").vector3Value;
                hatch.transform.localRotation *= Quaternion.Euler(so.FindProperty("openEuler").vector3Value);
            }
        }

        /// <summary>One line per pair of placed pieces with coplanar overlapping faces: what, where, how much.</summary>
        private static IEnumerable<string> PlacementFights(GameObject colony)
        {
            var renderers = new List<Renderer>();
            var owners = new List<Transform>();
            List<Tri> tris = Gather(colony, renderers, owners);

            var bins = new Dictionary<long, List<int>>();
            for (int i = 0; i < tris.Count; i++)
            {
                long key = BinKey(tris[i].Normal, tris[i].Offset, 0, 0, 0, 0);
                if (!bins.TryGetValue(key, out var list)) bins[key] = list = new List<int>();
                list.Add(i);
            }

            var area = new Dictionary<(int, int), float>();
            for (int i = 0; i < tris.Count; i++)
            {
                Tri a = tris[i];
                foreach (int sign in new[] { 1, -1 })
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dd = -1; dd <= 1; dd++)
                    {
                        if (!bins.TryGetValue(BinKey(a.Normal * sign, a.Offset * sign, dx, dy, dz, dd), out var list)) continue;
                        foreach (int j in list)
                        {
                            if (j <= i) continue;
                            Tri b = tris[j];
                            if (a.Owner == b.Owner) continue;
                            float facing = Vector3.Dot(a.Normal, b.Normal);
                            if (Mathf.Abs(facing) < ParallelDot || (facing > 0f) != (sign > 0)) continue;
                            if (facing < 0f && !TwoSided(renderers[a.Renderer], a.SubMesh) && !TwoSided(renderers[b.Renderer], b.SubMesh)) continue;
                            if (!OnPlaneOf(a, b) || !OnPlaneOf(b, a)) continue;

                            float overlap = OverlapArea(a, b);
                            if (overlap < MinOverlapArea) continue;

                            var pair = a.Renderer < b.Renderer ? (a.Renderer, b.Renderer) : (b.Renderer, a.Renderer);
                            area[pair] = (area.TryGetValue(pair, out float sum) ? sum : 0f) + overlap;
                        }
                    }
            }

            foreach (var kv in area.OrderByDescending(kv => kv.Value))
                yield return $"'{PathOf(renderers[kv.Key.Item1].transform, colony.transform)}' x " +
                             $"'{PathOf(renderers[kv.Key.Item2].transform, colony.transform)}' " +
                             $"{kv.Value * 10000f:0.0} cm2";
        }

        private static List<Tri> Gather(GameObject colony, List<Renderer> renderers, List<Transform> owners)
        {
            var tris = new List<Tri>();
            var baked = new List<Mesh>();
            try
            {
                foreach (Renderer renderer in colony.GetComponentsInChildren<Renderer>(false))
                {
                    if (!renderer.enabled || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;

                    Mesh mesh;
                    Matrix4x4 toWorld;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        mesh = new Mesh();
                        skinned.BakeMesh(mesh, true);
                        baked.Add(mesh);
                        toWorld = Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
                    }
                    else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                    {
                        mesh = filter.sharedMesh;
                        toWorld = renderer.transform.localToWorldMatrix;
                    }
                    else continue;

                    int rendererIndex = renderers.Count;
                    renderers.Add(renderer);
                    int owner = OwnerIndex(renderer.transform, colony.transform, owners);

                    Vector3[] vertices = mesh.vertices;
                    for (int v = 0; v < vertices.Length; v++) vertices[v] = toWorld.MultiplyPoint3x4(vertices[v]);

                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                        int[] index = mesh.GetTriangles(s);
                        for (int t = 0; t + 2 < index.Length; t += 3)
                        {
                            Vector3 a = vertices[index[t]], b = vertices[index[t + 1]], c = vertices[index[t + 2]];
                            Vector3 cross = Vector3.Cross(b - a, c - a);
                            float twiceArea = cross.magnitude;
                            // A triangle smaller than the threshold cannot overlap anything by more than it.
                            if (twiceArea * 0.5f < MinOverlapArea) continue;
                            Vector3 normal = cross / twiceArea;
                            tris.Add(new Tri
                            {
                                A = a, B = b, C = c, Normal = normal, Offset = Vector3.Dot(normal, a),
                                Renderer = rendererIndex, Owner = owner, SubMesh = s,
                            });
                        }
                    }
                }
            }
            finally
            {
                foreach (Mesh mesh in baked) Object.DestroyImmediate(mesh);
            }

            return tris;
        }

        /// <summary>
        /// The placed piece a renderer belongs to: the outermost prefab instance below the colony root (the hull's FBX
        /// is one, every prop is one, a suit nested in its stand belongs to the stand).
        /// </summary>
        private static int OwnerIndex(Transform t, Transform colony, List<Transform> owners)
        {
            Transform owner = colony;
            for (Transform x = t; x != null && x != colony; x = x.parent)
                if (PrefabUtility.IsAnyPrefabInstanceRoot(x.gameObject)) owner = x;

            int index = owners.IndexOf(owner);
            if (index >= 0) return index;
            owners.Add(owner);
            return owners.Count - 1;
        }

        private static long BinKey(Vector3 normal, float offset, int dx, int dy, int dz, int dd)
        {
            long qx = Mathf.RoundToInt(normal.x * NormalBins) + dx + 64;
            long qy = Mathf.RoundToInt(normal.y * NormalBins) + dy + 64;
            long qz = Mathf.RoundToInt(normal.z * NormalBins) + dz + 64;
            long qd = Mathf.FloorToInt(offset / OffsetBin) + dd;
            return (((qx * 128 + qy) * 128 + qz) << 32) ^ (uint)qd;
        }

        private static bool OnPlaneOf(Tri plane, Tri t) =>
            Mathf.Abs(Vector3.Dot(plane.Normal, t.A) - plane.Offset) <= PlaneTolerance &&
            Mathf.Abs(Vector3.Dot(plane.Normal, t.B) - plane.Offset) <= PlaneTolerance &&
            Mathf.Abs(Vector3.Dot(plane.Normal, t.C) - plane.Offset) <= PlaneTolerance;

        private static bool TwoSided(Renderer renderer, int subMesh)
        {
            Material[] materials = renderer.sharedMaterials;
            Material material = subMesh < materials.Length ? materials[subMesh] : null;
            return material != null && material.HasProperty("_Cull") && Mathf.Approximately(material.GetFloat("_Cull"), (float)CullMode.Off);
        }

        /// <summary>Area of the intersection of two coplanar triangles: b clipped by a in a's dominant projection plane.</summary>
        private static float OverlapArea(Tri a, Tri b)
        {
            Vector3 n = a.Normal;
            int drop = Mathf.Abs(n.x) > Mathf.Abs(n.y)
                ? (Mathf.Abs(n.x) > Mathf.Abs(n.z) ? 0 : 2)
                : (Mathf.Abs(n.y) > Mathf.Abs(n.z) ? 1 : 2);
            Vector2 P(Vector3 p) => drop == 0 ? new Vector2(p.y, p.z) : drop == 1 ? new Vector2(p.z, p.x) : new Vector2(p.x, p.y);

            Vector2 a0 = P(a.A), a1 = P(a.B), a2 = P(a.C);
            if (Cross(a1 - a0, a2 - a0) < 0f) (a1, a2) = (a2, a1);

            var polygon = new List<Vector2> { P(b.A), P(b.B), P(b.C) };
            polygon = Clip(polygon, a0, a1);
            polygon = Clip(polygon, a1, a2);
            polygon = Clip(polygon, a2, a0);

            float twice = 0f;
            for (int i = 0; i < polygon.Count; i++) twice += Cross(polygon[i], polygon[(i + 1) % polygon.Count]);
            float component = drop == 0 ? n.x : drop == 1 ? n.y : n.z;
            return Mathf.Abs(twice) * 0.5f / Mathf.Abs(component);
        }

        /// <summary>Sutherland-Hodgman: the part of <paramref name="polygon"/> left of the edge e0 -> e1.</summary>
        private static List<Vector2> Clip(List<Vector2> polygon, Vector2 e0, Vector2 e1)
        {
            var kept = new List<Vector2>();
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 c0 = polygon[i], c1 = polygon[(i + 1) % polygon.Count];
                float s0 = Cross(e1 - e0, c0 - e0), s1 = Cross(e1 - e0, c1 - e0);
                if (s0 >= 0f) kept.Add(c0);
                if ((s0 >= 0f) != (s1 >= 0f)) kept.Add(Vector2.Lerp(c0, c1, s0 / (s0 - s1)));
            }
            return kept;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static string PathOf(Transform t, Transform root)
        {
            string path = t.name;
            for (Transform x = t.parent; x != null && x != root; x = x.parent) path = x.name + "/" + path;
            return path;
        }
    }
}
