using System.Collections;
using UnityEngine;
using UnityEngine.Events;

// Click a lever -> rotate the handle once -> fire OnPulled.
// Wire OnPulled to anything (enable a hidden door, play sounds, trigger an InteriorPortal, etc.).
public class LeverInteraction : MonoBehaviour, IInteractable
{
    [Header("Visuals")]
    [Tooltip("The transform that rotates when pulled. If null, uses this GameObject.")]
    [SerializeField] private Transform handle;

    [Tooltip("Local euler offset applied to the handle when pulled.")]
    [SerializeField] private Vector3 pulledLocalEuler = new Vector3(60f, 0f, 0f);

    [SerializeField] private float animDuration = 0.6f;

    [Header("Behavior")]
    [Tooltip("If true, the lever can only be pulled once.")]
    [SerializeField] private bool oneShot = true;

    [Header("Events")]
    [Tooltip("Fires when the handle has finished animating to the pulled position.")]
    [SerializeField] private UnityEvent onPulled;

    private bool busy;
    private bool pulled;
    private Quaternion restRotation;

    private void Awake()
    {
        if (handle == null) handle = transform;
        restRotation = handle.localRotation;
    }

    public bool CanInteract() => !busy && !(oneShot && pulled);

    public void Interact(Interactor interactor)
    {
        if (!CanInteract()) return;
        StartCoroutine(PullRoutine());
    }

    private IEnumerator PullRoutine()
    {
        busy = true;
        Quaternion target = restRotation * Quaternion.Euler(pulledLocalEuler);
        float t = 0f;
        float dur = Mathf.Max(0.01f, animDuration);
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
            handle.localRotation = Quaternion.Slerp(restRotation, target, k);
            yield return null;
        }
        handle.localRotation = target;
        pulled = true;

        try { onPulled?.Invoke(); }
        catch (System.Exception e) { Debug.LogException(e); }

        busy = false;
    }
}
