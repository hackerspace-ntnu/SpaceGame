// A component that answers a character's conversation instead of its authored dialog lines.
//
// DialogInteraction is the one IInteractable on a talking character (Interactor resolves one per
// collider), so anything else that wants to answer "the player pressed talk" — a settlement
// resident picking a line from what it knows about you — plugs in here rather than becoming a
// second IInteractable whose turn would depend on component order.
using UnityEngine;

namespace SpaceGame.Gameplay
{
    public interface IDialogResponder
    {
        /// <summary>Whether this responder takes the conversation with <paramref name="player"/> now.</summary>
        bool CanRespond(Transform player);

        /// <summary>
        /// Answer <paramref name="player"/>. Called on the machine whose player pressed talk: the
        /// responder decides there when that machine is the server, and sends the request on when it
        /// is a client — the network is the responder's business, not the dialog's.
        /// </summary>
        void Respond(Transform player);
    }
}
