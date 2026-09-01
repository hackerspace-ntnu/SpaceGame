using UnityEngine;

namespace SpaceGame.Gear.JumpingRod
{
    /// <summary>
    /// The bounce, as pure arithmetic. No Rigidbody, no scene, no time — hand it numbers and it
    /// answers with numbers, so every rule below can be pinned by a test.
    ///
    /// <para>
    /// Where the rod HANGS is here too, rather than in the item that draws it, because it is the
    /// same measurement as the one that decides a landing: the tip sits one contact band under
    /// the holder's soles, so it reaches the ground on exactly the frame the bounce fires.
    /// </para>
    ///
    /// <para>
    /// The rod does not simulate a spring. It answers one question per landing — how fast does the
    /// player leave? — and the visible squash is a separate, purely cosmetic function of how close
    /// they are to the ground. A physically integrated coil with 0.11 m of travel passes its whole
    /// stroke in under one physics step at these speeds, so it would be invisible; and holding the
    /// player down for long enough to see it makes the rod feel sticky, which is the one thing a
    /// pogo must never be. GDC-L1-FEEL-0007: tune for the sensation, not for physical accuracy.
    /// </para>
    /// <para>
    /// What survives from the physical model is the part the player can feel: arrive harder, leave
    /// higher, up to a limit — and never lower than the height the rod is built to give.
    /// </para>
    /// </summary>
    public static class JumpingRodHopModel
    {
        /// <summary>
        /// Clearance under the holder's soles — the one measurement everything else here is
        /// expressed in.
        ///
        /// <para>
        /// <paramref name="rootAboveFeet"/> is the parameter that exists to stop this being got
        /// wrong, and it is not optional padding: a character's pivot is not necessarily at their
        /// feet, and this project's player carries theirs about a metre above their soles. Answer
        /// this from the pivot instead and a player standing flat on the ground reports a metre of
        /// air — wider than the contact band, wider than the squash band, so the rod never fires
        /// and never squashes and never says why. Ask <c>BodyFeet</c> for the drop; do not type it.
        /// </para>
        /// </summary>
        public static float Clearance(float pivotY, float rootAboveFeet, float groundY)
            => pivotY - rootAboveFeet - groundY;

        /// <summary>
        /// Where to hang the planted rod under its holder, as a height relative to the holder's
        /// pivot — the same measurement as <see cref="Clearance"/>, seen from the other side.
        ///
        /// <para>
        /// The tip is hung exactly one <paramref name="contactHeight"/> below the soles, so that
        /// it meets the ground on the very frame the bounce fires. That is one number doing both
        /// jobs rather than a hand-tuned offset that has to be kept in step with the contact band
        /// by somebody remembering to.
        /// </para>
        /// <para>
        /// <paramref name="prefabBottom"/> is where the rod model's lowest point sits relative to
        /// its own pivot, at the size it is being planted at. Measured rather than assumed: this
        /// model's origin happens to be at its tip, and a re-export that moved it to the middle
        /// would otherwise plant half a rod underground.
        /// </para>
        /// </summary>
        public static float TipOffset(float rootAboveFeet, float contactHeight, float prefabBottom)
            => -rootAboveFeet - contactHeight - prefabBottom;

        /// <summary>
        /// Whether the rod's tip has reached the ground.
        ///
        /// <paramref name="heightAboveGround"/> is the clearance under the player's feet. The
        /// descending test matters: without it a player still rising through the contact band on
        /// the step after a bounce bounces again immediately, and the hop is spent before it leaves
        /// the ground.
        /// </summary>
        public static bool HasTouchedDown(float heightAboveGround, float verticalSpeed,
                                          float contactHeight)
            => heightAboveGround <= contactHeight && verticalSpeed <= 0f;

        /// <summary>
        /// The speed the player leaves the ground with.
        ///
        /// <paramref name="arrivalSpeed"/> is how fast they were falling, as a positive number.
        ///
        /// <para>
        /// Clamped at both ends, and both clamps are what make the rod feel like a machine rather
        /// than a physics toy. The floor is the promise: step on it, do nothing, and it throws you
        /// the same distance every time — this is the "continuous high jumps" the thing exists
        /// for. The ceiling stops a bounce off a cliff from compounding out of the streamed world.
        /// Between them, <c>EnergyReturn</c> below 1 means a big arrival is handed back slightly
        /// smaller each bounce and settles back to the cruise height instead of ringing forever.
        /// </para>
        /// </summary>
        public static float TakeoffSpeed(float arrivalSpeed, JumpingRodConfig cfg)
        {
            if (cfg == null) return 0f;

            float returned = Mathf.Abs(arrivalSpeed) * cfg.EnergyReturn;

            return Mathf.Clamp(returned, cfg.MinHopSpeed, cfg.MaxHopSpeed);
        }

        /// <summary>
        /// How far the coil is squashed, 0 (extended) to 1 (solid), for a player whose feet are
        /// <paramref name="heightAboveGround"/> above the ground.
        ///
        /// <para>
        /// A function of clearance alone, deliberately. It needs no events, no timers and no
        /// netcode: every machine already has the player's pose, so every machine computes the same
        /// squash for the same moment and a watcher sees the rod work exactly as its owner does.
        /// It also comes out right for free — the coil loads on the way down and releases on the
        /// way up, because that is what the clearance does.
        /// </para>
        /// </summary>
        public static float Compression(float heightAboveGround, float compressHeight)
        {
            if (compressHeight <= 1e-4f) return 0f;

            return Mathf.Clamp01((compressHeight - heightAboveGround) / compressHeight);
        }
    }
}
