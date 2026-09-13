using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Writes private <c>[SerializeField]</c> fields from a builder script.
    ///
    /// <para>
    /// Those fields are not reachable from an editor script any other way, and making them public
    /// purely so a builder could set them would widen the runtime API for a build-time
    /// convenience. So every prefab builder in this project reaches them through a
    /// <see cref="SerializedObject"/> — and, before this file existed, every one of them carried
    /// its own private copy of the same six one-line setters. Fourteen copies of
    /// <c>if (p != null) p.floatValue = value;</c> is not fourteen decisions.
    /// </para>
    /// <para>
    /// The one piece of behaviour worth sharing rather than re-typing is the failure mode: a field
    /// that has been renamed since the builder was written must <b>warn</b>, not silently do
    /// nothing. A builder whose reference quietly went unset produces a prefab that inspects almost
    /// correctly and is broken in play, which is the most expensive way for this to go wrong.
    /// </para>
    /// </summary>
    public static class SerializedFields
    {
        public static void Set(SerializedObject so, string name, Object value)
        {
            SerializedProperty p = Find(so, name);
            if (p != null) p.objectReferenceValue = value;
        }

        public static void SetFloat(SerializedObject so, string name, float value)
        {
            SerializedProperty p = Find(so, name);
            if (p != null) p.floatValue = value;
        }

        /// <summary>Also the way a <c>LayerMask</c> is written — it serializes as an int.</summary>
        public static void SetInt(SerializedObject so, string name, int value)
        {
            SerializedProperty p = Find(so, name);
            if (p != null) p.intValue = value;
        }

        public static void SetBool(SerializedObject so, string name, bool value)
        {
            SerializedProperty p = Find(so, name);
            if (p != null) p.boolValue = value;
        }

        public static void SetString(SerializedObject so, string name, string value)
        {
            SerializedProperty p = Find(so, name);
            if (p != null) p.stringValue = value;
        }

        public static void SetVector3(SerializedObject so, string name, Vector3 value)
        {
            SerializedProperty p = Find(so, name);
            if (p != null) p.vector3Value = value;
        }

        /// <summary>
        /// An enum by NAME. Enums here carry explicit numbers (<c>SfxId</c> is a numbered catalogue),
        /// so writing an index would silently pick a different entry the day one is inserted.
        /// </summary>
        public static void SetEnumByName(SerializedObject so, string name, string entry)
        {
            SerializedProperty p = Find(so, name);
            if (p == null) return;

            int index = System.Array.IndexOf(p.enumNames, entry);
            if (index < 0)
            {
                Debug.LogWarning($"[Build] '{entry}' is not a value of {name}.");
                return;
            }

            p.enumValueIndex = index;
        }

        private static SerializedProperty Find(SerializedObject so, string name)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null)
                Debug.LogWarning($"[Build] {so.targetObject.GetType().Name} has no serialized " +
                                 $"field '{name}' — it was renamed; this value is unset.");
            return p;
        }
    }
}
