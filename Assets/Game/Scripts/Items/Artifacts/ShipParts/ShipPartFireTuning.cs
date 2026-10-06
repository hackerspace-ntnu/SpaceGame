using System;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>The numbers a fire is tuned by. Serialized on <see cref="ShipPartFire"/>.</summary>
    [Serializable]
    public sealed class ShipPartFireTuning
    {
        [Tooltip("Seconds after the oxygen plant is back in its mount and running before a " +
                 "burnt-out unit still seated in its cradle catches fire. A breather after the haul: " +
                 "the crew get their air back and see the unit sparking before it goes up " +
                 "(GDC-L1-LEVEL-0003: a rest beat after a peak, then the next rise).")]
        [Min(0f)] public float igniteDelay = 75f;

        [Tooltip("How strong a fire is the moment it catches, 0..1. Small enough to read as " +
                 "'something is starting' rather than an emergency already lost.")]
        [Range(0.01f, 1f)] public float startStrength = 0.15f;

        [Tooltip("Seconds a fire left alone takes to grow from nothing to full strength. Spraying " +
                 "must beat this rate, so it is also how forgiving a late or wavering spray is.")]
        [Min(1f)] public float growSeconds = 45f;
    }
}
