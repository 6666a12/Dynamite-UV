"""Surface-current overlay for the approved v5 scene; v5 beauty remains untouched.

Loads the original camera and keyed blade transforms. Black body geometry occludes
white current filaments, producing a visible-current mask for independent compositing.
"""
from pathlib import Path
import json
import math
import sys

import bpy
from mathutils import Vector

sys.path.insert(0,str(Path(__file__).resolve().parent))
import render_concepts as base
from render_blade_motion import interpolate

ROOT=base.ROOT
SOURCE=ROOT/'design/grades/v5/motion_S'
OUT=ROOT/'design/grades/v5_current/motion_S'

# Points lie in the front enamel or its recessed channel. Positive Z faces the camera.
UPPER_ROUTE=[(1.03,1.07,.210),(.44,1.08,.212),(-.36,1.095,.211),
             (-.77,.88,.212),(-.875,.57,.211),(-.58,.32,.211),(-.04,-.02,.211),(.19,-.12,.212)]
BRANCH=[(-.875,.57,.211),(-.65,.72,.211),(-.43,.81,.211)]


def emission(name,value):
    m=bpy.data.materials.new(name);m.use_nodes=True
    nt=m.node_tree;nt.nodes.clear()
    out=nt.nodes.new('ShaderNodeOutputMaterial');node=nt.nodes.new('ShaderNodeEmission')
    node.inputs['Color'].default_value=(value,value,value,1);node.inputs['Strength'].default_value=1
    nt.links.new(node.outputs[0],out.inputs['Surface'])
    return m


def make_arc(name,route,parent,mat,radius,phase):
    samples=[];distance=0
    for aa,bb in zip(route,route[1:]):
        a,b=Vector(aa),Vector(bb);length=(b-a).length
        steps=max(4,math.ceil(length/.031))
        tangent=(b-a).normalized();normal=Vector((-tangent.y,tangent.x,0))
        for i in range(steps):
            t=i/steps;samples.append((a.lerp(b,t),normal.copy(),distance+length*t))
        distance+=length
    samples.append((Vector(route[-1]),normal,distance))
    curve=bpy.data.curves.new(name,'CURVE');curve.dimensions='3D'
    curve.bevel_depth=radius;curve.bevel_resolution=2
    spline=curve.splines.new('POLY');spline.points.add(len(samples)-1)
    obj=bpy.data.objects.new(name,curve);bpy.context.collection.objects.link(obj);obj.parent=parent
    curve.materials.append(mat)
    return {'obj':obj,'samples':samples,'length':distance,'phase':phase,'radius':radius}


def animate_arc(arc,frame,amount,phase_frame):
    obj=arc['obj'];length=arc['length'];phase=arc['phase']
    # Two travelling charge packets; continuity and branch attachment are preserved.
    head=(phase_frame*.064+phase)%1
    for i,(point,normal,distance) in enumerate(arc['samples']):
        t=distance/length
        d=(t-head+.5)%1-.5
        d2=(t-head-.43+.5)%1-.5
        packet=math.exp(-.5*(d/.115)**2)+.65*math.exp(-.5*(d2/.085)**2)
        envelope=math.sin(math.pi*t)**.5
        noise=(math.sin(distance*17+phase_frame*.23+phase*3)*.52+
               math.sin(distance*53-phase_frame*.46)*.30+
               math.sin(distance*121+phase_frame*.71)*.18)
        pos=point+normal*(.031*noise*envelope)
        v=obj.data.splines[0].points[i];v.co=(*pos,1)
        v.radius=amount*(.27+.86*min(1,packet))
        v.keyframe_insert('co',frame=frame);v.keyframe_insert('radius',frame=frame)
    obj.hide_render=amount<=0
    obj.keyframe_insert('hide_render',frame=frame)


def main():
    for folder in ['current_mask','idle_mask']:(OUT/folder).mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'S_blade_motion.blend'))
    sc=bpy.context.scene
    black=emission('Occluding solid',0);white=emission('Surface current',1)
    # Keep original silhouette and depth occlusion, while excluding its radiance from this pass.
    for obj in sc.objects:
        if obj.type in {'MESH','CURVE'}:
            for slot in obj.material_slots:slot.material=black
    root=bpy.data.objects['Impact root']
    upper=bpy.data.objects['Upper assembly'];lower=bpy.data.objects['Lower assembly']
    arcs=[]
    arcs.append(make_arc('Top current',UPPER_ROUTE,upper,white,.0064,0))
    bottom=[(-x,-y,z-.09) for x,y,z in UPPER_ROUTE]
    arcs.append(make_arc('Bottom current',bottom,lower,white,.0064,.37))
    arcs.append(make_arc('Top fork',BRANCH,upper,white,.0038,.18))
    arcs.append(make_arc('Bottom fork',[(-x,-y,z-.09) for x,y,z in BRANCH],lower,white,.0038,.55))
    sc.view_settings.view_transform='Standard';sc.view_settings.look='None';sc.view_settings.exposure=0
    sc.cycles.samples=12
    sc.frame_end=54
    manifest={'fps':24,'intro_frames':30,'idle_loop_frames':24,
              'base':'../../v5/motion_S','body_lightning_style':'thin current following enamel paths',
              'intro':[]}
    for f in range(1,31):
        sc.frame_set(f)
        amount=interpolate(f,[(1,0),(3,.1),(5,.45),(7,.9),(8,1.3),(10,1.15),(16,.9),(23,.78),(30,.72)])
        for arc in arcs:animate_arc(arc,f,amount,f)
        sc.render.filepath=str(OUT/'current_mask'/f'S_{f:04d}.png')
        bpy.ops.render.render(write_still=True)
        manifest['intro'].append({'frame':f,'amount':amount})
        print(f'SURFACE_CURRENT {f}/30',flush=True)
    # Separate optional rest loop: fixed body, no repeating slam or brightness flash.
    for i in range(24):
        f=31+i;sc.frame_set(f)
        phase_frame=30+i*(1/(24*.064))
        # Loopable deformations use a circular phase; wrap is continuous.
        angle=math.tau*i/24
        for arc in arcs:
            obj=arc['obj'];length=arc['length'];phase=arc['phase']
            head=(.064*30+i/24+phase)%1
            for j,(point,normal,distance) in enumerate(arc['samples']):
                t=distance/length;d=(t-head+.5)%1-.5
                packet=math.exp(-.5*(d/.12)**2)
                jitter=(math.sin(distance*17+angle+phase*3)*.56+math.sin(distance*53-angle)*.3+math.sin(distance*121+angle*2)*.14)
                pos=point+normal*(.025*jitter*math.sin(math.pi*t)**.5)
                v=obj.data.splines[0].points[j];v.co=(*pos,1);v.radius=.72*(.35+.8*packet)
                v.keyframe_insert('co',frame=f);v.keyframe_insert('radius',frame=f)
            obj.hide_render=False;obj.keyframe_insert('hide_render',frame=f)
        sc.render.filepath=str(OUT/'idle_mask'/f'S_{i+1:04d}.png')
        bpy.ops.render.render(write_still=True)
        print(f'IDLE_CURRENT {i+1}/24',flush=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'S_surface_current.blend'))
    (OUT/'current.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')


if __name__=='__main__':main()
