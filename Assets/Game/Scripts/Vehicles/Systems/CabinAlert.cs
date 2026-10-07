using UnityEngine;

namespace SpaceGame.Vehicles
{
    /// <summary>
    /// The red alert lamps in a cabin: off, or throbbing while something has gone wrong.
    ///
    /// <para>
    /// Presentation only, and local to each machine. Nothing here is replicated and nothing here is
    /// read by gameplay — every machine runs the same pulse for itself off its own clock, which is
    /// what a light being in the same phase on two screens is worth (nothing) against what
    /// replicating it would cost (a message per flash).
    /// </para>
    ///
    /// <para>
    /// The lamps are switched OFF rather than dimmed to zero between flashes. A URP point light at
    /// zero intensity is still a light the renderer culls, sorts and considers for every object in
    /// range, and there are several of them inside a hull that is often on screen; disabling is the
    /// difference between a cost while the alarm sounds and a cost for the rest of the game.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class CabinAlert : MonoBehaviour
    {
        [Tooltip("The lamps to drive. Left empty, every Light under this object is used, which is " +
                 "the normal case — put the lamps under the same parent this sits on.")]
        [SerializeField] private Light[] lamps;

        [Tooltip("Colour the cabin is washed in. Red reads as an alarm in every game ever made; the " +
                 "field exists so a different vehicle can be amber or blue without a second script.")]
        [SerializeField] private Color alertColor = new(1f, 0.14f, 0.1f);

        [Tooltip("Brightest the lamps get at the top of a pulse.")]
        [SerializeField, Min(0f)] private float peakIntensity = 12f;

        [Tooltip("Full flashes per second. Around one is an alarm; much faster reads as a strobe and " +
                 "is genuinely unpleasant to sit inside for half a minute.")]
        [SerializeField, Min(0.05f)] private float pulsesPerSecond = 0.9f;

        [Tooltip("How sharply each pulse snaps on. 1 is a soft sine throb; higher spends more of " +
                 "each cycle dark, which reads as a lamp being switched rather than a glow.")]
        [SerializeField, Min(1f)] private float pulseSharpness = 2.5f;

        [Tooltip("Below this fraction of peak the lamps are switched off outright rather than left " +
                 "on at a brightness nobody can see. See the class note on why that matters.")]
        [SerializeField, Range(0f, 0.5f)] private float cutoff = 0.04f;

        private bool alarming;
        private float phase;

        /// <summary>Is the alarm sounding?</summary>
        public bool IsAlarming => alarming;

        private void Awake()
        {
            ResolveLamps();
            ApplyIntensity(0f);
        }

        /// <summary>
        /// Start or stop the alarm.
        ///
        /// <para>
        /// The phase is reset on each start so the first flash lands the moment the alarm does.
        /// Left running, a pulse that happened to be mid-cycle would begin the emergency with the
        /// cabin dark for most of a second.
        /// </para>
        /// </summary>
        public void SetAlarm(bool on)
        {
            if (alarming == on) return;

            alarming = on;
            phase = 0f;

            if (!on) ApplyIntensity(0f);
        }

        private void Update()
        {
            if (!alarming) return;

            phase += Time.deltaTime * pulsesPerSecond;

            // Sine folded to 0..1, then sharpened. Pow on a normalised value keeps the peak at
            // exactly 1 however sharp it is set, so the brightness stays what peakIntensity says.
            float wave = (Mathf.Sin(phase * Mathf.PI * 2f) + 1f) * 0.5f;

            ApplyIntensity(Mathf.Pow(wave, pulseSharpness));
        }

        private void ApplyIntensity(float normalised)
        {
            if (lamps == null) return;

            bool lit = normalised > cutoff;

            foreach (Light lamp in lamps)
            {
                if (lamp == null) continue;

                lamp.enabled = lit;
                if (!lit) continue;

                lamp.color = alertColor;
                lamp.intensity = peakIntensity * normalised;
            }
        }

        private void ResolveLamps()
        {
            if (lamps != null && lamps.Length > 0) return;

            lamps = GetComponentsInChildren<Light>(includeInactive: true);

            if (lamps.Length == 0)
                Debug.LogWarning($"[CabinAlert] '{name}' has no lamps under it, so the alarm is " +
                                 "invisible.", this);
        }

        private void OnValidate()
        {
            // So the colour and brightness can be judged in the Inspector without entering play
            // mode. Only ever writes while the alarm is off, which is the state the prefab is
            // saved in — a preview that wrote during an alarm would fight Update.
            if (!Application.isPlaying && !alarming) ApplyIntensity(0f);
        }
    }
}
