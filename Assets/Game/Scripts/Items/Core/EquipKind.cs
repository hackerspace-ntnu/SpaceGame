namespace SpaceGame.Items
{
    /// <summary>
    /// Where on the body an item is equipped when it is equipped at all.
    ///
    /// <para>
    /// Authored on the <see cref="InventoryItem"/> asset rather than on the prefab, because the
    /// server decides whether an item may go into a slot and it must not have to instantiate a
    /// prefab to find out. Values are persisted in item assets — append only, never reorder.
    /// </para>
    /// </summary>
    public enum EquipKind
    {
        /// <summary>Held in the hand from a hotbar slot, fired on the Use button.</summary>
        Hand = 0,

        /// <summary>Worn on a forearm, fired on that arm's own key. A gauntlet in a hotbar slot is inert.</summary>
        Gauntlet = 1,

        /// <summary>Worn on the back — clipped to the expedition rig's lash rail — and deployed on a
        /// double tap of jump. Inert in a hotbar slot.</summary>
        Back = 2,

        /// <summary>
        /// Worn on the chest, and fired on the same double tap of jump. Inert in a hotbar slot.
        ///
        /// <para>
        /// The back and the chest are two places for <b>one</b> slot, not two slots: an item's kind
        /// decides which of them it seats on, so wearing a chest item is mutually exclusive with
        /// wearing a back one for free — there is only ever one <see cref="BodySlot.Torso"/> to put
        /// either in, and no rule anywhere has to enforce the exclusion.
        /// </para>
        /// </summary>
        Chest = 3,
    }
}
