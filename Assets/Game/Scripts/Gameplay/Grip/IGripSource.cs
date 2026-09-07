using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Something that scales how well a body can hold onto the ground.
    ///
    /// <para>
    /// Two unrelated things want to answer this question and neither of them is movement's
    /// business: a condition riding on the body itself (the Slick status), and a coat painted on
    /// the surface under it (a slick film, ice, wet sand). Both are "your feet cannot get
    /// purchase", so both answer the same interface and a mover never learns which one it is
    /// talking to (GDC-L1-SYS-0005 — one system per job, and the two would otherwise be two).
    /// </para>
    /// <para>
    /// A source is asked, it does not push. It never writes a velocity, which is what keeps the
    /// player's own movement owner-authoritative: the server owns the flag, the machine that owns
    /// the body reads it and applies it on the frame it is read (GDC-L1-MP-0004, GDC-L1-FEEL-0002).
    /// A server that wrote the player's velocity instead would be overwritten within a tick with
    /// nothing in the console.
    /// </para>
    /// </summary>
    public interface IGripSource
    {
        /// <summary>
        /// How much grip this source leaves <paramref name="body"/> standing at
        /// <paramref name="groundPoint"/>, as a multiplier of normal.
        ///
        /// <para>
        /// Return <see cref="GroundGrip.Full"/> for a body this source has nothing to say about —
        /// every source is asked about every mover, so "not mine" is the common answer and must be
        /// cheap. Anything below 1 is a reduction; 0 is frictionless.
        /// </para>
        /// </summary>
        float GripFor(GameObject body, Vector3 groundPoint);
    }
}
