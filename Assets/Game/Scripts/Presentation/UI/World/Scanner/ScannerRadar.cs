using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Items;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The item scanner's plan-position display: range rings, a turning sweep, and one glyph per
    /// contact, drawn as a single uGUI <see cref="Graphic"/> on the world-space canvas laid over
    /// the scanner's screen plate.
    ///
    /// <para>
    /// One graphic rather than a hierarchy of <see cref="Image"/>s. A ring is not a rectangle, and
    /// a per-contact GameObject would mean creating and destroying transforms at the scan rate on
    /// an item that is itself instantiated on every hotbar switch. Everything here is generated
    /// into one mesh in <see cref="OnPopulateMesh"/>, so the whole instrument is one draw call
    /// with no sprite, no texture and no atlas — and it stays crisp at any zoom, because a canvas
    /// mesh is vector geometry rather than pixels.
    /// </para>
    /// <para>
    /// It draws in the rect's own units and knows nothing about metres: contacts arrive already
    /// normalised to the scanner's range by <see cref="ItemScannerScreen"/>, which owns the frame
    /// they are read against.
    /// </para>
    /// </summary>
    [AddComponentMenu("SpaceGame/UI/Scanner Radar")]
    public sealed class ScannerRadar : MaskableGraphic
    {
        /// <summary>One contact as the plot draws it.</summary>
        public readonly struct Blip
        {
            /// <summary>Position in the plot, each axis -1..1 of range. +y is the holder's forward.</summary>
            public readonly Vector2 Plot;

            /// <summary>How fresh the return is, 0 gone to 1 just swept.</summary>
            public readonly float Strength;

            /// <summary>Which glyph to draw it with.</summary>
            public readonly ScanClass Class;

            public Blip(Vector2 plot, float strength, ScanClass cls)
            {
                Plot = plot;
                Strength = strength;
                Class = cls;
            }
        }

        [Header("Plot")]
        [Tooltip("Range rings drawn inside the outer one. Three rings quarter the range, which is " +
                 "what a reader estimates a distance against.")]
        [SerializeField, Range(1, 5)] private int rings = 3;

        [Tooltip("Line width of a ring or a spoke, in canvas units.")]
        [SerializeField, Min(0.5f)] private float lineWidth = 1.5f;

        [Tooltip("Segments in a full circle. Below about 40 the outer ring reads as a polygon.")]
        [SerializeField, Range(12, 128)] private int segments = 64;

        [Header("Sweep")]
        [Tooltip("Degrees of tail behind the beam. The tail is the phosphor decay a real tube " +
                 "leaves, and is what makes the direction of travel readable.")]
        [SerializeField, Range(10f, 180f)] private float sweepArc = 55f;

        [Tooltip("Opacity at the leading edge of the beam, fading to nothing along the tail.")]
        [SerializeField, Range(0f, 1f)] private float sweepAlpha = 0.5f;

        [Header("Contacts")]
        [Tooltip("Size of a contact glyph, in canvas units.")]
        [SerializeField, Min(2f)] private float blipSize = 9f;

        [Header("Ink")]
        [Tooltip("Rings, spokes and the centre mark.")]
        [SerializeField] private Color grid = new(0.42f, 1f, 0.6f, 0.35f);

        [Tooltip("Contacts and the beam.")]
        [SerializeField] private Color phosphor = new(0.42f, 1f, 0.6f, 1f);

        private readonly List<Blip> blips = new();
        private float sweep;

        /// <summary>
        /// Hands the plot everything it draws. Cheap: it only stores, and asks for one rebuild.
        /// </summary>
        /// <param name="contacts">Contacts in plot space. Copied, so the caller keeps its list.</param>
        /// <param name="sweep01">Beam phase, 0..1 of a full turn, clockwise from forward.</param>
        public void Present(IReadOnlyList<Blip> contacts, float sweep01)
        {
            blips.Clear();
            for (int i = 0; contacts != null && i < contacts.Count; i++) blips.Add(contacts[i]);
            sweep = sweep01;
            SetVerticesDirty();
        }

        /// <summary>Nothing on the plot. For a display that has just been switched off.</summary>
        public void Clear()
        {
            if (blips.Count == 0) return;
            blips.Clear();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect r = rectTransform.rect;
            float radius = Mathf.Min(r.width, r.height) * 0.5f - lineWidth;
            if (radius <= lineWidth) return;

            Vector2 centre = r.center;

            for (int i = 1; i <= rings; i++)
                AddRing(vh, centre, radius * i / rings, grid);

            // Two spokes rather than a full graticule: the plot is small, and the axes are all a
            // reader needs to tell "ahead" from "to my left" at a glance.
            AddQuad(vh, centre + new Vector2(-radius, -lineWidth * 0.5f),
                        centre + new Vector2(radius, lineWidth * 0.5f), grid);
            AddQuad(vh, centre + new Vector2(-lineWidth * 0.5f, -radius),
                        centre + new Vector2(lineWidth * 0.5f, radius), grid);

            AddSweep(vh, centre, radius);

            // The holder, at the middle of their own plot.
            AddQuad(vh, centre - Vector2.one * lineWidth, centre + Vector2.one * lineWidth, phosphor);

            foreach (Blip blip in blips) AddBlip(vh, centre, radius, blip);
        }

        /// <summary>A circle as a strip of quads, one per segment.</summary>
        private void AddRing(VertexHelper vh, Vector2 centre, float radius, Color colour)
        {
            float half = lineWidth * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * 2f * Mathf.PI / segments;
                float a1 = (i + 1) * 2f * Mathf.PI / segments;
                Vector2 d0 = new(Mathf.Sin(a0), Mathf.Cos(a0));
                Vector2 d1 = new(Mathf.Sin(a1), Mathf.Cos(a1));

                int b = vh.currentVertCount;
                vh.AddVert(centre + d0 * (radius - half), colour, Vector2.zero);
                vh.AddVert(centre + d0 * (radius + half), colour, Vector2.zero);
                vh.AddVert(centre + d1 * (radius + half), colour, Vector2.zero);
                vh.AddVert(centre + d1 * (radius - half), colour, Vector2.zero);
                vh.AddTriangle(b, b + 1, b + 2);
                vh.AddTriangle(b, b + 2, b + 3);
            }
        }

        /// <summary>
        /// The beam: a fan from the centre spanning <see cref="sweepArc"/> behind the leading edge,
        /// bright at the edge and transparent at the tail.
        /// </summary>
        private void AddSweep(VertexHelper vh, Vector2 centre, float radius)
        {
            if (sweepAlpha <= 0f) return;

            int steps = Mathf.Max(3, Mathf.RoundToInt(segments * sweepArc / 360f));
            float lead = sweep * 2f * Mathf.PI;

            for (int i = 0; i < steps; i++)
            {
                float t0 = i / (float)steps;
                float t1 = (i + 1) / (float)steps;

                // Clockwise from forward, so the beam turns the way the numbers on a compass do.
                float a0 = lead - t0 * sweepArc * Mathf.Deg2Rad;
                float a1 = lead - t1 * sweepArc * Mathf.Deg2Rad;

                Color c0 = Tint(phosphor, sweepAlpha * (1f - t0));
                Color c1 = Tint(phosphor, sweepAlpha * (1f - t1));

                int b = vh.currentVertCount;
                vh.AddVert(centre, Tint(phosphor, sweepAlpha * 0.35f), Vector2.zero);
                vh.AddVert(centre + new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)) * radius, c0, Vector2.zero);
                vh.AddVert(centre + new Vector2(Mathf.Sin(a1), Mathf.Cos(a1)) * radius, c1, Vector2.zero);
                vh.AddTriangle(b, b + 1, b + 2);
            }
        }

        /// <summary>
        /// One contact. The glyph is a SHAPE per class, not a colour per class: the plate is one
        /// colour of phosphor, and a colour-coded plot would say nothing to a reader who cannot
        /// separate the hues anyway.
        /// </summary>
        private void AddBlip(VertexHelper vh, Vector2 centre, float radius, Blip blip)
        {
            if (blip.Strength <= 0.01f) return;

            Vector2 at = centre + Vector2.ClampMagnitude(blip.Plot, 1f) * radius;
            Color colour = Tint(phosphor, blip.Strength);
            float s = blipSize * 0.5f;
            float bar = lineWidth;

            switch (blip.Class)
            {
                case ScanClass.Container:
                    // A hollow square: four bars, so it reads as a box rather than a lump.
                    AddQuad(vh, at + new Vector2(-s, s - bar), at + new Vector2(s, s), colour);
                    AddQuad(vh, at + new Vector2(-s, -s), at + new Vector2(s, -s + bar), colour);
                    AddQuad(vh, at + new Vector2(-s, -s), at + new Vector2(-s + bar, s), colour);
                    AddQuad(vh, at + new Vector2(s - bar, -s), at + new Vector2(s, s), colour);
                    break;

                case ScanClass.Site:
                    AddTriangle(vh, at + new Vector2(0f, s), at + new Vector2(s, -s),
                                    at + new Vector2(-s, -s), colour);
                    break;

                case ScanClass.Signal:
                    // A cross, which is the one glyph still readable when two contacts overlap.
                    AddQuad(vh, at + new Vector2(-s, -bar * 0.5f), at + new Vector2(s, bar * 0.5f), colour);
                    AddQuad(vh, at + new Vector2(-bar * 0.5f, -s), at + new Vector2(bar * 0.5f, s), colour);
                    break;

                default:
                    AddQuad(vh, at - Vector2.one * s * 0.7f, at + Vector2.one * s * 0.7f, colour);
                    break;
            }
        }

        private static void AddQuad(VertexHelper vh, Vector2 min, Vector2 max, Color colour)
        {
            int b = vh.currentVertCount;
            vh.AddVert(new Vector3(min.x, min.y), colour, Vector2.zero);
            vh.AddVert(new Vector3(min.x, max.y), colour, Vector2.zero);
            vh.AddVert(new Vector3(max.x, max.y), colour, Vector2.zero);
            vh.AddVert(new Vector3(max.x, min.y), colour, Vector2.zero);
            vh.AddTriangle(b, b + 1, b + 2);
            vh.AddTriangle(b, b + 2, b + 3);
        }

        private static void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color colour)
        {
            int at = vh.currentVertCount;
            vh.AddVert(new Vector3(a.x, a.y), colour, Vector2.zero);
            vh.AddVert(new Vector3(b.x, b.y), colour, Vector2.zero);
            vh.AddVert(new Vector3(c.x, c.y), colour, Vector2.zero);
            vh.AddTriangle(at, at + 1, at + 2);
        }

        private static Color Tint(Color colour, float alpha) =>
            new(colour.r, colour.g, colour.b, colour.a * Mathf.Clamp01(alpha));
    }
}
