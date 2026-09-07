// Builds the Clanker -- the robot cowboy of the Clanker faction -- from the body and clips
// borrowed from Red Planet Rampage (Assets/ThirdParty/RedPlanetRampage, BSD-4-Clause; see
// THIRD_PARTY_NOTICES.md at the repo root for what that licence asks of us).
//
// What comes from RPR is the skinned body (one SkinnedMeshRenderer, three submeshes, a 66-bone
// Rigify skeleton under "rig.001") and nine in-place locomotion clips authored against that
// skeleton as loose .anim assets. Nothing else: RPR's materials use its own dither shader graph,
// and its controller is built around a first-person shooter's inputs. This builder dresses the
// body from this project's palette and drives it through the same AgentAnimatorDriver contract
// every other creature uses, so the Clanker is an ordinary agent to everything downstream.
//
// Three things about the source worth knowing before touching this:
//
//   SIZE. The FBX is 9.65 m tall. Its root imports at lossyScale 1 with every child at 100 (the
//   centimetre convention, see ArtPipeline.md), and the mesh really is that big in metres. The
//   prefab therefore holds the model on a child scaled to TargetHeight, with colliders and the
//   NavMeshAgent on the unscaled root in true metres. Nothing on the root may be sized off the
//   model's local numbers.
//
//   CLIPS. The .anim files bind by transform path starting at "rig.001", so the Animator has to
//   sit on the FBX instance root -- the object whose child is "rig.001" -- and not on the prefab
//   root. Move it and every clip silently binds to nothing. There is no hurt, death or shoot clip:
//   death is the ragdoll (RagdollWiring adds AgentRagdoll), hurt and shoot are trigger parameters
//   with no state so the modules that fire them do not warn, and IsAiming is a bool nothing reads.
//
//   STRIDE. The walk cycle is 0.425 s and the foot travels ~3.8 m (unscaled) per cycle, which at
//   TargetHeight is a stride speed near 5 m/s -- RPR's player is fast. MeasureStrideSpeed reads
//   that off the clip at build time and the NavMeshAgent's run speed is set to it, so the feet
//   do not skate at a run; the patrol walk is a fraction of it and the blend tree's idle<->walk
//   blend scales the stride to match.
//
// Re-run from: Tools > SpaceGame > Agents > Build Clanker Prefab
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Core.Persistence.EditorTools;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.World;
using SpaceGame.World.Safety;
using Unity.Netcode;

namespace SpaceGame.EditorTools
{
    public static class ClankerBuilder
    {
        public const string Fbx = "Assets/ThirdParty/RedPlanetRampage/Models/YiiHaw.fbx";
        public const string ClipDir = "Assets/ThirdParty/RedPlanetRampage/Animation";
        public const string ControllerPath = "Assets/Game/Art/Animations/Creatures/Clanker.controller";
        public const string PrefabPath = "Assets/Game/Prefabs/Agents/Robots/Clanker.prefab";
        private const string MaterialDir = "Assets/Game/Art/Materials/Characters";

        private const string FactionPath = "Assets/Game/ScriptableObjects/Factions/Core/RobotFaction.asset";
        private const string RelationshipsPath = "Assets/Game/ScriptableObjects/Factions/Core/GlobalRelationships.asset";
        // What a Clanker carries: a real InventoryItem, rolled at spawn by NpcRandomLoadout, held
        // and fired through EntityEquipmentController + NpcItemUseModule -- the sand nomads' path,
        // not the built-in AgentRangedCombatModule ray. The gun in its hand is the gun you loot
        // off it, because it is the same prefab. Ranged artifacts only: NpcItemUseModule fires the
        // held item at a target between minRange and maxRange, and a gauntlet or a scanner in the
        // hand would be raised and "used" at nothing every cooldown.
        private static readonly string[] HeldWeaponPaths =
        {
            "Assets/Game/Resources/Items/Artifacts/basicgun.asset",
            "Assets/Game/Resources/Items/Artifacts/GravelBlaster.asset",
        };
        private const float GunMinRange = 4f;
        private const float GunMaxRange = 28f;

