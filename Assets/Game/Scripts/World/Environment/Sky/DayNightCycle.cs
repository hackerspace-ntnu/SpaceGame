// What hour it is, everywhere at once.
//
// This used to accumulate Time.deltaTime locally, which meant every machine ran its own day. Two
// players a few minutes apart drifted visibly; a late joiner started at the hour the scene was
// authored with while the host was most of a night ahead, so one player's midnight was another's
// afternoon and neither could describe where they were by the light.
//
// The fix is the one the sandstorm system already uses: the hour is a pure function of a clock every
// machine agrees on (Sandstorms.Now — server time in a session, plain game time without one) plus an
// anchor saying which reading of that clock counts as which hour. Two machines evaluating the same
// function against the same clock cannot drift, and a player joining four hours in derives the same
// evening as everyone else. Nothing is sent per frame, and nothing needs to be.
//
// The one case arithmetic alone cannot reach is a host that LOADED a save. SaveManager restores on
// the server only, so the host's anchor moves to an hour no client can compute for itself, and no
// amount of shared-clock arithmetic bridges that — the information genuinely has to cross the wire.
// So the ANCHOR ITSELF is replicated: two numbers, once, whenever it moves. That is done by
// SkyNetwork (Core/Multiplayer/SkyNetwork.cs), which lives on the NetworkGameManager's
// NetworkObject because this component sits on the Sun prefab and the Sun has none — a
// NetworkBehaviour without a NetworkObject is an error in Netcode.
//
// The split is deliberate: this class stays the only thing that knows what time it is. SkyNetwork
// never computes an hour, never keeps one for its own use and never answers a question about the
// sky. It carries a statement between machines and hands it straight back to this class.
using System;
using System.Collections.Generic;
using SpaceGame.Core;
using SpaceGame.World.Weather;
using UnityEngine;

namespace SpaceGame.World
{
    public class DayNightCycle : MonoBehaviour
    {
        /// <summary>Floor on <see cref="cycleDuration"/>. A zero-length day is a division by zero.</summary>
        private const float MinCycleDuration = 0.0001f;

        [Header("Day/Night Cycle Settings")]
        [Tooltip("Duration of a full day/night cycle in seconds")]
        public float cycleDuration = 2400f;

        [Tooltip("Freeze the cycle and keep the rotation authored in the scene. Used by the main menu so the light never drifts into night.")]
        public bool freezeCycle = false;

        [Tooltip("Starting time of day (0 = midnight, 0.5 = noon)")]
        [Range(0f, 1f)]
        public float startTimeOfDay = 0.25f;

        [Header("Rotation Settings")]
        [Tooltip("The axis around which the light rotates (typically X-axis for sun/moon)")]
        public Vector3 rotationAxis = Vector3.right;

        private Light directionalLight;

        /// <summary>The hour the light was last pointed at. Read by the anchor when the clock changes.</summary>
        private float currentTimeOfDay;

        // The anchor: at clock reading anchorClock the world reads anchorPhase, and every other
        // hour follows from cycleDuration. Two numbers rather than an accumulator, because an
        // accumulator can only ever describe THIS machine's history.
        private double anchorClock;
        private double anchorPhase;

        /// <summary>
        /// Which clock <see cref="anchorClock"/> was measured against — a session's server time, or
        /// this process's game time. The two count from completely different origins, so an anchor
        /// taken against one is meaningless against the other.
        /// </summary>
        private bool anchoredToSession;

        /// <summary>
        /// True once anything at all has set the anchor. Before that the two numbers above are
        /// zeroes rather than an answer, and a replicator that published them anyway would tell
        /// every client in the session that it is midnight.
        /// </summary>
        private bool hasAnchor;

        /// <summary>
        /// True once an AUTHORITY has said what hour it is — a save loaded on this machine, or the
        /// server through <c>SkyNetwork</c>. It stops <see cref="BeginDay"/> from overwriting a
        /// statement that arrived first, which both of those routinely do: global savers register in
        /// OnEnable and SaveManager applies staged state on the spot, and a joining client is handed
        /// the session's anchor from that same OnEnable — both before this component's Start runs.
        /// </summary>
        private bool hasStatedHour;

