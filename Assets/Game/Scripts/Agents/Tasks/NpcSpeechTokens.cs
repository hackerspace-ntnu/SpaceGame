// Lets an authored line ask the NPC what it is actually doing.
//
// Without this, tasks are invisible in conversation: an NPC can be three days into a journey to a
// wreck and, asked about it, recite the same fixed line it shipped with. The whole value of giving
// NPCs jobs is that the player can find out about them, and the cheapest possible way to expose
// that is a token an author can drop into any existing dialog or chatter line.
//
// Deliberately shared between DialogInteraction and ChatterModule so a line means the same thing in
// both, and a writer can move one between them without rewriting it.
using System.Text;
using UnityEngine;

namespace SpaceGame.Agents
{
    public static class NpcSpeechTokens
    {
        /// <summary>What the NPC would say it is doing. "picking over the old wreck".</summary>
        public const string Task = "{task}";

        /// <summary>Where it is headed, when that place has a name. "the Vela wreck".</summary>
        public const string Destination = "{destination}";

        /// <summary>"heading to X" / "working at X" / "looking for work" — a whole clause.</summary>
        public const string Doing = "{doing}";

        /// <summary>True if <paramref name="line"/> contains anything worth resolving.</summary>
        public static bool HasToken(string line) =>
            !string.IsNullOrEmpty(line) && line.IndexOf('{') >= 0;

        /// <summary>
        /// Substitute the tokens for whatever <paramref name="task"/> currently reports.
        ///
        /// A null task module is not an error — most NPCs will never have one. Tokens then resolve
        /// to neutral filler rather than being left as literal braces on screen, which is the one
        /// outcome that always looks broken.
        /// </summary>
        public static string Resolve(string line, NpcTaskModule task)
        {
            if (!HasToken(line)) return line;

            var builder = new StringBuilder(line);

            string label = task != null && !string.IsNullOrWhiteSpace(task.CurrentLabel)
                ? task.CurrentLabel
                : "keeping busy";

            string destination = task != null && !string.IsNullOrWhiteSpace(task.CurrentDestinationName)
                ? task.CurrentDestinationName
                : "out there";

            builder.Replace(Task, label);
            builder.Replace(Destination, destination);
            builder.Replace(Doing, DescribeDoing(task, label, destination));

            return builder.ToString();
        }

        private static string DescribeDoing(NpcTaskModule task, string label, string destination)
        {
            if (task == null || !task.HasTasks) return "looking for work";

            bool named = !string.IsNullOrWhiteSpace(task.CurrentDestinationName);

            return task.CurrentPhase switch
            {
                NpcTaskModule.Phase.Travelling => named ? $"{label}, out at {destination}" : label,
                NpcTaskModule.Phase.Dwelling   => named ? $"{label}, here at {destination}" : label,
                _                              => "deciding what's next",
            };
        }
    }
}
