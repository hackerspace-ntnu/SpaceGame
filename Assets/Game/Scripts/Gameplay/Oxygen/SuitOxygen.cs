using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The air the wearer is breathing: a 60-second suit reserve, and the tank plugged into the
    /// pack's socket that keeps it full.
    ///
    /// <para>
    /// <b>The rules, in full, because the whole point of this class is that they are predictable:</b>
    /// </para>
    /// <list type="number">
    /// <item>Inside a <see cref="BreathableVolume"/> nothing drains and the suit refills from the
    /// ambient air. Walking into the ship must not cost tank charge, or shelter would be a
    /// purchase.</item>
    /// <item>Outside one, the socketed tank is drained a second per second and the suit is held
    /// full off it.</item>
    /// <item>With no tank, or an empty one, the SUIT drains instead. That is the 60-second reserve,
    /// and it is the only thing standing between the player and suffocation — the window in which
    /// they are meant to swap tanks.</item>
    /// <item>At zero suit, <see cref="suffocationDamage"/> every <see cref="suffocationInterval"/>.</item>
    /// </list>
    /// <para>
    /// <b>A tank supplies you from the socket and from nowhere else.</b> One in your hand, on the
    /// mat, or in a hotbar slot is cargo. See <see cref="OxygenSocket"/>; until 2026-09-04 a tank
    /// could also be breathed straight from the hand, which was a second path to the same outcome
    /// with an unexplainable waste rule attached.
    /// </para>
    /// <para>
    /// <b>Everything here is in SECONDS OF AIR</b>, which is why the drain rate is 1 and not a tuned
    /// fraction: a 30-minute tank holds 1800 and a suit holds 60, and the two numbers can be
    /// compared, added and shown without a conversion anywhere. The gauge divides by a capacity to
    /// get its percentage and nothing else ever does.
    /// </para>
    /// <para>
    /// <b>Server decides, everyone displays.</b> Modelled on <c>SandstormVictim</c> and
    /// <c>NetworkedHealthComponent</c>: the drain and the suffocation damage are applied only where
    /// this entity is simulated, the values are published through <see cref="NetworkVariable{T}"/>s,
    /// and a client cannot suffocate itself. The warnings are presentation and are raised by each
    /// machine for its own player.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class SuitOxygen : NetworkBehaviour
    {
        /// <summary>
        /// Every live suit, so a volume being switched off or streamed out can take itself back
        /// out of all of them. Trigger exits are not raised when a collider is disabled.
        /// </summary>
        private static readonly List<SuitOxygen> All = new(8);

        [Header("The suit's own reserve")]
        [Tooltip("Seconds of air the SUIT holds with no tank at all. A last resort and a swap " +
                 "window, deliberately short: long enough to reach into the pack, not long enough " +
                 "to walk home on.")]
        [SerializeField, Min(1f)] private float suitSeconds = 60f;

        [Tooltip("Seconds of air spent per second outside breathable air. One, because the unit " +
                 "IS the second — change a tank's capacity to retune the journey, not this.")]
        [SerializeField, Min(0f)] private float drainPerSecond = 1f;

        [Tooltip("Seconds of the suit's own reserve refilled per second while standing in " +
                 "breathable air. Faster than the drain so that stepping inside reads as relief " +
                 "rather than as a slow recovery the player has to wait out.")]
        [SerializeField, Min(0f)] private float refillPerSecond = 6f;

        [Header("Running out")]
        [Tooltip("Damage per suffocation tick once the suit is empty.")]
        [SerializeField, Min(1)] private int suffocationDamage = 5;

        [Tooltip("Seconds between suffocation ticks. Damage is an event, not a continuous " +
                 "quantity: ticking every frame floods the network and gives the player nothing " +
                 "extra to feel.")]
        [SerializeField, Min(0.1f)] private float suffocationInterval = 1f;

        [Header("Warnings")]
        [Tooltip("Fraction of the CONNECTED TANK at or below which the visor warns. At the " +
                 "standard 30-minute tank this is three minutes' notice.")]
        [SerializeField, Range(0f, 1f)] private float warnFraction = 0.10f;

        /// <summary>
        /// Seconds left in the suit's own reserve. Everyone / Server, matching
        /// <c>NetworkedHealthComponent</c>: owner WRITE permission would let a client edit its own
        /// air, and owner READ permission publishes to nobody for a server-owned object.
        /// </summary>
        private readonly NetworkVariable<float> networkSuit = new(
            60f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>
        /// How full the connected tank is, 0..1, and negative when nothing is connected.
        ///
        /// <para>
        /// Published separately from the pack's own contents list even though the pack replicates
        /// that too, because the two answer different questions at different rates: the pack says
        /// where the gear is and is written a hundred times over a tank, while this is the number
        /// the gauge in front of the player's eye is drawn from. One negative value carries "no
        /// tank" rather than a second bool — a fraction cannot legitimately be negative, so the
        /// sign is free.
        /// </para>
        /// </summary>
        private readonly NetworkVariable<float> networkTank = new(
            -1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>
        /// The authoritative values where this is simulated, and the last replicated ones
        /// everywhere else. Kept as plain fields rather than read out of the NetworkVariables so
        /// that an unspawned or offline suit still works — writing a NetworkVariable that has never
        /// spawned throws.
        /// </summary>
        private float suit;
        private float tank = -1f;

        private readonly HashSet<BreathableVolume> volumes = new();

        private OxygenSocket socket;
        private HealthComponent health;
        private float sinceSuffocation;
        private string warningShown;

        /// <summary>Seconds of air in the suit's own reserve.</summary>
        public float SuitSeconds => suit;

        /// <summary>Seconds of air a full suit holds.</summary>
        public float SuitCapacity => suitSeconds;

        /// <summary>The suit's reserve as 0..1.</summary>
        public float SuitFraction => suitSeconds > 0f ? Mathf.Clamp01(suit / suitSeconds) : 0f;

        /// <summary>Is a tank plugged into the pack's socket? An EMPTY one still counts.</summary>
        public bool TankConnected => tank >= 0f;

        /// <summary>How full the connected tank is, 0..1. Zero with no tank.</summary>
        public float TankFraction => tank >= 0f ? Mathf.Clamp01(tank) : 0f;

        /// <summary>
        /// Is the wearer living off the suit's own reserve — no tank, or a dry one? The one state
        /// worth a name, because it is the only one with a deadline attached.
        /// </summary>
        public bool OnReserve => !Breathing && TankFraction <= 0f;

        /// <summary>Whether the wearer is standing in breathable air and therefore not draining.</summary>
        public bool Breathing => volumes.Count > 0;

        /// <summary>Fraction of the connected tank at or below which the visor warns.</summary>
        public float WarnFraction => warnFraction;

        /// <summary>True where this machine decides what happens to this suit.</summary>
        private bool Authoritative => Network.Simulates(this);

        private void Awake()
        {
            health = GetComponentInChildren<HealthComponent>();
            socket = new OxygenSocket(gameObject);
            suit = suitSeconds;
        }

        private void OnEnable() => All.Add(this);

        private void OnDisable()
        {
            All.Remove(this);
            volumes.Clear();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                networkSuit.Value = suit;
                networkTank.Value = tank;
                return;
            }

            // Late joiners arrive with the current values already in the variables and no change
            // events coming, so read them once on spawn.
            networkSuit.OnValueChanged += ApplySuit;
            networkTank.OnValueChanged += ApplyTank;
            suit = networkSuit.Value;
            tank = networkTank.Value;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) return;

            networkSuit.OnValueChanged -= ApplySuit;
            networkTank.OnValueChanged -= ApplyTank;
        }

        private void ApplySuit(float _, float next) => suit = next;

        private void ApplyTank(float _, float next) => tank = next;

        /// <summary>Called by a <see cref="BreathableVolume"/> the wearer has entered.</summary>
        public void EnterBreathable(BreathableVolume volume)
        {
            if (volume != null) volumes.Add(volume);
        }

        /// <summary>Called by a <see cref="BreathableVolume"/> the wearer has left.</summary>
        public void ExitBreathable(BreathableVolume volume)
        {
            if (volume != null) volumes.Remove(volume);
        }

        /// <summary>
        /// Takes <paramref name="volume"/> out of every suit that thought it was inside it.
        /// <para>
        /// Unity raises no <c>OnTriggerExit</c> when a collider is disabled or its scene unloads,
        /// so without this a player standing in a chunk that streams out keeps the shelter for
        /// ever and never breathes their own supply again.
        /// </para>
        /// </summary>
        public static void ForgetVolume(BreathableVolume volume)
        {
            if (volume == null) return;

            foreach (SuitOxygen s in All) s.volumes.Remove(volume);
        }

        private void Update()
        {
            // Warnings are presentation: every machine raises them for its own player, off the
            // values the server replicated. Doing it server-side would announce one player's
            // failing suit on everybody's visor.
            if (IsLocalPlayersSuit()) UpdateWarnings();

            if (!Authoritative) return;

            float dt = Time.deltaTime;

            socket.Refresh();
            Breathe(dt);
            Publish();
            Suffocate(dt);
        }

        /// <summary>
        /// One tick of the supply rules: take what the tank can give, then settle the suit.
        ///
        /// <para>
        /// The tank is drawn on FIRST and the suit is topped up off it, so a player with a tank
        /// spends the whole tank before their own reserve is touched at all. That ordering is the
        /// entire difference between "the suit is a last resort" and "the suit is a second tank you
        /// happen to burn first".
        /// </para>
        /// </summary>
        private void Breathe(float dt)
        {
            float wanted = Breathing ? 0f : drainPerSecond * dt;

            // Asked for on top of the tick: rule 2 is that the tank holds the suit FULL, so a
            // reserve that was run down and then had a fresh tank plugged into it fills back up out
            // of that tank. Rate-limited to the same refill the ambient air gives, because an
            // instant thirty-second gulp the moment a tank goes in reads as a glitch rather than as
            // a system — and because the tank pays for it either way.
            float topUp = Breathing
                ? 0f
                : Mathf.Min(suitSeconds - suit, refillPerSecond * dt);

            float fromTank = wanted + topUp > 0f ? socket.Draw(wanted + topUp) : 0f;

            suit = SuitAfter(Breathing, suit, suitSeconds, refillPerSecond * dt, wanted, fromTank);
        }

        /// <summary>
        /// Where the suit's own reserve ends up after one tick. <b>Static and pure</b>, which is the
        /// point: <c>Awake</c> does not run on an <c>AddComponent</c> in an EditMode test and
        /// <c>Update</c> never runs at all, so a rule that lives only inside a MonoBehaviour's
        /// frame loop is a rule no test can reach — the same reason every decision in
        /// <c>VisorGauge</c> is a static helper.
        /// </summary>
        /// <param name="breathing">Standing in breathable air.</param>
        /// <param name="suit">Seconds in the reserve now.</param>
        /// <param name="capacity">Seconds a full reserve holds.</param>
        /// <param name="refill">Seconds of ambient air this tick is worth. Ignored outside shelter.</param>
        /// <param name="wanted">Seconds of air this tick costs. Zero inside shelter.</param>
        /// <param name="fromTank">Seconds the socketed tank actually supplied, at most <paramref name="wanted"/>.</param>
        public static float SuitAfter(bool breathing, float suit, float capacity,
                                      float refill, float wanted, float fromTank)
        {
            // Shelter: the ambient air refills the reserve and the tank is never touched. Walking
            // into the ship must not cost tank charge, or shelter would be a purchase.
            if (breathing) return Mathf.Min(capacity, suit + refill);

            // The tank's contribution goes in and the tick's cost comes out, and the result is
            // clamped ONCE at the end.
            //
            // Clamping the sum to capacity FIRST is the bug this line replaced: a suit already at
            // capacity had the tank's whole contribution clamped away and then paid the tick out of
            // its own reserve anyway, so a player with a full tank lost a second of reserve every
            // second. It is invisible for the first fifty-nine seconds of a thirty-minute tank.
            return Mathf.Clamp(suit + fromTank - wanted, 0f, capacity);
        }

        private void Suffocate(float dt)
        {
            if (suit > 0f)
            {
                // Reset rather than let it run: reaching air a moment before a tick should not bank
                // that tick against you for the next time you run out.
                sinceSuffocation = 0f;
                return;
            }

            sinceSuffocation += dt;
            if (sinceSuffocation < suffocationInterval) return;

            sinceSuffocation = 0f;
            if (health != null && health.GetHealth > 0)
                NetDamage.Apply(health.gameObject, suffocationDamage);
        }

        private void Publish()
        {
            tank = socket.Connected ? socket.Charge : -1f;

            // IsSpawned, because writing a NetworkVariable that has never spawned throws — and an
            // offline session has no NetworkManager at all, which is the ordinary case in the
            // editor rather than an exotic one.
            if (!IsSpawned || !IsServer) return;

            networkSuit.Value = suit;
            networkTank.Value = tank;
        }

        /// <summary>
        /// Whether this suit belongs to the player driving this machine — the only one whose
        /// warnings belong on this screen.
        /// </summary>
        private bool IsLocalPlayersSuit() => !IsSpawned || IsOwner;

        /// <summary>
        /// The three states worth telling the player about, in order of severity, posted through
        /// one id so they replace each other rather than stacking.
        ///
        /// <para>
        /// <b>Only on a CHANGE of state.</b> <c>SystemMessages.Post</c> replaces by id, so posting
        /// every frame would work and would also restart the banner's animation sixty times a
        /// second. <see cref="warningShown"/> is the text last posted, so the comparison is against
        /// what the player can actually see.
        /// </para>
        /// </summary>
        private void UpdateWarnings()
        {
            string text = null;
            MessageSeverity severity = MessageSeverity.Warning;

            if (Breathing)
            {
                // Shelter. Nothing is running out, whatever the gauges read.
            }
            else if (suit <= 0f)
            {
                text = "SUFFOCATING";
                severity = MessageSeverity.Alarm;
            }
            else if (TankFraction <= 0f)
            {
                // The reserve is engaged: no tank, or a dry one. This is the alarm the 60 seconds
                // exist for, and it fires whether or not a tank was ever fitted — a player who set
                // out with an empty socket is in exactly the same trouble as one whose tank ran dry.
                text = "RESERVE OXYGEN";
                severity = MessageSeverity.Alarm;
            }
            else if (TankFraction <= warnFraction)
            {
                text = "OXYGEN LOW";
            }

            if (text == warningShown) return;

            if (text == null) SystemMessages.Clear("suit.oxygen");
            else SystemMessages.Post("suit.oxygen", text, severity);

            warningShown = text;
        }

        /// <summary>
        /// Server-side restore from a save. Not a property setter: this must never be reachable
        /// from ordinary gameplay code, which changes air only by breathing it.
        ///
        /// <para>
        /// The suit only. The tank is not restored from here and must not be — its charge belongs
        /// to the tank, travels in the pack's own record, and is picked up again by the next
        /// <see cref="OxygenSocket.Refresh"/>. Storing it in two places is how the two come to
        /// disagree.
        /// </para>
        /// </summary>
        public void RestoreOxygen(float value)
        {
            suit = Mathf.Clamp(value, 0f, suitSeconds);

            if (IsSpawned && IsServer) networkSuit.Value = suit;

            // Re-evaluated from scratch next frame, so a world reloaded into an already-failing
            // suit still announces itself.
            warningShown = null;
        }

        /// <summary>
        /// Push the connected tank's live charge back into the pack, so a save records it as it
        /// actually is rather than as it was up to a percent ago. Called by the saver.
        /// </summary>
        public void FlushTank() => socket?.Flush();
    }
}
