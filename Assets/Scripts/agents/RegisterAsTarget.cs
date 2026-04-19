// Add this to any GameObject that should be findable by entity modules (e.g. the Player).
// It registers/unregisters the transform in EntityTargetRegistry automatically.
using UnityEngine;

public class RegisterAsTarget : MonoBehaviour
{
    [SerializeField] private string targetTag = "Player";

    private void OnEnable() => EntityTargetRegistry.Register(targetTag, transform);
    private void OnDisable() => EntityTargetRegistry.Unregister(targetTag, transform);
}
