using UnityEngine;
using UnityEngine.Rendering;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A column of light standing over whatever the current objective has left lying in the open —
    /// the module from the wreck, the artifact thrown clear — so it can be picked out from the
    /// ramp a hundred metres away.
    ///
    /// <para>
    /// The environmental half of the guidance (<c>GDC-L1-LEVEL-0001</c>): the visor's waypoint
    /// says where, the column is something in the world to walk toward. A <b>placeholder</b> — a
    /// stretched cylinder in an unlit, additive material — until the art pass gives salvage a
    /// proper smoke plume or distress flare.
    /// </para>
    /// <para>
    /// Per machine, drawn from <see cref="ObjectiveStep.TryGetBeacon"/> every frame. The item it
    /// marks replicates on its own, so the column follows it with nothing added to the wire, and
    /// goes out the moment somebody picks it up.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ObjectiveBeacon : MonoBehaviour
    {
        [Tooltip("Unlit, transparent. Its base colour is what the column glows.")]
        [SerializeField] private Material material;

        [Tooltip("Height of the column above the thing it marks, metres. Tall enough to clear a dune.")]
        [SerializeField, Min(1f)] private float height = 60f;

        [Tooltip("Diameter of the column, metres.")]
        [SerializeField, Min(0.05f)] private float width = 0.8f;

        [Tooltip("Seconds per slow pulse. Motion is what the eye catches first at a distance.")]
        [SerializeField, Min(0.1f)] private float pulseSeconds = 1.8f;

        [Tooltip("How far the pulse dims the column, as a share of its colour's alpha.")]
        [SerializeField, Range(0f, 1f)] private float pulseDepth = 0.35f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Transform column;
        private Renderer columnRenderer;
        private MaterialPropertyBlock block;
        private Color baseColour;

        private void Awake()
        {
            if (material == null)
            {
                Debug.LogError("[Objectives] ObjectiveBeacon has no material, so no beacon will be drawn.", this);
                enabled = false;
                return;
            }

            GameObject shape = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shape.name = "BeaconColumn";

            // Light, not a thing: nothing may stand on it, aim at it or be shadowed by it.
            Destroy(shape.GetComponent<Collider>());

            column = shape.transform;
            column.SetParent(transform, false);

            // The primitive is two units tall, so its Y scale is half the height wanted.
            column.localScale = new Vector3(width, height * 0.5f, width);

            columnRenderer = shape.GetComponent<Renderer>();
            columnRenderer.sharedMaterial = material;
            columnRenderer.shadowCastingMode = ShadowCastingMode.Off;
            columnRenderer.receiveShadows = false;

            baseColour = material.GetColor(BaseColorId);
            block = new MaterialPropertyBlock();
            shape.SetActive(false);
        }

        private void LateUpdate()
        {
            ObjectiveDirector director = ObjectiveDirector.Instance;
            ObjectiveStep step = director != null ? director.Current : null;

            Vector3 at = default;
            bool shown = step != null && step.TryGetBeacon(director.World, out at);

            if (column.gameObject.activeSelf != shown) column.gameObject.SetActive(shown);
            if (shown) Place(at);
        }

        private void Place(Vector3 at)
        {
            column.position = at + Vector3.up * (height * 0.5f);

            float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * (2f * Mathf.PI / pulseSeconds));
            Color colour = baseColour;
            colour.a *= 1f - pulseDepth * wave;

            block.SetColor(BaseColorId, colour);
            columnRenderer.SetPropertyBlock(block);
        }
    }
}
