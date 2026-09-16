// Decides when a tribe's war with one player has a war party in the field, and settles the score
// every time one is resolved.
//
// The director decides WHEN a party exists; NpcWorldSim moves it. Kept apart so the world sim does not
// become a catch-all. Server-only, on the NpcWorldSim object beside FactionGoodwillLedger.
//
// Design: docs/superpowers/specs/2026-09-16-rosters-and-war-parties-design.md §5–§7.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.World;
using Random = UnityEngine.Random;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NpcWorldSim))]
    public class WarPartyDirector : MonoBehaviour
    {
        public static WarPartyDirector Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [SerializeField] private WarPartySettings settings = WarPartySettings.Default;

        [Tooltip("Seconds between decisions. Independent of the world sim's tick: a party the sim marks " +
                 "wiped out waits, frozen, until the next decision resolves it.")]
        [SerializeField] private float decisionInterval = 0.5f;

        [Tooltip("How close a party must come to its trail fix to count as there. Folded or spawned.")]
        [SerializeField] private float trailArriveRadius = 20f;

        [Tooltip("Seconds a party waits for a quarry who has not been in the game this session (a party " +
                 "restored by a load) before it is released unresolved. The war itself keeps.")]
        [SerializeField] private float absentQuarryGrace = 120f;

        [Tooltip("Directions tried for a fallback origin before accepting one a player might see.")]
        [SerializeField] private int fallbackAttempts = 8;

        private sealed class QuarryWatch
        {
            public HealthComponent Health;
            public Action Handler;
        }

        private readonly WarBook book = new();
        private readonly Dictionary<War, QuarryWatch> watches = new();

        // Wars whose quarry has been in the game this session. A quarry absent since load is still
        // binding; one that was here and is gone has left, and only then is their party released.
        private readonly HashSet<War> present = new();

        // Seconds each war's party has waited for a quarry never present this session. Reset on binding.
        private readonly Dictionary<War, float> absentFor = new();

        private readonly List<Vector3> playerPositions = new();
        private readonly HashSet<FactionDefinition> reportedMissingTemplate = new();

        private NpcWorldSim sim;
        private FactionGoodwillLedger subscribedLedger;
        private float timer;

        public WarBook Book => book;

        private float Staging => WarPartyRules.StagingDistance(sim.SpawnRadius, settings.stagingMargin);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[WarParty] A second WarPartyDirector. One world, one director.", this);
                enabled = false;
                return;
            }

            Instance = this;
            sim = GetComponent<NpcWorldSim>();
        }

        // Start rather than OnEnable: the ledger on this same object may not have run Awake yet.
        private void Start()
        {
            subscribedLedger = FactionGoodwillLedger.Instance;
            if (subscribedLedger != null) subscribedLedger.BandChanged += OnBandChanged;
            if (sim != null) sim.QuarrySighted += OnQuarrySighted;
        }

        private void OnDestroy()
        {
            if (subscribedLedger != null) subscribedLedger.BandChanged -= OnBandChanged;
            if (sim != null) sim.QuarrySighted -= OnQuarrySighted;

            foreach (War war in book.Snapshot()) Unwatch(war);
            absentFor.Clear();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!Network.Decides) return;

            timer -= Time.deltaTime;
            if (timer > 0f) return;

            float elapsed = decisionInterval - timer;
            timer = decisionInterval;
            Step(elapsed);
        }

        private void Step(float elapsed)
        {
            FactionGoodwillLedger ledger = FactionGoodwillLedger.Instance;
            if (ledger == null || sim == null) return;

            // Adopt before reconciling (spec §7): reconciling first would see "at war, no party" after
            // a load and raise a second party beside the restored one.
            AdoptRestoredParties();
            Reconcile(ledger);

            book.Tick(elapsed);
            sim.CollectPlayerPositions(playerPositions);

            foreach (War war in book.Snapshot())
                StepWar(war, elapsed);
        }

        // ── Saved escalation (FactionGoodwillSaveable) ───────────────────────────

        public int WarTierFor(FactionDefinition tribe, string profileId) => book.TierFor(tribe, profileId);

        public void RestoreWarTier(FactionDefinition tribe, string profileId, int tier) =>
            book.RestoreTier(tribe, profileId, tier);

        // ── Wars opening and closing ─────────────────────────────────────────────

        private void OnBandChanged(FactionDefinition tribe, string profileId, GoodwillBand previous, GoodwillBand next)
        {
            // A null profile is a client's mirror updating: a picture of a decision, not one.
            if (!Network.Decides || string.IsNullOrEmpty(profileId)) return;

            if (next == GoodwillBand.AtWar) book.Open(tribe, profileId);
            else if (previous == GoodwillBand.AtWar) EndWar(book.Find(tribe, profileId));
        }

        /// <summary>
        /// RestoreRow raises no event, so after a load the ledger can say "at war" without the director
        /// ever hearing it. Only a bound quarry's war is ended here: an unbound player's rows have not
        /// been restored yet, and their Wary default is not an answer.
        /// </summary>
        private void Reconcile(FactionGoodwillLedger ledger)
        {
            foreach ((FactionDefinition tribe, string profileId, float _, GoodwillBand band) in ledger.All())
            {
                if (band != GoodwillBand.AtWar) continue;

                bool restored = book.Find(tribe, profileId) == null;
                War war = book.Open(tribe, profileId);

                // spec §7: "The cooldown is not saved. It restarts from full on load." A war found
                // here that was not already open was not adopted alongside its party (that runs
                // first) and was not opened live by OnBandChanged this session — so it must not let
                // StepWar raise a party the instant its quarry binds. Only a live open keeps the
                // War constructor's 0 cooldown, which is what makes that one raise at once.
                if (restored) book.ClearParty(war, settings.partyCooldown);
            }

            foreach (War war in book.Snapshot())
            {
                if (!TryGetQuarry(war.ProfileId, out _)) continue;
                if (ledger.BandFor(war.Tribe, war.ProfileId) != GoodwillBand.AtWar) EndWar(war);
            }
        }

        private void EndWar(War war)
        {
            if (war == null) return;

            if (war.HasParty) sim.ReleaseGroup(war.PartyGroupId);
            Unwatch(war);
            present.Remove(war);
            absentFor.Remove(war);
            book.Close(war);

            Notify(war, WarNotice.GaveUp);
        }

        private void AdoptRestoredParties()
        {
            foreach (NpcGroup group in new List<NpcGroup>(sim.Groups))
            {
                if (!group.IsWarParty || book.FindByGroup(group.Id) != null) continue;

                FactionDefinition tribe = sim.TribeOf(group);
                if (tribe == null) continue;

                War existing = book.Find(tribe, group.QuarryProfileId);
                if (existing != null && existing.HasParty && sim.FindGroup(existing.PartyGroupId) != null)
                {
                    Debug.LogWarning($"[WarParty] '{group.Id}' is a second party for the war that " +
                                     $"'{existing.PartyGroupId}' is already fighting; releasing it.", this);
                    sim.ReleaseGroup(group.Id);
                    continue;
                }

                book.Adopt(tribe, group.QuarryProfileId, group.Id, group.Tier);
            }
        }

        // ── One war ──────────────────────────────────────────────────────────────

        private void StepWar(War war, float elapsed)
        {
            if (book.Find(war.Tribe, war.ProfileId) != war) return;   // closed earlier this step

            if (!TryGetQuarry(war.ProfileId, out GameObject quarry))
            {
                // Here earlier and gone now: they left. The war waits in their save (spec §6); their
                // party does not wait in the world, and nothing is resolved. Never here this session
                // (a party a load restored): the same, once they have had the grace to rejoin.
                if (present.Remove(war) && war.HasParty) ReleaseUnresolved(war);
                else if (war.HasParty && AbsentPastGrace(war, elapsed)) ReleaseUnresolved(war);

                Unwatch(war);
                return;
            }

            present.Add(war);
            absentFor.Remove(war);
            Watch(war, quarry);

            Vector3 quarryPosition = quarry.transform.position;

            if (!war.HasParty)
            {
                if (book.ReadyToRaise(war)) Raise(war, quarryPosition);
                return;
            }

            NpcGroup party = sim.FindGroup(war.PartyGroupId);
            if (party == null)
            {
                book.ClearParty(war, settings.partyCooldown);
                return;
            }

            if (WarPartyRules.IsDefeated(party.FightersSpawned, party.FightersDead, party.WipedOut))
            {
                Resolve(war, Reckoning.Defeated);
                return;
            }

            if (WarPartyRules.ShouldAbandon(party.Position, quarryPosition, settings.maxPursuitDistance))
            {
                Resolve(war, Reckoning.Abandoned);
                return;
            }

            if (!party.Spawned)
            {
                Track(party, quarryPosition);
                return;
            }

            // Spawned and out of sight of its quarry for trailInterval: a fresh fix, walked to by the
            // live leader. Catch-up stays folded-only — a spawned party is in somebody's view.
            if (NeedsFreshTrail(party))
            {
                RefreshTrail(party, quarryPosition);
                sim.SteerSpawned(party, party.Lead, trailArriveRadius);
            }
        }

        private bool AbsentPastGrace(War war, float elapsed)
        {
            absentFor.TryGetValue(war, out float absent);
            absent += elapsed;
            absentFor[war] = absent;
            return absent > absentQuarryGrace;
        }

        private void ReleaseUnresolved(War war)
        {
            sim.ReleaseGroup(war.PartyGroupId);
            book.ClearParty(war, settings.partyCooldown);
            absentFor.Remove(war);
        }

        private void Raise(War war, Vector3 quarryPosition)
        {
            NpcGroupTemplate template = sim.WarPartyTemplateFor(war.Tribe);
            if (template == null)
            {
                if (reportedMissingTemplate.Add(war.Tribe))
                    Debug.LogError($"[WarParty] {war.Tribe.factionName} is at war but the NpcWorldSim has no " +
                                   "war-party template for it (runtimeOnly, bountyHunters, tribe set). Run " +
                                   "Tools/SpaceGame/Agents/Wire War Party Templates.", this);

                book.ClearParty(war, settings.partyCooldown);
                return;
            }

            string id = book.AssignParty(war, taken => sim.FindGroup(taken) != null);
            NpcGroup party = sim.CreateGroup(template, id, ChooseOrigin(quarryPosition));
            if (party == null)
            {
                book.ClearParty(war, settings.partyCooldown);
                return;
            }

            party.QuarryProfileId = war.ProfileId;
            party.Tier = war.Tier;
            RefreshTrail(party, quarryPosition);

            Notify(war, WarNotice.Raised);
        }

        /// <summary>The nearest camp if nobody can see it, else a point beyond staging nobody can see.</summary>
        private Vector3 ChooseOrigin(Vector3 quarryPosition)
        {
            float staging = Staging;

            if (WorldSiteRegistry.TryFindNearest(SiteKind.Camp, quarryPosition, settings.campSearchRadius, out WorldSite camp)
                && WarPartyRules.IsUnobserved(camp.Position, playerPositions, staging))
                return camp.Position;

            Vector3 origin = quarryPosition;
            for (int attempt = 0; attempt < Mathf.Max(1, fallbackAttempts); attempt++)
            {
                origin = WarPartyRules.FallbackOrigin(quarryPosition, Random.insideUnitCircle, staging + settings.fallbackExtra);
                if (WarPartyRules.IsUnobserved(origin, playerPositions, staging)) break;
            }

            return origin;
        }

        /// <summary>Refresh a folded party's fuzzy fix on its quarry and catch it up unseen (spec §5.2).</summary>
        private void Track(NpcGroup party, Vector3 quarryPosition)
        {
            if (NeedsFreshTrail(party)) RefreshTrail(party, quarryPosition);

            float staging = Staging;
            if (WarPartyRules.TryCatchUp(party.Position, party.Lead, staging + settings.trailFuzz,
                                         playerPositions, staging, out Vector3 moved))
                party.Position = moved;
        }

        private bool NeedsFreshTrail(NpcGroup party) => !party.HasLead || party.LeadAge >= settings.trailInterval;

        /// <summary>Where the quarry roughly is: tracks, not telepathy (spec §5.2).</summary>
        private void RefreshTrail(NpcGroup party, Vector3 quarryPosition)
        {
            party.Lead = WarPartyRules.TrailFix(quarryPosition, Random.insideUnitCircle, settings.trailFuzz);
            party.HasLead = true;
            party.LeadAge = 0f;
            party.ArriveRadius = trailArriveRadius;
        }

        private void Resolve(War war, Reckoning outcome)
        {
            sim.ReleaseGroup(war.PartyGroupId);

            int maxTier = war.Tribe.roster != null ? war.Tribe.roster.MaxTier : 0;
            book.Resolve(war, outcome, maxTier, settings.partyCooldown);

            // After the book: a credit that ends the war closes it through BandChanged, which must find
            // the party already settled.
            FactionGoodwillLedger ledger = FactionGoodwillLedger.Instance;
            if (ledger != null) ledger.Credit(war.Tribe, war.ProfileId, WarPartyRules.CreditFor(outcome, settings));

            if (outcome == Reckoning.Defeated && book.Find(war.Tribe, war.ProfileId) == war)
                Notify(war, WarNotice.Weakening);
        }

        // ── Caught ───────────────────────────────────────────────────────────────

        private void Watch(War war, GameObject quarry)
        {
            HealthComponent health = quarry.GetComponentInChildren<HealthComponent>();
            if (watches.TryGetValue(war, out QuarryWatch current) && current.Health == health) return;

            Unwatch(war);
            if (health == null) return;

            var watch = new QuarryWatch { Health = health };
            watch.Handler = () => OnQuarryDied(war, watch.Health);
            health.OnDeath += watch.Handler;
            watches[war] = watch;
        }

        private void Unwatch(War war)
        {
            if (!watches.TryGetValue(war, out QuarryWatch watch)) return;

            if (watch.Health != null) watch.Health.OnDeath -= watch.Handler;
            watches.Remove(war);
        }

        /// <summary>Caught only if THIS party dealt the blow; a Clanker, a fall or another tribe is not a reckoning.</summary>
        private void OnQuarryDied(War war, HealthComponent health)
        {
            if (!Network.Decides || health == null || health.IsRestoring) return;
            if (book.Find(war.Tribe, war.ProfileId) != war || !war.HasParty) return;

            if (WarPartyRules.IsCaughtBy(GroupMembership.GroupIdOf(health.LastDamageSource), war.PartyGroupId))
                Resolve(war, Reckoning.Caught);
        }

        // ── Telling people ───────────────────────────────────────────────────────

        private void OnQuarrySighted(NpcGroup group, GameObject fighter)
        {
            FactionDefinition tribe = sim.TribeOf(group);
            DialogPool pool = tribe != null && tribe.roster != null ? tribe.roster.hostileLines : null;
            if (pool == null || pool.lines == null || pool.lines.Length == 0) return;

            if (fighter != null && fighter.TryGetComponent(out ChatterModule chatter))
                chatter.WarCry(Random.Range(0, pool.lines.Length));
        }

        private static void Notify(War war, WarNotice notice)
        {
            if (TryGetQuarry(war.ProfileId, out GameObject quarry)
                && quarry.TryGetComponent(out FactionGoodwillNetwork network))
                network.Notify(war.Tribe, notice);
        }

        private static bool TryGetQuarry(string profileId, out GameObject quarry)
        {
            quarry = null;
            PlayerSaveService players = SaveManager.Instance != null ? SaveManager.Instance.Players : null;
            return players != null && players.TryGetBoundPlayer(profileId, out quarry);
        }

        private void OnValidate()
        {
            decisionInterval = Mathf.Clamp(decisionInterval, 0.1f, 5f);
            fallbackAttempts = Mathf.Max(1, fallbackAttempts);
            trailArriveRadius = Mathf.Max(1f, trailArriveRadius);
            absentQuarryGrace = Mathf.Max(0f, absentQuarryGrace);
            settings.partyCooldown = Mathf.Max(0f, settings.partyCooldown);
            settings.campSearchRadius = Mathf.Max(0f, settings.campSearchRadius);
            settings.stagingMargin = Mathf.Max(0f, settings.stagingMargin);
            settings.fallbackExtra = Mathf.Max(0f, settings.fallbackExtra);
            settings.caughtCredit = Mathf.Max(0f, settings.caughtCredit);
            settings.defeatedCredit = Mathf.Max(0f, settings.defeatedCredit);
            settings.maxPursuitDistance = Mathf.Max(settings.campSearchRadius, settings.maxPursuitDistance);
            settings.trailInterval = Mathf.Max(1f, settings.trailInterval);
            settings.trailFuzz = Mathf.Max(0f, settings.trailFuzz);
        }
    }
}
