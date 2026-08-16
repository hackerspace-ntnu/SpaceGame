// Marches the ostrich's gait forward and checks the properties that make it a walk rather than a
// slide. Everything here runs against the real prefab and the real rig: OstrichLocomotion exposes
// Initialise() and Step(dt) precisely so the machine can be driven deterministically without a
// player loop, and the invariants below are the ones that broke while it was being built.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Creatures.Ostrich;
using SpaceGame.Locomotion;

namespace SpaceGame.Tests
{
    public class OstrichLocomotionTests
    {
        private const string PrefabPath = "Assets/Game/Prefabs/agents/creatures/Ostrich.prefab";
        private const float Dt = 1f / 60f;
        private const int Frames = 600;

        /// The gait as it ran over one trial, reduced to the numbers worth asserting on.
        private struct Trace
        {
            public float WorstPlantedSlip;      // a planted foot must not move, at all
            public float BodyBob;               // peak-to-peak height of the body
            public float HighestPlantedFoot;    // above the ground plane
            public int StepsLeft, StepsRight;
            public int AirborneFrames;          // both feet off the ground
            public int SingleSupportFrames;
            public int FallingFrames;           // gravity claimed the height
            public float TravelledFlat;
            public float MaxSpeed;
        }

        private GameObject ground;
        private GameObject bird;

        [TearDown]
        public void TearDown()
        {
            if (bird != null) Object.DestroyImmediate(bird);
            if (ground != null) Object.DestroyImmediate(ground);
        }

        private OstrichLocomotion Spawn() => Spawn(1.2f, true);

        /// Spawn height and ground are parameters so a test can reproduce the world scene, where the
        /// bird exists in the persistent scene and the terrain under it streams in later or not at all.
        private OstrichLocomotion Spawn(float spawnHeight, bool groundActive)
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "TestGround";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(900f, 1f, 900f);
            ground.SetActive(groundActive);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "Ostrich prefab missing at " + PrefabPath);

            bird = Object.Instantiate(prefab);
            bird.transform.position = new Vector3(0f, spawnHeight, 0f);

            // Only the locomotion is under test; the driver and mount would fight it for input.
            foreach (MonoBehaviour mb in bird.GetComponents<MonoBehaviour>())
                if (!(mb is OstrichLocomotion)) mb.enabled = false;

            Physics.SyncTransforms();

