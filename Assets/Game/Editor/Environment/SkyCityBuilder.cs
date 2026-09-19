// Builds the Sky City prefab from sky_city.fbx, colliders and all.
//
// The model comes out of Blender via
// Assets/Game/Art/Models/_Source~/models/vehicles/sky_city_export.py. Everything
// below that -- import settings, the collider set, the ladder markers, the cull
// LODGroup, static flags and the prefab -- is generated here rather than
// hand-authored, because a prefab wired by hand is a prefab nobody can rebuild
// after the model changes.
//
// Re-running is safe and is the intended workflow: re-export the FBX, run this,
// and the prefab and its collision hulls are rebuilt in place.
//
// Re-run from: Tools > Environment > Build Sky City Prefab
//
// ---------------------------------------------------------------------------
// WHERE THE COLLISION COMES FROM
//
// Not from renderer bounds. The city's walkways, stairs, railings and houses are
// merged meshes, leaning scrap and sagging rope; a box over any of them is either
// a slab in mid-air or a wall across a path. So collision comes from three places,
// and all three are checked in Blender, before export, by walking every route a
// player can take (sky_city_traversal.verify):
//
//   * COL_SkyCity_#### -- the collision set sky_city_traversal authors, one
//     convex island per object: every floor, stair ramp, railing, wall, house,
//     tower and crane footprint. An eight-corner axis-aligned island becomes a
//     BoxCollider, anything else a convex MeshCollider on a hull mesh saved to
//     HullsPath. The island objects themselves are deleted.
//   * The RULE TABLE below -- for renderers whose own shape is the right
//     collision: the gas bags as convex hulls, the cage, cradles, old deck and
//     stern gear as static mesh colliders, crates and the stern handrails as
//     boxes. sky_city_traversal mirrors this table (MESH_COLLIDED,
//     BOX_COLLIDED) so its route check tests exactly what ships. Change both.
//   * Nothing else. Cloth, rope, washing, lamps, flags, the outriggers' rigging
//     (which passes through the castle's rooms) and everything the island set
//     already covers get no collider, by listed rule, not by omission.
//
// LADDERS -- LAD_SkyCity_## transforms are kept under Ladders, each with a _Top
// child (where a climber steps off), an _Exit child (the floor they step onto)
// and a Ladder component, which LadderClimber on the player climbs - from the
// bottom, or from the top by walking into the gap its rails leave.
//
// SCALE -- the city is modelled, and route-checked in Blender, at 1 unit = 1 m,
// and ships at Scale on the prefab root. The route check's player is a 2.0 m
// capsule with 2.1 m of headroom; the real player is 3.0 m tall (a 2 m capsule
// on a transform stretched 1.5 in Y, see PlayerCharacter.md), so every ceiling
// the check passed has to grow by at least 3.0 / 2.1 = 1.43. Everything scales
// with it, colliders and ladder markers included. Scaling only widens what the
// check passed, with one cost: every lip between surfaces grows by the same
// factor (the player has no step-up). Ladder-top gaps (0.7 m modelled) end up
// wider than the 1.0 m-wide player, which is why a Ladder can be taken hold of
// from the top.
//
// Multiplayer / persistence -- static scene geometry with no state: no
// NetworkObject, no saver. See StaticPropBuilder.MarkStatic.
// ---------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Fit = SpaceGame.EditorTools.StaticPropBuilder.Fit;
using NamedFit = SpaceGame.EditorTools.StaticPropBuilder.NamedFit;

namespace SpaceGame.EditorTools
{
    public static class SkyCityBuilder
    {
        public const string FbxPath =
            "Assets/Game/Art/Models/Environment/Structures/sky_city.fbx";
        public const string PrefabPath =
            "Assets/Game/Prefabs/Environment/Structures/SkyCity.prefab";
        public const string HullsPath =
            "Assets/Game/Prefabs/Environment/Structures/SkyCity_CollisionHulls.asset";
        public const string CollisionPrefix = "COL_SkyCity_";
        public const string CollisionGroup = "Collision";
        public const string LadderPrefix = "LAD_SkyCity_";
        public const string LadderGroup = "Ladders";
        public const string LadderTop = "_Top";
        public const string LadderExit = "_Exit";
        private const string RootName = "SkyCity";

