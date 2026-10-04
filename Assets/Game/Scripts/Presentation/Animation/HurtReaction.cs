using SpaceGame.Core;
using SpaceGame.Gameplay;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Raises <see cref="CharacterMoment.Hurt"/> when a humanoid body takes damage — on the server,
    /// shown on every machine.
    ///
    /// <para>
    /// Decided on the server, because only the server knows a hit happened:
    /// <see cref="HealthComponent.OnDamage"/> fires where <c>Damage()</c> ran, and every other
    /// machine learns the new health through <c>RestoreHealth</c>, which cannot tell a hit from a
    /// heal. So <see cref="BodyLanguage.ReactEverywhere"/> picks the flinch here and sends it
    /// (GDC-L1-ANIM-0003: the hit reaction is the confirmation that a hit landed, so every watcher
    /// must see the same one).
    /// </para>
    /// <para>
    /// Which flinch, how often and how soon again is the <c>Hurt</c> row of the reaction table, and
    /// the cue picks by posture: the whole body standing still, the upper body on the move or seated,
    /// the upper body always for the player (its <see cref="BodyLanguage"/> has full body off).
    /// </para>
    /// <para><b>Persistence:</b> none; a flinch is half a second long.</para>
    /// </summary>
    public sealed class HurtReaction : MonoBehaviour
    {
        private BodyLanguage body;
        private HealthComponent health;

        private void Awake()
        {
            body = BodyLanguage.Of(this);
            health = GetComponentInParent<HealthComponent>();
            if (health == null) health = GetComponentInChildren<HealthComponent>();
        }

        private void OnEnable()
        {
            if (health != null) health.OnDamage += OnDamage;
        }

        private void OnDisable()
        {
            if (health != null) health.OnDamage -= OnDamage;
        }

        private void OnDamage(int amount)
        {
            if (!Network.Decides || !health.Alive || body == null) return;
            // A block that let part of the blow through has already shown the hit its own way; a
            // flinch on top would cut the guard off mid-raise and read as a hit taken in full.
            if (health.LastDefense != DamageDefense.None) return;
            body.ReactEverywhere(CharacterMoment.Hurt);
        }
    }
}
