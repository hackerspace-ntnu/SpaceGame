// Writes what a scene says about its bounty hunts into the hunt assets when the scene is saved: where each
// BountyHuntSite stands, and which settlement owns each hunt (SettlementBounties). The board and the visor need both
// while those chunks are not loaded. Saving the scene is the only step: there is no menu to remember.
//
// A scene can only speak for itself. Two sites or two owners for one hunt in the same scene are errors; a hunt that
// moves to another scene is warned about, since that is also how a second site or owner elsewhere shows up.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.World.Bounties;

namespace SpaceGame.EditorTools.Posters
{
    [InitializeOnLoad]
    internal static class BountyHuntRecorder
    {
        static BountyHuntRecorder() => EditorSceneManager.sceneSaved += Record;

        private static void Record(Scene scene)
        {
            var sites = new Dictionary<BountyHunt, BountyHuntSite>();
            var owners = new Dictionary<BountyHunt, SettlementBounties>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (BountyHuntSite site in root.GetComponentsInChildren<BountyHuntSite>(true))
                    RecordSite(scene, site, sites);

                foreach (SettlementBounties owner in root.GetComponentsInChildren<SettlementBounties>(true))
                    RecordOwner(scene, owner, owners);
            }
        }

        private static void RecordSite(Scene scene, BountyHuntSite site, Dictionary<BountyHunt, BountyHuntSite> seen)
        {
            BountyHunt hunt = site.Hunt;
            if (hunt == null)
            {
                Debug.LogError($"[Bounties] Site '{site.name}' in {scene.name} names no hunt.", site);
                return;
            }

            if (seen.TryGetValue(hunt, out BountyHuntSite first))
            {
                Debug.LogError($"[Bounties] '{hunt.name}' has two sites in {scene.name} ('{first.name}', '{site.name}'); " +
                               "only the first is recorded. Delete one.", site);
                return;
            }
            seen.Add(hunt, site);

            if (hunt.HasSite && hunt.SiteScene != scene.path)
                Debug.LogWarning($"[Bounties] '{hunt.name}' had its site in {hunt.SiteScene}; it is now '{site.name}' in " +
                                 $"{scene.path}. If the old site is still there, delete it.", site);

            if (hunt.RecordSite(site.transform.position, scene.path)) AssetDatabase.SaveAssetIfDirty(hunt);
        }

        private static void RecordOwner(Scene scene, SettlementBounties owner, Dictionary<BountyHunt, SettlementBounties> seen)
        {
            string settlementId = owner.Settlement.SettlementId;
            if (string.IsNullOrEmpty(settlementId))
            {
                Debug.LogError($"[Bounties] '{owner.name}' in {scene.name} owns hunts but has no settlement id, so none of " +
                               "them can be taken. Run Tools/SpaceGame/Expeditions/Stamp Settlement Identity and save again.", owner);
                return;
            }

            foreach (SettlementBounties.Entry entry in owner.Entries)
            {
                BountyHunt hunt = entry?.hunt;
                if (hunt == null)
                {
                    Debug.LogError($"[Bounties] '{owner.name}' in {scene.name} has an entry with no hunt.", owner);
                    continue;
                }

                if (seen.TryGetValue(hunt, out SettlementBounties first))
                {
                    Debug.LogError($"[Bounties] '{hunt.name}' is owned twice in {scene.name} ('{first.name}', '{owner.name}'); " +
                                   "only the first is recorded. A hunt has exactly one owner.", owner);
                    continue;
                }
                seen.Add(hunt, owner);

                if (!string.IsNullOrEmpty(hunt.OwnerSettlementId) && hunt.OwnerSettlementId != settlementId)
                    Debug.LogWarning($"[Bounties] '{hunt.name}' was owned by settlement {hunt.OwnerSettlementId}; it is now " +
                                     $"'{owner.name}' ({settlementId}). If the old owner still lists it, remove it there.", owner);

                if (hunt.RecordOwner(settlementId)) AssetDatabase.SaveAssetIfDirty(hunt);
            }
        }
    }
}
