// A map projector every machine in the session agrees about — where it can.
//
// Pressing E powers the emitter and the terrain hologram rises off it; pressing again shuts it
// down. The on/off bit rides a NetLatch, the same protocol as doors and levers: placed under a
// NetworkObject (a ship interior) the state replicates, and as a plain chunk prop the latch
// collapses to a local dispatch — the switch is then per-machine, which is coherent here because
// the hologram's CONTENT is per-viewer anyway (fog of war and discovery are the local player's).
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Powers a fixed <see cref="MapHologramTerrain"/> on and off from a press.
    ///
    /// <para>
    /// <see cref="IPersistentEntity"/> for the same reason a door is: a projector has none of the
    /// components <c>SaveablePolicy.NeedsSaving</c> otherwise looks for, so without the marker it
    /// would never get a <c>SaveableEntity</c> and <c>ProjectorSaveable</c> would never run.
    /// Whether it was left running is world state a player changed.
    /// </para>
    /// </summary>
    public class HoloProjectorInteraction : MonoBehaviour, IInteractable, ILatchHost, IPersistentEntity
    {
        [Tooltip("The hologram this switch powers. Must live on this prefab with its projectorAnchor assigned, or the map appears beside the player's face instead of over the emitter.")]
        [SerializeField] private MapHologramTerrain hologram;

        private NetLatch latch;

        /// <summary>Whether the emitter is running — the latch's state, i.e. the session's answer.</summary>
        public bool IsPowered => latch != null && latch.IsOn;

        /// <summary>One projector, one latch. See ILatchHost for why this answers before Awake.</summary>
        public int LatchCount => 1;

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay. Goes through the
        /// latch rather than the hologram so the restored state is the one the session reads.
        /// </summary>
        public void RestorePowered(bool on) => latch?.Restore(on);

        private void Awake()
        {
            latch = new NetLatch(this, ApplyPower);
        }

        private void OnEnable() => latch?.Enable();

        private void OnDisable() => latch?.Disable();

        /// <summary>
        /// Delegated to the latch so the crosshair, the key and the server's re-check give one
        /// answer. Also refuses while unwired — a prompt on a projector that cannot project is a
        /// prompt that lies.
        /// </summary>
        public bool CanInteract() => hologram != null && latch != null && latch.Accepts(latch.Next);

        public void Interact(Interactor interactor)
        {
            if (!CanInteract()) return;

            latch.Toggle();
        }

        /// <summary>
        /// Called on every machine by the latch. "Instant" (a late joiner, a load) still plays the
        /// hologram's own rise-in — there is no landed pose for a volumetric to snap to, and a
        /// third of a second of rise reads as the projector spinning up, not as a glitch.
        /// </summary>
        private void ApplyPower(bool on, bool instant)
        {
            if (hologram != null) hologram.SetVisible(on);
        }
    }
}
