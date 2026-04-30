using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drop-anywhere component that marks a GameObject (or sub-tree) as a hidden
/// ruin secret. While dormant the assigned renderers are invisible. When the
/// Ruin Scanner pulses over it, the renderers swap to a holographic reveal
/// material for `revealDuration` seconds, then fade back to invisible.
///
/// Optional `secretRoot` lets you reveal a child object (e.g. a door mesh
/// that's normally disabled) without touching the trigger volume itself.
/// </summary>
[DisallowMultipleComponent]
public class RuinSecret : MonoBehaviour, IRuinSecret
{
    [Tooltip("Root of the visuals to reveal. If null, uses this GameObject.")]
    [SerializeField] private GameObject secretRoot;

    [Tooltip("Renderers that should be hidden until scanned. Auto-collected from secretRoot if empty.")]
    [SerializeField] private List<Renderer> revealRenderers = new();

    [Tooltip("Material applied to revealRenderers while exposed. Should be a holographic / additive shader.")]
    [SerializeField] private Material revealMaterial;

    [Tooltip("If true the secret's renderers stay disabled when not revealed (recommended for hidden doors).")]
    [SerializeField] private bool hideWhenDormant = true;

    [Tooltip("Default reveal duration if the scanner doesn't override it. Seconds.")]
    [SerializeField] private float defaultRevealDuration = 6f;

    [Tooltip("Optional interactable component (anything deriving from Behaviour, e.g. an IInteractable script). Disabled while dormant, enabled while exposed — so the player can only interact with the secret while the scanner has it lit up.")]
    [SerializeField] private Behaviour interactable;

    [Tooltip("Fade-in time at the start of a reveal. Seconds.")]
    [SerializeField] private float fadeInDuration = 0.4f;

    [Tooltip("Fade-out time at the end of a reveal. Seconds.")]
    [SerializeField] private float fadeOutDuration = 1.2f;

    private readonly Dictionary<Renderer, Material[]> originalMaterials = new();
    private float revealEndTime;
    private float revealStartTime;
    private float activeDuration;
    private bool isRevealed;
    private MaterialPropertyBlock mpb;
    private static readonly int RevealAlphaId = Shader.PropertyToID("_RevealAlpha");

    public Vector3 Position => (secretRoot != null ? secretRoot.transform : transform).position;

    private void Awake()
    {
        if (secretRoot == null) secretRoot = gameObject;
        if (revealRenderers.Count == 0)
            secretRoot.GetComponentsInChildren(includeInactive: true, revealRenderers);

        foreach (var r in revealRenderers)
            if (r != null) originalMaterials[r] = r.sharedMaterials;

        mpb = new MaterialPropertyBlock();
        ApplyDormantState();
    }

    private void ApplyDormantState()
    {
        if (interactable != null) interactable.enabled = false;
        if (!hideWhenDormant) return;
        foreach (var r in revealRenderers)
            if (r != null) r.enabled = false;
    }

    public void Reveal(float duration)
    {
        if (interactable != null) interactable.enabled = true;

        if (revealMaterial == null)
        {
            // No reveal material assigned — just make the object visible while active.
            revealEndTime = Time.time + Mathf.Max(0.1f, duration);
            revealStartTime = Time.time;
            activeDuration = Mathf.Max(0.1f, duration);
            foreach (var r in revealRenderers)
                if (r != null) r.enabled = true;
            isRevealed = true;
            enabled = true;
            return;
        }

        // Extend if already revealed.
        float dur = duration > 0f ? duration : defaultRevealDuration;
        revealEndTime = Mathf.Max(revealEndTime, Time.time + dur);
        if (!isRevealed)
        {
            revealStartTime = Time.time;
            activeDuration = dur;
            SwapToRevealMaterial();
            isRevealed = true;
        }
        else
        {
            // Reset start so the fade-in feels fresh on a re-pulse.
            revealStartTime = Time.time;
            activeDuration = dur;
        }
        enabled = true;
    }

    private void SwapToRevealMaterial()
    {
        foreach (var r in revealRenderers)
        {
            if (r == null) continue;
            int n = r.sharedMaterials.Length;
            var mats = new Material[n];
            for (int i = 0; i < n; i++) mats[i] = revealMaterial;
            r.sharedMaterials = mats;
            r.enabled = true;
        }
    }

    private void RestoreDormantMaterials()
    {
        foreach (var kv in originalMaterials)
        {
            if (kv.Key == null) continue;
            kv.Key.sharedMaterials = kv.Value;
        }
        if (hideWhenDormant)
        {
            foreach (var r in revealRenderers)
                if (r != null) r.enabled = false;
        }
        if (interactable != null) interactable.enabled = false;
    }

    private void Update()
    {
        if (!isRevealed) { enabled = false; return; }

        float now = Time.time;
        float elapsed = now - revealStartTime;
        float remaining = revealEndTime - now;

        float alpha;
        if (elapsed < fadeInDuration)
            alpha = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, fadeInDuration));
        else if (remaining < fadeOutDuration)
            alpha = Mathf.Clamp01(remaining / Mathf.Max(0.0001f, fadeOutDuration));
        else
            alpha = 1f;

        foreach (var r in revealRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(mpb);
            mpb.SetFloat(RevealAlphaId, alpha);
            r.SetPropertyBlock(mpb);
        }

        if (now >= revealEndTime)
        {
            isRevealed = false;
            RestoreDormantMaterials();
            enabled = false;
        }
    }
}
