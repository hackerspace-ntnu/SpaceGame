using UnityEngine;

/// <summary>
/// TEST-ONLY: ensures an InteriorManager exists in the scene and spawns a placeholder
/// entrance "door" near the player on Start. Walk up and press E to test the interior system.
///
/// Self-installs on play via RuntimeInitializeOnLoadMethod — no manual scene wiring required.
/// Set <see cref="autoInstallEnabled"/> to false (or delete this script) once real entrances exist.
/// </summary>
public class InteriorTestBootstrap : MonoBehaviour
{
    private const string InteriorAssetPath = "Interiors/Interior_Test";
    // Off by default now that real entrances exist. Flip to true if you want the
    // placeholder cube-entrance back for testing.
    private const bool autoInstallEnabled = false;

    [SerializeField] private InteriorScene interiorToTest;
    [SerializeField] private Transform playerOverride;
    [SerializeField] private Vector3 spawnOffsetLocalToPlayer = new Vector3(3f, 0f, 3f);
    [SerializeField] private float spawnDelay = 2f;

    private bool spawned;
    private float timer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInstall()
    {
        if (!autoInstallEnabled) return;
        if (FindFirstObjectByType<InteriorTestBootstrap>() != null) return;
        var so = Resources.Load<InteriorScene>(InteriorAssetPath);
        if (so == null)
        {
            // Asset isn't in a Resources folder — that's fine, the user can drop one in
            // a placed component instead. Skip silently rather than spamming errors.
            return;
        }
        var go = new GameObject("InteriorTestBootstrap");
        var bs = go.AddComponent<InteriorTestBootstrap>();
        bs.interiorToTest = so;
    }

    private void Awake()
    {
        if (FindFirstObjectByType<InteriorManager>() == null)
        {
            var go = new GameObject("InteriorManager");
            go.AddComponent<InteriorManager>();
            Debug.Log("[InteriorTestBootstrap] Created InteriorManager singleton.");
        }
    }

    private void Update()
    {
        if (spawned || interiorToTest == null) return;
        timer += Time.deltaTime;
        if (timer < spawnDelay) return;

        Transform player = playerOverride;
        if (player == null)
        {
            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null) player = tagged.transform;
        }
        if (player == null) return;

        Vector3 pos = player.position
                    + player.right * spawnOffsetLocalToPlayer.x
                    + Vector3.up * spawnOffsetLocalToPlayer.y
                    + player.forward * spawnOffsetLocalToPlayer.z;

        var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
        door.name = "TestInteriorEntrance";
        door.transform.position = pos + Vector3.up;
        door.transform.localScale = new Vector3(1.2f, 2f, 0.2f);
        door.transform.rotation = Quaternion.LookRotation(-player.forward, Vector3.up);

        var entrance = door.AddComponent<InteriorEntrance>();
        entrance.Initialize(interiorToTest);

        Debug.Log($"[InteriorTestBootstrap] Spawned test entrance at {door.transform.position}. Walk up and press E.");
        spawned = true;
    }
}
