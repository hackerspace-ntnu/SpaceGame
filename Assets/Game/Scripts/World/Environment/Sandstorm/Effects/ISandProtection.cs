// What "the right equipment" means, as an interface.
//
// Anything under a character that can keep sand out implements this: a sealed helmet spawned into
// the hand, a cloak on the body, a suit upgrade on the root. SandstormVictim asks the hierarchy
// rather than the inventory, so gear protects you by being worn, not by being listed somewhere.
namespace SpaceGame.World.Weather
{
    public interface ISandProtection
    {
        /// <summary>
        /// How much of the storm this keeps out, 0 to 1. Protection is the best single source, not
        /// the sum — see <see cref="SandstormVictim"/> for why.
        /// </summary>
        float SandProtection { get; }
    }
}
