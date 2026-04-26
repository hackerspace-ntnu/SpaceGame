// Rider-driven continuous steering channel for motors. Parallels IMovementMotor (AI goal channel)
// but carries raw per-frame rider input instead of a goal. SteerModule forwards one of these to
// the motor each frame while the rider is steering; the motor interprets it in its own physics
// model (tank steer on ground, throttle/yaw/vertical in flight, etc.).
//
// A motor that implements IRiderControllable must stamp Time.frameCount inside ApplyRiderInput
// and, in its Tick(MoveIntent) implementation, skip MoveIntent interpretation on the same frame
// so the AI channel can't fight the rider. Arc/cooldown updates should still run.
using UnityEngine;

public readonly struct RiderInput
{
    // x = yaw (turn left/right), y = throttle (forward/back). Already smoothed by SteerModule.
    public readonly Vector2 Move;
    // Ascend/descend axis for flying motors. Ground motors ignore it.
    public readonly float Vertical;
    // Rider asked for a "running" speed (sprint) this frame.
    public readonly bool IsRunning;

    public RiderInput(Vector2 move, float vertical, bool isRunning)
    {
        Move = move;
        Vertical = vertical;
        IsRunning = isRunning;
    }
}

public interface IRiderControllable
{
    void ApplyRiderInput(in RiderInput input, float deltaTime);
}
