using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "SceneReference", menuName = "Scene Management/Scene Reference")]
public class SceneReference : ScriptableObject
{

    [SerializeField] private string sceneName;

    public string SceneName => sceneName;

    
#if UNITY_EDITOR
    [SerializeField] private SceneAsset sceneAsset;
#endif


    public void OnValidate()
    {
#if UNITY_EDITOR
        if (sceneAsset != null)
            sceneName = sceneAsset.name;
#endif
    }
    
}