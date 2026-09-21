// Draws a ranged Vector2 / Vector2Int as two named boxes instead of X and Y.
//
// See RangeLabelsAttribute for why. On a field the attribute cannot describe — someone puts it on a
// float — this falls through to the ordinary drawer rather than drawing something wrong or nothing
// at all.
using UnityEditor;
using UnityEngine;
using SpaceGame.World.Towns;

namespace SpaceGame.EditorTools
{
    [CustomPropertyDrawer(typeof(RangeLabelsAttribute))]
    public class RangeLabelsDrawer : PropertyDrawer
    {
        private const float Gap = 6f;
        private const float MaxSubLabel = 64f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            bool isInt = property.propertyType == SerializedPropertyType.Vector2Int;

            if (!isInt && property.propertyType != SerializedPropertyType.Vector2)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var labels = (RangeLabelsAttribute)attribute;

            label = EditorGUI.BeginProperty(position, label, property);
            Rect row = EditorGUI.PrefixLabel(position, label);

            // The prefix label has already been drawn, so the two boxes must not be indented again.
            int indent = EditorGUI.indentLevel;
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUI.indentLevel = 0;

            float half = (row.width - Gap) * 0.5f;
            var low = new Rect(row.x, row.y, half, row.height);
            var high = new Rect(row.x + half + Gap, row.y, half, row.height);
            EditorGUIUtility.labelWidth = Mathf.Min(MaxSubLabel, half * 0.6f);

            EditorGUI.BeginChangeCheck();

            if (isInt)
            {
                Vector2Int value = property.vector2IntValue;
                int x = EditorGUI.IntField(low, new GUIContent(labels.Low), value.x);
                int y = EditorGUI.IntField(high, new GUIContent(labels.High), value.y);
                if (EditorGUI.EndChangeCheck()) property.vector2IntValue = new Vector2Int(x, y);
            }
            else
            {
                Vector2 value = property.vector2Value;
                float x = EditorGUI.FloatField(low, new GUIContent(labels.Low), value.x);
                float y = EditorGUI.FloatField(high, new GUIContent(labels.High), value.y);
                if (EditorGUI.EndChangeCheck()) property.vector2Value = new Vector2(x, y);
            }

            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
}
