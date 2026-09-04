using System;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// The view used while arranging gear on your own body: in front of the chest, level, narrow.
    ///
    /// <para>
    /// The shot is on the body's flattened FORWARD, looking back at it — the camera goes to where
    /// the player faces; the body never turns. Framed from the thighs up (the look target is the
    /// chest bone), so the two forearms and the shoulders, which are the targets, are large. FOV 40
    /// is the pack's narrow lens: flat perspective, honest sizes.
    /// </para>
    /// <para>
    /// If the player is facing a wall, a spherecast from the chest along the shot pulls the lens in
    /// to the wall, never nearer than <see cref="Shot.MinLensDistance"/>. The player's own
    /// colliders are always ignored — the probe starts inside them.
    /// </para>
    /// <para>
    /// <b>One heading, and it never turns.</b> The lens used to swing 180 degrees round to the pack
    /// whenever a back item was picked up, because the lash rail it clips to cannot be seen from
    /// the front. That premise is gone: the torso's ghost is a mount frame standing over the
    /// shoulders and its preview is the item's own WORN model — a pair of wings down both flanks —
    /// and both read from the front. The swing cost the player their bearings, hid the face, and
    /// took both forearm sites out of reach in the middle of a carry.
    /// </para>
    /// </summary>
    public sealed class BodyFocusCamera : FocusCamera
    {
        /// <summary>The authored shot. Serialized on <c>BodyFocusSession</c> so it is tuned in the Inspector, not in code.</summary>
        [Serializable]
        public struct Shot
        {
            [Tooltip("Metres in front of the look target, along the body's flattened forward.")]
            public float LensDistance;

            [Tooltip("Metres the lens sits above the look target.")]
            public float LensRise;

            [Tooltip("Degrees the lens looks down. Modest — the horizon should stay honest.")]
            public float PitchDown;

            public float FieldOfView;

            [Tooltip("Seconds the camera takes to fly from the eye to the shot.")]
            public float FlyInSeconds;

            [Tooltip("Nearest the lens is allowed when a wall pulls it in.")]
            public float MinLensDistance;

            [Tooltip("Radius of the wall probe. Also the clearance the lens keeps from whatever " +
                     "pulls it in, so it must stay comfortably larger than the camera's near plane.")]
            public float PullInRadius;

            [Tooltip("What can pull the lens in. The player's own colliders are always ignored.")]
            public LayerMask Blockers;

            /// <summary>
            /// The whole figure, from slightly above.
            ///
            /// <para>
            /// The shot used to frame the chest from 2.3 m at a 40 degree FOV, which covered
            /// 1.67 m of a 3.25 m astronaut — head and legs both cut off. Worn gear reaches from
            /// the shoulders to the knees now, so the screen has to show the whole person: 4.4 m
            /// at 44 degrees covers 3.55 m, and the lens is lifted half a metre above the chest
            /// and tipped 9 degrees down so the figure sits centred rather than bottom-cropped.
            /// </para>
            /// <para>
            /// These are also written into <c>PlayerCharacter.prefab</c>. `Shot` is a serialized
            /// struct, so the prefab's own YAML is what ships — this initializer only ever runs
            /// for a struct built fresh in code.
            /// </para>
            /// </summary>
            public static Shot Default => new()
            {
                LensDistance = 4.4f,
                LensRise = 0.50f,
                PitchDown = 9f,
                FieldOfView = 44f,
                FlyInSeconds = 0.4f,
                MinLensDistance = 0.9f,
                PullInRadius = 0.25f,
                Blockers = ~0,
            };
        }

        private Transform target;
        private Transform ignore;

        /// <summary>
        /// The shot's heading: the body's forward, flattened and frozen for the session.
        ///
        /// <para>
        /// Frozen rather than read live, and that is deliberate — a heading taken from the bone
        /// every frame would swing the lens whenever the body turned under it.
        /// </para>
        /// </summary>
        private Vector3 forward = Vector3.forward;

        private Shot shot;

        // A full buffer does not mean "that was all of them": SphereCastNonAlloc stops filling once
        // it is out of room and promises no order, so whichever hits it dropped are arbitrary —
        // including, sometimes, the nearest, which is the only one this reads. That is not a
        // theoretical worry here. The body screen is opened INSIDE the ship as often as out on the
        // sand, and the ship's hull is not one collider: PlayerShipBuilder mounts a baked proxy of
        // 420 separate convex hulls, and a sweep the length of a room crosses a great many of them.
        // Losing the nearest fails UNSAFE — the lens stays at its full distance, which is through
        // the bulkhead — so grow and re-cast rather than answer from a partial list, the same way
        // WalkerGround does for a leg looking for the floor.
        private RaycastHit[] hits = new RaycastHit[32];

        /// <summary>Ceiling on the growth, so a pathological scene cannot make this allocate without bound.</summary>
        private const int MaxHits = 512;

        /// <param name="lookTarget">What the lens looks at — the chest bone. Tracked live.</param>
        /// <param name="bodyForward">The body's forward; flattened here and frozen for the session.</param>
        /// <param name="ignoreRoot">The player's root: nothing under it can pull the lens in.</param>
        /// <param name="shot">The authored pose, read once — it is not a live tunable mid-session.</param>
        /// <param name="playerCamera">Switched off, with its AudioListener, for the duration.</param>
        public static BodyFocusCamera Spawn(Transform lookTarget, Vector3 bodyForward, Transform ignoreRoot, in Shot shot, Camera playerCamera)
        {
            if (lookTarget == null) return null;

            var go = new GameObject("BodyFocusCamera");
            var focus = go.AddComponent<BodyFocusCamera>();

            focus.target = lookTarget;
            focus.ignore = ignoreRoot;
            focus.shot = shot;

            // Flattened and frozen: the chest bone swings with every breath and step, and a shot
            // that took its heading from the live bone would swing with it. Only the POSITION
            // tracks the target.
            var flat = new Vector3(bodyForward.x, 0f, bodyForward.z);
            focus.forward = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;

            // Last, and only once every field the base reads is set: Begin asks for the shot's pose
            // immediately, to seed the flight and the depth of field.
            focus.Begin(playerCamera);
            return focus;
        }

        protected override bool HasTarget => target != null;
        protected override float PitchDown => shot.PitchDown;
        protected override float Fov => shot.FieldOfView;
        protected override float FlyInSeconds => shot.FlyInSeconds;

        // FlyInDelay is left at the base's 0. PackFocusCamera holds the player's own view for a
        // beat because the pack is still unfolding under it; nothing here is mid-animation, and a
        // pause before a screen that opens on a keypress reads as a stutter.

        /// <summary>Looking back down the forward the lens sits on.</summary>
        protected override float LensYaw() => Quaternion.LookRotation(-forward, Vector3.up).eulerAngles.y;

        protected override float FocusDistance() => Vector3.Distance(transform.position, target.position);

        protected override Vector3 LensPosition()
        {
            Vector3 origin = target.position;
            float distance = LensStandoff.Resolve(shot.LensDistance, NearestBlocker(origin), shot.PullInRadius, shot.MinLensDistance);

            // The rise is added after the standoff rather than probed for. It is smaller than the
            // probe's radius, so the raised lens is still inside the volume the sphere swept and
            // cannot have crept into anything the sweep cleared.
            return origin + forward * distance + Vector3.up * shot.LensRise;
        }

        /// <summary>
        /// Distance along the shot to the nearest SURFACE that is not the player, or
        /// <see cref="float.PositiveInfinity"/> when the way is clear.
        ///
        /// <para>
        /// The probe's radius is added back on, and that conversion is the whole point of this
        /// method. <c>RaycastHit.distance</c> from a sphere cast is how far the sphere's CENTRE
        /// travelled before contact, so for a wall across the shot it is already a radius short of
        /// the wall itself. <see cref="LensStandoff.Resolve"/> then subtracts a radius of its own
        /// for clearance; handing it the centre-travel distance raw would subtract the radius twice
        /// and stop the lens two radii out from a wall it was asked to come within one of — a
        /// framing error that stays inside the shot and so would never be reported as a bug.
        /// </para>
        /// </summary>
        private float NearestBlocker(Vector3 origin)
        {
            int count = Sweep(origin);
            while (count >= hits.Length && hits.Length < MaxHits)
            {
                hits = new RaycastHit[hits.Length * 2];
                count = Sweep(origin);
            }

            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                // The player's own colliders are skipped rather than masked out — the capsule is on
                // the Default layer, alongside plenty of what the lens must not pass through, so a
                // mask that excluded it would also excuse a real wall. Same trade PlayerLook makes
                // for the eye slide. IsChildOf is inclusive of the root itself, so it covers a
                // collider sitting on the root as well as everything under it.
                Collider hit = hits[i].collider;
                if (hit == null || (ignore != null && hit.transform.IsChildOf(ignore))) continue;

                // A hit at distance 0 is a sweep that started already overlapping, and it is taken
                // at face value: the floor below is what keeps it usable, and a lens left at full
                // distance inside geometry renders backfaces and shows the player nothing at all.
                // The player is very nearly the only thing that can produce one — their capsule is
                // radius 0.5 and this probe is 0.25, so an upright body keeps its chest a clear
                // quarter-metre from any surface it collides with, and the capsule itself is
                // filtered out above. Two cases get past that: a collider the player is allowed to
                // stand inside (Physics.IgnoreCollision pairs, which queries ignore — a portal
                // aperture), and a mounted player, whose root is a CHILD of the vehicle so the
                // vehicle's own hulls are not under `ignore`. Both land on the floor below, which
                // is the outcome this comment is really promising.
                nearest = Mathf.Min(nearest, hits[i].distance);
            }

            return nearest + shot.PullInRadius;
        }

        /// <summary>
        /// Swept exactly <see cref="Shot.LensDistance"/>, which is how far the lens itself travels:
        /// anything the sphere does not touch on the way out cannot be within a radius of the lens
        /// once it lands, and a longer sweep would only find blockers the standoff discards.
        /// </summary>
        private int Sweep(Vector3 origin) =>
            Physics.SphereCastNonAlloc(origin, shot.PullInRadius, forward, hits, shot.LensDistance,
                                       shot.Blockers, QueryTriggerInteraction.Ignore);
    }
}
