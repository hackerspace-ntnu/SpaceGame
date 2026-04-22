using UnityEngine;

/// <summary>
/// Interface for projectiles that support charging mechanics.
/// Charging allows gradual power-up before firing.
/// </summary>
public interface IChargeable
{
    /// <summary>
    /// Get the current charge level (0 to 1).
    /// </summary>
    float GetChargeLevel();

    /// <summary>
    /// Update charge state. Called each frame while charging.
    /// </summary>
    /// <param name="chargeProgress">Normalized charge progress (0 to 1)</param>
    void UpdateCharge(float chargeProgress);

    /// <summary>
    /// Called when charging is complete and projectile should be released.
    /// </summary>
    void OnChargeComplete();

    /// <summary>
    /// Called if charging is cancelled (e.g., dropped without firing).
    /// </summary>
    void OnChargeCancelled();
}
