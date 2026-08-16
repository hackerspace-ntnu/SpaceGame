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

            CacheMountedPlayerReferences(playerMovement, mountPointOverride);
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

        // Disable collision between every rider collider and every mount collider so the rider's
        // kinematic body can't shove the mount via contacts at the seat point.
        private void IgnoreRiderMountCollisions()
        {
            if (mountedPlayer == null)
                return;

            Collider[] riderColliders = mountedPlayer.GetComponentsInChildren<Collider>(true);
            Collider[] mountColliders = GetComponentsInChildren<Collider>(true);
            if (riderColliders.Length == 0 || mountColliders.Length == 0)
                return;

            var pairs = new System.Collections.Generic.List<(Collider, Collider)>(
                riderColliders.Length * mountColliders.Length);

            foreach (Collider r in riderColliders)
            {
                if (!r) continue;
                foreach (Collider m in mountColliders)
                {
                    if (!m || r == m) continue;
                    Physics.IgnoreCollision(r, m, true);
                    pairs.Add((r, m));
                }
            }

            ignoredCollisionPairs = pairs.ToArray();
        }

        private void RestoreRiderMountCollisions()
        {
            if (ignoredCollisionPairs == null)
                return;

            foreach (var (a, b) in ignoredCollisionPairs)
            {
                if (a && b)
                    Physics.IgnoreCollision(a, b, false);
            }

            ignoredCollisionPairs = null;
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

        public void Dismount()
        {
            if (!IsMounted)
                return;

            Transform rider = mountedPlayer;
            UnparentRider(rider);

            Vector3 dismountPosition = dismountPoint
                ? dismountPoint.position
                : transform.position + transform.right * fallbackDismountDistance;

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
            SetThirdPersonCameraEnabled(false);
            SetFirstPersonCameraEnabled(true);
            SetMountedVisorEnabled(true);
            PlayerMovement dismountedMovement = mountedPlayerMovement;
            Dismounted?.Invoke(dismountedMovement);
            ReleaseRuntimeThirdPersonCamera();
            ClearMountedReferences();
            activeSeatPoint = seatPoint;
            lastMountChangeTime = Time.time;
        }

        // The teardown counterpart to Dismount, for when the mount is going away underneath the rider
        // and reparenting is illegal (see OnDisable). Everything Dismount restores — the rider's
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
            runtimeThirdPersonCamera = null;
            ignoredCollisionPairs = null;
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
            activeSeatPoint = mountPointOverride ? mountPointOverride : seatPoint;
        }

        private void DisableRiderComponentsForMount()
        {
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
            if (disablePlayerMovement && mountedPlayerMovement)
                mountedPlayerMovement.enabled = true;

            if (disablePlayerLook && mountedPlayerLook)
            {
                mountedPlayerLook.SetHeadVisible(false);
                mountedPlayerLook.enabled = true;
            }

            if (disablePlayerInteractor && mountedInteractor)
                mountedInteractor.enabled = true;
        }

        private void EnterMountedRigidbodyState()
        {
            if (!mountedPlayerRigidbody)
                return;

            playerRigidbodyWasKinematic = mountedPlayerRigidbody.isKinematic;
            playerRigidbodyHadGravity = mountedPlayerRigidbody.useGravity;
            playerRigidbodyInterpolation = mountedPlayerRigidbody.interpolation;
            mountedPlayerRigidbody.linearVelocity = Vector3.zero;
            mountedPlayerRigidbody.angularVelocity = Vector3.zero;
            mountedPlayerRigidbody.isKinematic = true;
            mountedPlayerRigidbody.useGravity = false;
            // Rider is parented to the mount and follows it via the transform hierarchy. Letting
            // the rider's Rigidbody also interpolate makes it sample stale world positions one
            // physics step behind the parent, which renders the rider drifting off the seat.
            mountedPlayerRigidbody.interpolation = RigidbodyInterpolation.None;
        }

        private void ExitMountedRigidbodyState()
        {
            if (!mountedPlayerRigidbody)
                return;

            mountedPlayerRigidbody.isKinematic = playerRigidbodyWasKinematic;
            mountedPlayerRigidbody.useGravity = playerRigidbodyHadGravity;
            mountedPlayerRigidbody.interpolation = playerRigidbodyInterpolation;
            mountedPlayerRigidbody.linearVelocity = Vector3.zero;
            mountedPlayerRigidbody.angularVelocity = Vector3.zero;
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
            mountedPlayer = null;
            mountedPlayerMovement = null;
            mountedPlayerLook = null;
            mountedInteractor = null;
            mountedPlayerRigidbody = null;
            mountedFirstPersonCamera = null;
            mountedFirstPersonCameraRoot = null;
        }

        private void ReleaseRuntimeThirdPersonCamera()
        {
            if (runtimeThirdPersonCamera == null)
                return;
            Destroy(runtimeThirdPersonCamera.gameObject);
            runtimeThirdPersonCamera = null;
        }
    }
}
