using SpaceGame.Gameplay;
using SpaceGame.Presentation;
using SpaceGame.World.Bounties;
using UnityEngine;

namespace SpaceGame.World.Posters
{
    /// <summary>How the bounty wants its bandit brought in. Printed as the tag on the poster.</summary>
    public enum WantedStatus
    {
        Alive = 0,
        Dead = 1,
    }

    /// <summary>
    /// A wanted poster. Put it on any GameObject and point <see cref="Agent"/> at a character:
    /// the editor fits a paper sheet and a collider, then bakes the poster automatically and
    /// bakes it again whenever the agent or the text changes (WantedPosterAutoBake). Right-click
    /// it in game to read it full-screen.
    ///
    /// <para>
    /// Scenery: nothing is sent or saved. Reading opens <see cref="PosterViewerUI"/> on the
    /// presser's machine only. Baking happens in the editor; a poster whose agent is set from
    /// code at runtime keeps whatever picture it was saved with.
    /// </para>
    /// <para>
    /// A poster a <see cref="BountyHunt"/> prints offers that hunt while it is read, and is
    /// stamped CLAIMED or EXPIRED once it has ended (<see cref="BountyPosters"/>).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WantedPoster : MonoBehaviour, IInteractable, IInteractionReadout, IInteractionMoment
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        [Tooltip("The character on the poster: a prefab, or a character in the scene. Drawn head-and-shoulders, facing out.")]
        [SerializeField] private GameObject agent;

        [Tooltip("Printed after NAME:. \"Unknown\" for a bandit nobody has named.")]
        [SerializeField] private string displayName = "Unknown";

        [Tooltip("Printed after REWARD:, as written.")]
        [SerializeField] private string reward = "400 jugs of water";

        [SerializeField] private WantedStatus status = WantedStatus.Alive;

        [Tooltip("The poster's own colour: the sun, the brackets, the labels, warm clothing, and darkened, the banner.")]
        [SerializeField] private Color accent = new(0.851f, 0.537f, 0.184f, 1f);

        [Tooltip("Printed after HT:. Empty prints the agent's measured height in metres.")]
        [SerializeField] private string heightLabel = "";

        [Header("Portrait framing")]
        [Tooltip("How much of the agent's height the portrait shows, from the top down. 0.5 is head and shoulders.")]
        [Range(0.2f, 1f)] [SerializeField] private float portraitSpan = 0.5f;

        [Tooltip("Space above the top of the agent, as a fraction of its height.")]
        [Range(0f, 0.2f)] [SerializeField] private float headroom = 0.03f;

        [Header("Baked by the editor")]
        [Tooltip("The paper the poster is printed on.")]
        [SerializeField] private Renderer sheet;

        [Tooltip("The baked poster. Do not assign by hand.")]
        [SerializeField] private Texture2D poster;

        [Tooltip("What the baked poster was made from; a mismatch bakes it again.")]
        [SerializeField] private string bakedFrom = "";

        private MaterialPropertyBlock block;

        public GameObject Agent => agent;
        public string DisplayName => displayName;
        public string Reward => reward;
        public WantedStatus Status => status;
        public Color Accent => accent;
        public string HeightLabel => heightLabel;
        public float PortraitSpan => portraitSpan;
        public float Headroom => headroom;
        public Renderer Sheet => sheet;
        public Texture2D Poster => poster;
        public string BakedFrom => bakedFrom;

        private void OnEnable()
        {
            ShowPoster();
            if (Application.isPlaying) BountyPosters.Hang(this);
        }

        private void OnDisable()
        {
            if (Application.isPlaying) BountyPosters.TakeDown(this);
        }

        public bool CanInteract() => poster != null;

        public void Interact(Interactor interactor) => PosterViewerUI.Show(poster, BountyPosters.OfferFor(this));

        public string Label => $"Wanted: {displayName}";

        public string Prompt => "RMB: read";

        public float? Value01 => null;

        public string ValueText => string.Empty;

        // The poster fills the screen at once; a reach towards the wall would play behind it.
        public CharacterMoment InteractionMoment => CharacterMoment.None;

        /// <summary>Puts the baked poster on the sheet. The sheet's material is shared; the picture rides a property block.</summary>
        private void ShowPoster()
        {
            if (sheet == null || poster == null) return;
            block ??= new MaterialPropertyBlock();
            sheet.GetPropertyBlock(block);
            block.SetTexture(BaseMapId, poster);
            sheet.SetPropertyBlock(block);
        }

#if UNITY_EDITOR
        /// <summary>Raised whenever the poster is edited or loaded in the editor; the auto-bake listens.</summary>
        public static event System.Action<WantedPoster> Edited;

        private void OnValidate()
        {
            ShowPoster();
            Edited?.Invoke(this);
        }

        /// <summary>Editor only: the bake writes its result here.</summary>
        public void SetBaked(Texture2D baked, string from)
        {
            poster = baked;
            bakedFrom = from;
            ShowPoster();
        }

        /// <summary>Editor only: the setup gives a new poster its sheet.</summary>
        public void SetSheet(Renderer paper)
        {
            sheet = paper;
            ShowPoster();
        }
#endif
    }
}
