// WorkshopOutpost.prefab: the workshop as something a level designer drops into a chunk scene. It is an Outpost
// already set up (docs/AI/systems/Outposts.md): the workshop prefab, four Shellbacks and their culture, and the
// settlement's own identity. Place it, turn it so the gate faces where it should, press Generate + Bake World NavMesh.
//
// Generate is what makes the workshop work in the world, which is why this is an Outpost and not the workshop itself:
// it stands the building on flattened ground, wraps it in the NetworkObject its rooms, hoist and gate talk over
// (a NetworkObject may not live in the prefab), and settles the residents in its beds and posts.
//
// Made the first time and only updated in place after that, so instances in chunk scenes keep their overrides.
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents.Residents;
using SpaceGame.Core.Persistence;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class WorkshopOutpostBuilder
    {
        public const string PrefabPath = "Assets/Game/Prefabs/Environment/Structures/Workshop/WorkshopOutpost.prefab";
        public const int ResidentCount = 4;
        private const string ResidentPath = "Assets/Game/Prefabs/Agents/Characters/Shellback/Shellback_Villager.prefab";
        private const string CulturePath = "Assets/Game/ScriptableObjects/Residents/ShellbackCulture.asset";

        [MenuItem("Tools/SpaceGame/Workshop/Rebuild Workshop Outpost Prefab")]
        private static void RebuildMenu() => Debug.Log("[Workshop] " + Rebuild());

        /// <summary>Write or update the outpost prefab, and report what was read back.</summary>
        public static string Rebuild()
        {
            var workshop = AssetDatabase.LoadAssetAtPath<GameObject>(WorkshopPrefabBuilder.PrefabPath);
            var resident = AssetDatabase.LoadAssetAtPath<GameObject>(ResidentPath);
            var culture = AssetDatabase.LoadAssetAtPath<SettlementCulture>(CulturePath);
            if (workshop == null || resident == null || culture == null)
                return "missing the workshop or Shellback prefab, or the Shellback culture";

            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("WorkshopOutpost");
            try
            {
                Configure(root, workshop, resident, culture);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }

            return Verify(workshop, resident, culture);
        }

        private static void Configure(GameObject root, GameObject workshop, GameObject resident, SettlementCulture culture)
        {
            if (!root.TryGetComponent(out Outpost outpost)) outpost = root.AddComponent<Outpost>();
            var so = new SerializedObject(outpost);
            SerializedFields.Set(so, "outpostPrefab", workshop);
            SerializedFields.Set(so, "culture", culture);
            SerializedProperty residents = so.FindProperty("residents");
            residents.arraySize = 1;
            SerializedProperty entry = residents.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("prefab").objectReferenceValue = resident;
            entry.FindPropertyRelative("count").intValue = ResidentCount;
            entry.FindPropertyRelative("spawnChance").floatValue = 1f;
            so.ApplyModifiedPropertiesWithoutUndo();

            // The settlement's identity, which its residents are known by across chunk loads. External: an identity,
            // not a world record (docs/AI/systems/Residents.md). Each placed copy stamps its own id in the scene.
            if (!root.TryGetComponent(out SaveableEntity identity)) identity = root.AddComponent<SaveableEntity>();
            var entity = new SerializedObject(identity);
            entity.FindProperty("scope").enumValueIndex = (int)SaveScope.External;
            entity.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string Verify(GameObject workshop, GameObject resident, SettlementCulture culture)
        {
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Outpost outpost = saved != null ? saved.GetComponent<Outpost>() : null;
            if (outpost == null) return "NOT SAVED: the outpost prefab cannot be read back";

            var so = new SerializedObject(outpost);
            SerializedProperty entry = so.FindProperty("residents").GetArrayElementAtIndex(0);
            SaveableEntity identity = saved.GetComponent<SaveableEntity>();
            bool right = so.FindProperty("outpostPrefab").objectReferenceValue == workshop &&
                         so.FindProperty("culture").objectReferenceValue == culture &&
                         entry.FindPropertyRelative("prefab").objectReferenceValue == resident &&
                         entry.FindPropertyRelative("count").intValue == ResidentCount &&
                         identity != null && identity.Scope == SaveScope.External;
            return right ? $"{PrefabPath} saved: the workshop, {ResidentCount} Shellbacks, an External identity"
                         : $"{PrefabPath} READ BACK WRONG";
        }
    }
}
