using Unity.Netcode;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// A supply unit the player carries in order to plug it into a machine: an oxygen bottle, a
    /// power cell.
    ///
    /// <para>
    /// <b>Its use verb is optional, and only a charged bottle has one.</b> Set
    /// <see cref="spentVariant"/> and using this item breathes it: the wearer's
    /// <see cref="SuitOxygen"/> is topped up and the item becomes its drained twin in the same
    /// hotbar slot. Left empty — which is what the power cell does — there is no verb at all,
    /// because there is nothing a cell does when you pull a trigger; its verb belongs to the
    /// machine you plug it into, which is right-clicked (<c>OxygenGeneratorDock</c>).
    /// </para>
    /// <para>
    /// The class would exist even with no verb at all, because <see cref="UsableItem.OnEquipped"/>
    /// is what gives an item its hold pose: an item with no <c>UsableItem</c> equips perfectly and
    /// then stands in the idle tree with a cylinder floating in a slack fist.
    /// </para>
    /// <para>
    /// <b>Its charge is its IDENTITY, not a field.</b> A drained bottle and a charged one are two
    /// <see cref="InventoryItem"/> assets, so the state travels on the wire, into the save file,
    /// into the hotbar, onto the pack mat and into the icon for free — the hotbar replicates item
    /// IDs, and <see cref="ItemState"/> does not replicate at all (see Inventory.md), so a charge
    /// held in a bag would be a number only the server could see. <see cref="charged"/> is
    /// therefore authored per prefab and never written at runtime.
    /// </para>
    /// </summary>
    public class DockableSupply : UsableItem
    {
        [Header("Charge")]
        [Tooltip("Is this the charged variant? Authored, never written at runtime — a drained " +
                 "bottle and a full one are two separate items.")]
        [SerializeField] private bool charged = true;

        [Tooltip("The emissive gauge or charge ladder. Optional.")]
        [SerializeField] private Renderer readout;

        [Tooltip("Which submesh of that renderer is the emissive one. -1 paints all of them.")]
        [SerializeField] private int readoutMaterialIndex = EmissiveLamp.WholeRenderer;

        [SerializeField] private Color chargedColour = new Color(0.35f, 1f, 0.45f);

        [Tooltip("What the gauge reads at empty. Not black: an unlit CRT is dark GLASS, and a " +
                 "black one reads as a hole in the object.")]
        [SerializeField] private Color emptyColour = new Color(0.06f, 0.09f, 0.07f);

        [Header("Spending it")]
        [Tooltip("What this becomes once breathed — the drained twin of this item. Leave empty " +
                 "for a supply that has no use verb at all, such as the power cell.")]
        [SerializeField] private InventoryItem spentVariant;

        /// <summary>Whether this variant is the charged one.</summary>
        public bool Charged => charged;

        /// <summary>The emissive part, so a dock can paint the copy of this item standing in it.</summary>
        public Renderer Readout => readout;

        /// <summary>Which submesh of <see cref="Readout"/> is emissive.</summary>
        public int ReadoutMaterialIndex => readoutMaterialIndex;

        /// <summary>Gauge colour at full.</summary>
        public Color ChargedColour => chargedColour;

        /// <summary>Gauge colour at empty.</summary>
        public Color EmptyColour => emptyColour;

        /// <summary>The gauge colour for a charge in 0..1. Shared so a dock's animation matches.</summary>
        public Color ColourAt(float charge01) =>
            Color.Lerp(emptyColour, chargedColour, Mathf.Clamp01(charge01));

        private void Awake() => PaintReadout(charged ? 1f : 0f);

        /// <summary>
        /// Show <paramref name="charge01"/> on the gauge. Called once on this instance, and per
        /// frame by a dock on the inert copy standing in it while a bottle fills.
        /// </summary>
        public void PaintReadout(float charge01) =>
            EmissiveLamp.Paint(readout, readoutMaterialIndex, ColourAt(charge01));

        /// <summary>
        /// Breathes the bottle: tops the wearer's suit up and swaps this item for its drained twin
        /// in the slot it was used from.
        ///
        /// <para>
        /// Server-side, which is <see cref="UseAuthority.Server"/>, the default — consuming a
        /// supply changes shared state, and the slot write goes through
        /// <see cref="IPlayerInventory.TrySetSlot"/>, which refuses off the server anyway.
        /// </para>
        /// <para>
        /// The SELECTED slot is the one swapped, not a search for a slot holding this item. The
        /// player pressed use on the thing in their hand, and a search would empty a different
        /// bottle in a different slot if they happened to carry two.
        /// </para>
        /// <para>
        /// <b>It never refuses.</b> Using a full bottle on an almost-full suit wastes most of it,
        /// and that is allowed: a refusal is more frustrating than a waste the player can see for
        /// themselves in a gauge that barely moved, and checking "is this worth it" on two machines
        /// makes the answer depend on which one replicated first.
        /// </para>
        /// </summary>
        protected override void Use()
        {
            if (!charged || spentVariant == null || owner == null) return;

            SuitOxygen suit = owner.GetComponentInChildren<SuitOxygen>();
            if (suit == null) return;

            var inventory = owner.GetComponent<IPlayerInventory>();
            if (inventory == null) return;

            int slot = inventory.SelectedSlotIndex;
            if (slot < 0) return;

            suit.Refill(suit.BottleRestores);
            inventory.TrySetSlot(slot, spentVariant);
        }

        /// <summary>
        /// Says what just happened, on the wearer's own screen only.
        ///
        /// <para>
        /// Present runs on every machine, so it has to ask whether this is the local player's
        /// bottle: without that check, watching somebody else drink theirs posts a message about
        /// YOUR suit onto your visor.
        /// </para>
        /// </summary>
        protected override void Present()
        {
            if (!charged || spentVariant == null || owner == null) return;

            var netObj = owner.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned && !netObj.IsOwner) return;

            SystemMessages.Post("suit.bottle", "OXYGEN REPLENISHED", MessageSeverity.Info);
        }

    #if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying) PaintReadout(charged ? 1f : 0f);
        }
    #endif
    }
}
