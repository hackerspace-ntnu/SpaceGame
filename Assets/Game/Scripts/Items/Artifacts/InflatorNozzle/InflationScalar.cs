// The signed inflation scalar, read and stepped in one place.
//
// Separated from the artifact because it is the part of the pump with no Unity state in it: given
// what a body is presenting now and what this nozzle pumps toward, it says what the next instant
// looks like. That makes it readable on its own, and it means the three machines that each need
// the same number — the owner timing its pop, the server raising the pressure, and every machine
// driving the dial — read it through one function rather than three copies that drift.
using UnityEngine;
using SpaceGame.Gameplay.Status;

namespace SpaceGame.Items
{
    /// <summary>
    /// The arithmetic of pumping something up, or down.
    ///
    /// <para>
    /// <b>The scalar is signed and the item is a sign.</b> −1 is small and heavy, +1 is big and
    /// light, and <see cref="InflatedStatus"/> turns whichever one it is given into scale and mass.
    /// So the ballast half of this property is this same code with <c>toward</c> negative rather
    /// than a second implementation (<c>GDC-L1-SYS-0005</c>), and every function here is written in
    /// terms of a direction rather than of "bigger".
    /// </para>
    /// <para>
    /// <b>Nothing here holds the value.</b> The body does, in its replicated status, which is what
    /// lets two players pump the same crate cooperatively and what lets a machine that joined
    /// halfway through land on the same number as everyone else.
    /// </para>
    /// </summary>
    public static class InflationScalar
    {
        /// <summary>
        /// The inflation <paramref name="body"/> is actually presenting right now, −1…+1.
        ///
        /// <para>
        /// The replicated magnitude scaled by how much of the condition's own clock is left, which
        /// is exactly what <see cref="InflatedStatus"/> draws — so a pump that reads this and
        /// re-applies the result holds a body at size, and one that stops leaves the clock to ease
        /// it back down. Reading the bare magnitude instead would snap a half-deflated body back to
        /// full the moment somebody touched it again.
        /// </para>
        /// </summary>
        public static float Presented(StatusReceiver body)
        {
            if (body == null) return 0f;

            return Mathf.Clamp(body.MagnitudeOf(StatusKind.Inflated), -1f, 1f)
                   * body.RemainingShare(StatusKind.Inflated);
        }

        /// <summary>
        /// How far along its own range a nozzle pumping toward <paramref name="toward"/> has got a
        /// body sitting at <paramref name="scalar"/>, 0…1.
        ///
        /// <para>
        /// A share rather than the raw scalar, so the dial, the pop threshold and any future
        /// ballast item all read one number that means the same thing in both directions. A body
        /// pumped the OTHER way reads zero rather than a negative share: this nozzle has made no
        /// progress on it, which is the honest answer for a needle and for a threshold alike.
        /// </para>
        /// </summary>
        public static float Progress(float scalar, float toward) =>
            Mathf.Abs(toward) < 1e-4f ? 0f : Mathf.Clamp01(scalar / toward);

        /// <summary>
        /// Where <paramref name="scalar"/> gets to after <paramref name="deltaTime"/> of pumping
        /// toward <paramref name="toward"/>, given that a full stroke takes
        /// <paramref name="secondsToFull"/>.
        ///
        /// <para>
        /// A rate over the whole range rather than a rate per unit, so the seconds-to-full figure
        /// on the prefab is the number a designer actually tunes against — and so a nozzle tuned to
        /// half the range still fills its own range in that many seconds.
        /// </para>
        /// </summary>
        public static float Pumped(float scalar, float toward, float secondsToFull, float deltaTime)
        {
            float limit = Mathf.Clamp(toward, -1f, 1f);
            if (secondsToFull <= 0f) return limit;

            return Mathf.MoveTowards(scalar, limit,
                                     Mathf.Abs(limit) / secondsToFull * deltaTime);
        }

        /// <summary>
        /// May this body be resized at all?
        ///
        /// <para>
        /// Only a body PhysX can push. Resizing grows or shrinks a body's colliders, and one that
        /// grows into a wall is depenetrated back out of it — which is free for anything dynamic. A
        /// KINEMATIC body cannot be depenetrated and cannot be lifted by the buoyancy either, so
        /// resizing one would end with a creature welded halfway into a cliff and no way out of it.
        /// That case is not hypothetical: mounting makes a rider's body kinematic, a ragdoll pins
        /// one to hold a body down, and a parked vehicle is one all the time. A body with no
        /// Rigidbody at all is refused for the same reason — there is nothing to resolve the
        /// overlap it would make.
        /// </para>
        /// <para>
        /// Refusing a mounted rider is also the anti-griefing half of this rule
        /// (<c>GDC-L1-MP-0002</c>): a player strapped into a seat cannot be resized out of it.
        /// </para>
        /// <para>
        /// It lives here rather than on either item because it is a fact about the PROPERTY, not
        /// about the tool reaching for it — and because the moment a second item pumped this scalar
        /// there were two copies of it, which is two places for the rule to drift.
        /// </para>
        /// </summary>
        public static bool CanResize(StatusReceiver body)
        {
            if (body == null) return false;

            // From the parent, like everything else that resolves a body off the collider an aim
            // happened to hit: the Rigidbody sits on the root and the receiver may not.
            Rigidbody weighted = body.GetComponentInParent<Rigidbody>();
            return weighted != null && !weighted.isKinematic;
        }
    }
}
