// Builds the Caraxoids' Unity-side animation from the exported FBX: the clip import settings and
// one animator controller per Caraxoid. Prefabs are authored assets and are NOT touched here.
//
// The FBX come out of Blender via
// Assets/Game/Art/Models/_Source~/models/creatures/caraxoid_export.py, one per Caraxoid, every
// clip baked as a take. Re-export, then run Tools > Creatures > Build Caraxoid Animators; the
// controllers are rebuilt in place (AnimatorControllerRebuild), so prefabs keep their references.
//
// ## Speeds
//
// Thresholds are in MODEL units per second — how fast a planted foot passes under the body at
// scale 1. The Blender gaits (gait.py, baked into caraxoid.blend) are tuned to these exact speeds,
// so change both together. The prefabs are scaled (8.5 m adults, 3 m raptor...), so
// each prefab sets AgentAnimatorDriver.animationSpeedMultiplier = 1 / (scale * animatorSpeedScale)
// to turn its world velocity back into model units; one controller then serves every size of the
// same body (the tan raptor and the 8.5 m mother share Caraxoid_Tan).
//
// Every child is a clip at a playback rate, placed at the speed its feet cover at that rate, so
// the legs keep up with the body anywhere on the tree. Walk and Run are different gaits (a
// lateral walk, a gallop or a theropod run) with their feet out of phase: blended half and half
// they cancel, and a creature chasing at the speed between them glides with its legs barely
// moving. So the walk is sped up and the run slowed down until they almost meet, and the
// cross-fade between the two gaits is only that narrow gap.
//
// ## Attacks on the move
//
// Bite, Roar and Hurt play on an UpperBody layer masked to the spine, neck, head and jaw (and a
// biped's arms). On the base layer they froze the legs mid-bite while the motor kept the creature
// moving, and every attack on the run looked like a glide.
//
// ## Root bone
//
// The clips animate `Root` for pounces and jumps. Root is made the motion node and baked into the
// pose. The turns carry no root rotation at all: the motor turns the creature, and the looping turn
// clip steps the feet on the spot with the head leading and the tail trailing.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class CaraxoidAnimatorBuilder
    {
        private const string ModelDir = "Assets/Game/Art/Models/Creatures/Organic/Caraxoid";
        private const string ControllerDir = "Assets/Game/Art/Animations/Creatures";

        private sealed class Body
        {
            public string Variant;     // Red, Blue, Tan, Baby
            public string Rig;         // the FBX's armature object
            public float WalkStride;   // model units / s at playback speed 1
            public float RunStride;
            public string Fbx => $"{ModelDir}/caraxoid_{Variant.ToLowerInvariant()}.fbx";
            public string Controller => $"{ControllerDir}/Caraxoid_{Variant}.controller";
            public string Mask => $"{ControllerDir}/Caraxoid_{Variant}_UpperBody.mask";
        }

        private static readonly Body[] Bodies =
        {
            new Body { Variant = "Red", Rig = "Caraxoid_Rig", WalkStride = 8.7f, RunStride = 20.3f },
            new Body { Variant = "Blue", Rig = "CaraxoidBlue_Rig", WalkStride = 8.7f, RunStride = 20.3f },
            new Body { Variant = "Tan", Rig = "CaraxoidTan_Rig", WalkStride = 18.9f, RunStride = 43.6f },
            new Body { Variant = "Baby", Rig = "CaraxoidBaby_Rig", WalkStride = 18.9f, RunStride = 43.6f },
        };

        private static readonly HashSet<string> Looping = new HashSet<string> { "Idle", "Walk", "Run", "RestIdle", "ClingBite", "Beg", "TurnLeft", "TurnRight" };

        // TurnSpeed is the driver's yaw rate in degrees per second, positive to the right.
        private const float TurnRate = 60f;
        private const float TurnEnterRate = 18f;
        private const float TurnExitRate = 9f;
        private const float TurnEnterSpeedFraction = 0.25f;   // of the walk stride

        // The locomotion tree's slow and fast ends: (playback rate, at this fraction of the stride).
        private const float SlowWalkRate = 0.5f;
        private const float FastWalkRate = 1.45f;
        private const float SlowRunRate = 0.65f;
        private const float FastRunRate = 2f;

        // Bones the UpperBody layer owns: the spine and everything from the neck up, by name.
        private static readonly string[] UpperBodyRoots = { "Neck_01", "Crown_Ctrl", "Mane_Ctrl" };
        private const string SpinePrefix = "Spine";
        private const string ArmPrefix = "FrontLeg_";

        [MenuItem("Tools/Creatures/Build Caraxoid Animators")]
        public static void BuildAll()
        {
            foreach (Body body in Bodies)
            {
                ConfigureImport(body);
                BuildController(body);
            }
            Debug.Log($"[CaraxoidAnimatorBuilder] Built {Bodies.Length} Caraxoid controllers in {ControllerDir}.");
        }

        private static void ConfigureImport(Body body)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(body.Fbx);
            if (importer == null)
                throw new System.InvalidOperationException($"{body.Fbx} is not imported. Run caraxoid_export.py first.");

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.motionNodeName = $"{body.Rig}/Root";
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                // takes are "<rig>|Caraxoid<Variant>_<Clip>"
                string action = clip.takeName.Substring(clip.takeName.IndexOf('|') + 1);
                string name = action.Substring(action.IndexOf('_') + 1);
                clip.name = name;
                clip.loopTime = Looping.Contains(name);
                clip.loopPose = false;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static AnimationClip Clip(Body body, string name)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(body.Fbx).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == name);
            if (clip == null)
                throw new System.InvalidOperationException(
                    $"Clip '{name}' missing from {body.Fbx}. If the import was just changed, run the build again: " +
                    "the first read after a reimport can still return the previous clip set.");
            return clip;
        }

        private static void BuildController(Body body)
        {
            AnimatorController c = AnimatorControllerRebuild.LoadOrCreateEmpty(body.Controller);

            // AgentAnimatorDriver's names, verbatim, misspelling included.
            foreach (string f in new[] { "SpeedX", "SpeedY", "TurnSpeed", "FallSpeed" })
                c.AddParameter(f, AnimatorControllerParameterType.Float);
            foreach (string b in new[] { "IsGrounded", "IsImmobalized", "IsAiming", "IsResting", "IsClinging" })
                c.AddParameter(b, AnimatorControllerParameterType.Bool);
            // Meele = CloseCombatModule; Hurt/Death/Die = HealthReactionModule; Roar = NestModule's
            // warning; TailPlay/Scratch/Beg = AnimatorFidgetModule; LookAround = a resting fidget;
            // Jump/Pounce = for scripted use.
            foreach (string t in new[] { "Meele", "Hurt", "Death", "Die", "Roar", "TailPlay", "Scratch", "Beg",
                                         "LookAround", "Jump", "Pounce" })
                c.AddParameter(t, AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine sm = c.layers[0].stateMachine;

            AnimationClip walk = Clip(body, "Walk"), run = Clip(body, "Run");
            var paces = new (AnimationClip clip, float rate, float stride)[]
            {
                (walk, SlowWalkRate, body.WalkStride), (walk, 1f, body.WalkStride), (walk, FastWalkRate, body.WalkStride),
                (run, SlowRunRate, body.RunStride), (run, 1f, body.RunStride), (run, FastRunRate, body.RunStride),
            };
            BlendTree locoTree = AnimatorGraph.Tree1D(c, "Locomotion", "SpeedY", (Clip(body, "Idle"), 0f));
            foreach (var (clip, rate, stride) in paces)
                locoTree.AddChild(clip, stride * rate);
            ChildMotion[] paced = locoTree.children;
            for (int i = 0; i < paces.Length; i++)
                paced[i + 1].timeScale = paces[i].rate;
            locoTree.children = paced;
            AnimatorState locomotion = sm.AddState("Locomotion");
            locomotion.motion = locoTree;
            sm.defaultState = locomotion;

            var turnTree = new BlendTree { name = "Turn", blendType = BlendTreeType.Simple1D, blendParameter = "TurnSpeed", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(turnTree, c);
            turnTree.AddChild(Clip(body, "TurnLeft"), -TurnRate);
            turnTree.AddChild(Clip(body, "Idle"), 0f);
            turnTree.AddChild(Clip(body, "TurnRight"), TurnRate);
            AnimatorState turn = sm.AddState("Turn");
            turn.motion = turnTree;
            float turnEnterSpeed = body.WalkStride * TurnEnterSpeedFraction;
            foreach (var (mode, rate) in new[] { (AnimatorConditionMode.Greater, TurnEnterRate), (AnimatorConditionMode.Less, -TurnEnterRate) })
            {
                AnimatorStateTransition into = Edge(locomotion, turn, 0.2f);
                into.AddCondition(AnimatorConditionMode.Less, turnEnterSpeed, "SpeedY");
                into.AddCondition(mode, rate, "TurnSpeed");
            }
            AnimatorStateTransition settled = Edge(turn, locomotion, 0.25f);
            settled.AddCondition(AnimatorConditionMode.Less, TurnExitRate, "TurnSpeed");
            settled.AddCondition(AnimatorConditionMode.Greater, -TurnExitRate, "TurnSpeed");
            Edge(turn, locomotion, 0.15f).AddCondition(AnimatorConditionMode.Greater, turnEnterSpeed * 2f, "SpeedY");

            // Resting in the nest: lie down, rest, look around now and then, get up.
            AnimatorState lieDown = sm.AddState("LieDown");
            lieDown.motion = Clip(body, "LieDown");
            AnimatorState rest = sm.AddState("RestIdle");
            rest.motion = Clip(body, "RestIdle");
            AnimatorState look = sm.AddState("RestLookAround");
            look.motion = Clip(body, "RestLookAround");
            AnimatorState getUp = sm.AddState("GetUp");
            getUp.motion = Clip(body, "GetUp");
            foreach (AnimatorState from in new[] { locomotion, turn })
                Edge(from, lieDown, 0.3f).AddCondition(AnimatorConditionMode.If, 0f, "IsResting");
            ExitTo(lieDown, rest, 0.2f);
            Edge(rest, look, 0.3f).AddCondition(AnimatorConditionMode.If, 0f, "LookAround");
            ExitTo(look, rest, 0.3f);
            foreach (AnimatorState from in new[] { lieDown, rest, look })
                Edge(from, getUp, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0f, "IsResting");
            ExitTo(getUp, locomotion, 0.25f);

            // One-shots that return to locomotion. Fidgets only start from standing still, so
            // they never cut into a walk or out of the nest.
            OneShotFromAny(sm, Clip(body, "Jump"), "Jump", locomotion);
            foreach (string fidget in new[] { "TailPlay", "Scratch" })
                OneShotFrom(locomotion, sm.AddState(fidget), Clip(body, fidget), fidget, locomotion);
            if (body.Variant == "Baby")
                OneShotFrom(locomotion, sm.AddState("Beg"), Clip(body, "Beg"), "Beg", locomotion, loops: 2);

            // Pounce onto something big, cling and bite while IsClinging, jump off.
            AnimatorState pounce = sm.AddState("PounceAttack");
            pounce.motion = Clip(body, "PounceAttack");
            AnimatorState cling = sm.AddState("ClingBite");
            cling.motion = Clip(body, "ClingBite");
            AnimatorState jumpDown = sm.AddState("JumpDown");
            jumpDown.motion = Clip(body, "JumpDown");
            AnyEdge(sm, pounce, "Pounce");
            ExitTo(pounce, cling, 0.1f);
            Edge(cling, jumpDown, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0f, "IsClinging");
            ExitTo(jumpDown, locomotion, 0.2f);

            AnimatorState death = sm.AddState("Death");
            death.motion = Clip(body, "Death");
            foreach (string trigger in new[] { "Death", "Die" })
                AnyEdge(sm, death, trigger);

            // Attacks and flinches from the waist up, so the legs keep running underneath.
            AnimatorStateMachine upper = AnimatorGraph.AddLayer(c, "UpperBody", BuildUpperBodyMask(body), 1f, ikPass: false);
            AnimatorState empty = AnimatorGraph.AddState(upper, "Empty", null);
            upper.defaultState = empty;
            OneShotFromAny(upper, Clip(body, "Bite"), "Meele", empty);
            OneShotFromAny(upper, Clip(body, "Roar"), "Roar", empty);
            OneShotFromAny(upper, Clip(body, "Hurt"), "Hurt", empty);

            AnimatorControllerRebuild.SaveAndVerify(c, body.Controller, minimumStates: 10);
        }

        // Rebuilt in place, like the controller, so the layer's reference to it survives.
        private static AvatarMask BuildUpperBodyMask(Body body)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(body.Fbx);
            bool biped = body.Variant == "Tan" || body.Variant == "Baby";
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(body.Mask);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, body.Mask);
            }
            mask.transformCount = 0;
            mask.AddTransformPath(model.transform, true);
            for (int i = 0; i < mask.transformCount; i++)
                mask.SetTransformActive(i, OwnedByUpperBody(mask.GetTransformPath(i), biped));
            EditorUtility.SetDirty(mask);
            return mask;
        }

        private static bool OwnedByUpperBody(string path, bool biped)
        {
            string[] bones = path.Split('/');
            string own = bones[bones.Length - 1];
            if (own.StartsWith(SpinePrefix))
                return true;
            return bones.Any(b => UpperBodyRoots.Contains(b) || (biped && b.StartsWith(ArmPrefix)));
        }

        private static AnimatorStateTransition Edge(AnimatorState from, AnimatorState to, float duration)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = duration;
            return t;
        }

        private static void ExitTo(AnimatorState from, AnimatorState to, float duration)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = 1f - Mathf.Min(0.2f, duration);
            t.duration = duration;
        }

        private static void AnyEdge(AnimatorStateMachine sm, AnimatorState to, string trigger)
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(to);
            t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            t.hasExitTime = false;
            t.duration = 0.12f;
            t.canTransitionToSelf = false;
        }

        private static void OneShotFromAny(AnimatorStateMachine sm, AnimationClip clip, string trigger, AnimatorState back)
        {
            AnimatorState state = sm.AddState(clip.name);
            state.motion = clip;
            AnyEdge(sm, state, trigger);
            ExitTo(state, back, 0.2f);
        }

        private static void OneShotFrom(AnimatorState from, AnimatorState state, AnimationClip clip, string trigger,
                                        AnimatorState back, int loops = 1)
        {
            state.motion = clip;
            AnimatorStateTransition into = Edge(from, state, 0.25f);
            into.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            into.AddCondition(AnimatorConditionMode.Less, 0.1f, "SpeedY");
            AnimatorStateTransition t = state.AddTransition(back);
            t.hasExitTime = true;
            t.exitTime = loops - 0.15f;
            t.duration = 0.25f;
            // Anything that moves the body ends a fidget early.
            Edge(state, back, 0.2f).AddCondition(AnimatorConditionMode.Greater, 0.5f, "SpeedY");
        }
    }
}
