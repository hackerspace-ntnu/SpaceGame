import bpy, math
arm=bpy.data.objects["ConjurerRig"]
arm.animation_data.action=bpy.data.actions["Walk"]
sc=bpy.context.scene
SCALE=(3.019*6)/(37.49-2.757)   # 6x player height
FPS=30.0
prev=None; vels=[]
samples=[]
for f in range(1,74):
    sc.frame_set(f); bpy.context.view_layer.update()
    sh=arm.pose.bones["Shin.L"]; ft=arm.pose.bones["Foot.L"]
    ank=sh.tail.copy(); toe=ft.tail.copy()
    contact=min(ank.z, toe.z)          # lowest point of the foot
    samples.append((f, ank.x, ank.z, toe.x, toe.z, contact))
# planted = frames where this foot's lowest point is near the floor
floor=min(s[5] for s in samples)
planted=[s for s in samples if s[5] < floor+0.35]
print(f"floor={floor:.2f}  planted frames: {[s[0] for s in planted]}")
xs=[(s[0],s[1]) for s in planted]
# contiguous run velocities
for (f0,x0),(f1,x1) in zip(xs, xs[1:]):
    if f1-f0==1:
        vels.append((x0-x1)*FPS)   # backward travel per second, blender units
if vels:
    import statistics
    m=statistics.mean(vels)
    print(f"stance samples={len(vels)}  mean backward foot speed = {m:.2f} u/s = {m*SCALE:.2f} m/s")
    print(f"  min {min(vels)*SCALE:.2f}  max {max(vels)*SCALE:.2f} m/s   (spread = skating)")
