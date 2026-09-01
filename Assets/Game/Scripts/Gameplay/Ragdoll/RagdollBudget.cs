using System.Collections.Generic;

namespace SpaceGame.Gameplay.Ragdoll
{
    /// <summary>
    /// How many bodies may be simulating at once, and what happens to the oldest when too many are.
    ///
    /// <para>
    /// A jointed skeleton is not free. One blast from the repulsor gauntlet reaches twenty metres
    /// through a hundred-degree cone, and the crowd it is aimed at is the whole point of the weapon
    /// — so the worst case is not one ragdoll, it is every creature in a camp going limp on the
    /// same frame, each with a dozen bodies and a dozen joints for the solver to resolve against
    /// each other and the terrain. GDC-L1-PERF-0004: decide what the system may spend rather than
    /// discovering it in a profiler after a playtest.
    /// </para>
    ///
    /// <para>
    /// Eviction takes the OLDEST rather than the furthest away. Distance is the tempting metric and
    /// it is the wrong one: it changes as the player walks, so a corpse would thaw and refreeze as
    /// they moved past it, and the frame the budget is protecting is the one right after the blast
    /// — when every ragdoll is equally close. Age is stable, and the oldest limp body is reliably
    /// the one that has already finished being interesting.
    /// </para>
    ///
    /// <para>
    /// Static state, deliberately, and it is per-process rather than per-scene. A budget is a
    /// property of the machine's frame, not of a scene — and this project streams chunks in and out
    /// constantly, so a scene-scoped budget would reset itself while corpses stayed lying around.
    /// </para>
    /// </summary>
    public static class RagdollBudget
    {
        /// <summary>Limp bodies, oldest first.</summary>
        private static readonly List<RagdollRig> live = new List<RagdollRig>();

        /// <summary>
        /// Take a place in the budget, freezing the oldest bodies if there is no room.
        /// </summary>
        /// <param name="cap">
        /// The registering rig's own limit. Carried per-call rather than held here so it stays a
        /// serialized field on the prefab — a crowd of ostriches and a single boss can be worth
        /// different ceilings, and a static number here could not tell them apart.
        /// </param>
        public static void Register(RagdollRig rig, int cap)
        {
            if (rig == null) return;

            live.Remove(rig);
            live.Add(rig);

            if (cap <= 0) return;

            while (live.Count > cap)
            {
                int victim = OldestEvictable(rig);
                if (victim < 0) return;

                RagdollRig oldest = live[victim];
                live.RemoveAt(victim);

                // Removed from the list BEFORE freezing, because Freeze is entitled to call back in
                // here through OnDestroy. Mutating the list from inside its own loop is the one
                // failure mode this ordering exists to rule out.
                if (oldest != null) oldest.Freeze();
            }
        }

        /// <summary>
        /// The oldest body that has already come to rest, or failing that the oldest body at all.
        ///
        /// <para>
        /// Freezing leaves the bones exactly where they are, so freezing a body that is still
        /// FALLING preserves a pose nobody wants — in the worst case a creature that went limp this
        /// frame and has not yet toppled, left standing bolt upright and dead. Preferring a settled
        /// body means the pose being frozen is the pose it was going to hold anyway. That worst case
        /// is a real one rather than a hypothetical: a world reloading with a graveyard in it puts
        /// every corpse limp on the same frame.
        /// </para>
        ///
        /// <para>
        /// Returns -1 when the only candidate is the rig that just registered — better to run one
        /// over the cap for a moment than to freeze the body the blast was actually about.
        /// </para>
        /// </summary>
        private static int OldestEvictable(RagdollRig exclude)
        {
            int fallback = -1;

            for (int i = 0; i < live.Count; i++)
            {
                if (live[i] == null || live[i] == exclude) continue;
                if (live[i].IsSettled) return i;
                if (fallback < 0) fallback = i;
            }

            return fallback;
        }

        /// <summary>Give the place back. Safe to call for a rig that never took one.</summary>
        public static void Unregister(RagdollRig rig)
        {
            if (rig != null) live.Remove(rig);
        }

        /// <summary>How many bodies are limp right now. For diagnostics.</summary>
        public static int LiveCount => live.Count;
    }
}
