using System;

namespace SpaceGame.Vehicles
{
    /// <summary>
    /// How a hull module's mesh is named, and how to read a <see cref="ShipPartKind"/> back out of
    /// it: <c>Part_&lt;Kind&gt;_&lt;Side&gt;</c>, written by <c>ship_parts.py</c> at export.
    ///
    /// <para>
    /// Here rather than in the builder that first needed it because the convention now has three
    /// readers — the ship's own sockets, the terminal's miniature, and the export script that
    /// writes the names — and a convention with three copies is a convention with three versions.
    /// </para>
    /// </summary>
    public static class ShipPartNaming
    {
        /// <summary>What every module mesh's name starts with. Matches <c>ROLE_PREFIX</c> in ship_parts.py.</summary>
        public const string Prefix = "Part_";

        public static bool IsPart(string meshName) =>
            !string.IsNullOrEmpty(meshName) && meshName.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>Reads the kind out of <c>Part_&lt;Kind&gt;_&lt;Side&gt;</c>. False for anything else.</summary>
        public static bool TryParseKind(string meshName, out ShipPartKind kind)
        {
            kind = default;
            if (!IsPart(meshName)) return false;

            int start = Prefix.Length;
            int end = meshName.LastIndexOf('_');
            if (end <= start) return false;

            return Enum.TryParse(meshName.Substring(start, end - start), out kind);
        }
    }
}
