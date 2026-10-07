using SpaceGame.Characters;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Adapts <see cref="JetpackFlight"/> to <see cref="IVisorGaugeSource"/>.
    ///
    /// <para>
    /// <b>The nozzles are behind you, and that is the whole argument for this gauge.</b> The pack
    /// already says everything about its heat diegetically — the tips go red, the smoke comes off
    /// them — and in first person the wearer can see none of it. Misreading heat is what drops a
    /// player out of the sky, so the one person who needs the readout is the one person the
    /// hardware cannot tell (<c>GDC-L1-UX-0003</c>). The warning fraction here is read off the
    /// flight's own config rather than typed again, so the bar turns amber on the same frame the
    /// tips start glowing.
    /// </para>
    /// <para>
    /// <b>It reads INVERTED, like a fuel bar rather than like a thermometer.</b> Every other gauge
    /// on this visor empties toward danger, and a lone bar that fills toward it would be read
    /// wrong at a glance by exactly the players who are too busy to read it properly. So the
    /// number drawn is how much burn is LEFT, and the same "low is bad" habit covers all three.
    /// </para>
    /// <para>
    /// Holds the component rather than its numbers, like <see cref="OxygenGaugeSource"/> and
    /// <see cref="HealthGaugeSource"/>, so the gauge reads through to live heat — and reports
    /// <see cref="Available"/> false when no pack is worn, which is what hides it.
    /// </para>
    /// </summary>
    public class JetpackHeatGaugeSource : IVisorGaugeSource
    {
        /// <summary>
        /// How often the player is searched for a flight component, seconds.
        ///
        /// <para>
        /// The component is added and destroyed as the pack is worn and taken off, so a binding
        /// taken once at spawn is wrong for the rest of the session. Re-resolving on a timer
        /// rather than on every property read keeps this to four <c>GetComponent</c> calls a
        /// second instead of several hundred, and a gauge that takes a quarter of a second to
        /// appear when gear is put on is not a gauge anyone notices being late.
        /// </para>
        /// </summary>
        private const float ResolveInterval = 0.25f;

        private GameObject player;
        private JetpackFlight flight;
        private float nextResolve;

        /// <summary>Points the source at a player body. Safe with null.</summary>
        public void Bind(GameObject value)
        {
            player = value;
            flight = null;
            nextResolve = 0f;
        }

        /// <summary>The flight currently read, or null while no pack is worn.</summary>
        public JetpackFlight Flight
        {
            get
            {
                if (flight != null) return flight;
                if (player == null || Time.unscaledTime < nextResolve) return null;

                nextResolve = Time.unscaledTime + ResolveInterval;
                flight = player.GetComponent<JetpackFlight>();

                return flight;
            }
        }

        /// <summary>Burn remaining, as a percentage. See the class summary for why it is inverted.</summary>
        public float Current
        {
            get
            {
                JetpackFlight live = Flight;
                return live == null ? 0f : 100f * (1f - live.HeatFraction);
            }
        }

        public float Max => Flight != null ? 100f : 0f;

        /// <summary>
        /// Relabelled while the motors are locked out, because at that moment the number is no
        /// longer the question — "how much burn is left" is a thing to manage, "you cannot fly"
        /// is a thing to be told. The same relabelling trick the oxygen gauge uses for its reserve.
        /// </summary>
        public string Label
        {
            get
            {
                JetpackFlight live = Flight;
                return live != null && live.Heat.Overheated ? "JET OVERHEAT" : "JET BURN";
            }
        }

        /// <summary>
        /// Amber where the tips start glowing — the same number the pods use, taken off the same
        /// config, and inverted along with the value.
        /// </summary>
        public float WarnFraction
        {
            get
            {
                JetpackFlight live = Flight;
                return live == null ? 0.3f : 1f - live.Config.WarnFraction;
            }
        }

        /// <summary>
        /// Critical for the whole of an overheat and never otherwise. A pack at the relight point
        /// is still locked out, so the alarm has to key off the latch rather than off the bar —
        /// which is the same call the oxygen gauge makes about its reserve.
        /// </summary>
        public float AlarmFraction
        {
            get
            {
                JetpackFlight live = Flight;
                return live != null && live.Heat.Overheated ? 2f : 0f;
            }
        }

        public bool Available => Flight != null;
    }
}
