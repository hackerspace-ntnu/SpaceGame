using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// The fill bar on a carried reservoir: how full an oxygen tank or a battery is, drawn on the
    /// object itself as a bar that grows from one end and ramps green → amber → red as it drains.
    ///
    /// <para>
    /// <b>A handle resolved by NAME, not a component.</b> The same bar has to read in three places
    /// and only one of them has scripts: the live item in the hand or the sand, the inert copy
    /// standing in the oxygen plant's collar, and the inert copy lying on a pack mat or the ship's
    /// gear wall. <c>DisplayCopy.Strip</c> takes every MonoBehaviour off the last two, so a copy has
    /// no <see cref="DockableSupply"/> to ask and never will. Binding by child name is what lets one
    /// painter serve all three — the trick <c>OxygenGenerator</c> already used privately for the
    /// flat tint this replaces, promoted to the seam so every call site is one line.
    /// </para>
    /// <para>
    /// <b>The BAR is the message and the colour only confirms it.</b> Green→amber→red is exactly the
    /// axis red-green colourblind players lose, so a hue-only gauge — which is what this replaced —
    /// tells perhaps one player in twelve nothing at all (<c>GDC-L1-UX-0003</c>,
    /// <c>GDC-L1-UX-0006</c>: never encode information in colour alone). Length carries the reading;
    /// the ramp is a second, redundant channel on top of it. That is also why the battery's authored
    /// five-bar ladder was a defensible design before this existed: it was a COUNT rather than a hue.
    /// A bar keeps that property and adds resolution.
    /// </para>
    /// <para>
    /// <b>Bind once, paint often.</b> <see cref="Bind"/> walks the hierarchy; <see cref="Paint"/>
    /// does not. The plant repaints a filling bottle every frame, and a per-frame
    /// <c>GetComponentsInChildren</c> over an item is the kind of cost that never shows up in a test
    /// and shows up in a profile.
    /// </para>
    /// </summary>
    public readonly struct SupplyGauge
    {
        /// <summary>
        /// The empty parent at the LOW end of the bar, scaled along its own local <b>+X</b> to fill
        /// it. An anchor rather than the quad itself because scaling a quad scales it about its own
        /// middle, which grows a bar from the centre outwards in both directions; the pivot has to
        /// be the end the fill starts from, and an empty parent is the cheapest pivot there is —
        /// no shader, no UVs and no custom mesh.
        /// </summary>
        public const string AnchorName = "Gauge_Anchor";

        /// <summary>The lit part, the child of <see cref="AnchorName"/> that the ramp paints.</summary>
        public const string FillName = "Gauge_Fill";

        /// <summary>
        /// The dark backing the fill runs over, and it is <b>not</b> decoration: on both models the
        /// geometry underneath is authored permanently lit — the bottle's CRT strip is an emissive
        /// material and the battery's ladder has three of five bars baked on. Without something
        /// opaque in front of them an empty tank reads as a full one.
        /// </summary>
        public const string TrackName = "Gauge_Track";

        /// <summary>
        /// Full. <c>Mat_Emissive_Green_CRT</c> (#6BFF9E) from the shared Blender palette.
        ///
        /// <para>
        /// The three stops are the palette's own indicator triad rather than three colours invented
        /// here, so a supply gauge reads as the same instrument family as every other lamp in the
        /// game. They are constants and not serialized fields on purpose: two of the three things
        /// this paints have had their components stripped, so a value that lived in an Inspector
        /// would be honoured on the item in your hand and silently ignored on the same item lying on
        /// the mat beside it.
        /// </para>
        /// </summary>
        public static readonly Color Full = new(0.420f, 1.000f, 0.620f);

        /// <summary>Half empty. <c>Mat_Emissive_Amber</c> (#FFB347).</summary>
        public static readonly Color Mid = new(1.000f, 0.702f, 0.278f);

        /// <summary>Nearly out. <c>Mat_Emissive_Red_Warn</c> (#FF4436).</summary>
        public static readonly Color Low = new(1.000f, 0.267f, 0.212f);

        /// <summary>
        /// Where <see cref="Mid"/> lands, so the ramp is two straight lerps rather than one through
        /// a muddy green-red middle. At the half mark because that is where a phone battery turns:
        /// the convention is already in the player's head and borrowing it costs nothing
        /// (<c>GDC-L1-UX-0004</c>).
        /// </summary>
        public const float MidStop = 0.5f;

        private readonly Transform anchor;
        private readonly Renderer fill;

        private SupplyGauge(Transform anchor, Renderer fill)
        {
            this.anchor = anchor;
            this.fill = fill;
        }

        /// <summary>Whether <paramref name="root"/> actually carries a bar. A rifle does not.</summary>
        public bool Exists => anchor != null;

        /// <summary>
        /// Find the bar under <paramref name="root"/>. Walks the hierarchy once; the result is
        /// <see cref="Exists"/>-false and harmless for anything with no gauge on it, which is most
        /// items and every display copy of one.
        /// </summary>
        public static SupplyGauge Bind(Transform root)
        {
            if (root == null) return default;

            Transform found = null;
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name != AnchorName) continue;

                found = all[i];
                break;
            }

            if (found == null) return default;

            Transform quad = found.Find(FillName);
            return new SupplyGauge(found, quad != null ? quad.GetComponent<Renderer>() : null);
        }

        /// <summary>
        /// Draw <paramref name="charge01"/>: the bar's length and its colour together. Clamped,
        /// because every caller arrives from a drain or a fill by a delta and an unclamped one would
        /// draw a bar past the end of its own track.
        /// </summary>
        public void Paint(float charge01)
        {
            if (anchor == null) return;

            float charge = Mathf.Clamp01(charge01);

            // Zero is a real value here, not a degenerate one: a quad at zero scale draws nothing,
            // which is exactly what an empty tank should show. Nothing inverts this transform —
            // ItemBounds only ever multiplies by it — so the usual zero-scale hazard does not apply.
            anchor.localScale = new Vector3(charge, 1f, 1f);

            EmissiveLamp.Paint(fill, ColourAt(charge));
        }

        /// <summary>
        /// The ramp. Two straight lerps meeting at <see cref="MidStop"/>, so the reading passes
        /// through amber rather than through the grey-brown that a single green→red lerp crosses at
        /// the halfway point.
        /// </summary>
        public static Color ColourAt(float charge01)
        {
            float charge = Mathf.Clamp01(charge01);

            return charge >= MidStop
                ? Color.Lerp(Mid, Full, (charge - MidStop) / (1f - MidStop))
                : Color.Lerp(Low, Mid, charge / MidStop);
        }
    }
}
