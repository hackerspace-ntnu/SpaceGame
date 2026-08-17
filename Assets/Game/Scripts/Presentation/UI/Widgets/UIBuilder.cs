using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The small set of uGUI primitives the code-built menus are assembled from.
    /// <para>
    /// Rows are laid out with anchors rather than nested layout groups on purpose. A
    /// <see cref="LayoutElement"/> and a <see cref="LayoutGroup"/> both report layout priority 1,
    /// so a row that carries both takes the larger of the two preferred heights and silently
    /// inflates — the bug MinigameConfigUI works around with an extra "Content" child per row.
    /// Anchoring inside a fixed-height row sidesteps it entirely: only the outer column is a
    /// layout group, and everything inside a row is positioned against the row's own edges.
    /// </para>
    /// </summary>
    public static class UIBuilder
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Fills the parent, inset by the given edge distances.</summary>
        public static RectTransform Fill(RectTransform rect, float left = 0f, float bottom = 0f,
            float right = 0f, float top = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        /// <summary>Pins to the parent's left edge at a fixed width, full height.</summary>
        public static RectTransform LeftColumn(RectTransform rect, float x, float width)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, 0f);
            return rect;
        }

        /// <summary>Pins to the parent's right edge at a fixed width, full height.</summary>
        public static RectTransform RightColumn(RectTransform rect, float x, float width)
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-x, 0f);
            rect.sizeDelta = new Vector2(width, 0f);
            return rect;
        }

        public static Image Sprite(RectTransform host, Sprite sprite, Color color,
            Image.Type type = Image.Type.Sliced)
        {
            var image = host.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.type = type;
            image.raycastTarget = false;
            // A 9-sliced sprite whose border is thicker than the rect it is drawn into leaves a
            // visible notch unless the slices are allowed to shrink with it.
            if (type == Image.Type.Sliced) image.pixelsPerUnitMultiplier = 1f;
            return image;
        }

        public static Image Solid(RectTransform host, Color color)
        {
            var image = host.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static TextMeshProUGUI Label(RectTransform host, string text, int size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left, FontStyles style = FontStyles.Normal)
        {
            var label = host.gameObject.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                label.font = TMP_Settings.defaultFontAsset;

            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.fontStyle = style;
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        /// <summary>A label that owns its own full-size rect inside <paramref name="parent"/>.</summary>
        public static TextMeshProUGUI LabelIn(RectTransform parent, string name, string text, int size,
            Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Left,
            FontStyles style = FontStyles.Normal)
        {
            var rect = Fill(Rect(name, parent));
            return Label(rect, text, size, color, alignment, style);
        }

        /// <summary>
        /// Wires a Button with a colour block that leaves the target graphic's own colour alone
        /// (the tint block multiplies, so the graphic is forced white and the block carries the
        /// colour). Matches how the minigame screens tint their text-only buttons.
        /// </summary>
        public static Button Clickable(RectTransform host, Graphic target, Color normal, Color highlight,
            Color pressed = default)
        {
            target.raycastTarget = true;

            var button = host.gameObject.AddComponent<Button>();
            button.targetGraphic = target;
            target.color = Color.white;

            var colors = button.colors;
            colors.normalColor = normal;
            colors.selectedColor = normal;
            colors.highlightedColor = highlight;
            colors.pressedColor = pressed == default ? Color.Lerp(highlight, Color.black, 0.25f) : pressed;
            colors.disabledColor = new Color(normal.r, normal.g, normal.b, normal.a * 0.35f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.09f;
            button.colors = colors;
            return button;
        }

        /// <summary>
        /// An invisible full-rect graphic that catches clicks and hovers for a composed control,
        /// so the row as a whole is the hit target rather than only its text.
        /// </summary>
        public static Image HitArea(RectTransform host)
        {
            var image = host.gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
            return image;
        }

        public static VerticalLayoutGroup Column(RectTransform host, float spacing, RectOffset padding = null,
            TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var group = host.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset(0, 0, 0, 0);
            group.childAlignment = alignment;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            return group;
        }

        public static LayoutElement FixedHeight(RectTransform rect, float height)
        {
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
            return element;
        }

        /// <summary>A fixed-height empty row, used to space sections apart inside a column.</summary>
        public static RectTransform Spacer(RectTransform parent, float height)
        {
            var rect = Rect("Spacer", parent);
            FixedHeight(rect, height);
            return rect;
        }

        /// <summary>
        /// The project is on the new Input System, so a StandaloneInputModule is inert and a scene
        /// without an EventSystem has nothing clickable at all. Gameplay scenes are not guaranteed
        /// to carry one, so any screen that can open over gameplay has to check.
        /// </summary>
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            // Kept across scene loads so a menu that outlives its scene keeps working — but only
            // in play mode: DontDestroyOnLoad throws outright when called from an editor script,
            // which an editor-time layout preview is.
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);
        }
    }
}
