using UnityEngine;

namespace SpaceGame.Gameplay.SpriteHamlet
{
    /// <summary>
    /// The keeper's breath, over its head: one pip per poison ball it can take, filling green as the
    /// balls land. The whole of the fail state, readable at a glance from across the clearing
    /// (GDC-L1-UX-0003) — the player should never lose the fight without having seen it coming.
    ///
    /// <para>
    /// Shown only during the witch fight, on every machine, from the replicated
    /// <see cref="PoisonTally"/>. Built at runtime from one sprite: a row of
    /// <see cref="SpriteRenderer"/>s that turns to face the camera, with a pop when a pip fills.
    /// </para>
    /// </summary>
    public class PoisonPips : MonoBehaviour
    {
        [SerializeField] private Sprite pip;

        [Tooltip("Height of the row above the keeper's feet, metres.")]
        [SerializeField, Min(0f)] private float height = 1.25f;

        [Tooltip("Distance between pips, metres.")]
        [SerializeField, Min(0.01f)] private float spacing = 0.2f;

        [Tooltip("Width of one pip, metres.")]
        [SerializeField, Min(0.01f)] private float size = 0.16f;

        [SerializeField] private Color emptyColour = new(1f, 1f, 1f, 0.55f);
        [SerializeField] private Color fullColour = new(0.45f, 1f, 0.2f, 1f);

        [Tooltip("How much bigger a pip pops when it fills, as a fraction.")]
        [SerializeField, Min(0f)] private float pop = 0.8f;

        [SerializeField, Min(0.01f)] private float popSeconds = 0.35f;

        private PoisonTally tally;
        private KeeperCarry carry;
        private Transform row;
        private SpriteRenderer[] pips = System.Array.Empty<SpriteRenderer>();
        private float[] popLeft = System.Array.Empty<float>();
        private int shownHits = -1;

        private void Awake()
        {
            tally = GetComponent<PoisonTally>();
            carry = GetComponent<KeeperCarry>();
        }

        private void Start()
        {
            if (tally == null || pip == null) return;

            row = new GameObject("PoisonPips").transform;
            row.SetParent(transform, false);
            row.localPosition = Vector3.up * height;

            int count = tally.FallsAt;
            pips = new SpriteRenderer[count];
            popLeft = new float[count];
            float scale = size / Mathf.Max(0.0001f, pip.bounds.size.x);
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Pip" + i);
                go.transform.SetParent(row, false);
                go.transform.localPosition = Vector3.right * ((i - (count - 1) * 0.5f) * spacing);
                go.transform.localScale = Vector3.one * scale;
                pips[i] = go.AddComponent<SpriteRenderer>();
                pips[i].sprite = pip;
            }
        }

        private void LateUpdate()
        {
            if (row == null) return;

            bool show = carry == null || carry.InLiftableStretch;
            if (row.gameObject.activeSelf != show) row.gameObject.SetActive(show);
            if (!show) return;

            int hits = tally.Hits;
            if (shownHits >= 0)
                for (int i = shownHits; i < hits && i < popLeft.Length; i++)
                    popLeft[i] = popSeconds;
            shownHits = hits;

            float scale = size / Mathf.Max(0.0001f, pip.bounds.size.x);
            for (int i = 0; i < pips.Length; i++)
            {
                popLeft[i] = Mathf.Max(0f, popLeft[i] - Time.deltaTime);
                float p = popLeft[i] / popSeconds;
                pips[i].color = i < hits ? fullColour : emptyColour;
                pips[i].transform.localScale = Vector3.one * (scale * (1f + pop * p * p));
            }

            // Over the keeper as this machine draws it — in someone's arms, that is the arms.
            Transform carrier = carry != null && carry.IsCarried ? carry.Carrier : null;
            Vector3 feet = carrier != null ? carry.ArmPoint(carrier) : transform.position;
            row.position = feet + Vector3.up * height;

            Camera view = Camera.main;
            if (view != null) row.rotation = view.transform.rotation;
        }
    }
}
