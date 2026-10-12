using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// What the visor's objective panel can show: a title, status lines and somewhere to head. The crew's chain step
    /// is one (<see cref="ObjectiveDirector.Tracked"/>); a mission given out in the world is another.
    ///
    /// <para>EVERY machine, every frame: presentation only, derived from state the machine already holds.</para>
    /// </summary>
    public interface ITrackedObjective
    {
        string Title { get; }

        /// <summary>Lines under the title. May use TMP rich text. Empty for none.</summary>
        string Status { get; }

        bool TryGetWaypoint(out Vector3 position);
    }

    /// <summary>
    /// The world missions open on this machine, newest last. The visor tracks the newest one, and the crew's chain
    /// step when there is none: a mission the player just took on is what they are doing now.
    /// </summary>
    public static class TrackedObjectives
    {
        private static readonly List<ITrackedObjective> Open = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Open.Clear();

        /// <summary>The newest open mission, or null when none is.</summary>
        public static ITrackedObjective Newest => Open.Count > 0 ? Open[Open.Count - 1] : null;

        /// <summary>Opens <paramref name="objective"/> on this machine, or makes it the newest again. Idempotent.</summary>
        public static void Track(ITrackedObjective objective)
        {
            if (objective == null) return;
            Open.Remove(objective);
            Open.Add(objective);
        }

        /// <summary>Closes <paramref name="objective"/> on this machine. Idempotent.</summary>
        public static void Untrack(ITrackedObjective objective) => Open.Remove(objective);

        public static bool IsTracked(ITrackedObjective objective) => Open.Contains(objective);
    }
}
