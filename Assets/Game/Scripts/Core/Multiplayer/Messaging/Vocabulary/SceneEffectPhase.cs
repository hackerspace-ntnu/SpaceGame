namespace SpaceGame.Core
{
    /// <summary>Which half of a transition <see cref="NetMsg.SceneEffects"/> is asking for.</summary>
    public static class SceneEffectPhase
    {
        /// <summary>Begin the out phase — fade down, muffle, play the cutscene.</summary>
        public const int Out = 0;

        /// <summary>The destination has landed; run the in phase and finish.</summary>
        public const int In = 1;
    }
}
