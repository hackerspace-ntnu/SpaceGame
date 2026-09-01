using UnityEngine;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// The hull burning through the atmosphere on its way down: the plasma sheath outside the
    /// canopy, and the light it throws into the cabin.
    ///
    /// <para>
    /// <b>Presentation only, and it needs no netcode at all.</b> It moves no ship, holds no state
    /// anybody reads, and the game would still be correct if it were deleted mid-descent — the same
    /// property <c>ArrivalCutscene</c> is built around. What makes it agree across machines is that
    /// it derives everything from <see cref="SeatedRider.SecondsSinceLaunch"/>, which is a
    /// replicated instant on the server's clock. Every machine computes the same burn for the same
    /// hull from the same number, so nothing about the fire is ever sent.
    /// </para>
    ///
    /// <para>
    /// <b>It lives on the HULL, not on the camera.</b> The cutscene is per-machine and knows only
    /// about the local player's own ship; a versus match launches one hull per team and every one of
    /// them is on fire. Driving this from the ship means a rival team's ship burns on your screen
    /// too, with no second code path and nothing extra on the wire.
    /// </para>
    ///
    /// <para>
    /// It holds nothing worth saving. A loaded world never re-crashes (<c>ArrivalSaveable</c>), and
    /// a hull restored from a save reports <see cref="SeatedRider.SecondsSinceLaunch"/> of -1, which
    /// is already "dark" — a wreck cannot come out of a save still alight.
    /// </para>
    /// </summary>
    // After SeatedRider (100) and in LateUpdate for the same reason it is: the descent teleports the
    // hull from a coroutine that resumes between Update and LateUpdate, so a glow posed in Update
    // sits a frame behind the ship it belongs to.
    [DefaultExecutionOrder(150)]
    [DisallowMultipleComponent]
    public class EntryBurn : MonoBehaviour
    {
        [Tooltip("The shell that draws the plasma: an ellipsoid enclosing the hull, drawn on its " +
                 "back faces. Saved DISABLED — a parked ship is not on fire.")]
        [SerializeField] private Renderer shell;

        [Tooltip("The wash the fire throws into the cabin, from in front of the crew so it reads " +
                 "as coming in through the canopy. Saved disabled, and switched rather than dimmed " +
                 "to zero: a URP light at zero intensity is still a light the renderer sorts and " +
                 "considers for every object in range.")]
        [SerializeField] private Light glow;

        [Tooltip("Where the descent's progress is read from. On this same hull — it is the one " +
                 "thing that knows when the formation launched, on a clock every machine shares.")]
        [SerializeField] private SeatedRider rider;

        [Tooltip("How long the descent takes. Read from the local ArrivalDirector when there is " +
                 "one, because that component is the authority and exists on every machine; this " +
                 "value is the fallback for a hull dropped into a test scene on its own.")]
        [SerializeField, Min(0.1f)] private float descentDuration = 26f;

        [SerializeField] private EntryBurnCurve curve = new()
        {
            Ignite = 0.03f,
            Full = 0.16f,
            Fade = 0.42f,
            Extinguish = 0.70f,
        };

        [Tooltip("Colour the cabin is washed in. Warmer and paler than the plasma's own deep red: " +
                 "this is the light that got through the glass, not the fire itself.")]
        [SerializeField] private Color glowColor = new(1f, 0.52f, 0.22f);

        [Tooltip("Brightest the cabin wash gets, at the peak of the burn.")]
        [SerializeField, Min(0f)] private float peakGlowIntensity = 9f;

        [Tooltip("How far the glow reaches into the cabin. It only has to cross the seats.")]
        [SerializeField, Min(0f)] private float glowRange = 12f;

        [Tooltip("Flickers per second of the whole sheath. Slow enough to read as fire rather than " +
                 "as a fault in the render.")]
        [SerializeField, Min(0.1f)] private float flickerHz = 2.7f;

        [Tooltip("How deep the flicker goes, either side of steady. Capped low on purpose: a crew " +
                 "member sits inside this light for the better part of twenty seconds with no way " +
                 "to look away from it, and a deep flicker at this duration is a strobe.")]
        [SerializeField, Range(0f, 0.5f)] private float flickerDepth = 0.18f;

        [Tooltip("Below this fraction of full burn the shell and the lamp are switched off outright " +
                 "rather than drawn at a brightness nobody can see.")]
        [SerializeField, Range(0f, 0.2f)] private float cutoff = 0.02f;

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int FlickerId = Shader.PropertyToID("_Flicker");

        // Built in Awake rather than initialised inline: a MaterialPropertyBlock constructed
        // outside a live engine throws, and a static field initialiser runs wherever the class is
        // first touched — including an edit-mode test that never enters play mode.
        private MaterialPropertyBlock block;

        /// <summary>
        /// True once the sheath has lit and gone out again, so the component can stop working. The
        /// wreck stands in the world for the rest of the session and has nothing left to draw.
        /// </summary>
        private bool burnedOut;

        /// <summary>How hard the sheath is burning right now, 0..1. For tests and for the Inspector.</summary>
        public float Burn { get; private set; }

        private void Awake()
        {
            block = new MaterialPropertyBlock();

            if (shell == null)
                Debug.LogWarning($"[EntryBurn] '{name}' has no plasma shell, so the entry burn is " +
                                 "invisible from outside the window.", this);

            Extinguish();
        }

        private void LateUpdate()
        {
            if (burnedOut) return;

            float since = rider != null ? rider.SecondsSinceLaunch : -1f;

            // -1 is "this hull has not launched", which covers a parked ship, a wreck restored from
            // a save, and the seconds between spawning at the top of the arc and the crew being
            // strapped in. None of them are on fire.
            if (since < 0f)
            {
                Extinguish();
                return;
            }

            float duration = ArrivalDirector.Instance != null
                ? ArrivalDirector.Instance.DescentDuration
                : descentDuration;

            Burn = curve.Intensity(since / Mathf.Max(0.1f, duration));

            if (Burn <= cutoff)
            {
                // Lit once and now out: the descent is past the burn and there is nothing else
                // coming. Everything after this frame would be a curve evaluation returning zero.
                if (Extinguish()) burnedOut = true;
                return;
            }

            float flicker = EntryBurnCurve.Flicker(Time.time, flickerHz, flickerDepth);

            if (shell != null)
            {
                shell.enabled = true;
                shell.GetPropertyBlock(block);
                block.SetFloat(IntensityId, Burn);
                block.SetFloat(FlickerId, flicker);
                shell.SetPropertyBlock(block);
            }

            if (glow != null)
            {
                glow.enabled = true;
                glow.color = glowColor;
                glow.range = glowRange;
                glow.intensity = peakGlowIntensity * Burn * flicker;
            }
        }

        /// <summary>
        /// Put the fire out. Returns whether it had been alight, which is how
        /// <see cref="LateUpdate"/> tells "the burn just finished" from "it has not started".
        /// </summary>
        private bool Extinguish()
        {
            bool wasAlight = Burn > 0f || (shell != null && shell.enabled) || (glow != null && glow.enabled);

            Burn = 0f;
            if (shell != null) shell.enabled = false;
            if (glow != null) glow.enabled = false;

            return wasAlight;
        }
    }
}
