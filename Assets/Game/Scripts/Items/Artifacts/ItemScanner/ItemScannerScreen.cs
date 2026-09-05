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
        private static readonly int FlipXId = Shader.PropertyToID("_FlipX");

        private readonly Vector4[] blips = new Vector4[MaxBlips];
        private readonly float[] seenAt = new float[MaxBlips];

        private MaterialPropertyBlock block;
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

            ScreenFrame frame = FrameOf(screenRenderer);

            screenRenderer.GetPropertyBlock(block, materialIndex);
            block.SetVectorArray(BlipsId, blips);
            block.SetFloat(BlipCountId, liveBlips);
            block.SetFloat(SweepId, sweep);
            block.SetFloat(PowerId, power);
            block.SetFloat(NearestId, Mathf.Round(Mathf.Min(nearest, 999f)));
            block.SetFloat(ContactsId, Mathf.Min(totalContacts, 99));
            block.SetFloat(RangeId, Mathf.Round(Mathf.Min(range, 999f)));
            block.SetFloat(AspectId, frame.Aspect);

            // Only when the plate's own UVs answered it. A plate without usable UVs leaves the
            // material's authored _FlipX alone rather than overriding it with a guess.
            if (frame.Measured) block.SetFloat(FlipXId, frame.Mirrored ? 1f : 0f);

            screenRenderer.SetPropertyBlock(block, materialIndex);
        }

        /// <summary>What the shader needs to know about the plate it is drawing on.</summary>
        private readonly struct ScreenFrame
        {
            public readonly float Aspect;
            public readonly bool Mirrored;
            public readonly bool Measured;

            public ScreenFrame(float aspect, bool mirrored, bool measured)
            {
                Aspect = aspect;
                Mirrored = mirrored;
                Measured = measured;
            }
        }

        /// <summary>
        /// The plate's shape and handedness, resolved through the renderer's own transform.
        ///
        /// <para>
        /// <b>Aspect.</b> The shader draws circles, so an aspect that disagrees with the plate
        /// turns every range ring into an ellipse — the kind of wrongness nobody notices in the
        /// inspector and everybody notices on the screen. It is measured through the UVs rather
        /// than off the bounding box, because the box only answers the question for a plate lying
        /// square to its own axes, and the screen is raked back toward the wearer with that rake
        /// baked into the mesh. The honest measurement is how far the surface travels per unit of
        /// u against per unit of v, which no orientation can disturb.
        /// </para>
        /// <para>
        /// <b>Handedness.</b> A viewer facing the plate reads u as right and v as up, so the cross
        /// product of the two must point back out at them along the surface normal. Where it does
        /// not, the plate is mirrored and every contact draws on the wrong side — which is what the
        /// material's <c>_FlipX</c> exists to undo, and which was until now a value somebody had to
        /// guess from the model's handedness and then confirm in play.
        /// </para>
        /// <para>
        /// Both are taken in world space, through <see cref="Transform.localToWorldMatrix"/>. The
        /// model library ships object transforms unbaked, so the screen plate carries its seating
        /// on the arm as node rotation and node scale — and a non-uniform scale there is invisible
        /// to the mesh while stretching every ring on the display by however much it happens to be.
        /// </para>
        /// </summary>
        private static ScreenFrame FrameOf(Renderer r)
        {
            var filter = r.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return new ScreenFrame(BoxAspectOf(r), false, false);

            if (!frames.TryGetValue(mesh, out UvFrame uv))
            {
                uv = MeasureUvFrame(mesh);
                frames[mesh] = uv;
            }

            if (!uv.Valid) return new ScreenFrame(BoxAspectOf(r), false, false);

            Matrix4x4 m = r.transform.localToWorldMatrix;
            Vector3 gradU = m.MultiplyVector(uv.GradU);
            Vector3 gradV = m.MultiplyVector(uv.GradV);
            Vector3 normal = m.MultiplyVector(uv.Normal);

            float height = gradV.magnitude;
            float aspect = height > 1e-5f
                ? Mathf.Clamp(gradU.magnitude / height, 0.25f, 4f)
                : 1f;

            bool mirrored = Vector3.Dot(Vector3.Cross(gradU, gradV), normal) < 0f;
            return new ScreenFrame(aspect, mirrored, true);
        }

        /// <summary>The plate's UV frame, in its mesh's own space.</summary>
        private readonly struct UvFrame
        {
            public readonly Vector3 GradU;
            public readonly Vector3 GradV;
            public readonly Vector3 Normal;
            public readonly bool Valid;

            public UvFrame(Vector3 gradU, Vector3 gradV, Vector3 normal)
            {
                GradU = gradU;
                GradV = gradV;
                Normal = normal;
                Valid = true;
            }
        }

        /// <summary>
        /// One entry per screen MESH, not per screen: two scanners share the plate they were built
        /// from, and the gradients are a property of that plate. The transform is applied per call
        /// instead, because that part is not shared — it is where the seating and the scale live.
        /// </summary>
        private static readonly Dictionary<Mesh, UvFrame> frames = new();

        private static UvFrame MeasureUvFrame(Mesh mesh)
        {
            Vector2[] uv = mesh.uv;
            Vector3[] verts = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] tris = mesh.triangles;
            if (uv == null || uv.Length != verts.Length || tris.Length < 3) return default;

            // The largest triangle in UV space: the display face, and the one least distorted by
            // the arithmetic below. A bezel or a back face is smaller in u,v than the screen is.
            int best = -1;
            float bestArea = 0f;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector2 a = uv[tris[i]], b = uv[tris[i + 1]], c = uv[tris[i + 2]];
                float area = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y));
                if (area <= bestArea) continue;
                bestArea = area;
                best = i;
            }

            if (best < 0 || bestArea < 1e-8f) return default;

            Vector2 uvA = uv[tris[best]], uvB = uv[tris[best + 1]], uvC = uv[tris[best + 2]];
            Vector3 pA = verts[tris[best]], pB = verts[tris[best + 1]], pC = verts[tris[best + 2]];

            // Solve p = pA + u * U + v * V for the two gradients: the metres the surface covers per
            // unit of u against per unit of v.
            Vector2 dUv1 = uvB - uvA, dUv2 = uvC - uvA;
            Vector3 dP1 = pB - pA, dP2 = pC - pA;
            float det = dUv1.x * dUv2.y - dUv2.x * dUv1.y;
            if (Mathf.Abs(det) < 1e-8f) return default;

            Vector3 gradU = (dP1 * dUv2.y - dP2 * dUv1.y) / det;
            Vector3 gradV = (dP2 * dUv1.x - dP1 * dUv2.x) / det;

            // Handedness comes from the shaded normals rather than the winding: the winding only
            // says which way the triangle faces once the renderer's cull mode is also known, and
            // the plate's authored normals are what the lighting already agrees with.
            Vector3 normal = normals != null && normals.Length == verts.Length
                ? normals[tris[best]] + normals[tris[best + 1]] + normals[tris[best + 2]]
                : Vector3.Cross(pB - pA, pC - pA);

            if (normal.sqrMagnitude < 1e-10f) return default;

            return new UvFrame(gradU, gradV, normal.normalized);
        }

        /// <summary>The old bounding-box guess, for a plate that ships without usable UVs.</summary>
        private static float BoxAspectOf(Renderer r)
        {
            // Scaled, because localBounds is the mesh's own box while the plate wears its seating
            // on the arm as node scale.
            Vector3 e = Vector3.Scale(r.localBounds.size, r.transform.lossyScale);
            e = new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z));

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
