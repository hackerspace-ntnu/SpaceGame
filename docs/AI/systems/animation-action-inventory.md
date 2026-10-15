# Animation action inventory

Generated from the `CharacterAction` assets on 2026-10-03 — regenerate rather than hand-edit. Companion to [AnimationCatalog.md](AnimationCatalog.md).

## Cue coverage

Actions answering each cue before the 2026-10-03 pass and now, and how many can be held (loop or enter-loop-exit; `Hold` ignores one-shots).

| Cue | Before | Now | Of which loop |
|---|---|---|---|
| acrobatic | 14 | 14 | 0 |
| afraid | 3 | 5 | 2 |
| agree | 1 | 1 | 0 |
| aim | 7 | 9 | 4 |
| angry | 7 | 11 | 2 |
| applaud | 0 | 2 | 1 |
| argue | 2 | 3 | 1 |
| ask | 1 | 1 | 0 |
| attack | 23 | 23 | 0 |
| beckon | 1 | 1 | 1 |
| block | 4 | 5 | 3 |
| bored | 4 | 11 | 9 |
| bow | 0 | 1 | 0 |
| brawl | 6 | 6 | 0 |
| carry | 6 | 10 | 6 |
| catch | 3 | 3 | 0 |
| celebrate | 9 | 18 | 6 |
| chant | 0 | 1 | 0 |
| climb | 16 | 17 | 5 |
| cold | 2 | 3 | 2 |
| collapse | 0 | 2 | 2 |
| confused | 0 | 1 | 0 |
| cook | 3 | 15 | 10 |
| cough | 0 | 1 | 0 |
| craft | 4 | 10 | 8 |
| cry | 0 | 2 | 1 |
| dance | 11 | 17 | 13 |
| dig | 3 | 5 | 5 |
| disagree | 2 | 4 | 0 |
| disgust | 0 | 3 | 0 |
| dodge | 2 | 2 | 0 |
| draw | 0 | 2 | 0 |
| drink | 4 | 8 | 1 |
| drunk | 0 | 3 | 3 |
| eat | 4 | 6 | 4 |
| exhausted | 0 | 1 | 1 |
| explain | 5 | 10 | 2 |
| fall | 7 | 11 | 1 |
| farewell | 4 | 9 | 0 |
| farm | 0 | 1 | 1 |
| fidget | 4 | 16 | 0 |
| fish | 0 | 7 | 4 |
| gather | 0 | 3 | 2 |
| gesture | 2 | 6 | 0 |
| getup | 9 | 11 | 0 |
| give | 10 | 11 | 0 |
| greet | 4 | 8 | 1 |
| hammer | 2 | 3 | 3 |
| hungry | 0 | 2 | 1 |
| hurt | 4 | 5 | 0 |
| injured | 5 | 13 | 8 |
| interact | 1 | 3 | 0 |
| ladder | 1 | 2 | 2 |
| laugh | 2 | 5 | 2 |
| listen | 4 | 4 | 2 |
| locomotion | 65 | 74 | 51 |
| mine | 0 | 3 | 3 |
| music | 0 | 1 | 1 |
| operate | 0 | 7 | 1 |
| pickup | 10 | 15 | 1 |
| point | 4 | 6 | 2 |
| prone | 0 | 3 | 0 |
| putdown | 3 | 5 | 0 |
| read | 0 | 2 | 1 |
| receive | 0 | 2 | 0 |
| repair | 7 | 10 | 8 |
| restrain | 0 | 1 | 0 |
| sad | 6 | 10 | 6 |
| salute | 0 | 1 | 0 |
| search | 10 | 18 | 5 |
| serve | 0 | 4 | 1 |
| shoot | 6 | 7 | 0 |
| sing | 0 | 1 | 0 |
| sit | 12 | 12 | 7 |
| sitground | 2 | 2 | 2 |
| sleep | 2 | 3 | 2 |
| sneak | 9 | 11 | 7 |
| start | 4 | 5 | 0 |
| stop | 5 | 6 | 0 |
| stumble | 0 | 3 | 1 |
| swordplay | 3 | 3 | 0 |
| talk | 5 | 7 | 5 |
| tend | 3 | 9 | 8 |
| think | 3 | 4 | 3 |
| threaten | 2 | 4 | 1 |
| throw | 8 | 13 | 0 |
| tired | 4 | 8 | 6 |
| turn | 9 | 11 | 0 |
| work | 34 | 48 | 28 |