        /// <summary>
        /// How far a Clanker spots a person, and how far one can get before it gives up. The
        /// inline AgentTargeting defaults (35 / 45 m) are a creature's; a sentry on open sand
        /// with a gun that reaches 28 m should see you coming from well past that. Written into
        /// a TargetingProfile asset so it can be tuned in the Inspector and saved by id.
        /// </summary>
        public const float SightAcquireRange = 80f;
        public const float SightLoseRange = 100f;
        public const string TargetingProfilePath = "Assets/Game/ScriptableObjects/Targeting/Clanker.asset";

        /// <summary>Animator bool NpcPassenger holds while the Clanker rides a robot horse.</summary>
        public const string SeatedParameter = "IsSeated";

        /// <summary>How far one Clanker's sighting or hit carries to the others.</summary>
        public const float AlertRadius = 60f;

        /// <summary>
        /// Height the body is scaled to. The astronaut is 3.25 m and the nomads 3 m; a Clanker
        /// should look them in the eye.
        /// </summary>
        public const float TargetHeight = 3.2f;

        /// <summary>Patrol pace as a fraction of the run the stride was measured at.</summary>
        public const float WalkFraction = 0.45f;

        /// <summary>
        /// Fraction of a walk cycle a foot spends on the ground. The clip does not say; 0.6 is a
        /// walking gait's usual duty factor and is what turns foot travel into ground speed.
        /// </summary>
        public const float StanceDuty = 0.6f;

        // The palette copies these are made from. The (DoubleSided) assets under Materials/Vehicles
        // are the only standalone .mat files the palette has; every other palette material is a
        // sub-asset of some FBX. The copies here are back-face culled again -- the body is a closed
        // mesh, and ArtPipeline.md is explicit that a closed mesh must not be double-sided.
        private static readonly (string name, string source)[] Materials =
        {
            ("Clanker_Body", "Assets/Game/Art/Materials/Vehicles/Mat_Metal_Rust_Heavy (DoubleSided).mat"),
            ("Clanker_Trim", "Assets/Game/Art/Materials/Vehicles/Mat_Metal_Steel_Worn (DoubleSided).mat"),
            ("Clanker_Eyes", "Assets/Game/Art/Materials/Vehicles/Mat_Emissive_Red_Warn (DoubleSided).mat"),
        };

        // Submesh -> material, by triangle count measured on import (17416 body / 2174 trim / 96 eyes).
        // Red eyes are the tell: a Clanker is hostile on sight and should read that way at range.
        private static readonly int[] SubmeshMaterial = { 0, 1, 2 };

        private static readonly string[] Clips = { "idle", "Walk", "back", "SideStepLeft", "SideStepRight", "CrouchIdle" };

        [MenuItem("Tools/SpaceGame/Agents/Build Clanker Prefab")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Fbx) == null)
            {
                Debug.LogError($"[ClankerBuilder] No FBX at {Fbx}. Copy it from the Red Planet Rampage clone first.");
                return;
            }

            ConfigureImporter();

            Dictionary<string, AnimationClip> clips = LoadClips();
            if (clips == null) return;

            float strideSpeed = MeasureStrideSpeed(clips["Walk"]);
            if (strideSpeed <= 0f) return;

            Material[] materials = BuildMaterials();
            AnimatorController controller = BuildController(clips, strideSpeed);
            GameObject prefab = BuildPrefab(controller, materials, strideSpeed);
            if (prefab == null) return;

            // Sync, not SyncMenu: the menu variant ends in a modal dialog, which parks the whole
            // editor behind an OK button nobody driving this from a script can press.
            Debug.Log(NetworkPrefabRegistrar.Sync(out _, out _));
            if (!SaveableWiring.TryWirePrefabs())
                Debug.LogError("[ClankerBuilder] Save wiring failed; run Tools > Save System > Wire Saveable Prefabs by hand.");
            RagdollWiring.WirePrefabs();

            if (!Verify(out string report))
            {
                Debug.LogError("[ClankerBuilder] " + report);
                return;
            }

