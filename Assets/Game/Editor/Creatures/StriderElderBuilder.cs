// The Striders' elder: the user's cyborg (strider1.blend's `cyborg`, rigged by strider_elder_rig.py
// in strider_characters.blend), a humanoid torso on four robotic legs that walks on the crab
// walker's legged stack. One to three ride each walking city, one per house on its standing post
// (StandingRider), and go ashore with the crew at a stop (CrewShift). It has no attack: the elder
// is a figure, not a fighter (spec 2026-10-04-strider-characters-design).
//
// Re-run from: Tools > Creatures > Build Strider Elder
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Core.Persistence.EditorTools;
using SpaceGame.Creatures;
using SpaceGame.Creatures.Crab;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public static class StriderElderBuilder
    {
        public const string PrefabPath = "Assets/Game/Prefabs/Agents/Characters/Striders/StriderElder.prefab";
        public const string ModelPath = "Assets/Game/Art/Models/Characters/Striders/strider_elder.fbx";

        /// <summary>As tall as every other NPC (spec decision 4), sole to crown.</summary>
        public const float Height = 3f;

        private const int Legs = 4;
        private const int Health = 200;
        /// <summary>The crab's mass: at 1 the elder is shoved around by anything that touches it.</summary>
        private const float BodyMass = 100f;
        /// <summary>A slow, stately walk; it only ever walks between its post and the crew ashore.</summary>
        private const float MoveSpeed = 1.6f;
        private const float TurnSpeed = 45f;
        /// <summary>Unhurried steps. The legs, not moveSpeed, cap the walk (Striders.md Gotchas): 0.45 s
        /// steps measured 0.50 m/s, too slow to reach the gangway inside CrewShift's recall timeout.</summary>
        private const float StepDuration = 0.3f;
        /// <summary>An upright torso, not a crab's shell lying along the hillside.</summary>
        private const float SlopeFollow = 0.5f;
        private const float MaxShellTilt = 12f;

        private const string ChestBone = "Chest";
        private const string HeadBone = "Head";

        private static readonly (string mesh, string col)[] BodyBoxes =
        {
            ("Mesh_Elder_Pelvis", "COL_Pelvis"),
            ("Mesh_Elder_Chest", "COL_Chest"),
            ("Mesh_Elder_Head", "COL_Head"),
        };

        [MenuItem("Tools/Creatures/Build Strider Elder")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[StriderElder] No model at {ModelPath}. Export it with " +
                               "_Source~/models/vehicles/strider_characters_export.py StriderElder.");
                return;
            }

            GameObject root = CrabWalkerBuilder.BuildBodyFrom(model, Legs, "StriderElder", BodyBoxes, ScaleToHeight(model));
            if (root == null) return;
            try
            {
                Wire(root);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            Debug.Log(NetworkPrefabRegistrar.Sync(out _, out _));
            if (!SaveableWiring.TryWirePrefabs())
                Debug.LogError("[StriderElder] Save wiring failed; run Tools > Save System > Wire Saveable Prefabs.");
            RagdollWiring.WirePrefabs();
            // Last write: the scratch root lived in the open scene, which switched this off.
            NetworkObjectDefaults.KeepSceneMigrationSync(PrefabPath);
            Debug.Log($"[StriderElder] Built {PrefabPath}.");
        }

        /// <summary>
        /// How far the built elder's root (its hip plane: the legged stack rides there) stands above
        /// its soles, in metres. A standing post is raised this far off the deck, so the elder's feet
        /// rest on the deck. Negative when the prefab has not been built.
        /// </summary>
        public static float StandingHeight()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) return -1f;
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                probe.transform.position = Vector3.zero;
                float low = float.MaxValue;
                foreach (Renderer r in probe.GetComponentsInChildren<Renderer>(true)) low = Mathf.Min(low, r.bounds.min.y);
                return -low;
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        /// <summary>The scale that brings the model's rendered height to <see cref="Height"/>.</summary>
        private static float ScaleToHeight(GameObject model)
        {
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                // Position only: the FBX root carries the import's axis rotation, and resetting it lays
                // the model on its back and measures its depth.
                probe.transform.position = Vector3.zero;
                float low = float.MaxValue, high = float.MinValue;
                foreach (Renderer r in probe.GetComponentsInChildren<Renderer>(true))
                {
                    low = Mathf.Min(low, r.bounds.min.y);
                    high = Mathf.Max(high, r.bounds.max.y);
                }
                return Height / Mathf.Max(high - low, 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        private static void Wire(GameObject root)
        {
            var driver = root.GetComponent<CrabDriver>();
            SerializedFields.Edit(driver, so =>
            {
                SerializedFields.SetBool(so, "lateralSteering", false);
                SerializedFields.SetFloat(so, "moveSpeed", MoveSpeed);
                SerializedFields.SetFloat(so, "turnSpeed", TurnSpeed);
            });
            SerializedFields.Edit(root.GetComponent<CrabLocomotion>(), so =>
            {
                SerializedFields.SetFloat(so, "stepDuration", StepDuration);
                SerializedFields.SetFloat(so, "slopeFollow", SlopeFollow);
                SerializedFields.SetFloat(so, "maxShellTilt", MaxShellTilt);
            });
            root.GetComponent<Rigidbody>().mass = BodyMass;

            var brain = root.AddComponent<AgentController>();
            SerializedFields.Edit(brain, so => SerializedFields.Set(so, "MotorComponent", driver));

            var health = root.AddComponent<HealthComponent>();
            SerializedFields.Edit(health, so => { SerializedFields.SetInt(so, "maxHealth", Health); SerializedFields.SetInt(so, "currentHealth", Health); });
            root.AddComponent<HealthReactionModule>();

            EntityFactionWiring.Ensure(root, System.IO.Path.GetFileNameWithoutExtension(PrefabPath));
            root.AddComponent<PerceptionModule>();
            root.AddComponent<AgentTargeting>();

            var travel = root.AddComponent<GoalTravelModule>();
            SerializedFields.Edit(travel, so => SerializedFields.SetInt(so, "priority", ModulePriority.Fallback + 1));

            root.AddComponent<StandingRider>();
            var sway = root.AddComponent<StriderElderSway>();
            SerializedFields.Edit(sway, so =>
            {
                SerializedFields.Set(so, "chest", FindBone(root, ChestBone));
                SerializedFields.Set(so, "head", FindBone(root, HeadBone));
            });

            var tracked = root.AddComponent<SpaceGame.World.SceneTracked>();
            SerializedFields.Edit(tracked, so => SerializedFields.SetEnumByName(so, "policy", nameof(SpaceGame.World.SceneTracked.UnloadPolicy.Migrate)));
            root.AddComponent<SpaceGame.World.Safety.UnderTerrainGuard>();
            // A new prefab, so no scene holds an instance yet: the identity is ours to add here, and
            // Ensure (which refuses to add one) fills in the rest of the netcode set around it.
            root.AddComponent<Unity.Netcode.NetworkObject>();
            AgentNetworkWiring.Ensure(root);
            root.AddComponent<SaveableEntity>();
        }

        private static Transform FindBone(GameObject root, string bone)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == bone) return t;
            Debug.LogError($"[StriderElder] The model has no {bone} bone; the torso will not sway.");
            return null;
        }
    }
}
