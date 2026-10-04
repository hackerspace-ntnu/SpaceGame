// A cart has one holder, lets go of a holder it is carried away from, is found by an id every machine derives alike, is left
// standing where its holder let go, and is remembered there: through the ledger, through a save, and when its chunk loads again.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using SpaceGame.Persistence;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class PushableTests
    {
        private readonly List<GameObject> made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in made)
            {
                if (go == null) continue;
                go.GetComponent<Pushable>()?.Unregister();
                go.GetComponent<PushableLedger>()?.Unbind();
                Object.DestroyImmediate(go);
            }
            made.Clear();
        }

        // Two wheels and a bar, travel toward -Z: a handcart without the model.
        private Pushable MakeCart(string name, Vector3 at, bool authored = true)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            // A cart's id includes its sibling index, which a chunk scene keeps fixed and a test scene full of other tests' leftovers does not.
            go.transform.SetSiblingIndex(0);
            made.Add(go);
            Pushable cart = go.AddComponent<Pushable>();
            if (authored)
            {
                Transform gripA = Child(go, "Grip_A", new Vector3(0.58f, 0.52f, 1.9f));
                Transform gripB = Child(go, "Grip_B", new Vector3(-0.58f, 0.52f, 1.9f));
                Transform wheelA = Child(go, "Wheel_A", new Vector3(0.8f, -0.05f, -0.05f));
                Transform wheelB = Child(go, "Wheel_B", new Vector3(-0.8f, -0.05f, -0.05f));
                Transform axle = Child(go, "Axle", new Vector3(0f, 0.55f, -0.05f));
                var so = new SerializedObject(cart);
                so.FindProperty("handleLeft").objectReferenceValue = gripA;
                so.FindProperty("handleRight").objectReferenceValue = gripB;
                so.FindProperty("axle").objectReferenceValue = axle;
                SerializedProperty wheels = so.FindProperty("wheelContacts");
                wheels.arraySize = 2;
                wheels.GetArrayElementAtIndex(0).objectReferenceValue = wheelA;
                wheels.GetArrayElementAtIndex(1).objectReferenceValue = wheelB;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            cart.Register();
            return cart;
        }

        private static Transform Child(GameObject parent, string name, Vector3 local)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent.transform, false);
            child.localPosition = local;
            return child;
        }

        private Transform MakeBody(string name)
        {
            var go = new GameObject(name);
            made.Add(go);
            return go.transform;
        }

        private PushableLedger MakeLedger()
        {
            var go = new GameObject("Ledger");
            made.Add(go);
            PushableLedger ledger = go.AddComponent<PushableLedger>();
            ledger.Bind();
            return ledger;
        }

        private static void Carry(Pushable cart, Vector3 to) => cart.PlaceAt(new CartPose(to, cart.transform.rotation));

        [Test]
        public void ACartHasOneHolder()
        {
            Pushable cart = MakeCart("OneHolder", Vector3.zero);
            Transform first = MakeBody("first"), second = MakeBody("second");

            Assert.IsTrue(cart.TryClaim(first));
            Assert.IsFalse(cart.TryClaim(second), "taken");
            Assert.IsTrue(cart.TryClaim(first), "the holder's own claim is idempotent");
            Assert.AreSame(first, cart.Holder);
            Assert.IsFalse(cart.Release(second), "a stranger cannot take the handles off the holder");
            Assert.AreSame(first, cart.Holder);
        }

        [Test]
        public void LettingGoFreesTheCart_AndSaysSo()
        {
            Pushable cart = MakeCart("Release", Vector3.zero);
            Transform first = MakeBody("first");
            int released = 0;
            cart.Released += (_, _) => released++;
            cart.TryClaim(first);

            Assert.IsTrue(cart.Release(first));
            Assert.AreEqual(1, released);
            Assert.IsTrue(cart.IsFree);
            Assert.IsFalse(cart.Release(first), "letting go twice does nothing");
            Assert.AreEqual(1, released);
            Assert.IsTrue(cart.TryClaim(MakeBody("next")));
        }

        [Test]
        public void ADestroyedHolderFreesItsCart()
        {
            Pushable cart = MakeCart("Destroyed", Vector3.zero);
            Transform holder = MakeBody("holder");
            cart.TryClaim(holder);

            Object.DestroyImmediate(holder.gameObject);

            Assert.IsTrue(cart.IsFree);
            Assert.IsTrue(cart.TryClaim(MakeBody("next")));
        }

        [Test]
        public void ACartThatIsNotAuthoredCannotBePushed()
        {
            Pushable bare = MakeCart("Bare", Vector3.zero, authored: false);

            Assert.IsFalse(bare.IsAuthored);
            Assert.IsFalse(bare.CanInteract());
            Assert.IsNull(Pushable.NearestFree(Vector3.zero, 100f));
        }

        [Test]
        public void TheNearestFreeCartWithinReachIsTheOneTaken()
        {
            Pushable near = MakeCart("Near", new Vector3(0f, 0f, -2f));
            Pushable far = MakeCart("Far", new Vector3(30f, 0f, 0f));
            Vector3 body = Vector3.zero;

            Assert.AreSame(near, Pushable.NearestFree(body, 6f), "the handles are 1.9 m in front of the root, so near is the closer");
            near.TryClaim(MakeBody("holder"));
            Assert.IsNull(Pushable.NearestFree(body, 6f), "the only cart in reach is taken");
            Assert.AreSame(far, Pushable.NearestFree(body, 50f));
        }

        [Test]
        public void EveryMachineDerivesTheSameIdFromTheSameHierarchy()
        {
            Pushable a = MakeCart("Twin", Vector3.zero);
            int id = a.Id;
            a.Unregister();
            Object.DestroyImmediate(a.gameObject);

            Pushable b = MakeCart("Twin", new Vector3(9f, 0f, 9f));

            Assert.AreEqual(id, b.Id, "the id is the hierarchy's, not the position's");
            Assert.AreNotEqual(0, id);
            Assert.AreSame(b, Pushable.Find(id));
        }

        [Test]
        public void ACartLeftAwayFromHomeIsRememberedWhereItWasLeft()
        {
            PushableLedger ledger = MakeLedger();
            Pushable cart = MakeCart("Left", Vector3.zero);
            Transform holder = MakeBody("holder");
            cart.TryClaim(holder);
            Carry(cart, new Vector3(12f, 0f, 5f));

            cart.Release(holder);

            Assert.AreEqual(1, ledger.Count);
            Assert.Less(Vector3.Distance(cart.HomePose.Position, Vector3.zero), 0.001f, "home is where it was authored");
        }

        [Test]
        public void ACartLeftAtHomeIsNotRemembered()
        {
            PushableLedger ledger = MakeLedger();
            Pushable cart = MakeCart("Home", Vector3.zero);
            Transform holder = MakeBody("holder");
            cart.TryClaim(holder);
            Carry(cart, new Vector3(12f, 0f, 5f));
            cart.Release(holder);
            Assert.AreEqual(1, ledger.Count);

            cart.TryClaim(holder);
            Carry(cart, Vector3.zero);
            cart.Release(holder);

            Assert.AreEqual(0, ledger.Count, "back where it started: nothing to remember");
        }

        [Test]
        public void ARestPoseSurvivesTheSaveFile_AndAReloadedCartTakesIt()
        {
            PushableLedger ledger = MakeLedger();
            Pushable cart = MakeCart("Saved", Vector3.zero);
            Transform holder = MakeBody("holder");
            cart.TryClaim(holder);
            Carry(cart, new Vector3(12f, 0f, 5f));
            cart.Release(holder);
            var stood = new Vector3(12f, 0f, 5f);

            JObject saved = JObject.FromObject(ledger.CaptureState(), SaveSerializer.Serializer);
            string text = saved.ToString();
            ledger.Unbind();
            cart.Unregister();
            Object.DestroyImmediate(cart.gameObject);

            // A new session: a fresh ledger reads the file, and the chunk loads the cart at its authored pose.
            PushableLedger fresh = MakeLedger();
            fresh.RestoreState(JObject.Parse(text));
            Pushable reloaded = MakeCart("Saved", Vector3.zero);
            PushableLedger.Shown(reloaded);

            Assert.AreEqual(1, fresh.Count);
            Assert.Less(Vector3.Distance(reloaded.transform.position, stood), 0.5f, "it stands where it was left");
            Assert.Greater(Vector3.Distance(reloaded.transform.position, reloaded.HomePose.Position), 5f, "not at its authored pose");
        }

        [Test]
        public void ALedgerWithNothingInItSavesNothing()
        {
            PushableLedger ledger = MakeLedger();

            Assert.IsNull(ledger.CaptureState());
        }

        [Test]
        public void ARestoreFromAnEmptyRecordPutsEveryCartBackHome()
        {
            PushableLedger ledger = MakeLedger();
            Pushable cart = MakeCart("Emptied", Vector3.zero);
            Transform holder = MakeBody("holder");
            cart.TryClaim(holder);
            Carry(cart, new Vector3(12f, 0f, 5f));
            cart.Release(holder);
            Assert.AreEqual(1, ledger.Count);

            ledger.RestoreState(null);

            Assert.AreEqual(0, ledger.Count, "null is a value: the world was saved with every cart at home");
            Assert.Less(Vector3.Distance(cart.transform.position, cart.HomePose.Position), 0.001f);
        }

        [Test]
        public void AHeldCartIsNotMovedByTheLedger()
        {
            PushableLedger ledger = MakeLedger();
            Pushable cart = MakeCart("Held", Vector3.zero);
            Transform holder = MakeBody("holder");
            cart.TryClaim(holder);
            Carry(cart, new Vector3(3f, 0f, 3f));

            ledger.RestoreState(null);
            PushableLedger.Shown(cart);

            Assert.Less(Vector3.Distance(cart.transform.position, new Vector3(3f, 0f, 3f)), 0.001f, "the pusher poses a held cart");
        }

        [Test]
        public void EveryCartDecorationIsAuthoredForPushing()
        {
            string[] paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs/Environment/Decorations" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => System.IO.Path.GetFileNameWithoutExtension(p) is "Deco_Handcart" or "Deco_Handcart_Hover" or "Deco_FoodCart" or "Deco_MineCart")
                .ToArray();
            Assert.AreEqual(4, paths.Length, "the four cart decorations");

            foreach (string path in paths)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Pushable cart = prefab.GetComponent<Pushable>();
                Assert.IsNotNull(cart, path);
                Assert.IsTrue(cart.IsAuthored, $"{path}: two grips and its wheels");
                Assert.IsNotNull(prefab.transform.Find("Handlebar"), $"{path}: the hands need a bar to close on, the shafts are wider than a Raxy's arms reach");
                Assert.IsNotNull(prefab.GetComponent<Rigidbody>(), $"{path}: a moved collider needs a body");
                Assert.IsTrue(prefab.GetComponent<Rigidbody>().isKinematic, path);
                Assert.IsTrue(prefab.GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetStaticEditorFlags(t.gameObject) == 0),
                              $"{path}: a static renderer would stay where it was baked while the cart moves");
            }
        }

        [Test]
        public void ThePlayerTheSessionAndTheDroverAreWiredForCarts()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab");
            Assert.IsNotNull(player.GetComponent<SpaceGame.Characters.PlayerPushing>(), "a NetworkBehaviour must be on the networked player prefab, never added at runtime");

            var session = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Systems/NetworkGameManager.prefab");
            Assert.IsNotNull(session.GetComponent<PushableLedger>(), "the ledger lives with the session's one NetworkObject");

            var drover = AssetDatabase.LoadAssetAtPath<SpaceGame.Agents.Residents.ResidentArchetype>("Assets/Game/ScriptableObjects/Residents/Archetypes/Drover.asset");
            Assert.IsTrue(drover.pushesCart);
            Assert.IsFalse(drover.heldItem != null && drover.heldItem.name.StartsWith("Carry_Cart"),
                           "the Drover pushes a real cart, it does not hold a cart item (its lasso is its tool, stowed while it pushes)");
        }

        [Test]
        public void NoNomadBuildingHoldsAStaticCart()
        {
            var stuck = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs/Environment/Structures/NomadSettlement" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (Pushable cart in prefab.GetComponentsInChildren<Pushable>(true))
                    foreach (Transform part in cart.GetComponentsInChildren<Transform>(true))
                        if (GameObjectUtility.GetStaticEditorFlags(part.gameObject) != 0)
                            stuck.Add($"{path}: {cart.name}/{part.name}");
            }

            Assert.IsEmpty(stuck, "a cart that is Batching Static keeps drawing where it was baked while its collider moves:\n" + string.Join("\n", stuck));
        }
    }
}
