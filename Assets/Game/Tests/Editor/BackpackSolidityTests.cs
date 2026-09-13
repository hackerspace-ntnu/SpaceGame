using System.Collections;
using System.Reflection;
using NUnit.Framework;
using SpaceGame.Items;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// When the rig is a solid body, and when it is only a picture of one.
    ///
    /// <para>
    /// The rig's body collider is fitted to the UNFOLDED pack — 1.81 x 0.87 x 2.19 m on the shipped
    /// one, see <c>ExpeditionRigWiring.EnsureBodyCollider</c> — and it has no Rigidbody, so off a
    /// back it is a STATIC collider. A deploy flies that box from 0.45 m in front of the wearer's
    /// chest down to the sand a pace ahead, and the box reaches 1.58 m along its own +Z, which the
    /// landing rotation points AT the player. So for the whole flight the box encloses the wearer,
    /// and the shallowest way out of it is straight DOWN — 0.84 m of it against a 3 m capsule.
    /// PhysX resolves that the only way it can: it drives the player down. On sand nobody notices,
    /// because the terrain shoves them back and <c>UnderTerrainGuard</c> catches whatever is left.
    /// On a built floor — a ship's cargo bay, a deck, a platform — 0.84 m is straight through it,
    /// and the player ends up underneath. That was reported as "I put my backpack down inside the
    /// spaceship and spawned under the spaceship", and the ship had nothing to do with it.
    /// </para>
    /// <para>
    /// So the rule these pin is: <b>the rig is a body only while it is standing still on the
    /// ground.</b> Worn is off (it would ride the wearer's own compound collider), in flight is off
    /// (an animated transform is not a body), settled is on.
    /// </para>
    /// <para>
    /// The PACK here is deactivated, the way <c>BackpackFoldTests</c>' rigs are — the fold answers
    /// an object that is not running with the settled pose instead of a coroutine — while the
    /// WEARER is left active, because the controller has to be able to start the arc. Neither is a
    /// detail to swap round: an EditMode <c>StartCoroutine</c> on an active object runs the first
    /// segment and stops there, which is all this needs, but on an inactive one Unity logs an
    /// error and hands back null, and an unexpected error fails the test that provoked it.
    /// </para>
    /// </summary>
    public class BackpackSolidityTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private const string RigPrefab = "Assets/Game/Prefabs/Items/Equipment/ExpeditionRig.prefab";
        private const string PlayerPrefab = "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";

        /// <summary>Where the pack is set down in these tests: a pace in front, on flat ground.</summary>
        private static readonly Pose Grounded = new(new Vector3(0f, 0f, 2.96f), Quaternion.identity);

        private GameObject packGo;
        private GameObject wearerGo;

        [TearDown]
        public void CleanUp()
        {
            if (packGo != null) UnityEngine.Object.DestroyImmediate(packGo);
            if (wearerGo != null) UnityEngine.Object.DestroyImmediate(wearerGo);
        }

        // ─────────── the rule ───────────

        [Test]
        public void AWornPackIsNotABody()
        {
            Fixture rig = BuildRig();

            Invoke(rig.Controller, "SnapToWorn");

            Assert.IsFalse(rig.Body.enabled,
                "colliders under one Rigidbody are one compound collider, so a solid worn pack " +
                "bolts a 2 m box onto the wearer's capsule");
        }

        [Test]
        public void APackInFlightIsNotABody()
        {
            Fixture rig = BuildRig();
            Invoke(rig.Controller, "SnapToWorn");

            StartTheDeployFlight(rig.Controller);

            Assert.AreEqual(BackpackController.State.Deploying, rig.Controller.CurrentState,
                            "the flight has to have started for the rest of this to mean anything");

            Assert.IsFalse(rig.Body.enabled,
                "the toss starts 0.45 m in front of the wearer and the box reaches back further " +
                "than that — switching it on there pushes them down through whatever they are " +
                "standing on");
        }

        [Test]
        public void APackStandingOnTheGroundIsABody()
        {
            Fixture rig = BuildRig();
            Invoke(rig.Controller, "SnapToWorn");

            StartTheDeployFlight(rig.Controller);
            Invoke(rig.Controller, "FinishDeploy", Grounded);

            Assert.AreEqual(BackpackController.State.Open, rig.Controller.CurrentState);

            Assert.IsTrue(rig.Body.enabled,
                "a pack on the sand is a thing in the world: it is what the crosshair aims at to " +
                "open it, and what a player walks into rather than through");
        }

        [Test]
        public void APackFlyingHomeIsNotABody()
        {
            Fixture rig = BuildRig();
            Invoke(rig.Controller, "SnapToWorn");
            StartTheDeployFlight(rig.Controller);
            Invoke(rig.Controller, "FinishDeploy", Grounded);

            Assume.That(rig.Body.enabled, "the stow starts from a pack that IS a body");

            // Stepped by hand rather than started: RunStow folds the rig on the ground first and
            // only then flies, so one MoveNext runs everything up to the arc — which is exactly
            // the moment the pack leaves the ground.
            var stow = (IEnumerator)rig.Controller.GetType().GetMethod("RunStow", Hidden)
                                       .Invoke(rig.Controller, null);

            Assert.IsTrue(stow.MoveNext(), "RunStow should reach its flight and yield on it");

            Assert.IsFalse(rig.Body.enabled,
                "the stow arc goes up over the wearer's own head — a solid pack on that path " +
                "swats them into the ground");
        }

        // ─────────── the geometry that forces it ───────────

        [Test]
        public void TheTossStartsWithTheRigsOwnBoxAroundTheWearer()
        {
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            Assert.IsNotNull(rig, $"the shipped rig is at {RigPrefab}");

            var box = rig.GetComponent<BoxCollider>();
            Assert.IsNotNull(box, "the rig carries its body collider on its root");

            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Assert.IsNotNull(player, $"the wearer is at {PlayerPrefab}");

            var controller = player.GetComponent<BackpackController>();
            Assert.IsNotNull(controller, "the wearer carries the controller that throws it");

            // The landing rotation is LookRotation(towardPlayer, ...) — see
            // BackpackController.TryFindGroundPose — so the rig's +Z points AT the wearer, and
            // this is how far the box reaches along it.
            float reachAtTheWearer = box.center.z + box.size.z * 0.5f;
            float startsInFront = Field<float>(controller, "tossStartForward");

            Assert.Greater(reachAtTheWearer, startsInFront,
                $"the box reaches {reachAtTheWearer:F2} m back toward the wearer from a toss that " +
                $"starts {startsInFront:F2} m in front of them, so its rear face is BEHIND their " +
                "own axis on the first frame of the flight. That is why a flying pack is not a " +
                "body — and if these numbers ever change so that it clears them, this test is " +
                "where to come and say so.");
        }

        // ─────────── fixtures ───────────

        private readonly struct Fixture
        {
            public readonly BackpackController Controller;
            public readonly Collider Body;

            public Fixture(BackpackController controller, Collider body)
            {
                Controller = controller;
                Body = body;
            }
        }

        /// <summary>
        /// A pack and the wearer it answers to, wired by hand — the same build as
        /// <c>BackpackNetworkingTests.DeployedPack</c>, which is where the reasoning for each line
        /// is written out. The pack is inactive and the wearer is not; see the class summary.
        /// </summary>
        private Fixture BuildRig()
        {
            packGo = new GameObject("pack");
            packGo.SetActive(false);

            Collider body = packGo.AddComponent<BoxCollider>();

            var surfaceGo = new GameObject("SURF_Leaf");
            surfaceGo.transform.SetParent(packGo.transform, false);

            var surface = surfaceGo.AddComponent<PackSurface>();
            surface.GetType().GetField("id", Hidden).SetValue(surface, PackSurfaceId.Leaf);
            surface.GetType().GetField("size", Hidden).SetValue(surface, new Vector2(0.9f, 0.75f));

            var pack = packGo.AddComponent<BackpackObject>();
            Invoke(pack, "Awake");

            wearerGo = new GameObject("wearer");

            var controller = wearerGo.AddComponent<BackpackController>();

            pack.Bind(controller);
            controller.GetType().GetField("<Pack>k__BackingField", Hidden).SetValue(controller, pack);

            return new Fixture(controller, body);
        }

        /// <summary>
        /// Put the controller into the deploy flight. The arc starts and immediately stops at its
        /// first yield — EditMode never ticks it on — which leaves the state machine exactly where
        /// a flying pack sits.
        /// </summary>
        private static void StartTheDeployFlight(BackpackController controller) =>
            Invoke(controller, "StartDeploy", Grounded);

        private static void Invoke(object target, string method, params object[] args)
        {
            MethodInfo found = target.GetType().GetMethod(method, Hidden);
            Assert.IsNotNull(found, $"{target.GetType().Name} has no {method}");
            found.Invoke(target, args);
        }

        private static T Field<T>(object target, string name)
        {
            FieldInfo found = target.GetType().GetField(name, Hidden);
            Assert.IsNotNull(found, $"{target.GetType().Name} has no {name}");
            return (T)found.GetValue(target);
        }
    }
}
