// Assets/Game/Scripts/Vehicles/Monowheel/MonowheelWheel.cs
using System;
using UnityEngine;

namespace SpaceGame.Vehicles.Monowheel
{
    /// <summary>
    /// One wheel of a monowheel, as MEASURED by MonowheelPresentationBuilder from the ring's
    /// geometry — never assumed, because FBX import re-orients bones and the doubles are cambered.
    /// Points are in the vehicle root's local space (the ring bone spins, so it cannot hold them).
    /// </summary>
    [Serializable]
    public sealed class MonowheelWheel
    {
        [Tooltip("The Bone_Ring* transform that spins. Its paddles and mounts ride under it.")]
        public Transform ringBone;

        [Tooltip("Axle in the ring bone's local space, signed so a positive turn rolls the vehicle forward.")]
        public Vector3 localAxle = Vector3.right;

        [Tooltip("Metres from hub to paddle tip: the radius the wheel rolls on.")]
        public float paddleRadius = 1.965f;

        [Tooltip("Where the paddles meet the ground, in root space. Spray and dust are born here.")]
        public Vector3 localContact;

        [Tooltip("The hub, in root space. Smoke rises from here.")]
        public Vector3 localHub;

        public ParticleSystem spray;
        public ParticleSystem dust;
        public ParticleSystem smoke;
    }
}
