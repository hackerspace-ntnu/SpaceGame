// Decides when the weather turns, and where a storm walks in from.
//
// Server-only, and deliberately simple: a weighted roll on an interval. The interesting design
// lives in the profiles; this only has to make storms arrive at a rate that keeps the desert
// tense without making it unplayable. Nothing here is replicated — the server's decision becomes
// one StormInstance record, and that is what crosses the network.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World.Weather
{
    [DisallowMultipleComponent]
    public class SandstormDirector : MonoBehaviour
    {
        [Serializable]
        public struct WeightedProfile
        {
            public SandstormProfile profile;

            [Tooltip("Relative chance of being picked. Zero never fires.")]
            [Min(0f)] public float weight;
        }

        [Header("What can blow in")]
        [SerializeField] private List<WeightedProfile> profiles = new List<WeightedProfile>();

        [Header("How often")]
        [Tooltip("Seconds before the first storm of the session.")]
        [SerializeField] private Vector2 firstDelayRange = new Vector2(180f, 420f);

        [Tooltip("Seconds of clear weather between storms, measured from the end of the last one.")]
        [SerializeField] private Vector2 intervalRange = new Vector2(300f, 900f);

        [Header("Where")]
        [Tooltip("Centre of the area storms cross, in world XZ.")]
        [SerializeField] private Vector2 worldCenter = Vector2.zero;

        [Tooltip("Size of that area in metres. The default matches this project's 4000 x 3000 " +
                 "streaming grid, so a storm crosses the whole playable world.")]
        [SerializeField] private Vector2 worldSize = new Vector2(4000f, 3000f);

        [Tooltip("Bearing storms usually travel toward: 0 is +Z, 90 is +X. Gives the desert a " +
                 "prevailing weather direction the player can learn.")]
        [SerializeField, Range(0f, 360f)] private float prevailingBearing = 45f;

        [Tooltip("Degrees either side of the prevailing bearing an individual storm may vary.")]
        [SerializeField, Range(0f, 180f)] private float bearingSpread = 50f;

        [Tooltip("Give a travelling storm exactly enough life to cross the area and leave. Off, " +
                 "the profile's own duration is used, which for a moving wall usually means it " +
                 "expires somewhere over the map or wanders off it and keeps going.")]
        [SerializeField] private bool matchDurationToCrossing = true;

        [Header("Debug")]
        [Tooltip("Draw the area storms cross, and the prevailing bearing, in the scene view.")]
        [SerializeField] private bool drawGizmos = true;

        private float countdown;
        private int activeStormId;

        /// <summary>
        /// True once a save has stated the schedule. Stops <see cref="OnEnable"/> re-rolling it.
        ///
        /// <para>
        /// The order of the two is genuinely undecidable: a global saver registers in its own
        /// <c>OnEnable</c> and <c>SaveManager</c> applies staged state on the spot, so the restore
        /// can land either side of this component waking up. A latch is the only version that gives
        /// the same answer both ways round.
        /// </para>
        /// </summary>
        private bool scheduleRestored;

        /// <summary>Seconds of clear weather left before the next storm is rolled.</summary>
        public float Countdown => countdown;

        /// <summary>The storm this director started and is waiting out, or 0 when the sky is clear.</summary>
        public int ActiveStormId => activeStormId;

        private void OnEnable()
        {
            if (scheduleRestored) return;

            countdown = UnityEngine.Random.Range(firstDelayRange.x, firstDelayRange.y);
        }

        /// <summary>
        /// Restore-only. Puts the weather schedule back where the save left it. Called by the save
        /// system; do not call from gameplay.
        ///
        /// <para>
        /// Without this, loading a world re-rolled <c>firstDelayRange</c> and reset
        /// <see cref="activeStormId"/> to zero: the whole schedule restarted, so a player who saved
        /// twenty minutes into a clear stretch got another three-to-seven-minute wait, and a player
        /// who saved with a storm overhead had the director immediately conclude nothing was running
        /// and start counting toward a second one.
        /// </para>
        /// </summary>
        public void RestoreSchedule(float countdown, int activeStormId)
        {
            this.countdown = countdown;
            this.activeStormId = activeStormId;
            scheduleRestored = true;
        }

        private void Update()
        {
            SandstormManager manager = SandstormManager.Instance;
            if (manager == null || !manager.HasAuthority)
                return;

            // The interval means clear weather BETWEEN storms, so the clock does not run while one
            // is overhead. Otherwise a long storm eats its own gap and the next arrives instantly.
            if (IsStormStillRunning(manager))
                return;

            countdown -= Time.deltaTime;
            if (countdown > 0f)
                return;

            countdown = UnityEngine.Random.Range(intervalRange.x, intervalRange.y);
            SpawnOne(manager);
        }

        private bool IsStormStillRunning(SandstormManager manager)
        {
            if (activeStormId == 0)
                return false;

            var storms = manager.Resolved;
            for (int i = 0; i < storms.Count; i++)
            {
                if (storms[i].Id == activeStormId)
                    return true;
            }

            activeStormId = 0;
            return false;
        }

        /// <summary>Rolls a storm and starts it now. Exposed so a debug key or a quest can force one.</summary>
        [ContextMenu("Spawn A Storm Now")]
        public void SpawnNow()
        {
            SandstormManager manager = SandstormManager.Instance;
            if (manager != null && manager.HasAuthority)
                SpawnOne(manager);
        }

        private void SpawnOne(SandstormManager manager)
        {
            SandstormProfile profile = PickProfile();
            if (profile == null)
                return;

            float bearing = prevailingBearing + UnityEngine.Random.Range(-bearingSpread, bearingSpread);
            float sideways = UnityEngine.Random.Range(-1f, 1f);

            Vector2 origin = EntryOrigin(worldCenter, worldSize, bearing, profile, sideways);
            float duration = matchDurationToCrossing
                ? CrossingDuration(worldSize, profile)
                : -1f;

            if (manager.TrySpawn(profile, new Vector3(origin.x, profile.baseHeight, origin.y),
                                 bearing, out int id, duration))
            {
                activeStormId = id;
            }
        }

        private SandstormProfile PickProfile()
        {
            float total = 0f;
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i].profile != null)
                    total += profiles[i].weight;
            }

            if (total <= 0f)
                return null;

            float roll = UnityEngine.Random.Range(0f, total);
            for (int i = 0; i < profiles.Count; i++)
            {
                if (profiles[i].profile == null)
                    continue;

                roll -= profiles[i].weight;
                if (roll <= 0f)
                    return profiles[i].profile;
            }

            return null;
        }

        /// <summary>
        /// Where a storm starts so that it walks onto the map rather than appearing on top of the
        /// player. Static and pure so the placement can be checked in a test rather than by
        /// waiting fifteen minutes for the weather to turn.
        /// </summary>
        /// <param name="sideways">-1 to 1: how far off the centre line the storm enters.</param>
        public static Vector2 EntryOrigin(Vector2 center, Vector2 size, float bearingDegrees,
                                          SandstormProfile profile, float sideways)
        {
            Vector2 heading = StormShape.HeadingFromDegrees(bearingDegrees);
            Vector2 across = new Vector2(-heading.y, heading.x);

            // Far enough back that even the storm's feathered leading edge is clear of the area
            // before it starts moving — otherwise the first thing the player sees is a storm
            // fading up around them.
            float reach = EntryDistance(size, profile);
            float lateral = sideways * 0.5f * Mathf.Max(size.x, size.y);

            return center - heading * reach + across * lateral;
        }

        /// <summary>Seconds for a storm to cross the area and clear the far side.</summary>
        public static float CrossingDuration(Vector2 size, SandstormProfile profile)
        {
            if (profile.travelSpeed <= 0f)
                return profile.duration;

            return 2f * EntryDistance(size, profile) / profile.travelSpeed;
        }

        private static float EntryDistance(Vector2 size, SandstormProfile profile) =>
            0.5f * size.magnitude + profile.radius + profile.edgeFeather;

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
                return;

            Vector3 center = new Vector3(worldCenter.x, 0f, worldCenter.y);
            Gizmos.color = new Color(1f, 0.85f, 0.4f, 0.8f);
            Gizmos.DrawWireCube(center, new Vector3(worldSize.x, 1f, worldSize.y));

            Vector2 heading = StormShape.HeadingFromDegrees(prevailingBearing);
            Vector3 arrow = new Vector3(heading.x, 0f, heading.y) * (0.25f * worldSize.magnitude);
            Gizmos.DrawLine(center - arrow, center + arrow);
        }
    }
}
