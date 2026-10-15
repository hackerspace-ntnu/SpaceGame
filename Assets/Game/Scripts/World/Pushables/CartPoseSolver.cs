// Where a pushed cart stands: its handles in the pusher's hands, its wheels on the ground, its nose the way the pusher faces.
//
// Pure arithmetic with the ground handed in as a function, so the whole of it is testable without a physics scene. The cart
// is posed from the body, never the other way round: the pusher is the one thing with a will, and a cart that pulled on
// the body it follows would need a second writer for the same transform.
//
// Two kinds of cart. A cart on two wheels with shafts (a handcart) rests on its axle and is pitched about it until the
// handles are where the hands are: the pusher lifts the shafts, the way a person does. A cart that stands on three or more
// contacts (a mine cart, a stall on four wheels, a hover cart) cannot tilt to meet the hands, so it rides the ground and the
// pusher's arms reach the handles it has.
using System;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>A cart's own geometry in its local space: what <see cref="CartPoseSolver"/> needs and nothing else.</summary>
    public readonly struct CartShape
    {
        public readonly Vector3 HandleMid;
        public readonly Vector3[] Contacts;
        public readonly Vector3? Axle;
        public readonly float RideHeight;

        /// <param name="handleMid">The point between the two grips.</param>
        /// <param name="contacts">Where the cart touches level ground at rest: the bottom of each wheel.</param>
        /// <param name="axle">Set for a two-wheeled cart: the point it pitches about. Null for one that stands on its contacts.</param>
        /// <param name="rideHeight">Metres the cart floats above the ground (a hover cart); 0 for wheels.</param>
        public CartShape(Vector3 handleMid, Vector3[] contacts, Vector3? axle, float rideHeight)
        {
            HandleMid = handleMid;
            Contacts = contacts;
            Axle = axle;
            RideHeight = rideHeight;
        }

        /// <summary>The middle of the cart's contacts, in its own space.</summary>
        public Vector3 ContactCentre
        {
            get
            {
                Vector3 centre = Vector3.zero;
                foreach (Vector3 contact in Contacts) centre += contact;
                return centre / Mathf.Max(1, Contacts.Length);
            }
        }

        /// <summary>How high the handles stand above the ground when the cart rests level on it.</summary>
        public float HandleRise => HandleMid.y - ContactCentre.y + RideHeight;

        /// <summary>The way the cart travels, level, in its own space: from the handles toward the middle of its contacts.</summary>
        public Vector3 Forward
        {
            get
            {
                Vector3 level = Vector3.ProjectOnPlane(ContactCentre - HandleMid, Vector3.up);
                return level.sqrMagnitude > 1e-6f ? level.normalized : Vector3.forward;
            }
        }
    }

    public readonly struct CartPose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public CartPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    public static class CartPoseSolver
    {
        // Each pass samples the ground where the last pass left the wheels; three settle every slope this game has.
        private const int Passes = 4;

        // Contacts closer together than this along an axis are one group: the cart has no extent on that axis to tilt over.
        private const float GroupTolerance = 1e-4f;

        /// <summary>The pose that puts the cart's handles at <paramref name="hands"/>, facing <paramref name="bodyYaw"/> degrees, wheels on the ground.</summary>
        /// <param name="groundY">Ground height at a world x, z.</param>
        public static CartPose Solve(in CartShape shape, Vector3 hands, float bodyYaw, Func<float, float, float> groundY)
        {
            Vector3 forward = shape.Forward;
            float cartYaw = bodyYaw - Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

            return shape.Axle.HasValue
                ? SolveTwoWheeled(shape, shape.Axle.Value, hands, cartYaw, groundY)
                : SolveOnContacts(shape, shape.HandleMid, hands, cartYaw, groundY);
        }

        /// <summary>
        /// The cart standing unheld: its wheels where <paramref name="wheelsAt"/> puts the middle of its contacts, heading
        /// <paramref name="cartYaw"/> degrees (the cart's own, not the pusher's), shafts down, tilted to the ground under it.
        /// </summary>
        public static CartPose Rest(in CartShape shape, Vector3 wheelsAt, float cartYaw, Func<float, float, float> groundY) =>
            SolveOnContacts(shape, shape.ContactCentre, wheelsAt, cartYaw, groundY);

        // The axle stays one wheel radius above the ground; the pitch about it is whatever brings the handles to the hands' height.
        private static CartPose SolveTwoWheeled(in CartShape shape, Vector3 axle, Vector3 hands, float cartYaw, Func<float, float, float> groundY)
        {
            Vector3 lever = shape.HandleMid - axle;
            float wheelRadius = axle.y - MeanHeight(shape.Contacts);
            float length = Mathf.Max(Mathf.Sqrt(lever.y * lever.y + lever.z * lever.z), 1e-4f);
            float lean = Mathf.Atan2(lever.z, lever.y);

            Quaternion rotation = Quaternion.Euler(0f, cartYaw, 0f);
            Vector3 flat = rotation * new Vector3(lever.x, 0f, lever.z);
            var axleAt = new Vector2(hands.x - flat.x, hands.z - flat.z);
            float axleHeight = 0f;
            Vector3 position = Vector3.zero;

            for (int pass = 0; pass < Passes; pass++)
            {
                axleHeight = groundY(axleAt.x, axleAt.y) + wheelRadius + shape.RideHeight;

                float angle = Mathf.Acos(Mathf.Clamp((hands.y - axleHeight) / length, -1f, 1f));
                float up = angle - lean, down = -angle - lean;
                float pitch = Mathf.Abs(up) <= Mathf.Abs(down) ? up : down;

                position = new Vector3(axleAt.x, axleHeight, axleAt.y) - rotation * axle;
                float roll = Mathf.Rad2Deg * SlopeAngle(shape.Contacts, Sample(shape.Contacts, position, rotation, groundY), Axis.X);
                rotation = Quaternion.Euler(pitch * Mathf.Rad2Deg, cartYaw, roll);

                Vector3 handles = rotation * lever;
                axleAt = new Vector2(hands.x - handles.x, hands.z - handles.z);
            }

            return new CartPose(new Vector3(axleAt.x, axleHeight, axleAt.y) - rotation * axle, rotation);
        }

        // One point of the cart (the handles, or its wheels' middle) is held over a ground position; the cart sits on its
        // contacts, tilted to the ground under them.
        private static CartPose SolveOnContacts(in CartShape shape, Vector3 anchor, Vector3 holdAt, float cartYaw, Func<float, float, float> groundY)
        {
            Quaternion rotation = Quaternion.Euler(0f, cartYaw, 0f);
            Vector3 position = Vector3.zero;

            for (int pass = 0; pass < Passes; pass++)
            {
                Vector3 held = rotation * anchor;
                position = new Vector3(holdAt.x - held.x, position.y, holdAt.z - held.z);

                float[] ground = Sample(shape.Contacts, position, rotation, groundY);
                float pitch = -SlopeAngle(shape.Contacts, ground, Axis.Z) * Mathf.Rad2Deg;
                float roll = SlopeAngle(shape.Contacts, ground, Axis.X) * Mathf.Rad2Deg;
                rotation = Quaternion.Euler(pitch, cartYaw, roll);

                position.y = shape.RideHeight;
                for (int i = 0; i < shape.Contacts.Length; i++)
                    position.y += (ground[i] - (rotation * shape.Contacts[i]).y) / shape.Contacts.Length;
            }

            Vector3 anchored = rotation * anchor;
            return new CartPose(new Vector3(holdAt.x - anchored.x, position.y, holdAt.z - anchored.z), rotation);
        }

        private enum Axis { X, Z }

        private static float[] Sample(Vector3[] contacts, Vector3 position, Quaternion rotation, Func<float, float, float> groundY)
        {
            var ground = new float[contacts.Length];
            for (int i = 0; i < contacts.Length; i++)
            {
                Vector3 at = position + rotation * contacts[i];
                ground[i] = groundY(at.x, at.z);
            }
            return ground;
        }

        // How steeply the ground climbs across the contacts along one local axis: the far half against the near half, as an angle.
        // Zero when every contact is on one line across that axis, which has nothing to tilt over.
        private static float SlopeAngle(Vector3[] contacts, float[] ground, Axis axis)
        {
            float middle = 0f;
            foreach (Vector3 contact in contacts) middle += Along(contact, axis) / contacts.Length;

            float farAlong = 0f, nearAlong = 0f, farGround = 0f, nearGround = 0f;
            int far = 0, near = 0;
            for (int i = 0; i < contacts.Length; i++)
            {
                float along = Along(contacts[i], axis);
                if (along > middle + GroupTolerance) { farAlong += along; farGround += ground[i]; far++; }
                else if (along < middle - GroupTolerance) { nearAlong += along; nearGround += ground[i]; near++; }
            }

            if (far == 0 || near == 0) return 0f;
            return Mathf.Atan2(farGround / far - nearGround / near, farAlong / far - nearAlong / near);
        }

        private static float Along(Vector3 contact, Axis axis) => axis == Axis.X ? contact.x : contact.z;

        private static float MeanHeight(Vector3[] contacts)
        {
            float sum = 0f;
            foreach (Vector3 contact in contacts) sum += contact.y;
            return contacts.Length == 0 ? 0f : sum / contacts.Length;
        }
    }
}
