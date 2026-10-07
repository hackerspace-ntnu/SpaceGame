using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Turns a saved effect token back into a live <see cref="Effect"/>.
    ///
    /// <para>
    /// <b>Why this exists.</b> An effect is three delegates and a timer. The timer survives a save
    /// trivially; the delegates cannot survive it at all, and the object that authored them — a
    /// single-use potion — has usually been destroyed seconds before the save is written. So a
    /// record can only ever say <i>which</i> effect was running, and something has to know how to
    /// build that one again.
    /// </para>
    /// <para>
    /// <b>Why registration is per item rather than a list here.</b> Each effect item registers
    /// itself from its own <c>[RuntimeInitializeOnLoadMethod]</c>, so adding an effect item is one
    /// line inside that item and nothing here. A central table would be a list of names in a file
    /// nobody edits when they add the twelfth potion — which is the failure mode this project has
    /// already been bitten by, and the reason <c>IPersistentEntity</c> is an interface rather than a
    /// list of type names.
    /// </para>
    /// </summary>
    public static class EffectCatalog
    {
        private static readonly Dictionary<string, Func<Effect>> Factories = new();

        /// <summary>
        /// Teach the catalog how to rebuild one kind of effect.
        ///
        /// <paramref name="token"/> is written into save files, so it is permanent — rename it and
        /// every saved effect under the old spelling is dropped (with a warning, not a throw).
        /// </summary>
        public static void Register(string token, Func<Effect> factory)
        {
            if (string.IsNullOrEmpty(token) || factory == null) return;
            Factories[token] = factory;
        }

        /// <summary>
        /// Build a fresh effect of the named kind, running for <paramref name="remaining"/> seconds
        /// rather than its full duration.
        ///
        /// <para>
        /// False for a token this build has never heard of — an effect deleted since the save, or
        /// one whose registration never ran. That is a bad save, not a broken game: the player loses
        /// the tail of one buff.
        /// </para>
        /// </summary>
        public static bool TryBuild(string token, float remaining, out Effect effect)
        {
            effect = null;

            if (string.IsNullOrEmpty(token) || remaining <= 0f) return false;

            if (!Factories.TryGetValue(token, out Func<Effect> factory))
            {
                Debug.LogWarning($"[Effect] No factory registered for saved effect '{token}', so it " +
                                 "was dropped. Was the item that produces it removed from the build?");
                return false;
            }

            effect = factory();
            if (effect == null) return false;

            // The factory builds the effect at its authored duration, because that is the one thing
            // it knows how to do and the one thing every other caller wants. Only a restore has an
            // opinion about how much of it is left.
            effect.timer = remaining;
            effect.SaveToken = token;
            return true;
        }
    }
}
