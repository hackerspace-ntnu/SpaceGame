// Builds the Sky Tribe's escort vessels and the fleet prefab that gathers them
// round the Sky City, their flagship.
//
// The vessels come out of Blender via
// Assets/Game/Art/Models/_Source~/models/vehicles/sky_fleet_export.py, one FBX
// each, already shrunk to scale and centred on their origin. Each becomes its own
// prefab - they are their own ships - and SkyCityFleet.prefab nests the Sky City
// prefab with the three placed round it. Build the city first
// (Tools > Environment > Build Sky City Prefab); this reuses it, not a copy.
//
// Re-run from: Tools > Environment > Build Sky Fleet Prefabs
//
// Collision is by renderer rule, like the buildings: nobody walks these ships
// yet, so they need to be solid to fly into and land on, not to walk round. Gas
// bags and hulls are convex; the cage, the engines' ducts and the gondola decks
// are mesh colliders, because a hull fills what they are open around.
//
// Multiplayer / persistence -- static scene geometry with no state, like the
// city: no NetworkObject, no saver. See StaticPropBuilder.MarkStatic.
using System.Linq;
using UnityEditor;
using UnityEngine;
using Fit = SpaceGame.EditorTools.StaticPropBuilder.Fit;
using NamedFit = SpaceGame.EditorTools.StaticPropBuilder.NamedFit;

namespace SpaceGame.EditorTools
{
    public static class SkyFleetBuilder
    {
        public const string FleetPrefabPath = "Assets/Game/Prefabs/Environment/Structures/SkyCityFleet.prefab";
        private const string FbxFolder = "Assets/Game/Art/Models/Environment/Structures/SkyFleet";
        private const string PrefabFolder = "Assets/Game/Prefabs/Environment/Structures/SkyFleet";
        private const string FleetRootName = "SkyCityFleet";

        // One cull level, as on the city.
        private const float LodCullRatio = 0.02f;

        public struct Vessel
        {
            public string Name;
            public string Fbx;
            /// <summary>Where the fleet prefab holds it, relative to the city's origin.</summary>
            public Vector3 Position;
            public float Yaw;

            public string PrefabPath => $"{PrefabFolder}/{Name}.prefab";
            public string FbxPath => $"{FbxFolder}/{Fbx}.fbx";
        }

        // Round the flagship at three heights, clear of the reach of its sails and
        // outriggers (x -49..46; the ships are ~36 m across, more when yawed): the
        // freighter high on one flank, the skiff forward and the tug low astern on
        // the other. Unity space, metres; the build warns if one overlaps the city.
        public static readonly Vessel[] Vessels =
        {
            new Vessel { Name = "SkyFreighter", Fbx = "sky_freighter", Position = new Vector3(78f, 26f, -8f), Yaw = 8f },
            new Vessel { Name = "SkySkiff", Fbx = "sky_skiff", Position = new Vector3(-80f, 8f, 38f), Yaw = -12f },
            new Vessel { Name = "SkyTug", Fbx = "sky_tug", Position = new Vector3(-78f, -14f, -40f), Yaw = 20f },
        };

        private static readonly NamedFit[] Rules =
        {
            new NamedFit { Match = "Mesh_SkyCity_Bag", Fit = Fit.Convex, Note = "gas envelope" },
            new NamedFit { Match = "Mesh_SkyCity_Cage", Fit = Fit.Mesh, Note = "ring frames round the bags" },
            new NamedFit { Match = "Mesh_SkyCity_SternGear", Fit = Fit.Mesh, Note = "open engine ducts and pylons" },
            new NamedFit { Match = "Mesh_SkyCity_Under", Fit = Fit.Mesh, Note = "gondola deck and its rails" },
            new NamedFit { Match = "Mesh_SkyCity_Home", Fit = Fit.Convex, Note = "cabin capsule" },
            new NamedFit { Match = "Mesh_SkyCity_Prow", Fit = Fit.Convex, Note = "bow block" },
            new NamedFit { Match = "Mesh_SkyCity_Beacon", Fit = Fit.Convex, Note = "lantern tower" },
            new NamedFit { Match = "Cube", Fit = Fit.Box, Note = "the freighter's hull beam" },
            new NamedFit { Match = "Mesh_SkyCity_Outriggers", Fit = Fit.None, Note = "booms and rigging" },
            new NamedFit { Match = "Mesh_SkyCity_Sail", Fit = Fit.None, Note = "sailcloth" },
            new NamedFit { Match = "Mesh_SkyCity_Flag", Fit = Fit.None, Note = "pennants and mast rigs" },
        };

        [MenuItem("Tools/Environment/Build Sky Fleet Prefabs")]
        public static void Build()
        {
            var city = AssetDatabase.LoadAssetAtPath<GameObject>(SkyCityBuilder.PrefabPath);
            if (city == null)
            {
                Debug.LogError($"No {SkyCityBuilder.PrefabPath}. Build the Sky City prefab first.");
                return;
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine("SkyFleetBuilder");
            StaticPropBuilder.EnsureFolder(PrefabFolder);
            foreach (Vessel vessel in Vessels)
                report.AppendLine($"  {vessel.Name}: {BuildVessel(vessel)}");

            var fleet = new GameObject(FleetRootName);
            try
            {
                var flagship = (GameObject)PrefabUtility.InstantiatePrefab(city, fleet.transform);
                Bounds cityBounds = RendererBounds(flagship);
                foreach (Vessel vessel in Vessels)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(vessel.PrefabPath);
                    var ship = (GameObject)PrefabUtility.InstantiatePrefab(prefab, fleet.transform);
                    ship.transform.SetLocalPositionAndRotation(vessel.Position, Quaternion.Euler(0f, vessel.Yaw, 0f));
                    Bounds shipBounds = RendererBounds(ship);
                    if (shipBounds.Intersects(cityBounds))
                        report.AppendLine($"  WARNING {vessel.Name}'s bounds overlap the city's - move it in Vessels.");
                }
                PrefabUtility.SaveAsPrefabAsset(fleet, FleetPrefabPath);
                report.AppendLine($"  saved {FleetPrefabPath}");
            }
            finally
            {
                Object.DestroyImmediate(fleet);
            }
            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }

        private static string BuildVessel(Vessel vessel)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(vessel.FbxPath) == null)
                throw new System.InvalidOperationException($"No FBX at {vessel.FbxPath}. Run sky_fleet_export.py first.");

            StaticPropBuilder.ConfigureImporter(vessel.FbxPath);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(vessel.FbxPath);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                root.name = vessel.Name;
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                StaticPropBuilder.FitCounts fits = StaticPropBuilder.ApplyFits(root, Rules);
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                StaticPropBuilder.BuildLodGroup(root, renderers, LodCullRatio);
                StaticPropBuilder.MarkStatic(root);
                PrefabUtility.SaveAsPrefabAsset(root, vessel.PrefabPath);
                Vector3 size = RendererBounds(root).size;
                return $"{renderers.Length} renderers, {size.x:F1} x {size.y:F1} x {size.z:F1} m, colliders {fits}";
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>World bounds of everything drawn under <paramref name="go"/>; it must be in a scene.</summary>
        public static Bounds RendererBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
