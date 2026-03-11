
using System;
using UnityEngine;

public class GameServiceLoader : MonoBehaviour
{
    private void Awake()
    {
        GameServices.ItemDropService = new PlayerDropService();
    }
}

