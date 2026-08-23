// Reads a prefab's stamped identity out of its FILE.
//
// Everything else asks Unity what a prefab looks like, and that is the one question which cannot see
// the failure this exists for: SaveableEntity.OnValidate is inside `#if UNITY_EDITOR` and fills
// prefabId in memory the moment an asset is loaded, so the component always agrees with the GUID
// while the serialized bytes stay empty. A build ships the bytes.
//
// It lives here rather than being copied into the wiring tool, the validator and the tests because
// the first version WAS copied into all three, and all three shared the same blind spot: they only
// understood one of the two ways Unity serializes this field.
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SpaceGame.Core.Persistence.EditorTools
{
    public static class SaveablePrefabFile
    {
        /// <summary>
        /// A prefab VARIANT stores the field as an override on the base's component, not as a field
        /// of its own — <c>propertyPath: prefabId</c> followed by <c>value:</c>, with the
        /// <c>target:</c> line naming the base prefab. A reader that only knows the plain form
        /// reports every variant as unstamped, which is a false alarm on exactly the prefabs most
        /// likely to be correct.
        /// </summary>
        private static readonly Regex OverrideForm = new(
            @"propertyPath: prefabId\s*\n\s*value: (?<value>[0-9a-f]*)",
            RegexOptions.Compiled);

        /// <summary>
        /// The <c>prefabId</c> this prefab actually ships with, or empty when it ships without one.
        ///
        /// Handles both serialized forms. Empty covers "the field is blank", "there is no such
        /// field" and "the file could not be read" — three different causes with one consequence, so
        /// one answer.
        /// </summary>
        public static string ReadPrefabId(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return string.Empty;

            string text;
            try { text = File.ReadAllText(assetPath); }
            catch (IOException) { return string.Empty; }

            // The plain form first: a prefab that owns its SaveableEntity outright.
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.TrimStart();
                if (!trimmed.StartsWith("prefabId:", StringComparison.Ordinal)) continue;

                string value = trimmed.Substring("prefabId:".Length).Trim();
                if (!string.IsNullOrEmpty(value)) return value;
            }

            Match match = OverrideForm.Match(text);
            return match.Success ? match.Groups["value"].Value : string.Empty;
        }

        /// <summary>Whether this prefab's file names the prefab itself, which is the only correct answer.</summary>
        public static bool IsStampedCorrectly(string assetPath, string assetGuid) =>
            !string.IsNullOrEmpty(assetGuid) && ReadPrefabId(assetPath) == assetGuid;
    }
}
