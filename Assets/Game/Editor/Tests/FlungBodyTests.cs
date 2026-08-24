using System.Reflection;
using NUnit.Framework;
using SpaceGame.Characters;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class FlungBodyTests
    {
        [Test]
        public void FlungBody_RunsAfterPlayerMovement()
        {
            var attrs = typeof(FlungBody)
                .GetCustomAttributes(typeof(DefaultExecutionOrder), false);
            Assert.AreEqual(1, attrs.Length,
                "FlungBody must declare DefaultExecutionOrder — PlayerMovement deletes velocity written before it runs.");
            Assert.GreaterOrEqual(((DefaultExecutionOrder)attrs[0]).order, 200);
        }

        [Test]
        public void OnDisable_ClearsAPendingImpulse()
        {
            // A bare GameObject, no physics and no play mode — Awake never runs, which is fine
            // here: OnDisable's job is to drop the latch regardless of what Awake wired up.
            var go = new GameObject("flung");
            FlungBody flung = go.AddComponent<FlungBody>();

            FieldInfo pending = typeof(FlungBody)
                .GetField("pending", BindingFlags.NonPublic | BindingFlags.Instance);
            pending.SetValue(flung, new Vector3(3f, 0f, 0f));

            typeof(FlungBody)
                .GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(flung, null);

            Assert.AreEqual(Vector3.zero, (Vector3)pending.GetValue(flung),
                "A latched impulse must not survive OnDisable — carried over, it would fire " +
                "stale on the next enable (e.g. after a respawn).");

            Object.DestroyImmediate(go);
        }
    }
}
