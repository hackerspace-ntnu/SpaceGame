using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Everything that differs between two carried tools. <see cref="HandToolBuilder"/> turns one of
    /// these into a prefab and an item asset; <see cref="HandToolRoster"/> is the table of them.
    ///
    /// <para>
    /// Axis overrides are in the item's own Unity space, which for these models is the Blender space of
    /// the <c>Coll_Deco_Tool_*</c> collection converted by the FBX importer: the item's origin is the
    /// grip point, Blender +Z (the business end) is Unity +Y, and Blender -Y (the working face, or
    /// the muzzle) is Unity +Z. The pose and the grip offsets are not authored here: the
    /// <see cref="Stance"/> says how the tool is held and <see cref="GripFitter"/> derives them.
    /// </para>
    /// </summary>
    public sealed class HandToolSpec
    {
        /// <summary>The collection name without <c>Coll_Deco_</c>: <c>Tool_Hammer</c>, <c>Carry_Bucket_Wood</c>.</summary>
        public string Id;

        /// <summary>The name shown for the item.</summary>
        public string Title;

        /// <summary>The roster group; also the model and prefab sub-folder.</summary>
        public string Category;

        /// <summary>How the tool is held. <see cref="GripFitter"/> turns it into pose and offsets.</summary>
        public CarryStance Stance = CarryStance.Wield;

        /// <summary>A pose other than the stance's own, for the odd tool that wants one.</summary>
        public ItemGrip.HoldStyle? PoseOverride;

        /// <summary>Item axis along the tool when it is not the stance's default (+Y); a scraper's handle lies along +X.</summary>
        public Vector3? ItemAlong;

        /// <summary>Item axis of the working face when it is not the stance's default; at right angles to the along axis.</summary>
        public Vector3? ItemFace;

        /// <summary>Longest-axis size in the hand, metres.</summary>
        public float HoldSize;

        /// <summary>Longest-axis size on the pack, metres; 0 follows <see cref="HoldSize"/>.</summary>
        public float PackSize;

        /// <summary>Slides the tool after it is aimed, metres in the holder's space (+Y up, +Z forward).</summary>
        public Vector3 Nudge;

        /// <summary>
        /// Where along the tool the hand closes, relative to its <c>Root_*</c>: metres at hold size along the
        /// tool's own length, positive toward the business end. The root marks the grip the model's author
        /// meant; this is for the tool whose root is not where a hand would take it (the end of a haft).
        /// </summary>
        public float GripShift;

        /// <summary>Name of the <c>CharacterAction</c> the holder plays on use, or null for none.</summary>
        public string UseAction;

        /// <summary>
        /// True for an item that cannot go on a belt or pack at all (a cart): it is held for as long as it
        /// is carried. Every other tool hangs somewhere, worked out by <see cref="BeltHangs"/>.
        /// </summary>
        public bool CarryOnly;

        /// <summary>Slings the tool on the back although it is short enough for a hip: one too broad to hang beside a leg.</summary>
        public bool Slung;

        /// <summary>Item axis a stowed tool lies along when it is not the stance's own (a shield stands on its edge).</summary>
        public Vector3? HangAlong;

        /// <summary>Item axis that faces out from the wearer when the thinnest side is not the one that should (a sickle's blade must not stick out sideways).</summary>
        public Vector3? HangOut;

        /// <summary>
        /// Where the belt holds the item, in item space, when someone authored a loop for it; otherwise
        /// <see cref="BeltHangs"/> derives one from the tool's shape. Set together with <see cref="HangDown"/>.
        /// </summary>
        public Vector3? HangPoint;

        /// <summary>Which way is down while the item hangs from <see cref="HangPoint"/>, in item space.</summary>
        public Vector3 HangDown = Vector3.down;

        public string ModelPath => $"Assets/Game/Art/Models/Items/Tools/{Category}/{ModelFileName}.fbx";

        public string PrefabPath => $"Assets/Game/Prefabs/Items/Tools/{Category}/{Id}.prefab";

        public string ItemPath => $"Assets/Game/Resources/Items/Tools/{Id}.asset";

        /// <summary>The FBX name: the id in lower case, which is how the exporter names them.</summary>
        public string ModelFileName => Id.ToLowerInvariant();
    }
}
