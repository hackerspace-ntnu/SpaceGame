// Notices that somebody is brandishing a weapon at this agent, and feeds it to the aggression meter.
//
// **This game has no aim button.** The player's verbs are Move, Look, Jump, Crouch, Dash, Interact,
// Drop and the hotbar; there is no aim-down-sights and no holster. The design (§3.3) asked for
// "points a held weapon at this agent", which assumed a verb that does not exist, and the first
// version of this file translated it into "holds anything and looks at you" — which is exactly what
// you must do to TALK to somebody, since DialogInteraction needs you facing them for the prompt.
// Walking up to a nomad to trade would have wound him to hostile in about six seconds and then left
// him refusing to talk, which is the mechanic eating the game's own conversation verb.
//
// So menace is brandishing, built out of two verbs that do exist:
//
//   a weapon in your hand   InventoryItem.menacing, authored per item. Guns, staves, the bazooka.
//                           NOT gauntlets, whatever they do — a gauntlet is gear you are wearing
//                           rather than something you have drawn, so a wrist blade reads no
//                           differently from a torch.
//   and a shot just fired   ProvocationModule.HeardGunshotFrom, which this agent recorded through
//                           its own ears. Holding a gun near somebody is not a threat; having just
//                           fired one while squared up at them is.
//
// Both, not either. That is what keeps it off the path to a conversation: you can walk into a camp
// armed and be left alone, and the moment you let a round off next to somebody and keep facing them,
// they start counting.
//
// Side-effect module (ClaimsMovement == false): it decides nothing about where the body goes, and
// returns null every frame. It exists as a module rather than a bare MonoBehaviour so that
// AgentController's authority gate covers it — an agent's aggression is shared state, and two
// machines winding up the same nomad is the same class of bug as two machines damaging it.
//
// **It reads the player's BODY, not their camera.** A remote player has no camera on the server, so
// AimProvider.GetAimRay falls back to the body's forward without saying so; this module runs on the
// server for every agent in the world, so reading it would have been silently wrong for every
// client in the session. PlayerLook writes yaw onto the body's Rigidbody and that replicates, so the
// body's facing is the one part of "where are they looking" that is true on every machine — and it
// is the honest thing to model anyway: a nomad watches your shoulders, not your eyes. The cone is
// therefore measured on the HORIZONTAL plane.
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    public class MenaceSensor : BehaviourModuleBase
    {
        [Header("Sensing")]
        [Tooltip("Seconds between sweeps. Not per frame: this walks the entity registry and asks " +
                 "each candidate for its aim, and every agent in the world pays it.")]
        [SerializeField] private float scanInterval = 0.25f;

        [Tooltip("How far off the player's facing may point and still count as \"squared up at me\", " +
                 "in degrees, measured on the horizontal plane. Generous on purpose — somebody who " +
                 "has you in the middle of their screen is threatening you whether or not a ray " +
                 "would actually hit your collider.")]
        [Range(1f, 45f)]
        [SerializeField] private float aimConeDegrees = 20f;

        [Tooltip("Height up this body the aim is measured against. A character's origin is between " +
                 "its feet, so 0 asks whether the player is aiming at the sand it stands on.")]
        [SerializeField] private float targetHeightOffset = 1.2f;

        [Tooltip("Require line of sight. Off means a nomad is menaced through the rock he is " +
                 "hiding behind.")]
        [SerializeField] private bool requireLineOfSight = true;

        [Tooltip("Seconds after a shot that the shooter still counts as brandishing. Long enough " +
                 "that squaring up right after firing is read as a threat; short enough that a " +
                 "hunt two minutes ago is forgotten.")]
        [SerializeField] private float brandishWindow = 6f;

        [SerializeField] private LayerMask sightBlockers = ~0;

        // Side effect only: this module reports a threat, it never decides where to walk.
        public override bool ClaimsMovement => false;

        private ProvocationModule provocation;

        private float scanTimer;

        // Who is currently aiming at us and for how long. One slot, not a list: the meter has no
        // notion of being menaced by two people at once, and the nearest threat is the one worth
        // telegraphing at. A second aimer simply takes over when the first looks away.
        private Transform aimer;
        private float aimedForSeconds;

        private void Reset() => SetPriorityDefault(ModulePriority.RangedAttack);

        private void Awake() => provocation = GetComponent<ProvocationModule>();

        private void OnEnable()
        {
            scanTimer = 0f;
            aimer = null;
            aimedForSeconds = 0f;
        }

        public override string ModuleDescription =>
            "Feeds the aggression meter while a player points a held item at this agent.\n\n" +
            "• Range and points-per-second come from ProvocationModule's AggressionSettings " +
            "(menaceRange, menaceDelay, menaceGainPerSecond), so one Inspector block holds the " +
            "whole temperament.\n" +
            "• aimConeDegrees — how near the aim has to pass\n" +
            "• Side-effect module: never claims the frame.";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (provocation == null || provocation.IsProvoked)
            {
                // Already fighting. The meter is full and the grudge owns the agent from here, so
                // there is nothing menace could add and no reason to pay for the sweep.
                aimer = null;
                aimedForSeconds = 0f;
                return null;
            }

            AggressionSettings settings = provocation.Settings;

            scanTimer -= deltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = Mathf.Max(0.05f, scanInterval);
                aimer = FindAimer(in settings);
            }

            if (aimer == null)
            {
                // Not a decay — the accumulator is about THIS episode of being aimed at. Forgiveness
                // of the meter itself is ProvocationModule's calmRate, and having two different
                // clocks pulling the same number down is how tuning becomes guesswork.
                aimedForSeconds = 0f;
                return null;
            }

            aimedForSeconds += deltaTime;

            // The delay is what stops a camera sweep across a camp menacing everyone in it. Only
            // the seconds AFTER it count, so holding an aim for menaceDelay is worth nothing and
            // holding it for twice that is worth one second's points.
            if (aimedForSeconds >= settings.menaceDelay)
                provocation.AddAggression(AggressionInput.Menace, deltaTime, aimer);

            return null;
        }

        /// <summary>
        /// The nearest player pointing a held item at this body, or null.
        ///
        /// Walks <see cref="EntityTargetRegistry"/> rather than an OverlapSphere for the reason the
        /// registry exists: the candidates are entities, the count is small, and a physics query
        /// would find colliders this has to map back to entities anyway.
        /// </summary>
        private Transform FindAimer(in AggressionSettings settings)
        {
            Vector3 chest = transform.position + Vector3.up * targetHeightOffset;
            float rangeSqr = settings.menaceRange * settings.menaceRange;
            float cosCone = Mathf.Cos(aimConeDegrees * Mathf.Deg2Rad);

            Transform best = null;
            float bestSqr = float.MaxValue;

            System.Collections.Generic.IReadOnlyList<EntityFaction> all = EntityTargetRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityFaction candidate = all[i];
                if (candidate == null || candidate.transform == transform)
                    continue;

                float sqr = (candidate.transform.position - transform.position).sqrMagnitude;
                if (sqr > rangeSqr || sqr >= bestSqr)
                    continue;

                if (!IsAimingAtMe(candidate.transform, chest, cosCone))
                    continue;

                best = candidate.transform;
                bestSqr = sqr;
            }

            return best;
        }

        /// <summary>Is this a player, holding something, squared up at this body?</summary>
        private bool IsAimingAtMe(Transform candidate, Vector3 chest, float cosCone)
        {
            // An EquipmentController is what makes it a player: it is the player's hand, and no NPC
            // has one (an NPC carries EntityEquipmentController instead). An NPC pointing a gun at
            // this agent is not menace, it is an attack, and it arrives through damage.
            //
            // Empty hands are not a threat. Walking through a camp with nothing drawn has to stay
            // possible, and requiring a held item is what separates "standing near me" from "squared
            // up at me" without asking what kind of item it is — a scanner levelled at a nomad reads
            // as being sized up, which is close enough to the intent.
            // A weapon actually drawn. EquipmentController is also what makes it a player: an NPC
            // carries EntityEquipmentController instead, and an NPC pointing a gun at this agent is
            // not menace but an attack, which arrives through damage.
            EquipmentController equipment = candidate.GetComponentInChildren<EquipmentController>();
            if (equipment == null || equipment.HeldItemAsset == null || !equipment.HeldItemAsset.menacing)
                return false;

            // ...and a shot, recently, heard by THIS agent. Without it, walking through a camp with
            // a rifle out is a threat, and so is standing still to read somebody's dialogue.
            if (provocation == null || !provocation.HeardGunshotFrom(candidate, brandishWindow))
                return false;

            // Flattened on both sides. See the header: the body's yaw is the only part of a remote
            // player's look direction that exists on the server, so comparing anything else works
            // for the host and quietly fails for every client.
            Vector3 facing = candidate.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f)
                return false;

            Vector3 toMe = chest - candidate.position;
            toMe.y = 0f;
            float distance = toMe.magnitude;
            if (distance < 1e-3f)
                return true;

            if (Vector3.Dot(facing.normalized, toMe / distance) < cosCone)
                return false;

            if (!requireLineOfSight)
                return true;

            // From roughly the chest on both sides rather than from the feet, or every pebble
            // between two standing bodies counts as cover. QueryTriggerInteraction.Ignore because a
            // trigger volume is not cover and the world is full of them — an interaction probe or a
            // chunk boundary would otherwise read as a wall.
            Vector3 from = candidate.position + Vector3.up * targetHeightOffset;
            return !Physics.Linecast(from, chest, out RaycastHit hit, sightBlockers,
                                     QueryTriggerInteraction.Ignore)
                   || hit.transform.IsChildOf(transform)
                   || hit.transform.IsChildOf(candidate);
        }

        protected override void OnValidate()
        {
            scanInterval = Mathf.Max(0.05f, scanInterval);
            brandishWindow = Mathf.Max(0f, brandishWindow);
            targetHeightOffset = Mathf.Max(0f, targetHeightOffset);
        }
    }
}
