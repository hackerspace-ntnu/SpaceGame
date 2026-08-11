using UnityEngine;

/// <summary>
/// Drop on a settlement, quest target, NPC, etc. and it shows up on the map
/// while enabled. Position tracks this transform live.
/// </summary>
[DisallowMultipleComponent]
public class MapMarker : MonoBehaviour
{
    [SerializeField] private MapMarkerType type = MapMarkerType.Neutral;
    [SerializeField] private string label;
    [Tooltip("If true, the marker is hidden on the map until the chunk it sits in has been revealed.")]
    [SerializeField] private bool requiresRevealedChunk = true;

    private MapService.Marker handle;

    private void OnEnable()
    {
        TryRegister();
    }

    private void OnDisable()
    {
        if (handle != null && MapService.Instance != null)
            MapService.Instance.RemoveMarker(handle);
        handle = null;
    }

    private void Start()
    {
        // MapService may spawn after this object — retry if needed.
        if (handle == null) TryRegister();
    }

    private void TryRegister()
    {
        if (handle != null) return;
        var svc = MapService.Instance;
        if (svc == null) return;
        handle = svc.RegisterMarker(transform, type, label, requiresRevealedChunk);
    }

    public void SetType(MapMarkerType newType)
    {
        type = newType;
        if (handle != null) handle.type = newType;
    }

    public void SetLabel(string newLabel)
    {
        label = newLabel;
        if (handle != null) handle.label = newLabel;
    }
}
