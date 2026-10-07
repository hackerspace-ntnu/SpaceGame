namespace SpaceGame.Items
{
    /// <summary>
    /// Which holder is generated over an item when it is placed. Chosen from the item's measured
    /// proportions, never authored per socket — with free placement there are no sockets to
    /// author, and the holder has to be right wherever the player drops the thing.
    /// </summary>
    public enum HolderKind
    {
        /// Small enough to hang. Longest axis under 0.12 m.
        Clip,
        /// Tall and round — canisters, bottles, tanks. Shock-cord rings.
        Cord,
        /// Long and thin — staff, rifle, rod. Webbing straps and buckles.
        Webbing,
        /// Long with the mass at one end — hand tools. An open sleeve.
        Sleeve,
        /// No dominant axis. A bungee X pulled over it.
        Bungee
    }
}
