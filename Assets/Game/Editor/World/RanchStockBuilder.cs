// Builds the ranch's three prefab variants:
//   Esko_Ranch  — the tame esko kept in the yard: has to be won over (CreatureTrust) before it takes a saddle,
//                     and grazes a small home patch so it is still in the yard when the crew get there.
//   Esko_Wild   — one of the wild herd (RanchStock): grazes its range, bolts from anybody who comes close at a
//                     lumbering trot a galloping rider can run down, and is just as wary of a saddle.
//   Raxy_Wrangler   — the drover who works the ranch (RanchWrangler) and says its quest's lines (QuestVoice).
// Both eskos weigh a real animal's weight, so on foot a roped one drags the player and from the saddle the mount
// holds it (LassoArtifact.HolderMass).
//
// Variants, so the base prefabs' own tuning, rigs and colliders are inherited, not copied. Everything a variant changes
// is written here and nowhere else; re-running rebuilds them.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class RanchStockBuilder
    {
        private const string EskoPath = "Assets/Game/Prefabs/Agents/creatures/Esko.prefab";
        private const string DroverPath = "Assets/Game/Prefabs/Agents/Characters/Raxy/Raxy_Job_Drover.prefab";
        public const string TamePath = "Assets/Game/Prefabs/Agents/creatures/Esko_Ranch.prefab";
        public const string WildPath = "Assets/Game/Prefabs/Agents/creatures/Esko_Wild.prefab";
        public const string WranglerPath = "Assets/Game/Prefabs/Agents/Characters/Raxy/Raxy_Wrangler.prefab";
        public const string SitePath = "Assets/Game/ScriptableObjects/Ranch/RaxyRanchSite.asset";
        public const string QuestPath = "Assets/Game/ScriptableObjects/Objectives/Ranch/RaxyRanchQuest.asset";

        // What an esko weighs, kilograms. The base prefab's kinematic body says 1, which the lasso reads as a 2 kg
        // animal a player tows like a toy; this is an eight-metre grazer.
        private const float StockMass = 700f;

        // The tame one: a few pets win it over, and it keeps to a patch of yard this big.
        private const int TamePetsToTame = 3;
        private const float TameHomeRadius = 8f;

        // The wild ones: twice the patience, a range this big, and they bolt from anybody this close and keep going to
        // the safe radius — at a share of their run a galloping rider closes on.
        private const int WildPetsToTame = 6;
        private const float WildHomeRadius = 30f;
        private const float WildFleeTrigger = 16f;
        private const float WildFleeSafe = 34f;
        private const float WildFleeSpeed = 0.7f;

        [MenuItem("Tools/SpaceGame/World/Build Ranch Stock")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[RanchStockBuilder] Exit Play mode first -- prefab edits made in play mode are discarded.");
                return;
            }

            string open = PrefabStageUtility.GetCurrentPrefabStage()?.assetPath;
            if (open == TamePath || open == WildPath || open == WranglerPath)
            {
                Debug.LogError("[RanchStockBuilder] " + open + " is open in Prefab Mode; close it first.");
                return;
            }

            var site = AssetDatabase.LoadAssetAtPath<RanchSite>(SitePath);
            var quest = AssetDatabase.LoadAssetAtPath<ObjectiveChain>(QuestPath);
            if (site == null || quest == null)
            {
                Debug.LogError("[RanchStockBuilder] Missing " + (site == null ? SitePath : QuestPath) + ".");
                return;
            }

            if (!Variant(EskoPath, TamePath, horn => TuneStock(horn, TamePetsToTame, TameHomeRadius, "Esko")) ||
                !Variant(EskoPath, WildPath, TuneWild) ||
                !Variant(DroverPath, WranglerPath, drover => TuneWrangler(drover, site, quest)))
                return;

            Debug.Log("[RanchStockBuilder] Built " + TamePath + ", " + WildPath + ", " + WranglerPath + "\n" +
                      NetworkPrefabRegistrar.Sync(out _, out _));
        }

        private static bool Variant(string basePath, string variantPath, System.Action<GameObject> tune)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            if (source == null)
            {
                Debug.LogError("[RanchStockBuilder] Missing " + basePath + ".");
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(variantPath) == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                try
                {
                    PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(variantPath);
            try
            {
                tune(contents);
                SaveablePolicy.Ensure(contents, out _);
                DistanceDormancyWiring.Ensure(contents);
                PrefabUtility.SaveAsPrefabAsset(contents, variantPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            // Its own network hash and save prefab id, written into the file the player reads.
            CarriedGearItems.StampOnDisk(variantPath);
            return true;
        }

        private static void TuneStock(GameObject horn, int petsToTame, float homeRadius, string label)
        {
            Set(Ensure<RanchStock>(horn), "kind", k => k.enumValueIndex = (int)RanchStock.Kind.Tame);
            CreatureTrust trust = Ensure<CreatureTrust>(horn);
            Ensure<CreatureTrustSaveable>(horn);
            Set(trust, "petsToTame", p => p.intValue = petsToTame);

            Set(horn.GetComponent<WanderModule>(), "homeRadius", p => p.floatValue = homeRadius);
            horn.GetComponent<Rigidbody>().mass = StockMass;

            PettableModule pettable = horn.GetComponentInChildren<PettableModule>(true);
            if (pettable != null) Set(pettable, "label", p => p.stringValue = label);
        }

        private static void TuneWild(GameObject horn)
        {
            TuneStock(horn, WildPetsToTame, WildHomeRadius, "Wild esko");
            Set(Ensure<RanchStock>(horn), "kind", k => k.enumValueIndex = (int)RanchStock.Kind.Wild);

            var flee = new SerializedObject(horn.GetComponent<FleeModule>());
            flee.FindProperty("fleeFromCurrentTarget").boolValue = false;
            flee.FindProperty("fleeFromRelationship").enumValueIndex = (int)FactionRelationship.Neutral;
            flee.FindProperty("triggerRadius").floatValue = WildFleeTrigger;
            flee.FindProperty("safeRadius").floatValue = WildFleeSafe;
            flee.FindProperty("fleeSpeedMultiplier").floatValue = WildFleeSpeed;
            flee.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void TuneWrangler(GameObject drover, RanchSite site, ObjectiveChain quest)
        {
            Set(Ensure<RanchWrangler>(drover), "site", p => p.objectReferenceValue = site);

            var voice = new SerializedObject(Ensure<QuestVoice>(drover));
            SerializedProperty quests = voice.FindProperty("quests");
            quests.arraySize = 1;
            quests.GetArrayElementAtIndex(0).objectReferenceValue = quest;
            voice.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T Ensure<T>(GameObject target) where T : Component =>
            target.TryGetComponent(out T existing) ? existing : target.AddComponent<T>();

        private static void Set(Object target, string property, System.Action<SerializedProperty> write)
        {
            var data = new SerializedObject(target);
            write(data.FindProperty(property));
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
