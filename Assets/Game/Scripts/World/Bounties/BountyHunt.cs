// One bounty hunt: the wanted poster that prints it, the tips a settlement passes on while the crew hunt, what it pays,
// who stands at its site, and which settlement owns it. The whole of what a writer fills in to make a hunt
// (Bounties.md, "Adding a hunt").
//
// Lives in Resources/Bounties/ so every machine finds the same set (BountyCatalog). The poster supplies who is
// wanted, by what name and in which state; nothing is entered twice. The site is written by the BountyHuntSite marker
// and the owner by the settlement's SettlementBounties when their scenes are saved, so both are known while those
// chunks are not loaded.
using System;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Items;
using SpaceGame.World.Posters;

namespace SpaceGame.World.Bounties
{
    /// <summary>Who a cast entry puts at the site.</summary>
    public enum CastWho
    {
        /// <summary>The poster's own agent: the one the crew are paid for. Exactly one per hunt.</summary>
        Target,

        /// <summary>A prefab named on the entry.</summary>
        Prefab,

        /// <summary>Dealt from a tribe's roster by role, as a war party is.</summary>
        RosterRole,
    }

    /// <summary>When a cast entry is stood at the site.</summary>
    public enum HuntSpawnWhen
    {
        /// <summary>As soon as the hunt is taken and the site is loaded.</summary>
        OnTaken,

        /// <summary>Only once a resident has given the first tip.</summary>
        OnFirstTip,
    }

    [CreateAssetMenu(menuName = "SpaceGame/Bounty Hunt", fileName = "Bounty_")]
    public sealed class BountyHunt : ScriptableObject
    {
        /// <summary>The only placeholder a tip may use: the target's name from the poster.</summary>
        public const string TargetToken = "{target}";

#if UNITY_EDITOR
        // OutlawFaction.asset: who a hunt's people fight for unless an entry says otherwise, so a fight with them is
        // never a fight with the town that owns the hunt.
        private const string OutlawFactionGuid = "4815f6a4ad2c743ad900584b2c1e0c4b";
#endif

        [Serializable]
        public struct Reward
        {
            public InventoryItem item;
            [Min(1)] public int count;
        }

        /// <summary>One line of the hunt's cast: who, how many, where they stand, and when they arrive.</summary>
        [Serializable]
        public sealed class CastEntry
        {
            [Tooltip("Target: the poster's agent (exactly one per hunt). Prefab: the prefab below. RosterRole: dealt from the roster below.")]
            public CastWho who = CastWho.Prefab;

            [Tooltip("Prefab only. A registered network prefab with a SaveableEntity.")]
            public GameObject prefab;

            [Tooltip("RosterRole only: the tribe whose people are dealt.")]
            public FactionRoster roster;

            [Tooltip("RosterRole only. Never Rider: a Rider entry is a mount, not a person.")]
            public RosterRole role = RosterRole.Warrior;

            [Min(0)] public int minCount = 1;
            [Min(0)] public int maxCount = 1;

            [Tooltip("The BountyHuntSpots under the site with this tag are where they stand. Extra people scatter round the last one.")]
            public string spotTag = string.Empty;

            [Tooltip("Who they fight for. Outlaw by default, so the town that owns the hunt never takes their side. The " +
                     "target's must not be the board's captive faction: surrendering is switching to it.")]
            public FactionDefinition faction;

            public HuntSpawnWhen spawnWhen = HuntSpawnWhen.OnTaken;
        }

        [Tooltip("Written once when the asset is made; saves name the hunt by it. Never edit or copy it.")]
        [SerializeField] private string id;

        [Tooltip("The wanted poster prefab that prints this hunt. Its agent is the target (a registered network prefab), " +
                 "its name is the target's, its DEAD/ALIVE tag is the state that pays in full.")]
        [SerializeField] private WantedPoster poster;

        [Tooltip("What the settlement's people say about where the target is, one whole sentence each. Each resident always " +
                 "gives the same one, so ask around. May use {target}.")]
        [SerializeField, TextArea(1, 3)] private string[] tips = Array.Empty<string>();

        [Tooltip("Paid in full when the target is brought in the way the poster wants them.")]
        [SerializeField] private Reward[] rewards = Array.Empty<Reward>();

        [Tooltip("The share of each reward paid when the target comes in the other way (dead when wanted alive, or alive " +
                 "when wanted dead). Rounded down, so a single item may pay nothing.")]
        [SerializeField, Range(0f, 1f)] private float otherStateShare = 0.5f;

        [Tooltip("Everyone stood at the site while the hunt is open. Exactly one Target entry.")]
        [SerializeField] private CastEntry[] cast = Array.Empty<CastEntry>();

        [Header("Written by the BountyHuntSite marker")]
        [SerializeField] private bool hasSite;
        [SerializeField] private Vector3 sitePosition;
        [SerializeField] private string siteScene = string.Empty;

        [Tooltip("While the hunt is open, every player staying further than this from the owning settlement for long ends it.")]
        [SerializeField, Min(10f)] private float boundsRadius = 1500f;

        [Header("Written by the owning settlement's SettlementBounties")]
        [SerializeField] private string ownerSettlementId = string.Empty;

        public string Id => id;
        public WantedPoster Poster => poster;
        public GameObject Target => poster != null ? poster.Agent : null;
        public string TargetName => poster != null ? poster.DisplayName : string.Empty;
        public WantedStatus Wanted => poster != null ? poster.Status : WantedStatus.Alive;
        public string[] Tips => tips;
        public Reward[] Rewards => rewards;
        public float OtherStateShare => otherStateShare;
        public CastEntry[] Cast => cast;
        public bool HasSite => hasSite;
        public Vector3 SitePosition => sitePosition;
        public string SiteScene => siteScene;
        public float BoundsRadius => boundsRadius;
        public string OwnerSettlementId => ownerSettlementId;

        /// <summary>The tip at <paramref name="index"/> with the target's name filled in.</summary>
        public string Tip(int index) =>
            tips == null || tips.Length == 0 ? string.Empty : tips[index].Replace(TargetToken, TargetName);

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N");
                UnityEditor.EditorUtility.SetDirty(this);
            }

            FactionDefinition outlaw = null;
            foreach (CastEntry entry in cast)
            {
                if (entry == null || entry.faction != null) continue;
                outlaw ??= UnityEditor.AssetDatabase.LoadAssetAtPath<FactionDefinition>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(OutlawFactionGuid));
                entry.faction = outlaw;
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }

        /// <summary>Editor only: the site marker writes where it stands.</summary>
        public bool RecordSite(Vector3 position, string scenePath)
        {
            if (hasSite && sitePosition == position && siteScene == scenePath) return false;
            hasSite = true;
            sitePosition = position;
            siteScene = scenePath;
            UnityEditor.EditorUtility.SetDirty(this);
            return true;
        }

        /// <summary>Editor only: the owning settlement writes its id.</summary>
        public bool RecordOwner(string settlementId)
        {
            settlementId ??= string.Empty;
            if (ownerSettlementId == settlementId) return false;
            ownerSettlementId = settlementId;
            UnityEditor.EditorUtility.SetDirty(this);
            return true;
        }
#endif
    }
}
