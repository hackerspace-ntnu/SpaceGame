// What every tribe currently thinks of every player, and the one place that answer is decided.
//
// The other meter. ProvocationModule is per agent and resets; this is per (faction, player), it
// persists, and it is what makes the world remember you — shoot enough Sand Tribe and the whole
// tribe knows, in every camp, after a reload.
//
// Server-owned and server-only, like NpcWorldSim beside which it lives. Goodwill decides who
// targets whom, so two machines keeping their own copy is two machines disagreeing about who is at
// war; clients get a read-only mirror of THEIR OWN bands in Task 3.3.
//
// Design: docs/superpowers/specs/2026-09-07-faction-system-design.md §3.4. The maths is in
// GoodwillMath, which is pure and tested on its own; this file owns the rows, the events and the
// clock.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.World;

namespace SpaceGame.Agents
{
    /// <summary>What a player did. Deltas are serialized per event on the ledger.</summary>
    public enum GoodwillEvent
    {
        /// <summary>Damaged a member. Magnitude is the fraction of that member's max health.</summary>
        Hit,

        /// <summary>Killed a member.</summary>
        Kill,

        /// <summary>Killed a member's mount or vehicle.</summary>
        MountKill,

        /// <summary>Killed a Clanker where a member could see it. The cheapest amends there is.</summary>
        ClankerKilled,

        /// <summary>Completed a trade. The other half of amends (plan Task 4.5).</summary>
        Traded,
    }

    public class FactionGoodwillLedger : MonoBehaviour
    {
        /// <summary>
        /// The live ledger, or null offline-and-unspawned. A static, so it is cleared explicitly:
        /// a domain that survives play mode would otherwise hand the next session the last one's
        /// rows (INVARIANTS, "statics outlive the world").
        /// </summary>
        public static FactionGoodwillLedger Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [Header("Who keeps a ledger")]
        [Tooltip("The factions that have an opinion of you. A tribe: Sand, and later Mechanics and " +
                 "Sky.\n\n" +
                 "An explicit list rather than a rule, because the rule needs Phase 4: the design " +
                 "says \"factions with a roster\", and FactionRoster does not exist yet. Clankers " +
                 "and Outlaws are deliberately absent and must stay absent — you cannot befriend " +
                 "either, so a ledger row for them would be a number nothing can ever move.")]
        [SerializeField] private FactionDefinition[] tribes = Array.Empty<FactionDefinition>();

        [Tooltip("The faction players are on in the open world. Used to tell a player apart from " +
                 "an NPC when a hit is attributed.")]
        [SerializeField] private FactionDefinition crewFaction;

        [Tooltip("GlobalRelationships. Read to spread one act across the factions that have an " +
                 "opinion about the victim's.")]
        [SerializeField] private FactionRelationshipTable relationships;

        [Header("Bands")]
        [SerializeField] private GoodwillThresholds thresholds = GoodwillThresholds.Default;

        [Header("What each event is worth")]
        [Tooltip("Cost of a hit that barely scratched somebody.")]
        [SerializeField] private float hitCostMin = 2f;

        [Tooltip("Cost of a hit that would have killed outright. Scaled between the two by the " +
                 "fraction of max health taken, so a graze is not a murder.")]
        [SerializeField] private float hitCostMax = 10f;

        [SerializeField] private float killCost = 25f;
        [SerializeField] private float mountKillCost = 15f;
        [SerializeField] private float clankerKilledCredit = 3f;
        [SerializeField] private float tradeCredit = 5f;

        [Header("Spreading")]
        [Tooltip("What a faction HOSTILE to the victim thinks of you for the same act. The enemy of " +
                 "my enemy: a fraction of the delta, with the sign flipped.")]
        [Range(0f, 1f)]
        [SerializeField] private float hostileSpread = 0.2f;

        [Tooltip("What a faction ALLIED to the victim thinks of it. Same sign, a fraction of the size.")]
        [Range(0f, 1f)]
        [SerializeField] private float alliedSpread = 0.4f;

