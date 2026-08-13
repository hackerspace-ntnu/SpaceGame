using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Vehicles.Crawler;

namespace SpaceGame.EditorTools
{
    /// The whole machine, on real ground, with the real rig.
    ///
    /// The unit tests pin down each piece in isolation; this one exists because the faults it watches
    /// for are properties of the assembled walker and nothing else can catch them: a foot that is not
    /// on the surface, a leg stretched past its reach, and a machine that will not cross the ground.
    ///
    /// It runs in EditMode rather than PlayMode because everything needed is available without a
    /// running player loop — DesertCrawlerLocomotion.Initialise and .Step are public and dt-driven for
    /// exactly this reason, and Unity's raycasts work against colliders in an edit-mode scene once the
    /// transforms are synced.
    public class SpiderWalkerGroundingTests
    {
        private const string PrefabPath = "Assets/Game/Prefabs/Agents/Vehicles/Ground/RigWalker.prefab";

        private GameObject world;
        private GameObject walker;
        private DesertCrawlerLocomotion locomotion;

        [SetUp]
        public void SetUp()
        {
            world = new GameObject("TestWorld");

            // Level ground, a ramp up to a plateau, and a boulder squarely in the way — between them
            // these are the cases the old code failed on: a slope for the fabricated contact point, an
            // edge for the ledge test, and something solid for the legs to be driven through.
            Slab(new Vector3(0f, -1f, 0f), new Vector3(400f, 2f, 400f), Quaternion.identity);
            Slab(new Vector3(60f, 4f, 0f), new Vector3(60f, 2f, 200f), Quaternion.Euler(0f, 0f, -14f));
            Slab(new Vector3(120f, 7f, 0f), new Vector3(120f, 2f, 200f), Quaternion.identity);
            Slab(new Vector3(30f, 1f, 6f), new Vector3(9f, 6f, 9f), Quaternion.identity);

            Physics.SyncTransforms();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"{PrefabPath} is missing; the walker cannot be tested without it");

            walker = Object.Instantiate(prefab);
            walker.transform.position = new Vector3(-40f, 40f, 0f);
            Physics.SyncTransforms();

            locomotion = walker.GetComponent<DesertCrawlerLocomotion>();
            Assert.IsNotNull(locomotion, "the walker prefab has no DesertCrawlerLocomotion");

            // Awake does not run for objects instantiated in edit mode, so drive setup by hand.
            locomotion.Initialise();
            Assert.IsTrue(locomotion.IsReady, "no leg chains were found on the rig");
            locomotion.SnapToGround();
        }

        [TearDown]
        public void TearDown()
        {
            if (walker != null) Object.DestroyImmediate(walker);
            if (world != null) Object.DestroyImmediate(world);
        }

