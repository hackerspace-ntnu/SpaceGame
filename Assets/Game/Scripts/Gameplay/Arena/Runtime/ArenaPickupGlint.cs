using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using SpaceGame.Items;

namespace SpaceGame.Gameplay.Arena
{
    /// <summary>
    /// Every few seconds, a slash of light flashes across every weapon lying loose in the arena, so
    /// a player can spot one from across the map before anyone picks it up (GDC-L1-LEVEL-0001 —
    /// pull the eye with light and contrast, not with a HUD marker).
    ///
    /// <para>
    /// Presentation only, on every machine. It asks the scene for the spawned
    /// <see cref="PickupableItem"/>s once per flash: a weapon a pad put down, one a dead player
    /// dropped — anything on the floor. A picked-up weapon is despawned, so it simply stops being
    /// found. Nothing here is networked or saved; every peer runs the same clock and the flashes
    /// need not line up between screens.
    /// </para>
    ///
    /// <para>
    /// The look is <c>PickupGlint.shader</c>'s: a camera-facing quad per item whose
    /// <c>_Progress</c> this component advances from 0 to 1 over <see cref="flashSeconds"/>.
    /// </para>
    /// </summary>
    public class ArenaPickupGlint : MonoBehaviour
    {
        [Tooltip("A material using SpaceGame/Effects/PickupGlint.")]
        [SerializeField] private Material glintMaterial;

        [Tooltip("Seconds from the start of one flash to the start of the next.")]
        [SerializeField, Min(0.1f)] private float intervalSeconds = 3f;

        [Tooltip("How long one flash lasts, sweep and fade together.")]
        [SerializeField, Min(0.05f)] private float flashSeconds = 0.55f;

        [Tooltip("Size of the slash relative to the item's own size. 1 spans the item exactly.")]
        [SerializeField, Min(0.1f)] private float sizeToItem = 1.6f;

        [Tooltip("The smallest the slash is drawn, metres — so a small item still gets a flash " +
                 "that can be seen.")]
        [SerializeField, Min(0.05f)] private float minimumSize = 0.9f;

        [Tooltip("The largest the slash is drawn, metres, whatever the item measures.")]
        [SerializeField, Min(0.05f)] private float maximumSize = 3f;

        [Tooltip("How far each flash's slash may be turned from the last, radians, so consecutive " +
                 "flashes do not stamp the same shape.")]
        [SerializeField, Range(0f, Mathf.PI)] private float spinVariation = 0.5f;

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int SpinId = Shader.PropertyToID("_Spin");

        /// <summary>One slash, over one item, for the length of one flash.</summary>
        private sealed class Flash
        {
            public Transform Quad;
            public MeshRenderer Renderer;
            public Transform Target;
            public Vector3 LocalCentre;
            public float Spin;
        }

        private readonly List<Flash> pool = new();
        private readonly List<Flash> showing = new();
        private MaterialPropertyBlock block;
        private Mesh quad;
        private float flashStartedAt = float.NegativeInfinity;
        private float nextFlashAt;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            if (glintMaterial == null)
            {
                Debug.LogError($"[Arena] '{name}' has no glint material, so weapons on the ground " +
                               "never flash.", this);
                enabled = false;
            }
        }

        private void OnDisable()
        {
            foreach (Flash flash in showing) flash.Quad.gameObject.SetActive(false);
            pool.AddRange(showing);
            showing.Clear();
        }

        private void Update()
        {
            if (Time.time >= nextFlashAt)
            {
                nextFlashAt = Time.time + intervalSeconds;
                StartFlash();
            }

            if (showing.Count > 0) AdvanceFlash();
        }

        private void StartFlash()
        {
            flashStartedAt = Time.time;

            foreach (PickupableItem pickup in FindObjectsByType<PickupableItem>(FindObjectsSortMode.None))
            {
                if (!pickup.IsSpawned) continue;
                if (!TryMeasure(pickup.transform, out Vector3 centre, out float size)) continue;

                Flash flash = Take();
                flash.Target = pickup.transform;
                flash.LocalCentre = pickup.transform.InverseTransformPoint(centre);
                flash.Spin = Random.Range(-spinVariation, spinVariation);
                flash.Quad.localScale = Vector3.one * size;
                flash.Quad.gameObject.SetActive(true);
                showing.Add(flash);
            }
        }

        private void AdvanceFlash()
        {
            float progress = (Time.time - flashStartedAt) / flashSeconds;

            for (int i = showing.Count - 1; i >= 0; i--)
            {
                Flash flash = showing[i];

                // Done, or picked up mid-flash: a despawned pickup is a destroyed transform.
                if (progress >= 1f || flash.Target == null)
                {
                    flash.Quad.gameObject.SetActive(false);
                    flash.Target = null;
                    showing.RemoveAt(i);
                    pool.Add(flash);
                    continue;
                }

                // Followed every frame: a weapon just put down is still settling onto the ground.
                flash.Quad.position = flash.Target.TransformPoint(flash.LocalCentre);

                block.SetFloat(ProgressId, progress);
                block.SetFloat(SpinId, flash.Spin);
                flash.Renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>The centre of what can be seen of an item, and how big its slash should be.</summary>
        private bool TryMeasure(Transform item, out Vector3 centre, out float size)
        {
            centre = item.position;
            size = 0f;

            bool any = false;
            Bounds bounds = default;
            foreach (Renderer r in item.GetComponentsInChildren<Renderer>())
            {
                // The item's own meshes only. A weapon's trail, line or particle renderer can sit
                // idle at the world origin, and one of those in the sum stretches the bounds
                // across the map and puts the slash halfway to nowhere.
                if (!r.enabled || r is not (MeshRenderer or SkinnedMeshRenderer)) continue;
                if (any) bounds.Encapsulate(r.bounds);
                else { bounds = r.bounds; any = true; }
            }

            if (!any) return false;

            Vector3 extents = bounds.extents;
            centre = bounds.center;
            size = Mathf.Clamp(2f * Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z)) * sizeToItem,
                               minimumSize, maximumSize);
            return true;
        }

        private Flash Take()
        {
            if (pool.Count > 0)
            {
                Flash reused = pool[^1];
                pool.RemoveAt(pool.Count - 1);
                return reused;
            }

            var go = new GameObject("PickupGlint");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = quad;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = glintMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // The shader turns the quad to face the camera, which the flat mesh's own bounds know
            // nothing about; a cube the quad's size keeps it from being culled side-on.
            renderer.localBounds = new Bounds(Vector3.zero, Vector3.one);

            return new Flash { Quad = go.transform, Renderer = renderer };
        }
    }
}
