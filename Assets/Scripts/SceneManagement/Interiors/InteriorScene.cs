using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Designer-facing definition of an interior space.
/// Points to a Unity scene asset and names the spawn/exit anchors inside it.
/// </summary>
[CreateAssetMenu(fileName = "Interior_", menuName = "Scene Management/Interior Scene")]
public class InteriorScene : ScriptableObject
{
    [SerializeField] private string sceneName;
    [Tooltip("Anchor id (matches an InteriorAnchor inside the interior scene) where the player is placed when entering.")]
    [SerializeField] private string spawnAnchorId = "entrance";

    public string SceneName => sceneName;
    public string SpawnAnchorId => spawnAnchorId;

#if UNITY_EDITOR
    [SerializeField] private SceneAsset sceneAsset;

    private void OnValidate()
    {
        if (sceneAsset != null)
            sceneName = sceneAsset.name;

        ValidateAnchor();
        ValidateBuildSettings();
    }

    // Catch typos in spawnAnchorId at edit time, but only if the target scene is
    // already loaded — opening scenes from OnValidate hangs the editor. If the
    // scene isn't loaded, we skip; the runtime warning ("dropped at origin") still
    // catches it the first time someone enters the interior.
    private void ValidateAnchor()
    {
        if (sceneAsset == null || string.IsNullOrEmpty(spawnAnchorId)) return;

        var loaded = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneAsset.name);
        if (!loaded.IsValid() || !loaded.isLoaded) return;

        if (InteriorAnchor.Find(loaded, spawnAnchorId) == null)
            Debug.LogWarning($"[InteriorScene] '{name}' references anchor '{spawnAnchorId}' but no InteriorAnchor with that id exists in {sceneAsset.name}.", this);
    }

    private void ValidateBuildSettings()
    {
        if (sceneAsset == null) return;
        string scenePath = AssetDatabase.GetAssetPath(sceneAsset);
        foreach (var s in EditorBuildSettings.scenes)
        {
            if (s.path == scenePath && s.enabled) return;
        }
        Debug.LogWarning($"[InteriorScene] '{name}' targets {sceneAsset.name} but that scene is not in (enabled in) Build Settings — runtime LoadSceneAsync will fail.", this);
    }
#endif
}
