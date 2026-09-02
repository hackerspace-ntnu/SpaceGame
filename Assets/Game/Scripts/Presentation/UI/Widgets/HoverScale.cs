using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Zooms this rect while the pointer is over it, and dips it while pressed.
    /// <para>
    /// The scale sibling of <see cref="HoverTint"/>, for controls whose text is drawn over the
    /// world rather than in a menu column — a tint is invisible on a label that is already in a
    /// team's own colour, but a zoom reads against any background. Driven from a
    /// <see cref="Selectable"/> so a control that cannot be clicked right now does not pretend
    /// otherwise by responding to the pointer.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class HoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private float hovered = 1.12f;
        [SerializeField] private float pressed = 0.94f;
        [SerializeField] private float speed = 14f;

        /// <summary>Whose interactability gates the effect. Optional — null means always on.</summary>
        [SerializeField] private Selectable selectable;

        private bool isHovered;
        private bool isPressed;
        private float current = 1f;

        public void Bind(Selectable owner) => selectable = owner;

        public void OnPointerEnter(PointerEventData eventData) => isHovered = true;

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovered = false;
            isPressed = false;
        }

        public void OnPointerDown(PointerEventData eventData) => isPressed = true;

        public void OnPointerUp(PointerEventData eventData) => isPressed = false;

        private void OnDisable()
        {
            isHovered = false;
            isPressed = false;
        }

        private float Target()
        {
            if (selectable != null && !selectable.interactable) return 1f;
            if (isPressed) return pressed;
            return isHovered ? hovered : 1f;
        }

        private void Update()
        {
            // Unscaled: the screens this runs in are the ones that freeze the game clock.
            current = Mathf.Lerp(current, Target(), 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
            transform.localScale = Vector3.one * current;
        }
    }
}
