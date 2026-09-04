// Trading moves items between two bags, so the failures worth pinning are the ones that lose
// something: paying for goods there is no room for, buying stock that has run out, and a full
// inventory refusing an even swap it can obviously make.
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay.Trading;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class TradingTests
    {
        private readonly List<GameObject> spawned = new();
        private readonly List<ScriptableObject> assets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);

            foreach (ScriptableObject asset in assets)
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);

            spawned.Clear();
            assets.Clear();
        }

        private InventoryItem Item(string itemName)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.itemName = itemName;
            assets.Add(item);
            return item;
        }

        /// <summary>
        /// Run a component's private Awake by hand.
        ///
        /// Edit-mode tests do not get one: Unity calls Awake for AddComponent only in play mode, so
        /// anything that builds its state there — <see cref="EntityInventoryComponent"/> constructs
        /// its whole Inventory in Awake — is still half-initialised when the test touches it, and
        /// fails with a bare NullReferenceException that says nothing about why.
        /// </summary>
        private static void Awaken(Component component)
        {
            component.GetType()
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(component, null);
        }

        private TraderInteraction Trader(params TradeOffer[] offers)
        {
            var go = new GameObject("Trader");
            spawned.Add(go);

            var trader = go.AddComponent<TraderInteraction>();
            foreach (TradeOffer offer in offers) trader.AddOffer(offer);

            return trader;
        }

        /// <summary>
        /// A slot-based inventory with no stacking and no networking, which is exactly what the real
        /// one is once <c>PlayerInventoryNetwork</c> is out of the picture.
        /// </summary>
        private class FakeInventory : IPlayerInventory
        {
            private readonly InventorySlot[] slots;

            public FakeInventory(int size)
            {
                slots = new InventorySlot[size];
                for (int i = 0; i < size; i++) slots[i] = new InventorySlot(i);
            }

            public int SelectedSlotIndex { get; private set; } = -1;

            public event Action<InventorySlot> OnSlotSelected;
            public event Action<int, InventorySlot> OnSlotChanged;
            public event Action<InventoryItem> OnItemDropped;

            public bool TryAddItem(InventoryItem item)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (!slots[i].IsEmpty) continue;

                    slots[i].Item = item;
                    OnSlotChanged?.Invoke(i, slots[i]);
                    return true;
                }

                return false;
            }

            public bool TryRemoveItem(int index)
            {
                if (index < 0 || index >= slots.Length || slots[index].IsEmpty) return false;

                slots[index].Item = null;
                OnSlotChanged?.Invoke(index, slots[index]);
                return true;
            }

            public void SelectSlot(int slotIndex)
            {
                SelectedSlotIndex = slotIndex;
                OnSlotSelected?.Invoke(GetSelectedSlot());
            }

            public bool TrySetSlot(int index, InventoryItem item)
            {
                if (index < 0 || index >= slots.Length) return false;

                slots[index].Item = item;
                OnSlotChanged?.Invoke(index, slots[index]);
                return true;
            }

            public void RestoreSlots(IReadOnlyList<InventoryItem> items, int selectedSlot)
            {
                for (int i = 0; i < slots.Length; i++)
                    slots[i].Item = items != null && i < items.Count ? items[i] : null;

                SelectedSlotIndex = selectedSlot;
            }

            public int GetInventorySize() => slots.Length;
            public InventorySlot GetSlot(int index) => index >= 0 && index < slots.Length ? slots[index] : null;
            public InventorySlot GetSelectedSlot() => GetSlot(SelectedSlotIndex);
            public InventoryItem GetSelectedItem() => GetSelectedSlot()?.Item;

            public void Drop(InventoryItem item) => OnItemDropped?.Invoke(item);
        }

        [Test]
        public void AnOfferIsUnaffordableWithoutTheGoods()
        {
            InventoryItem scrap = Item("Scrap Plate");
            InventoryItem water = Item("Water Cell");

            TraderInteraction trader = Trader(new TradeOffer
            {
                wants = scrap, wantsCount = 2, gives = water, givesCount = 1,
            });

            var bag = new FakeInventory(6);
            bag.TryAddItem(scrap);

            Assert.IsFalse(trader.CanAfford(0, bag), "one plate does not pay for a two-plate offer");

            bag.TryAddItem(scrap);
            Assert.IsTrue(trader.CanAfford(0, bag), "two does");
        }

        [Test]
        public void TakingAnOfferSwapsTheItems()
        {
            InventoryItem scrap = Item("Scrap Plate");
            InventoryItem water = Item("Water Cell");

            TraderInteraction trader = Trader(new TradeOffer
            {
                wants = scrap, wantsCount = 2, gives = water, givesCount = 1,
            });

            var bag = new FakeInventory(6);
            bag.TryAddItem(scrap);
            bag.TryAddItem(scrap);

            Assert.IsTrue(trader.TryExecute(0, bag, null));

            Assert.AreEqual(0, TraderInteraction.CountHeld(bag, scrap), "payment should be gone");
            Assert.AreEqual(1, TraderInteraction.CountHeld(bag, water), "goods should have arrived");
        }

        [Test]
        public void AFullBagCanStillMakeAnEvenSwap()
        {
            // The slots being paid with are freed before the goods land, so "no free slot" is not
            // the right question. Refusing here would mean a full inventory can never trade at all,
            // which is precisely when a player most wants to.
            InventoryItem scrap = Item("Scrap Plate");
            InventoryItem water = Item("Water Cell");

            TraderInteraction trader = Trader(new TradeOffer
            {
                wants = scrap, wantsCount = 1, gives = water, givesCount = 1,
            });

            var bag = new FakeInventory(2);
            bag.TryAddItem(scrap);
            bag.TryAddItem(Item("Rope"));

            Assert.IsTrue(trader.CanAfford(0, bag), "a one-for-one swap needs no spare slot");
            Assert.IsTrue(trader.TryExecute(0, bag, null));
            Assert.AreEqual(1, TraderInteraction.CountHeld(bag, water));
        }

        [Test]
        public void AnUnevenSwapWithNoRoomIsRefusedBeforePaymentIsTaken()
        {
            // The order that matters: verify, then take, then give. Getting it wrong robs the player.
            InventoryItem scrap = Item("Scrap Plate");
            InventoryItem water = Item("Water Cell");

            TraderInteraction trader = Trader(new TradeOffer
            {
                wants = scrap, wantsCount = 1, gives = water, givesCount = 3,
            });

            var bag = new FakeInventory(2);
            bag.TryAddItem(scrap);
            bag.TryAddItem(Item("Rope"));

            Assert.IsFalse(trader.CanAfford(0, bag), "no room for three cells in a two-slot bag");
            Assert.IsFalse(trader.TryExecute(0, bag, null));
            Assert.AreEqual(1, TraderInteraction.CountHeld(bag, scrap), "payment must not have been taken");
        }

        [Test]
        public void StockRunsOut()
        {
            InventoryItem scrap = Item("Scrap Plate");
            InventoryItem water = Item("Water Cell");

            var offer = new TradeOffer
            {
                wants = scrap, wantsCount = 1, gives = water, givesCount = 1, stock = 1,
            };

            TraderInteraction trader = Trader(offer);

            var bag = new FakeInventory(6);
            bag.TryAddItem(scrap);
            bag.TryAddItem(scrap);

            Assert.IsTrue(trader.TryExecute(0, bag, null));

            // Offline this machine owns the trader, so its books are settled inline — no message,
            // no channel, nothing that can silently drop. An earlier version sent to the server even
            // when it WAS the server, and a trader with no NetChannel swallowed it: stock never ran
            // out and the payment never reached the trader's bag.
            Assert.AreEqual(0, offer.stock);
            Assert.IsFalse(offer.InStock);
            Assert.IsFalse(trader.CanAfford(0, bag), "a sold-out offer is not affordable at any price");
        }

        [Test]
        public void PaymentEndsUpInTheTradersOwnBag()
        {
            // The other half of settling the books, and the half a player actually sees: sell a
            // scavenger your scrap and they should be carrying it — which is what lets them offer
            // it on to somebody else, and what makes killing a trader worth something.
            InventoryItem scrap = Item("Scrap Plate");
            InventoryItem water = Item("Water Cell");

            TraderInteraction trader = Trader(new TradeOffer
            {
                wants = scrap, wantsCount = 1, gives = water, givesCount = 1,
            });

            EntityInventoryComponent bag = trader.gameObject.AddComponent<EntityInventoryComponent>();
            Awaken(bag);

            var player = new FakeInventory(6);
            player.TryAddItem(scrap);

            Assert.IsTrue(trader.TryExecute(0, player, null));

            int held = 0;
            foreach (InventoryItem item in bag.GetAllItems())
                if (item == scrap) held++;

            Assert.AreEqual(1, held, "the trader should be carrying what it was paid");
        }

        [Test]
        public void UnlimitedStockNeverDepletes()
        {
            InventoryItem scrap = Item("Scrap Plate");
            InventoryItem water = Item("Water Cell");

            var offer = new TradeOffer
            {
                wants = scrap, wantsCount = 1, gives = water, givesCount = 1, stock = -1,
            };

            TraderInteraction trader = Trader(offer);

            var bag = new FakeInventory(8);
            bag.TryAddItem(scrap);
            bag.TryAddItem(scrap);

            Assert.IsTrue(trader.TryExecute(0, bag, null));
            Assert.IsTrue(trader.TryExecute(0, bag, null));
            Assert.AreEqual(-1, offer.stock, "-1 means unlimited and must not count down");
        }

        [Test]
        public void ProfileStockIsClonedSoOneTraderDoesNotEmptyEveryOtherOne()
        {
            // A ScriptableObject is shared, and in the editor a mutated one is written back to disk.
            // Without the clone, buying the last water cell from one scavenger would empty every
            // scavenger in the world using that profile — permanently.
            var profile = ScriptableObject.CreateInstance<TraderProfile>();
            assets.Add(profile);

            profile.offers.Add(new TradeOffer
            {
                wants = Item("Scrap Plate"), gives = Item("Water Cell"), stock = 3,
            });

            List<TradeOffer> copy = profile.CloneOffers();
            copy[0].stock = 0;

            Assert.AreEqual(3, profile.offers[0].stock, "the asset must be untouched");
        }

        [Test]
        public void IncompleteOffersAreIgnored()
        {
            TraderInteraction trader = Trader(new TradeOffer { wants = Item("Scrap"), gives = null });

            Assert.AreEqual(0, trader.Offers.Count, "an offer missing half of the swap is not an offer");
            Assert.IsFalse(trader.HasStock);
        }
    }
}
