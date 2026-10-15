// Turns the marker objects an export script leaves in an FBX into the components they stand for.
//
//   COL_<Model>_####   collision islands. An island whose distinct corners are the 8 corners of an
//                      axis-aligned box becomes a BoxCollider; anything else becomes a convex
//                      MeshCollider whose hull mesh is saved to one asset. The island objects are
//                      deleted. Islands are authored split, one per convex piece, in Blender — never
//                      one mesh split here (ArtPipeline.md).
//   LAD_<Name>         ladder feet, each with <Name>_Top and <Name>_Exit children. Each becomes a
//                      Ladder, configured from those two points.
//
// Shared by SkyCityBuilder and DuneBargeBuilder. Build islands with the model at lossy scale 1 —
// the islands are in metres — and scale the model afterwards; the colliders scale with it.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class ModelMarkerImport
    {
        public const string LadderTop = "_Top";
        public const string LadderExit = "_Exit";

        // Distinct-corner tolerance: the import splits a vertex once per face it belongs to.
        private const float CornerTolerance = 0.001f;

        public struct CollisionCounts
        {
            public int Boxes;
            public int Hulls;
            public int Degenerate;

            public override string ToString() =>
                $"{Boxes} box + {Hulls} convex hull" + (Degenerate > 0 ? $", {Degenerate} DEGENERATE skipped" : "");
        }

        /// <summary>Replaces every `prefix`* mesh under `root` with a collider under a new `group`
        /// child. Hull meshes go to `hullsPath` (rewritten). Throws if there are no islands.</summary>
        public static CollisionCounts BuildIslandColliders(GameObject root, string prefix, string group,
                                                           string hullsPath, string exporter)
        {
            var counts = new CollisionCounts();
            var sources = root.GetComponentsInChildren<MeshFilter>(true)
                .Where(mf => mf.name.StartsWith(prefix, System.StringComparison.Ordinal))
                .OrderBy(mf => mf.name, System.StringComparer.Ordinal)
                .ToList();
            if (sources.Count == 0)
                throw new System.InvalidOperationException($"No {prefix}* objects under {root.name} - export it with {exporter}.");

            AssetDatabase.DeleteAsset(hullsPath);
            var parent = new GameObject(group).transform;
            parent.SetParent(root.transform, false);
            Object hullAsset = null;

            foreach (MeshFilter mf in sources)
            {
                Mesh mesh = mf.sharedMesh;
                Matrix4x4 toRoot = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                List<Vector3> corners = Corners(mesh.vertices.Select(v => toRoot.MultiplyPoint3x4(v)));
                if (corners.Count < 4)
                {
                    counts.Degenerate++;
                    continue;
                }

                string id = mf.name.Substring(prefix.Length);
                if (IsAxisAlignedBox(corners, out Bounds box))
                {
                    var go = new GameObject("Box_" + id);
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = box.center;
                    go.AddComponent<BoxCollider>().size = box.size;
                    counts.Boxes++;
                }
                else
                {
                    Vector3 centre = corners.Aggregate(Vector3.zero, (a, b) => a + b) / corners.Count;
                    var hull = new Mesh { name = root.name + "Hull_" + id };
                    hull.SetVertices(corners.Select(c => c - centre).ToList());
                    hull.SetTriangles(HullTriangles(mesh, toRoot, corners), 0);
                    hull.RecalculateBounds();
                    if (hullAsset == null)
                    {
                        AssetDatabase.CreateAsset(hull, hullsPath);
                        hullAsset = hull;
                    }
                    else
                    {
                        AssetDatabase.AddObjectToAsset(hull, hullAsset);
                    }

                    var go = new GameObject("Hull_" + id);
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = centre;
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = hull;
                    mc.convex = true;
                    counts.Hulls++;
                }
                Object.DestroyImmediate(mf.gameObject);
            }
            return counts;
        }

        /// <summary>Adds a configured Ladder to every `prefix`* marker and gathers them under a new
        /// `group` child of `root`. Throws on a marker missing its _Top or _Exit child.</summary>
        public static int GatherLadders(GameObject root, string prefix, string group, string exporter)
        {
            var parent = new GameObject(group).transform;
            parent.SetParent(root.transform, false);
            var ladders = root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith(prefix, System.StringComparison.Ordinal)
                            && !t.name.EndsWith(LadderTop, System.StringComparison.Ordinal)
                            && !t.name.EndsWith(LadderExit, System.StringComparison.Ordinal))
                .ToList();
            foreach (Transform ladder in ladders)
            {
                Transform top = ladder.Find(ladder.name + LadderTop);
                Transform exit = ladder.Find(ladder.name + LadderExit);
                if (top == null || exit == null)
                    throw new System.InvalidOperationException(
                        $"{ladder.name} has no {LadderTop}/{LadderExit} child - re-export with {exporter}.");
                ladder.SetParent(parent, true);
                ladder.gameObject.AddComponent<SpaceGame.Gameplay.Ladder>().Configure(top, exit);
            }
            return ladders.Count;
        }

        private static List<Vector3> Corners(IEnumerable<Vector3> points)
        {
            var corners = new List<Vector3>();
            foreach (Vector3 p in points)
            {
                if (!corners.Any(c => (c - p).sqrMagnitude < CornerTolerance * CornerTolerance))
                    corners.Add(p);
            }
            return corners;
        }

        private static bool IsAxisAlignedBox(List<Vector3> corners, out Bounds bounds)
        {
            bounds = new Bounds(corners[0], Vector3.zero);
            foreach (Vector3 c in corners) bounds.Encapsulate(c);
            if (corners.Count != 8) return false;
            Bounds b = bounds;
            return corners.All(c =>
                OnEither(c.x, b.min.x, b.max.x) && OnEither(c.y, b.min.y, b.max.y) && OnEither(c.z, b.min.z, b.max.z));
        }

        private static bool OnEither(float v, float a, float b) =>
            Mathf.Abs(v - a) < CornerTolerance || Mathf.Abs(v - b) < CornerTolerance;

        // The island's own triangles, re-indexed onto its distinct corners. A convex MeshCollider cooks
        // its hull from the vertices; the triangles keep the mesh a truthful picture of the collider.
        private static List<int> HullTriangles(Mesh mesh, Matrix4x4 toRoot, List<Vector3> corners)
        {
            Vector3[] v = mesh.vertices;
            int[] source = mesh.triangles;
            var tris = new List<int>(source.Length);
            foreach (int i in source)
            {
                Vector3 p = toRoot.MultiplyPoint3x4(v[i]);
                tris.Add(corners.FindIndex(c => (c - p).sqrMagnitude < CornerTolerance * CornerTolerance));
            }
            return tris;
        }
    }
}
