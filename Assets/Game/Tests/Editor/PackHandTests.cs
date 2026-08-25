using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The arithmetic the click interaction rests on: the byte packing that carries a hotbar slot
    /// and a pack surface across the wire together, and the quarter-turn cycle a refused click
    /// walks.
    ///
    /// <para>
    /// The state machine itself is a MonoBehaviour on a runtime-spawned focus camera and needs a
    /// live NetworkManager to say anything about, so what is tested here is the arithmetic it uses
    /// — which is where a silent wrong answer would come from.
    /// </para>
    /// </summary>
    public class PackHandTests
    {
        /// <summary>
        /// Every hotbar slot survives the trip beside every surface. Calls the REAL encode/decode
        /// pair rather than mirroring the formula — a test that reimplements the arithmetic it is
        /// checking passes happily while the shipped code says something else.
        /// </summary>
        [Test]
        public void TheStowWireCarriesBothTheSlotAndTheSurface()
        {
            foreach (int slot in new[] { 0, 1, 3, 9, 255 })
            {
                foreach (PackSurfaceId surface in System.Enum.GetValues(typeof(PackSurfaceId)))
                {
                    int a = BackpackController.EncodeStowTarget(slot, surface);

                    Assert.IsTrue(BackpackController.TryDecodeStowTarget(a, out int back,
                                                                        out PackSurfaceId face),
                                  $"slot {slot} on {surface} must decode at all");

                    Assert.AreEqual(slot, back, $"slot {slot} on {surface}");
                    Assert.AreEqual(surface, face, $"surface {surface} in slot {slot}");
                }
            }
        }

        /// <summary>
        /// The decode is the trust boundary: <c>A</c> arrives from another machine. It must refuse
        /// anything that cannot have come from the encoder rather than resolving to a real slot on
        /// a surface nobody named.
        /// </summary>
        [Test]
        public void AMalformedStowTargetIsRefusedRatherThanGuessedAt()
        {
            Assert.IsFalse(BackpackController.TryDecodeStowTarget(-1, out _, out _),
                           "a negative A is not something the encoder can produce");

            Assert.IsFalse(BackpackController.TryDecodeStowTarget(0 | (0xFE << 8), out _, out _),
                           "an undefined surface must not decode");
        }

        /// <summary>
        /// The refused click's answer. Four of them return the item to the turn it started at, so
        /// a player who over-clicks is never stuck with an orientation they cannot get back to.
        /// </summary>
        [Test]
        public void FourRefusedClicksReturnTheItemToItsStartingTurn()
        {
            float yaw = 0f;

            foreach (float expected in new[] { 90f, 180f, 270f, 0f })
            {
                yaw = PackGrid.SnapYaw(Mathf.Repeat(yaw + 90f, 360f));
                Assert.AreEqual(expected, yaw);
            }
        }
    }
}
