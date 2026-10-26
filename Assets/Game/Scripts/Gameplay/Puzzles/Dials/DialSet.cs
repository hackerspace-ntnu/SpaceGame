// A set of cranked dials whose arrangement is one small number of shared world state: the
// weathervane ring, the sun shrine's mirrors. The server owns the arrangement; every machine is
// told it and shows it.
//
// Lifted out of WeathervaneRing when the mirrors became the second puzzle on the same protocol. The
// wire is the ask/announce shape NetLatch uses, widened from one bit to a packed arrangement
// (NetMsg.DialTurn / DialState). What a crank DOES to the arrangement, what a fresh one looks like
// and how it is drawn are the subclass's; everything about agreeing on it is here.
using SpaceGame.Core;
using SpaceGame.Diagnostics;
using SpaceGame.Persistence;
using UnityEngine;

namespace SpaceGame.Gameplay.Puzzles
{
    /// <summary>
    /// A puzzle's dial arrangement and its replication.
    ///
    /// <para>
    /// <see cref="IPersistentEntity"/> because nothing else about a set of stone posts qualifies it
    /// for saving, and a puzzle that re-rolls on load hands the players a different one from the one
    /// they were halfway through. Needs a NetworkObject + NetRelay on this GameObject to reach
    /// clients; without one it works on each machine alone, like any unnetworked chunk prop.
    /// </para>
    /// </summary>
    public abstract class DialSet : MonoBehaviour, IPersistentEntity
    {
        private const int AskVerb = -1;

        /// <summary>"No crank was turned": a restore, a joiner's answer, the authority's first roll.</summary>
        protected const int InstantVerb = -1;

        private int packed;
        private bool known;
        private Coroutine askRoutine;

        /// <summary>True once this machine knows the arrangement — rolled, restored or told.</summary>
        public bool IsKnown => known;

        /// <summary>The whole arrangement as one number. Meaningful only while <see cref="IsKnown"/>.</summary>
        public int Packed => packed;

        /// <summary>
        /// Whether a crank will be accepted. Also every crank's CanInteract, so the crosshair and the
        /// server's re-check read the same sentence. A client that has not heard the arrangement yet
        /// has nothing honest to turn.
        /// </summary>
        public virtual bool CanTurn => known;

        /// <summary>How many dials this set has; a crank's index must be below it.</summary>
        protected abstract int DialCount { get; }

        /// <summary>The arrangement after one turn of <paramref name="dial"/>'s crank.</summary>
        protected abstract int Turn(int arrangement, int dial);

        /// <summary>The arrangement the authority opens a fresh world with.</summary>
        protected abstract int Fresh(System.Random random);

        /// <summary>
        /// Every machine: draw <paramref name="arrangement"/>. <paramref name="turnedDial"/> is the
        /// crank that caused it, or <see cref="InstantVerb"/>; <paramref name="instant"/> means the
        /// state was true before this machine was looking, so land in it silently.
        /// </summary>
        protected abstract void Show(int arrangement, int turnedDial, bool instant);

        /// <summary>What the crosshair says about one dial. Empty for nothing worth saying.</summary>
        public virtual string Describe(int dial) => string.Empty;

        /// <summary>A 0–1 bar for the crosshair while a crank is aimed at, or null for none.</summary>
        public virtual float? Progress => null;

        private void OnEnable()
        {
            this.NetOn(NetMsg.DialTurn, OnTurnRequested);
            this.NetOn(NetMsg.DialState, OnStateAnnounced);

            if (Network.IsNetworked && !Network.Server)
                askRoutine = StartCoroutine(Fault.Coroutine(this, $"{GetType().Name}.Ask",
                    this.NetToServerWhenSpawned(NetMsg.DialTurn, new NetArg { A = AskVerb })));
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.DialTurn, OnTurnRequested);
            this.NetOff(NetMsg.DialState, OnStateAnnounced);
            if (askRoutine != null) StopCoroutine(askRoutine);
            askRoutine = null;
        }

        protected virtual void Start()
        {
            // A restore that already landed is the arrangement; otherwise the authority rolls one.
            // Decides, not Simulates: before the NetworkObject is spawned Simulates is true on every
            // machine, and a client that rolled its own would show a puzzle nobody else has.
            if (known || !Network.Decides) return;

            Apply(Fresh(new System.Random()), InstantVerb);
        }

        /// <summary>One press of <paramref name="dial"/>'s crank. Asks; nothing moves here.</summary>
        public void RequestTurn(int dial)
        {
            if (!CanTurn || dial < 0 || dial >= DialCount) return;
            this.NetToServer(NetMsg.DialTurn, new NetArg { A = dial });
        }

        /// <summary>
        /// Restore-only. Called by the save system. Lands instantly and silently — a loaded world
        /// should not swing every dial at the player — and announces it for anyone already here.
        /// </summary>
        public void Restore(int arrangement)
        {
            Apply(arrangement, InstantVerb);
            if (Network.Simulates(this)) Announce(InstantVerb);
        }

        private void OnTurnRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;

            if (arg.A == AskVerb)
            {
                if (known) Announce(InstantVerb);
                return;
            }

            // Re-checked here rather than trusted from the sender: two players can crank the last
            // dial home on the same frame, and the second must not undo the first.
            if (!CanTurn || arg.A < 0 || arg.A >= DialCount) return;

            Apply(Turn(packed, arg.A), arg.A);
            Announce(arg.A);
        }

        private void OnStateAnnounced(in NetArg arg, ulong sender) => Apply(arg.A, arg.B);

        private void Announce(int turnedDial) =>
            this.NetToAll(NetMsg.DialState, new NetArg { A = packed, B = turnedDial });

        /// <summary>
        /// Every machine: move to <paramref name="next"/>. Idempotent — the host hears its own
        /// broadcast back, and a joiner's answer goes to everyone.
        /// </summary>
        private void Apply(int next, int turnedDial)
        {
            if (known && next == packed) return;

            bool instant = turnedDial == InstantVerb || !known;
            packed = next;
            known = true;

            Show(packed, turnedDial, instant);
        }
    }
}
