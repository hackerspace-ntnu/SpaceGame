using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The oxygen plant says how it is, at a glance: its status lamps glow a slow green pulse while it runs (in its mount,
    /// whole, powered), blink red while it is cracked, and sit amber while it is whole but has no cell. Coming online plays a
    /// short power-up once. Derived every frame from the mount's and the generator's replicated state, so every machine shows
    /// the same thing and nothing is sent; nothing is saved either — the state it reads already is.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OxygenPlantStatusLights : MonoBehaviour
    {
        private enum Reading { Unset, NoPower, Cracked, Running }

        [SerializeField] private OxygenPlantMount mount;

        [Tooltip("The status lamps, painted together.")]
        [SerializeField] private Renderer[] lamps = System.Array.Empty<Renderer>();

        [SerializeField] private Color running = new(0.2f, 1f, 0.35f);
        [SerializeField] private Color cracked = new(1f, 0.12f, 0.08f);
        [SerializeField] private Color noPower = new(1f, 0.6f, 0.1f);

        [Tooltip("The running pulse, per second. Slow: a heartbeat, not an alarm.")]
        [SerializeField, Min(0f)] private float pulseRate = 0.6f;

        [Tooltip("The cracked blink, per second.")]
        [SerializeField, Min(0f)] private float blinkRate = 1.5f;

        [Tooltip("Played once when the plant comes online. No dedicated power-up event exists in the catalog.")]
        [SerializeField] private SfxId onlineSound = SfxId.ShipRepair;

        private Reading shown = Reading.Unset;

        private void Update()
        {
            if (mount == null) return;

            Reading now = mount.Running ? Reading.Running : mount.Damaged ? Reading.Cracked : Reading.NoPower;

            // The first reading is the state the plant was found in (a spawn, a load): no fanfare for that.
            if (now == Reading.Running && shown != Reading.Running && shown != Reading.Unset)
                Sfx.Play(onlineSound, transform.position, GetInstanceID());
            shown = now;

            Color colour = now switch
            {
                Reading.Running => running * Mathf.Lerp(0.55f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * pulseRate * 2f * Mathf.PI)),
                Reading.Cracked => Mathf.Repeat(Time.time * blinkRate, 1f) < 0.5f ? cracked : cracked * 0.15f,
                _ => noPower,
            };

            foreach (Renderer lamp in lamps) EmissiveLamp.Paint(lamp, colour);
        }
    }
}
