// Whether the ground ahead is something the legs will walk UP.
//
// Nothing in the stack had an opinion about this. Body height comes from a ray straight down under
// the machine and footholds come from rays under the aim point; neither looks at how steep what it
// found is, so every legged machine walked up a cliff face at full speed for as long as the rays
// kept returning geometry.
//
// The hard part is not the limit, it is telling a HILL from a STEP. A legged machine's whole appeal
// is that it crosses ground a wheel cannot, and the surface normal at the foot of a boulder, a
// ledge, a crate or a stair is vertical -- so a limit written against the local normal, which is how
// a CharacterController does it, takes the step-up away and leaves a six-legged station stopped by a
// knee-high rock. Two tests instead, and a refusal needs only one of them:
//
//   1. SUSTAINED GRADE, rise over the whole run. A boulder barely moves the height at the far end,
//      so it is invisible here; a hillside is not.
//
//   2. A WALL: one segment of the run that is both steeper than the limit AND rises more than the
//      machine can step up. Both halves are load-bearing. Without the angle, a long segment on gentle
//      ground gets refused for the total it accumulates. Without the rise, every ledge is a wall
//      and (1)'s tolerance for short obstacles is undone one segment down. This is what catches the
//      cliff standing at the far end of an otherwise flat approach, whose average grade is diluted
//      by the flat ground in front of it.
//
// No raycasting here, deliberately, same as WalkerFoothold and WalkerSurface: it takes heights that
// have already been sampled, so the arithmetic is testable without a physics scene.
//
// A MISSING SAMPLE IS NEVER A REFUSAL. A ray that finds no ground means an unloaded chunk, not a
// void -- the rule WalkerGround and the foothold search already follow. Refusing on missing terrain
// would park every machine at a chunk border.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public static class WalkerClimb
    {
        /// One ground height along the probe run.
        public struct Sample
        {
            /// Along the run from the machine, in world units.
            public float Distance;
            /// World height of the ground there.
            public float Height;
            /// False where the ray found nothing, which is unloaded terrain rather than a void.
            public bool Found;
        }

        /// A run shorter than this cannot be measured against, so nothing is refused on it.
        private const float MinRun = 0.05f;

        /// How much of the commanded travel survives the ground ahead: 1 unobstructed, 0 refused.
        ///
        /// `startHeight` is the ground under the machine and `riseCeiling` is the tallest thing it
        /// can step up onto -- the leg's own measured lift, NOT its reach. Reach is the wrong
        /// number and was tried: the walking station's legs are twenty times its step height, so a
        /// ceiling taken from reach excused a ten-metre wall as a ledge.
        ///
        /// `taper` is the band below `maxAngle` over which the scale falls off, so a machine
        /// meeting a hillside slows into it instead of stopping as if it had hit glass.
        ///
        /// The taper applies to the sustained grade only. A wall is not a matter of degree.
        public static float TravelScale(float startHeight, Sample[] samples, int count,
                                        float maxAngle, float taper, float riseCeiling)
        {
            if (samples == null || count <= 0) return 1f;

            float tanLimit = Mathf.Tan(Mathf.Clamp(maxAngle, 0f, 89f) * Mathf.Deg2Rad);

            // A wall anywhere along the run refuses the whole run. Segments are measured from the
            // last sample that FOUND ground rather than from the previous index, so a hole in the
            // middle of the run is spanned rather than treated as a height of zero -- there is no
            // reading to be had from a ray that hit nothing, and inventing one puts a phantom cliff
            // wherever the terrain has not streamed in.
            float prevDistance = 0f;
            float prevHeight = startHeight;

            for (int i = 0; i < count; i++)
            {
                if (!samples[i].Found) continue;

                float run = samples[i].Distance - prevDistance;
                float rise = samples[i].Height - prevHeight;

                if (run > MinRun && rise > riseCeiling && rise > run * tanLimit) return 0f;

                prevDistance = samples[i].Distance;
                prevHeight = samples[i].Height;
            }

            // Sustained grade, taken at the FURTHEST sample that found ground -- not at a fixed run
            // length, so a probe that ran off the edge of a loaded chunk is measured over the part
            // it could actually see rather than being scaled against a distance it never reached.
            if (prevDistance <= MinRun) return 1f;

            float grade = Mathf.Atan2(prevHeight - startHeight, prevDistance) * Mathf.Rad2Deg;
            return GradeScale(grade, maxAngle, taper);
        }

        /// The sustained grade's own contribution, split out so it can be reasoned about alone.
        ///
        /// Downhill is never gated. A machine refused in the direction it is facing has to be able
        /// to back off, and backing off is downhill by construction.
        public static float GradeScale(float gradeDegrees, float maxAngle, float taper)
        {
            if (gradeDegrees <= 0f) return 1f;
            if (gradeDegrees >= maxAngle) return 0f;

            float band = Mathf.Max(taper, 1e-3f);
            float soft = maxAngle - band;
            if (gradeDegrees <= soft) return 1f;

            return Mathf.Clamp01((maxAngle - gradeDegrees) / band);
        }
    }
}
