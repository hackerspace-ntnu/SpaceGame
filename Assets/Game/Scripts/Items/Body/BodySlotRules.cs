namespace SpaceGame.Items
{
    /// <summary>
    /// What fits where. The one answer both the server (deciding a move) and the gear screen
    /// (predicting the server's answer for a hover colour) read, so they cannot disagree.
    /// </summary>
    public static class BodySlotRules
    {
        /// <summary>May an item of this kind be worn in this slot?</summary>
        public static bool Accepts(BodySlot slot, EquipKind kind)
        {
            switch (slot)
            {
                // One slot, both trunk kinds. The exclusion the design asks for — a back item or a
                // chest item, never both — is this line and nothing else: there is one place to put
                // either, so putting one in displaces the other through the ordinary swap every
                // slot already does.
                case BodySlot.Torso:         return kind is EquipKind.Back or EquipKind.Chest;
                case BodySlot.LeftGauntlet:
                case BodySlot.RightGauntlet: return kind == EquipKind.Gauntlet;
                default:                     return false;
            }
        }

        /// <summary>
        /// May an item of this kind be put in this slot? A hotbar slot stores anything — a gauntlet
        /// lying in the hotbar is simply inert — so only body slots discriminate.
        /// </summary>
        public static bool Accepts(GearRef target, EquipKind kind)
        {
            if (target.IsNone) return false;
            return !target.IsBody || Accepts(target.Slot, kind);
        }

        /// <summary>Does selecting this item on the hotbar put it in the hand?</summary>
        public static bool HandEquips(EquipKind kind) => kind == EquipKind.Hand;

        /// <summary>The first body slot that takes this kind, or null for a hand item.</summary>
        public static BodySlot? FirstSlotFor(EquipKind kind)
        {
            for (int i = 0; i < GearRef.BodySlotCount; i++)
            {
                var slot = (BodySlot)i;
                if (Accepts(slot, kind)) return slot;
            }

            return null;
        }
    }
}
