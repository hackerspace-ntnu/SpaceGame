// A pushed cart has its handles in the pusher's hands, its wheels on the ground and its nose the way the pusher faces,
// whatever the ground does and whichever way the cart is built; and a cart let go stands where its wheels are.
using System;
using NUnit.Framework;
using SpaceGame.World;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class CartPoseSolverTests
    {
        private const float Close = 0.01f;

        // A handcart: two wheels (radius 0.6) on an axle, shafts ending in a bar at z 1.9, travel toward -Z.
        private static readonly CartShape Handcart = new CartShape(
            new Vector3(0f, 0.52f, 1.9f),
            new[] { new Vector3(0.8f, -0.05f, -0.05f), new Vector3(-0.8f, -0.05f, -0.05f) },
            new Vector3(0f, 0.55f, -0.05f),
            0f);

        // A mine cart: four wheels, handles on its +X end, travel toward -X.
        private static readonly CartShape MineCart = new CartShape(
            new Vector3(1.2f, 1.1f, 0f),
            new[]
            {
                new Vector3(0.55f, -0.045f, 0.5f), new Vector3(0.55f, -0.045f, -0.5f),
                new Vector3(-0.55f, -0.045f, 0.5f), new Vector3(-0.55f, -0.045f, -0.5f),
            },
            null,
            0f);

        private static readonly Func<float, float, float> Flat = (x, z) => 0f;
        private static readonly Func<float, float, float> Rising = (x, z) => 0.3f * x;

        private static Vector3 Handles(CartShape shape, CartPose pose) => pose.Position + pose.Rotation * shape.HandleMid;

        private static float HeightAbove(Func<float, float, float> ground, CartShape shape, CartPose pose, Vector3 contact)
        {
            Vector3 at = pose.Position + pose.Rotation * contact;
            return at.y - ground(at.x, at.z);
        }

        [Test]
        public void TheCartFacesTheWayItsPusherFaces_WhateverWayItIsBuilt()
        {
            foreach (CartShape shape in new[] { Handcart, MineCart })
            foreach (float yaw in new[] { 0f, 30f, 90f, 200f, -75f })
            {
                CartPose pose = CartPoseSolver.Solve(shape, new Vector3(4f, 1.3f, 2f), yaw, Flat);
                Vector3 travel = pose.Rotation * shape.Forward;
                Vector3 pusher = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Assert.Less(Vector3.Angle(Vector3.ProjectOnPlane(travel, Vector3.up), pusher), 0.5f, $"yaw {yaw}");
            }
        }

        [Test]
        public void ATwoWheeledCart_HasItsHandlesInTheHands()
        {
            foreach (float height in new[] { 0.9f, 1.3f, 1.7f })
            {
                var hands = new Vector3(3f, height, -2f);
                CartPose pose = CartPoseSolver.Solve(Handcart, hands, 40f, Flat);
                Assert.Less(Vector3.Distance(Handles(Handcart, pose), hands), Close, $"hands at {height}");
            }
        }

        [Test]
        public void ATwoWheeledCart_RidesOnItsAxle_AndTheShaftsComeUpToTheHands()
        {
            CartPose low = CartPoseSolver.Solve(Handcart, new Vector3(0f, 0.6f, 0f), 0f, Flat);
            CartPose high = CartPoseSolver.Solve(Handcart, new Vector3(0f, 1.6f, 0f), 0f, Flat);

            Vector3 axleLow = low.Position + low.Rotation * new Vector3(0f, 0.55f, -0.05f);
            Vector3 axleHigh = high.Position + high.Rotation * new Vector3(0f, 0.55f, -0.05f);
            Assert.AreEqual(0.6f, axleLow.y, Close, "one wheel radius above the ground");
            Assert.AreEqual(0.6f, axleHigh.y, Close, "lifting the shafts does not lift the wheels");
            Assert.Greater(Handles(Handcart, high).y, Handles(Handcart, low).y);
        }

        [Test]
        public void HandsHigherThanTheShaftsCanReach_PitchAsFarAsTheyCanAndNoFurther()
        {
            CartPose pose = CartPoseSolver.Solve(Handcart, new Vector3(0f, 9f, 0f), 0f, Flat);

            Assert.IsFalse(float.IsNaN(pose.Position.x + pose.Position.y + pose.Position.z));
            Assert.Less(Handles(Handcart, pose).y, 3f, "the cart does not follow the hands into the sky");
        }

        [Test]
        public void ACartOnFourWheels_SitsOnTheGround_WithItsHandlesOverTheHands()
        {
            var hands = new Vector3(5f, 1.4f, 7f);
            CartPose pose = CartPoseSolver.Solve(MineCart, hands, 120f, Flat);

            foreach (Vector3 contact in MineCart.Contacts)
                Assert.AreEqual(0f, HeightAbove(Flat, MineCart, pose, contact), Close);
            Vector3 handles = Handles(MineCart, pose);
            Assert.AreEqual(hands.x, handles.x, Close);
            Assert.AreEqual(hands.z, handles.z, Close);
        }

        [Test]
        public void EveryWheelTouchesTheGround_OnASlope()
        {
            foreach (CartShape shape in new[] { Handcart, MineCart })
            foreach (float yaw in new[] { 0f, 90f, 215f })
            {
                CartPose pose = CartPoseSolver.Solve(shape, new Vector3(2f, 1.3f, 1f), yaw, Rising);
                foreach (Vector3 contact in shape.Contacts)
                    Assert.AreEqual(0f, HeightAbove(Rising, shape, pose, contact), 0.04f, $"yaw {yaw}");
            }
        }

        [Test]
        public void AHoverCartFloatsItsRideHeightAboveTheGround()
        {
            var hover = new CartShape(
                new Vector3(0f, 0.87f, 1.8f),
                new[] { new Vector3(0.5f, 0.02f, 0.5f), new Vector3(-0.5f, 0.02f, 0.5f), new Vector3(0.5f, 0.02f, -0.5f), new Vector3(-0.5f, 0.02f, -0.5f) },
                null,
                0.15f);

            CartPose pose = CartPoseSolver.Solve(hover, new Vector3(0f, 1.2f, 0f), 10f, Flat);

            foreach (Vector3 contact in hover.Contacts)
                Assert.AreEqual(0.15f, HeightAbove(Flat, hover, pose, contact), Close);
        }

        [Test]
        public void ACartLetGo_StandsWhereItsWheelsAre_WithTheShaftsDown()
        {
            var hands = new Vector3(3f, 1.5f, -2f);
            CartPose held = CartPoseSolver.Solve(Handcart, hands, 40f, Flat);
            Vector3 wheels = held.Position + held.Rotation * Handcart.ContactCentre;
            float yaw = held.Rotation.eulerAngles.y;

            CartPose rest = CartPoseSolver.Rest(Handcart, wheels, yaw, Flat);

            Vector3 wheelsAfter = rest.Position + rest.Rotation * Handcart.ContactCentre;
            Assert.AreEqual(wheels.x, wheelsAfter.x, Close);
            Assert.AreEqual(wheels.z, wheelsAfter.z, Close);
            foreach (Vector3 contact in Handcart.Contacts)
                Assert.AreEqual(0f, HeightAbove(Flat, Handcart, rest, contact), Close);
            Assert.Less(Vector3.Angle(rest.Rotation * Vector3.up, Vector3.up), 0.5f, "level again: the shafts are down");
            Assert.AreEqual(yaw, rest.Rotation.eulerAngles.y, 0.5f, "its heading is kept");
        }
    }
}
