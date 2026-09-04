using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SpaceGame.Characters;
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
    /// <b>It marks the HIT, not the component.</b> This used to answer "where is the target" by
    /// taking the union of every <see cref="Renderer"/> under the interactable component, which is
    /// a different question from the one <see cref="Interactor"/> had already answered and got a
    /// visibly different answer: an interactable resolved off a parent — which is how any collider
    /// with no component of its own answers — bracketed the whole hull at its centroid, and one
    /// whose collider is a bare trigger standing proud of a fixture bracketed the empty air the
    /// trigger pads out into. The Interactor now publishes the collider and point it arbitrated,
    /// and <see cref="FramedSubject"/> frames the smallest drawn thing containing that hit.
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

        [Tooltip("Largest it may draw, so standing inside a big fixture does not frame the screen. " +
                 "Also the point at which a target stops being framed and its hit point is marked " +
                 "instead — see MarksHitPoint.")]
        [SerializeField, Min(32f)] private float maxSize = 420f;

        [Tooltip("Padding around the target's projected bounds.")]
        [SerializeField] private float padding = 10f;

        [Tooltip("Radius, in metres, of the mark drawn on a bare hit point — a spot on a wall, a " +
                 "control with nothing drawn under it, anything too big to frame.")]
        [SerializeField, Min(0.01f)] private float pointRadius = 0.25f;

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
        private Canvas canvas;

        private RectTransform infoPanel;
        private CanvasGroup infoGroup;
        private TextMeshProUGUI infoLabel;
        private TextMeshProUGUI infoValue;
        private TextMeshProUGUI infoPrompt;
        private RectTransform infoBarTrack;
        private Image infoBarFill;

        /// <summary>
        /// The things on this player's own body that describe what is under the crosshair without
        /// being interactables. Resolved with the Interactor, and dropped with it.
        /// </summary>
        private ICrosshairReadout[] readouts;

        private Object lastTarget;
        private float snapElapsed;
        private float infoAlpha;
        private float infoDisplayedFill;

        /// <summary>What the crosshair is on, already reduced to something drawable.</summary>
        private struct Aim
        {
            /// <summary>Renderers under this are framed. Null marks <see cref="Point"/> instead.</summary>
            public Transform Subject;

            /// <summary>Where the look ray landed.</summary>
            public Vector3 Point;

            /// <summary>What the bracket re-snaps on.</summary>
            public Object Key;

            public InteractionDisplay Display;
            public bool HasDisplay;
        }

        private void Awake()
        {
            root = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
            Build();
        }

        private void LateUpdate()
        {
            if (!TryResolveAim(out Aim aim))
            {
                group.alpha = 0f;
                lastTarget = null;
                FadeInfoBox(shown: false);
                return;
            }

            if (!ReferenceEquals(aim.Key, lastTarget))
            {
                lastTarget = aim.Key;
                snapElapsed = 0f;
            }

            Camera view = referenceCamera != null ? referenceCamera : Camera.main;
            if (view == null || !TryProject(view, aim, out Vector2 centre, out float size))
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

            if (aim.HasDisplay)
            {
                infoPanel.anchoredPosition = InfoBoxAt(centre, placedSize);
                UpdateInfoBox(aim.Display);
                FadeInfoBox(shown: true);
            }
            else
            {
                FadeInfoBox(shown: false);
            }
        }

        // ── What the crosshair is on ─────────────────────────────────────────

        /// <summary>
        /// The interactable under the crosshair, reduced to something drawable, or — when there is
        /// none — whatever else on this player's body claims the aim.
        /// </summary>
        private bool TryResolveAim(out Aim aim)
        {
            aim = default;

            ResolveSources();
            if (playerInteractor == null || !playerInteractor.isActiveAndEnabled) return false;

            if (playerInteractor.IsHoveringInteractable
                && playerInteractor.HoveredInteractable is Component target && target != null)
            {
                Collider hit = playerInteractor.HoveredCollider;

                aim.Subject = FramedSubject(hit != null ? hit.transform : null, target.transform);
                aim.Point = hit != null ? playerInteractor.HoveredPoint : target.transform.position;
                aim.Key = target;

                // A prompt switched off with an InteractionPrompt still gets a bracket: the player
                // is told there is something there and left to find out what.
                aim.HasDisplay = InteractionPromptResolver.TryResolve(
                    playerInteractor.HoveredInteractable, out aim.Display);
                return true;
            }

            if (readouts == null) return false;

            foreach (ICrosshairReadout source in readouts)
            {
                // A readout on a body that has since been destroyed is a MissingReferenceException
                // waiting on the next property read, so the Unity null test comes first.
                if (source is Behaviour behaviour && (behaviour == null || !behaviour.isActiveAndEnabled))
                    continue;
                if (!source.TryReadCrosshair(out CrosshairReadout readout)) continue;

                aim.Subject = readout.Subject;
                aim.Point = readout.Point;
                aim.Key = readout.Key;
                aim.Display = readout.Display;
                aim.HasDisplay = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Bind to the player this visor hangs under — never to whoever happens to be first in the
        /// scene. This used to be <c>FindFirstObjectByType</c>, which in a session with a second
        /// player returns an arbitrary body: the bracket then described what a stranger was looking
        /// at, on a machine that could not act on it. Read off the parent chain like the rest of
        /// the helmet, which is also correct earlier — see <see cref="HelmetHUDController"/>.
        /// </summary>
        private void ResolveSources()
        {
            if (playerInteractor != null) return;

            PlayerController player = GameplayMenuScope.FindLocalPlayer(this);
            if (player == null) return;

            // includeInactive: the Interactor lives on a camera rig a mount switches off, and a
            // visor that only ever looked once while mounted would never find it again.
            playerInteractor = player.GetComponentInChildren<Interactor>(true);
            readouts = player.GetComponentsInChildren<ICrosshairReadout>(true);
        }

        /// <summary>
        /// The smallest drawn thing that contains the hit — what the bracket should frame.
        ///
        /// <para>
        /// Walks up from the collider the ray actually met, and stops at the interactable itself.
        /// Both ends matter. Starting at the hit is what keeps the bracket on the bay-door leaf
        /// rather than between all six of them; stopping at the interactable is what stops a
        /// renderer-less control climbing on into the hull it is bolted to and framing the ship.
        /// </para>
        /// <para>
        /// Null means nothing under the aim is drawn at all — a bare trigger on a bare pivot — and
        /// the caller marks the hit point instead. That is the honest answer: the padding that
        /// stands such a trigger proud of its fixture is exactly the distance the old bracket was
        /// wrong by.
        /// </para>
        /// <para>
        /// Renderers rather than colliders throughout: an interactable's trigger is often a big
        /// invisible box, and bracketing that marks empty air beside the thing.
        /// </para>
        /// </summary>
        /// <param name="hit">The collider's transform, or null when there was no collider.</param>
        /// <param name="interactable">The component that answered, and the bound on the walk.</param>
        public static Transform FramedSubject(Transform hit, Transform interactable)
        {
            if (hit != null && interactable != null && hit.IsChildOf(interactable))
            {
                for (Transform step = hit; step != null; step = step.parent)
                {
                    if (IsDrawn(step)) return step;
                    if (step == interactable) break;
                }
                return null;
            }

            // Not one hierarchy — an InteractableProxy redirecting the press somewhere else, or no
            // collider at all. Neither end can be walked, so take whichever end is drawn.
            if (IsDrawn(hit)) return hit;
            return IsDrawn(interactable) ? interactable : null;
        }

        /// <summary>
        /// Whether a target is too big to be framed, in which case the bracket marks the point the
        /// player is aiming at instead.
        ///
        /// <para>
        /// A bracket is a frame: it says "this thing, this big". Past the size cap it stops being
        /// able to say that — the corners clamp while the centre stays on the object's centroid, so
        /// a modest mark floats somewhere in the middle of a hull the player is standing inside,
        /// nowhere near their crosshair. A mark on the hit point is the weaker claim and the true
        /// one.
        /// </para>
        /// </summary>
        public static bool MarksHitPoint(float projectedSize, float maxSize) =>
            projectedSize > maxSize;

        private static bool IsDrawn(Transform candidate) =>
            candidate != null && candidate.GetComponentInChildren<Renderer>() != null;

        // ── Drawing ──────────────────────────────────────────────────────────

        /// <summary>
        /// Projects the aim to a screen-space square. A square rather than the bounds' own aspect:
        /// the bracket marks a POSITION, and a tall thin rectangle around a ladder reads as a UI
        /// element rather than as a mark on the world.
        /// </summary>
        private bool TryProject(Camera view, in Aim aim, out Vector2 centre, out float size)
        {
            centre = default;
            size = 0f;

            Vector3 worldCentre = aim.Point;
            float worldRadius = pointRadius;

            if (aim.Subject != null && TryBounds(aim.Subject, out Bounds bounds))
            {
                worldCentre = bounds.center;
                worldRadius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            }

            if (!TryMeasure(view, worldCentre, worldRadius, out centre, out size)) return false;

            if (MarksHitPoint(size, maxSize)
                && !TryMeasure(view, aim.Point, pointRadius, out centre, out size)) return false;

            size = Mathf.Clamp(size, minSize, maxSize);
            return true;
        }

        /// <summary>A world sphere as a canvas-space centre and diameter, padding included.</summary>
        private bool TryMeasure(Camera view, Vector3 worldCentre, float worldRadius,
                                out Vector2 centre, out float size)
        {
            size = 0f;
            if (!TryToCanvas(view, worldCentre, out centre)) return false;

            // The radius measured on screen, by projecting a point one radius up from the centre.
            if (!TryToCanvas(view, worldCentre + (view.transform.up * worldRadius), out Vector2 edge))
                return false;

            size = (Vector2.Distance(centre, edge) * 2f) + (padding * 2f);
            return true;
        }

        /// <summary>
        /// A world point as an anchoredPosition on this layer.
        ///
        /// <para>
        /// Through <see cref="RectTransformUtility"/> rather than by dividing the screen point by
        /// the canvas scale, because this layer is not guaranteed to sit at the canvas origin:
        /// <see cref="VisorSway"/> writes an offset onto the visor root every frame, so the whole
        /// helmet — this bracket with it — lags a few pixels behind a head turn. Everything else on
        /// the layer is meant to do that. A mark that claims to be ON something in the world is not,
        /// and drifting off its target exactly while the player swings the camera onto it is the
        /// worst moment to drift. Asking the rectangle where a screen point falls inside it takes
        /// that offset — and any other ancestor transform — out of the answer.
        /// </para>
        /// </summary>
        private bool TryToCanvas(Camera view, Vector3 world, out Vector2 canvasPoint)
        {
            canvasPoint = default;

            Vector3 screen = view.WorldToScreenPoint(world);
            if (screen.z <= 0f) return false;   // behind the eye

            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    root, screen, uiCamera, out Vector2 local)) return false;

            // Local space is measured from the pivot; anchoredPosition on a bottom-left-anchored
            // child is measured from the corner.
            canvasPoint = local + Vector2.Scale(root.rect.size, root.pivot);
            return true;
        }

        /// <summary>The union of the renderers under a subject, or false when it draws nothing.</summary>
        private static bool TryBounds(Transform subject, out Bounds bounds)
        {
            bounds = default;

            Renderer[] renderers = subject.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        /// <summary>
        /// Where the info box sits: above the bracket, or below it when there is no room above.
        /// Kept inside the canvas on both axes — a name pushed off the top edge by a target near
        /// the ceiling is a name the player cannot read, which is the same failure as putting it in
        /// the wrong place.
        /// </summary>
        private Vector2 InfoBoxAt(Vector2 centre, float bracketSize)
        {
            float half = (bracketSize * 0.5f) + infoGap + (infoHeight * 0.5f);
            Vector2 canvasSize = root.rect.size;

            float y = centre.y + half;
            if (y + (infoHeight * 0.5f) > canvasSize.y) y = centre.y - half;

            return new Vector2(
                Mathf.Clamp(centre.x, infoWidth * 0.5f, Mathf.Max(infoWidth * 0.5f, canvasSize.x - (infoWidth * 0.5f))),
                Mathf.Clamp(y, infoHeight * 0.5f, Mathf.Max(infoHeight * 0.5f, canvasSize.y - (infoHeight * 0.5f))));
        }

        private void Place(Vector2 centreInCanvasPixels, float size)
        {
            // The canvas is anchored bottom-left in the same space TryToCanvas reports, so the
            // projected point is used directly rather than being re-based.
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
            infoValue.text = display.ValueText;
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

            // Shares the label's band, pushed to the other end. IInteractionReadout has always had
            // a ValueText beside its bar — the rake in degrees, the hoist in percent, how much is
            // in a pack — and nothing in the project drew it, so every readout that bothered to
            // write one was writing into nothing.
            infoValue = UIBuilder.LabelIn(infoPanel, "Value", string.Empty, VisorStyle.MicroSize,
                                          VisorStyle.InkDim, TextAlignmentOptions.Right);
            PinRow((RectTransform)infoValue.transform, 4f, 20f);

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
