// The floor of the room, and the only way to fail in it.
//
// The fail state is gravity, not a buzzer: there is no "you touched a wall" rule anywhere here. Lose
// coordination, go slack, drop below the wall tops, and the cell falls in. That is self-explanatory
// the first time anyone sees it, and it means the tension never lets up, because holding the cell up
// at all is the thing you are doing the entire time.
//
// Alone, it is a floor. The cell can be set down, so the puzzle becomes sequential — park it, walk
// round, re-rig, pull again — and the room turns from a test of nerve into a test of planning. That
// is not a difficulty slider; it is a different game in the same geometry, which is why the swap is
// worth having rather than just locking the room behind a second player.
using SpaceGame.Core;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>The hazard surface under the maze, and the trigger that eats what falls into it.</summary>
    public class CruciblePit : NetworkBehaviour
    {
        [Tooltip("Shown when the pit is lava.")]
        [SerializeField] private GameObject lavaVisuals;

        [Tooltip("Shown when the pit is a plain floor.")]
        [SerializeField] private GameObject floorVisuals;

        [Tooltip("Where a destroyed cell comes back.")]
        [SerializeField] private Transform cradle;

        [Tooltip("The cell this room hands out. One is put in the cradle when the room spawns.")]
        [SerializeField] private GameObject cellPrefab;

        private CrucibleCarrier cell;

        private readonly NetworkVariable<bool> hazard = new(false);

        /// <summary>
        /// Lava needs somebody to coordinate with.
        ///
        /// <para>
        /// One rope cannot hold a cell up — two taut ropes and gravity are what pin it to a point —
        /// so a lava floor for a solo player is not a hard room, it is an impossible one.
        /// </para>
        /// </summary>
        public static bool HazardFor(int players) => players >= 2;

        public bool HazardActive => hazard.Value;

        public override void OnNetworkSpawn()
        {
            hazard.OnValueChanged += OnHazardChanged;
            ShowHazard(hazard.Value);

            if (!IsServer) return;

            NetworkManager.OnClientConnectedCallback += OnRosterChanged;
            NetworkManager.OnClientDisconnectCallback += OnRosterChanged;
            Recount();
            EnsureCell();
        }

        public override void OnNetworkDespawn()
        {
            hazard.OnValueChanged -= OnHazardChanged;

            if (NetworkManager == null) return;

            NetworkManager.OnClientConnectedCallback -= OnRosterChanged;
            NetworkManager.OnClientDisconnectCallback -= OnRosterChanged;
        }

        private void OnHazardChanged(bool was, bool now) => ShowHazard(now);

        // A second player joining floods the room, mid-attempt, with the cell in the air. That is
        // the intended drama rather than an edge case to smooth over: it teaches what this place is
        // in one shot, and it is far better theatre than a sign on the wall.
        private void OnRosterChanged(ulong client) => Recount();

        private void Recount()
        {
            if (!IsServer || NetworkManager == null) return;

            hazard.Value = HazardFor(NetworkManager.ConnectedClientsIds.Count);
        }

        private void ShowHazard(bool active)
        {
            if (lavaVisuals != null) lavaVisuals.SetActive(active);
            if (floorVisuals != null) floorVisuals.SetActive(!active);
        }

        /// <summary>
        /// Put a cell in the cradle. Server only.
        ///
        /// <para>
        /// The cell is deliberately not persisted — the lava eats it several times a minute — so an
        /// unsolved room hands out a fresh one rather than restoring the last one it had.
        /// <see cref="CrucibleCarrier.Recradle"/> reuses that same body from then on, so this
        /// runs once per room.
        /// </para>
        /// </summary>
        private void EnsureCell()
        {
            if (cell != null) return;

            if (cellPrefab == null || cradle == null)
            {
                Debug.LogError("[Crucible] No cell prefab or cradle wired — the room has nothing to " +
                               "carry and cannot be finished.", this);
                return;
            }

            GameObject spawned = GameServices.World.Spawn(cellPrefab, cradle.position, cradle.rotation);
            cell = spawned != null ? spawned.GetComponent<CrucibleCarrier>() : null;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer || !hazard.Value || cradle == null) return;

            var carrier = other.GetComponentInParent<CrucibleCarrier>();
            if (carrier == null) return;

            carrier.Recradle(cradle.position);
        }
    }
}
