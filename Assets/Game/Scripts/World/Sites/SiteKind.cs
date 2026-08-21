namespace SpaceGame.World
{
    /// <summary>
    /// What a place in the world is FOR, from an NPC's point of view.
    ///
    /// Deliberately a small closed enum rather than a string tag: an NPC task names the kind of
    /// place it needs, and a typo in a string would silently mean "this NPC never finds anywhere to
    /// go" — which reads in play as an NPC that simply stands still, with nothing anywhere to say
    /// why.
    /// </summary>
    public enum SiteKind
    {
        /// <summary>Where a group sleeps and returns to. The origin most task searches measure from.</summary>
        Home,

        /// <summary>A waypoint camp — somewhere to stop on a long journey, not somewhere to live.</summary>
        Camp,

        /// <summary>Something old and worth picking over. What a ruin-seeker travels to.</summary>
        Ruin,

        /// <summary>Salvage. What a scavenger travels to.</summary>
        ScrapField,

        /// <summary>Water. The reason a caravan detours.</summary>
        WaterHole,

        /// <summary>Somewhere trade happens. What a travelling salesman strings together into a route.</summary>
        TradePost,

        /// <summary>Where animals gather. A tracker travels here; the chase itself is ordinary targeting.</summary>
        AnimalGround,

        /// <summary>Nothing in particular — a navigation reference, and a fallback destination.</summary>
        Landmark,
    }
}
