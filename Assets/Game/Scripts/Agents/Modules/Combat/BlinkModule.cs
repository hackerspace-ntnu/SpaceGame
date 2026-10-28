// Vanishes from where a fighter has cornered it and reappears a few steps away -- the witch, who
// never lets anyone stand at her elbow for long.
//
// A side effect, not a movement answer: the blink is instant, so there is no path to walk and the
// frame is left to whatever else the creature does. Fires when its target closes inside
// tooCloseDistance, at most once per cooldown. The destination is whichever point of the nearest
// BlinkArena within maxHop of her lies furthest from the target, so a blink always buys distance --
// but only a little. A hop across the whole clearing read as the fight resetting; a short one keeps
// her in reach of a stasis shot (changed 2026-10-14). The arena is found, not referenced, because the
// witch is spawned at runtime and a prefab cannot point into a scene.
//
// The move goes through NetworkedTeleport.Move, the one call that moves a networked body and makes
// it stick (it warps the NavMeshAgent rather than writing the transform). Ticks only on the server.
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    public class BlinkModule : BehaviourModuleBase
    {
        [Tooltip("How far away a BlinkArena may be and still be its arena, metres.")]
        [SerializeField] private float arenaSearchRadius = 60f;

        [Tooltip("A target closer than this makes it blink, metres.")]
        [SerializeField] private float tooCloseDistance = 4f;

        [Tooltip("Seconds between blinks at the soonest.")]
        [SerializeField] private float cooldown = 6f;

        [Tooltip("Furthest a blink may carry her, metres. Only arena points this close are candidates; " +
                 "with none in reach she does not blink.")]
        [SerializeField] private float maxHop = 7f;

        private float readyAt;

        public override bool ClaimsMovement => false;

        private void Reset() => SetPriorityDefault(ModulePriority.Reactive);

        private void OnEnable() => readyAt = Time.time + cooldown;

        public override string ModuleDescription =>
            "Side effect: when its target comes inside tooCloseDistance, teleports to the point of " +
            "the nearest BlinkArena within maxHop furthest from that target, at most once per cooldown.\n\n" +
            "• arenaSearchRadius — how far its BlinkArena may be\n" +
            "• tooCloseDistance / cooldown / maxHop";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (Time.time < readyAt || context.Targeting == null || !context.Targeting.HasTarget)
                return null;

            Transform target = context.Targeting.Target;
            if ((target.position - context.Position).sqrMagnitude > tooCloseDistance * tooCloseDistance)
                return null;

            if (!BlinkArena.TryFindNear(context.Position, arenaSearchRadius, out BlinkArena arena))
                return null;

            Transform destination = arena.FurthestFrom(target.position, context.Position, maxHop);
            if (destination == null)
                return null;

            readyAt = Time.time + cooldown;
            Vector3 face = target.position - destination.position;
            face.y = 0f;

            NetworkedTeleport.Move(gameObject, destination.position,
                                   face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : transform.rotation);
            return null;
        }

        protected override void OnValidate()
        {
            arenaSearchRadius = Mathf.Max(1f, arenaSearchRadius);
            tooCloseDistance = Mathf.Max(0.5f, tooCloseDistance);
            cooldown = Mathf.Max(0.5f, cooldown);
            maxHop = Mathf.Max(1f, maxHop);
        }
    }
}
