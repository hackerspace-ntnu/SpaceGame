using UnityEngine;

namespace SpaceGame.Vehicles.DuneFoil
{
    /// <summary>
    /// How high the craft rides and how well the foil grips.
    ///
    /// The strut is nearly fourteen metres long. At rest the foil makes no lift and the hull
    /// sits on the sand, which is also the only reason a player can get aboard; at speed the
    /// hull climbs the strut until the foil tip is at the surface. The craft therefore rises
    /// and falls with its own speed rather than sitting at a fixed height, and the sand line is
    /// tracked by raycast so it follows the dunes.
    ///
    /// Everything vertical it reports is rate-limited. A hull that jitters even a few centimetres
    /// a frame is not a cosmetic problem here: the deck is a walking surface and every rider on
    /// it is moved by the hull's own frame-to-frame delta, so the hull's noise becomes the
    /// player's noise. See <see cref="maxVerticalSpeed"/>.
    ///
    /// Reports numbers. <see cref="DuneFoilLocomotion"/> is what actually moves the hull.
    /// </summary>
    public class FoilLift : MonoBehaviour
    {
        [Header("Geometry")]
        [Tooltip("Strut length: how far the hull can climb before the foil tip surfaces and " +
                 "stops making lift. Measured off the model by the builder.")]
        [SerializeField, Min(0.1f)] private float maxRideHeight = 13.18f;

        [Tooltip("Speed at which the hull first lifts off the sand, m/s.")]
        [SerializeField, Min(0.1f)] private float takeoffSpeed = 5f;

        [Header("Response")]
        [Tooltip("How quickly the craft climbs to its target height. Low: a foiler rises over a " +
                 "second or two, it does not snap.")]
        [SerializeField, Min(0.1f)] private float riseSpeed = 1.1f;

        [Tooltip("How quickly it settles back down. Slower than the rise, so a gust that comes " +
                 "and goes does not drop the whole hull and pick it up again.")]
        [SerializeField, Min(0.1f)] private float sinkSpeed = 0.75f;

        [Tooltip("Extra smoothing on the sand height, so a lumpy dune does not shake the hull.")]
        [SerializeField, Min(0.1f)] private float groundFollowSpeed = 2.5f;

        [Tooltip("Hard ceiling on how fast the hull may move vertically, m/s. The last line of " +
                 "defence against a visible jump: a chunk streaming in, a probe that lands on a " +
                 "rock, a frame hitch. Anything that would move the hull faster than this is " +
                 "served out over the following frames instead.")]
        [SerializeField, Min(0.5f)] private float maxVerticalSpeed = 9f;

