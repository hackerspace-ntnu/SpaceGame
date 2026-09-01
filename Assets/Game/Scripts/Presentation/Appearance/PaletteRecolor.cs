using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Paints a model's named materials from a palette, without touching the material assets.
    ///
    /// <para>
    /// Extracted from <c>SuitRecolor</c> when the ship livery needed the identical machinery. What
    /// is shared is everything that was hard to get right, and every part of it failed silently the
    /// first time: property blocks rather than material instances, matching by material NAME rather
    /// than slot index, reading a slot's existing block back before writing it, and converting to
    /// linear on upload. What a subclass supplies is only the table and what an index means.
    /// </para>
    ///
    /// <para>
    /// <b>Property blocks, not material instances.</b> Instancing would mean a clone per material
    /// per object with a lifetime to manage, and a leak on every despawn in a session people join
    /// and leave. A property block has no lifetime at all. The cost is that these renderers leave
    /// the SRP batcher; with a handful of astronauts and ships that does not matter, and if it ever
    /// does the fallback is extracting the recoloured materials to real assets and swapping
    /// instances.
    /// </para>
    ///
    /// <para>
    /// <b>Matched by material name, not by slot index.</b> Slot order shifts on a re-export; the
    /// names are what the export script and the source file agree on. The trade is that a re-export
    /// which RENAMES a material silently stops recolouring, which is why <see cref="Scan"/> shouts
    /// when it has nothing to paint and EditMode tests assert every name in a table still exists on
    /// its model.
    /// </para>
    /// </summary>
    public abstract class PaletteRecolor : MonoBehaviour
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
        // arrived on the wrong shader still recolours instead of silently staying its authored hue.
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

        /// <summary>What is currently painted, or -1 before anything has been.</summary>
        private int applied = -1;

        /// <summary>The palette index this model is wearing, or -1 before anything has been applied.</summary>
        public int Current => applied;

        /// <summary>Slots this component paints. Zero means the model changed under it.</summary>
        public int TargetCount
        {
            get { Scan(); return targets.Count; }
        }

        /// <summary>Which materials this model paints, and where each sits relative to the choice.</summary>
        protected abstract IReadOnlyList<SuitPalette.Relationship> Relationships { get; }

        /// <summary>
        /// The colour <paramref name="index"/> means, or false when it means "leave the authored
        /// materials alone" — which is how an unclaimed model says it has no colour yet.
        /// </summary>
        protected abstract bool TryColorOf(int index, out Color chosen);

        /// <summary>
        /// The index to record once <paramref name="index"/> has been applied, so a repeat call
        /// costs nothing. Identity unless a subclass folds several inputs onto one colour — a
        /// palette that clamps does, and without this the same colour would be repainted whenever a
        /// peer on a longer palette published an index past the end of ours.
        /// </summary>
        protected virtual int Normalise(int index) => index;

        /// <summary>
        /// The material name to match against the table, given what a renderer reports.
        ///
        /// A material read off a live renderer may be a clone, and Unity names clones
        /// "X (Instance)" — matching the raw name would miss every one of them. Subclasses whose
        /// models carry further suffixes strip those too.
        /// </summary>
        protected virtual string MatchNameOf(string materialName) =>
            StripSuffix(materialName, " (Instance)");

        /// <summary>What to say when the table matches nothing on this model. Named types, so the
        /// message can point at the file that was probably renamed.</summary>
        protected abstract string NothingToPaintMessage { get; }

        protected virtual void Awake() => Scan();

        /// <summary>
        /// Paints the model. Cheap enough to call on every arrow press in a lobby.
        ///
        /// Out-of-range indices are the subclass's problem to fold, not this method's to reject:
        /// the value arrives from a peer's NetworkVariable and from a save, and a build that has
        /// seen a longer palette must not be able to throw here.
        /// </summary>
        public void Apply(int index)
        {
            Scan();

            int normalised = Normalise(index);
            if (applied == normalised) return;

            if (!TryColorOf(normalised, out Color chosen)) return;

            applied = normalised;

            MaterialPropertyBlock reusable = Block;

            for (int i = 0; i < targets.Count; i++)
            {
                Target target = targets[i];
                if (target.Renderer == null) continue;

                Color derived = ToUploadSpace(SuitPalette.Derive(chosen, target.Relationship));

                // Read the existing block back first. Overwriting it wholesale would drop anything
                // else overriding this slot — and something else overriding a renderer is exactly
                // what a damage flash or a cloaking effect would want to do.
                target.Renderer.GetPropertyBlock(reusable, target.Slot);
                reusable.SetColor(baseColorId, derived);
                reusable.SetColor(colorId, derived);
                target.Renderer.SetPropertyBlock(reusable, target.Slot);
            }
        }

        /// <summary>
        /// Re-reads the model. Only needed if renderers are added after <see cref="Awake"/>, which
        /// nothing does today — exposed because a rig or hull swap is the one change that would
        /// silently stop this working.
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

            IReadOnlyList<SuitPalette.Relationship> table = Relationships;
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

            foreach (Renderer renderer in renderers)
            {
                // sharedMaterials, not materials: the latter INSTANTIATES every material on the
                // renderer just by being read, which is the exact per-object material clone this
                // component exists to avoid.
                Material[] slots = renderer.sharedMaterials;

                for (int slot = 0; slot < slots.Length; slot++)
                {
                    if (slots[slot] == null) continue;

                    string materialName = MatchNameOf(slots[slot].name);

                    for (int r = 0; r < table.Count; r++)
                    {
                        if (table[r].MaterialName != materialName) continue;

                        targets.Add(new Target(renderer, slot, table[r]));
                        break;
                    }
                }
            }

            if (targets.Count == 0)
                Debug.LogError(NothingToPaintMessage, this);
        }

        protected static string StripSuffix(string value, string suffix) =>
            string.IsNullOrEmpty(value) || !value.EndsWith(suffix) ? value : value[..^suffix.Length];

        /// <summary>
        /// Converts a gamma colour for upload.
        ///
        /// <see cref="Material.SetColor"/> does this conversion itself in a linear project;
        /// <see cref="MaterialPropertyBlock"/> does NOT, and uploads whatever it is handed. Without
        /// this every recolour comes out visibly washed out — a bright, chalky version of the right
        /// hue — which is easy to mistake for a lighting problem.
        /// </summary>
        private static Color ToUploadSpace(Color gamma) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear ? gamma.linear : gamma;
    }
}
