// Places the camp the workshop's gear thieves hole up in: the scavenger outpost, with nobody living at it, marked as a
// GearThiefCamp, far out in the dunes south of the workshop's gate (docs/AI/systems/WorkshopMission.md). Generates it
// (the ground is flattened under it), saves its chunk scene, and records where it is in the GearThiefCampSite the
// mission reads; then re-bake the world NavMesh.
//
// The site was chosen 2026-10-11 off the terrain and the world NavMesh, asked for "a good distance from the gate":
// ~730 m from the gate's Shellback stand, a 722 m walk with no detour, under 0.3 m of relief over 40 m, in an empty
// chunk (Chunk_5_1). Run again to regenerate the camp; a camp recorded in another chunk is taken away from there first
// (its ground restored), so there is only ever one.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class GearThiefCampPlacer
    {
        private const string ChunkPath = "Assets/Game/Scenes/world/Chunks/Chunk_5_1.unity";
        private const string OutpostPath = "Assets/Game/Prefabs/Environment/Structures/Outpost/Outpost_Scavenger_01.prefab";
        private const string CampName = "GearThiefCamp";
        private static readonly Vector2 Site = new(2565f, -95f);
        // Turned to face back north, towards the workshop the thieves robbed.
        private const float Yaw = 0f;

        [MenuItem("Tools/SpaceGame/Workshop/Place Gear Thief Camp")]
        private static void PlaceMenu() => Debug.Log("[Workshop] " + Place());

        /// <summary>Place (or regenerate) the camp and save its chunk scene. Returns what was done.</summary>
        public static string Place()
        {
            if (EditorApplication.isPlaying) return "not in Play mode";

            GearThiefCampSite site = WorkshopMissionBuilder.EnsureCampSite();
            string removed = site.IsPlaced && site.ChunkScenePath != ChunkPath ? RemoveFrom(site.ChunkScenePath) : string.Empty;

            Scene scene = SceneManager.GetSceneByPath(ChunkPath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ChunkPath, OpenSceneMode.Additive);
            try
            {
                GearThiefCamp camp = FindCamp(scene);
                bool made = camp == null;
                if (made) camp = MakeCamp(scene);

                SettlementEditor.Generate(camp.GetComponent<Outpost>());
                if (!EditorSceneManager.SaveScene(scene)) return $"could not save {ChunkPath}";

                site.Record(camp.transform.position, ChunkPath);
                EditorUtility.SetDirty(site);
                AssetDatabase.SaveAssetIfDirty(site);

                int posts = camp.Posts().Count;
                return $"{removed}{(made ? "placed" : "regenerated")} the gear thieves' camp at {camp.transform.position} in " +
                       $"{scene.name}: {posts} posts, gear rest at {camp.GearRest.position}. Re-bake the world NavMesh.";
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>Takes the camp out of <paramref name="scenePath"/>, its ground restored, and saves the scene.</summary>
        private static string RemoveFrom(string scenePath)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                GearThiefCamp old = FindCamp(scene);
                if (old == null) return string.Empty;
                old.GetComponent<Outpost>().Clear();
                Object.DestroyImmediate(old.gameObject);
                EditorSceneManager.SaveScene(scene);
                return $"took the old camp out of {scene.name}; ";
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static GearThiefCamp FindCamp(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out GearThiefCamp camp)) return camp;
            return null;
        }

        private static GearThiefCamp MakeCamp(Scene scene)
        {
            var go = new GameObject(CampName);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetPositionAndRotation(new Vector3(Site.x, GroundAt(Site), Site.y), Quaternion.Euler(0f, Yaw, 0f));

            Outpost outpost = go.AddComponent<Outpost>();
            var so = new SerializedObject(outpost);
            so.FindProperty("outpostPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(OutpostPath);
            so.FindProperty("residents").arraySize = 0;
            so.FindProperty("culture").objectReferenceValue = null;   // nobody lives here: the thieves are the mission's
            so.ApplyModifiedPropertiesWithoutUndo();

            return go.AddComponent<GearThiefCamp>();
        }

        private static float GroundAt(Vector2 xz)
        {
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                Vector3 origin = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (xz.x < origin.x || xz.x > origin.x + size.x || xz.y < origin.z || xz.y > origin.z + size.z) continue;
                return terrain.SampleHeight(new Vector3(xz.x, 0f, xz.y)) + origin.y;
            }
            return 0f;
        }
    }
}