        /// <summary>Uniform scale on the prefab root. See SCALE above for its upper bound.</summary>
        public const float Scale = 1.5f;

        // Points of one island closer than this are one corner: Blender writes a
        // box's corners exactly, and the import splits them per face.
        private const float CornerTolerance = 0.001f;

        // One cull level, as on the buildings -- there are no decimated meshes
        // to hang a real chain off. See StaticPropBuilder.BuildLodGroup.
        private const float LodCullRatio = 0.02f;

        // -------------------------------------------------------------------
        // Per-renderer rules. Ordered: the FIRST prefix match wins. Every
        // renderer is Mesh_SkyCity_*.
        // -------------------------------------------------------------------
        private static readonly NamedFit[] Rules =
        {
            // Shape is the collision.
            new NamedFit { Match = "Mesh_SkyCity_Bag", Fit = Fit.Convex, Note = "gas envelope, an ovoid fills half its box" },
            new NamedFit { Match = "Mesh_SkyCity_Cage", Fit = Fit.Mesh, Note = "ring frames curve down over the lanes' inner edge" },
            new NamedFit { Match = "Mesh_SkyCity_Cradles", Fit = Fit.Mesh, Note = "bag saddles and hoops beside the lanes" },
            new NamedFit { Match = "Mesh_SkyCity_Decks", Fit = Fit.Mesh, Note = "lane lamps, deck edges; the islands carry the floor" },
            new NamedFit { Match = "Mesh_SkyCity_SternGear", Fit = Fit.Mesh, Note = "pylons, ducts and rudder at the stern piers" },
            new NamedFit { Match = "Mesh_SkyCity_Stow", Fit = Fit.Box, MinHeight = 1.2f, Note = "crate stacks; barrels are walked past" },
            new NamedFit { Match = "Mesh_SkyCity_Rail", Fit = Fit.Box, Note = "stern apron handrails, straight kit parts" },

            // Covered by the COL_SkyCity islands.
            new NamedFit { Match = "Mesh_SkyCity_BowCastle", Fit = Fit.None, Note = "hollow castle: walls, floors, doors" },
            new NamedFit { Match = "Mesh_SkyCity_CastleClimb", Fit = Fit.None, Note = "castle stairs, landings, terraces" },
            new NamedFit { Match = "Mesh_SkyCity_SternBlock", Fit = Fit.None, Note = "stern tower, terrace and rim" },
            new NamedFit { Match = "Mesh_SkyCity_SternTerrace", Fit = Fit.None, Note = "terrace railings" },
            new NamedFit { Match = "Mesh_SkyCity_SternSupports", Fit = Fit.None, Note = "columns" },
            new NamedFit { Match = "Mesh_SkyCity_Keel", Fit = Fit.None, Note = "girder under the deck" },
            new NamedFit { Match = "Mesh_SkyCity_Prow", Fit = Fit.None, Note = "inside the castle's ground floor" },
            new NamedFit { Match = "Mesh_SkyCity_Gantry", Fit = Fit.None, Note = "crown gantry deck, railings, galleries, balconies" },
            new NamedFit { Match = "Mesh_SkyCity_Beacon", Fit = Fit.None, Note = "tower bodies and galleries" },
            new NamedFit { Match = "Mesh_SkyCity_Dome", Fit = Fit.None, Note = "tower bodies and galleries" },
            new NamedFit { Match = "Mesh_SkyCity_Dish", Fit = Fit.None, Note = "dish plinth and gallery" },
            new NamedFit { Match = "Mesh_SkyCity_Walk", Fit = Fit.None, Note = "platforms, skywalks, piers, slots, underlays" },
            new NamedFit { Match = "Mesh_SkyCity_Under", Fit = Fit.None, Note = "under-keel walkways" },
            new NamedFit { Match = "Mesh_SkyCity_SkywalkStair", Fit = Fit.None, Note = "stairs, posts" },
            new NamedFit { Match = "Mesh_SkyCity_Crossings", Fit = Fit.None, Note = "keel bridges, porch" },
            new NamedFit { Match = "Mesh_SkyCity_LaneRail", Fit = Fit.None, Note = "lane railings and poles" },
            new NamedFit { Match = "Mesh_SkyCity_ShaftLanding", Fit = Fit.None, Note = "ladder landings on the gantry" },
            new NamedFit { Match = "Mesh_SkyCity_RoofTerrace", Fit = Fit.None, Note = "roof railings" },
            new NamedFit { Match = "Mesh_SkyCity_Ladder", Fit = Fit.None, Note = "ladder rails" },
            new NamedFit { Match = "Mesh_SkyCity_Home", Fit = Fit.None, Note = "houses, as boxes turned with them" },
            new NamedFit { Match = "Mesh_SkyCity_Facade", Fit = Fit.None, Note = "street walls" },
            new NamedFit { Match = "Mesh_SkyCity_Stilts", Fit = Fit.None, Note = "posts under raised shanties" },
            new NamedFit { Match = "Mesh_SkyCity_Crane", Fit = Fit.None, Note = "crane footprints; booms and loads overhead" },
            new NamedFit { Match = "Mesh_SkyCity_Goods", Fit = Fit.None, Note = "pile footprints" },
            new NamedFit { Match = "Mesh_SkyCity_Stock", Fit = Fit.None, Note = "pile footprints" },
            new NamedFit { Match = "Mesh_SkyCity_Shade", Fit = Fit.None, Note = "market awnings" },
            new NamedFit { Match = "Mesh_SkyCity_Flood", Fit = Fit.None, Note = "floodlights" },

            // Walked under, past or through.
            new NamedFit { Match = "Mesh_SkyCity_StreetLife", Fit = Fit.None, Note = "washing, cables, plants, stalls - stalls and stoves are islands" },
            new NamedFit { Match = "Mesh_SkyCity_Outriggers", Fit = Fit.None, Note = "rigging runs through the castle's rooms" },
            new NamedFit { Match = "Mesh_SkyCity_Climbs", Fit = Fit.None, Note = "old ladders" },
            new NamedFit { Match = "Mesh_SkyCity_Sail", Fit = Fit.None, Note = "sailcloth" },
            new NamedFit { Match = "Mesh_SkyCity_Flag", Fit = Fit.None, Note = "pennants and mast rigs" },
            new NamedFit { Match = "Mesh_SkyCity_Lamp", Fit = Fit.None, Note = "lanterns on the cage legs" },
        };

