// A fenced pen the settlement keeps animals in. Put it on the pen's prefab root and size the box to
// the floor inside the fence. Settlement.Generate then fills it with a few animals of its stock
// prefab, standing on the walkable ground inside the box. They are placed as ordinary scene
// instances after the residents and from their own seed stream, so a pen never moves who lives where.
//
// Nothing here is runtime state. What keeps the animals in is the pen's fence colliders and its
// gate (a carving NavMeshObstacle on the leaf): while the gate is shut the pen floor is a NavMesh
// island, and the stock's wander only picks points a complete path leads to. Opening the gate
// (DoorInteraction) joins the island to the rest of the settlement, and they may walk out.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class SettlementPen : MonoBehaviour
    {
        /// <summary>A uniformly distributed point on ground that may be inside the pen; the pen rejects the ones that are not.</summary>
        public delegate Vector3 WalkableSampler(ref SettlementPlacementUtil.SeededRng rng);

        [Header("Area")]
        [Tooltip("Centre of the floor inside the fence, in this object's local space.")]
        [SerializeField] private Vector3 areaCenter = new Vector3(0f, 1f, 0f);
        [Tooltip("Size of the box in this object's local space. Keep it inside the fence and tall enough to hold the ground.")]
        [SerializeField] private Vector3 areaSize = new Vector3(8f, 2f, 8f);

        [Header("Stock")]
        [Tooltip("The animal kept here. Wants a bounded wander and a NavMeshAgent; DuneRat_Penned is built for it.")]
        [SerializeField] private GameObject stockPrefab;
        [SerializeField, Min(0)] private int minCount = 4;
        [SerializeField, Min(0)] private int maxCount = 7;
        [Tooltip("Metres kept between two animals at placement.")]
        [SerializeField, Min(0f)] private float stockSpacing = 1f;
        [Tooltip("Tries per animal before it is given up on; every miss is reported by Generate.")]
        [SerializeField, Min(1)] private int placementAttempts = 30;

        [Header("Gate")]
        [Tooltip("The door that lets the stock out. Optional: only drawn, and named in Generate's report when stock could walk out with the gate shut.")]
        [SerializeField] private DoorInteraction gate;

        public GameObject StockPrefab => stockPrefab;
        public DoorInteraction Gate => gate;

        /// <summary>The box's axis-aligned world bounds: every corner of the pen floor lies inside.</summary>
        public Bounds WorldBounds
        {
            get
            {
                Vector3 half = areaSize * 0.5f;
                var bounds = new Bounds(transform.TransformPoint(areaCenter), Vector3.zero);
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? -half.x : half.x,
                        (i & 2) == 0 ? -half.y : half.y,
                        (i & 4) == 0 ? -half.z : half.z);
                    bounds.Encapsulate(transform.TransformPoint(areaCenter + corner));
                }
                return bounds;
            }
        }

        /// <summary>Whether a world point is inside the pen's box.</summary>
        public bool Contains(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint) - areaCenter;
            Vector3 half = areaSize * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>How many animals this pen gets: min..max inclusive, from the seeded stream.</summary>
        public int RollCount(ref SettlementPlacementUtil.SeededRng rng) => minCount + rng.NextIndex(Mathf.Max(minCount, maxCount) - minCount + 1);

        /// <summary>
        /// Where the stock stands: up to <paramref name="wanted"/> points drawn from <paramref name="sample"/>, each inside
        /// the box and at least <c>stockSpacing</c> from the others. Fewer come back when the floor has no room left.
        /// </summary>
        public List<Vector3> PlanStock(ref SettlementPlacementUtil.SeededRng rng, WalkableSampler sample, out int wanted)
        {
            wanted = RollCount(ref rng);
            var points = new List<Vector3>(wanted);
            for (int i = 0; i < wanted; i++)
            {
                for (int attempt = 0; attempt < placementAttempts; attempt++)
                {
                    Vector3 point = sample(ref rng);
                    if (!Contains(point) || Settlement.IsCloserThan(point, points, stockSpacing)) continue;
                    points.Add(point);
                    break;
                }
            }
            return points;
        }

        private void OnValidate()
        {
            maxCount = Mathf.Max(minCount, maxCount);
            areaSize = Vector3.Max(areaSize, Vector3.zero);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(areaCenter, areaSize);
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.15f);
            Gizmos.DrawCube(areaCenter, areaSize);

            if (gate == null) return;
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.TransformPoint(areaCenter), gate.transform.position);
        }
    }
}
