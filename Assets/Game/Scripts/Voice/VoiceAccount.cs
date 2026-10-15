// This player's Unity account id — the one identity that stays the same from session to session.
//
// Per-person voice levels are remembered against it. A display name can change mid-session and two
// players can share one; a netcode client id is reassigned every time anybody connects. The UGS id
// of an anonymously signed-in player is stable for that install and profile, which is exactly the
// lifetime "I turned Øyvind's quiet microphone up" ought to have.
//
// Solo play, a host of one, may never initialise Unity Services, and AuthenticationService.Instance
// throws when they are not up — so this asks UnityServices.State first, the same check
// SessionLauncher makes, and answers empty rather than touching the service.
using Unity.Services.Authentication;
using Unity.Services.Core;

namespace SpaceGame.Voice
{
    /// <summary>The local player's stable identity, or empty when there is none to be had.</summary>
    public static class VoiceAccount
    {
        public static string LocalId
        {
            get
            {
                if (UnityServices.State != ServicesInitializationState.Initialized) return string.Empty;

                var auth = AuthenticationService.Instance;
                return auth.IsSignedIn ? VoiceRoster.CleanAccountId(auth.PlayerId) : string.Empty;
            }
        }
    }
}
