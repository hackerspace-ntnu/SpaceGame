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
    /// Both routes call <see cref="InteriorManager.ExitInterior"/>, which returns the body to the
    /// exterior position recorded when it entered. A re-arm cooldown stops the exit from
    /// re-firing on the same frame the body is teleported (mirrors VolumeTrigger's logic).
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

        /// <summary>
        /// Who leaving is actually about. A rider is inside on their mount's record, so the mount
        /// is the body that has an exterior position to be returned to — see
        /// <see cref="InteriorManager.ResolveOccupant"/>. Null means "not inside anything", which
        /// is the whole eligibility test: nothing else can be exited.
        /// </summary>
        private static GameObject OccupantFor(GameObject body) =>
            InteriorManager.Instance != null ? InteriorManager.Instance.ResolveOccupant(body) : null;

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

            // "Whoever InteriorManager brought in here" rather than the "Player" tag. A creature or
            // a mount walks through the same entrance a player does, and the tag check left every
            // one of them — and any rider on their back — with no way out.
            TryExit(root);
        }

        private void TryExit(GameObject body)
        {
            if (body == null) return;
            if (Time.time < armedAt) return;
            if (InteriorManager.Instance == null)
            {
                Debug.LogWarning("[CaveExitCover] No InteriorManager — cannot exit cave.", this);
                return;
            }

            GameObject occupant = OccupantFor(body);
            if (occupant == null) return;   // not inside an interior — nothing to return them to

            armedAt = Time.time + Mathf.Max(0.1f, rearmCooldown);
            InteriorManager.Instance.ExitInterior(occupant);
        }
    }
}
