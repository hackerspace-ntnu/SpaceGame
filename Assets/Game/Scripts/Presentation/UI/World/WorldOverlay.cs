// The screen-space surface that world-anchored labels are drawn on.
//
// Two things needed the same substrate — damage numbers over whatever you just shot, and a name
// over every other player — and both are the same job underneath: take a point in the world, put
// text at it on screen, keep the text a readable size no matter how far away the point is.
//
// It builds itself rather than being placed on PlayerHUD.prefab, for a reason specific to this
// game: the world streams. Chunk scenes load and unload continuously, and a canvas that lived in
// one would blink out of existence whenever the player walked away from where it was authored.
// DontDestroyOnLoad sidesteps that, and it means neither feature can be broken by a prefab that
// somebody forgot to re-save.
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    [DisallowMultipleComponent]
    public class WorldOverlay : MonoBehaviour
    {
        /// <summary>Reference resolution, matched to PlayerHUD so text is the same size on both.</summary>
        private static readonly Vector2 ReferenceResolution = new(1920f, 1080f);

        /// <summary>
        /// Behind PlayerHUD (which sits at 0), so the crosshair, the health readout and every menu
        /// draw over these labels rather than under them. World annotations are the bottom layer of
        /// the UI: they describe the world, and the chrome describes the game.
        /// </summary>
        private const int SortingOrder = -1;

        private static WorldOverlay instance;

        private RectTransform layer;
        private Camera eye;
        private Camera eyeOverride;

        /// <summary>The overlay for this session, or null once the game is shutting down.</summary>
        public static WorldOverlay Instance => instance;

        /// <summary>The rect that labels parent themselves to.</summary>
        public RectTransform Layer => layer;

        /// <summary>
        /// The camera labels are projected through.
        /// <para>
        /// Re-resolved whenever the cached one goes missing or is switched off, because this game
        /// swaps cameras during play — mounting a vehicle activates a third-person camera cloned
        /// from a prefab, and the first-person one goes away with it.
        /// </para>
        /// </summary>
        public Camera Eye
        {
            get
            {
                if (eyeOverride != null && eyeOverride.isActiveAndEnabled) return eyeOverride;
                if (eye == null || !eye.isActiveAndEnabled) eye = Camera.main;
                return eye;
            }
        }

        /// <summary>
        /// Project through this camera instead of whichever one is tagged MainCamera. Null hands
        /// the choice back to <see cref="Eye"/>.
        /// <para>
        /// The auto-resolve is right for the game and wrong for anything that needs a definite
        /// answer — a test, a cutscene, a second viewport — because Camera.main picks the first
        /// enabled camera carrying the tag, and which one that is has no defined order.
        /// </para>
        /// </summary>
        public Camera EyeOverride
        {
            get => eyeOverride;
            set => eyeOverride = value;
        }

        private bool built;

        /// <summary>
        /// Built after the first scene load rather than on demand, so a subsystem never has to
        /// handle "the surface I draw on does not exist yet" on the very frame it has something to
        /// show. Idle it is one empty canvas: nameplates find no other players in a menu, and
        /// damage numbers only appear when something is hit.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() => Create();

        /// <summary>
        /// The overlay, building it if this is the first ask. Idempotent — a second call hands back
        /// the same one rather than a second canvas drawing every label twice.
        /// </summary>
        public static WorldOverlay Create()
        {
            if (instance != null) return instance;

            var go = new GameObject("WorldOverlay");
            if (Application.isPlaying) DontDestroyOnLoad(go);

            WorldOverlay overlay = go.AddComponent<WorldOverlay>();

            // Called explicitly rather than left to Awake, which AddComponent raises in play mode
            // and does not raise outside it. Build is idempotent so the two paths cannot both run.
            overlay.Build();

            return overlay;
        }

        private void Awake() => Build();

        private void Build()
        {
            if (built) return;

            if (instance != null && instance != this)
            {
                // DestroyImmediate outside play mode: plain Destroy is a no-op there and logs an
                // error instead, which would leave a second canvas alive drawing every label twice.
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
                return;
            }

            instance = this;
            built = true;

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // Deliberately no GraphicRaycaster. Nothing here is clickable, and adding one would put
            // a full-screen canvas in front of every real button in the game.

            // A child rather than the canvas's own RectTransform: Unity drives a root Canvas's rect
            // itself and overwrites anchors written to it, so projecting against a stretched child
            // is the only way to be sure of the rect labels are positioned in.
            var layerGo = new GameObject("Layer", typeof(RectTransform));
            layer = (RectTransform)layerGo.transform;
            layer.SetParent(transform, false);
            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.pivot = new Vector2(0.5f, 0.5f);
            layer.offsetMin = Vector2.zero;
            layer.offsetMax = Vector2.zero;

            // Bound explicitly for the same reason Build exists at all: AddComponent raises OnEnable
            // in play mode and not outside it, and a damage listener that silently never attached is
            // precisely the failure this whole feature already had once.
            gameObject.AddComponent<DamageNumbers>().Bind();
            gameObject.AddComponent<PlayerNameplates>();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>
        /// Where <paramref name="world"/> falls on the canvas. False when it is behind the camera,
        /// where the projection folds back on itself and would place the label on the opposite side
        /// of the screen from the thing it describes.
        /// </summary>
        public bool Project(Vector3 world, out Vector2 canvasPoint)
        {
            canvasPoint = default;

            Camera cam = Eye;
            if (cam == null || layer == null) return false;

            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0f) return false;

            // Null camera, deliberately: for a Screen Space - Overlay canvas that is the documented
            // argument, and passing the scene camera instead silently returns points scaled wrong.
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                layer, screen, null, out canvasPoint);
        }

        /// <summary>True when a canvas point is inside the visible area, with room for the label.</summary>
        public bool IsOnScreen(Vector2 canvasPoint, float margin = 0f)
        {
            if (layer == null) return false;

            Rect r = layer.rect;
            return canvasPoint.x >= r.xMin - margin && canvasPoint.x <= r.xMax + margin
                && canvasPoint.y >= r.yMin - margin && canvasPoint.y <= r.yMax + margin;
        }

        // ------------------------------------------------------------------- labels

        private static Material outlinedFont;

        /// <summary>
        /// Builds a label of the kind this overlay draws: centred, unwrapped, non-interactive, and
        /// outlined so it stays readable against sand, sky and shadow alike — which is the whole
        /// difficulty of text laid over a 3D scene rather than over a panel.
        /// </summary>
        public static TextMeshProUGUI CreateLabel(RectTransform parent, string name, float fontSize, float width)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, fontSize * 1.6f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            tmp.text = string.Empty;

            Material outlined = EnsureOutlinedFont(tmp);
            if (outlined != null) tmp.fontSharedMaterial = outlined;

            return tmp;
        }

        /// <summary>
        /// One outlined material instance shared by every label on this overlay.
        ///
        /// Shared rather than per-label because TMP's <c>outlineWidth</c> property instantiates a
        /// material behind your back, and a pool of labels each with its own copy cannot batch.
        /// Fading is done through the vertex colour (<c>TMP_Text.alpha</c>) precisely so it stays
        /// per-label and does not have to touch this.
        /// </summary>
        private static Material EnsureOutlinedFont(TMP_Text sample)
        {
            if (outlinedFont != null) return outlinedFont;
            if (sample == null || sample.font == null || sample.font.material == null) return null;

            outlinedFont = new Material(sample.font.material) { hideFlags = HideFlags.DontSave };
            outlinedFont.EnableKeyword(ShaderUtilities.Keyword_Outline);
            outlinedFont.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 0.9f));
            outlinedFont.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.18f);

            return outlinedFont;
        }

        // ------------------------------------------------------------------- anchoring

        /// <summary>Used when an entity offers nothing to measure — roughly a person's height.</summary>
        private const float FallbackHeadOffset = 1.9f;

        /// <summary>Clear air between the top of the entity and the bottom of its label.</summary>
        private const float HeadMargin = 0.35f;

        /// <summary>
        /// How far above <paramref name="target"/>'s own origin its head sits, in metres.
        ///
        /// Measured rather than assumed, because the things that get labelled here range from a
        /// crouching player to a six-legged habitat, and a fixed offset would bury the label inside
        /// the larger ones. Colliders are preferred over renderers: they are what the entity
        /// physically is, they do not swing about with an animation, and reading their bounds does
        /// not touch a skinned mesh.
        ///
        /// Worth caching per entity by the caller — the answer barely changes, and this walks the
        /// whole hierarchy.
        /// </summary>
        public static float HeadOffset(GameObject target)
        {
            if (target == null) return FallbackHeadOffset;

            float originY = target.transform.position.y;
            float top = float.NegativeInfinity;

            foreach (Collider collider in target.GetComponentsInChildren<Collider>())
            {
                // Triggers are interaction volumes — a pickup radius, an aggro range — and are
                // routinely far larger than the body they belong to.
                if (collider == null || collider.isTrigger || !collider.enabled) continue;
                top = Mathf.Max(top, collider.bounds.max.y);
            }

            if (float.IsNegativeInfinity(top))
            {
                foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
                {
                    // Only real geometry. A trail or a particle system reports the bounds of its
                    // spread, which for a muzzle flash or a jetpack plume is most of the sky.
                    if (renderer is not (MeshRenderer or SkinnedMeshRenderer)) continue;
                    if (!renderer.enabled) continue;
                    top = Mathf.Max(top, renderer.bounds.max.y);
                }
            }

            if (float.IsNegativeInfinity(top)) return FallbackHeadOffset;

            return Mathf.Max(0.2f, top - originY) + HeadMargin;
        }
    }
}
