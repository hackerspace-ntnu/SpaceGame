using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Persistence;

namespace SpaceGame.Vehicles
{
    /// <summary>
    /// Which of a hull's modules are fitted. One per ship, on the root.
    ///
    /// <para>
    /// The state is a bitmask over <see cref="Sockets"/> held in a <see cref="NetworkVariable{T}"/>
    /// rather than announced in messages, for the reason the backpack replicates its layout as a
    /// list: a message answers the people who were listening, and a joining player was not. A mask
    /// is simply true when they arrive.
    /// </para>
    /// <para>
    /// <see cref="IPersistentEntity"/> because a repaired ship is the whole point of the loop and
    /// a rack has none of the components <c>SaveablePolicy</c> otherwise looks for — without the
    /// marker every module a player fitted would be gone, for everyone, after one load.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ShipPartRack : NetworkBehaviour, IPersistentEntity
    {
        /// <summary>Bits in an int. Eleven sockets today; the guard is for the hull after next.</summary>
        public const int MaxSockets = 31;

        [Tooltip("Every mount point on this hull. Order is the bit order of the saved and " +
                 "replicated mask, so it must not be shuffled once a save exists.")]
        [SerializeField] private ShipPartSocket[] sockets;

        [Tooltip("Which sockets are fitted when this ship is first placed. 0 means it spawns " +
                 "wrecked with every module missing, which is the point of the salvage loop.")]
        [SerializeField] private int authoredInstalledMask;

        [Tooltip("Which sockets hold a burnt-out unit when this ship is first placed. A broken unit " +
                 "occupies its socket without working: it does not count as fitted, and nothing " +
                 "can be fitted until a player takes it off (BrokenShipPart). A bit here is ignored " +
                 "for a socket that is also in the installed mask.")]
        [SerializeField] private int authoredBrokenMask;

        private readonly NetworkVariable<int> networkMask = new(0);
        private readonly NetworkVariable<int> networkBroken = new(0);

        // Mirror networkMask and networkBroken, and are the sole source of truth when there is no
        // session at all.
        private int mask;
        private int broken;
        private bool spawned;
        private bool applied;

        private static readonly List<ShipPartRack> active = new();

        /// <summary>
        /// Every rack currently in the world. A carried module asks this rather than searching the
        /// scene, because it asks once a frame while it is held.
        /// </summary>
        public static IReadOnlyList<ShipPartRack> Active => active;

        /// <summary>Raised on this machine whenever the fitted set changes.</summary>
        public event Action Changed;

        public IReadOnlyList<ShipPartSocket> Sockets => Resolved();

        /// <summary>The mask this ship was authored with — what "unrepaired" means for it.</summary>
        public int AuthoredMask => authoredInstalledMask;

        /// <summary>The fitted set, as a bitmask over <see cref="Sockets"/>.</summary>
        public int InstalledMask => mask;

        public bool IsInstalled(int socketIndex) =>
            socketIndex >= 0 && socketIndex < Resolved().Count && (mask & (1 << socketIndex)) != 0;

        /// <summary>The burnt-out units this ship was authored with.</summary>
        public int AuthoredBrokenMask => authoredBrokenMask;

        /// <summary>The sockets holding a burnt-out unit, as a bitmask over <see cref="Sockets"/>.</summary>
        public int BrokenMask => broken;

        /// <summary>
        /// Does this socket hold a burnt-out unit? It is occupied — nothing fits — but it is not
        /// fitted, so it never counts toward <see cref="IsComplete"/>.
        /// </summary>
        public bool IsBroken(int socketIndex) =>
            socketIndex >= 0 && socketIndex < Resolved().Count && (broken & (1 << socketIndex)) != 0;

        /// <summary>True when every socket on this hull is filled.</summary>
        public bool IsComplete => Resolved().Count > 0 && mask == FullMask;

        private int FullMask
        {
            get
            {
                int count = Mathf.Min(Resolved().Count, MaxSockets);
                return count >= 31 ? int.MaxValue : (1 << count) - 1;
            }
        }

        private void Awake()
        {
            mask = authoredInstalledMask;
            broken = authoredBrokenMask & ~mask;
            ApplyToSockets();
        }

        private void OnEnable() => active.Add(this);

        private void OnDisable() => active.Remove(this);

        public override void OnNetworkSpawn()
        {
            spawned = true;
            networkMask.OnValueChanged += OnNetworkMaskChanged;
            networkBroken.OnValueChanged += OnNetworkBrokenChanged;

            if (IsServer)
            {
                networkMask.Value = mask;
                networkBroken.Value = broken;
                return;
            }

            // A late joiner reads both now: OnValueChanged never replays.
            SetLocal(networkMask.Value, networkBroken.Value);
        }

        public override void OnNetworkDespawn()
        {
            spawned = false;
            networkMask.OnValueChanged -= OnNetworkMaskChanged;
            networkBroken.OnValueChanged -= OnNetworkBrokenChanged;
        }

        /// <summary>
        /// Fit a module. Authority only — <see cref="Network.Simulates"/> rather than
        /// <c>IsServer</c>, so a ship sitting in a chunk with no spawned NetworkObject still works.
        ///
        /// <para>
        /// Idempotent by construction: a socket that is already filled is refused. Host dispatch
        /// re-enters, and a client whose message crossed another player's would otherwise consume
        /// a second module into the same mount.
        /// </para>
        /// </summary>
        /// <returns>True when this call is what filled the socket.</returns>
        public bool TryInstall(int socketIndex, ShipPartKind kind)
        {
            if (!Network.Simulates(this)) return false;
            if (!Accepts(socketIndex, kind)) return false;

            SetMaskAuthoritative(mask | (1 << socketIndex));
            return true;
        }

        /// <summary>
        /// Would <see cref="TryInstall"/> succeed? Asked by the owner before sending, so a shot
        /// into empty air is never billed a module, and by the server before accepting one.
        /// </summary>
        public bool Accepts(int socketIndex, ShipPartKind kind)
        {
            IReadOnlyList<ShipPartSocket> all = Resolved();

            if (socketIndex < 0 || socketIndex >= all.Count) return false;
            if (all[socketIndex] == null || all[socketIndex].Kind != kind) return false;

            return !IsInstalled(socketIndex) && !IsBroken(socketIndex);
        }

        /// <summary>
        /// Take the burnt-out unit out of a socket, freeing it for a working module. Authority
        /// only, idempotent: the second of two players reaching for the same unit is refused.
        /// </summary>
        /// <returns>True when this call is what emptied the socket.</returns>
        public bool TryRemoveBroken(int socketIndex)
        {
            if (!Network.Simulates(this)) return false;
            if (!IsBroken(socketIndex)) return false;

            SetAuthoritative(mask, broken & ~(1 << socketIndex));
            return true;
        }

        /// <summary>Index of a socket in the mask, or -1 when it is not on this rack.</summary>
        public int IndexOf(ShipPartSocket socket)
        {
            IReadOnlyList<ShipPartSocket> all = Resolved();

            for (int i = 0; i < all.Count; i++)
                if (all[i] == socket) return i;

            return -1;
        }

        /// <summary>
        /// Overwrite the whole fitted set. For the save system, which restores a mask wholesale,
        /// and for nothing else — gameplay fits one module at a time through
        /// <see cref="TryInstall"/>.
        /// </summary>
        public void RestoreMask(int value) => RestoreMasks(value, broken);

        /// <summary>
        /// Overwrite both sets at once, for the save system. A socket in both is fitted: a unit
        /// cannot be burnt out in a mount that holds a working one.
        /// </summary>
        public void RestoreMasks(int installed, int brokenUnits)
        {
            int fitted = installed & FullMask;
            SetAuthoritative(fitted, brokenUnits & FullMask & ~fitted);
        }

        private void SetMaskAuthoritative(int value) => SetAuthoritative(value, broken & ~value);

        private void SetAuthoritative(int installed, int brokenUnits)
        {
            if (spawned && IsServer)
            {
                // The NetworkVariable callbacks drive SetLocal on the host too, so the host does
                // not get a second, different path through the same change.
                networkMask.Value = installed;
                networkBroken.Value = brokenUnits;
                return;
            }

            SetLocal(installed, brokenUnits);
        }

        private void OnNetworkMaskChanged(int previous, int current) => SetLocal(current, broken);

        private void OnNetworkBrokenChanged(int previous, int current) => SetLocal(mask, current);

        private void SetLocal(int installed, int brokenUnits)
        {
            if (mask == installed && broken == brokenUnits && applied) return;

            mask = installed;
            broken = brokenUnits;
            ApplyToSockets();
            Changed?.Invoke();
        }

        private void ApplyToSockets()
        {
            IReadOnlyList<ShipPartSocket> all = Resolved();

            for (int i = 0; i < all.Count; i++)
                if (all[i] != null)
                    all[i].SetState((mask & (1 << i)) != 0, (broken & (1 << i)) != 0);

            applied = true;
        }

        /// <summary>
        /// The authored array, or a discovered one when nobody wired it. Discovery is sorted so
        /// two machines cannot number the same hull differently — hierarchy order is stable, but
        /// only as long as nobody reorders children, and the mask outlives that.
        /// </summary>
        private IReadOnlyList<ShipPartSocket> Resolved()
        {
            if (sockets != null && sockets.Length > 0) return sockets;

            var found = new List<ShipPartSocket>(GetComponentsInChildren<ShipPartSocket>(true));
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            sockets = found.ToArray();

            if (sockets.Length > MaxSockets)
            {
                Debug.LogError($"{name}: {sockets.Length} ship part sockets, but the replicated " +
                               $"mask holds {MaxSockets}. The extra sockets can never be filled.", this);
            }

            return sockets;
        }
    }
}
