
public static class GameServices
{
    
    public static void Initialize()
    {
        LoadServices(Game.Mode);

        Game.OnGameModeChanged += LoadServices;
    }
    
    static void LoadServices(GameMode mode)
    {
        switch (Game.Mode)
        {
            case GameMode.Multiplayer:
                break;
            
            case GameMode.Singleplayer:
                ItemDropService = new PlayerDropService();
                World = new WorldService();
                break;
                
        }
    }
    
    public static IItemDropService ItemDropService { get; set; }
    
    public static IWorldService World { get; set; }
}

