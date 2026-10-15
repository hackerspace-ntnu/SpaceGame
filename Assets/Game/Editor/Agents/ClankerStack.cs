// What makes a body a Clanker: every component of the robot cowboy that does not care which mesh
// it is wearing. ClankerBuilder hands it the RPR body, ClankerBodyBuilder the three Same Gev
// Dudios bodies (PatrolRobot 1/2/3), and both get the same patrol, the same gun, the same faction,
// the same netcode and the same savers -- so a Clanker behaves exactly the same whatever it looks
// like, and a change to how Clankers behave is made once, here.
//
// What the body brings is a ClankerBodyFit: its Animator, its measured bounds, its right hand, the
// run speed, its footprint radius, and the three AgentAnimatorDriver numbers that keep its own clips from skating.
// Everything sized from the body (capsule, NavMeshAgent, eye heights) is sized from those bounds,
// in true metres on the unscaled prefab root.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.World;
using SpaceGame.World.Safety;
using Unity.Netcode;

namespace SpaceGame.EditorTools
{
    /// <summary>What one body tells <see cref="ClankerStack.Apply"/> about itself.</summary>
    public struct ClankerBodyFit
    {
        /// <summary>The body's own Animator, wherever its clips need it to sit.</summary>
        public Animator animator;

        /// <summary>The body's bounds in the prefab root's space, soles already on y = 0.</summary>
        public Bounds bounds;

        /// <summary>
        /// Capsule and NavMeshAgent radius. The body's to measure, because what counts as its
        /// footprint depends on its bind pose: arms held out are reach, not width.
        /// </summary>
        public float radius;

        /// <summary>The right-hand bone the held gun parents to.</summary>
        public Transform handSocket;

        /// <summary>NavMeshAgent speed. The patrol walk is <see cref="ClankerStack.WalkFraction"/> of it.</summary>
        public float runSpeed;

        // AgentAnimatorDriver's per-body tuning: its blend-tree scale, its walk boost and its
        // playback rate (the anti-skate knob). Everything else about the driver is the stack's.
        public float animationSpeedMultiplier;
        public float walkAnimBoost;
        public float animatorSpeedScale;
    }

    public static class ClankerStack
    {
        private const string FactionPath = "Assets/Game/ScriptableObjects/Factions/Core/ClankerFaction.asset";
        private const string RelationshipsPath = "Assets/Game/ScriptableObjects/Factions/Core/GlobalRelationships.asset";

        // What a Clanker carries: a real InventoryItem, rolled at spawn by NpcRandomLoadout, held
        // and fired through EntityEquipmentController + NpcItemUseModule -- the sand nomads' path,
        // not the built-in AgentRangedCombatModule ray. The gun in its hand is the gun you loot
        // off it, because it is the same prefab. Ranged artifacts only: NpcItemUseModule fires the
        // held item at a target between minRange and maxRange, and a gauntlet or a scanner in the
        // hand would be raised and "used" at nothing every cooldown.
        // The same seven the sand nomads roll from (NomadPrefabBuilder): every one has been fired
        // by an NpcItemUseModule in play. Not the flamethrower or the cryo sprayer, which have not.
        private static readonly string[] HeldWeaponPaths =
        {
            "Assets/Game/Resources/Items/Artifacts/basicgun.asset",
            "Assets/Game/Resources/Items/Artifacts/GravelBlaster.asset",
            "Assets/Game/Resources/Items/Artifacts/NetGun.asset",
            "Assets/Game/Resources/Items/Artifacts/LaserStaff.asset",
            "Assets/Game/Resources/Items/Artifacts/BallLightningWeapon.asset",
            "Assets/Game/Resources/Items/Artifacts/LightningSpell.asset",
            "Assets/Game/Resources/Items/Artifacts/DragonBazooka.asset",
        };

