using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Gameplay.Hazards
{
    /// <summary>
    /// A standing haze of choking spores around a puffball in the witch's forest: a player inside it
    /// loses health steadily until they step out, or burn the puffball (<see cref="BurnAway"/>).
    ///
    /// <para>
    /// Constant rather than puffing, on purpose: a cloud that comes and goes needs its timing on the
    /// wire or it hurts at moments the player cannot see. A haze that is always drawn and always
    /// hurts agrees with itself on every machine with nothing sent. The damage is the server's
    /// (<see cref="NetDamage"/>); the haze is the prefab's own particles.
    /// </para>
    /// </summary>
    public class SporeCloud : MonoBehaviour
    {
        [Tooltip("How far the haze reaches from this object, metres. Match the particles.")]
        [SerializeField, Min(0.5f)] private float radius = 4f;

        [Tooltip("Damage per tick to each player inside.")]
        [SerializeField, Min(1)] private int damagePerTick = 3;

        [Tooltip("Seconds between ticks.")]
        [SerializeField, Min(0.1f)] private float tickSeconds = 1f;

        private float nextTick;

        private void Update()
        {
            if (!Network.Decides || Time.time < nextTick) return;
            nextTick = Time.time + tickSeconds;

            foreach (PlayerIdentity player in PlayerIdentity.All)
            {
                if (player == null) continue;
                if ((player.transform.position - transform.position).sqrMagnitude > radius * radius) continue;

                NetDamage.Apply(player.gameObject, damagePerTick, transform);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.9f, 0.3f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
