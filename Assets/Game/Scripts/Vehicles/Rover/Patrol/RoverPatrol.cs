using System.Collections.Generic;
using UnityEngine;
using SpaceGame.World;
using SpaceGame.World.Weather;

namespace SpaceGame.Vehicles
{
    /// <summary>
    /// A rover that drives a slow loop round its settlement, forever, with nobody at the wheel.
    ///
    /// <para>
    /// <b>Nothing is sent and nothing is saved, because where the rover is is a function.</b> Its loop is built from the settlement's
    /// seed and its own scenery id, and where it is on the loop is the shared clock times its speed, so every machine puts it in the
    /// same place with no <c>NetworkObject</c>, no sync and no record: the same reason a sandstorm is a record and a clock, not an
    /// entity. After a load it resumes the loop at whatever the clock says, which no player can tell from a rover that never stopped.
    /// The pose is only ever written by this component (a kinematic body, moved in <c>FixedUpdate</c>).
    /// </para>
    /// <para>
    /// The loop is proposed ring-wise (<see cref="RoverRoute.Candidate"/>) and kept only where the ground is drivable: terrain not
    /// steeper than <see cref="maxSlopeDegrees"/>, with no rock, wall or prop in the way. That probe runs once, on every machine, against
    /// the same chunk's colliders, so the loops agree; a loop that cannot be found logs once and the rover stays parked.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RoverPatrol : MonoBehaviour
    {
        private const int MaxHits = 16;
        private const int MaxOverlaps = 16;
        private const int SamplesPerCheck = 2;

        [Header("Loop")]
        [Tooltip("Metres beyond the settlement's outermost building walls the loop stays between (inner, outer).")]
        [SerializeField] private Vector2 ringMargin = new(30f, 90f);
        [Tooltip("Control points the loop is drawn through.")]
        [SerializeField, Min(RoverRoute.MinControlPoints)] private int controlPoints = 8;
        [Tooltip("Other places tried for a control point whose first choice is undrivable.")]
        [SerializeField, Min(1)] private int alternatives = 6;
        [Tooltip("Whole new loops tried before the rover gives up and stays parked.")]
        [SerializeField, Min(1)] private int loopAttempts = 4;

        [Header("Driving")]
        [Tooltip("Metres per second along the loop.")]
        [SerializeField, Min(0.1f)] private float cruiseSpeed = 2.5f;
        [Tooltip("Seconds the body takes to settle onto a change of ground, so a stone under a wheel does not jerk the hull.")]
        [SerializeField, Min(0.01f)] private float poseSettleSeconds = 0.3f;

        [Header("Ground")]
        [Tooltip("Half the track and half the wheelbase, metres: where the four ground probes sit under the hull.")]
        [SerializeField] private Vector2 wheelFootprint = new(2.2f, 3.2f);
        [Tooltip("Metres above the rover's start the ground probes begin: higher than any hill on the loop.")]
        [SerializeField, Min(1f)] private float probeHeight = 120f;
        [Tooltip("Steepest terrain a loop may cross, degrees.")]
        [SerializeField, Range(1f, 45f)] private float maxSlopeDegrees = 18f;
        [Tooltip("Radius of the clear space a loop point needs round it, metres: the hull and a margin.")]
        [SerializeField, Min(0.5f)] private float clearanceRadius = 4f;

        [Header("Wheels")]
        [Tooltip("One pivot per wheel, at the wheel's centre with the axle along its local X.")]
        [SerializeField] private Transform[] wheels = System.Array.Empty<Transform>();
        [Tooltip("Metres: how far a wheel turns for each metre driven.")]
        [SerializeField, Min(0.05f)] private float wheelRadius = 0.9f;

        private readonly RaycastHit[] hits = new RaycastHit[MaxHits];
        private readonly Collider[] overlaps = new Collider[MaxOverlaps];
        private Quaternion[] wheelRest;
        private Rigidbody body;
        private RoverRoute route;
        private bool gaveUp;
        private double phaseMetres;
        private float direction = 1f;
        private Vector3 settledUp = Vector3.up;
        private float settledY;
        private bool posed;
        private float wheelAngle;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            wheelRest = new Quaternion[wheels.Length];
            for (int i = 0; i < wheels.Length; i++) wheelRest[i] = wheels[i].localRotation;
        }

        private void FixedUpdate()
        {
            if (route == null && !TryBuildRoute()) return;

            double metres = StormClock.Shared * cruiseSpeed * direction + phaseMetres;
            Vector2 flat = route.PointAt((float)(metres % route.Length));
            Vector2 heading = route.HeadingAt((float)(metres % route.Length)) * direction;
            PoseOn(flat, heading, out Vector3 position, out Quaternion rotation);

            body.MovePosition(position);
            body.MoveRotation(rotation);
        }

        private void Update()
        {
            if (route == null) return;

            wheelAngle += cruiseSpeed * Time.deltaTime / wheelRadius * Mathf.Rad2Deg;
            for (int i = 0; i < wheels.Length; i++)
                wheels[i].localRotation = wheelRest[i] * Quaternion.Euler(wheelAngle * direction, 0f, 0f);
        }

        // ── The loop ─────────────────────────────────────────────────────────