            Debug.Log($"[ClankerBuilder] Built {PrefabPath}. {report}", prefab);
        }

        // ── import ───────────────────────────────────────────────────────────────

        private static void ConfigureImporter()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            bool dirty = importer.animationType != ModelImporterAnimationType.Generic
                         || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel
                         || importer.importAnimation
                         || !importer.useFileScale
                         || !Mathf.Approximately(importer.globalScale, 1f);

            // Generic, from this model: a Rigify DEF- skeleton is not a humanoid avatar and the
            // clips are loose .anim assets bound by path, so nothing is retargeted. importAnimation
            // is off because the FBX carries no takes worth importing and a take list would only
            // confuse the clip lookup.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.isReadable = true;

            if (dirty) importer.SaveAndReimport();
        }

        private static Dictionary<string, AnimationClip> LoadClips()
        {
            var result = new Dictionary<string, AnimationClip>();
            foreach (string name in Clips)
            {
                string path = $"{ClipDir}/rig.001_{name}.anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    Debug.LogError($"[ClankerBuilder] Missing clip {path}.");
                    return null;
                }
                result[name] = clip;
            }
            return result;
        }

        // ── stride ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Ground speed the walk clip is authored for, at <see cref="TargetHeight"/>: the distance
        /// a foot travels along the body's forward axis over one cycle, divided by the time it
        /// spends on the ground. Measured off the clip so a re-export cannot leave a stale number
        /// behind.
        /// </summary>
        public static float MeasureStrideSpeed(AnimationClip walk)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                float scale = ModelScale(instance);
                instance.transform.localScale = Vector3.one * scale;

                Transform foot = instance.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == "DEF-foot.L");
                if (foot == null)
                {
                    Debug.LogError("[ClankerBuilder] No DEF-foot.L bone; cannot measure the stride.");
                    return 0f;
                }

                const int samples = 48;
                float min = float.MaxValue, max = float.MinValue;
                for (int i = 0; i <= samples; i++)
                {
                    walk.SampleAnimation(instance, walk.length * i / samples);
                    float z = instance.transform.InverseTransformPoint(foot.position).z * scale;
                    min = Mathf.Min(min, z);
                    max = Mathf.Max(max, z);
                }

                float speed = (max - min) / (StanceDuty * walk.length);
                Debug.Log($"[ClankerBuilder] Walk clip: foot travels {max - min:F2} m per {walk.length:F3} s cycle " +
                          $"at scale {scale:F3} -> stride speed {speed:F2} m/s.");
                return speed;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Uniform scale that brings the bind pose to <see cref="TargetHeight"/>.</summary>
        private static float ModelScale(GameObject instance)
        {
            Bounds b = BindPoseBounds(instance);
            return TargetHeight / b.size.y;
        }

        private static Bounds BindPoseBounds(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // ── materials ────────────────────────────────────────────────────────────

        private static Material[] BuildMaterials()
        {
            EnsureFolder(MaterialDir);
            var result = new Material[Materials.Length];
            for (int i = 0; i < Materials.Length; i++)
            {
                string path = $"{MaterialDir}/{Materials[i].name}.mat";
                var source = AssetDatabase.LoadAssetAtPath<Material>(Materials[i].source);
                if (source == null)
                {
                    Debug.LogError($"[ClankerBuilder] Palette material missing: {Materials[i].source}");
                    continue;
                }

                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(source);
                    AssetDatabase.CreateAsset(material, path);
                }
                else
                {
                    material.CopyPropertiesFromMaterial(source);
                    material.shader = source.shader;
                }

                // Back-face culling restored: the (DoubleSided) source exists for open-sheet hulls.
                if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
                EditorUtility.SetDirty(material);
                result[i] = material;
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        // ── targeting ────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates or updates the Clanker's targeting profile in place (the GUID is its save id).
        /// </summary>
        private static TargetingProfile WriteTargetingProfile()
        {
            EnsureFolder(System.IO.Path.GetDirectoryName(TargetingProfilePath).Replace('\\', '/'));
            var profile = AssetDatabase.LoadAssetAtPath<TargetingProfile>(TargetingProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<TargetingProfile>();
                AssetDatabase.CreateAsset(profile, TargetingProfilePath);
            }

            profile.relationship = FactionRelationship.Hostile;
            profile.acquisitionRange = SightAcquireRange;
            profile.loseRange = SightLoseRange;
            profile.memoryDuration = 10f;
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
                throw new System.InvalidOperationException($"[ClankerBuilder] {TargetingProfilePath} has no id after saving.");
            return profile;
        }

        // ── controller ───────────────────────────────────────────────────────────

        private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips, float strideSpeed)
        {
            EnsureFolder(System.IO.Path.GetDirectoryName(ControllerPath).Replace('\\', '/'));

            // Emptied through the AnimatorController API and rebuilt in place. The first Clanker
            // build deleted and recreated the asset; the second wiped its sub-assets by hand and
            // reset the layers. Each left a controller that later had no states and a robot frozen
            // in its bind pose with a clean console -- AnimatorControllerRebuild has both stories.
            AnimatorController controller = AnimatorControllerRebuild.LoadOrCreateEmpty(ControllerPath);

            // AgentAnimatorDriver's names, verbatim. "Die" as well as "Death" because the driver
            // sends one and HealthReactionModule the other; "AssualtShoot" (sic) is what
            // AgentRangedCombatModule fires. Hurt and AssualtShoot have no state to go to -- there
            // is no clip -- but a missing parameter is a warning every frame per agent.
            controller.AddParameter("SpeedX", AnimatorControllerParameterType.Float);
            controller.AddParameter("SpeedY", AnimatorControllerParameterType.Float);
            controller.AddParameter("FallSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsImmobalized", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsAiming", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Hurt", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Death", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("AssualtShoot", AnimatorControllerParameterType.Trigger);
            // Held by NpcPassenger while this body is cargo on a robot horse (ClankerOutrider).
            controller.AddParameter(SeatedParameter, AnimatorControllerParameterType.Bool);

            AnimatorStateMachine root = controller.layers[0].stateMachine;

            // One 2-D tree over the body-local velocity in true m/s (the prefab sets both driver
            // scale factors to 1). Forward at the measured stride speed, back and the two side
            // steps at the same magnitude; at a patrol walk the tree sits between idle and Walk
            // and the stride shrinks with it, which is what keeps the feet honest at half speed.
            var tree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.FreeformDirectional2D,
                blendParameter = "SpeedX",
                blendParameterY = "SpeedY",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(clips["idle"], Vector2.zero);
            tree.AddChild(clips["Walk"], new Vector2(0f, strideSpeed));
            tree.AddChild(clips["back"], new Vector2(0f, -strideSpeed));
            tree.AddChild(clips["SideStepLeft"], new Vector2(-strideSpeed, 0f));
            tree.AddChild(clips["SideStepRight"], new Vector2(strideSpeed, 0f));

            AnimatorState locomotion = root.AddState("Locomotion");
            locomotion.motion = tree;
            root.defaultState = locomotion;

            // In the saddle. MountedRiderPose can only bend a Humanoid rider, and this rig imports
            // Generic, so the nearest thing the clip set has to sitting is the crouch: knees bent,
            // weight low. NpcPassenger raises the flag on every machine that seats the body.
            AnimatorState seated = root.AddState("Seated");
            seated.motion = clips["CrouchIdle"];
            AnimatorStateTransition sitDown = locomotion.AddTransition(seated);
            sitDown.AddCondition(AnimatorConditionMode.If, 0f, SeatedParameter);
            sitDown.hasExitTime = false;
            sitDown.duration = 0.2f;
            AnimatorStateTransition standUp = seated.AddTransition(locomotion);
            standUp.AddCondition(AnimatorConditionMode.IfNot, 0f, SeatedParameter);
            standUp.hasExitTime = false;
            standUp.duration = 0.2f;

            // The body goes to the ragdoll on death; this state only has to stop the walk cycle
            // for the frames before physics takes over. The crouch idle is the nearest thing the
            // clip set has to a slump.
            AnimatorState death = root.AddState("Death");
            death.motion = clips["CrouchIdle"];
            foreach (string trigger in new[] { "Death", "Die" })
            {
                AnimatorStateTransition t = root.AddAnyStateTransition(death);
                t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                t.duration = 0.1f;
                t.hasExitTime = false;
                t.canTransitionToSelf = false;
            }

            return AnimatorControllerRebuild.SaveAndVerify(controller, ControllerPath, minimumStates: 3);
        }

        // ── prefab ───────────────────────────────────────────────────────────────

        private static GameObject BuildPrefab(AnimatorController controller, Material[] materials, float strideSpeed)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);

            var root = new GameObject("Clanker");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.name = "Body";
            model.transform.SetParent(root.transform, false);

            // Scale to size, then drop the soles onto y = 0: the FBX's lowest vertex sits below
            // its origin, and every agent prefab here is authored soles-at-pivot so the ground
            // conform's offset starts from zero.
            float scale = ModelScale(model);
            model.transform.localScale = Vector3.one * scale;
            Bounds bounds = BindPoseBounds(model);
            model.transform.localPosition = new Vector3(0f, -bounds.min.y, 0f);
            bounds = BindPoseBounds(model);

            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var mats = new Material[skin.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++)
                mats[i] = materials[SubmeshMaterial[Mathf.Min(i, SubmeshMaterial.Length - 1)]];
            skin.sharedMaterials = mats;
            // A skinned body's bind-pose bounds are not where the body is once it animates.
            skin.updateWhenOffscreen = true;

            // The Animator lives on the FBX instance root: the clips' curve paths begin at its
            // child "rig.001". CreateFromThisModel put one there; if a future import setting drops
            // it, one is added in the same place.
            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<Avatar>().FirstOrDefault();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // -- physical presence, in true metres on the unscaled root --------------
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.6f;
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.radius = radius;
            capsule.height = bounds.size.y;
            capsule.center = new Vector3(0f, bounds.size.y * 0.5f, 0f);

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            float runSpeed = strideSpeed;
            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = radius;
            agent.height = bounds.size.y;
            agent.speed = runSpeed;
            agent.angularSpeed = 200f;
            agent.acceleration = 14f;
            agent.stoppingDistance = 2f;
            agent.autoBraking = true;

            // -- motor, animation, brain ---------------------------------------------
            var motor = root.AddComponent<NavMeshAgentMotor>();
            SetField(motor, "agent", agent);
            SetFloat(motor, "walkSpeedMultiplier", WalkFraction);

            var driver = root.AddComponent<AgentAnimatorDriver>();
            SetField(driver, "animator", animator);
            SetFloat(driver, "animationSpeedMultiplier", 1f);
            SetFloat(driver, "walkAnimBoost", 1f);
            SetFloat(driver, "animatorSpeedScale", 1f);
            SetFloat(driver, "measuredRunSpeed", runSpeed * 0.7f);

            var brain = root.AddComponent<AgentController>();
            SetField(brain, "MotorComponent", motor);
            SetField(brain, "animatorDriver", driver);

            var health = root.AddComponent<HealthComponent>();
            SetInt(health, "maxHealth", 120);
            SetInt(health, "currentHealth", 120);
            root.AddComponent<HealthReactionModule>();

            var faction = root.AddComponent<EntityFaction>();
            SetField(faction, "faction", AssetDatabase.LoadAssetAtPath<FactionDefinition>(FactionPath));
            SetField(faction, "relationshipTable", AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(RelationshipsPath));

            // A machine watches a wide arc, and a robot cowboy sees a long way across open sand.
            var perception = root.AddComponent<PerceptionModule>();
            SetFloat(perception, "fieldOfViewAngle", 150f);
            SetFloat(perception, "eyeHeight", bounds.size.y * 0.9f);
            SetFloat(perception, "memoryDuration", 10f);
            SetInt(perception, "occlusionLayers", LayerMaskOf("Default", "Ground", "Interior"));

            AgentTargeting targeting = root.GetComponent<AgentTargeting>();
            if (targeting == null) targeting = root.AddComponent<AgentTargeting>();
            SetField(targeting, "profile", WriteTargetingProfile());

            // -- behaviour: a patrol that chases, kites and shoots ---------------------
            // Script-added modules keep priority 0 (Unity does not call Reset for AddComponent),
            // so every one is set explicitly or it ties with the patrol.
            var patrol = root.AddComponent<PatrolModule>();
            SetPriority(patrol, ModulePriority.Fallback);
            SetFloat(patrol, "patrolRadius", 25f);

            var chase = root.AddComponent<ChaseModule>();
            SetPriority(chase, ModulePriority.Reactive);
            SetFloat(chase, "chaseStopDistance", 6f);

            var search = root.AddComponent<SearchModule>();
            SetPriority(search, ModulePriority.Reactive - 1);

            var keepDistance = root.AddComponent<KeepDistanceModule>();
            SetPriority(keepDistance, ModulePriority.Ambient);

            // -- the gun in its hand ---------------------------------------------------
            Transform hand = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "DEF-hand.R");
            if (hand == null) Debug.LogWarning("[ClankerBuilder] No DEF-hand.R bone; the held item falls back to a name search.");

            var inventory = root.AddComponent<EntityInventoryComponent>();
            SetInt(inventory, "inventorySize", 1);

            var equipment = root.AddComponent<EntityEquipmentController>();
            if (hand != null) SetField(equipment, "handSocket", hand);
            SetInt(equipment, "startingSlot", 0);
            SetBool(equipment, "aimHeldItem", true);
            SetFloat(equipment, "eyeHeight", bounds.size.y * 0.85f);

            var loadout = root.AddComponent<NpcRandomLoadout>();
            {
                var so = new SerializedObject(loadout);
                SerializedProperty candidates = so.FindProperty("candidates");
                InventoryItem[] items = HeldWeaponPaths.Select(AssetDatabase.LoadAssetAtPath<InventoryItem>).Where(i => i != null).ToArray();
                if (items.Length < HeldWeaponPaths.Length)
                    Debug.LogError("[ClankerBuilder] A held-weapon artifact in HeldWeaponPaths is missing.");
                candidates.arraySize = items.Length;
                for (int i = 0; i < items.Length; i++) candidates.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
                SerializedFields.SetInt(so, "slot", 0);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var use = root.AddComponent<NpcItemUseModule>();
            SetPriority(use, ModulePriority.RangedAttack);
            SetInt(use, "slotIndex", 0);
            SetEnum(use, "trigger", 0);                       // Trigger.TargetInRange
            SetFloat(use, "minRange", GunMinRange);
            SetFloat(use, "maxRange", GunMaxRange);
            SetFloat(use, "cooldown", 1.2f);
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

            EnsureFolder(System.IO.Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            Object.DestroyImmediate(root);
            if (!ok)
            {
                Debug.LogError($"[ClankerBuilder] Could not save {PrefabPath}.");
                return null;
            }

            Debug.Log($"[ClankerBuilder] Body scaled {scale:F3} to {bounds.size.y:F2} m tall, capsule r {radius:F2}; " +
                      $"run {runSpeed:F2} m/s, walk {runSpeed * WalkFraction:F2} m/s.");
            return saved;
        }

        private static bool Verify(out string report)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { report = "prefab did not save"; return false; }

            var missing = new List<string>();
            void Need<T>() where T : Component { if (prefab.GetComponentInChildren<T>(true) == null) missing.Add(typeof(T).Name); }
            Need<Animator>(); Need<AgentController>(); Need<NavMeshAgent>(); Need<EntityFaction>();
            Need<NpcItemUseModule>(); Need<EntityEquipmentController>(); Need<NpcRandomLoadout>(); Need<EntityLootTable>();
            Need<NetworkObject>(); Need<NetAuthority>(); Need<SaveableEntity>();
            Need<HealthComponent>(); Need<AgentGroundConform>();

            var animator = prefab.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.transform.Find("rig.001") == null)
                missing.Add("Animator is not on the object that owns rig.001 -- the clips bind to nothing");
            var controller = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
            if (controller == null)
                missing.Add("Animator has no controller");
            else if (controller.layers.Length == 0 || controller.layers[0].stateMachine.states.Length < 2)
                missing.Add("the controller has no states -- the robot would stand in its bind pose");

            var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skin != null && skin.sharedMaterials.Any(m => m == null || m.name.StartsWith("Material.")))
                missing.Add("a submesh still wears an FBX sub-asset material");

            var netObj = prefab.GetComponent<NetworkObject>();
            if (netObj != null && netObj.PrefabIdHash == 0)
                missing.Add("NetworkObject GlobalObjectIdHash is 0 -- clients cannot spawn it");

            var saveable = prefab.GetComponent<SaveableEntity>();
            if (saveable == null || string.IsNullOrEmpty(saveable.PrefabId))
                missing.Add("SaveableEntity has no prefabId -- it will vanish on load");

            report = missing.Count == 0
                ? "verified: animator on rig root, palette materials, network hash, save id."
                : "missing: " + string.Join("; ", missing);
            return missing.Count == 0;
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
            if (p == null) { Debug.LogWarning($"[ClankerBuilder] {target.GetType().Name}.{field} not found."); return; }
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
                if (layer < 0) Debug.LogWarning($"[ClankerBuilder] No layer named '{n}'.");
                else mask |= 1 << layer;
            }
            return mask;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
