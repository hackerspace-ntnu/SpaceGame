using System;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;

namespace SpaceGame.Vehicles
{
    /// <summary>
    /// A player planted at a station that borrows their movement keys: a wheel, a capstan. Their own movement is
    /// switched off, their feet are held where they stood relative to an anchor, and a press of interact anywhere
    /// lets go. They keep their body, their camera and their look.
    ///
    /// <para>
    /// Only ever begun on the machine that owns the player: it switches a body's controls off and on again, and a
    /// machine that does that to somebody else's body hands their movement to the wrong side of the wire.
    /// <c>DuneFoilHelm</c> does the same by hand and predates this.
    /// </para>
    /// </summary>
    public sealed class StationFooting
    {
        private Rigidbody body;
        private PlayerInputManager input;
        private PlayerMovement movement;
        private bool restoreMovement;
        private Transform anchor;
        private Vector3 offset;
        private Action letGo;
        private int beganFrame = -1;

        /// <summary>Somebody is planted here, on this machine.</summary>
        public bool Held { get; private set; }

        /// <summary>The planted player's movement keys, x across and y along; zero with nobody planted.</summary>
        public Vector2 Keys => Held && input != null ? input.MoveInput : Vector2.zero;

        /// <summary>Whether the planted player still has Interact down; false with nobody planted.</summary>
        public bool InteractHeld => Held && input != null && input.InteractHeld;

        /// <summary>Whether the planted player still has Use down; false with nobody planted.</summary>
        public bool UseHeld => Held && input != null && input.UseHeld;

        /// <summary>
        /// Plant <paramref name="player"/> where they stand, no further than <paramref name="reach"/> from
        /// <paramref name="standBy"/>. False for a body with no player controls: nothing was taken.
        /// <paramref name="onLetGo"/> may be null for a station that is let go of some other way.
        /// </summary>
        public bool Begin(GameObject player, Transform standBy, float reach, Action onLetGo)
        {
            PlayerController controller = player != null ? player.GetComponentInChildren<PlayerController>(true) : null;
            if (controller == null) return false;

            body = player.GetComponent<Rigidbody>();
            input = controller.Input;
            movement = controller.GetComponent<PlayerMovement>();
            restoreMovement = movement != null && movement.enabled;
            if (restoreMovement) movement.enabled = false;

            // Where they stand, not one exact spot. Only the reach across is clamped: the height is how far their
            // pivot sits above their own feet.
            anchor = standBy;
            Vector3 local = anchor.InverseTransformPoint(player.transform.position);
            Vector3 flat = Vector3.ClampMagnitude(new Vector3(local.x, 0f, local.z), reach);
            offset = new Vector3(flat.x, local.y, flat.z);

            letGo = onLetGo;
            beganFrame = Time.frameCount;
            if (input != null) input.OnInteractPressed += OnInteract;

            Held = true;
            return true;
        }

        /// <summary>Hold them in place. Call from LateUpdate, after whatever moves the anchor.</summary>
        public void Hold()
        {
            if (!Held || body == null) return;

            Vector3 target = anchor.TransformPoint(offset);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = target;
            body.transform.position = target;

            if (movement != null) movement.ForceIdleAnimation();
        }

        /// <summary>Give them their legs back. Safe when nobody is planted, and when the player is already gone.</summary>
        public void End()
        {
            if (input != null) input.OnInteractPressed -= OnInteract;
            if (restoreMovement && movement != null) movement.enabled = true;

            body = null;
            input = null;
            movement = null;
            restoreMovement = false;
            letGo = null;
            Held = false;
        }

        // The press that planted them must not also let go: it is raised on the frame the claim landed.
        private void OnInteract()
        {
            if (Time.frameCount != beganFrame) letGo?.Invoke();
        }
    }
}
