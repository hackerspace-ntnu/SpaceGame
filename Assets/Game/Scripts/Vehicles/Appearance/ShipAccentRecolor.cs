using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Presentation;

namespace SpaceGame.Vehicles
{
    /// <summary>
    /// Paints a ship's livery in a team's colour.
    ///
    /// <para>
    /// The ship-shaped half of <see cref="PaletteRecolor"/>, and the counterpart to
    /// <c>SuitRecolor</c>: same machinery, different table. That is the point — a team's ship and
    /// its crew are painted from the SAME swatch index by the same arithmetic, so they cannot come
    /// out different shades of the team's colour.
    /// </para>
    ///
    /// <para>
    /// Knows nothing about netcode. <see cref="ShipTeamAccent"/> is what puts a swatch on the wire
    /// and calls this on every machine; a dev tool or an editor preview can call it directly.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ShipAccentRecolor : PaletteRecolor
    {
        protected override IReadOnlyList<SuitPalette.Relationship> Relationships =>
            ShipAccentPalette.Relationships;

        /// <summary>
        /// False for <see cref="ShipAccentPalette.NoTeam"/>, which is what every ship outside a
        /// versus match carries. A story-world lander keeps the paint it was authored in rather
        /// than being repainted in swatch zero the moment this component wakes up.
        /// </summary>
        protected override bool TryColorOf(int index, out Color chosen)
        {
            // Reached only after Normalise, which folds every negative onto the sentinel — so this
            // is the whole of "no colour yet" rather than one case of it.
            if (index == ShipAccentPalette.NoTeam)
            {
                chosen = default;
                return false;
            }

            chosen = SuitPalette.ColorOf(index);
            return true;
        }

        /// <summary>
        /// Clamped into the palette, except for the "no team" sentinel, which has to survive: it is
        /// the difference between a hull nobody has claimed and one claimed by the first team.
        /// </summary>
        protected override int Normalise(int index) =>
            index < 0 ? ShipAccentPalette.NoTeam : SuitPalette.Clamp(index);

        /// <summary>
        /// Also strips <c>" (DoubleSided)"</c>. Every material on a lander carries it —
        /// <c>DoubleSidedMaterials</c> is run over the whole model by the ship builder — so without
        /// this the table matches nothing and the livery silently never appears.
        /// </summary>
        protected override string MatchNameOf(string materialName) =>
            ShipAccentPalette.BaseName(materialName);

        protected override string NothingToPaintMessage =>
            $"[ShipAccentRecolor] '{name}' has no material matching ShipAccentPalette.Relationships, " +
            "so team colours will do nothing on this hull. The ship's materials were probably " +
            "renamed by a rebuild — ShipAccentTests asserts the expected names and will say which.";
    }
}
