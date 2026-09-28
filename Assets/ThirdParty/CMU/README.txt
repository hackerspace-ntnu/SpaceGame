TODO: 
- Look into Blender purge: https://studio.blender.org/training/pipeline-and-tools/blender-purge/

I did not create the animations/assets used in this. The data used in this project was obtained from mocap.cs.cmu.edu.  The database was created with funding from NSF EIA-0196217. I converted it to .fbx and .glTF from .bvh files I got from cgspeed.com with the use of Blender. I also re-targeted it to an example CC0 model with the use of a Blender add-on, Auto-Rig Pro. The model was made by Quaternius. The animations are free to use/modify/redistribute, so long as you don't just sell them as animations.

I have no affiliation with any of these mentioned parties.

For most use cases, these animations will require at least a little bit of tweaking/work. Hopefully one day they'll just be plug and play for most uses. While you can use them for general animation, or whatever you want, I did convert/modify them for use as characters in video games. They do have root motion though, which isn't usually ideal for video games. There's some tips below on how to deal with that.

One thing I actually did make, is the attached Blender script to convert everything. I love Blender dearly, but I did not enjoy making this script and don't like Blender's scripting a whole lot. Use case would be if you wanted to try to convert the animations yourself.

Quick tips if you want to use them for game characters:
-A quick way to basically get rid of the root motion of the character, is to just delete the location values of the root/hip bone. You'll probably want to leave the rotation values alone. This is not a perfect solution, and will be especially noticable for animations like crouching.
-If you intend to use the character rig included, you can install the attached Blender plugin for a few more tools/settings. For more options/tools, Auto-Rig Pro is a pretty cool Blender add-on. It normally runs for a single $40(ish) purchase.



Acknowledgements:
*Below each source is their policy on usage rights.

	Original source of animations:
	http://mocap.cs.cmu.edu:
		"How can I use this data? The motion capture data may be copied, modified, or redistributed without permission."

	Source I got them from:
	https://sites.google.com/a/cgspeed.com/cgspeed/manifesto:
		"CMU places no restrictions on the use of the original dataset, and I
		(Bruce) place no additional restrictions on the use of this particular
		BVH conversion.

		Here's the relevant paragraph from mocap.cs.cmu.edu:

  			Use this data!  This data is free for use in research and commercial
  			projects worldwide.  If you publish results obtained using this data,
  			we would appreciate it if you would send the citation to your
  			published paper to jkh+mocap@cs.cmu.edu, and also would add this text
  			to your acknowledgments section: "The data used in this project was
  			obtained from mocap.cs.cmu.edu.  The database was created with funding
  			from NSF EIA-0196217.""

	CC0 Character model by Quaternius:
	https://quaternius.com/packs/ultimatemodularcharacters.html