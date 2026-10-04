using System.Collections.Generic;
using System.Linq;
using System.Text;
using SpaceGame.Agents.Residents;
using SpaceGame.Gameplay;
using SpaceGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools.Outposts
{
    /// <summary>
    /// Whether residents can use an outpost prefab, asked of a NavMesh baked over it alone: the settlement's own throwaway bake
    /// (<see cref="SettlementWalkableArea"/>, the world bake's settings) in a scratch scene on flat ground. Reports, for each ladder,
    /// whether both ends of its link have mesh and how large a floor the top one is on, and for each spot whether a resident can stand
    /// at it -- counting a spot on a deck as reachable when the deck's mesh holds a ladder's exit, which is how it is reached in play.
    /// The world NavMesh is never touched. <b>Tools/SpaceGame/Outposts/Check Walkability Of Selected Prefab</b>.
    /// </summary>
    public static class OutpostWalkability
    {
        // Metres: flat ground out to this far past the prefab's widest piece, so nothing on its edge stands over the void.
        private const float GroundMargin = 12f;
        private const float WeldPrecision = 100f;

        // A ladder's link starts this far out from its rungs and snaps each end to the mesh within this: Ladder's own defaults.
        private const float LadderApproach = 1f;
        private const float LinkSnap = 1f;

        [MenuItem("Tools/SpaceGame/Outposts/Check Walkability Of Selected Prefab")]
        private static void CheckSelected()
        {
            foreach (GameObject prefab in Selection.gameObjects.Where(AssetDatabase.Contains))
                Debug.Log(Check(prefab));
        }

        public static string Check(GameObject prefab)
        {
            Scene scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            Scene before = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scratch);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scratch);
                Bounds bounds = instance.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + GroundMargin;

                var ground = new GameObject("Ground");
                ground.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.AddComponent<BoxCollider>().size = new Vector3(radius * 2f, 1f, radius * 2f);
                Physics.SyncTransforms();

                using SettlementWalkableArea area = SettlementWalkableArea.Bake(Vector3.zero, radius, null);
                return area == null ? $"{prefab.name}: nothing baked" : Describe(prefab.name, instance);
            }
            finally
            {
                SceneManager.SetActiveScene(before);
                EditorSceneManager.CloseScene(scratch, removeScene: true);
            }
        }

        private static string Describe(string name, GameObject instance)
        {
            var report = new StringBuilder($"{name}:");
            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            int[] island = Islands(triangulation);
            var area = new Dictionary<int, float>();
            for (int t = 0; t < triangulation.indices.Length / 3; t++)
                area[island[t]] = (area.TryGetValue(island[t], out float a) ? a : 0f) + TriangleArea(triangulation, t);

            // The heart Generate would find: the largest walkable area the outpost's spots join, not whatever lies under its centre.
            Vector3 heart = SettlementPlaces.FindHeart(instance.transform, ResidentTuning.Instance.doorStandDistances, Vector3.zero);
            var exitIslands = new HashSet<int>();
            foreach (Ladder ladder in instance.GetComponentsInChildren<Ladder>())
            {
                bool foot = NavMesh.SamplePosition(ladder.Foot + ladder.TowardClimber * LadderApproach, out _, LinkSnap, NavMesh.AllAreas);
                bool exit = NavMesh.SamplePosition(ladder.ExitPoint, out NavMeshHit exitHit, LinkSnap, NavMesh.AllAreas);
                int exitIsland = exit ? IslandAt(triangulation, island, exitHit.position) : -1;
                if (exitIsland >= 0) exitIslands.Add(exitIsland);
                report.Append($"\n  ladder {ladder.name}: foot {(foot ? "on mesh" : "NO MESH")}, exit {(exit ? $"on a {area[exitIsland]:0.#} m2 floor" : "NO MESH")}");
            }

            int usable = 0, deckOnly = 0;
            var lost = new List<string>();
            foreach (SettlementSpot spot in instance.GetComponentsInChildren<SettlementSpot>().Where(s => s.Use != null && s.gameObject.activeInHierarchy))
            {
                if (SettlementPlaces.TryStand(spot.Position, spot.Use.elevated, heart, out _, out string why)) usable++;
                else if (NavMesh.SamplePosition(spot.Position, out NavMeshHit hit, 1.2f, NavMesh.AllAreas) && exitIslands.Contains(IslandAt(triangulation, island, hit.position))) deckOnly++;
                else lost.Add($"{spot.name} ({spot.Use.name}): {why}");
            }
            report.Append($"\n  spots: {usable} usable on the ground, {deckOnly} on a deck a ladder reaches, {lost.Count} unusable");
            foreach (string spot in lost) report.Append($"\n    {spot}");
            return report.ToString();
        }

        // Union-find over triangles that share an edge, vertices welded by position (a bake repeats a vertex per polygon).
        private static int[] Islands(NavMeshTriangulation tri)
        {
            int count = tri.indices.Length / 3;
            var parent = Enumerable.Range(0, count).ToArray();
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }

            var welded = new Dictionary<Vector3Int, int>();
            int Weld(Vector3 v)
            {
                var key = new Vector3Int(Mathf.RoundToInt(v.x * WeldPrecision), Mathf.RoundToInt(v.y * WeldPrecision), Mathf.RoundToInt(v.z * WeldPrecision));
                if (!welded.TryGetValue(key, out int id)) welded[key] = id = welded.Count;
                return id;
            }

            var edgeOwner = new Dictionary<(int, int), int>();
            for (int t = 0; t < count; t++)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = Weld(tri.vertices[tri.indices[t * 3 + e]]), b = Weld(tri.vertices[tri.indices[t * 3 + (e + 1) % 3]]);
                    (int, int) edge = a < b ? (a, b) : (b, a);
                    if (edgeOwner.TryGetValue(edge, out int other)) parent[Find(t)] = Find(other);
                    else edgeOwner[edge] = t;
                }
            }
            return Enumerable.Range(0, count).Select(Find).ToArray();
        }

        private static int IslandAt(NavMeshTriangulation tri, int[] island, Vector3 point)
        {
            int best = -1;
            float bestSqr = float.PositiveInfinity;
            for (int t = 0; t < island.Length; t++)
            {
                Vector3 centre = (tri.vertices[tri.indices[t * 3]] + tri.vertices[tri.indices[t * 3 + 1]] + tri.vertices[tri.indices[t * 3 + 2]]) / 3f;
                float sqr = (centre - point).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                best = island[t];
                bestSqr = sqr;
            }
            return best;
        }

        private static float TriangleArea(NavMeshTriangulation tri, int t) =>
            Vector3.Cross(tri.vertices[tri.indices[t * 3 + 1]] - tri.vertices[tri.indices[t * 3]], tri.vertices[tri.indices[t * 3 + 2]] - tri.vertices[tri.indices[t * 3]]).magnitude * 0.5f;
    }
}
