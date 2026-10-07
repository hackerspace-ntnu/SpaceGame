// Who is offered a way aboard, and when.
//
// DeckBoarding sits on the hull ROOT so that every hull collider resolves to it — that is what makes
// the craft boardable from any angle instead of only from the one side of an eighteen-metre hull the
// gangway happens to be on. The cost of that reach is that the hull colliders also reach up under the
// deck you are standing on, so once aboard, looking at the mast or down at the planks offered to put
// you where you already were, and pressing it threw you back amidships mid-passage.
//
// Refusing it for everybody is not the fix: a second player still on the sand needs the prompt. So
// the refusal is per-interactor, through IContextualInteractable, and that split is what these pin.
//
// In Editor/ rather than beside the other EditMode tests because DeckBoarding lives in the default
// assembly, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.DuneFoil;

namespace SpaceGame.EditorTools
{
    public class DuneFoilBoardingTests
    {
        private GameObject craft;
        private GameObject player;

        [TearDown]
        public void TearDown()
        {
            if (craft != null) Object.DestroyImmediate(craft);
            if (player != null) Object.DestroyImmediate(player);
        }

        /// A hull with a deck to land on and a carry volume that says who is already aboard.
        private DeckBoarding BuildCraft()
        {
            craft = new GameObject("Craft");
            craft.transform.position = Vector3.zero;

            GameObject deckObject = new GameObject("COL_Deck");
            deckObject.transform.SetParent(craft.transform, false);
            BoxCollider deck = deckObject.AddComponent<BoxCollider>();
            deck.size = new Vector3(4f, 0.3f, 15f);
            deck.center = new Vector3(0f, 1.8f, 0f);

            GameObject carryObject = new GameObject("COL_CarryVolume");
            carryObject.transform.SetParent(craft.transform, false);
            BoxCollider carry = carryObject.AddComponent<BoxCollider>();
            carry.isTrigger = true;
            carry.size = new Vector3(4f, 2.6f, 15f);
            carry.center = new Vector3(0f, 3.2f, 0f);

            DeckBoarding boarding = craft.AddComponent<DeckBoarding>();
            boarding.Bind(deck, carry, null);
            return boarding;
        }

        /// A body with an Interactor on it, which is how the real player is put together.
        private Interactor BuildPlayer(Vector3 position)
        {
            player = new GameObject("Player");
            player.transform.position = position;
            player.AddComponent<Rigidbody>().isKinematic = true;
            player.AddComponent<CapsuleCollider>();
            return player.AddComponent<Interactor>();
        }

        [Test]
        public void SomeoneOnTheSand_IsOfferedAWayAboard()
        {
            DeckBoarding boarding = BuildCraft();
            Interactor onTheSand = BuildPlayer(new Vector3(6f, 0f, 0f));

            Assert.IsTrue(boarding.CanInteract(), "The craft is parked and has a deck.");
            Assert.IsTrue(((IContextualInteractable)boarding).CanInteract(onTheSand),
                "Somebody standing alongside must be offered a way up.");
        }

        [Test]
        public void SomeoneAlreadyOnTheDeck_IsNotOfferedAWayAboard()
        {
            DeckBoarding boarding = BuildCraft();
            Interactor aboard = BuildPlayer(new Vector3(0f, 2.4f, 2f));

            Assert.IsFalse(((IContextualInteractable)boarding).CanInteract(aboard),
                "Standing on the planks, looking at the mast, the craft offered to put the " +
                "player where they already were — and pressing it teleported them amidships.");
        }

        [Test]
        public void TheRefusalIsPerPlayer_NotForEverybody()
        {
            DeckBoarding boarding = BuildCraft();

            Interactor aboard = BuildPlayer(new Vector3(0f, 2.4f, 2f));
            Assert.IsFalse(((IContextualInteractable)boarding).CanInteract(aboard));

            // The plain, world-level question must still say yes: a second player on the sand
            // has to be able to climb up while the first one is walking the deck.
            Assert.IsTrue(boarding.CanInteract(),
                "One player being aboard must not close the craft to everyone else.");
        }

        [Test]
        public void AnInteractorless_QueryIsNotRefused()
        {
            DeckBoarding boarding = BuildCraft();
            Assert.IsTrue(((IContextualInteractable)boarding).CanInteract(null),
                "With nobody to ask about, the contextual test must not refuse.");
        }

        [Test]
        public void ACraftUpOnItsFoil_OffersNothingToAnybody()
        {
            // The deck is thirteen metres in the air at speed and the gangway is stowed. Boarding
            // from the sand would be a teleport into a moving hull.
            craft = new GameObject("Craft");
            FoilLift foil = craft.AddComponent<FoilLift>();
            foil.MaxRideHeight = 13f;

            GameObject deckObject = new GameObject("COL_Deck");
            deckObject.transform.SetParent(craft.transform, false);
            BoxCollider deck = deckObject.AddComponent<BoxCollider>();
            deck.size = new Vector3(4f, 0.3f, 15f);

            DeckBoarding boarding = craft.AddComponent<DeckBoarding>();
            boarding.Bind(deck, null, foil);

            Assert.IsTrue(boarding.CanInteract(), "Parked, the hull is on the sand and boardable.");

            // Fly it: the foil reports the ride height, so drive it there the way the craft does.
            for (int i = 0; i < 600; i++) foil.Tick(35f, Vector3.forward, 1f / 60f);

            Assert.Greater(foil.RideHeight01, 0.5f, "The craft has to be flying for this to mean anything.");
            Assert.IsFalse(boarding.CanInteract(),
                "A craft flying metres up must not put a 'climb aboard' prompt on screen.");
        }
    }
}
