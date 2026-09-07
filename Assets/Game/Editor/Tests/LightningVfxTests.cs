// The lightning strike's bolt is a FLAT, single mesh — the built-in Plane — with the bolt itself
// painted into its UVs by LightningVFX.shadergraph. Nothing orients that plane towards the viewer,
// so the only thing standing between "a bolt" and "half the players see nothing" is the graph
// rendering both of the plane's faces.
//
// The failure is silent and asymmetric: whoever authors the effect stands on the lit side while
// they tune it, and it looks finished. ART-02 was reported as "renders over roughly 180 degrees of
// viewing directions, not at all from the other 180" — that is exactly one back-face-culled plane.
//
// Read from the .shadergraph text rather than off a material, unlike
// NetGunWiringTests.TheCordMaterialIsTwoSided: this graph has m_AllowMaterialOverride off, so there
// is no _Cull property on any material to query — URP bakes the cull straight into the generated
// shader from the target's Render Face. The second test is what keeps the first one meaningful; a
// guard on a graph the effect no longer uses guards nothing.
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

namespace SpaceGame.EditorTools
{
    public class LightningVfxTests
    {
        private const string ShaderGraphPath = "Assets/Game/Art/Shaders/Effects/LightningVFX.shadergraph";
        private const string VfxPath = "Assets/Game/Art/VisualEffects/Lightning.vfx";

        /// <summary>
        /// UniversalTarget.RenderFace.Both. The enum is internal to URP's ShaderGraph editor
        /// assembly, so the serialized value is spelled out here: Both = 0 (Cull Off),
        /// Back = 1 (Cull Front), Front = 2 (Cull Back).
        /// </summary>
        private const int RenderFaceBoth = 0;

        [Test]
        public void TheBoltPlaneRendersBothFaces()
        {
            string graph = File.ReadAllText(ShaderGraphPath);

            Match allowOverride = Regex.Match(graph, "\"m_AllowMaterialOverride\": (true|false)");
            Assert.IsTrue(allowOverride.Success, $"no UniversalTarget settings in {ShaderGraphPath}");
            Assert.AreEqual("false", allowOverride.Groups[1].Value,
                "LightningVFX now allows material override, so the cull comes from a material's " +
                "_Cull instead of the graph — assert that material instead of this field.");

            Match renderFace = Regex.Match(graph, "\"m_RenderFace\": (\\d+)");
            Assert.IsTrue(renderFace.Success, $"no Render Face setting in {ShaderGraphPath}");
            Assert.AreEqual(RenderFaceBoth, int.Parse(renderFace.Groups[1].Value),
                "LightningVFX culls a face again. Its mesh output draws the bolt on a flat, " +
                "unoriented Plane, so anything but Render Face = Both makes the strike invisible " +
                "from half of the viewing sphere (ART-02).");
        }

        [Test]
        public void TheLightningEffectStillShadesItsBoltWithThatGraph()
        {
            string guid = AssetDatabase.AssetPathToGUID(ShaderGraphPath);
            Assert.IsNotEmpty(guid, $"missing from the project: {ShaderGraphPath}");

            StringAssert.Contains(guid, File.ReadAllText(VfxPath),
                $"{VfxPath} no longer shades any output with LightningVFX, so the two-sided " +
                "guard above no longer covers the bolt.");
        }
    }
}
