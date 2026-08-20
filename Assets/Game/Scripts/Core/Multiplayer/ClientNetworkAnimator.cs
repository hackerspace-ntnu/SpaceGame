using Unity.Netcode.Components;

namespace SpaceGame.Core
{
    public class ClientNetworkAnimator : NetworkAnimator
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false; // Allows the client to send animation data
        }
    }
}
