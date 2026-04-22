// Optional motor extensions for mounts that can jump or leap.
// Mounted modules (SteerModule) call these instead of assuming a concrete motor type.
using UnityEngine;

public interface IMountJumpMotor
{
    void RequestJump();
}

// Leap = a long horizontal dash with a vertical arc. Used by SteerModule's hold-jump-to-leap mechanic.
public interface IMountLeapMotor
{
    bool IsLeapAvailable { get; }
    bool IsLeaping { get; }
    void RequestLeap(Vector3 direction, float horizontalDistance, float verticalHeight, float duration);
}
