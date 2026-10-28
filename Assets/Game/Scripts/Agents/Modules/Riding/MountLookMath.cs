// The two pieces of arithmetic behind the mounted camera's "leave it where I put it, then bring
// it home slowly" behaviour. Pulled out of MountModule as pure statics because the interesting
// cases — does it hold for exactly the delay, does it overshoot, does 350° come home the short
// way — are all answerable without a scene, a rider or a camera.
using UnityEngine;

namespace SpaceGame.Agents
{
    public static class MountLookMath
    {
        /// <summary>
        /// Fold an angle into (-180, 180].
        ///
        /// The orbit offset accumulates without bound — that is deliberate, you can swing the
        /// camera all the way round the mount and keep going. But an unwrapped 350° offset
        /// recentres by unwinding 350° instead of going 10° the other way, which reads as the
        /// camera taking the scenic route home for no reason anyone can see.
        /// </summary>
        public static float WrapAngle(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f)
                degrees -= 360f;
            else if (degrees <= -180f)
                degrees += 360f;
            return degrees;
        }

        /// <summary>
        /// Hold a wrapped yaw offset inside a seat's look limit.
        ///
        /// A limit of 180 is not a limit at all, and that is the point of naming this rather than
        /// clamping inline: the offset arrives already folded into (-180, 180], so a ±180 clamp can
        /// never fire, and a rider who keeps turning wraps onto the near side and carries on round.
        /// That is what lets one field say both "you may look anywhere" and "this seat faces one
        /// way" without a sentinel value meaning the first.
        /// </summary>
        /// <param name="wrappedOffset">Offset already through <see cref="WrapAngle"/>.</param>
        /// <param name="limitEachWay">Degrees either side of the seat's forward. 180 = all the way round.</param>
        public static float ClampYaw(float wrappedOffset, float limitEachWay)
        {
            return limitEachWay >= 180f
                ? wrappedOffset
                : Mathf.Clamp(wrappedOffset, -limitEachWay, limitEachWay);
        }

        /// <summary>
        /// Advance an orbit offset one frame towards zero, holding it completely until the rider
        /// has been off the look stick for <paramref name="delay"/> seconds.
        ///
        /// Held rather than eased so "I parked the camera on the ostrich's flank" survives a long
        /// straight ride untouched, and so the return, when it comes, is slow enough to read as
        /// drift rather than as the camera correcting you.
        /// </summary>
        /// <param name="offset">Current offset in degrees.</param>
        /// <param name="timeSinceInput">Seconds since the rider last moved the look stick.</param>
        /// <param name="delay">Seconds of stillness before the drift home starts.</param>
        /// <param name="speed">Degrees per second, once it does.</param>
        public static float StepRecentre(float offset, float timeSinceInput, float delay,
                                         float speed, float deltaTime)
        {
            offset = WrapAngle(offset);

            if (timeSinceInput < delay || speed <= 0f || deltaTime <= 0f)
                return offset;

            // MoveTowards, not a lerp: a lerp's rate depends on how far out you are, so a 90° view
            // would race home and a 10° one would crawl. A constant rate is the thing that can be
            // described to a player in one sentence.
            return Mathf.MoveTowards(offset, 0f, speed * deltaTime);
        }
    }
}
