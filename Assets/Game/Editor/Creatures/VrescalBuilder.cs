// Builds every Unity-side asset the Vrescal needs, from the exported FBX up.
//
// The FBX comes out of Blender via
// Assets/Game/Art/Models/_Source~/models/creatures/vrescal_export.py. Everything
// below that -- import settings, animation clips, the animator controller, the
// wildlife faction, and the prefab itself -- is generated here rather than
// hand-authored, for the same reason CrabWalkerBuilder and HorseBuilder exist:
// a prefab wired by hand is a prefab nobody can rebuild after the model changes.
//
// Re-running is safe and is the intended workflow. Re-export the FBX, run this,
// and the controller and prefab are rebuilt in place against the new clips.
// It does not overwrite an existing GlobalRelationships row.
//
// Re-run from: Tools > Creatures > Build Vrescal Prefab
using System.Collections.Generic;
using System.Linq;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.EditorTools
{
    public static class VrescalBuilder
    {
        private const string Fbx =
            "Assets/Game/Art/Models/Creatures/Organic/Vrescal/vrescal.fbx";
        private const string ControllerDir = "Assets/Game/Art/Animations/Creatures";
        private const string ControllerPath = ControllerDir + "/Vrescal.controller";
        private const string ClipDir = ControllerDir + "/Vrescal";
        // The FBX instance is named after the file; the re-pathed curves are
        // prefixed with it, so the two must agree.
        private const string ModelChild = "vrescal";
        private const string PrefabPath =
            "Assets/Game/Prefabs/Agents/Creatures/Vrescal.prefab";
        private const string FactionDir =
            "Assets/Game/ScriptableObjects/Factions/Core";
        private const string WildlifePath = FactionDir + "/WildlifeFaction.asset";
        private const string PlayerPath = FactionDir + "/PlayerFaction.asset";
        private const string RelationshipsPath = FactionDir + "/GlobalRelationships.asset";

        // Movement speeds, in metres per second, for a 5.5 m animal. The walk is
        // deliberately slow -- it crawls -- and the run is the sprint a
        // crocodilian can only hold in a straight line for a few seconds.
        private const float RunSpeed = 4.2f;
        private const float WalkSpeed = 1.6f;

        // Blender action -> (take name, first frame, last frame, loops).
        //
        // Looping clips stop one frame short of the authored length on purpose:
        // vrescal_anim.py makes the last frame an exact copy of the first so the
        // cycle closes, and playing both would hold that pose for two frames
        // every lap -- a visible hitch at the top of every stride.
        private struct Clip
        {
            public string Name, Take;
            public int First, Last;
            public bool Loop;
        }

        private static readonly Clip[] Clips =
        {
            new Clip { Name = "Vrescal_Idle",   Take = "Arm_Vrescal|Vrescal_Idle",   First = 1, Last = 95, Loop = true },
            new Clip { Name = "Vrescal_Walk",   Take = "Arm_Vrescal|Vrescal_Walk",   First = 1, Last = 39, Loop = true },
            new Clip { Name = "Vrescal_Run",    Take = "Arm_Vrescal|Vrescal_Run",    First = 1, Last = 25, Loop = true },
            new Clip { Name = "Vrescal_Attack", Take = "Arm_Vrescal|Vrescal_Attack", First = 1, Last = 34, Loop = false },
            new Clip { Name = "Vrescal_Hurt",   Take = "Arm_Vrescal|Vrescal_Hurt",   First = 1, Last = 18, Loop = false },
            new Clip { Name = "Vrescal_Death",  Take = "Arm_Vrescal|Vrescal_Death",  First = 1, Last = 56, Loop = false },
        };

        [MenuItem("Tools/Creatures/Build Vrescal Prefab")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Fbx) == null)
            {
                Debug.LogError($"No FBX at {Fbx}. Run vrescal_export.py first.");
                return;
            }

            ConfigureImporter();
            Dictionary<string, AnimationClip> clips = RehostClips();
            AnimatorController controller = BuildController(clips);
            FactionDefinition wildlife = EnsureWildlifeFaction();
            GameObject prefab = BuildPrefab(controller, wildlife);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Vrescal built: {PrefabPath}", prefab);
            Selection.activeObject = prefab;
        }

        // -------------------------------------------------------------------
        // 1. Model import
        // -------------------------------------------------------------------

        private static void ConfigureImporter()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Fbx);

            // Generic, not Humanoid: the skeleton is a crocodilian, and there is
            // no sane mapping onto a humanoid avatar. The avatar has to come from
            // this model because nothing else in the project shares its rig.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.importNormals = ModelImporterNormals.Import;

            // The limb and body meshes are parented to bones rather than skinned,
            // so they exist as real child transforms of the skeleton. Optimising
            // the hierarchy away would delete the very transforms the clips
            // animate, and the creature would import as a motionless heap.
            importer.optimizeGameObjects = false;
            importer.optimizeBones = false;

            importer.clipAnimations = Clips.Select(c => new ModelImporterClipAnimation
            {
                name = c.Name,
                takeName = c.Take,
                firstFrame = c.First,
                lastFrame = c.Last,
                loopTime = c.Loop,
                loopPose = c.Loop,
                wrapMode = c.Loop ? WrapMode.Loop : WrapMode.ClampForever,
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

        private static AnimationClip FindClip(string name)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(Fbx)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == name);
            if (clip == null)
                Debug.LogError($"Clip '{name}' missing from {Fbx} — check the " +
                               "take names in vrescal_export.py against Clips[].");
            return clip;
        }

        /// <summary>
        /// Copy the imported clips into standalone .anim assets with every curve
        /// path prefixed by the model child's name.
        ///
        /// This exists because of where the Animator has to live.
        /// <see cref="AgentAnimatorDriver"/> derives forward speed from
        /// <c>animator.transform.worldToLocalMatrix</c>, so the Animator must sit
        /// on a transform whose +Z is the creature's forward and whose scale is 1
        /// — that is, the clean prefab root. But Blender FBXs import in this
        /// project carrying a (270, 0, 0) rotation and a x100 scale on their root
        /// node (<c>horse_robot.fbx</c> is the same), which cannot be removed from
        /// the import, so the model has to stay a child. Leaving the Animator down
        /// on that child makes world +Z land on its local -X and <c>SpeedY</c>
        /// reads a constant zero: the blend tree never leaves Idle.
        ///
        /// Curve paths are relative to the Animator's GameObject, so moving the
        /// Animator up one level means re-pathing the curves up one level too.
        /// </summary>
        private static AnimationClip Rehost(AnimationClip source, string prefix,
                                            bool loop)
        {
            string path = $"{ClipDir}/{source.name}.anim";
            var clip = new AnimationClip { name = source.name, frameRate = source.frameRate };

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                EditorCurveBinding moved = binding;
                moved.path = string.IsNullOrEmpty(binding.path)
                    ? prefix
                    : prefix + "/" + binding.path;
                AnimationUtility.SetEditorCurve(
                    clip, moved, AnimationUtility.GetEditorCurve(source, binding));
            }

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // -------------------------------------------------------------------
        // 2. Animator controller
        // -------------------------------------------------------------------

        private static Dictionary<string, AnimationClip> RehostClips()
        {
            EnsureFolder(ClipDir);
            var map = new Dictionary<string, AnimationClip>();
            foreach (Clip c in Clips)
            {
                AnimationClip src = FindClip(c.Name);
                if (src == null) continue;
                AssetDatabase.DeleteAsset($"{ClipDir}/{c.Name}.anim");
                map[c.Name] = Rehost(src, ModelChild, c.Loop);
            }
            return map;
        }

        private static AnimatorController BuildController(
            Dictionary<string, AnimationClip> clips)
        {
            EnsureFolder(ControllerDir);
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller =
                AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            // These names are AgentAnimatorDriver's, verbatim, misspellings and
            // all — it calls SetFloat/SetBool on them unconditionally, and a
            // parameter it cannot find is a warning every frame. "Die" is here
            // as well as "Death" because AgentAnimatorDriver.TriggerDie sends
            // "Die" while HealthReactionModule sends "Death".
            controller.AddParameter("SpeedX", AnimatorControllerParameterType.Float);
            controller.AddParameter("SpeedY", AnimatorControllerParameterType.Float);
            controller.AddParameter("FallSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsImmobalized", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsAiming", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Hurt", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Death", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Meele", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine root = controller.layers[0].stateMachine;

            // One 1-D blend tree on forward speed. The thresholds are in true
            // metres per second, which only holds because the prefab sets
            // AgentAnimatorDriver's two scale factors to 1 — by default it
            // multiplies velocity by 3x and the tree would sit pinned at Run.
            var tree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "SpeedY",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(clips["Vrescal_Idle"], 0f);
            tree.AddChild(clips["Vrescal_Walk"], WalkSpeed);
            tree.AddChild(clips["Vrescal_Run"], RunSpeed);

            AnimatorState locomotion = root.AddState("Locomotion");
            locomotion.motion = tree;
            root.defaultState = locomotion;

            AnimatorState attack = AddOneShot(root, clips["Vrescal_Attack"],
                                              "Attack", "Meele", 0.08f);
            AnimatorState hurt = AddOneShot(root, clips["Vrescal_Hurt"],
                                            "Hurt", "Hurt", 0.05f);

            // Death has no way back: the clip ends on the corpse pose and holds
            // it, so there is no exit transition and no separate corpse state.
            AnimatorState death = root.AddState("Death");
            death.motion = clips["Vrescal_Death"];
            foreach (string trigger in new[] { "Death", "Die" })
            {
                AnimatorStateTransition t = root.AddAnyStateTransition(death);
                t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                t.duration = 0.1f;
                t.hasExitTime = false;
                t.canTransitionToSelf = false;
            }

            ReturnToLocomotion(attack, locomotion, 0.86f);
            ReturnToLocomotion(hurt, locomotion, 0.80f);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static AnimatorState AddOneShot(AnimatorStateMachine root,
                                                AnimationClip clip,
                                                string stateName,
                                                string trigger, float blend)
        {
            AnimatorState state = root.AddState(stateName);
            state.motion = clip;
            AnimatorStateTransition t = root.AddAnyStateTransition(state);
            t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            t.duration = blend;
            t.hasExitTime = false;
            // Without this a second trigger mid-attack restarts the state and the
            // creature stutters instead of finishing the bite.
            t.canTransitionToSelf = false;
            return state;
        }

        private static void ReturnToLocomotion(AnimatorState from,
                                               AnimatorState locomotion,
                                               float exitTime)
        {
            AnimatorStateTransition back = from.AddTransition(locomotion);
            back.hasExitTime = true;
            back.exitTime = exitTime;
            back.duration = 0.15f;
        }

        // -------------------------------------------------------------------
        // 3. Faction
        // -------------------------------------------------------------------

        private static FactionDefinition EnsureWildlifeFaction()
        {
            var wildlife = AssetDatabase.LoadAssetAtPath<FactionDefinition>(WildlifePath);
            if (wildlife == null)
            {
                EnsureFolder(FactionDir);
                wildlife = ScriptableObject.CreateInstance<FactionDefinition>();
                wildlife.factionName = "Wildlife";
                wildlife.debugColor = new Color(0.85f, 0.70f, 0.27f);
                AssetDatabase.CreateAsset(wildlife, WildlifePath);
                Debug.Log($"Created {WildlifePath}");
            }

            var player = AssetDatabase.LoadAssetAtPath<FactionDefinition>(PlayerPath);
            var table = AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(
                RelationshipsPath);
            if (player == null || table == null)
            {
                Debug.LogWarning("PlayerFaction or GlobalRelationships missing — " +
                                 "the Vrescal will read as Neutral until a " +
                                 "Wildlife/Player pair exists.");
                return wildlife;
            }

            // `relationships` is private, so the row goes in through
            // SerializedObject rather than by reflection — it keeps the undo
            // stack and the .asset's serialisation honest.
            var so = new SerializedObject(table);
            SerializedProperty list = so.FindProperty("relationships");
            if (list == null)
            {
                Debug.LogWarning("FactionRelationshipTable.relationships not found " +
                                 "— was it renamed? Add the Wildlife/Player pair " +
                                 "by hand.");
                return wildlife;
            }

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty row = list.GetArrayElementAtIndex(i);
                Object a = row.FindPropertyRelative("factionA").objectReferenceValue;
                Object b = row.FindPropertyRelative("factionB").objectReferenceValue;
                bool match = (a == wildlife && b == player) ||
                             (a == player && b == wildlife);
                if (match)
                    return wildlife;      // already declared; do not clobber it
            }

            list.arraySize++;
            SerializedProperty added = list.GetArrayElementAtIndex(list.arraySize - 1);
            added.FindPropertyRelative("factionA").objectReferenceValue = wildlife;
            added.FindPropertyRelative("factionB").objectReferenceValue = player;
            added.FindPropertyRelative("relationship").enumValueIndex =
                (int)FactionRelationship.Hostile;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(table);
            Debug.Log("Added Wildlife <-> Player = Hostile to GlobalRelationships.");
            return wildlife;
        }

        // -------------------------------------------------------------------
        // 4. Prefab
        // -------------------------------------------------------------------

        private static GameObject BuildPrefab(AnimatorController controller,
                                              FactionDefinition wildlife)
        {
            // The prefab root is a FRESH GameObject and the model hangs under it,
            // the same shape CrabWalkerBuilder and HorseBuilder use.
            //
            // Do not be tempted to use the imported FBX object as the root. It
            // arrives carrying Blender's compensating transform -- for this model
            // rotation (270, 90, 0) and scale 27.59, being the 0.2759 export
            // factor times Unity's 100 file-unit scale -- with mesh data scaled
            // down to match. The creature therefore *looks* correct while every
            // value set in metres below is silently multiplied by 27.59: the
            // collider becomes 45 x 40 x 119 m and the NavMeshAgent a 26 m-radius
            // giant that no NavMesh can host. Nothing warns; it just cannot be
            // placed.
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var root = new GameObject("Vrescal");
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            // worldPositionStays: false — keep the FBX's own local transform
            // verbatim. It is what carries the authored pivot and orientation.
            model.transform.SetParent(root.transform, false);
            model.name = ModelChild;

            // The FBX brings its own Animator. Drop it: two Animators on one
            // hierarchy is a silent source of "the clip plays but nothing moves",
            // and this one sits on the rotated, x27.59 child where
            // AgentAnimatorDriver's velocity maths comes out wrong (see Rehost).
            foreach (Animator stray in model.GetComponentsInChildren<Animator>(true))
                Object.DestroyImmediate(stray);

            Animator animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;      // NavMeshAgent owns movement
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            // No avatar on purpose. A Generic Animator with none still plays
            // clips by transform path, which is all that is needed with root
            // motion off — and the model's avatar is rooted at the child, which
            // is the level we just moved off.
            animator.avatar = null;

            // -- physical presence ------------------------------------------
            // A box, not the capsule the generic-enemy profile uses: this animal
            // is 5.5 m long and 1.8 m wide, and a capsule around it would either
            // miss the tail or swallow half the dune.
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.78f, -0.30f);
            box.size = new Vector3(1.65f, 1.45f, 4.30f);

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = RunSpeed;
            agent.angularSpeed = 190f;
            agent.acceleration = 9f;
            agent.radius = 0.95f;
            agent.height = 1.7f;
            agent.stoppingDistance = 1.1f;
            agent.autoBraking = true;

            // -- motor and controller ---------------------------------------
            var motor = root.AddComponent<NavMeshAgentMotor>();
            SetField(motor, "agent", agent);
            // Walk is 1.6 of the agent's 4.2, and the blend tree's Walk threshold
            // is that same 1.6 -- change one and the creature moon-walks.
            SetFloat(motor, "walkSpeedMultiplier", WalkSpeed / RunSpeed);
            SetFloat(motor, "faceRotateSpeed", 5.5f);

            var animDriver = root.AddComponent<AgentAnimatorDriver>();
            SetField(animDriver, "animator", animator);
            // Both scales to 1 so SpeedY reaches the blend tree as true m/s.
            SetFloat(animDriver, "animationSpeedMultiplier", 1f);
            SetFloat(animDriver, "walkAnimBoost", 1f);

            var controllerComp = root.AddComponent<AgentController>();
            SetField(controllerComp, "MotorComponent", motor);
            SetField(controllerComp, "animatorDriver", animDriver);

            // -- health and reactions ---------------------------------------
            var health = root.AddComponent<HealthComponent>();
            SetInt(health, "maxHealth", 260);
            SetInt(health, "currentHealth", 260);
            root.AddComponent<HealthReactionModule>();

            // -- senses and behaviour ---------------------------------------
            var perception = root.AddComponent<PerceptionModule>();
            SetFloat(perception, "fieldOfViewAngle", 150f);   // eyes set wide
            SetFloat(perception, "eyeHeight", 1.35f);
            SetFloat(perception, "memoryDuration", 7f);
            // Left unset this warns every spawn and treats line-of-sight as always
            // clear. These are the three layers PerceptionModule falls back to.
            SetInt(perception, "occlusionLayers",
                   LayerMask.GetMask("Default", "Ground", "Interior"));

            var chase = root.AddComponent<ChaseModule>();
            SetFloat(chase, "chaseStopDistance", 2.6f);       // long snout
            SetFloat(chase, "chaseSpeedMultiplier", 1f);      // agent is already the sprint

            var melee = root.AddComponent<CloseCombatModule>();
            SetFloat(melee, "attackRange", 3.4f);
            SetFloat(melee, "attackCooldown", 2.1f);
            SetInt(melee, "attackDamage", 26);
            SetFloat(melee, "attackCommitDuration", 0.62f);   // the bite lands mid-clip

            var wander = root.AddComponent<WanderModule>();
            SetBool(wander, "limitWanderRadius", false);
            SetFloat(wander, "freeRoamRadius", 70f);
            // Must stay 1. WanderModule leaves isRunning false, so NavMeshAgentMotor
            // already scales the agent's 4.2 by walkSpeedMultiplier (1.6/4.2) before
            // this multiplier is applied on top -- the two compound. Anything below 1
            // therefore lands the roam speed *under* WalkSpeed while the blend tree
            // still keys Walk at exactly WalkSpeed, so the walk clip never reaches
            // full weight and the animal slides along in a half-idle pose. The 0.55
            // this used to carry produced 0.88 m/s against a 1.6 threshold: a 5.5 m
            // animal creeping at a third of a body length per second, which reads as
            // standing still. The crawl is already expressed by WalkSpeed itself.
            SetFloat(wander, "speedMultiplier", 1f);
            SetFloat(wander, "minWaitTime", 2.5f);
            SetFloat(wander, "maxWaitTime", 8f);

            var faction = root.AddComponent<EntityFaction>();
            SetField(faction, "faction", wildlife);
            SetField(faction, "relationshipTable",
                     AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(
                         RelationshipsPath));

            root.AddComponent<AgentTargeting>();

            // Roams between chunks, so it has to survive its chunk unloading.
            var tracked = root.AddComponent<SceneTracked>();
            SetEnum(tracked, "policy", (int)SceneTracked.UnloadPolicy.Migrate);
            SetBool(tracked, "keepChunksLoaded", false);

            EnsureFolder("Assets/Game/Prefabs/Agents/Creatures");
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        // -------------------------------------------------------------------
        // Serialized-field helpers
        //
        // Every setter goes through SerializedObject and warns instead of
        // throwing when a field is missing. The fields below are private
        // [SerializeField]s on components this file does not own, so a rename in
        // the agent system should show up as one clear warning per field the
        // next time someone rebuilds -- not as a prefab that looks fine and
        // silently has default values in it.
        // -------------------------------------------------------------------

        private static SerializedProperty Find(Object target, string field)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning(
                    $"{target.GetType().Name}.{field} not found — it was probably " +
                    "renamed. That value is left at its default on the Vrescal " +
                    "prefab; update VrescalBuilder.");
                return null;
            }
            return prop;
        }

        private static void Apply(SerializedProperty prop) =>
            prop.serializedObject.ApplyModifiedPropertiesWithoutUndo();

        private static void SetField(Object target, string field, Object value)
        {
            SerializedProperty p = Find(target, field);
            if (p == null) return;
            p.objectReferenceValue = value;
            Apply(p);
        }

        private static void SetFloat(Object target, string field, float value)
        {
            SerializedProperty p = Find(target, field);
            if (p == null) return;
            p.floatValue = value;
            Apply(p);
        }

        private static void SetInt(Object target, string field, int value)
        {
            SerializedProperty p = Find(target, field);
            if (p == null) return;
            p.intValue = value;
            Apply(p);
        }

        private static void SetBool(Object target, string field, bool value)
        {
            SerializedProperty p = Find(target, field);
            if (p == null) return;
            p.boolValue = value;
            Apply(p);
        }

        private static void SetEnum(Object target, string field, int index)
        {
            SerializedProperty p = Find(target, field);
            if (p == null) return;
            p.enumValueIndex = index;
            Apply(p);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = new List<string>(path.Split('/'));
            string built = parts[0];
            for (int i = 1; i < parts.Count; i++)
            {
                string next = built + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }
    }
}