        // What a Clanker is carrying HOME (design doc: they hunt artifacts). Bag slot 1, never
        // drawn, dropped with the gun on death -- so a dead Clanker is worth looting and two dead
        // Clankers are not the same loot. Half of them carry nothing but the gun.
        private static readonly string[] CarriedArtifactPaths =
        {
            "Assets/Game/Resources/Items/Artifacts/Lantern.asset",
            "Assets/Game/Resources/Items/Artifacts/StormFlask.asset",
            "Assets/Game/Resources/Items/Artifacts/AntiGravityPotion.asset",
            "Assets/Game/Resources/Items/Artifacts/JumpingRod.asset",
            "Assets/Game/Resources/Items/Artifacts/Flamethrower.asset",
            "Assets/Game/Resources/Items/Artifacts/CryoSprayer.asset",
            "Assets/Game/Resources/Items/Artifacts/FoamGun.asset",
            "Assets/Game/Resources/Items/Artifacts/BottledSingularity.asset",
            "Assets/Game/Resources/Items/Artifacts/InflatorNozzle.asset",
        };
        private const int CarriedArtifactEmptyRolls = 10;

        /// <summary>
        /// The band the Clanker's held gun fires in. Public because a Clanker is not always the
        /// thing that chooses where it stands — a mounted one is carried into range by its horse,
        /// and <c>RobotHorseBuilder</c> picks the outrider's standoff against these two numbers.
        /// </summary>
        public const float GunMinRange = 4f;

        /// <inheritdoc cref="GunMinRange"/>
        public const float GunMaxRange = 28f;

        /// <summary>
        /// How far a Clanker spots a person, and how far one can get before it gives up. The
        /// inline AgentTargeting defaults (35 / 45 m) are a creature's; a sentry on open sand
        /// with a gun that reaches 28 m should see you coming from well past that. Written into
        /// a TargetingProfile asset so it can be tuned in the Inspector and saved by id.
        /// </summary>
        public const float SightAcquireRange = 110f;
        public const float SightLoseRange = 140f;
        public const string TargetingProfilePath = "Assets/Game/ScriptableObjects/Targeting/Clanker.asset";

        /// <summary>How far one Clanker's sighting or hit carries to the others.</summary>
        public const float AlertRadius = 60f;

        /// <summary>How far a Clanker on foot wanders from where it stood. A band's leader carries the band.</summary>
        public const float PatrolRadius = 35f;

        /// <summary>Patrol pace as a fraction of the run.</summary>
        public const float WalkFraction = 0.45f;

        /// <summary>
        /// A watching machine animates a replicated move as a run above this fraction of the run
        /// speed: well clear of the patrol walk (<see cref="WalkFraction"/>), under the run itself.
        /// </summary>
        public const float WatchedRunFraction = 0.7f;

        private const int MaxHealth = 160;

