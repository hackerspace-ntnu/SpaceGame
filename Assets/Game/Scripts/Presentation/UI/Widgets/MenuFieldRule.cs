using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Makes a <see cref="MenuField"/>'s underline say whether the field is idle, under the pointer,
    /// or actually taking what you type.
    ///
    /// <para>
    /// The menu's language has no boxes, so a field has exactly one piece of chrome to say this
    /// with — the rule under the words. Resting it is <see cref="MenuEntry.Caption"/>, the same
    /// washed-out navy every other caption on the page is drawn in, which is precisely why a field
    /// that <i>was</i> focused looked identical to one that was not: the only difference was a caret
    /// one pixel wide standing next to 64pt bold type.
    /// </para>
    ///
    /// <para>
    /// So there are three cues, not one. Hovering brings the rule up to full <see cref="MenuEntry.Idle"/>,
    /// which is what tells you this line is a control at all. Focusing grows a thicker rule across it
    /// from the left. And the placeholder recedes, so the hint stops competing with the caret for the
    /// same few pixels.
    /// </para>
    ///
    /// <para>
    /// Focus is read from <see cref="TMP_InputField.isFocused"/> each frame rather than driven from
    /// <c>onSelect</c>/<c>onDeselect</c>. Those two do not fire symmetrically around submit or around
    /// a field switched off underneath the player by a <see cref="CanvasGroup"/> — both of which
    /// happen on the lobby's join page — and a focus cue that latches on is worse than none. One bool
    /// read per frame per field is not worth being clever about.
    /// </para>
    /// </summary>
    public class MenuFieldRule : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>How long the focus rule takes to cross the field. Short enough to feel like a response.</summary>
        public const float FocusSeconds = 0.12f;

        /// <summary>The focused rule sits proud of the resting one rather than replacing it.</summary>
        public const float FocusThickness = 5f;

        /// <summary>What the placeholder's alpha is multiplied by once the field has focus.</summary>
        private const float PlaceholderFade = 0.3f;

        private TMP_InputField field;
        private Image underline;
        private RectTransform focusRule;
        private TextMeshProUGUI placeholder;
        private float width;

        private bool pointerInside;

        /// <summary>0 at rest, 1 fully focused. Drives the rule's width and the placeholder's fade.</summary>
        private float focus;

        /// <summary>0 at rest, 1 fully hovered. Separate from <see cref="focus"/> so a focused field does not dim when the pointer leaves.</summary>
        private float hover;

        internal void Bind(TMP_InputField field, Image underline, RectTransform focusRule,
                           TextMeshProUGUI placeholder, float width)
        {
            this.field = field;
            this.underline = underline;
            this.focusRule = focusRule;
            this.placeholder = placeholder;
            this.width = width;
        }

        public void OnPointerEnter(PointerEventData eventData) => pointerInside = true;

        public void OnPointerExit(PointerEventData eventData) => pointerInside = false;

        private void Update()
        {
            // A field switched off by a CanvasGroup keeps whatever pointer state it had when it went
            // quiet, because OnPointerExit is not delivered to something that has stopped blocking
            // raycasts. Reading interactability here is what stops a locked field sitting lit.
            bool live = field != null && field.IsInteractable();

            // Unscaled, for the same reason MenuBusy is: these screens can be raised over a paused
            // game, and a cue that stops moving reads as a control that has stopped working.
            float step = Time.unscaledDeltaTime / FocusSeconds;

            focus = Mathf.MoveTowards(focus, live && field.isFocused ? 1f : 0f, step);
            hover = Mathf.MoveTowards(hover, live && pointerInside ? 1f : 0f, step);

            if (focusRule != null)
                focusRule.sizeDelta = new Vector2(width * focus, FocusThickness);

            // Whichever cue is stronger wins, so moving the pointer off a focused field does not
            // take the underline back down with it.
            if (underline != null)
                underline.color = Color.Lerp(MenuEntry.Caption, MenuEntry.Idle, Mathf.Max(focus, hover));

            if (placeholder != null)
            {
                Color resting = MenuEntry.Caption;
                placeholder.color = new Color(resting.r, resting.g, resting.b,
                                              Mathf.Lerp(resting.a, resting.a * PlaceholderFade, focus));
            }
        }
    }
}
