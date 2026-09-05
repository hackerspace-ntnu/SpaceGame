// Whether this room has been beaten, and the vault that answers for it.
//
// One flag. The vault's state is DERIVED from it rather than saved beside it — two records of one
// fact are two records that can disagree, and the one that disagrees is always the one the player is
// standing in front of.
//
// The cell is deliberately not persisted. It is a consumable that the lava destroys several times a
// minute, so an unsolved room simply spawns a fresh one on load. That sidesteps the runtime-spawned
// entity problem in this project entirely: no prefab id to resolve, nothing to duplicate on reload,
// and nothing to go quietly missing.
using Newtonsoft.Json.Linq;
using SpaceGame.Persistence;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>The Crucible's one piece of durable state.</summary>
    public class CrucibleRoom : NetworkBehaviour, ISaveable
    {
        public const string Key = "crucible";

        [Tooltip("Moved aside when the room is solved.")]
        [SerializeField] private GameObject vaultDoor;

        private readonly NetworkVariable<bool> solved = new(false);

        public bool Solved => solved.Value;

        public override void OnNetworkSpawn()
        {
            solved.OnValueChanged += OnSolvedChanged;
            ShowVault(solved.Value);
        }

        public override void OnNetworkDespawn() => solved.OnValueChanged -= OnSolvedChanged;

        private void OnSolvedChanged(bool was, bool now) => ShowVault(now);

        /// <summary>
        /// Server only, and idempotent — the socket can report the same seating on several frames
        /// before it clears its reference.
        /// </summary>
        public void Solve()
        {
            if (!IsServer || solved.Value) return;

            solved.Value = true;
        }

        private void ShowVault(bool open)
        {
            if (vaultDoor != null) vaultDoor.SetActive(!open);
        }

        // ── Persistence ────────────────────────────────────────────────────────

        public string SaveKey => Key;

        public object CaptureState() => solved.Value ? new JObject { ["solved"] = true } : null;

        /// <summary>
        /// A null state means the room was at its defaults when the save was taken, which is a value
        /// and not an absence: it has to put the room back to unsolved rather than leave whatever
        /// this session happens to be holding.
        /// </summary>
        public void RestoreState(JObject state)
        {
            bool wasSolved = state != null && state.Value<bool>("solved");

            // A client cannot write a server-authoritative NetworkVariable; it will be told. Showing
            // the vault locally regardless keeps a single-machine load correct in the frame before
            // the variable's own change callback would have arrived.
            if (IsServer) solved.Value = wasSolved;

            ShowVault(wasSolved);
        }
    }
}
