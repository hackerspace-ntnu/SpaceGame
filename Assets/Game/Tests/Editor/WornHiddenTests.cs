using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Characters;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The register of worn gear <c>PlayerLook</c> keeps out of its own camera's view.
    ///
    /// <para>
    /// What this pins is the HAND-BACK, not the hiding. Hiding is decided per camera render by a
    /// <c>RenderPipelineManager</c> callback that no edit-mode test can raise, and it is
    /// self-correcting anyway: it writes a mode for every camera, every frame, so a wrong answer
    /// lasts one frame. Un-registering is the half with no second chance — the renderers leave the
    /// register in the same call, the callback never looks at them again, and a pack set down on
    /// the sand would stay <see cref="ShadowCastingMode.ShadowsOnly"/> for the rest of the session.
    /// That is a silent failure of exactly the kind the pack's own history is full of: the wearer
    /// sees a shadow with no pack over it and nothing in the console.
    /// </para>
    /// <para>
    /// A plain <c>MeshRenderer</c>, deliberately, and not the helmet's <c>SkinnedMeshRenderer</c>:
    /// the register exists because the pack and the display copies of the gear strapped to it are
    /// built at runtime out of item prefabs, which are whatever a renderer can be. The field it
    /// backs was widened from <c>SkinnedMeshRenderer[]</c> to <c>Renderer[]</c> for this.
    /// </para>
    /// </summary>
    public class WornHiddenTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            spawned.Clear();
        }

        private PlayerLook Look()
        {
            var go = new GameObject("Player");
            spawned.Add(go);
            return go.AddComponent<PlayerLook>();
        }

        private Renderer Renderer(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            spawned.Add(go);
            return go.GetComponent<MeshRenderer>();
        }

        [Test]
        public void ClearingTheRegisterGivesTheOutgoingRenderersTheirShadowsBack()
        {
            PlayerLook look = Look();
            Renderer pack = Renderer("Pack");

            look.SetWornHidden(new[] { pack });

            // Stand in for the frame the wearer's own camera rendered while the pack was on their
            // back: that is the only thing that ever writes this mode, and it is what has to be
            // undone.
            pack.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

            look.SetWornHidden(null);

            Assert.That(pack.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On),
                        "a pack that has left the player's back must be drawn to them again");
        }

        [Test]
        public void ReRegisteringGivesTheReplacedRenderersTheirShadowsBack()
        {
            PlayerLook look = Look();
            Renderer oldCopy = Renderer("Old display copy");
            Renderer newCopy = Renderer("New display copy");

            look.SetWornHidden(new[] { oldCopy });
            oldCopy.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

            // What every contents change does: the display copies are rebuilt wholesale and the
            // register is replaced with the new set. The outgoing copies are usually destroyed with
            // the rebuild, but not always — the rig's own meshes survive it — so the hand-back has
            // to happen on a replacement and not only on a clear.
            look.SetWornHidden(new[] { newCopy });

            Assert.That(oldCopy.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On),
                        "a renderer dropped from the register must be drawn again");
        }

        [Test]
        public void ClearingTheRegisterSurvivesARendererDestroyedWhileItWasIn()
        {
            PlayerLook look = Look();
            Renderer copy = Renderer("Display copy");

            look.SetWornHidden(new[] { copy });

            // The ordinary case, not an edge one: a rebuild destroys every display copy before the
            // controller hands over the new set, so the register is always holding dead entries by
            // the time it is replaced.
            Object.DestroyImmediate(copy.gameObject);

            Assert.DoesNotThrow(() => look.SetWornHidden(null));
        }
    }
}
