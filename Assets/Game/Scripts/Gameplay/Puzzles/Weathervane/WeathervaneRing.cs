// A ring of weathervanes around a wind vent. Crank them all to point up the plateau and the vent
// opens a gust that carries whoever stands in it to the top — and stays open for good.
//
// The server owns the arrangement; every machine is told it and shows it. The wire is the same
// ask/announce shape NetLatch uses, widened from one bit to a packed ring (NetMsg.VaneTurn /
// VaneState), because a vane has four positions rather than two.
using SpaceGame.Core;
using SpaceGame.Diagnostics;
using SpaceGame.Persistence;
using UnityEngine;

namespace SpaceGame.Gameplay.Puzzles
{
    /// <summary>
    /// The weathervane puzzle's state and its replication.
    ///
    /// <para>
    /// <see cref="IPersistentEntity"/> because nothing else about a ring of stone posts qualifies it
    /// for saving, and a solved ring that re-scrambles on load strands whoever saved on the plateau.
    /// Needs a NetworkObject + NetRelay on this GameObject to reach clients; without one it works on
    /// each machine alone, like any unnetworked chunk prop.
    /// </para>
    /// </summary>
    public class WeathervaneRing : MonoBehaviour, IPersistentEntity
    {
        private const int AskVerb = -1;
        private const int InstantVerb = -1;

        [Tooltip("The vanes, in order round the ring. A crank turns its own vane and the next one.")]
        [SerializeField] private WeathervaneVane[] vanes;

        [Tooltip("The gust this ring opens once every vane points the right way.")]
        [SerializeField] private WindUpdraft updraft;

        [Tooltip("How many random cranks, from solved, a fresh ring is scrambled by.")]
        [SerializeField] private int scrambleCranks = 6;

        private WeathervanePositions positions;
        private bool known;
        private Coroutine askRoutine;

        /// <summary>True once this machine knows the arrangement — scrambled, restored or told.</summary>
        public bool IsKnown => known;

        public WeathervanePositions Positions => positions;

        public bool IsSolved => known && positions.IsSolved;

        /// <summary>
        /// Whether a crank will be accepted. Also the crank's CanInteract, so the crosshair and the
        /// server's re-check read the same sentence: a solved ring is locked open for good, and a
        /// client that has not heard the arrangement yet has nothing honest to turn.
        /// </summary>
        public bool CanTurn => known && !positions.IsSolved;

        public int IndexOf(WeathervaneVane vane) => System.Array.IndexOf(vanes, vane);

        private void Awake() => positions = WeathervanePositions.Solved(vanes.Length);

        private void OnEnable()
        {
            this.NetOn(NetMsg.VaneTurn, OnTurnRequested);
            this.NetOn(NetMsg.VaneState, OnStateAnnounced);

            if (NetJoin.ShouldAsk(this))
                askRoutine = StartCoroutine(Fault.Coroutine(this, "WeathervaneRing.Ask",
                    NetJoin.AskWhenSpawned(this, () =>
                        this.NetToServer(NetMsg.VaneTurn, new NetArg { A = AskVerb }))));
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.VaneTurn, OnTurnRequested);
            this.NetOff(NetMsg.VaneState, OnStateAnnounced);
            if (askRoutine != null) StopCoroutine(askRoutine);
            askRoutine = null;
        }

        private void Start()
        {
            // A restore that already landed is the arrangement; otherwise the authority rolls one.
            // Decides, not Simulates: before the NetworkObject is spawned Simulates is true on every
            // machine, and a client that scrambled its own ring would show a puzzle nobody else has.
            if (known || !Network.Decides) return;

            Apply(WeathervanePositions.Scramble(vanes.Length, scrambleCranks, new System.Random()),
                  InstantVerb);
        }

        /// <summary>One press of <paramref name="vane"/>'s crank. Asks; nothing moves here.</summary>
        public void RequestTurn(int vane)
        {
            if (!CanTurn || vane < 0 || vane >= vanes.Length) return;
            this.NetToServer(NetMsg.VaneTurn, new NetArg { A = vane });
        }

        /// <summary>
        /// Restore-only. Called by the save system. Lands instantly and silently — a loaded world
        /// should not swing every vane at the player — and announces it for anyone already here.
        /// </summary>
        public void RestorePositions(int packed)
        {
            Apply(new WeathervanePositions(vanes.Length, packed), InstantVerb);
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
            // vane home on the same frame, and the second must not un-solve the ring.
            if (!CanTurn || arg.A < 0 || arg.A >= vanes.Length) return;

            Apply(positions.Turn(arg.A), arg.A);
            Announce(arg.A);
        }

        private void OnStateAnnounced(in NetArg arg, ulong sender) =>
            Apply(new WeathervanePositions(vanes.Length, arg.A), arg.B);

        private void Announce(int crankedVane) =>
            this.NetToAll(NetMsg.VaneState, new NetArg { A = positions.Packed, B = crankedVane });

        /// <summary>
        /// Every machine: move the vanes and the gust to <paramref name="next"/>. Idempotent — the
        /// host hears its own broadcast back, and a joiner's answer goes to everyone.
        /// </summary>
        private void Apply(WeathervanePositions next, int crankedVane)
        {
            if (known && next.Equals(positions)) return;

            bool instant = crankedVane == InstantVerb || !known;
            positions = next;
            known = true;

            for (int i = 0; i < vanes.Length; i++)
                vanes[i].Show(positions[i], instant, cranked: i == crankedVane);

            updraft.SetOpen(positions.IsSolved, instant);
        }
    }
}
