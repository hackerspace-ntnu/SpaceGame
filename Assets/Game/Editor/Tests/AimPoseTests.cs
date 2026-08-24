// The three pieces of aim arithmetic that fail silently if they are wrong.
//
// None of these throws when inverted. A goal anchored on the body instead of the eye still
// produces a plausible position; a grip frame applied the wrong way round still produces a valid
// rotation; an exponential ease still moves towards its target. Each one simply looks slightly
// off in play mode, in a way that reads as "the pose needs tuning" rather than as a bug — which
// is exactly the kind of thing worth pinning.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using SpaceGame.Characters;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class AimPoseTests
    {
        /// <summary>Where the eye sits on the player, roughly. Only the pitch matters here.</summary>
        private static readonly Vector3 Eye = new Vector3(0f, 1.45f, 0f);

        [Test]
        public void TheHandGoalFollowsThePitch_RatherThanStayingLevel()
        {
            // The whole reason aiming uses IK. The body does not pitch — only the camera does —
            // so a goal derived from the body would leave the weapon level while the player
            // looked up, which is the exact failure the design rejected a spine lean to avoid.
            Vector3 forwardOffset = new Vector3(0f, 0f, 0.34f);

            Vector3 level = AimPose.HandGoal(Eye, Quaternion.identity, forwardOffset);
            Vector3 up = AimPose.HandGoal(Eye, Quaternion.Euler(-45f, 0f, 0f), forwardOffset);

            Assert.Greater(up.y, level.y + 0.1f,
                "looking up must raise the hand goal; if it does not, the goal is anchored to the body");
            Assert.Less(up.z, level.z,
                "pitching up trades forward reach for height");
        }

        [Test]
        public void TheHandRotationUndoesTheGripFrame_SoTheItemPointsDownTheRay()
        {
            // The trap this catches: aiming the HAND at the target aims the ITEM somewhere else,
            // because an item is seated at a fixed rotation relative to the grip frame and on this
            // rig that frame is most of a right angle. Inverting the wrong operand compiles, runs,
            // and puts the barrel a quarter turn off.
            Quaternion ray = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            Quaternion gripFrame = Quaternion.Euler(0f, 90f, 0f);

            Quaternion hand = AimPose.HandRotationForItem(ray, gripFrame);

            // Re-seat the item the way EquipItemSocket does — hand rotation composed with the
            // frame — and the item must come out pointing down the ray.
            Quaternion item = hand * gripFrame;

            Assert.Less(Quaternion.Angle(item, ray), 0.01f,
                "the item, not the wrist, has to end up pointing at the target");
        }

        [Test]
        public void TheEaseActuallyReachesOne()
        {
            // An exponential ease approaches its target and never arrives. An aim weight stuck at
            // 0.997 leaves the hand permanently a few millimetres short of the eye and the IK
            // never fully takes over — visible as the weapon not quite settling.
            float t = 0f;
            for (int i = 0; i < 100; i++)
                t = AimPose.Ease(t, 1f, 0.15f, 1f / 60f);

            Assert.AreEqual(1f, t, 1e-6f, "the blend must arrive, not merely approach");
        }

        [Test]
        public void AZeroBlendTimeSnaps()
        {
            // Callers that want no blend should not need a special case, and a division by zero
            // here would produce NaN and poison every downstream weight.
            Assert.AreEqual(1f, AimPose.Ease(0f, 1f, 0f, 1f / 60f), 1e-6f);
        }

        [Test]
        public void TheElbowHintSitsOffTheShoulderToHandLine()
        {
            // Without a hint the two-bone solver puts the elbow anywhere on the circle around the
            // shoulder-to-hand axis and flips sides when the hand crosses the body — which is what
            // looking up and down does. A hint ON the line is no hint at all.
            Vector3 shoulder = new Vector3(0.18f, 1.4f, 0f);
            Vector3 hand = new Vector3(0.06f, 1.4f, 0.34f);

            Vector3 hint = AimPose.ElbowHint(shoulder, hand, Quaternion.identity,
                                             new Vector3(0.30f, -0.28f, -0.05f));

            Vector3 axis = (hand - shoulder).normalized;
            float offAxis = Vector3.ProjectOnPlane(hint - shoulder, axis).magnitude;

            Assert.Greater(offAxis, 0.1f,
                "the hint must be well off the shoulder-to-hand axis or the solver is unconstrained");
            Assert.Less(hint.y, shoulder.y, "the elbow belongs below the shoulder, not above it");
        }
    }
}
