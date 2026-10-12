using SpaceGame.Gameplay;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    /// <summary>
    /// A gentle plate from the sand up to the foot of a boarding ramp, sized to wherever the hull
    /// happens to have come to rest.
    ///
    /// <para>
    /// A ramp's foot is laid on the hull's own ground plane, but a hull is set down on the HIGHEST
    /// ground it spans (<c>ShipGrounding</c>), so on real terrain the sand at a ramp's foot is
    /// usually lower than that plane. The foot then ends in a lip above the sand, and a player
    /// capsule with no step offset cannot walk over a lip: it has to jump. The ramp itself cannot
    /// simply be pushed into the sand, because the ramps are part of the hull's body and a buried
    /// ramp props a 60-tonne ship off its skirts (PlayerShip.md, Gotchas).
    /// </para>
    /// <para>
    /// So the plate is its OWN kinematic body: it carries the player and cannot carry the hull. A
    /// kinematic body still shoves any dynamic one it touches with infinite mass, and the plate
    /// joins the ramp it continues, so it is told to ignore every collider of its own hull each
    /// time it comes out.
    /// Every machine fits it from the sand under the foot — terrain is the same everywhere, so
    /// nothing is sent — while the ramp is deployed, and switches it off otherwise.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class RampApron : MonoBehaviour
    {
        [Tooltip("The ramp this plate continues: its walking box, whose local Z runs along the slope.")]
        [SerializeField] private BoxCollider ramp;

        [Tooltip("The part whose open pose deploys the ramp. Empty for a ramp that is always out.")]
        [SerializeField] private ArticulatedPart deployedBy;

        [Tooltip("The plate's slope, in degrees. Gentler than any ramp, so the sand-to-ramp step " +
                 "is the easiest part of the climb.")]
        [SerializeField, Range(5f, 30f)] private float slopeDegrees = 20f;

        [Tooltip("A drop smaller than this needs no plate, in metres.")]
        [SerializeField, Min(0f)] private float minimumDrop = 0.02f;

        [Tooltip("A drop larger than this is not a ramp's business (a cliff edge), in metres.")]
        [SerializeField, Min(0.1f)] private float maximumDrop = 2.5f;

        [Tooltip("Seconds between refits while deployed: the ground under a parked hull streams " +
                 "in, and a hull can be driven off and parked again.")]
        [SerializeField, Min(0.05f)] private float refitSeconds = 0.5f;

        [Tooltip("The plate's thickness, in metres. It hangs below the walking line.")]
        [SerializeField, Min(0.01f)] private float thickness = 0.1f;

        // Rounds of the landing solve. Each halves the error on any slope gentler than the plate's.
        private const int LandingRounds = 4;

        // How far above the foot the sand is probed from, in metres: enough to find sand that has
        // risen above the foot, which is a buried foot and needs no plate.
        private const float ProbeLift = 0.5f;

        private BoxCollider plate;
        private float refitTimer;
        private bool hullIgnored;

        private void Awake()
        {
            plate = GetComponent<BoxCollider>();
            var body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            plate.enabled = false;
        }

        private void Update()
        {
            if (ramp == null || plate == null) return;

            bool deployed = deployedBy == null || (deployedBy.IsOpen && !deployedBy.IsMoving);
            if (!deployed)
            {
                Hide();
                refitTimer = 0f;
                return;
            }

            refitTimer -= Time.deltaTime;
            if (refitTimer > 0f) return;
            refitTimer = refitSeconds;

            Fit();
        }

        /// <summary>Size the plate from the ramp's foot down to the sand, or switch it off.</summary>
        public void Fit()
        {
            if (!TryFoot(out Vector3 foot, out Vector3 outward, out Vector3 side, out float width) ||
                !TryLanding(foot, outward, side, width, out float drop))
            {
                Hide();
                return;
            }

            float slope = slopeDegrees * Mathf.Deg2Rad;
            float length = drop / Mathf.Sin(slope);
            Vector3 along = (outward * Mathf.Cos(slope) - Vector3.up * Mathf.Sin(slope)).normalized;
            Vector3 normal = Vector3.Cross(along, side).normalized;
            if (normal.y < 0f) normal = -normal;

            // The top face runs from the ramp's foot edge to the sand; the slab hangs below it, so
            // the join with the ramp's own top face is a single line.
            transform.SetPositionAndRotation(foot + along * (length * 0.5f) - normal * (thickness * 0.5f),
                                             Quaternion.LookRotation(along, normal));
            Vector3 scale = transform.lossyScale;
            plate.center = Vector3.zero;
            plate.size = new Vector3(width / Mathf.Max(1e-4f, scale.x), thickness / Mathf.Max(1e-4f, scale.y),
                                     length / Mathf.Max(1e-4f, scale.z));
            plate.enabled = true;
            if (!hullIgnored) IgnoreHull();
        }

        // A collider switched off forgets what it was told to ignore, so it is told again on the
        // way back out.
        private void Hide()
        {
            plate.enabled = false;
            hullIgnored = false;
        }

        /// <summary>
        /// Gathered when the plate comes out rather than once: parts are fitted to a hull and taken
        /// off it at runtime, and a collider the plate was never told about is one it pushes.
        /// </summary>
        private void IgnoreHull()
        {
            foreach (Collider c in transform.root.GetComponentsInChildren<Collider>(true))
                if (c != plate) Physics.IgnoreCollision(plate, c, true);
            hullIgnored = true;
        }

        /// <summary>
        /// How far the plate drops before its far edge meets the sand. The sand is read where the
        /// plate LANDS, not under the foot — on a dune that falls away the two differ, and a plate
        /// sized from the foot ends in the very lip it exists to remove. The landing moves with the
        /// drop, so it is solved by a few rounds of "measure, move, measure". Across the width the
        /// LOWEST sand wins: higher sand then covers the plate's edge instead of standing below it.
        /// </summary>
        private bool TryLanding(Vector3 foot, Vector3 outward, Vector3 side, float width, out float drop)
        {
            drop = 0f;
            float run = 0f;
            for (int round = 0; round < LandingRounds; round++)
            {
                Vector3 edge = foot + outward * run;
                float sandY = float.PositiveInfinity;
                for (int s = -1; s <= 1; s++)
                {
                    if (!TrySand(edge + side * (s * width * 0.5f), foot.y, out float y)) return false;
                    sandY = Mathf.Min(sandY, y);
                }

                drop = foot.y - sandY;
                if (drop < minimumDrop || drop > maximumDrop) return false;
                run = drop / Mathf.Tan(slopeDegrees * Mathf.Deg2Rad);
            }
            return true;
        }

        /// <summary>
        /// The middle of the ramp's top edge at its LOWER end, the flat direction out of the ship at
        /// that end, the ramp's side axis and its width.
        /// </summary>
        private bool TryFoot(out Vector3 foot, out Vector3 outward, out Vector3 side, out float width)
        {
            Transform t = ramp.transform;
            Vector3 top = ramp.center + Vector3.up * (ramp.size.y * 0.5f);
            Vector3 a = t.TransformPoint(top + Vector3.forward * (ramp.size.z * 0.5f));
            Vector3 b = t.TransformPoint(top - Vector3.forward * (ramp.size.z * 0.5f));

            foot = a.y < b.y ? a : b;
            Vector3 head = a.y < b.y ? b : a;

            outward = foot - head;
            outward.y = 0f;
            side = t.right;
            width = Mathf.Abs(ramp.size.x * t.lossyScale.x);

            if (outward.sqrMagnitude < 1e-6f) return false;
            outward.Normalize();
            return true;
        }

        /// <summary>
        /// The sand at a point of the plate's edge: the world's static collision, never this hull nor anybody standing
        /// on the sand — a player waiting at the foot is not the ground. A miss (ground not streamed
        /// in yet) fits nothing.
        /// </summary>
        private bool TrySand(Vector3 at, float footY, out float sandY) =>
            ShipGrounding.TryResolveCollisionGround(new Vector2(at.x, at.z), footY + ProbeLift,
                                                    maximumDrop + ProbeLift * 2f, transform.root.gameObject,
                                                    out sandY);
    }
}
