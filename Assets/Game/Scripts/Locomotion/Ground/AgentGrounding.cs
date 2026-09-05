// Putting a NavMesh agent's body on the ground rather than on the NavMesh.
//
// The two are not the same surface and never were. The world mesh is baked at voxelSize 0.3333 and
// Recast puts each polygon at the TOP of the voxel column it came from, so the mesh floats: a
// median of 0.257 m above the terrain across 1384 samples, from 0.262 m below to 0.600 m above.
// NavMeshAgentMotor sets the transform to exactly what navigation says, so every agent inherited
// that error verbatim -- which is why they were all hovering, and why they hovered by DIFFERENT
// amounts depending on where they stood.
//
// A constant baseOffset cannot fix that. Subtracting the median leaves the middle half of the
// world within 6 cm, but still floats 22 cm at p95 and buries the body to the shins at the worst
// sample. The error is terrain-dependent, so the correction has to be measured per frame.
//
// This class is the arithmetic only. It never touches a Transform or a collider, so the whole of
// it is testable without a scene; the raycasting lives in WalkerGround and the wiring in
// AgentGroundConform.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    /// Per-agent tuning for <see cref="AgentGrounding"/>. A struct so a MonoBehaviour can serialize
    /// the fields and hand them over each frame without allocating.
    public struct AgentGroundingSettings
    {
        /// Distance from the body's pivot to its soles. Every agent prefab in this project is
        /// authored with the soles at the pivot (measured: -0.107 m to +0.014 m), so this is 0
        /// unless a particular model says otherwise.
        public float SoleOffset;

        /// Cap on the correction in metres, either way. The largest error measured in the world is
        /// 0.60 m; anything past this cap is a probe that found something it should not have -- a
        /// cave roof, a collider streaming in -- and clamping is what stops it teleporting a body.
        public float MaxCorrection;

        /// First-order follow rate for the height, in 1/seconds.
        public float HeightFollowSpeed;

        /// How much of the ground's tilt the body takes on. 0 stands bolt upright and reads as a
        /// cardboard cut-out on a hillside; 1 lies the body flat on the slope, which is right for a
        /// quadruped and wrong for anything that walks on two legs.
        public float SlopeFollow;

        /// Cap on the tilt in degrees. The world bakes walkable ground up to 60 degrees, and a
        /// biped leaned over that far has fallen over.
        public float MaxTiltDegrees;

        /// First-order follow rate for the tilt, in 1/seconds.
        public float TiltFollowSpeed;
    }

    /// <summary>
    /// Per-frame solve for where an agent's body should sit and how it should lean. Stateful across
    /// frames (both outputs are smoothed), so one instance per agent.
    /// </summary>
    public sealed class AgentGrounding
    {
        private readonly Quaternion restBodyRotation;

        private float heightOffset;
        private Quaternion tilt = Quaternion.identity;
        private bool primed;

        private Quaternion lastWritten;
        private bool hasWritten;

        public AgentGrounding(Quaternion restBodyRotation)
        {
            this.restBodyRotation = restBodyRotation;
            BodyRotation = restBodyRotation;
        }

        /// Metres to add to wherever navigation put the body. Feeds NavMeshAgent.baseOffset.
        public float HeightOffset => heightOffset;

        /// Local rotation to write on the body's visual root.
        public Quaternion BodyRotation { get; private set; }

        /// The smoothed slope tilt alone, without the pose it was composed onto. Exposed for tests
        /// and gizmos; gameplay wants <see cref="BodyRotation"/>.
        public Quaternion LastTilt => tilt;

        /// The visual root's rotation as the prefab authored it, for a caller putting the body back
        /// the way it found it.
        public Quaternion RestBodyRotation => restBodyRotation;

        /// <summary>
        /// Forget the smoothed state so the next <see cref="Step"/> snaps. Called from OnEnable: a
        /// creature is re-enabled by respawn, chunk streaming and save restores, and each of those
        /// puts it somewhere new. Easing across from the old correction would show it sliding into
        /// place.
        /// </summary>
        public void Reset()
        {
            heightOffset = 0f;
            tilt = Quaternion.identity;
            primed = false;
            hasWritten = false;
            BodyRotation = restBodyRotation;
        }

        /// <param name="grounded">Whether the probe found anything at all. False decays both
        /// outputs to neutral rather than holding a correction for ground that is not there.</param>
        /// <param name="navSurfaceY">World Y of the NavMesh polygon under the body, with every
        /// offset already stripped off it.</param>
        /// <param name="groundY">World Y of the real surface the probe found. Ignored when
        /// <paramref name="grounded"/> is false.</param>
        /// <param name="localGroundNormal">The surface normal, in the AGENT's local space, so the
        /// tilt turns with the body instead of being pinned to world axes.</param>
        /// <param name="currentBodyRotation">Whatever is on the visual root's localRotation right
        /// now, read before this write. See <see cref="Baseline"/>.</param>
        public void Step(bool grounded, float navSurfaceY, float groundY, Vector3 localGroundNormal,
                         Quaternion currentBodyRotation, in AgentGroundingSettings settings, float dt)
        {
            float targetOffset;
            Quaternion targetTilt;

            if (grounded)
            {
                float cap = Mathf.Max(0f, settings.MaxCorrection);
                targetOffset = Mathf.Clamp(groundY + settings.SoleOffset - navSurfaceY, -cap, cap);

                // Reuses the walkers' tilt solve rather than repeating it: same clamp, same
                // fraction-of-the-slope tunable, already tested.
                var plane = new WalkerSupportPlane
                {
                    Normal = localGroundNormal.sqrMagnitude > 1e-8f
                        ? localGroundNormal.normalized
                        : Vector3.up,
                    Height = 0f,
                    Valid = true,
                };
                targetTilt = plane.Tilt(settings.SlopeFollow, settings.MaxTiltDegrees);
            }
            else
            {
                targetOffset = 0f;
                targetTilt = Quaternion.identity;
            }

            if (primed)
            {
                heightOffset = Mathf.Lerp(heightOffset, targetOffset,
                                          Rate(settings.HeightFollowSpeed, dt));
                tilt = Quaternion.Slerp(tilt, targetTilt, Rate(settings.TiltFollowSpeed, dt));
            }
            else
            {
                heightOffset = targetOffset;
                tilt = targetTilt;
                primed = true;
            }

            BodyRotation = tilt * Baseline(currentBodyRotation);
            lastWritten = BodyRotation;
            hasWritten = true;
        }

        /// The project's standard frame-rate-independent first-order follow.
        private static float Rate(float speed, float dt)
            => dt > 0f ? 1f - Mathf.Exp(-Mathf.Max(0f, speed) * dt) : 1f;

        /// <summary>
        /// What to tilt FROM, and the one genuinely subtle thing in this class.
        ///
        /// <para>
        /// The node the tilt lands on is animated on some rigs and not on others. The Golem's clips
        /// carry a rotation curve for <c>Bone_Root</c> and the DuneRat's for <c>Arm_DuneRat</c>; the
        /// Nomad's <c>Model</c>, the PatrolRobots' <c>Armature</c> and the Vrescal's <c>vrescal</c>
        /// have nothing driving them at all. The two cases want opposite treatment, and getting it
        /// wrong fails loudly in both directions: tilt from the rest pose on an animated node and
        /// the tilt erases the animation; tilt from the read-back value on a node nothing drives and
        /// last frame's tilt is multiplied in again, every frame, until the body is spinning.
        /// </para>
        /// <para>
        /// A serialized flag per prefab would answer it, and would be silently wrong the first time
        /// someone re-exports a rig with a root curve it did not have before. Read it off the
        /// transform instead. If the rotation still holds exactly what was written last frame,
        /// nothing else touched it and the baseline is the rest pose. If it changed, the Animator
        /// wrote it, and that is the baseline. A clip that momentarily lands on exactly the value
        /// written costs one frame of rest-pose baseline and corrects itself on the next.
        /// </para>
        /// </summary>
        private Quaternion Baseline(Quaternion current)
        {
            if (!hasWritten) return current;

            // abs() because q and -q are the same rotation. 0.99999 is about half a degree.
            return Mathf.Abs(Quaternion.Dot(current, lastWritten)) > 0.99999f
                ? restBodyRotation
                : current;
        }
    }
}
