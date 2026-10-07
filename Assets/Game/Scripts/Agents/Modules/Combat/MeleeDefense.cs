using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Ragdoll;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>
    /// Lets a humanoid NPC sometimes catch a melee blow on its guard, or duck out of it, instead of
    /// taking it — the player's punch and blade stop being a sure thing against someone who sees
    /// them coming.
    ///
    /// <para>
    /// <b>Where it sits.</b> An <see cref="IDamageFilter"/> on the body's <see cref="HealthComponent"/>:
    /// the decision is made inside <c>Damage</c>, at the moment the blow would land, on the machine
    /// that decides damage (the server, for every NPC). Nothing else is needed to keep it
    /// server-side — a filter never runs where a hit only arrives as replicated health.
    /// </para>
    /// <para>
    /// <b>The rules</b> (<see cref="Decide"/>): only a <see cref="DamageKind.Melee"/> blow — never
    /// a bullet, a blast or a fall; only while the body lives, stands (not knocked down or held),
    /// is not committed to a swing of its own, is off its cooldown, and has the attacker inside
    /// its <see cref="facingCone"/>. Those exceptions are the counterplay: hit it from the side,
    /// while it swings, or while it is down, and the blow always lands (GDC-L1-BAL-0004). The
    /// odds are low and a defence is never offered twice running (<see cref="cooldown"/>), so it
    /// spices a fight rather than making the player's hits feel like a coin toss
    /// (GDC-L1-BAL-0006).
    /// </para>
    /// <para>
    /// <b>It only counts if it is seen.</b> A block or dodge is raised as a
    /// <see cref="CharacterMoment"/> through <see cref="BodyLanguage.ReactEverywhere(CharacterMoment)"/>,
    /// which plays it on every machine; if nothing fits the body's posture (every dodge is
    /// full-body, so a walking NPC has none) the hit simply lands. A blow that vanished with no
    /// guard raised would read as a bug, not a defence (GDC-L1-ANIM-0003). The attacker is told
    /// too — see <c>DamageNumbers</c> (GDC-L1-FEEL-0004).
    /// </para>
    /// <para>
    /// <b>Persistence:</b> none, as a decision. The only state is a cooldown a few seconds long
    /// and the roll stream; both start fresh after a load.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MeleeDefense : MonoBehaviour, IDamageFilter
    {
        /// <summary>Below this squared length a direction is no direction: the attacker is standing inside the body.</summary>
        private const float DegenerateSqr = 1e-6f;

        [Header("Odds")]
        [Tooltip("Chance, 0-1, that a melee blow this body faces is caught on its guard.")]
        [SerializeField, Range(0f, 1f)] private float blockChance = 0.12f;

        [Tooltip("Chance, 0-1, that it is ducked or stepped out of instead. One roll decides both, " +
                 "so block + dodge is the share of blows answered at all; clamped to fit.")]
        [SerializeField, Range(0f, 1f)] private float dodgeChance = 0.08f;

        [Tooltip("Share of a blocked blow that still lands, 0-1. 0 stops it whole; a dodge always does.")]
        [SerializeField, Range(0f, 1f)] private float blockPassThrough = 0.25f;

        [Header("When")]
        [Tooltip("Full width, in degrees, of the cone in front of the body the attacker must stand " +
                 "in to be seen coming. A blow from outside it always lands.")]
        [SerializeField, Range(0f, 360f)] private float facingCone = 120f;

        [Tooltip("Seconds after a block or dodge before this body may defend again, so the answer " +
                 "never comes twice running.")]
        [SerializeField, Min(0f)] private float cooldown = 3f;

        private HealthComponent health;
        private CloseCombatModule melee;
        private AgentRagdoll ragdoll;
        private System.Random rolls;
        private float readyAt;

        /// <summary>What the body knows at the moment a blow would land. Everything <see cref="Decide"/> reads.</summary>
        public struct Circumstances
        {
            public bool Melee;
            public bool Alive;
            /// <summary>Mid-swing: committed to its own attack, arms busy.</summary>
            public bool Committed;
            /// <summary>Knocked down, netted or tied.</summary>
            public bool Down;
            public Vector3 Facing;
            /// <summary>From the defender to the attacker. Zero when there is no attacker.</summary>
            public Vector3 ToAttacker;
            public float Now;
            public float ReadyAt;
        }

        /// <summary>
        /// How this blow is met, from a uniform <paramref name="roll"/> in [0, 1): under
        /// <paramref name="blockChance"/> it is blocked, under block + dodge it is dodged, otherwise
        /// taken. Pure, so the rules are testable without a body.
        /// </summary>
        public static DamageDefense Decide(in Circumstances c, float coneDegrees, float blockChance,
                                           float dodgeChance, float roll)
        {
            if (!c.Melee || !c.Alive || c.Committed || c.Down || c.Now < c.ReadyAt) return DamageDefense.None;
            if (!Faces(c.Facing, c.ToAttacker, coneDegrees)) return DamageDefense.None;

            if (roll < blockChance) return DamageDefense.Blocked;
            return roll < blockChance + dodgeChance ? DamageDefense.Dodged : DamageDefense.None;
        }

        /// <summary>
        /// Whether <paramref name="toAttacker"/> lies inside the <paramref name="coneDegrees"/>-wide
        /// cone around <paramref name="facing"/>, on the ground plane: a blow from a ledge above is
        /// still in front of you. No attacker, or one standing inside the body, is not seen.
        /// </summary>
        public static bool Faces(Vector3 facing, Vector3 toAttacker, float coneDegrees)
        {
            Vector3 ahead = Vector3.ProjectOnPlane(facing, Vector3.up);
            Vector3 to = Vector3.ProjectOnPlane(toAttacker, Vector3.up);
            if (ahead.sqrMagnitude < DegenerateSqr || to.sqrMagnitude < DegenerateSqr) return false;

            return Vector3.Angle(ahead, to) <= coneDegrees * 0.5f;
        }

        private void OnEnable()
        {
            Resolve();
            if (health != null) health.AddFilter(this);
        }

        private void OnDisable()
        {
            if (health != null) health.RemoveFilter(this);
        }

        // Resolved on enable rather than in Awake: the filter has to be registered from OnEnable,
        // and AddComponent outside play mode (tests, builders) raises no Awake at all.
        private void Resolve()
        {
            if (health == null) health = GetComponentInParent<HealthComponent>();
            if (health == null) health = GetComponentInChildren<HealthComponent>();
            if (melee == null) melee = GetComponentInParent<CloseCombatModule>();
            if (ragdoll == null) ragdoll = GetComponentInParent<AgentRagdoll>();
        }

        /// <summary>Server (the deciding machine): the blow is about to land. See the class remarks.</summary>
        public void Filter(HealthComponent victim, ref DamageHit hit)
        {
            var now = new Circumstances
            {
                Melee = hit.Kind == DamageKind.Melee,
                Alive = victim.Alive,
                Committed = melee != null && melee.CommitTimer > 0f,
                Down = ragdoll != null && ragdoll.IsHeldOrDown,
                Facing = transform.forward,
                ToAttacker = hit.Source != null ? hit.Source.position - transform.position : Vector3.zero,
                Now = Time.time,
                ReadyAt = readyAt,
            };

            DamageDefense defense = Decide(now, facingCone, blockChance, dodgeChance, (float)Rolls.NextDouble());
            if (defense == DamageDefense.None) return;

            CharacterMoment shown = defense == DamageDefense.Blocked ? CharacterMoment.Blocked : CharacterMoment.Dodged;
            if (!BodyLanguage.ReactEverywhere(this, shown)) return;

            readyAt = Time.time + cooldown;
            hit.Defense = defense;
            hit.Amount = defense == DamageDefense.Blocked ? Mathf.RoundToInt(hit.Amount * blockPassThrough) : 0;
        }

        /// <summary>
        /// This body's own roll stream, seeded from the seed its animation already uses — so one
        /// body's run of luck is reproducible within a session. Only the server ever draws from it.
        /// </summary>
        private System.Random Rolls
        {
            get
            {
                if (rolls != null) return rolls;

                CharacterActions actions = GetComponentInParent<CharacterActions>();
                rolls = new System.Random(actions != null ? actions.Seed : GetInstanceID());
                return rolls;
            }
        }

        private void OnValidate()
        {
            dodgeChance = Mathf.Min(dodgeChance, 1f - blockChance);
        }
    }
}
