using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.Vehicles
{
    /// <summary>
    /// Which parts of a ship wear a team's colour, and how.
    ///
    /// <para>
    /// The colour itself is not defined here. A team's colour IS a <see cref="SuitPalette"/> swatch
    /// index — that is what the lobby picks, what <c>TeamColorRules</c> steps through and what
    /// <c>VersusSession.ColorOf</c> answers with — so a second palette for ships would be a second
    /// table that has to agree with the first, forever. A team's ship and its crew wear the same
    /// swatch by construction.
    /// </para>
    ///
    /// <para>
    /// What IS here is the ship's own answer to "which materials are paint". The lander is mostly
    /// sand-coloured hull, greys and glass, with four materials authored as painted surfaces. Those
    /// four are the livery; everything else is deliberately left alone, for the reason
    /// <see cref="SuitPalette.Relationships"/> gives about the astronaut — a hull flooded with one
    /// colour stops reading as the same ship, and the canopy stops reading as glass.
    /// </para>
    ///
    /// <para>
    /// Offsets are measured against <c>Mat_Paint_Safety_Orange</c> as it ships today (#D9541F —
    /// H 17.0°, S 0.857, V 0.851), the most saturated of the four and therefore the one that reads
    /// as the ship's paint. Keeping the relationships rather than flooding all four with the same
    /// value is what keeps the nacelles a shade off the hatch and the secondary hull panels a muted
    /// version of both, so the livery still has depth whatever swatch a team wears.
    /// </para>
    /// </summary>
    public static class ShipAccentPalette
    {
        /// <summary>No team has claimed this hull, so its authored paint stands.</summary>
        public const int NoTeam = -1;

        /// <summary>
        /// The painted materials, and where each sits relative to the chosen colour.
        ///
        /// <para>
        /// Reusing <see cref="SuitPalette.Relationship"/> rather than declaring a ship-shaped copy
        /// of it: the type is a material name plus an HSV offset, which is exactly what this needs,
        /// and <see cref="SuitPalette.Derive"/> then applies both tables by the same arithmetic.
        /// </para>
        /// </summary>
        public static readonly SuitPalette.Relationship[] Relationships =
        {
            // The reference itself — hazard stripes and painted trim take the team colour directly.
            new("Mat_Paint_Safety_Orange", 0f, 1f, 1f),

            new("Mat_Lander_Door", 15.7f, 0.935f, 1.000f),           // hatch — a touch warmer
            new("Mat_Lander_Nacelle", 17.4f, 0.967f, 0.914f),        // engine housings — warmer, deeper
            new("Mat_Lander_Hull_Secondary", 8.4f, 0.653f, 0.952f),  // panelling — the muted version
        };

        /// <summary>
        /// The material name underneath a renderer's, with the two suffixes this project adds.
        ///
        /// <para>
        /// <c>" (Instance)"</c> is Unity's, on any material read off a live renderer that has been
        /// cloned. <c>" (DoubleSided)"</c> is <c>DoubleSidedMaterials</c>', which the ship builder
        /// runs over the whole model — so EVERY material on a lander carries it, and a table
        /// matching raw names would match nothing at all and paint nothing, silently.
        /// </para>
        /// </summary>
        public static string BaseName(string materialName)
        {
            if (string.IsNullOrEmpty(materialName)) return materialName;

            return StripSuffix(StripSuffix(materialName, " (Instance)"), " (DoubleSided)");
        }

        /// <summary>
        /// The colour a material takes for a swatch index, or false when this material is not ours
        /// — or when the hull belongs to no team, which is the same answer for the same reason:
        /// there is no colour to paint it, and swatch zero is not a stand-in for one.
        /// </summary>
        public static bool TryDerive(int swatchIndex, string materialName, out Color color)
        {
            color = default;

            if (swatchIndex < 0) return false;

            string bare = BaseName(materialName);

            for (int i = 0; i < Relationships.Length; i++)
            {
                if (Relationships[i].MaterialName != bare) continue;

                color = SuitPalette.Derive(SuitPalette.ColorOf(swatchIndex), Relationships[i]);
                return true;
            }

            return false;
        }

        private static string StripSuffix(string value, string suffix) =>
            value.EndsWith(suffix) ? value[..^suffix.Length] : value;
    }
}
