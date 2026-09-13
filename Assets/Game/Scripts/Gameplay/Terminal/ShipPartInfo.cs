using System.Collections.Generic;
using System.Text;
using SpaceGame.Vehicles;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// What the terminal calls each hull module and what it tells the crew the module is for.
    ///
    /// <para>
    /// Pure, and here rather than on the item assets, for the reason <see cref="ShipTelemetry"/>
    /// gives: the words a player reads belong in one place that can be asserted without a scene.
    /// <c>InventoryItem</c> carries a name for the thing in your hands — "Nuclear Motor" — and
    /// nothing about what the hull does without it, which is the only question the schematic is
    /// asked.
    /// </para>
    /// <para>
    /// Every <see cref="ShipPartKind"/> must have both lines. A kind appended to the enum with no
    /// entry here fails <c>TerminalTests.ShipPartInfo_HasWordsForEveryKind</c> rather than
    /// shipping a module whose panel is blank.
    /// </para>
    /// </summary>
    public static class ShipPartInfo
    {
        /// <summary>The module's name in the terminal's own voice — upper case, as the glass draws it.</summary>
        public static string Name(ShipPartKind kind) => kind switch
        {
            ShipPartKind.AntiGravity => "ANTI-GRAV SPINE",
            ShipPartKind.NuclearMotor => "NUCLEAR MOTOR",
            ShipPartKind.ReactorCore => "REACTOR CORE",
            ShipPartKind.SmallMotor => "BELLY TURBINE",
            ShipPartKind.AirIntake => "NOSE INTAKE",
            ShipPartKind.LongTurbine => "FLANK TURBINE",
            ShipPartKind.Gun => "DECK GUN",
            _ => kind.ToString().ToUpperInvariant(),
        };

        /// <summary>What the module does, in the two sentences a readout has room for.</summary>
        public static string Function(ShipPartKind kind) => kind switch
        {
            ShipPartKind.AntiGravity =>
                "Carries the hull's weight along the starboard flank. Without it the lander is "
                + "dead weight standing on its legs.",
            ShipPartKind.NuclearMotor =>
                "Main drive, on the roof. Two of them push the hull out of atmosphere; on one, "
                + "you will not make orbit.",
            ShipPartKind.ReactorCore =>
                "Fuel for the main drive. The motors turn over without a core fitted — they just "
                + "burn nothing.",
            ShipPartKind.SmallMotor =>
                "Belly turbine for hover and trim. Missing ones make the descent wallow.",
            ShipPartKind.AirIntake =>
                "Feeds the turbines air through the nose. With it gone, nothing that breathes "
                + "atmosphere runs.",
            ShipPartKind.LongTurbine =>
                "Atmospheric lift along the flank. This is what actually holds the hull up while "
                + "there is still air under it.",
            ShipPartKind.Gun =>
                "Starboard mount. Not needed to fly, and badly missed when something follows you "
                + "home.",
            _ => "No entry. Add this kind to ShipPartInfo.",
        };

        public static string StatusWord(bool installed) => installed ? "FITTED" : "MISSING";

        /// <summary>"FITTED    1 OF 2" — how many of this KIND are aboard, not this one socket.</summary>
        public static string FittedLine(int fitted, int total) => $"FITTED    {fitted} OF {total}";

        /// <summary>The line under a missing module: what the player is supposed to do about it.</summary>
        public static string ActionLine(bool installed) =>
            installed ? "NO ACTION REQUIRED" : "SALVAGE REQUIRED";

        /// <summary>The block under a picked module's name: what it is, how many of its kind are aboard, what to do.</summary>
        public static string Detail(bool installed, int fittedOfKind, int totalOfKind) =>
            $"STATUS    {StatusWord(installed)}\n{FittedLine(fittedOfKind, totalOfKind)}\n{ActionLine(installed)}";

        /// <summary>The hull's own answer, with nothing picked. Two words and a number.</summary>
        public static string OverviewCount(int installedMask, IReadOnlyList<ShipPartKind> kinds)
        {
            int total = kinds?.Count ?? 0;
            if (total == 0) return "NO MODULE RACK";

            return $"{CountInstalled(installedMask, total)} OF {total} FITTED";
        }

        /// <summary>
        /// What is still missing, with nothing picked. Listed by KIND, not by socket — a crew
        /// hunting two motors is hunting one kind of thing, and eleven rows of socket would be
        /// eleven rows nobody reads.
        /// </summary>
        public static string OverviewBody(int installedMask, IReadOnlyList<ShipPartKind> kinds)
        {
            int total = kinds?.Count ?? 0;
            if (total == 0) return "This terminal is not aboard a hull.";

            if (CountInstalled(installedMask, total) >= total)
                return "All modules aboard.\nThe lander is airworthy.";

            var sb = new StringBuilder();
            sb.AppendLine("STILL MISSING");
            foreach (ShipPartKind kind in MissingKinds(installedMask, kinds))
            {
                int need = TotalOfKind(kinds, kind) - FittedOfKind(installedMask, kinds, kind);
                sb.AppendLine(need > 1 ? $"  {Name(kind)}  x{need}" : $"  {Name(kind)}");
            }

            sb.Append("\nPick one for detail.");
            return sb.ToString();
        }

        /// <summary>Every kind with at least one empty socket, in socket order and without repeats.</summary>
        public static List<ShipPartKind> MissingKinds(int installedMask, IReadOnlyList<ShipPartKind> kinds)
        {
            var missing = new List<ShipPartKind>();
            if (kinds == null) return missing;

            for (int i = 0; i < kinds.Count; i++)
            {
                if (IsInstalled(installedMask, i)) continue;
                if (!missing.Contains(kinds[i])) missing.Add(kinds[i]);
            }
            return missing;
        }

        public static int FittedOfKind(int installedMask, IReadOnlyList<ShipPartKind> kinds, ShipPartKind kind)
        {
            int n = 0;
            for (int i = 0; kinds != null && i < kinds.Count; i++)
                if (kinds[i] == kind && IsInstalled(installedMask, i)) n++;
            return n;
        }

        public static int TotalOfKind(IReadOnlyList<ShipPartKind> kinds, ShipPartKind kind)
        {
            int n = 0;
            for (int i = 0; kinds != null && i < kinds.Count; i++)
                if (kinds[i] == kind) n++;
            return n;
        }

        public static int CountInstalled(int installedMask, int socketCount)
        {
            int n = 0;
            for (int i = 0; i < socketCount; i++)
                if (IsInstalled(installedMask, i)) n++;
            return n;
        }

        public static bool IsInstalled(int installedMask, int socketIndex) =>
            socketIndex >= 0 && socketIndex < ShipPartRack.MaxSockets &&
            (installedMask & (1 << socketIndex)) != 0;
    }
}
