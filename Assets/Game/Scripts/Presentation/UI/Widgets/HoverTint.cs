using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Recolours a graphic while the pointer is over this object.
    /// <para>
    /// A Button's own colour transition only drives its single target graphic, and the composed
    /// controls in these menus are hit-tested by an invisible full-rect image so the whole row is
    /// clickable — which means the thing the Button tints is the thing nobody can see. This drives
    /// the visible part alongside it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class HoverTint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Graphic target;
        [SerializeField] private Color normal = Color.white;
        [SerializeField] private Color hovered = Color.white;
        [SerializeField] private float fadeSpeed = 14f;

        private Color current;

        public void Bind(Graphic graphic, Color normalColor, Color hoveredColor)
        {
            target = graphic;
            normal = normalColor;
            hovered = hoveredColor;
            current = normalColor;
            if (target != null) target.color = normalColor;
        }

        /// <summary>Re-points the resting colour, e.g. when a tab becomes the selected one.</summary>
        public void SetNormal(Color normalColor)
        {
            normal = normalColor;
            if (!isHovered && target != null) target.color = normalColor;
        }

        private bool isHovered;

        public void OnPointerEnter(PointerEventData eventData) => isHovered = true;

        public void OnPointerExit(PointerEventData eventData) => isHovered = false;

        private void OnDisable() => isHovered = false;

        private void Update()
        {
            if (target == null) return;

            // Unscaled: the screens this runs in are the ones that freeze the game clock.
            current = Color.Lerp(current, isHovered ? hovered : normal, 1f - Mathf.Exp(-fadeSpeed * Time.unscaledDeltaTime));
            target.color = current;
        }
    }
}
