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
            // The networked path owns its own binding, and doing it twice would leave two objects
            // claiming one profile record.
            if (Network.IsNetworked && GetComponent<PlayerSaveSync>() != null) return;

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

            SaveManager.Instance?.Players?.Unbind(boundProfileId);
            boundProfileId = null;
        }
    }
}
