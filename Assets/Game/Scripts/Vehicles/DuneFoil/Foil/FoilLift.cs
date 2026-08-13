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
        [Tooltip("How quickly the craft settles to its target height. Low: a foiler rises and " +
                 "sinks over a second or two, it does not snap.")]
        [SerializeField, Min(0.1f)] private float riseSpeed = 1.8f;

        [Tooltip("Extra smoothing on the sand height, so a lumpy dune does not shake the hull.")]
        [SerializeField, Min(0.1f)] private float groundFollowSpeed = 3f;

        [Header("Ground")]
        [Tooltip("Layers counted as sand.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("How far above the hull to start the downward probe.")]
        [SerializeField, Min(1f)] private float probeHeight = 40f;

        [Tooltip("How far down to probe.")]
        [SerializeField, Min(1f)] private float probeDistance = 200f;

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

        private float smoothedGroundY;
        private bool groundPrimed;
        private readonly RaycastHit[] probeHits = new RaycastHit[16];

        /// <summary>Current ride height above the sand, metres.</summary>
        public float RideHeight { get; private set; }

        /// <summary>Ride height as 0..1 of the strut. What drag and grip blend on.</summary>
        public float RideHeight01 => maxRideHeight <= 0f ? 0f : Mathf.Clamp01(RideHeight / maxRideHeight);

        /// <summary>World Y of the sand under the craft.</summary>
        public float GroundY { get; private set; }

        /// <summary>Surface normal of the sand under the craft. Up when nothing was hit.</summary>
        public Vector3 GroundNormal { get; private set; } = Vector3.up;

        /// <summary>True once a ground hit has been found; false over a hole in the world.</summary>
        public bool HasGround { get; private set; }

        /// <summary>True when the hull is clear of the sand and the craft is foiling.</summary>
        public bool IsFoiling => RideHeight01 > 0.05f;

        /// <summary>Strut length. The ceiling on ride height.</summary>
        public float MaxRideHeight { get => maxRideHeight; set => maxRideHeight = Mathf.Max(0.1f, value); }

        /// <summary>Speed at which the hull leaves the sand.</summary>
        public float TakeoffSpeed { get => takeoffSpeed; set => takeoffSpeed = Mathf.Max(0.1f, value); }

        /// <summary>
        /// Advance the ride height for this frame and return the world Y the hull should sit at.
        /// </summary>
        /// <param name="speed">Horizontal speed, m/s.</param>
        /// <param name="deltaTime">Frame time.</param>
        public float Tick(float speed, float deltaTime)
        {
            SampleGround(deltaTime);

            float target = FoilPhysics.RideHeight(speed, takeoffSpeed, maxRideHeight);
            RideHeight = Mathf.Lerp(RideHeight, target, 1f - Mathf.Exp(-riseSpeed * deltaTime));

            return smoothedGroundY + RideHeight;
        }

        /// <summary>Deceleration from sand drag at this speed, m/s².</summary>
        public float DragDeceleration(float speed)
        {
            return FoilPhysics.SandDrag(speed, RideHeight01, hullDrag, foilDrag);
        }

        /// <summary>Fraction of sideways velocity the foil removes this frame.</summary>
        public float LateralGrip()
        {
            return FoilPhysics.LateralGrip(RideHeight01, gripSubmerged, gripFlying);
        }

        private void SampleGround(float deltaTime)
        {
            Vector3 origin = transform.position + Vector3.up * probeHeight;

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
            // long as somebody stood under the ray. Under the RAY: this is one probe cast from
            // the hull origin, so it happened standing amidships and nowhere else.
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, probeHits, probeDistance,
                                                groundMask, QueryTriggerInteraction.Ignore);
            bool found = false;
            float bestY = float.NegativeInfinity;
            Vector3 bestNormal = Vector3.up;

            for (int i = 0; i < count; i++)
            {
                Transform hitTransform = probeHits[i].collider.transform;
                if (hitTransform == transform || hitTransform.IsChildOf(transform)) continue;
                // Sand is a static collider. Anything moving under its own physics is cargo or a
                // passenger, and a craft does not ride on those — least of all the ones it is
                // carrying, which is what closes the loop above.
                Rigidbody hitBody = probeHits[i].collider.attachedRigidbody;
                if (hitBody != null && !hitBody.isKinematic) continue;
                if (probeHits[i].point.y > bestY)
                {
                    bestY = probeHits[i].point.y;
                    bestNormal = probeHits[i].normal;
                    found = true;
                }
            }

            if (found)
            {
                GroundY = bestY;
                GroundNormal = bestNormal;
                HasGround = true;
            }
            else if (!groundPrimed)
            {
                // No terrain yet — a chunk still streaming in. Sit where we are rather than
                // dropping the craft through the world.
                GroundY = transform.position.y - RideHeight;
                HasGround = false;
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

        private void OnDrawGizmosSelected()
        {
            Vector3 p = transform.position;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(p, p + Vector3.down * RideHeight);
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);
            Gizmos.DrawWireCube(p + Vector3.down * RideHeight, new Vector3(6f, 0.05f, 18f));
        }
    }
}
