using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>
    /// One record of what a body was before anything picked it up, shared by everything that picks
    /// bodies up.
    ///
    /// <para>
    /// <b>This exists because two carriers can hold the same body, and used to.</b> A player rides
    /// the arrival down in a <c>SeatedRider</c> chair — kinematic, gravity off — and then takes the
    /// helm of the same ship through <c>MountModule</c>. Each carrier captured "what the body was"
    /// at the moment IT arrived, so the mount banked the SEATED state as the truth and handed it
    /// back on dismount: the player was returned to the world kinematic and weightless, which reads
    /// as a character that cannot move and falls up. Capturing once, on the first hold, and
    /// restoring once, on the last release, is the only arrangement in which nesting cannot invent a
    /// state the body was never in.
    /// </para>
    ///
    /// <para>
    /// It is also the answer to a question the rest of the project asks badly.
    /// <c>PlayerMovement.EnsureMovableBody</c> and <c>UnderTerrainGuard</c> both need to know
    /// whether a body is being carried, and both phrased it as "does it have a parent" — which is
    /// true of a mounted rider and false of a seated one, because the player's NetworkTransform is
    /// owner-authoritative and world-space and so a rider is carried by pose, not by parenting.
    /// A seated player was therefore released from kinematic by its own movement component, every
    /// physics step, while the seat was still holding it.
    /// </para>
    ///
    /// <para>
    /// <b>Not every claim is a full freeze, and the record is what makes that safe.</b>
    /// <c>UnderTerrainGuard</c> parks a body by taking its gravity away and pinning its position,
    /// leaving it otherwise under physics — see <see cref="SuspendGravity"/>. That is a claim on the
    /// same three flags, so it goes through the same record: a guard that remembered
    /// <c>useGravity</c> privately handed a park to whoever picked the body up next, who banked
    /// "weightless" as this body's normal state and gave it back on release. That is a player who
    /// walks on air, and it is the crash landing's version of the bug the paragraph above describes.
    /// </para>
    ///
    /// <para>
    /// Static, because the two carriers live on different components and neither owns the body. Keyed
    /// on the <see cref="Rigidbody"/> rather than the GameObject so a holder cannot register one and
    /// release the other.
    /// </para>
    /// </summary>
    public static class CarriedBody
    {
        private class Record
        {
            public bool WasKinematic;
            public bool HadGravity;
            public RigidbodyInterpolation Interpolation;
            public readonly HashSet<object> Holders = new();
        }

        private static readonly Dictionary<Rigidbody, Record> s_held = new();

        /// <summary>
        /// Dropped between play sessions. Static state outlives a play mode entered without a domain
        /// reload, and a record left over from the last session describes a body that no longer
        /// exists — or worse, one that has been recreated and would be "restored" to a stranger's
        /// physics.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => s_held.Clear();

        /// <summary>
        /// Take a body over: freeze it where it is put, remembering what it was if nobody else
        /// already has.
        ///
        /// <para>
        /// Idempotent per holder, as every replicated apply in this project is required to be — a
        /// carrier that re-asserts its hold every frame must not deepen it.
        /// </para>
        /// </summary>
        public static void Hold(GameObject body, object holder)
        {
            Rigidbody rb = Claim(body, holder);
            if (rb == null) return;

            // Velocity first: writing it to a body that is already kinematic is a Unity warning and
            // a no-op, so the speed it was carrying would be reapplied under the next teleport and
            // show up as a shudder.
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            rb.isKinematic = true;
            rb.useGravity = false;

            // Interpolation renders a body from where physics had it one step ago, and a step ago is
            // a long way back when the carrier is a ship flying a descent. The rider visibly shakes
            // loose of the chair without this.
            rb.interpolation = RigidbodyInterpolation.None;
        }

        /// <summary>
        /// Take a body's weight away and nothing else: it stays dynamic, keeps its velocity and goes
        /// on colliding, it simply stops falling.
        ///
        /// <para>
        /// For <c>UnderTerrainGuard</c>'s park, which holds a body still at its own X/Z while the
        /// chunk under it loads. It pins the position itself, every physics step, so a full
        /// <see cref="Hold"/> would be wrong twice over — a kinematic chassis with jointed legs and
        /// wheels behaves nothing like a dynamic one, and the guard is on every vehicle in the game.
        /// Gravity is the only flag it has ever needed, because an articulated machine held by its
        /// chassis alone tears itself apart as its parts fall independently.
        /// </para>
        /// <para>
        /// It is still a CLAIM, registered in the same record and ended by the same
        /// <see cref="Release"/>, and that is the whole point: whoever picks the body up next finds
        /// the state it had before the park rather than the park itself.
        /// </para>
        /// </summary>
        public static void SuspendGravity(GameObject body, object holder)
        {
            Rigidbody rb = Claim(body, holder);
            if (rb == null) return;

            rb.useGravity = false;
        }

        /// <summary>
        /// Register <paramref name="holder"/> against <paramref name="body"/>, capturing what the
        /// body was if nobody else already has. Returns the body to write, or null when there is
        /// nothing to claim.
        /// </summary>
        private static Rigidbody Claim(GameObject body, object holder)
        {
            Rigidbody rb = Resolve(body);
            if (rb == null || holder == null) return null;

            if (!s_held.TryGetValue(rb, out Record hold))
            {
                hold = new Record
                {
                    WasKinematic = rb.isKinematic,
                    HadGravity = rb.useGravity,
                    Interpolation = rb.interpolation,
                };
                s_held[rb] = hold;
            }

            hold.Holders.Add(holder);
            return rb;
        }

        /// <summary>
        /// Let go. The body gets its own physics back only once the LAST holder has released it, and
        /// gets back what it was before the FIRST one took it.
        /// </summary>
        public static void Release(GameObject body, object holder)
        {
            Rigidbody rb = Resolve(body);
            if (rb == null || holder == null) return;
            if (!s_held.TryGetValue(rb, out Record hold)) return;

            hold.Holders.Remove(holder);
            if (hold.Holders.Count > 0) return;

            s_held.Remove(rb);

            rb.isKinematic = hold.WasKinematic;
            rb.useGravity = hold.HadGravity;
            rb.interpolation = hold.Interpolation;

            // Handed back at rest rather than carrying whatever the ride implied, so nobody is flung
            // across the cabin the instant they get their weight back.
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// Drop every claim this holder has, without restoring anything.
        ///
        /// <para>
        /// For teardown, where the carrier or the body is being destroyed and every restore would
        /// reach into a doomed object. A holder that simply forgot would leave its claim standing
        /// forever and the body could never be handed back — see <c>MountModule.AbandonRider</c>.
        /// </para>
        /// </summary>
        public static void Abandon(object holder)
        {
            if (holder == null) return;

            var emptied = new List<Rigidbody>();

            foreach (KeyValuePair<Rigidbody, Record> entry in s_held)
            {
                if (!entry.Value.Holders.Remove(holder)) continue;
                if (entry.Value.Holders.Count == 0) emptied.Add(entry.Key);
            }

            // A body whose last holder abandoned it is dropped from the record but NOT restored: the
            // holder gave up precisely because touching it is unsafe. If it survives, whoever picks
            // it up next captures it fresh, which is the correct answer for a body nothing owns.
            foreach (Rigidbody rb in emptied) s_held.Remove(rb);
        }

        /// <summary>
        /// Is something carrying this body? The question <c>PlayerMovement</c> and
        /// <c>UnderTerrainGuard</c> have to ask before deciding a frozen body is a fault.
        /// </summary>
        public static bool IsHeld(GameObject body)
        {
            Rigidbody rb = Resolve(body);
            return rb != null && s_held.ContainsKey(rb);
        }

        /// <summary>
        /// Is anybody OTHER than <paramref name="holder"/> carrying this body?
        ///
        /// <para>
        /// The question a holder has to ask about a body it is itself holding.
        /// <c>UnderTerrainGuard</c> stands aside for anything else that has taken a body — a seat, a
        /// saddle — but it is a holder too now, and asking <see cref="IsHeld"/> from inside its own
        /// park would answer "yes, me" and leave it holding a body forever.
        /// </para>
        /// </summary>
        public static bool IsHeldByOther(GameObject body, object holder)
        {
            Rigidbody rb = Resolve(body);
            if (rb == null || !s_held.TryGetValue(rb, out Record hold)) return false;

            foreach (object other in hold.Holders)
                if (!ReferenceEquals(other, holder)) return true;

            return false;
        }

        private static Rigidbody Resolve(GameObject body) =>
            body != null ? body.GetComponent<Rigidbody>() : null;
    }
}
