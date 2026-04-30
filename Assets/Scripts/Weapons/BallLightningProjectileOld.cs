using UnityEngine;

/// <summary>
/// DEPRECATED: This file has been moved to Assets/Scripts/Weapons/Projectiles/BallLightningProjectile.cs
/// 
/// The projectile system has been refactored to use an abstract Projectile base class
/// for better extensibility and modularity. The new BallLightningProjectile extends
/// the Projectile base class with all the same movement, lighting, and impact behavior.
/// 
/// If you have prefabs referencing this old location, please update them to point to:
/// Assets/Scripts/Weapons/Projectiles/BallLightningProjectile.cs
/// </summary>
#pragma warning disable CS0162 // Unreachable code detected

public class BallLightningProjectile_OLD : MonoBehaviour
{
    [Tooltip("DEPRECATED - Do not use this class. Use Assets/Scripts/Weapons/Projectiles/BallLightningProjectile instead.")]
    public bool deprecatedDoNotUse = true;
}
