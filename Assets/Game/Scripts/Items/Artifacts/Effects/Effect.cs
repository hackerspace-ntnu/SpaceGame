using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// One timed change to a Rigidbody: what it does when it starts, every step it is running, and
    /// what it undoes when it stops.
    /// </summary>
    public class Effect
    {
        public float timer;
        public System.Action<Rigidbody> applyEffect;
        public System.Action<Rigidbody> onTick;
        public System.Action<Rigidbody> stopEffect;

        /// <summary>
        /// What this effect IS, so a second one of the same kind can replace it rather than run
        /// beside it. Null means "stacks with everything", which is right for a one-off nudge and
        /// wrong for anything that toggles a flag on and back off.
        ///
        /// <para>
        /// The reason this is not simply a field on the item that created it: the item is usually
        /// gone. A single-use potion is consumed and destroyed by
        /// <see cref="EquipmentController"/> the moment the server counts the use, while its
        /// five-second effect is still running, and the next potion is a fresh instance that has
        /// never heard of it. The key lives on the effect so the manager — which outlives both —
        /// is the one that can tell them apart.
        /// </para>
        /// </summary>
        public object Key;

        /// <summary>
        /// The name this effect can be REBUILT under, or null for one that cannot be.
        ///
        /// <para>
        /// <see cref="Key"/> cannot serve: it is a <c>System.Type</c>, and even written out as a
        /// name it would only say what created the effect, never how to make another one — the
        /// behaviour lives in three <c>System.Action</c> delegates, and a delegate closing over a
        /// local is not something a file can hold.
        /// </para>
        /// <para>
        /// So a saved effect stores this token plus its remaining timer, and
        /// <see cref="EffectCatalog"/> turns the token back into a fresh set of delegates. Fresh is
        /// also correct rather than merely convenient: the anti-gravity potion's <c>onApply</c>
        /// captures the body's gravity flag as it finds it, so an effect rebuilt on a loaded body
        /// records that body's real resting state instead of one remembered from last session.
        /// </para>
        /// <para>
        /// Null means "this one does not survive a reload", which is a legitimate answer for a
        /// one-off nudge and is what any effect registered through the raw delegate overload gets.
        /// </para>
        /// </summary>
        public string SaveToken;

        public Effect(float duration)
        {
            timer = duration;
        }
    }
}
