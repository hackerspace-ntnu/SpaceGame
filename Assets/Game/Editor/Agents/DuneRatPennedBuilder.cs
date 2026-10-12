// Builds DuneRat_Penned: a prefab variant of the dune rat for a settlement pen.
//
// The wild rat roams the whole NavMesh and is Wildlife, which is Hostile toward people on sight -- a
// pen of them would charge the gate the moment it opened. The penned rat is Fauna instead (peaceful
// until hurt, ProvocationModule to turn on whoever hurt it; the base's chase and bite stay so it can),
// and its wander stays on a small patch and only picks points a complete NavMesh path leads to, so
// behind a shut gate it mills about the pen floor instead of standing at the fence for a point on the
// far side of it. It has no FleeModule, so nothing sends it at a fence in a panic.
//
// A variant, so the base rat's own tuning, rig and colliders are inherited, not copied. Everything the
// variant changes is written here and nowhere else; re-running rebuilds it.
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;

namespace SpaceGame.EditorTools
{
    public static class DuneRatPennedBuilder
    {
        public const string BasePath = "Assets/Game/Prefabs/agents/creatures/DuneRat.prefab";
        public const string VariantPath = "Assets/Game/Prefabs/agents/creatures/DuneRat_Penned.prefab";
        private const string FaunaPath = "Assets/Game/ScriptableObjects/Factions/Core/FaunaFaction.asset";

        // How far from where it stands the rat picks its next point, and how far off a candidate the NavMesh
        // may be snapped to. Small: a pen is a few metres across, and a snap that reaches across the fence
        // lands on the far island, which the reachability test then throws away.
        private const float WanderRadius = 4f;
        private const float WanderSampleDistance = 1.5f;

        [MenuItem("Tools/SpaceGame/Agents/Build Penned Dune Rat")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[DuneRatPennedBuilder] Exit Play mode first -- prefab edits made in play mode are discarded.");
                return;
            }
            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == VariantPath)
            {
                Debug.LogError("[DuneRatPennedBuilder] " + VariantPath + " is open in Prefab Mode; close it first.");
                return;
            }

            var baseRat = AssetDatabase.LoadAssetAtPath<GameObject>(BasePath);
            var fauna = AssetDatabase.LoadAssetAtPath<FactionDefinition>(FaunaPath);
            if (baseRat == null || fauna == null)
            {
                Debug.LogError("[DuneRatPennedBuilder] Missing " + (baseRat == null ? BasePath : FaunaPath) + ".");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath) == null) CreateVariant(baseRat);

            GameObject contents = PrefabUtility.LoadPrefabContents(VariantPath);
            try
            {
                Tune(contents, fauna);
                DistanceDormancyWiring.Ensure(contents);
                PrefabUtility.SaveAsPrefabAsset(contents, VariantPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            Stamp();
            Debug.Log("[DuneRatPennedBuilder] Built " + VariantPath + "\n" + NetworkPrefabRegistrar.Sync(out _, out _));
        }

        private static void CreateVariant(GameObject baseRat)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(baseRat);
            try
            {
                DistanceDormancyWiring.Ensure(instance);
                PrefabUtility.SaveAsPrefabAsset(instance, VariantPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void Tune(GameObject rat, FactionDefinition fauna)
        {
            var faction = rat.GetComponent<EntityFaction>();
            var factionData = new SerializedObject(faction);
            factionData.FindProperty("faction").objectReferenceValue = fauna;
            factionData.ApplyModifiedPropertiesWithoutUndo();

            if (rat.GetComponent<ProvocationModule>() == null) rat.AddComponent<ProvocationModule>();

            var wander = new SerializedObject(rat.GetComponent<WanderModule>());
            wander.FindProperty("limitWanderRadius").boolValue = true;
            wander.FindProperty("wanderRadius").floatValue = WanderRadius;
            wander.FindProperty("sampleDistance").floatValue = WanderSampleDistance;
            wander.FindProperty("onlyReachableDestinations").boolValue = true;
            wander.ApplyModifiedPropertiesWithoutUndo();

            SaveablePolicy.Ensure(rat, out _);
        }

        // On the saved asset, not the preview scene LoadPrefabContents gives: the variant's own prefabId
        // is its asset GUID, and NetworkObject can only derive its GlobalObjectIdHash from a real asset
        // (a hash of 0 would pass for the first prefab to register it and break every other).
        private static void Stamp()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
            var entity = new SerializedObject(asset.GetComponent<SaveableEntity>());
            entity.FindProperty("prefabId").stringValue = AssetDatabase.AssetPathToGUID(VariantPath);
            entity.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(asset.GetComponent<NetworkObject>());
            PrefabUtility.SavePrefabAsset(asset);
            AssetDatabase.ImportAsset(VariantPath, ImportAssetOptions.ForceUpdate);
        }
    }
}
