// Disables all IBehaviourModules on this GameObject and its children while a rider is mounted.
// Re-enables them on dismount. Ensures mounted steering owns movement without interference.
// Modules are cached at Awake — call RefreshModuleCache() if modules are added at runtime.
using UnityEngine;

public class MountSuppressorModule : MonoBehaviour
{
    [SerializeField] private MountController mountController;

    private MonoBehaviour[] moduleComponents;

    private void Awake()
    {
        if (!mountController)
            mountController = GetComponent<MountController>();

        CacheModules();
    }

    private void OnEnable()
    {
        if (!mountController)
            return;
        mountController.Mounted += OnMounted;
        mountController.Dismounted += OnDismounted;
    }

    private void OnDisable()
    {
        if (!mountController)
            return;
        mountController.Mounted -= OnMounted;
        mountController.Dismounted -= OnDismounted;
    }

    public void RefreshModuleCache() => CacheModules();

    private void CacheModules()
    {
        MonoBehaviour[] all = GetComponentsInChildren<MonoBehaviour>(true);
        int count = 0;
        foreach (MonoBehaviour mb in all)
            if (mb is IBehaviourModule && mb != this)
                count++;

        moduleComponents = new MonoBehaviour[count];
        int i = 0;
        foreach (MonoBehaviour mb in all)
            if (mb is IBehaviourModule && mb != this)
                moduleComponents[i++] = mb;
    }

    private void OnMounted(PlayerMovement player)
    {
        if (moduleComponents == null)
            return;
        foreach (MonoBehaviour mb in moduleComponents)
            if (mb) mb.enabled = false;
    }

    private void OnDismounted(PlayerMovement player)
    {
        if (moduleComponents == null)
            return;
        foreach (MonoBehaviour mb in moduleComponents)
            if (mb) mb.enabled = true;
    }
}
