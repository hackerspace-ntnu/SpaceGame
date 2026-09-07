// What may go in a container at all: the size gate, and the rule about riders.
//
// Both questions are asked BEFORE a capture starts rather than discovered during one, because a
// capture is destructive. Half a capture is a creature that has been despawned and not recorded.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay.Ragdoll;

namespace SpaceGame.Gameplay.Containment
{
    /// <summary>
    /// Does this body fit in this container, and is it whole?
    ///
    /// <para>
    /// Static and stateless: the gate is asked from the aim pass while a container is being held,
    /// from the capture itself, and from EditMode tests, and none of those share an object.
    /// </para>
    /// </summary>
    public static class ContainmentFit
    {
        /// <summary>
        /// The body a hit belongs to — the thing the save system would address, not the collider.
        ///
        /// <para>
        /// <b>Not <c>GetComponentInParent&lt;Rigidbody&gt;()</c>.</b> A body that has gone limp is
        /// a built ragdoll whose every bone carries a Rigidbody and a collider of its own
        /// (<c>RagdollRig.BuildBone</c>), so the nearest Rigidbody to a ray that hit a downed
        /// creature is that creature's FOREARM. Bottling it would record a bone. The same reasoning
        /// <c>Hogtie.BodyOf</c> gives, reached through a different component: a
        /// <see cref="SaveableEntity"/> is exactly the unit a record describes, and ragdoll bones
        /// never carry one.
        /// </para>
        /// <para>
        /// Falls back to the transform root when there is no entity at all. That body cannot
        /// actually be contained — <see cref="TryFit"/> refuses it by name rather than by silence —
        /// but naming the root is what makes the refusal readable.
        /// </para>
        /// </summary>
        public static GameObject BodyOf(Component hit)
        {
            if (hit == null) return null;

            SaveableEntity entity = hit.GetComponentInParent<SaveableEntity>();
            return entity != null ? entity.gameObject : hit.transform.root.gameObject;
        }

        /// <summary>
        /// Is this a player? Players are held for a few seconds, never recorded — see
        /// <see cref="BottledPlayer"/>.
        ///
        /// Asked through <see cref="PlayerRagdoll"/> rather than through a tag or a save scope
        /// because that component is the thing the hold actually needs, so a body that answers yes
        /// here is by construction a body the hold can be applied to.
        /// </summary>
        public static bool IsPlayer(GameObject body) =>
            body != null && body.GetComponentInParent<PlayerRagdoll>() != null;

        /// <summary>
        /// The NPC in this body's saddle, or null. A player rider is NOT reported here — see
        /// <see cref="HasPlayerRider"/>, which is a refusal rather than a thing to carry along.
        /// </summary>
        public static GameObject RiderOf(GameObject body)
        {
            if (body == null) return null;

            NpcPassenger passenger = body.GetComponent<NpcPassenger>();
            return passenger != null ? passenger.Rider : null;
        }

        /// <summary>Is a player sitting on this body right now?</summary>
        public static bool HasPlayerRider(GameObject body)
        {
            if (body == null) return false;

            MountModule mount = body.GetComponent<MountModule>();
            return mount != null && mount.IsMounted;
        }

        /// <summary>
        /// The world-space box this body and everything on it occupies.
        ///
        /// <para>
        /// Built from COLLIDERS rather than renderers, because the collider is what the game
        /// treats as the shape of a thing — a creature whose fur mesh doubles its silhouette is
        /// not twice as hard to bottle — and because a renderer bound is inflated by every particle
        /// system and trail the body is dragging behind it.
        /// </para>
        /// <para>
        /// Triggers are skipped. They are interaction volumes, not bodies: a mount's mount-me
        /// trigger routinely stands a metre proud of the animal on every side, and counting one
        /// would have the gate measure the invitation rather than the guest.
        /// </para>
        /// <para>
        /// Disabled colliders are skipped too, and that is deliberate rather than incidental. A
        /// disabled collider is already absent from every physics query in the project, so counting
        /// one here would make the gate disagree with the aim that selected the target.
        /// </para>
        /// </summary>
        /// <returns>False when the body has no measurable shape at all.</returns>
        public static bool TryMeasure(GameObject body, out Bounds bounds)
        {
            bounds = default;
            if (body == null) return false;

            bool any = false;

            foreach (Collider collider in body.GetComponentsInChildren<Collider>())
            {
                if (collider == null || collider.isTrigger || !collider.enabled) continue;

                if (!any)
                {
                    bounds = collider.bounds;
                    any = true;
                    continue;
                }

                bounds.Encapsulate(collider.bounds);
            }

            return any;
        }

        /// <summary>The volume of a bounding box, in cubic metres.</summary>
        public static float VolumeOf(Bounds bounds) =>
            Mathf.Abs(bounds.size.x * bounds.size.y * bounds.size.z);

        /// <summary>
        /// Everything that is about to be taken, carrier first — so the gate measures the group
        /// and the capture records the group.
        ///
        /// One list, built once, rather than each caller walking the saddle again: the rider is a
        /// child of the mount at capture time and a sibling of it a moment later, and a second walk
        /// taken at the wrong moment finds a different answer.
        /// </summary>
        public static List<GameObject> Group(GameObject body)
        {
            var group = new List<GameObject> { body };

            GameObject rider = RiderOf(body);
            if (rider != null) group.Add(rider);

            return group;
        }

        /// <summary>
        /// May this container take this body, right now?
        ///
        /// <para>
        /// <paramref name="refusal"/> is a player-facing sentence, not a log line: every "no" here
        /// is something the person holding the container is entitled to be told. A use that
        /// silently does nothing is indistinguishable from an item that is broken, which is the
        /// feedback half of GDC-L1-UX-0003 — the container's own presentation decides how the
        /// sentence is shown, but the reason has to exist for it to show one.
        /// </para>
        /// <para>
        /// <b>A ridden mount is refused outright rather than separated.</b> Rider and mount are one
        /// capture or no capture. An NPC rider goes in with the animal, because both are records
        /// and the pair can be put back exactly as it was. A PLAYER in the saddle cannot: a player
        /// is never a record, and taking the animal out from under them would separate the two
        /// silently, which is the one outcome the design rules out.
        /// </para>
        /// </summary>
        public static bool TryFit(GameObject body, ContainmentSettings settings, out string refusal)
        {
            refusal = null;

            if (body == null)
            {
                refusal = "Nothing there.";
                return false;
            }

            if (settings == null) settings = new ContainmentSettings();

            if (HasPlayerRider(body))
            {
                refusal = "Somebody is riding that.";
                return false;
            }

            if (!TryMeasure(body, out Bounds bounds))
            {
                refusal = $"{body.name} has no solid shape to draw in.";
                return false;
            }

            // The rider is measured WITH the mount rather than instead of it. Encapsulating both
            // boxes is what makes the rated volume mean "the whole of what comes out again".
            GameObject rider = RiderOf(body);
            if (rider != null && TryMeasure(rider, out Bounds riderBounds)) bounds.Encapsulate(riderBounds);

            float volume = VolumeOf(bounds);
            if (volume > settings.RatedVolume)
            {
                refusal = $"{body.name} is too big for this container.";
                return false;
            }

            // Every part of the group has to be recordable, and the check is here rather than at
            // the moment of despawn for the reason this file exists: a capture that discovers
            // halfway through that the rider names no prefab has already despawned the mount.
            foreach (GameObject part in Group(body))
            {
                if (IsPlayer(part)) continue;

                if (!Captivity.CanRecord(part, out string why))
                {
                    refusal = why;
                    return false;
                }
            }

            return true;
        }
    }
}
