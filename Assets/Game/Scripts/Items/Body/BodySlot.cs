using System;

namespace SpaceGame.Items
{
    /// <summary>
    /// The three worn slots. Values are persisted (the body saver's list is positional by this)
    /// and travel on the wire — append only, never renumber. The NAMES are free to change: both the
    /// save and the wire carry the index, which is why <see cref="Torso"/> could be renamed from
    /// "Back" without touching a save file.
    /// </summary>
    public enum BodySlot
    {
        /// <summary>
        /// The one slot on the trunk, with two places to sit: <see cref="EquipKind.Back"/> gear
        /// clips to the expedition rig's lash rail behind you, <see cref="EquipKind.Chest"/> gear
        /// to the chest. Which one an item takes is read off the item, never chosen here — so
        /// "you can have one or the other" needs no rule, only this being a single slot.
        /// </summary>
        Torso = 0,

        LeftGauntlet = 1,
        RightGauntlet = 2,
    }

    /// <summary>Which of the two slot lists a <see cref="GearRef"/> names.</summary>
    public enum GearArea
    {
        Hotbar = 0,
        Body = 1,
    }

    /// <summary>
    /// One slot, in either list. The hotbar and the body are two server-owned lists with two
    /// different owners, and a move can cross between them — so the move request, the use
    /// message and the gear screen all need a single way to name a slot that does not care which
    /// list it is in.
    /// </summary>
    public readonly struct GearRef : IEquatable<GearRef>
    {
        public const int BodySlotCount = 3;

        public readonly GearArea Area;
        public readonly int Index;

        public GearRef(GearArea area, int index)
        {
            Area = area;
            Index = index;
        }

        public static GearRef Hotbar(int index) => new(GearArea.Hotbar, index);
        public static GearRef Body(BodySlot slot) => new(GearArea.Body, (int)slot);

        /// <summary>No slot at all — what an unset or malformed code decodes to.</summary>
        public static GearRef None => new(GearArea.Hotbar, -1);

        public bool IsNone => Index < 0;
        public bool IsBody => Area == GearArea.Body && Index >= 0;
        public bool IsHotbar => Area == GearArea.Hotbar && Index >= 0;

        /// <summary>The body slot this names. Only meaningful when <see cref="IsBody"/>.</summary>
        public BodySlot Slot => (BodySlot)Index;

        public bool Equals(GearRef other) => Area == other.Area && Index == other.Index;
        public override bool Equals(object obj) => obj is GearRef other && Equals(other);
        public override int GetHashCode() => ((int)Area << 16) ^ Index;
        public static bool operator ==(GearRef a, GearRef b) => a.Equals(b);
        public static bool operator !=(GearRef a, GearRef b) => !a.Equals(b);

        public override string ToString() => IsNone ? "none" : IsBody ? Slot.ToString() : $"hotbar {Index + 1}";
    }
}
