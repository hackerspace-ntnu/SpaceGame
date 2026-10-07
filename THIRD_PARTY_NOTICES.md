# Third-party notices

Assets in this repository that were not made here, with the licence each one carries. Keep
this file current: a licence with an attribution clause is a promise the shipped game has to
keep, and the credits line below is part of that promise.

## Red Planet Rampage — the Clanker robot

**What:** the robot body `YiiHaw.fbx` (the "Y11-H4W" character) and its nine locomotion clips
(`rig.001_{idle,Walk,back,SideStepLeft,SideStepRight,CrouchForward,CrouchIdle,CrouchRight,Leap}.anim`),
under [`Assets/ThirdParty/RedPlanetRampage/`](Assets/ThirdParty/RedPlanetRampage/). Used as
the body of the Clanker faction. Materials, shaders, textures, controllers and code from that
project are **not** included; the body is dressed from this project's own palette.

**From:** <https://github.com/hackerspace-ntnu/Red-Planet-Rampage>, commit
`25835ac03389d0be948abeda8c31f68965d682b9` (2025-11-09), with the developers' permission.

**Licence:** BSD 4-Clause, Copyright (c) 2022, Hackerspace NTNU. Full text in
[`Assets/ThirdParty/RedPlanetRampage/LICENSE.md`](Assets/ThirdParty/RedPlanetRampage/LICENSE.md).

**What the licence asks of us:**

1. Keep the copyright notice and the licence text with the assets (done: the file above).
2. Reproduce the notice in the documentation or materials shipped with a build (this file
   ships with the game).
3. Any advertising material that mentions the Clankers must carry this line, verbatim:

   > This product includes software developed by Prosjekt: Spill, of Hackerspace NTNU.

   There is no credits screen in the game yet (2026-09-07); when one exists, this line goes on it.
4. Do not use Hackerspace NTNU's name or its contributors' names to endorse this game without
   written permission.

**Lore carried over, with thanks:** the robots were sent to terraform Mars and received
western films in place of their instructions. In SpaceGame they have since moved on to this
planet — see `docs/superpowers/specs/2026-09-07-faction-system-design.md` §3.7.

## Kenney — Sci-Fi Sounds

**What:** `lowFrequency_explosion_001.ogg`, shipped as
[`Assets/StreamingAssets/Audio/storm_ward_pulse.ogg`](Assets/StreamingAssets/Audio/storm_ward_pulse.ogg),
the storm ward's pulse.

**From:** Kenney, "Sci-Fi Sounds" 1.0 (2020-10-11), <https://kenney.nl/assets/sci-fi-sounds>,
downloaded 2026-09-17.

**Licence:** Creative Commons Zero (CC0 1.0), <http://creativecommons.org/publicdomain/zero/1.0/>.
Nothing is owed; crediting Kenney (www.kenney.nl) is invited, not required.

## Quaternius — Universal Animation Library

**What:** `UAL1_Standard.fbx`, the in-place edition of the Universal Animation Library 1
(Standard): 42 humanoid clips on a mannequin, used through Humanoid retargeting for the player
and every humanoid NPC. Under
[`Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/`](Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/)
with the pack's own `License.txt` and `README.txt`. The root-motion copy (`UAL1_Standard_RM.fbx`)
and the Godot/Unreal files are not included.

**From:** Quaternius, <https://quaternius.itch.io/universal-animation-library>, downloaded
2026-09-25.

**Licence:** Creative Commons Zero (CC0 1.0), <https://creativecommons.org/publicdomain/zero/1.0/>.
Nothing is owed; crediting Quaternius is invited, not required.

## CMU Graphics Lab Motion Capture Database

**What:** a curated subset of the CMU motion-capture takes, as FBX retargeted to a CC0 Quaternius
mannequin, under [`Assets/ThirdParty/CMU/`](Assets/ThirdParty/CMU/) (`Takes/` plus the `cuts.json`
that names the clips cut from each). The full pack is kept Unity-invisible and git-ignored at
`Assets/Game/Art/Animations/_Packed~/` and is not part of the repository.

**From:** mocap.cs.cmu.edu, via the cgspeed.com BVH conversion and a community FBX conversion
(the pack's own `README.txt` and `LICENSE.txt` are kept beside the takes).

**Licence:** "The motion capture data may be copied, modified, or redistributed without
permission" and "You may include this data in commercially-sold products, but you may not resell
this data directly, even in converted form." Anyone we pass the takes on to must be told the same.

**Credit (requested by CMU):** "The data used in this project was obtained from mocap.cs.cmu.edu.
The database was created with funding from NSF EIA-0196217." — goes on the credits screen.
