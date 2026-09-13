namespace SpaceGame.Items
{
    /// <summary>The server's answer to "may this move happen", and what kind of move it is.</summary>
    public readonly struct MoveResult
    {
        public readonly bool Allowed;

        /// <summary>The target was occupied, so the two items change places.</summary>
        public readonly bool IsSwap;

        /// <summary>Why not, for the console. Empty when allowed.</summary>
        public readonly string Reason;

        private MoveResult(bool allowed, bool isSwap, string reason)
        {
            Allowed = allowed;
            IsSwap = isSwap;
            Reason = reason;
        }

        public static MoveResult Move() => new(true, false, string.Empty);
        public static MoveResult Swap() => new(true, true, string.Empty);
        public static MoveResult Refuse(string reason) => new(false, false, reason);
    }

    /// <summary>
    /// Decides a move between any two gear slots, hotbar or body, from nothing but the two slots
    /// and what is in them. Pure, so the gear screen can run the same decision for its hover colour
    /// and an EditMode test can pin every branch.
    /// </summary>
    public static class GearMoves
    {
        /// <param name="fromKind">Kind of the item being moved, or null when the source is empty.</param>
        /// <param name="toKind">Kind of the item already in the target, or null when it is empty.</param>
        /// <param name="mounted">Is the player riding something? Nothing moves then — a deployed wing
        /// pack must not be pulled out from under its craft, and the mount owns Q/E anyway.</param>
        public static MoveResult Resolve(GearRef from, EquipKind? fromKind, GearRef to, EquipKind? toKind, bool mounted)
        {
            if (from.IsNone || to.IsNone) return MoveResult.Refuse("no such slot");
            if (from == to) return MoveResult.Refuse("same slot");
            if (fromKind == null) return MoveResult.Refuse("nothing to move");
            if (mounted) return MoveResult.Refuse("not while riding");

            if (!BodySlotRules.Accepts(to, fromKind.Value))
                return MoveResult.Refuse($"{fromKind.Value} does not fit {to}");

            if (toKind == null) return MoveResult.Move();

            if (!BodySlotRules.Accepts(from, toKind.Value))
                return MoveResult.Refuse($"{toKind.Value} does not fit {from}");

            return MoveResult.Swap();
        }
    }
}
