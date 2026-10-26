// Thorns bursting out of the ground where the witch's strike lands, then sinking back.
//
// The witch's attack used to be Merob's: lightning out of the sky. That made her a second conjurer
// and muddied whose power was whose, and the story already had the answer — since Merob fell asleep
// "the forest has grown thorns where there were none". So her cast now brings the thorns up under
// the target. The CAST is unchanged (ConjurerCastModule's wind-up, ground warning, aim lock and
// damage); only what is drawn where it lands is this.
//
// Pure presentation, spawned by LightningStrike.Present on every machine. The damage was billed
// on the server over the same radius this covers.
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// A cluster of thorn spikes that erupts across a strike's blast radius, holds, and withdraws.
    ///
    /// <para>
    /// <b>The picture covers the damage, edge to edge.</b> The radius comes from the strike, not
    /// from this prefab, so a spike is drawn wherever a body would have been hurt and nowhere it
    /// would not (GDC-L1-UX-0003) — a thorn standing outside the blast teaches the player the wrong
    /// size of the attack the first time they stand next to one and live.
    /// </para>
    /// <para>
    /// <b>Every machine draws the same thorns.</b> The layout is seeded from the strike point,
    /// which every machine was sent, so two players watching the same strike see the same spikes
    /// without anything about them travelling.
    /// </para>
    /// </summary>
    public sealed class ThornEruption : MonoBehaviour, IStrikeEffect
    {
        [Header("Spikes")]
        [Tooltip("One thorn, modelled 1 m tall with its origin at the base.")]
        [SerializeField] private GameObject spikePrefab;

        [SerializeField, Min(1)] private int spikeCount = 14;

        [Tooltip("Height range of a spike, metres. The tallest grow in the middle of the blast. " +
                 "Taller than a player on purpose: a strike that does not tower over you reads as " +
                 "scenery, not as the thing that just hurt you.")]
        [SerializeField] private Vector2 spikeHeight = new Vector2(1.3f, 2.6f);

        [Tooltip("How much thicker than the model a spike is drawn, at full height.")]
        [SerializeField, Min(0.1f)] private float thickness = 1.1f;

        [Tooltip("Degrees an outer spike leans away from the centre, so the cluster reads as having " +
                 "burst outward from one point.")]
        [SerializeField, Range(0f, 60f)] private float outwardLean = 28f;

        [Tooltip("Fraction of the blast radius the outermost spikes stand at, so their tips — " +
                 "which lean outward — finish near the edge rather than past it.")]
        [SerializeField, Range(0.3f, 1f)] private float spread = 0.85f;

        [Header("Timing")]
        [Tooltip("Seconds to burst up. Short: the strike is a snap, not a growth.")]
        [SerializeField, Min(0.01f)] private float riseSeconds = 0.12f;

        [Tooltip("How far past full height the spikes overshoot as they burst, as a fraction.")]
        [SerializeField, Range(0f, 0.5f)] private float overshoot = 0.18f;

        [SerializeField, Min(0f)] private float holdSeconds = 0.7f;

        [Tooltip("Seconds to sink back into the ground.")]
        [SerializeField, Min(0.01f)] private float sinkSeconds = 0.45f;

        [Header("Ground")]
        [Tooltip("What a spike stands on. Bodies are skipped whatever this says.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("How far above the strike point each spike looks down for the ground, metres.")]
        [SerializeField, Min(0.1f)] private float probeHeight = 3f;

        [Header("Burst")]
        [Tooltip("Earth and leaves thrown up as the thorns break the ground. Optional.")]
        [SerializeField] private ParticleSystem burst;

        private Transform[] spikes;
        private float[] heights;
        private float elapsed;
        private bool struck;

        public void Strike(Vector3 from, Vector3 to, float radius)
        {
            transform.SetPositionAndRotation(to, Quaternion.identity);

            if (burst != null)
            {
                // The earth breaks over the same ground the spikes cover.
                ParticleSystem.ShapeModule shape = burst.shape;
                shape.radius = radius * spread;
                burst.Play(withChildren: true);
            }

            if (spikePrefab == null) return;

            var random = new System.Random(Seed(to));
            spikes = new Transform[spikeCount];
            heights = new float[spikeCount];

            for (int i = 0; i < spikeCount; i++)
            {
                // Square root for an even spread over the disc rather than a crowd at its centre.
                float reach = Mathf.Sqrt((float)random.NextDouble()) * radius * spread;
                float bearing = (float)random.NextDouble() * Mathf.PI * 2f;
                Vector3 outward = new Vector3(Mathf.Cos(bearing), 0f, Mathf.Sin(bearing));
                Vector3 at = Ground(to + outward * reach);

                float centrality = radius > 0f ? 1f - reach / (radius * spread) : 1f;
                heights[i] = Mathf.Lerp(spikeHeight.x, spikeHeight.y, centrality * 0.6f + (float)random.NextDouble() * 0.4f);

                Quaternion lean = Quaternion.AngleAxis(outwardLean * (1f - centrality), Vector3.Cross(Vector3.up, outward));
                Quaternion yaw = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);

                GameObject spike = Instantiate(spikePrefab, at, lean * yaw, transform);
                spike.transform.localScale = Vector3.zero;
                spikes[i] = spike.transform;
            }

            elapsed = 0f;
            struck = true;
        }

        private void Update()
        {
            if (!struck) return;

            elapsed += Time.deltaTime;
            float growth = Growth(elapsed);

            for (int i = 0; i < spikes.Length; i++)
            {
                if (spikes[i] == null) continue;
                float h = heights[i] * growth;
                // Thickness follows height less than one to one, so a sinking spike reads as
                // withdrawing into the ground rather than shrinking in place.
                float w = heights[i] * thickness * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(growth));
                spikes[i].localScale = new Vector3(w, h, w);
            }

            if (elapsed >= riseSeconds + holdSeconds + sinkSeconds) Destroy(gameObject);
        }

        /// <summary>0 → 1 + overshoot → 1 on the burst, 1 through the hold, 1 → 0 on the sink.</summary>
        private float Growth(float t)
        {
            if (t < riseSeconds)
            {
                float u = t / riseSeconds;
                return u * (1f + overshoot * Mathf.Sin(u * Mathf.PI));
            }

            t -= riseSeconds;
            if (t < holdSeconds) return 1f;

            t -= holdSeconds;
            float s = Mathf.Clamp01(t / sinkSeconds);
            return 1f - s * s;
        }

        /// <summary>The ground under <paramref name="point"/>, skipping anything that is a body.</summary>
        private Vector3 Ground(Vector3 point)
        {
            Vector3 top = point + Vector3.up * probeHeight;
            RaycastHit[] hits = Physics.RaycastAll(top, Vector3.down, probeHeight * 2f, groundMask, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            Vector3 ground = point;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.attachedRigidbody != null || hit.distance >= nearest) continue;
                nearest = hit.distance;
                ground = hit.point;
            }

            return ground;
        }

        /// <summary>The same seed on every machine for the same strike point.</summary>
        private static int Seed(Vector3 point) =>
            Mathf.RoundToInt(point.x * 10f) * 73856093 ^ Mathf.RoundToInt(point.z * 10f) * 19349663;
    }
}
