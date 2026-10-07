// The pure half of the /exp debug commands (ExpeditionCommands): what a typed line asks for, which settlement or
// band it names, and how the answer is laid out for chat. Plain values in, plain values out, so every rule of the
// command line is covered by edit-mode tests without a session, a director or a world.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SpaceGame.Agents.Expeditions
{
    public enum ExpeditionVerb
    {
        List,
        Force,
        Advance,
        Days,
        Home,
        Record,
        Depart,
    }

    /// <summary>A settlement or band named on the command line: its number in /exp list's output, or its id.</summary>
    public readonly struct ExpeditionTarget
    {
        /// <summary>1-based number in /exp list's output; 0 when the target is named by <see cref="Id"/>.</summary>
        public readonly int Number;
        public readonly string Id;

        public ExpeditionTarget(int number, string id) => (Number, Id) = (number, id);

        public bool ByNumber => Number > 0;

        public override string ToString() => ByNumber ? "#" + Number : Id;
    }

    /// <summary>One parsed /exp line. Only the fields its verb takes are set.</summary>
    public readonly struct ExpeditionCommand
    {
        public readonly ExpeditionVerb Verb;
        public readonly ExpeditionTarget Target;
        public readonly string GoalId;
        /// <summary>Stages to end (advance).</summary>
        public readonly int Count;
        /// <summary>Game days to simulate (days).</summary>
        public readonly float Days;

        public ExpeditionCommand(ExpeditionVerb verb, ExpeditionTarget target = default, string goalId = null, int count = 0,
                                 float days = 0f) =>
            (Verb, Target, GoalId, Count, Days) = (verb, target, goalId, count, days);
    }

    public static class ExpeditionCommandParser
    {
        private const char NumberMark = '#';

        /// <summary>One verb: its word, how to type it, and how many words may follow it.</summary>
        private readonly struct Form
        {
            public readonly ExpeditionVerb Verb;
            public readonly string Word;
            public readonly string Usage;
            public readonly int MinArgs;
            public readonly int MaxArgs;

            public Form(ExpeditionVerb verb, string word, string usage, int minArgs, int maxArgs) =>
                (Verb, Word, Usage, MinArgs, MaxArgs) = (verb, word, usage, minArgs, maxArgs);
        }

        private static readonly Form[] Forms =
        {
            new Form(ExpeditionVerb.List, "list", "/exp list — every settlement that runs bands, and every band, numbered", 0, 0),
            new Form(ExpeditionVerb.Force, "force", "/exp force <settlement> <goal> — raise a band now; it leaves at the next departure hour", 2, 2),
            new Form(ExpeditionVerb.Depart, "depart", "/exp depart <band> — an announced band leaves now: it musters, or sets out if its settlement is not loaded", 1, 1),
            new Form(ExpeditionVerb.Advance, "advance", "/exp advance <band> [n] — end the band's current stage (n times, default 1)", 1, 2),
            new Form(ExpeditionVerb.Days, "days", "/exp days <band> <n> — run a band nobody is near n game days ahead (0.5 is half a day)", 2, 2),
            new Form(ExpeditionVerb.Home, "home", "/exp home <band> — skip to its ReturnHome stage; a band nobody is near is put at its hand-off point", 1, 1),
            new Form(ExpeditionVerb.Record, "record", "/exp record <band> — print the band's saved record as JSON", 1, 1),
        };

        private const string TargetNote =
            "<settlement> and <band> are a number from /exp list (3 or #3) or an id. The server runs these, with developer mode on.";

        /// <summary>Every verb, one per line, then how a settlement or band is named.</summary>
        public static readonly string Usage = BuildUsage();

        private static string BuildUsage()
        {
            var text = new StringBuilder();
            foreach (Form form in Forms) text.Append(form.Usage).Append('\n');
            return text.Append(TargetNote).ToString();
        }

        /// <summary>
        /// Parses the words after <c>/exp</c>. False with <paramref name="problem"/> set to what to tell the player:
        /// the whole <see cref="Usage"/> for no words, help or an unknown verb; the verb's own usage for wrong words.
        /// </summary>
        public static bool TryParse(IReadOnlyList<string> args, out ExpeditionCommand command, out string problem)
        {
            command = default;
            problem = null;

            if (args == null || args.Count == 0 || args[0] == "help" || args[0] == "?")
            {
                problem = Usage;
                return false;
            }

            int found = Array.FindIndex(Forms, f => string.Equals(f.Word, args[0], StringComparison.OrdinalIgnoreCase));
            if (found < 0)
            {
                problem = $"Unknown /exp verb '{args[0]}'.\n{Usage}";
                return false;
            }

            Form form = Forms[found];
            int given = args.Count - 1;
            if (given < form.MinArgs || given > form.MaxArgs)
            {
                problem = Misuse(form, null);
                return false;
            }

            if (form.Verb == ExpeditionVerb.List)
            {
                command = new ExpeditionCommand(ExpeditionVerb.List);
                return true;
            }

            if (!TryParseTarget(args[1], out ExpeditionTarget target, out string targetProblem))
            {
                problem = Misuse(form, targetProblem);
                return false;
            }

            switch (form.Verb)
            {
                case ExpeditionVerb.Force:
                    command = new ExpeditionCommand(form.Verb, target, goalId: args[2]);
                    return true;

                case ExpeditionVerb.Advance:
                    int count = 1;
                    if (given == 2 && !TryParsePositiveInt(args[2], out count))
                    {
                        problem = Misuse(form, $"'{args[2]}' is not a whole number of stages above 0.");
                        return false;
                    }
                    command = new ExpeditionCommand(form.Verb, target, count: count);
                    return true;

                case ExpeditionVerb.Days:
                    if (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float days) ||
                        float.IsNaN(days) || float.IsInfinity(days) || days <= 0f)
                    {
                        problem = Misuse(form, $"'{args[2]}' is not a number of days above 0.");
                        return false;
                    }
                    command = new ExpeditionCommand(form.Verb, target, days: days);
                    return true;

                default:
                    command = new ExpeditionCommand(form.Verb, target);
                    return true;
            }
        }

        /// <summary>A number from /exp list (<c>3</c> or <c>#3</c>, from 1), or any other word as an id.</summary>
        public static bool TryParseTarget(string word, out ExpeditionTarget target, out string problem)
        {
            target = default;
            problem = null;

            bool marked = word.Length > 0 && word[0] == NumberMark;
            string digits = marked ? word.Substring(1) : word;
            if (!TryParseNonNegativeInt(digits, out int number))
            {
                if (marked)
                {
                    problem = $"'{word}' is not a number from /exp list.";
                    return false;
                }
                target = new ExpeditionTarget(0, word);
                return true;
            }

            if (number == 0)
            {
                problem = "Numbers from /exp list start at 1.";
                return false;
            }

            target = new ExpeditionTarget(number, null);
            return true;
        }

        /// <summary>
        /// The item <paramref name="target"/> names in <paramref name="items"/>, listed in the order /exp list prints
        /// them; <paramref name="noun"/> ("band") is what the problem calls it.
        /// </summary>
        public static bool TryResolve<T>(ExpeditionTarget target, IReadOnlyList<T> items, Func<T, string> idOf, string noun,
                                         out T found, out string problem)
        {
            found = default;
            problem = null;

            if (target.ByNumber)
            {
                if (target.Number <= items.Count)
                {
                    found = items[target.Number - 1];
                    return true;
                }
                problem = items.Count == 0 ? $"There are no {noun}s right now."
                        : $"There is no {noun} #{target.Number}: /exp list shows {items.Count}.";
                return false;
            }

            foreach (T item in items)
            {
                if (!string.Equals(idOf(item), target.Id, StringComparison.Ordinal)) continue;
                found = item;
                return true;
            }
            problem = $"No {noun} has the id '{target.Id}'. /exp list shows them.";
            return false;
        }

        private static string Misuse(Form form, string why) =>
            (why != null ? why + "\n" : string.Empty) + "Usage: " + form.Usage + "\n" + TargetNote;

        private static bool TryParsePositiveInt(string word, out int value) => TryParseNonNegativeInt(word, out value) && value > 0;

        private static bool TryParseNonNegativeInt(string word, out int value) =>
            int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>How /exp's answers read: the numbered list, a band's place in its trip, and chat-sized lines.</summary>
    public static class ExpeditionCommandText
    {
        /// <summary>Every settlement and band, numbered from 1 in the order the targets resolve against.</summary>
        public static string List(IReadOnlyList<SettlementState> settlements, IReadOnlyList<ExpeditionRecord> bands)
        {
            var text = new StringBuilder();
            if (settlements.Count == 0) text.Append("No settlement runs bands.");
            else text.Append($"Settlements: {settlements.Count}");

            for (int i = 0; i < settlements.Count; i++)
            {
                SettlementState state = settlements[i];
                string lastGoal = string.IsNullOrEmpty(state.lastGoalId) ? "none" : state.lastGoalId;
                text.Append('\n').Append($"#{i + 1} {state.settlementId} — {state.roster?.Length ?? 0} residents, " +
                                         $"{state.rotation} bands raised, last goal {lastGoal}");
            }

            text.Append('\n').Append(bands.Count == 0 ? "No bands." : $"Bands: {bands.Count}");
            for (int i = 0; i < bands.Count; i++)
            {
                ExpeditionRecord band = bands[i];
                text.Append('\n').Append($"#{i + 1} {band.id} of settlement {SettlementLabel(settlements, band.settlementId)}" +
                                         $" — {band.goalId}, {band.phase}, {Stage(band)}");
                text.Append('\n').Append("Members: ").Append(Members(band));
            }
            return text.ToString();
        }

        /// <summary>Where the band is in its trip: the day it leaves, or its stage, waypoint and time left.</summary>
        public static string Stage(ExpeditionRecord band)
        {
            if (band.phase == ExpeditionPhase.Announced) return $"leaves on day {band.departDay}";
            if (band.stageIndex < 0) return "trip not begun";
            if (band.stageIndex >= band.stages.Length) return "trip over";

            StageRecord stage = band.stages[band.stageIndex];
            var text = new StringBuilder($"stage {band.stageIndex + 1}/{band.stages.Length} {stage.kind}");
            if (stage.kind == StageKind.Search)
                text.Append($", waypoint {Math.Min(band.waypointsDone + 1, stage.waypoints)}/{stage.waypoints}");
            if (band.stageMinutesLeft >= 0f)
                text.Append($", {Math.Ceiling(band.stageMinutesLeft).ToString(CultureInfo.InvariantCulture)} min left");
            return text.ToString();
        }

        /// <summary>Each member's key, the leader marked, and its health or death.</summary>
        public static string Members(ExpeditionRecord band)
        {
            var text = new StringBuilder();
            foreach (MemberRecord member in band.members)
            {
                if (text.Length > 0) text.Append(", ");
                text.Append(member.residentKey);
                if (member.isLeader) text.Append(" leader");
                text.Append(member.dead ? " dead" : $" {Math.Round(member.health01 * 100f).ToString(CultureInfo.InvariantCulture)}%");
            }
            return text.Length > 0 ? text.ToString() : "none";
        }

        /// <summary>
        /// <paramref name="text"/> as chat lines: one per line of it, blank ones dropped, each longer than
        /// <paramref name="maxCharacters"/> broken at its last space that fits, or cut where none does.
        /// </summary>
        public static List<string> ChatLines(string text, int maxCharacters)
        {
            var lines = new List<string>();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                while (line.Length > maxCharacters)
                {
                    int cut = line.LastIndexOf(' ', maxCharacters);
                    if (cut <= 0) cut = maxCharacters;
                    lines.Add(line.Substring(0, cut).TrimEnd());
                    line = line.Substring(cut).TrimStart();
                }
                if (line.Length > 0) lines.Add(line);
            }
            return lines;
        }

        private static string SettlementLabel(IReadOnlyList<SettlementState> settlements, string settlementId)
        {
            for (int i = 0; i < settlements.Count; i++)
                if (settlements[i].settlementId == settlementId) return "#" + (i + 1);
            return settlementId;
        }
    }
}
