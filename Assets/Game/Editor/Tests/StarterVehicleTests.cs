// The vehicle parked beside a landed hull: where it goes, what it stands on, and why it is only ever
// parked once per world.
//
// The failures worth catching are the quiet ones. An offset that clips the side stair still spawns a
// vehicle — inside the ship, where it shoves the hull or pins the ramp. A ground probe that takes an
// NPC standing on the spot for the ground parks the vehicle on the NPC's head. And a world that
// forgets it has been arrived in flies the arrival again on every load, parking another vehicle
// beside the wreck each time.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types,
// and an asmdef cannot reference Assembly-CSharp.
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay.Arrival;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTools
{
    public class StarterVehiclePlacementTests
    {
        private const float Tolerance = 0.001f;

        [Test]
        public void GroundPoint_TurnsTheOffsetWithTheHull()
        {
            // Yawed 90°, the hull's right (+x) points at world -z and its nose (+z) at world +x.
            var hull = new Vector3(10f, 5f, 20f);

            Vector2 toPort = StarterVehiclePlacement.GroundPoint(hull, 90f, new Vector2(-22f, 0f));
            Vector2 ahead = StarterVehiclePlacement.GroundPoint(hull, 90f, new Vector2(0f, 8f));

            Assert.AreEqual(10f, toPort.x, Tolerance);
            Assert.AreEqual(42f, toPort.y, Tolerance);
            Assert.AreEqual(18f, ahead.x, Tolerance);
            Assert.AreEqual(20f, ahead.y, Tolerance);
        }

        [Test]
        public void Facing_IsUprightAndTurnedFromTheHullsHeading()
        {
            Quaternion facing = StarterVehiclePlacement.Facing(30f, 15f);

            Assert.AreEqual(0f, Vector3.Angle(Vector3.up, facing * Vector3.up), Tolerance);
            Assert.AreEqual(45f, facing.eulerAngles.y, Tolerance);
        }

        [Test]
        public void Clearance_IsTheGapToTheFootprintLessTheVehiclesRadius()
        {
            var hull = new Bounds(Vector3.zero, new Vector3(20f, 5f, 30f));

            Assert.AreEqual(10f, StarterVehiclePlacement.Clearance(new Vector2(-22f, 0f), 2f, hull), Tolerance,
                            "abeam: 12 m from the side, less a 2 m radius");
            Assert.AreEqual(5f - 2f, StarterVehiclePlacement.Clearance(new Vector2(13f, 19f), 2f, hull), Tolerance,
                            "off a corner the gap is the diagonal");
            Assert.Less(StarterVehiclePlacement.Clearance(new Vector2(9f, 0f), 2f, hull), 0f,
                        "a vehicle whose centre is on the hull overlaps it");
            Assert.Less(StarterVehiclePlacement.Clearance(new Vector2(11f, 0f), 2f, hull), 0f,
                        "a vehicle whose edge reaches the hull overlaps it");
        }
    }

    public class StarterVehicleDeliveryTests
    {
        private const float Tolerance = 0.001f;

        private IWorldService previousWorld;
        private readonly System.Collections.Generic.List<GameObject> made = new();

        [SetUp]
        public void SetUp()
        {
            previousWorld = GameServices.World;
            GameServices.World = new WorldService();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in made.Where(g => g != null)) Object.DestroyImmediate(go);
            made.Clear();
            GameServices.World = previousWorld;
        }

        private GameObject Make(GameObject go)
        {
            made.Add(go);
            return go;
        }

        [Test]
        public void ParksBesideTheHull_OnTheStaticGround_NotOnABodyStandingThere()
        {
            const float groundY = 3f;

            GameObject ground = Make(GameObject.CreatePrimitive(PrimitiveType.Plane));
            ground.transform.position = new Vector3(0f, groundY, 0f);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);

            GameObject hull = Make(new GameObject("Hull"));
            hull.transform.SetPositionAndRotation(new Vector3(5f, groundY, -7f), Quaternion.Euler(0f, 90f, 0f));
            GameObject shell = Make(GameObject.CreatePrimitive(PrimitiveType.Cube));
            shell.transform.SetParent(hull.transform, false);
            shell.transform.localScale = new Vector3(20f, 8f, 28f);

            GameObject stranger = Make(new GameObject("Vehicle stand-in"));
            var starter = new ArrivalStarterVehicle { Prefab = stranger };
            Vector2 spot = StarterVehiclePlacement.GroundPoint(hull.transform.position, 90f, starter.HullOffset);

            // A kinematic body — an NPC, a mount, a crewmate — standing on the exact spot. The world's
            // surface is its STATIC collision (ShipGrounding.IsWorldSurface); this is not it.
            GameObject bystander = Make(GameObject.CreatePrimitive(PrimitiveType.Cube));
            bystander.transform.position = new Vector3(spot.x, groundY + 1.5f, spot.y);
            bystander.transform.localScale = Vector3.one * 3f;
            bystander.AddComponent<Rigidbody>().isKinematic = true;

            Physics.SyncTransforms();

            GameObject parked = Make(starter.Deliver(hull));

            Assert.IsNotNull(parked);
            Assert.AreEqual(spot.x, parked.transform.position.x, Tolerance);
            Assert.AreEqual(spot.y, parked.transform.position.z, Tolerance);
            Assert.That(parked.transform.position.y, Is.GreaterThan(groundY).And.LessThan(groundY + 1f),
                        "just above the plane, not on the bystander's head");
            Assert.AreEqual(0f, Vector3.Angle(Vector3.up, parked.transform.up), Tolerance, "upright");
            Assert.AreEqual(0f, Vector3.Angle(hull.transform.forward, parked.transform.forward), Tolerance,
                            "pointing the way the ship points");
        }

        /// <summary>
        /// The wheel persists by its OWN record, as a runtime-spawned world entity: a stamped prefab
        /// id the load can re-instantiate it from, world scope (an NPC-style spawn disowns it to an
        /// external owner, and no owner here would ever bring it back), and its pose in the bag.
        /// </summary>
        [Test]
        public void ThePlayersMonowheelIsParkedAsASavedWorldEntity()
        {
            GameObject ground = Make(GameObject.CreatePrimitive(PrimitiveType.Plane));
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            GameObject hull = Make(new GameObject("Hull"));
            Physics.SyncTransforms();

            var starter = new ArrivalStarterVehicle
            {
                Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StriderMonowheelBuilder.PlayerPrefabPath)
            };
            GameObject parked = Make(starter.Deliver(hull));

            var entity = parked.GetComponent<SaveableEntity>();
            Assert.IsNotNull(entity, "the parked wheel has no SaveableEntity, so it is gone on reload");
            Assert.IsTrue(entity.BelongsToWorld, "the parked wheel's record was disowned from the world save");
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(StriderMonowheelBuilder.PlayerPrefabPath), entity.PrefabId,
                            "a record whose prefab id resolves to nothing is dropped on load");

            var bag = new StateBag();
            entity.Capture(bag);
            Assert.IsTrue(bag.Has(TransformSaveable.Key), "the wheel would reload at its prefab's pose, not where it was left");
        }

        [Test]
        public void ParksNothingWhenNoVehicleIsConfigured()
        {
            GameObject hull = Make(new GameObject("Hull"));

            Assert.IsNull(new ArrivalStarterVehicle().Deliver(hull));
        }
    }

    public class StarterVehicleWiringTests
    {
        private const string ScenePath = "Assets/Game/Scenes/world/persistentScene.unity";
        private const string ShipPrefabPath = "Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab";

        /// <summary>
        /// Metres of open ground wanted between the parked vehicle and anything solid on the ship.
        /// Generous on purpose: the side stair slides out further than the stowed prefab shows, and a
        /// player stepping off the ramp should not walk into the vehicle.
        /// </summary>
        private const float MinClearance = 5f;

        /// <summary>
        /// The persistent scene's director — the values that actually fly, not the class's
        /// initialisers (a serialized field keeps its old value; INVARIANTS).
        /// </summary>
        private static void WithSceneStarter(System.Action<SerializedProperty> check)
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                ArrivalDirector director = scene.GetRootGameObjects()
                    .SelectMany(g => g.GetComponentsInChildren<ArrivalDirector>(true)).Single();
                check(new SerializedObject(director).FindProperty("starterVehicle"));
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void TheWorldParksThePlayersMonowheel()
        {
            WithSceneStarter(starter =>
            {
                Object prefab = starter.FindPropertyRelative("prefab").objectReferenceValue;

                Assert.IsNotNull(prefab, "persistentScene's ArrivalDirector parks nothing beside the ship");
                Assert.AreEqual(StriderMonowheelBuilder.PlayerPrefabPath, AssetDatabase.GetAssetPath(prefab));
            });
        }

        [Test]
        public void TheParkingSpotIsClearOfTheShipItsStairAndItsRamp()
        {
            Vector2 offset = default;
            WithSceneStarter(starter => offset = starter.FindPropertyRelative("hullOffset").vector2Value);

            GameObject ship = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath));
            GameObject wheel = Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>(StriderMonowheelBuilder.PlayerPrefabPath));
            try
            {
                ship.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                wheel.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Physics.SyncTransforms();

                // At the origin and unturned, world bounds ARE the hull's own axes.
                Bounds hull = SolidBounds(ship);
                Bounds body = SolidBounds(wheel);
                float radius = new Vector2(Mathf.Max(-body.min.x, body.max.x),
                                           Mathf.Max(-body.min.z, body.max.z)).magnitude;

                float clearance = StarterVehiclePlacement.Clearance(offset, radius, hull);

                Assert.GreaterOrEqual(clearance, MinClearance,
                    $"a {radius:F1} m vehicle at {offset} is {clearance:F1} m from the ship's solid " +
                    $"footprint {hull.min.x:F1}..{hull.max.x:F1} x {hull.min.z:F1}..{hull.max.z:F1}");
            }
            finally
            {
                Object.DestroyImmediate(ship);
                Object.DestroyImmediate(wheel);
            }
        }

        /// <summary>
        /// Every solid collider, the way <c>ShipHull</c> measures a hull: triggers are not the ship,
        /// and a collider physics has never seen reports an empty box at the world origin.
        /// </summary>
        private static Bounds SolidBounds(GameObject root)
        {
            Collider[] solid = root.GetComponentsInChildren<Collider>()
                .Where(c => !c.isTrigger && c.bounds.size != Vector3.zero).ToArray();
            Assert.IsNotEmpty(solid, $"'{root.name}' has no solid collider to measure");

            Bounds bounds = solid[0].bounds;
            foreach (Collider c in solid.Skip(1)) bounds.Encapsulate(c.bounds);
            return bounds;
        }
    }

    public class ArrivalOncePerWorldTests
    {
        private GameObject host;

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
        }

        private (ArrivalDirector Director, ArrivalSaveable Saveable) MakeDirector()
        {
            host = new GameObject("Arrival");
            var director = host.AddComponent<ArrivalDirector>();
            var saveable = host.AddComponent<ArrivalSaveable>();

            var so = new SerializedObject(director);
            so.FindProperty("shipPrefab").objectReferenceValue = host;
            so.ApplyModifiedPropertiesWithoutUndo();

            return (director, saveable);
        }

        [Test]
        public void ANewWorldIsStillToBeArrivedIn()
        {
            (ArrivalDirector director, ArrivalSaveable saveable) = MakeDirector();

            Assert.IsTrue(director.IsPending);
            Assert.IsFalse(((ArrivalSaveable.State)saveable.CaptureState()).arrived);
        }

        /// <summary>
        /// What keeps the starter vehicle to one per world: the vehicle is parked by a landing, and a
        /// world that reloads as arrived never flies one.
        /// </summary>
        [Test]
        public void AWorldSavedAsArrivedNeverFliesTheArrivalAgain()
        {
            (ArrivalDirector director, ArrivalSaveable saveable) = MakeDirector();

            saveable.RestoreState(JObject.FromObject(new ArrivalSaveable.State { arrived = true }));

            Assert.IsFalse(director.IsPending, "a reloaded world must not re-crash, or re-park its vehicle");
            Assert.IsTrue(((ArrivalSaveable.State)saveable.CaptureState()).arrived, "and it stays arrived");
        }
    }
}
