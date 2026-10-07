using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTests
{
    /// <summary>
    /// The two rules that make the flamethrower's fire affordable, and the one Unity trap that made
    /// it invisible.
    ///
    /// <para>
    /// All three failed silently in the shipped item — no exception, no warning, just a weapon that
    /// emitted a handful of specks — which is exactly the kind of thing worth a test rather than a
    /// comment.
    /// </para>
    /// </summary>
    public class GroundFireTests
    {
        private const string PrefabPath =
            "Assets/Game/Prefabs/Items/Artifacts/Gadgets/GroundFire.prefab";

        /// <summary>
        /// <b>The bug this whole system was rebuilt around.</b>
        /// <c>EmissionModule.rateOverTimeMultiplier</c> is not a scale on a constant rate — it IS
        /// the constant — so writing a throttle straight into it turns 210 particles a second into
        /// less than one. <see cref="FlameLayers"/> captures the authored rate and multiplies by
        /// hand; this is what says it still does.
        /// </summary>
        [Test]
        public void HalfThrottleEmitsHalfTheAuthoredRate()
        {
            var holder = new GameObject("Flame");

            try
            {
                var system = holder.AddComponent<ParticleSystem>();

                ParticleSystem.EmissionModule emission = system.emission;
                emission.rateOverTime = 200f;

                var layers = new FlameLayers(system);
                layers.SetRate(0.5f);

                Assert.AreEqual(100f, system.emission.rateOverTime.constant, 0.001f,
                                "A half throttle did not halve the authored emission rate. If this " +
                                "reads 0.5 the throttle has been written into rateOverTimeMultiplier " +
                                "again, which replaces the rate instead of scaling it.");
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        /// <summary>
        /// A jet held on one spot calls <c>Kindle</c> ten times a second at the same place. Without
        /// the merge that is ten patches inside a metre — ten lights, ten overlap sweeps and ten
        /// times the fill — for a fire the player sees as one.
        /// </summary>
        [Test]
        public void FireLaidTwiceInOneCellIsOnePatch()
        {
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"No ground fire prefab at {PrefabPath}. Run " +
                                     "Tools ▸ SpaceGame ▸ Items ▸ Build Flamethrower Fire.");

            var point = new Vector3(1000f, 0f, 1000f);

            try
            {
                GroundFireField.Kindle(prefab, point, null);
                GroundFireField.Kindle(prefab, point + new Vector3(0.2f, 0f, 0.2f), null);

                Assert.AreEqual(1, GroundFireField.LiveCount,
                                "Two ignitions a fifth of a metre apart made two patches. The cell " +
                                "grid is what keeps a swept jet to a trail rather than a heap.");
            }
            finally
            {
                Clear();
            }
        }

        /// <summary>
        /// Past the budget the nearest few patches are lights and the rest are fire without
        /// illumination. The budget has to be held here rather than hoped for: a patch has no idea
        /// how many others are competing.
        /// </summary>
        [Test]
        public void OnlyTheNearestPatchesAreAllowedToLight()
        {
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"No ground fire prefab at {PrefabPath}.");

            try
            {
                int laid = GroundFireField.MaxLitPatches + 4;
                for (int i = 0; i < laid; i++)
                {
                    // Two cells apart, so none of them merge into a neighbour.
                    GroundFireField.Kindle(
                        prefab,
                        new Vector3(1000f + i * GroundFireField.CellSize * 2f, 0f, 1000f),
                        null);
                }

                Assert.AreEqual(laid, GroundFireField.LiveCount, "Patches merged that should not have.");

                GroundFireField.Budget(new Vector3(1000f, 0f, 1000f));

                int allowed = 0;
                foreach (GroundFire fire in Object.FindObjectsByType<GroundFire>(FindObjectsSortMode.None))
                    if (fire.GlowAllowed) allowed++;

                Assert.AreEqual(GroundFireField.MaxLitPatches, allowed,
                                "The wrong number of patches were allowed to light the world. Every "
                                + "patch past the budget is still fire — it simply stops being a "
                                + "light source.");
            }
            finally
            {
                Clear();
            }
        }

        /// <summary>
        /// The field holds its patches in statics, and statics outlive a test. Destroying the
        /// objects makes the field sweep its own nulls on the next pass, which is the same path a
        /// scene unload takes.
        /// </summary>
        private static void Clear()
        {
            foreach (GroundFire fire in Object.FindObjectsByType<GroundFire>(FindObjectsSortMode.None))
                Object.DestroyImmediate(fire.gameObject);

            GroundFireField.Budget(Vector3.zero);
        }
    }
}
