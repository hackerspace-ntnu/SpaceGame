// The weathervane puzzle's rules, with no Unity in them: which way each vane points, what one
// crank does, and when the ring is solved. Kept apart from the MonoBehaviour so the rules can be
// tested and so the save file, the wire and the scene all read one definition of "solved".
using System;

namespace SpaceGame.Gameplay.Puzzles
{
    /// <summary>
    /// The quarter-turn each vane stands at, packed two bits a vane into one int.
    ///
    /// <para>
    /// Position 0 is the vane's authored pose, which is the pose that points up the plateau: a ring
    /// is solved exactly when every vane is back at 0. A crank turns its own vane a quarter
    /// clockwise and drags the NEXT vane round the ring with it, which is what makes this a puzzle
    /// rather than four independent dials.
    /// </para>
    /// <para>
    /// That coupling is not invertible on a ring of four — some arrangements cannot be solved at
    /// all. So a ring is never set to an arbitrary arrangement: it is scrambled by cranking FROM the
    /// solved pose, and every arrangement a crank can reach, cranks can undo (four turns of one
    /// crank is the identity, so any turn is undone by three more).
    /// </para>
    /// </summary>
    public readonly struct WeathervanePositions : IEquatable<WeathervanePositions>
    {
        public const int Quarters = 4;
        public const int MaxVanes = 15;   // 2 bits each, inside a signed int

        public readonly int Count;
        public readonly int Packed;

        public WeathervanePositions(int count, int packed)
        {
            if (count < 2 || count > MaxVanes)
                throw new ArgumentOutOfRangeException(nameof(count), count,
                    $"A weathervane ring needs 2..{MaxVanes} vanes.");
            Count = count;
            Packed = packed & ((1 << (2 * count)) - 1);
        }

        public static WeathervanePositions Solved(int count) => new WeathervanePositions(count, 0);

        public int this[int vane] => (Packed >> (2 * vane)) & (Quarters - 1);

        public bool IsSolved => Packed == 0;

        /// <summary>The ring after one turn of <paramref name="vane"/>'s crank.</summary>
        public WeathervanePositions Turn(int vane)
        {
            if (vane < 0 || vane >= Count) throw new ArgumentOutOfRangeException(nameof(vane));
            return Advance(vane).Advance((vane + 1) % Count);
        }

        private WeathervanePositions Advance(int vane)
        {
            int shift = 2 * vane;
            int next = (this[vane] + 1) % Quarters;
            return new WeathervanePositions(Count, (Packed & ~(3 << shift)) | (next << shift));
        }

        /// <summary>
        /// A random unsolved arrangement, reached by <paramref name="cranks"/> random cranks from
        /// the solved pose — so it is always solvable. Re-rolled until it is not the solved pose
        /// itself, which a short run of cranks can land back on.
        /// </summary>
        public static WeathervanePositions Scramble(int count, int cranks, Random random)
        {
            if (cranks < 1) throw new ArgumentOutOfRangeException(nameof(cranks));

            WeathervanePositions ring;
            do
            {
                ring = Solved(count);
                for (int i = 0; i < cranks; i++) ring = ring.Turn(random.Next(count));
            }
            while (ring.IsSolved);
            return ring;
        }

        public bool Equals(WeathervanePositions other) => Count == other.Count && Packed == other.Packed;
        public override bool Equals(object obj) => obj is WeathervanePositions other && Equals(other);
        public override int GetHashCode() => (Count * 397) ^ Packed;
        public override string ToString()
        {
            var digits = new char[Count];
            for (int i = 0; i < Count; i++) digits[i] = (char)('0' + this[i]);
            return new string(digits);
        }
    }
}
