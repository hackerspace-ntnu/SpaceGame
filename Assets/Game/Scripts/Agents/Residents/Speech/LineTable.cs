// The line library as data. Writers edit a tab-separated spreadsheet, not a ScriptableObject, and a
// row is sent on the wire by the FNV-1a hash of its KEY — never its row number — so inserting,
// sorting or deleting rows can never make one machine say a different line than another.
//
// Parsing never throws: every malformed row is skipped and described in Errors, which the
// validator, the inspector and the shipped-file test all read.
using System;
using System.Collections.Generic;
using System.Text;

namespace SpaceGame.Agents.Residents
{
    /// <summary>One authored line. A null criterion matches anything.</summary>
    public sealed class LineRow
    {
        public uint id;
        public string key, speaker, text;
        public Topic? topic;
        public string stance;
        public Observation? observation;
        public Activity? activity;
        public Register? repliesTo;
        public Register register;

        private StanceFamily? stanceFamily;
        private Stance? exactStance;

        /// <summary>True when the stance cell is empty, names <paramref name="s"/>, or names its family.</summary>
        public bool MatchesStance(Stance s) =>
            exactStance.HasValue ? exactStance.Value == s
            : !stanceFamily.HasValue || stanceFamily.Value == Attitude.FamilyOf(s);

        internal bool TrySetStance(string cell)
        {
            stance = cell;
            if (cell == null) return true;
            // A family wins over the stance of the same name: "hostile" in the sheet means the bucket.
            if (Enum.TryParse(cell, true, out StanceFamily family)) stanceFamily = family;
            else if (Enum.TryParse(cell, true, out Stance exact)) exactStance = exact;
            else return false;
            return true;
        }
    }

    public sealed class LineTable
    {
        private static readonly string[] Columns =
            { "key", "speaker", "topic", "stance", "observation", "activity", "repliesTo", "register", "text" };

        /// <summary>A whole sentence may name at most this many people or things.</summary>
        public const int MaxTokensPerLine = 2;

        private readonly List<LineRow> rows = new();
        private readonly Dictionary<uint, LineRow> byId = new();
        private readonly List<string> errors = new();

        public IReadOnlyList<LineRow> Rows => rows;
        public IReadOnlyList<string> Errors => errors;

        public bool TryGet(uint id, out LineRow row) => byId.TryGetValue(id, out row);

        /// <summary>FNV-1a, 32 bit, over the key's UTF-8 bytes. The wire format of a line.</summary>
        public static uint IdOf(string key)
        {
            const uint offsetBasis = 2166136261u;
            const uint prime = 16777619u;

            uint hash = offsetBasis;
            foreach (byte b in Encoding.UTF8.GetBytes(key ?? string.Empty))
                hash = unchecked((hash ^ b) * prime);
            return hash;
        }

        public static LineTable Parse(string tsv)
        {
            var table = new LineTable();
            int[] column = null;
            string[] lines = (tsv ?? string.Empty).Split('\n');

            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n].TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;

                string[] cells = line.Split('\t');
                if (column == null)
                {
                    column = table.ReadHeader(cells);
                    if (column == null) return table;
                    continue;
                }

                table.ReadRow(cells, column, n + 1);
            }

            if (column == null) table.errors.Add("No header row.");
            return table;
        }

        private int[] ReadHeader(string[] cells)
        {
            var column = new int[Columns.Length];
            for (int c = 0; c < Columns.Length; c++)
            {
                column[c] = Array.FindIndex(cells, h => string.Equals(h.Trim(), Columns[c], StringComparison.OrdinalIgnoreCase));
                if (column[c] >= 0) continue;
                errors.Add($"Header is missing the '{Columns[c]}' column.");
                return null;
            }
            return column;
        }

        private void ReadRow(string[] cells, int[] column, int lineNumber)
        {
            string Cell(int c) => column[c] < cells.Length && cells[column[c]].Trim().Length > 0 ? cells[column[c]].Trim() : null;

            var row = new LineRow { key = Cell(0), speaker = Cell(1), text = Cell(8) };
            string at = $"line {lineNumber} ('{row.key}')";
            if (row.key == null || row.text == null)
            {
                errors.Add($"{at}: key and text are required.");
                return;
            }

            bool stanceKnown = row.TrySetStance(Cell(3));
            if (!stanceKnown) errors.Add($"{at}: unknown stance '{row.stance}'.");

            bool ok = stanceKnown
                      & TryEnum(Cell(2), at, "topic", out row.topic)
                      & TryEnum(Cell(4), at, "observation", out row.observation)
                      & TryEnum(Cell(5), at, "activity", out row.activity)
                      & TryEnum(Cell(6), at, "repliesTo", out row.repliesTo)
                      & TryEnum(Cell(7), at, "register", out Register? register);
            if (!ok) return;

            row.register = register ?? Register.Statement;
            row.id = IdOf(row.key);

            int tokens = SpeechTokens.CountTokens(row.text, out string unknown);
            if (unknown != null) errors.Add($"{at}: unknown token '{unknown}'.");
            else if (tokens > MaxTokensPerLine) errors.Add($"{at}: {tokens} tokens; a line may name at most {MaxTokensPerLine}.");
            else if (byId.TryGetValue(row.id, out LineRow first))
                errors.Add(first.key == row.key ? $"{at}: duplicate key." : $"{at}: id collides with '{first.key}' — rename the key.");
            else
            {
                rows.Add(row);
                byId.Add(row.id, row);
            }
        }

        private bool TryEnum<T>(string cell, string at, string name, out T? value) where T : struct, Enum
        {
            value = null;
            if (cell == null) return true;
            if (Enum.TryParse(cell, true, out T parsed) && Enum.IsDefined(typeof(T), parsed))
            {
                value = parsed;
                return true;
            }

            errors.Add($"{at}: unknown {name} '{cell}'.");
            return false;
        }
    }
}
