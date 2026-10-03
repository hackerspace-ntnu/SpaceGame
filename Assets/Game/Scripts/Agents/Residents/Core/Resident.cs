// One settlement resident: who it is (set by Settlement.Generate), the numbers its two axes derive,
// the one live deviation from its day plan, and going indoors and back out.
//
// Deliberately NOT a decision-maker. Where to be is ResidentRoutine's, how provocation escalates is
// ProvocationModule's, what to say is ResidentVoice's. Damage is listened to for MEMORY only — a hit
// is the stock instant fight, shoves climb the stock ladder (jostlesToFight from temper) — and so is
// damage to whoever is attacking this resident: a player hurting them has defended it, a deed the
// settlement hears of (Gossip, Favor). The one fight call made here is an ally's: nerve and bonds
// decide whether this resident joins it or goes home. Waking lives here rather than in a module
// because an offstage agent ticks no modules at all.
using System;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.World;

namespace SpaceGame.Agents.Residents
{
    /// <summary>Why two residents are linked; gossip and kin reactions follow these.</summary>
    [Serializable]
    public struct ResidentBond
    {
        public int other;
        public BondKind kind;
    }

    [DisallowMultipleComponent]
    public sealed class Resident : MonoBehaviour
    {
        private const float DefaultAxis = 0.5f;
        // Never 1: a single bump is an accident, so even the prickliest warns once before a shove is a fight.
        private const int MostJostlesToFight = 3, FewestJostlesToFight = 2;
        private const float WarmForgiveScale = 2f, PricklyForgiveScale = 0.5f;
        private const float WarmFamiliarityScale = 1.3f, PricklyFamiliarityScale = 0.7f;
        private const float WarmRacketScale = 1.5f, PricklyRacketScale = 0.5f;
        // Nerve at or above this walks out towards a disturbance; below it only comes to the door.
        private const float BoldNerve = 0.5f;
        // Nerve at or above this joins an ally's fight on the first alert; below it goes home, unless
        // bonded to whoever was hurt.
        private const float JoinsFightNerve = 0.35f;
        // The sprint racket is emitted every 2 s; a longer silence than this starts the count over.
        private const float RacketContinuityGap = 3f;

        [Header("Set by Settlement.Generate")]
        public ResidentArchetype archetype;
        public string displayName;
        [Tooltip("-1 = the archetype's.")] public float nerveOverride = -1f;
        [Tooltip("-1 = the archetype's.")] public float temperOverride = -1f;
        [Tooltip("Game minutes added to this resident's drawn bedtime.")] public float bedtimeOffset;
        [Tooltip("This resident's place in its settlement's roster; bonds and memories name residents by it.")]
        public int index;
        [Tooltip("Where it lives. Empty = it sleeps in the open, at campPosition.")]
        public Dwelling home;
        public Vector3 campPosition;
        public int seed;
        public ResidentBond[] bonds = Array.Empty<ResidentBond>();
        public Settlement settlement;

        private HealthComponent health;
        private EntityFaction faction;
        private NoiseReceiverModule ears;
        private AlertReceiverModule alerts;
        private ResidentVoice voice;
        private OverrideKind overrideKind;
        private bool offstage;
        private int gunshotsHeard;
        private float racketStart, lastRacket = float.NegativeInfinity;
        private Vector3 glancePoint;
        private float glanceUntil = float.NegativeInfinity;

        public AgentController Agent { get; private set; }
        public AgentGoal Goal { get; private set; }
        public ProvocationModule Provocation { get; private set; }
        public InteractionFocusModule Focus { get; private set; }
        public ResidentPresence Presence { get; private set; }
        public ResidentMemory Memory { get; } = new ResidentMemory();

        public Settlement Settlement
        {
            get
            {
                // Only the settlement this character was generated into (or stands under). The same
                // character prefab also walks in caravans, and a copy out there is nobody's resident.
                if (!settlement) settlement = GetComponentInParent<Settlement>();
                return settlement;
            }
        }

