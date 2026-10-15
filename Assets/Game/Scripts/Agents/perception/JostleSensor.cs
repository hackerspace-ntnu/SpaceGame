// Notices a player shoving into this agent, and counts it on the provocation ladder.
//
// A shove is the one provocation a player commits by accident — cutting through a crowded market —
// so it is the one that warns before it fights: ProvocationModule.Jostled climbs one rung of the
// ladder (jostlesToFight), where a hit or a shot skips the ladder entirely. jostleCooldown is what
// keeps one long push from counting as three shoves.
//
// **Contact is measured, not collided.** This runs on the server, where a client's player is a
// replicated transform: its Rigidbody is not the one being simulated, so collision callbacks and
// velocities only ever describe the host. Positions replicate for everybody, so contact is a
// horizontal distance against both bodies' radii and the push is the closing speed between two
// sweeps' positions — one answer for the host and every client.
//
// Side-effect module (ClaimsMovement == false), like MenaceSensor: AgentController's authority gate
// keeps it on the deciding machine, and an offstage agent ticks no modules, so nobody is shoved
// through a wall. Holds no state worth saving — a shove is over in a second; what it did to the
// band is ProvocationModule's, and that is saved there.
using System.Collections.Generic;
using SpaceGame.Characters;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Agents
{
    public class JostleSensor : BehaviourModuleBase
    {
        [Header("Sensing")]
        [Tooltip("Seconds between sweeps of the players. Short, because a shove is over in a fraction " +
                 "of a second and the closing speed is measured across one sweep.")]
        [SerializeField] private float scanInterval = 0.1f;

        [Tooltip("Metres of gap between the two bodies' radii that still counts as touching: the skin " +
                 "of both colliders plus a sweep's worth of motion.")]
        [SerializeField] private float contactSlack = 0.2f;

        [Tooltip("Metres the player's position may sit above or below this agent's and still be shoving " +
                 "it, rather than walking past on the terrace overhead. The player's position is " +
                 "about a metre above its soles; this agent's is at its feet.")]
        [SerializeField] private float maxHeightGap = 2.5f;

        [Tooltip("Metres per second a touching player must be closing on this agent for it to count as " +
                 "a shove. Slower is standing close, or sidling past.")]
        [SerializeField] private float minPushSpeed = 1f;

        [Tooltip("Seconds after one jostle from a player before the next from them can count, however " +
                 "they keep pushing — renewed or continued.")]
        [SerializeField] private float jostleCooldown = 1.5f;

        // Side effect only: this module reports a shove, it never decides where to walk.
        public override bool ClaimsMovement => false;

        public override string ModuleDescription =>
            "Counts a player shoving into this agent on ProvocationModule's jostle ladder.\n\n" +
            "• Contact: horizontal distance ≤ own radius + contactSlack + the player's radius\n" +
            "• Shove: closing faster than minPushSpeed, at most once per jostleCooldown per player\n" +
            "• Ignored unless the agent's AggressionSettings.jostlesToFight > 0\n" +
            "• Side-effect module: never claims the frame.";

        private struct Contact
        {
            public Vector3 position;
            public float radius;
            public float lastJostleAt;
        }

        private readonly Dictionary<Transform, Contact> contacts = new Dictionary<Transform, Contact>();
        private readonly List<Transform> departed = new List<Transform>();
        private readonly List<Transform> players = new List<Transform>();

        private ProvocationModule provocation;
        private float bodyRadius;
        private float sinceSweep;

        private void Reset() => SetPriorityDefault(ModulePriority.Ambient);

        private void Awake() => provocation = GetComponent<ProvocationModule>();

        private void OnEnable()
        {
            contacts.Clear();
            sinceSweep = 0f;
            bodyRadius = OwnRadius();
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            // Already fighting, there is no ladder left to climb. Forget the positions too: the
            // first sweep after the grudge lapses must not read a minute's walk as one shove.
            if (provocation == null || provocation.IsProvoked)
            {
                contacts.Clear();
                return null;
            }

            sinceSweep += deltaTime;
            if (sinceSweep < scanInterval)
                return null;

            Sweep(sinceSweep);
            sinceSweep = 0f;
            return null;
        }

        /// <summary>
        /// How fast (m/s) a body that moved from <paramref name="from"/> to <paramref name="to"/> in
        /// <paramref name="seconds"/> closed on <paramref name="target"/>, on the horizontal plane;
        /// negative when it backed away. Measured along the line it started on, so a body that ends
        /// the sweep on top of the target still reads as having closed on it.
        /// </summary>
        public static float ClosingSpeed(Vector3 from, Vector3 to, Vector3 target, float seconds)
        {
            Vector3 line = Flat(target - from);
            if (seconds <= 0f || line.sqrMagnitude < 1e-6f)
                return 0f;

            return Vector3.Dot(Flat(to - from) / seconds, line.normalized);
        }

        /// <summary>Is one sweep's reading of one player a shove worth a rung?</summary>
        public static bool IsJostle(bool touching, float closingSpeed, float secondsSinceLast,
                                    float minPushSpeed, float cooldown) =>
            touching && closingSpeed > minPushSpeed && secondsSinceLast >= cooldown;

        private void Sweep(float elapsed)
        {
            Vector3 here = transform.position;
            float now = Time.time;

            SessionPlayers.Collect(players);
            for (int i = 0; i < players.Count; i++)
            {
                Transform player = players[i];
                Vector3 position = player.position;

                // The first sweep only learns where they are: a speed needs two positions.
                if (!contacts.TryGetValue(player, out Contact contact))
                {
                    contacts[player] = new Contact
                    {
                        position = position,
                        radius = PlayerRadius(player),
                        lastJostleAt = float.NegativeInfinity,
                    };
                    continue;
                }

                bool touching = Mathf.Abs(position.y - here.y) <= maxHeightGap &&
                                Flat(position - here).magnitude <= bodyRadius + contactSlack + contact.radius;
                float closing = ClosingSpeed(contact.position, position, here, elapsed);

                bool jostled = IsJostle(touching, closing, now - contact.lastJostleAt, minPushSpeed, jostleCooldown);

                contact.position = position;
                if (jostled)
                    contact.lastJostleAt = now;
                contacts[player] = contact;

                if (jostled)
                    provocation.Jostled(player);
            }

            ForgetDeparted();
        }

        // A player who left the session leaves a destroyed key behind.
        private void ForgetDeparted()
        {
            departed.Clear();
            foreach (Transform player in contacts.Keys)
                if (player == null)
                    departed.Add(player);

            foreach (Transform player in departed)
                contacts.Remove(player);
        }

        // The agent's own capsule where it has one, else the NavMeshAgent's footprint.
        private float OwnRadius()
        {
            if (TryGetComponent(out CapsuleCollider capsule))
                return RadiusOf(capsule);

            return TryGetComponent(out NavMeshAgent navAgent) ? navAgent.radius * FlatScale(transform) : 0f;
        }

        // The body capsule, asked of PlayerMovement: the ragdoll puts more capsules on the bones.
        private static float PlayerRadius(Transform player)
        {
            CapsuleCollider capsule = player.TryGetComponent(out PlayerMovement movement) ? movement.BodyCapsule : null;
            return capsule != null ? RadiusOf(capsule) : 0f;
        }

        private static float RadiusOf(CapsuleCollider capsule) => capsule.radius * FlatScale(capsule.transform);

        private static float FlatScale(Transform t)
        {
            Vector3 scale = t.lossyScale;
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        protected override void OnValidate()
        {
            scanInterval = Mathf.Max(0.05f, scanInterval);
            contactSlack = Mathf.Max(0f, contactSlack);
            maxHeightGap = Mathf.Max(0f, maxHeightGap);
            minPushSpeed = Mathf.Max(0f, minPushSpeed);
            jostleCooldown = Mathf.Max(0f, jostleCooldown);
        }
    }
}
