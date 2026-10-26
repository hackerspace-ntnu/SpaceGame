<!-- The words of the sprite hamlet questline. The mechanics are in docs/AI/systems/SpriteHamletQuest.md. -->

# The sprite hamlet — story bible

Everything the characters of the sprite hamlet questline say, and the story under it. The
questline itself — how the Oathbranch, the witch's herbs, the chant and Merob's curse work — is told in
[the systems](the-systems.md) and, for agents, in `docs/AI/systems/SpriteHamletQuest.md`.

## Backstory (as the villagers know it — one fragment each, all optional)

For centuries the hamlets lived under an oath with Merob: he guarded them, and they took from nature
only what it gave freely. In a greedy age they traded the sacred herbs of the Tree of Life to a
scavenger-trader for glittering artifacts, the oath broke, and Merob went away. Generations later an
elder dreamed of him; the hamlets searched the wilds, found him asleep beneath the Tree of Life,
built their village in its roots and woke him with offerings of herbs. A few years ago a witch came
for the herbs; refused, she cursed Merob asleep. Since then the hamlet has sheltered under a dome of
stasis held up by the Oathbranch, a staff grown from the Tree itself: whatever touches the dome is held
still where it stands. **The twist:** the witch is the old trader, Morwen
Crookhat, kept alive for centuries by the herbs she bought; her hut stands on the old trading post,
and the three Oath Stones round it are where the oath was first sworn.

## Villager fragments

1. "Merob? He sleeps at the gate. Don't fear him — go and see the Elder, he knows more than any of us."
2. "We swore an oath, long ago. Take only what the forest gives. We… did not always keep it."
3. "My great-great-grandmother said we once traded the sacred herbs for shiny things. Shameful times."
4. "The old trader wore a hat bent like a broken twig. Paid in glittering junk, she did."
5. "When Merob came home the Tree bloomed all at once. I was only a sprout!"
6. "She came at dusk and asked for the herbs by name. As if she had tasted them before…"
7. "Since he fell asleep the forest has grown thorns where there were none."
8. "The Elder keeps something old in his head. Older than the Tree, he says. Don't tell him I said so."

## Elder Thistlewick

- **Before acceptance:** "So, the sky has sent us a traveller. Sit, sit — my knees will not let me
  stand long." / "Our guardian Merob sleeps under a witch's curse, and without him the old oath is
  only words." / "The witch has been spotted from time to time wandering the forest, but the hamlets
  are simply too scared to pursue her any further and flee back to the village." — *Question:* "Will
  you find where she lives?" — *Yes:* "Bless your boots. Follow the wilted herbs; she drops them as
  she walks." — *No:* "Then rest a while. The forest will still be here."
- **Finding the hut:** "The wilted herbs, traveller. She cannot help dropping them."
- **Report (bestowal):** "Her hut… on the old trading post? Then it is as I feared." / "Her life is in
  the sacred herbs we sold her — she wears them in her hat. No blade of yours will touch her." / "But
  rue undoes what the sacred herbs do. Bramble, Pip, Wisteria — take a bottle each." / "They are too
  small to catch her, and their throws too slow to find her. Lift the Oathbranch from the Tree —
  whatever it strikes is held still, and while she is held, their rue will find its mark." / "The dome falls when the branch leaves its root. We will be brave
  until Merob wakes."
- **The Oathbranch is taken** (speaker OATHKEEPERS): "The Oathbranch! Freeze her with it, and we will
  do the rest." / "Mind the dome is gone now. Quickly!"
- **While the keepers are out:** "Bring them home. All three."
- **Merob woken and wrong:** "The curse did not lift with her — it soured. Do not destroy him!"
- **After:** "The oath is spoken again. The Tree remembers. And so will we."
- **The stash** (briefed as it opens): "Take them, traveller. The things we once traded our oath for — we want none of them now." A grappling hook and a flame gauntlet appear before the Tree's roots.
- **Once it is all over:** "Rest a while, traveller. The stash beneath the Tree is yours — those trinkets have caused us grief enough."

## The Oath-Chant (three verses, one per keeper — sung to free Merob)

1. *Bramble:* "What the root gives freely, we take; what it keeps, we leave."
2. *Pip:* "No herb for silver, no bloom for brass, no leaf for a glittering thing."
3. *Wisteria:* "Guard us, and we guard the green. Sworn again, and kept."

## Morwen Crookhat (the monologue)

"Ohh, it has been a long time since I have seen you creatures!" / "Your herbs have granted me many
extra years, and I have been ever so grateful." / "Yes — the trader with the crooked hat. You sold
them to me yourselves, remember?" / "But now you know my secret, and you know where I live." / "No
one should have that. I must be rid of you."

**As the fight begins (speaker OATHKEEPERS):** "Nothing of yours will hurt her, the herbs in her hat
keep her whole!" / "Hold her still with the Oathbranch and our rue will find her!" / "And mind her
poison — three breaths of it and we are done for. Carry us out of it!" Each herb the rue burns goes
dark on her hat; the last one the keepers climb her hat and pluck out by hand, and the centuries she
borrowed come due.

## Merob

- **Briefing when he turns (speaker OATHKEEPERS):** "No, no! He is a friend! Don't break him!"
- **Freed:** the keepers: "The oath holds!" — Merob lays down his staff: he would never use such a
  nature-destroying artifact again.

## Favours

| Villager | Favour | Steps | Reward |
| --- | --- | --- | --- |
| Mosswick, the moss-roof cottager | The fallen log | `MoveAwayStep` (log off the door) → `RewardStep` | Battery, oxygen tank |
| Hazelbud, the basket-weaver | The lost acorn basket | `DeliverItemStep` (shelf-fungus ledge; jumping rod, jetpack or grapple) → `RewardStep` | StormWard |
| Dewdrop, the well-keeper (a dewling) | The spore-choked well | `BurnStep` (growth, `burnableDuring` the favour) → `RewardStep` | Lantern |

Their thanks, as the reward step briefs it: Mosswick — "Free at last! Take these, I found them in
the roots." Hazelbud — "My basket! Oh, every acorn still in it. Here, take this." Dewdrop — "Clear
water again! Take this lantern, it lit my nights while you worked."
