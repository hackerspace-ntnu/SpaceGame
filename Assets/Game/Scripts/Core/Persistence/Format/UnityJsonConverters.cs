using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SpaceGame.Persistence
{
    /// <summary>
    /// Newtonsoft converters for the handful of Unity structs that appear in save data.
    ///
    /// These are not a convenience. Newtonsoft serializes public properties, and Vector3 exposes
    /// <c>normalized</c> — itself a Vector3, which exposes <c>normalized</c> — so the default
    /// contract walks that chain until the stack ends. Quaternion has <c>eulerAngles</c> and
    /// <c>normalized</c> with the same problem. Every Unity struct that reaches JSON needs an
    /// explicit converter, and each writes only its real fields.
    ///
    /// Reading is deliberately tolerant: a missing component reads as zero rather than throwing,
    /// so a payload written by an older build that stored a Vector2 where a Vector3 now sits still
    /// loads.
    /// </summary>
    public class Vector2Converter : JsonConverter<Vector2>
    {
        public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x"); writer.WriteValue(value.x);
            writer.WritePropertyName("y"); writer.WriteValue(value.y);
            writer.WriteEndObject();
        }

        public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue,
                                         bool hasExistingValue, JsonSerializer serializer)
        {
            JObject o = JObject.Load(reader);
            return new Vector2(JsonNumbers.Read(o, "x"), JsonNumbers.Read(o, "y"));
        }
    }

    public class Vector3Converter : JsonConverter<Vector3>
    {
        public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x"); writer.WriteValue(value.x);
            writer.WritePropertyName("y"); writer.WriteValue(value.y);
            writer.WritePropertyName("z"); writer.WriteValue(value.z);
            writer.WriteEndObject();
        }

        public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue,
                                         bool hasExistingValue, JsonSerializer serializer)
        {
            JObject o = JObject.Load(reader);
            return new Vector3(JsonNumbers.Read(o, "x"), JsonNumbers.Read(o, "y"), JsonNumbers.Read(o, "z"));
        }
    }

    public class Vector2IntConverter : JsonConverter<Vector2Int>
    {
        public override void WriteJson(JsonWriter writer, Vector2Int value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x"); writer.WriteValue(value.x);
            writer.WritePropertyName("y"); writer.WriteValue(value.y);
            writer.WriteEndObject();
        }

        public override Vector2Int ReadJson(JsonReader reader, Type objectType, Vector2Int existingValue,
                                            bool hasExistingValue, JsonSerializer serializer)
        {
            JObject o = JObject.Load(reader);
            return new Vector2Int(
                Mathf.RoundToInt(JsonNumbers.Read(o, "x")),
                Mathf.RoundToInt(JsonNumbers.Read(o, "y")));
        }
    }

    public class QuaternionConverter : JsonConverter<Quaternion>
    {
        public override void WriteJson(JsonWriter writer, Quaternion value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x"); writer.WriteValue(value.x);
            writer.WritePropertyName("y"); writer.WriteValue(value.y);
            writer.WritePropertyName("z"); writer.WriteValue(value.z);
            writer.WritePropertyName("w"); writer.WriteValue(value.w);
            writer.WriteEndObject();
        }

        public override Quaternion ReadJson(JsonReader reader, Type objectType, Quaternion existingValue,
                                            bool hasExistingValue, JsonSerializer serializer)
        {
            JObject o = JObject.Load(reader);

            var q = new Quaternion(
                JsonNumbers.Read(o, "x"), JsonNumbers.Read(o, "y"),
                JsonNumbers.Read(o, "z"), JsonNumbers.Read(o, "w"));

            // An all-zero quaternion is not a rotation — it is what a truncated or pre-rotation
            // payload reads as, and handing it to Transform.rotation makes the object vanish from
            // the renderer. Identity is the only safe reading of "no rotation was stored".
            return q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f ? Quaternion.identity : q;
        }
    }

    public class ColorConverter : JsonConverter<Color>
    {
        public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("r"); writer.WriteValue(value.r);
            writer.WritePropertyName("g"); writer.WriteValue(value.g);
            writer.WritePropertyName("b"); writer.WriteValue(value.b);
            writer.WritePropertyName("a"); writer.WriteValue(value.a);
            writer.WriteEndObject();
        }

        public override Color ReadJson(JsonReader reader, Type objectType, Color existingValue,
                                       bool hasExistingValue, JsonSerializer serializer)
        {
            JObject o = JObject.Load(reader);
            return new Color(
                JsonNumbers.Read(o, "r"), JsonNumbers.Read(o, "g"),
                JsonNumbers.Read(o, "b"), JsonNumbers.Read(o, "a", 1f));
        }
    }

    internal static class JsonNumbers
    {
        /// <summary>A component's value, or <paramref name="fallback"/> when it is absent or not a number.</summary>
        public static float Read(JObject o, string name, float fallback = 0f)
        {
            JToken token = o?[name];
            if (token == null || token.Type == JTokenType.Null) return fallback;

            return token.Type is JTokenType.Float or JTokenType.Integer
                ? token.Value<float>()
                : fallback;
        }
    }
}
