// The three uGUI primitives a world-space readout on a fixture is assembled from: the canvas
// laid on a surface, a flat panel, a label. Lifted out of RepairStationBuilder when the standing
// terminal needed the same three — one definition of what a millimetre canvas is, not two.
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceGame.EditorTools
{
    public static class WorldCanvasBuilder
    {
        /// <summary>One canvas unit is one millimetre: a 0.38 m screen is a 380-unit canvas.</summary>
        public const float CanvasUnit = 0.001f;

        /// <summary>
        /// A world-space canvas of <paramref name="sizeMm"/> at <paramref name="position"/>, facing
        /// <paramref name="rotation"/>'s forward. A world-space Canvas reads correctly from the side
        /// its forward points AWAY from, so pass the rotation whose forward points INTO the surface.
        /// </summary>
        public static RectTransform Canvas(Transform parent, string name, Vector2 sizeMm,
                                           Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.layer = LayerMask.NameToLayer("UI");
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = sizeMm;
            rect.localScale = Vector3.one * CanvasUnit;
            rect.SetPositionAndRotation(position, rotation);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            return rect;
        }

        public static Image Panel(Transform parent, string name, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A panel stretched over its whole parent.</summary>
        public static Image Fill(Transform parent, string name, Color colour)
        {
            Image image = Panel(parent, name, colour);
            image.rectTransform.anchorMin = Vector2.zero;
            image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.sizeDelta = Vector2.zero;
            return image;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, float size,
                                            Vector2 position, Vector2 extent,
                                            TextAlignmentOptions alignment = TextAlignmentOptions.Center,
                                            FontStyles style = FontStyles.Bold)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = extent;
            // Explicit: a TextMeshProUGUI created by script outside play mode is saved with no
            // font, and a prefab whose text has none renders nothing in a build.
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }
    }
}
