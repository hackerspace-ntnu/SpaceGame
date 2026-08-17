using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Paints an astronaut in a chosen suit colour.
    ///
    /// <para>
    /// Deliberately knows nothing about netcode, the lobby, or settings. The same component runs on
    /// the networked player body and on the four figures standing in the lobby, and the only thing
    /// either of them does is call <see cref="Apply"/> with an index. <see cref="PlayerIdentity"/>
    /// supplies it from a NetworkVariable in the world; the lobby supplies it from what the player
    /// is pressing right now.
    /// </para>
    ///
    /// <para>
    /// <b>Property blocks, not material instances.</b> The astronaut's materials are embedded in
    /// astronaut_tobb.fbx and are read-only sub-assets, so they cannot be written to; instancing
    /// them instead would mean 15 clones per astronaut with a lifetime to manage, and a leak on
    /// every despawn in a session people join and leave. A property block has no lifetime at all.
    /// The cost is that these renderers leave the SRP batcher — with at most four astronauts that
    /// does not matter, and if it ever does the fallback is extracting the seven materials to real
    /// assets and swapping instances.
    /// </para>
    ///
    /// <para>
    /// <b>Matched by material name, not by slot index.</b> Slot order can shift on a re-export;
    /// the names are what the export script and the Blender file agree on. The trade is that a
    /// re-export which RENAMES a material would silently stop recolouring, which is why
    /// <see cref="Apply"/> shouts when it has nothing to paint and an EditMode test asserts every
    /// name in <see cref="SuitPalette.Relationships"/> still exists on the model.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class SuitRecolor : MonoBehaviour
    {
        // Created on first use rather than in a field initializer, and this is not a style choice.
        // A static initializer runs the first time the class is touched, which for a component on a
        // prefab is while Unity is DESERIALISING it — and constructing a MaterialPropertyBlock there
        // throws "CreateImpl is not allowed to be called from a MonoBehaviour constructor". It fails
        // at asset-import time, not at runtime, so it looks like a broken prefab rather than a broken
        // script.
        private static MaterialPropertyBlock block;

        // URP Lit calls it _BaseColor; the built-in Standard shader calls it _Color, and the
        // project's own materials carry both. Writing both costs nothing and means a material that
        // arrived on the wrong shader still recolours instead of silently staying orange.
        private static int baseColorId;
        private static int colorId;
        private static bool shaderIdsResolved;

        /// <summary>Reused across every call and every instance — a block is a bag of values, not state.</summary>
        private static MaterialPropertyBlock Block
        {
            get
            {
                if (block == null) block = new MaterialPropertyBlock();

                if (!shaderIdsResolved)
                {
                    baseColorId = Shader.PropertyToID("_BaseColor");
                    colorId = Shader.PropertyToID("_Color");
                    shaderIdsResolved = true;
                }

                return block;
            }
        }

        /// <summary>One renderer slot this component paints, and the rule that decides its colour.</summary>
        private readonly struct Target
        {
            public readonly Renderer Renderer;
            public readonly int Slot;
            public readonly SuitPalette.Relationship Relationship;

            public Target(Renderer renderer, int slot, SuitPalette.Relationship relationship)
            {
                Renderer = renderer;
                Slot = slot;
                Relationship = relationship;
            }
        }

        private readonly List<Target> targets = new();

        private bool scanned;

        /// <summary>What is currently painted, so a redundant call costs nothing.</summary>
        private int applied = -1;

        /// <summary>The colour this astronaut is wearing, or -1 before anything has been applied.</summary>
        public int Current => applied;

        /// <summary>Slots this component paints. Zero means the model changed under it.</summary>
        public int TargetCount
        {
            get { Scan(); return targets.Count; }
        }

        private void Awake() => Scan();

        /// <summary>
        /// Paints the astronaut. Cheap enough to call on every arrow press.
        ///
        /// Out-of-range indices are clamped rather than rejected: this value arrives from a peer's
        /// NetworkVariable and from a save, and a build that has seen a longer palette must not be
        /// able to throw here.
        /// </summary>
        public void Apply(int swatchIndex)
        {
            int index = SuitPalette.Clamp(swatchIndex);

            Scan();

            if (applied == index) return;
            applied = index;

            Color chosen = SuitPalette.ColorOf(index);
            MaterialPropertyBlock reusable = Block;

            for (int i = 0; i < targets.Count; i++)
            {
                Target target = targets[i];
                if (target.Renderer == null) continue;

                Color derived = ToUploadSpace(SuitPalette.Derive(chosen, target.Relationship));

                // Read the existing block back first. Overwriting it wholesale would drop anything
                // else overriding this slot — and something else overriding a player's renderer is
                // exactly the sort of thing a damage flash or a cloaking effect would want to do.
                target.Renderer.GetPropertyBlock(reusable, target.Slot);
                reusable.SetColor(baseColorId, derived);
                reusable.SetColor(colorId, derived);
                target.Renderer.SetPropertyBlock(reusable, target.Slot);
            }
        }

        /// <summary>
        /// Re-reads the model. Only needed if renderers are added after Awake, which nothing does
        /// today — exposed because a rig swap is the one change that would silently stop this
        /// working.
        /// </summary>
        public void Rescan()
        {
            scanned = false;
            applied = -1;
            Scan();
        }

        private void Scan()
        {
            if (scanned) return;
            scanned = true;

            targets.Clear();

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

            foreach (Renderer renderer in renderers)
            {
                // sharedMaterials, not materials: the latter INSTANTIATES every material on the
                // renderer just by being read, which is the exact per-astronaut material clone this
                // component exists to avoid.
                Material[] slots = renderer.sharedMaterials;

                for (int slot = 0; slot < slots.Length; slot++)
                {
                    if (slots[slot] == null) continue;

                    string name = StripInstanceSuffix(slots[slot].name);

                    for (int r = 0; r < SuitPalette.Relationships.Length; r++)
                    {
                        if (SuitPalette.Relationships[r].MaterialName != name) continue;

                        targets.Add(new Target(renderer, slot, SuitPalette.Relationships[r]));
                        break;
                    }
                }
            }

            if (targets.Count == 0)
            {
                Debug.LogError($"[SuitRecolor] '{name}' has no material matching " +
                               "SuitPalette.Relationships, so the suit colour will do nothing. The " +
                               "model's materials were probably renamed by a Blender re-export — " +
                               "SuitPaletteTests asserts the expected names and will say which.",
                               this);
            }
        }

        /// <summary>
        /// A material read off a live renderer may be a clone, and Unity names clones
        /// "Material.043 (Instance)". Matching the raw name would miss every one of them.
        /// </summary>
        private static string StripInstanceSuffix(string materialName)
        {
            const string suffix = " (Instance)";

            return materialName.EndsWith(suffix)
                ? materialName[..^suffix.Length]
                : materialName;
        }

        /// <summary>
        /// Converts a gamma colour for upload.
        ///
        /// <see cref="Material.SetColor"/> does this conversion itself in a linear project;
        /// <see cref="MaterialPropertyBlock"/> does NOT, and uploads whatever it is handed. Without
        /// this every suit came out visibly washed out — bright, chalky versions of the right hue —
        /// which is easy to mistake for a lighting problem.
        /// </summary>
        private static Color ToUploadSpace(Color gamma) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear ? gamma.linear : gamma;
    }
}