        /// <summary>The settlement's people this resident is one of; null for a copy that belongs to none.</summary>
        public SettlementSociety Society => Settlement ? Settlement.Society : null;

        public float Nerve => nerveOverride >= 0f ? nerveOverride : archetype ? archetype.nerve : DefaultAxis;
        public float Temper => temperOverride >= 0f ? temperOverride : archetype ? archetype.temper : DefaultAxis;
        public string RoleName => archetype ? archetype.roleName : string.Empty;
        public Lifestyle Lifestyle => archetype ? archetype.Lifestyle : Lifestyle.Roamer;
        public string DisplayName => !string.IsNullOrEmpty(displayName) ? displayName : RoleName;
        public bool IsDead => health && !health.Alive;
        public bool IsOffstage => offstage;

        // §6.4: temper runs warm (0) → prickly (1).
        public int JostlesToFight => Mathf.RoundToInt(Mathf.Lerp(MostJostlesToFight, FewestJostlesToFight, Temper));

        /// <summary>Does an ally's alert alone put this resident in the fight?</summary>
        public bool JoinsAlliesFights => Nerve >= JoinsFightNerve;
        public float ForgiveDays =>
            ResidentTuning.Instance.forgiveDaysBase * Mathf.Lerp(WarmForgiveScale, PricklyForgiveScale, Temper);
        /// <summary>Familiarity one talk is worth to this resident (tuning's per-talk gain, scaled by temper).</summary>
        public float FamiliarityGain =>
            ResidentTuning.Instance.familiarityPerTalk * Mathf.Lerp(WarmFamiliarityScale, PricklyFamiliarityScale, Temper);
        public Stance OpeningStance => Attitude.Opening(Nerve, Temper);

        public OverrideKind Override => Time.time < OverrideUntil ? overrideKind : OverrideKind.None;
        public Vector3 OverridePoint { get; private set; }
        public float OverrideUntil { get; private set; }

        /// <summary>Where a just-woken resident is looking, for settleSeconds after waking; null otherwise.</summary>
        public Vector3? GlancePoint => Time.time < glanceUntil ? glancePoint : (Vector3?)null;

        private void Awake()
        {
            Agent = GetComponent<AgentController>();
            Goal = GetComponentInChildren<AgentGoal>(true);
            Provocation = GetComponentInChildren<ProvocationModule>(true);
            Focus = GetComponentInChildren<InteractionFocusModule>(true);
            Presence = GetComponent<ResidentPresence>();
            health = GetComponent<HealthComponent>();
            faction = GetComponent<EntityFaction>();
            ears = GetComponentInChildren<NoiseReceiverModule>(true);
            alerts = GetComponentInChildren<AlertReceiverModule>(true);
            voice = GetComponent<ResidentVoice>();
        }

        private void OnEnable()
        {
            if (health)
            {
                health.OnDamage += HandleDamaged;
                health.OnDeath += HandleDied;
            }
            if (ears) ears.Heard += HandleHeard;
            if (alerts) alerts.HeardAllyHurt += HandleAllyHurt;
            HealthComponent.AnyDamaged += HandleAnyDamaged;
        }

        private void OnDisable()
        {
            if (health)
            {
                health.OnDamage -= HandleDamaged;
                health.OnDeath -= HandleDied;
            }
            if (ears) ears.Heard -= HandleHeard;
            if (alerts) alerts.HeardAllyHurt -= HandleAllyHurt;
            HealthComponent.AnyDamaged -= HandleAnyDamaged;
        }

        private void Start()
        {
            ApplyDerivedTuning();
            QuietStockModules();
        }

        private void Update()
        {
            if (offstage && Network.Decides && PlanWantsAwake()) ComeOnstage();
        }

