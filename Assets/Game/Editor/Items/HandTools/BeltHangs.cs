using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// How a stowed tool lies on the wearer, in item space: which of its points lands on the belt anchor,
    /// which of its directions points up toward the garment and which points out from the body, and the
    /// slots it may hang from. <see cref="HandToolBuilder"/> writes it into the prefab's
    /// <see cref="BeltMount"/> and its <c>BeltHang</c> child.
    /// </summary>
    public readonly struct BeltHang
    {
        public readonly Vector3 Point;
        public readonly Vector3 Up;
        public readonly Vector3 Out;
        public readonly IReadOnlyList<BeltSlot> Slots;

        public BeltHang(Vector3 point, Vector3 up, Vector3 outward, IReadOnlyList<BeltSlot> slots)
        {
            Point = point;
            Up = up;
            Out = outward;
            Slots = slots;
        }
    }

    /// <summary>
    /// Works out how every carried tool stows, from its own shape, so a new tool needs no hang row.
    ///
    /// <para>
    /// A short tool hangs from a hip by the end of its handle, business end down, lying flat against the
    /// thigh (its thinnest side outward). A vessel hangs from its handle exactly as it hangs from a fist.
    /// A long tool is slung across the back with its business end up over one shoulder, flat to the
    /// back. Which of those a tool is follows from its stance and its size alone; only a tool whose loop
    /// was authored (<see cref="HandToolSpec.HangPoint"/>) or whose shape misleads the rule
    /// (<see cref="HandToolSpec.HangAlong"/>) says anything of its own.
    /// </para>
    /// </summary>
    public static class BeltHangs
    {
        /// <summary>Longest side, metres, of a tool that still hangs from a hip; anything longer is slung on the back.</summary>
        public const float HipMaxLength = 0.9f;

        /// <summary>How far from the butt a hip tool is held, as a fraction of its length: a hammer's loop sits just inside its end.</summary>
        private const float HandleInset = 0.12f;

        /// <summary>How far from the butt a slung tool is held, as a fraction of its length, so its head rides high over a shoulder.</summary>
        private const float SlingFraction = 0.25f;

        /// <summary>Degrees a slung tool leans off the vertical across the back.</summary>
        private const float SlingLean = 15f;

        /// <summary>Metres between the back anchor and the nearest face of a slung tool.</summary>
        private const float SlingClearance = 0.06f;

        /// <summary>Metres of a hanging vessel that may overlap the leg: the thigh and skirt beside a hip anchor.</summary>
        private const float VesselBodyDepth = 0.1f;

        /// <summary>Metres within which two sides count as equally thin, so the stance's own face wins.</summary>
        private const float SideTie = 0.02f;

        /// <summary>A short tool takes a hip first; the back is kept for what cannot hang anywhere else.</summary>
        private static readonly BeltSlot[] ShortSlots =
        {
            BeltSlot.HipRight, BeltSlot.HipLeft, BeltSlot.PackLeft, BeltSlot.PackRight, BeltSlot.Back
        };

        /// <summary>A long tool hangs only where it clears the legs.</summary>
        private static readonly BeltSlot[] LongSlots = { BeltSlot.Back, BeltSlot.PackLeft, BeltSlot.PackRight };

        /// <param name="bounds">The tool's meshes in its own space, at the size they were built.</param>
        public static BeltHang Of(HandToolSpec spec, Bounds bounds)
        {
            bool slung = spec.Slung || spec.HoldSize > HipMaxLength;
            IReadOnlyList<BeltSlot> slots = slung ? LongSlots : ShortSlots;

            if (spec.HangPoint.HasValue)
            {
                Vector3 authoredUp = -spec.HangDown.normalized;
                Vector3 outward = Vector3.ProjectOnPlane(spec.HangOut ?? Vector3.forward, authoredUp);
                if (outward.sqrMagnitude < 1e-4f) outward = Vector3.ProjectOnPlane(Vector3.right, authoredUp);
                return new BeltHang(spec.HangPoint.Value, authoredUp, outward.normalized, slots);
            }

            StanceDefinition stance = CarryStances.Of(spec.Stance);
            Vector3 along = spec.HangAlong ?? spec.ItemAlong ?? stance.ItemAlong;
            Vector3 flat = spec.HangOut ?? ThinnestSide(bounds, along, spec.ItemFace ?? stance.ItemFace);
            float start = Vector3.Dot(bounds.center, along) - Vector3.Dot(Abs(along), bounds.extents);
            float length = 2f * Vector3.Dot(Abs(along), bounds.extents);

            if (slung)
            {
                float nearFace = Vector3.Dot(bounds.center, flat) - Vector3.Dot(Abs(flat), bounds.extents);
                Vector3 lean = Vector3.Cross(flat, along);
                Vector3 up = Mathf.Cos(SlingLean * Mathf.Deg2Rad) * along + Mathf.Sin(SlingLean * Mathf.Deg2Rad) * lean;
                Vector3 point = along * (start + SlingFraction * length) + flat * (nearFace - SlingClearance);
                return new BeltHang(point, up, flat, slots);
            }

            // A vessel's handle is where the fist closes, so it hangs from there, rim up, held out far enough
            // that the part of it wider than the thigh does not hang inside the leg.
            if (spec.Stance == CarryStance.Hang)
            {
                float nearSide = Vector3.Dot(bounds.center, flat) - Vector3.Dot(Abs(flat), bounds.extents);
                return new BeltHang(flat * Mathf.Min(0f, nearSide + VesselBodyDepth), along, flat, slots);
            }

            return new BeltHang(along * (start + HandleInset * length), -along, flat, slots);
        }

        /// <summary>
        /// The item axis, at right angles to <paramref name="along"/>, along which the tool is thinnest: laid
        /// outward it keeps the tool flat to the body. A tie goes to <paramref name="preferred"/>.
        /// </summary>
        private static Vector3 ThinnestSide(Bounds bounds, Vector3 along, Vector3 preferred)
        {
            Vector3 best = Vector3.zero;
            float thinnest = float.MaxValue;
            foreach (Vector3 axis in new[] { preferred, Vector3.right, Vector3.up, Vector3.forward })
            {
                if (Mathf.Abs(Vector3.Dot(axis, along)) > 0.5f) continue;

                float size = Vector3.Dot(Abs(axis), bounds.size);
                if (size >= thinnest - SideTie) continue;
                best = axis;
                thinnest = size;
            }

            return best;
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
