// The settlement-wide tallies of a residents baseline run, for the summary: rows written per boundary
// kind, and plan segments closed and reached per activity.
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SpaceGame.Agents.Residents;

namespace SpaceGame.Agents
{
    internal sealed class ResidentBaselineCounts
    {
        private readonly SortedDictionary<string, int> rowsByBoundary = new();
        private readonly SortedDictionary<Activity, int> closedByActivity = new();
        private readonly SortedDictionary<Activity, int> reachedByActivity = new();

        public int Rows { get; private set; }

        public void NoteRow(string boundary)
        {
            Rows++;
            rowsByBoundary.TryGetValue(boundary, out int count);
            rowsByBoundary[boundary] = count + 1;
        }

        public void NoteSegment(Activity activity, bool reached)
        {
            closedByActivity.TryGetValue(activity, out int closed);
            closedByActivity[activity] = closed + 1;
            if (!reached) return;
            reachedByActivity.TryGetValue(activity, out int count);
            reachedByActivity[activity] = count + 1;
        }

        public void AppendRows(StringBuilder report)
        {
            report.Append("rows: ").Append(Rows).Append(" (");
            bool first = true;
            foreach (KeyValuePair<string, int> entry in rowsByBoundary)
            {
                if (!first) report.Append(", ");
                report.Append(entry.Key).Append(' ').Append(entry.Value);
                first = false;
            }
            report.AppendLine(")");
        }

        public void AppendSegments(StringBuilder report, float reachedWithin)
        {
            int closed = 0, reached = 0;
            foreach (KeyValuePair<Activity, int> entry in closedByActivity)
            {
                closed += entry.Value;
                reachedByActivity.TryGetValue(entry.Key, out int count);
                reached += count;
            }

            report.Append("segments closed: ").Append(closed)
                  .Append(", reached within ").Append(reachedWithin.ToString("0.#", CultureInfo.InvariantCulture)).Append(" m: ")
                  .Append(reached).Append(" (").Append(Percent(reached, closed)).AppendLine(")");

            foreach (KeyValuePair<Activity, int> entry in closedByActivity)
            {
                reachedByActivity.TryGetValue(entry.Key, out int count);
                report.Append("  ").Append(entry.Key).Append(": ").Append(count).Append('/').Append(entry.Value)
                      .Append(" (").Append(Percent(count, entry.Value)).AppendLine(")");
            }
        }

        private static string Percent(int part, int whole) =>
            whole > 0 ? (100f * part / whole).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "n/a";
    }
}
