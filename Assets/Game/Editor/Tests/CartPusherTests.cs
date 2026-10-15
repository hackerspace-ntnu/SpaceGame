// A body with its hands on a cart puts its hands on the handles, keeps the cart ahead of it as it turns and walks, takes the cart
// off everybody else, and gives the cart and the collisions back when it lets go. A resident does the same for its work.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceGame.Agents.Residents;
using SpaceGame.Items;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class CartPusherTests
    {
        private const string RaxyPath = "Assets/Game/Prefabs/Agents/Characters/Raxy/Raxy_handyman.prefab";
        private const string Decorations = "Assets/Game/Prefabs/Environment/Decorations/";
        private const float Stage = 700f;
        private const float HandsOnTheGrip = 0.06f;

        private readonly List<GameObject> made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in made)
            {
                if (go == null) continue;
                go.GetComponent<CartPusher>()?.Release();
                go.GetComponent<Pushable>()?.Unregister();
                Object.DestroyImmediate(go);
            }
            made.Clear();
        }

        private GameObject MakeGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.hideFlags = HideFlags.DontSave;
            ground.transform.position = new Vector3(0f, Stage - 0.5f, 0f);
            ground.transform.localScale = new Vector3(80f, 1f, 80f);
            made.Add(ground);
            return ground;
        }

        private GameObject MakeRaxy(Vector3 at)
        {
            GameObject prefab = AssetDatabase.LoadAllAssetsAtPath(RaxyPath).OfType<GameObject>().First(g => g.transform.parent == null);
            var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            body.hideFlags = HideFlags.DontSave;
            body.transform.SetPositionAndRotation(at, Quaternion.identity);
            made.Add(body);
            return body;
        }

        private Pushable MakeCart(string decoration, float scale, Vector3 at)
        {
            var cart = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{Decorations}{decoration}.prefab"));
            cart.hideFlags = HideFlags.DontSave;
            cart.transform.localScale = Vector3.one * scale;
            cart.transform.position = at;
            made.Add(cart);
            Physics.SyncTransforms();
            Pushable pushable = cart.GetComponent<Pushable>();
            pushable.Register();
            return pushable;
        }

        private static void Settle(CartPusher pusher)
        {
            pusher.Follow(10f);
            Physics.SyncTransforms();
            pusher.Follow(0.02f);
        }

        // Where the fist closes on a hand: the grip frame's origin, which is what the arm reaches with.
        private static Vector3 Palm(GameObject body, HumanBodyBones bone)
        {
            Animator animator = body.GetComponentInChildren<Animator>(true);
            Transform hand = animator.GetBoneTransform(bone);
            return hand.TransformPoint(HandGripFrame.Derive(animator, hand, bone == HumanBodyBones.RightHand).LocalPosition);
        }

        private static float ToNearestGrip(Pushable cart, Vector3 palm) =>
            Mathf.Min(Vector3.Distance(palm, cart.HandleLeft.position), Vector3.Distance(palm, cart.HandleRight.position));

        private static readonly object[][] EveryCart =
        {
            new object[] { "Transport/Deco_Handcart", 1.4f }, new object[] { "Transport/Deco_Handcart", 1.8f },
            new object[] { "Transport/Deco_Handcart_Hover", 1.8f }, new object[] { "Tavern/Deco_FoodCart", 1.8f },
            new object[] { "Mining/Deco_MineCart", 1.45f },
        };

        [Test]
        public void TheHandsCloseOnTheGrips_OfEveryCartAtTheScalesASettlementPlacesThem()
        {
            foreach (object[] c in EveryCart)
            {
                MakeGround();
                GameObject raxy = MakeRaxy(new Vector3(0f, Stage, 0f));
                Pushable cart = MakeCart((string)c[0], (float)c[1], new Vector3(6f, Stage, 6f));
                CartPusher pusher = CartPusher.On(raxy);
                Assert.IsTrue(pusher.Grip(cart), c[0].ToString());

                Settle(pusher);

                Assert.Less(ToNearestGrip(cart, Palm(raxy, HumanBodyBones.LeftHand)), HandsOnTheGrip, $"{c[0]} x{c[1]}: left hand");
                Assert.Less(ToNearestGrip(cart, Palm(raxy, HumanBodyBones.RightHand)), HandsOnTheGrip, $"{c[0]} x{c[1]}: right hand");
                pusher.Release();
                TearDown();
            }
        }

        [Test]
        public void ThePlayersAstronautClosesItsHandsOnTheCartsToo()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab");
            foreach (object[] c in EveryCart)
            {
                MakeGround();
                var body = (GameObject)PrefabUtility.InstantiatePrefab(player);
                body.hideFlags = HideFlags.DontSave;
                made.Add(body);
                // The player's root is about a metre above its soles.
                body.transform.position = new Vector3(0f, Stage, 0f);
                Animator animator = body.GetComponentInChildren<Animator>(true);
                float soles = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y - 0.1f;
                body.transform.position = new Vector3(0f, Stage + (Stage - soles), 0f);
                Pushable cart = MakeCart((string)c[0], (float)c[1], new Vector3(6f, Stage, 6f));
                CartPusher pusher = CartPusher.On(body);
                Assert.IsTrue(pusher.Grip(cart), c[0].ToString());

                Settle(pusher);

                Assert.Less(ToNearestGrip(cart, Palm(body, HumanBodyBones.LeftHand)), HandsOnTheGrip, $"{c[0]} x{c[1]}: left hand");
                Assert.Less(ToNearestGrip(cart, Palm(body, HumanBodyBones.RightHand)), HandsOnTheGrip, $"{c[0]} x{c[1]}: right hand");
                pusher.Release();
                TearDown();
            }
        }

        [Test]
        public void TheWheelsStandOnTheGround_WhileTheCartIsHeld()
        {
            MakeGround();
            GameObject raxy = MakeRaxy(new Vector3(0f, Stage, 0f));
            Pushable cart = MakeCart("Transport/Deco_Handcart", 1.4f, new Vector3(6f, Stage, 6f));
            CartPusher pusher = CartPusher.On(raxy);
            pusher.Grip(cart);

            Settle(pusher);

            CartShape shape = cart.Shape;
            float wheelRadius = shape.Axle.Value.y - shape.ContactCentre.y;
            Assert.AreEqual(Stage + wheelRadius, cart.WorldOf(shape.Axle.Value).y, 0.03f, "the axle is one wheel radius above the ground, however high the shafts are lifted");
        }

        [Test]
        public void TheCartFollowsTheBodyAsItWalksAndTurns()
        {
            MakeGround();
            GameObject raxy = MakeRaxy(new Vector3(0f, Stage, 0f));
            Pushable cart = MakeCart("Mining/Deco_MineCart", 1.45f, new Vector3(6f, Stage, 6f));
            CartPusher pusher = CartPusher.On(raxy);
            pusher.Grip(cart);
            Settle(pusher);
            Vector3 first = cart.HandlePosition - raxy.transform.position;

            raxy.transform.SetPositionAndRotation(new Vector3(10f, Stage, -4f), Quaternion.Euler(0f, 90f, 0f));
            Physics.SyncTransforms();
            pusher.Follow(0.02f);

            Vector3 now = cart.HandlePosition - raxy.transform.position;
            Assert.AreEqual(first.magnitude, now.magnitude, 0.05f, "the handles stay the same distance from the body");
            Assert.Less(Vector3.Angle(Quaternion.Euler(0f, 90f, 0f) * Vector3.ProjectOnPlane(first, Vector3.up), Vector3.ProjectOnPlane(now, Vector3.up)), 2f,
                        "and swing round with it");
        }

        [Test]
        public void ACartHeldByOneBodyCannotBeTakenByAnother()
        {
            MakeGround();
            GameObject first = MakeRaxy(new Vector3(0f, Stage, 0f)), second = MakeRaxy(new Vector3(3f, Stage, 0f));
            Pushable cart = MakeCart("Transport/Deco_Handcart", 1.4f, new Vector3(6f, Stage, 6f));

            Assert.IsTrue(CartPusher.On(first).Grip(cart));
            Assert.IsFalse(CartPusher.On(second).Grip(cart));
            Assert.AreSame(first.transform, cart.Holder);
        }

        [Test]
        public void TheBodyWalksThroughItsOwnCart_AndBumpsIntoItAgainOnceLetGo()
        {
            MakeGround();
            GameObject raxy = MakeRaxy(new Vector3(0f, Stage, 0f));
            Pushable cart = MakeCart("Transport/Deco_Handcart", 1.4f, new Vector3(6f, Stage, 6f));
            Collider body = raxy.GetComponentInChildren<Collider>();
            Collider part = cart.GetComponent<Collider>();
            CartPusher pusher = CartPusher.On(raxy);

            pusher.Grip(cart);
            Assert.IsTrue(Physics.GetIgnoreCollision(body, part), "holding it, the body does not collide with it");

            pusher.Release();
            Assert.IsFalse(Physics.GetIgnoreCollision(body, part), "let go, it is solid to the body again");
            Assert.IsTrue(cart.IsFree);
        }

        [Test]
        public void ABodyWithNoArmsCannotHoldACart()
        {
            var plain = new GameObject("NoArms");
            made.Add(plain);
            Pushable cart = MakeCart("Transport/Deco_Handcart", 1.4f, new Vector3(6f, Stage, 6f));

            Assert.IsFalse(CartPusher.On(plain).Grip(cart));
            Assert.IsTrue(cart.IsFree, "a failed grip leaves the cart free");
        }

        // ── a resident at work ───────────────────────────────────────────────────────────────────

        [Test]
        public void AResidentAtWork_TakesTheNearestFreeCart_AndLetsGoWhenTheWorkEnds()
        {
            MakeGround();
            GameObject raxy = MakeRaxy(new Vector3(0f, Stage, 0f));
            Pushable cart = MakeCart("Transport/Deco_Handcart", 1.4f, new Vector3(0f, Stage, 0f));
            var pushing = new ResidentPushing(raxy);

            pushing.Sync(true);
            Assert.IsTrue(pushing.IsPushing);
            Assert.AreEqual(cart.Id, pushing.CartId);
            Assert.AreSame(raxy.transform, cart.Holder);

            pushing.Sync(false);
            Assert.IsFalse(pushing.IsPushing);
            Assert.AreEqual(ResidentPresence.NoCart, pushing.CartId);
            Assert.IsTrue(cart.IsFree);
        }

        [Test]
        public void AResidentWithNoCartInReachWorksEmptyHanded()
        {
            MakeGround();
            GameObject raxy = MakeRaxy(new Vector3(0f, Stage, 0f));
            MakeCart("Transport/Deco_Handcart", 1.4f, new Vector3(50f, Stage, 50f));
            var pushing = new ResidentPushing(raxy);

            pushing.Sync(true);

            Assert.IsFalse(pushing.IsPushing);
            Assert.AreEqual(ResidentPresence.NoCart, pushing.CartId);
        }

        [Test]
        public void AResidentDoesNotTakeACartAnotherBodyHolds()
        {
            MakeGround();
            GameObject raxy = MakeRaxy(new Vector3(0f, Stage, 0f));
            Pushable cart = MakeCart("Transport/Deco_Handcart", 1.4f, new Vector3(0f, Stage, 0f));
            Transform other = new GameObject("other").transform;
            made.Add(other.gameObject);
            cart.TryClaim(other);

            var pushing = new ResidentPushing(raxy);
            pushing.Sync(true);

            Assert.IsFalse(pushing.IsPushing);
        }

        [Test]
        public void AResidentWhoseCartIsCarriedAwayLetsGoAtTheNextSync()
        {
            MakeGround();
            GameObject raxy = MakeRaxy(new Vector3(0f, Stage, 0f));
            Pushable cart = MakeCart("Transport/Deco_Handcart", 1.4f, new Vector3(0f, Stage, 0f));
            var pushing = new ResidentPushing(raxy);
            pushing.Sync(true);
            Assert.IsTrue(pushing.IsPushing);

            cart.Unregister();
            pushing.Sync(true);

            Assert.IsFalse(pushing.IsPushing, "the chunk unloaded under its hands");
        }
    }
}
