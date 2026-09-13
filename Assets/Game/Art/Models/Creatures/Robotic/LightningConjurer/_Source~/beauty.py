import bpy, math, sys, os
from mathutils import Vector, Matrix
argv=sys.argv[sys.argv.index('--')+1:]
out, action, frames = argv[0], argv[1], [int(x) for x in argv[2].split(',')]
sc=bpy.context.scene
sc.render.engine='BLENDER_EEVEE'
sc.render.resolution_x=520; sc.render.resolution_y=760
sc.eevee.taa_render_samples=48
sc.view_settings.view_transform='Standard'
sc.view_settings.look='None'
try: sc.eevee.use_raytracing=True
except Exception: pass

arm=bpy.data.objects["ConjurerRig"]
if action=='REST':
    if arm.animation_data: arm.animation_data.action=None
    for pb in arm.pose.bones: pb.matrix_basis=Matrix.Identity(4)
else:
    arm.animation_data.action=bpy.data.actions[action]
keep={arm.name}
def d(o):
    for c in o.children: keep.add(c.name); d(c)
d(arm)
for o in bpy.data.objects:
    if o.type in {'MESH','CURVE'}: o.hide_render = o.name not in keep

# make the palette's emissive materials actually emit for this preview only
for name,power in (("Mat_Emissive_Portal_Blue", 1.3),):
    m=bpy.data.materials.get(name)
    if m and m.node_tree:
        b=next((n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None)
        if b:
            b.inputs['Emission Color'].default_value = b.inputs['Base Color'].default_value
            b.inputs['Emission Strength'].default_value = power

# ground + lights
bpy.ops.mesh.primitive_plane_add(size=400, location=(0,0,2.757))
gp=bpy.context.object
gm=bpy.data.materials.new("GroundPreview"); gm.use_nodes=True
gm.node_tree.nodes["Principled BSDF"].inputs['Base Color'].default_value=(0.05,0.05,0.06,1)
gm.node_tree.nodes["Principled BSDF"].inputs['Roughness'].default_value=0.9
gp.data.materials.append(gm)
w=bpy.data.worlds.new("W"); sc.world=w; w.use_nodes=True
w.node_tree.nodes["Background"].inputs[0].default_value=(0.035,0.04,0.055,1)
w.node_tree.nodes["Background"].inputs[1].default_value=1.0
def lamp(name,loc,energy,size,color=(1,1,1)):
    ld=bpy.data.lights.new(name,'AREA'); ld.energy=energy; ld.size=size; ld.color=color
    lo=bpy.data.objects.new(name,ld); sc.collection.objects.link(lo)
    lo.location=loc
    lo.rotation_euler=(Vector((0.3,-0.3,22))-Vector(loc)).to_track_quat('-Z','Y').to_euler()
    return lo
lamp("key",(30,-38,58),42000,26)
lamp("fill",(-42,-20,30),14000,30,(0.65,0.75,1.0))
lamp("rim",(-16,42,46),30000,22,(0.55,0.8,1.0))

ctr=Vector((0.3,-0.3,26)); size=54
cd=bpy.data.cameras.new("C"); cam=bpy.data.objects.new("C",cd)
sc.collection.objects.link(cam); sc.camera=cam; cd.type='PERSP'; cd.lens=70
loc=ctr+Vector((1.5,-1.15,0.22)).normalized()*size*2.5
cam.location=loc; cam.rotation_euler=(ctr-loc).to_track_quat('-Z','Y').to_euler()
for f in frames:
    if action!='REST': sc.frame_set(f)
    sc.render.filepath=os.path.join(out,f"b_{action}_{f:03d}.png")
    bpy.ops.render.render(write_still=True)
print("rendered")
