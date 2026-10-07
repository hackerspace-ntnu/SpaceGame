using UnityEngine;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// CRT snow on the terminal's glass: what every page but the hull drawing shows until the lander has
    /// a working long-range transmitter (<see cref="TerminalScreen"/>).
    ///
    /// <para>
    /// One small noise texture, made once and never rewritten; the snow moves because the image samples a
    /// different random window of it a couple of dozen times a second, and a brighter band rolls down
    /// the glass the way a tube without a signal drifts. No render texture and no per-frame upload, so a
    /// terminal nobody is looking at costs a few float writes.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public sealed class TerminalStatic : MonoBehaviour
    {
        [Tooltip("Texels across the noise texture. Only a window of it is drawn at once (below), so the " +
                 "snow has resolution x window specks across the glass.")]
        [SerializeField, Min(8)] private int resolution = 256;

        [Tooltip("Fraction of the texture's width one screen-width of snow samples. Smaller = coarser grain.")]
        [SerializeField, Range(0.05f, 1f)] private float window = 0.25f;

        [Tooltip("New snow per second.")]
        [SerializeField, Min(1f)] private float framesPerSecond = 24f;

        [Tooltip("Brightest a speck of snow gets, as alpha over the dark glass.")]
        [SerializeField, Range(0f, 1f)] private float grainAlpha = 0.55f;

        [Tooltip("A brighter band that rolls down the glass, and how long one pass takes. Optional.")]
        [SerializeField] private RectTransform rollBand;
        [SerializeField, Min(0.1f)] private float rollSeconds = 3.5f;

        private RawImage image;
        private Texture2D noise;
        private float nextFrame;

        private void OnEnable()
        {
            image = GetComponent<RawImage>();
            if (noise == null) noise = MakeNoise(resolution, grainAlpha);
            image.texture = noise;
            nextFrame = 0f;
        }

        private void OnDestroy()
        {
            if (noise != null) Destroy(noise);
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now >= nextFrame)
            {
                nextFrame = now + 1f / framesPerSecond;
                image.uvRect = new Rect(Random.value, Random.value, window, window * Aspect());
            }

            Roll(now);
        }

        /// <summary>The window's height over its width, so the grain is square on a non-square page.</summary>
        private float Aspect()
        {
            Rect rect = ((RectTransform)transform).rect;
            return rect.width > 0f ? rect.height / rect.width : 1f;
        }

        private void Roll(float now)
        {
            if (rollBand == null) return;

            Rect rect = ((RectTransform)transform).rect;
            float t = Mathf.Repeat(now / rollSeconds, 1f);
            rollBand.anchoredPosition = new Vector2(0f, Mathf.Lerp(rect.height, -rect.height, t) * 0.5f);
        }

        /// <summary>Grey specks of random brightness on clear, point-sampled and tiling.</summary>
        private static Texture2D MakeNoise(int size, float alpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "TerminalStatic",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontSave,
            };

            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                byte v = (byte)Random.Range(0, 256);
                pixels[i] = new Color32(v, v, v, (byte)(v * alpha));
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture;
        }
    }
}
