// A scripted stand-in for a player in the agent benchmark: a targetable Humans body walking a
// circle round the arena, so hostile agents chase, aim and swing at something the whole run.
//
// It cannot be killed. A target that dies stops being a target, and the load would then change
// halfway through the measurement — the second half of the run would be measuring idle agents.
using SpaceGame.Gameplay;
using UnityEngine;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(HealthComponent))]
    public sealed class BenchmarkPlayer : MonoBehaviour, IDamageFilter
    {
        private Vector3 centre;
        private float radius;
        private float angularSpeed;
        private float angle;
        private float standHeight;
        private HealthComponent health;

        /// <summary>
        /// Call before the object is activated. <paramref name="speed"/> is along the path, m/s;
        /// a negative one walks the circle the other way.
        /// </summary>
        public void Configure(Vector3 orbitCentre, float orbitRadius, float speed, float startAngle, float height)
        {
            centre = orbitCentre;
            radius = Mathf.Max(orbitRadius, Mathf.Epsilon);
            angularSpeed = speed / radius;
            angle = startAngle;
            standHeight = height;
            Place();
        }

        private void Awake() => health = GetComponent<HealthComponent>();

        private void OnEnable() => health.AddFilter(this);

        private void OnDisable() => health.RemoveFilter(this);

        private void Update()
        {
            angle += angularSpeed * Time.deltaTime;
            Place();
        }

        private void Place()
        {
            var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            var heading = new Vector3(-offset.z, 0f, offset.x) * Mathf.Sign(angularSpeed);
            transform.SetPositionAndRotation(centre + offset * radius + Vector3.up * standHeight,
                                             Quaternion.LookRotation(heading.sqrMagnitude > 0f ? heading : Vector3.forward));
        }

        public void Filter(HealthComponent victim, ref DamageHit hit) => hit.Amount = 0;
    }
}
