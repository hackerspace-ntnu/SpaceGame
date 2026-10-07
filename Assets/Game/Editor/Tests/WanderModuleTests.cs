// A failed destination pick must back off, not retry every frame -- see WanderModule.Tick.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class WanderModuleTests
    {
        [Test]
        public void AFailedPickBacksOffInsteadOfRetryingEveryFrame()
        {
            var go = new GameObject("WanderModuleTestAgent");
            try
            {
                var wander = go.AddComponent<WanderModule>();
                // Far enough from anything any other test could have baked or added to the shared
                // NavMesh this editor session that no sampled candidate can ever land on it -- every
                // pick attempt is guaranteed to fail regardless of run order.
                go.transform.position = new Vector3(500_000f, 500_000f, 500_000f);

                var context = new AgentContext { Self = go.transform, Position = go.transform.position };

                MoveIntent? first = wander.Tick(context, 0.1f);
                Assert.IsNull(first, "nothing reachable to move to");
                Assert.Greater(wander.WaitTimer, 0f,
                    "a failed pick must set the wait timer, or Tick re-rolls (and re-fails) every frame");

                float waitAfterFirst = wander.WaitTimer;
                MoveIntent? second = wander.Tick(context, 0.1f);
                Assert.IsNull(second);
                Assert.AreEqual(waitAfterFirst - 0.1f, wander.WaitTimer, 1e-4f,
                    "the second tick must be spending down the back-off, not attempting another pick");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
