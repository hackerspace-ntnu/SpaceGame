// The one question every mover in this game asks about the ground: how much of it can I use?
//
// It lives in a leaf assembly of its own, with no references at all, precisely so that everything
// that moves can consume it. The player is in Assembly-CSharp, legged rigs are in
// SpaceGame.Locomotion and every vehicle is in an assembly of its own, and an asmdef cannot
// reference Assembly-CSharp — so a seam that lived with the player would have been a seam only the
// player could ask. An assembly that depends on nothing can be referenced by all of them.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The registry of everything currently scaling somebody's grip, and the query that resolves it.
    ///
    /// <para>
    /// Sources register while they are acting and unregister when they stop, so the list holds the
    /// handful of live slicks in the world rather than an entry per body — a mover asking this
    /// question on an ordinary day walks a list of length zero and gets <see cref="Full"/> back.
    /// </para>
    /// <para>
    /// <b>Ask it only while grounded.</b> A body in mid-air is not standing on the patch below it,
    /// and a mover that reads one anyway skids through the air over a slick pool it has jumped
    /// clean over.
    /// </para>
    /// </summary>
    public static class GroundGrip
    {
        /// <summary>Ordinary ground: the multiplier that changes nothing.</summary>
        public const float Full = 1f;

        private static readonly List<IGripSource> Sources = new List<IGripSource>();

        // Statics outlive the world, the session and play mode — enter-play-mode options are on in
        // this project, so a list left holding last session's slicks would answer for bodies that
        // no longer exist.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession() => Sources.Clear();

        /// <summary>
        /// Start answering for <paramref name="source"/>. Idempotent: registering twice would have
        /// it asked twice, which for a minimum costs nothing and for anything else would be a bug
        /// waiting to be written.
        /// </summary>
        public static void Add(IGripSource source)
        {
            if (source == null || Sources.Contains(source)) return;
            Sources.Add(source);
        }

        /// <summary>
        /// Stop answering for <paramref name="source"/>. Safe for one that was never added — a
        /// status cleared twice, a patch that expired on the same frame it was broken.
        /// </summary>
        public static void Remove(IGripSource source)
        {
            if (source != null) Sources.Remove(source);
        }

        /// <summary>
        /// How much grip <paramref name="body"/> has, standing at <paramref name="groundPoint"/>.
        /// 1 is ordinary ground and 0 is frictionless.
        ///
        /// <para>
        /// The <b>smallest</b> multiplier wins rather than the product of them, and that is a
        /// design decision rather than an implementation shortcut: standing slicked on a slicked
        /// floor is as slippery as the slipperier of the two, not twenty times worse. Multiplying
        /// would let two ordinary effects compose into a state with no way out of it, which is the
        /// "locked into a predetermined loss" that GDC-L1-BAL-0004 rules out — a bad situation the
        /// player can still act inside is counterplay; one they cannot is a sentence.
        /// </para>
        /// </summary>
        public static float For(GameObject body, Vector3 groundPoint)
        {
            if (body == null || Sources.Count == 0) return Full;

            float grip = Full;
            for (int i = 0; i < Sources.Count; i++)
            {
                float reported = Sources[i].GripFor(body, groundPoint);
                if (reported < grip) grip = reported;
            }

            return grip < 0f ? 0f : grip;
        }
    }
}