## The new actions

One row per action; the variants column is how many clips it picks between.

### Mixamo singles (`Art/Animations/Humanoid`) — 42 actions, 51 variants

| Action | Slot | Plays | Variants | Cues |
|---|---|---|---|---|
| Bartend | Upper | once | 4 | serve, work, craft |
| Bartend Hold | Upper | loop | 1 | serve, work, craft |
| Bow Quick Formal | Full | once | 1 | bow, farewell |
| Button Pushing | Upper | once | 1 | operate, interact |
| Crouch Idle Mixamo | Full | loop | 1 | sneak |
| Defeated React | Full | once | 1 | sad, disagree |
| Defeated Slump | Full | loop | 1 | sad, tired |
| Draw Pistol | Upper | once | 1 | draw, aim |
| Enter Code | Upper | once | 1 | operate |
| Fax Hold | Upper | loop | 1 | operate |
| Fax Operate | Upper | once | 2 | operate, work |
| Fish Cast Mixamo | Full | once | 1 | fish, throw |
| Fish Wait Hold | Full | loop | 1 | fish |
| Gather Crouch Hold | Full | loop | 1 | gather, tend |
| Gather From Ground | Full | once | 1 | gather, pickup |
| Get Up Mixamo | Full | once | 3 | getup |
| Grab From Floor | Full | once | 1 | pickup |
| Kneel Work | Full | enter-loop-exit | 1 | repair, tend, work |
| Ladder Climb Mixamo | Full | loop | 1 | ladder, climb |
| Laying Sleep | Full | loop | 1 | sleep |
| Nervous Look Around | Full | once | 1 | afraid, search, fidget |
| Opening | Upper | once | 1 | interact, work |
| Push Heavy Release | Full | once | 1 | work |
| Reacting Nervous | Upper | once | 1 | afraid, fidget |
| Restrain | Full | once | 1 | restrain |
| Rummage | Upper | loop | 1 | search, tend |
| Salute Formal | Full | once | 1 | salute, farewell |
| Search Pockets | Upper | once | 1 | search |
| Search Pockets Hold | Upper | loop | 1 | search |
| Sit Gun Handling | Full | once | 1 | fidget, bored |
| Sit Point | Full | once | 1 | point, explain |
| Sit Rub Arm | Full | once | 1 | cold, fidget |
| Slip And Recover | Full | enter-loop-exit | 1 | collapse, stumble |
| Slip Fall | Full | once | 1 | stumble, fall |
| Stumble Fall | Full | once | 2 | stumble, fall |
| Take Item | Full | once | 1 | receive, search |
| Talk Beat Mixamo | Upper | once | 3 | gesture, explain |
| Throw Overhand Wide | Full | once | 1 | throw |
| Walk Backwards Mixamo | Full | loop | 1 | locomotion |
| Walk Stroll | Full | loop | 1 | locomotion, tired |
| Walk Swagger Mixamo | Full | loop | 1 | locomotion |
| Wipe Sweat | Upper | once | 1 | tired, fidget |

### Motion Cast FREE01 (`ThirdParty/Motion Cast-FREE01`) — 20 actions, 31 variants

