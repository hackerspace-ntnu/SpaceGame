// The whole craft, sailed.
//
// SailAerodynamicsTests and FoilPhysicsTests pin the rules. These pin what the rules add up to when
// they are wired into the real prefab and stepped: does it sail, does it stop, does the wheel point
// it, does the hull hold still. Every one of those was broken at some point in a way that the
// pure-function tests could not see, because each function was individually defensible and the
// combination was not.
//
// Driven through DuneFoilLocomotion.Step at a fixed time step rather than through a play session,
// the same way WalkerPlatformCarrier.CarryRiders is driven — deterministic, no frames, no waiting.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Vehicles.DuneFoil;

namespace SpaceGame.Tests
{
    public class DuneFoilCraftTests
    {
        private const string PrefabPath =
            "Assets/Game/Prefabs/Agents/Vehicles/Ground/DuneFoil.prefab";

        private const float Step = 1f / 60f;

        /// Far from anything else in any open scene, with its own ground and its own wind.
        private static readonly Vector3 Berth = new Vector3(100000f, 0f, 100000f);

        private GameObject ground;
        private GameObject windObject;
        private GameObject craft;

        private DuneFoilLocomotion locomotion;
        private SailRig rig;
        private FoilLift foil;
        private FoilRudder rudder;
        private float windToDegrees;

        [SetUp]
        public void SetUp()
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "DuneFoilTestGround";
            ground.transform.position = Berth + Vector3.down * 5f;
            ground.transform.localScale = new Vector3(40000f, 10f, 40000f);

            windObject = new GameObject("DuneFoilTestWind");
            windObject.transform.position = Berth;
            // WindField is [ExecuteAlways], so this registers itself as the scene wind here in
            // the editor without a play session.
            windObject.AddComponent<WindField>().SetWind(0f, 12f);

            Assert.IsNotNull(WindField.Active,
                "No WindField registered. With no wind the rig makes nothing and every test below " +
                "passes for the wrong reason.");

            Vector3 to = WindField.Active.Direction;
            windToDegrees = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        }

        [TearDown]
        public void TearDown()
        {
            if (craft != null) Object.DestroyImmediate(craft);
            if (windObject != null) Object.DestroyImmediate(windObject);
            if (ground != null) Object.DestroyImmediate(ground);
        }

        /// <param name="offWind">Heading relative to the way the wind is going. -90 is a beam reach,
        /// 180 puts the craft head to wind.</param>
        private void Launch(float offWind)
        {
            if (craft != null) Object.DestroyImmediate(craft);

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(source, $"No craft at {PrefabPath}. Rebuild it from " +
                                     "Tools > Vehicles > Build Dune Foil Prefab.");

            craft = (GameObject)PrefabUtility.InstantiatePrefab(source);
            craft.transform.position = Berth;
            craft.transform.rotation = Quaternion.Euler(0f, windToDegrees + offWind, 0f);

            locomotion = craft.GetComponent<DuneFoilLocomotion>();
            rig = craft.GetComponent<SailRig>();
            foil = craft.GetComponent<FoilLift>();
            rudder = craft.GetComponent<FoilRudder>();

            // Nobody is aboard in a test, and the mooring is what would otherwise hold the hull.
            locomotion.HoldStation = false;
            locomotion.Halt();
        }

        private void Sail(float seconds, float helm = 0f)
        {
            int steps = Mathf.RoundToInt(seconds / Step);
            for (int i = 0; i < steps; i++)
            {
                if (helm != 0f) locomotion.SetRudder(helm);
                locomotion.Step(Step);
            }
        }

        // --- does it sail ------------------------------------------------------

        [Test]
        public void OnABeamReach_TheCraftSailsAndGetsUpOnItsFoil()
        {
            Launch(-90f);
            Sail(70f);

            Assert.Greater(locomotion.Speed, 6f,
                $"A beam reach in a 12 m/s breeze made only {locomotion.Speed:F1} m/s.");
            Assert.Greater(foil.RideHeight01, 0.2f,
                $"At {locomotion.Speed:F1} m/s the hull should be up on the foil; it is at " +
                $"{foil.RideHeight:F1} m of {foil.MaxRideHeight:F1}.");
        }

