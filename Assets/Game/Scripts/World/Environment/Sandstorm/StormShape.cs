// The geometry of a storm, and nothing else.
//
// Everything in here is a pure function of numbers — no MonoBehaviour, no ScriptableObject, no
// scene to stand in. That is deliberate: this math decides who takes damage AND what the shader
// draws, so if the two ever disagree the storm you can see stops matching the storm that hurts
// you. Keeping it in one dependency-free place is what lets an EditMode test pin it down.
using UnityEngine;

namespace SpaceGame.World.Weather
{
    public enum StormShapeKind
    {
        /// <summary>Roughly cylindrical. Has edges, so you can see round it and walk round it.</summary>
        Cell = 0,

        /// <summary>
        /// A slab advancing along its heading — a haboob front. Wide enough that going round it is
        /// not the answer; outrunning it or sheltering is.
        /// </summary>
        Wall = 1,
    }

    /// <summary>
    /// A storm's geometry at one instant, resolved from its profile and wherever it has drifted to.
    ///
    /// Passed by reference into <see cref="StormShape.Density"/> and uploaded to the shader field
    /// for field, so the CPU and the GPU work from the same nine numbers by construction rather
    /// than by two people remembering to keep them in step.
    /// </summary>
    public struct StormFootprint
    {
        public StormShapeKind Kind;

        /// <summary>World XZ of the storm's centre — for a Wall, the middle of the slab.</summary>
        public Vector2 Center;

        /// <summary>Unit XZ. The direction of travel, and for a Wall also the slab's normal.</summary>
        public Vector2 Heading;

        /// <summary>Cell: core radius. Wall: half-thickness of the slab.</summary>
        public float Radius;

        /// <summary>Wall only: half-width across the heading. Zero or less means unbounded.</summary>
        public float LateralExtent;

        /// <summary>Metres over which density falls from full to nothing at the horizontal edges.</summary>
        public float EdgeFeather;

        /// <summary>World Y the storm sits on. Below it the air is just as thick — sand fills holes.</summary>
        public float BaseY;

        /// <summary>Metres of sand above <see cref="BaseY"/>. This is what makes a storm tower.</summary>
        public float Height;

        /// <summary>Metres of fade at the top, so the storm dissolves into sky instead of ending.</summary>
        public float HeightFeather;
    }

    public static class StormShape
    {
        /// <summary>
        /// How thick the sand is at <paramref name="worldPos"/>, 0 to 1, from shape alone. The
        /// storm's lifecycle intensity is applied on top of this by <see cref="StormInstance"/> —
        /// keeping the two separate is what lets a storm fade in without appearing to change size.
        /// </summary>
        public static float Density(in StormFootprint f, Vector3 worldPos)
        {
            float horizontal = HorizontalDensity(f, new Vector2(worldPos.x, worldPos.z));
            if (horizontal <= 0f)
                return 0f;

            return horizontal * VerticalDensity(f, worldPos.y);
        }

        public static float HorizontalDensity(in StormFootprint f, Vector2 worldXZ)
        {
            Vector2 relative = worldXZ - f.Center;

            if (f.Kind == StormShapeKind.Cell)
                return Falloff(relative.magnitude, f.Radius, f.EdgeFeather);

            // A wall is a slab: thin along its heading, wide across it. Both axes feather, so the
            // front arrives as a gradient rather than switching on in the frame it touches you.
            Vector2 heading = f.Heading.sqrMagnitude > 0f ? f.Heading.normalized : Vector2.up;
            Vector2 across = new Vector2(-heading.y, heading.x);

            float along = Mathf.Abs(Vector2.Dot(relative, heading));
            float density = Falloff(along, f.Radius, f.EdgeFeather);
            if (density <= 0f || f.LateralExtent <= 0f)
                return density;

            float sideways = Mathf.Abs(Vector2.Dot(relative, across));
            return density * Falloff(sideways, f.LateralExtent, f.EdgeFeather);
        }

        public static float VerticalDensity(in StormFootprint f, float worldY)
        {
            float above = worldY - f.BaseY;

            // Standing in a canyon does not get you out of a sandstorm; it fills up first. Only
            // climbing above the ceiling helps, which is what makes height worth having.
            if (above <= 0f)
                return 1f;

            float feather = Mathf.Min(f.HeightFeather, f.Height);
            return Falloff(above, f.Height - feather, feather);
        }

        /// <summary>
        /// 1 inside <paramref name="core"/>, 0 past <paramref name="core"/> + <paramref name="feather"/>,
        /// smooth in between. A zero feather gives a hard edge rather than a divide by zero.
        /// </summary>
        public static float Falloff(float distance, float core, float feather)
        {
            if (distance <= core)
                return 1f;

            return 1f - Smoothstep(core, core + feather, distance);
        }

        /// <summary>
        /// The GLSL smoothstep, which Unity does not ship: Mathf.SmoothStep interpolates between
        /// two values, it does not map a value onto an eased 0..1 across two edges.
        /// </summary>
        public static float Smoothstep(float edge0, float edge1, float x)
        {
            if (edge1 <= edge0)
                return x < edge1 ? 0f : 1f;

            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Compass bearing to a unit XZ direction: 0 is +Z, 90 is +X. Storms are authored as
        /// bearings because that is how <c>WindField</c> already talks about direction, and two
        /// conventions for "which way" in one desert would be a bug factory.
        /// </summary>
        public static Vector2 HeadingFromDegrees(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }
    }
}
