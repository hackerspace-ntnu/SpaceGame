using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The two halves of the foam weld are a C# constant and an HLSL define that have to agree, and
    /// nothing in either file can see the other. These are the claims that go silently wrong when
    /// somebody changes one of them.
    /// </summary>
    public class FoamFieldTests
    {
        private const string ShaderInclude = "Assets/Game/Art/Shaders/Artifacts/FoamSurface.hlsl";
        private const string BlobPrefab = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/FoamBlob.prefab";
        private const string GunPrefab = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/FoamGun.prefab";

        /// <summary>
        /// The uploader's array and the shader's array are the same length.
        ///
        /// <para>
        /// A mismatch does not error and does not go black. Unity fixes a global array's size at
        /// the first upload: a C# array longer than the define writes blobs the shader never reads
        /// (a mass that stops welding partway through), and a shorter one leaves the tail holding
        /// whatever it held last frame (fillets welded onto foam that has already dissolved). Both
        /// look like an art bug, from a hundred lines away in a different language.
        /// </para>
        /// </summary>
        [Test]
        public void TheUploadedArrayIsTheLengthTheShaderReads()
        {
            string source = File.ReadAllText(ShaderInclude);
            Match match = Regex.Match(source, @"#define\s+FOAM_MAX_BLOBS\s+(\d+)");

            Assert.IsTrue(match.Success, $"No FOAM_MAX_BLOBS define in {ShaderInclude}.");
            Assert.AreEqual(FoamField.MaxBlobs, int.Parse(match.Groups[1].Value),
                            "FoamField.MaxBlobs and FOAM_MAX_BLOBS have drifted apart.");
        }

        /// <summary>
        /// One player's whole budget fits in the weld field.
        ///
        /// <para>
        /// Past the field, a blob falls back to its own mesh normal and draws as a plain sphere. So
        /// a budget larger than the array is a gun whose own mass reads as a pile of balls at the
        /// far end of a sweep — which is exactly the complaint this artifact was retuned to fix,
        /// and it would come back the moment somebody raised the budget alone.
        /// </para>
        /// </summary>
        [Test]
        public void OnePlayersBudgetFitsInTheWeldField()
        {
            var gun = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(GunPrefab);
            Assert.IsNotNull(gun, $"No prefab at {GunPrefab}.");

            var artifact = gun.GetComponent<FoamGunArtifact>();
            Assert.IsNotNull(artifact, "The foam gun prefab has no FoamGunArtifact.");

            var serialized = new UnityEditor.SerializedObject(artifact);
            int budget = serialized.FindProperty("liveDabBudget").intValue;

            Assert.LessOrEqual(budget, FoamField.MaxBlobs,
                               "The live dab budget is larger than the weld field, so the tail of " +
                               "a sweep will draw as separate spheres.");
        }

        /// <summary>
        /// A blob can only ever be SMALLER than the radius the gun measures its catch sweep with.
        ///
        /// <para>
        /// The gun sweeps <c>FullRadius + catchMargin</c> for bodies to encase, and that number is
        /// read off this prefab. Variance that could push a lump past its full radius would make
        /// the lump reach further than the sweep that decided who was in it — a player visibly
        /// inside the foam and not held by it.
        /// </para>
        /// </summary>
        [Test]
        public void SizeVarianceOnlyEverShrinksABlob()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BlobPrefab);
            Assert.IsNotNull(prefab, $"No prefab at {BlobPrefab}.");

            var blob = prefab.GetComponent<FoamBlob>();
            Assert.IsNotNull(blob, "The foam blob prefab has no FoamBlob.");

            var serialized = new UnityEditor.SerializedObject(blob);
            float variance = serialized.FindProperty("radiusVariance").floatValue;

            Assert.GreaterOrEqual(variance, 0f, "A negative variance would grow blobs past the sweep.");
            Assert.Less(variance, 1f, "A variance of 1 collapses a blob to nothing.");
        }

        /// <summary>
        /// The arc lands on a volume the colliders do not describe.
        ///
        /// <para>
        /// This is what makes foam build on foam. A lump's collider is a twentieth of its size
        /// until its foam has flown and under half of it for the first second and a half of the
        /// swell, so at fifteen dabs a second an arc traced against the colliders alone drops the
        /// whole opening of a held spray straight through the pile and onto the ground: a carpet
        /// where the player was building a mound. <c>FoamField.FirstAlong</c> is what the trace
        /// is given instead, and this is the plumbing it rides — a chord test that wins is
        /// reported as the landing, point and normal, with no collider anywhere in the answer.
        /// </para>
        /// <para>
        /// No colliders and no scene: the mask is empty on purpose, so the only thing that can
        /// make this trace succeed is the volume test.
        /// </para>
        /// </summary>
        [Test]
        public void TheArcLandsOnAVolumeTheCollidersDoNotDescribe()
        {
            Vector3 origin = Vector3.zero;
            Vector3 direction = Vector3.forward;
            const float speed = 22f;
            const float gravity = 1.4f;
            const float flight = 2f;

            Assert.IsFalse(SprayArc.Trace(origin, direction, speed, gravity, flight, 0,
                                          null, null, out RaycastHit _, out float _),
                           "an empty mask with no volume test must reach nothing at all");

            // A wall across the arc at four metres out, reported the way FoamField reports a lump.
            const float wall = 4f;
            Vector3 expected = default;

            bool Hit(Vector3 from, Vector3 to, out Vector3 point, out Vector3 normal,
                     out float distance)
            {
                point = default;
                normal = Vector3.back;
                distance = 0f;

                if (from.z >= wall || to.z <= wall) return false;

                float t = (wall - from.z) / (to.z - from.z);
                point = from + (to - from) * t;
                distance = Vector3.Distance(from, to) * t;
                expected = point;
                return true;
            }

            Assert.IsTrue(SprayArc.Trace(origin, direction, speed, gravity, flight, 0, null, null,
                                         Hit, out Vector3 landing, out Vector3 surface,
                                         out float travel),
                          "the volume test was never consulted, so foam cannot land on foam");

            Assert.AreEqual(expected.z, landing.z, 1e-3f, "the landing is not on the volume");
            Assert.AreEqual(Vector3.back, surface, "the volume's own normal was not carried out");
            Assert.Greater(travel, 0f, "a landing four metres out cannot take no time to reach");
            Assert.Less(travel, flight, "the flight time ran past the arc it was measured on");
        }

        /// <summary>
        /// A dab settles ONTO the ground, rather than through it or above it.
        ///
        /// <para>
        /// <c>FoamSettle</c> walks a lump downhill by dropping it and letting whatever it lands in
        /// push it back out, so both halves fail in silence and in opposite directions. A fall the
        /// world never stops buries the mound under the terrain it was sprayed onto; a seat that
        /// does not subtract the lump's own clearance leaves it hovering a radius above the ground,
        /// which reads as foam refusing to touch anything. Neither throws.
        /// </para>
        /// <para>
        /// Flat ground and no foam, which is the one case with an answer that can be written down:
        /// the lump rests exactly its clearance above the surface, and it does not wander sideways
        /// while doing it.
        /// </para>
        /// </summary>
        [Test]
        public void ADabSettlesOntoTheGroundRatherThanThroughIt()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                ground.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.transform.localScale = new Vector3(40f, 1f, 40f);
                Physics.SyncTransforms();

                const float radius = 1.3f;
                const float overlap = 0.4f;
                float clearance = radius * (1f - overlap);

                Vector3 rest = FoamSettle.Resolve(Vector3.zero, Vector3.up, radius, ~0, null, null,
                                                  steps: 10, stepShare: 0.35f, overlap: overlap,
                                                  maxDrop: 44f);

                Assert.AreEqual(clearance, rest.y, 1e-2f,
                                "a lump laid on flat ground must rest exactly its clearance above " +
                                "it — lower is foam sunk into the terrain, higher is foam hovering");

                Assert.AreEqual(0f, new Vector2(rest.x, rest.z).magnitude, 1e-2f,
                                "nothing was pushing this lump sideways, so a slide means the " +
                                "solve drifts on level ground and every mound will lean");
            }
            finally
            {
                Object.DestroyImmediate(ground);
            }
        }

        /// <summary>
        /// The droplets fly the arc the foam is traced along.
        ///
        /// <para>
        /// The gun is a hose: <c>SprayArc</c> integrates a parabola in C# from the three serialized
        /// numbers below and that is where the lump is laid, while the stream the player watches is
        /// an ordinary ParticleSystem under Unity's own gravity. They are the same curve only while
        /// the start speed, the gravity modifier and the longest life agree — and the symptom of
        /// their drifting apart is foam landing somewhere the stream was never seen to go, which
        /// reads as the gun being inaccurate rather than as a mismatch. The particle numbers live
        /// in FoamGunSprayBuilder and reach the prefab only when it is re-run, which is the drift
        /// this catches.
        /// </para>
        /// </summary>
        [Test]
        public void TheDropletsFlyTheArcTheFoamIsTracedAlong()
        {
            var gun = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(GunPrefab);
            Assert.IsNotNull(gun, $"No prefab at {GunPrefab}.");

            var artifact = gun.GetComponent<FoamGunArtifact>();
            Assert.IsNotNull(artifact, "The foam gun prefab has no FoamGunArtifact.");

            var nozzle = gun.GetComponentInChildren<FoamGunNozzle>(true);
            Assert.IsNotNull(nozzle, "The foam gun prefab has no FoamGunNozzle.");

            var jet = new UnityEditor.SerializedObject(nozzle)
                      .FindProperty("jet").objectReferenceValue as ParticleSystem;
            Assert.IsNotNull(jet, "The nozzle has no jet, so spraying is invisible.");

            var serialized = new UnityEditor.SerializedObject(artifact);
            float speed = serialized.FindProperty("sprayTravelSpeed").floatValue;
            float gravity = serialized.FindProperty("sprayGravity").floatValue;
            float flight = serialized.FindProperty("sprayFlightTime").floatValue;

            ParticleSystem.MainModule main = jet.main;

            // A RANGE, not a constant: the stream is not one droplet, and the spread is what stops
            // it reading as a line of pellets. What has to hold is that the traced arc is the one
            // the spread is centred on.
            Assert.LessOrEqual(main.startSpeed.constantMin, speed,
                               "every droplet leaves the bell faster than the foam does");
            Assert.GreaterOrEqual(main.startSpeed.constantMax, speed,
                                  "every droplet leaves the bell slower than the foam does");

            float meanFall = (main.gravityModifier.constantMin + main.gravityModifier.constantMax) * 0.5f;
            Assert.AreEqual(gravity, meanFall, 0.05f,
                            "the droplets fall at a different rate than the foam");

            // The longest-lived droplet has to still be in the air when the trace gives up, or the
            // far end of a lob lands out of a stream that has already evaporated.
            Assert.GreaterOrEqual(main.startLifetime.constantMax, flight,
                                  "the stream dies before the trace stops looking for a surface");

            // The ceiling is reached only while spraying at open sky, where nothing kills a droplet
            // early — and a system at its ceiling starves the stream at the bell rather than
            // trimming its tail, which is the one place a stutter is visible.
            float emission = jet.emission.rateOverTime.constant;
            Assert.GreaterOrEqual(main.maxParticles, Mathf.CeilToInt(emission * main.startLifetime.constantMax),
                                  "the jet's ceiling is below what its own rate keeps alive");
        }
    }
}
