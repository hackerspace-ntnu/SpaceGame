using System;
using System.Collections.Generic;
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
        /// carries the root along, none of which the game ever shows. The hips are in the list too: the
        /// mask's Root part is off, the base layer owns them, and a gun clip's hips lean the whole torso
        /// back, which would pitch every tool fitted against it.
        /// </summary>
        private static readonly HumanBodyBones[] UnmaskedBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Neck, HumanBodyBones.Head, HumanBodyBones.LeftEye, HumanBodyBones.RightEye, HumanBodyBones.Jaw,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
        };

        /// <summary>
        /// What an arms-only hold leaves to the walk beneath (the Hold Arms layer has no body part). Put back
        /// to the rest pose after its clip is sampled, so the arms hang from the torso they really hang from.
        /// </summary>
        private static readonly HumanBodyBones[] TorsoBones =
            { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest };

        /// <summary>The finger joints whose centre is the middle of a closed fist: what a handle passes through.</summary>
        private static readonly HumanBodyBones[] FistBones =
        {
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
        };

        private readonly HumanoidAnimationProfile profile;
        private readonly Transform chest;
        private readonly Vector3 chestForward;
        private readonly (Transform Bone, Vector3 Position, Quaternion Rotation)[] restPose;
        private readonly (Transform Bone, Vector3 Position, Quaternion Rotation)[] restTorso;

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
            restTorso = TorsoBones.Select(b => Animator.GetBoneTransform(b))
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

        /// <summary>Hold the pose a hold style plays, mid-clip. False when the profile has none.</summary>
        public bool Pose(ItemGrip.HoldStyle style)
        {
            HumanoidAnimationProfile.HoldPose pose = profile.HoldPoses.FirstOrDefault(p => p.style == style);
            if (pose.clip == null) return false;

            Sample(pose.clip);

            foreach ((Transform bone, Vector3 position, Quaternion rotation) in restPose)
                bone.SetLocalPositionAndRotation(position, rotation);

            if (pose.armsOnly)
                foreach ((Transform bone, Vector3 position, Quaternion rotation) in restTorso)
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

        private void Sample(AnimationClip clip) => Sample(clip, clip.length * 0.5f);

        private void Sample(AnimationClip clip, float time)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(Animator.gameObject, clip, time);
            AnimationMode.EndSampling();
        }

        /// <summary>
        /// The whole body as <paramref name="clip"/> poses it <paramref name="time"/> seconds in, with nothing put
        /// back: the legs, the root and the head too, which is what an action on the Full layer shows. Not for
        /// fitting a grip (<see cref="Pose"/> is), for asking where a hand goes while a worker works.
        /// </summary>
        public void Show(AnimationClip clip, float time) => Sample(clip, time);

        /// <summary>Where the grip frame puts an item's grip point: the palm, in world space, in whatever pose is held.</summary>
        public Vector3 GripOrigin => Hand.TransformPoint(Frame.LocalPosition);

        /// <summary>
        /// The middle of the closed fingers' loop in world space, in whatever pose is held: where a handle really
        /// passes through the fist. The grip frame's origin is a fraction of the hand's length from the wrist, which on
        /// the Raxy (long fingers curled well beyond the knuckles) is a hand's width short of the fist, so a tool seated
        /// on it lies along the wrist instead of in the hand.
        /// </summary>
        public Vector3 FistCentre()
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (HumanBodyBones bone in FistBones)
            {
                Transform joint = Animator.GetBoneTransform(bone);
                if (joint == null) continue;

                sum += joint.position;
                count++;
            }

            return count == 0 ? GripOrigin : sum / count;
        }

        /// <summary>The hand's grip frame, in the holder's <see cref="Facing"/> space, in whatever pose is currently held.</summary>
        public Quaternion GripFrameInBody() =>
            Quaternion.Inverse(Facing) * Hand.rotation * Frame.LocalRotation;

        /// <summary>
        /// Draw the body as its bones are NOW, for as long as the returned scope lives. A skinned mesh is skinned once per
        /// editor frame, so every render after the first in one command shows the pose the body had when the frame began:
        /// the arm hangs in the picture while the tool, parented to the bone, is where the bone really is, and the tool
        /// looks as if it floats beside the hand. This hides each skinned renderer and draws a baked copy in its place.
        /// </summary>
        public IDisposable Bake() => new BakedBody(Body);

        private sealed class BakedBody : IDisposable
        {
            private readonly List<SkinnedMeshRenderer> hidden = new List<SkinnedMeshRenderer>();
            private readonly List<GameObject> copies = new List<GameObject>();
            private readonly List<Mesh> meshes = new List<Mesh>();

            public BakedBody(GameObject body)
            {
                foreach (SkinnedMeshRenderer skin in body.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                {
                    if (!skin.enabled || skin.sharedMesh == null) continue;

                    var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                    skin.BakeMesh(mesh, true);
                    var copy = new GameObject(skin.name + " (baked)") { hideFlags = HideFlags.HideAndDontSave };
                    copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                    copy.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                    copy.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
                    copy.transform.localScale = skin.transform.lossyScale;

                    skin.enabled = false;
                    hidden.Add(skin);
                    copies.Add(copy);
                    meshes.Add(mesh);
                }
            }

            public void Dispose()
            {
                foreach (SkinnedMeshRenderer skin in hidden)
                    if (skin != null) skin.enabled = true;
                foreach (GameObject copy in copies)
                    if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
                foreach (Mesh mesh in meshes)
                    if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        public void Dispose()
        {
            AnimationMode.StopAnimationMode();
            if (Body != null) UnityEngine.Object.DestroyImmediate(Body);
        }
    }
}
