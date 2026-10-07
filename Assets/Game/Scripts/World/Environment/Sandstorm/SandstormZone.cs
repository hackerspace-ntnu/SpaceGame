// A storm that lives here.
//
// Drop it in a scene, pick a profile, and that part of the map is permanently dangerous — the
// hazard-region half of the system, as opposed to the weather the director rolls. Same
// StormInstance record underneath, so everything downstream treats the two identically.
//
// It runs in edit mode so the footprint gizmos, and optionally the silhouette itself, are visible
// while you place it. The preview object is HideAndDontSave: it is never written into the scene
// file, so authoring a storm cannot leave junk behind in a commit.
using UnityEngine;

namespace SpaceGame.World.Weather
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class SandstormZone : MonoBehaviour
    {
        [Tooltip("Which storm sits here. The zone's own position is the storm's centre.")]
        [SerializeField] private SandstormProfile profile;

        [Tooltip("Bearing the storm travels toward: 0 is +Z, 90 is +X. Only matters if the " +
                 "profile has a travel speed — a parked storm still uses it to orient a wall and " +
                 "to decide which way the sand streaks.")]
        [SerializeField, Range(0f, 360f)] private float headingDegrees = 45f;

        [Tooltip("Seconds this storm lives. Zero — the default for a placed zone — means forever. " +
                 "Negative uses the profile's own duration.")]
        [SerializeField] private float duration = 0f;

        [Tooltip("Fixes the wander and gust pattern. Any non-zero value makes this zone identical " +
                 "on every run, which is what you want for a hand-tuned set piece.")]
        [SerializeField] private uint seed = 1u;

        [Header("Authoring")]
        [Tooltip("Show the storm's silhouette in the scene view while editing. Needs the same " +
                 "material SandstormVisuals uses. The preview object is never saved.")]
        [SerializeField] private Material previewMaterial;

        [SerializeField] private bool drawGizmos = true;

        private int stormId;
        private SandstormWall preview;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;

            SandstormManager.RecordsRestored += OnRecordsRestored;
            TryRegister();
        }

        private void OnDisable()
        {
            SandstormManager.RecordsRestored -= OnRecordsRestored;

            if (Application.isPlaying && stormId != 0)
            {
                Sandstorms.Despawn(stormId);
                stormId = 0;
            }

            DestroyPreview();
        }

        /// <summary>
        /// A load replaced the storm list, so whatever id this zone was holding is stale.
        ///
        /// Dropping it back to zero re-arms the register-and-adopt retry in <see cref="Update"/>,
        /// which finds this zone's storm in the restored list and takes it back over. Deliberately
        /// not a despawn: the storm that id named is already gone from the list.
        /// </summary>
        private void OnRecordsRestored() => stormId = 0;

        private void Update()
        {
            if (!Application.isPlaying)
            {
                UpdatePreview();
                return;
            }

            // The manager may not exist yet, and in a networked session it cannot accept storms
            // until its NetworkObject has spawned. Retrying beats guessing at execution order.
            if (stormId == 0)
                TryRegister();
        }

        private void TryRegister()
        {
            if (profile == null)
                return;

            SandstormManager manager = SandstormManager.Instance;
            if (manager == null || !manager.HasAuthority)
                return;

            // Adopt before spawning. After a load this zone's storm is already in the restored list
            // with its original StartTime — and therefore its original position, wander phase and
            // gust phase — so spawning a second one would leave two identical storms stacked on the
            // same spot forever, one of them dating from this moment.
            if (manager.TryAdopt(profile, transform.position, seed, out stormId))
                return;

            manager.TrySpawn(profile, transform.position, headingDegrees, out stormId, duration, seed);
        }

        // ── Editor preview ────────────────────────────────────────────────────────

        private void UpdatePreview()
        {
            if (profile == null || previewMaterial == null || !drawGizmos)
            {
                DestroyPreview();
                return;
            }

            // Parented to the zone, and it matters: the preview is HideAndDontSave so it never
            // lands in a scene file, and an object with that flag is NOT destroyed when a scene
            // unloads. Left at the root it would follow the editor into every scene opened
            // afterwards — which is exactly how a sandstorm ends up hanging over the main menu.
            // The wall divides out its parent's scale, so being a child costs nothing.
            if (preview == null)
            {
                preview = SandstormWall.Create("Sandstorm Zone Preview", previewMaterial, hidden: true);
                preview.transform.SetParent(transform, worldPositionStays: true);
            }

            Vector2 center = new Vector2(transform.position.x, transform.position.z);
            StormFootprint footprint = profile.Footprint(center, StormShape.HeadingFromDegrees(headingDegrees));

            // Full intensity and no camera density: the preview shows the storm at its worst,
            // which is what you are trying to judge while placing it.
            preview.Apply(profile, footprint, 1f, 0f);
        }

        private void DestroyPreview()
        {
            if (preview == null)
                return;

            if (Application.isPlaying)
                Destroy(preview.gameObject);
            else
                DestroyImmediate(preview.gameObject);

            preview = null;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || profile == null)
                return;

            Vector3 origin = new Vector3(transform.position.x, profile.baseHeight, transform.position.z);
            Vector2 heading = StormShape.HeadingFromDegrees(headingDegrees);
            var forward = new Vector3(heading.x, 0f, heading.y);
            var across = new Vector3(-heading.y, 0f, heading.x);

            Gizmos.color = new Color(0.95f, 0.72f, 0.35f, 0.9f);
            if (profile.shape == StormShapeKind.Cell)
            {
                DrawCircle(origin, profile.radius);
                Gizmos.color = new Color(0.95f, 0.72f, 0.35f, 0.35f);
                DrawCircle(origin, profile.radius + profile.edgeFeather);
            }
            else
            {
                float halfWidth = profile.lateralExtent > 0f ? profile.lateralExtent : profile.wallDrawHalfWidth;
                DrawSlab(origin, forward, across, profile.radius, halfWidth);
                Gizmos.color = new Color(0.95f, 0.72f, 0.35f, 0.35f);
                DrawSlab(origin, forward, across, profile.radius + profile.edgeFeather, halfWidth);
            }

            // The height is the number hardest to picture from the inspector, so draw it: four
            // uprights and a ring at the ceiling.
            Gizmos.color = new Color(0.95f, 0.72f, 0.35f, 0.6f);
            Vector3 top = origin + Vector3.up * profile.height;
            float span = profile.shape == StormShapeKind.Cell ? profile.radius : profile.radius;
            Gizmos.DrawLine(origin + forward * span, top + forward * span);
            Gizmos.DrawLine(origin - forward * span, top - forward * span);
            Gizmos.DrawLine(origin + across * span, top + across * span);
            Gizmos.DrawLine(origin - across * span, top - across * span);

            Gizmos.color = Color.white;
            Gizmos.DrawLine(origin, origin + forward * (profile.radius + profile.edgeFeather));
        }

        private static void DrawCircle(Vector3 center, float radius)
        {
            const int segments = 48;
            Vector3 previous = center + new Vector3(0f, 0f, radius);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                Vector3 next = center + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }

        private static void DrawSlab(Vector3 center, Vector3 forward, Vector3 across, float halfThickness, float halfWidth)
        {
            Vector3 a = center + forward * halfThickness + across * halfWidth;
            Vector3 b = center + forward * halfThickness - across * halfWidth;
            Vector3 c = center - forward * halfThickness - across * halfWidth;
            Vector3 d = center - forward * halfThickness + across * halfWidth;

            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }
    }
}
