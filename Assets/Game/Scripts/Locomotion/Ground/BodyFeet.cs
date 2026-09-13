// Where a character's feet are, for anything that has to meet the ground under one.
//
// The trap this exists for: a character's pivot is not necessarily at their soles. This project's
// player is authored with the capsule slung about a metre below the transform every other system
// measures from, so "how far above the ground is this body" answered from `transform.position` is
// out by a metre — which is wider than any ground-contact band in the game, and therefore not a
// number that is slightly wrong. It is a gate that never opens, with nothing logged and nothing
// thrown. DeckBoarding found it first ("a hardcoded offset would bury them to the knees or drop
// them from a height"); the jumping rod found it again from scratch, which is what put the
// measurement here instead of in either of them.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    /// <summary>
    /// How far a body's pivot sits above its soles, measured from the body's own colliders.
    ///
    /// <para>
    /// Measured rather than authored, because a hardcoded drop is right for exactly one character
    /// and quietly wrong for the next one to use the same code. Built once per body and queried
    /// afterwards: the collider sweep is the expensive half and the shape of a body does not
    /// change, even though its dimensions do — see <see cref="RootAboveFeet"/>.
    /// </para>
    /// </summary>
    public sealed class BodyFeet
    {
        private static readonly Collider[] None = new Collider[0];

        private readonly Transform body;
        private readonly Collider[] colliders;

        public BodyFeet(Transform body)
        {
            this.body = body;
            colliders = body != null ? body.GetComponentsInChildren<Collider>(true) : None;
        }

        /// <summary>
        /// Distance from the pivot down to the lowest point of the body's solid colliders, never
        /// negative.
        ///
        /// <para>
        /// Re-measured on every read rather than cached, because a body's dimensions move under
        /// it: <c>PlayerStance</c> shortens the capsule to crouch. It happens to lower the centre
        /// by half of what the height lost, so the soles stay put — but a cached figure would be
        /// relying on that, and the read is a walk over two or three colliders.
        /// </para>
        /// </summary>
        public float RootAboveFeet
        {
            get
            {
                if (body == null) return 0f;

                float drop = 0f;
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider c = colliders[i];

                    // Triggers are reach, not shape — an interaction volume around the ankles is
                    // not something the body stands on. A disabled collider is not there at all,
                    // which is what every held item's colliders become when they are equipped.
                    if (c == null || !c.enabled || c.isTrigger) continue;
                    if (!c.gameObject.activeInHierarchy) continue;

                    drop = Mathf.Max(drop, body.position.y - c.bounds.min.y);
                }

                return drop;
            }
        }
    }
}
