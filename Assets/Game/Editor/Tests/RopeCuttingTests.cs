// What a beam may and may not cut, checked without a physics scene.
//
// The whole feature is one geometric predicate — does this segment pass within r of that polyline —
// and every interesting case is a case the predicate gets wrong if it is written casually. A rope
// hanging above the beam rather than beside it is the one an XZ-only routine misses; a rope crossing
// the beam's LINE beyond where the beam stopped is the one an infinite-ray version cuts through a
// wall; a rope collapsed to a single point is what all three systems draw for a frame while they are
// being built, and an unguarded division there returns NaN, which compares false against every
// threshold and so fails silently.
//
// The registry half is pinned too, because the ordering there is load-bearing rather than tidy:
// cutting a rope unregisters it, and a loop that cut while enumerating would skip the rope after it.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types,
// and an asmdef cannot reference Assembly-CSharp.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class RopeCuttingTests
    {
        /// <summary>A rope of fixed shape that remembers whether it was cut. Nothing else.</summary>
        private sealed class FakeRope : ICuttableRope
        {
            private readonly Vector3[] points;

            /// <summary>
            /// Take itself out of the registry as it is cut, the way a leash does: Snap destroys
            /// its GameObject and the OnDisable that follows unregisters it, inside the very call
            /// that found it.
            /// </summary>
            private readonly bool unregistersOnCut;

            public FakeRope(params Vector3[] points) : this(false, points) { }

            public FakeRope(bool unregistersOnCut, params Vector3[] points)
            {
                this.unregistersOnCut = unregistersOnCut;
                this.points = points;
            }

            public int Cuts { get; private set; }

            public void AppendSpan(List<Vector3> into) => into.AddRange(points);

            public void Cut()
            {
                Cuts++;
                if (unregistersOnCut) CuttableRopes.Unregister(this);
            }
        }

        [TearDown]
        public void ClearRegistry()
        {
            // The registry is static and outlives a test. Copied first: Unregister mutates it.
            var live = new List<ICuttableRope>(CuttableRopes.All);
            foreach (ICuttableRope rope in live) CuttableRopes.Unregister(rope);
        }

        private static float Distance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2) =>
            Mathf.Sqrt(CuttableRopes.SegmentDistanceSq(p1, q1, p2, q2));

        // ── The predicate ──────────────────────────────────────────────────────

        [Test]
        public void CrossingSegmentsMeetAtZero()
        {
            float d = Distance(new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f),
                               new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f));

            Assert.That(d, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void ARopeAboveTheBeamIsMeasuredInThreeDimensions()
        {
            // Directly over the beam's midpoint by 2 m, and crossing it in plan view. A horizontal
            // measurement calls this a hit, which is the mistake the one other segment routine in
            // the codebase would make here — see CuttableRopes.SegmentDistanceSq.
            float d = Distance(new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f),
                               new Vector3(0f, 2f, -1f), new Vector3(0f, 2f, 1f));

            Assert.That(d, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void TheBeamIsASegmentAndNotARay()
        {
            // The rope crosses the beam's LINE at x = 5, five metres past where the beam stopped.
            // That stop is what a wall between the two produces, so this is the line-of-sight case.
            float d = Distance(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                               new Vector3(5f, 0f, -1f), new Vector3(5f, 0f, 1f));

            Assert.That(d, Is.EqualTo(4f).Within(1e-4f));
        }

        [Test]
        public void ParallelSegmentsMeasureTheirOffsetRatherThanNaN()
        {
            float d = Distance(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f),
                               new Vector3(0f, 0f, 3f), new Vector3(10f, 0f, 3f));

            Assert.That(d, Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void ADegenerateRopeIsMeasuredRatherThanDividedBy()
        {
            // Both ends in the same place, which is what every one of these systems draws for a
            // frame or two while a rope is being built or torn down.
            float d = Distance(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f),
                               new Vector3(5f, 4f, 0f), new Vector3(5f, 4f, 0f));

            Assert.That(float.IsNaN(d), Is.False, "a zero-length rope must not produce NaN");
            Assert.That(d, Is.EqualTo(4f).Within(1e-4f));
        }

        // ── The query ──────────────────────────────────────────────────────────

        [Test]
        public void OnlyRopesInsideTheRadiusAreCut()
        {
            // The beam runs up the z axis, so "far" is offset ACROSS it. Offsetting it further
            // ALONG z would leave it standing in the beam's path — which is what the first draft of
            // this test did, and it is the same mistake as measuring a rope's distance in plan view.
            var near = new FakeRope(new Vector3(0.1f, -1f, 0f), new Vector3(0.1f, 1f, 0f));
            var far = new FakeRope(new Vector3(2f, -1f, 0f), new Vector3(2f, 1f, 0f));

            CuttableRopes.Register(near);
            CuttableRopes.Register(far);

            int cut = CuttableRopes.CutAlong(new Vector3(0f, 0f, -5f), new Vector3(0f, 0f, 5f), 0.15f);

            Assert.That(cut, Is.EqualTo(1));
            Assert.That(near.Cuts, Is.EqualTo(1));
            Assert.That(far.Cuts, Is.Zero);
        }

        [Test]
        public void ARopeIsCutOnceHoweverManyOfItsSegmentsAreInTheBeam()
        {
            // A leash appends its wraps, so a rope draped over the beam presents several segments
            // inside the radius. Cutting it once per segment would announce one snap per bend.
            var draped = new FakeRope(new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 0f),
                                      new Vector3(1f, 0f, 0f));

            CuttableRopes.Register(draped);

            CuttableRopes.CutAlong(new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f), 0.2f);

            Assert.That(draped.Cuts, Is.EqualTo(1));
        }

        [Test]
        public void ARopeThatIsNotOutContributesNothing()
        {
            var coiled = new FakeRope();

            CuttableRopes.Register(coiled);

            Assert.That(CuttableRopes.CutAlong(Vector3.zero, Vector3.forward * 5f, 1f), Is.Zero);
            Assert.That(coiled.Cuts, Is.Zero);
        }

        [Test]
        public void CuttingOneRopeDoesNotSkipTheNext()
        {
            // A leash unregisters itself as it is cut, from inside the call that found it. If that
            // happened during the enumeration, the rope after it would be stepped over.
            var first = new FakeRope(true, new Vector3(0f, -1f, 0f), new Vector3(0f, 1f, 0f));
            var second = new FakeRope(true, new Vector3(0f, -1f, 0.05f), new Vector3(0f, 1f, 0.05f));

            CuttableRopes.Register(first);
            CuttableRopes.Register(second);

            int cut = CuttableRopes.CutAlong(new Vector3(0f, 0f, -5f), new Vector3(0f, 0f, 5f), 0.2f);

            Assert.That(cut, Is.EqualTo(2));
            Assert.That(first.Cuts, Is.EqualTo(1));
            Assert.That(second.Cuts, Is.EqualTo(1));
        }

        [Test]
        public void RegisteringTwiceCutsOnce()
        {
            var rope = new FakeRope(new Vector3(0f, -1f, 0f), new Vector3(0f, 1f, 0f));

            CuttableRopes.Register(rope);
            CuttableRopes.Register(rope);

            CuttableRopes.CutAlong(new Vector3(0f, 0f, -5f), new Vector3(0f, 0f, 5f), 0.2f);

            Assert.That(rope.Cuts, Is.EqualTo(1));
        }
    }
}
