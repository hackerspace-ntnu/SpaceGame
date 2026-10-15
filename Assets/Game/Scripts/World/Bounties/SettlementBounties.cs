// The hunts a settlement owns: put one on the Settlement, list its hunts and when each is offered. A hunt is taken only
// from its owner's posters and handed in only in its owner's bounds, never at whatever settlement is nearest (an
// Outpost is a Settlement too). Saving the scene writes the owner's id into each hunt (BountyHuntRecorder), so the
// board knows the owner while its chunk is not loaded.
//
// Every machine reads it the same way (poster prompts), the server enforces it (BountyBoard.Take). Nothing saved.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.World.Bounties
{
    /// <summary>When a settlement puts a hunt on offer.</summary>
    public enum OfferCondition
    {
        /// <summary>From the start.</summary>
        Always,

        /// <summary>Once another hunt has been claimed.</summary>
        AfterClaimed,

        /// <summary>Once the reader stands well enough with the town's people.</summary>
        Goodwill,
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Settlement))]
    public sealed class SettlementBounties : MonoBehaviour
    {
        [Serializable]
        public sealed class Entry
        {
            public BountyHunt hunt;

            public OfferCondition offeredWhen = OfferCondition.Always;

            [Tooltip("AfterClaimed only: the hunt that must be claimed first.")]
            public BountyHunt afterClaimed;

            [Tooltip("Goodwill only: the least standing with the town's faction the reader must have.")]
            public GoodwillBand minGoodwill = GoodwillBand.Friendly;
        }

        private static readonly List<SettlementBounties> Loaded = new();

        [Tooltip("Goodwill entries only: whose goodwill counts. The town's own people.")]
        [SerializeField] private FactionDefinition townFaction;

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        private Settlement settlement;

        public Settlement Settlement => settlement != null ? settlement : settlement = GetComponent<Settlement>();
        public IReadOnlyList<Entry> Entries => entries;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Loaded.Clear();

        /// <summary>The loaded settlement that owns <paramref name="hunt"/>, or null while its chunk is not loaded.</summary>
        public static SettlementBounties Owner(BountyHunt hunt)
        {
            if (hunt == null) return null;
            foreach (SettlementBounties owner in Loaded)
                if (owner.EntryFor(hunt) != null) return owner;
            return null;
        }

        /// <summary>
        /// Whether this settlement offers <paramref name="hunt"/> to <paramref name="reader"/> now: it is one of its hunts,
        /// and its condition is met. Whether the crew may take it (none open, never closed) is the board's question.
        /// </summary>
        public bool Offers(BountyHunt hunt, BountyBoard board, Transform reader)
        {
            Entry entry = EntryFor(hunt);
            if (entry == null || board == null) return false;

            switch (entry.offeredWhen)
            {
                case OfferCondition.Always:
                    return true;
                case OfferCondition.AfterClaimed:
                    return board.IsClosed(BountyCatalog.IndexOf(entry.afterClaimed), out BountyClosure closure) &&
                           closure == BountyClosure.Claimed;
                case OfferCondition.Goodwill:
                    return StandingOf(reader) >= entry.minGoodwill;
                default:
                    throw new ArgumentOutOfRangeException(nameof(entry.offeredWhen), entry.offeredWhen, null);
            }
        }

        private Entry EntryFor(BountyHunt hunt)
        {
            foreach (Entry entry in entries)
                if (entry != null && entry.hunt == hunt) return entry;
            return null;
        }

        private GoodwillBand StandingOf(Transform reader)
        {
            FactionGoodwillLedger ledger = FactionGoodwillLedger.Instance;
            if (ledger == null || townFaction == null || reader == null) return GoodwillBand.Wary;
            return ledger.BandForEntity(townFaction, reader.GetComponentInParent<EntityFaction>());
        }

        private void OnEnable()
        {
            if (!Loaded.Contains(this)) Loaded.Add(this);
        }

        private void OnDisable() => Loaded.Remove(this);
    }
}
