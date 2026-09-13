using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The little lander behind the glass: a miniature of the hull, an orthographic lens orbiting
    /// it, and a render texture the SHIP page draws.
    ///
    /// <para>
    /// This is the 3D half and it owns nothing about the UI — no labels, no cursor, no page. The
    /// UI half is <see cref="ShipSchematicView"/>, which drives the orbit and asks this what the
    /// cursor is over. Split because they fail differently: framing maths is arithmetic, and this
    /// is scene plumbing that can only be wrong at runtime.
    /// </para>
    /// <para>
    /// <b>The miniature is invisible to every camera but this one.</b> Its renderers rest DISABLED
    /// and are switched on for the length of this camera's render only, off the render pipeline's
    /// own begin/end callbacks — <c>PlayerLook</c>'s technique for hiding a player's head from
    /// their own eye. That is what lets a full-size drawing of the ship sit inside the cabinet
    /// without anyone in the room seeing it, and it costs no culling-mask edits on cameras this
    /// system does not own. The <c>Schematic</c> layer does the other direction: this lens renders
    /// that layer and nothing else, so the cockpit around the miniature never turns up in the shot.
    /// </para>
    /// <para>
    /// Presentation only. Which modules are fitted is <see cref="ShipPartRack"/>'s replicated
    /// business and arrives in a <see cref="TelemetrySnapshot"/>; the framing, the cursor and the
    /// pinned module are local to whoever is reading, exactly like the terminal's freed cursor.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipSchematicStage : MonoBehaviour
    {
        /// <summary>Nothing is picked. Never treated as a socket index.</summary>
        public const int NoPart = -1;

        [Header("Wiring")]
        [Tooltip("The baked miniature. Built by Tools ▸ SpaceGame ▸ Build Ship Schematic Prefab.")]
        [SerializeField] private GameObject miniaturePrefab;

        [Tooltip("Draws the miniature's faces — the dark card the lines sit on, and the depth that " +
                 "hides the ones behind it. SpaceGame/SchematicHull.")]
        [SerializeField] private Shader schematicShader;

        [Tooltip("Draws the miniature's feature edges. SpaceGame/SchematicWire.")]
        [SerializeField] private Shader wireShader;

        [Header("Render")]
        [Tooltip("Width of the render texture in pixels. Its height follows the viewport's aspect.")]
        [SerializeField, Min(64)] private int resolution = 640;

        [Tooltip("The dark the hull is drawn on. The tube's own ink, so the viewport reads as part of the glass.")]
        [SerializeField] private Color background = new(0.02f, 0.075f, 0.045f, 1f);

        [Tooltip("How close a crew member must be for the display to bother rendering, metres. " +
                 "A terminal nobody is near costs nothing.")]
        [SerializeField, Min(1f)] private float readableDistance = 6f;

        [Header("Phosphor")]
        [SerializeField] private Color fitted = new(0.42f, 1f, 0.6f);
        [SerializeField] private Color missing = new(1f, 0.27f, 0.21f);
        [SerializeField] private Color picked = new(0.85f, 1f, 0.9f);

        [Tooltip("Seconds per pulse on the modules that are not aboard. The only thing on the " +
                 "glass that moves, because it is the only thing the crew can act on.")]
        [SerializeField, Min(0.2f)] private float pulsePeriod = 1.6f;

        [Header("Picking")]
        [Tooltip("How far OUTSIDE a module's outline the cursor may sit and still pick it, in " +
                 "fractions of the viewport's HALF-height. Only used when the cursor misses every " +
                 "module outright — the modules are small enough that demanding an exact hit made " +
                 "picking one a pixel hunt.")]
        [SerializeField, Min(0f)] private float pickMargin = 0.14f;

        private ShipSchematicModel model;

        /// <summary>
        /// Every renderer of the miniature, flattened at build time. The visibility hook below runs
        /// twice for every frame this display is up and walks all of them; going through the
        /// model's iterator there would allocate an enumerator on each pass, which is a hundred and
        /// fifty renderers' worth of garbage a frame for a green picture on a screen.
        /// </summary>
        private Renderer[] renderers = System.Array.Empty<Renderer>();

        /// <summary>The hull half of <see cref="renderers"/>, for the same reason.</summary>
        private Renderer[] hullRenderers = System.Array.Empty<Renderer>();

        private Camera lens;
        private RenderTexture target;
        private Material material;
        private Material wireMaterial;
        private MaterialPropertyBlock block;

        /// <summary>Socket index in the ship's rack for each of the miniature's parts, or <see cref="NoPart"/>.</summary>
        private int[] socketOf;

        /// <summary>
        /// Each part's box in model space, measured once. A part that names no socket on this hull
        /// keeps an empty box, which is what makes it unpickable without a second test in the loop.
        /// </summary>
        private Bounds[] partBoxes = System.Array.Empty<Bounds>();

        private float aspect = 1.6f;
        private bool rendering;
        private bool unbuildable;
        private int installedMask;
        private int hovered = NoPart;
        private int selected = NoPart;

        public bool Ready => model != null && lens != null;

        /// <summary>What the page's viewport draws. Null until the stage has been asked to render.</summary>
        public Texture Texture => target;

        /// <summary>The orbit is here so the view can drive it and the lens can read it without a third owner.</summary>
        public ShipSchematicOrbit Orbit { get; } = new();

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void OnDisable() => SetRendering(false);

        private void OnDestroy()
        {
            SetRendering(false);
            Release();

            // Made at runtime and owned by nobody else; without this every terminal that streams
            // in leaves materials behind for the length of the session.
            if (material != null) Destroy(material);
            if (wireMaterial != null) Destroy(wireMaterial);
        }

        /// <summary>
        /// Start or stop drawing. The SHIP page calls this as it is shown and hidden; the miniature
        /// itself is built once and kept, because rebuilding a ship model to flip a tab is not a
        /// trade anyone wants.
        /// </summary>
        public void SetRendering(bool value)
        {
            if (value && !Build()) return;
            if (rendering == value) return;

            rendering = value;
            if (lens != null) lens.enabled = value;

            if (value)
            {
                RenderPipelineManager.beginCameraRendering += Show;
                RenderPipelineManager.endCameraRendering += Hide;
            }
            else
            {
                RenderPipelineManager.beginCameraRendering -= Show;
                RenderPipelineManager.endCameraRendering -= Hide;
                SetVisible(false);
            }
        }

        /// <summary>The shape of the hole on the glass. Re-sizes the render texture when it changes.</summary>
        public void SetViewport(float viewportAspect)
        {
            viewportAspect = Mathf.Clamp(viewportAspect, 0.2f, 6f);
            if (Mathf.Approximately(aspect, viewportAspect) && target != null) return;

            aspect = viewportAspect;
            Release();
            if (rendering) Build();
        }

        /// <summary>Is anybody close enough to read this? Checked by the view a few times a second.</summary>
        public bool WithinReadingDistance()
        {
            Transform reader = GameplayMenuScope.LocalPlayerTransform;
            return reader != null &&
                   Vector3.Distance(reader.position, transform.position) <= readableDistance;
        }

        // ── What is fitted ───────────────────────────────────────────────────

        /// <summary>The fitted set, from the reading the page was given. Cheap and idempotent.</summary>
        public void Apply(in TelemetrySnapshot snapshot) => installedMask = snapshot.PartsInstalledMask;

        public void SetHovered(int socketIndex) => hovered = socketIndex;

        public void SetSelected(int socketIndex) => selected = socketIndex;

        private bool IsInstalled(int socketIndex) => ShipPartInfo.IsInstalled(installedMask, socketIndex);

        // ── Pointing at it ───────────────────────────────────────────────────

        /// <summary>
        /// Which module is under a point on the viewport, in 0..1 from its bottom left, or
        /// <see cref="NoPart"/>.
        ///
        /// <para>
        /// Against the modules' own boxes rather than through the physics scene. Colliders would
        /// have to live somewhere, and a set of eleven invisible boxes the size of a lander sitting
        /// inside the cockpit is a thing every OverlapBox in the game would then have to know about.
        /// A box test costs eleven ray-slab intersections and cannot leak out of the schematic.
        /// </para>
        /// <para>
        /// The arithmetic itself is <see cref="ShipSchematicPick"/>, which is pure and tested; this
        /// only turns the module it names into a socket on the ship's rack.
        /// </para>
        /// </summary>
        public int Raycast(Vector2 uv)
        {
            if (!Ready) return NoPart;

            int part = ShipSchematicPick.At(Orbit, uv, aspect, partBoxes, pickMargin);
            return part != ShipSchematicPick.Nothing ? socketOf[part] : NoPart;
        }

        // ── The frame ────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (!rendering || !Ready) return;

            Orbit.Step(Time.unscaledDeltaTime);
            Orbit.Lens(out Vector3 position, out Quaternion rotation);

            lens.transform.localPosition = position;
            lens.transform.localRotation = rotation;
            lens.orthographicSize = Orbit.Size;

            Paint();
        }

        /// <summary>
        /// One property block per renderer, every frame the display is up. Cheap — a lander is
        /// thirty-odd renderers — and it keeps the pulse on the missing modules alive without a
        /// second update path deciding when a repaint is due.
        /// </summary>
        private void Paint()
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / pulsePeriod);
            bool anyPicked = selected != NoPart;

            for (int i = 0; i < socketOf.Length; i++)
            {
                Renderer r = model.Parts[i].partRenderer;
                if (r == null) continue;

                int socket = socketOf[i];
                bool installedHere = socket != NoPart && IsInstalled(socket);
                bool isHovered = socket != NoPart && socket == hovered;
                bool isSelected = socket != NoPart && socket == selected;

                // Three steps, and the modules NOT picked stay at full strength on purpose: every
                // one of them is a thing the reader may want to click next, and the pick is read
                // off the pale colour rather than off everything else going dark.
                Color colour = isSelected ? picked : installedHere ? fitted : missing;

                float fill = installedHere ? 0.20f : 0.10f + 0.16f * pulse;
                float wire = installedHere ? 1.4f : 1.9f + 0.9f * pulse;

                if (isHovered) { fill = Mathf.Max(fill, 0.34f); wire = Mathf.Max(wire, 2.4f); }
                if (isSelected) { fill = 0.55f; wire = 3.2f; }

                // The module's lines and its faces are one object to a reader, so they never
                // disagree about colour: a lit face under dark lines reads as two things.
                Tint(r, colour, fill, wire);
                Tint(model.Parts[i].wireRenderer, colour, fill, wire);
            }

            // Only the hull steps back while a module is picked, and only a little — enough for the
            // modules to sit forward of it, not enough to lose the shape they are bolted to.
            float hullFill = anyPicked ? 0.09f : 0.15f;
            float hullWire = anyPicked ? 0.55f : 0.95f;
            for (int i = 0; i < hullRenderers.Length; i++) Tint(hullRenderers[i], fitted, hullFill, hullWire);
        }

        /// <summary>
        /// One reading painted onto one renderer. The same three numbers go to a face renderer and
        /// to a line renderer; each shader takes the two it has a use for.
        /// </summary>
        private void Tint(Renderer r, Color colour, float fill, float wire)
        {
            if (r == null) return;

            r.GetPropertyBlock(block);
            block.SetColor(ColorId, colour);
            block.SetFloat(FillId, fill);
            block.SetFloat(WireId, wire);
            r.SetPropertyBlock(block);
        }

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int WireId = Shader.PropertyToID("_Wire");

        // ── Per-camera visibility ────────────────────────────────────────────

        private void Show(ScriptableRenderContext context, Camera camera)
        {
            if (camera == lens) SetVisible(true);
        }

        private void Hide(ScriptableRenderContext context, Camera camera)
        {
            if (camera == lens) SetVisible(false);
        }

        private void SetVisible(bool value)
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = value;
        }

        // ── Building it ──────────────────────────────────────────────────────

        /// <summary>Makes the miniature, the lens and the texture, once. False when it cannot.</summary>
        private bool Build()
        {
            if (Ready && target != null) return true;

            // The view asks to render every frame it is up, so a stage that cannot be built must
            // complain exactly once rather than once per frame for as long as anyone stands there.
            if (unbuildable) return false;

            if (miniaturePrefab == null || schematicShader == null || wireShader == null)
            {
                unbuildable = true;

                // Name the field. This fires on a prefab built before the field existed, and
                // "something is missing" sends the reader looking at the model rather than at the
                // build step that never ran.
                string absent = miniaturePrefab == null ? "miniaturePrefab"
                              : schematicShader == null ? "schematicShader"
                              : "wireShader";

                Debug.LogError($"{name}: the schematic's '{absent}' is not wired. The prefab predates " +
                               "it — run Tools ▸ SpaceGame ▸ Build Standing Terminal Prefab, then " +
                               "Tools ▸ Vehicles ▸ Build PlayerShip Prefab.", this);
                return false;
            }

            block ??= new MaterialPropertyBlock();

            if (material == null)
            {
                material = new Material(schematicShader) { name = "SchematicHull (runtime)" };
                material.hideFlags = HideFlags.HideAndDontSave;
            }

            if (wireMaterial == null)
            {
                wireMaterial = new Material(wireShader) { name = "SchematicWire (runtime)" };
                wireMaterial.hideFlags = HideFlags.HideAndDontSave;
            }

            int layer = SchematicLayer();

            if (model == null)
            {
                GameObject instance = Instantiate(miniaturePrefab, transform, false);
                instance.name = "Miniature";

                // Planted on the stage's own origin at unit scale, so the miniature's space and
                // the stage's are the same space — which is what lets the orbit hand the lens a
                // LOCAL pose and the cursor a LOCAL ray without a conversion in between.
                instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                model = instance.GetComponent<ShipSchematicModel>();
                if (model == null)
                {
                    unbuildable = true;
                    Debug.LogError($"{name}: '{miniaturePrefab.name}' carries no ShipSchematicModel.", this);
                    Destroy(instance);
                    return false;
                }

                renderers = new List<Renderer>(model.All()).ToArray();

                // The hull's faces and its lines are painted with the same numbers, always, so they
                // are one list here — only the MATERIAL tells them apart.
                var hullBoth = new List<Renderer>(model.Hull);
                hullBoth.AddRange(model.HullWire);
                hullRenderers = hullBoth.ToArray();

                foreach (Renderer r in renderers)
                {
                    r.enabled = false;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.gameObject.layer = layer;
                }

                foreach (Renderer r in model.Faces()) r.sharedMaterial = material;
                foreach (Renderer r in model.Wires()) r.sharedMaterial = wireMaterial;

                ResolveSockets();
            }

            if (lens == null)
            {
                var holder = new GameObject("SchematicLens");
                holder.transform.SetParent(transform, false);
                holder.layer = layer;

                lens = holder.AddComponent<Camera>();
                lens.orthographic = true;
                lens.clearFlags = CameraClearFlags.SolidColor;
                lens.backgroundColor = background;
                lens.cullingMask = 1 << layer;
                lens.nearClipPlane = 0.01f;
                lens.farClipPlane = ShipSchematicOrbit.Standoff * 3f;
                lens.allowHDR = false;
                lens.allowMSAA = false;
                lens.enabled = false;

                // No shadows, no post: the world's colour grade and its weather have nothing to
                // say about a drawing on a cathode ray tube.
                var urp = holder.AddComponent<UniversalAdditionalCameraData>();
                urp.renderShadows = false;
                urp.renderPostProcessing = false;
                urp.requiresColorOption = CameraOverrideOption.Off;
                urp.requiresDepthOption = CameraOverrideOption.Off;

                Orbit.Adopt(model.Bounds, aspect);
            }

            if (target == null)
            {
                int height = Mathf.Max(64, Mathf.RoundToInt(resolution / aspect));
                target = new RenderTexture(resolution, height, 16, RenderTextureFormat.Default)
                {
                    name = "ShipSchematic",
                    filterMode = FilterMode.Bilinear,
                    antiAliasing = 1,
                };
                target.Create();
                lens.targetTexture = target;
            }

            return true;
        }

        private void Release()
        {
            if (lens != null) lens.targetTexture = null;

            if (target != null)
            {
                target.Release();
                Destroy(target);
                target = null;
            }
        }

        /// <summary>
        /// Ties each of the miniature's modules to a bit of the ship's replicated mask, by name.
        /// A module that names no socket on this hull is drawn as hull — a schematic showing a gun
        /// the ship has no mount for would be a lie the player cannot check.
        /// </summary>
        private void ResolveSockets()
        {
            IReadOnlyList<ShipPartSocket> sockets = Sockets();
            socketOf = new int[model.Parts.Count];
            partBoxes = new Bounds[model.Parts.Count];

            var unresolved = new List<string>();
            for (int i = 0; i < socketOf.Length; i++)
            {
                socketOf[i] = NoPart;
                string wanted = model.Parts[i].socketName;

                for (int s = 0; s < (sockets?.Count ?? 0); s++)
                {
                    if (sockets[s] == null || sockets[s].name != wanted) continue;
                    socketOf[i] = s;
                    break;
                }

                if (socketOf[i] == NoPart) unresolved.Add(wanted);
                else partBoxes[i] = model.PartBounds(i);
            }

            if (sockets != null && sockets.Count > 0 && unresolved.Count > 0)
            {
                Debug.LogError($"{name}: the schematic draws {unresolved.Count} module(s) this hull has " +
                               $"no socket for: {string.Join(", ", unresolved)}. Rebuild the schematic " +
                               "and the ship from the same model.", this);
            }
        }

        private IReadOnlyList<ShipPartSocket> Sockets()
        {
            ShipPartRack rack = GetComponentInParent<ShipPartRack>();
            if (rack == null && transform.root != null)
                rack = transform.root.GetComponentInChildren<ShipPartRack>(true);

            return rack != null ? rack.Sockets : null;
        }

        /// <summary>
        /// The layer only this lens renders. Missing, everything ends up on Default and the
        /// schematic frames the cockpit it stands in, which is a confusing picture rather than a
        /// blank one — so it is worth an error.
        /// </summary>
        private int SchematicLayer()
        {
            int layer = LayerMask.NameToLayer(LayerName);
            if (layer >= 0) return layer;

            Debug.LogError($"{name}: no layer named '{LayerName}'. Add it in Project Settings ▸ " +
                           "Tags and Layers; the schematic cannot be isolated without it.", this);
            return 0;
        }

        public const string LayerName = "Schematic";
    }
}
