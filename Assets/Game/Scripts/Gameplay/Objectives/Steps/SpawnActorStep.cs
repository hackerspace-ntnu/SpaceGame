using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// The server puts a character into the world when the step begins — the witch, home when the
    /// crew and the Oathkeepers arrive — and the step lasts <see cref="holdSeconds"/>, long enough for
    /// what she has to say.
    ///
    /// <para>
    /// The character is spawned through the world service, so it replicates and is saved as a
    /// runtime entity under its own prefab id; the step remembers nothing about it. <c>Begun</c> is
    /// saved, so a reload never spawns a second one. The hold restarts after a reload — the clock is
    /// per machine.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Spawn Actor Step")]
    public class SpawnActorStep : ObjectiveStep
    {
        [Tooltip("A registered network prefab carrying a QuestActor.")]
        [SerializeField] private GameObject prefab;

        [Tooltip("The QuestSite id of the place to spawn at.")]
        [SerializeField] private string siteId;

        [Tooltip("World-space offset from the site's centre, metres.")]
        [SerializeField] private Vector3 offset;

        [Tooltip("Yaw the character faces when it appears, degrees.")]
        [SerializeField] private float facingYaw;

        [Tooltip("Absolute height the ground probe starts from, metres. Above anything at the site.")]
        [SerializeField, Min(1f)] private float probeHeight = 400f;

        [Tooltip("Seconds the step lasts after it begins.")]
        [SerializeField, Min(0f)] private float holdSeconds = 12f;

        public override bool TryBegin(ObjectiveWorld world)
        {
            if (prefab == null)
            {
                Debug.LogError($"[Objectives] '{name}' has no prefab to spawn; the step goes on without one.", this);
                return true;
            }

            if (!QuestSite.TryGet(siteId, out Vector3 site, out _)) return false;

            Vector3 at = site + offset;
            if (!ShipGrounding.TryResolveGround(new Vector2(at.x, at.z), probeHeight, out float groundY)) return false;

            GameServices.World.Spawn(prefab, new Vector3(at.x, groundY, at.z), Quaternion.Euler(0f, facingYaw, 0f));
            return true;
        }

        public override bool IsMet(ObjectiveWorld world) => world.StepSeconds >= holdSeconds;
    }
}
