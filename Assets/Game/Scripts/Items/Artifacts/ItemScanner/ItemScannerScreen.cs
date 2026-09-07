using System.Collections.Generic;
using TMPro;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// Drives the scanner's display: turns a list of contacts into the plot and the readouts on
    /// the world-space canvas laid over the screen plate.
    ///
    /// <para>
    /// The display is built the way the standing terminal's is — a millimetre-unit world-space
    /// canvas standing a fraction of a millimetre off the glass, with the plate's own emissive
    /// green showing through behind it — and for the same reason: it is text and vector geometry,
    /// so it stays crisp however close the wearer brings their arm, and it needs no render texture
    /// and no second camera. <see cref="ScannerRadar"/> draws the rings, the beam and the contacts;
    /// this class decides what they mean. The canvas is built onto the prefab by
    /// <c>ItemScannerScreenBuilder</c>, never at runtime.
    /// </para>
    /// <para>
    /// Contacts arrive in world space and are resolved here into the scanner's own frame, because
    /// that frame is what the display means: +Y is where the holder is facing, X is across. A
    /// contact behind the holder plots below the centre of the disc.
    /// </para>
    /// </summary>
    public class ItemScannerScreen : MonoBehaviour
    {
        /// <summary>Most contacts the plot will draw at once.</summary>
        public const int MaxBlips = 24;

        [Header("Wiring")]
        [Tooltip("The display canvas. Switched off outright while the set is dark, so an unpowered " +
                 "scanner costs nothing to draw.")]
        [SerializeField] private Canvas canvas;

        [Tooltip("Fades the whole display as the tube warms up and collapses.")]
        [SerializeField] private CanvasGroup group;

        [Tooltip("The plot: rings, beam and contacts.")]
        [SerializeField] private ScannerRadar radar;

        [Tooltip("Top line — what the set is and how far it reaches.")]
        [SerializeField] private TextMeshProUGUI headerText;

        [Tooltip("The nearest contact's distance. The one number worth reading at a glance.")]
        [SerializeField] private TextMeshProUGUI nearestText;

        [Tooltip("How many contacts the scan found, including any past the plot's limit.")]
        [SerializeField] private TextMeshProUGUI countText;

        [Tooltip("The screen plate's renderer, so the glass behind the canvas dims with the tube.")]
        [SerializeField] private Renderer screenRenderer;

        [Tooltip("Which material slot on that renderer is the display face.")]
        [SerializeField] private int materialIndex;

        [Header("Beam")]
        [Tooltip("Seconds for one turn of the sweep. Also how long a contact takes to be refreshed, " +
                 "since the two are the same event.")]
        [SerializeField] private float sweepPeriod = 2.4f;

        [Tooltip("Seconds a contact keeps glowing after the last scan that saw it. Longer than " +
                 "the scan interval on purpose: a target flickering in and out of a wall should " +
                 "fade, not blink.")]
        [SerializeField] private float contactFade = 1.1f;

        [Tooltip("How lit a contact stays between passes of the beam, as a share of its full glow.")]
        [SerializeField, Range(0f, 1f)] private float restingGlow = 0.35f;

        [Header("Tube")]
        [Tooltip("Seconds the display takes to warm up or collapse when switched.")]
        [SerializeField] private float warmupTime = 0.55f;

        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private readonly List<ScannerRadar.Blip> plot = new();
        private readonly Vector2[] plotted = new Vector2[MaxBlips];
        private readonly ScanClass[] classes = new ScanClass[MaxBlips];
        private readonly float[] seenAt = new float[MaxBlips];

        private MaterialPropertyBlock block;
        private Color glassEmission;
        private bool glassEmits;
        private float canvasWidthScale = 1f;

        private float power;
        private float sweep;
        private int liveBlips;
        private int totalContacts;
        private float nearest;
        private float range = 50f;
        private bool on;

        /// <summary>Sweep phase 0..1, so the artifact can time its ping to the beam.</summary>
        public float Sweep => sweep;

        /// <summary>How lit the tube is, 0 dark to 1 warm. Drives the item's own glow.</summary>
        public float Power => power;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (canvas != null) canvasWidthScale = Mathf.Abs(canvas.transform.localScale.x);

            // The plate's authored emission, so the glass can be dimmed with the tube and put back
            // exactly as the material has it. Read once: it is a property of the model, not of
            // this instance, and the instance only ever scales it.
            Material glass = screenRenderer != null ? Slot(screenRenderer) : null;
            glassEmits = glass != null && glass.HasProperty(EmissionId);
            if (glassEmits) glassEmission = glass.GetColor(EmissionId);

            Push();
        }

        /// <summary>Switch the tube on or off. The warm-up is animated, not instant.</summary>
        public void SetOn(bool value) => on = value;

        /// <summary>Dark immediately — for unequip, where nothing should linger on a destroyed item.</summary>
        public void Blackout()
        {
            on = false;
            power = 0f;
            liveBlips = 0;
            totalContacts = 0;
            nearest = 0f;
            if (radar != null) radar.Clear();
            Push();
        }

        /// <summary>
        /// Hand the display the result of one scan.
        ///
        /// <paramref name="forward"/> and <paramref name="right"/> are the horizontal frame the
        /// contacts are read against, and must be unit length and perpendicular.
        /// </summary>
        public void Report(List<ScanContact> contacts, int totalFound, Vector3 origin,
                           Vector3 forward, Vector3 right, float scanRange)
        {
            range = Mathf.Max(1f, scanRange);
            totalContacts = totalFound;
            liveBlips = Mathf.Min(contacts.Count, MaxBlips);
            nearest = contacts.Count > 0 ? contacts[0].Distance : 0f;

            float now = Time.time;
            for (int i = 0; i < liveBlips; i++)
            {
                Vector3 offset = contacts[i].Position - origin;

                // Flattened deliberately. The display is a plan view: a crate on a roof twenty
                // metres up is at the same place on it as one at your feet, which is what a player
                // reading a map expects. Height goes unshown rather than shown wrongly.
                plotted[i] = new Vector2(Vector3.Dot(offset, right) / range,
                                         Vector3.Dot(offset, forward) / range);
                classes[i] = contacts[i].Class;
                seenAt[i] = now;
            }
        }

        private void LateUpdate()
        {
            float target = on ? 1f : 0f;
            power = warmupTime <= 0f
                ? target
                : Mathf.MoveTowards(power, target, Time.deltaTime / warmupTime);

            if (power > 0.001f && sweepPeriod > 0f)
                sweep = Mathf.Repeat(sweep + Time.deltaTime / sweepPeriod, 1f);

            Push();
        }

        /// <summary>Everything the display shows, from the last scan and the beam's phase.</summary>
        private void Push()
        {
            KeepReadable();
            if (canvas != null) canvas.enabled = power > 0.001f;
            if (group != null) group.alpha = power;

            if (glassEmits && screenRenderer != null)
            {
                screenRenderer.GetPropertyBlock(block, materialIndex);
                block.SetColor(EmissionId, glassEmission * Mathf.Max(power, 0.06f));
                screenRenderer.SetPropertyBlock(block, materialIndex);
            }

            if (power <= 0.001f) return;

            if (radar != null)
            {
                BuildPlot();
                radar.Present(plot, sweep);
            }

            if (headerText != null) headerText.text = $"SCAN  {Mathf.Round(Mathf.Min(range, 999f)):0}M";
            if (nearestText != null)
                nearestText.text = liveBlips > 0 ? $"{Mathf.Round(Mathf.Min(nearest, 999f)):0}M" : "--";
            if (countText != null)
                countText.text = totalContacts == 1 ? "1 CONTACT" : $"{Mathf.Min(totalContacts, 99)} CONTACTS";
        }

        /// <summary>
        /// The contact list the plot draws, each with the brightness it has earned.
        ///
        /// <para>
        /// Two decays multiply. One is the time since the last scan that SAW the contact: a blip
        /// that vanishes on the frame a scan misses it makes the display twitch at the scan rate,
        /// where one that fades reads as a real return. The other is the time since the BEAM last
        /// crossed its bearing, which is what makes the plot look swept rather than lit — a
        /// contact flares as the beam reaches it and settles back to <see cref="restingGlow"/>.
        /// </para>
        /// </summary>
        private void BuildPlot()
        {
            plot.Clear();
            float now = Time.time;

            for (int i = 0; i < liveBlips; i++)
            {
                float age = now - seenAt[i];
                float seen = contactFade <= 0f ? 1f : Mathf.Clamp01(1f - age / contactFade);
                if (seen <= 0.01f) continue;

                // Turns clockwise from forward, the same frame the beam is drawn in.
                float bearing = Mathf.Repeat(Mathf.Atan2(plotted[i].x, plotted[i].y) / (2f * Mathf.PI), 1f);
                float sinceSwept = Mathf.Repeat(sweep - bearing, 1f);
                float swept = Mathf.Lerp(restingGlow, 1f, 1f - sinceSwept);

                plot.Add(new ScannerRadar.Blip(plotted[i], seen * swept, classes[i]));
            }
        }

        /// <summary>
        /// Cancels a mirrored seating, so the words on the glass read forwards on both arms.
        ///
        /// <para>
        /// <c>ForearmSeat</c> puts a gauntlet on the LEFT arm by giving it a negative X scale
        /// rather than by mirroring the model, because the cuff's buckles have a side. Everything
        /// the device draws survives that; text does not — a reflected canvas is a canvas read in
        /// a mirror. So the canvas takes the reflection back out of its own scale, which leaves
        /// the plate mirrored (as intended) and the display the right way round (as required).
        /// </para>
        /// <para>
        /// Checked every frame rather than on equip: the seating is applied by another component,
        /// and reading a sign is cheaper than agreeing with it about ordering.
        /// </para>
        /// </summary>
        private void KeepReadable()
        {
            if (canvas == null) return;

            // The PARENT's frame, deliberately: the canvas's own matrix already carries the
            // correction, so reading that would flip the sign back every frame.
            Transform seat = canvas.transform.parent;
            bool mirrored = seat != null && seat.localToWorldMatrix.determinant < 0f;
            float want = mirrored ? -canvasWidthScale : canvasWidthScale;
            Vector3 scale = canvas.transform.localScale;
            if (Mathf.Approximately(scale.x, want)) return;

            scale.x = want;
            canvas.transform.localScale = scale;
        }

        /// <summary>The shared material in the display's own slot, or null if the slot is empty.</summary>
        private Material Slot(Renderer r)
        {
            Material[] materials = r.sharedMaterials;
            return materialIndex >= 0 && materialIndex < materials.Length ? materials[materialIndex] : null;
        }
    }
}
