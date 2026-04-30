using System.Collections;
using UnityEngine;

/// <summary>
/// Drop-anywhere persistent map POI. Add this component to any GameObject in
/// any scene (chunk scenes included) — no controller, no other setup. It
/// captures its world position on first enable and registers a static marker
/// with `MapService`. The marker persists for the rest of the session even if
/// the GameObject's chunk is unloaded, and re-enabling won't duplicate it.
///
/// Use `MapMarker` instead if you need a moving marker that follows a transform.
/// </summary>
[DisallowMultipleComponent]
public class MapPOI : MonoBehaviour
{
    [Tooltip("Display name shown next to the marker on the map.")]
    [SerializeField] private string poiName = "POI";

    [Tooltip("Marker color category (Discovery, Quest, Hostile, Friendly, Neutral).")]
    [SerializeField] private MapMarkerType type = MapMarkerType.Discovery;

    [Tooltip("If on, the marker is visible regardless of which chunks the player has explored. If off, only shows once the chunk is revealed.")]
    [SerializeField] private bool alwaysVisible = true;

    [Tooltip("Stable unique ID. Auto-generated on first add — don't edit unless you know what you're doing.")]
    [HideInInspector]
    [SerializeField] private string id;

    private void Reset()    => EnsureId();
    private void OnValidate() => EnsureId();

    private void EnsureId()
    {
        if (string.IsNullOrEmpty(id))
            id = System.Guid.NewGuid().ToString("N");
    }

    private void OnEnable()
    {
        EnsureId();
        if (MapService.Instance != null) Register();
        else StartCoroutine(WaitAndRegister());
    }

    private IEnumerator WaitAndRegister()
    {
        // MapService may live in the persistent scene which loads after a chunk
        // scene awakes — wait a frame or two for it to spawn.
        while (MapService.Instance == null && isActiveAndEnabled)
            yield return null;
        if (isActiveAndEnabled) Register();
    }

    private void Register()
    {
        var svc = MapService.Instance;
        if (svc == null) return;
        if (svc.HasPOI(id)) return;
        svc.RegisterPOI(id, transform.position, type, poiName, !alwaysVisible);
    }

    /// <summary>
    /// Use after editing name/type in the editor at runtime — re-registers under the same ID.
    /// </summary>
    public void Refresh()
    {
        var svc = MapService.Instance;
        if (svc == null || string.IsNullOrEmpty(id)) return;
        // Currently MapService doesn't support in-place updates, so we just
        // skip if already present. Users editing live can disable→enable the GO.
        if (!svc.HasPOI(id))
            svc.RegisterPOI(id, transform.position, type, poiName, !alwaysVisible);
    }
}
