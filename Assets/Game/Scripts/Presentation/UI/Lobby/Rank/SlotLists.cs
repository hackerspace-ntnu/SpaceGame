using System.Collections.Generic;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// The rank keeps one entry per roster slot in several parallel lists, grown as the roster
    /// grows and never shrunk — a player who has left keeps their slot in case they rejoin.
    /// </summary>
    internal static class SlotLists
    {
        /// <summary>Pads a list with defaults up to and including <paramref name="index"/>.</summary>
        public static void Grow<T>(List<T> list, int index)
        {
            while (list.Count <= index) list.Add(default);
        }
    }
}
