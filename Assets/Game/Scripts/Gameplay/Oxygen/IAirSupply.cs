namespace SpaceGame.Gameplay
{
    /// <summary>
    /// What keeps a <see cref="BreathableVolume"/> breathable: the lander's oxygen plant, in its
    /// mount and running. A volume with no supply is breathable for as long as it exists (a cave, a
    /// sealed habitat); a volume with one is breathable only while the supply says so.
    /// </summary>
    public interface IAirSupply
    {
        /// <summary>Is there air to breathe right now? Read on every machine, every frame.</summary>
        bool SuppliesAir { get; }
    }
}
