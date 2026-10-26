// A ring of weathervanes around a wind vent. Crank them all to point up the plateau and the vent
// opens a gust that carries whoever stands in it to the top — and stays open for good.
//
// The arrangement, its wire and its save are DialSet's. What is left here is what makes it a
// weathervane ring: a crank drags the next vane round with it (WeathervanePositions), a fresh ring
// is scrambled from solved, and solved is permanent.
using UnityEngine;

namespace SpaceGame.Gameplay.Puzzles
{
    public class WeathervaneRing : DialSet
    {
        [Tooltip("The vanes, in order round the ring. A crank turns its own vane and the next one.")]
        [SerializeField] private WeathervaneVane[] vanes;

        [Tooltip("The gust this ring opens once every vane points the right way.")]
        [SerializeField] private WindUpdraft updraft;

        [Tooltip("How many random cranks, from solved, a fresh ring is scrambled by.")]
        [SerializeField] private int scrambleCranks = 6;

        public WeathervanePositions Positions => new(vanes.Length, Packed);

        public bool IsSolved => IsKnown && Positions.IsSolved;

        /// <summary>A solved ring is locked open for good, so nobody is stranded on the plateau.</summary>
        public override bool CanTurn => base.CanTurn && !Positions.IsSolved;

        protected override int DialCount => vanes.Length;

        protected override int Turn(int arrangement, int dial) =>
            new WeathervanePositions(vanes.Length, arrangement).Turn(dial).Packed;

        protected override int Fresh(System.Random random) =>
            WeathervanePositions.Scramble(vanes.Length, scrambleCranks, random).Packed;

        protected override void Show(int arrangement, int turnedDial, bool instant)
        {
            var positions = new WeathervanePositions(vanes.Length, arrangement);

            for (int i = 0; i < vanes.Length; i++)
                vanes[i].Show(positions[i], instant, cranked: i == turnedDial);

            updraft.SetOpen(positions.IsSolved, instant);
        }

        public override string Describe(int dial) =>
            IsKnown && Positions[dial] == 0 ? "pointing up the plateau" : string.Empty;
    }
}
