using System;
using Newtonsoft.Json.Linq;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Persistence;

namespace SpaceGame.Gameplay.SpriteHamlet
{
    /// <summary>
    /// How many of the witch's poison balls have reached a keeper. Three separate balls and it falls,
    /// and the fight is lost.
    ///
    /// <para>
    /// <b>A count, not health.</b> A keeper's health is untouchable — <c>StunOnHitModule</c> swallows
    /// every other blow and floors the keeper for it. A <see cref="DamageKind.Poison"/> ball is
    /// neither: this filter zeroes it and counts it, and the stun lets it pass, so a keeper reached by
    /// the poison coughs and keeps throwing, and the player reads the danger off three pips rather
    /// than a health bar on a creature that otherwise cannot be hurt.
    /// </para>
    /// <para>
    /// <b>No healing.</b> The count only climbs, until the fight is reset by a fall. It is saved, so
    /// quitting before the third ball does not hand the keeper its breath back.
    /// </para>
    /// <para>
    /// <b>Decided on the server</b> — the filter runs where damage is decided — and replicated, so
    /// every machine draws the same pips and the same greening.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(HealthComponent))]
    public class PoisonTally : NetworkBehaviour, IDamageFilter, ISaveable
    {
        public const string Key = "poisonTally";

        [Tooltip("Separate poison balls that make the keeper fall.")]
        [SerializeField, Min(1)] private int fallsAt = 3;

        private readonly NetworkVariable<int> hits = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private HealthComponent health;

        /// <summary>SERVER: a keeper has just taken its last ball.</summary>
        public static event Action<PoisonTally> Fell;

        /// <summary>Every machine: the count changed, old → new.</summary>
        public event Action<int, int> HitsChanged;

        public int Hits => hits.Value;

        public int FallsAt => fallsAt;

        public bool Fallen => hits.Value >= fallsAt;

        public string SaveKey => Key;

        private void Awake() => health = GetComponent<HealthComponent>();

        private void OnEnable()
        {
            health.AddFilter(this);
            hits.OnValueChanged += OnHitsChanged;
        }

        private void OnDisable()
        {
            health.RemoveFilter(this);
            hits.OnValueChanged -= OnHitsChanged;
        }

        private void OnHitsChanged(int previous, int current) => HitsChanged?.Invoke(previous, current);

        public void Filter(HealthComponent victim, ref DamageHit hit)
        {
            if (hit.Kind != DamageKind.Poison) return;

            // Never health, never "blocked": the count is the whole of what a ball does to a keeper.
            hit.Amount = 0;
            if (Fallen) return;

            hits.Value = Mathf.Min(fallsAt, hits.Value + 1);
            if (Fallen) Fell?.Invoke(this);
        }

        /// <summary>SERVER: back on its feet with a clear chest — the fight is starting over.</summary>
        public void Clear()
        {
            if (hits.Value != 0) hits.Value = 0;
        }

        public object CaptureState() => new State { hits = hits.Value };

        public void RestoreState(JObject state)
        {
            if (!Network.Decides) return;

            // Never restored as fallen. A fall resets the fight a moment later, and a save written in
            // that moment would load a keeper lying in the grass with nothing left to stand it up.
            int restored = state?["hits"] is { Type: JTokenType.Integer } value ? value.Value<int>() : 0;
            hits.Value = Mathf.Clamp(restored, 0, fallsAt - 1);
        }

        private struct State
        {
            public int hits;
        }
    }
}
