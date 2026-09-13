using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The thrower's own view of the throw they are winding up: the arc, and the mouth at the end
    /// of it.
    ///
    /// <para>
    /// <b>Why this had to exist.</b> The wind-up's whole job is to tell the player how far this
    /// throw will go, and the twirling loop was carrying that on its own — it opens from
    /// <c>coilRadius</c> to <c>openRadius</c> as the charge builds. That works for everyone
    /// watching and for nobody throwing. This is a first-person game whose eye is bolted to the
    /// player root at 1.45 m (see <c>PlayerLook</c> and the note in <c>Backpack.md</c>), and the
    /// loop is spun at <c>twirlHeight</c> — 2.1 m on the same root, which is <b>0.65 m straight
    /// above the camera</b>. The one gauge the item had was out of frame for the only person who
    /// needed to read it, and the parts of it that were not were skimming the near plane.
    /// </para>
    /// <para>
    /// So the loop stays where it belongs — over the head, where it reads as a lasso to everyone
    /// else — and the thrower gets told the same thing in the place they are actually looking
    /// (<c>GDC-L1-UX-0003</c>: the interface answers the player's question at a glance, and
    /// <c>GDC-L1-FEEL-0004</c>: the feedback that matters is the feedback aimed at the person
    /// making the decision). It is drawn in the world rather than on the HUD because the question
    /// is a spatial one — *will it reach that animal* — and a number on the visor cannot answer it.
    /// </para>
    ///
    /// <para>
    /// <b>Owner-only, and never networked.</b> This is one player's aim, drawn from the charge
    /// their own machine is accumulating. It is a plain local <c>GameObject</c> for the reason
    /// every projectile and rope in this project is: only what <c>GameServices.World.Spawn</c> is
    /// handed belongs in the network prefab list.
    /// </para>
    ///
    /// <para>
    /// One polyline does both jobs — the arc sampled from <see cref="LassoThrow"/>, then a ring
    /// walked around the point it lands on. Two renderers would draw the same information in two
    /// styles and cost a second material for it.
    /// </para>
    /// </summary>
    [System.Serializable]
    public class LassoAim
    {
        [Tooltip("Samples along the arc. This is a guide, not a rope — it does not need enough " +
                 "points to look like one.")]
        [SerializeField, Range(4, 40)] private int arcSamples = 18;

        [Tooltip("Segments in the ring drawn at the landing point.")]
        [SerializeField, Range(6, 40)] private int ringSegments = 20;

        [SerializeField] private float width = 0.035f;

        [Tooltip("Drawn where the throw would land. Kept equal to the loop's own mouth by the " +
                 "artifact, so the ring the player is placing on an animal is the circle that " +
                 "will actually decide whether they caught it.")]
        [SerializeField] private float minRingRadius = 0.22f;

        [Tooltip("Metres the ring floats above whatever it landed on, so it is not fighting the " +
                 "ground for the same pixels.")]
        [SerializeField] private float groundOffset = 0.05f;

        [Tooltip("Cold — a flick, thrown the instant it was pressed.")]
        [SerializeField] private Color weakColor = new Color(0.85f, 0.78f, 0.6f, 0.35f);

        [Tooltip("Fully wound.")]
        [SerializeField] private Color strongColor = new Color(1f, 0.86f, 0.42f, 0.85f);

        [Tooltip("Drawn in place of the two above when the arc is stopped by geometry short of " +
                 "where the player is pointing.")]
        [SerializeField] private Color blockedColor = new Color(0.9f, 0.35f, 0.3f, 0.7f);

        [Tooltip("The guide's material. Left empty a plain unlit one is built at runtime, which " +
                 "draws something visible rather than nothing.")]
        [SerializeField] private Material material;

        private GameObject host;
        private LineRenderer line;
        private Material runtimeMaterial;

        /// <summary>Whether the guide is on screen right now.</summary>
        public bool IsVisible => line != null && line.enabled;

        /// <summary>
        /// Draw the arc this charge would throw.
        ///
        /// <para>
        /// The arc is sampled from the same <see cref="LassoThrow.SolveVelocity"/> the throw itself
        /// is built from, rather than from a second approximation of it. A preview that came from
        /// its own maths would be a promise the item does not keep — and the whole reason this
        /// exists is that the item was already breaking one.
        /// </para>
        /// </summary>
        public void Draw(Vector3 start, Vector3 target, float gravity, float apex, float maxSpeed,
                         float charge, float ringRadius, bool blocked)
        {
            EnsureLine();
            if (line == null) return;

            Vector3 velocity = LassoThrow.SolveVelocity(start, target, gravity, apex, maxSpeed,
                                                        out float flightTime);

            int arc = Mathf.Max(4, arcSamples);
            int ring = Mathf.Max(6, ringSegments);
            int count = arc + ring + 1;

            if (line.positionCount != count) line.positionCount = count;

            for (int i = 0; i < arc; i++)
            {
                float t = i / (float)(arc - 1) * flightTime;
                line.SetPosition(i, LassoThrow.PointAt(start, velocity, gravity, t));
            }

            // The ring is drawn flat about the world up rather than about the surface normal. The
            // landing point is resolved from an aim ray that may have hit nothing at all, in which
            // case there is no normal to be had, and a ring that changed orientation depending on
            // whether the player happened to be pointing at something would read as a bug.
            Vector3 centre = target + Vector3.up * groundOffset;
            float radius = Mathf.Max(ringRadius, minRingRadius);

            for (int i = 0; i <= ring; i++)
            {
                float theta = i / (float)ring * Mathf.PI * 2f;
                line.SetPosition(arc + i,
                    centre + new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta)) * radius);
            }

            Color color = blocked ? blockedColor : Color.Lerp(weakColor, strongColor, Mathf.Clamp01(charge));
            line.startColor = color;
            line.endColor = color;
            line.enabled = true;
        }

        public void Hide()
        {
            if (line != null) line.enabled = false;
        }

        /// <summary>
        /// Take the guide down for good. Called from the item's own teardown, because this owns a
        /// GameObject and a possibly-runtime material and neither is parented to anything that
        /// would take them with it.
        /// </summary>
        public void Dispose()
        {
            if (host != null) Object.Destroy(host);
            if (runtimeMaterial != null) Object.Destroy(runtimeMaterial);

            host = null;
            line = null;
            runtimeMaterial = null;
        }

        private void EnsureLine()
        {
            if (line != null) return;

            host = new GameObject("Lasso Aim Guide");
            line = host.AddComponent<LineRenderer>();

            line.useWorldSpace = true;
            line.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            line.widthMultiplier = width;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.textureMode = LineTextureMode.Tile;
            line.alignment = LineAlignment.View;

            // A guide is drawn for one player's benefit and is not part of the world. Casting a
            // shadow from it would put a second, wrong lasso on the sand for everybody else.
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            line.material = material != null ? material : BuildFallbackMaterial();
            line.enabled = false;
        }

        /// <summary>
        /// A plain unlit material for when the prefab has none.
        ///
        /// <para>
        /// Deliberately a fallback rather than the normal path. <c>Shader.Find</c> only reaches
        /// shaders a build actually included, so an authored material on the prefab is the one that
        /// is guaranteed to be there — this is what stops a missing reference turning into an
        /// invisible aim guide with a clean console.
        /// </para>
        /// </summary>
        private Material BuildFallbackMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;

            runtimeMaterial = new Material(shader) { name = "Lasso Aim (runtime)" };
            return runtimeMaterial;
        }
    }
}
