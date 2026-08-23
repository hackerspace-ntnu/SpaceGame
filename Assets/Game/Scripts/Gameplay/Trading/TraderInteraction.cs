// Makes a character a trader: they ask, you answer, a panel opens.
//
// Deliberately NOT an IInteractable. Interactor resolves exactly one IInteractable per collider via
// GetComponent, so a second one on a character that already has a DialogInteraction would make
// which of the two answers depend on component order — silently, per prefab, and differently after
// anyone reorders the inspector. Instead DialogInteraction asks this component whether it wants the
// conversation, which also means a trader gets the existing question popup, the existing Y/N keys
// and the existing typewriter for free.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Items;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay.Trading
{
    [DisallowMultipleComponent]
    public class TraderInteraction : MonoBehaviour
    {
        [Header("Stock")]
        [Tooltip("Shared stock list. Cloned at startup, so buying from one trader does not empty " +
                 "every other trader using the same asset.")]
        [SerializeField] private TraderProfile profile;

        [Tooltip("Extra offers specific to this character, appended to the profile's.")]
        [SerializeField] private List<TradeOffer> extraOffers = new();

        [Header("Manners")]
        [Tooltip("Seconds before this trader offers to trade again after being turned down. " +
                 "Without it, every attempt to have an ordinary conversation is intercepted.")]
        [SerializeField] private float declineCooldown = 45f;

        [Tooltip("Ask about trade before saying anything else. Off means the character talks " +
                 "normally and only offers once its dialog has been exhausted.")]
        [SerializeField] private bool offerBeforeDialog = true;

        [Header("Voice")]
        [SerializeField] private string greetingOverride = string.Empty;

        private readonly List<TradeOffer> offers = new();
        private float nextOfferTime;
        private bool sessionOpen;

        public IReadOnlyList<TradeOffer> Offers => offers;

        public string DisplayName => string.IsNullOrWhiteSpace(name) ? "Trader" : name;

        public string DeclineLine => profile != null ? profile.declineLine : "Suit yourself.";

        public bool HasStock
        {
            get
            {
                foreach (TradeOffer offer in offers)
                    if (offer != null && offer.IsValid && offer.InStock) return true;

                return false;
            }
        }

        private void Awake()
        {
            if (profile != null)
                offers.AddRange(profile.CloneOffers());

            foreach (TradeOffer offer in extraOffers)
                if (offer != null && offer.IsValid) offers.Add(offer);
        }

        private void OnEnable()  => this.NetOn(NetMsg.Trade, OnTradeRequested);
        private void OnDisable() => this.NetOff(NetMsg.Trade, OnTradeRequested);

        // ── Being talked to ──────────────────────────────────────────────────────

        /// <summary>
        /// Offer to trade, if now is the moment. Returns true when this component has taken over
        /// the conversation, so <see cref="DialogInteraction"/> knows not to also say a line.
        /// </summary>
        public bool TryOfferTrade(DialogInteraction dialog, Interactor interactor)
        {
            if (!offerBeforeDialog || dialog == null || interactor == null) return false;
            if (sessionOpen || TradeUI.IsOpen) return false;
            if (Time.time < nextOfferTime) return false;
            if (offers.Count == 0) return false;

            if (!HasStock)
            {
                // Out of stock is worth saying once, then falling silent for the cooldown — a
                // trader who keeps offering nothing is worse than one who has stopped offering.
                nextOfferTime = Time.time + declineCooldown;

                if (profile != null && !string.IsNullOrWhiteSpace(profile.soldOutLine) &&
                    NpcDialogPopupUI.Instance != null)
                {
                    NpcDialogPopupUI.Instance.Show(profile.soldOutLine, 2.5f);
                    return true;
                }

                return false;
            }

            string greeting = !string.IsNullOrWhiteSpace(greetingOverride)
                ? greetingOverride
                : profile != null ? profile.greeting : "Care to trade?";

            string accept = profile != null ? profile.acceptLabel : "Trade";
            string decline = profile != null ? profile.declineLabel : "Not now";

            return dialog.AskQuestion(greeting, accept, decline,
                onYes: () => OpenPanel(interactor),
                onNo: () =>
                {
                    nextOfferTime = Time.time + declineCooldown;

                    if (NpcDialogPopupUI.Instance != null && !string.IsNullOrWhiteSpace(DeclineLine))
                        NpcDialogPopupUI.Instance.Show(DeclineLine, 2f);
                });
        }

        private void OpenPanel(Interactor interactor)
        {
            if (NpcDialogPopupUI.Instance != null)
                NpcDialogPopupUI.Instance.Hide();

            sessionOpen = true;
            TradeUI.Open(this, interactor, () => sessionOpen = false);
        }

        // ── Executing a trade ────────────────────────────────────────────────────

        /// <summary>
        /// Does the player hold everything this offer asks for?
        ///
        /// Counted in slots, because <see cref="Inventory"/> has no stacking: three of an item is
        /// three occupied slots, and there is no quantity anywhere to read instead.
        /// </summary>
        public bool CanAfford(int offerIndex, IPlayerInventory inventory)
        {
            if (inventory == null || !TryGetOffer(offerIndex, out TradeOffer offer)) return false;
            if (!offer.InStock) return false;

            int held = CountHeld(inventory, offer.wants);
            if (held < offer.wantsCount) return false;

            // Room for what comes back. The slots being freed by the payment count toward it, which
            // is what lets a full inventory still make an even swap.
            int freeAfterPayment = CountFree(inventory) + offer.wantsCount;
            return freeAfterPayment >= offer.givesCount;
        }

        /// <summary>
        /// Take the offer.
        ///
        /// <para>
        /// The player's half runs locally and replicates on its own — <see cref="IPlayerInventory"/>
        /// is already server-authoritative, which is the same route a picked-up item takes.
        /// </para>
        /// <para>
        /// The trader's half — its stock, and what goes into its own bag — is sent to the server,
        /// because two players in one session must not both be able to take the last of something.
        /// Offline and on the host, <see cref="NetMessaging"/> collapses that to a direct call.
        /// </para>
        /// </summary>
        public bool TryExecute(int offerIndex, IPlayerInventory inventory, GameObject buyer)
        {
            if (!CanAfford(offerIndex, inventory)) return false;
            if (!TryGetOffer(offerIndex, out TradeOffer offer)) return false;

            // Payment first. If adding the goods failed after taking payment the player would be
            // robbed, so the order is: verify (CanAfford, above), take, give — and CanAfford has
            // already established there is room for the goods.
            for (int taken = 0; taken < offer.wantsCount; taken++)
            {
                int slot = FindHeld(inventory, offer.wants);
                if (slot < 0) return false;

                inventory.TryRemoveItem(slot);
            }

            for (int given = 0; given < offer.givesCount; given++)
                inventory.TryAddItem(offer.gives);

            // Settle the trader's books directly when this machine is the one that owns them — which
            // is every single-player session and the host. Only a remote client has to ask.
            //
            // Deliberately NOT routed through NetMessaging in both cases. Sending to the server from
            // the server does dispatch locally, but only if the entity has a NetChannel to dispatch
            // through — and a trader that has never registered one silently drops the message,
            // leaving stock that never runs out and payment that never arrives. Making the local
            // path a plain method call means the common case cannot fail quietly.
            if (Network.Simulates(this))
            {
                SettleTraderSide(offerIndex);
            }
            else
            {
                var arg = new NetArg { A = offerIndex };
                this.NetToServer(NetMsg.Trade, arg.With(buyer));
            }

            return true;
        }

        /// <summary>Server side: a remote client took an offer.</summary>
        private void OnTradeRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;

            SettleTraderSide(arg.A);
        }

        /// <summary>
        /// The trader's own books: stock down, payment into its bag.
        ///
        /// Stock is re-checked here rather than trusted from whoever asked. That is the whole reason
        /// a client's trade takes a round trip: their copy of the count can be a round trip out of
        /// date, and two players can be looking at the same last water cell.
        /// </summary>
        private void SettleTraderSide(int offerIndex)
        {
            if (!TryGetOffer(offerIndex, out TradeOffer offer) || !offer.InStock) return;

            if (offer.stock > 0) offer.stock--;

            if (TryGetComponent(out EntityInventoryComponent bag))
            {
                for (int i = 0; i < offer.wantsCount; i++)
                    bag.TryAddItem(offer.wants);
            }
        }

        public bool TryGetOffer(int index, out TradeOffer offer)
        {
            if (index >= 0 && index < offers.Count && offers[index] != null && offers[index].IsValid)
            {
                offer = offers[index];
                return true;
            }

            offer = null;
            return false;
        }

        /// <summary>Add an offer at runtime — a trader restocking from what a task yielded.</summary>
        public void AddOffer(TradeOffer offer)
        {
            if (offer == null || !offer.IsValid) return;

            // Marked as the save system's only way of telling a runtime offer from an authored one.
            // An authored offer comes back from the profile next session on its own; this one exists
            // nowhere but in this list and in the record.
            offer.runtimeAdded = true;
            offers.Add(offer);
        }

        /// <summary>
        /// Seconds left on the post-decline silence, or zero if the trader will offer right now.
        ///
        /// A duration rather than the deadline itself, because <c>Time.time</c> restarts with the
        /// session and a stored deadline would either have already passed or sit 45 seconds into a
        /// clock that has just been reset.
        /// </summary>
        public float OfferCooldownRemaining => Mathf.Max(0f, nextOfferTime - Time.time);

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// <para>
        /// Replaces the working copy of the stock outright. That is deliberate and is the whole
        /// difficulty of persisting a trader: <see cref="TraderProfile.CloneOffers"/> exists so that
        /// buying from one scavenger does not empty every scavenger sharing the asset, which means
        /// the live list is rebuilt from the asset in Awake on every single session — anything a
        /// player did to it is gone before this component has finished waking up. So a restore
        /// cannot nudge the list; it has to hand back the list.
        /// </para>
        /// <para>
        /// Whoever calls this owns the merge — see <c>TraderSaveable</c>, which keeps offers the
        /// profile has gained since the save was written.
        /// </para>
        /// </summary>
        public void RestoreOffers(List<TradeOffer> restored, float cooldownRemaining)
        {
            offers.Clear();

            if (restored != null)
            {
                foreach (TradeOffer offer in restored)
                    if (offer != null && offer.IsValid) offers.Add(offer);
            }

            nextOfferTime = Time.time + Mathf.Max(0f, cooldownRemaining);
        }

        // ── Inventory helpers ────────────────────────────────────────────────────

        public static int CountHeld(IPlayerInventory inventory, InventoryItem item)
        {
            if (inventory == null || item == null) return 0;

            int count = 0;
            for (int i = 0; i < inventory.GetInventorySize(); i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                if (slot != null && !slot.IsEmpty && slot.Item == item) count++;
            }

            return count;
        }

        private static int FindHeld(IPlayerInventory inventory, InventoryItem item)
        {
            for (int i = 0; i < inventory.GetInventorySize(); i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                if (slot != null && !slot.IsEmpty && slot.Item == item) return i;
            }

            return -1;
        }

        private static int CountFree(IPlayerInventory inventory)
        {
            int count = 0;
            for (int i = 0; i < inventory.GetInventorySize(); i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                if (slot == null || slot.IsEmpty) count++;
            }

            return count;
        }

        private void OnValidate()
        {
            declineCooldown = Mathf.Max(0f, declineCooldown);
        }
    }
}