| Action | Slot | Plays | Variants | Cues |
|---|---|---|---|---|
| Angry Stamp | Full | once | 2 | angry |
| Applaud | Full | once | 1 | applaud, celebrate |
| Applaud Hold | Full | loop | 1 | applaud, celebrate |
| Bored Check Watch | Full | once | 1 | bored, fidget |
| Cheer Bounce | Full | loop | 1 | celebrate |
| Cheer Joyful | Full | once | 1 | celebrate |
| Cough | Upper | once | 1 | cough, fidget |
| Disgust Stink | Full | once | 3 | disgust |
| Exhausted Rest | Full | enter-loop-exit | 1 | exhausted, tired |
| Gesture Speech | Upper | once | 6 | gesture, explain, talk |
| Hungry Belly | Full | once | 1 | hungry, fidget |
| Hungry Belly Hold | Full | loop | 1 | hungry |
| Laugh Heartily | Full | once | 1 | laugh |
| Laugh Heartily Hold | Full | loop | 1 | laugh |
| Laugh Mad | Full | enter-loop-exit | 1 | laugh, threaten |
| Lost Look | Full | once | 2 | confused, search |
| Sob | Full | enter-loop-exit | 1 | cry, sad |
| Threaten Point | Upper | once | 1 | threaten, angry |
| Wave Big | Upper | once | 3 | greet, farewell |
| Wave Big Hold | Upper | loop | 1 | greet |

### Mocap Central sample (`ThirdParty/MocapCentral`) — 45 actions, 68 variants

| Action | Slot | Plays | Variants | Cues |
|---|---|---|---|---|
| Conversation Beat | Upper | once | 4 | gesture, explain, talk |
| Crawl Prone | Full | once | 1 | prone, sneak |
| Dance Freestyle | Full | loop | 1 | dance |
| Dance Groovy | Full | enter-loop-exit | 1 | dance |
| Dance Legs Kick | Full | enter-loop-exit | 2 | dance, celebrate |
| Dance Look To Side | Full | enter-loop-exit | 1 | dance |
| Dance Pump Arms | Full | enter-loop-exit | 1 | dance, celebrate |
| Dance Showpieces | Full | once | 3 | dance, celebrate |
| Drill Low | Full | loop | 1 | mine, work, repair |
| Drink Moonshine | Upper | loop | 1 | drink, drunk |
| Drop To Prone | Full | once | 1 | prone |
| Drunk Idle | Full | loop | 1 | drunk, bored |
| Excited Burst | Full | enter-loop-exit | 1 | celebrate |
| Excited Long | Full | once | 2 | celebrate |
| Frustrated | Full | once | 5 | angry, disgust |
| Hat Tip | Upper | once | 1 | greet, farewell |
| Heavy Idle | Full | loop | 1 | bored, block |
| Idle Look Around Mocap | Full | once | 1 | fidget, search |
| Idle Scratch Arm | Upper | once | 1 | fidget |
| Injured Fidget | Full | once | 3 | injured, fidget, search |
| Injured Idle | Full | loop | 1 | injured |
| Injured Turn Left | Full | once | 2 | turn, injured |
| Injured Turn Right | Full | once | 2 | turn, injured |
| Injured Walk Start | Full | once | 1 | start, injured |
| Injured Walk Stop | Full | once | 1 | stop, injured |
| Play Piano | Full | enter-loop-exit | 1 | music, work |
| Point Right Long | Upper | once | 1 | point, explain |
| Rise From Prone | Full | once | 1 | getup, prone |
| Sad Slow | Full | once | 1 | sad, cry |
| Sing | Full | once | 3 | sing |
| Spellbook Chant | Full | once | 2 | chant |
| Spellbook Draw | Full | once | 1 | draw, read |
| Spellbook Read | Full | enter-loop-exit | 2 | read, think |
| Spellbook Stow | Full | once | 1 | putdown |
| Stinky Idle | Full | once | 2 | disgust, fidget |
| This Guy | Upper | once | 1 | argue, disagree, gesture |
| Vending Fail | Full | once | 1 | operate, angry |
| Vending Insert | Full | once | 1 | operate |
| Vending Take | Full | once | 1 | operate, pickup |
| Walk Drunk Mocap | Full | loop | 1 | locomotion, drunk |
| Walk Heavy Hammer | Full | loop | 4 | locomotion |
| Walk Injured Belly | Full | loop | 1 | locomotion, injured |
| Walk Swagger Mocap | Full | loop | 1 | locomotion |
| Wave Left Hand | Upper | once | 1 | greet, farewell |
| Wounded Collapse | Full | enter-loop-exit | 1 | collapse, injured, fall |

