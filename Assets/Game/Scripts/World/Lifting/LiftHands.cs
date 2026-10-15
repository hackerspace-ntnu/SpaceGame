// The carrier's own machine, while its player has a load in both hands: whatever was in them is put away, interact anywhere
// or Esc lets go, drawing an item lets go, dying lets go, and the walk is slowed — stopped while the take-hold or the let-go
// plays. The same for every ILiftLoad, so a prop and a captive are let go of by the same keys.
//
// A plain class owned by the load, because the load is what knows whether it is still in this machine's hands (its own
// replicated state) and how it is put down (its own request to the server).
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Characters;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Presentation;
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.World
{
    public sealed class LiftHands
    {
        private readonly ILiftLoad load;
        private readonly Func<bool> heldHere;
        private readonly Action putDown;
        private readonly Action interact;
        private readonly Func<float> speedFraction;

        private PlayerMovement movement;
        private PlayerController controller;
        private PlayerInputManager inputs;
        private Interactor interactor;
        private IPlayerInventory inventory;
        private int grantedFrame = -1;

        /// <param name="load">The load in the hands.</param>
        /// <param name="heldHere">Is the load in THIS machine's player's hands, by the load's replicated state.</param>
        /// <param name="putDown">Let go, from Esc, death or a drawn item.</param>
        /// <param name="interact">An interact press anywhere — the load decides whether that lets go.</param>
        /// <param name="speedFraction">The carrier's walk while holding, as a fraction of their ordinary walk.</param>
        public LiftHands(ILiftLoad load, Func<bool> heldHere, Action putDown, Action interact, Func<float> speedFraction)
        {
            this.load = load;
            this.heldHere = heldHere;
            this.putDown = putDown;
            this.interact = interact;
            this.speedFraction = speedFraction;
        }

        /// <summary>When the take-hold or the let-go clip ends; the body stands still until then.</summary>
        public float SettleEndsAt { get; set; } = -1f;

        /// <summary>Whether a player's hands are taken by this load on this machine.</summary>
        public bool IsTaken => controller != null;

        /// <summary>The carrier's own player: hands emptied, input heard, the walk slowed. Given back by <see cref="Tick"/>.</summary>
        public void Take(GameObject who)
        {
            Release();
            grantedFrame = Time.frameCount;
            movement = who.GetComponent<PlayerMovement>();
            controller = who.GetComponent<PlayerController>();
            if (controller == null) return;

            inventory = controller.PlayerInventory;
            if (inventory != null)
            {
                // Both hands are on the load: whatever was in them goes away, and drawing something puts the load down.
                if (inventory.SelectedSlotIndex >= 0) inventory.SelectSlot(inventory.SelectedSlotIndex);
                inventory.OnSlotSelected += OnSlotSelected;
            }

            inputs = controller.Input;
            if (inputs != null) inputs.OnInteractPressed += OnInteractPressed;
        }

        /// <summary>Give the player their hands, input and walk back. Safe twice.</summary>
        public void Release()
        {
            if (inventory != null) inventory.OnSlotSelected -= OnSlotSelected;
            if (inputs != null) inputs.OnInteractPressed -= OnInteractPressed;
            inventory = null;
            inputs = null;
            interactor = null;
            if (movement != null) movement.StopHauling();
            movement = null;
            controller = null;
        }

        /// <summary>Every frame on every machine: the pace while held, Esc and death. Lets go of the hands once the load is out of them.</summary>
        public void Tick()
        {
            if (movement == null && controller == null) return;

            bool holding = heldHere();
            bool settling = Time.time < SettleEndsAt;

            if (!holding && !settling)
            {
                Release();
                return;
            }

            if (movement != null) movement.StartHauling(settling ? 0f : movement.WalkSpeed * speedFraction());
            if (!holding || controller == null) return;

            if (controller.IsDead)
            {
                putDown();
                return;
            }

            // Gated on the shared menu scope, as the seat's Esc is: the chat box and the settings fields use Esc for "never
            // mind", and closing one of those must not drop the load as a side effect.
            Keyboard keys = Keyboard.current;
            if (keys != null && PutsDown(keys.escapeKey.wasPressedThisFrame, false, GameplayMenuScope.AcceptsGameplayInput, false))
                putDown();
        }

        // Interact anywhere lets go, not only with the crosshair on the load. Not on the frame the hold was granted: offline
        // the grant lands inside the very press that asked for it.
        private void OnInteractPressed()
        {
            if (AimsAtDock()) return;
            if (PutsDown(false, true, true, Time.frameCount == grantedFrame)) interact();
        }

        // The same press at a dock that takes this load means "set it in", which the dock asks for itself.
        private bool AimsAtDock()
        {
            if (interactor == null && controller != null) interactor = controller.GetComponentInChildren<Interactor>(true);
            return interactor != null && interactor.HoveredInteractable is LiftDock dock && dock.Takes(load);
        }

        private void OnSlotSelected(InventorySlot slot)
        {
            if (slot != null && !slot.IsEmpty && heldHere()) putDown();
        }

        /// <summary>
        /// Does this frame's input put a held load down? Esc does — unless a menu, the chat box or a settings field has it,
        /// where Esc means "never mind" — and interact does, except on the frame the hold was granted (offline that is the very
        /// press that asked for it).
        /// </summary>
        public static bool PutsDown(bool escape, bool interact, bool gameplayInput, bool grantedThisFrame) =>
            (escape && gameplayInput) || (interact && !grantedThisFrame);

        /// <summary>
        /// May <paramref name="who"/> take hold of something with both hands: alive, not seated, not pushing a cart, not
        /// already hauling something else (a load, a captive on a lasso), and with the arms a hold needs.
        /// <paramref name="alreadyHoldingThis"/>: the haul the body is slowed by is this hold's own, or one it is giving up
        /// for it, so it does not count.
        /// </summary>
        public static bool MayTakeHold(GameObject who, bool alreadyHoldingThis)
        {
            if (who == null) return false;
            if (who.TryGetComponent(out PlayerController body) && body.IsDead) return false;
            if (who.TryGetComponent(out PlayerSeating seating) && seating.IsSeated) return false;
            if (who.TryGetComponent(out PlayerPushing pushing) && pushing.IsPushing) return false;
            if (who.TryGetComponent(out PlayerMovement walk) && walk.IsHauling && !alreadyHoldingThis) return false;
            return LiftCarrier.On(who).CanReach;
        }

        /// <summary>Plays <paramref name="action"/> on <paramref name="who"/> where this machine may, and answers how long it lasts.</summary>
        public static float Play(GameObject who, CharacterAction action, float fallbackSeconds)
        {
            if (who == null) return fallbackSeconds;

            CharacterActions actions = who.GetComponentInChildren<CharacterActions>(true);
            if (action != null && actions != null) actions.Play(action);
            return SecondsOf(action, who, fallbackSeconds);
        }

        /// <summary>How long <paramref name="action"/> lasts on <paramref name="who"/>, at that body's own speed for it.</summary>
        public static float SecondsOf(CharacterAction action, GameObject who, float fallbackSeconds)
        {
            if (action == null || who == null) return fallbackSeconds;

            CharacterActions actions = who.GetComponentInChildren<CharacterActions>(true);
            float speed = actions != null ? actions.PlaybackSpeed(action) : 1f;
            return action.Seconds(0, speed);
        }
    }
}
