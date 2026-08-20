// The wing pack: a folded ornithopter carried in the inventory. Using it in mid-air spawns the
// craft, straps the player into the prone cradle and hands them the controls.
//
// The pack is a launcher, not the aircraft. Everything about flying lives on the spawned prefab
// (OrnithopterFlightMotor + MountModule + SteerModule); this class owns the moment of transition
// and the teardown, which is the part that has three ways to fire and has to survive all of them.
using SpaceGame.Vehicles.Ornithopter;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    public class WingPackItem : UsableItem
    {
        [Header("Craft")]
        [Tooltip("The ornithopter prefab spawned and mounted when the pack is used.")]
        [SerializeField] private GameObject ornithopterPrefab;

        [Header("Launch window")]
        [Tooltip("The pack is air-only. Ground within this distance straight down counts as standing " +
                 "on it, and the pack refuses.")]
        [SerializeField, Min(0.1f)] private float groundClearance = 0.6f;

        [Tooltip("How far below to look before a drop counts as big enough to launch into.")]
        [SerializeField, Min(1f)] private float minLaunchClearance = 6f;

        [Tooltip("How far ahead of the player to probe for that drop. This is what makes standing on " +
                 "a cliff EDGE work: the ray straight down hits the ledge the player is standing on, " +
                 "so the one that matters is cast out over the drop they are looking at.")]
        [SerializeField, Min(0f)] private float ledgeProbeForward = 1.5f;

        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Launch")]
        [Tooltip("How much of the player's speed at the moment of use carries into the launch. A run " +
                 "and a jump should be worth something.")]
        [SerializeField, Range(0f, 1f)] private float speedCarry = 1f;

        [Tooltip("Metres above the player to place the craft, so the wings do not open through the " +
                 "ledge that was just stepped off.")]
        [SerializeField] private float launchLift = 1.2f;

        // Live flight. All three teardown paths funnel through ReleaseCraft.
        private GameObject craft;
        private MountModule craftMount;
        private OrnithopterFlightMotor craftMotor;
        private Renderer[] heldRenderers;

        protected override bool CanUse()
        {
            if (!base.CanUse())
                return false;

            if (ornithopterPrefab == null)
            {
                Debug.LogError("WingPackItem: no ornithopter prefab assigned.", this);
                return false;
            }

            if (craft != null)
                return false;               // already flying

            if (owner == null)
                return false;

            if (!HasLaunchRoom(owner.transform))
            {
                // Deliberately not silent. "Nothing happened" is indistinguishable from a broken item,
                // and this is the one item in the game that refuses on the ground by design.
                Debug.Log("WingPack: need air under you — jump, or step off something.", this);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Air-only, in the two senses that matter: already falling, or standing at the top of a drop
        /// worth jumping into.
        /// </summary>
        private bool HasLaunchRoom(Transform player)
        {
            Vector3 origin = player.position + Vector3.up * 0.1f;

            bool airborne = !Physics.Raycast(origin, Vector3.down, groundClearance,
                                             groundMask, QueryTriggerInteraction.Ignore);
            if (airborne)
                return true;

            Vector3 ahead = origin + player.forward * ledgeProbeForward;
            bool dropAhead = !Physics.Raycast(ahead, Vector3.down, minLaunchClearance,
                                              groundMask, QueryTriggerInteraction.Ignore);
            return dropAhead;
        }

        /// <summary>
        /// Owner-side: record the heading and the speed being carried into the launch.
        ///
        /// Both are measured on the pilot's own machine because both are theirs. The server has a
        /// replicated copy of the player's rotation and a Rigidbody it is not the one simulating,
        /// so a launch resolved there would come out a frame stale and a jump's worth of speed
        /// short — and that speed is the whole reward for running at the edge.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            if (owner == null) return;

            Vector3 forward = owner.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;

            arg.R = Quaternion.LookRotation(forward.normalized, Vector3.up);
            arg.P = new Vector3(LaunchSpeed(), 0f, 0f);
        }

        // Server-authoritative (the UsableItem default), so this only ever runs where spawning is
        // allowed. The pilot never names a prefab: the server reads ornithopterPrefab off its own
        // copy of the pack, which is why no whitelist is needed to stop a client asking for
        // something else.
        protected override void Use() =>
            SpawnAndMountCraft(owner, UseArg.R, UseArg.P.x);

        /// <summary>How much of the player's current speed carries into the launch.</summary>
        private float LaunchSpeed()
        {
            if (!owner.TryGetComponent(out Rigidbody playerBody))
                return 0f;

            Vector3 flat = playerBody.linearVelocity;
            flat.y = 0f;
            return flat.magnitude * speedCarry;
        }

        /// <summary>
        /// The whole launch, from spawning the craft to handing the pilot the controls. One path for
        /// offline, host and a remote pilot's launch running on the server — the difference is only
        /// WHERE it runs, never WHAT it does.
        /// </summary>
        public bool SpawnAndMountCraft(GameObject pilot, Quaternion facing, float carriedSpeed)
        {
            if (craft != null)
                return false;               // already flying

            if (ornithopterPrefab == null || pilot == null)
                return false;

            Transform player = pilot.transform;
            Vector3 forward = facing * Vector3.forward;

            ulong pilotOwner = NetworkSpawn.NoOwner;
            if (Network.IsNetworked && pilot.TryGetComponent(out NetworkObject pilotNetObj))
                pilotOwner = pilotNetObj.OwnerClientId;

            // Ownership goes to the pilot so their local flight input drives the craft and replicates
            // outward, rather than the server overwriting it every tick.
            craft = GameServices.World.Spawn(ornithopterPrefab, player.position, facing, pilotOwner);
            if (craft == null)
                return false;

            craftMount = craft.GetComponent<MountModule>();
            craftMotor = craft.GetComponent<OrnithopterFlightMotor>();
            if (craftMount == null || craftMotor == null)
            {
                Debug.LogError("WingPackItem: prefab needs both MountModule and OrnithopterFlightMotor.",
                               this);
                GameServices.World.Despawn(craft);
                craft = null;
                return false;
            }

            // Place the craft so its SEAT lands where the player already is, rather than its origin.
            // Mounting parents the player to the seat at identity, so without this the player is
            // teleported by however far the cradle sits from the prefab root.
            Transform seat = craftMount.ActiveSeatPoint;
            if (seat != null)
            {
                Vector3 seatOffset = seat.position - craft.transform.position;
                craft.transform.position = player.position + Vector3.up * launchLift - seatOffset;
            }
            else
            {
                craft.transform.position = player.position + Vector3.up * launchLift;
            }

            craftMotor.Landed += HandleLanded;
            craftMount.Dismounted += HandleDismounted;

            Interactor interactor = pilot.GetComponentInChildren<Interactor>(true);
            if (interactor == null || !craftMount.TryMount(interactor, null))
            {
                Debug.LogError("WingPackItem: could not mount the player onto the craft.", this);
                ReleaseCraft(dismountFirst: false);
                return false;
            }

            craftMotor.Launch(forward, carriedSpeed);
            SetHeldVisible(false);
            return true;
        }

        /// <summary>
        /// Takes ownership of a craft this pack did not spawn — one restored from a save with the
        /// pilot already strapped in.
        ///
        /// <b>Why it is needed.</b> A load rebuilds the craft and re-seats the rider through the save
        /// system, which knows nothing about wing packs. Without this the pack comes back believing it
        /// is stowed while its owner is a hundred metres up: the folded pack renders in the pilot's
        /// hand mid-flight, using it again would deploy a second craft, and — worst — nothing is
        /// subscribed to <c>Landed</c>, so touching down leaves the ornithopter standing in the sand
        /// forever with the player walking out of it.
        ///
        /// Idempotent, and called on the normal launch path too, where it is a no-op because
        /// <see cref="SpawnAndMountCraft"/> has already taken the craft. One owner, one teardown path,
        /// whether the flight began with a keypress or with a save file.
        /// </summary>
        public void AdoptCraft(GameObject existing)
        {
            if (craft != null || existing == null)
                return;

            MountModule mount = existing.GetComponent<MountModule>();
            OrnithopterFlightMotor motor = existing.GetComponent<OrnithopterFlightMotor>();
            if (mount == null || motor == null)
                return;

            craft = existing;
            craftMount = mount;
            craftMotor = motor;

            craftMotor.Landed += HandleLanded;
            craftMount.Dismounted += HandleDismounted;

            SetHeldVisible(false);
        }

        private void HandleLanded() => ReleaseCraft(dismountFirst: true);

        // The rider bailed out (Escape). The craft goes with them — and because the pack has unlimited
        // uses and is usable while falling, bailing out and redeploying is a legitimate move rather
        // than a dead end.
        private void HandleDismounted(PlayerMovement _) => ReleaseCraft(dismountFirst: false);

        /// <summary>
        /// The single teardown path. Reached from landing, from dismounting, from switching hotbar
        /// slot mid-flight, and from the item being destroyed — so it has to be safe to call twice and
        /// safe to call while the player is still parented into the craft.
        /// </summary>
        private void ReleaseCraft(bool dismountFirst)
        {
            if (craft == null)
                return;

            // Unsubscribe BEFORE dismounting: Dismount raises Dismounted, which re-enters here.
            if (craftMotor != null) craftMotor.Landed -= HandleLanded;
            if (craftMount != null) craftMount.Dismounted -= HandleDismounted;

            // Get the player out from under the craft before destroying it — they are parented to the
            // seat, and destroying the parent takes the player with it.
            if (dismountFirst && craftMount != null && craftMount.IsMounted)
                craftMount.Dismount();

            GameObject doomed = craft;
            craft = null;
            craftMount = null;
            craftMotor = null;

            // Despawn through the world service so the craft disappears for every player, not just
            // whoever was flying it. Only the server may retire a networked object; on a client the
            // authoritative despawn arrives from the server, so don't destroy it out from under that.
            if (Network.IsNetworked && !Network.Server &&
                doomed.TryGetComponent(out NetworkObject doomedNetObj) && doomedNetObj.IsSpawned)
            {
                SetHeldVisible(true);
                return;
            }

            GameServices.World.Despawn(doomed);
            SetHeldVisible(true);
        }

        /// <summary>Hide the folded pack in the player's hand while the real thing is deployed.</summary>
        private void SetHeldVisible(bool visible)
        {
            heldRenderers ??= GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in heldRenderers)
                if (r) r.enabled = visible;
        }

        public override void OnUnequipped(GameObject holder)
        {
            base.OnUnequipped(holder);
            ReleaseCraft(dismountFirst: true);
        }

        private void OnDestroy() => ReleaseCraft(dismountFirst: true);
    }
}
