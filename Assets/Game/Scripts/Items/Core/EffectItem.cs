using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// EffectItem is for effects that need time-based management.
    /// Examples: Anti-gravity potion, slow effect, speed boost over time, damage over time, etc.
    /// These items register their effects with the EffectManager and automatically clean up when unequipped.
    /// </summary>
    public abstract class EffectItem : UsableItem
    {
        /// <summary>
        /// Owner-run. An effect is registered against the holder's own Rigidbody, and that body is
        /// simulated on the holder's machine — a server that applied the force would be fighting
        /// the owner-authoritative transform coming back the other way.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Owner;

        protected Effect currentEffect;

        /// <summary>
        /// Create and register an effect with the EffectManager.
        /// Call this from your Use() implementation.
        /// </summary>
        protected void RegisterEffect(float duration, System.Action<Rigidbody> onApply, 
            System.Action<Rigidbody> onTick, System.Action<Rigidbody> onStop)
        {
            var effectManager = owner.GetComponent<EffectManager>();
            if (effectManager == null)
            {
                Debug.LogWarning("EffectManager not found on player!");
                return;
            }

            // Remove previous effect if still active
            if (currentEffect != null)
            {
                effectManager.RemoveEffect(currentEffect);
            }

            // Create and register new effect
            currentEffect = new Effect(duration)
            {
                applyEffect = onApply,
                onTick = onTick,
                stopEffect = onStop
            };

            effectManager.AddEffect(currentEffect);
        }
    }
}
