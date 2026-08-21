// Where the floor is under a hovering hull.
//
// Two things make this more than a raycast.
//
// One probe is not enough for a hull this size. An eleven-metre ship whose nose is already over a
// rise still reads flat ground under its middle, so it noses straight into the slope and stops. The
// ground reported here is the HIGHEST of a small ring of probes across the footprint, so the hull
// starts climbing as soon as any part of it has something underneath — which is what a skirt of
// lift fans would do.
//
// And a probe must reject more than the machine's own colliders. Rays go through WalkerGround,
// which is the one place in this project that also rejects anything standing on the machine: a
// player on the deck is carried rather than parented, so a naive probe reads their capsule as
// ground, the hull rises to "rider + ride height", the deck takes the rider up with it, and the
// vehicle climbs into the sky on its own passenger, parked, at zero speed.
using System;
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>
    /// Downward ground sampling for hovering vehicles. Serialized inline by the motor that owns it;
    /// call <see cref="Initialize"/> once before the first <see cref="TrySampleGroundY"/>.
    /// </summary>
    [Serializable]
    public class HoverGroundSensor
    {
        [Tooltip("Layers counted as ground. Static world geometry only — anything under its own " +
                 "physics is rejected regardless of layer.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("Half-extents of the probe ring in the craft's own axes: X across the beam, Y fore " +
                 "and aft. Set them to the hull's own half-size — a ring as wide as a long ship is " +
                 "long would find the rocks standing beside it and lift off over those.")]
        [SerializeField] private Vector2 footprintExtents = new Vector2(2f, 4f);

        [Tooltip("How far above the craft each probe starts, so ground slightly higher than the " +
                 "hull's origin is still found. Keep it small: a probe starts above the craft and " +
                 "looks down, so anything within this band — a low bridge, a hangar roof — is read " +
                 "as the ground the craft should be riding on.")]
        [SerializeField, Min(0f)] private float probeStartHeight = 0.5f;

        [Tooltip("How far down each probe looks from its start height.")]
        [SerializeField, Min(1f)] private float probeDistance = 80f;

        [Tooltip("How far above the craft to re-probe from when the normal probes find nothing. " +
                 "That means the craft is under the surface — clipped into a dune it could not " +
                 "climb — and a ray started inside the terrain sees no floor at all, so it has to " +
                 "be re-cast from clear air above to find the surface to climb back out to.")]
        [SerializeField, Min(1f)] private float recoveryProbeHeight = 60f;

        [Tooltip("Smoothing on the sampled ground height, so crossing the edge of a rock does not " +
                 "step the target. Higher follows terrain more literally.")]
        [SerializeField, Min(0.1f)] private float groundFollowSpeed = 8f;

        private Transform self;
        private WalkerGround ground;
        private float smoothedGroundY;
        private float riseRate;
        private bool primed;

        /// <summary>World Y of the ground last found under the craft.</summary>
        public float GroundY => smoothedGroundY;

        /// <summary>
        /// How fast the ground under the craft is rising, m/s. Driving a slope moves the target out
        /// from under a height servo continuously, and a servo that only answers to error has to sit
        /// permanently below its target to produce any climb at all — which on a hull this long means
        /// the nose ploughs into the hill. Handing the caller the rate as well lets it match the
        /// slope outright and spend the error term on the bumps.
        /// </summary>
        public float GroundRiseRate => riseRate;

        /// <summary>True once a probe has found ground; false over a hole or an unstreamed chunk.</summary>
        public bool HasGround { get; private set; }

        /// <summary>Half-extents the probe ring covers, in the craft's own axes.</summary>
        public Vector2 FootprintExtents
        {
            get => footprintExtents;
            set => footprintExtents = Vector2.Max(value, Vector2.zero);
        }

        /// <summary>Bind the sensor to the craft casting the rays. Its own colliders are never ground.</summary>
        public void Initialize(Transform self)
        {
            this.self = self;
            ground = new WalkerGround(self, groundMask, probeStartHeight, probeDistance);
            primed = false;
            HasGround = false;
        }

        /// <summary>
        /// Highest ground under the craft's footprint. False when nothing was found, which is a
        /// caller's cue to hold station rather than to drop.
        ///
        /// The height handed back is smoothed and then lag-corrected. Smoothing alone would be a
        /// quiet lie on any slope: a first-order filter tracking a steadily rising signal settles a
        /// fixed distance behind it — exactly rate ÷ follow speed — so the craft would ride that
        /// much low for the whole climb and plough its nose in. Adding the lag back is not a
        /// prediction, it is undoing a known constant offset.
        /// </summary>
        public bool TrySampleGroundY(Vector3 position, float deltaTime, out float groundY)
        {
            groundY = smoothedGroundY;
            if (ground == null)
                return false;

            HasGround = TryHighestUnderFootprint(position, out float rawGroundY);
            if (!HasGround)
                return false;

            // First sample lands exactly, so a craft spawned over a hill does not sweep up to it
            // from wherever the previous vehicle happened to leave the filter.
            float previousGroundY = smoothedGroundY;
            smoothedGroundY = primed
                ? Mathf.Lerp(smoothedGroundY, rawGroundY, 1f - Mathf.Exp(-groundFollowSpeed * deltaTime))
                : rawGroundY;

            // Differentiating the smoothed height is noisy on its own — one probe crossing the edge
            // of a rock is a step change — so the rate gets the same filter the height does.
            if (primed && deltaTime > 0f)
            {
                float rawRate = (smoothedGroundY - previousGroundY) / deltaTime;
                riseRate = Mathf.Lerp(riseRate, rawRate, 1f - Mathf.Exp(-groundFollowSpeed * deltaTime));
            }
            primed = true;

            groundY = smoothedGroundY + riseRate / groundFollowSpeed;
            return true;
        }

        // Centre, both beam ends and both ends of the hull, in the craft's own axes so the long
        // probes stay along the hull as it turns.
        private bool TryHighestUnderFootprint(Vector3 position, out float highestY)
        {
            highestY = float.NegativeInfinity;

            bool found = ProbeAt(position, ref highestY);
            found |= ProbeAlong(position, self.right, footprintExtents.x, ref highestY);
            found |= ProbeAlong(position, self.forward, footprintExtents.y, ref highestY);

            return found || TryRecoverFromBurial(position, out highestY);
        }

        private bool ProbeAlong(Vector3 position, Vector3 axis, float extent, ref float highestY)
        {
            if (extent <= 0f)
                return false;

            Vector3 offset = Vector3.ProjectOnPlane(axis, Vector3.up).normalized * extent;
            bool found = ProbeAt(position + offset, ref highestY);
            found |= ProbeAt(position - offset, ref highestY);
            return found;
        }

        private bool ProbeAt(Vector3 at, ref float highestY)
        {
            if (!ground.Below(at, out RaycastHit hit))
                return false;
            highestY = Mathf.Max(highestY, hit.point.y);
            return true;
        }

        // When the whole footprint comes up empty the craft is either over a hole or buried in one:
        // a downward ray cast from inside a mesh reports no hit at all, so a hull that clipped into
        // a dune it could not climb looks exactly like open sky. Re-cast from well above and the
        // surface it is under is the first thing the ray meets — the height it has to climb back
        // out to. Over a genuine hole this still finds nothing and the craft holds station.
        //
        // Only ever cast from the centre, and only when every probe missed. Running it per-probe
        // would fire it for a ring probe that happens to sit inside the cliff the craft is driving
        // past, find the clifftop thirty metres up, and fly the craft up the wall.
        private bool TryRecoverFromBurial(Vector3 position, out float surfaceY)
        {
            surfaceY = 0f;
            if (!ground.Ray(position + Vector3.up * recoveryProbeHeight,
                            recoveryProbeHeight + probeDistance, out RaycastHit hit))
                return false;

            surfaceY = hit.point.y;
            return true;
        }
    }
}