        [Header("Recovery")]
        [Tooltip("Points shed per in-game hour, toward zero from either side. The brake on the " +
                 "damage-begets-hostility loop (design §3.4): a war has to be able to be waited out.")]
        [SerializeField] private float decayPerGameHour = 2f;

        [Tooltip("Real seconds per in-game hour, used only when no DayNightCycle is live — a test " +
                 "scene, or the arena. Normally read from the cycle's own cycleDuration / 24.")]
        [SerializeField] private float fallbackSecondsPerGameHour = 100f;

        [Header("Being hunted")]
        [Tooltip("How near an at-war player you have to be to be shot at for their sake.\n\n" +
                 "Deliberately large: the tribe is hunting one person, and anybody travelling with " +
                 "them is read as part of that. Walk away and it stops applying, which is what " +
                 "keeps it from permanently punishing a bystander for somebody else's war.")]
        [SerializeField] private float huntSpillRadius = 200f;

        /// <summary>
        /// Raised on the server when a band changes: faction, the player's profile id, the band
        /// left, the band entered. Task 3.3 puts it on the wire; Phase 7 barks it.
        /// </summary>
        public event Action<FactionDefinition, string, GoodwillBand, GoodwillBand> BandChanged;

        private sealed class Row
        {
            /// <summary>
            /// The faction this row belongs to, held rather than looked back up by id.
            ///
            /// <c>Registry&lt;FactionDefinition&gt;</c> only knows about assets Unity has loaded,
            /// and a faction is loaded when something referencing it is — so resolving the id was a
            /// lookup that could return null for a row the ledger itself had just written, which
            /// made those rows invisible to <see cref="All"/> and therefore to the saver.
            /// </summary>
            public FactionDefinition Faction;

            public float Value;
            public GoodwillBand Band = GoodwillBand.Wary;
        }

        // Keyed by faction id then profile id. Two levels rather than a tuple key so a whole
        // player's reputation can be enumerated when they disconnect, and so a faction's rows can
        // be walked without touching everybody else's.
        private readonly Dictionary<string, Dictionary<string, Row>> rows = new();

        private readonly HashSet<string> trackedIds = new();

