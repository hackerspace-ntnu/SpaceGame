
using System;

public static class Game
{
    public static GameMode Mode { get; private set; } = GameMode.Singleplayer;

    public static event Action<GameMode> OnGameModeChanged;

    public static void SetMode(GameMode mode)
    {
        if (Mode == mode)
            return;

        Mode = mode;
        OnGameModeChanged?.Invoke(mode);
    }
}
