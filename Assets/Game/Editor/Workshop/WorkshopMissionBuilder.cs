// The workshop's story, built into Workshop.prefab by WorkshopPrefabBuilder after the gear doors and the crane: the
// mission component, the places it uses (outside the gate, the middle of the main room, the reactor core's cradle in the
// vault), and what it spawns or hands out -- the gate's Shellback, the family shut inside, the gear thief, and the stolen
// Heart Gear (docs/AI/systems/WorkshopMission.md).
//
// The prefabs and the item are made the first time and kept after that, like the gate's gears: a prefab saved again
// from a new object gets new file ids, and the network prefab list would still name the old ones.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class WorkshopMissionBuilder
    {
        private const string ShellbackPath = "Assets/Game/Prefabs/Agents/Characters/Shellback/Shellback_Villager.prefab";
        private const string MechanicPath = "Assets/Game/Prefabs/Agents/Characters/Shellback/Shellback_Mechanic.prefab";
        private const string GatekeeperPath = "Assets/Game/Prefabs/Agents/Characters/Shellback/Shellback_Gatekeeper.prefab";
        private const string FamilyPath = "Assets/Game/Prefabs/Agents/Characters/Shellback/Shellback_WorkshopFamily.prefab";
        private const string FamilyMechanicPath = "Assets/Game/Prefabs/Agents/Characters/Shellback/Shellback_WorkshopFamily_Mechanic.prefab";
        private const string OutlawPath = "Assets/Game/Prefabs/Agents/Caravan/BountyHunter.prefab";
        private const string ThiefPath = "Assets/Game/Prefabs/Agents/Caravan/Raider_GearThief.prefab";
        private const string CarrierPath = "Assets/Game/Prefabs/Agents/Caravan/Raider_GearCarrier.prefab";

        // Where the vault's reactor core rests on its cradle, as the model marks it.
        private const string CoreRestMark = "Vault_CoreRest";

        // Where the thieves' camp is recorded for the mission, written by GearThiefCampPlacer.
        public const string CampSitePath = "Assets/Game/ScriptableObjects/Workshop/GearThiefCampSite.asset";

        // A clue on the thieves' trail: a small cog from the kit, lying in the sand.
        private const string ClueModel = "gear_xs";

        // The carrier runs only when told to (the band's alarm), never at the mere sight of a player, so a player can
        // sneak up on the camp; once running he keeps going until the nearest player is this far behind him.
        private const float CarrierTriggerRadius = 0.1f;
        private const float CarrierSafeRadius = 70f;
        private const float CarrierFleeSpeed = 1.35f;

        // The thieves are bandits with a gravel blaster each: a pipe shotgun whose spread tapers past 15 m, so they open
        // up inside that and leave the last few metres to their fists.
        private const string ThiefGunPath = "Assets/Game/Resources/Items/Artifacts/GravelBlaster.asset";
        private const float ThiefShortestShot = 3f;
        private const float ThiefLongestShot = 18f;

        // The Heart Gear: the gear kit's biggest carried gear, drawn smaller than on the gate so it can be carried off.
        private const string HeartGearModel = "gear_xl";
        private const int HeartGearTeeth = 40;
        private const float HeartGearScale = 0.8f;

        // Where the Shellback paces: out along the lane from the shut gate's face, past the leaves' swing and the pile.
        private const float GateStandOut = 18f;
        private const float StandClearOfPile = 3f;
        private const float StandClearOfSweep = 1.5f;

        // The meeting: the first open floor walking in from the main room's centre towards the lane, so nobody is
        // asked to stand inside whatever stands in the middle.
        private const float MeetingSearchStep = 1f;
        private const float MeetingSearchMax = 10f;
        private const float MeetingProbeY = 1f;
        private const float BodyRadius = 0.5f;
        private const float BodyHeight = 3f;
        private const float FloorClearance = 0.3f;

        /// <summary>
        /// Build the mission into <paramref name="root"/>, round the doors and the crane already built. Returns what it
        /// built, for the rebuild's log line.
        /// </summary>
        public static string Attach(GameObject root, WorkshopLayout layout, GearDoor gate, GearDoor vault, WorkshopCrane crane,
                                    Transform console)
        {
            if (gate == null || vault == null || crane == null || console == null)
                throw new InvalidDataException("Build the gate, the vault's door and the crane before the mission.");

            var problems = new List<string>();
            Physics.SyncTransforms();

            Transform mission = new GameObject("Mission").transform;
            mission.SetParent(root.transform, false);

            Transform gateStand = new GameObject("GateStand").transform;
            gateStand.SetParent(mission, false);
            gateStand.localPosition = new Vector3(gate.transform.localPosition.x + GateStandOut, 0f, 0f);
            gateStand.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);   // +Z faces the gate
            CheckStand(gate, gateStand.position, problems);

            Transform meeting = new GameObject("Meeting").transform;
            meeting.SetParent(mission, false);
            meeting.localPosition = MeetingPoint(root, problems);
            meeting.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);    // +Z faces the lane in

            Transform coreRest = new GameObject("CoreRest").transform;
            coreRest.SetParent(mission, false);
            Pose rest = layout.PoseOf(CoreRestMark);
            coreRest.SetLocalPositionAndRotation(rest.position, rest.rotation);

            InventoryItem heartGear = EnsureHeartGear();
            GameObject gatekeeper = EnsureVariant(ShellbackPath, GatekeeperPath, go => Ensure<WorkshopGatekeeper>(go));
            GameObject relative = EnsureVariant(ShellbackPath, FamilyPath, go => Ensure<WorkshopFamilyMember>(go));
            GameObject mechanic = EnsureVariant(MechanicPath, FamilyMechanicPath, go => Ensure<WorkshopFamilyMember>(go));
            // The family shut inside: who stands where in the row, left to right.
            GameObject[] family = { relative, mechanic, relative };
            GameObject thief = EnsureVariant(OutlawPath, ThiefPath, go => MakeThief(go, carrier: false));
            GameObject carrier = EnsureVariant(ThiefPath, CarrierPath, go => MakeThief(go, carrier: true));
            AssetDatabase.SaveAssets();

            WorkshopMission component = mission.gameObject.AddComponent<WorkshopMission>();
            var so = new SerializedObject(component);
            SerializedFields.Set(so, "gate", gate);
            SerializedFields.Set(so, "gateStand", gateStand);
            SerializedFields.Set(so, "meeting", meeting);
            SerializedFields.Set(so, "gatekeeperPrefab", gatekeeper);
            SerializedFields.Set(so, "stolenGear", heartGear);
            so.FindProperty("thieves.thiefPrefab").objectReferenceValue = thief;
            so.FindProperty("thieves.carrierPrefab").objectReferenceValue = carrier;
            so.FindProperty("trail.clueModel").objectReferenceValue = CarriedGearItems.LoadModel(ClueModel);
            SerializedFields.Set(so, "campSite", EnsureCampSite());
            SerializedFields.Set(so, "vault", vault);
            SerializedFields.Set(so, "mainWheel", MainWheel(root));
            SerializedFields.Set(so, "vaultCore", crane.Load);
            SerializedFields.Set(so, "coreRest", coreRest);
            SerializedFields.Set(so, "crane", crane);
            SerializedFields.Set(so, "craneConsole", console);
            SerializedProperty members = so.FindProperty("family.members");
            members.arraySize = family.Length;
            for (int i = 0; i < family.Length; i++) members.GetArrayElementAtIndex(i).objectReferenceValue = family[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            string where = $"mission: meeting at ({meeting.localPosition.x:0.#}, {meeting.localPosition.y:0.##}, " +
                           $"{meeting.localPosition.z:0.#}), Shellback {GateStandOut} m out from the gate";
            return problems.Count == 0 ? where : $"{where}; MISSION: {string.Join("; ", problems)}";
        }

        /// <summary>The main room's hand cog, which the vault chapter points at first.</summary>
        private static WorkshopWheel MainWheel(GameObject root)
        {
            foreach (WorkshopWheel wheel in root.GetComponentsInChildren<WorkshopWheel>(true))
                if (wheel.Id == WorkshopLayout.MainWheelId) return wheel;
            throw new InvalidDataException($"No hand cog '{WorkshopLayout.MainWheelId}' to point the vault chapter at: build the wheels before the mission.");
        }

        /// <summary>The stand must be clear of the pile, which he would kick about, and of both leaves' swing.</summary>
        private static void CheckStand(GearDoor gate, Vector3 stand, List<string> problems)
        {
            var door = new SerializedObject(gate);
            SerializedProperty pile = door.FindProperty("pile");
            for (int i = 0; i < pile.arraySize; i++)
            {
                var seat = (Transform)pile.GetArrayElementAtIndex(i).FindPropertyRelative("seat").objectReferenceValue;
                if (seat != null && Flat(seat.position - stand).magnitude < StandClearOfPile)
                    problems.Add($"the Shellback's stand is within {StandClearOfPile} m of pile spot {i}");
            }

            SerializedProperty leaves = door.FindProperty("leaves");
            for (int i = 0; i < leaves.arraySize; i++)
            {
                var node = (Transform)leaves.GetArrayElementAtIndex(i).FindPropertyRelative("node").objectReferenceValue;
                if (node != null && Flat(stand - node.position).magnitude < LeafReach(node) + StandClearOfSweep)
                    problems.Add($"the Shellback's stand is inside the swing of leaf {node.name}");
            }
        }

        /// <summary>How far the leaf reaches from its hinge, which is its node's pivot.</summary>
        private static float LeafReach(Transform node)
        {
            float reach = 0f;
            foreach (Renderer renderer in node.GetComponentsInChildren<Renderer>(true))
            {
                Bounds b = renderer.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3((corner & 1) == 0 ? b.min.x : b.max.x, 0f, (corner & 4) == 0 ? b.min.z : b.max.z);
                    reach = Mathf.Max(reach, Flat(point - node.position).magnitude);
                }
            }
            return reach;
        }

        private static Vector3 MeetingPoint(GameObject root, List<string> problems)
        {
            for (float x = 0f; x <= MeetingSearchMax; x += MeetingSearchStep)
            {
                Vector3 floor = WorkshopHomeBuilder.OnFloor(root, x, 0f, MeetingProbeY, "meeting", new List<string>());
                Vector3 feet = floor + Vector3.up * (BodyRadius + FloorClearance);
                Vector3 head = floor + Vector3.up * (BodyHeight - BodyRadius);
                if (!Blocked(root, feet, head)) return floor;
            }

            problems.Add($"no open floor within {MeetingSearchMax} m of the main room's centre towards the lane");
            return Vector3.zero;
        }

        /// <summary>Whether anything of the workshop stands in a body's way there. Only the workshop's: an open scene may hold another.</summary>
        private static bool Blocked(GameObject root, Vector3 feet, Vector3 head)
        {
            foreach (Collider hit in Physics.OverlapCapsule(feet, head, BodyRadius, ~0, QueryTriggerInteraction.Ignore))
                if (hit.transform.IsChildOf(root.transform)) return true;
            return false;
        }

        /// <summary>The asset the camp's site is recorded in, made empty the first time; Place Gear Thief Camp fills it.</summary>
        public static GearThiefCampSite EnsureCampSite()
        {
            var site = AssetDatabase.LoadAssetAtPath<GearThiefCampSite>(CampSitePath);
            if (site != null) return site;

            Directory.CreateDirectory(Path.GetDirectoryName(CampSitePath));
            site = ScriptableObject.CreateInstance<GearThiefCampSite>();
            AssetDatabase.CreateAsset(site, CampSitePath);
            return site;
        }

        private static InventoryItem EnsureHeartGear()
        {
            float across = 2f * GearDoorBuilder.Geometry(GearDoorLayout.Load()).OuterRadius(HeartGearTeeth) *
                           HeartGearScale / GearDoorBuilder.ModelScale;
            return CarriedGearItems.Ensure("HeartGear", "Heart Gear", HeartGearModel, HeartGearScale, across);
        }

        /// <summary>
        /// A gear thief is an outlaw with a gun and a bag, and a loot table that drops both when he falls. The carrier
        /// also runs (FleeModule, above everything but a cutscene) when the band raises the alarm. Idempotent.
        /// </summary>
        private static void MakeThief(GameObject thief, bool carrier)
        {
            ArmThief(thief, carrier);
            if (!thief.TryGetComponent(out EntityLootTable _))
            {
                var loot = new SerializedObject(thief.AddComponent<EntityLootTable>());
                loot.FindProperty("dropInventoryContents").boolValue = true;
                loot.ApplyModifiedPropertiesWithoutUndo();
            }

            var role = new SerializedObject(Ensure<GearThief>(thief));
            role.FindProperty("isCarrier").boolValue = carrier;
            role.ApplyModifiedPropertiesWithoutUndo();

            if (carrier)
            {
                var run = new SerializedObject(Ensure<FleeModule>(thief));
                run.FindProperty("priority").intValue = ModulePriority.Override;
                run.FindProperty("fleeFromRelationship").enumValueIndex = (int)FactionRelationship.Hostile;
                run.FindProperty("triggerRadius").floatValue = CarrierTriggerRadius;
                run.FindProperty("safeRadius").floatValue = CarrierSafeRadius;
                run.FindProperty("fleeSpeedMultiplier").floatValue = CarrierFleeSpeed;
                run.ApplyModifiedPropertiesWithoutUndo();
            }
            SaveablePolicy.Ensure(thief, out _);
        }

        /// <summary>
        /// The gravel blaster in the bag's first slot, drawn at spawn and fired by an NpcItemUseModule. A fixed starting
        /// item, not a roll: every machine builds the same bag, so a peer's hand holds the slot the server fires from
        /// (EntityEquipmentController.OnItemUsedElsewhere). The carrier keeps his gun but never fires it: his job is to
        /// run, and a runner shooting back over his shoulder would turn the chase into a gunfight.
        /// </summary>
        private static void ArmThief(GameObject thief, bool carrier)
        {
            var gun = AssetDatabase.LoadAssetAtPath<InventoryItem>(ThiefGunPath);
            if (gun == null) throw new FileNotFoundException($"No gravel blaster at {ThiefGunPath} to arm the thieves with.");

            var bag = new SerializedObject(Ensure<EntityInventoryComponent>(thief));
            SerializedProperty starting = bag.FindProperty("startingItems");
            starting.arraySize = 1;
            starting.GetArrayElementAtIndex(0).objectReferenceValue = gun;
            bag.ApplyModifiedPropertiesWithoutUndo();

            Ensure<EntityEquipmentController>(thief);
            Ensure<EntityEquipmentSaveable>(thief);

            var shooting = new SerializedObject(Ensure<NpcItemUseModule>(thief));
            shooting.FindProperty("priority").intValue = ModulePriority.RangedAttack;
            shooting.FindProperty("active").boolValue = !carrier;
            shooting.FindProperty("minRange").floatValue = ThiefShortestShot;
            shooting.FindProperty("maxRange").floatValue = ThiefLongestShot;
            shooting.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// A variant of <paramref name="basePath"/> at <paramref name="path"/>: made the first time (never saved again from
        /// a new object, which would give it new file ids), and brought up to date in place after that.
        /// <paramref name="configure"/> must be idempotent.
        /// </summary>
        private static GameObject EnsureVariant(string basePath, string path, System.Action<GameObject> configure)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    configure(contents);
                    StripBakedChannels(contents);
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                return CarriedGearItems.StampOnDisk(path);
            }

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            if (source == null) throw new FileNotFoundException($"No prefab at {basePath} to make {path} from.");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                instance.name = Path.GetFileNameWithoutExtension(path);
                configure(instance);
                StripBakedChannels(instance);
                PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
            return CarriedGearItems.StampOnDisk(path);
        }

        /// <summary>The <typeparamref name="T"/> on <paramref name="go"/>, added if it has none.</summary>
        private static T Ensure<T>(GameObject go) where T : Component =>
            go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();

        /// <summary>
        /// A component added in the editor has its OnEnable run, and one that listens on the network channel then adds a
        /// NetChannel to the root: saved into the prefab, it is a stray the runtime never needed (it makes its own).
        /// </summary>
        private static void StripBakedChannels(GameObject root)
        {
            foreach (NetChannel channel in root.GetComponents<NetChannel>())
                if (PrefabUtility.IsAddedComponentOverride(channel) || !PrefabUtility.IsPartOfPrefabInstance(channel))
                    Object.DestroyImmediate(channel);
        }

        private static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
    }
}
