using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The eight looks the drifters shipped with, as seed assets.
    ///
    /// <para>
    /// These exist so the style folder is never empty and so there is something to duplicate rather
    /// than a blank asset to fill in from nothing. They are <b>starting points, not the truth</b>:
    /// once an asset exists, this file has no say over it, and <see cref="CreateMissing"/> will not
    /// touch it again. Tuning a drifter's eyes means editing the asset, never editing this.
    /// </para>
    ///
    /// <para>
    /// The numbers reproduce what was baked on 2026-09-22, so seeding a fresh project and baking
    /// gives back the eyes that are in the game rather than something slightly different.
    /// </para>
    /// </summary>
    public static class EyeStylePresets
    {
        /// <summary>
        /// Writes any built-in style that has no asset yet, and leaves every existing one alone.
        /// </summary>
        /// <returns>How many were created.</returns>
        public static int CreateMissing()
        {
            string folder = StylizedEyeBuilder.SeedFolder;
            EnsureFolder(folder);

            var existing = StylizedEyeBuilder.LoadStyles();
            int made = 0;

            foreach (var seed in Seeds())
            {
                if (Array.Exists(existing, s => s != null && s.name == seed.name))
                {
                    UnityEngine.Object.DestroyImmediate(seed);
                    continue;
                }

                AssetDatabase.CreateAsset(seed, $"{folder}/{seed.name}.asset");
                made++;
            }

            if (made > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return made;
        }

        /// <summary>
        /// The shared starting point. The angles are sized against the visible cap of the eyeball:
        /// the lids crop it to roughly 50 deg of the sphere, so a 45 deg iris fills what is on show.
        /// The pupil is deliberately huge -- 25 deg of that 45 -- which is what makes these read as
        /// cartoon eyes rather than an eyeball with a dot on it.
        /// </summary>
        private static EyeStyle Base(string name)
        {
            var style = ScriptableObject.CreateInstance<EyeStyle>();
            style.name = name;
            style.Sclera = Hex("120D0A");
            style.Pupil = Hex("07060A");
            style.LimbalRing = Hex("14090B");
            style.Iris = EyeShape.Round(45f);
            style.PupilShape = EyeShape.Round(25f);
            style.LimbalWidth = 6f;
            style.EdgeSoftness = 1.4f;
            style.Catchlights = EyeStyle.DefaultCatchlights();
            style.Emission = Color.black;
            style.EmissionStrength = 0f;
            style.EmissionArea = EyeEmissionArea.Iris;
            style.Smoothness = 0.55f;
            return style;
        }

        /// <summary>A bright iris on a near-black eyeball -- the alien read: all iris, no white.</summary>
        private static EyeStyle Glowing(string name, string inner, string outer, string rim,
                                        string glow, float strength)
        {
            var style = Base(name);
            style.IrisInner = Hex(inner);
            style.IrisOuter = Hex(outer);
            style.LimbalRing = Hex(rim);
            style.Emission = Hex(glow);
            style.EmissionStrength = strength;
            return style;
        }

        private static EyeStyle[] Seeds()
        {
            var amber = Glowing("Amber", "FFA23A", "C4480A", "2B1204", "FF7A14", 0.55f);
            var ember = Glowing("Ember", "FF6A3C", "94180A", "2A0805", "FF3A12", 0.6f);
            var acid = Glowing("Acid", "B6FF5E", "2F7A14", "0E2006", "7CE01E", 0.5f);

            var glacier = Glowing("Glacier", "9FF0FF", "16679E", "061C2C", "3FC6FF", 0.5f);
            glacier.Sclera = Hex("0B1016");

            var violet = Glowing("Violet", "D49BFF", "51219A", "170728", "9A46FF", 0.5f);
            violet.Sclera = Hex("100A18");

            var gold = Glowing("Gold", "FFDC6A", "A86E06", "2A1A02", "FFB61E", 0.45f);

            // "White eyes", reading one: a white eyeball, the nearest thing here to a human eye.
            // The only style with a pale sclera, which is why its iris is a washed grey rather than
            // the white it started as -- white on white left nothing but the limbal ring visible and
            // the eye read as an empty hoop. It also pulls back from the huge pupil the rest wear.
            var ivory = Base("Ivory");
            ivory.Sclera = Hex("F1ECE2");
            ivory.IrisInner = Hex("DCE7EC");
            ivory.IrisOuter = Hex("8FA5B2");
            ivory.LimbalRing = Hex("3C4750");
            ivory.LimbalWidth = 4.5f;
            ivory.Iris = EyeShape.Round(38f);
            ivory.PupilShape = EyeShape.Round(22f);

            // "White eyes", reading two: no pupil at all, the whole eye lit blank. The pupil is
            // switched off by size rather than painted the same white as the iris, which is the
            // same picture and says what is meant.
            var blank = Base("Blank");
            blank.Sclera = Hex("E6E6E0");
            blank.IrisInner = Hex("EDEDE8");
            blank.IrisOuter = Hex("EDEDE8");
            blank.Pupil = Hex("EDEDE8");
            blank.LimbalRing = Hex("C9C9C2");
            blank.LimbalWidth = 4f;
            blank.Iris = EyeShape.Round(46f);
            blank.PupilShape = EyeShape.Round(0f);
            blank.EdgeSoftness = 6f;
            blank.Emission = Hex("FFFFFF");
            blank.EmissionStrength = 0.3f;

            return new[] { amber, ember, acid, glacier, violet, gold, ivory, blank };
        }

        private static Color Hex(string rgb)
        {
            int packed = int.Parse(rgb, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Color(((packed >> 16) & 0xFF) / 255f,
                             ((packed >> 8) & 0xFF) / 255f,
                             (packed & 0xFF) / 255f, 1f);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
