// What the claws do while the machine walks.
//
// This is deliberately NOT a manipulation system. There is no reach planning, no grasping and no
// target in the world: a claw sways in time with the walk and comes up when the machine wants to
// look dangerous, and that is the whole brief. `WalkerArm` already turns a world point plus a tip
// direction into a posed limb; all that is missing is where the point goes, and that is here.
//
// Two things it borrows from what the legs already learned:
//
//   * THE SWAY IS DRIVEN FROM THE GAIT CLOCK, not from a timer of its own. A separate timer drifts
//     against the footfalls at every speed change and the machine stops reading as one animal.
//   * NOTHING AT STRIDE FREQUENCY GOES THROUGH A FILTER. The sway is evaluated, not smoothed. Only
//     the raise -- which is a state change, not a cycle -- is eased, and it is eased on its own
//     clock rather than on a first-order approach, so it arrives when it says it will.
//
// A plain class with no MonoBehaviour and no scene, so it is testable against a procedural rig.
// Everything it needs from a claw is measured off the rig at the first frame: where the arm rests,
// which side of the body it is on, how far it can reach, and which way it points. Nothing is
// authored in world units, so rescaling the model rescales the motion.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Locomotion;

namespace SpaceGame.Creatures.Crab
{
    public class CrabClawMotion
    {
        private readonly float sway;
        private readonly float raiseAmount;
        private readonly float raiseTime;

        /// Measured once, from the rest pose. Sized at bind: invariant I3, nothing per-frame allocates.
        private Vector3[] restLocal;
        private Vector3[] raisedLocal;
        private Vector3[] tipLocal;
        private float[] side;
        private float[] reach;
        private int bound;

        /// Where the raise is being asked to go, 0 or 1. Written by whatever decides the machine is
        /// threatened; read here and approached at a fixed rate.
        public float Wanted;

        /// Where the raise has actually got to.
        public float Raise { get; private set; }

        public CrabClawMotion(float sway, float raiseAmount, float raiseTime)
        {
            this.sway = sway;
            this.raiseAmount = raiseAmount;
            this.raiseTime = Mathf.Max(raiseTime, 1e-3f);
        }

        /// Write both claws' targets for this frame. `phase` is the gait clock's, `effort` the fraction
        /// of top speed being worked at -- a standing crab holds its claws still rather than waving them
        /// at nothing, which is the same reason the body's bob is scaled by effort.
        public void Pose(IReadOnlyList<WalkerArm> arms, Transform body, float phase, float effort,
                         float dt)
        {
            if (arms == null || body == null || arms.Count == 0) return;
            if (bound != arms.Count) Bind(arms, body);

            Raise = Mathf.MoveTowards(Raise, Mathf.Clamp01(Wanted), dt / raiseTime);

            for (int i = 0; i < arms.Count; i++)
            {
                WalkerArm arm = arms[i];

                // Opposite ends of the cycle for the two sides, so the claws counter each other the way
                // the legs on either side of the wave do rather than pumping in unison.
                float t = phase + (side[i] < 0f ? 0.5f : 0f);
                float wave = Mathf.Sin(2f * Mathf.PI * t);

                // A floor under the effort scaling: a stopped crab still breathes, it just does not
                // wave. Without it the claws freeze dead the instant the machine stops, which reads as
                // the animation having been switched off.
                float amplitude = sway * reach[i] * Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(effort));

                // Along the nose and a little up: the claw reaches out and back over the ground it is
                // walking across, which is where a crab's chelae live.
                Vector3 offset = new Vector3(0f, Mathf.Abs(wave) * amplitude * 0.4f, wave * amplitude);

                // The raise is a LERP BETWEEN TWO MEASURED POSES, not a displacement added to the rest
                // one. Pushing the target up by a fraction of the linkage is how a claw ends up asking
                // for somewhere its own arm cannot reach: the rest pose is already most of the way out,
                // so anything added to it lands outside. The raised pose is built from the SHOULDER at
                // a fixed fraction of what the arm can span, which is inside by construction.
                arm.Target = body.TransformPoint(
                    Vector3.Lerp(restLocal[i] + offset, raisedLocal[i] + offset * 0.4f, Raise));

                // The tip swings from where the rig points it toward straight up as the claw comes up,
                // so a raised claw presents its jaws rather than dragging them along the ground.
                arm.TipDirection = body.TransformDirection(
                    Vector3.Slerp(tipLocal[i], Vector3.up, Raise * 0.8f));
            }
        }

        /// Measure the claws. Side comes from where the shoulder sits in body space rather than from
        /// the arm's index, for the same reason the gait takes its ordering from `HomeLocal`: a rig
        /// re-authored with its limbs in another order still gets left and right the right way round.
        private void Bind(IReadOnlyList<WalkerArm> arms, Transform body)
        {
            int n = arms.Count;
            restLocal = new Vector3[n];
            raisedLocal = new Vector3[n];
            tipLocal = new Vector3[n];
            side = new float[n];
            reach = new float[n];

            for (int i = 0; i < n; i++)
            {
                WalkerArm arm = arms[i];
                Vector3 contact = arm.Rig.ContactPoint;
                Vector3 shoulder = body.InverseTransformPoint(arm.Rig.Anchor.position);

                restLocal[i] = body.InverseTransformPoint(contact);
                side[i] = Mathf.Sign(shoulder.x);

                Vector3 out_ = contact - arm.Rig.TipJoint.position;
                tipLocal[i] = out_.sqrMagnitude > 1e-8f
                    ? body.InverseTransformDirection(out_).normalized
                    : Vector3.down;

                reach[i] = Mathf.Max(arm.Rig.Geometry.MaxReach, 1e-3f);

                // Up, forward, and back toward the centreline, so the two claws close over the front of
                // the shell rather than splaying wider -- a threat display, not a shrug. Placed from
                // the shoulder at `raiseAmount` of what the linkage can span, so the pose is reachable
                // whatever the arm's proportions turn out to be.
                var dir = new Vector3(-side[i] * 0.30f, 1f, 0.45f).normalized;
                raisedLocal[i] = shoulder + dir * (reach[i] * Mathf.Clamp01(raiseAmount));
            }

            bound = n;
        }
    }
}
