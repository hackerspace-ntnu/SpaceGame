using UnityEngine;
using SpaceGame.Audio;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A ripped cable end that shorts out now and then: a burst of sparks from its emitter and, most times, an electric
    /// crackle. On top of the emitter's own trickle, so the end is never quite still. Pure presentation: each machine rolls its
    /// own bursts, nothing is sent, nothing is saved; it runs only while its object is active.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SparkingCable : MonoBehaviour
    {
        [Tooltip("The sparks thrown from the cable end. Its own emission keeps trickling; bursts are emitted on top.")]
        [SerializeField] private ParticleSystem sparks;

        [Tooltip("Seconds between shorts, random in this range.")]
        [SerializeField] private Vector2 interval = new(0.7f, 3f);

        [Tooltip("Sparks thrown by one short, random in this range.")]
        [SerializeField] private Vector2Int burst = new(8, 22);

        [Tooltip("How often a short also crackles, 0-1. Not every time, or it reads as a loop.")]
        [SerializeField, Range(0f, 1f)] private float crackleChance = 0.65f;

        [Tooltip("The crackle. There is no dedicated event in the catalog (the FMOD project is lost); the ball-lightning arc is the closest.")]
        [SerializeField] private SfxId crackle = SfxId.WeaponBallLightningArc;

        private float nextShort;

        private void OnEnable()
        {
            nextShort = Time.time + Random.Range(0f, interval.y);
            if (sparks != null && !sparks.isPlaying) sparks.Play();
        }

        private void Update()
        {
            if (Time.time < nextShort) return;
            nextShort = Time.time + Random.Range(interval.x, Mathf.Max(interval.x, interval.y));

            if (sparks != null) sparks.Emit(Random.Range(burst.x, Mathf.Max(burst.x, burst.y) + 1));
            if (Random.value < crackleChance) Sfx.Play(crackle, transform.position, GetInstanceID());
        }
    }
}