        // ------------------------------------------------------------------ the live cycles
        //
        // A tiny registry and two static events, rather than a reference either way, because the
        // thing that replicates the anchor cannot hold one: SkyNetwork lives on the
        // NetworkGameManager's NetworkObject in persistentScene, and the Sun is a separate object in
        // a scene that can be loaded and unloaded underneath it. Neither owns the other, and this
        // class still compiles and runs with no netcode in the project at all.

        private static readonly List<DayNightCycle> live = new();

        /// <summary>
        /// Every enabled cycle in the loaded scenes. Usually exactly one — the Sun in
        /// persistentScene — but the main menu has its own frozen one, so during a handover between
        /// scenes both exist for a frame and neither is "the" cycle.
        /// </summary>
        public static IReadOnlyList<DayNightCycle> Live => live;

        /// <summary>
        /// Raised whenever a cycle's anchor moves, i.e. whenever something states what hour it is.
        /// The server's cue to replicate that statement.
        /// </summary>
        public static event Action<DayNightCycle> AnchorMoved;

        /// <summary>
        /// Raised when a cycle wakes up, before its Start. The cue to hand an already-replicated
        /// hour to a sun that arrived after the session did — a client streams its world in on the
        /// server's schedule, so the Sun frequently exists later than the object carrying the hour.
        /// </summary>
        public static event Action<DayNightCycle> CycleAwake;

        private void OnEnable()
        {
            live.Add(this);
            CycleAwake?.Invoke(this);
        }

        private void OnDisable() => live.Remove(this);

        // ------------------------------------------------------------------ the hour

        /// <summary>
        /// The clock every machine agrees on. Borrowed from the weather rather than defined again:
        /// the storms and the sun share one sky, and two clocks that could disagree is exactly the
        /// bug this class was written to remove.
        /// </summary>
        public static double Now => Sandstorms.Now;

        /// <summary>Where the day is right now: 0 is midnight, 0.5 noon.</summary>
        public float TimeOfDay => PhaseAt(Now);

        /// <summary>
        /// Where the day is when the shared clock reads <paramref name="clock"/>.
        /// <para>
        /// Wrapped in double before it is narrowed. A session left running overnight puts hundreds
        /// of whole cycles in front of the fraction that matters, and a float that wide has already
        /// lost the minutes.
        /// </para>
        /// </summary>
        public float PhaseAt(double clock)
        {
            double cycles = anchorPhase + (clock - anchorClock) / Mathf.Max(MinCycleDuration, cycleDuration);
            return (float)(cycles - System.Math.Floor(cycles));
        }

        /// <summary>
        /// States that the world reads <paramref name="phase"/> when the shared clock reads
        /// <paramref name="clock"/>. The only way the hour is ever set.
        /// </summary>
        public void AnchorTo(float phase, double clock)
        {
            anchorPhase = phase;
            anchorClock = clock;
            anchoredToSession = Network.IsNetworked;
            hasAnchor = true;

            // Announced rather than pushed at anybody: this class does not know whether there is a
            // session, and has to keep working when there is not.
            AnchorMoved?.Invoke(this);
        }

        /// <summary>
        /// Has anything set this cycle's anchor yet? False means the two numbers are still zeroes,
        /// which is a value and not an answer.
        /// </summary>
        public bool HasAnchor => hasAnchor;

        /// <summary>
        /// The anchor as it stands, for something that has to carry it elsewhere — the wire.
        /// <para>
        /// <paramref name="phase"/> narrows exactly: every value ever stored arrived as a float. The
        /// double is for the arithmetic in <see cref="PhaseAt"/>, not for extra precision here.
        /// </para>
        /// </summary>
        public void ReadAnchor(out float phase, out double clock)
        {
            phase = (float)anchorPhase;
            clock = anchorClock;
        }

        /// <summary>
        /// Pins the authored <see cref="startTimeOfDay"/> to the current clock's own origin.
        /// <para>
        /// In a session that origin is zero — the session itself. <c>ServerTime.Time</c> counts
        /// seconds since the host opened it and every client estimates the same number, so pinning
        /// the authored hour to zero makes the whole formula machine-independent: a late joiner
        /// runs it and lands on the host's hour, with nothing sent to tell it.
        /// </para>
        /// <para>
        /// With no session there is only this machine, and <c>Time.timeAsDouble</c> counts from
        /// process start — so the origin is now. Otherwise five minutes spent in the main menu
        /// would already be three hours of daylight by the time the world opened.
        /// </para>
        /// </summary>
        public void AnchorToClockOrigin() =>
            AnchorTo(startTimeOfDay, Network.IsNetworked ? 0d : Now);

