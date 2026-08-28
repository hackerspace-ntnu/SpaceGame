// The one thing about the sky that goes on the wire.
//
// Time of day is deliberately NOT synchronised. DayNightCycle derives it as a pure function of a
// clock every machine already agrees on, so two machines that never speak still show the same
// evening — see the header on World/Environment/Sky/DayNightCycle.cs. Sending a float every frame
// would be bandwidth spent to make an already-exact answer worse.
//
// One case that function cannot reach on its own: a host that LOADED a save. SaveManager restores on
// the server only, so the host's anchor moves to an hour derived from a file no client has read.
// Nothing a client can compute recovers it — this is the one piece of information that genuinely has
// to cross the wire. So the ANCHOR is replicated: the time-of-day value and the clock reading it was
// taken against, two numbers, written once each time the anchor moves.
//
// WHY THIS OBJECT. DayNightCycle lives on the Sun prefab, which has no NetworkObject, and a
// NetworkBehaviour on an object without one is an error in Netcode. The NetworkGameManager
// GameObject already carries a NetworkObject, is placed in persistentScene — loaded beneath every
// gameplay scene, including an additively loaded minigame arena — and spawns on every peer before
// the first player does. ChatNetwork is here for exactly the same reasons. Putting a NetworkObject
// on the Sun instead would mean registering a network prefab, spawning it server-side and re-saving
// every scene holding one, all to carry two numbers.
//
// WHAT THIS IS NOT. It is not a second source of truth. It never computes an hour, never keeps one
// for its own use and answers no question about the sky. It reads a statement out of DayNightCycle
// and hands a statement back to DayNightCycle; the cycle remains the only thing that knows what time
// it is. With no NetworkManager at all this component is simply never spawned, nothing subscribes,
// and the offline single-player path in DayNightCycle is untouched.
using System;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.Core
{
    /// <summary>
    /// Replicates the day/night anchor once, so a client joining a world the host LOADED derives the
    /// saved hour instead of the authored one.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class SkyNetwork : NetworkBehaviour
    {
        /// <summary>
        /// What the server states about the sky: at clock reading <see cref="SkyAnchor.Clock"/> the
        /// world reads <see cref="SkyAnchor.Phase"/>.
        /// <para>
        /// One struct rather than two NetworkVariables, because the two numbers are only meaningful
        /// together. Netcode reads a behaviour's variables one at a time and raises each callback as
        /// it goes, so a separate pair would be observed half-updated — a new phase against a stale
        /// clock reading is an arbitrary hour, and the sun would be pointed at it before the second
        /// value landed.
        /// </para>
        /// </summary>
        public struct SkyAnchor : INetworkSerializable, IEquatable<SkyAnchor>
        {
            /// <summary>
            /// False until the server has actually said something. Without it the default value —
            /// phase 0 at clock 0 — is indistinguishable from a genuine statement that the world is
            /// at midnight, and every client joining before the host's sun had woken up would be
            /// told so.
            /// </summary>
            public bool Stated;

            /// <summary>Time of day, 0 midnight to 1 midnight.</summary>
            public float Phase;

            /// <summary>The reading of the shared clock that <see cref="Phase"/> was measured at.</summary>
            public double Clock;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Stated);
                serializer.SerializeValue(ref Phase);
                serializer.SerializeValue(ref Clock);
            }

            /// <summary>
            /// Netcode compares the old and new value before it marks the variable dirty, so an
            /// exact re-statement of the same anchor costs nothing. This is what makes it safe for
            /// <c>Publish</c> to run on every announcement without rate-limiting itself.
            /// </summary>
            public bool Equals(SkyAnchor other) =>
                Stated == other.Stated &&
                Phase.Equals(other.Phase) &&
                Clock.Equals(other.Clock);
        }

        // Server-write, everyone-read: the hour is the server's to state. Not Owner — this object is
        // scene-placed, so its owner is the server anyway, and Owner permission on a server-owned
        // object is a permission nobody holds.
        private readonly NetworkVariable<SkyAnchor> anchor = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public override void OnNetworkSpawn()
        {
            // Subscribed on every peer and filtered inside. The host writes this variable and
            // Netcode raises the callback on the writer too, so without the guard in the handler the
            // host would adopt its own broadcast straight back.
            anchor.OnValueChanged += OnAnchorReplicated;

            if (IsServer)
            {
                DayNightCycle.AnchorMoved += Publish;

                // Catch-up. This spawn and the Sun's Start race, and a save restore can land earlier
                // still — AnchorMoved has already fired for it and will not fire again. Reading the
                // cycle's current anchor here is the server-side counterpart of the read-on-spawn
                // below.
                PublishFromLiveCycle();
                return;
            }

            // A sun that streams in later is told when it wakes; one that is already here is told
            // now. Both are needed: a client loads its chunks on the server's schedule, so the Sun
            // may exist either side of this spawn.
            DayNightCycle.CycleAwake += ApplyTo;

            // The read-on-spawn. Netcode delivers a NetworkVariable's current value in the spawn
            // payload and only ever raises OnValueChanged for LATER writes — it never replays — so a
            // client joining a session whose anchor was set minutes ago would otherwise hear nothing
            // at all, which is precisely the late-joiner case this class exists for.
            ApplyToLiveCycles();
        }

        public override void OnNetworkDespawn() => Unsubscribe();

        public override void OnDestroy()
        {
            // Not only in OnNetworkDespawn: these are STATIC events, so a subscription left behind
            // by a destroyed component keeps the component alive and then throws a
            // MissingReferenceException the next time a sun anywhere announces its hour.
            Unsubscribe();

            // Disposes this behaviour's NetworkVariables and deregisters it from its NetworkObject.
            base.OnDestroy();
        }

        private void Unsubscribe()
        {
            anchor.OnValueChanged -= OnAnchorReplicated;
            DayNightCycle.AnchorMoved -= Publish;
            DayNightCycle.CycleAwake -= ApplyTo;
        }

        // ------------------------------------------------------------------ server side

        /// <summary>The first live cycle that has actually stated an hour, published to everyone.</summary>
        private void PublishFromLiveCycle()
        {
            var cycles = DayNightCycle.Live;
            for (int i = 0; i < cycles.Count; i++)
            {
                if (cycles[i] == null || !cycles[i].HasAnchor) continue;

                Publish(cycles[i]);
                return;
            }
        }

        /// <summary>
        /// States <paramref name="cycle"/>'s anchor to the session. Runs on the server for every
        /// announcement, which is rare by construction: an anchor moves when the world opens, when a
        /// save is loaded, and when the clock underneath it changes identity — never per frame.
        /// </summary>
        private void Publish(DayNightCycle cycle)
        {
            if (!IsServer || !IsSpawned || cycle == null) return;

            // An anchor nobody has set is two zeroes, and publishing it would say "midnight" with
            // the full authority of the server.
            if (!cycle.HasAnchor) return;

            cycle.ReadAnchor(out float phase, out double clock);
            anchor.Value = new SkyAnchor { Stated = true, Phase = phase, Clock = clock };
        }

        // ------------------------------------------------------------------ client side

        private void OnAnchorReplicated(SkyAnchor previous, SkyAnchor current) => ApplyToLiveCycles();

        private void ApplyToLiveCycles()
        {
            var cycles = DayNightCycle.Live;

            // Backwards: AdoptAnchor does not enable or disable anything today, but this list is
            // maintained from OnEnable/OnDisable and a forward loop over it is one refactor away
            // from skipping an entry.
            for (int i = cycles.Count - 1; i >= 0; i--)
                ApplyTo(cycles[i]);
        }

        /// <summary>
        /// Hands the replicated anchor to one cycle. The hour goes straight into DayNightCycle and
        /// is not kept here, so there is still exactly one thing in the game that knows the time.
        /// </summary>
        private void ApplyTo(DayNightCycle cycle)
        {
            // The server is where this value came from. Adopting it back would restate the host's
            // own anchor a tick later and, worse, mark the host's world as "an authority has spoken"
            // — the flag DayNightCycle.BeginDay reads to decide whether the authored hour still
            // applies.
            if (IsServer || cycle == null) return;

            // Read from the variable rather than from a change callback's argument: this same
            // method is what a sun waking up later calls, and there is no "new value" in that case
            // — only the one standing.
            SkyAnchor stated = anchor.Value;
            if (!stated.Stated) return;

            cycle.AdoptAnchor(stated.Phase, stated.Clock);
        }
    }
}
