using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// What a burnt-out unit's fire does, as arithmetic: when it catches, how it grows, how it is put
    /// out. Pure, so every rule can be asserted without a ship, a network or a scene.
    /// </summary>
    public static class ShipPartFireRules
    {
        /// <summary>
        /// May the burnt-out unit be taken off its cradle? Only once it has burned and been put out:
        /// before that it is jammed in its socket, and while it burns it is too hot to touch.
        /// </summary>
        public static bool MayRemove(ShipPartFireState state) => state.phase == ShipPartFirePhase.Out;

        /// <summary>
        /// Advance the fire by <paramref name="deltaTime"/> seconds.
        ///
        /// <para>
        /// The ignition clock runs only while the fire is <paramref name="armed"/>: the crew are down
        /// and the ship's oxygen plant is back in its mount and running — the plant coming back on
        /// line is what sets the burnt-out unit off. The unit cannot leave its cradle until it has
        /// burned and been put out, so every world gets this fire exactly once. It is one clock for
        /// the whole world: a save keeps it, so quitting a minute in does not buy another delay.
        /// </para>
        /// </summary>
        public static ShipPartFireState Step(ShipPartFireState state, float deltaTime, bool armed,
                                             ShipPartFireTuning tuning)
        {
            if (deltaTime <= 0f || tuning == null) return state;

            switch (state.phase)
            {
                case ShipPartFirePhase.Dormant:
                    if (!armed) return state;

                    state.armedSeconds += deltaTime;
                    if (state.armedSeconds < tuning.igniteDelay) return state;

                    state.phase = ShipPartFirePhase.Burning;
                    state.strength = tuning.startStrength;
                    return state;

                case ShipPartFirePhase.Burning:
                    state.strength = Mathf.Min(1f, state.strength + deltaTime / tuning.growSeconds);
                    return state;

                default:
                    return state;
            }
        }

        /// <summary>
        /// Knock <paramref name="amount"/> off a burning fire's strength. At zero it is out, for good.
        /// A fire that is not burning is left exactly as it is.
        /// </summary>
        public static ShipPartFireState Douse(ShipPartFireState state, float amount)
        {
            if (!state.IsBurning || amount <= 0f) return state;

            state.strength -= amount;
            if (state.strength > 0f) return state;

            state.strength = 0f;
            state.phase = ShipPartFirePhase.Out;
            return state;
        }
    }
}
