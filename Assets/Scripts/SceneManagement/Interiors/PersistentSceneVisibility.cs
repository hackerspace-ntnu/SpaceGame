using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Snapshots and toggles persistent-scene rendering state (directional lights, post-process volumes,
/// visor overlays) while one or more interior scenes are active. Use Suspend() on first interior enter
/// and Restore() on last interior exit so persistent-scene lighting and overlays don't bleed into interiors.
/// </summary>
public class PersistentSceneVisibility
{
    private readonly Scene scene;

    private readonly List<Light> suspendedLights = new();
    private readonly List<Volume> suspendedVolumes = new();
    private readonly List<GameObject> suspendedOverlays = new();

    private bool suspended;

    public PersistentSceneVisibility(Scene scene)
    {
        this.scene = scene;
    }

    public void Suspend()
    {
        if (suspended || !scene.IsValid() || !scene.isLoaded) return;
        suspended = true;

        foreach (var go in scene.GetRootGameObjects())
        {
            CollectAndDisable(go);
        }
    }

    public void Restore()
    {
        if (!suspended) return;
        suspended = false;

        foreach (var l in suspendedLights) if (l != null) l.enabled = true;
        foreach (var v in suspendedVolumes) if (v != null) v.enabled = true;
        foreach (var go in suspendedOverlays) if (go != null) go.SetActive(true);

        suspendedLights.Clear();
        suspendedVolumes.Clear();
        suspendedOverlays.Clear();
    }

    private void CollectAndDisable(GameObject root)
    {
        // Directional lights — any in the persistent scene
        foreach (var light in root.GetComponentsInChildren<Light>(true))
        {
            if (light.type != LightType.Directional) continue;
            if (!light.enabled) continue;
            light.enabled = false;
            suspendedLights.Add(light);
        }

        // Volumes (post-processing). Disabling the component is enough — no need to mess with priority.
        foreach (var volume in root.GetComponentsInChildren<Volume>(true))
        {
            if (!volume.enabled) continue;
            volume.enabled = false;
            suspendedVolumes.Add(volume);
        }

        // Visor / overlay GameObjects (matched by name, case-insensitive contains "visor")
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var go = t.gameObject;
            if (!go.activeSelf) continue;
            var n = go.name;
            if (n.Length == 0) continue;
            if (n.IndexOf("visor", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            go.SetActive(false);
            suspendedOverlays.Add(go);
        }
    }
}
