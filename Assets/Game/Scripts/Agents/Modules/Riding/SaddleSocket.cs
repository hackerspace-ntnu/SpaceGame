// A place on an animal a saddle can be fitted, and the state of whether one is.
//
// The saddle itself is a PLAIN Instantiate on every machine, parented to a bone — not a spawned
// NetworkObject. That is the same call BackpackController makes for the pack, and for the same
// reasons: a NetworkObject parented into a rig's bone hierarchy has to have that reparenting
// replicated and re-applied after every spawn and every scene load, and the thing being replicated
// here is one bool. So the SOCKET holds the state and every machine builds its own visual from it.
//
// What the saddle changes about the animal:
//   * it becomes rideable   -- the MountModule is enabled; without a saddle it is disabled, and a
//                              disabled Behaviour is one the Interactor skips outright
//   * it can carry gear     -- the saddle prefab brings its own PackContainer
//   * it can be taken off   -- by the trigger on the saddle, see SaddleRemover
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class SaddleSocket : MonoBehaviour
    {
        [Header("Fitting")]
        [Tooltip("The saddle to build on this animal's back. A plain prefab, NOT a network prefab " +
                 "— every machine instantiates its own copy from the replicated saddled flag.")]
        [SerializeField] private GameObject saddlePrefab;

        [Tooltip("Bone the saddle hangs off. Left empty this looks for `spine1`, which is where " +
                 "the back is on every creature rigged in this project.")]
        [SerializeField] private Transform mountBone;

        [Tooltip("Where the saddle sits, in the ANIMAL ROOT's space — not the bone's. " +
                 "Authoring against the root is what makes this measurable: the saddle model's " +
                 "origin is already on the animal's spine at the seat centre, so this is that " +
                 "point read straight off the model. A bone's own axes are whatever the rig " +
                 "export left them as, and offsets authored in them cannot be derived from " +
                 "anything.")]
        [SerializeField] private Vector3 rootPosition = Vector3.zero;
        [SerializeField] private Vector3 rootEuler = Vector3.zero;

        [Tooltip("Undo the bone's scale, but keep the ANIMAL's. Every transform in an imported " +
                 "FBX carries lossyScale 100 (the centimetre convention), so a saddle parented to " +
                 "a bone without this arrives a hundred times too big. What it must NOT undo is " +
                 "the animal's own scale: a saddle on a creature built at 1.5x is a bigger saddle, " +
                 "and one held at world scale would sit on his back like a toy. Off only if the " +
                 "rig is genuinely unscaled.")]
        [SerializeField] private bool compensateBoneScale = true;

        [Header("What it enables")]
        [Tooltip("Enabled while saddled, disabled while bare. Nobody rides a bare animal.")]
        [SerializeField] private MountModule mount;

        [Tooltip("Given back to the world when the saddle is taken off, and what the item that " +
                 "fits it must match. Without it a removed saddle simply vanishes.")]
        [SerializeField] private InventoryItem saddleItem;

        [Header("Removal")]
        [Tooltip("How far from the animal removed gear lands, in metres.")]
        [SerializeField] private float dropRadius = 1.6f;

        [Tooltip("How far above the animal's origin dropped gear starts. It falls from there.")]
        [SerializeField] private float dropHeight = 1.2f;

        private GameObject saddleInstance;
        private bool saddled;

        /// <summary>Whether an animal is wearing a saddle right now.</summary>
        public bool IsSaddled => saddled;

        /// <summary>The live saddle, or null. The pack container hangs off this.</summary>
        public GameObject Saddle => saddleInstance;

        /// <summary>What this animal's saddle is, as an item. Read by the artifact that fits it.</summary>
        public InventoryItem SaddleItem => saddleItem;

        private void Awake()
        {
            if (mount == null) mount = GetComponentInChildren<MountModule>(true);
            if (mountBone == null) mountBone = FindBone("spine1");
            ApplySaddled(saddled);
        }

        private void OnEnable()
        {
            this.NetOn(NetMsg.SaddleFit, OnFitRequested);
            this.NetOn(NetMsg.SaddleSet, OnSaddleSet);
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.SaddleFit, OnFitRequested);
            this.NetOff(NetMsg.SaddleSet, OnSaddleSet);
        }

        private Transform FindBone(string boneName)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (t.name == boneName)
                    return t;
            return null;
        }

        // ── Asking ───────────────────────────────────────────────────────────

        /// <summary>
        /// Ask the server to fit or remove. Called by the artifact and by the saddle's own
        /// remover; both are presses on a machine that may not be the one that decides.
        /// </summary>
        public void Request(bool wanted, GameObject actor)
        {
            NetMessaging.NetSendTo(gameObject, NetMsg.SaddleFit,
                                   new NetArg { A = wanted ? 1 : 0 }.With(actor),
                                   NetTo.Server);
        }

        private void OnFitRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Owns(this)) return;
            Decide(arg.A != 0, arg.Resolve());
        }

        /// <summary>
        /// Server-side fit, for a caller already on the server. Returns whether the saddle actually
        /// went on.
        ///
        /// <para>
        /// The answer is the point. <c>PlaceableItem</c> spends the item it was used from, and it
        /// may only do that when something happened: a click at an animal already wearing one, or
        /// at a socket with no prefab, must not eat the saddle. Going through <see cref="Request"/>
        /// could not tell it — a message has no return value — so an already-server caller asks
        /// directly.
        /// </para>
        /// </summary>
        public bool Fit() => Network.Owns(this) && Decide(true, null);

        /// <summary>
        /// The whole decision, in one place because both ways in have to make it identically.
        /// Returns whether the state changed.
        /// </summary>
        private bool Decide(bool wanted, GameObject actor)
        {
            if (wanted == saddled) return false;
            if (wanted && saddlePrefab == null) return false;

            // Refuse to strip a saddle out from under a rider — the seat would vanish mid-ride and
            // MountModule would be disabled while it still held a player.
            if (!wanted && mount != null && mount.IsMounted) return false;

            if (!wanted)
                SpillAndReturn(actor);

            saddled = wanted;
            this.NetToAll(NetMsg.SaddleSet, new NetArg { A = saddled ? 1 : 0 });
            return true;
        }

        private void OnSaddleSet(in NetArg arg, ulong sender) => ApplySaddled(arg.A != 0);

        // ── Building the thing ───────────────────────────────────────────────

        /// <summary>
        /// Bring this machine's copy into line with the flag. Idempotent — it is called on every
        /// message, on Awake, and by the save restore, and only ever does work when it differs.
        /// </summary>
        public void ApplySaddled(bool value)
        {
            saddled = value;

            if (saddled && saddleInstance == null && saddlePrefab != null)
            {
                Transform parent = mountBone != null ? mountBone : transform;
                saddleInstance = Instantiate(saddlePrefab, parent);
                saddleInstance.name = saddlePrefab.name;
                // The bone's scale is the animal's own scale times the FBX centimetre factor, so
                // dividing the animal's back in leaves exactly the factor -- and the saddle ends up
                // in the animal's scale rather than the world's.
                float bone = compensateBoneScale ? parent.lossyScale.x : 1f;
                float animal = compensateBoneScale ? transform.lossyScale.x : 1f;
                saddleInstance.transform.localScale =
                    Vector3.one * (animal / (Mathf.Abs(bone) > 1e-4f ? bone : 1f));

                // Placed in world terms AFTER parenting, so the bone's own orientation and scale
                // drop out of the arithmetic entirely. It still rides the bone from here on.
                saddleInstance.transform.position = transform.TransformPoint(rootPosition);
                saddleInstance.transform.rotation = transform.rotation * Quaternion.Euler(rootEuler);

                foreach (SaddleRemover remover in
                         saddleInstance.GetComponentsInChildren<SaddleRemover>(true))
                    remover.Bind(this);
            }
            else if (!saddled && saddleInstance != null)
            {
                Destroy(saddleInstance);
                saddleInstance = null;
            }

            if (mount != null)
                mount.enabled = saddled;
        }

        // ── Taking it off ────────────────────────────────────────────────────

        /// <summary>
        /// Server. Everything the saddle was carrying hits the ground, and the saddle itself
        /// becomes an item again.
        ///
        /// <para>
        /// Dropping rather than deleting is the whole point: gear stowed on an animal is not a
        /// second inventory the player can lose track of, it is on a thing that walks away. Taking
        /// the saddle off has to put every item somewhere the player can pick it up again.
        /// </para>
        /// </summary>
        private void SpillAndReturn(GameObject actor)
        {
            var spilled = new List<InventoryItem>();

            if (saddleInstance != null)
            {
                foreach (PackContainer container in
                         saddleInstance.GetComponentsInChildren<PackContainer>(true))
                {
                    // Copied first: TakeOut mutates the layout this is walking.
                    var ids = new List<string>();
                    foreach (PackPlacement placement in container.Layout.Placements)
                        ids.Add(placement.ItemId);

                    foreach (string id in ids)
                    {
                        InventoryItem item = container.TakeOut(id);
                        if (item != null) spilled.Add(item);
                    }
                }
            }

            // The saddle goes back to whoever took it off, not onto the sand. Taking a placeable
            // back into your inventory is what Q does everywhere else, and a saddle that lands at
            // your feet reads as dropped rather than as retrieved -- worse on an animal that is
            // still walking, because it walks away from it.
            //
            // Its CARGO still spills, and that difference is deliberate: it can be far more than a
            // pack has room for, and losing a saddle to a full inventory would be worse than
            // picking a few things up.
            bool returned = false;
            if (saddleItem != null && actor != null)
            {
                IPlayerInventory inventory = actor.GetComponentInParent<IPlayerInventory>();
                returned = inventory != null && inventory.TryAddItem(saddleItem);
            }

            if (saddleItem != null && !returned)
                spilled.Add(saddleItem);        // full pack, or nobody asked: it falls

            for (int i = 0; i < spilled.Count; i++)
                DropIntoWorld(spilled[i], i, spilled.Count);
        }

        private void DropIntoWorld(InventoryItem item, int index, int count)
        {
            if (item == null || item.itemPrefab == null) return;

            // Spread them round the animal rather than stacking them inside each other, and start
            // above the ground so each falls onto whatever it is standing on.
            float angle = count <= 1 ? 0f : (index / (float)count) * Mathf.PI * 2f;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dropRadius;
            Vector3 where = transform.position + offset + Vector3.up * dropHeight;

            GameServices.World.Spawn(item.itemPrefab, where, Quaternion.identity);
        }

        private void OnValidate()
        {
            dropRadius = Mathf.Max(0f, dropRadius);
            dropHeight = Mathf.Max(0f, dropHeight);
        }
    }
}
