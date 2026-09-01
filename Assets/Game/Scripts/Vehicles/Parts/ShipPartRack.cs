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

        private readonly NetworkVariable<int> networkMask = new(0);

        // Mirrors networkMask, and is the sole source of truth when there is no session at all.
        private int mask;
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
            ApplyToSockets();
        }

        private void OnEnable() => active.Add(this);

        private void OnDisable() => active.Remove(this);

        public override void OnNetworkSpawn()
        {
            spawned = true;
            networkMask.OnValueChanged += OnNetworkMaskChanged;

            if (IsServer) networkMask.Value = mask;
            else SetMaskLocal(networkMask.Value);
        }

        public override void OnNetworkDespawn()
        {
            spawned = false;
            networkMask.OnValueChanged -= OnNetworkMaskChanged;
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

            return !IsInstalled(socketIndex);
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
        public void RestoreMask(int value) => SetMaskAuthoritative(value & FullMask);

        private void SetMaskAuthoritative(int value)
        {
            if (spawned && IsServer)
            {
                // The NetworkVariable callback drives SetMaskLocal on the host too, so the host
                // does not get a second, different path through the same change.
                networkMask.Value = value;
                return;
            }

            SetMaskLocal(value);
        }

        private void OnNetworkMaskChanged(int previous, int current) => SetMaskLocal(current);

        private void SetMaskLocal(int value)
        {
            if (mask == value && applied) return;

            mask = value;
            ApplyToSockets();
            Changed?.Invoke();
        }

        private void ApplyToSockets()
        {
            IReadOnlyList<ShipPartSocket> all = Resolved();

            for (int i = 0; i < all.Count; i++)
                if (all[i] != null)
                    all[i].SetInstalled((mask & (1 << i)) != 0);

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
