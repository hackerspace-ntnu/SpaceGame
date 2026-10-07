// A place to sit: the thing a resident or a player sits ON, instead of a height somebody sampled.
//
// A seat owns three facts and nothing else. Where the hips rest (the sit point, +Z the way the sitter faces), how far
// the feet hang below it to the floor (the foot drop, so the body root can stand where the feet are), and who is sitting.
// One occupant only: a claim is refused while somebody else holds the seat, and a seat that is carried away from the pose
// it was claimed at throws its sitter off, because a body left behind at the old pose would sit in the air.
//
// A seat is scenery, like every decoration: no NetworkObject, nothing saved, the same bytes in every machine's scene.
// So it is addressed by an id derived from where it sits in the hierarchy (the id a SaveableEntity falls back on),
// which every machine computes alike. The claim is not stored here: a resident's claim rides ResidentPresence's seat
// id and a player's rides PlayerSeating's, and each machine records it on the seat it resolves that id to. A claim is
// therefore re-derived on load and on a late join, never saved.
using System;
using System.Collections.Generic;
using SpaceGame.Characters;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>How a body sits on a seat, which is a fact about the seat's height and shape. Append only: prefabs store the number.</summary>
    public enum SeatPose
    {
        /// <summary>A stool, a pot, a bench: the base layer's chair sit (knees bent, feet down the front of the seat).</summary>
        Stool = 0,

        /// <summary>A cushion on the ground: the cross-legged floor sit, held as a loop of the spot's cue.</summary>
        Floor = 1,

        /// <summary>A bunk: the sleeper lies on it, the body root on the mattress, the sleep loop held.</summary>
        Lie = 2,
    }

    [DisallowMultipleComponent]
    public sealed class Seat : MonoBehaviour, IInteractable, IContextualInteractable, IInteractionReadout, IInteractionMoment
    {
        [Tooltip("Where the hips rest, +Z the way the sitter faces. Empty = this transform.")]
        [SerializeField] private Transform sitPoint;

        [Tooltip("How a resident sits here. Stool: the chair sit, legs down the front, for anything with a solid body under the " +
                 "sitter (a pot, a stool, a bench). Floor: the cross-legged sit, for a cushion with room for the legs on it. " +
                 "Lie: a bed, where only a sleeping resident lies.")]
        [SerializeField] private SeatPose pose = SeatPose.Stool;

        [Tooltip("Metres, in this seat's own units, from the sit point straight down to the floor the feet hang to. " +
                 "The sitter's body stands there: a floor cushion is 0, a stool is its height.")]
        [SerializeField, Min(0f)] private float footDrop;

        [Tooltip("Metres, in this seat's own units, in front of the seat where a player stands up.")]
        [SerializeField, Min(0f)] private float standUpDistance = 1f;

        [Tooltip("The seat counts as moved, and throws its sitter off, once the sit point is this many metres from where it was claimed.")]
        [SerializeField, Min(0.001f)] private float moveTolerance = 0.05f;

        [Tooltip("...or turned this many degrees about the vertical.")]
        [SerializeField, Range(1f, 90f)] private float turnTolerance = 10f;

        private static readonly Dictionary<int, Seat> ById = new();

        private int id;
        private Transform occupant;
        private Vector3 claimedPosition;
        private float claimedYaw;

        /// <summary>Raised when the sitter leaves: released by its owner, or thrown off because the seat moved. Not raised for a destroyed sitter.</summary>
        public event Action<Seat, Transform> Vacated;

        public Transform SitPoint => sitPoint != null ? sitPoint : transform;

        /// <summary>How a resident sits here. A player's pose is the chair's (<c>ChairPose</c>) whatever this says.</summary>
        public SeatPose Pose => pose;

        /// <summary>Where the hips rest, in the world.</summary>
        public Vector3 SitPosition => SitPoint.position;

        /// <summary>The way a sitter faces, level.</summary>
        public Quaternion Facing => YawOf(SitPoint.forward);

        /// <summary>Where the sitter's feet are: the floor below the sit point. A sitter's body root stands here.</summary>
        public Vector3 FeetPosition => SitPosition + transform.TransformVector(Vector3.down * footDrop);

        /// <summary>Where a player stands up: out in front of the seat, on the floor.</summary>
        public Vector3 StandUpPosition => FeetPosition + Facing * transform.TransformVector(Vector3.forward * standUpDistance);

        /// <summary>Who sits here; null when nobody does, or the sitter has been destroyed.</summary>
        public Transform Occupant => occupant != null ? occupant : null;

        public bool IsFree => Occupant == null;

        /// <summary>The id every machine derives alike; never 0, which is "no seat" on the wire.</summary>
        public int Id => id != 0 ? id : id = IdOf(gameObject);

        public static IEnumerable<Seat> All => ById.Values;

        public static int IdOf(GameObject seat) => SceneryId.Of(seat);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ById.Clear();

        /// <summary>The seat this id names on this machine, or null (not loaded yet, or never was).</summary>
        public static Seat Find(int seatId) => seatId != 0 && ById.TryGetValue(seatId, out Seat seat) && seat != null ? seat : null;

        /// <summary>The nearest free seat within <paramref name="reach"/> metres of <paramref name="point"/> across the ground, or null.</summary>
        public static Seat NearestFree(Vector3 point, float reach)
        {
            Seat best = null;
            float bestSqr = reach * reach;
            foreach (Seat seat in ById.Values)
            {
                if (seat == null || !seat.IsFree) continue;

                float sqr = seat.FlatSqrDistanceTo(point);
                if (sqr > bestSqr) continue;
                best = seat;
                bestSqr = sqr;
            }
            return best;
        }

        /// <summary>Whether any seat at all, free or not, is within <paramref name="reach"/> metres of <paramref name="point"/> across the ground.</summary>
        public static bool AnyWithin(Vector3 point, float reach)
        {
            foreach (Seat seat in ById.Values)
                if (seat != null && seat.IsWithin(point, reach)) return true;
            return false;
        }

        /// <summary>Whether the sit point is within <paramref name="reach"/> metres of <paramref name="point"/> across the ground: a sit point is above the floor a spot stands on.</summary>
        public bool IsWithin(Vector3 point, float reach) => FlatSqrDistanceTo(point) <= reach * reach;

        /// <summary>Squared distance from the sit point to <paramref name="point"/>, ignoring height.</summary>
        public float FlatSqrDistanceTo(Vector3 point) => Vector3.ProjectOnPlane(SitPosition - point, Vector3.up).sqrMagnitude;

        /// <summary>Whether a seat claimed at one pose has been carried off it: past the position tolerance, or turned past the yaw tolerance.</summary>
        public static bool HasMoved(Vector3 claimed, float claimedYaw, Vector3 now, float nowYaw, float moveTolerance, float turnTolerance) =>
            (now - claimed).sqrMagnitude > moveTolerance * moveTolerance || Mathf.Abs(Mathf.DeltaAngle(claimedYaw, nowYaw)) > turnTolerance;

        private void OnEnable() => Register();

        private void OnDisable() => Unregister();

        /// <summary>Makes this seat findable. Done from OnEnable; public so an edit-mode test can stand a seat up.</summary>
        public void Register()
        {
            if (!ById.TryGetValue(Id, out Seat other) || other == null)
            {
                ById[Id] = this;
                return;
            }

            if (other != this)
                Debug.LogError($"[Seat] '{name}' and '{other.name}' derive the same id {Id}; the second cannot be sat on by id. " +
                               "Rename one or move it in its hierarchy.", this);
        }

        /// <summary>Lets the sitter go and makes this seat unfindable. Done from OnDisable; public so an edit-mode test can take a seat down.</summary>
        public void Unregister()
        {
            Release(occupant);
            if (ById.TryGetValue(Id, out Seat registered) && registered == this) ById.Remove(Id);
        }

        /// <summary>Takes the seat for <paramref name="who"/>. False when somebody else holds it; true again for the holder.</summary>
        public bool TryClaim(Transform who)
        {
            if (who == null) return false;
            if (Occupant == who) return true;
            if (!IsFree) return false;

            occupant = who;
            claimedPosition = SitPosition;
            claimedYaw = Facing.eulerAngles.y;
            return true;
        }

        /// <summary>Lets <paramref name="who"/> go. Ignores anybody who is not the sitter, so a late release cannot evict the next one.</summary>
        public bool Release(Transform who)
        {
            if (who == null || Occupant != who) return false;

            occupant = null;
            Vacated?.Invoke(this, who);
            return true;
        }

        private void LateUpdate() => ReleaseIfMoved();

        /// <summary>A seat carried away from the pose it was claimed at lets go of its sitter: the body does not travel with it.</summary>
        public void ReleaseIfMoved()
        {
            if (Occupant != null && HasMoved(claimedPosition, claimedYaw, SitPosition, Facing.eulerAngles.y, moveTolerance, turnTolerance))
                Release(Occupant);
        }

        private static Quaternion YawOf(Vector3 forward)
        {
            Vector3 level = Vector3.ProjectOnPlane(forward, Vector3.up);
            return level.sqrMagnitude > Mathf.Epsilon ? Quaternion.LookRotation(level, Vector3.up) : Quaternion.identity;
        }

        // ── the player's way onto it ─────────────────────────────────────────────────────────────

        public bool CanInteract() => IsFree && pose != SeatPose.Lie;

        public bool CanInteract(Interactor interactor)
        {
            PlayerSeating seating = PlayerSeating.Of(interactor);
            return seating != null && seating.CanSit;
        }

        public void Interact(Interactor interactor) => PlayerSeating.Of(interactor)?.RequestSit(this);

        public CharacterMoment InteractionMoment => CharacterMoment.None;

        public string Label => "Seat";
        public string Prompt => "RMB: sit";
        public float? Value01 => null;
        public string ValueText => null;
    }
}
