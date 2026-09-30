// The Striders' crab outrider: the six-legged crab walker with a Strider on its back, flanking the
// walking city and serving as the tribe's war-party mount (roster role Rider). The mount carries,
// the rider shoots -- the crab has no attack (AgentSystem.md).
//
// Re-run from: Tools > Creatures > Build Strider Crab Outrider
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Core.Persistence.EditorTools;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public static class StriderCrabOutriderBuilder
    {
        public const string PrefabPath = "Assets/Game/Prefabs/Agents/Characters/Striders/StriderCrabOutrider.prefab";
        private const int Legs = 6;

        /// <summary>Faster than the wild crab's 1.6 m/s so it keeps up with the city's 2.7 (Spike findings).</summary>
        private const float OutriderMoveSpeed = 3.2f;
        /// <summary>The stock gait (stepDuration 0.4) caps the crab at 1.76 m/s whatever moveSpeed says;
        /// 0.22 s steps reach 3.2 m/s with the feet still planted (spike: slip 0.016 m/m, tilt 0 deg).</summary>
        private const float OutriderStepDuration = 0.22f;
        private const int Health = 180;
        /// <summary>Rider's feet above the shell's top, in metres.</summary>
        private const float SeatRise = 0.2f;
        /// <summary>CrabWalker6's hand-tuned mass; at 1 the crab is shoved around by anything that touches it.</summary>
        private const float BodyMass = 100f;
        /// <summary>The crabs flank the column on row 2, ~90 m behind the lead house. FormationModule's
        /// default 40 m made them count as separated at their own slot and ride for the leader, so they
        /// hovered among the houses. The crawler's figure, for the crawler's neighbours.</summary>
        private const float RegroupDistance = 150f;

        [MenuItem("Tools/Creatures/Build Strider Crab Outrider")]
        public static void Build()
        {
            GameObject root = CrabWalkerBuilder.BuildBody(Legs);
            if (root == null) return;
            root.name = "StriderCrabOutrider";

            var driver = root.GetComponent<SpaceGame.Creatures.CrabDriver>();
            SerializedFields.Edit(driver, so => SerializedFields.SetFloat(so, "moveSpeed", OutriderMoveSpeed));
            SerializedFields.Edit(root.GetComponent<SpaceGame.Creatures.Crab.CrabLocomotion>(),
                so => SerializedFields.SetFloat(so, "stepDuration", OutriderStepDuration));
            root.GetComponent<Rigidbody>().mass = BodyMass;

            var brain = root.AddComponent<AgentController>();
            SerializedFields.Edit(brain, so => SerializedFields.Set(so, "MotorComponent", driver));

            var health = root.AddComponent<HealthComponent>();
            SerializedFields.Edit(health, so => { SerializedFields.SetInt(so, "maxHealth", Health); SerializedFields.SetInt(so, "currentHealth", Health); });
            root.AddComponent<HealthReactionModule>();

            EntityFactionWiring.Ensure(root, System.IO.Path.GetFileNameWithoutExtension(PrefabPath));

            root.AddComponent<PerceptionModule>();
            root.AddComponent<AgentTargeting>();
            RobotHorseBuilder.AddOutriderBehaviour(root);

            var formation = root.AddComponent<FormationModule>();
            SerializedFields.Edit(formation, so =>
            {
                SerializedFields.SetInt(so, "priority", ModulePriority.Social);
                SerializedFields.SetString(so, "formationId", string.Empty);
                // It flanks the lead house: gathering in the rest ring would park it among the legs.
                SerializedFields.SetBool(so, "holdSlotAtRest", true);
                SerializedFields.SetFloat(so, "regroupDistance", RegroupDistance);
            });
            var travel = root.AddComponent<GoalTravelModule>();
            SerializedFields.Edit(travel, so => SerializedFields.SetInt(so, "priority", ModulePriority.Fallback + 1));

            AttachRider(root);

            var tracked = root.AddComponent<SpaceGame.World.SceneTracked>();
            SerializedFields.Edit(tracked, so => SerializedFields.SetEnumByName(so, "policy", nameof(SpaceGame.World.SceneTracked.UnloadPolicy.Migrate)));
            root.AddComponent<SpaceGame.World.Safety.UnderTerrainGuard>();
            // A new prefab, so no scene holds an instance yet: the identity is ours to add here, and
            // Ensure (which refuses to add one) fills in the rest of the netcode set around it.
            root.AddComponent<Unity.Netcode.NetworkObject>();
            AgentNetworkWiring.Ensure(root);
            root.AddComponent<SaveableEntity>();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            Debug.Log(NetworkPrefabRegistrar.Sync(out _, out _));
            if (!SaveableWiring.TryWirePrefabs())
                Debug.LogError("[StriderCrabOutrider] Save wiring failed; run Tools > Save System > Wire Saveable Prefabs.");
            RagdollWiring.WirePrefabs();
            // Last write: the scratch root lived in the open scene, which switched this off.
            NetworkObjectDefaults.KeepSceneMigrationSync(PrefabPath);
            Debug.Log($"[StriderCrabOutrider] Built {PrefabPath}.");
        }

        private static void AttachRider(GameObject root)
        {
            Bounds shell = new Bounds(root.transform.position, Vector3.zero);
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) shell.Encapsulate(r.bounds);

            var seat = new GameObject("SeatPoint");
            seat.transform.SetParent(root.transform, false);
            seat.transform.position = new Vector3(shell.center.x, shell.max.y + SeatRise, shell.center.z);

            var rider = AssetDatabase.LoadAssetAtPath<GameObject>(NomadPrefabBuilder.StriderNomads[0].PrefabPath);
            if (rider == null) Debug.LogError("[StriderCrabOutrider] Build the Strider nomads first; the crab rides empty.");

            var passenger = root.AddComponent<NpcPassenger>();
            SerializedFields.Edit(passenger, so =>
            {
                SerializedFields.Set(so, "riderPrefab", rider);
                SerializedFields.Set(so, "seatPoint", seat.transform);
                SerializedFields.SetVector3(so, "seatOffset", Vector3.zero);
                SerializedFields.SetBool(so, "spawnOnStart", true);
            });
        }
    }
}
