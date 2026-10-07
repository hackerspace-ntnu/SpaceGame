// The speaking pip: a solid disc with a glow breathing behind it.
//
// Shape and motion, never colour alone (GDC-L1-UX-0006). A pip that is there and pulsing versus not
// there at all reads the same to a colour-blind player as to anyone else, which a name that merely
// changed tint would not. Shared by the speaking list and the over-head nameplates so "this person
// is talking" looks the same everywhere it is shown.
using UnityEngine;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>One speaking indicator, built into a parent and driven once a frame.</summary>
    internal sealed class VoicePip
    {
        /// <summary>Breaths per second. Near a relaxed speaking cadence, so it reads as "talking".</summary>
        private const float PulseHz = 2.2f;

        /// <summary>The glow at rest, relative to the dot.</summary>
        private const float GlowScale = 2.2f;

        /// <summary>How far past its rest size the glow swells at the top of a breath.</summary>
        private const float GlowGrowth = 0.55f;

        private const float GlowAlphaLow = 0.25f;
        private const float GlowAlphaHigh = 0.7f;

        public readonly RectTransform Rect;

        private readonly Image dot;
        private readonly Image glow;
        private readonly RectTransform glowRect;
        private Color current;

        public VoicePip(RectTransform parent, float size, Color color)
        {
            Rect = Centered(UIBuilder.Rect("SpeakingPip", parent), size);

            // Glow first so the dot draws over it.
            glowRect = Centered(UIBuilder.Rect("Glow", Rect), size * GlowScale);
            glow = UIBuilder.Sprite(glowRect, UITheme.GlowSprite, color, Image.Type.Simple);
            glow.raycastTarget = false;

            dot = UIBuilder.Sprite(Centered(UIBuilder.Rect("Dot", Rect), size), UITheme.CircleSprite,
                                   color, Image.Type.Simple);
            dot.raycastTarget = false;

            current = color;
            Show(false);
        }

        public void Show(bool visible)
        {
            // Unity's == — the pip lives under someone else's GameObject, which can be destroyed
            // (a scene unload taking the overlay) before whoever holds this handle hears about it.
            if (Rect == null) return;
            if (Rect.gameObject.activeSelf != visible) Rect.gameObject.SetActive(visible);
        }

        /// <summary>
        /// Recolours the pip — a speaker's team colour, or the accent where there are no teams.
        /// Only the hue: <see cref="Animate"/> owns the alpha.
        /// </summary>
        public void SetColor(Color color)
        {
            if (Rect == null) return;
            if (color.r == current.r && color.g == current.g && color.b == current.b) return;

            current = color;
            dot.color = new Color(color.r, color.g, color.b, dot.color.a);
            glow.color = new Color(color.r, color.g, color.b, glow.color.a);
        }

        /// <summary>
        /// Advances the breath. <paramref name="alpha"/> scales the whole pip, so it fades with a
        /// nameplate at distance instead of staying bright on a name that is going.
        /// </summary>
        public void Animate(float time, float alpha)
        {
            if (Rect == null) return;

            float breath = 0.5f + 0.5f * Mathf.Sin(time * PulseHz * 2f * Mathf.PI);

            glowRect.localScale = Vector3.one * (1f + GlowGrowth * breath);

            Color glowColor = glow.color;
            glowColor.a = alpha * Mathf.Lerp(GlowAlphaLow, GlowAlphaHigh, breath);
            glow.color = glowColor;

            Color dotColor = dot.color;
            dotColor.a = alpha;
            dot.color = dotColor;
        }

        private static RectTransform Centered(RectTransform rect, float size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(size, size);
            return rect;
        }
    }
}
