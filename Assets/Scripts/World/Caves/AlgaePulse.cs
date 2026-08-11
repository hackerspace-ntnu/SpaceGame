using UnityEngine;
using UnityEngine.InputSystem;

// Pushes interaction-wave parameters to the AlgaeRock shader via globals.
//
// Each wave is a (worldPos, startTime) tuple. The shader expands the wavefront outward
// in world space; it naturally dies after waveLifetime seconds.
//
// MAX_WAVES is small (4) — fragments do that many distance checks. Increase carefully.
public class AlgaePulse : MonoBehaviour
{
    public const int MAX_WAVES = 4;

    [Header("Wave Shape")]
    [Tooltip("Wavefront speed in meters per second.")]
    [SerializeField] private float waveSpeed = 8f;
    [Tooltip("Lifetime of a wave in seconds. After this, the wave stops being evaluated.")]
    [SerializeField] private float waveLifetime = 1.5f;
    [Tooltip("Thickness of the visible band of the wavefront in meters.")]
    [SerializeField] private float waveThickness = 1.2f;
    [Tooltip("HDR intensity boost applied to algae emission inside the wavefront band.")]
    [SerializeField] private float waveIntensity = 12f;

    [Header("Auto-trigger on Collision")]
    [Tooltip("If true, OnCollisionEnter / OnTriggerEnter on the GameObject this component is attached to will fire a wave at the contact point. Requires a Collider on the same GameObject. For moving objects (player), a Rigidbody is also required somewhere in the contact pair.")]
    [SerializeField] private bool autoTriggerOnCollision = true;
    [Tooltip("Layers that fire a wave on collision/trigger. Leave at Everything (-1) for testing.")]
    [SerializeField] private LayerMask triggerMask = ~0;

    [Header("Debug")]
    [Tooltip("Press this key to fire a test wave at this GameObject's position. Useful for confirming the shader path works before debugging collision.")]
    [SerializeField] private Key debugTriggerKey = Key.P;
    [Tooltip("Log every Trigger() call to the Console.")]
    [SerializeField] private bool logTriggers = true;

    // ---- Singleton-ish access for external Trigger() calls ----
    private static AlgaePulse _instance;
    public static AlgaePulse Instance => _instance;

    // Shader global IDs
    private static readonly int ID_Origins = Shader.PropertyToID("_AlgaePulseOrigins");
    private static readonly int ID_Params  = Shader.PropertyToID("_AlgaePulseParams");
    private static readonly int ID_Shape   = Shader.PropertyToID("_AlgaePulseShape");

    // Per-wave state (Vector4 because Shader.SetGlobalVectorArray is the simplest path)
    private readonly Vector4[] _origins = new Vector4[MAX_WAVES]; // xyz = pos, w = start time
    private readonly Vector4[] _params  = new Vector4[MAX_WAVES]; // x = active(0/1), y = end time, z/w unused
    private int _nextSlot;

    void OnEnable()
    {
        if (_instance == null) _instance = this;
        // Push empty arrays so the shader's lookups are well-defined even before any wave fires.
        for (int i = 0; i < MAX_WAVES; i++) { _origins[i] = Vector4.zero; _params[i] = Vector4.zero; }
        PushAll();
    }

    void OnDisable()
    {
        if (_instance == this) _instance = null;
    }

    void Update()
    {
        // Push shape every frame so inspector tweaks are live.
        Shader.SetGlobalVector(ID_Shape, new Vector4(waveSpeed, waveThickness, waveIntensity, 0f));

        // Expire waves whose end-time has passed.
        float now = Time.time;
        bool dirty = false;
        for (int i = 0; i < MAX_WAVES; i++)
        {
            if (_params[i].x > 0.5f && now > _params[i].y)
            {
                _params[i]  = Vector4.zero;
                _origins[i] = Vector4.zero;
                dirty = true;
            }
        }
        if (dirty) PushAll();

        // Debug keyboard trigger (new Input System).
        var kb = Keyboard.current;
        if (debugTriggerKey != Key.None && kb != null && kb[debugTriggerKey].wasPressedThisFrame)
            Trigger(transform.position);
    }

    // -------- Public API --------

    public void Trigger(Vector3 worldPos)
    {
        float startTime = Time.time;
        float endTime   = startTime + waveLifetime;

        int slot = _nextSlot;
        _nextSlot = (_nextSlot + 1) % MAX_WAVES;

        _origins[slot] = new Vector4(worldPos.x, worldPos.y, worldPos.z, startTime);
        _params[slot]  = new Vector4(1f, endTime, 0f, 0f);

        PushAll();

        if (logTriggers)
            Debug.Log($"[AlgaePulse] Trigger at {worldPos} (slot {slot}, ends at {endTime:F2})", this);
    }

    // -------- Auto-trigger hooks --------

    void OnCollisionEnter(Collision col)
    {
        if (!autoTriggerOnCollision) return;
        if ((triggerMask.value & (1 << col.gameObject.layer)) == 0) return;
        Vector3 point = col.contactCount > 0 ? col.GetContact(0).point : col.transform.position;
        Trigger(point);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!autoTriggerOnCollision) return;
        if ((triggerMask.value & (1 << other.gameObject.layer)) == 0) return;
        Trigger(other.ClosestPoint(transform.position));
    }

    // -------- Internals --------

    private void PushAll()
    {
        Shader.SetGlobalVectorArray(ID_Origins, _origins);
        Shader.SetGlobalVectorArray(ID_Params,  _params);
        Shader.SetGlobalVector(ID_Shape, new Vector4(waveSpeed, waveThickness, waveIntensity, 0f));
    }
}