        [Test]
        public void HeadToWind_TheCraftMakesNoWay()
        {
            Launch(180f);
            Sail(45f);

            Assert.Less(locomotion.Speed, 1.5f,
                $"Pointing straight into the wind, the craft still made {locomotion.Speed:F2} m/s.");
        }

        [Test]
        public void WithEverySailStruck_TheCraftComesToAGenuineStop()
        {
            Launch(-90f);
            Sail(40f);
            float underWay = locomotion.Speed;
            Assert.Greater(underWay, 3f, "The craft has to be moving before this test means anything.");

            foreach (SailSurface sail in rig.Sails) sail.SetHoist(0f);
            Sail(30f);

            Assert.AreEqual(0f, locomotion.Speed, 1e-3f,
                $"Struck the whole rig from {underWay:F1} m/s and it is still making " +
                $"{locomotion.Speed:F3} m/s. A craft that never quite stops reads as broken.");
        }

        // --- does it hold still ------------------------------------------------

        [Test]
        public void TheHullNeverJumpsVertically()
        {
            // The deck is a walking surface and every rider on it is moved by the hull's own
            // frame-to-frame delta, so vertical noise in the hull IS vertical noise in the player.
            Launch(-90f);
            Sail(25f);

            float worst = 0f;
            float previous = craft.transform.position.y;
            for (int i = 0; i < 600; i++)
            {
                locomotion.Step(Step);
                float y = craft.transform.position.y;
                worst = Mathf.Max(worst, Mathf.Abs(y - previous));
                previous = y;
            }

            Assert.Less(worst, 0.06f,
                $"The hull moved {worst * 1000f:F0} mm in a single frame under way.");
        }

        [Test]
        public void AMooredCraft_SitsPerfectlyStill()
        {
            Launch(-90f);
            locomotion.HoldStation = true;
            Sail(5f);

            float settled = craft.transform.position.y;
            float drift = 0f;
            for (int i = 0; i < 300; i++)
            {
                locomotion.Step(Step);
                drift = Mathf.Max(drift, Mathf.Abs(craft.transform.position.y - settled));
            }

            Assert.Less(drift, 0.01f,
                $"A craft at its berth drifted {drift * 1000f:F1} mm vertically over five seconds.");
        }

        // --- does the wheel point it -------------------------------------------

        [Test]
        public void TheWheelTurnsTheCraft_BothWays()
        {
            Launch(-90f);
            Sail(45f);
            float before = craft.transform.eulerAngles.y;
            Sail(6f, helm: 1f);
            float toStarboard = Mathf.DeltaAngle(before, craft.transform.eulerAngles.y);

            Launch(-90f);
            Sail(45f);
            before = craft.transform.eulerAngles.y;
            Sail(6f, helm: -1f);
            float toPort = Mathf.DeltaAngle(before, craft.transform.eulerAngles.y);

            Assert.Greater(toStarboard, 15f,
                $"Hard a-starboard for six seconds turned the craft {toStarboard:F1} degrees.");
            Assert.Less(toPort, -15f,
                $"Hard a-port for six seconds turned the craft {toPort:F1} degrees.");
        }

        [Test]
        public void TheWheelSwingsTheFoilItself()
        {
            Launch(-90f);
            Transform strut = FindDeep(craft.transform, "Foil_Strut");
            Assert.IsNotNull(strut, "The craft has no Foil_Strut node to steer.");

            Quaternion amidships = strut.localRotation;
            Sail(3f, helm: 1f);

            float swung = Quaternion.Angle(amidships, strut.localRotation);
            Assert.Greater(swung, 20f,
                $"The wheel is supposed to turn the FOIL, and the strut moved {swung:F1} degrees.");
            Assert.AreEqual(Mathf.Abs(rudder.SteerAngle), swung, 1.5f,
                "The blade the player can see must be at the angle the model is steering with.");
        }

        [Test]
        public void LettingGoOfTheHelm_CentresTheBlade()
        {
            Launch(-90f);
            Sail(3f, helm: 1f);
            Assert.Greater(Mathf.Abs(rudder.SteerAngle), 20f, "The blade has to be over first.");

            Sail(5f);       // nobody at the wheel

            Assert.Less(Mathf.Abs(rudder.SteerAngle), 1f,
                $"Five seconds after standing down the blade is still {rudder.SteerAngle:F1} " +
                "degrees over; the craft would sail itself in a circle.");
            Assert.IsFalse(locomotion.IsHelmDriven);
        }