        /// <summary>
        /// Adds the whole Clanker stack to <paramref name="root"/>, which already holds the body
        /// described by <paramref name="fit"/>. The root must be unscaled: everything here is in
        /// true metres.
        /// </summary>
        public static void Apply(GameObject root, ClankerBodyFit fit)
        {
            Bounds bounds = fit.bounds;
            float radius = fit.radius;

            // -- physical presence, in true metres on the unscaled root --------------
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.radius = radius;
            capsule.height = bounds.size.y;
            capsule.center = new Vector3(0f, bounds.size.y * 0.5f, 0f);

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = radius;
            agent.height = bounds.size.y;
            agent.speed = fit.runSpeed;
            agent.angularSpeed = 200f;
            agent.acceleration = 14f;
            agent.stoppingDistance = 2f;
            agent.autoBraking = true;

            // -- motor, animation, brain ---------------------------------------------
            var motor = root.AddComponent<NavMeshAgentMotor>();
            SetField(motor, "agent", agent);
            SetFloat(motor, "walkSpeedMultiplier", WalkFraction);

            var driver = root.AddComponent<AgentAnimatorDriver>();
            SetField(driver, "animator", fit.animator);
            SetFloat(driver, "animationSpeedMultiplier", fit.animationSpeedMultiplier);
            SetFloat(driver, "walkAnimBoost", fit.walkAnimBoost);
            SetFloat(driver, "animatorSpeedScale", fit.animatorSpeedScale);
            SetFloat(driver, "measuredRunSpeed", fit.runSpeed * WatchedRunFraction);

            var brain = root.AddComponent<AgentController>();
            SetField(brain, "MotorComponent", motor);
            SetField(brain, "animatorDriver", driver);

            var health = root.AddComponent<HealthComponent>();
            SetInt(health, "maxHealth", MaxHealth);
            SetInt(health, "currentHealth", MaxHealth);
            root.AddComponent<HealthReactionModule>();

            var faction = root.AddComponent<EntityFaction>();
            SetField(faction, "faction", AssetDatabase.LoadAssetAtPath<FactionDefinition>(FactionPath));
            SetField(faction, "relationshipTable", AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(RelationshipsPath));

            // A machine watches a wide arc, and a robot cowboy sees a long way across open sand.
            var perception = root.AddComponent<PerceptionModule>();
            SetFloat(perception, "fieldOfViewAngle", VisionBaseline.MinFieldOfView);
            SetFloat(perception, "eyeHeight", bounds.size.y * 0.9f);
            SetFloat(perception, "memoryDuration", VisionBaseline.MinMemory);
            SetInt(perception, "occlusionLayers", LayerMaskOf("Default", "Ground", "Interior"));

            AgentTargeting targeting = root.GetComponent<AgentTargeting>();
            if (targeting == null) targeting = root.AddComponent<AgentTargeting>();
            SetField(targeting, "profile", WriteTargetingProfile());

            // -- behaviour: a patrol that chases, kites and shoots ---------------------
            // Script-added modules keep priority 0 (Unity does not call Reset for AddComponent),
            // so every one is set explicitly or it ties with the patrol.
            var patrol = root.AddComponent<PatrolModule>();
            SetPriority(patrol, ModulePriority.Fallback);
            SetFloat(patrol, "patrolRadius", PatrolRadius);
            SetFloat(patrol, "minWaitTime", 2f);
            SetFloat(patrol, "maxWaitTime", 6f);

            // Bands. Inert on the prefab (no id); every placer that puts Clankers down together --
            // the settlement generator, the squad placer, the town's spawner -- gives the group an
            // id and makes one of them the leader. The leader patrols; the rest keep formation.
            var formation = root.AddComponent<FormationModule>();
            SetPriority(formation, ModulePriority.Social);
            SetString(formation, "formationId", string.Empty);
            SetBool(formation, "isLeader", false);

            var chase = root.AddComponent<ChaseModule>();
            SetPriority(chase, ModulePriority.Reactive);
            SetFloat(chase, "chaseStopDistance", 6f);

            var search = root.AddComponent<SearchModule>();
            SetPriority(search, ModulePriority.Reactive - 1);

            var keepDistance = root.AddComponent<KeepDistanceModule>();
            SetPriority(keepDistance, ModulePriority.Ambient);

            // -- the gun in its hand ---------------------------------------------------
            if (fit.handSocket == null)
                Debug.LogWarning($"[ClankerStack] {root.name} has no hand bone; the held item falls back to a name search.");

            var inventory = root.AddComponent<EntityInventoryComponent>();
            SetInt(inventory, "inventorySize", 2);      // the gun, and what it is carrying home

            var equipment = root.AddComponent<EntityEquipmentController>();
            if (fit.handSocket != null) SetField(equipment, "handSocket", fit.handSocket);
            SetInt(equipment, "startingSlot", 0);
            SetBool(equipment, "aimHeldItem", true);
            SetFloat(equipment, "eyeHeight", bounds.size.y * 0.85f);

            AddLoadout(root, HeldWeaponPaths, slot: 0, equip: true, emptyRolls: 0);
            AddLoadout(root, CarriedArtifactPaths, slot: 1, equip: false, emptyRolls: CarriedArtifactEmptyRolls);

            var use = root.AddComponent<NpcItemUseModule>();
            SetPriority(use, ModulePriority.RangedAttack);
            SetInt(use, "slotIndex", 0);
            SetEnum(use, "trigger", 0);                       // Trigger.TargetInRange
            SetFloat(use, "minRange", GunMinRange);
            SetFloat(use, "maxRange", GunMaxRange);
            SetFloat(use, "cooldown", 0.9f);
            SetInt(use, "burstCount", 1);
            SetFloat(use, "reactionDelay", 0.3f);            // a machine, not a surprised person
            SetFloat(use, "targetHeightOffset", 1.6f);       // chest height on a 3.2 m astronaut
            SetString(use, "useAnimTrigger", "AssualtShoot");

            // Drops whatever it was holding. No fixed loot entries: the gun IS the loot.
            var loot = root.AddComponent<EntityLootTable>();
            SetBool(loot, "dropInventoryContents", true);

            var look = root.AddComponent<IdleLookAroundModule>();
            SetPriority(look, ModulePriority.Ambient);

            // A sighting or a hit is passed to every Clanker within the radius through the
            // targeting registry (AlertBroadcaster); the receivers take it as a grudge and do not
            // pass it on. The whole town is 260 m across, so the radius reaches the next ring.
            var broadcaster = root.AddComponent<AlertBroadcaster>();
            SetFloat(broadcaster, "alertRadius", AlertRadius);
            var receiver = root.AddComponent<AlertReceiverModule>();
            SetPriority(receiver, ModulePriority.Reactive - 1);
            SetFloat(receiver, "alertDuration", 15f);

            // Hostile by stance, so the grudge is redundant most of the time; it is here because
            // an alert sticks through ProvocationModule and because a Clanker that is hit should
            // announce its attacker to the others.
            var provocation = root.AddComponent<ProvocationModule>();
            SetFloat(provocation, "leashRange", SightLoseRange);
            SetFloat(provocation, "calmDownDelay", 45f);

            root.AddComponent<NoiseEmitter>();
            var hearing = root.AddComponent<NoiseReceiverModule>();
            SetPriority(hearing, ModulePriority.Reactive - 2);

            var tracked = root.AddComponent<SceneTracked>();
            SetEnum(tracked, "policy", (int)SceneTracked.UnloadPolicy.Migrate);
            SetBool(tracked, "keepChunksLoaded", false);
            root.AddComponent<UnderTerrainGuard>();

            // -- netcode, NetworkObject first so the NetworkBehaviours have something to ride --
            root.AddComponent<NetworkObject>();
            root.AddComponent<ClientNetworkTransform>();
            root.AddComponent<NetRelay>();
            root.AddComponent<NetAuthority>();
            root.AddComponent<NetworkedHealthComponent>();

            // -- persistence: in the builder, because a rebuild overwrites the prefab wholesale --
            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();
            root.AddComponent<HealthSaveable>();
            root.AddComponent<AgentStateSaveable>();

            AgentGroundConformWiring.Ensure(root);
            DistanceDormancyWiring.Ensure(root);

            // The humanoid bodies' actions and the components that play them. A no-op on the RPR
            // body, which animates with its own controller rather than the humanoid one.
            CharacterActionWiring.Ensure(root);
        }

