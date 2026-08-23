using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay.Trading;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists a trader's books: what is left of each offer, what a task has added to them since,
    /// and how long they have agreed to stop asking.
    ///
    /// <para>
    /// <b>Why this is harder than it looks.</b> <see cref="TraderProfile.CloneOffers"/> takes a
    /// fresh working copy in <c>Awake</c>, and its own docstring says why: stock is decremented as
    /// the player buys, and a shared asset that remembered those decrements would empty every
    /// scavenger in the world at once — and, in the editor, write the emptying back to disk. So the
    /// one mechanism that makes stock safe to spend is the same mechanism that makes it impossible
    /// to keep. Restoring therefore replaces the list rather than editing it, which is what
    /// <see cref="TraderInteraction.RestoreOffers"/> is for.
    /// </para>
    /// <para>
    /// <b>Offers are matched by what they are, not by where they sit.</b> An index into the list is
    /// what the trade protocol uses within a session, and it is exactly the wrong key across
    /// sessions: adding one offer to a profile asset would shift every later one, and a save would
    /// hand the water cell's remaining stock to whatever slid into its slot. The key is the swap
    /// itself — this item for that item, in these numbers.
    /// </para>
    /// <para>
    /// <b>An offer the profile has gained since the save is kept.</b> A record is a statement about
    /// the offers that existed when it was written and says nothing about one added afterwards, so
    /// anything live that the record does not account for survives with its authored stock. The
    /// reverse — an offer in the record that no longer exists on the profile — is only re-created
    /// when it was added at RUNTIME, because that one exists nowhere else.
    /// </para>
    /// <para>
    /// There is no money in this game to store: <see cref="TradeOffer"/> is pure barter, so stock
    /// and the decline cooldown are the whole of a trader's mutable state. The open/closed state of
    /// the trade panel is deliberately not stored — a UI window is not something a world remembers.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(TraderInteraction))]
    public class TraderSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "trader";       // written into save files — NEVER rename

        private TraderInteraction trader;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private TraderInteraction Trader =>
            trader != null ? trader : trader = GetComponent<TraderInteraction>();

        public string SaveKey => Key;

        public struct OfferState
        {
            public string wants;
            public int wantsCount;
            public string gives;
            public int givesCount;

            /// <summary>How they put it. Carried because a runtime offer's pitch exists nowhere else.</summary>
            public string pitch;

            /// <summary>Takes remaining. -1 is unlimited, matching <see cref="TradeOffer.stock"/>.</summary>
            public int stock;

            /// <summary>Added during play rather than authored — see <see cref="TradeOffer.runtimeAdded"/>.</summary>
            public bool runtime;
        }

        public struct State
        {
            public List<OfferState> offers;

            /// <summary>Seconds of post-decline silence still owed. See <see cref="TraderInteraction.OfferCooldownRemaining"/>.</summary>
            public float cooldown;
        }

        public object CaptureState()
        {
            if (Trader == null) return null;

            IReadOnlyList<TradeOffer> live = Trader.Offers;
            if (live == null || live.Count == 0) return null;

            var captured = new List<OfferState>(live.Count);

            foreach (TradeOffer offer in live)
            {
                if (offer == null || !offer.IsValid) continue;

                captured.Add(new OfferState
                {
                    wants = offer.wants.ID,
                    wantsCount = offer.wantsCount,
                    gives = offer.gives.ID,
                    givesCount = offer.givesCount,
                    pitch = offer.pitch,
                    stock = offer.stock,
                    runtime = offer.runtimeAdded,
                });
            }

            if (captured.Count == 0) return null;

            return new State { offers = captured, cooldown = Trader.OfferCooldownRemaining };
        }

        public void RestoreState(JObject state)
        {
            if (Trader == null) return;

            // No record means this trader was as the profile made them — which is what Awake has
            // just finished cloning, so there is nothing to undo. Deliberately NOT clearing the
            // list: that would leave a trader with nothing to sell for the rest of the session.
            if (state == null) return;

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            if (restored.offers == null) return;

            // The live list as the profile built it a moment ago, and a tally of what the record
            // accounts for. Anything left over is an offer the profile has gained since the save.
            var live = new List<TradeOffer>(Trader.Offers);
            var accounted = new Dictionary<string, int>(restored.offers.Count);

            var rebuilt = new List<TradeOffer>(restored.offers.Count);

            foreach (OfferState record in restored.offers)
            {
                string key = KeyOf(record.wants, record.wantsCount, record.gives, record.givesCount);
                accounted[key] = accounted.TryGetValue(key, out int seen) ? seen + 1 : 1;

                TradeOffer offer = Rebuild(record);
                if (offer != null) rebuilt.Add(offer);
            }

            foreach (TradeOffer offer in live)
            {
                if (offer == null || !offer.IsValid) continue;

                // A runtime offer that is still in the list is one this same session added, and the
                // record already speaks for it. Only authored offers can be new to the record.
                if (offer.runtimeAdded) continue;

                string key = KeyOf(offer.wants.ID, offer.wantsCount, offer.gives.ID, offer.givesCount);

                if (accounted.TryGetValue(key, out int remaining) && remaining > 0)
                {
                    accounted[key] = remaining - 1;
                    continue;
                }

                rebuilt.Add(offer);
            }

            Trader.RestoreOffers(rebuilt, restored.cooldown);
        }

        /// <summary>
        /// A saved offer as a live one, or null when the record names an item that no longer exists.
        ///
        /// A missing item is reported rather than silently dropped: an offer whose goods have been
        /// deleted from the project is a designer's problem, and a trader quietly losing half their
        /// stock is exactly the kind of thing nobody notices until a playtest.
        /// </summary>
        private TradeOffer Rebuild(OfferState record)
        {
            InventoryItem wants = Resolve(record.wants);
            InventoryItem gives = Resolve(record.gives);

            if (wants == null || gives == null)
            {
                Debug.LogWarning($"[Save] Trader '{name}' had an offer for '{record.wants}' → " +
                                 $"'{record.gives}', and one of those items is not in the registry. " +
                                 "The offer was dropped. Was the item asset deleted?", this);
                return null;
            }

            return new TradeOffer
            {
                wants = wants,
                wantsCount = Mathf.Max(1, record.wantsCount),
                gives = gives,
                givesCount = Mathf.Max(1, record.givesCount),
                pitch = record.pitch,
                stock = record.stock,
                runtimeAdded = record.runtime,
            };
        }

        private static InventoryItem Resolve(string id) =>
            string.IsNullOrEmpty(id) ? null : Registry<InventoryItem>.Get(id);

        /// <summary>What an offer IS, as a string: this many of one thing for that many of another.</summary>
        private static string KeyOf(string wants, int wantsCount, string gives, int givesCount) =>
            $"{wants}x{Mathf.Max(1, wantsCount)}>{gives}x{Mathf.Max(1, givesCount)}";
    }
}
