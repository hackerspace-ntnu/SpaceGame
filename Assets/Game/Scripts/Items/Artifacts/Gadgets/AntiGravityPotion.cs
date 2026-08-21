using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Drink it and float for five seconds.
    ///
    /// The networking is entirely <see cref="EffectItem"/>'s: the server counts the use and takes
    /// the empty bottle out of the hotbar, the drinker's own machine turns their gravity off, and
    /// the other three watch them rise through the transform sync that is already running.
    /// </summary>
    public class AntiGravityPotion : EffectItem
    {
        private const float Duration = 5f;    // seconds
        private const float FloatForce = 1f;  // upward acceleration, m/s²

        /// <summary>
        /// What this effect is called in a save file. Permanent — see <see cref="Effect.SaveToken"/>.
        /// </summary>
        public const string EffectToken = "antigravity";

        /// <summary>
        /// Teach <see cref="EffectCatalog"/> how to make another one of these.
        ///
        /// <para>
        /// Here rather than in a table inside the catalog, so that adding an effect item never means
        /// remembering to edit a second file. It runs once per session, before any scene loads, and
        /// costs a dictionary insert.
        /// </para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterWithCatalog() => EffectCatalog.Register(EffectToken, BuildEffect);

        /// <summary>
        /// The float, as a standalone effect.
        ///
        /// <para>
        /// <b>Static, and that is the point.</b> A restored effect has to be built when the potion
        /// that produced it no longer exists — it is single-use, so EquipmentController destroys it
        /// the instant the server counts the use, four and a half seconds before the effect ends.
        /// An instance method could not be called at all by then, and a field read through a
        /// destroyed MonoBehaviour is a trap left for whoever touches a Unity API next to it.
        /// </para>
        /// </summary>
        public static Effect BuildEffect()
        {
            // Captured by BOTH closures below, rather than kept in a field.
            //
            // A shared local is the only version that is still correct once two potions overlap,
            // because each call to this method gets its own — and it is also what lets a RESTORED
            // effect record the gravity flag of the body it is actually landing on rather than one
            // remembered from last session.
            bool gravityWasOn = true;

            return new Effect(Duration)
            {
                Key = typeof(AntiGravityPotion),
                SaveToken = EffectToken,

                // Restored, not forced back to true. Gravity is already off for a mounted player
                // (MountModule) and for one the under-terrain guard is rescuing, and both of those
                // put it back to what they found. A potion that ends by asserting `true` hands
                // gravity back to a rider mid-flight; one that ends after a mount has captured its
                // `false` leaves the player with gravity off for good.
                applyEffect = rb =>
                {
                    gravityWasOn = rb.useGravity;
                    rb.useGravity = false;
                },

                // Acceleration, not Force: the lift has to feel the same on a heavy player and a
                // light one, and ForceMode.Acceleration is the mode that ignores mass.
                onTick = rb => rb.AddForce(Vector3.up * FloatForce, ForceMode.Acceleration),

                stopEffect = rb => rb.useGravity = gravityWasOn,
            };
        }

        protected override void ApplyEffect() => RegisterEffect(BuildEffect());
    }
}
