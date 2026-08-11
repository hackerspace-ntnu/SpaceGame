using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.World
{
    /// <summary>
    /// The dark slab that caps a procedural cave's entrance passage. Doubles as the cave exit:
    ///   • <see cref="IInteractable"/> — the player can click/interact with the slab to leave,
    ///   • walk-in volume — stepping into the slab's trigger collider also exits.
    ///
    /// Both routes call <see cref="InteriorManager.ExitInterior"/>, which returns the player to the
    /// exterior position recorded when they entered. A re-arm cooldown stops the exit from
    /// re-firing on the same frame the player is teleported (mirrors VolumeTrigger's logic).
    ///
    /// Spawned and configured by <see cref="CaveSpawner"/>. The collider is the slab's own box,
    /// set to <c>isTrigger</c> so it never physically blocks the player.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CaveExitCover : MonoBehaviour, IInteractable
    {
        [Tooltip("Seconds before the walk-in volume can fire again after a successful exit.")]
        [SerializeField] private float rearmCooldown = 1.5f;

        [Tooltip("Also exit when the player walks into the slab volume (not only on click).")]
        [SerializeField] private bool exitOnWalkIn = true;

        private float armedAt;

        private void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null && !col.isTrigger) col.isTrigger = true;
        }

        // ---- IInteractable (click to exit) --------------------------------------

        public bool CanInteract() => InteriorManager.Instance != null && Time.time >= armedAt;

        public void Interact(Interactor interactor)
        {
            if (interactor == null) return;
            TryExit(interactor.gameObject);
        }

        // ---- Walk-in volume (step into the slab to exit) ------------------------

        private void OnTriggerEnter(Collider other) => TryWalkInExit(other);

        // Poll while overlapping too — InteriorManager teleports the player and an instantaneous
        // teleport does not raise OnTriggerEnter. The cooldown + InteriorManager's own return-info
        // guard prevent an immediate re-fire.
        private void OnTriggerStay(Collider other) => TryWalkInExit(other);

        private void TryWalkInExit(Collider other)
        {
            if (!exitOnWalkIn || other == null) return;
            GameObject root = other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : other.gameObject;
            if (!root.CompareTag("Player")) return;
            TryExit(root);
        }

        private void TryExit(GameObject player)
        {
            if (player == null) return;
            if (Time.time < armedAt) return;
            if (InteriorManager.Instance == null)
            {
                Debug.LogWarning("[CaveExitCover] No InteriorManager — cannot exit cave.", this);
                return;
            }
            armedAt = Time.time + Mathf.Max(0.1f, rearmCooldown);
            InteriorManager.Instance.ExitInterior(player);
        }
    }
}
