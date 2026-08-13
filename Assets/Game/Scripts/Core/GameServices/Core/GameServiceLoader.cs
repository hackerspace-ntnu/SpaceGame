using UnityEngine;

namespace SpaceGame.Core
{
    public class GameServiceLoader : MonoBehaviour
    {
        void Awake()
        {
            GameServices.Initialize();
        }
    }
}
