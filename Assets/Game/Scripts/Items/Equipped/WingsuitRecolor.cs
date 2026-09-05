using System.Collections.Generic;
using SpaceGame.Characters;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Paints a wingsuit in its wearer's suit colour.
    ///
    /// <para>
    /// The gear-shaped half of <see cref="PaletteRecolor"/>, alongside <c>SuitRecolor</c> and
    /// <c>ShipAccentRecolor</c>: same machinery, its own table. It needs one because
    /// <see cref="SuitPalette.Relationships"/> is specifically the astronaut's material list —
    /// <c>SuitCustomizationTests</c> asserts every name in it exists on <c>astronaut.fbx</c>, which
    /// is what stops a stale entry silently tinting the wrong part of the model, and a wingsuit
    /// material has no business being on that list.
    /// </para>
    /// <para>
    /// The colour itself is not a second palette: the membrane takes the chosen swatch straight, so
    /// the wing and the harness it grows out of are the same colour by construction rather than by
    /// two tables agreeing.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class WingsuitRecolor : PaletteRecolor
    {
        /// <summary>
        /// The membrane, and nothing else. The spars and buckles are the suit's hardware greys —
        /// left alone for the reason the astronaut's neutrals are, because a wing flooded with one
        /// colour stops reading as cloth stretched over a frame.
        /// </summary>
        /// <summary>The Unity material name <c>WingsuitBuilder</c> creates for the cloth.</summary>
        public const string MembraneMaterial = "WingsuitMembrane";

        /// <summary>
        /// The worn wing's cloth. A second material rather than a second use of the first, because
        /// the two panels have different object spaces and want different wind amplitudes — see
        /// <c>WingsuitBuilder.WornWind</c>. Both are the wearer's suit colour, so both are here.
        /// </summary>
        public const string WornMembraneMaterial = "WingsuitWornMembrane";

        public static readonly SuitPalette.Relationship[] Membrane =
        {
            new(MembraneMaterial, 0f, 1f, 1f),
            new(WornMembraneMaterial, 0f, 1f, 1f),
        };

        protected override IReadOnlyList<SuitPalette.Relationship> Relationships => Membrane;

        /// <summary>Always a colour: every index means a swatch once clamped.</summary>
        protected override bool TryColorOf(int index, out Color chosen)
        {
            chosen = SuitPalette.ColorOf(index);
            return true;
        }

        /// <summary>Clamped rather than rejected — the value arrives from a peer and from a save.</summary>
        protected override int Normalise(int index) => SuitPalette.Clamp(index);

        protected override string NothingToPaintMessage =>
            $"[WingsuitRecolor] '{name}' has no material named {MembraneMaterial}, so the wings " +
            "will not take the player's suit colour. WingsuitBuilder is what names it — re-run " +
            "Tools ▸ SpaceGame ▸ Items ▸ Build Wingsuit.";
    }
}