        /// <summary>
        /// Takes an hour stated by an authority: a save loaded on this machine, or — on a client —
        /// the server's own anchor arriving through <c>SkyNetwork</c>.
        /// <para>
        /// <paramref name="clock"/> is the reading the phase was measured against, and it has to
        /// travel with the phase. The same phase applied against each machine's own "now" would be a
        /// different hour on every machine that applied it, which is exactly why the pair is
        /// replicated together rather than the hour alone.
        /// </para>
        /// </summary>
        public void AdoptAnchor(float phase, double clock)
        {
            // A frozen light keeps the angle the scene authored, and neither a save nor the server
            // may be the one thing that swings it. Same rule as Start below.
            if (freezeCycle) return;

            AnchorTo(Mathf.Repeat(phase, 1f), clock);
            hasStatedHour = true;
            UpdateLightRotation();
        }

        /// <summary>
        /// Puts the world back at a saved hour. Called by the save adapter; see
        /// <c>DayNightSaveable</c>.
        /// <para>
        /// Only the machine that loaded the file runs this — SaveManager restores on the server and
        /// offline, never on a client. That is the right shape: the hour is the server's to state,
        /// and every client is told it once by <c>SkyNetwork</c> instead of reading the file.
        /// </para>
        /// <para>
        /// A saved phase carries no clock reading with it, because a save file outlives the session
        /// it was written in. So it is anchored to this machine's reading of NOW — the moment the
        /// world is being put back — and that reading is what then gets replicated.
        /// </para>
        /// </summary>
        public void RestoreTimeOfDay(float phase) => AdoptAnchor(phase, Now);

        /// <summary>
        /// Settles which hour this cycle opens at. Called from <see cref="Start"/>, and public so a
        /// test can ask the question without a frame ever having run.
        /// <para>
        /// Does nothing once an authority has spoken — see <see cref="hasStatedHour"/> for the two
        /// ways that happens before Start. Re-deriving here is what put a loaded world back at the
        /// authored morning, and would now do the same to a client that had just been told the
        /// session's hour.
        /// </para>
        /// </summary>
        public void BeginDay()
        {
            if (hasStatedHour) return;

            AnchorToClockOrigin();
        }

        void Start()
        {
            // Get the Light component attached to this GameObject
            directionalLight = GetComponent<Light>();

            if (directionalLight == null)
            {
                Debug.LogError("DayNightCycle: No Light component found on this GameObject!");
                enabled = false;
                return;
            }

            if (directionalLight.type != LightType.Directional)
            {
                Debug.LogWarning("DayNightCycle: Light is not set to Directional type!");
            }

            // A frozen light keeps whatever angle the scene authored - never snap it to startTimeOfDay.
            if (freezeCycle)
            {
                enabled = false;
                return;
            }

            BeginDay();
            UpdateLightRotation();
        }

        void Update()
        {
            SyncClockSource();
            UpdateLightRotation();
        }

        /// <summary>
        /// Restates the anchor when the clock underneath it changes identity — a session coming up
        /// under a scene that started without one, or shutting down under one that had it.
        /// <para>
        /// Readings from the two clocks are not comparable, so an anchor left alone across the
        /// handover would throw the sun to a random hour. A world that has been told its hour keeps
        /// that hour and has it restated against the new clock; one that has not simply re-derives
        /// the authored hour from the new origin, which is the only value a joining client could
        /// ever compute for itself.
        /// </para>
        /// <para>
        /// Restating raises <see cref="AnchorMoved"/> like any other statement, and that is what
        /// republishes a host's loaded hour once its session finally comes up: a world loaded before
        /// StartHost was anchored against game time, and the number clients need is the one measured
        /// against server time.
        /// </para>
        /// </summary>
        private void SyncClockSource()
        {
            if (anchoredToSession == Network.IsNetworked) return;

            if (hasStatedHour) AnchorTo(currentTimeOfDay, Now);
            else AnchorToClockOrigin();
        }

        private void UpdateLightRotation()
        {
            currentTimeOfDay = TimeOfDay;

            // Convert time of day (0-1) to rotation angle (0-360 degrees)
            float rotationAngle = currentTimeOfDay * 360f;

            // Apply rotation around the specified axis
            transform.rotation = Quaternion.AngleAxis(rotationAngle, rotationAxis);
        }
    }
}
