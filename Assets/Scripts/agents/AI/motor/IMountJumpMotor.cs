// Optional motor extension for mounts that can perform jumps.
// Mounted brains/controllers call this instead of assuming a concrete motor type.
public interface IMountJumpMotor
{
    void RequestJump();
}
