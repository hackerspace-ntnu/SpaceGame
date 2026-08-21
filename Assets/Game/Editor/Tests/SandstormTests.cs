// The parts of a sandstorm that have to be right, checked without a world to put one in.
//
// Two things are worth protecting here. The first is that the shape math means what the profile
// says it means — radius, feather and height are the numbers a designer tunes, and if "radius"
// quietly meant "diameter" every storm in the game would be wrong together and nobody would
// notice. The second is determinism: storm position is recomputed on every machine from a seed
// and a clock rather than replicated, so two evaluations of the same record MUST agree exactly.
// That contract is invisible in single player and shows up in multiplayer as one player taking
// damage in what everyone else can see is clear air.
using NUnit.Framework;
using SpaceGame.World.Weather;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace SpaceGame.EditorTools
{
    public class SandstormShapeTests
    {
        private static StormFootprint Cell(float radius = 100f, float feather = 50f,
                                           float height = 200f, float heightFeather = 80f) =>
            new StormFootprint
            {
                Kind = StormShapeKind.Cell,
                Center = Vector2.zero,
                Heading = Vector2.up,
                Radius = radius,
                EdgeFeather = feather,
                BaseY = 0f,
                Height = height,
                HeightFeather = heightFeather,
            };

        private static StormFootprint Wall(float halfThickness = 100f, float lateralExtent = 0f,
                                           float feather = 50f) =>
            new StormFootprint
            {
                Kind = StormShapeKind.Wall,
                Center = Vector2.zero,
                Heading = Vector2.up, // travelling toward +Z
                Radius = halfThickness,
                LateralExtent = lateralExtent,
                EdgeFeather = feather,
                BaseY = 0f,
                Height = 200f,
                HeightFeather = 80f,
            };

        [Test]
        public void CellIsFullAtItsCore()
        {
            Assert.AreEqual(1f, StormShape.Density(Cell(), Vector3.zero), 1e-4f);
            Assert.AreEqual(1f, StormShape.Density(Cell(), new Vector3(99f, 1f, 0f)), 1e-4f);
        }

        [Test]
        public void CellIsEmptyPastItsFeather()
        {
            Assert.AreEqual(0f, StormShape.Density(Cell(), new Vector3(151f, 1f, 0f)), 1e-4f);
        }

        [Test]
        public void CellIsHalfDenseAtTheFeatherMidpoint()
        {
            // Radius 100, feather 50: 125 m out is exactly halfway through the fade.
            Assert.AreEqual(0.5f, StormShape.Density(Cell(), new Vector3(125f, 1f, 0f)), 1e-3f);
        }

        [Test]
        public void CellRadiusIsARadiusNotADiameter()
        {
            // The whole point of the assertion: 100 m out is still inside a radius-100 storm.
            Assert.Greater(StormShape.Density(Cell(), new Vector3(100f, 1f, 0f)), 0.99f);
            Assert.Less(StormShape.Density(Cell(), new Vector3(140f, 1f, 0f)), 0.2f);
        }

        [Test]
        public void StormEndsAtItsCeiling()
        {
            StormFootprint cell = Cell(height: 200f, heightFeather: 80f);

            Assert.AreEqual(1f, StormShape.Density(cell, new Vector3(0f, 100f, 0f)), 1e-4f);
            Assert.AreEqual(0f, StormShape.Density(cell, new Vector3(0f, 200.1f, 0f)), 1e-4f);
        }

        [Test]
        public void SandFillsHolesBelowTheBase()
        {
            // Standing in a canyon must not be a way out of a storm.
            Assert.AreEqual(1f, StormShape.Density(Cell(), new Vector3(0f, -40f, 0f)), 1e-4f);
        }

        [Test]
        public void WallIsThinAlongItsHeadingAndUnboundedAcrossIt()
        {
            StormFootprint wall = Wall(halfThickness: 100f, lateralExtent: 0f);

            // Deep inside the slab, five kilometres off to the side: still full density, which is
            // what "you cannot go round a haboob" has to mean.
            Assert.AreEqual(1f, StormShape.Density(wall, new Vector3(5000f, 1f, 0f)), 1e-4f);

            // Ahead of the front, it is clear.
            Assert.AreEqual(0f, StormShape.Density(wall, new Vector3(0f, 1f, 151f)), 1e-4f);
        }

        [Test]
        public void BoundedWallHasSides()
        {
            StormFootprint wall = Wall(halfThickness: 100f, lateralExtent: 300f);

            Assert.AreEqual(1f, StormShape.Density(wall, new Vector3(200f, 1f, 0f)), 1e-4f);
            Assert.AreEqual(0f, StormShape.Density(wall, new Vector3(400f, 1f, 0f)), 1e-4f);
        }

        [Test]
        public void HeadingDegreesFollowCompassBearings()
        {
            Vector2 north = StormShape.HeadingFromDegrees(0f);
            Vector2 east = StormShape.HeadingFromDegrees(90f);

            Assert.AreEqual(0f, north.x, 1e-4f);
            Assert.AreEqual(1f, north.y, 1e-4f);
            Assert.AreEqual(1f, east.x, 1e-4f);
            Assert.AreEqual(0f, east.y, 1e-4f);
        }
    }

    public class StormInstanceTests
    {
        private SandstormProfile profile;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<SandstormProfile>();
            profile.shape = StormShapeKind.Cell;
            profile.radius = 200f;
            profile.edgeFeather = 100f;
            profile.height = 400f;
            profile.heightFeather = 150f;
            profile.travelSpeed = 10f;
            profile.wanderAmplitude = 0f; // isolated in its own test
            profile.duration = 100f;
            profile.gustAmplitude = 0f;
            profile.intensityOverLife = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(profile);

        private StormInstance Storm(float duration = 100f) => new StormInstance
        {
            Id = 1,
            ProfileIndex = 0,
            Seed = 12345u,
            Origin = Vector2.zero,
            HeadingDegrees = 90f, // toward +X
            StartTime = 1000d,
            Duration = duration,
        };

        [Test]
        public void TravelsAlongItsHeadingAtProfileSpeed()
        {
            StormState state = Storm().Evaluate(profile, 1010d);

            Assert.AreEqual(100f, state.Center.x, 1e-2f); // 10 s at 10 m/s, bearing 90 = +X
            Assert.AreEqual(0f, state.Center.y, 1e-2f);
        }

        [Test]
        public void IntensityFollowsTheLifecycleCurve()
        {
            StormInstance storm = Storm();

            Assert.AreEqual(0f, storm.Evaluate(profile, 1000d).Intensity, 1e-3f);
            Assert.AreEqual(0.5f, storm.Evaluate(profile, 1050d).Intensity, 1e-3f);
            Assert.AreEqual(1f, storm.Evaluate(profile, 1100d).Intensity, 1e-3f);
        }

        [Test]
        public void ParkedStormHoldsItsSteadyIntensity()
        {
            profile.steadyIntensity = 0.8f;
            StormInstance parked = Storm(duration: 0f);

            Assert.AreEqual(0.8f, parked.Evaluate(profile, 1000d).Intensity, 1e-3f);
            Assert.AreEqual(0.8f, parked.Evaluate(profile, 99000d).Intensity, 1e-3f);
            Assert.IsFalse(parked.IsExpired(99000d));
        }

        [Test]
        public void ExpiresAtItsDuration()
        {
            StormInstance storm = Storm(duration: 100f);

            Assert.IsFalse(storm.IsExpired(1099d));
            Assert.IsTrue(storm.IsExpired(1100d));
        }

        [Test]
        public void ClockSkewCannotPushTheStormBeforeItsOwnStart()
        {
            // A client's estimate of server time can sit a few milliseconds behind. Evaluating a
            // negative age would sample the lifecycle curve off its front end and, worse, walk the
            // storm backwards along its heading.
            StormState state = Storm().Evaluate(profile, 999.9d);

            Assert.AreEqual(0f, state.Center.x, 1e-3f);
            Assert.AreEqual(0f, state.Intensity, 1e-3f);
        }

        [Test]
        public void TwoMachinesEvaluatingTheSameRecordAgreeExactly()
        {
            // The contract behind the whole netcode design: nothing about a storm is replicated
            // after birth, so identical inputs must give bit-identical output.
            profile.wanderAmplitude = 80f;
            profile.gustAmplitude = 0.2f;

            StormInstance onServer = Storm();
            StormInstance onClient = Storm();

            for (double t = 1000d; t < 1100d; t += 7.3d)
            {
                StormState a = onServer.Evaluate(profile, t);
                StormState b = onClient.Evaluate(profile, t);

                Assert.AreEqual(a.Center.x, b.Center.x);
                Assert.AreEqual(a.Center.y, b.Center.y);
                Assert.AreEqual(a.Intensity, b.Intensity);
            }
        }

        [Test]
        public void DifferentSeedsGiveDifferentStorms()
        {
            profile.wanderAmplitude = 80f;

            StormInstance first = Storm();
            StormInstance second = Storm();
            second.Seed = 999u;

            Assert.AreNotEqual(first.Evaluate(profile, 1050d).Center.y,
                               second.Evaluate(profile, 1050d).Center.y);
        }

        [Test]
        public void WanderMovesTheStormAcrossItsHeadingNotAlongIt()
        {
            profile.wanderAmplitude = 80f;
            profile.wanderPeriod = 20f;

            StormState wandering = Storm().Evaluate(profile, 1050d);

            // Bearing 90 travels along +X, so all wander must land on Z and the along-track
            // distance must still be exactly speed times time.
            Assert.AreEqual(500f, wandering.Center.x, 1e-2f);
            Assert.AreNotEqual(0f, wandering.Center.y);
            Assert.LessOrEqual(Mathf.Abs(wandering.Center.y), 80f);
        }
    }

    public class SandstormPlacementTests
    {
        private SandstormProfile profile;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<SandstormProfile>();
            profile.radius = 300f;
            profile.edgeFeather = 150f;
            profile.travelSpeed = 10f;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(profile);

        [Test]
        public void StormEntersFromOutsideTheAreaItWillCross()
        {
            var size = new Vector2(4000f, 3000f);
            Vector2 origin = SandstormDirector.EntryOrigin(Vector2.zero, size, 90f, profile, 0f);

            // Bearing 90 travels toward +X, so it must start well off the -X side — beyond the
            // half-diagonal plus its own reach, or the player watches it fade up on top of them.
            Assert.Less(origin.x, -0.5f * size.magnitude);
            Assert.AreEqual(0f, origin.y, 1e-3f);
        }

        [Test]
        public void CrossingDurationOutlivesTheCrossingItself()
        {
            var size = new Vector2(4000f, 3000f);
            float duration = SandstormDirector.CrossingDuration(size, profile);
            Vector2 origin = SandstormDirector.EntryOrigin(Vector2.zero, size, 90f, profile, 0f);

            float travelled = profile.travelSpeed * duration;
            Assert.Greater(origin.x + travelled, 0.5f * size.x,
                           "A storm must at least reach the far edge before it expires.");
        }

        [Test]
        public void ParkedProfileKeepsItsOwnDuration()
        {
            profile.travelSpeed = 0f;
            profile.duration = 0f;

            Assert.AreEqual(0f, SandstormDirector.CrossingDuration(new Vector2(4000f, 3000f), profile));
        }
    }

    public class SandstormNoiseTests
    {
        [Test]
        public void StaysInRange()
        {
            for (float t = -50f; t < 50f; t += 0.37f)
            {
                float value = StormNoise.Value(7u, t);
                Assert.GreaterOrEqual(value, 0f);
                Assert.LessOrEqual(value, 1f);
            }
        }

        [Test]
        public void IsContinuousAcrossLatticePoints()
        {
            // A jump at an integer boundary would show up in play as the wind changing direction
            // in one frame, every wander period, forever.
            float before = StormNoise.Value(3u, 4f - 1e-4f);
            float at = StormNoise.Value(3u, 4f);
            float after = StormNoise.Value(3u, 4f + 1e-4f);

            Assert.AreEqual(at, before, 1e-3f);
            Assert.AreEqual(at, after, 1e-3f);
        }

        [Test]
        public void RepeatsExactlyForTheSameSeedAndTime()
        {
            Assert.AreEqual(StormNoise.Value(42u, 13.7f), StormNoise.Value(42u, 13.7f));
        }
    }

    // The wiring the three render layers cannot check for themselves.
    //
    // Both bugs guarded here presented as "the storm looks wrong" with a clean console, which is
    // the worst kind: nothing throws, nothing warns, and the only symptom is a screen full of the
    // wrong sand.
    public class SandstormRenderWiringTests
    {
        private const string FogMaterialPath = "Assets/Game/Art/Materials/Environment/SandstormFog.mat";
        private const string WallMaterialPath = "Assets/Game/Art/Materials/Environment/SandstormWall.mat";

        [Test]
        public void FogMarchesTheSameSandTheSilhouetteDoes()
        {
            // With no volume assigned the sampler falls back to a flat default, and a constant
            // "noise" makes the density constant too: the storm interior becomes a uniform,
            // structureless wall at maximum opacity with no billows in it and no thin patches for
            // light to get through. It reads in game as the screen simply going dark.
            var fog = AssetDatabase.LoadAssetAtPath<Material>(FogMaterialPath);
            var wall = AssetDatabase.LoadAssetAtPath<Material>(WallMaterialPath);

            Assert.IsNotNull(fog, FogMaterialPath + " is missing.");
            Assert.IsNotNull(wall, WallMaterialPath + " is missing.");

            Texture sand = fog.GetTexture("_SandstormNoise");
            Assert.IsNotNull(sand, "The fullscreen fog has no sand volume assigned.");
            Assert.AreSame(wall.GetTexture("_SandstormNoise"), sand,
                           "The fog and the silhouette must march the same sand, or walking into " +
                           "a storm changes what it is made of.");
        }

        [Test]
        public void TheFogTargetCanHoldItsCoverage()
        {
            // The fog pass writes coverage into alpha and the composite is nothing but
            // lerp(scene, fog.rgb, fog.a). A target without an alpha channel throws the coverage
            // away with no error at all, and the composite then reads the 1.0 that an absent
            // channel returns and paints the whole screen — black wherever the ray missed the
            // storm. That is what a storm interior looked like in play.
            Assert.IsTrue(GraphicsFormatUtility.HasAlphaChannel(SandstormRenderFeature.FogFormat),
                          "The sandstorm fog target must use a format with an alpha channel.");
        }

        [Test]
        public void SkyLightReachesTheShaders()
        {
            // Inside a storm the sun march is fully occluded in every direction, so this is the
            // only light there is. It used to be read with SampleSH(), which is zero in both of
            // the paths that call it, and the interior rendered black.
            AmbientMode mode = RenderSettings.ambientMode;
            Color ambient = RenderSettings.ambientLight;

            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.white;

                Shader.SetGlobalVector("_SandstormSkyLight", Vector4.zero);
                SandstormVisuals.PushSkyLight();

                Vector4 pushed = Shader.GetGlobalVector("_SandstormSkyLight");
                Assert.Greater(pushed.x, 0.1f, "A white sky must reach the sand as light.");
                Assert.Greater(pushed.y, 0.1f);
                Assert.Greater(pushed.z, 0.1f);
            }
            finally
            {
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientLight = ambient;
            }
        }
    }
}
