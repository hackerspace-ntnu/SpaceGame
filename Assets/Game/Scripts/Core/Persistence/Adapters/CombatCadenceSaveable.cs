using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the clocks every weapon on an agent runs on: cooldowns, bursts in flight, and how
    /// long it has been holding somebody in its sights.
    ///
    /// <b>Every one of these reloaded at zero, and that is a free hit for whoever reloads.</b> A
    /// creature caught mid-recovery from a swing comes back able to strike in the same frame it
    /// hydrates. A rifleman two rounds into a three-round burst drops the third. An NPC that had
    /// been tracking the player for four tenths of its half-second reaction delay starts the count
    /// again, so every load hands the player another moment of grace — and a player who reloads
    /// often is never shot at by anything.
    ///
    /// <b>One saver, three components, because they are one question.</b> An agent can carry a melee
    /// module, a profile-driven ranged module and one or more artifact-firing modules at once — they
    /// are composed, not alternatives — and all three keep the same shape of state for the same
    /// reason. Splitting them would mean three keys, three policy clauses and three files to keep in
    /// step, for state that is captured and restored identically.
    ///
    /// <b>Positional over each component array.</b> An NPC with two <see cref="NpcItemUseModule"/>s
    /// — the documented way to give it a weapon it swaps by range — has two independent cadences,
    /// and index i is component i in <c>GetComponents</c> order. A module added to the prefab since
    /// the save reads as "at its defaults", which is the right answer for a weapon that did not
    /// exist to have been fired.
    ///
    /// <b>Deferred only for the aim tracker.</b> The cadences are self-contained numbers and are
    /// applied as the record lands. The one reference here is the target an artifact-firing module
    /// was leading, which is another entity or a player and does not exist yet at that point.
    /// </summary>
    public class CombatCadenceSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "combatCadence";

        public string SaveKey => Key;

        public struct RangedState
        {
            public float cooldown;
            public int burstRemaining;
            public float burstTimer;
            public int burstSpread;
            public bool engaged;
            public float strafeTimer;
            public bool hasStrafeDestination;
            public Vector3 strafeDestination;

            /// <summary>
            /// Who the last shot was billed to, so <c>OnKillEvent</c> can still be attributed after a
            /// load. Unresolvable is an ordinary answer and means "nobody", which costs one missed
            /// kill credit on a target that has since gone.
            /// </summary>
            public SaveRef firingAt;
        }

        public struct MeleeState
        {
            public float cooldown;

            /// <summary>
            /// Seconds left of the swing this agent is committed to. The one field here that is
            /// visible rather than merely fair: it is what stops Chase reclaiming the frame and
            /// walking the agent out of its own attack animation.
            /// </summary>
            public float commit;

            public bool engaged;
        }

        public struct ItemUseState
        {
            public float cooldown;
            public int burstRemaining;
            public float burstTimer;

            /// <summary>The has-aimed-long-enough accumulator behind <c>reactionDelay</c>.</summary>
            public float targetHeldFor;

            public bool hasFacingTarget;
            public Vector3 facingPoint;

            /// <summary>
            /// The target the lead-prediction tracker was differencing against, and where it was.
            /// Restored together or not at all: a tracker given a target with no previous position
            /// reads a whole session's displacement as one frame of movement and leads the shot into
            /// the next county.
            /// </summary>
            public SaveRef lastTarget;

            public Vector3 lastTargetPosition;
        }

        public struct State
        {
            public RangedState[] ranged;
            public MeleeState[] melee;
            public ItemUseState[] itemUse;
        }

        private AgentRangedCombatModule[] rangedModules;
        private CloseCombatModule[] meleeModules;
        private NpcItemUseModule[] itemUseModules;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private AgentRangedCombatModule[] Ranged =>
            rangedModules ??= GetComponents<AgentRangedCombatModule>();

        private CloseCombatModule[] Melee =>
            meleeModules ??= GetComponents<CloseCombatModule>();

        private NpcItemUseModule[] ItemUse =>
            itemUseModules ??= GetComponents<NpcItemUseModule>();

        private State pending;
        private bool hasPending;

        public object CaptureState()
        {
            AgentRangedCombatModule[] ranged = Ranged;
            CloseCombatModule[] melee = Melee;
            NpcItemUseModule[] itemUse = ItemUse;

            if (ranged.Length == 0 && melee.Length == 0 && itemUse.Length == 0) return null;

            var state = new State
            {
                ranged = new RangedState[ranged.Length],
                melee = new MeleeState[melee.Length],
                itemUse = new ItemUseState[itemUse.Length],
            };

            for (int i = 0; i < ranged.Length; i++)
            {
                AgentRangedCombatModule m = ranged[i];
                state.ranged[i] = new RangedState
                {
                    cooldown = m.CooldownTimer,
                    burstRemaining = m.BurstRemaining,
                    burstTimer = m.BurstTimer,
                    burstSpread = m.BurstSpread,
                    engaged = m.Engaged,
                    strafeTimer = m.StrafeTimer,
                    hasStrafeDestination = m.HasStrafeDestination,
                    strafeDestination = m.StrafeDestination,
                    firingAt = SaveRef.From(m.FiringAtObject),
                };
            }

            for (int i = 0; i < melee.Length; i++)
            {
                CloseCombatModule m = melee[i];
                state.melee[i] = new MeleeState
                {
                    cooldown = m.CooldownTimer,
                    commit = m.CommitTimer,
                    engaged = m.Engaged,
                };
            }

            for (int i = 0; i < itemUse.Length; i++)
            {
                NpcItemUseModule m = itemUse[i];
                state.itemUse[i] = new ItemUseState
                {
                    cooldown = m.CooldownTimer,
                    burstRemaining = m.BurstRemaining,
                    burstTimer = m.BurstTimer,
                    targetHeldFor = m.TargetHeldFor,
                    hasFacingTarget = m.HasFacingTarget,
                    facingPoint = m.FacingPoint,
                    lastTarget = SaveRef.From(m.LastTarget),
                    lastTargetPosition = m.LastTargetPosition,
                };
            }

            return state;
        }

        public void RestoreState(JObject state)
        {
            hasPending = false;
            pending = default;

            // No record under this key means the agent was at its defaults when the save was taken,
            // and a saver has to be able to say so. Everything is put back to what OnEnable would
            // have left — including the aim trackers, which must not keep a target from a stale
            // pending payload.
            if (state == null)
            {
                ResetToDefaults();
                return;
            }

            pending = state.ToObject<State>(SaveSerializer.Serializer);
            hasPending = true;

            ApplyCadences(in pending);
        }

        /// <summary>
        /// Runs many times — once world-wide, again on every PlayerBound, again per late chunk
        /// hydrate — so it is idempotent by construction: seeding the aim tracker twice with the same
        /// target and the same position leaves it exactly where the first pass did.
        /// </summary>
        public void OnLoadComplete()
        {
            if (!hasPending) return;

            NpcItemUseModule[] itemUse = ItemUse;
            ItemUseState[] records = pending.itemUse;

            bool resolvedEverything = true;

            if (records != null)
            {
                for (int i = 0; i < records.Length && i < itemUse.Length; i++)
                {
                    if (!records[i].lastTarget.IsSet) continue;

                    // Kept on failure, consumed on success: the referent may be a player who has not
                    // rejoined yet, and dropping the ref would mean the NPC never re-seeds its lead.
                    if (!records[i].lastTarget.TryResolve(out GameObject target))
                    {
                        resolvedEverything = false;
                        continue;
                    }

                    itemUse[i].RestoreAimTracking(target.transform, records[i].lastTargetPosition);
                }
            }

            AgentRangedCombatModule[] ranged = Ranged;
            RangedState[] rangedRecords = pending.ranged;

            if (rangedRecords != null)
            {
                for (int i = 0; i < rangedRecords.Length && i < ranged.Length; i++)
                {
                    if (!rangedRecords[i].firingAt.IsSet) continue;

                    if (!rangedRecords[i].firingAt.TryResolve(out GameObject victim))
                    {
                        resolvedEverything = false;
                        continue;
                    }

                    ranged[i].RestoreFiringAt(victim);
                }
            }

            if (resolvedEverything) hasPending = false;
        }

        private void ApplyCadences(in State state)
        {
            AgentRangedCombatModule[] ranged = Ranged;
            if (state.ranged != null)
                for (int i = 0; i < state.ranged.Length && i < ranged.Length; i++)
                {
                    RangedState r = state.ranged[i];
                    ranged[i].RestoreCadence(r.cooldown, r.burstRemaining, r.burstTimer, r.burstSpread,
                                             r.engaged, r.strafeTimer, r.hasStrafeDestination,
                                             r.strafeDestination);
                }

            CloseCombatModule[] melee = Melee;
            if (state.melee != null)
                for (int i = 0; i < state.melee.Length && i < melee.Length; i++)
                {
                    MeleeState m = state.melee[i];
                    melee[i].RestoreCadence(m.cooldown, m.commit, m.engaged);
                }

            NpcItemUseModule[] itemUse = ItemUse;
            if (state.itemUse != null)
                for (int i = 0; i < state.itemUse.Length && i < itemUse.Length; i++)
                {
                    ItemUseState u = state.itemUse[i];
                    itemUse[i].RestoreCadence(u.cooldown, u.burstRemaining, u.burstTimer,
                                              u.targetHeldFor, u.hasFacingTarget, u.facingPoint);

                    // Cleared here and re-seeded in OnLoadComplete if the record names a target that
                    // still exists. Without the clear, a module could keep a tracker from the live
                    // scene that the record says nothing about.
                    itemUse[i].RestoreAimTracking(null, Vector3.zero);
                }
        }

        private void ResetToDefaults()
        {
            foreach (AgentRangedCombatModule m in Ranged)
            {
                m.RestoreCadence(0f, 0, 0f, 0, false, 0f, false, Vector3.zero);
                m.RestoreFiringAt(null);
            }

            foreach (CloseCombatModule m in Melee)
                m.RestoreCadence(0f, 0f, false);

            foreach (NpcItemUseModule m in ItemUse)
            {
                m.RestoreCadence(0f, 0, 0f, 0f, false, Vector3.zero);
                m.RestoreAimTracking(null, Vector3.zero);
            }
        }
    }
}
