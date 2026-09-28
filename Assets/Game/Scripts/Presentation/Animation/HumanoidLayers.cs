namespace SpaceGame.Presentation
{
    /// <summary>
    /// The shape of the generated humanoid controller: its layer names, in order, and the one
    /// function that names an action's states.
    ///
    /// <para>
    /// Shared by the builder that writes the controller and by <see cref="CharacterActions"/>,
    /// which plays states by name hash. A state named one way by the builder and looked up another
    /// way at runtime is an action that silently never plays, so there is exactly one place the
    /// name is made.
    /// </para>
    /// </summary>
    public static class HumanoidLayers
    {
        public const string Base = "Base Layer";

        /// <summary>Hold poses and the gauntlet raise, driven by PlayerAimRig / HoldAnimator.</summary>
        public const string UpperBody = "Upper Body";

        /// <summary>The left arm's own hold pose, for two worn devices at once.</summary>
        public const string WornLeft = "Worn Left";

        /// <summary>The wingsuit pose. Always the top layer: a gliding body's arms are the wing.</summary>
        public const string Glide = "Glide";

        /// <summary>
        /// The resting state of every layer above the Base Layer. It animates nothing, so a layer
        /// resting here passes the pose beneath it through untouched, even at weight 1.
        /// </summary>
        public const string EmptyState = "Empty";

        public const string ActionFull = "Action Full";
        public const string ActionUpper = "Action Upper";
        public const string ActionLeftArm = "Action Left Arm";
        public const string ActionRightArm = "Action Right Arm";

        /// <summary>
        /// Additive blending, above every overriding action layer so it adds to whatever they
        /// settled on — the aim pose, a full-body action, the bare hold pose — and below Glide,
        /// whose arms are a wing no recoil should bend.
        /// </summary>
        public const string ActionAdditive = "Action Additive";

        /// <summary>Every layer, bottom to top.</summary>
        public static readonly string[] Order =
        {
            Base, UpperBody, WornLeft, ActionFull, ActionUpper, ActionLeftArm, ActionRightArm, ActionAdditive, Glide
        };

        /// <summary>The stage of an action a state plays.</summary>
        public enum Stage
        {
            Main,
            Enter,
            Exit
        }

        public static string ForSlot(CharacterAction.Slot slot) => slot switch
        {
            CharacterAction.Slot.Full => ActionFull,
            CharacterAction.Slot.Upper => ActionUpper,
            CharacterAction.Slot.LeftArm => ActionLeftArm,
            CharacterAction.Slot.RightArm => ActionRightArm,
            CharacterAction.Slot.Additive => ActionAdditive,
            _ => throw new System.ArgumentOutOfRangeException(nameof(slot), slot, "a slot with no layer")
        };

        /// <summary>
        /// Whether <paramref name="slot"/>'s layer adds to the pose beneath rather than replacing
        /// it. Such a slot never competes with the others: a full-body action does not stop it,
        /// and it stops nothing.
        /// </summary>
        public static bool IsAdditive(CharacterAction.Slot slot) => slot == CharacterAction.Slot.Additive;

        /// <summary>The state that plays <paramref name="stage"/> of variant <paramref name="variant"/>.</summary>
        public static string StateName(CharacterAction action, int variant, Stage stage) =>
            stage == Stage.Main ? $"{action.name} {variant}" : $"{action.name} {variant} {stage}";
    }
}
