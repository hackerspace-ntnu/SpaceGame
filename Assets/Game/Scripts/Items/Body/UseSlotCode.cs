namespace SpaceGame.Items
{
    /// <summary>
    /// How a <see cref="GearRef"/> rides in <c>NetArg.A</c> on a use message.
    ///
    /// <para>
    /// <c>A</c> has always carried the hotbar slot, and the server reads it as its stale-slot
    /// guard. Packing the area into bits above the index keeps every hotbar code exactly what it
    /// was — 0, 1, 2 — so nothing that reads <c>A</c> today changes meaning, and a body slot is a
    /// number no hotbar could ever produce.
    /// </para>
    /// </summary>
    public static class UseSlotCode
    {
        private const int AreaShift = 8;
        private const int IndexMask = (1 << AreaShift) - 1;

        public static int Encode(GearRef slot)
        {
            if (slot.IsNone) return -1;
            return ((int)slot.Area << AreaShift) | (slot.Index & IndexMask);
        }

        public static GearRef Decode(int code)
        {
            if (code < 0) return GearRef.None;
            return new GearRef((GearArea)(code >> AreaShift), code & IndexMask);
        }

        /// <summary>Which list a code names, without decoding the rest.</summary>
        public static GearArea AreaOf(int code) => code < 0 ? GearArea.Hotbar : (GearArea)(code >> AreaShift);
    }
}
