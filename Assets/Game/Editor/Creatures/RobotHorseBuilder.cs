// Builds every Unity-side asset the robot horse needs, from the exported FBX up: the import
// settings, the animator controller, the rust material, and TWO prefabs from one component list.
//
// The FBX comes out of Blender via
// Assets/Game/Art/Models/_Source~/models/creatures/robot_horse/robot_horse_export.py, from a
// rig the same folder's robot_horse_rig.py rebuilt out of the author's horse1.blend (rigid
// per-plate binding, hooves on the cannon bones, IK gone) and robot_horse_anim.py animated
// (Idle, Walk, Run, TurnL, TurnR, all in place).
//
// Re-running is safe and is the intended workflow. Re-export the FBX, run this, and the
// controller and both prefabs are rebuilt in place against the new clips. **Everything the horse
// needs must be in this file**: the prefabs are overwritten wholesale, so a component added by
// hand in the Inspector is discarded by the next build with nothing said.
//
// ## What it is
//
// A machine built like a horse, and the two things the Clankers ride it as:
//
//   RobotHorse       Wildlife. Fauna, so it targets nobody on its own; it wanders, it flees a
//                    gunshot, it turns and kicks when cornered (FightOrFlightModule, the Appa
//                    pattern). Born saddled, so a player who finds one can climb on -- and the
//                    saddle can be taken off like any other.
//   ClankerOutrider  The same horse on the Clankers' side, with a Clanker in the saddle. The
//                    HORSE is the agent (NpcPassenger: the rider is cargo with its brain off): it
//                    patrols the town, joins the faction's alerts, and charges what it is handed,
//                    trampling at the end. Shoot the rider and it gets off and fights on foot;
//                    the horse keeps coming.
//
// Mounted running is the point of the animal. The run clip is played fast (AnimatorSpeedScale)
// and the agent's speed is derived from the clip's own stride at that rate, so the feet keep up
// with 14 m/s instead of skating under it.
//
// Re-run from: Tools > Creatures > Build Robot Horse
using System.Linq;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Core.Persistence.EditorTools;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.EditorTools
{
    public static class RobotHorseBuilder
    {
        public const string Fbx = "Assets/Game/Art/Models/Creatures/Robotic/RobotHorse/robot_horse.fbx";
        public const string ControllerPath = "Assets/Game/Art/Animations/Creatures/RobotHorse.controller";
        public const string WildPrefabPath = "Assets/Game/Prefabs/Agents/creatures/RobotHorse.prefab";
        public const string OutriderPrefabPath = "Assets/Game/Prefabs/Agents/Robots/ClankerOutrider.prefab";

        private const string MaterialPath = "Assets/Game/Art/Materials/Characters/RobotHorse_Body.mat";
        private const string PaletteMaterial = "Assets/Game/Art/Materials/Vehicles/Mat_Metal_Rust_Heavy (DoubleSided).mat";
        private const string RustTexture = "Assets/Game/Art/Textures/Creatures/RobotHorse_Rust.png";

        // The Sandloper's saddle rather than Appa's: it is the narrow one with the boards behind
        // the cantle, and Appa's panniers were fitted to a bison's flank. Both fit the ONE saddle
        // item. SEAT_Rider_Loper sits 0.109 m above that saddle's origin (SandloperBuilder).
        private const string SaddlePrefabPath = "Assets/Game/Prefabs/Items/Saddles/SandloperSaddle.prefab";
        private const string SaddleItemPath = "Assets/Game/Resources/Items/Artifacts/Saddle.asset";
        private const float SeatRise = 0.109f;

        private const string FactionDir = "Assets/Game/ScriptableObjects/Factions/Core";
        private const string FaunaPath = FactionDir + "/FaunaFaction.asset";
        private const string RobotFactionPath = FactionDir + "/RobotFaction.asset";
        private const string RelationshipsPath = FactionDir + "/GlobalRelationships.asset";

        // ── the model, measured off robot_horse.blend (robot_horse_rig.py's probe) ─────────
        //
        // Blender (x, y, z) arrives in Unity as (-x, z, -y). The horse faces -Y in the file, so
        // +Z here. At scale 1: 2.13 m to the ear tips, 1.53 m at the back over spine2, belly at
        // 0.79 m, 0.71 m across the barrel, nose at +1.64 and tail at -0.93.
        private const float BodyHeight = 2.13f;
        private const float BackHeight = 1.527f;
        private const float BellyHeight = 0.794f;
        private const float BodyHalfWidth = 0.36f;
        private const float HeadHeight = 1.86f;

        // Where the saddle's origin lands on the back, in the model's own space: the top of the
        // back over spine2's head (Blender y 0.208).
        private static readonly Vector3 SaddleSeat = new Vector3(0f, BackHeight, -0.208f);

        /// <summary>
        /// How much bigger than the file it is built, on the prefab ROOT (the Appa convention:
        /// colliders, bones, seat and saddle all scale with it for free; only NavMeshAgent
        /// numbers and speeds are world units and are multiplied by hand). 1.8 puts the back at
        /// 2.75 m, which is what a 3.2 m Clanker needs under it and reads as a big machine
        /// under a player.
        /// </summary>
        public const float Scale = 1.8f;

        // ── speeds, derived from the clips rather than picked ──────────────────────────────
        //
        // robot_horse_anim.py prints the ground speed each gait's stance phase implies, at scale
        // 1 and playback rate 1: a hind hoof travels 1.22 m per 1.67 s walk cycle and 1.77 m per
        // 0.92 s run cycle. Multiply by the scale and the playback rate and the feet match the
        // floor. The rate is what makes the gallop fast; the stride is what keeps it honest.
        private const float WalkStrideSpeed = 1.13f;
        private const float RunStrideSpeed = 5.53f;
        public const float AnimatorSpeedScale = 1.4f;
        public const float WalkSpeed = WalkStrideSpeed * Scale * AnimatorSpeedScale;   // 2.85 m/s
        public const float RunSpeed = RunStrideSpeed * Scale * AnimatorSpeedScale;     // 13.9 m/s

        // Turning on the spot. The turn clips sweep TURN_SWEEP_DEG (30) per 32-frame cycle, so
        // at rate 1.4 they step out ~32 deg/s. The agent is allowed 60: a machine that gallops at
        // 14 m/s and pivots at a stroll cannot follow a path, and the feet slipping a little in a
        // turn is the lesser evil (the same trade Appa makes at 45).
        private const float TurnSpeed = 60f;
        private const float TurnEnterRate = 18f;
        private const float TurnExitRate = 9f;
        private const float TurnEnterSpeed = 0.5f;
        private const float TurnExitSpeed = 0.9f;

        // The Clanker rider's origin sits this far below the seat's surface, in world metres. A
        // Clanker cannot be posed by MountedRiderPose (Generic rig), so it plays its crouch clip
        // in the saddle (IsSeated -> CrouchIdle, ClankerBuilder). Measured by sampling that clip
        // on the built prefab: DEF-spine stands at 1.38 m above the soles and crouches to 0.95.
        public const float ClankerSeatDrop = 0.95f;

        private struct Clip
        {
            public string Name, Take;
            public int Last;
        }

        // Every clip loops and stops one frame short of the authored length, because the last
        // authored frame is an exact copy of the first (robot_horse_anim.py keys frames 0..N).
        private static readonly Clip[] Clips =
        {
            new Clip { Name = "RobotHorse_Idle",  Take = "Arm_RobotHorse|RobotHorse_Idle",  Last = 143 },
            new Clip { Name = "RobotHorse_Walk",  Take = "Arm_RobotHorse|RobotHorse_Walk",  Last = 39 },
            new Clip { Name = "RobotHorse_Run",   Take = "Arm_RobotHorse|RobotHorse_Run",   Last = 21 },
            new Clip { Name = "RobotHorse_TurnL", Take = "Arm_RobotHorse|RobotHorse_TurnL", Last = 31 },
            new Clip { Name = "RobotHorse_TurnR", Take = "Arm_RobotHorse|RobotHorse_TurnR", Last = 31 },
        };

        /// <summary>The two animals this file builds. Everything that differs between them.</summary>
        private readonly struct Design
        {
            public readonly string Name, PrefabPath, FactionPath;
            public readonly bool Wild;

            public Design(string name, string prefabPath, string factionPath, bool wild)
            {
                Name = name; PrefabPath = prefabPath; FactionPath = factionPath; Wild = wild;
            }
        }

        private static readonly Design[] Designs =
        {
            new Design("RobotHorse", WildPrefabPath, FaunaPath, wild: true),
            new Design("ClankerOutrider", OutriderPrefabPath, RobotFactionPath, wild: false),
        };

        [MenuItem("Tools/Creatures/Build Robot Horse")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Fbx) == null)
            {
                Debug.LogError($"No FBX at {Fbx}. Run robot_horse_export.py first.");
                return;
            }

            ConfigureImporter();
            if (!ClipsAreImported()) return;

            AnimatorController controller = BuildController();
            Material body = BuildMaterial();

            foreach (Design design in Designs)
            {
                GameObject prefab = BuildPrefab(design, controller, body);
                Debug.Log($"{design.Name} built: {design.PrefabPath}", prefab);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            NetworkPrefabRegistrar.Sync(out int added, out int total);
            if (added > 0) Debug.Log($"[RobotHorse] Registered {added} network prefab(s); {total} in the project.");

            // The wiring passes add the rest of the savers and stamp prefabIds, and the ragdoll
            // pass gives death a body. Both are re-run because this build just threw their work
            // away (see AppaBuilder.WireSaveables for how that went unnoticed once).
            if (!SaveableWiring.TryWirePrefabs())
                Debug.LogError("[RobotHorse] Wire Saveable Prefabs refused to run (Play mode?). The " +
                               "horses have no prefabId and will not survive a reload. Build again.");
            RagdollWiring.WirePrefabs();

            Verify();
        }

        // -------------------------------------------------------------------
        // 1. Model import
        // -------------------------------------------------------------------

        private static void ConfigureImporter()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.importNormals = ModelImporterNormals.Import;
            // One skinned mesh over 32 bones; nothing is bone-parented, but the saddle socket and
            // the seat look bones up by name, and an optimised hierarchy has no names to find.
            importer.optimizeGameObjects = false;
            importer.optimizeBones = false;

            importer.clipAnimations = Clips.Select(c => new ModelImporterClipAnimation
            {
                name = c.Name,
                takeName = c.Take,
                firstFrame = 0,
                lastFrame = c.Last,
                loopTime = true,
                loopPose = true,
                wrapMode = WrapMode.Loop,
                keepOriginalPositionY = true,
                keepOriginalPositionXZ = true,
                keepOriginalOrientation = true,
                lockRootRotation = true,
                lockRootHeightY = true,
                lockRootPositionXZ = true,
            }).ToArray();

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Refuse to build on a partial clip set. The first import after new takes are added can
        /// still read the old clips back (AppaBuilder.ClipsAreImported has the history).
        /// </summary>
        private static bool ClipsAreImported()
        {
            var present = new System.Collections.Generic.HashSet<string>(
                AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>().Select(c => c.name));
            string[] missing = Clips.Select(c => c.Name).Where(n => !present.Contains(n)).ToArray();
            if (missing.Length == 0) return true;

            Debug.LogError($"Robot horse NOT built: clip(s) missing from the imported FBX -- " +
                           $"{string.Join(", ", missing)}. Run Tools > Creatures > Build Robot Horse again; " +
                           "if they are still missing, check the take names printed by robot_horse_export.py.");
            return false;
        }

        private static AnimationClip FindClip(string name)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == name);
            if (clip == null) Debug.LogError($"Clip '{name}' missing from {Fbx}.");
            return clip;
        }

        // -------------------------------------------------------------------
        // 2. Animator controller
        // -------------------------------------------------------------------

        private static AnimatorController BuildController()
        {
            EnsureFolder(System.IO.Path.GetDirectoryName(ControllerPath).Replace('\\', '/'));

            // Emptied through the API and rebuilt in place -- see AnimatorControllerRebuild for
            // the two ways of doing this that each produced a controller with no states.
            AnimatorController controller = AnimatorControllerRebuild.LoadOrCreateEmpty(ControllerPath);

            // AgentAnimatorDriver's names, verbatim, misspelling included. Hurt has no clip and
            // stays a parameter so the driver's SetTrigger is not a warning every hit.
            controller.AddParameter("SpeedX", AnimatorControllerParameterType.Float);
            controller.AddParameter("SpeedY", AnimatorControllerParameterType.Float);
            controller.AddParameter("TurnSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("FallSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsImmobalized", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsAiming", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Hurt", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Death", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine root = controller.layers[0].stateMachine;

            // One 1-D tree on forward speed in true m/s (the driver's two clip-choosing
            // multipliers are set to 1 on the prefab). Walk and Run sit at exactly the speeds
            // their strides cover at the playback rate, so between them the blend is honest too.
            var tree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "SpeedY",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(FindClip("RobotHorse_Idle"), 0f);
            tree.AddChild(FindClip("RobotHorse_Walk"), WalkSpeed);
            tree.AddChild(FindClip("RobotHorse_Run"), RunSpeed);

            AnimatorState locomotion = root.AddState("Locomotion");
            locomotion.motion = tree;
            root.defaultState = locomotion;

            // Turning on the spot, the Appa arrangement: TurnSpeed is a yaw rate, positive to
            // the right, so the left clip sits on the negative side with Idle in the middle.
            var turnTree = new BlendTree
            {
                name = "Turn",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "TurnSpeed",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(turnTree, controller);
            turnTree.AddChild(FindClip("RobotHorse_TurnL"), -TurnSpeed);
            turnTree.AddChild(FindClip("RobotHorse_Idle"), 0f);
            turnTree.AddChild(FindClip("RobotHorse_TurnR"), TurnSpeed);

            AnimatorState turn = root.AddState("Turn");
            turn.motion = turnTree;

            foreach (var (mode, threshold) in new[]
                     {
                         (AnimatorConditionMode.Greater, TurnEnterRate),
                         (AnimatorConditionMode.Less, -TurnEnterRate),
                     })
            {
                AnimatorStateTransition into = locomotion.AddTransition(turn);
                into.AddCondition(AnimatorConditionMode.Less, TurnEnterSpeed, "SpeedY");
                into.AddCondition(mode, threshold, "TurnSpeed");
                into.hasExitTime = false;
                into.duration = 0.2f;
            }

            AnimatorStateTransition settled = turn.AddTransition(locomotion);
            settled.AddCondition(AnimatorConditionMode.Less, TurnExitRate, "TurnSpeed");
            settled.AddCondition(AnimatorConditionMode.Greater, -TurnExitRate, "TurnSpeed");
            settled.hasExitTime = false;
            settled.duration = 0.25f;

            AnimatorStateTransition walkedOff = turn.AddTransition(locomotion);
            walkedOff.AddCondition(AnimatorConditionMode.Greater, TurnExitSpeed, "SpeedY");
            walkedOff.hasExitTime = false;
            walkedOff.duration = 0.15f;

            // Death is the ragdoll's (RagdollWiring adds AgentRagdoll); this state only stops the
            // gait for the frames before physics takes the body.
            AnimatorState death = root.AddState("Death");
            death.motion = FindClip("RobotHorse_Idle");
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

        // -------------------------------------------------------------------
        // 3. Material
        // -------------------------------------------------------------------

        /// <summary>
        /// The heavy-rust palette material with a rust albedo on top. The mesh is 291 rigid
        /// plates each carrying its primitive's default UVs, so a tileable texture reads as
        /// weathering on every part rather than as one image stretched over the animal.
        /// </summary>
        private static Material BuildMaterial()
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(PaletteMaterial);
            if (source == null)
            {
                Debug.LogError($"[RobotHorseBuilder] Palette material missing: {PaletteMaterial}");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.CopyPropertiesFromMaterial(source);
                material.shader = source.shader;
            }

            var rust = AssetDatabase.LoadAssetAtPath<Texture2D>(RustTexture);
            if (rust == null)
                Debug.LogError($"[RobotHorseBuilder] No rust texture at {RustTexture}; the horse is flat brown.");
            material.SetTexture("_BaseMap", rust);
            material.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
            // The albedo carries the rust colour itself; the palette's tint would double it.
            material.SetColor("_BaseColor", new Color(0.92f, 0.86f, 0.8f, 1f));
            material.SetFloat("_Metallic", 0.55f);
            material.SetFloat("_Smoothness", 0.28f);
            // Closed plates: the (DoubleSided) source exists for open-sheet hulls.
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            EditorUtility.SetDirty(material);
            return material;
        }

        // -------------------------------------------------------------------
        // 4. Prefabs
        // -------------------------------------------------------------------

        private static GameObject BuildPrefab(Design design, AnimatorController controller, Material body)
        {
            EnsureFolder(System.IO.Path.GetDirectoryName(design.PrefabPath).Replace('\\', '/'));

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            root.name = design.Name;
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.transform.localScale = Vector3.one * Scale;

            // Soles onto y = 0. The file's lowest vertex sits 0.08 m below its origin (the hooves'
            // undersides), and every agent prefab here is authored soles-at-pivot so the ground
            // conform's offset starts from zero. The rig and mesh are the root's direct children.
            Bounds bind = BindPoseBounds(root);
            foreach (Transform child in root.transform)
                child.localPosition += Vector3.up * (-bind.min.y / Scale);

            // -- look ------------------------------------------------------------------
            foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.sharedMaterials = Enumerable.Repeat(body, skin.sharedMaterials.Length).ToArray();
                skin.updateWhenOffscreen = true;   // bind-pose bounds are not where a gallop is
            }

            Animator animator = root.GetComponentInChildren<Animator>(true) ?? root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // -- physical presence, in the model's units under the scaled root ----------
            // Barrel and legs as one block: a box from the hooves to the back over the body's
            // length, so you cannot walk between the legs but can stand beside the neck.
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, BackHeight * 0.5f, 0f);
            box.size = new Vector3(BodyHalfWidth * 2f, BackHeight, 1.7f);

            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = RunSpeed;            // the run; the AI walks at a fraction of it
            agent.angularSpeed = TurnSpeed;
            agent.acceleration = 8f * Scale;   // a gallop it can actually get into
            agent.radius = 0.45f * Scale;
            agent.height = BodyHeight * Scale;
            agent.stoppingDistance = 1.5f * Scale;
            agent.autoBraking = true;

            // -- motor, animation, brain ----------------------------------------------
            var motor = root.AddComponent<NavMeshAgentMotor>();
            SetField(motor, "agent", agent);
            SetFloat(motor, "walkSpeedMultiplier", WalkSpeed / RunSpeed);
            SetFloat(motor, "faceRotateSpeed", 3f);
            SetFloat(motor, "mountedJumpHeight", 1.0f * Scale);

            var driver = root.AddComponent<AgentAnimatorDriver>();
            SetField(driver, "animator", animator);
            SetFloat(driver, "animationSpeedMultiplier", 1f);
            SetFloat(driver, "walkAnimBoost", 1f);
            SetFloat(driver, "animatorSpeedScale", AnimatorSpeedScale);

            var brain = root.AddComponent<AgentController>();
            SetField(brain, "MotorComponent", motor);
            SetField(brain, "animatorDriver", driver);

            // -- health --------------------------------------------------------------
            var health = root.AddComponent<HealthComponent>();
            SetInt(health, "maxHealth", 220);
            SetInt(health, "currentHealth", 220);
            root.AddComponent<HealthReactionModule>();

            // -- senses ----------------------------------------------------------------
            var perception = root.AddComponent<PerceptionModule>();
            SetFloat(perception, "fieldOfViewAngle", design.Wild ? 220f : 170f);
            SetFloat(perception, "eyeHeight", HeadHeight * Scale);
            SetFloat(perception, "memoryDuration", 8f);
            SetInt(perception, "occlusionLayers", LayerMaskOf("Default", "Ground", "Interior"));

            var ears = root.AddComponent<NoiseReceiverModule>();
            SetInt(ears, "investigateOn", (int)(design.Wild ? NoiseTypeMask.None : NoiseTypeMask.Gunshot | NoiseTypeMask.Explosion));
            SetInt(ears, "aggroOn", (int)(design.Wild ? NoiseTypeMask.Gunshot | NoiseTypeMask.Explosion : NoiseTypeMask.None));
            SetInt(ears, "priority", ModulePriority.Reactive - 2);

            // -- faction and targeting -----------------------------------------------
            var faction = root.AddComponent<EntityFaction>();
            SetField(faction, "faction", AssetDatabase.LoadAssetAtPath<FactionDefinition>(design.FactionPath));
            SetField(faction, "relationshipTable", AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(RelationshipsPath));

            AgentTargeting targeting = root.GetComponent<AgentTargeting>();
            if (targeting == null) targeting = root.AddComponent<AgentTargeting>();
            if (!design.Wild)
                SetField(targeting, "profile", AssetDatabase.LoadAssetAtPath<TargetingProfile>(ClankerBuilder.TargetingProfilePath));

            var provocation = root.AddComponent<ProvocationModule>();
            SetFloat(provocation, "leashRange", design.Wild ? 45f : 100f);
            SetFloat(provocation, "calmDownDelay", 45f);
            SetInt(provocation, "damageThreshold", 5);

            // -- behaviour --------------------------------------------------------------
            // Priorities set explicitly on every module: AddComponent does not run Reset, so a
            // script-added module keeps priority 0 and ties with the fallback.
            if (design.Wild) AddWildBehaviour(root, driver, provocation);
            else AddOutriderBehaviour(root);

            var chase = root.AddComponent<ChaseModule>();
            SetFloat(chase, "chaseStopDistance", (design.Wild ? 2.5f : 3.5f) * Scale);
            SetFloat(chase, "chaseSpeedMultiplier", 1f);
            SetInt(chase, "priority", ModulePriority.Reactive);

            // A kick (wild) or a trampling charge (outrider). No clip: the shove is the visual.
            var melee = root.AddComponent<CloseCombatModule>();
            SetFloat(melee, "attackRange", 3.2f * Scale);
            SetFloat(melee, "attackCooldown", design.Wild ? 2.5f : 2f);
            SetInt(melee, "attackDamage", design.Wild ? 30 : 25);
            SetFloat(melee, "attackCommitDuration", 0.5f);
            SetString(melee, "attackAnimTrigger", string.Empty);
            SetInt(melee, "priority", ModulePriority.MeleeAttack);
            SetFloat(melee, "knockbackSpeed", design.Wild ? 12f : 16f);
            SetFloat(melee, "knockbackLift", 0.3f);
            SetFloat(melee, "knockbackLeapDistance", 5f);
            SetFloat(melee, "knockbackLeapHeight", 1.5f);
            SetFloat(melee, "knockbackLeapDuration", 0.5f);

            // -- streaming, multiplayer, persistence -------------------------------------
            var tracked = root.AddComponent<SceneTracked>();
            SetEnum(tracked, "policy", (int)SceneTracked.UnloadPolicy.Migrate);
            SetBool(tracked, "keepChunksLoaded", false);
            root.AddComponent<SpaceGame.World.Safety.UnderTerrainGuard>();

            root.AddComponent<Unity.Netcode.NetworkObject>();
            root.AddComponent<ClientNetworkTransform>();
            root.AddComponent<NetRelay>();
            root.AddComponent<NetAuthority>();
            root.AddComponent<NetworkedHealthComponent>();

            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();
            root.AddComponent<HealthSaveable>();
            root.AddComponent<AgentStateSaveable>();
            root.AddComponent<ProvocationSaveable>();
            if (design.Wild) root.AddComponent<FleeSaveable>();

            // -- the saddle, and who is in it ----------------------------------------------
            AttachSaddle(root);
            if (!design.Wild) AttachRider(root);

            AgentGroundConformWiring.Ensure(root);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, design.PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>Wander, flee a gunshot, and turn to kick when cornered: the Appa temperament.</summary>
        private static void AddWildBehaviour(GameObject root, AgentAnimatorDriver driver, ProvocationModule provocation)
        {
            var wander = root.AddComponent<WanderModule>();
            SetBool(wander, "limitWanderRadius", false);
            SetFloat(wander, "freeRoamRadius", 70f);
            SetFloat(wander, "speedMultiplier", WalkSpeed / RunSpeed);
            SetFloat(wander, "minWaitTime", 4f);
            SetFloat(wander, "maxWaitTime", 14f);
            SetInt(wander, "priority", ModulePriority.Fallback);

            var flee = root.AddComponent<FleeModule>();
            SetBool(flee, "fleeFromCurrentTarget", true);
            SetFloat(flee, "triggerRadius", 25f);
            SetFloat(flee, "safeRadius", 60f);
            SetFloat(flee, "fleeSpeedMultiplier", 1f);   // the run IS the flee
            SetInt(flee, "priority", ModulePriority.Override);

            // No roar clip and no roar sound: the trigger, flag and file are cleared so the
            // module skips the telegraph and goes straight from cornered to the kick.
            var temperament = root.AddComponent<FightOrFlightModule>();
            SetField(temperament, "fleeModule", flee);
            SetField(temperament, "animatorDriver", driver);
            SetField(temperament, "provocation", provocation);
            SetInt(temperament, "enrageDamage", 80);
            SetFloat(temperament, "corneredDistance", 6f * Scale);
            SetFloat(temperament, "rageDuration", 10f);
            SetString(temperament, "roarTrigger", string.Empty);
            SetString(temperament, "roaringFlag", string.Empty);
            SetString(temperament, "roarFile", string.Empty);
            SetFloat(temperament, "roarDuration", 0f);
            SetInt(temperament, "priority", ModulePriority.Override + 1);
        }

        /// <summary>
        /// Patrol the town and answer the faction's alerts. The same alert wiring as the Clanker
        /// on foot (ClankerBuilder), so a rider's sighting reaches the outrider and its charge
        /// is announced back.
        /// </summary>
        private static void AddOutriderBehaviour(GameObject root)
        {
            var patrol = root.AddComponent<PatrolModule>();
            SetInt(patrol, "priority", ModulePriority.Fallback);
            SetFloat(patrol, "patrolRadius", 45f);
            SetFloat(patrol, "minWaitTime", 2f);
            SetFloat(patrol, "maxWaitTime", 6f);

            var search = root.AddComponent<SearchModule>();
            SetInt(search, "priority", ModulePriority.Reactive - 1);

            var broadcaster = root.AddComponent<AlertBroadcaster>();
            SetFloat(broadcaster, "alertRadius", ClankerBuilder.AlertRadius);
            var receiver = root.AddComponent<AlertReceiverModule>();
            SetInt(receiver, "priority", ModulePriority.Reactive - 1);
            SetFloat(receiver, "alertDuration", 15f);
        }

        /// <summary>
        /// Born saddled. The MountModule starts disabled as on every saddled animal and
        /// <see cref="SaddleSocket"/> enables it in Awake because startSaddled is on; a player
        /// can still take the saddle off, and a reload restores whichever state was saved.
        /// </summary>
        private static void AttachSaddle(GameObject root)
        {
            var mount = root.AddComponent<MountModule>();
            SetVector3(mount, "seatOffset", SaddleSeat + Vector3.up * SeatRise);
            mount.enabled = false;

            var steer = root.AddComponent<SteerModule>();
            SetField(steer, "mountModule", mount);
            SetBool(steer, "riderCanRun", true);
            SetBool(steer, "jumpEnabled", true);
            SetBool(steer, "leapEnabled", false);

            var socket = root.AddComponent<SaddleSocket>();
            GameObject saddle = AssetDatabase.LoadAssetAtPath<GameObject>(SaddlePrefabPath);
            if (saddle == null)
                Debug.LogError($"No saddle at {SaddlePrefabPath}. Run Tools > Creatures > Build Saddle first.");
            SetField(socket, "saddlePrefab", saddle);
            SetField(socket, "saddleItem", AssetDatabase.LoadAssetAtPath<InventoryItem>(SaddleItemPath));
            SetField(socket, "mount", mount);
            SetVector3(socket, "rootPosition", SaddleSeat);
            SetBool(socket, "startSaddled", true);
            SetFloat(socket, "dropRadius", 1.6f * Scale);
            SetFloat(socket, "dropHeight", 1.2f * Scale);

            var quick = root.AddComponent<SaddleQuickRelease>();
            SetField(quick, "socket", socket);
            SetFloat(quick, "reach", 3f * Scale);

            root.AddComponent<SaddleSaveable>();
            // Bends a humanoid rider's legs round the barrel; ignores a Generic one.
            root.AddComponent<MountedRiderPose>();
            // The networked half of mounting: server-decided seats, ownership to the rider.
            root.AddComponent<MountNetworkSync>();
        }

        /// <summary>
        /// A Clanker in the saddle, spawned by the horse on the server at start. The seat marker
        /// is a child of the scaled root, so the drop below the seat is given in model units.
        /// </summary>
        private static void AttachRider(GameObject root)
        {
            var seat = new GameObject("SeatPoint");
            seat.transform.SetParent(root.transform, false);
            seat.transform.localPosition = SaddleSeat + Vector3.up * SeatRise;

            GameObject rider = AssetDatabase.LoadAssetAtPath<GameObject>(ClankerBuilder.PrefabPath);
            if (rider == null)
                Debug.LogError($"No Clanker at {ClankerBuilder.PrefabPath}; build it first or the outrider rides empty.");

            var passenger = root.AddComponent<NpcPassenger>();
            SetField(passenger, "riderPrefab", rider);
            SetField(passenger, "seatPoint", seat.transform);
            SetVector3(passenger, "seatOffset", new Vector3(0f, -ClankerSeatDrop / Scale, 0f));
            SetBool(passenger, "spawnOnStart", true);
            SetFloat(passenger, "dismountSideOffset", 2.6f);
            SetFloat(passenger, "dismountSampleDistance", 8f);
        }

        private static Bounds BindPoseBounds(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // -------------------------------------------------------------------
        // 5. Verify
        // -------------------------------------------------------------------

        private static void Verify()
        {
            foreach (Design design in Designs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(design.PrefabPath);
                if (prefab == null) { Debug.LogError($"[RobotHorseBuilder] {design.PrefabPath} was not written."); continue; }

                var saveable = prefab.GetComponent<SaveableEntity>();
                if (saveable == null || string.IsNullOrEmpty(saveable.PrefabId))
                    Debug.LogError($"[RobotHorseBuilder] {design.Name} has no prefabId; it will not survive a reload.");
                if (prefab.GetComponent<Unity.Netcode.NetworkObject>() == null)
                    Debug.LogError($"[RobotHorseBuilder] {design.Name} has no NetworkObject.");
                if (!design.Wild && prefab.GetComponent<NpcPassenger>() == null)
                    Debug.LogError($"[RobotHorseBuilder] {design.Name} has nobody to ride it.");
            }
        }

        // -------------------------------------------------------------------
        // Serialized-field helpers (SerializedObject, so private fields serialize as the
        // Inspector would write them)
        // -------------------------------------------------------------------

        private static SerializedProperty Find(Object target, string field)
        {
            SerializedProperty prop = new SerializedObject(target).FindProperty(field);
            if (prop == null)
                Debug.LogError($"{target.GetType().Name} has no serialized field '{field}'. A rename " +
                               "upstream silently leaves this unset.", target);
            return prop;
        }

        private static void Apply(SerializedProperty p) => p.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        private static void SetField(Object t, string f, Object v) { var p = Find(t, f); if (p == null) return; p.objectReferenceValue = v; Apply(p); }
        private static void SetFloat(Object t, string f, float v) { var p = Find(t, f); if (p == null) return; p.floatValue = v; Apply(p); }
        private static void SetInt(Object t, string f, int v) { var p = Find(t, f); if (p == null) return; p.intValue = v; Apply(p); }
        private static void SetBool(Object t, string f, bool v) { var p = Find(t, f); if (p == null) return; p.boolValue = v; Apply(p); }
        private static void SetString(Object t, string f, string v) { var p = Find(t, f); if (p == null) return; p.stringValue = v; Apply(p); }
        private static void SetEnum(Object t, string f, int v) { var p = Find(t, f); if (p == null) return; p.enumValueIndex = v; Apply(p); }
        private static void SetVector3(Object t, string f, Vector3 v) { var p = Find(t, f); if (p == null) return; p.vector3Value = v; Apply(p); }

        private static int LayerMaskOf(params string[] names)
        {
            int mask = 0;
            foreach (string n in names)
            {
                int layer = LayerMask.NameToLayer(n);
                if (layer >= 0) mask |= 1 << layer;
            }
            return mask;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
