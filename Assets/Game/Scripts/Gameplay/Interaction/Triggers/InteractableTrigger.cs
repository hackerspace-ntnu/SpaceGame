using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Drop on a GameObject that also has any <see cref="ITriggerable"/> (SceneTransition,
    /// CutsceneAction, …). On player interact (raycast → E) it forwards the Interactor's
    /// GameObject to the triggerable. One trigger component, any number of action types.
    ///
    /// Deliberately ungated, unlike <see cref="VolumeTrigger"/>, and the difference is worth stating
    /// because the two classes look like the same thing. A volume is a local OBSERVATION: every
    /// player's body exists on every machine, so an untreated volume fires on all of them for all of
    /// them. An interact is a local ACTION: Interactor is fed by PlayerInputManager, which
    /// PlayerController.DisablePlayer switches off on every body this machine does not own, so this
    /// method can only ever run for the player sitting at this keyboard. There is no wrong machine
    /// to gate out, and gating on the server instead would break it outright — a client pressing E
    /// would then do nothing at all.
    ///
    /// What the triggerable does with that press is the triggerable's own business to replicate; see
    /// <see cref="ITriggerable"/>.
    /// </summary>
    [AddComponentMenu("Triggers/Interactable Trigger")]
    public class InteractableTrigger : MonoBehaviour, IInteractable
    {
        [Tooltip("Optional. If unset, the first ITriggerable on this GameObject is used.")]
        [SerializeField] private MonoBehaviour triggerableOverride;

        private ITriggerable cached;

        private void Awake()
        {
            cached = ResolveTriggerable();
        }

        public bool CanInteract()
        {
            var t = cached ?? ResolveTriggerable();
            // Use the player root as a CanInteract probe — the actual initiator is supplied
            // by Interact(). This is a hover-time check, so a missing initiator just means
            // "can in principle"; the real eligibility check happens in Trigger().
            return t != null;
        }

        public void Interact(Interactor interactor)
        {
            if (interactor == null) return;
            var t = cached ?? ResolveTriggerable();
            if (t == null) return;
            if (!t.CanTrigger(interactor.gameObject)) return;
            t.Trigger(interactor.gameObject);
        }

        private ITriggerable ResolveTriggerable()
        {
            if (triggerableOverride is ITriggerable explicitT) return explicitT;
            return GetComponent<ITriggerable>();
        }
    }
}
