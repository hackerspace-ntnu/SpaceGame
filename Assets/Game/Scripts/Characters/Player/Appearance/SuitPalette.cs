using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// The suit colours a player can choose from, and the rule that turns one choice into the whole
    /// astronaut.
    ///
    /// <para>
    /// This is a static table rather than a ScriptableObject on purpose. What is synced between peers
    /// is an <b>index</b>, and this table is the key that decodes it — two builds holding different
    /// palette assets would make index 3 mean different colours on different machines, and that kind
    /// of disagreement is invisible until someone describes their suit over voice chat. A static
    /// table can neither desync nor be left unassigned in an Inspector. The price is that retuning a
    /// colour is a code edit; this is a small file, and this project already builds its art from
    /// scripts.
    /// </para>
    ///
    /// <para>
    /// Colours here are in <b>gamma (sRGB) space</b>, which is what the offsets in
    /// <see cref="Relationships"/> were measured in and what a hex code means.
    /// <see cref="SuitRecolor"/> converts on upload — see the note there, because
    /// MaterialPropertyBlock does not convert for you the way Material.SetColor does.
    /// </para>
    /// </summary>
    public static class SuitPalette
    {
        /// <summary>
        /// One choice on the cycler.
        /// </summary>
        public readonly struct Swatch
        {
            public readonly string Name;
            public readonly Color Color;

            public Swatch(string name, int rgb)
            {
                Name = name;
                Color = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
            }
        }

        /// <summary>
        /// How one of the astronaut's other hued materials follows the chosen colour.
        ///
        /// Every offset is measured against the harness as it ships today (#E7771C — H 26.9°,
        /// S 0.879, V 0.906), in sRGB HSV. Keeping the relationships rather than flooding every
        /// hued part with the same value is what stops the model collapsing into one block of
        /// colour: the pack panel stays a step lighter, the scarf darker, the trim brighter, and
        /// the pack detail lands on the far side of the wheel, so the astronaut still reads as
        /// two-tone whatever is picked.
        /// </summary>
        public readonly struct Relationship
        {
            /// <summary>The material's name as it arrives from the FBX. Matched by name, not by slot.</summary>
            public readonly string MaterialName;

            /// <summary>Degrees around the wheel from the chosen colour.</summary>
            public readonly float HueOffset;

            public readonly float SaturationScale;
            public readonly float ValueScale;

            public Relationship(string materialName, float hueOffset, float saturationScale,
                float valueScale)
            {
                MaterialName = materialName;
                HueOffset = hueOffset;
                SaturationScale = saturationScale;
                ValueScale = valueScale;
            }
        }

        /// <summary>
        /// The choices, in cycler order.
        ///
        /// Ember is first and is the colour the astronaut has always been, so a player who never
        /// touches the arrows and every astronaut authored before this feature look identical.
        /// </summary>
        public static readonly Swatch[] Swatches =
        {
            new("Ember",       0xE7771C),
            new("Signal Red",  0xFF3B30),
            new("Magenta",     0xFF2FB0),
            new("Violet",      0xB94BFF),
            new("Ultramarine", 0x4A5BFF),

            // Cobalt rather than an azure — #00A2FF sat 0.149 from Cyan, which
            // TheSwatchesAreTellableApart rejects and which in the lobby meant two presses that
            // appeared to do nothing. A deeper blue is distinct from both its neighbours.
            new("Cobalt",      0x0057E7),

            new("Cyan",        0x00C8FF),
            new("Aqua",        0x00D6A5),
            new("Green",       0x2ED84B),
            new("Lime",        0xB5E838),
            new("Sunburst",    0xFFD400),
            new("Tangerine",   0xFF8A00),
            new("Bone",        0xF2F0EA),
            new("Graphite",    0x2A2E35),
        };

        /// <summary>
        /// How many swatches are handed out as a random default.
        ///
        /// The last two — Bone and Graphite — are near-neutral, and an astronaut that arrives
        /// almost uncoloured reads as the feature having failed rather than as a choice. They stay
        /// on the cycler and are only ever reached deliberately.
        /// </summary>
        public const int VividCount = 12;

        /// <summary>
        /// Every material the chosen colour reaches, and how.
        ///
        /// <para>
        /// The three at offset zero are one visual zone that happens to be three material
        /// datablocks: they carry the identical orange today and sit on the harness, vest,
        /// kneepads, straps and buckle. Splitting them would be three controls that must always
        /// agree.
        /// </para>
        ///
        /// <para>
        /// Everything NOT listed here is left alone, and that is most of the model: the suit body,
        /// the helmet, the gloves and boots, the hardware and the tubing are the greys and blacks
        /// the astronaut is mostly made of, and they are what keeps a bright suit from reading as
        /// a jelly bean.
        /// </para>
        /// </summary>
        public static readonly Relationship[] Relationships =
        {
            // Harness, vest, kneepads, straps, buckle — the dominant colour itself.
            new("Material.043", 0f, 1f, 1f),
            new("Material.002", 0f, 1f, 1f),
            new("Material.047", 0f, 1f, 1f),

            new("Material.049",   7.4f, 0.76f, 1.00f),  // pack panel — a step lighter
            new("Material.048", -11.2f, 0.90f, 0.63f),  // scarf and cubes — darker
            new("Material.045",  20.6f, 1.14f, 1.10f),  // scarf trim — brighter
            new("Material.046", 198.7f, 0.62f, 0.79f),  // pack detail — the opposite side
        };

        public static int Count => Swatches.Length;

        /// <summary>Clamps anything — a stale save, a peer on an older build — into the list.</summary>
        public static int Clamp(int index) => Mathf.Clamp(index, 0, Swatches.Length - 1);

        public static Color ColorOf(int index) => Swatches[Clamp(index)].Color;

        public static string NameOf(int index) => Swatches[Clamp(index)].Name;

        /// <summary>Steps the cycler, wrapping at both ends so neither arrow is ever a dead end.</summary>
        public static int Step(int index, int direction)
        {
            int count = Swatches.Length;
            return ((Clamp(index) + direction) % count + count) % count;
        }

        /// <summary>A vivid swatch, for a player who has never chosen one.</summary>
        public static int RandomDefault() => Random.Range(0, VividCount);

        /// <summary>
        /// The colour one material takes when <paramref name="chosen"/> is picked.
        ///
        /// Saturation and value are clamped rather than allowed to overflow, which is why the
        /// brightest swatches lose a little of the trim's intended lift — the alternative is a
        /// wrapped value, and a trim that goes dark when the suit goes bright looks like a bug.
        /// </summary>
        public static Color Derive(Color chosen, in Relationship relationship)
        {
            Color.RGBToHSV(chosen, out float h, out float s, out float v);

            h = Mathf.Repeat(h + relationship.HueOffset / 360f, 1f);
            s = Mathf.Clamp01(s * relationship.SaturationScale);
            v = Mathf.Clamp01(v * relationship.ValueScale);

            return Color.HSVToRGB(h, s, v);
        }

        /// <summary>The colour a material takes for a swatch index, or null when it is not ours.</summary>
        public static bool TryDerive(int swatchIndex, string materialName, out Color color)
        {
            for (int i = 0; i < Relationships.Length; i++)
            {
                if (Relationships[i].MaterialName != materialName) continue;

                color = Derive(ColorOf(swatchIndex), Relationships[i]);
                return true;
            }

            color = default;
            return false;
        }
    }
}