        /// <summary>
        /// Saves <paramref name="root"/> over <paramref name="path"/> and destroys the scene copy.
        /// Saving over an existing prefab keeps its GUID, so every list, recipe and save that
        /// refers to it by GUID still does.
        /// </summary>
        public static GameObject Save(GameObject root, string path)
        {
            StaticPropBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            Object.DestroyImmediate(root);
            if (ok) return saved;

            Debug.LogError($"[ClankerStack] Could not save {path}.");
            return null;
        }

        /// <summary>
        /// The body-agnostic half of a built Clanker's check, read off disk: the components that
        /// make it play, the network hash and the save id. Appends what is wrong to <paramref name="missing"/>.
        /// </summary>
        public static void Verify(GameObject prefab, List<string> missing)
        {
            void Need<T>() where T : Component { if (prefab.GetComponentInChildren<T>(true) == null) missing.Add(typeof(T).Name); }
            Need<Animator>(); Need<AgentController>(); Need<NavMeshAgent>(); Need<EntityFaction>();
            Need<NpcItemUseModule>(); Need<EntityEquipmentController>(); Need<NpcRandomLoadout>(); Need<EntityLootTable>();
            Need<NetworkObject>(); Need<NetAuthority>(); Need<SaveableEntity>();
            Need<HealthComponent>(); Need<AgentGroundConform>();

            var animator = prefab.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.runtimeAnimatorController == null)
                missing.Add("Animator has no controller");