        [MenuItem("Tools/Environment/Build Sky City Prefab")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) == null)
            {
                Debug.LogError($"No FBX at {FbxPath}. Run sky_city_export.py first.");
                return;
            }

            StaticPropBuilder.ConfigureImporter(FbxPath);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = RootName;
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var report = new System.Text.StringBuilder();
            report.AppendLine("SkyCityBuilder");
            try
            {
                Vector3 ls = root.transform.lossyScale;
                if ((ls - Vector3.one).sqrMagnitude > 1e-6f)
                    throw new System.InvalidOperationException(
                        $"{RootName} imported at lossyScale {ls:F4}, not 1 - the collision islands are in metres.");

                CollisionCounts islands = BuildIslandColliders(root);
                // The hulls must be on disk before the prefab that references them.
                AssetDatabase.SaveAssets();
                int ladders = GatherLadders(root);
                StaticPropBuilder.FitCounts rules = StaticPropBuilder.ApplyFits(root, Rules);

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                StaticPropBuilder.BuildLodGroup(root, renderers, LodCullRatio);
                StaticPropBuilder.MarkStatic(root);
                root.transform.localScale = Vector3.one * Scale;

                StaticPropBuilder.EnsureFolder(
                    System.IO.Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);

                int tris = renderers
                    .Select(r => r.GetComponent<MeshFilter>())
                    .Where(mf => mf != null && mf.sharedMesh != null)
                    .Sum(mf => mf.sharedMesh.triangles.Length / 3);
                report.AppendLine($"  {renderers.Length} renderers, {tris} tris, root scale {Scale}");
                report.AppendLine(
                    $"  collision islands: {islands.Boxes} box + {islands.Hulls} convex hull" +
                    (islands.Degenerate > 0 ? $", {islands.Degenerate} DEGENERATE skipped" : ""));
                report.AppendLine($"  renderer rules: {rules}");
                report.AppendLine($"  {ladders} ladder markers under {LadderGroup}");
                report.AppendLine($"  saved {PrefabPath} and {HullsPath}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }

        // -------------------------------------------------------------------
        // COL_SkyCity_#### islands
        // -------------------------------------------------------------------

        private struct CollisionCounts
        {
            public int Boxes;
            public int Hulls;
            public int Degenerate;
        }

        private static CollisionCounts BuildIslandColliders(GameObject root)
        {
            var counts = new CollisionCounts();
            var sources = root.GetComponentsInChildren<MeshFilter>(true)
                .Where(mf => mf.name.StartsWith(CollisionPrefix, System.StringComparison.Ordinal))
                .OrderBy(mf => mf.name, System.StringComparer.Ordinal)
                .ToList();
            if (sources.Count == 0)
                throw new System.InvalidOperationException(
                    $"No {CollisionPrefix}* objects in {FbxPath} - export it with sky_city_export.py.");

            AssetDatabase.DeleteAsset(HullsPath);
            var group = new GameObject(CollisionGroup).transform;
            group.SetParent(root.transform, false);
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

                string id = mf.name.Substring(CollisionPrefix.Length);
                if (IsAxisAlignedBox(corners, out Bounds box))
                {
                    var go = new GameObject("Box_" + id);
                    go.transform.SetParent(group, false);
                    go.transform.localPosition = box.center;
                    go.AddComponent<BoxCollider>().size = box.size;
                    counts.Boxes++;
                }
                else
                {
                    Vector3 centre = corners.Aggregate(Vector3.zero, (a, b) => a + b) / corners.Count;
                    var hull = new Mesh { name = "SkyCityHull_" + id };
                    hull.SetVertices(corners.Select(c => c - centre).ToList());
                    hull.SetTriangles(HullTriangles(mesh, toRoot, corners), 0);
                    hull.RecalculateBounds();
                    if (hullAsset == null)
                    {
                        AssetDatabase.CreateAsset(hull, HullsPath);
                        hullAsset = hull;
                    }
                    else
                    {
                        AssetDatabase.AddObjectToAsset(hull, hullAsset);
                    }

                    var go = new GameObject("Hull_" + id);
                    go.transform.SetParent(group, false);
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

        // The island's distinct corners: the import splits a vertex once per face
        // it belongs to, and these are the same point written again.
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

        // The island's own triangles, re-indexed onto its distinct corners. A
        // convex MeshCollider cooks its hull from the vertices; the triangles are
        // kept so the mesh is also a truthful picture of the collider when drawn.
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

        // -------------------------------------------------------------------
        // Ladders
        // -------------------------------------------------------------------

        private static int GatherLadders(GameObject root)
        {
            var group = new GameObject(LadderGroup).transform;
            group.SetParent(root.transform, false);
            var ladders = root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith(LadderPrefix, System.StringComparison.Ordinal)
                            && !t.name.EndsWith(LadderTop, System.StringComparison.Ordinal)
                            && !t.name.EndsWith(LadderExit, System.StringComparison.Ordinal))
                .ToList();
            foreach (Transform ladder in ladders)
            {
                Transform top = ladder.Find(ladder.name + LadderTop);
                Transform exit = ladder.Find(ladder.name + LadderExit);
                if (top == null || exit == null)
                    throw new System.InvalidOperationException(
                        $"{ladder.name} has no {LadderTop}/{LadderExit} child - re-export with sky_city_export.py.");
                ladder.SetParent(group, true);
                ladder.gameObject.AddComponent<SpaceGame.Gameplay.Ladder>().Configure(top, exit);
            }
            return ladders.Count;
        }
    }
}
