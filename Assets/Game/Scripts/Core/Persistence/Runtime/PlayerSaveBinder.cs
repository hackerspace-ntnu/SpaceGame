using UnityEngine;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Binds a player that no network spawn produced.
    ///
    /// <see cref="PlayerSaveSync"/> covers the normal path — Netcode spawns a player, the owner
    /// reports its profile, the server restores it — but it is a NetworkBehaviour and does nothing
    /// without a NetworkObject. Two common cases have neither:
    ///
    ///   • a player object placed directly in a test or world scene at edit time, which is how the
    ///     project's scenes are usually entered from the editor;
    ///   • the offline PlayerCharacter prefab, which carries no NetworkObject at all.
    ///
    /// Both are still the local player and still need their inventory and health to survive a save.
    /// Without this they are captured by nothing, and a save written from an editor session comes
    /// back with an empty player list — which is exactly what the first live probe of this system
    /// found.
    /// </summary>
    public class PlayerSaveBinder : MonoBehaviour
    {
        private string boundProfileId;

        private void Start()
        {
            // In a networked session — which includes every hosted singleplayer game — the real
            // player is the one Netcode spawns, and PlayerSaveSync binds it. Nothing else may.
            //
            // The earlier version only stepped aside for objects that HAD a PlayerSaveSync, which
            // let persistentScene's authored PlayerCharacter (an offline placeholder with no
            // NetworkObject, sitting at its authored position with its components switched off by
            // PlayerController.DisablePlayer) claim the same profile as the spawned player. Whoever
            // bound last won, so a save could record the placeholder's position instead of where
            // the player actually stood.
            if (Network.IsNetworked) return;

            SaveManager manager = SaveManager.Instance;
            if (manager?.Players == null) return;

            boundProfileId = PlayerProfile.LocalId;

            // Position is deliberately not applied. This object was placed by the scene or by a
            // spawner that already decided where it goes, and moving it here would fight that —
            // the streamed world in particular has only loaded the chunks around where it stands.
            manager.Players.Bind(boundProfileId, gameObject, applyPosition: false);
        }

        private void OnDestroy()
        {
            if (string.IsNullOrEmpty(boundProfileId)) return;

            PlayerSaveService players = SaveManager.Instance?.Players;

            // Only while this object is still the one speaking for the profile. Unbinding captures
            // the LIVE player's state into the record and drops them from the bound table, so doing
            // it on behalf of a profile something else has taken over writes the wrong player's
            // state down and then stops capturing them at all. Same rule as PlayerSaveSync's.
            if (players != null &&
                players.TryGetBoundPlayer(boundProfileId, out GameObject live) &&
                live != gameObject)
            {
                boundProfileId = null;
                return;
            }

            players?.Unbind(boundProfileId);
            boundProfileId = null;
        }
    }
}