### EEJANAI cooking (`ThirdParty/EEJANAI_Team`) — 16 actions, 18 variants

| Action | Slot | Plays | Variants | Cues |
|---|---|---|---|---|
| Blend Fruit | Upper | loop | 1 | cook, craft |
| Chop Vegetables | Upper | loop | 1 | cook, craft |
| Chop Vegetables Once | Upper | once | 1 | cook, craft |
| Cook Pan | Upper | loop | 1 | cook |
| Cook Wok | Upper | loop | 1 | cook |
| Eat Bowl Hand | Upper | loop | 1 | eat |
| Eat Bowl Meal | Upper | once | 1 | eat |
| Grill Meat | Upper | loop | 1 | cook |
| Plate Food | Upper | once | 2 | serve, cook |
| Pour Water | Upper | once | 1 | serve, drink |
| Season Food | Upper | once | 2 | cook |
| Sip Drink | Upper | once | 1 | drink |
| Stir | Upper | loop | 1 | cook |
| Stir Drink | Upper | once | 1 | cook, drink |
| Wash Vegetables | Upper | loop | 1 | cook, tend |
| Wash Vegetables Long | Upper | once | 1 | cook, tend |

### ExplosiveLLC crafter (`ThirdParty/ExplosiveLLC`) — 8 actions, 8 variants

| Action | Slot | Plays | Variants | Cues |
|---|---|---|---|---|
| Carry Idle Crate | Full | loop | 1 | carry |
| Carry Pickup Crate | Full | once | 1 | pickup, carry |
| Carry Putdown Crate | Full | once | 1 | putdown |
| Hand Over Crate | Full | once | 1 | give |
| Idle Worker | Full | loop | 1 | bored |
| Receive Crate | Full | once | 1 | receive |
| Walk Carry Crate | Full | loop | 1 | locomotion, carry |
| Walk Worker | Full | loop | 1 | locomotion |

### Kevin Iglesias Human Animations (`ThirdParty/Kevin Iglesias`) — 18 actions, 40 variants

| Action | Slot | Plays | Variants | Cues |
|---|---|---|---|---|
| Aim Rifle Hold | Upper | loop | 3 | aim |
| Death Fall Short | Full | once | 1 | fall |
| Farm Plough | Full | enter-loop-exit | 2 | farm, work, dig |
| Fish Cast | Full | once | 2 | fish, throw |
| Fish Pull Out | Full | once | 2 | fish |
| Fish Pull Out Long | Full | enter-loop-exit | 2 | fish, carry |
| Fish Reel Fight | Full | loop | 2 | fish |
| Fish Rod | Full | enter-loop-exit | 2 | fish, work |
| Gather Plants | Full | loop | 6 | gather, pickup, tend, work |
| Hammer Ground | Full | enter-loop-exit | 4 | hammer, craft, repair, work |
| Hit Damage | Upper | once | 1 | hurt |
| Idle Calm Female | Full | loop | 1 | bored |
| Idle Calm Male | Full | loop | 1 | bored |
| Mine Ground | Full | loop | 4 | mine, dig, work |
| Mine Wall | Full | loop | 4 | mine, work |
| Rifle Shoot Level | Upper | once | 1 | shoot |
| Throw Boomerang | Full | once | 1 | throw |
| Throw Spear Whole Body | Full | once | 1 | throw |
