// Builds every Unity-side asset the Dune Rat needs, from the exported FBX up.
//
// The FBX comes out of Blender via
// Assets/Game/Art/Models/_Source~/models/creatures/dune_rat_export.py.
// Everything below that -- import settings, animation clips, the animator
// controller, the wildlife faction, and the prefab itself -- is generated here
// rather than hand-authored, for the same reason CrabWalkerBuilder,
// HorseBuilder and VrescalBuilder exist: a prefab wired by hand is a prefab
// nobody can rebuild after the model changes.
//
// Re-running is safe and is the intended workflow. Re-export the FBX, run this,
// and the controller and prefab are rebuilt in place against the new clips.
// It does not overwrite an existing GlobalRelationships row.
//
// Re-run from: Tools > Creatures > Build Dune Rat Prefab
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
    public static class DuneRatBuilder
    {
        private const string Fbx =
            "Assets/Game/Art/Models/Creatures/Organic/DuneRat/dune_rat.fbx";
        private const string ControllerDir = "Assets/Game/Art/Animations/Creatures";
        private const string ControllerPath = ControllerDir + "/DuneRat.controller";
        private const string PrefabPath =
            "Assets/Game/Prefabs/Agents/Creatures/DuneRat.prefab";
        private const string MaterialPath =
            "Assets/Game/Art/Models/Creatures/Organic/DuneRat/DuneRat.mat";
        private const string FactionDir =
            "Assets/Game/ScriptableObjects/Factions/Core";
        private const string WildlifePath = FactionDir + "/WildlifeFaction.asset";
        private const string PlayerPath = FactionDir + "/PlayerFaction.asset";
        private const string RelationshipsPath = FactionDir + "/GlobalRelationships.asset";

        // Movement speeds, in metres per second.
        //
        // These two are NOT design numbers to taste -- they are measured off the
        // clips. dune_rat_anim.py sweeps each planted toe backwards at a
        // constant rate, so every cycle carries a ground speed that follows
        // from the geometry:
        //
        //     speed = stance sweep / (duty x clip duration)
        //
        // and the script prints the result. Walk is 0.550 m of sweep over a
        // 0.62 duty in 0.800 s; Run is 0.772 m over a 0.36 duty in 0.467 s.
        // Retune the gait in Blender and these have to be retyped from what it
        // prints, or the feet will skate: the animal will be moving at a speed
        // its legs are not stepping out.
        private const float WalkSpeed = 1.109f;
        private const float RunSpeed = 4.595f;

        // The animal, measured off the .blend after dune_rat_rig.py places it:
        // 2.60 m nose to tail tip, 0.86 m across, 1.26 m to the ear tips, with
        // 1.69 m of that length being tail. Bipedal -- see the note on the
        // collider below, and the long comment in dune_rat_anim.py.
        private const float Height = 1.264f;

        // Blender action -> (take name, first frame, last frame, loops).
        //
        // Looping clips stop one frame short of the authored length on purpose:
        // dune_rat_anim.py makes the last frame an exact copy of the first so
        // the cycle closes, and playing both would hold that pose for two
        // frames every lap. On the 16-frame run that is a visible hitch at the
        // top of every stride.
        private struct Clip
        {
            public string Name, Take;
            public int First, Last;
            public bool Loop;
        }

        private static readonly Clip[] Clips =
        {
            new Clip { Name = "DuneRat_Idle",   Take = "Arm_DuneRat|DuneRat_Idle",   First = 1, Last = 90, Loop = true },
            new Clip { Name = "DuneRat_Walk",   Take = "Arm_DuneRat|DuneRat_Walk",   First = 1, Last = 25, Loop = true },
            new Clip { Name = "DuneRat_Run",    Take = "Arm_DuneRat|DuneRat_Run",    First = 1, Last = 15, Loop = true },
            new Clip { Name = "DuneRat_Attack", Take = "Arm_DuneRat|DuneRat_Attack", First = 1, Last = 30, Loop = false },
            new Clip { Name = "DuneRat_Hurt",   Take = "Arm_DuneRat|DuneRat_Hurt",   First = 1, Last = 16, Loop = false },
            new Clip { Name = "DuneRat_Death",  Take = "Arm_DuneRat|DuneRat_Death",  First = 1, Last = 50, Loop = false },
        };

        [MenuItem("Tools/Creatures/Build Dune Rat Prefab")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Fbx) == null)
            {
                Debug.LogError($"No FBX at {Fbx}. Run dune_rat_export.py first.");
                return;
            }

            ConfigureImporter();
            AnimatorController controller = BuildController();
            Material hide = EnsureHideMaterial();
            FactionDefinition wildlife = EnsureWildlifeFaction();
            GameObject prefab = BuildPrefab(controller, wildlife, hide);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Dune Rat built: {PrefabPath}", prefab);
            Selection.activeObject = prefab;
        }

        // -------------------------------------------------------------------
        // 1. Model import
        // -------------------------------------------------------------------

        private static void ConfigureImporter()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Fbx);

            // Generic, not Humanoid. The skeleton is a two-legged rodent with a
            // metre of tail and no arms worth the name; a humanoid avatar would
            // have to invent a mapping for all of it. The avatar comes from this
            // model because nothing else in the project shares the rig.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.importNormals = ModelImporterNormals.Import;

            // Unlike the Vrescal -- whose meshes are bone-parented, so stripping
            // transforms would delete the very things its clips animate -- this
            // is a single properly skinned mesh, and optimising would be safe.
            // It is left off anyway so the bones stay addressable: attaching a
            // blood effect or a bite socket to `head` needs a real transform,
            // and turning this on later is a one-line change with a visible
            // reason, whereas turning it off later is a mystery.
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
                               "take names printed by dune_rat_export.py " +
                               "against Clips[].");
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
            tree.AddChild(FindClip("DuneRat_Idle"), 0f);
            tree.AddChild(FindClip("DuneRat_Walk"), WalkSpeed);
            tree.AddChild(FindClip("DuneRat_Run"), RunSpeed);

            AnimatorState locomotion = root.AddState("Locomotion");
            locomotion.motion = tree;
            root.defaultState = locomotion;

            // Blends are shorter than the Vrescal's. This animal is a quarter
            // of its mass and the one-shots are half the length, so an 0.08 s
            // fade into a 30-frame attack eats a tenth of the clip.
            AnimatorState attack = AddOneShot(root, controller, "Attack",
                                              "DuneRat_Attack", "Meele", 0.06f);
            AnimatorState hurt = AddOneShot(root, controller, "Hurt",
                                            "DuneRat_Hurt", "Hurt", 0.04f);

            // Death has no way back: the clip ends on the corpse pose and holds
            // it, so there is no exit transition and no separate corpse state.
            AnimatorState death = root.AddState("Death");
            death.motion = FindClip("DuneRat_Death");
            foreach (string trigger in new[] { "Death", "Die" })
            {
                AnimatorStateTransition t = root.AddAnyStateTransition(death);
                t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                t.duration = 0.1f;
                t.hasExitTime = false;
                t.canTransitionToSelf = false;
            }

            ReturnToLocomotion(attack, locomotion, 0.84f);
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
            back.duration = 0.12f;
        }

        // -------------------------------------------------------------------
        // 3. Material
        // -------------------------------------------------------------------

        // An explicit, opaque URP material owned by this builder, rather than
        // whatever Unity synthesises from the FBX.
        //
        // This is not tidiness. Every hide entry in palette.blend ships with
        // Blender's blend_method set to 'HASHED' — alpha-hashed transparency —
        // and that rides through the FBX into the material Unity generates. The
        // Dune Rat rendered see-through in the scene for two independent
        // reasons, and this was the second one: even with the winding repaired,
        // an alpha-blended creature is still a ghost. Owning the material means
        // the prefab cannot inherit that again, from this palette entry or any
        // future one.
        //
        // The colour is Mat_Hide_Sand_Pale's, #E7B345 at roughness 0.72, so the
        // animal still matches the Vrescal and the rest of the desert.
        private static Material EnsureHideMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning("URP/Lit shader not found — falling back to " +
                                 "the FBX's own material. If the rat renders " +
                                 "see-through, this is why.");
                return existing;
            }

            Material mat = existing != null ? existing : new Material(shader);
            mat.shader = shader;

            // Straight sRGB #E7B345.
            var sand = new Color(231f / 255f, 179f / 255f, 69f / 255f, 1f);
            mat.SetColor("_BaseColor", sand);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 1f - 0.72f);

            // Opaque, explicitly and on every knob URP looks at. Setting
            // _Surface alone is not enough — the render queue and the blend
            // state are separate properties and a material that has ever been
            // transparent keeps them.
            mat.SetFloat("_Surface", 0f);                  // 0 = Opaque
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetFloat("_ZWrite", 1f);
            mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;

            if (existing == null)
                AssetDatabase.CreateAsset(mat, MaterialPath);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // -------------------------------------------------------------------
        // 4. Faction
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
                                 "the Dune Rat will read as Neutral until a " +
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
        // 5. Prefab
        // -------------------------------------------------------------------

        private static GameObject BuildPrefab(AnimatorController controller,
                                              FactionDefinition wildlife,
                                              Material hide)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            root.name = "DuneRat";
            root.transform.position = Vector3.zero;

            // The FBX arrives with its own Animator (Generic avatar). Reuse it
            // rather than adding a second one on the root -- two Animators on one
            // hierarchy is a silent source of "the clip plays but nothing moves".
            Animator animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;      // NavMeshAgent owns movement

            // AlwaysAnimate, not CullUpdateTransforms.
            //
            // CullUpdateTransforms decides whether to write bone transforms by
            // testing the SkinnedMeshRenderer's bounds against the camera, and
            // those bounds are computed by Unity from the bind pose — not from
            // the clips. This animal's clips move the tail and legs well
            // outside the bind-pose box, so the bounds are a poor proxy for
            // where it actually is, and the failure mode is a creature frozen
            // mid-stride while plainly on screen. That is a miserable bug to
            // find, and the saving on a handful of wildlife agents is not worth
            // being exposed to it.
            //
            // Set explicitly so it is recorded as a prefab override rather than
            // silently inherited from the FBX importer's default.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // The avatar rides in from the FBX. Assert it rather than assume:
            // a Generic rig with a missing or invalid avatar plays every clip
            // to no visible effect, which reads exactly like "the animator is
            // not running".
            if (animator.avatar == null || !animator.avatar.isValid)
                Debug.LogError("Dune Rat avatar is missing or invalid — the " +
                               "clips will play and nothing will move. Check " +
                               "avatarSetup on the FBX importer.");

            // Own the material outright; see EnsureHideMaterial.
            if (hide != null)
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = hide;
                    r.sharedMaterials = mats;
                }
            }

            // -- physical presence ------------------------------------------
            // A box covering the *body*, stopping short of the tail. 1.69 m of
            // this animal's 2.60 m is tail, and it is a whip held out behind for
            // balance -- wrapping it would give a 0.9 m creature a collider
            // longer than a groundcar, and the player would be blocked by empty
            // air a body length behind it. The Vrescal takes the opposite
            // choice for the opposite reason: its tail is as thick as its trunk.
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.62f, 0.04f);
            box.size = new Vector3(0.80f, 1.16f, 1.70f);

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = RunSpeed;
            agent.angularSpeed = 340f;             // small, light, turns hard
            agent.acceleration = 16f;
            agent.radius = 0.42f;
            agent.height = Height;
            agent.stoppingDistance = 1.0f;
            agent.autoBraking = true;

            // -- motor and controller ---------------------------------------
            var motor = root.AddComponent<NavMeshAgentMotor>();
            SetField(motor, "agent", agent);
            // Walk is 1.109 of the agent's 4.595, and the blend tree's Walk
            // threshold is that same 1.109 -- change one and the rat moon-walks.
            SetFloat(motor, "walkSpeedMultiplier", WalkSpeed / RunSpeed);
            SetFloat(motor, "faceRotateSpeed", 9f);

            var animDriver = root.AddComponent<AgentAnimatorDriver>();
            SetField(animDriver, "animator", animator);
            // Both scales to 1 so SpeedY reaches the blend tree as true m/s.
            SetFloat(animDriver, "animationSpeedMultiplier", 1f);
            SetFloat(animDriver, "walkAnimBoost", 1f);

            var controllerComp = root.AddComponent<AgentController>();
            SetField(controllerComp, "MotorComponent", motor);
            SetField(controllerComp, "animatorDriver", animDriver);

            // -- health and reactions ---------------------------------------
            // Far softer than the Vrescal's 260. This one is fast and comes in
            // numbers; it is not meant to be a wall.
            var health = root.AddComponent<HealthComponent>();
            SetInt(health, "maxHealth", 90);
            SetInt(health, "currentHealth", 90);
            root.AddComponent<HealthReactionModule>();

            // -- senses and behaviour ---------------------------------------
            var perception = root.AddComponent<PerceptionModule>();
            SetFloat(perception, "fieldOfViewAngle", 210f);   // prey eyes, set wide
            SetFloat(perception, "eyeHeight", 0.97f);         // measured: head bone
            SetFloat(perception, "memoryDuration", 5f);
            // Left unset this mask reads as Nothing, line-of-sight always
            // succeeds, and PerceptionModule warns once per spawn while
            // falling back to these same three layers. Setting it explicitly
            // is the difference between a rat that can be hidden from and one
            // that sees through the dunes.
            SetLayerMask(perception, "occlusionLayers",
                         new[] { "Default", "Ground", "Interior" });

            var chase = root.AddComponent<ChaseModule>();
            SetFloat(chase, "chaseStopDistance", 1.4f);
            SetFloat(chase, "chaseSpeedMultiplier", 1f);      // agent is already the sprint

            var melee = root.AddComponent<CloseCombatModule>();
            SetFloat(melee, "attackRange", 1.7f);
            SetFloat(melee, "attackCooldown", 1.2f);
            SetInt(melee, "attackDamage", 12);
            // The strike lands on frame 16 of the 30-frame attack, which at
            // 30 fps is 0.53 s in. Committing for 0.5 s holds the animal still
            // until the bite has actually connected.
            SetFloat(melee, "attackCommitDuration", 0.5f);

            var wander = root.AddComponent<WanderModule>();
            SetBool(wander, "limitWanderRadius", false);
            SetFloat(wander, "freeRoamRadius", 90f);
            // The same ratio as the motor's, so an unbothered rat travels at
            // exactly the speed the walk clip steps out.
            SetFloat(wander, "speedMultiplier", WalkSpeed / RunSpeed);
            SetFloat(wander, "minWaitTime", 1.5f);
            SetFloat(wander, "maxWaitTime", 6f);

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

            // Every component this prefab needs must be added HERE. A rebuild overwrites the asset
            // wholesale, so anything added by hand in the Inspector is silently gone.
            AgentGroundConformWiring.Ensure(root);

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
                    "renamed. That value is left at its default on the Dune Rat " +
                    "prefab; update DuneRatBuilder.");
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

        // Layers are looked up by name and skipped if the project does not have
        // one, so adding this creature never silently masks the wrong layer
        // just because a layer list was reordered.
        private static void SetLayerMask(Object target, string field, string[] layers)
        {
            SerializedProperty p = Find(target, field);
            if (p == null) return;
            int mask = 0;
            foreach (string name in layers)
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer < 0)
                {
                    Debug.LogWarning($"No layer named '{name}' — left out of " +
                                     $"{target.GetType().Name}.{field}.");
                    continue;
                }
                mask |= 1 << layer;
            }
            p.intValue = mask;
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
