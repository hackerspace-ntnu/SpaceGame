// Builds every Unity-side asset the Golem needs, from the exported FBX up.
//
// The FBX comes out of Blender via
// Assets/Game/Art/Models/_Source~/models/creatures/golem_export.py. Everything
// below that -- import settings, animation clips, the animator controller, the
// wildlife faction, and the prefab itself -- is generated here rather than
// hand-authored, for the same reason VrescalBuilder, CrabWalkerBuilder and
// HorseBuilder exist: a prefab wired by hand is a prefab nobody can rebuild
// after the model changes.
//
// Re-running is safe and is the intended workflow. Re-export the FBX, run this,
// and the controller and prefab are rebuilt in place against the new clips.
// It does not overwrite an existing GlobalRelationships row.
//
// Re-run from: Tools > Creatures > Build Golem Prefab
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
    public static class GolemBuilder
    {
        private const string Fbx =
            "Assets/Game/Art/Models/Creatures/Constructs/Golem/golem.fbx";
        private const string ControllerDir = "Assets/Game/Art/Animations/Creatures";
        private const string ControllerPath = ControllerDir + "/Golem.controller";
        private const string PrefabPath =
            "Assets/Game/Prefabs/Agents/Creatures/Golem.prefab";
        private const string FactionDir =
            "Assets/Game/ScriptableObjects/Factions/Core";
        private const string WildlifePath = FactionDir + "/WildlifeFaction.asset";
        private const string PlayerPath = FactionDir + "/PlayerFaction.asset";
        private const string RelationshipsPath = FactionDir + "/GlobalRelationships.asset";

        // Movement speeds, in metres per second, for a 2.60 m construct.
        //
        // These are not taste. golem_anim.py foot-locks every locomotion clip:
        // during stance a contact has to travel backwards at exactly the speed
        // the body travels forwards, so
        //
        //     speed = 2 * half_stride / (duty * cycle_seconds)
        //
        // and the script prints these two figures when it runs. Change a stride
        // or a duty factor there and these must be updated to match, or the
        // golem moon-walks. The walk is genuinely slow because the legs are
        // short -- the hip sits only 1.34 m up on a 2.60 m body -- and the run
        // is quick because it is a bound: two flight phases per cycle mean the
        // contacts only have to track the ground for 35% of it.
        private const float RunSpeed = 3.86f;
        private const float WalkSpeed = 0.97f;

        // Blender action -> (take name, first frame, last frame, loops).
        //
        // Looping clips stop one frame short of the authored length on purpose:
        // golem_anim.py makes the last frame an exact copy of the first so the
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
            new Clip { Name = "Golem_Idle",   Take = "Arm_Golem|Golem_Idle",   First = 1, Last = 119, Loop = true },
            new Clip { Name = "Golem_Walk",   Take = "Arm_Golem|Golem_Walk",   First = 1, Last = 35,  Loop = true },
            new Clip { Name = "Golem_Run",    Take = "Arm_Golem|Golem_Run",    First = 1, Last = 25,  Loop = true },
            new Clip { Name = "Golem_Attack", Take = "Arm_Golem|Golem_Attack", First = 1, Last = 48,  Loop = false },
            new Clip { Name = "Golem_Hurt",   Take = "Arm_Golem|Golem_Hurt",   First = 1, Last = 22,  Loop = false },
            new Clip { Name = "Golem_Death",  Take = "Arm_Golem|Golem_Death",  First = 1, Last = 72,  Loop = false },
        };

        [MenuItem("Tools/Creatures/Build Golem Prefab")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Fbx) == null)
            {
                Debug.LogError($"No FBX at {Fbx}. Run golem_export.py first.");
                return;
            }

            ConfigureImporter();
            AnimatorController controller = BuildController();
            FactionDefinition wildlife = EnsureWildlifeFaction();
            GameObject prefab = BuildPrefab(controller, wildlife);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Golem built: {PrefabPath}", prefab);
            Selection.activeObject = prefab;
        }

        // -------------------------------------------------------------------
        // 1. Model import
        // -------------------------------------------------------------------

        private static void ConfigureImporter()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Fbx);

            // Generic, not Humanoid. The golem is a knuckle-walker whose arms
            // are as load-bearing as its legs and whose rest pose is a
            // four-point stance; retargeting that onto a humanoid avatar would
            // stand it up and destroy the read. The avatar has to come from
            // this model because nothing else in the project shares its rig.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.importNormals = ModelImporterNormals.Import;

            // The FBX is written Z-up, straight out of Blender's own axes, and
            // Unity is asked to bake the Z-up -> Y-up conversion into the data.
            //
            // This is load-bearing and not obvious. Left off, Unity applies the
            // conversion to the animation curves but *not* to the bind pose,
            // because the bind pose's share of it lives on the armature node
            // and Unity discards that node's rotation: the golem stood up
            // correctly whenever a clip was playing and lay on its back in the
            // scene view whenever one was not. Pre-rotating the data in
            // golem_export.py instead fixes the bind pose and double-rotates
            // every clip. Baking here is the only setting that gets both.
            importer.bakeAxisConversion = true;

            // The golem is 30 separate rocks parented to bones rather than
            // skinned, so they exist as real child transforms of the skeleton.
            // Optimising the hierarchy away would delete the very transforms
            // the clips animate, and the creature would import as a motionless
            // heap of boulders.
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
                               "take names in golem_export.py against Clips[].");
            return clip;
        }

        // -------------------------------------------------------------------
        // 2. Animator controller
        // -------------------------------------------------------------------

        private static AnimatorController BuildController()
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
            tree.AddChild(FindClip("Golem_Idle"), 0f);
            tree.AddChild(FindClip("Golem_Walk"), WalkSpeed);
            tree.AddChild(FindClip("Golem_Run"), RunSpeed);

            AnimatorState locomotion = root.AddState("Locomotion");
            locomotion.motion = tree;
            root.defaultState = locomotion;

            // Longer blends than a light creature gets. Snapping a two-tonne
            // construct between states in 80 ms is the single fastest way to
            // make it read as papier-mache.
            AnimatorState attack = AddOneShot(root, controller, "Attack",
                                              "Golem_Attack", "Meele", 0.16f);
            AnimatorState hurt = AddOneShot(root, controller, "Hurt",
                                            "Golem_Hurt", "Hurt", 0.10f);

            // Death has no way back: the clip ends on the collapsed pose and
            // holds it for its last twenty frames, so there is no exit
            // transition and no separate corpse state.
            AnimatorState death = root.AddState("Death");
            death.motion = FindClip("Golem_Death");
            foreach (string trigger in new[] { "Death", "Die" })
            {
                AnimatorStateTransition t = root.AddAnyStateTransition(death);
                t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                t.duration = 0.14f;
                t.hasExitTime = false;
                t.canTransitionToSelf = false;
            }

            // The attack's recovery runs to frame 48 but the golem is back on
            // all fours by 38, so it leaves early enough not to feel rooted.
            ReturnToLocomotion(attack, locomotion, 0.82f);
            ReturnToLocomotion(hurt, locomotion, 0.78f);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static AnimatorState AddOneShot(AnimatorStateMachine root,
                                                AnimatorController controller,
                                                string stateName, string clipName,
                                                string trigger, float blend)
        {
            AnimatorState state = root.AddState(stateName);
            state.motion = FindClip(clipName);
            AnimatorStateTransition t = root.AddAnyStateTransition(state);
            t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            t.duration = blend;
            t.hasExitTime = false;
            // Without this a second trigger mid-swing restarts the state and the
            // creature stutters instead of finishing the slam.
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
            back.duration = 0.22f;
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
                                 "the Golem will read as Neutral until a " +
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
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            root.name = "Golem";
            root.transform.position = Vector3.zero;

            // The FBX arrives with its own Animator (Generic avatar). Reuse it
            // rather than adding a second one on the root -- two Animators on
            // one hierarchy is a silent source of "the clip plays but nothing
            // moves".
            Animator animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;      // NavMeshAgent owns movement
            // AlwaysAnimate, not CullUpdateTransforms. The golem is 30 separate
            // bone-parented renderers with no skinned mesh, so the bounds Unity
            // culls against are whatever those 30 small boxes happen to cover
            // in the bind pose -- not a volume that follows the animation. When
            // it decides the creature is off-screen it stops writing the
            // transforms, and the golem freezes mid-stride in plain sight.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // -- physical presence ------------------------------------------
            // Measured off the built prefab in world space: 2.60 m across the
            // shoulders, 2.58 m tall, 2.51 m deep with the fists planted, soles
            // on y = 0. A capsule around that would either miss both arms or
            // swallow the ground between the legs, so it gets the box.
            //
            // These are root-*local* numbers and they are only metres because
            // the root sits at scale 1 -- see the `apply_scale_options` note in
            // golem_export.py. If a future export ever puts a factor back on
            // the root, this collider silently inflates by that factor: a
            // 260-metre box that swallows the terrain, with nothing rendering
            // any differently. Verify `lossyScale` and `BoxCollider.bounds` in
            // world space, never the inspector's local values.
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(-0.02f, 1.29f, 0.07f);
            box.size = new Vector3(2.60f, 2.58f, 2.51f);

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = RunSpeed;
            // Slow to turn on purpose. Everything else about the creature says
            // "mass", and a construct that pivots like a turret undoes all of
            // it in one frame.
            agent.angularSpeed = 110f;
            agent.acceleration = 5f;
            agent.radius = 0.95f;
            agent.height = 2.6f;
            agent.stoppingDistance = 1.6f;
            agent.autoBraking = true;

            // -- motor and controller ---------------------------------------
            var motor = root.AddComponent<NavMeshAgentMotor>();
            SetField(motor, "agent", agent);
            // Walk is 0.97 of the agent's 3.86, and the blend tree's Walk
            // threshold is that same 0.97 -- change one and the golem skates.
            SetFloat(motor, "walkSpeedMultiplier", WalkSpeed / RunSpeed);
            SetFloat(motor, "faceRotateSpeed", 3.2f);

            var animDriver = root.AddComponent<AgentAnimatorDriver>();
            SetField(animDriver, "animator", animator);
            // Both scales to 1 so SpeedY reaches the blend tree as true m/s.
            SetFloat(animDriver, "animationSpeedMultiplier", 1f);
            SetFloat(animDriver, "walkAnimBoost", 1f);

            var controllerComp = root.AddComponent<AgentController>();
            SetField(controllerComp, "MotorComponent", motor);
            SetField(controllerComp, "animatorDriver", animDriver);

            // -- health and reactions ---------------------------------------
            // It is made of rock. Killing it should take a magazine, not a clip.
            var health = root.AddComponent<HealthComponent>();
            SetInt(health, "maxHealth", 420);
            SetInt(health, "currentHealth", 420);
            root.AddComponent<HealthReactionModule>();

            // -- senses and behaviour ---------------------------------------
            var perception = root.AddComponent<PerceptionModule>();
            SetFloat(perception, "fieldOfViewAngle", 130f);   // head is low and forward
            SetFloat(perception, "eyeHeight", 1.55f);
            SetFloat(perception, "memoryDuration", 11f);      // it does not lose interest
            // Left unset this is Nothing, which makes every line-of-sight test
            // succeed through walls; PerceptionModule falls back to these three
            // at runtime and warns once per spawn asking to be told explicitly.
            SetInt(perception, "occlusionLayers",
                   LayerMaskOf("Default", "Ground", "Interior"));

            var chase = root.AddComponent<ChaseModule>();
            SetFloat(chase, "chaseStopDistance", 2.4f);
            SetFloat(chase, "chaseSpeedMultiplier", 1f);      // agent is already the bound

            var melee = root.AddComponent<CloseCombatModule>();
            SetFloat(melee, "attackRange", 3.0f);             // both fists, swung wide
            SetFloat(melee, "attackCooldown", 3.2f);
            SetInt(melee, "attackDamage", 45);
            // The slam lands on frame 26 of 48 at 30 fps.
            SetFloat(melee, "attackCommitDuration", 0.87f);

            var wander = root.AddComponent<WanderModule>();
            SetBool(wander, "limitWanderRadius", false);
            SetFloat(wander, "freeRoamRadius", 45f);
            SetFloat(wander, "speedMultiplier", 0.25f);       // walk pace when unbothered
            SetFloat(wander, "minWaitTime", 4f);
            SetFloat(wander, "maxWaitTime", 14f);

            var faction = root.AddComponent<EntityFaction>();
            SetField(faction, "faction", wildlife);
            SetField(faction, "relationshipTable",
                     AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(
                         RelationshipsPath));

            // Several of the modules above declare [RequireComponent] on this,
            // so by now it is usually already there. Adding a second one is
            // legal and silent, and leaves the prefab with two components
            // fighting over the same target slot.
            if (root.GetComponent<AgentTargeting>() == null)
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
                    "renamed. That value is left at its default on the Golem " +
                    "prefab; update GolemBuilder.");
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

        // LayerMask.GetMask returns 0 for the whole call if any one name is
        // missing, which would silently reinstate the "Nothing" it is here to
        // avoid. This skips absent layers and warns instead.
        private static int LayerMaskOf(params string[] names)
        {
            int mask = 0;
            foreach (string n in names)
            {
                int layer = LayerMask.NameToLayer(n);
                if (layer < 0)
                    Debug.LogWarning($"No layer named '{n}' — leaving it out of " +
                                     "the Golem's occlusion mask.");
                else
                    mask |= 1 << layer;
            }
            return mask;
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
