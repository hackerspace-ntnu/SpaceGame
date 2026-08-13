using UnityEngine;

namespace SpaceGame.Vehicles.DuneFoil
{
    /// <summary>
    /// The wind. One of these in the scene decides which way it blows and how hard, and
    /// everything that cares — the sails, the cloth shader, anything added later — reads it
    /// from here rather than keeping its own idea.
    ///
    /// Deliberately not a physics system: the wind is a design surface, not a simulation. It
    /// has a direction you can set and read in the inspector while the game runs, a slow drift
    /// so it is never quite constant, and gusts, because a perfectly steady wind makes sailing
    /// a solved problem after the first ten seconds.
    /// </summary>
    // Runs in edit mode too, so a rig in the scene trims itself to the wind while you are
    // authoring it instead of hanging dead until play is pressed.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]   // settle the wind before anything reads it this frame
    public class WindField : MonoBehaviour
    {
        /// <summary>
        /// The wind the scene is using. First one to wake up wins; a scene should only have one.
        /// </summary>
        public static WindField Active { get; private set; }

        [Header("Base wind")]
        [Tooltip("Compass bearing the wind blows FROM, in degrees. 0 = from world north (+Z), " +
                 "90 = from the east (+X). Named the way a forecast is, because that is how " +
                 "anyone sailing will think about it.")]
        [SerializeField, Range(0f, 360f)] private float bearingFrom = 225f;

        [Tooltip("Base wind speed in m/s. 5 is a light breeze, 12 a good sailing wind, 20 heavy.")]
        [SerializeField, Min(0f)] private float baseSpeed = 12f;

        [Header("Drift")]
        [Tooltip("How far the bearing wanders either side of its base, in degrees.")]
        [SerializeField, Min(0f)] private float driftAmplitude = 18f;
        [Tooltip("Seconds for roughly one wander. Long: a windshift should be worth noticing.")]
        [SerializeField, Min(1f)] private float driftPeriod = 45f;

        [Header("Gusts")]
        [Tooltip("Peak extra speed in a gust, m/s.")]
        [SerializeField, Min(0f)] private float gustStrength = 5f;
        [Tooltip("How quickly gusts come and go.")]
        [SerializeField, Min(0.01f)] private float gustFrequency = 0.25f;
        [Tooltip("Size of a gust cell in metres. Larger means the whole craft is gusted at once.")]
        [SerializeField, Min(1f)] private float gustCellSize = 120f;

        [Header("Debug")]
        [SerializeField] private bool drawGizmo = true;

        private float noiseOrigin;

        /// <summary>Unit vector the wind blows TOWARD, horizontal.</summary>
        public Vector3 Direction { get; private set; } = Vector3.forward;

        /// <summary>Unit vector the wind blows FROM. What a sailor points at.</summary>
        public Vector3 DirectionFrom => -Direction;

        /// <summary>Base speed including drift and the global gust term, m/s.</summary>
        public float Speed { get; private set; }

        /// <summary>Direction times speed, at the field's nominal point.</summary>
        public Vector3 Velocity => Direction * Speed;

        /// <summary>Current bearing the wind blows from, degrees, including drift.</summary>
        public float BearingFrom { get; private set; }

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                Debug.LogWarning($"[WindField] {name} is a second wind field; {Active.name} " +
                                 "already owns the scene wind. Disabling this one.", this);
                enabled = false;
                return;
            }
            Active = this;
            noiseOrigin = Random.value * 1000f;
            Recalculate(0f);
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        private void Update() => Recalculate(Time.time);

        private void Recalculate(float time)
        {
            // Perlin rather than a sine, so the drift does not have an obvious period the
            // player can learn and sail against.
            float drift = (Mathf.PerlinNoise(noiseOrigin, time / Mathf.Max(driftPeriod, 1f)) - 0.5f)
                          * 2f * driftAmplitude;

            BearingFrom = bearingFrom + drift;

            // Bearing is the direction it comes FROM, so the vector it travels along is the
            // opposite. Unity's Y rotation is clockwise from +Z looking down, which is already
            // how compass bearings run.
            Vector3 from = Quaternion.Euler(0f, BearingFrom, 0f) * Vector3.forward;
            Direction = -from;

            Speed = Mathf.Max(0f, baseSpeed + GustAt(transform.position, time));
        }

        /// <summary>
        /// Wind at a world position. Gusts are spatial, so two craft a few hundred metres apart
        /// are not in the same puff, and a long hull can be gusted unevenly along its length.
        /// </summary>
        public Vector3 SampleAt(Vector3 worldPosition)
        {
            return Direction * Mathf.Max(0f, baseSpeed + GustAt(worldPosition, Time.time));
        }

        /// <summary>Speed at a world position, without building a vector.</summary>
        public float SpeedAt(Vector3 worldPosition)
        {
            return Mathf.Max(0f, baseSpeed + GustAt(worldPosition, Time.time));
        }

        private float GustAt(Vector3 worldPosition, float time)
        {
            if (gustStrength <= 0f) return 0f;

            // The gust field is advected downwind, so gusts arrive from windward the way they
            // should rather than appearing on top of you.
            Vector3 advected = worldPosition - Direction * (baseSpeed * time);
            float u = advected.x / gustCellSize + noiseOrigin;
            float v = advected.z / gustCellSize + time * gustFrequency;

            // biased below zero as often as above, so gusts and lulls both happen
            return (Mathf.PerlinNoise(u, v) - 0.45f) * 2f * gustStrength;
        }

        /// <summary>Set the wind from code — for a weather system, a scripted storm, or a test.</summary>
        public void SetWind(float newBearingFrom, float newBaseSpeed)
        {
            bearingFrom = Mathf.Repeat(newBearingFrom, 360f);
            baseSpeed = Mathf.Max(0f, newBaseSpeed);
            Recalculate(Time.time);
        }

        private void OnValidate()
        {
            if (Application.isPlaying) Recalculate(Time.time);
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmo) return;

            // Recalculate in edit mode so the arrow tracks the inspector before pressing play.
            Vector3 dir = Application.isPlaying
                ? Direction
                : -(Quaternion.Euler(0f, bearingFrom, 0f) * Vector3.forward);
            float speed = Application.isPlaying ? Speed : baseSpeed;

            Vector3 origin = transform.position;
            Vector3 tip = origin + dir * Mathf.Max(speed, 1f);

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(origin, tip);
            Gizmos.DrawSphere(tip, 0.4f);

            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            Gizmos.DrawLine(tip, tip - dir * 2f + right * 1.2f);
            Gizmos.DrawLine(tip, tip - dir * 2f - right * 1.2f);
        }
    }
}
