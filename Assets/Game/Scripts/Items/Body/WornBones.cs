// Bone-name hints for a rig the humanoid lookup cannot read, shared by every body that wears gear —
// the player's BodyEquipmentController and an NPC's EntityBodyEquipment — so the two never disagree
// about which bone a gauntlet straps to.
namespace SpaceGame.Items
{
    public static class WornBones
    {
        public static readonly string[] BackHints = { "Spine", "Chest", "Torso" };
        public static readonly string[] LeftForearmHints = { "LeftForeArm", "ForeArm_L", "L_ForeArm", "forearm.L" };
        public static readonly string[] RightForearmHints = { "RightForeArm", "ForeArm_R", "R_ForeArm", "forearm.R" };
        public static readonly string[] LeftHandHints = { "LeftHand", "Hand_L", "L_Hand", "hand.L" };
        public static readonly string[] RightHandHints = { "RightHand", "Hand_R", "R_Hand", "hand.R" };
    }
}
