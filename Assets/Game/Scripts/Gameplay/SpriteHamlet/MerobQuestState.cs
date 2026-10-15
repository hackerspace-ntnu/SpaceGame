using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.Gameplay.SpriteHamlet
{
    /// <summary>
    /// What the witch's curse does to Merob, read off the hamlet quest.
    ///
    /// <list type="bullet">
    /// <item><b>Cursed asleep</b> (inside <see cref="cursedAsleep"/>): nobody walking up wakes him and
    /// no weapon touches him.</item>
    /// <item><b>Awake and wrong</b> (after it): woken where he stands — the moment the witch dies, or
    /// the moment his chunk streams in afterwards — and hostile, as the conjurer always is.</item>
    /// <item><b>Freed</b> (inside <see cref="freed"/>): sworn to the hamlet again — he takes the
    /// <see cref="freedFaction"/>, lets go of whoever he was fighting, and stands up whole.</item>
    /// </list>
    ///
    /// <para>
    /// <b>Derived, never saved.</b> Everything here is set from the chain's progress whenever it
    /// changes and whenever he loads, on every machine, so a reload or a late joiner finds him in the
    /// right state with nothing extra in the save. Beating him down is <c>SubduableModule</c>'s; the
    /// staff he gives away is the quest step's.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(HealthComponent))]
    public class MerobQuestState : MonoBehaviour, IDamageFilter
    {
        [Tooltip("While the chain is inside this window he sleeps under the curse and cannot be woken or hurt.")]
        [SerializeField] private QuestStepWindow cursedAsleep;

        [Tooltip("While the chain is inside this window the oath is reforged and he is the hamlet's again.")]
        [SerializeField] private QuestStepWindow freed;

        [Tooltip("His side once freed.")]
        [SerializeField] private FactionDefinition freedFaction;

        private HealthComponent health;
        private DormantModule dormant;
        private SubduableModule subduable;
        private EntityFaction faction;
        private AgentTargeting targeting;
        private ObjectiveDirector director;
        private bool asleep;

        private void Awake()
        {
            health = GetComponent<HealthComponent>();
            dormant = GetComponent<DormantModule>();
            subduable = GetComponent<SubduableModule>();
            faction = GetComponent<EntityFaction>();
            targeting = GetComponent<AgentTargeting>();
        }

        private void OnEnable()
        {
            health.AddFilter(this);
            director = ObjectiveDirector.Instance;
            if (director != null) director.Changed += OnChanged;
            Apply();
        }

        private void OnDisable()
        {
            health.RemoveFilter(this);
            if (director != null) director.Changed -= OnChanged;
        }

        private void OnChanged(ObjectiveRun run, bool live)
        {
            if (run.Chain == cursedAsleep.chain || run.Chain == freed.chain) Apply();
        }

        public void Filter(HealthComponent victim, ref DamageHit hit)
        {
            if (!asleep) return;

            hit.Amount = 0;
            hit.Defense = DamageDefense.Blocked;
        }

        private void Apply()
        {
            // Outside a story world there is no quest to read: leave him as the prefab made him.
            ObjectiveRun run = cursedAsleep.FindRun();
            if (run == null) return;

            asleep = cursedAsleep.Contains(run);
            if (dormant != null)
            {
                dormant.CanWake = !asleep;
                if (!asleep) dormant.WakeNow();
            }

            if (!freed.Contains(freed.FindRun())) return;

            if (faction != null && freedFaction != null) faction.SetFaction(freedFaction);
            if (!Network.Decides) return;

            if (targeting != null) targeting.ClearTarget();
            if (subduable != null && subduable.IsSubdued) subduable.Revive();
        }
    }
}
