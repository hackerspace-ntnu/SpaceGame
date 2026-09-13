using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// A reservoir the player carries in order to plug it into something: an oxygen tank, a
    /// battery.
    ///
    /// <para>
    /// <b>It HAS a reservoir; it is not one.</b> The fill, its capacity, its kind and its gauge all
    /// live on the sibling <see cref="SupplyReservoir"/>, which is a plain component any item can
    /// hold. This class is the other half — the item's one <see cref="UsableItem"/> — and it was
    /// worth separating the moment a second kind of item wanted a tank: a sprayer needs a reservoir
    /// AND a trigger, and <c>EquipmentController.Equip</c> resolves a held item with a single
    /// <c>GetComponent&lt;UsableItem&gt;</c>, so two of them on one prefab is one of them chosen at
    /// random (<c>GDC-L1-ARCH-0002</c>).
    /// </para>
    /// <para>
    /// <b>It has no use verb, deliberately.</b> A tank does nothing in your hand and nothing
    /// anywhere on the pack except its socket; a battery does nothing until it is fitted to a
    /// machine. Both of their verbs belong to the receptacle they are put into, which is
    /// right-clicked (<c>OxygenGeneratorDock</c>, and the pack's own socket). Until 2026-09-04 a
    /// charged tank could also be breathed straight from the hand, which was a second path to the
    /// same outcome with a different and unexplainable waste rule — using a full tank on a suit
    /// that holds one minute threw away twenty-nine of them.
    /// </para>
    /// <para>
    /// The class would exist even with no verb at all, because <see cref="UsableItem.OnEquipped"/>
    /// is what gives an item its hold pose: an item with no <c>UsableItem</c> equips perfectly and
    /// then stands in the idle tree with a cylinder floating in a slack fist. It carries no state
    /// of its own either — <see cref="UsableItem.CaptureItemState"/> already writes the sibling
    /// reservoir's fill into the slot's bag for every item there is, so there is nothing here to
    /// override.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(SupplyReservoir))]
    public class DockableSupply : UsableItem
    {
        /// <summary>
        /// Nothing. The reservoir's verb belongs to the receptacle it is plugged into — see the
        /// class summary. <c>Use</c> is abstract on <see cref="UsableItem"/>, so this is the
        /// shape "no verb" takes rather than an omission.
        /// </summary>
        protected override void Use() { }
    }
}
