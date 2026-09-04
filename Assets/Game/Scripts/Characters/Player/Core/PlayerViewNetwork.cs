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
//
//     Yaw-replicates-with-the-body holds only while the body can turn. A SEATED player's is held
//     at a seat pose by somebody else, so the horizontal half of their look is spent on their neck
//     instead (PlayerHeadLook) and travels here beside the pitch. On foot it is zero and nothing
//     downstream changes.
//   • WHETHER THEIR TORCH IS ON. The flashlight used to be a child of that same camera, so a
//     remote player's lamp was not merely un-replicated, it was switched off with its parent —
//     there was no light in the scene to replicate. Since 2026-09-03 the lamp is the head of a
//     WORN GAUNTLET on the forearm (FlashlightGauntletArtifact), which is instantiated on every
//     machine from replicated body-slot state and is never switched off with a camera. The lamp is
//     handed to this component by that gauntlet rather than searched for, and a player wearing no
//     flashlight gauntlet has no torch to replicate at all.
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

        /// <summary>
        /// How far the head is turned off the body's forward, in degrees. Zero for anyone on foot.
        ///
        /// <para>
        /// Beside the pitch rather than folded into it because it answers the same question for the
        /// same reason and has the same late-joiner problem: somebody who connects while four
        /// people are sitting in a cockpit looking at each other must see them looking at each
        /// other, and a message announcing each head turn went out long before they arrived.
        /// </para>
        /// </summary>
        private readonly NetworkVariable<float> netHeadYaw = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<bool> netTorch = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private PlayerController controller;
        private PlayerLook look;
        private PlayerHeadLook headLook;
        private Flashlight torch;

        private Transform aimPivot;
        private float shownPitch;
        private float shownHeadYaw;
        private float publishedPitch;
        private float publishedHeadYaw;
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

        /// <summary>
        /// This player's head pitch on THIS machine — their own live value, or the eased copy of
        /// their last replicated one. Read by <see cref="PlayerHeadLook"/> to pose a remote head.
        /// </summary>
        public float HeadPitch => shownPitch;

        /// <summary>Head yaw off the body's forward, same rules as <see cref="HeadPitch"/>.</summary>
        public float HeadYaw => shownHeadYaw;

        /// <summary>Is this player's torch lit? True on every machine, not just theirs.</summary>
        public bool TorchOn => netTorch.Value;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            look = GetComponent<PlayerLook>();

            // Added here rather than authored on the prefab: this is the component that publishes
            // what the head is doing, so it is the one that must be sure there is something
            // deciding it — on remote copies too, where nothing else on the character is still
            // running, and where re-exporting the model cannot lose it.
            headLook = GetComponent<PlayerHeadLook>();
            if (headLook == null) headLook = gameObject.AddComponent<PlayerHeadLook>();

            aimPivot = new GameObject("AimPivot").transform;
            aimPivot.SetParent(transform, worldPositionStays: false);
        }

        public override void OnNetworkSpawn()
        {
            // Late joiners and newly streamed-in players arrive with the current values already in
            // the variables and no change event coming, so both are read once here rather than
            // waited for. Same rule as PlayerInventoryNetwork.AdoptCurrentState.
            shownPitch = netPitch.Value;
            shownHeadYaw = netHeadYaw.Value;

            // Usually null here: nothing is worn until BodyEquipmentController's adopt pass has
            // run. A gauntlet arriving later brings its own lamp through SetTorch, which applies
            // the current value at that point.
            ApplyTorch(netTorch.Value);
        }

        /// <summary>
        /// Take charge of a lamp somebody put on this body.
        ///
        /// <para>
        /// Called by <see cref="SpaceGame.Items.FlashlightGauntletArtifact"/> as it is worn, on
        /// every machine. Pushed rather than pulled because a worn gauntlet is instantiated and
        /// parented inside one call, and a search of the player for a <see cref="Flashlight"/> run
        /// any earlier than that — in <c>Awake</c>, in <c>OnNetworkSpawn</c> — finds nothing and
        /// never looks again.
        /// </para>
        /// <para>
        /// The new lamp is switched to the replicated value immediately on a peer, so a player
        /// putting a lit torch on is lit for everyone on the frame it appears rather than on the
        /// owner's next publish.
        /// </para>
        /// </summary>
        public void SetTorch(Flashlight lamp)
        {
            torch = lamp;
            if (torch == null) return;

            if (!OwnsThisPlayer()) ApplyTorch(netTorch.Value);
        }

        /// <summary>
        /// Give up a lamp that is about to be destroyed.
        ///
        /// <para>
        /// Ignores a lamp that is not the one held, so an unequip arriving after a swap cannot
        /// unhook the gauntlet that replaced it.
        /// </para>
        /// <para>
        /// The owner does NOT publish false here: <see cref="Publish"/> reads <c>torch != null &amp;&amp;
        /// torch.IsOn</c> every frame and will send it on the next one. Doing it twice is how the
        /// published value and the variable get to disagree.
        /// </para>
        /// </summary>
        public void ClearTorch(Flashlight lamp)
        {
            if (torch == lamp) torch = null;
        }

        // LateUpdate, so the pitch read here is the one PlayerLook wrote in Update this frame
        // rather than last frame's.
        private void LateUpdate()
        {
            if (OwnsThisPlayer())
            {
                // PlayerHeadLook, not PlayerLook, and that is the point of it: a seated player's
                // PlayerLook is switched off for the whole arrival, so its pitch is frozen at
                // whatever they were looking at when they sat down. The head look answers in both
                // modes and is the only thing that knows the seated yaw at all.
                if (headLook != null)
                {
                    shownPitch = headLook.Pitch;
                    shownHeadYaw = headLook.Yaw;
                }
                else if (look != null)
                {
                    shownPitch = look.Pitch;
                }

                Publish();
            }
            else
            {
                float catchUp = 1f - Mathf.Exp(-aimSmoothing * Time.deltaTime);

                shownPitch = Mathf.LerpAngle(shownPitch, netPitch.Value, catchUp);
                shownHeadYaw = Mathf.LerpAngle(shownHeadYaw, netHeadYaw.Value, catchUp);
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

            if (Mathf.Abs(Mathf.DeltaAngle(publishedHeadYaw, shownHeadYaw)) >= publishThreshold)
            {
                publishedHeadYaw = shownHeadYaw;
                netHeadYaw.Value = shownHeadYaw;
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

            torch.Switch(lit);
        }

        private void PoseAimPivot()
        {
            if (aimPivot == null) return;

            // Read every frame rather than cached: the camera's local position is the eye height,
            // and crouching or a rig change is free to move it.
            Transform camera = controller != null ? controller.PlayerCameraTransform : null;
            if (camera != null) aimPivot.localPosition = camera.localPosition;

            // Yaw as well as pitch now. It is zero for anyone on foot — their body is already
            // pointing where they look — so this only changes the answer for a player whose body
            // cannot turn, and for them it is the difference between a weapon that follows their
            // head and one that stares out of the windscreen while they look at the seat beside
            // them.
            aimPivot.localRotation = Quaternion.Euler(shownPitch, shownHeadYaw, 0f);
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
