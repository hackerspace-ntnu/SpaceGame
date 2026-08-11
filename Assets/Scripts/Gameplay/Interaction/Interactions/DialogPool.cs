using UnityEngine;

[CreateAssetMenu(fileName = "DialogPool", menuName = "Dialog/Dialog Pool")]
public class DialogPool : ScriptableObject
{
    private static readonly string[] DefaultLines =
    {
        "Morning shift starts at first siren. If you hear two sirens, run for cover.",
        "I traded half a battery for fresh bread today. Worth it.",
        "That crane by the scrapyard still works, just don't pull the red lever.",
        "The dunes look calm, but the wind can erase your tracks in minutes.",
        "If your canteen tastes like metal, change the filter before sunset.",
        "The old relay tower blinks three times when scouts return.",
        "I trust machines more than I trust smiling strangers.",
        "Keep your boots dry. Desert nights freeze harder than people expect.",
        "When crows gather on one roof, something is usually wrong nearby.",
        "You hear that humming?\nThat's the shield wall charging for nightfall.",
        "Never camp in dry riverbeds. Storm water moves faster than fear.",
        "The market judge likes exact numbers. Bring receipts, not stories.",
        "I watched a patrol vanish behind those rocks. No gunfire, just silence.",
        "If the lantern burns green, the fuel is contaminated.",
        "My map says shortcut. My knees say trap.",
        "South gate closes early on dust-heavy days.",
        "The mechanic at Bay Two can fix anything except bad decisions.",
        "I owe the medic a favor and two clean bandages.",
        "You can hear trains at night, but the tracks ended years ago.",
        "Rule one: don't panic.\nRule two: if you panic, move anyway.",
        "That bunker door opens only when the generator runs above eighty percent.",
        "A quiet checkpoint is never really quiet.",
        "I found fresh footprints by the water tanks. Not ours.",
        "Barter tip: food beats ammo when people are tired.",
        "If your radio pops twice, rotate the antenna and wait.",
        "The northern ridge has shade till noon. Good place to rest.",
        "Don't touch glowing moss without gloves.",
        "Our lookout can spot smoke from ten kilometers away.",
        "Someone painted arrows on the canyon wall, but one arrow lies.",
        "When the bell rings once,\nlock your doors and count your people.",
        "I've seen raiders pretend to be pilgrims. Ask them where they're from.",
        "The greenhouse survived because they buried half of it underground.",
        "Fuel is scarce, patience is scarcer.",
        "If you break line of sight, break your silhouette too.",
        "That old fountain still works after rain, for about ten minutes.",
        "I keep spare laces; snapped boots can kill faster than bullets.",
        "The astronomer says tonight's sky is clear enough for long signals.",
        "Don't light a fire on ridge tops unless you want company.",
        "Some doors are locked for safety. Some are locked for secrets.",
        "I heard children laughing in the tunnels.\nNo one lives there.",
        "Salted meat lasts longer, but it makes you thirstier than fear.",
        "If your compass spins, step away from buried cables.",
        "The quartermaster remembers every unpaid debt.",
        "Our drones avoid the western sinkhole for a reason.",
        "Last caravan brought tea, glass beads, and bad news.",
        "Keep your knife sharp, not your tongue.",
        "The wind farm groans before storms by about twenty minutes.",
        "Never chase lights in the fog valley.",
        "I sleep with one boot on. Old habit, good habit.",
        "At dusk the canyon echoes.\nCount echoes to guess distance.",
        "If the gate team salutes twice, it means unknown convoy inbound.",
        "Someone moved the warning signs overnight.",
        "The refinery smells sweet when a pipe is about to fail.",
        "Don't step on black sand near the vents.",
        "I stash emergency water behind the third rusted drum.",
        "The tailor reinforced my coat with sailcloth. Best trade this month.",
        "If your flashlight flickers, don't wait for complete darkness.",
        "People fear the ruins because they remember too much.",
        "The medic says half our injuries come from rushing.",
        "When in doubt,\nmark your route,\nleave breadcrumbs.",
        "I've got a lucky coin. It isn't lucky, just loud when dropped.",
        "The radio room goes silent every day at noon. Nobody knows why.",
        "Watchtower Four reports movement near the salt flats.",
        "A clean engine means someone visited recently.",
        "The schoolhouse now stores grain and old books.",
        "If you hear flutes at night, follow the guards, not the music.",
        "One spark can feed a camp or burn a district.",
        "We measure trust in shared water, not shared words.",
        "The bridge sways, but it still holds two mules and a cart.",
        "I found carved symbols near the caves.\nSame pattern as last year.",
        "Sunburn slows you down more than a heavy pack.",
        "The old map room flooded. Ink ran, truths stayed.",
        "Today feels quiet, and that bothers me.",
        "If scavengers offer miracle cures, ask what's in the bottle.",
        "The station clock is wrong, but everyone still checks it.",
        "I heard distant thunder.\nCould be weather, could be artillery.",
        "Bring rope before pride when crossing gullies.",
        "Our baker hides sugar in the flour bins.",
        "No one enters the archive alone after dark.",
        "The generator likes clean air and hates fine dust.",
        "Yesterday's footprints pointed both ways. Make of that what you will.",
        "If you're lost, follow power lines downhill.",
        "The chapel bell cracked, but the sound still carries.",
        "I traded stories for medicine once.\nBest bargain I ever made.",
        "The snipers nest where shadows move slowly.",
        "You can smell rain three hours before it comes.",
        "Someone keeps fixing the fence at night and never takes credit.",
        "Spare socks are wealth in this climate.",
        "That convoy flag is old alliance colors. Could be friend, could be bait.",
        "At sunrise, metal burns skin faster than sand.",
        "The quiet kid in logistics can decode almost anything.",
        "Don't laugh at superstition when it keeps people cautious.",
        "I buried a cache by the dead tree.\nIf it's gone, someone needed it.",
        "When alarms fail, listen for dogs. They hear trouble first.",
        "The eastern pass is open today, but only until moonrise.",
        "Our best scout says the horizon looked wrong this morning.",
        "If the sky goes copper, seal windows and breathe through cloth.",
        "Leave one lantern lit for those coming home late.",
        "The city remembers everyone.\nSometimes it remembers too loudly.",
        "I've survived this long by checking corners and checking people twice."
    };

    [TextArea(2, 5)]
    public string[] lines = DefaultLines;

    public static string[] GetDefaultLines()
    {
        return (string[])DefaultLines.Clone();
    }

    private void OnValidate()
    {
        if (lines != null && lines.Length > 0)
        {
            return;
        }

        lines = (string[])DefaultLines.Clone();
    }
}
