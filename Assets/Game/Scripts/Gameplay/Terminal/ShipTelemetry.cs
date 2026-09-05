using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// One reading of everything the ship's terminal shows. Plain data, filled by
    /// <see cref="ShipTelemetrySource"/> from the ship's own replicated components, so every
    /// machine composes the same screen from the same numbers.
    /// </summary>
    public struct TelemetrySnapshot
    {
        public bool OxygenPresent;
        public bool OxygenPowered;
        /// <summary>0..1, or -1 when no cell is fitted.</summary>
        public float OxygenBattery01;
        /// <summary>0..1, or -1 when no bottle is docked.</summary>
        public float OxygenTank01;
        public bool OxygenFilling;

        /// <summary>
        /// Which hull modules are fitted, as a bitmask over <see cref="PartKinds"/> — the ship's
        /// own replicated <c>ShipPartRack</c> mask, passed through unchanged.
        /// </summary>
        public int PartsInstalledMask;

        /// <summary>
        /// What kind of module each socket takes, in the rack's own order, so bit i and
        /// <c>PartKinds[i]</c> are the same socket. Empty when the terminal is not aboard a hull.
        /// </summary>
        public ShipPartKind[] PartKinds;

        public string[] CrewNames;
        /// <summary>Crew positions relative to the ship, metres, x across and y forward.</summary>
        public Vector2[] CrewOffsets;

        /// <summary>Storm intensity at the ship, 0..1.</summary>
        public float Storm01;

        /// <summary>Time of day as a fraction of the cycle, 0 = midnight.</summary>
        public float TimeOfDay01;

        public Vector3 Position;
        public float HeadingDegrees;

        public static TelemetrySnapshot Empty => new()
        {
            OxygenBattery01 = -1f,
            OxygenTank01 = -1f,
            PartKinds = System.Array.Empty<ShipPartKind>(),
            CrewNames = System.Array.Empty<string>(),
            CrewOffsets = System.Array.Empty<Vector2>(),
        };
    }

    /// <summary>What a subsystem's pip on the schematic should say about it.</summary>
    public enum PipState { Ok, Warn, Fault }

    /// <summary>
    /// Turns a <see cref="TelemetrySnapshot"/> into the text the terminal draws. Pure functions,
    /// so a page can be asserted from a snapshot without a scene, and so the words a player reads
    /// live in one place rather than scattered through UI code.
    /// </summary>
    public static class ShipTelemetry
    {
        /// <summary>Storm intensity above which the terminal calls it a storm rather than an advisory.</summary>
        public const float StormThreshold = 0.35f;

        /// <summary>Radar range of the GPS page's crew plot, metres.</summary>
        public const float RadarRange = 250f;

        public static string Clock(float timeOfDay01)
        {
            float hours = Mathf.Repeat(timeOfDay01, 1f) * 24f;
            int h = Mathf.FloorToInt(hours);
            int m = Mathf.FloorToInt((hours - h) * 60f);
            return $"{h:00}:{m:00}";
        }

        public static string OxygenLine(in TelemetrySnapshot s)
        {
            if (!s.OxygenPresent) return "O2 PLANT         NOT FITTED";
            if (!s.OxygenPowered) return "O2 PLANT         NO POWER";

            string cell = s.OxygenBattery01 >= 0f ? $"CELL {Mathf.RoundToInt(s.OxygenBattery01 * 100f)}%" : "NO CELL";
            string tank = s.OxygenFilling ? "FILLING"
                        : s.OxygenTank01 >= 0f ? $"BOTTLE {Mathf.RoundToInt(s.OxygenTank01 * 100f)}%"
                        : "NO BOTTLE";
            return $"O2 PLANT         ONLINE  {cell}  {tank}";
        }

        public static string StormLine(in TelemetrySnapshot s)
        {
            if (s.Storm01 >= StormThreshold) return $"WEATHER          SANDSTORM  {Mathf.RoundToInt(s.Storm01 * 100f)}%";
            if (s.Storm01 > 0f) return $"WEATHER          ADVISORY   {Mathf.RoundToInt(s.Storm01 * 100f)}%";
            return "WEATHER          CLEAR";
        }

        /// <summary>The hull's modules as one line: what the salvage loop is actually asking for.</summary>
        public static string ModulesLine(in TelemetrySnapshot s)
        {
            int total = s.PartKinds?.Length ?? 0;
            if (total == 0) return "HULL MODULES     NO RACK";

            int fitted = ShipPartInfo.CountInstalled(s.PartsInstalledMask, total);
            string state = fitted >= total ? "COMPLETE" : "INCOMPLETE";
            return $"HULL MODULES     {state}  {fitted}/{total}";
        }

        public static string CrewLine(in TelemetrySnapshot s)
        {
            int count = s.CrewNames?.Length ?? 0;
            if (count == 0) return "CREW             NONE ABOARD";
            return $"CREW             {count}  " + string.Join(", ", s.CrewNames);
        }

        /// <summary>The STATUS page: one line per subsystem.</summary>
        public static string StatusPage(in TelemetrySnapshot s)
        {
            var sb = new StringBuilder();
            sb.AppendLine(ModulesLine(s));
            sb.AppendLine(OxygenLine(s));
            sb.AppendLine(StormLine(s));
            sb.AppendLine(CrewLine(s));
            sb.Append($"SHIP TIME        {Clock(s.TimeOfDay01)}");
            return sb.ToString();
        }

        /// <summary>The GPS page's readout: where the ship is and which way it points.</summary>
        public static string GpsPage(in TelemetrySnapshot s)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"POS  X {s.Position.x,8:0.0}   Z {s.Position.z,8:0.0}");
            sb.AppendLine($"ALT  {s.Position.y,8:0.0} m");
            sb.AppendLine($"HDG  {Mathf.Repeat(s.HeadingDegrees, 360f),5:000}°  {Compass(s.HeadingDegrees)}");
            sb.Append($"CREW IN RANGE  {CountInRange(s.CrewOffsets, RadarRange)}  (R {RadarRange:0} m)");
            return sb.ToString();
        }

        public static string Compass(float headingDegrees)
        {
            string[] points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int index = Mathf.RoundToInt(Mathf.Repeat(headingDegrees, 360f) / 45f) % 8;
            return points[index];
        }

        public static int CountInRange(IReadOnlyList<Vector2> offsets, float range)
        {
            int n = 0;
            if (offsets == null) return 0;
            for (int i = 0; i < offsets.Count; i++)
                if (offsets[i].magnitude <= range) n++;
            return n;
        }

        public static PipState OxygenPip(in TelemetrySnapshot s) =>
            !s.OxygenPresent || !s.OxygenPowered ? PipState.Fault
            : s.OxygenTank01 < 0f && !s.OxygenFilling ? PipState.Warn
            : PipState.Ok;

        /// <summary>The hull's own pip: whole, part-way, or stripped.</summary>
        public static PipState ModulesPip(in TelemetrySnapshot s)
        {
            int total = s.PartKinds?.Length ?? 0;
            if (total == 0) return PipState.Fault;

            int fitted = ShipPartInfo.CountInstalled(s.PartsInstalledMask, total);
            return fitted >= total ? PipState.Ok : fitted > 0 ? PipState.Warn : PipState.Fault;
        }

        public static PipState WeatherPip(in TelemetrySnapshot s) =>
            s.Storm01 >= StormThreshold ? PipState.Fault : s.Storm01 > 0f ? PipState.Warn : PipState.Ok;

        /// <summary>One reading of one subsystem, as the strip under the schematic states it.</summary>
        public readonly struct Segment
        {
            public readonly string Text;
            public readonly PipState State;

            public Segment(string text, PipState state)
            {
                Text = text;
                State = state;
            }
        }

        /// <summary>
        /// The strip under the schematic: every subsystem in four words, each carrying its own
        /// state so the screen can colour it.
        ///
        /// <para>
        /// Words AND colour on the same run of text, rather than a row of coloured pips beside a
        /// row of words. The pips were the faster read and the words were the precise one, and
        /// keeping both meant saying everything twice on a screen 600 units wide.
        /// </para>
        /// <para>
        /// Modules first: they are what the drawing above the strip is about, and the only line
        /// on it the crew can do anything about today.
        /// </para>
        /// </summary>
        public static Segment[] SummarySegments(in TelemetrySnapshot s)
        {
            int total = s.PartKinds?.Length ?? 0;
            int fitted = ShipPartInfo.CountInstalled(s.PartsInstalledMask, total);

            return new[]
            {
                new Segment($"MOD {fitted}/{total}", ModulesPip(s)),
                new Segment($"O2 {(OxygenPip(s) == PipState.Ok ? "OK" : OxygenPip(s) == PipState.Warn ? "LOW" : "OFF")}", OxygenPip(s)),
                new Segment($"WX {(WeatherPip(s) == PipState.Ok ? "CLEAR" : WeatherPip(s) == PipState.Warn ? "ADVISORY" : "STORM")}", WeatherPip(s)),
            };
        }
    }
}