        [Header("Ground")]
        [Tooltip("Layers counted as sand.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("How far above the hull to start the downward probe.")]
        [SerializeField, Min(1f)] private float probeHeight = 40f;

        [Tooltip("How far down to probe.")]
        [SerializeField, Min(1f)] private float probeDistance = 200f;

        [Tooltip("How far above the sand it was last standing on the craft will accept a new " +
                 "surface, metres. Stops the probe latching onto the roof of a ruin or the top " +
                 "of a cliff the hull is passing beside and flying the craft up onto it.")]
        [SerializeField, Min(0.5f)] private float maxGroundStep = 6f;

        [Header("Grip")]
        [Tooltip("Fraction of sideways motion the foil kills when fully buried.")]
        [SerializeField, Range(0f, 1f)] private float gripSubmerged = 0.98f;

        [Tooltip("Fraction it kills when flying high, with only the foil tip in the sand.")]
        [SerializeField, Range(0f, 1f)] private float gripFlying = 0.6f;

        [Header("Drag")]
        [Tooltip("Drag coefficient with the hull ploughing through sand.")]
        [SerializeField, Min(0f)] private float hullDrag = 0.05f;

        [Tooltip("Drag coefficient up on the foil. Far lower; that is the point of a foiler.")]
        [SerializeField, Min(0f)] private float foilDrag = 0.004f;

        [Tooltip("Deceleration that does not depend on speed, with the hull down, m/s². Without " +
                 "one of these the craft never actually stops — quadratic drag approaches zero " +
                 "and never gets there, so a furled rig leaves the hull creeping forever.")]
        [SerializeField, Min(0f)] private float hullResistance = 0.9f;

        [Tooltip("The same up on the foil, m/s². Small: a foiler coasts.")]
        [SerializeField, Min(0f)] private float foilResistance = 0.12f;

        [Header("Climbing")]
        [Tooltip("How far ahead along the heading to measure the slope, metres. Roughly a hull " +
                 "length: short enough that a dune is felt as you reach it, long enough that the " +
                 "craft is not steered by every ripple.")]
        [SerializeField, Min(1f)] private float slopeLookahead = 18f;

        [Tooltip("Deceleration at the stall grade, m/s². The rig makes a couple of m/s², so this " +
                 "being an order of magnitude larger is the point: dunes win.")]
        [SerializeField, Min(0f)] private float climbCost = 14f;

        [Tooltip("Gradient the craft cannot carry way through. 0.3 is about 17 degrees.")]
        [SerializeField, Min(0.01f)] private float stallGrade = 0.30f;

        [Tooltip("Smoothing on the measured gradient. Raw, it jitters with every ripple the " +
                 "forward probe crosses and the craft surges.")]
        [SerializeField, Min(0.1f)] private float gradeFollowSpeed = 4f;

        private float smoothedGroundY;
        private bool groundPrimed;
        private float smoothedGrade;
        private float lastHullY;
        private bool hullPrimed;
        private readonly RaycastHit[] probeHits = new RaycastHit[24];

        /// <summary>Current ride height above the sand, metres.</summary>
        public float RideHeight { get; private set; }

        /// <summary>Ride height as 0..1 of the strut. What drag and grip blend on.</summary>
        public float RideHeight01 => maxRideHeight <= 0f ? 0f : Mathf.Clamp01(RideHeight / maxRideHeight);

        /// <summary>World Y of the sand under the craft, as measured this frame.</summary>
        public float GroundY { get; private set; }

        /// <summary>World Y of the sand the hull is actually flying off — the smoothed one.</summary>
        public float SurfaceY => smoothedGroundY;

        /// <summary>Surface normal of the sand under the craft. Up when nothing was hit.</summary>
        public Vector3 GroundNormal { get; private set; } = Vector3.up;

        /// <summary>True on any frame the probe actually found sand.</summary>
        public bool HasGround { get; private set; }

        /// <summary>True when the hull is clear of the sand and the craft is foiling.</summary>
        public bool IsFoiling => RideHeight01 > 0.05f;

        /// <summary>Strut length. The ceiling on ride height.</summary>
        public float MaxRideHeight { get => maxRideHeight; set => maxRideHeight = Mathf.Max(0.1f, value); }

        /// <summary>Speed at which the hull leaves the sand.</summary>
        public float TakeoffSpeed { get => takeoffSpeed; set => takeoffSpeed = Mathf.Max(0.1f, value); }

        /// <summary>Gradient of the sand ahead, rise over run. Positive uphill.</summary>
        public float ClimbGrade { get; private set; }

        /// <summary>World Y of the sand a lookahead ahead of the craft.</summary>
        public float GroundYAhead { get; private set; }

        /// <summary>True when the ground ahead is steeper than the craft can carry way through.</summary>
        public bool IsStalling => ClimbGrade > stallGrade;

        /// <summary>
        /// Forget everything measured and take the ground fresh next frame.
        ///
        /// Call after teleporting the craft or moving it between scenes. Without it the hull
        /// carries its old sand line into the new place and then walks to the real one at the
        /// vertical rate limit, which on a long jump is a craft visibly sinking through the world
        /// for several seconds.
        /// </summary>
        public void Prime()
        {
            groundPrimed = false;
            hullPrimed = false;
            smoothedGrade = 0f;
            ClimbGrade = 0f;

            // The ride height goes too. It is the other half of the hull's altitude, and a craft
            // that arrives somewhere new still holding thirteen metres of it hangs in the air and
            // then sinks to the sand over the next couple of seconds, which is the same visible
            // wrongness the ground reference was reset to avoid.
            RideHeight = 0f;
        }

        /// <summary>
        /// Advance the ride height for this frame and return the world Y the hull should sit at.
        /// </summary>
        /// <param name="speed">Horizontal speed, m/s.</param>
        /// <param name="heading">Craft heading, world space, horizontal, unit length.</param>
        /// <param name="deltaTime">Frame time.</param>
        public float Tick(float speed, Vector3 heading, float deltaTime)
        {
            SampleGround(deltaTime);
            SampleSlope(heading, deltaTime);

            float target = FoilPhysics.RideHeight(speed, takeoffSpeed, maxRideHeight);
            float response = target > RideHeight ? riseSpeed : sinkSpeed;
            RideHeight = Mathf.Lerp(RideHeight, target, 1f - Mathf.Exp(-response * deltaTime));

            float wanted = smoothedGroundY + RideHeight;

            // The one place the hull's height is finally decided, and the one place a rate limit
            // is worth anything. Everything above smooths its own signal; this promises that
            // whatever they produce between them, the hull never moves vertically faster than a
            // craft plausibly can.
            if (!hullPrimed)
            {
                lastHullY = wanted;
                hullPrimed = true;
                return wanted;
            }

            lastHullY = Mathf.MoveTowards(lastHullY, wanted, maxVerticalSpeed * deltaTime);
            return lastHullY;
        }

        /// <summary>Deceleration from sand drag at this speed, m/s².</summary>
        public float DragDeceleration(float speed)
        {
            return FoilPhysics.SandDrag(speed, RideHeight01, hullDrag, foilDrag);
        }

        /// <summary>
        /// Deceleration that does not care about speed — what finally brings the craft to a stop.
        /// </summary>
        public float RollingDeceleration()
        {
            return FoilPhysics.RollingResistance(RideHeight01, hullResistance, foilResistance);
        }

        /// <summary>Deceleration from the climb ahead, m/s². Zero on the flat and downhill.</summary>
        public float ClimbDeceleration()
        {
            return FoilPhysics.SlopeDeceleration(ClimbGrade, climbCost, stallGrade);
        }

        /// <summary>Fraction of sideways velocity the foil removes this frame.</summary>
        public float LateralGrip()
        {
            return FoilPhysics.LateralGrip(RideHeight01, gripSubmerged, gripFlying);
        }

        /// <summary>
        /// Look ahead along the heading and work out how hard the sand there is climbing.
        ///
        /// Measured against the RAW ground under the craft rather than the smoothed value: the
        /// smoothing on <see cref="smoothedGroundY"/> is a lag, and differencing a lagged signal
        /// against an unlagged one reports a slope purely from the craft's own vertical motion.
        /// A craft settling onto flat sand would read as climbing and brake itself to a stop.
        /// </summary>
        private void SampleSlope(Vector3 heading, float deltaTime)
        {
            Vector3 flat = new Vector3(heading.x, 0f, heading.z);
            if (flat.sqrMagnitude < 1e-6f || !HasGround)
            {
                // Nothing to measure against — decay rather than hold the last grade, or a craft
                // that loses its ground reference stays braked for as long as it is airborne.
                smoothedGrade = Mathf.Lerp(smoothedGrade, 0f,
                                           1f - Mathf.Exp(-gradeFollowSpeed * deltaTime));
                ClimbGrade = smoothedGrade;
                return;
            }

            Vector3 ahead = transform.position + flat.normalized * slopeLookahead;

            float raw;
            // No acceptance window on the lookahead. A wall ahead SHOULD read as an unclimbable
            // grade and stop the craft; that is the feature. It is only the probe the hull flies
            // off that must refuse to latch onto scenery.
            //
            // "No window" is +infinity, not -infinity: `ceiling` is an UPPER bound, so every
            // finite hit clears a -infinity ceiling and is diverted into the lowest-above-ceiling
            // bucket. That made the lookahead return the LOWEST surface ahead, so a rock or a
            // floor slab under the dune face read as a downhill grade and the climb braking
            // switched itself off. SampleGround uses +infinity for this same meaning.
            if (ProbeGround(ahead, float.PositiveInfinity, out float aheadY, out _))
            {
                GroundYAhead = aheadY;
                raw = FoilPhysics.ClimbGrade(aheadY - GroundY, slopeLookahead);
            }
            else
            {
                // Sailing out over the edge of the world, or off a cliff. Not a climb.
                GroundYAhead = GroundY;
                raw = 0f;
            }

            smoothedGrade = Mathf.Lerp(smoothedGrade, raw,
                                       1f - Mathf.Exp(-gradeFollowSpeed * deltaTime));
            ClimbGrade = smoothedGrade;
        }

        private void SampleGround(float deltaTime)
        {
            float ceiling = groundPrimed ? GroundY + maxGroundStep : float.PositiveInfinity;

            if (ProbeGround(transform.position, ceiling, out float y, out Vector3 normal))
            {
                GroundY = y;
                GroundNormal = normal;
                HasGround = true;
            }
            else
            {
                // Nothing under us: a chunk still streaming in, a hole in the world, or the craft
                // sailing off a cliff edge. HOLD the last sand line rather than inventing one.
                //
                // This used to leave HasGround true forever after the first successful probe,
                // which quietly told the climb model it had a valid reference over open air. And
                // the fallback that did run wrote "here, minus the ride height" into GroundY,
                // which is the craft measuring the ground off its own altitude — a loop that
                // walks the hull upward for as long as the probe keeps missing.
                HasGround = false;
                GroundNormal = Vector3.up;
                if (!groundPrimed)
                {
                    GroundY = transform.position.y - RideHeight;
                }
            }

            if (!groundPrimed)
            {
                smoothedGroundY = GroundY;
                groundPrimed = true;
                return;
            }

            smoothedGroundY = Mathf.Lerp(smoothedGroundY, GroundY,
                                         1f - Mathf.Exp(-groundFollowSpeed * deltaTime));
        }

        /// <summary>
        /// Highest sand under a world position, ignoring the craft, anything it is carrying, and
        /// anything standing improbably far above the sand it was last on.
        /// Takes the XZ of <paramref name="at"/>; the ray always starts above the hull.
        /// </summary>
        /// <param name="ceiling">
        /// Highest surface that counts as ground. Pass negative infinity to take whatever is
        /// there. When every hit is above it, the LOWEST of them is returned instead, so a craft
        /// that has genuinely arrived somewhere higher climbs onto it rather than falling through
        /// the world waiting for a surface it will never accept.
        /// </param>
        private bool ProbeGround(Vector3 at, float ceiling, out float groundY,
                                 out Vector3 groundNormal)
        {
            groundY = 0f;
            groundNormal = Vector3.up;

            Vector3 origin = new Vector3(at.x, transform.position.y + probeHeight, at.z);

            // Every hit, not the first one — the probe starts above the craft and the craft is
            // in the way of its own ground. A single Raycast finds the deck, the craft then
            // flies to "deck + ride height", and next frame the deck is higher still: it climbs
            // away on its own hull. Skip anything belonging to this craft and take the highest
            // surface below it that does not.
            //
            // Skipping the craft's own colliders closed only half of that loop. The craft also
            // CARRIES riders: a player on the deck is not parented to the hull, they are moved
            // along by WalkerPlatformCarrier. So the probe found the player's capsule, took it
            // for the highest ground under the craft, and the hull flew to "player + ride
            // height" — which lifted the deck, which lifted the player, which raised the probe
            // again. The craft climbed away on its own passenger, parked, at zero speed, for as
            // long as somebody stood under the ray.
            //
            // Both probes run through here, so the slope lookahead gets the same treatment: a
            // rider walking forward on the deck must not read as a dune rising ahead of the bow.
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, probeHits, probeDistance,
                                                groundMask, QueryTriggerInteraction.Ignore);
            bool found = false;
            float bestY = float.NegativeInfinity;
            Vector3 bestNormal = Vector3.up;

            bool foundAbove = false;
            float lowestAboveY = float.PositiveInfinity;
            Vector3 lowestAboveNormal = Vector3.up;

            for (int i = 0; i < count; i++)
            {
                Transform hitTransform = probeHits[i].collider.transform;
                if (hitTransform == transform || hitTransform.IsChildOf(transform)) continue;
                // Sand is a static collider. Anything moving under its own physics is cargo or a
                // passenger, and a craft does not ride on those — least of all the ones it is
                // carrying, which is what closes the loop above.
                Rigidbody hitBody = probeHits[i].collider.attachedRigidbody;
                if (hitBody != null && !hitBody.isKinematic) continue;

                float y = probeHits[i].point.y;

                if (y > ceiling)
                {
                    if (y < lowestAboveY)
                    {
                        lowestAboveY = y;
                        lowestAboveNormal = probeHits[i].normal;
                        foundAbove = true;
                    }
                    continue;
                }

                if (y > bestY)
                {
                    bestY = y;
                    bestNormal = probeHits[i].normal;
                    found = true;
                }
            }

            if (found)
            {
                groundY = bestY;
                groundNormal = bestNormal;
                return true;
            }

            if (foundAbove)
            {
                groundY = lowestAboveY;
                groundNormal = lowestAboveNormal;
                return true;
            }

            return false;
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 p = transform.position;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(p, p + Vector3.down * RideHeight);
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);
            Gizmos.DrawWireCube(p + Vector3.down * RideHeight, new Vector3(6f, 0.05f, 18f));

            // The slope lookahead, and how hard it is braking: green rolls, red stops.
            Vector3 ahead = p + transform.forward * slopeLookahead;
            Gizmos.color = Color.Lerp(Color.green, Color.red,
                                      Mathf.Clamp01(ClimbGrade / Mathf.Max(stallGrade, 1e-3f)));
            Gizmos.DrawLine(new Vector3(p.x, GroundY, p.z),
                            new Vector3(ahead.x, GroundYAhead, ahead.z));
            Gizmos.DrawWireSphere(new Vector3(ahead.x, GroundYAhead, ahead.z), 1f);
        }
    }
}