        /// <summary>
        /// Writes the temper-derived jostle ladder, the global settle time and the nerve-derived answer
        /// to an ally's alert into the provocation meter: the bold fill it on the first alert, the rest
        /// never climb it — <see cref="HandleAllyHurt"/> sends them home or, bonded, into the fight.
        /// </summary>
        public void ApplyDerivedTuning()
        {
            // A caravan copy of the same prefab is nobody's resident and keeps the prefab's own temperament.
            if (!Provocation || Society == null) return;

            AggressionSettings settings = Provocation.Settings;
            settings.jostlesToFight = JostlesToFight;
            settings.settleSeconds = ResidentTuning.Instance.settleSeconds;
            settings.allyHurtGain = JoinsAlliesFights ? settings.attackAt : 0f;
            Provocation.Settings = settings;
        }

        /// <summary>
        /// Nothing stock competes with the routine's hold: footsteps and gunshots are not walked over to (a
        /// disturbance wakes and draws a resident its own way), idle looking and chatter are the routine's and
        /// the voice's, and warnings are spoken through the culture's lines. Members only, like the tuning.
        /// </summary>
        private void QuietStockModules()
        {
            if (Society == null) return;

            if (ears) ears.ReactTo(NoiseTypeMask.None, NoiseTypeMask.Alert);
            foreach (WatchModule watch in GetComponentsInChildren<WatchModule>(true)) watch.enabled = false;
            foreach (IdleLookAroundModule look in GetComponentsInChildren<IdleLookAroundModule>(true)) look.enabled = false;
            foreach (ChatterModule chatter in GetComponentsInChildren<ChatterModule>(true)) chatter.enabled = false;
            foreach (AggressionTelegraphModule telegraph in GetComponentsInChildren<AggressionTelegraphModule>(true)) telegraph.Mute();
        }

        /// <summary>The roster indices this resident likes to spend free time near: family and friends.</summary>
        public int[] CloseTo()
        {
            if (bonds == null) return Array.Empty<int>();
            return Array.ConvertAll(Array.FindAll(bonds, b => b.kind != BondKind.Coworker), b => b.other);
        }

        public void SetOverride(OverrideKind kind, Vector3 point, float seconds)
        {
            overrideKind = kind;
            OverridePoint = point;
            OverrideUntil = Time.time + seconds;
        }

        /// <summary>Server: home to its door for <paramref name="seconds"/>. A scripted override is never displaced.</summary>
        public void Shelter(float seconds)
        {
            if (Override == OverrideKind.Scripted) return;
            SettlementSociety society = Society;
            SettlementPlace door = society != null ? society.Place(society.HomeOf(this)) : null;
            if (door != null) SetOverride(OverrideKind.Shelter, door.Position, seconds);
        }

        /// <summary>Family, friend or coworker, whichever of the two holds the bond.</summary>
        public bool IsBondedTo(Resident other) =>
            other != null && other != this && other.Settlement == Settlement && (Names(other.index) || other.Names(index));

        private bool Names(int other)
        {
            if (bonds == null) return false;
            foreach (ResidentBond bond in bonds)
                if (bond.other == other) return true;
            return false;
        }

        /// <summary>Server: indoors. Leaves the target registry; every machine hides the body from presence.</summary>
        public void GoOffstage()
        {
            if (offstage) return;

            offstage = true;
            gunshotsHeard = 0;
            lastRacket = float.NegativeInfinity;
            if (Agent) Agent.Offstage = true;
            if (faction) faction.enabled = false;
            if (Presence) Presence.Publish(Activity.Sleep, 0, true);
        }

        /// <summary>Server: back out of the door. The routine publishes the real activity on its next pass.</summary>
        public void ComeOnstage()
        {
            if (!offstage) return;

            offstage = false;
            if (Agent) Agent.Offstage = false;
            if (faction) faction.enabled = true;
            if (Presence) Presence.Publish(Activity.Walking, 0, false);
        }

