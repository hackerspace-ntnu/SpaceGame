using SpaceGame.Gameplay;

namespace SpaceGame.Core
{
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
                // Both modes get the same services. WorldService already branches internally on
                // Network.IsNetworked, and leaving this case empty left GameServices.World null in
                // multiplayer — every GameServices.World.Despawn call site (item pickup among them)
                // threw a NullReference the moment the mode was Multiplayer.
                case GameMode.Multiplayer:
                case GameMode.Singleplayer:
                    ItemDropService = new PlayerDropService();
                    World = new WorldService();
                    break;

            }
        }
    
        public static IItemDropService ItemDropService { get; set; }
    
        public static IWorldService World { get; set; }
    }
}
