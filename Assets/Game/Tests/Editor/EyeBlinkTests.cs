using System.Reflection;
using NUnit.Framework;
using SpaceGame.EditorTools;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// What <see cref="EyeBlink"/> actually hands the eye shader across a death and a revive.
    ///
    /// <para>
    /// The colour check is the one that matters: the lids first shipped converting the skin colour to
    /// linear before <c>MaterialPropertyBlock.SetColor</c>, which linearises again by itself, and every
    /// lid came out a darker, oversaturated cousin of its face with nothing logged. The raw vector in
    /// the block is the only place that shows.
    /// </para>
    ///
    /// <para>
    /// EditMode does not run a component's messages, so they are invoked by hand, in Unity's order.
    /// </para>
    /// </summary>
    public class EyeBlinkTests
    {
        private static readonly Color Skin = new Color(0.83f, 0.73f, 0.55f);
        private const float UpperRest = 180f;
        private const float Meet = -15f;

        private GameObject character;
        private Material eyeMaterial;

        [TearDown]
        public void CleanUp()
        {
            if (character != null) Object.DestroyImmediate(character);
            if (eyeMaterial != null) Object.DestroyImmediate(eyeMaterial);
        }

        [Test]
        public void DeathShutsTheEyesInTheSkinColourAndReviveOpensThem()
        {
            var shader = Shader.Find(StylizedEyeBuilder.EyeShaderName);
            Assert.That(shader, Is.Not.Null, "the eye shader must compile for this to mean anything");
            eyeMaterial = new Material(shader);

            character = new GameObject("Blinker");
            var health = character.AddComponent<HealthComponent>();

            var eyeObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eyeObject.transform.SetParent(character.transform);
            var eye = eyeObject.GetComponent<Renderer>();
            eye.sharedMaterial = eyeMaterial;

            var blink = character.AddComponent<EyeBlink>();
            var so = new SerializedObject(blink);
            SerializedFields.SetObjects(so, "eyes", new[] { eye });
            SerializedFields.SetColor(so, "lidColour", Skin);
            SerializedFields.SetFloat(so, "upperLidRest", UpperRest);
            SerializedFields.SetFloat(so, "lidsMeet", Meet);
            so.ApplyModifiedPropertiesWithoutUndo();

            Invoke(blink, "Awake");
            Invoke(blink, "OnEnable");
            Assert.That(UpperEdge(eye), Is.EqualTo(UpperRest * Mathf.Deg2Rad).Within(1e-4f), "a live character starts open");

            health.Damage(health.GetMaxHealth);
            Assert.That(UpperEdge(eye), Is.EqualTo(Meet * Mathf.Deg2Rad).Within(1e-4f), "a dead character's eyes are shut");

            Vector4 uploaded = Block(eye).GetVector("_LidColor");
            Color expected = QualitySettings.activeColorSpace == ColorSpace.Linear ? Skin.linear : Skin;
            Assert.That(uploaded.x, Is.EqualTo(expected.r).Within(2e-3f), "lid colour linearised exactly once");
            Assert.That(uploaded.z, Is.EqualTo(expected.b).Within(2e-3f), "lid colour linearised exactly once");

            health.ResetToFull();
            Assert.That(UpperEdge(eye), Is.EqualTo(UpperRest * Mathf.Deg2Rad).Within(1e-4f), "a revived character opens its eyes");

            Invoke(blink, "OnDisable");
        }

        private static void Invoke(EyeBlink blink, string message) =>
            typeof(EyeBlink).GetMethod(message, BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(blink, null);

        private static MaterialPropertyBlock Block(Renderer eye)
        {
            var block = new MaterialPropertyBlock();
            eye.GetPropertyBlock(block);
            return block;
        }

        private static float UpperEdge(Renderer eye) => Block(eye).GetVector("_LidEdges").x;
    }
}
