// What a settlement prop promises: rests are numbered in hierarchy order, a promised prop or rest is nobody
// else's, a picked-up prop is gone from the ground and a set-down one stands at its rest, and the building's
// saver writes nothing while every prop is home and brings a prop that was in somebody's hands home on load.
// Offline, no NetworkManager: the deciding machine is this one.
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.Agents.Residents.Tests
{
    public class SettlementPropTests
    {
        private GameObject root;
        private SettlementPropSync sync;
        private SettlementProp basket;
        private PropRest home, shelf;
        private SettlementProps props;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Settlement");
            root.AddComponent<Settlement>();
            Transform generated = Child("Generated", root.transform, Vector3.zero);
            Transform building = Child("Building", generated, Vector3.zero);
            SettlementSpot pile = Child("Pile", building, new Vector3(0f, 0f, 2f)).gameObject.AddComponent<SettlementSpot>();
            SettlementSpot store = Child("Store", building, new Vector3(5f, 0f, 2f)).gameObject.AddComponent<SettlementSpot>();
            home = Rest("Home", building, Vector3.zero, pile);
            shelf = Rest("Shelf", building, new Vector3(5f, 1f, 0f), store);

            basket = Child("Basket", building, Vector3.zero).gameObject.AddComponent<SettlementProp>();
            Set(basket, "home", home);
            sync = building.gameObject.AddComponent<SettlementPropSync>();
            props = root.GetComponent<Settlement>().Props;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        [Test]
        public void RestsAreNumberedInHierarchyOrder_AndAPropStartsAtHome()
        {
            Assert.AreEqual(2, props.RestCount);
            Assert.AreSame(home, props.Rest(0));
            Assert.AreSame(shelf, props.Rest(1));
            Assert.AreEqual(0, props.StateOf(basket));
            Assert.AreSame(basket, props.FreePropAt(0));
            Assert.IsFalse(props.IsFree(0), "a rest with a prop on it is taken");
            Assert.IsTrue(props.IsFree(1));
        }

        [Test]
        public void APromisedPropAndRest_AreNobodyElses()
        {
            props.Reserve(basket, 1);
            Assert.IsNull(props.FreePropAt(0));
            Assert.IsFalse(props.IsFree(1));

            props.Release(basket, 1);
            Assert.AreSame(basket, props.FreePropAt(0));
            Assert.IsTrue(props.IsFree(1));
        }

        [Test]
        public void APickedUpProp_LeavesTheGround_AndASetDownOneStandsAtItsRest()
        {
            Assert.IsTrue(props.Pick(basket));
            Assert.AreEqual(SettlementPropSync.Carried, props.StateOf(basket));
            Assert.IsFalse(basket.gameObject.activeSelf);
            Assert.IsFalse(props.Pick(basket), "a prop already in hand cannot be picked up twice");

            props.Put(basket, 1);
            Assert.AreEqual(1, props.StateOf(basket));
            Assert.IsTrue(basket.gameObject.activeSelf);
            Assert.That(Vector3.Distance(basket.transform.position, shelf.Position), Is.LessThan(1e-4f));
            Assert.IsTrue(props.IsFree(0));
        }

        [Test]
        public void TheSaver_WritesNothingAtHome_AndBringsACarriedPropHome()
        {
            Assert.IsNull(sync.CaptureState(), "every prop at home is the default");

            props.Pick(basket);
            props.Put(basket, 1);
            object moved = sync.CaptureState();
            Assert.IsNotNull(moved);

            props.Put(basket, 0);
            sync.RestoreState(JObject.FromObject(moved, SaveSerializer.Serializer));
            Assert.AreEqual(1, props.StateOf(basket), "a moved prop restores to the rest it was saved at");

            sync.RestoreState(JObject.FromObject(new SettlementPropSync.State { rests = new[] { SettlementPropSync.Carried } }, SaveSerializer.Serializer));
            Assert.AreEqual(0, props.StateOf(basket), "a prop saved in somebody's hands comes back home");

            sync.RestoreState(null);
            Assert.AreEqual(0, props.StateOf(basket));
        }

        private static Transform Child(string name, Transform parent, Vector3 position)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            return t;
        }

        private static PropRest Rest(string name, Transform parent, Vector3 position, SettlementSpot spot)
        {
            PropRest rest = Child(name, parent, position).gameObject.AddComponent<PropRest>();
            Set(rest, "spot", spot);
            return rest;
        }

        private static void Set(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(property).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
