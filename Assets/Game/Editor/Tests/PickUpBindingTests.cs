// One button picks things up, and the prompt names that same button.
//
// Placing and picking up are the two halves of one verb and used to sit on two different buttons
// each: loose salvage came back on right mouse, while a lantern the player had put down came back
// on Q -- which is also the left gauntlet's trigger, so pocketing a lamp fired a worn device with
// it. These pin the rule that replaced that: the interact press operates what it can operate and
// otherwise takes the thing back, and nothing offers a pick-up it will then refuse.
using NUnit.Framework;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class PickUpBindingTests
    {
        /// <summary>A door: something to operate, nothing to take home.</summary>
        private class Fixture : IInteractable
        {
            public bool Works = true;
            public bool CanInteract() => Works;
            public void Interact(Interactor interactor) { }
        }

        /// <summary>A placed lantern: nothing to operate, and it comes back.</summary>
        private class Placed : IInteractable, IRetrievable
        {
            public bool Available = true;
            public bool CanInteract() => false;
            public void Interact(Interactor interactor) { }
            public bool CanRetrieve() => Available;
            public void Retrieve(Interactor interactor) { }
        }

        /// <summary>A saddle: both meanings of the press, and they do the same thing.</summary>
        private class Strapped : IInteractable, IRetrievable
        {
            public bool CanInteract() => true;
            public void Interact(Interactor interactor) { }
            public bool CanRetrieve() => true;
            public void Retrieve(Interactor interactor) { }
        }

        private class Winch : IInteractable, ISecondaryInteractable
        {
            public bool CanInteract() => true;
            public void Interact(Interactor interactor) { }
            public bool CanSecondaryInteract() => true;
            public void SecondaryInteract(Interactor interactor) { }
        }

        // ── which of the two things the press means ──────────────────────────

        [Test]
        public void APlacedThingWithNothingToOperateComesBack()
        {
            Assert.IsTrue(Interactor.PressPicksUp(new Placed(), canUse: false));
        }

        [Test]
        public void SomethingWithAVerbOfItsOwnIsOperated_NotPocketed()
        {
            // The saddle is both; the primary verb wins the button. It has to, or a placeable that
            // does something could never be operated at all.
            Assert.IsFalse(Interactor.PressPicksUp(new Strapped(), canUse: true));
        }

        [Test]
        public void AThingThatCannotBePickedUpRightNowOffersNothing()
        {
            // A permanent placement -- retrievable false -- must not light the crosshair, because
            // IsActionable asks this same question. A prompt that refuses the press is the failure
            // both callers exist to prevent.
            Assert.IsFalse(Interactor.PressPicksUp(new Placed { Available = false }, canUse: false));
        }

        [Test]
        public void AnOrdinaryInteractableIsNeverAPickUp()
        {
            Assert.IsFalse(Interactor.PressPicksUp(new Fixture(), canUse: true));
            Assert.IsFalse(Interactor.PressPicksUp(new Fixture { Works = false }, canUse: false));
        }

        // ── and the words say the same thing ─────────────────────────────────

        [Test]
        public void ThePromptForAPlacedThingNamesTheInteractButton()
        {
            Assert.AreEqual("RMB: pick up", InteractionPromptResolver.DerivePrompt(new Placed()));
        }

        [Test]
        public void ThePromptNamesTheVerbWhenThereIsOne()
        {
            // Not "RMB: interact   RMB: pick up": one press cannot be offered twice.
            string prompt = InteractionPromptResolver.DerivePrompt(new Strapped());

            Assert.AreEqual(InteractionPromptResolver.DefaultPrompt, prompt);
        }

        [Test]
        public void TheUseLineIsStillAppendedToASecondaryVerb()
        {
            string prompt = InteractionPromptResolver.DerivePrompt(new Winch());

            Assert.AreEqual(InteractionPromptResolver.DefaultPrompt
                            + InteractionPromptResolver.SecondarySuffix, prompt);
        }
    }
}
