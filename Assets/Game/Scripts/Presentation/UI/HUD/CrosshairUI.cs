using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The crosshair, and the two things allowed to change it.
    ///
    /// <para>
    /// <b>Hover</b> — <see cref="playerInteractor"/> brightening it over something usable. Note
    /// that this half has never actually run: the reference is unassigned on PlayerHUD.prefab and
    /// nothing fills it in, so <see cref="Update"/> returned on its first line every frame. Its
    /// sibling <c>InteractionPromptUI</c> solves the same problem with a
    /// <c>FindFirstObjectByType&lt;Interactor&gt;</c> fallback and would fix this in one line —
    /// deliberately not done here, because switching on idle dimming the game has never shipped
    /// with is a look change, not a bug fix, and belongs to whoever owns the HUD.
    /// </para>
    /// <para>
    /// <b>Aim hint</b> — a held item reporting that its aim would land. Kept independent of the
    /// above precisely so it works while that reference stays unwired.
    /// </para>
    /// </summary>
    public class CrosshairUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Interactor playerInteractor;
        [SerializeField] private RawImage crosshairImage;

        [Header("Alpha")]
        [Range(0f, 1f)] public float idleAlpha = 0.25f;
        [Range(0f, 1f)] public float activeAlpha = 1f;
        public float fadeSpeed = 10f;

        [Header("Aim hint")]
        [Tooltip("Colour the crosshair takes while a held item reports that its aim would land. " +
                 "Safety orange, matching the grapple dart's own high-vis band.")]
        [SerializeField] private Color hintColor = new Color(1f, 0.45f, 0.15f, 1f);

        [Tooltip("How much larger the crosshair grows while hinting. 1 disables the growth.")]
        [SerializeField] private float hintScale = 1.3f;

        private float _currentAlpha;

        /// <summary>The authored look, so the hint has something to return to.</summary>
        private Color _baseColor = Color.white;
        private Vector3 _baseScale = Vector3.one;

        private bool _hinted;
        private bool _hintedThisFrame;

        /// <summary>
        /// Report that the held item's aim would land on something it can act on.
        ///
        /// <para>
        /// Set every frame it stays true; simply stop calling when it does not. The flag is
        /// latched and cleared below, so an item unequipped mid-aim releases the hint on its own
        /// rather than leaving the crosshair lit for a tool the player is no longer holding.
        /// </para>
        /// </summary>
        public void SetAimHint(bool on) => _hintedThisFrame = on;

        private void Start()
        {
            if (crosshairImage == null) return;

            _baseColor = crosshairImage.color;
            _baseScale = crosshairImage.rectTransform.localScale;

            // Seeded from the authored alpha rather than 0, so the first hint fades up from what
            // is actually on screen instead of from invisible.
            _currentAlpha = _baseColor.a;
        }

        private void Update()
        {
            if (!crosshairImage) return;

            _hinted = _hintedThisFrame;
            _hintedThisFrame = false;

            // Nothing is driving the crosshair and nothing is easing back to rest: leave it
            // exactly as authored. This is what keeps the hint from quietly enabling the dormant
            // hover behaviour described on the class.
            bool settling = NeedsSettle();
            if (playerInteractor == null && !_hinted && !settling) return;

            float k = Time.deltaTime * fadeSpeed;

            Color target = _hinted ? hintColor : _baseColor;
            Color color = Color.Lerp(crosshairImage.color, target, k);

            Vector3 targetScale = _hinted ? _baseScale * hintScale : _baseScale;
            crosshairImage.rectTransform.localScale =
                Vector3.Lerp(crosshairImage.rectTransform.localScale, targetScale, k);

            // Alpha moves only when something is entitled to move it. Left alone, a HUD with no
            // interactor keeps the alpha it was authored with.
            if (playerInteractor != null || _hinted)
            {
                bool bright = _hinted
                              || (playerInteractor != null && playerInteractor.IsHoveringInteractable);

                _currentAlpha = Mathf.Lerp(_currentAlpha, bright ? activeAlpha : idleAlpha, k);
                color.a = _currentAlpha;
            }
            else
            {
                color.a = crosshairImage.color.a;
            }

            crosshairImage.color = color;
        }

        /// <summary>Is the crosshair still on its way back to the authored look?</summary>
        private bool NeedsSettle()
        {
            Color c = crosshairImage.color;

            return Mathf.Abs(c.r - _baseColor.r) > 0.004f
                || Mathf.Abs(c.g - _baseColor.g) > 0.004f
                || Mathf.Abs(c.b - _baseColor.b) > 0.004f
                || (crosshairImage.rectTransform.localScale - _baseScale).sqrMagnitude > 1e-6f;
        }
    }
}
