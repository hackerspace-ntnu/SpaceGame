// The sun shrine's vault door. Opened once, by the server, when the seal has been held in sunlight
// long enough — and never shut again. A one-way NetLatch: the same state, wire and late-joiner path
// as a one-shot lever, so every machine sees the same open door and a load puts it back open.
using System.Collections;
using SpaceGame.Audio;
using SpaceGame.Persistence;
using UnityEngine;

namespace SpaceGame.Gameplay.Puzzles
{
    /// <summary>
    /// <see cref="IPersistentEntity"/> because a stone door qualifies for saving by nothing else, and
    /// a vault that seals itself again on load locks away whatever the players had not yet carried out.
    /// </summary>
    public class SunSeal : MonoBehaviour, ILatchHost, IPersistentEntity
    {
        [Tooltip("The door slab. It sinks straight down into the floor when the seal opens.")]
        [SerializeField] private Transform door;

        [Tooltip("How far the door sinks, metres. Its full height, so the doorway is clear.")]
        [SerializeField, Min(0f)] private float sinkDepth = 3.4f;

        [SerializeField, Min(0.1f)] private float openSeconds = 4f;

        [SerializeField] private SfxId openSound = SfxId.InteractDoorOpen;

        private NetLatch latch;
        private Vector3 shut;

        public bool IsOpen => latch != null && latch.IsOn;

        public int LatchCount => 1;

        private void Awake()
        {
            // In Awake and not Start: OnEnable subscribes the latch, and a joiner's answer can
            // arrive before Start would have captured the shut pose.
            shut = door.localPosition;
            latch = new NetLatch(this, Apply, oneWay: true);
        }

        private void OnEnable() => latch?.Enable();

        private void OnDisable() => latch?.Disable();

        /// <summary>Server only: the seal has been lit long enough. Asks; the latch decides.</summary>
        public void Open() => latch?.Request(true);

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreOpen(bool open) => latch?.Restore(open);

        private void Apply(bool open, bool instant)
        {
            Vector3 target = open ? shut + Vector3.down * sinkDepth : shut;
            if (instant || !isActiveAndEnabled)
            {
                door.localPosition = target;
                return;
            }

            Sfx.Play(openSound, door.position, GetInstanceID());
            StartCoroutine(Sink(door.localPosition, target));
        }

        private IEnumerator Sink(Vector3 from, Vector3 to)
        {
            for (float t = 0f; t < openSeconds; t += Time.deltaTime)
            {
                door.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / openSeconds));
                yield return null;
            }
            door.localPosition = to;
        }
    }
}
