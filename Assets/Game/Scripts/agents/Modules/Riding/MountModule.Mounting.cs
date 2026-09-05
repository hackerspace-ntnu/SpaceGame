// Mount/dismount flow, rider state caching, rigidbody handoff, and third-person camera spawn/cleanup.
// Split off MountModule.cs purely for readability.
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    public partial class MountModule
    {
        public bool CanMount(Interactor interactor) =>
            IsAvailableForMount && interactor != null && interactor.GetComponentInParent<PlayerMovement>() != null;

        public bool TryMount(Interactor interactor, Transform mountPointOverride)
        {
            if (!CanMount(interactor))
                return false;

            PlayerMovement playerMovement = interactor.GetComponentInParent<PlayerMovement>();
            if (!playerMovement)
                return false;

            VacateSeatForPlayer();
            CacheMountedPlayerReferences(playerMovement, mountPointOverride);
            // Arm before anything parents the rider: the beacon is the only thing that can tell a
            // later Dismount that the rider is being destroyed rather than merely leaving.
            RiderTeardownBeacon.Arm(mountedPlayer);
            SubscribeToRiderDeath();
            DisableRiderComponentsForMount();
            EnterMountedRigidbodyState();
            ParentRiderToMount();
            ApplyModuleSuppression();
            StopOwnMotorOnMount();
            FreezeOwnRigidbodyRotationOnMount();
            IgnoreRiderMountCollisions();
            SuppressRootMotionOnMount();
            InitializeMountedViewState();
            ApplyPerspective(defaultPerspective);
            lastMountChangeTime = Time.time;
            Mounted?.Invoke(playerMovement);
            return true;
        }

        // Clear any pending nav destination / velocity so the mount doesn't keep
        // cruising on its own when the rider takes over with no input.
        private void StopOwnMotorOnMount()
        {
            if (allowAISelfMovementWhenMounted)
                return;
            IMovementMotor motor = GetComponent<IMovementMotor>();
            motor?.ForceStop();
        }

        // Rider is a kinematic Rigidbody parented inside the mount's collider — contacts from that
        // overlap spin the mount in place. The rider owns rotation via SteerModule writing to
        // transform.rotation directly, so physics rotation isn't wanted while mounted anyway.
        // Lock it, remember the original constraints, restore on dismount.
        private void FreezeOwnRigidbodyRotationOnMount()
        {
            if (!ownRigidbody)
                ownRigidbody = GetComponent<Rigidbody>();
            if (!ownRigidbody)
                return;
            ownRigidbodyConstraints = ownRigidbody.constraints;
            ownRigidbodyConstraintsCaptured = true;
            ownRigidbody.constraints = ownRigidbodyConstraints | RigidbodyConstraints.FreezeRotation;
            ownRigidbody.angularVelocity = Vector3.zero;
        }

        private void RestoreOwnRigidbodyRotationAfterDismount()
        {
            if (!ownRigidbody || !ownRigidbodyConstraintsCaptured)
                return;
            ownRigidbody.constraints = ownRigidbodyConstraints;
            ownRigidbody.angularVelocity = Vector3.zero;
            ownRigidbodyConstraintsCaptured = false;
        }

        // Stop the rider's kinematic body shoving the mount via contacts at the seat point. The
        // pairs themselves are RiderCollisionIgnore's business — NpcPassenger seats a rider in this
        // same saddle and needs the identical suspension.
        private void IgnoreRiderMountCollisions() => riderCollisions.Apply(mountedPlayer, transform);

        private void RestoreRiderMountCollisions() => riderCollisions.Restore();

        /// <summary>
        /// Turf out whoever is already in the saddle, before a player is seated on top of them.
        ///
        /// <para>
        /// A caravan animal carries an NPC seated by <see cref="NpcPassenger"/>, which this class
        /// deliberately knows nothing about — so the seat reads as free and the mount happily put
        /// the player inside the nomad. Asked through <see cref="ISeatOccupant"/> so the eviction
        /// stays a question the seat can pose without knowing who is in it.
        /// </para>
        /// <para>
        /// Before the player is cached and parented, so the seat changes hands in one direction
        /// only and the riding pose is never asked to hold two riders at once.
        /// </para>
        /// </summary>
        private void VacateSeatForPlayer()
        {
            var occupants = new System.Collections.Generic.List<ISeatOccupant>();
            GetComponents(occupants);

            foreach (ISeatOccupant occupant in occupants)
            {
                if (occupant.HasRider)
                    occupant.VacateSeat();
            }
        }

        // Animator root motion can translate/rotate the mount transform even when every module
        // is suppressed and every intent is Idle — a classic "mount walks in circles" source.
        // Turn it off on the mount's animators for the mounted duration.
        private void SuppressRootMotionOnMount()
        {
            if (allowAISelfMovementWhenMounted)
                return;
            Animator[] animators = GetComponentsInChildren<Animator>(true);
            suppressibleAnimators = animators;
            suppressibleAnimatorRootMotion = new bool[animators.Length];
            for (int i = 0; i < animators.Length; i++)
            {
                suppressibleAnimatorRootMotion[i] = animators[i].applyRootMotion;
                animators[i].applyRootMotion = false;
            }
        }

        private void RestoreRootMotionAfterDismount()
        {
            if (suppressibleAnimators == null)
                return;
            for (int i = 0; i < suppressibleAnimators.Length; i++)
            {
                if (suppressibleAnimators[i])
                    suppressibleAnimators[i].applyRootMotion = suppressibleAnimatorRootMotion[i];
            }
            suppressibleAnimators = null;
            suppressibleAnimatorRootMotion = null;
        }

        /// <summary>
        /// Dismount, standing the rider at an explicit world position instead of at the mount's
        /// dismount point.
        ///
        /// For the cases where the mount is in no state to say where its own dismount point is: a
        /// crashed aircraft is embedded in a cliff at whatever attitude it hit at, and its dismount
        /// marker is wherever that attitude swung it — inside the rock as often as not. The caller
        /// has already resolved somewhere solid; this is how it gets used.
        ///
        /// Routed through <see cref="Dismount"/> rather than duplicating it, so the re-entrancy
        /// guard, the teardown beacon and every restore behave identically whoever chose the spot.
        /// </summary>
        public void DismountAt(Vector3 position)
        {
            if (!IsMounted)
                return;

            dismountPositionOverride = position;
            Dismount();
        }

        public void Dismount()
        {
            if (!IsMounted)
                return;

            // Re-entrancy guard. IsMounted alone does not close this: mountedPlayer is not cleared
            // until ClearMountedReferences at the very end, but Dismounted fires before it, and
            // listeners routinely dismount in response (WingPackItem tears the craft down, which
            // reaches Dismount again). Death added a seventh path in, so this stops relying on
            // every listener remembering to unsubscribe before it calls back.
            if (dismounting)
                return;
            dismounting = true;
            try
            {
                DismountInternal();
            }
            finally
            {
                dismounting = false;
            }
        }

        private void DismountInternal()
        {
            Transform rider = mountedPlayer;

            // Consumed here rather than in DismountAt, and consumed on every path out including the
            // abandon one: an override left standing would silently relocate the NEXT rider to
            // wherever the last one crashed.
            Vector3? requestedPosition = dismountPositionOverride;
            dismountPositionOverride = null;

            // The rider is going away underneath us — they died, or whatever owns them is being
            // destroyed and took the seat with it. Every restore below reaches into that doomed
            // object, and the reparent is outright illegal, so there is nothing useful left to do
            // but forget them. Same reasoning as the mount-side case in OnDisable, mirrored.
            //
            // This lives HERE, not at the call sites, because six independent paths reach Dismount
            // (SteerModule, MountNetworkSync, DuneRiderController, WingPackItem, OnDisable, and
            // anything added later) and each one would otherwise need the same guard.
            if (!RiderTeardownBeacon.CanReparent(rider))
            {
                AbandonRider();
                lastMountChangeTime = Time.time;
                return;
            }

            UnparentRider(rider);

            Vector3 dismountPosition = requestedPosition ?? (dismountPoint
                ? dismountPoint.position
                : transform.position + transform.right * fallbackDismountDistance);

            // Recorded so the dismount can be replicated as a PLACE rather than as a bare event.
            // Every peer re-derives this position from its own copy of the mount, which is only the
            // same answer while the two copies agree — and the one case that matters most is the
            // one where they do not: a crashed aircraft is dismounted at ground the server probed
            // for, and a peer that recomputes puts its pilot under the wreck instead.
            lastDismountPosition = dismountPosition;
            hasLastDismountPosition = true;

            // Strip any tilt the rider inherited from a tilted mount — keep only yaw so the
            // player stands upright after dismount.
            Quaternion dismountRotation = Quaternion.Euler(0f, rider.eulerAngles.y, 0f);
            ApplyDismountPose(rider, dismountPosition, dismountRotation);

            ExitMountedRigidbodyState();
            RestoreRiderComponentsAfterDismount();
            RestoreOwnRigidbodyRotationAfterDismount();
            RestoreRiderMountCollisions();
            RestoreRootMotionAfterDismount();
            RestoreModuleSuppression();
            RestoreLocalViewAfterDismount();
            PlayerMovement dismountedMovement = mountedPlayerMovement;
            Dismounted?.Invoke(dismountedMovement);
            ReleaseRuntimeThirdPersonCamera();
            ClearMountedReferences();
            activeSeatPoint = seatPoint;
            lastMountChangeTime = Time.time;
        }

        // The teardown counterpart to Dismount, for when reparenting is illegal because one side of
        // the pairing is going away. Two ways in, and they are mirror images:
        //   • the MOUNT is going away underneath the rider  (OnDisable, activeInHierarchy == false)
        //   • the RIDER is going away underneath the mount  (Dismount, beacon says being destroyed)
        // Everything Dismount restores — the rider's
        // components, its Rigidbody, the ignored collision pairs, the third-person camera — belongs
        // to objects being deactivated or destroyed alongside this one, and the Dismounted event
        // would hand a doomed rider to listeners in the same state. So the only useful thing left is
        // to forget the rider, which is what stops anything here acting on a dead reference.
        //
        // The rider stays parented. That is correct while the whole hierarchy is going down; a mount
        // that is deactivated and later reactivated with a rider aboard would come back mounted but
        // untracked, so pooling a ridden mount would need a real dismount before the SetActive call.
        private void AbandonRider()
        {
            // Destroyed rather than merely forgotten, unlike everything else here. The mount camera
            // is spawned UNPARENTED (see EnsureRuntimeThirdPersonCamera) precisely so the vehicle's
            // motion does not reach it twice, which also means nothing takes it down with the
            // hierarchy this method exists to give up on. Dropping the reference alone leaves a live
            // camera and a second AudioListener in the scene for the rest of the session.
            ReleaseRuntimeThirdPersonCamera();
            riderCollisions.Forget();
            // Dropped rather than restored, for the same reason as everything else here: the body is
            // being destroyed. Left standing, the claim would outlive both of us and any carrier
            // that picked the body up again would never be able to hand it back.
            CarriedBody.Abandon(this);
            suppressibleAnimators = null;
            suppressibleAnimatorRootMotion = null;
            ownRigidbodyConstraintsCaptured = false;
            ClearMountedReferences();
            activeSeatPoint = seatPoint;
        }

        // Writing the transform alone isn't enough to place the rider. Physics.autoSyncTransforms is
        // off project-wide, so a direct transform write doesn't reach the Rigidbody until the next
        // physics step — until then the body still holds the pose it last synced, which is the mount's,
        // tilt and all. PlayerLook rebuilds the player's rotation every frame from
        // playerRigidbody.rotation, and Dismount re-enables it moments later, so the first Update after
        // dismount would read that stale pose and put the mount's orientation straight back on the
        // player. Write the Rigidbody too so both sides agree before anything reads either.
        //
        // Called while the rider is still kinematic with interpolation off (the mounted state), so the
        // pose lands immediately and there's no interpolation history to smear from once
        // ExitMountedRigidbodyState hands the body back to physics.
        private void ApplyDismountPose(Transform rider, Vector3 position, Quaternion rotation)
        {
            rider.SetPositionAndRotation(position, rotation);

            if (!mountedPlayerRigidbody)
                return;

            mountedPlayerRigidbody.position = position;
            mountedPlayerRigidbody.rotation = rotation;
        }

        // ─────────── Rider state cache/restore ───────────
        private void CacheMountedPlayerReferences(PlayerMovement playerMovement, Transform mountPointOverride)
        {
            mountedPlayer = playerMovement.transform;
            mountedPlayerMovement = playerMovement;
            mountedPlayerLook = mountedPlayer.GetComponent<PlayerLook>();
            mountedInteractor = mountedPlayer.GetComponentInChildren<Interactor>(true);
            mountedPlayerRigidbody = mountedPlayer.GetComponent<Rigidbody>();
            mountedFirstPersonCamera = mountedPlayer.GetComponentInChildren<Camera>(true);
            mountedFirstPersonCameraRoot = mountedPlayerLook != null ? mountedPlayerLook.cameraRoot : null;
            mountedAimProvider = mountedPlayer.GetComponent<AimProvider>();
            activeSeatPoint = mountPointOverride ? mountPointOverride : seatPoint;
        }

        private void DisableRiderComponentsForMount()
        {
            // Captured before anything is written, and captured whether or not this mount is
            // configured to take that component — an untaken component is remembered as-is, so the
            // restore below stays a no-op for it either way.
            riderMovementWasEnabled = mountedPlayerMovement && mountedPlayerMovement.enabled;
            riderLookWasEnabled = mountedPlayerLook && mountedPlayerLook.enabled;
            riderInteractorWasEnabled = mountedInteractor && mountedInteractor.enabled;

            if (disablePlayerMovement && mountedPlayerMovement)
            {
                mountedPlayerMovement.enabled = false;
                mountedPlayerMovement.ForceIdleAnimation();
            }

            if (disablePlayerLook && mountedPlayerLook)
                mountedPlayerLook.enabled = false;

            if (disablePlayerInteractor && mountedInteractor)
                mountedInteractor.enabled = false;
        }

        private void RestoreRiderComponentsAfterDismount()
        {
            // A dead rider gets nothing back. Death freezes movement, look and input and releases
            // the cursor for the death screen; dying mid-ride then tears the mount down, and this
            // restore runs AFTER that freeze. Re-enabling PlayerLook here is not just a stray flag:
            // PlayerLook.OnEnable re-locks the cursor and its LateUpdate keeps re-locking it every
            // frame, which is what makes the respawn button unclickable after dying while flying.
            //
            // The freeze stands until OnRevive hands control back.
            if (RiderIsDead())
                return;

            // What was taken, and only that. A rider who arrived with these already off is a remote
            // player being replayed by MountNetworkSync on a machine that does not own them — see
            // the fields' note in MountModule.cs. Waking those up is not a cosmetic slip: it hands
            // somebody else's PlayerLook this machine's cursor, every frame, until they quit.
            if (disablePlayerMovement && mountedPlayerMovement && riderMovementWasEnabled)
                mountedPlayerMovement.enabled = true;

            if (disablePlayerLook && mountedPlayerLook && riderLookWasEnabled)
                mountedPlayerLook.enabled = true;

            if (disablePlayerInteractor && mountedInteractor && riderInteractorWasEnabled)
                mountedInteractor.enabled = true;
        }

        /// <summary>
        /// Whether the rider being dismounted is dead — asked of PlayerController, which owns the
        /// death state, rather than inferred from the component flags this method is about to write.
        /// </summary>
        private bool RiderIsDead()
        {
            if (!mountedPlayer)
                return false;

            var controller = mountedPlayer.GetComponent<PlayerController>();
            return controller != null && controller.IsDead;
        }

        // Dying in the saddle used to leave the corpse strapped in and the mount still flying:
        // nothing between HealthComponent and MountModule knew the rider had died, and the only
        // things that ever dismount are landing, bailing out and teardown. The rider's death is a
        // dismount like any other, so it goes through the same single path.
        private void SubscribeToRiderDeath()
        {
            UnsubscribeFromRiderDeath();
            if (!mountedPlayer)
                return;

            mountedRiderHealth = mountedPlayer.GetComponent<HealthComponent>();
            if (mountedRiderHealth != null)
                mountedRiderHealth.OnDeath += HandleRiderDied;
        }

        private void UnsubscribeFromRiderDeath()
        {
            if (mountedRiderHealth != null)
                mountedRiderHealth.OnDeath -= HandleRiderDied;
            mountedRiderHealth = null;
        }

        // Ordering matters: PlayerController.OnDeath applies the freeze, and this runs off the same
        // OnDeath event, so the dismount that follows already sees IsDead and leaves the freeze
        // alone. Subscription order between the two listeners is not relied on — the guard in
        // RestoreRiderComponentsAfterDismount reads the flag, and if this somehow ran first the
        // freeze would simply be applied afterwards, which lands in the same place.
        private void HandleRiderDied()
        {
            if (IsMounted)
                Dismount();
        }

        // Freezing the rider is the same act whichever carrier does it, and — critically — it is NOT
        // this module's private business. A player can already be held by SeatedRider when they take
        // the helm of the ship they rode down in, and a second private capture there banks the
        // SEATED state as the truth and hands it back on dismount: a body returned kinematic and
        // weightless, which reads as a player who cannot move and has no gravity. CarriedBody
        // captures once, on the first hold, and restores once, on the last release.
        private void EnterMountedRigidbodyState()
        {
            if (!mountedPlayerRigidbody)
                return;

            CarriedBody.Hold(mountedPlayer.gameObject, this);
        }

        private void ExitMountedRigidbodyState()
        {
            if (!mountedPlayerRigidbody)
                return;

            CarriedBody.Release(mountedPlayer.gameObject, this);
        }

        // Netcode refuses to let a NetworkObject sit under a plain transform, and seatPoint is a bare
        // child marker — parenting straight to it throws InvalidParentException and leaves the rider
        // unparented mid-mount. So when both sides are networked, parent to the mount's NetworkObject
        // (the only legal parent) and carry the seat marker's offset in local space instead.
        private void ParentRiderToMount()
        {
            Transform rideParent = seatPoint ? seatPoint : transform;

            NetworkObject riderNetObj = mountedPlayer.GetComponent<NetworkObject>();
            bool riderIsNetworked = riderNetObj != null && riderNetObj.IsSpawned;

            if (riderIsNetworked)
            {
                NetworkObject mountNetObj = GetComponentInParent<NetworkObject>();
                if (mountNetObj == null || !mountNetObj.IsSpawned)
                {
                    // Nothing legal to parent to. Seat the rider by world pose and let SteerModule
                    // keep them there rather than throwing and half-mounting them.
                    SeatRiderWithoutParenting(rideParent);
                    return;
                }

                if (!riderNetObj.TrySetParent(mountNetObj, true))
                {
                    SeatRiderWithoutParenting(rideParent);
                    return;
                }

                // Local space is now the mount root's, not the seat marker's, so fold the marker's
                // offset in by hand to land in the same place the offline path does.
                Vector3 seatLocal = mountNetObj.transform.InverseTransformPoint(
                    rideParent.TransformPoint(seatOffset));
                mountedPlayer.localPosition = seatLocal;
                mountedPlayer.localRotation =
                    Quaternion.Inverse(mountNetObj.transform.rotation) * rideParent.rotation;
                return;
            }

            mountedPlayer.SetParent(rideParent, true);
            mountedPlayer.localPosition = seatOffset;
            mountedPlayer.localRotation = Quaternion.identity;
        }

        // Mirror of ParentRiderToMount: a spawned NetworkObject has to be detached through netcode so
        // the change replicates, rather than by a raw SetParent(null) that only happens locally.
        private static void UnparentRider(Transform rider)
        {
            NetworkObject riderNetObj = rider.GetComponent<NetworkObject>();
            if (riderNetObj != null && riderNetObj.IsSpawned)
            {
                if (riderNetObj.TryRemoveParent(true))
                    return;
            }

            rider.SetParent(null, true);
        }

        // Fallback for a networked rider with no legal NetworkObject parent: place them on the seat in
        // world space. Their Rigidbody is kinematic while mounted, so they stay put relative to a
        // stationary mount; a moving mount will drag them only as far as SteerModule re-seats them.
        private void SeatRiderWithoutParenting(Transform rideParent)
        {
            mountedPlayer.SetPositionAndRotation(rideParent.TransformPoint(seatOffset), rideParent.rotation);
        }

        private void ClearMountedReferences()
        {
            // Both teardown paths (Dismount and AbandonRider) end here, which makes this the one
            // place guaranteed to run whichever way the rider left.
            UnsubscribeFromRiderDeath();

            mountedPlayer = null;
            mountedPlayerMovement = null;
            mountedPlayerLook = null;
            mountedInteractor = null;
            mountedPlayerRigidbody = null;
            mountedFirstPersonCamera = null;
            mountedFirstPersonCameraRoot = null;
            mountedAimProvider = null;
        }

        private void ReleaseRuntimeThirdPersonCamera()
        {
            if (runtimeThirdPersonCamera == null)
                return;

            // DestroyImmediate outside play mode, because plain Destroy there is an editor ERROR
            // ("Destroy may not be called from edit mode") and the object survives regardless. That
            // is not hypothetical tidiness: mounting spawns this camera from ApplyPerspective, so
            // every EditMode test that mounts and dismounts something raises it, and an unhandled
            // error log fails the test that provoked it whatever it was actually asserting.
            if (Application.isPlaying)
                Destroy(runtimeThirdPersonCamera.gameObject);
            else
                DestroyImmediate(runtimeThirdPersonCamera.gameObject);

            runtimeThirdPersonCamera = null;
        }
    }
}
