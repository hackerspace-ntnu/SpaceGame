import bpy, math
from mathutils import Vector
arm=bpy.data.objects["ConjurerRig"]
arm.animation_data.action=bpy.data.actions["Walk"]
sc=bpy.context.scene
SCALE=(3.019*6)/(37.49-2.757)   # 6x player height now
FPS=30.0
N=72
dg=bpy.context.evaluated_depsgraph_get()
def lowest(objname):
    o=bpy.data.objects[objname]
    ev=o.evaluated_get(bpy.context.evaluated_depsgraph_get()); me=ev.to_mesh()
    z=min((o.matrix_world@v.co).z for v in me.vertices); ev.to_mesh_clear(); return z
rows=[]
for f in range(1,N+2):
    sc.frame_set(f); bpy.context.view_layer.update()
    rows.append((f, lowest("LeftFoot"), lowest("RightFoot")))
gmin=min(min(r[1],r[2]) for r in rows)
print(f"ground = {gmin:.3f}   (threshold {gmin+0.25:.3f})")
def contact_frame(idx):
    """first frame of each planted run, scanning cyclically"""
    planted=[r[0] for r in rows if r[idx] <= gmin+0.25]
    runs=[]; 
    for f in planted:
        if runs and f==runs[-1][-1]+1: runs[-1].append(f)
        else: runs.append([f])
    # merge a run that wraps around the loop seam
    if len(runs)>1 and runs[0][0]==1 and runs[-1][-1]==N+1:
        runs[0]=runs[-1]+runs[0]; runs.pop()
    return runs
for name,idx in (("LeftFoot",1),("RightFoot",2)):
    runs=contact_frame(idx)
    print(f"{name}: stance runs {[ (r[0],r[-1]) for r in runs ]}")
    for r in runs:
        f=r[0]
        print(f"    contact at frame {f}  -> t = {(f-1)/FPS:.4f}s")
print("\nper-frame lowest point (L, R):")
for f,l,rr in rows:
    mark = "  <L" if l<=gmin+0.25 else "    "
    mark += " R" if rr<=gmin+0.25 else "  "
    print(f"  f{f:3d}  L={l:6.2f}  R={rr:6.2f}{mark}")
