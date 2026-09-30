// Crawling through a hatch, on the player's own body.
//
// A hatch is too low to walk through, so passing one is not left to the player's legs: whatever
// asks (HatchPassage) hands this a short path — outside, the sill, inside — and the body is steered
// along it crouched, with gravity off, the way LadderClimber steers a climb. The hull's own colliders
// are ignored for the duration, because the path runs through the sill and the coaming by design;
// everything else in the world still collides. The last point is reached with SaveTeleport, the one
// function that moves a body instantly and cleanly, so the crawl always ends exactly on the floor.
//
// Owner only, like LadderClimber. The player's NetworkTransform is owner-authoritative, so peers
// see the crawl as that pose moving; nothing about it is on the wire and nothing is saved — a player
// loaded mid-crawl is simply standing where they were.
using System;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Teleporting;
using UnityEngine;

namespace SpaceGame.Characters
{
    [DefaultExecutionOrder(150)]
    [RequireComponent(typeof(Rigidbody), typeof(PlayerMovement))]
    public class HatchCrawler : MonoBehaviour, ITeleportAware
    {
        [Tooltip("Crawl speed along the path, m/s.")]
        [SerializeField, Min(0.1f)] private float crawlSpeed = 1.4f;

        [Tooltip("A waypoint counts as reached within this distance of the feet.")]
        [SerializeField, Min(0.01f)] private float arriveDistance = 0.1f;

        [Tooltip("A crawl that has not arrived after this long is finished where it was headed, so a " +
                 "snag can never leave the player stuck with gravity off.")]
        [SerializeField, Min(0.5f)] private float maxCrawlSeconds = 8f;

        private Rigidbody body;
        private PlayerMovement movement;
        private PlayerStance stance;

        private Vector3[] path;
        private int next;
        private float startedAt;
        private float feetBelowPivot;
        private bool gravityBefore;
        private Collider[] passThrough;
        private Collider[] ownColliders;
        private Action finished;

        public bool IsCrawling => path != null;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            movement = GetComponent<PlayerMovement>();
            stance = GetComponent<PlayerStance>();
        }

        /// <summary>
        /// Crawl the owner's body through `waypoints` (floor points, feet on them). `hull` is the set of
        /// colliders the path passes through. `onFinished` runs once, however the crawl ends.
        /// Refused while already crawling, on a machine that does not own this player, or with no path.
        /// </summary>
        public bool TryCrawl(Vector3[] waypoints, Collider[] hull, Action onFinished)
        {
            if (IsCrawling || waypoints == null || waypoints.Length == 0 || !Network.Owns(this)) return false;

            path = waypoints;
            next = 0;
            startedAt = Time.time;
            finished = onFinished;
            feetBelowPivot = body.position.y - movement.BodyCapsule.bounds.min.y;
            gravityBefore = body.useGravity;
            body.useGravity = false;
            movement.SetClimbing(true);
            if (stance != null) stance.HoldCrouch(true);
            ownColliders = GetComponentsInChildren<Collider>();
            passThrough = hull ?? Array.Empty<Collider>();
            SetIgnored(true);
            return true;
        }

        private void FixedUpdate()
        {
            if (!IsCrawling) return;

            Vector3 target = path[next] + Vector3.up * feetBelowPivot;
            Vector3 to = target - body.position;
            if (to.magnitude <= arriveDistance && ++next >= path.Length || Time.time - startedAt > maxCrawlSeconds)
            {
                Arrive();
                return;
            }

            target = path[next] + Vector3.up * feetBelowPivot;
            to = target - body.position;
            float step = crawlSpeed * Time.fixedDeltaTime;
            body.linearVelocity = to.magnitude <= step ? to / Time.fixedDeltaTime : to.normalized * crawlSpeed;
        }

        private void Arrive()
        {
            Vector3 end = path[path.Length - 1] + Vector3.up * feetBelowPivot;
            Release();
            SaveTeleport.Move(gameObject, end, transform.rotation);
        }

        /// <summary>Hand the body back. Safe to call when not crawling.</summary>
        private void Release()
        {
            if (!IsCrawling) return;

            path = null;
            SetIgnored(false);
            body.useGravity = gravityBefore;
            movement.SetClimbing(false);
            if (stance != null) stance.HoldCrouch(false);
            Action done = finished;
            finished = null;
            done?.Invoke();
        }

        private void SetIgnored(bool ignore)
        {
            foreach (Collider mine in ownColliders)
            {
                if (mine == null) continue;
                foreach (Collider theirs in passThrough)
                {
                    if (theirs != null) Physics.IgnoreCollision(mine, theirs, ignore);
                }
            }
        }

        /// <summary>A teleport is never a crawl: whatever moved the player took them out of the hatch.</summary>
        public void OnTeleported(in TeleportMove move) => Release();

        private void OnDisable() => Release();
    }
}
