// Owner-authoritative NetworkTransform, the transform counterpart to ClientNetworkAnimator.
//
// Needed by anything a CLIENT drives directly rather than the server: a mount whose ownership was
// handed to its rider, or a wing-pack ornithopter owned by its pilot. With the stock
// server-authoritative NetworkTransform the owner's local motion is overwritten by the server every
// tick, which reads as the vehicle fighting the player's input.
using Unity.Netcode.Components;

namespace SpaceGame.Core
{
    [UnityEngine.DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false; // The owning client sends transform data.
        }
    }
}
