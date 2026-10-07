using UnityEngine;
using SpaceGame.Characters;

/// <summary>
/// The local player's death overlay.
///
/// Driven from OnEnable rather than Start, and decided from the player's CURRENT state rather than
/// only from the death event. Both of those are the same fix for the same problem: this HUD spends
/// the beginning of its life switched off. PlayerController.Awake calls DisablePlayer, which
/// deactivates the HUD, and it only comes back in EnablePlayer — on a networked session (which
/// includes solo play, since that is hosted) that is OnNetworkSpawn.
///
/// A death restored from a save lands inside that window. Start runs a frame later, so it
/// subscribed after the only announcement there was ever going to be and then hid the screen
/// unconditionally on top — which is why loading a save you had died in woke you at 0 health with
/// no death screen. OnEnable is delivered synchronously inside SetActive(true), and the state read
/// below covers the orderings even that cannot see.
/// </summary>
public class DeathScreenUI : MonoBehaviour
{

    [SerializeField] private RectTransform deathScreen;
    [SerializeField] private PlayerController player;

    private bool bound;

    private void OnEnable() => Present();

    private void OnDisable() => Dismiss();

    private void OnDestroy() => Dismiss();

    /// <summary>
    /// Subscribes, then shows or hides the overlay according to whether the player is dead right
    /// now. Idempotent, and public because the decision has to be re-made every time this HUD comes
    /// back up — and because edit mode does not deliver OnEnable to a plain MonoBehaviour, so a test
    /// has no other way in.
    /// </summary>
    public void Present()
    {
        if (deathScreen == null || player == null)
        {
            Debug.LogWarning($"{name}: DeathScreenUI is missing required references.", this);
            return;
        }

        if (!bound)
        {
            player.OnPlayerDeath += ShowDeathScreen;
            player.OnPlayerRevive += HideDeathScreen;
            bound = true;
        }

        // The player's state, not an event. See the class note: by the time this HUD exists, the
        // death it needs to react to may already have been announced and lost.
        deathScreen.gameObject.SetActive(player.IsDead);
    }

    /// <summary>Drops the subscriptions. Safe to call twice, and safe on a player already destroyed.</summary>
    public void Dismiss()
    {
        if (!bound) return;
        bound = false;

        if (player == null) return;

        player.OnPlayerDeath -= ShowDeathScreen;
        player.OnPlayerRevive -= HideDeathScreen;
    }

    private void ShowDeathScreen()
    {
        if (deathScreen != null) deathScreen.gameObject.SetActive(true);
    }

    private void HideDeathScreen()
    {
        if (deathScreen != null) deathScreen.gameObject.SetActive(false);
    }

    public void Respawn()
    {
        if (player == null) return;

        var respawn = player.GetComponent<PlayerRespawn>();
        if (respawn == null)
        {
            Debug.LogError($"{name}: '{player.name}' has no PlayerRespawn, so this button cannot " +
                           "bring them back. Add one to the player prefab.", this);
            return;
        }

        // The screen deliberately stays up. This is a request, not an order — the server decides
        // whether the respawn happens and where, and it is allowed to say no (no SpawnPoint yet,
        // the chunk under it still streaming). Hiding here left a refused player frozen behind an
        // empty screen with no button left to press. The overlay comes down on OnPlayerRevive
        // instead, i.e. when the player is genuinely alive again.
        respawn.Request();
    }
}
