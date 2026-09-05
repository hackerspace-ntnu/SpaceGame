// Builds every Unity-side asset Appa needs, from the exported FBX up.
//
// The FBX comes out of Blender via
// Assets/Game/Art/Models/_Source~/models/creatures/appa_export.py.
// Everything below that -- import settings, animation clips, the animator
// controller and the prefab itself -- is generated here rather than
// hand-authored, for the same reason DuneRatBuilder and GolemBuilder exist:
// a prefab wired by hand is a prefab nobody can rebuild after the model changes.
//
// Re-running is safe and is the intended workflow. Re-export the FBX, run this,
// and the controller and prefab are rebuilt in place against the new clips.
//
// **Everything Appa needs must be in this file.** The builder overwrites the
// prefab wholesale, so a component added by hand in the Inspector is discarded
// by the next build with nothing said. GolemBuilder lost the Golem's
// SaveableEntity exactly that way, and the only symptom was a creature that
// quietly stopped persisting.
//
// ## What he is
//
// A six-legged bison. Friendly by default and no threat to anyone: FaunaFaction
// has zero rows in the relationship table, so AgentTargeting can never acquire
// a target on its own and the combat modules below simply never get a frame.
//
// Two things wake him up, and both go through AgentTargeting:
//
//   * **Being hurt.** ProvocationModule hands him his attacker.
//   * **Being shot at.** NoiseReceiverModule aggros on Gunshot, which hands him
//     the shooter. Nothing damaged him, so he stays in flee.
//
// FightOrFlightModule then decides which of the two stock behaviours answers:
// run (FleeModule, Override) or fight (Chase + CloseCombat, below it). It roars
// on the way into a fight, which is the player's warning.
//
// Re-run from: Tools > Creatures > Build Appa Prefab
using System.Linq;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Core.Persistence.EditorTools;
using SpaceGame.Gameplay;
using SpaceGame.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.EditorTools
{
    public static class AppaBuilder
    {
        private const string Fbx =
            "Assets/Game/Art/Models/Creatures/Organic/Appa/appa.fbx";
        private const string ControllerDir = "Assets/Game/Art/Animations/Creatures";
        private const string ControllerPath = ControllerDir + "/Appa.controller";
        private const string PrefabDir = "Assets/Game/Prefabs/Agents/creatures";
        private const string PrefabPath = PrefabDir + "/Appa.prefab";

        private const string FactionDir = "Assets/Game/ScriptableObjects/Factions/Core";
        private const string FaunaPath = FactionDir + "/FaunaFaction.asset";
        private const string RelationshipsPath = FactionDir + "/GlobalRelationships.asset";

        // Measured off the exported FBX rather than guessed: 5.75 m nose to
        // tail, 3.06 m across the horns, 3.26 m tall including the tail that
        // hangs below the sole plane in the author's sculpt.
        private const float BodyLength = 5.75f;
        private const float BodyWidth = 3.06f;

        // ── Speeds, derived from the clips rather than picked ────────────────
        //
        // Feet skate when the ground speed and the clip's own stride disagree,
        // and the fix is AgentAnimatorDriver.animatorSpeedScale -- which sets
        // the animator's playback RATE, not which clip is chosen. So the honest
        // order is: measure the stride, pick the scale, and let the speeds fall
        // out of it.
        //
        // Hip to sole is 1.25 m. appa_anim.py swings the femur +/-34 deg over a
        // 2.0 s walk and +/-52 deg over a 1.25 s run, so one cycle covers
        //     walk  2 * 1.25 * sin(34) = 1.40 m in 2.00 s = 0.70 m/s
        //     run   2 * 1.25 * sin(52) = 1.97 m in 1.25 s = 1.58 m/s
        // at playback rate 1. AnimatorSpeedScale 2.0 lifts those to 1.4 and 3.15,
        // which are the numbers below and the blend-tree thresholds.
        //
        // The reach was widened (from 26/40 deg) because the legs read as stiff:
        // the hoof sits at the end of the chain and inherits every rotation above
        // it, so the feet swung convincingly while the legs barely tilted. The
        // longer stride is why the playback rate came DOWN from 2.5 to 2.0 for
        // the same ground speed -- covering more distance per cycle means fewer
        // cycles per second, which also reads calmer on an animal this size.
        //
        // These make the feet match the ground; they do not claim to be the right
        // *feel*. That is a play-test question, not a derivation (GDC-L1-BAL-0005).
        private const float AnimatorSpeedScale = 2.0f;
        private const float WalkSpeed = 1.4f;
        private const float RunSpeed = 3.15f;

        // Turning on the spot, derived the same way. Appa_TurnL/R are 36 frames at
        // 24 fps, so 0.75 s per cycle at playback rate 2.0, and appa_anim.py's
        // TURN_SWEEP_DEG puts ~33 deg of body rotation in each cycle for the outer
        // legs -- 44 deg/s. The NavMeshAgent is held to that, down from the 130
        // it had while there was no turn clip to disagree with.
        //
        // Enter and exit thresholds are deliberately apart. A NavMeshAgent's yaw
        // rate crosses any single number several times a second while it settles
        // on a heading, and a creature that flickered between Idle and Turn twice
        // a second would look worse than one that never turned at all.
        private const float TurnSpeed = 45f;
        private const float TurnEnterRate = 18f;    // deg/s of yaw that starts the shuffle
        private const float TurnExitRate = 9f;      // and the slower rate that ends it
        // World-space radius of the "pet me" volume on his head. He is 3.06 m across the horns,
        // so a metre reads as "his head" and not as "his neck" or "anywhere near him".
        private const float PetTargetRadius = 1.0f;

        private const float TurnEnterSpeed = 0.35f; // only turn in place while near stationary
        private const float TurnExitSpeed = 0.60f;  // ... and hand back to the gait once walking

        // Blender action -> (take name, first frame, last frame, loops).
        //
        // The three looping clips stop one frame short of the authored length on
        // purpose: appa_anim.py makes their last frame an exact copy of the
        // first so the cycle closes, and playing both would hold that pose for
        // two frames every lap -- a visible hitch at the top of every stride.
        // The one-shots keep their last frame, which is their final pose.
        private struct Clip
        {
            public string Name, Take;
            public int First, Last;
            public bool Loop;
        }

        private static readonly Clip[] Clips =
        {
            new Clip { Name = "Appa_Idle",  Take = "Arm_Appa|Appa_Idle",  First = 0, Last = 191, Loop = true },
            new Clip { Name = "Appa_Walk",  Take = "Arm_Appa|Appa_Walk",  First = 0, Last = 47, Loop = true },
            new Clip { Name = "Appa_Run",   Take = "Arm_Appa|Appa_Run",   First = 0, Last = 29, Loop = true },
            new Clip { Name = "Appa_TurnL", Take = "Arm_Appa|Appa_TurnL", First = 0, Last = 35, Loop = true },
            new Clip { Name = "Appa_TurnR", Take = "Arm_Appa|Appa_TurnR", First = 0, Last = 35, Loop = true },
            new Clip { Name = "Appa_Graze", Take = "Arm_Appa|Appa_Graze", First = 0, Last = 95, Loop = true },
            new Clip { Name = "Appa_Happy", Take = "Arm_Appa|Appa_Happy", First = 0, Last = 60, Loop = false },
            new Clip { Name = "Appa_Roar",  Take = "Arm_Appa|Appa_Roar",  First = 0, Last = 48, Loop = false },
            new Clip { Name = "Appa_Ram",   Take = "Arm_Appa|Appa_Ram",   First = 0, Last = 36, Loop = false },
            new Clip { Name = "Appa_Hurt",  Take = "Arm_Appa|Appa_Hurt",  First = 0, Last = 18, Loop = false },
            new Clip { Name = "Appa_Death", Take = "Arm_Appa|Appa_Death", First = 0, Last = 72, Loop = false },
        };

        [MenuItem("Tools/Creatures/Build Appa Prefab")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Fbx) == null)
            {
                Debug.LogError($"No FBX at {Fbx}. Run appa_export.py first.");
                return;
            }

            ConfigureImporter();
            if (!ClipsAreImported())
                return;

            AnimatorController controller = BuildController();
            FactionDefinition fauna = LoadFauna();
            GameObject prefab = BuildPrefab(controller, fauna);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            RegisterForNetworking();
            WireSaveables();

            Debug.Log($"Appa built: {PrefabPath}", prefab);
            Selection.activeObject = prefab;
        }

        /// <summary>
        /// Refuse to build a controller out of clips the importer has not produced yet.
        ///
        /// <para>
        /// `ConfigureImporter` calls `SaveAndReimport`, and that is *not* always enough: the first
        /// build after new takes are added to the FBX can still read the previous clip set back
        /// out of `LoadAllAssetsAtPath`. What that produced was a controller holding only Idle and
        /// Walk — no Roar, no Ram, no Hurt, no Death, no error — and an Appa who could be enraged
        /// and would charge you in complete silence with a walk cycle playing.
        /// </para>
        /// <para>
        /// Stopping here is the whole point. Building on a partial clip set writes a plausible
        /// asset over a good one, and the next honest signal is a play-test.
        /// </para>
        /// </summary>
        private static bool ClipsAreImported()
        {
            var present = new System.Collections.Generic.HashSet<string>(
                AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>().Select(c => c.name));

            string[] missing = Clips.Select(c => c.Name).Where(n => !present.Contains(n)).ToArray();
            if (missing.Length == 0)
                return true;

            Debug.LogError(
                $"Appa NOT built: {missing.Length} clip(s) missing from the imported FBX — " +
                $"{string.Join(", ", missing)}. The importer has not caught up with the new takes " +
                "yet. Run Tools > Creatures > Build Appa Prefab again; if they are still missing, " +
                "check the take names printed by appa_export.py against Clips[].");
            return false;
        }

        /// <summary>
        /// Put the rebuilt prefab back in the network prefab list.
        ///
        /// <para>
        /// Saving the prefab keeps its GUID, so in practice the entry survives — but a first build
        /// on a fresh clone would not have one, and a creature missing from that list exists only
        /// on the host: clients see empty ground where he is standing.
        /// </para>
        /// </summary>
        private static void RegisterForNetworking()
        {
            NetworkPrefabRegistrar.Sync(out int added, out int total);
            if (added > 0)
                Debug.Log($"[Appa] Registered {added} network prefab(s); {total} in the project.");
        }

        /// <summary>
        /// Re-run the saveable pass, because this builder just destroyed its output.
        ///
        /// <para>
        /// `Wire Saveable Prefabs` adds eight more savers to Appa and stamps his `prefabId`, and
        /// building the prefab wholesale throws every one of them away. Leaving it to be run by
        /// hand means the failure mode is an Appa that looks perfect and silently stops surviving
        /// a reload — which is precisely how `PlayerShipBuilder` shipped a hull with no `prefabId`
        /// and five missing savers.
        /// </para>
        /// <para>
        /// The result is checked because it must be: the pass refuses outright in Play mode, and
        /// a builder that ignored that would save an unwired prefab and report success.
        /// </para>
        /// </summary>
        private static void WireSaveables()
        {
            if (SaveableWiring.TryWirePrefabs())
                return;

            Debug.LogError("[Appa] The prefab was built but Tools > Save System > Wire Saveable " +
                           "Prefabs refused to run — usually because the editor is in Play mode. " +
                           "Appa has no prefabId and is missing savers, so he will not survive a " +
                           "reload. Exit Play mode and build again.");
        }

        // -------------------------------------------------------------------
        // 1. Model import
        // -------------------------------------------------------------------

        private static void ConfigureImporter()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Fbx);

            // Generic, not Humanoid. Six legs, a tail and a jaw map onto no
            // humanoid avatar, and inventing one would only make the clips
            // retarget badly.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.importNormals = ModelImporterNormals.Import;

            // Off, and it has to stay off: twenty-one of Appa's twenty-seven
            // meshes are bone-parented props -- horns, teeth, eyes, hooves --
            // and optimising the hierarchy strips the very transforms they hang
            // from. Only six meshes are actually skinned.
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
                // The clips are authored in place, so the root must not travel.
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
                Debug.LogError($"Clip '{name}' missing from {Fbx} — check the take " +
                               "names printed by appa_export.py against Clips[].");
            return clip;
        }

        // -------------------------------------------------------------------
        // 2. Animator controller
        // -------------------------------------------------------------------

        private static AnimatorController BuildController()
        {
            EnsureFolder(ControllerDir);

            // Rebuilt in place, never deleted and recreated. Deleting the asset
            // burns its GUID, and while this build rewrites the prefab in the
            // same run, anything NOT rewritten -- another prefab, an override,
            // an AnimatorOverrideController -- keeps a reference to a GUID that
            // no longer exists and goes silently null.
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

            if (controller == null)
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            else
                Clear(controller);

            // These names are AgentAnimatorDriver's, verbatim, misspelling and
            // all -- it calls SetFloat/SetBool on them unconditionally and a
            // parameter it cannot find is a warning every frame. "Die" and
            // "Death" are both here because the driver sends "Die" while
            // HealthReactionModule sends "Death".
            controller.AddParameter("SpeedX", AnimatorControllerParameterType.Float);
            controller.AddParameter("SpeedY", AnimatorControllerParameterType.Float);
            // Optional in the driver -- it looks the parameter up once and skips
            // it on controllers that have no turn clips. Appa has them.
            controller.AddParameter("TurnSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("FallSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsImmobalized", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsAiming", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Hurt", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Death", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Ram", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Roar", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Happy", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("IsGrazing", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine root = controller.layers[0].stateMachine;

            // One 1-D blend tree on forward speed, thresholds in true metres per
            // second. That only holds because the prefab sets the driver's two
            // clip-choosing multipliers to 1 -- by default it scales velocity 3x
            // and the tree would sit pinned at Run.
            var tree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "SpeedY",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(FindClip("Appa_Idle"), 0f);
            tree.AddChild(FindClip("Appa_Walk"), WalkSpeed);
            tree.AddChild(FindClip("Appa_Run"), RunSpeed);

            AnimatorState locomotion = root.AddState("Locomotion");
            locomotion.motion = tree;
            root.defaultState = locomotion;

            // Turning on the spot. Without this he pivots like a turret: a
            // NavMeshAgent choosing a new heading rotates the transform and
            // reports no velocity at all, so SpeedY stays at 0, the tree sits on
            // Idle, and five and a half metres of animal swings round with its
            // feet planted.
            //
            // Idle sits at 0 in the middle so the state fades in and out through
            // the standing pose instead of popping to a full shuffle the instant
            // the threshold is crossed.
            var turnTree = new BlendTree
            {
                name = "Turn",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "TurnSpeed",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(turnTree, controller);
            // TurnSpeed is positive turning RIGHT -- it is a yaw rate, and Unity
            // yaws clockwise about +Y. So the left clip sits on the negative side.
            turnTree.AddChild(FindClip("Appa_TurnL"), -TurnSpeed);
            turnTree.AddChild(FindClip("Appa_Idle"), 0f);
            turnTree.AddChild(FindClip("Appa_TurnR"), TurnSpeed);

            AnimatorState turn = root.AddState("Turn");
            turn.motion = turnTree;

            // Two transitions in, because Animator conditions are ANDed and
            // "turning either way" is an OR.
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
                into.duration = 0.25f;
            }

            // Settled onto the new heading: both conditions together are
            // |TurnSpeed| < TurnExitRate.
            AnimatorStateTransition settled = turn.AddTransition(locomotion);
            settled.AddCondition(AnimatorConditionMode.Less, TurnExitRate, "TurnSpeed");
            settled.AddCondition(AnimatorConditionMode.Greater, -TurnExitRate, "TurnSpeed");
            settled.hasExitTime = false;
            settled.duration = 0.28f;

            // Or he simply started walking, which the gait already covers.
            AnimatorStateTransition walkedOff = turn.AddTransition(locomotion);
            walkedOff.AddCondition(AnimatorConditionMode.Greater, TurnExitSpeed, "SpeedY");
            walkedOff.hasExitTime = false;
            walkedOff.duration = 0.20f;

            // Long blends. He is five and a half metres of animal; snapping
            // between states in 80 ms reads as weightless.
            AnimatorState roar = AddOneShot(root, "Roar", "Appa_Roar", "Roar", 0.18f);
            AnimatorState ram = AddOneShot(root, "Ram", "Appa_Ram", "Ram", 0.14f);
            AnimatorState hurt = AddOneShot(root, "Hurt", "Appa_Hurt", "Hurt", 0.10f);

            // Being petted. A one-shot like the others, not a hold: the reaction has a shape --
            // lean in, enjoy it, settle -- and holding its middle frame would be a stare.
            AnimatorState happy = AddOneShot(root, "Happy", "Appa_Happy", "Happy", 0.20f);

            // Grazing is a STATE, not a trigger: he keeps his head down for as long as the task
            // runs, which is tens of seconds, and a one-shot would pop back up mid-meal.
            // NpcTaskModule holds IsGrazing true while it dwells at a feeding site.
            AnimatorState graze = root.AddState("Graze");
            graze.motion = FindClip("Appa_Graze");

            AnimatorStateTransition intoGraze = locomotion.AddTransition(graze);
            intoGraze.AddCondition(AnimatorConditionMode.If, 0f, "IsGrazing");
            intoGraze.AddCondition(AnimatorConditionMode.Less, TurnEnterSpeed, "SpeedY");
            intoGraze.hasExitTime = false;
            intoGraze.duration = 0.45f;   // he lowers his head, he does not drop it

            AnimatorStateTransition outOfGraze = graze.AddTransition(locomotion);
            outOfGraze.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsGrazing");
            outOfGraze.hasExitTime = false;
            outOfGraze.duration = 0.45f;

            // Walking off mid-meal beats waiting for the flag: the motor has already moved him.
            AnimatorStateTransition grazeWalked = graze.AddTransition(locomotion);
            grazeWalked.AddCondition(AnimatorConditionMode.Greater, TurnExitSpeed, "SpeedY");
            grazeWalked.hasExitTime = false;
            grazeWalked.duration = 0.30f;

            // Death has no way back: the clip ends on the collapsed pose and the
            // importer clamps it, so there is no exit transition and no separate
            // corpse state.
            AnimatorState death = root.AddState("Death");
            death.motion = FindClip("Appa_Death");
            foreach (string trigger in new[] { "Death", "Die" })
            {
                AnimatorStateTransition t = root.AddAnyStateTransition(death);
                t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                t.duration = 0.16f;
                t.hasExitTime = false;
                t.canTransitionToSelf = false;
            }

            // Each leaves as its own motion finishes rather than at the clip
            // end: the roar's last third is the settle, and the ram is back on
            // its feet well before frame 36.
            ReturnToLocomotion(roar, locomotion, 0.80f);
            ReturnToLocomotion(ram, locomotion, 0.78f);
            ReturnToLocomotion(hurt, locomotion, 0.75f);
            ReturnToLocomotion(happy, locomotion, 0.88f);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static AnimatorState AddOneShot(AnimatorStateMachine root, string stateName,
                                                string clipName, string trigger, float blend)
        {
            AnimatorState state = root.AddState(stateName);
            state.motion = FindClip(clipName);
            AnimatorStateTransition t = root.AddAnyStateTransition(state);
            t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            t.duration = blend;
            t.hasExitTime = false;
            // Without this a second trigger mid-charge restarts the state and he
            // stutters instead of finishing the ram.
            t.canTransitionToSelf = false;
            return state;
        }

        private static void ReturnToLocomotion(AnimatorState from, AnimatorState locomotion,
                                               float exitTime)
        {
            AnimatorStateTransition back = from.AddTransition(locomotion);
            back.hasExitTime = true;
            back.exitTime = exitTime;
            back.duration = 0.22f;
        }

        /// <summary>
        /// Empty an existing controller so it can be rebuilt without replacing the
        /// asset. Order matters: transitions reference states, so the states go
        /// last.
        /// </summary>
        private static void Clear(AnimatorController controller)
        {
            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            foreach (AnimatorStateTransition t in sm.anyStateTransitions.ToArray())
                sm.RemoveAnyStateTransition(t);
            foreach (AnimatorTransition t in sm.entryTransitions.ToArray())
                sm.RemoveEntryTransition(t);
            foreach (ChildAnimatorStateMachine child in sm.stateMachines.ToArray())
                sm.RemoveStateMachine(child.stateMachine);
            foreach (ChildAnimatorState child in sm.states.ToArray())
                sm.RemoveState(child.state);

            while (controller.parameters.Length > 0)
                controller.RemoveParameter(0);
        }

        // -------------------------------------------------------------------
        // 3. Faction
        // -------------------------------------------------------------------

        /// <summary>
        /// Fauna, and it must stay a faction with **no rows** in
        /// GlobalRelationships. `FactionRelationshipTable.Get` returns Neutral for
        /// any pair it has no row for, and AgentTargeting only ever queries for
        /// Hostile candidates — so zero rows is precisely what "peaceful" is built
        /// out of here. Adding a Hostile row toward the player to "make him react"
        /// would make every Fauna creature in the world attack on sight, and Appa
        /// would charge you unprovoked.
        /// </summary>
        private static FactionDefinition LoadFauna()
        {
            var fauna = AssetDatabase.LoadAssetAtPath<FactionDefinition>(FaunaPath);
            if (fauna == null)
                Debug.LogError($"No FaunaFaction at {FaunaPath}. Without an EntityFaction " +
                               "Appa is invisible to every targeting module and can never be " +
                               "provoked — silently.");
            return fauna;
        }

        // -------------------------------------------------------------------
        // 4. Prefab
        // -------------------------------------------------------------------

        private static GameObject BuildPrefab(AnimatorController controller, FactionDefinition fauna)
        {
            EnsureFolder(PrefabDir);

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            root.name = "Appa";
            root.transform.position = Vector3.zero;

            // Unpacked so everything below is stored in this prefab rather than
            // as overrides on the model prefab, which a reimport would silently
            // drop.
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);

            // -- animation ---------------------------------------------------
            Animator animator = root.GetComponentInChildren<Animator>(true)
                                ?? root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;      // the motor owns movement
            // AlwaysAnimate, not CullUpdateTransforms. Twenty-one of Appa's
            // renderers are bone-parented props, so the bounds Unity culls
            // against are whatever those small boxes cover in the bind pose --
            // not a volume that follows the animation. When it decides he is off
            // screen it stops writing transforms and he freezes mid-stride in
            // plain sight.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Every one of Appa's meshes stays SINGLE-sided, the hair included.
            // Do not reach for DoubleSidedMaterials here. It was tried, on the
            // assumption that hair is modelled as open sheets; the mane, shoulder
            // fur, brow tuft and ears are in fact closed volumes, 0 boundary
            // edges apiece, so there was never a missing half to restore.
            //
            // What it did instead was let URP draw the interior of every lock,
            // and URP -- unlike Blender's viewport -- does not flip a back face's
            // shading normal, so those interiors lit black wherever they won the
            // depth test. It was never the main fault (the mane looked wrong
            // because three skinned meshes were exported mirrored -- see
            // appa_export.py::_apply_object_transforms), but it added dark
            // speckle on top of it and it hid what was really going on.
            // appa_BUILD.md, "Rejected", carries the measurements.

            // -- physical presence -------------------------------------------
            // The torso only. A box around the horns and the tail would stop you
            // a metre short of an animal you can see there is room beside.
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 1.3f, 0.2f);
            box.size = new Vector3(BodyWidth * 0.55f, 2.0f, BodyLength * 0.6f);

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var agent = root.AddComponent<NavMeshAgent>();
            // The RUN speed, not the walk. ChaseModule and FleeModule both ask
            // for a speed multiplier on top of this, so an agent set to the walk
            // gives a creature that flees at a stroll.
            agent.speed = RunSpeed;
            // Matched to what Appa_TurnL/R actually step out -- see TurnSpeed. It
            // was 130, which no clip could have kept up with; the feet would have
            // skated round three times faster than they were placed.
            agent.angularSpeed = TurnSpeed;
            agent.acceleration = 6f;
            agent.radius = 1.5f;         // 3.06 m across the horns
            agent.height = 2.6f;
            agent.stoppingDistance = 2.0f;
            agent.autoBraking = true;

            // -- motor and controller ----------------------------------------
            var motor = root.AddComponent<NavMeshAgentMotor>();
            SetField(motor, "agent", agent);
            SetFloat(motor, "walkSpeedMultiplier", WalkSpeed / RunSpeed);
            SetFloat(motor, "faceRotateSpeed", 2.6f);

            var animDriver = root.AddComponent<AgentAnimatorDriver>();
            SetField(animDriver, "animator", animator);
            // Both to 1 so SpeedY reaches the blend tree as true m/s; the rate
            // correction is animatorSpeedScale, which is a different knob.
            SetFloat(animDriver, "animationSpeedMultiplier", 1f);
            SetFloat(animDriver, "walkAnimBoost", 1f);
            SetFloat(animDriver, "animatorSpeedScale", AnimatorSpeedScale);

            var controllerComp = root.AddComponent<AgentController>();
            SetField(controllerComp, "MotorComponent", motor);
            SetField(controllerComp, "animatorDriver", animDriver);

            // -- health -------------------------------------------------------
            // Big and slow, so he has to be able to absorb a few shots before
            // the decision to fight or run means anything.
            var health = root.AddComponent<HealthComponent>();
            SetInt(health, "maxHealth", 350);
            SetInt(health, "currentHealth", 350);
            var reaction = root.AddComponent<HealthReactionModule>();
            // On, for now: Appa was reported "taking damage for no reason" and nothing in the
            // project reads LastDamageSource back out. Turn it off once that is settled.
            SetBool(reaction, "logDamage", true);

            // -- senses --------------------------------------------------------
            var perception = root.AddComponent<PerceptionModule>();
            SetFloat(perception, "fieldOfViewAngle", 220f);   // prey animal, eyes wide apart
            SetFloat(perception, "eyeHeight", 2.3f);
            SetFloat(perception, "memoryDuration", 8f);
            // Left unset this is Nothing, which makes every line-of-sight test
            // succeed through walls; PerceptionModule falls back to these three
            // at runtime and warns once per spawn asking to be told explicitly.
            SetInt(perception, "occlusionLayers", LayerMaskOf("Default", "Ground", "Interior"));

            // Gunfire is the whole point of this component here. investigateOn is
            // deliberately None: a spooked animal does not walk toward the shot.
            // aggroOn hands him the shooter, which is what everything downstream
            // reads as "a threat exists".
            var ears = root.AddComponent<NoiseReceiverModule>();
            SetInt(ears, "investigateOn", (int)NoiseTypeMask.None);
            SetInt(ears, "aggroOn", (int)(NoiseTypeMask.Gunshot | NoiseTypeMask.Explosion));
            SetInt(ears, "priority", ModulePriority.Reactive - 2);

            // -- behaviour ------------------------------------------------------
            // Priorities are set explicitly on every one of these. Unity does not
            // call Reset() for AddComponent, so a module added from a script keeps
            // the serialized default of Fallback (0) and ties with wander -- which
            // looks exactly like the behaviour not being implemented.

            var wander = root.AddComponent<WanderModule>();
            SetBool(wander, "limitWanderRadius", false);
            SetFloat(wander, "freeRoamRadius", 70f);
            SetFloat(wander, "speedMultiplier", WalkSpeed / RunSpeed);  // grazing pace
            SetFloat(wander, "minWaitTime", 5f);
            SetFloat(wander, "maxWaitTime", 16f);
            SetInt(wander, "priority", ModulePriority.Fallback);

            var chase = root.AddComponent<ChaseModule>();
            SetFloat(chase, "chaseStopDistance", 3.2f);
            SetFloat(chase, "chaseSpeedMultiplier", 1f);   // the agent speed is already the bound
            SetInt(chase, "priority", ModulePriority.Reactive);

            // The ram. attackRange is generous because the reach is a head on the
            // end of a neck on the end of 5.75 m of animal.
            var melee = root.AddComponent<CloseCombatModule>();
            SetFloat(melee, "attackRange", 4.5f);
            SetFloat(melee, "attackCooldown", 3.0f);
            SetInt(melee, "attackDamage", 35);
            // The head lands on frame 18 of 36 at 24 fps; the commit holds him
            // through the follow-through so he cannot start walking mid-charge.
            SetFloat(melee, "attackCommitDuration", 1.0f);
            SetString(melee, "attackAnimTrigger", "Ram");
            SetInt(melee, "priority", ModulePriority.MeleeAttack);
            // Knockback is off by default on the shared module; Appa is the reason it exists.
            // Two tonnes of animal driving a lowered head into you should not read as a nudge, so
            // the shove is the loudest part of the hit and the damage is almost secondary. The
            // lift is small on purpose — enough to take your feet out, not enough to punt you.
            SetFloat(melee, "knockbackSpeed", 14f);
            SetFloat(melee, "knockbackLift", 0.28f);
            SetFloat(melee, "knockbackLeapDistance", 6f);
            SetFloat(melee, "knockbackLeapHeight", 2f);
            SetFloat(melee, "knockbackLeapDuration", 0.6f);

            // fleeFromCurrentTarget, not the faction scan: Fauna is Neutral
            // toward everything, so the relationship path would never find a
            // threat and he would stand and be shot.
            var flee = root.AddComponent<FleeModule>();
            SetBool(flee, "fleeFromCurrentTarget", true);
            SetFloat(flee, "triggerRadius", 22f);
            SetFloat(flee, "safeRadius", 48f);
            SetFloat(flee, "fleeSpeedMultiplier", 1.25f);
            SetInt(flee, "priority", ModulePriority.Override);

            // Sits above flee so its bookkeeping runs first every frame.
            var temperament = root.AddComponent<FightOrFlightModule>();
            SetField(temperament, "fleeModule", flee);
            SetField(temperament, "animatorDriver", animDriver);
            SetInt(temperament, "enrageDamage", 60);
            // Comfortably outside CloseCombatModule.attackRange (4.5), or he
            // turns to fight from a range he cannot reach and stands there.
            SetFloat(temperament, "corneredDistance", 9f);
            SetFloat(temperament, "rageDuration", 14f);
            // The roar clip runs 2.0 s and its last third is the settle.
            SetFloat(temperament, "roarDuration", 1.4f);
            SetInt(temperament, "priority", ModulePriority.Override + 1);

            // -- faction and targeting -------------------------------------------
            var faction = root.AddComponent<EntityFaction>();
            SetField(faction, "faction", fauna);
            SetField(faction, "relationshipTable",
                     AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(RelationshipsPath));

            // Several modules above declare [RequireComponent] on this, so by now
            // it is usually already there. Adding a second is legal and silent,
            // and leaves two components fighting over the same target slot.
            if (root.GetComponent<AgentTargeting>() == null)
                root.AddComponent<AgentTargeting>();

            // The leash matches AgentTargeting's own loseRange. Holding a grudge
            // further out than targeting will retain means re-asserting and
            // dropping it on alternate frames.
            var provocation = root.AddComponent<ProvocationModule>();
            SetFloat(provocation, "leashRange", 45f);
            SetFloat(provocation, "calmDownDelay", 45f);
            // He shrugs off a scrape. The bar to make a friendly animal turn on
            // you should be a real hit.
            SetInt(provocation, "damageThreshold", 5);
            SetField(temperament, "provocation", provocation);

            // -- streaming --------------------------------------------------------
            // He roams between chunks, so he has to survive his chunk unloading.
            var tracked = root.AddComponent<SceneTracked>();
            SetEnum(tracked, "policy", (int)SceneTracked.UnloadPolicy.Migrate);
            SetBool(tracked, "keepChunksLoaded", false);

            // -- multiplayer -------------------------------------------------------
            // Without these the creature exists only on the host: clients see
            // nothing, and damage dealt by a client is never reconciled.
            root.AddComponent<Unity.Netcode.NetworkObject>();
            root.AddComponent<NetworkedHealthComponent>();
            // simulationDrivers is left empty on purpose -- NetAuthority falls
            // back to SimulationDrivers.Discover, which finds the NavMeshAgent
            // and the motor without this builder having to name them.
            root.AddComponent<NetAuthority>();

            // -- persistence --------------------------------------------------------
            root.AddComponent<SaveableEntity>();
            root.AddComponent<TransformSaveable>();
            root.AddComponent<HealthSaveable>();
            // Required by SaveablePolicy for anything with an AgentTargeting.
            root.AddComponent<AgentStateSaveable>();
            // The grudge is the whole of his temperament. Without this a
            // provoked Appa reloads calm and, because Fauna is Neutral toward
            // everything, can never re-acquire the player on its own -- so he
            // would stay calm forever, mid-fight.
            root.AddComponent<ProvocationSaveable>();
            // Fleeing is hysteresis, not a derived value: for a threat sitting
            // between triggerRadius and safeRadius the flag is the only thing
            // that says which side of it he is on.
            root.AddComponent<FleeSaveable>();


            // -- petting ------------------------------------------------------------
            AttachPetTarget(root);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }


        /// <summary>
        /// A trigger on the head bone that offers "pet me", and nothing anywhere else that does.
        ///
        /// <para>
        /// The placement IS the feature. <see cref="Interactor.ResolveAlongRay"/> treats a trigger
        /// as a detection volume rather than a surface: it only answers when it carries the
        /// <c>IInteractable</c> on its own GameObject, and it never inherits one from a parent, so
        /// the ray passes straight through it otherwise. Put <see cref="PettableModule"/> on the
        /// root instead and the body's solid collider would answer for it — every square metre of
        /// a 5.75 m animal, tail and hooves included, would say "pet me".
        /// </para>
        /// <para>
        /// Parented to the head bone rather than placed at a fixed offset, so it follows him when
        /// he grazes with his nose on the ground or throws his head up to roar. A trigger, so it
        /// adds nothing to his collision — he is a kinematic body and a second solid collider on
        /// the head would change what he pushes.
        /// </para>
        /// </summary>
        private static void AttachPetTarget(GameObject root)
        {
            Transform head = FindBone(root.transform, "head");
            if (head == null)
            {
                Debug.LogError("Appa has no 'head' bone — the pet target has nowhere to hang, so " +
                               "he would be unpettable with nothing in the console to say why.");
                return;
            }

            var target = new GameObject("PetTarget");
            target.transform.SetParent(head, false);

            // **Undo the bone's scale.** Every transform in an imported FBX carries
            // lossyScale 100 (the centimetre convention — see the ArtPipeline doc), so a
            // collider authored in metres under a bone comes out a hundred times too big.
            // The first version of this asked for a 0.75 m sphere 0.35 m along the muzzle
            // and produced a **75 m sphere centred 38 m away**: the player stood inside it,
            // and a raycast that starts inside a collider does not report hitting it, so the
            // pet prompt could never appear no matter where you looked.
            float boneScale = head.lossyScale.x;
            if (boneScale <= 1e-4f)
            {
                Debug.LogError($"Appa's head bone has a degenerate scale ({boneScale}); the pet " +
                               "target cannot be sized against it.");
                boneScale = 1f;
            }
            target.transform.localScale = Vector3.one / boneScale;

            // Centred on the bone, no offset. The bone sits inside the skull and a sphere this
            // size covers it; an offset would have to be expressed in the bone's own axes, and
            // those are whatever the rig export left them as.
            target.transform.localPosition = Vector3.zero;

            var sphere = target.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = PetTargetRadius;

            float worldRadius = sphere.radius * target.transform.lossyScale.x;
            if (Mathf.Abs(worldRadius - PetTargetRadius) > 0.05f)
                Debug.LogError($"Pet target came out {worldRadius:F2} m across instead of " +
                               $"{PetTargetRadius:F2} m — the bone scale compensation is wrong.");
            else
                Debug.Log($"Pet target: {worldRadius:F2} m sphere on the head bone.");

            var pettable = target.AddComponent<PettableModule>();
            SetField(pettable, "agentRoot", root);
            SetField(pettable, "animatorDriver", root.GetComponentInChildren<AgentAnimatorDriver>(true));
            SetField(pettable, "mood", root.GetComponentInChildren<FightOrFlightModule>(true));
            SetString(pettable, "happyTrigger", "Happy");
            SetString(pettable, "label", "Appa");
            SetFloat(pettable, "cooldown", 3f);
        }

        /// <summary>Depth-first by exact name — the rig has one 'head' and it is not the root.</summary>
        private static Transform FindBone(Transform from, string boneName)
        {
            foreach (Transform t in from.GetComponentsInChildren<Transform>(true))
                if (t.name == boneName)
                    return t;
            return null;
        }

        // -------------------------------------------------------------------
        // Serialized-field helpers
        //
        // SerializedObject rather than reflection, so private [SerializeField]
        // fields are written the way the Inspector writes them and the prefab
        // serializes identically to a hand-authored one.
        // -------------------------------------------------------------------

        private static SerializedProperty Find(Object target, string field)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
                Debug.LogError($"{target.GetType().Name} has no serialized field '{field}'. " +
                               "A rename upstream silently leaves this unset.", target);
            return prop;
        }

        private static void Apply(SerializedProperty prop)
        {
            prop.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

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

        private static void SetString(Object target, string field, string value)
        {
            SerializedProperty p = Find(target, field);
            if (p == null) return;
            p.stringValue = value;
            Apply(p);
        }

        private static void SetEnum(Object target, string field, int value)
        {
            SerializedProperty p = Find(target, field);
            if (p == null) return;
            p.enumValueIndex = value;
            Apply(p);
        }

        private static int LayerMaskOf(params string[] names)
        {
            int mask = 0;
            foreach (string n in names)
            {
                int layer = LayerMask.NameToLayer(n);
                if (layer >= 0)
                    mask |= 1 << layer;
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
