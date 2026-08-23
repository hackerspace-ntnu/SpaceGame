using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Drives the scanner's display: turns a list of contacts into the numbers the
    /// <c>SpaceGame/ItemScannerScreen</c> shader draws.
    ///
    /// <para>
    /// Everything goes through a <see cref="MaterialPropertyBlock"/> rather than the material, so
    /// two scanners in a session — one in each of two players' hands, both instantiated from the
    /// same prefab — do not share a display. Writing to <c>renderer.material</c> would clone the
    /// material per instance instead, which works and then leaks one material per equip.
    /// </para>
    /// <para>
    /// Contacts arrive in world space and are resolved here into the scanner's own frame, because
    /// that frame is what the display means: +Y is where the holder is facing, X is across. A
    /// contact behind the holder gets a negative Y and the shader parks it in the rear strip.
    /// </para>
    /// </summary>
    public class ItemScannerScreen : MonoBehaviour
    {
        /// <summary>Must match MAX_BLIPS in the shader.</summary>
        public const int MaxBlips = 24;

        [Header("Wiring")]
        [Tooltip("The screen plate's renderer. Falls back to a Renderer on this object.")]
        [SerializeField] private Renderer screenRenderer;

        [Tooltip("Material index on that renderer, for a screen that shares a mesh with its bezel.")]
        [SerializeField] private int materialIndex;

        [Header("Beam")]
        [Tooltip("Seconds for one left-to-right pass of the sweep. Also the rate at which a " +
                 "contact's flare is refreshed, since the two are the same event.")]
        [SerializeField] private float sweepPeriod = 1.6f;

        [Tooltip("Seconds a contact keeps glowing after the last scan that saw it. Longer than " +
                 "the scan interval on purpose: a target flickering in and out of a wall should " +
                 "fade, not blink.")]
        [SerializeField] private float contactFade = 1.1f;

        [Header("Tube")]
        [Tooltip("Seconds the display takes to warm up or collapse when switched.")]
        [SerializeField] private float warmupTime = 0.55f;

        private static readonly int BlipsId = Shader.PropertyToID("_Blips");
        private static readonly int BlipCountId = Shader.PropertyToID("_BlipCount");
        private static readonly int SweepId = Shader.PropertyToID("_Sweep");
        private static readonly int PowerId = Shader.PropertyToID("_Power");
        private static readonly int NearestId = Shader.PropertyToID("_Nearest");
        private static readonly int ContactsId = Shader.PropertyToID("_Contacts");
        private static readonly int RangeId = Shader.PropertyToID("_RangeM");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");

        private readonly Vector4[] blips = new Vector4[MaxBlips];
        private readonly float[] seenAt = new float[MaxBlips];

        private MaterialPropertyBlock block;
        private float power;
        private float sweep;
        private int liveBlips;
        private int totalContacts;
        private float nearest;
        private float range = 100f;
        private bool on;

        /// <summary>Sweep phase 0..1, so the artifact can time its ping to the beam.</summary>
        public float Sweep => sweep;

        /// <summary>How lit the tube is, 0 dark to 1 warm. Drives the item's own glow.</summary>
        public float Power => power;

        private void Awake()
        {
            if (screenRenderer == null) screenRenderer = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
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
                float x = Vector3.Dot(offset, right) / range;
                float y = Vector3.Dot(offset, forward) / range;

                blips[i] = new Vector4(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(y, -1f, 1f),
                                       1f, (float)contacts[i].Class);
                seenAt[i] = now;
            }
            for (int i = liveBlips; i < MaxBlips; i++) blips[i] = Vector4.zero;
        }

        private void LateUpdate()
        {
            float target = on ? 1f : 0f;
            power = warmupTime <= 0f
                ? target
                : Mathf.MoveTowards(power, target, Time.deltaTime / warmupTime);

            if (power > 0.001f && sweepPeriod > 0f)
                sweep = Mathf.Repeat(sweep + Time.deltaTime / sweepPeriod, 1f);

            // Fade each contact from the moment it was last seen, rather than clearing the array
            // between scans. A blip that vanishes on the frame the scan misses it makes the
            // display twitch at the scan rate; one that decays reads as a real return.
            float now = Time.time;
            for (int i = 0; i < liveBlips; i++)
            {
                float age = now - seenAt[i];
                float strength = contactFade <= 0f ? 1f : Mathf.Clamp01(1f - age / contactFade);
                blips[i].z = strength;
            }

            Push();
        }

        private void Push()
        {
            if (screenRenderer == null || block == null) return;

            screenRenderer.GetPropertyBlock(block, materialIndex);
            block.SetVectorArray(BlipsId, blips);
            block.SetFloat(BlipCountId, liveBlips);
            block.SetFloat(SweepId, sweep);
            block.SetFloat(PowerId, power);
            block.SetFloat(NearestId, Mathf.Round(Mathf.Min(nearest, 999f)));
            block.SetFloat(ContactsId, Mathf.Min(totalContacts, 99));
            block.SetFloat(RangeId, Mathf.Round(Mathf.Min(range, 999f)));
            block.SetFloat(AspectId, AspectOf(screenRenderer));
            screenRenderer.SetPropertyBlock(block, materialIndex);
        }

        /// <summary>
        /// Width over height of the plate, measured from its own mesh.
        ///
        /// Measured rather than serialised because the shader draws circles: an aspect that
        /// disagrees with the mesh turns every range ring into an ellipse, and that is exactly the
        /// kind of wrongness nobody notices in the inspector and everybody notices on the screen.
        /// </summary>
        private static float AspectOf(Renderer r)
        {
            Bounds b = r.localBounds;
            Vector3 e = b.size;
            // The plate is thin on one axis; the other two are the display.
            float min = Mathf.Min(e.x, Mathf.Min(e.y, e.z));
            float w, h;
            if (Mathf.Approximately(min, e.z)) { w = e.x; h = e.y; }
            else if (Mathf.Approximately(min, e.y)) { w = e.x; h = e.z; }
            else { w = e.z; h = e.y; }
            return h > 1e-5f ? Mathf.Clamp(w / h, 0.25f, 4f) : 1f;
        }
    }
}
