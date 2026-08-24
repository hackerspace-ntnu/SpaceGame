// What a player's head is doing, published to everyone else.
//
// Two things about a player were visible only on their own machine, and both for the same reason:
// they live on the camera, and PlayerController.DisablePlayer switches the whole camera GameObject
// off on every remote copy.
//
//   • WHERE THEY ARE LOOKING. Yaw turns the body's Rigidbody and therefore replicates with the
//     transform, but pitch is a private float on PlayerLook that is spent on a child camera. So a
//     player aiming straight up at something appeared, to everyone else, to be staring at the
//     horizon — and the gun in their hand stayed flat, because Weapon.UpdateWeaponRotation had
//     nothing to aim a remote copy with and deliberately left it on the hand bone.
//   • WHETHER THEIR TORCH IS ON. The flashlight is a child of that same camera, so a remote
//     player's lamp was not merely un-replicated, it was switched off with its parent — there was
//     no light in the scene to replicate.
//
// ── Why NetworkVariables and not messages ──
// Both are STATE that a late joiner has to see, not events. Somebody who joins while a player is
// aiming down a shaft with their torch lit must see exactly that, and a message announcing the
// toggle was sent long before they connected. NetworkVariable is the mechanism for that; a
// NetMessaging pair is not, and would need a "tell me the current state" round trip of its own.
//
// Owner-write, because both values are facts about a body that is already owner-authoritative —
// the same authority its NetworkTransform runs under. The server is not a better source for either.
//
// ── The pivot ──
// The replicated pitch has to become a transform before anything can hang off it. AimPivot is a
// runtime child of the player that carries the camera's local position and the pitch — so on every
// machine it is the pose the owner's camera has, whether or not that camera is switched on. On the
// owner it is fed live from PlayerLook, because their own aim is available this frame and is better
// than anything that could arrive over a wire.
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Characters
{
    [DisallowMultipleComponent]
    public class PlayerViewNetwork : NetworkBehaviour
    {
        [Header("Aim")]
        [Tooltip("How fast a remote copy's aim catches up to each replicated value. Higher is " +
                 "snappier and more jittery. Cosmetic only — nothing gameplay-side reads this.")]
        [SerializeField] private float aimSmoothing = 18f;

        [Tooltip("Degrees of pitch change worth sending. A player holding still should cost " +
                 "nothing at all, and a fraction of a degree is not visible on a remote body.")]
        [SerializeField] private float publishThreshold = 0.25f;

        // Everyone/Owner: the owner is the only machine that knows either answer, and every other
        // machine needs both. Written only while spawned — see Publish.
        private readonly NetworkVariable<float> netPitch = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<bool> netTorch = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>
        /// Is this player aiming? Same argument as the other two: a late joiner has to see a
        /// player who is already holding their weapon up, and the message announcing the press
        /// went out long before they connected.
        /// </summary>
        private readonly NetworkVariable<bool> netAiming = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private PlayerController controller;
        private PlayerLook look;
        private Flashlight torch;

        private Transform aimPivot;
        private float shownPitch;
        private float publishedPitch;
        private bool publishedTorch;

        /// <summary>
        /// Where this player is looking, on any machine: their body's yaw plus their pitch.
        ///
        /// <para>
        /// Always present and always safe to read — on the owner it is this frame's aim, on
        /// everyone else the last replicated one, eased. Deliberately NOT wired into
        /// <see cref="AimProvider"/>: an item's aim must still travel in its use message rather
        /// than be recomputed per machine, because a smoothed copy of an aim is not the aim the
        /// shot was taken with. This is for things that only have to LOOK right.
        /// </para>
        /// </summary>
        public Transform AimPivot => aimPivot;

        /// <summary>Is this player's torch lit? True on every machine, not just theirs.</summary>
        public bool TorchOn => netTorch.Value;

        /// <summary>Is this player aiming? True on every machine, not just theirs.</summary>
        public bool Aiming => netAiming.Value;

        /// <summary>
        /// Owner-only. Called by <see cref="PlayerAimRig"/> once its own decision has been made.
        ///
        /// <para>
        /// Pushed rather than pulled because the rig is the thing that knows, and a pull would
        /// mean this component reaching for a component that may not be on every character.
        /// Guarded on IsSpawned for the same reason <see cref="Publish"/> is: writing a
        /// NetworkVariable before Netcode has spawned this object throws.
        /// </para>
        /// </summary>
        public void PublishAiming(bool aiming)
        {
            if (!IsSpawned || !IsOwner) return;
            if (netAiming.Value == aiming) return;
            netAiming.Value = aiming;
        }

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            look = GetComponent<PlayerLook>();

            // Included-inactive, because on a remote copy the camera this hangs under has already
            // been switched off by PlayerController.Awake.
            torch = GetComponentInChildren<Flashlight>(true);

            aimPivot = new GameObject("AimPivot").transform;
            aimPivot.SetParent(transform, worldPositionStays: false);
        }

        public override void OnNetworkSpawn()
        {
            // Late joiners and newly streamed-in players arrive with the current values already in
            // the variables and no change event coming, so both are read once here rather than
            // waited for. Same rule as PlayerInventoryNetwork.AdoptCurrentState.
            shownPitch = netPitch.Value;

            // Before the torch is applied, not after. Reparenting a lamp out of the switched-off
            // camera ACTIVATES it, which runs Flashlight.Awake — and Awake switches the light off.
            // Lighting it first would be undone a line later.
            if (!IsOwner) AdoptTorchForRemoteView();

            ApplyTorch(netTorch.Value);
        }

        /// <summary>
        /// Move the lamp somewhere it can actually be seen.
        ///
        /// <para>
        /// The flashlight is authored as a child of the Main Camera, and a remote player's camera
        /// GameObject is switched off wholesale — so there is nothing to light up. Rather than
        /// duplicating the lamp (two objects, two sets of tuning, one of them silently drifting
        /// from the other), the shipped one is moved onto the pivot, which is always active and
        /// carries the same pose the camera would have.
        /// </para>
        /// <para>
        /// Remote copies only. The owner's lamp is left exactly where it was authored, because for
        /// them it already works and the pivot would be a change with nothing to gain.
        /// </para>
        /// </summary>
        private void AdoptTorchForRemoteView()
        {
            if (torch == null) return;

            // worldPositionStays: false keeps the authored local offset — the lamp sits slightly
            // right of and below the eye, and that offset is expressed in camera space, which is
            // exactly what the pivot reproduces.
            torch.transform.SetParent(aimPivot, worldPositionStays: false);
        }

        // LateUpdate, so the pitch read here is the one PlayerLook wrote in Update this frame
        // rather than last frame's.
        private void LateUpdate()
        {
            if (OwnsThisPlayer())
            {
                shownPitch = look != null ? look.Pitch : shownPitch;
                Publish();
            }
            else
            {
                shownPitch = Mathf.LerpAngle(shownPitch, netPitch.Value,
                                             1f - Mathf.Exp(-aimSmoothing * Time.deltaTime));
                ApplyTorch(netTorch.Value);
            }

            PoseAimPivot();
        }

        /// <summary>
        /// Owner side: send the two values, and only when they have moved.
        ///
        /// Guarded on IsSpawned as well as ownership: this component exists on a player opened
        /// straight from the editor and on one in the frames before Netcode spawns it, and writing
        /// a NetworkVariable in either state throws.
        /// </summary>
        private void Publish()
        {
            if (!IsSpawned || !IsOwner) return;

            if (Mathf.Abs(Mathf.DeltaAngle(publishedPitch, shownPitch)) >= publishThreshold)
            {
                publishedPitch = shownPitch;
                netPitch.Value = shownPitch;
            }

            bool lit = torch != null && torch.IsOn;
            if (lit != publishedTorch)
            {
                publishedTorch = lit;
                netTorch.Value = lit;
            }
        }

        /// <summary>Remote side: make our copy of the lamp agree with its owner. Idempotent.</summary>
        private void ApplyTorch(bool lit)
        {
            if (torch == null || torch.IsOn == lit) return;

            torch.RestoreOn(lit);
        }

        private void PoseAimPivot()
        {
            if (aimPivot == null) return;

            // Read every frame rather than cached: the camera's local position is the eye height,
            // and crouching or a rig change is free to move it.
            Transform camera = controller != null ? controller.PlayerCameraTransform : null;
            if (camera != null) aimPivot.localPosition = camera.localPosition;

            aimPivot.localRotation = Quaternion.Euler(shownPitch, 0f, 0f);
        }

        /// <summary>
        /// True when the local player is this player. Offline — a scene opened in the editor, a
        /// test — everything is ours, which is the same answer every other gate in the project
        /// gives there.
        /// </summary>
        private bool OwnsThisPlayer()
        {
            if (!Network.IsNetworked) return true;

            return !IsSpawned || IsOwner;
        }
    }
}
