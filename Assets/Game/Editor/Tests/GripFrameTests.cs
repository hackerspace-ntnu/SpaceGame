// Which way a held item points, pinned against the rig itself.
//
// HandGripFrame builds the frame an item is posed in out of the hand's finger bones. The part
// that is easy to get wrong — and was wrong — is which anatomical axis counts as "forward".
// It was the back-of-hand normal, on the reasoning that a pistol's barrel exits over the
// curled index finger. Measured against the pose the artists actually authored for holding a
// gun, that was 94 degrees out: every artifact pointed across the body instead of down it.
//
// A frame can be wrong this way and still pass every self-consistency check — it stays
// perfectly orthonormal, the origin still lands in the palm, nothing throws. So the contract
// worth pinning is not internal consistency. It is agreement with the rig: in the pose the
// Hold bool drives to, a held item must point where the character is facing.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using SpaceGame.Items;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class GripFrameTests
    {
        private const string PlayerPrefab =
            "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";

        private GameObject instance;

        [TearDown]
        public void TearDown()
        {
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            if (instance != null) Object.DestroyImmediate(instance);
        }

        private Animator SpawnPlayer()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Assert.IsNotNull(prefab, "player prefab missing at " + PlayerPrefab);
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.hideFlags = HideFlags.DontSave;
            var anim = instance.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(anim, "player has no Animator");
            Assert.IsTrue(anim.isHuman, "player avatar is not humanoid — see the avatar notes");
            return anim;
        }

        private static AnimationClip FindHoldClip()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip Gun_Aim01"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    var clip = o as AnimationClip;
                    if (clip != null && clip.name.Contains("Gun_Aim01")) return clip;
                }
            }
            return null;
        }

        [Test]
        public void TheFrameIsDerivedFromTheFingerBones()
        {
            var anim = SpawnPlayer();
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);

            var frame = HandGripFrame.Derive(anim, hand, true);

            Assert.AreEqual("finger bones", frame.Source,
                "this rig has finger bones; falling back means the lookup broke");
        }

        [Test]
        public void TheFrameIsOrthonormal()
        {
            var anim = SpawnPlayer();
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var frame = HandGripFrame.Derive(anim, hand, true);

            Vector3 f = frame.LocalRotation * Vector3.forward;
            Vector3 u = frame.LocalRotation * Vector3.up;
            Assert.AreEqual(0f, Vector3.Dot(f, u), 1e-3f, "forward and up must be perpendicular");
            Assert.AreEqual(1f, f.magnitude, 1e-3f);
            Assert.AreEqual(1f, u.magnitude, 1e-3f);
        }

        [Test]
        public void AHeldItemPointsAlongTheFingers()
        {
            var anim = SpawnPlayer();
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var middle = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal)
                         ?? hand.Find("mixamorig:RightHandMiddle1");
            Assert.IsNotNull(middle, "no middle finger bone to measure against");

            var frame = HandGripFrame.Derive(anim, hand, true);
            Vector3 forward = hand.rotation * frame.LocalRotation * Vector3.forward;
            Vector3 alongFingers = (middle.position - hand.position).normalized;

            Assert.Less(Vector3.Angle(forward, alongFingers), 15f,
                "an item points the way the fingers point, not out of the back of the hand");
        }

        [Test]
        public void InTheAuthoredHoldPose_AHeldItemPointsWhereTheCharacterFaces()
        {
            // The contract that actually broke. 94 degrees before the fix, 4 after.
            var clip = FindHoldClip();
            if (clip == null) Assert.Ignore("HumanM@Gun_Aim01 not present in the project");

            var anim = SpawnPlayer();
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);

            AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(instance, clip, clip.length * 0.5f);
            AnimationMode.EndSampling();

            var frame = HandGripFrame.Derive(anim, hand, true);
            Vector3 forward = hand.rotation * frame.LocalRotation * Vector3.forward;

            float err = Vector3.Angle(forward, instance.transform.forward);
            Assert.Less(err, 20f,
                "in the pose the Hold bool drives to, a held item must point down the character's "
                + "facing; got " + err.ToString("F1") + " degrees");
        }

        [Test]
        public void TheGripSitsInTheFistRatherThanAtTheWrist()
        {
            var anim = SpawnPlayer();
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var frame = HandGripFrame.Derive(anim, hand, true);

            float outFromWrist = frame.LocalPosition.magnitude;
            Assert.Greater(outFromWrist, frame.HandLength * 0.2f,
                "the grip must sit out along the fingers, not on the wrist joint");
            Assert.Less(outFromWrist, frame.HandLength,
                "the grip must stay inside the hand, not past the knuckles");
        }
    }
}