        [Test]
        public void AStoppedCraft_CanStillBePointed_ButDoesNotPivot()
        {
            // Losing steerage when you stall is the point of stalling. Losing it completely means
            // a craft that has stopped on a dune face can never be got off it again.
            Launch(180f);
            foreach (SailSurface sail in rig.Sails) sail.SetHoist(0f);
            locomotion.Halt();
            Sail(3f);

            float before = craft.transform.eulerAngles.y;
            Sail(5f, helm: 1f);
            float turned = Mathf.Abs(Mathf.DeltaAngle(before, craft.transform.eulerAngles.y));

            Assert.Greater(turned, 1f, "A stopped craft must still be pointable.");
            Assert.Less(turned, 40f,
                $"A stopped seventeen-metre hull swung {turned:F0} degrees in five seconds; " +
                "it should shuffle, not pirouette.");
        }

        // --- are the sails a control -------------------------------------------

        [Test]
        public void TheSheet_IsWhatSetsTheSailAngle()
        {
            Launch(-90f);
            Sail(20f);

            SailSurface main = rig.MainSail;
            Assert.IsNotNull(main, "The rig has no main.");

            main.SetSheet(0f);
            Sail(4f);
            float hardIn = main.BoomAngle;

            main.SetSheet(1f);
            Sail(4f);
            float rightOut = main.BoomAngle;

            Assert.Greater(Mathf.Abs(rightOut), Mathf.Abs(hardIn) + 15f,
                $"Paying out the whole sheet moved the boom from {hardIn:F0} to {rightOut:F0} " +
                "degrees off the centreline. Rope length is supposed to BE the control.");
        }

        [Test]
        public void TheWingPanels_AreTrimmedWithTheMain()
        {
            // Otherwise they are two sails making force at whatever trim they were serialised
            // with, biasing the helm for reasons the player cannot see or change.
            Launch(-90f);
            rig.MainSail.SetSheet(1f);
            Sail(2f);

            foreach (SailSurface sail in rig.Sails)
            {
                if (sail == rig.Jib) continue;
                Assert.AreEqual(1f, sail.SheetOut, 0.01f,
                    $"{sail.name} did not follow the main sheet.");
            }
        }

        // --- is the post a control ----------------------------------------------

        [Test]
        public void LeaningThePostToWindward_StandsTheCraftUp_AndCostsDrive()
        {
            Launch(-90f);
            Sail(35f);

            float upright = locomotion.Heel;
            Assert.Greater(Mathf.Abs(upright), 0.5f,
                "The craft has to be heeling before leaning the post can be shown to fix it.");

            SailSurface main = rig.MainSail;
            main.SetCant(upright >= 0f ? -1f : 1f);     // into the wind, opposite the heel
            Sail(12f);

            Assert.Less(Mathf.Abs(locomotion.Heel), Mathf.Abs(upright),
                $"Post hard over to windward and the heel went {upright:F2} -> " +
                $"{locomotion.Heel:F2} degrees.");
            Assert.Less(main.EffectiveArea, main.Area,
                "A leaning sail presents less of itself to the wind; that is what it costs.");
        }

        [Test]
        public void ThePostLeansAcrossTheHull_NotForeAndAft()
        {
            // The control used to rake the mast fore and aft. The brief is a post that lowers
            // toward the left and toward the right, and the difference is visible on the model:
            // the masthead has to move sideways.
            Launch(-90f);
            Sail(1f);

            Transform post = FindDeep(craft.transform, "Main_Post");
            Assert.IsNotNull(post, "No Main_Post to measure.");

            Vector3 upright = LocalTop(post);
            rig.MainSail.SetCant(1f);
            Sail(6f);
            Vector3 leaned = LocalTop(post);

            float across = Mathf.Abs(leaned.x - upright.x);
            float foreAft = Mathf.Abs(leaned.z - upright.z);

            Assert.Greater(across, 1f,
                $"The masthead moved {across:F2} m across the hull; the post is barely leaning.");
            Assert.Greater(across, foreAft,
                $"The masthead moved {foreAft:F2} m fore and aft against {across:F2} m across. " +
                "This control is a cant, not a rake.");
        }

        private Vector3 LocalTop(Transform node)
        {
            Renderer[] renderers = node.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return craft.transform.InverseTransformPoint(bounds.center);
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
