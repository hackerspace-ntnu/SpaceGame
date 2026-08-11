// Shared target-reference hygiene for behaviour modules that resolve their own candidate
// (allies, neutrals, threats — anything AgentTargeting's single hostile decision doesn't cover).
//
// Two bugs this exists to prevent, both of which shipped:
//   * Holding a reference to a corpse. A dying entity stays active through its despawn delay
//     and an eliminated player object stays active forever, so `if (target) return;` keeps a
//     module fleeing, kiting or staring at a body indefinitely.
//   * Never re-evaluating. A module that resolves once at spawn commits to whoever happened
//     to be nearest then, and walks past a closer candidate for the rest of the match.
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    public static class TargetResolution
    {
        // True when a reference is still worth acting on. A target whose EntityFaction has been
        // disabled (how the minigame retires a corpse) is gone from the registry but its Transform
        // is still alive, so the faction check matters as much as the health one.
        public static bool IsViable(Transform candidate)
        {
            if (!candidate)
                return false;

            IDamageable damageable = candidate.GetComponentInChildren<IDamageable>();
            if (damageable != null && !damageable.Alive)
                return false;

            EntityFaction faction = candidate.GetComponentInParent<EntityFaction>();
            return faction == null || faction.isActiveAndEnabled;
        }

        // Returns the target a module should act on this frame, re-querying the registry when the
        // held reference has gone stale or the re-evaluation interval has elapsed.
        //
        // `timer` is per-module state; seed it at 0 so the first call resolves immediately.
        public static Transform Refresh(Transform current, ref float timer, float interval,
                                        float deltaTime, EntityFaction selfFaction,
                                        FactionRelationship relationship, Vector3 position)
        {
            timer -= deltaTime;

            bool viable = IsViable(current);
            if (viable && timer > 0f)
                return current;

            timer = Mathf.Max(0.05f, interval);

            Transform candidate = EntityTargetRegistry.ResolveNearest(selfFaction, relationship, position);
            if (IsViable(candidate))
                return candidate;

            // Nothing better available — keep a viable current target rather than dropping to null
            // and making the module restart its state machine for one frame.
            return viable ? current : null;
        }
    }
}
