// When a resident takes its hands off its work because it is looking at a player.
//
// Pure, so the rule can be tested as a sequence: ResidentAwareness glances at a noticed player every few
// seconds while they linger, so "stop while looking" alone would stutter the work on and off — the hands
// stay off for a moment after the look ends. Only a body that is WORKING lets go: a sleeper, a sitter by the
// fire and a climber keep their pose, because their loop is the pose rather than a task.
namespace SpaceGame.Agents.Residents
{
    public static class ResidentAttention
    {
        /// <summary>
        /// Whether a resident showing <paramref name="shown"/> has its hands off the work at
        /// <paramref name="now"/>. <paramref name="until"/> carries the end of the pause between calls.
        /// </summary>
        public static bool HandsOff(Activity shown, bool looking, float now, float resumeSeconds, ref float until)
        {
            if (shown != Activity.Work && shown != Activity.Chore) return false;

            if (looking) until = now + resumeSeconds;
            return now < until;
        }
    }
}