        /// <summary>Server: a disturbance got it up. Looks at the source; a bold one walks over to see.</summary>
        public void Wake(Vector3 source, bool approach)
        {
            ComeOnstage();

            float settle = ResidentTuning.Instance.settleSeconds;
            glancePoint = source;
            glanceUntil = Time.time + settle;
            if (approach) SetOverride(OverrideKind.Approach, source, settle);
        }

        private bool PlanWantsAwake()
        {
            SettlementSociety society = Society;
            DayPlan plan = society?.PlanFor(this);
            return plan != null && plan.At(society.NowMinutes, out PlanSegment segment) && segment.activity != Activity.Sleep;
        }

        private void HandleHeard(NoiseType type, Vector3 origin, Transform instigator)
        {
            if (!offstage || !Network.Decides) return;

            ResidentTuning tuning = ResidentTuning.Instance;
            bool disturbed = type switch
            {
                NoiseType.Gunshot or NoiseType.Explosion => ++gunshotsHeard >= tuning.gunshotsToWake,
                NoiseType.Custom => RacketLasted(tuning),
                _ => false,
            };
            if (disturbed) Wake(origin, Nerve >= BoldNerve);
        }

        // Prickly residents are woken by a shorter racket than warm ones (§6.4).
        private bool RacketLasted(ResidentTuning tuning)
        {
            float now = Time.time;
            if (now - lastRacket > RacketContinuityGap) racketStart = now;
            lastRacket = now;
            float needed = tuning.racketSecondsToWake * Mathf.Lerp(WarmRacketScale, PricklyRacketScale, Temper);
            return now - racketStart >= needed;
        }

        // An ally's alert — or an ally calling for help — about somebody this resident had no quarrel with. The bold
        // were already put in the fight by allyHurtGain; a bond to the victim (or the caller) puts anyone else in it;
        // the rest go home. One already fighting has nothing to decide, and is never sent home mid-fight.
        private void HandleAllyHurt(Transform attacker, Transform victim)
        {
            if (!Network.Decides || JoinsAlliesFights || !Provocation || Society == null || Provocation.IsProvoked) return;

            Resident hurt = victim ? victim.GetComponentInParent<Resident>() : null;
            if (IsBondedTo(hurt))
            {
                Provocation.Raise(AggressionBand.Grudge, attacker, AggressionInput.AllyHurt);
                return;
            }

            alerts.ClearAlert();
            Shelter(ResidentTuning.Instance.settleSeconds);
        }

        // Somebody hurt whoever is attacking this resident: a player doing that has defended it. Thanked once a
        // day, like the deed itself counts once a day — defending the same resident again is no new news.
        private void HandleAnyDamaged(HealthComponent hurt, int amount)
        {
            if (!Network.Decides || hurt == health || hurt.IsRestoring || IsDead) return;
            if (!hurt.TryGetComponent(out AgentTargeting theirs) || theirs.Target != transform) return;

            SettlementSociety society = Society;
            Transform defender = TargetResolution.EntityOf(hurt.LastDamageSource);
            string profile = ResidentMemory.ProfileOf(defender);
            if (society == null || profile == null) return;

            bool knew = Memory.Holds(profile, ActKind.Defended, index, society.Day);
            Gossip.Witness(this, defender, ActKind.Defended, ResidentTuning.Instance.noticeRadius);
            if (!knew && voice) voice.Say(Topic.Remark, defender, Observation.Defending);
        }

        // A killing blow raises OnDamage before OnDeath; it is reported once, as the death.
        private void HandleDamaged(int amount)
        {
            if (health.Alive) ReportHarm(ActKind.Hit);
        }

        private void HandleDied() => ReportHarm(ActKind.Killed);

        private void ReportHarm(ActKind act)
        {
            if (!Network.Decides || health.IsRestoring) return;

            Transform attacker = TargetResolution.EntityOf(health.LastDamageSource);
            if (!attacker) return;

            Gossip.Witness(this, attacker, act, ResidentTuning.Instance.noticeRadius);
        }
    }
}
