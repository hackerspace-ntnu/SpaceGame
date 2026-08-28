import bpy, sys
from mathutils import Matrix
action=sys.argv[sys.argv.index('--')+1]
arm=bpy.data.objects["ConjurerRig"]
arm.animation_data.action=bpy.data.actions[action]
sc=bpy.context.scene
print(f"{'f':>3} {'hipX':>7}{'hipZ':>7} {'kneeX':>7}{'kneeZ':>7} {'ankX':>7}{'ankZ':>7} {'toeX':>7}{'toeZ':>7}  {'knee-vs-line':>12}")
for f in range(1,42,4):
    sc.frame_set(f)
    bpy.context.view_layer.update()
    th=arm.pose.bones["Hip_L"]; sh=arm.pose.bones["Knee_L"]; ft=arm.pose.bones["Ankle_L"]
    hip=th.head.copy(); knee=th.tail.copy(); ank=sh.tail.copy(); toe=ft.tail.copy()
    # signed forward offset of knee from the straight hip->ankle line, in +X
    t=(knee.z-hip.z)/(ank.z-hip.z) if abs(ank.z-hip.z)>1e-6 else 0
    lineX=hip.x+t*(ank.x-hip.x)
    off=knee.x-lineX
    tag="FORWARD(human)" if off>0.05 else ("BACKWARD(reverse)" if off<-0.05 else "straight")
    print(f"{f:3d} {hip.x:7.2f}{hip.z:7.2f} {knee.x:7.2f}{knee.z:7.2f} {ank.x:7.2f}{ank.z:7.2f} {toe.x:7.2f}{toe.z:7.2f}  {off:7.2f} {tag}")
