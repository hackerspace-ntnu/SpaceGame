namespace SpaceGame.Gameplay
{
    /// <summary>One axis of a slewing motor: where it points and how fast it is turning.</summary>
    public struct SlewAxis
    {
        public float Angle;
        public float Velocity;

        public SlewAxis(float angle)
        {
            Angle = angle;
            Velocity = 0f;
        }
    }
}
