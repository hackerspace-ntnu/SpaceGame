using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// A Raxy standing far from the origin, posed by the real Upper Body clips, for as long as it is
    /// kept. Everything that has to know where a hand really is while a hold pose plays — the grip
    /// fitter and the preview — asks this instead of building its own copy.
    ///
    /// <para>
    /// No play mode and no scene of its own: the body is instantiated with
    /// <see cref="HideFlags.DontSave"/> and destroyed on <see cref="Dispose"/>.
    /// </para>
    /// </summary>
    public sealed class HandToolRig : IDisposable
    {
        private const string RaxyPath = "Assets/Game/Prefabs/Agents/Characters/Raxy/Raxy_handyman.prefab";
        private static readonly Vector3 StagePosition = new Vector3(0f, 600f, 0f);

        /// <summary>
        /// The bones the Upper Body layer does not drive: its mask is the torso, arms and fingers. A
        /// hold clip is a full-body clip, so sampling it also turns the head, bends the legs and
        /// carries the root along, none of which the game ever shows.
        /// </summary>
        private static readonly HumanBodyBones[] UnmaskedBones =
        {
            HumanBodyBones.Neck, HumanBodyBones.Head, HumanBodyBones.LeftEye, HumanBodyBones.RightEye, HumanBodyBones.Jaw,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
        };

        private readonly HumanoidAnimationProfile profile;
        private readonly Transform chest;
        private readonly Vector3 chestForward;
        private readonly (Transform Bone, Vector3 Position, Quaternion Rotation)[] restPose;

        public GameObject Body { get; }
        public Animator Animator { get; }
        public Transform Hand { get; }
        public Transform Hips { get; }
        public HandGripFrame Frame { get; }

        private HandToolRig(GameObject body, HumanoidAnimationProfile profile)
        {
            Body = body;
            this.profile = profile;
            Animator = body.GetComponentInChildren<Animator>(true);
            Hand = Animator.GetBoneTransform(HumanBodyBones.RightHand);
            Hips = Animator.GetBoneTransform(HumanBodyBones.Hips);
            Frame = HandGripFrame.Derive(Animator, Hand, true);

            chest = new[] { HumanBodyBones.UpperChest, HumanBodyBones.Chest, HumanBodyBones.Spine }
                .Select(b => Animator.GetBoneTransform(b)).First(t => t != null);
            chestForward = chest.InverseTransformDirection(body.transform.forward);

            AnimationMode.StartAnimationMode();

            // What the base layer would be doing under the hold: the character standing at ease and
            // facing front, which is the pose the hold clip is laid over. The bare prefab is not
            // that — a humanoid only turns to face front once something has been sampled onto it.
            Sample(profile.HoldPoses.First(p => p.style == ItemGrip.HoldStyle.Carry).clip);
            FaceFront();
            restPose = UnmaskedBones.Select(b => Animator.GetBoneTransform(b)).Append(Animator.transform)
                .Where(t => t != null).Select(t => (t, t.localPosition, t.localRotation)).ToArray();
        }

        /// <summary>The rig, or null with the reason logged when the Raxy or the animation profile is missing.</summary>
        public static HandToolRig Create()
        {
            GameObject prefab = AssetDatabase.LoadAllAssetsAtPath(RaxyPath)
                .OfType<GameObject>().FirstOrDefault(g => g.transform.parent == null);
            var profile = AssetDatabase.FindAssets("t:HumanoidAnimationProfile")
                .Select(g => AssetDatabase.LoadAssetAtPath<HumanoidAnimationProfile>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault();
            if (prefab == null || profile == null)
            {
                Debug.LogError("[HandTools] No Raxy prefab or no humanoid animation profile.");
                return null;
            }

            var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            body.hideFlags = HideFlags.DontSave;
            body.transform.SetPositionAndRotation(StagePosition, Quaternion.identity);
            return new HandToolRig(body, profile);
        }

        /// <summary>Hold the Upper Body pose a hold style plays, mid-clip. False when the profile has none.</summary>
        public bool Pose(ItemGrip.HoldStyle style)
        {
            HumanoidAnimationProfile.HoldPose pose = profile.HoldPoses.FirstOrDefault(p => p.style == style);
            if (pose.clip == null) return false;

            Sample(pose.clip);

            foreach ((Transform bone, Vector3 position, Quaternion rotation) in restPose)
                bone.SetLocalPositionAndRotation(position, rotation);

            // Clips disagree about which way the hips face (the gun clips turn them half way round);
            // in the game the base layer owns that, so every pose is judged with the holder facing front.
            FaceFront();
            return true;
        }

        /// <summary>
        /// The way the holder faces: +Z of this frame is "forward" and +Y up. Read off the chest bone's
        /// own forward axis, learnt from the bind pose, so it follows the torso that carries the arms.
        /// Not <c>Body.transform</c>, which sampling turns to whatever a clip's root says; not the
        /// hip line or the eyes, which a clip that faces the other way or tilts the head moves
        /// without the torso agreeing.
        /// </summary>
        public Quaternion Facing =>
            Quaternion.LookRotation(Vector3.ProjectOnPlane(chest.TransformDirection(chestForward), Vector3.up), Vector3.up);

        /// <summary>Turn the body about the vertical until it faces world +Z, so the preview cameras see its front.</summary>
        private void FaceFront()
        {
            float yaw = Vector3.SignedAngle(Facing * Vector3.forward, Vector3.forward, Vector3.up);
            Body.transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * Body.transform.rotation;
        }

        private void Sample(AnimationClip clip)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(Animator.gameObject, clip, clip.length * 0.5f);
            AnimationMode.EndSampling();
        }

        /// <summary>The hand's grip frame, in the holder's <see cref="Facing"/> space, in whatever pose is currently held.</summary>
        public Quaternion GripFrameInBody() =>
            Quaternion.Inverse(Facing) * Hand.rotation * Frame.LocalRotation;

        public void Dispose()
        {
            AnimationMode.StopAnimationMode();
            if (Body != null) UnityEngine.Object.DestroyImmediate(Body);
        }
    }
}