            var loco = bird.GetComponent<OstrichLocomotion>();
            loco.Initialise();
            return loco;
        }

        private Trace March(OstrichLocomotion loco, float speedFraction, float yawRate)
        {
            loco.SnapToGround();

            var t = new Trace { MaxSpeed = loco.MaxSpeed };
            loco.SetTwist(loco.MaxSpeed * speedFraction, yawRate);

            Vector3 start = bird.transform.position;
            var lastFoot = new Vector3[2];
            var wasPlanted = new bool[2];
            var wasSwinging = new bool[2];
            float minY = float.MaxValue, maxY = float.MinValue;
            t.HighestPlantedFoot = float.MinValue;

            for (int i = 0; i < Frames; i++)
            {
                loco.Step(Dt);
                OstrichLocomotion.Diagnostics d = loco.LastFrame;

                if (d.Airborne) t.AirborneFrames++;
                if (d.StanceLegs == 1) t.SingleSupportFrames++;
                if (loco.IsFalling) t.FallingFrames++;

                for (int leg = 0; leg < 2; leg++)
                {
                    if (!loco.TryGetFoot(leg, out Vector3 foot, out bool swinging)) continue;

                    if (!swinging)
                    {
                        if (wasPlanted[leg])
                            t.WorstPlantedSlip = Mathf.Max(t.WorstPlantedSlip,
                                                           Vector3.Distance(foot, lastFoot[leg]));
                        t.HighestPlantedFoot = Mathf.Max(t.HighestPlantedFoot, foot.y);
                    }

                    if (swinging && !wasSwinging[leg])
                    {
                        if (leg == 0) t.StepsLeft++; else t.StepsRight++;
                    }

                    wasSwinging[leg] = swinging;
                    wasPlanted[leg] = !swinging;
                    lastFoot[leg] = foot;
                }

                // skip the settling frames: the first stride is spent finding the ground
                if (i > 90)
                {
                    minY = Mathf.Min(minY, bird.transform.position.y);
                    maxY = Mathf.Max(maxY, bird.transform.position.y);
                }
            }

            t.BodyBob = maxY - minY;
            t.TravelledFlat = Vector2.Distance(new Vector2(start.x, start.z),
                                               new Vector2(bird.transform.position.x, bird.transform.position.z));
            return t;
        }

        [Test]
        public void RigIsDiscovered()
        {
            OstrichLocomotion loco = Spawn();
            Assert.IsTrue(loco.IsReady, "WalkerRig found no Coxa_/Hip_/Knee_/Ankle_ chains on the prefab");
            Assert.AreEqual(2, loco.LegCount, "an ostrich has two legs");
            Assert.Greater(loco.MaxSpeed, 1f, "stride and cadence must imply a usable top speed");
            Assert.Less(loco.MaxSpeed, 30f, "top speed is implausible; stride is probably over-derived");
        }

        /// The one that matters. A planted foot is a contract with the ground: if it moves, the bird
        /// is skating and no amount of leg animation will hide it.
        [Test]
        public void PlantedFeetDoNotSlide([Values(0f, 0.25f, 0.5f, 0.95f)] float speedFraction)
        {
            Trace t = March(Spawn(), speedFraction, 0f);
            Assert.Less(t.WorstPlantedSlip, 0.01f,
                        "a planted foot moved " + t.WorstPlantedSlip.ToString("F4") + " m");
        }

        [Test]
        public void PlantedFeetStayOnTheGround()
        {
            Trace t = March(Spawn(), 0.5f, 0f);
            Assert.Less(t.HighestPlantedFoot, 0.1f,
                        "a foot was planted in mid-air at y=" + t.HighestPlantedFoot.ToString("F3") +
                        "; footholds are being clamped toward the hip instead of onto the ground");
        }

        /// The body rides on the ground, not on its own feet. Deriving height from the feet alone is a
        /// feedback loop that used to send the bird six metres into the air.
        [Test]
        public void BodyDoesNotClimb([Values(0.25f, 0.5f, 0.95f)] float speedFraction)
        {
            Trace t = March(Spawn(), speedFraction, 0f);
            Assert.Less(t.BodyBob, 0.35f, "body height swung by " + t.BodyBob.ToString("F3") + " m");
            Assert.Greater(t.BodyBob, 0.001f, "the body should bob a little while walking");
        }

        [Test]
        public void GaitAlternates()
        {
            Trace t = March(Spawn(), 0.25f, 0f);
            Assert.Greater(t.StepsLeft, 3, "left leg barely stepped");
            Assert.Greater(t.StepsRight, 3, "right leg barely stepped");
            Assert.LessOrEqual(Mathf.Abs(t.StepsLeft - t.StepsRight), 2,
                               "the legs are not sharing the work: " + t.StepsLeft + " vs " + t.StepsRight);
        }

        /// A walk keeps a foot down. Both feet leaving the ground at walking pace means the two legs
        /// have fallen into step with each other, which is a hop.
        [Test]
        public void WalkKeepsAFootDown()
        {
            Trace t = March(Spawn(), 0.25f, 0f);
            Assert.Greater(t.SingleSupportFrames, Frames / 2,
                           "a walk should spend most of its time on one foot");
            Assert.Less(t.AirborneFrames, Frames / 10,
                        "both feet were off the ground for " + t.AirborneFrames + " frames at a walk");
        }

        /// A run is allowed -- expected, even -- to leave the ground. This is the counterpart to the
        /// test above, and together they say the gait changes character with speed.
        [Test]
        public void RunHasAFlightPhase()
        {
            Trace t = March(Spawn(), 0.95f, 0f);
            Assert.Greater(t.AirborneFrames, 20, "a run should have moments with both feet in the air");
        }

        [Test]
        public void TravelsAtTheCommandedSpeed()
        {
            OstrichLocomotion loco = Spawn();
            Trace t = March(loco, 0.5f, 0f);
            float expected = loco.MaxSpeed * 0.5f * Frames * Dt;
            Assert.That(t.TravelledFlat, Is.EqualTo(expected).Within(expected * 0.15f),
                        "asked for " + expected.ToString("F1") + " m, travelled " + t.TravelledFlat.ToString("F1"));
        }

        [Test]
        public void StandingStillTakesNoSteps()
        {
            Trace t = March(Spawn(), 0f, 0f);
            Assert.LessOrEqual(t.StepsLeft + t.StepsRight, 2, "the bird stepped while standing still");
            Assert.Less(t.TravelledFlat, 0.05f, "the bird drifted while standing still");
        }

        // ─────────── gravity ───────────

        /// A bird that finds itself in mid-air has to come down.
        ///
        /// It used to hang there indefinitely. The body's height is carried by its planted feet, and a
        /// foot stranded in the air holds the body up exactly as well as one standing on rock.
        [Test]
        public void SpawnedInTheAirItFalls()
        {
            OstrichLocomotion loco = Spawn(20f, true);

            for (int i = 0; i < 300; i++) loco.Step(Dt);

            Assert.That(bird.transform.position.y, Is.EqualTo(loco.RideHeight).Within(0.15f),
                        "dropped from 20 m, the bird settled at y=" +
                        bird.transform.position.y.ToString("F2") + " instead of its ride height");
        }

        /// Falling, not sinking. Routing the descent through the height smoothing gives an exponential
        /// glide that covers the same ground but reads as the bird being lowered on a wire.
        [Test]
        public void TheFallAccelerates()
        {
            OstrichLocomotion loco = Spawn(20f, true);

            var y = new float[60];
            for (int i = 0; i < y.Length; i++)
            {
                loco.Step(Dt);
                y[i] = bird.transform.position.y;
            }

            float early = y[9] - y[19];
            float late = y[39] - y[49];
            Assert.Greater(late, early * 1.5f,
                           "fell " + early.ToString("F2") + " m early and " + late.ToString("F2") +
                           " m later; a fall under gravity speeds up, a lerp slows down");
        }

        /// The bug as it happens in the world scene. The bird is placed in the persistent scene, so it
        /// exists before the terrain chunk beneath it has streamed in: its first ground ray finds
        /// nothing, its feet stay stranded at the rest pose, and when the ground finally arrives those
        /// stranded feet go on holding the body up over it.
        [Test]
        public void GroundArrivingLateBringsTheBirdDown()
        {
            OstrichLocomotion loco = Spawn(20f, false);

            for (int i = 0; i < 60; i++) loco.Step(Dt);
            Assert.Greater(bird.transform.position.y, 19f,
                           "with no ground anywhere under it the bird must hold its height rather " +
                           "than fall through an unloaded world");

            ground.SetActive(true);
            Physics.SyncTransforms();
            for (int i = 0; i < 300; i++) loco.Step(Dt);

            Assert.That(bird.transform.position.y, Is.EqualTo(loco.RideHeight).Within(0.15f),
                        "ground appeared underneath and the bird stayed at y=" +
                        bird.transform.position.y.ToString("F2"));
        }

        /// Landing has to hand the gait real footholds. Left on the ones it was stranded with, the bird
        /// stands on the ground with its feet still up where it fell from.
        [Test]
        public void LandingRePlantsTheFeet()
        {
            OstrichLocomotion loco = Spawn(20f, true);

            for (int i = 0; i < 300; i++) loco.Step(Dt);

            for (int leg = 0; leg < 2; leg++)
            {
                Assert.IsTrue(loco.TryGetFoot(leg, out Vector3 foot, out _));
                Assert.Less(Mathf.Abs(foot.y), 0.1f,
                            "foot " + leg + " is at y=" + foot.y.ToString("F2") + " after landing");
            }
        }

        /// The counterpart to RunHasAFlightPhase. A run genuinely leaves the ground for a fraction of a
        /// second, and reading that as a fall would drop the bird onto its own stride.
        [Test]
        public void RunningOnFlatGroundNeverFalls()
        {
            Trace t = March(Spawn(), 0.95f, 0f);
            Assert.AreEqual(0, t.FallingFrames,
                            "the run's flight phase was mistaken for a fall on " + t.FallingFrames +
                            " of " + Frames + " frames");
        }

        [Test]
        public void TurningDoesNotDragTheFeet([Values(45f, -80f)] float yawRate)
        {
            Trace t = March(Spawn(), 0.35f, yawRate);
            Assert.Less(t.WorstPlantedSlip, 0.01f,
                        "a planted foot was dragged " + t.WorstPlantedSlip.ToString("F4") + " m through a turn");
        }
    }
}
