using UnityEngine;

public class RocketBoosterController : MonoBehaviour
{
    [SerializeField] private GameObject boosterObjectPrefab;
    [SerializeField] private Transform boosterSpawnPoint;
    [SerializeField] private bool isBoosterActive = false;

    private GameObject activeBoosterInstance;

    public bool IsBoosterActive
    {
        get => isBoosterActive;
        set => SetBoosterActive(value);
    }

    private void Start()
    {
        // Initialize booster - create instance but keep it disabled
        if (boosterObjectPrefab != null)
        {
            Transform spawnPos = boosterSpawnPoint != null ? boosterSpawnPoint : transform;
            activeBoosterInstance = Instantiate(boosterObjectPrefab, spawnPos.position, spawnPos.rotation, spawnPos);
            activeBoosterInstance.name = "BoosterObject";
            activeBoosterInstance.SetActive(isBoosterActive);
        }
    }

    /// <summary>
    /// Enable or disable the booster visual effect
    /// </summary>
    public void SetBoosterActive(bool active)
    {
        isBoosterActive = active;

        if (activeBoosterInstance != null)
        {
            activeBoosterInstance.SetActive(active);
        }
        else if (active && boosterObjectPrefab != null)
        {
            // Create booster if it doesn't exist and we need it active
            Transform spawnPos = boosterSpawnPoint != null ? boosterSpawnPoint : transform;
            activeBoosterInstance = Instantiate(boosterObjectPrefab, spawnPos.position, spawnPos.rotation, spawnPos);
            activeBoosterInstance.name = "BoosterObject";
        }
    }

    /// <summary>
    /// Toggle the booster on/off
    /// </summary>
    public void ToggleBooster()
    {
        SetBoosterActive(!isBoosterActive);
    }

    private void OnDestroy()
    {
        if (activeBoosterInstance != null)
        {
            Destroy(activeBoosterInstance);
        }
    }
}