            var netObj = prefab.GetComponent<NetworkObject>();
            if (netObj != null && netObj.PrefabIdHash == 0)
                missing.Add("NetworkObject GlobalObjectIdHash is 0 -- clients cannot spawn it");

            var saveable = prefab.GetComponent<SaveableEntity>();
            if (saveable == null || string.IsNullOrEmpty(saveable.PrefabId))
                missing.Add("SaveableEntity has no prefabId -- it will vanish on load");
        }

        // ── targeting ────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates or updates the Clanker's targeting profile in place (the GUID is its save id).
        /// </summary>
        private static TargetingProfile WriteTargetingProfile()
        {
            StaticPropBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(TargetingProfilePath).Replace('\\', '/'));
            var profile = AssetDatabase.LoadAssetAtPath<TargetingProfile>(TargetingProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<TargetingProfile>();
                AssetDatabase.CreateAsset(profile, TargetingProfilePath);
            }

            profile.relationship = FactionRelationship.Hostile;
            profile.acquisitionRange = SightAcquireRange;
            profile.loseRange = SightLoseRange;
            profile.memoryDuration = VisionBaseline.MinMemory;
            profile.requireLineOfSightToAcquire = true;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            // The id is the asset GUID, the same key every other persisted ScriptableObject here
            // uses. TargetingProfile.OnValidate stamps it for an asset edited in the Inspector, but
            // one created and saved from code came back with an empty id even after a forced
            // import (2026-09-07) -- and AgentStateSaveable records profileId, so an agent restored
            // from a save would then reload on the inline 35 / 45 m defaults with nothing said.
            // Stamped here, and refused if it is still empty afterwards.
            string guid = AssetDatabase.AssetPathToGUID(TargetingProfilePath);
            if (profile.ID != guid)
            {
                profile.ID = guid;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }

            profile = AssetDatabase.LoadAssetAtPath<TargetingProfile>(TargetingProfilePath);
            if (string.IsNullOrEmpty(profile.ID))
                throw new System.InvalidOperationException($"[ClankerStack] {TargetingProfilePath} has no id after saving.");
            return profile;
        }

        // ── loadout ──────────────────────────────────────────────────────────────

        /// <summary>
        /// A random pick into one bag slot. <paramref name="emptyRolls"/> null candidates are
        /// appended so the roll can come up empty: NpcRandomLoadout treats a null pick as nothing.
        /// </summary>
        private static void AddLoadout(GameObject root, string[] paths, int slot, bool equip, int emptyRolls)
        {
            var loadout = root.AddComponent<NpcRandomLoadout>();
            var so = new SerializedObject(loadout);
            SerializedProperty candidates = so.FindProperty("candidates");
            InventoryItem[] items = paths.Select(AssetDatabase.LoadAssetAtPath<InventoryItem>).Where(i => i != null).ToArray();
            if (items.Length < paths.Length)
                Debug.LogError($"[ClankerStack] An artifact in the slot {slot} loadout list is missing.");
            candidates.arraySize = items.Length + emptyRolls;
            for (int i = 0; i < items.Length; i++) candidates.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            SerializedFields.SetInt(so, "slot", slot);
            so.FindProperty("equipAfterRoll").boolValue = equip;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── serialized-field helpers (SerializedFields warns on a renamed field) ────

        private static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedFields.Set(so, field, value);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            SerializedFields.SetFloat(so, field, value);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            SerializedFields.SetInt(so, field, value);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            SerializedFields.SetBool(so, field, value);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(Object target, string field, string value)
        {
            var so = new SerializedObject(target);
            SerializedFields.SetString(so, field, value);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnum(Object target, string field, int index)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[ClankerStack] {target.GetType().Name}.{field} not found."); return; }
            p.enumValueIndex = index;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetPriority(Component module, int priority) => SetInt(module, "priority", priority);

        private static int LayerMaskOf(params string[] names)
        {
            int mask = 0;
            foreach (string n in names)
            {
                int layer = LayerMask.NameToLayer(n);
                if (layer < 0) Debug.LogWarning($"[ClankerStack] No layer named '{n}'.");
                else mask |= 1 << layer;
            }
            return mask;
        }
    }
}
