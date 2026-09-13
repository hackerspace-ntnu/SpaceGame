// The two pieces of blend arithmetic that fail silently if they are wrong.
//
// Neither throws when it is wrong. An exponential ease still moves towards its target, and a zero
// blend time still produces a number. Each one simply looks slightly off in play mode, in a way
// that reads as "the pose needs tuning" rather than as a bug — which is exactly the kind of thing
// worth pinning.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using SpaceGame.Characters;

namespace SpaceGame.EditorTools
{
    public class PoseBlendTests
    {
        [Test]
        public void TheEaseActuallyReachesOne()
        {
            // An exponential ease approaches its target and never arrives. A layer weight stuck at
            // 0.997 leaves the pose permanently a hair short and the layer never fully takes over —
            // visible as the arm not quite settling.
            float t = 0f;
            for (int i = 0; i < 100; i++)
                t = PoseBlend.Ease(t, 1f, 0.15f, 1f / 60f);

            Assert.AreEqual(1f, t, 1e-6f, "the blend must arrive, not merely approach");
        }

        [Test]
        public void AZeroBlendTimeSnaps()
        {
            // Callers that want no blend should not need a special case, and a division by zero
            // here would produce NaN and poison every downstream weight.
            Assert.AreEqual(1f, PoseBlend.Ease(0f, 1f, 0f, 1f / 60f), 1e-6f);
        }
    }
}
