
using UnityEngine;

public class GameServiceLoader : MonoBehaviour
{
    void Awake()
    {
        GameServices.Initialize();
    }
}

