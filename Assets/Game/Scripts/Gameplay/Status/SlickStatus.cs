using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// A film of frost on the body itself: it cannot get purchase on anything, and nothing anyone
    /// throws will get purchase on it. What the cryo sprayer leaves on a body from the first touch
    /// of the plume, ten seconds longer than the freeze that follows it.
    ///
    /// <para>
    /// This is the body half of a pair. The ground half is a surface coat, and both answer
    /// <see cref="IGripSource"/> so that a mover asking "how much grip do I have" never learns which
    /// of the two is the reason. That single question is also what makes the film work for a walker,
    /// a wheel and a leg alike instead of only for the player.
    /// </para>
    /// <para>
    /// <b>The status does not push the body.</b> It registers an answer and waits to be asked, on
    /// whichever machine owns the mover. That is what keeps a slicked player's own movement
    /// owner-authoritative: the server owns the flag (GDC-L1-MP-0004) and the owner applies it on
    /// the frame it reads it, so the skid starts immediately rather than after a round trip
    /// (GDC-L1-FEEL-0002). A server that wrote the player's velocity instead would have it
    /// overwritten within a tick, silently.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class SlickStatus : StatusBehaviour, IGripSource
    {
        /// <summary>Twenty seconds, and twice what a freeze is worth: the film outlives it.</summary>
        private const float DefaultDuration = 20f;

        public SlickStatus() : base(DefaultDuration) { }

        [Tooltip("Grip left while slicked, as a share of normal. At 0.03 a body can still steer " +
                 "but can barely accelerate or brake, which is what being covered in frost IS — " +
                 "0 would be a body that can do nothing at all about where it is going.")]
        [SerializeField, Range(0f, 1f)] private float grip = 0.03f;

        /// <summary>
        /// The body this film is on, held only while the condition runs. Registration is what makes
        /// the film readable by movers, and it is given back in <see cref="OnCleared"/> — which the
        /// receiver also reaches when it is disabled, so a destroyed body cannot leave an answer
        /// behind in a static list.
        /// </summary>
        private Transform coated;

        public override StatusKind Kind => StatusKind.Slick;

        public override void OnApplied(StatusReceiver body)
        {
            coated = body.transform;
            GroundGrip.Add(this);
        }

        public override void OnCleared(StatusReceiver body)
        {
            GroundGrip.Remove(this);
            coated = null;
        }

        public float GripFor(GameObject body, Vector3 groundPoint)
        {
            if (coated == null || body == null) return GroundGrip.Full;

            // IsChildOf is true of the transform itself, so this covers both "the mover is the
            // coated body" and "the mover is a part of it" — a legged rig asks from its own root
            // and a player asks from the object the capsule is on.
            return body.transform.IsChildOf(coated) ? grip : GroundGrip.Full;
        }
    }
}
