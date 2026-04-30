using UnityEngine;

/// <summary>
/// Anything in the world the Ruin Scanner can expose: hidden doors, faded
/// glyphs, dormant mechanisms, floor patterns. Implementers decide what
/// "reveal" means for them — fade in a holo overlay, light up a symbol,
/// briefly enable a child object, etc.
/// </summary>
public interface IRuinSecret
{
    Vector3 Position { get; }
    void Reveal(float duration);
}