        private double lastDecayClock;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[Goodwill] A second FactionGoodwillLedger. One world, one ledger — " +
                               "two would each hold half the answer.", this);
                enabled = false;
                return;
            }

            Instance = this;

            trackedIds.Clear();
            foreach (FactionDefinition tribe in tribes)
            {
                if (tribe == null || string.IsNullOrEmpty(tribe.ID)) continue;

                if (tribe.defaultStance == FactionRelationship.Hostile)
                {
                    Debug.LogWarning($"[Goodwill] {tribe.factionName} is Hostile by default, so its " +
                                     "goodwill can never be moved. Remove it from the tribes list.", this);
                    continue;
                }

                trackedIds.Add(tribe.ID);
            }

            lastDecayClock = Now;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Does this faction keep an opinion of players at all?</summary>
        public bool Tracks(FactionDefinition faction) =>
            faction != null && !string.IsNullOrEmpty(faction.ID) && trackedIds.Contains(faction.ID);

        // ── Reading ───────────────────────────────────────────────────────────────

        /// <summary>The raw number, for a dev overlay and for the saver.</summary>
        public float ValueFor(FactionDefinition faction, string profileId) =>
            TryGetRow(faction, profileId, out Row row) ? row.Value : GoodwillMath.Neutral;

        /// <summary>
        /// What <paramref name="faction"/> thinks of this player, on their own account. Does not
        /// include being caught up in somebody else's war — see <see cref="BandForEntity"/>.
        ///
        /// <para>
        /// <b>On a client this answers from the mirror, and the mirror only holds that client's own
        /// bands.</b> The rows are server state; a client asking about somebody else gets `Wary`,
        /// which is the honest answer to "I was not told". Nothing on a client needs anybody else's
        /// — targeting runs on the server, and the only client-side reader is that player's own
        /// readout.
        /// </para>
        /// </summary>
        public GoodwillBand BandFor(FactionDefinition faction, string profileId)
        {
            if (!Network.Server && Network.IsNetworked)
                return MirroredBand(faction);

            return TryGetRow(faction, profileId, out Row row) ? row.Band : GoodwillBand.Wary;
        }

        // ── The client's read-only mirror ─────────────────────────────────────────
        //
        // Written only by FactionGoodwillNetwork on the local player, from an Rpc the server aimed
        // at that client. Nothing on a client ever mutates a row.

        private readonly Dictionary<string, GoodwillBand> mirror = new();

        /// <summary>What this client was last told about its own standing with a faction.</summary>
        public GoodwillBand MirroredBand(FactionDefinition faction) =>
            faction != null && !string.IsNullOrEmpty(faction.ID) &&
            mirror.TryGetValue(faction.ID, out GoodwillBand band)
                ? band
                : GoodwillBand.Wary;

        /// <summary>Client-side only. Called by the local player's FactionGoodwillNetwork.</summary>
        public void SetMirroredBand(FactionDefinition faction, GoodwillBand band)
        {
            if (faction == null || string.IsNullOrEmpty(faction.ID)) return;

            GoodwillBand previous = MirroredBand(faction);
            mirror[faction.ID] = band;

            if (previous != band)
                BandChanged?.Invoke(faction, null, previous, band);
        }

        /// <summary>Client-side only. On disconnect, so a rejoin does not inherit stale bands.</summary>
        public void ClearMirror() => mirror.Clear();

        // ── Addressing a faction over the wire ────────────────────────────────────
        //
        // By INDEX into the tribes list, not by id. A faction id is a 32-character GUID and NetArg
        // has no string field; hashing it would need a hash that is stable across machines and
        // builds, which C#'s string.GetHashCode explicitly is not. Both machines load the same
        // persistentScene and therefore the same list in the same order, so the index is the
        // cheapest thing that is actually the same on both ends.

        /// <summary>The wire index of a faction, or -1 if this ledger does not track it.</summary>
        public int IndexOf(FactionDefinition faction)
        {
            if (faction == null) return -1;

            for (int i = 0; i < tribes.Length; i++)
                if (tribes[i] == faction) return i;

            return -1;
        }

        /// <summary>The faction at a wire index, or null if the index is not one this build has.</summary>
        public FactionDefinition TribeAt(int index) =>
            index >= 0 && index < tribes.Length ? tribes[index] : null;

        /// <summary>Every tracked faction, for the replay a client gets when it binds.</summary>
        public IEnumerable<FactionDefinition> Tribes()
        {
            foreach (FactionDefinition tribe in tribes)
                if (Tracks(tribe)) yield return tribe;
        }

        /// <summary>
        /// What <paramref name="faction"/> thinks of the player behind <paramref name="playerEntity"/>
        /// **right now, where they are standing** — their own band, or `HostileOnSight` if they are
        /// close to somebody this faction is at war with.
        ///
        /// <para>
        /// <b>Hunting is personal; being shot at is not.</b> At war the tribe goes after the player
        /// who earned it, and anybody travelling with them is read as part of that — you do not get
        /// to stand behind your crewmate. Proximity rather than membership is what makes it
        /// reversible: walk away and you stop being a target, so a bystander is never permanently
        /// punished for somebody else's war.
        /// </para>
        /// <para>
        /// <b>"Travelling with them" is same-faction, and that one rule covers both game modes.</b>
        /// In the open world every player is on the crew faction, so it means the crew. In a versus
        /// match <c>MatchManager</c> re-teams players into per-team factions, so it means that
        /// player's team and nobody on the other side. No mode check anywhere.
        /// </para>
        /// </summary>
        public GoodwillBand BandForEntity(FactionDefinition faction, EntityFaction playerEntity)
        {
            if (playerEntity == null || !Tracks(faction))
                return GoodwillBand.Wary;

            if (!TryGetProfile(playerEntity.gameObject, out string profileId))
                return GoodwillBand.Wary;

            GoodwillBand own = BandFor(faction, profileId);

            // Already as bad as it gets on their own account; nothing to inherit.
            if (GoodwillMath.IsHostile(own))
                return own;

            return NearAnHuntedAlly(faction, playerEntity, profileId)
                ? GoodwillBand.HostileOnSight
                : own;
        }

        /// <summary>
        /// Is this player standing near a same-faction player this tribe is hunting?
        ///
        /// Walks <see cref="EntityTargetRegistry"/> rather than a physics overlap: the candidates
        /// are entities, there are at most a handful of players, and a sphere cast would find
        /// colliders this would have to map back to entities anyway.
        /// </summary>
        private bool NearAnHuntedAlly(FactionDefinition faction, EntityFaction playerEntity, string profileId)
        {
            float radiusSqr = huntSpillRadius * huntSpillRadius;
            Vector3 here = playerEntity.transform.position;

            IReadOnlyList<EntityFaction> all = EntityTargetRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityFaction other = all[i];
                if (other == null || other == playerEntity) continue;

                // Same side as them: the crew in the world, their team in a versus match.
                if (other.Faction != playerEntity.Faction) continue;

                if ((other.transform.position - here).sqrMagnitude > radiusSqr) continue;

                if (!TryGetProfile(other.gameObject, out string otherId) || otherId == profileId)
                    continue;

                if (IsHuntedCompany(here, playerEntity.Faction,
                                    other.transform.position, other.Faction,
                                    BandFor(faction, otherId), huntSpillRadius))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Does one other player make this one a target by standing near them? The whole rule, in
        /// one place and with no world around it, because it is three conditions that each have to
        /// hold and the interesting cases are all about which one fails.
        ///
        /// <para>
        /// <b>Same side</b> is the load-bearing one, and it is what makes this work in both game
        /// modes with no mode check anywhere: in the open world every player is on the crew faction,
        /// so it means the crew; in a versus match <c>MatchManager</c> re-teams players into
        /// per-team factions, so it means that player's team and nobody on the other side.
        /// </para>
        /// </summary>
        public static bool IsHuntedCompany(Vector3 here, FactionDefinition mySide,
                                           Vector3 theirPosition, FactionDefinition theirSide,
                                           GoodwillBand theirBand, float radius)
        {
            if (!GoodwillMath.IsHunting(theirBand))
                return false;

            // Neither null nor a different side. Two players with no faction at all are not
            // "the same team" — they are two unknowns, and guessing would put a spectator in a war.
            if (mySide == null || theirSide == null || mySide != theirSide)
                return false;

            return (theirPosition - here).sqrMagnitude <= radius * radius;
        }

        // ── Writing ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Something happened to <paramref name="victimFaction"/> and a player did it.
        ///
        /// <paramref name="magnitude"/> means whatever <paramref name="kind"/> says: the fraction of
        /// max health for a <see cref="GoodwillEvent.Hit"/>, and 1 for everything else.
        ///
        /// <para>
        /// Silently does nothing for an attacker who is not a player, a faction that keeps no
        /// ledger, or a machine that is not the server. All three are ordinary — most damage in the
        /// world is NPC on NPC.
        /// </para>
        /// </summary>
        public void Report(FactionDefinition victimFaction, EntityFaction attacker,
                           GoodwillEvent kind, float magnitude = 1f)
        {
            if (!Network.Decides || attacker == null) return;
            if (!TryGetProfile(attacker.gameObject, out string profileId)) return;

            float delta = DeltaFor(kind, magnitude);
            if (Mathf.Approximately(delta, 0f)) return;

            // The faction it was done to.
            Move(victimFaction, profileId, delta);

            // ...and everybody who has an opinion about them. The enemy of my enemy is pleased; a
            // friend of theirs is not. Both are a fraction of the original, so a single shot cannot
            // move the whole map.
            SpreadToOthers(victimFaction, profileId, delta);
        }

        private float DeltaFor(GoodwillEvent kind, float magnitude) => kind switch
        {
            GoodwillEvent.Hit           => GoodwillMath.HitDelta(magnitude, 1f, hitCostMin, hitCostMax),
            GoodwillEvent.Kill          => -Mathf.Abs(killCost),
            GoodwillEvent.MountKill     => -Mathf.Abs(mountKillCost),
            GoodwillEvent.ClankerKilled => Mathf.Abs(clankerKilledCredit),
            GoodwillEvent.Traded        => Mathf.Abs(tradeCredit),
            _                           => 0f,
        };

        private void SpreadToOthers(FactionDefinition victimFaction, string profileId, float delta)
        {
            if (victimFaction == null) return;

            foreach (FactionDefinition other in tribes)
            {
                if (other == null || other == victimFaction || !Tracks(other)) continue;

                // The STANCE layer, faction to faction, which is the table's own question — not the
                // entity resolver. FactionRelations.Resolve is the only way to ask what two
                // ENTITIES are to each other; asking what two FACTIONS are is what the table is for,
                // and layering a grudge or a goodwill row underneath it here would be circular.
                FactionRelationship stance = relationships != null
                    ? relationships.Get(victimFaction, other)
                    : FactionRelationship.Neutral;

                float share = stance switch
                {
                    FactionRelationship.Hostile => -delta * hostileSpread,
                    FactionRelationship.Allied  => delta * alliedSpread,
                    _                           => 0f,
                };

                if (!Mathf.Approximately(share, 0f))
                    Move(other, profileId, share);
            }
        }

        private void Move(FactionDefinition faction, string profileId, float delta)
        {
            if (!Tracks(faction) || string.IsNullOrEmpty(profileId)) return;

            Row row = RowFor(faction, profileId);
            row.Value = GoodwillMath.Apply(row.Value, delta);
            Reband(faction, profileId, row);
        }

        /// <summary>Restore-only. Called by the saver; do not call from gameplay.</summary>
        public void RestoreRow(FactionDefinition faction, string profileId, float value, GoodwillBand band)
        {
            if (!Tracks(faction) || string.IsNullOrEmpty(profileId)) return;

            Row row = RowFor(faction, profileId);
            row.Value = Mathf.Clamp(value, GoodwillMath.Min, GoodwillMath.Max);

            // The band is restored rather than recomputed, because hysteresis makes the band a
            // function of its own history: a player saved inside the sticky part of HostileOnSight
            // would otherwise come back Wary and the tribe would have forgiven them over a loading
            // screen. BandChanged is deliberately NOT raised — nothing changed, the world is only
            // being rebuilt.
            row.Band = band;
        }

        private void Reband(FactionDefinition faction, string profileId, Row row)
        {
            GoodwillBand next = GoodwillMath.BandFor(row.Value, row.Band, thresholds);
            if (next == row.Band) return;

            GoodwillBand previous = row.Band;
            row.Band = next;
            BandChanged?.Invoke(faction, profileId, previous, next);
        }

        // ── Time ──────────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!Network.Decides) return;

            float hours = (float)((Now - lastDecayClock) / SecondsPerGameHour);
            if (hours <= 0f) return;

            lastDecayClock = Now;
            DecayAll(hours);
        }

        /// <summary>
        /// Shed <paramref name="hours"/> of in-game time from every row. Public so the saver can
        /// apply an absence in one go on restore — time away from the world counts (design §3.4).
        /// </summary>
        public void DecayAll(float hours)
        {
            if (hours <= 0f || decayPerGameHour <= 0f) return;

            foreach (KeyValuePair<string, Dictionary<string, Row>> byFaction in rows)
            {
                foreach (KeyValuePair<string, Row> entry in byFaction.Value)
                {
                    Row row = entry.Value;
                    float before = row.Value;
                    row.Value = GoodwillMath.Decay(row.Value, hours, decayPerGameHour);

                    if (!Mathf.Approximately(before, row.Value))
                        Reband(row.Faction, entry.Key, row);
                }
            }
        }

        /// <summary>
        /// Real seconds in one in-game hour, from the live day/night cycle when there is one. The
        /// fallback is for a scene with no sky — a test fixture, or the arena.
        /// </summary>
        private float SecondsPerGameHour
        {
            get
            {
                IReadOnlyList<DayNightCycle> live = DayNightCycle.Live;
                if (live != null && live.Count > 0 && live[0] != null && live[0].cycleDuration > 0f)
                    return live[0].cycleDuration / 24f;

                return Mathf.Max(1f, fallbackSecondsPerGameHour);
            }
        }

        /// <summary>
        /// Shed the decay a player missed while they were away. Design §3.4: "decay runs on the
        /// server clock while the world is loaded, from the saved lastChangeTime when it is not, so
        /// an absence counts."
        ///
        /// <para>
        /// Real seconds are converted through the same <see cref="SecondsPerGameHour"/> the loaded
        /// clock uses, so a day away and a day played forgive the same amount. Applied to every row,
        /// not just the returning player's: the world kept turning for everybody.
        /// </para>
        /// </summary>
        public void ApplyAbsence(double realSecondsAway)
        {
            if (realSecondsAway <= 0d) return;

            DecayAll((float)(realSecondsAway / SecondsPerGameHour));
        }

        private static double Now => Time.timeAsDouble;

        // ── Rows ──────────────────────────────────────────────────────────────────

        private bool TryGetRow(FactionDefinition faction, string profileId, out Row row)
        {
            row = null;
            if (faction == null || string.IsNullOrEmpty(faction.ID) || string.IsNullOrEmpty(profileId))
                return false;

            return rows.TryGetValue(faction.ID, out Dictionary<string, Row> byPlayer)
                   && byPlayer.TryGetValue(profileId, out row);
        }

        private Row RowFor(FactionDefinition faction, string profileId)
        {
            if (!rows.TryGetValue(faction.ID, out Dictionary<string, Row> byPlayer))
                rows[faction.ID] = byPlayer = new Dictionary<string, Row>();

            if (!byPlayer.TryGetValue(profileId, out Row row))
                byPlayer[profileId] = row = new Row { Faction = faction };

            return row;
        }

        /// <summary>Every (faction, player, row) the ledger holds. For the saver and a dev overlay.</summary>
        public IEnumerable<(FactionDefinition Faction, string ProfileId, float Value, GoodwillBand Band)> All()
        {
            foreach (KeyValuePair<string, Dictionary<string, Row>> byFaction in rows)
            {
                foreach (KeyValuePair<string, Row> entry in byFaction.Value)
                {
                    Row row = entry.Value;
                    if (row.Faction == null) continue;

                    yield return (row.Faction, entry.Key, row.Value, row.Band);
                }
            }
        }

        // ── Identity ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The persisted profile id behind a GameObject, or false if it is not a player.
        ///
        /// Goes through <see cref="PlayerSaveService"/>, which is the component that already binds
        /// player-scoped saves — so the ledger is keyed by exactly the same identity its own saver
        /// will be (plan Task 3.3, persistence PATH C). Resolving it any other way would be a second
        /// answer free to drift from the first.
        /// </summary>
        private static bool TryGetProfile(GameObject candidate, out string profileId)
        {
            profileId = null;
            if (candidate == null) return false;

            PlayerSaveService players = SaveManager.Instance?.Players;
            return players != null && players.TryGetProfileFor(candidate, out profileId);
        }

        private void OnValidate()
        {
            hitCostMin = Mathf.Max(0f, hitCostMin);
            hitCostMax = Mathf.Max(hitCostMin, hitCostMax);
            killCost = Mathf.Max(0f, killCost);
            mountKillCost = Mathf.Max(0f, mountKillCost);
            decayPerGameHour = Mathf.Max(0f, decayPerGameHour);
            huntSpillRadius = Mathf.Max(0f, huntSpillRadius);
            fallbackSecondsPerGameHour = Mathf.Max(1f, fallbackSecondsPerGameHour);
            thresholds.hysteresis = Mathf.Max(0f, thresholds.hysteresis);
        }
    }
}
