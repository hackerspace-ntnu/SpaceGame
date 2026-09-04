using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The visor's answer to "what am I pointing at": four corner marks that snap around whatever
    /// the player is looking at, sized to the thing's own bounds, plus a look-at info box that
    /// unfolds beside the bracket with its name, its prompt and, where it has one, a live value.
    ///
    /// <para>
    /// <c>InteractionPromptUI</c> used to draw the info box as an unrelated widget pinned to a
    /// fixed spot on screen. It is folded in here instead because both halves read the same
    /// hovered target through the same projection — drawing them as two components would have
    /// meant resolving the hover and projecting its screen position twice, once per class.
    /// <see cref="InteractionPromptResolver"/> still owns every decision about what the words say;
    /// this only draws what it returns.
    /// </para>
    /// <para>
    /// The bracket snaps from oversize rather than fading in — it reads as the suit acquiring the
    /// target, and is one of only two motions on the whole layer that are not ambient (see
    /// <see cref="VisorStyle"/>). The info box fades instead: a hard cut as the crosshair crosses
    /// two handles a metre apart flickers badly when a live value is involved.
    /// </para>
    /// <para>
    /// This is the bracket and info-box two-thirds of the design spec's <c>VisorReticle</c>
    /// module; the crosshair third is not built yet and still lives on the separate, unmoved
    /// <c>CrosshairUI</c>. Adding it later is a matter of drawing it on this same root rather than
    /// inventing a fourth name for the reticle.
    /// </para>
    /// </summary>
    public class VisorReticle : MonoBehaviour
    {
        [Tooltip("The player's Interactor. Found at runtime when empty — the player is spawned, so " +
                 "it does not exist when this wakes.")]
        [SerializeField] private Interactor playerInteractor;

        [Tooltip("Camera the target is projected through. Empty means Camera.main, re-read every " +
                 "frame, which is what lets the bracket follow the view onto a mount.")]
        [SerializeField] private Camera referenceCamera;

        [Header("Bracket")]
        [Tooltip("Length of one corner arm, in reference pixels.")]
        [SerializeField, Min(4f)] private float armLength = 18f;

        [Tooltip("Smallest the bracket may draw, so a distant crate is still a mark and not a dot.")]
        [SerializeField, Min(16f)] private float minSize = 46f;

        [Tooltip("Largest it may draw, so standing inside a big fixture does not frame the screen.")]
        [SerializeField, Min(32f)] private float maxSize = 420f;

        [Tooltip("Padding around the target's projected bounds.")]
        [SerializeField] private float padding = 10f;

        [Tooltip("How much oversize the bracket starts at when it acquires a new target.")]
        [SerializeField, Range(1f, 2.5f)] private float snapFrom = 1.45f;

        [Tooltip("Seconds the snap takes to settle.")]
        [SerializeField, Min(0.01f)] private float snapSeconds = 0.12f;

        [Header("Info box")]
        [Tooltip("Width of the info box, in reference pixels.")]
        [SerializeField, Min(120f)] private float infoWidth = 300f;

        [Tooltip("Height of the info box, in reference pixels.")]
        [SerializeField, Min(48f)] private float infoHeight = 76f;

        [Tooltip("Gap between the bracket's edge and the info box above it.")]
        [SerializeField] private float infoGap = 14f;

        [Tooltip("Seconds for the info box to fade in and out. A hard cut as the target changes " +
                 "under a moving value reads as a flicker rather than a new readout.")]
        [SerializeField, Min(0.01f)] private float infoFadeSeconds = 0.12f;

        [Tooltip("Seconds a live value takes to catch up to its target. The control is usually " +
                 "held, so the value is moving continuously while the player reads it.")]
        [SerializeField, Min(0f)] private float infoValueLerpSeconds = 0.08f;

        private readonly RectTransform[] corners = new RectTransform[4];
        private CanvasGroup group;
        private RectTransform root;

        private RectTransform infoPanel;
        private CanvasGroup infoGroup;
        private TextMeshProUGUI infoLabel;
        private TextMeshProUGUI infoPrompt;
        private RectTransform infoBarTrack;
        private Image infoBarFill;

        private Component lastTarget;
        private float snapElapsed;
        private float infoAlpha;
        private float infoDisplayedFill;

        private void Awake()
        {
            root = (RectTransform)transform;
            Build();
        }

        private void LateUpdate()
        {
            IInteractable interactable = ResolveTarget();
            Component target = interactable as Component;

            if (target == null)
            {
                group.alpha = 0f;
                lastTarget = null;
                FadeInfoBox(shown: false);
                return;
            }

            if (!ReferenceEquals(target, lastTarget))
            {
                lastTarget = target;
                snapElapsed = 0f;
            }

            Camera view = referenceCamera != null ? referenceCamera : Camera.main;
            if (view == null || !TryProject(view, target, out Vector2 centre, out float size))
            {
                group.alpha = 0f;
                FadeInfoBox(shown: false);
                return;
            }

            snapElapsed += Time.unscaledDeltaTime;

            // Eased from oversize. Skipped entirely when the player has asked for less motion —
            // the bracket still appears, it simply appears at its final size.
            float t = GameSettings.ReduceVisorMotion || snapSeconds <= 0f
                ? 1f
                : Mathf.Clamp01(snapElapsed / snapSeconds);
            float scale = Mathf.Lerp(snapFrom, 1f, 1f - ((1f - t) * (1f - t)));

            group.alpha = 1f;
            float placedSize = size * scale;
            Place(centre, placedSize);

            if (InteractionPromptResolver.TryResolve(interactable, out InteractionDisplay display))
            {
                infoPanel.anchoredPosition = centre + new Vector2(0f, (placedSize * 0.5f) + infoGap + (infoHeight * 0.5f));
                UpdateInfoBox(display);
                FadeInfoBox(shown: true);
            }
            else
            {
                FadeInfoBox(shown: false);
            }
        }

        /// <summary>
        /// What the player is pointing at, as the interactable itself — so both the bracket's
        /// bounds projection and the info box's resolved text come from one hover.
        /// </summary>
        private IInteractable ResolveTarget()
        {
            if (playerInteractor == null) playerInteractor = FindFirstObjectByType<Interactor>();
            if (playerInteractor == null || !playerInteractor.isActiveAndEnabled) return null;
            if (!playerInteractor.IsHoveringInteractable) return null;

            return playerInteractor.HoveredInteractable;
        }

        /// <summary>
        /// Projects the target's renderer bounds to a screen-space square. A square rather than the
        /// bounds' own aspect: the bracket marks a POSITION, and a tall thin rectangle around a
        /// ladder reads as a UI element rather than as a mark on the world.
        /// </summary>
        private bool TryProject(Camera view, Component target, out Vector2 centre, out float size)
        {
            centre = default;
            size = 0f;

            Vector3 worldCentre = target.transform.position;
            float worldRadius = 0.5f;

            // Renderers rather than colliders: an interactable's trigger is often a big invisible
            // box standing proud of the thing, and bracketing that marks empty air beside it.
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                worldCentre = bounds.center;
                worldRadius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            }

            Vector3 screenCentre = view.WorldToScreenPoint(worldCentre);
            if (screenCentre.z <= 0f) return false;   // behind the eye

            // The radius measured on screen, by projecting a point one radius up from the centre.
            Vector3 screenEdge = view.WorldToScreenPoint(worldCentre + (view.transform.up * worldRadius));
            float screenRadius = Vector2.Distance(screenCentre, screenEdge);

            float canvasScale = UIScale.ScaleFactor(Screen.width, Screen.height);
            if (canvasScale <= 0f) canvasScale = 1f;

            centre = new Vector2(screenCentre.x, screenCentre.y) / canvasScale;
            size = Mathf.Clamp(((screenRadius * 2f) / canvasScale) + (padding * 2f), minSize, maxSize);
            return true;
        }

        private void Place(Vector2 centreInCanvasPixels, float size)
        {
            // The canvas is anchored bottom-left in the same space WorldToScreenPoint reports, so
            // the projected point is used directly rather than being re-based.
            float half = size * 0.5f;

            for (int i = 0; i < corners.Length; i++)
            {
                if (corners[i] == null) continue;

                float sx = (i == 0 || i == 2) ? -1f : 1f;   // left for 0/2, right for 1/3
                float sy = (i < 2) ? 1f : -1f;              // top for 0/1, bottom for 2/3

                corners[i].anchoredPosition = centreInCanvasPixels + new Vector2(sx * half, sy * half);
                corners[i].localScale = new Vector3(sx, sy, 1f);
                corners[i].sizeDelta = new Vector2(armLength, armLength);
            }
        }

        private void UpdateInfoBox(in InteractionDisplay display)
        {
            infoLabel.text = display.Label;
            infoPrompt.text = display.Prompt;

            float? value = display.Value01;
            bool hasValue = value.HasValue;
            if (infoBarTrack.gameObject.activeSelf != hasValue)
                infoBarTrack.gameObject.SetActive(hasValue);

            if (!hasValue) return;

            float target = Mathf.Clamp01(value.Value);
            infoDisplayedFill = infoValueLerpSeconds <= 0f
                ? target
                : Mathf.Lerp(infoDisplayedFill, target,
                             1f - Mathf.Exp(-Time.unscaledDeltaTime / infoValueLerpSeconds));
            infoBarFill.fillAmount = infoDisplayedFill;
        }

        private void FadeInfoBox(bool shown)
        {
            float target = shown ? 1f : 0f;
            infoAlpha = Mathf.MoveTowards(infoAlpha, target, Time.unscaledDeltaTime / infoFadeSeconds);
            infoGroup.alpha = infoAlpha;
            infoPanel.gameObject.SetActive(infoAlpha > 0.001f);
        }

        private void Build()
        {
            group = gameObject.GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            for (int i = 0; i < corners.Length; i++)
            {
                RectTransform corner = UIBuilder.Rect($"Corner{i}", root);

                // Anchored bottom-left with a centre pivot, so anchoredPosition is a plain screen
                // point in canvas units and the local scale flip mirrors the arm into its quadrant.
                corner.anchorMin = corner.anchorMax = Vector2.zero;
                corner.pivot = new Vector2(0.5f, 0.5f);

                Image image = UIBuilder.Sprite(corner, VisorStyle.BracketSprite, VisorStyle.Ink);
                image.raycastTarget = false;

                corners[i] = corner;
            }

            BuildInfoBox();
        }

        private void BuildInfoBox()
        {
            infoPanel = UIBuilder.Rect("InfoBox", root);
            infoPanel.anchorMin = infoPanel.anchorMax = Vector2.zero;
            infoPanel.pivot = new Vector2(0.5f, 0.5f);
            infoPanel.sizeDelta = new Vector2(infoWidth, infoHeight);

            infoGroup = infoPanel.gameObject.AddComponent<CanvasGroup>();
            infoGroup.alpha = 0f;
            infoGroup.interactable = false;
            infoGroup.blocksRaycasts = false;

            UIBuilder.Sprite(infoPanel, VisorStyle.Track(8), VisorStyle.InkFaint);

            infoLabel = UIBuilder.LabelIn(infoPanel, "Label", string.Empty, VisorStyle.BodySize,
                                         VisorStyle.Ink, TextAlignmentOptions.Left, FontStyles.Bold);
            PinRow((RectTransform)infoLabel.transform, 0f, 24f);

            infoPrompt = UIBuilder.LabelIn(infoPanel, "Prompt", string.Empty, VisorStyle.MicroSize,
                                          VisorStyle.InkDim, TextAlignmentOptions.Left);
            PinRow((RectTransform)infoPrompt.transform, 26f, 18f);

            infoBarTrack = UIBuilder.Rect("BarTrack", infoPanel);
            PinRow(infoBarTrack, infoHeight - 18f, VisorStyle.TrackHeight);
            UIBuilder.Sprite(infoBarTrack, VisorStyle.Track(VisorStyle.TrackHeight), VisorStyle.InkFaint);

            RectTransform fillRect = UIBuilder.Fill(UIBuilder.Rect("Fill", infoBarTrack));
            infoBarFill = UIBuilder.Sprite(fillRect, VisorStyle.Track(VisorStyle.TrackHeight), VisorStyle.Ink);
            infoBarFill.type = Image.Type.Filled;
            infoBarFill.fillMethod = Image.FillMethod.Horizontal;
            infoBarTrack.gameObject.SetActive(false);

            infoPanel.gameObject.SetActive(false);
        }

        /// <summary>Stretches a row across the info box's width, <paramref name="fromTop"/> down.</summary>
        private static void PinRow(RectTransform row, float fromTop, float height)
        {
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.offsetMin = new Vector2(10f, 0f);
            row.offsetMax = new Vector2(-10f, 0f);
            row.anchoredPosition = new Vector2(0f, -fromTop);
            row.sizeDelta = new Vector2(0f, height);
        }
    }
}