        private void Slab(Vector3 centre, Vector3 size, Quaternion rotation)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(world.transform);
            go.transform.SetPositionAndRotation(centre, rotation);
            go.transform.localScale = size;
        }

        /// A ramp rising along +z at `degrees`, whose surface meets the level ground at z = `baseZ`.
        ///
        /// +z because THAT IS WHERE THE RIG'S NOSE POINTS. A ramp built across the machine's path
        /// is not a climb test — it walks placidly along the foot of it forever, gains no height,
        /// is never refused, and every assertion reads as though the limit were broken.
        ///
        /// Built by the climb tests rather than in SetUp, so each owns the one piece of terrain it
        /// is about. They sit well clear of the shared world, whose own ramp faces the other way.
        ///
        /// LONG, and that is not arbitrary either. This machine's legs are fifteen metres and it
        /// looks a full thirty metres ahead, so a ramp of the size a person would sketch is a BUMP
        /// to it: the far probe lands back on the flat ground beyond the ramp, the grade averages
        /// to nothing, and again the test proves the opposite of what it was written to prove. It
        /// has to outrun the probe in every direction the machine might look, which is why it is
        /// wide as well as long — CanTravel is asked about headings ACROSS the face.
        private void Ramp(float baseZ, float laneX, float degrees)
        {
            const float length = 300f;
            const float width = 120f;
            const float thickness = 2f;

            float e = degrees * Mathf.Deg2Rad;

            // Laid so its TOP surface passes through (z = baseZ, y = 0): half its length along
            // world z puts the centre there, and the height follows from the surface standing one
            // half thickness out along the tilted normal.
            float half = length * 0.5f * Mathf.Cos(e);
            float cy = -Mathf.Cos(e) + Mathf.Tan(e) * (half - Mathf.Sin(e));

            Slab(new Vector3(laneX, cy, baseZ + half), new Vector3(width, thickness, length),
                 Quaternion.Euler(-degrees, 0f, 0f));
            Physics.SyncTransforms();
        }

        /// Drop the machine somewhere else in the world and let it find the ground again.
        private void PlaceAt(Vector3 position)
        {
            walker.transform.position = position;
            Physics.SyncTransforms();
            locomotion.SnapToGround();
            Physics.SyncTransforms();
        }

        /// Walks the machine across the whole test world, checking every frame.
        private void Walk(float speed, float yawRate, int frames, System.Action perFrame)
        {
            const float dt = 1f / 60f;
            for (int i = 0; i < frames; i++)
            {
                locomotion.SetTwist(speed, yawRate);
                locomotion.Step(dt);
                Physics.SyncTransforms();
                perFrame();
            }
        }

        /// Feet floating above the ground or sunk into it.
        [Test]
        public void EveryPlantedFootStaysOnTheSurfaceBeneathIt()
        {
            float worstGap = 0f;

            Walk(4f, 0f, 600, () =>
            {
                for (int i = 0; i < locomotion.LegCount; i++)
                {
                    if (!locomotion.TryGetFoot(i, out Vector3 foot, out bool swinging)) continue;
                    if (swinging) continue;

                    // Look for the surface under the foot from just above it. A planted foot should be
                    // sitting on whatever this finds.
                    if (!Physics.Raycast(foot + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 60f))
                        continue;
                    if (hit.collider.transform.IsChildOf(walker.transform)) continue;

                    worstGap = Mathf.Max(worstGap, Mathf.Abs(hit.point.y - foot.y));
                }
            });

            // A sole is a disc resting on a supporting plane, so its contact can legitimately sit a
            // little proud of a single ray under its centre. Metres of float or sink cannot.
            Assert.Less(worstGap, 0.75f, "a planted foot was off the ground beneath it");
        }

        [Test]
        public void NoLegIsEverStretchedBeyondItsReach()
        {
            float worst = 0f;
            Walk(4f, 0f, 600, () =>
                worst = Mathf.Max(worst, locomotion.LastFrame.WorstReachFraction));

            Assert.LessOrEqual(worst, 1f, "a foot was further from its hip than the leg is long");
        }

        /// The machine has to actually get somewhere. This is the test for the fault that sent the
        /// locomotion back to its committed form: commanded at full speed it must cross the ground,
        /// not stand still shuffling its feet.
        [Test]
        public void TheMachineActuallyCrossesTheGround()
        {
            Vector3 start = walker.transform.position;
            Walk(4f, 0f, 600, () => { });

            Assert.Greater(Vector3.Distance(start, walker.transform.position), 15f,
                "the walker went nowhere; the gait has stalled it");
        }

        /// A leg reporting itself unreachable is not a fault — it is the signal that makes broken
        /// ground work, and the gait answers it by stepping that leg early. What WOULD be a fault is
        /// the condition persisting, which means the early step is not happening or is not helping.
        [Test]
        public void ALegThatRunsOutOfReachIsSteppedAndRecovers()
        {
            int run = 0;
            int longestRun = 0;
            int framesAffected = 0;

            Walk(0f, 20f, 400, () =>
            {
                if (locomotion.LastFrame.UnreachableLegs > 0)
                {
                    framesAffected++;
                    run++;
                    longestRun = Mathf.Max(longestRun, run);
                }
                else
                {
                    run = 0;
                }
            });

            // A step takes a good fraction of a second at this pace, so a leg may legitimately be
            // stretched for a few dozen frames while its swing carries it home. Never for seconds.
            Assert.Less(longestRun, 90,
                $"a leg stayed out of reach for {longestRun} frames ({framesAffected} affected in 400)");
        }

        /// Support is what keeps the hull up. However bad the ground gets, most of the feet stay down.
        [Test]
        public void EnoughFeetStayDownToHoldTheMachineUp()
        {
            int fewest = int.MaxValue;
            Walk(4f, 8f, 600, () =>
                fewest = Mathf.Min(fewest, locomotion.LastFrame.StanceLegs));

            Assert.GreaterOrEqual(fewest, 3, "the walker was standing on fewer than three feet");
        }

        // ─────────── the climb limit ───────────

        /// The limit has to leave normal ground alone. A slope well inside it is still climbed, and
        /// this is the test that fails first if the gate is ever tightened into a machine that will
        /// not go uphill at all.
        [Test]
        public void ASlopeInsideTheLimitIsStillClimbed()
        {
            Ramp(-120f, -60f, 20f);
            PlaceAt(new Vector3(-60f, 20f, -140f));

            Vector3 start = walker.transform.position;
            Walk(4f, 0f, 900, () => { });
            Vector3 end = walker.transform.position;

            Assert.Greater(end.y - start.y, 2f,
                $"the machine would not climb a 20 degree slope, well inside its 35 degree limit " +
                $"(start {start:F1} end {end:F1} blocked={locomotion.ClimbBlocked} " +
                $"scale={locomotion.ClimbScale:F2})");
        }

        /// And the limit has to actually bite. Driven flat out at a face half again as steep as it
        /// will walk up, the machine must not end up on top of it.
        [Test]
        public void ASlopePastTheLimitIsRefused()
        {
            Ramp(-120f, -60f, 55f);
            PlaceAt(new Vector3(-60f, 20f, -140f));

            float startY = walker.transform.position.y;
            bool everBlocked = false;
            Walk(4f, 0f, 900, () => everBlocked |= locomotion.ClimbBlocked);

            Assert.IsTrue(everBlocked, "the machine never reported being refused by the slope");
            Assert.Less(walker.transform.position.y - startY, 2f,
                "the machine climbed a 55 degree face it should have refused");
        }

        /// The one that matters most, and the reason the gate reads the COMMAND rather than the
        /// machine's own motion. A gate that latches is worse than no gate: the clock is advanced by
        /// distance travelled, so a machine that has stopped itself can never reopen a phase slice
        /// to get going again. Backing off has to remain available at all times.
        [Test]
        public void AMachineRefusedByASlopeCanStillBackAway()
        {
            Ramp(-120f, -60f, 55f);
            PlaceAt(new Vector3(-60f, 20f, -140f));

            Walk(4f, 0f, 900, () => { });
            Assert.IsTrue(locomotion.ClimbBlocked,
                "the machine was not standing refused at the slope, so this proves nothing");

            Vector3 stuck = walker.transform.position;
            Walk(-4f, 0f, 300, () => { });

            Assert.Greater(Vector3.Distance(stuck, walker.transform.position), 5f,
                "the machine could not reverse away from a slope that had refused it");
        }

        /// What the AI's detour is built on: asking about a heading the machine has not taken.
        [Test]
        public void CanTravelAnswersForAHeadingNotYetTaken()
        {
            Ramp(-120f, -60f, 55f);
            PlaceAt(new Vector3(-60f, 20f, -120f));

            Assert.IsFalse(locomotion.CanTravel(Vector3.forward),
                "straight up the 55 degree face");
            Assert.IsTrue(locomotion.CanTravel(Vector3.back),
                "away from the face, over the level ground it just crossed");
            Assert.IsTrue(locomotion.CanTravel(Vector3.right),
                "along the foot of the face, which is the way around it");
        }
    }
}
