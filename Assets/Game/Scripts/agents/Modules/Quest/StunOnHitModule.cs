// A character that cannot be killed but can be knocked off its feet: every hit is swallowed, and
// the character stands stunned for stunSeconds instead -- an Oathkeeper struck mid-chant.
//
// Why not just health: an escort that can die turns a boss fight into a babysitting failure screen
// and nothing else. A stun keeps the threat (the chant stops while a keeper is down, so protecting
// them still matters) without the fail state. Same filter seam as a guard or a dodge: it runs only
// where damage is decided, so the stun is the server's, and a client reads it off whatever the
// stun stops (the chant).
//
// Scripted priority while stunned: standing there IS the behaviour, and nothing below -- following,
// walking to a chant stone -- may move a stunned body. Otherwise it passes.
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.SpriteHamlet;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(HealthComponent))]
    public class StunOnHitModule : BehaviourModuleBase, IDamageFilter, IChantInterruptible
    {
        [Tooltip("Seconds a hit leaves the character stunned. A second hit restarts the count.")]
        [SerializeField] private float stunSeconds = 3f;

        private HealthComponent health;
        private float stunnedUntil = float.NegativeInfinity;

        /// <summary>SERVER: knocked down by a hit and not yet back up.</summary>
        public bool IsStunned => Time.time < stunnedUntil;

        public bool IsInterrupted => IsStunned;

        private void Reset() => SetPriorityDefault(ModulePriority.Scripted);

        private void OnEnable()
        {
            health = GetComponent<HealthComponent>();
            health.AddFilter(this);
            stunnedUntil = float.NegativeInfinity;
        }

        private void OnDisable()
        {
            if (health != null) health.RemoveFilter(this);
        }

        public override string ModuleDescription =>
            "Cannot be killed: every hit is swallowed and stuns the character for stunSeconds, " +
            "during which it stands still. Poison is swallowed without a stun.\n\n" +
            "• stunSeconds — how long a hit keeps it down";

        public void Filter(HealthComponent victim, ref DamageHit hit)
        {
            if (hit.Amount <= 0) return;

            hit.Amount = 0;

            // The witch's poison is counted, not felt as a blow (PoisonTally): a keeper reached by a
            // ball coughs and greens and keeps throwing, and is neither floored nor shown 'blocked'.
            if (hit.Kind == DamageKind.Poison) return;

            hit.Defense = DamageDefense.Blocked;
            stunnedUntil = Time.time + stunSeconds;
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime) =>
            IsStunned ? MoveIntent.Idle() : null;

        protected override void OnValidate()
        {
            SetMinPriority(ModulePriority.Scripted);
            stunSeconds = Mathf.Max(0.1f, stunSeconds);
        }
    }
}
