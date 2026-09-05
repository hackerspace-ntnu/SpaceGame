// Builds every Unity-side asset the Sandloper needs, from the exported FBX up.
//
// The Sandloper is a large, rideable cousin of the dune rat: the same hand-authored skeleton and
// the same six clips, on a subdivided and repainted mesh, at twice the size. This builder is
// descended from DuneRatBuilder and diverges in four places, all of them deliberate:
//
//   * a Scale of 2, applied to the prefab root
//   * five hide materials assigned by slot, instead of one stamped over every slot
//   * no ChaseModule and no CloseCombatModule -- he is a mount, not a predator
//   * the mount stack: a disabled MountModule, a SteerModule, and a SaddleSocket
//
// Nothing here writes to any dune rat asset. Its FBX, prefab, controller and material are
// untouched, which is the whole reason this is a second builder rather than an option on that one.
//
// The FBX comes out of Blender via
// Assets/Game/Art/Models/_Source~/models/creatures/sandloper_export.py.
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
// Re-run from: Tools > Creatures > Build Sandloper Prefab
using System.Collections.Generic;
using System.Linq;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.EditorTools
{
    public static class SandloperBuilder
    {
        private const string Fbx =
            "Assets/Game/Art/Models/Creatures/Organic/Sandloper/sandloper.fbx";
        private const string ControllerDir = "Assets/Game/Art/Animations/Creatures";
        private const string ControllerPath = ControllerDir + "/Sandloper.controller";
        private const string PrefabPath =
            "Assets/Game/Prefabs/Agents/creatures/Sandloper.prefab";
        private const string MaterialDir =
            "Assets/Game/Art/Models/Creatures/Organic/Sandloper";
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
        // How much bigger than the dune rat he is. The rat is 1.26 m to the ear
        // tips, which is a big rodent and nothing you could sit on; 2x puts his
        // back at 2.2 m and makes him 5.2 m nose to tail -- a mount you climb.
        //
        // Applied to the prefab ROOT, so his collider, his bones, the saddle and
        // both mount offsets scale with him for free. What does NOT is anything
        // in world units held outside a transform, and those are multiplied
        // explicitly below: the NavMeshAgent's radius, height and speeds, and
        // the eye height. Angles are scale-free and are left alone.
        private const float Scale = 2.0f;

        // Measured off the clips by dune_rat_anim.py and then scaled: a 2x leg
        // sweeping the same arc covers 2x the ground per cycle, so the feet
        // still match the floor at the same playback rate.
        // sandloper.py pushes every pose 1.75x further from rest than the dune rat's,
        // because the rat's clips were authored for a nervy 1.26 m animal and read as a
        // shuffle on something 5 m long. A leg that sweeps 1.75x further covers 1.75x the
        // ground per cycle, so these have to follow it or the feet skate -- and they are
        // also why he stopped moving "really slowly".
        //
        // KEEP IN STEP WITH GAIT_GAIN in sandloper.py. The two numbers are one decision.
        private const float GaitGain = 1.75f;

        private const float WalkSpeed = 1.109f * Scale * GaitGain;
        private const float RunSpeed = 4.595f * Scale * GaitGain;

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
            new Clip { Name = "Sandloper_Idle",   Take = "Arm_Sandloper|Sandloper_Idle",   First = 1, Last = 90, Loop = true },
            new Clip { Name = "Sandloper_Walk",   Take = "Arm_Sandloper|Sandloper_Walk",   First = 1, Last = 25, Loop = true },
            new Clip { Name = "Sandloper_Run",    Take = "Arm_Sandloper|Sandloper_Run",    First = 1, Last = 15, Loop = true },
            new Clip { Name = "Sandloper_Attack", Take = "Arm_Sandloper|Sandloper_Attack", First = 1, Last = 30, Loop = false },
            new Clip { Name = "Sandloper_Hurt",   Take = "Arm_Sandloper|Sandloper_Hurt",   First = 1, Last = 16, Loop = false },
            new Clip { Name = "Sandloper_Death",  Take = "Arm_Sandloper|Sandloper_Death",  First = 1, Last = 50, Loop = false },
            new Clip { Name = "Sandloper_Jump",   Take = "Arm_Sandloper|Sandloper_Jump",   First = 1, Last = 29, Loop = false },
        };

        [MenuItem("Tools/Creatures/Build Sandloper Prefab")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Fbx) == null)
            {
                Debug.LogError($"No FBX at {Fbx}. Run sandloper_export.py first.");
                return;
            }

            ConfigureImporter();
            AnimatorController controller = BuildController();
            Material[] hide = EnsureHideMaterials();
            FactionDefinition wildlife = EnsureWildlifeFaction();
            GameObject prefab = BuildPrefab(controller, wildlife, hide);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Sandloper built: {PrefabPath}", prefab);
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
                               "take names printed by sandloper_export.py " +
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
            tree.AddChild(FindClip("Sandloper_Idle"), 0f);
            tree.AddChild(FindClip("Sandloper_Walk"), WalkSpeed);
            tree.AddChild(FindClip("Sandloper_Run"), RunSpeed);

            AnimatorState locomotion = root.AddState("Locomotion");
            locomotion.motion = tree;
            root.defaultState = locomotion;

            // Blends are shorter than the Vrescal's. This animal is a quarter
            // of its mass and the one-shots are half the length, so an 0.08 s
            // fade into a 30-frame attack eats a tenth of the clip.
            AnimatorState attack = AddOneShot(root, controller, "Attack",
                                              "Sandloper_Attack", "Meele", 0.06f);
            AnimatorState hurt = AddOneShot(root, controller, "Hurt",
                                            "Sandloper_Hurt", "Hurt", 0.04f);

            // Death has no way back: the clip ends on the corpse pose and holds
            // it, so there is no exit transition and no separate corpse state.
            // The hop a rider asks for with Space. Driven by a BOOL, not a trigger: the motor
            // decides how long he is off the ground, and a triggered one-shot would run to its own
            // length and finish before or after he actually lands.
            AnimatorState jump = root.AddState("Jump");
            jump.motion = FindClip("Sandloper_Jump");

            AnimatorStateTransition intoJump = root.AddAnyStateTransition(jump);
            intoJump.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsGrounded");
            intoJump.hasExitTime = false;
            intoJump.duration = 0.06f;      // he leaves the ground now
            intoJump.canTransitionToSelf = false;

            AnimatorStateTransition outOfJump = jump.AddTransition(locomotion);
            outOfJump.AddCondition(AnimatorConditionMode.If, 0f, "IsGrounded");
            outOfJump.hasExitTime = false;
            outOfJump.duration = 0.14f;     // and absorbs the landing on the way back

            AnimatorState death = root.AddState("Death");
            death.motion = FindClip("Sandloper_Death");
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
        // The Dune Rat rendered see-through in the scene for two independent
        // reasons, and this was the second one: even with the winding repaired,
        // an alpha-blended creature is still a ghost. Owning the material means
        // the prefab cannot inherit that again, from this palette entry or any
        // future one.
        //
        // The colour is Mat_Hide_Sand_Pale's, #E7B345 at roughness 0.72, so the
        // animal still matches the Vrescal and the rest of the desert.
        // Five hides, not one.
        //
        // DuneRatBuilder stamps a single material across every slot, because the
        // rat ships as one flat colour. The Sandloper is painted per-face in
        // sandloper.py -- dark along the sunlit back, mid on the flank, pale
        // underneath, dark keratin on the crest and feet -- and flattening that
        // to one colour would throw the whole paint job away.
        //
        // Owned outright rather than inherited from the FBX for the reason the
        // rat's note gives: the palette's hide materials ship alpha-hashed, and
        // an alpha-blended creature is a ghost. The order matches the material
        // order sandloper.py appends, which is the contract between the two.
        private static readonly (string Name, Color Colour, float Rough)[] Hides =
        {
            ("Sandloper_Back",  new Color(0.596f, 0.451f, 0.251f), 0.62f),  // #987340
            ("Sandloper_Flank", new Color(0.788f, 0.737f, 0.604f), 0.74f),  // #C9BC9A
            ("Sandloper_Belly", new Color(0.886f, 0.847f, 0.753f), 0.38f),  // #E2D8C0
            ("Sandloper_Claw",  new Color(0.290f, 0.239f, 0.180f), 0.34f),  // #4A3D2E
            ("Sandloper_Ear",   new Color(0.369f, 0.482f, 0.478f), 0.68f),  // #5E7B7A
        };

        private static Material[] EnsureHideMaterials()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning("URP/Lit shader not found - falling back to the FBX's own " +
                                 "materials. If he renders see-through, this is why.");
                return null;
            }

            EnsureFolder(MaterialDir);
            var made = new Material[Hides.Length];

            for (int i = 0; i < Hides.Length; i++)
            {
                string path = $"{MaterialDir}/{Hides[i].Name}.mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                Material mat = existing != null ? existing : new Material(shader);
                mat.shader = shader;

                mat.SetColor("_BaseColor", Hides[i].Colour);
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Smoothness", 1f - Hides[i].Rough);

                // Opaque on every knob URP looks at. Setting _Surface alone is not
                // enough: the queue and the blend state are separate properties and
                // a material that has ever been transparent keeps them.
                mat.SetFloat("_Surface", 0f);
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

                if (existing == null) AssetDatabase.CreateAsset(mat, path);
                EditorUtility.SetDirty(mat);
                made[i] = mat;
            }
            return made;
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
                                 "the Sandloper will read as Neutral until a " +
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
                                              Material[] hide)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            root.name = "Sandloper";
            // One number is the whole of "make him big": colliders, bones, the saddle and both

            // mount offsets are authored in his own space and follow it.

            root.transform.localScale = Vector3.one * Scale;
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
                Debug.LogError("Sandloper avatar is missing or invalid - the " +
                               "clips will play and nothing will move. Check " +
                               "avatarSetup on the FBX importer.");

            // By SLOT, not stamped across every slot: the mesh carries five, in the
            // order sandloper.py appended them.
            if (hide != null)
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        mats[i] = hide[Mathf.Min(i, hide.Length - 1)];
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
            agent.acceleration = 16f * Scale;
            agent.radius = 0.42f * Scale;
            agent.height = Height * Scale;
            agent.stoppingDistance = 1.0f * Scale;
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
            SetFloat(perception, "eyeHeight", 0.97f * Scale);         // measured: head bone
            SetFloat(perception, "memoryDuration", 5f);
            // Left unset this mask reads as Nothing, line-of-sight always
            // succeeds, and PerceptionModule warns once per spawn while
            // falling back to these same three layers. Setting it explicitly
            // is the difference between a rat that can be hidden from and one
            // that sees through the dunes.
            SetLayerMask(perception, "occlusionLayers",
                         new[] { "Default", "Ground", "Interior" });

            // No ChaseModule and no CloseCombatModule, unlike the dune rat he is
            // descended from. He is a mount: something you walk up to and saddle, not
            // something that bites you while you try. He keeps the rat's wide prey
            // eyes and its wandering, and nothing else of its temperament.

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

            AttachSaddleSocket(root);

            // -- multiplayer ------------------------------------------------
            // DuneRatBuilder does not do this and the rat's prefab carries it anyway, from
            // whichever tool wired it after the fact. Left implicit here the Sandloper builds
            // bare: no NetworkObject means he exists only on the host, clients see nothing, and
            // SaddleSocket's fit/remove messages have no object to travel on -- so a client could
            // never saddle him.
            root.AddComponent<Unity.Netcode.NetworkObject>();
            root.AddComponent<NetworkedHealthComponent>();
            // simulationDrivers left empty: NetAuthority falls back to SimulationDrivers.Discover,
            // which finds the NavMeshAgent and the motor without being told.
            root.AddComponent<NetAuthority>();

            // -- persistence ------------------------------------------------
            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();
            root.AddComponent<HealthSaveable>();
            // Required by SaveablePolicy for anything with an AgentTargeting.
            root.AddComponent<AgentStateSaveable>();

            // Roams between chunks, so it has to survive its chunk unloading.
            var tracked = root.AddComponent<SceneTracked>();
            SetEnum(tracked, "policy", (int)SceneTracked.UnloadPolicy.Migrate);
            SetBool(tracked, "keepChunksLoaded", false);

            EnsureFolder("Assets/Game/Prefabs/Agents/creatures");
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>
        /// Somewhere to put a saddle, and the ability to be ridden once one is on.
        ///
        /// <para>
        /// The MountModule is added DISABLED. A disabled Behaviour is one the Interactor skips
        /// outright, so a bare Sandloper offers no "ride" verb at all rather than one that appears
        /// and refuses; <see cref="SaddleSocket"/> turns it on and off with the saddle.
        /// </para>
        /// </summary>
        private static void AttachSaddleSocket(GameObject root)
        {
            var mount = root.AddComponent<MountModule>();
            // Measured off the saddle: SEAT_Rider sits 0.109 m above the saddle's origin, and the
            // origin sits on his spine at (0, 0.15, 1.10) in his own file -- which is Unity
            // (x, z, -y) = (0, 1.10, -0.15). RiderSink 0 puts the rider on the seat's surface;
            // an animal has a body under the saddle, so the chair convention of dropping a whole
            // leg would bury them in him. See Saddles.md.
            Vector3 seat = new Vector3(0f, 1.10f, -0.15f);
            SetVector3(mount, "seatOffset", seat + Vector3.up * 0.109f);
            mount.enabled = false;

            // Steering. NavMeshAgentMotor is an IRiderControllable, so this is the whole of it.
            var steer = root.AddComponent<SteerModule>();
            SetField(steer, "mountModule", mount);
            SetBool(steer, "riderCanRun", true);
            // Space. Sandloper_Jump plays because NavMeshAgentMotor reports IsAirborne and
            // AgentAnimatorDriver hands that to IsGrounded -- see MountSystem.md.
            SetBool(steer, "jumpEnabled", true);
            SetBool(steer, "leapEnabled", false);

            var socket = root.AddComponent<SaddleSocket>();
            SetField(socket, "saddlePrefab", AssetDatabase.LoadAssetAtPath<GameObject>(
                         "Assets/Game/Prefabs/Items/Saddles/SandloperSaddle.prefab"));
            // The ONE saddle item, shared with Appa. What differs per animal is the worn prefab
            // above, not the thing you carry -- you have "a saddle", and which one appears is the
            // animal's business.
            SetField(socket, "saddleItem", AssetDatabase.LoadAssetAtPath<InventoryItem>(
                         "Assets/Game/Resources/Items/Artifacts/Saddle.asset"));
            SetField(socket, "mount", mount);
            SetVector3(socket, "rootPosition", seat);
            SetFloat(socket, "dropRadius", 1.2f * Scale);
            SetFloat(socket, "dropHeight", 0.9f * Scale);

            // Q while standing beside him, as well as E aimed at the saddle's own grips.
            var quick = root.AddComponent<SaddleQuickRelease>();
            SetField(quick, "socket", socket);
            SetFloat(quick, "reach", 2.5f * Scale);

            root.AddComponent<SaddleSaveable>();
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
                    "renamed. That value is left at its default on the Sandloper " +
                    "prefab; update SandloperBuilder.");
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

        private static void SetVector3(Object target, string field, Vector3 value)
        {
            SerializedProperty p = Find(target, field);
            if (p == null) return;
            p.vector3Value = value;
            p.serializedObject.ApplyModifiedPropertiesWithoutUndo();
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

