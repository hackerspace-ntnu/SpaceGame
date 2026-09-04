// The animator floats a player replicates have to come to REST.
//
// NetworkAnimator sends a parameter the frame it differs from the last value it saw, so a float
// that keeps creeping is a float that keeps costing a reliable message. The property under test is
// therefore not the feel of the damping but its convergence: after a steady target, consecutive
// steps must return the same number.
//
// In Editor/ rather than beside the other EditMode tests because the struct lives in the default
// assembly, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using SpaceGame.Characters;

namespace SpaceGame.EditorTools
{
    public class DampedAnimatorFloatTests
    {
        private const float Damp = 0.1f;
        private const float Step = 0.02f;
        private const float Quantum = 0.05f;

        [Test]
        public void ASteadyTargetStopsProducingNewValues()
        {
            var f = new DampedAnimatorFloat();

            // A second of walking at a constant speed — fifty physics steps.
            float last = 0f;
            for (int i = 0; i < 50; i++) last = f.Step(4f, Damp, Step, Quantum);

            for (int i = 0; i < 50; i++)
                Assert.AreEqual(last, f.Step(4f, Damp, Step, Quantum),
                    "Once settled, the written value must not change from step to step, or every " +
                    "step is a network message.");
        }

        [Test]
        public void WrittenValuesLieOnTheQuantumGrid()
        {
            var f = new DampedAnimatorFloat();

            for (int i = 0; i < 20; i++)
            {
                float written = f.Step(3.3f, Damp, Step, Quantum);
                float steps = written / Quantum;
                Assert.AreEqual(steps, UnityEngine.Mathf.Round(steps), 1e-4f,
                    $"{written} is not a multiple of {Quantum}.");
            }
        }

        [Test]
        public void TheValueMovesTowardTheTargetAndSettlesOnIt()
        {
            var f = new DampedAnimatorFloat();

            float first = f.Step(4f, Damp, Step, Quantum);
            Assert.Greater(first, 0f, "The first step must already move toward the target.");
            Assert.Less(first, 4f, "A damped value must not snap to the target in one step.");

            for (int i = 0; i < 100; i++) f.Step(4f, Damp, Step, Quantum);

            Assert.AreEqual(4f, f.Step(4f, Damp, Step, Quantum), 1e-4f,
                "The settled value must be the target itself, on the grid.");
        }

        [Test]
        public void ZeroDampTimeSnapsToTheTarget()
        {
            var f = new DampedAnimatorFloat();
            Assert.AreEqual(4f, f.Step(4f, 0f, Step, Quantum), 1e-4f);
        }

        [Test]
        public void ResetSnapsTheDampedValue()
        {
            var f = new DampedAnimatorFloat();
            for (int i = 0; i < 50; i++) f.Step(4f, Damp, Step, Quantum);

            f.Reset(0f);

            Assert.AreEqual(0f, f.Value);
        }

        [Test]
        public void AZeroQuantumLeavesTheValueAlone()
        {
            Assert.AreEqual(1.2345f, DampedAnimatorFloat.Quantise(1.2345f, 0f));
        }
    }
}
