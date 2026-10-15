using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace EEjanaiTeam
{
    [InitializeOnLoad]
    public static class ToonShaderInstaller
    {
        private static ListRequest listRequest;
        private static AddRequest addRequest;

        static ToonShaderInstaller()
        {
            listRequest = Client.List();
            EditorApplication.update += CheckForToonShader;
        }

        private static void CheckForToonShader()
        {
            if (!listRequest.IsCompleted) return;

            EditorApplication.update -= CheckForToonShader;

            bool isToonShaderInstalled = false;
            foreach (var package in listRequest.Result)
            {
                if (package.name == "com.unity.toonshader")
                {
                    isToonShaderInstalled = true;
                    break;
                }
            }

            if (!isToonShaderInstalled)
            {
                Debug.Log("Installing Unity Toon Shader (0.11.0-preview)...");
                addRequest = Client.Add("com.unity.toonshader@0.11.0-preview");
                EditorApplication.update += InstallToonShader;
            }
        }

        private static void InstallToonShader()
        {
            if (!addRequest.IsCompleted) return;

            EditorApplication.update -= InstallToonShader;

            if (addRequest.Status == StatusCode.Success)
            {
                Debug.Log("Unity Toon Shader installed successfully.");
            }
            else
            {
                Debug.LogError("Failed to install Unity Toon Shader: " + addRequest.Error.message);
            }
        }
    }

}