        // False while it cannot be built yet (the chunk is still loading) or has been given up on.
        private bool TryBuildRoute()
        {
            if (gaveUp) return false;

            Settlement settlement = GetComponentInParent<Settlement>();
            Transform generated = settlement != null ? settlement.GeneratedRoot : null;
            if (generated == null)
            {
                Debug.LogError($"[Rover] '{name}' is not under a Settlement's generated root, so it has no loop to drive.", this);
                gaveUp = true;
                return false;
            }

            List<Bounds> buildings = SettlementPlaces.BuildingBounds(generated);
            if (buildings.Count == 0) return false;

            Vector2 centre = Vector2.zero;
            foreach (Bounds box in buildings) centre += new Vector2(box.center.x, box.center.z) / buildings.Count;
            float reach = 0f;
            foreach (Bounds box in buildings)
                reach = Mathf.Max(reach, Vector2.Distance(centre, new Vector2(box.center.x, box.center.z)) + Mathf.Max(box.extents.x, box.extents.z));

            int seed = settlement.Seed ^ SceneryId.Of(gameObject);
            for (int attempt = 0; attempt < loopAttempts; attempt++)
            {
                RoverRoute attempted = Propose(seed + attempt * 7919, centre, reach + ringMargin.x, reach + ringMargin.y);
                if (attempted == null || !IsDrivable(attempted)) continue;

                route = attempted;
                var phase = new System.Random(seed);
                phaseMetres = phase.NextDouble() * route.Length;
                direction = phase.NextDouble() < 0.5 ? 1f : -1f;
                return true;
            }

            Debug.LogWarning($"[Rover] '{name}' found no drivable loop {ringMargin.x:0}-{ringMargin.y:0} m out from its settlement " +
                             "(slope, rocks or walls everywhere it looked); it stays parked.", this);
            gaveUp = true;
            return false;
        }

        private RoverRoute Propose(int seed, Vector2 centre, float inner, float outer)
        {
            var points = new List<Vector2>(controlPoints);
            for (int slot = 0; slot < controlPoints; slot++)
                for (int alternative = 0; alternative < alternatives; alternative++)
                {
                    Vector2 candidate = RoverRoute.Candidate(seed, slot, controlPoints, alternative, centre, inner, outer);
                    if (!IsDrivable(candidate)) continue;

                    points.Add(candidate);
                    break;
                }
            return points.Count >= RoverRoute.MinControlPoints ? new RoverRoute(points) : null;
        }

        private bool IsDrivable(RoverRoute candidate)
        {
            IReadOnlyList<Vector2> samples = candidate.Samples;
            for (int i = 0; i < samples.Count; i += SamplesPerCheck)
                if (!IsDrivable(samples[i])) return false;
            return true;
        }

        // Terrain under the point that is not too steep, and nothing but terrain (and other rovers, which move) round it.
        private bool IsDrivable(Vector2 point)
        {
            if (!TryTerrainHeight(point, out float y)) return false;

            float step = clearanceRadius * 0.5f;
            float rise = 0f;
            foreach (Vector2 offset in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
            {
                if (!TryTerrainHeight(point + offset * step, out float around)) return false;
                rise = Mathf.Max(rise, Mathf.Abs(around - y));
            }
            if (Mathf.Atan2(rise, step) * Mathf.Rad2Deg > maxSlopeDegrees) return false;

            // Lifted so a slope within the limit never puts the ground inside the sphere.
            Vector3 centre = new(point.x, y + clearanceRadius * 1.5f, point.y);
            int count = Physics.OverlapSphereNonAlloc(centre, clearanceRadius, overlaps, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (overlaps[i] is not TerrainCollider && overlaps[i].GetComponentInParent<RoverPatrol>() == null) return false;
            return true;
        }

        // ── The pose ─────────────────────────────────────────────────────────

        private void PoseOn(Vector2 flat, Vector2 heading, out Vector3 position, out Quaternion rotation)
        {
            Vector3 forward = new(heading.x, 0f, heading.y);
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 front = Corner(flat, forward * wheelFootprint.y), back = Corner(flat, -forward * wheelFootprint.y);
            Vector3 starboard = Corner(flat, right * wheelFootprint.x), port = Corner(flat, -right * wheelFootprint.x);

            Vector3 up = Vector3.Cross(front - back, starboard - port).normalized;
            if (up.y < 0.1f) up = Vector3.up;
            float y = (front.y + back.y + starboard.y + port.y) * 0.25f;

            // Everything above is a function of the clock, so every machine agrees on it; this only takes the edge off a step in the
            // ground, and a centimetre of disagreement between machines is as far as it can go.
            float blend = posed ? 1f - Mathf.Exp(-Time.fixedDeltaTime / poseSettleSeconds) : 1f;
            posed = true;
            settledUp = Vector3.Slerp(settledUp, up, blend);
            settledY = Mathf.Lerp(settledY, y, blend);

            position = new Vector3(flat.x, settledY, flat.y);
            rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, settledUp), settledUp);
        }

        private Vector3 Corner(Vector2 flat, Vector3 offset)
        {
            Vector2 at = flat + new Vector2(offset.x, offset.z);
            return TryTerrainHeight(at, out float y) ? new Vector3(at.x, y, at.y) : new Vector3(at.x, settledY, at.y);
        }

        // The terrain's own surface under a point: rocks and props between are not ground.
        private bool TryTerrainHeight(Vector2 point, out float y)
        {
            var origin = new Vector3(point.x, transform.position.y + probeHeight, point.y);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, probeHeight * 2f, Physics.AllLayers, QueryTriggerInteraction.Ignore);

            y = 0f;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider is not TerrainCollider || hits[i].distance >= nearest) continue;
                nearest = hits[i].distance;
                y = hits[i].point.y;
            }

            return !float.IsPositiveInfinity(nearest);
        }
    }
}
