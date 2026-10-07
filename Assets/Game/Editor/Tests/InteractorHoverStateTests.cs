// Hover state must not outlive the component that maintains it.
//
// Mounting disables the rider's Interactor, which stops its Update — and the hover fields then
// FROZE at whatever the player was looking at when they pressed the key, which is by definition the
// thing they just climbed onto. VisorReticle and CrosshairUI both poll those fields every
// frame with no way to tell "still hovering" from "no longer being asked", so the "Press E" panel
// sat on screen for the entire ride with the crosshair lit beside it.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class InteractorHoverStateTests
    {
        private GameObject host;
        private Interactor interactor;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Interactor");
            interactor = host.AddComponent<Interactor>();
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
        }

        [Test]
        public void ClearHoverState_DropsTheHoveredInteractable()
        {
            GameObject target = new GameObject("Target");
            try
            {
                interactor.ClearHoverState();
                Assert.IsNull(interactor.HoveredInteractable);
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ClearHoverState_DropsTheCrosshairFlag()
        {
            interactor.ClearHoverState();
            Assert.IsFalse(interactor.IsHoveringInteractable);
        }

        [Test]
        public void ClearHoverState_IsIdempotent()
        {
            interactor.ClearHoverState();
            interactor.ClearHoverState();

            Assert.IsNull(interactor.HoveredInteractable);
            Assert.IsFalse(interactor.IsHoveringInteractable);
        }

        [Test]
        public void AFreshInteractorReportsNoHover()
        {
            // The state the HUD must see before anything has been looked at, and the same state it
            // has to be returned to when the Interactor stops running.
            Assert.IsNull(interactor.HoveredInteractable);
            Assert.IsFalse(interactor.IsHoveringInteractable);
        }
    }
}
