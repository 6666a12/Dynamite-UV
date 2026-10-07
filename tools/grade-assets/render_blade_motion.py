"""Render the selected S blade study: beauty frames plus projected emitter paths."""
from pathlib import Path
import argparse
import json
import math
import sys

import bpy
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

sys.path.insert(0,str(Path(__file__).resolve().parent))
import render_concepts as base

OUT=base.ROOT/'design/grades/v4/motion_S'
FPS=24
END=30
IMPACT=8


def interpolate(frame, keys):
    for i,(f,value) in enumerate(keys):
        if frame <= f:
            if i==0:return value
            pf,pv=keys[i-1]
            t=(frame-pf)/(f-pf)
            return pv+(value-pv)*t
    return keys[-1][1]


def main():
    global OUT
    parser=argparse.ArgumentParser()
    parser.add_argument('--bright',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    if args.bright:OUT=base.ROOT/'design/grades/v5/motion_S'
    (OUT/'body').mkdir(parents=True,exist_ok=True)
    sc=base.setup(48)
    palette=base.palette()
    base.blades(palette)
    mask_materials={}
    if args.bright:
        (OUT/'emission_mask').mkdir(exist_ok=True)
        for key,m in palette.items():
            mat=bpy.data.materials.new('Emission mask '+key)
            mat.use_nodes=True
            nt=mat.node_tree
            nt.nodes.clear()
            out=nt.nodes.new('ShaderNodeOutputMaterial')
            em=nt.nodes.new('ShaderNodeEmission')
            value=1 if key in ('edge','energy') else 0
            em.inputs['Color'].default_value=(value,value,value,1)
            em.inputs['Strength'].default_value=1
            nt.links.new(em.outputs[0],out.inputs['Surface'])
            mask_materials[m.name]=mat
        for key in ('edge','energy'):
            node=palette[key].node_tree.nodes.get('Principled BSDF')
            node.inputs['Emission Color'].default_value=(*base.linear('#22DFFF'),1)
    sc.render.resolution_x=sc.render.resolution_y=512
    sc.render.fps=FPS
    sc.frame_start=1
    sc.frame_end=END
    groups=[]
    root=bpy.data.objects.new('Impact root',None)
    sc.collection.objects.link(root)
    for name in ['Upper assembly','Lower assembly']:
        g=bpy.data.objects.new(name,None)
        sc.collection.objects.link(g)
        g.parent=root
        groups.append(g)
    for obj in list(sc.objects):
        if obj.type not in {'MESH','CURVE'}:continue
        lower=obj.name.endswith('1') or obj.name.startswith(('Bottom','Lower'))
        obj.parent=groups[1 if lower else 0]
    emission_curves=[o for o in sc.objects if o.type=='CURVE']
    metadata={'fps':FPS,'impact_frame':IMPACT,'end_frame':END,'frames':[],
              'lighting':'bright v5' if args.bright else 'subtle v4'}
    for f in range(1,END+1):
        sc.frame_set(f)
        scale=interpolate(f,[(1,1.06),(4,1.06),(7,1.02),(8,.955),(10,1.025),(15,1),(30,1)])
        angle=interpolate(f,[(1,-5),(5,-4),(7,-2),(8,1.8),(11,-.55),(16,0)])
        root.scale=(scale,scale,scale)
        root.rotation_euler=(0,0,math.radians(angle))
        root.location.y=interpolate(f,[(1,.10),(5,.10),(8,-.025),(11,.009),(16,0)])
        root.keyframe_insert('scale',frame=f)
        root.keyframe_insert('rotation_euler',frame=f)
        root.keyframe_insert('location',frame=f)
        separation=interpolate(f,[(1,.085),(5,.07),(7,.03),(8,0),(10,.012),(15,0)])
        for i,g in enumerate(groups):
            sign=1 if i==0 else -1
            g.location=(separation*sign,separation*.38*sign,0)
            g.keyframe_insert('location',frame=f)
        if args.bright:
            power=interpolate(f,[(1,.15),(5,.7),(7,1.6),(8,18),(9,12),(11,6),(15,3),(22,2.2),(30,2.2)])
            for key,factor in [('edge',1),('energy',4),('pink',1.5)]:
                socket=palette[key].node_tree.nodes.get('Principled BSDF').inputs['Emission Strength']
                socket.default_value=power*factor
                socket.keyframe_insert('default_value',frame=f)
        bpy.context.view_layer.update()
        paths=[]
        for o in emission_curves:
            path=[]
            for v in o.data.splines[0].points:
                p=world_to_camera_view(sc,sc.camera,o.matrix_world@Vector(v.co[:3]))
                path.append([p.x*512,(1-p.y)*512])
            paths.append(path)
        projected=world_to_camera_view(sc,sc.camera,root.matrix_world@Vector((.07,-.035,.21)))
        metadata['frames'].append({'frame':f,'paths':paths,'contact':[projected.x*512,(1-projected.y)*512]})
        sc.render.filepath=str(OUT/'body'/f'S_{f:04d}.png')
        bpy.ops.render.render(write_still=True)
        if args.bright:
            saved=[]
            for obj in sc.objects:
                if obj.type not in {'MESH','CURVE'}:continue
                for slot in obj.material_slots:
                    saved.append((slot,slot.material))
                    slot.material=mask_materials[slot.material.name]
            view=(sc.view_settings.view_transform,sc.view_settings.look,sc.view_settings.exposure)
            sc.view_settings.view_transform='Standard'
            sc.view_settings.look='None'
            sc.view_settings.exposure=0
            sc.cycles.samples=8
            sc.render.filepath=str(OUT/'emission_mask'/f'S_{f:04d}.png')
            bpy.ops.render.render(write_still=True)
            for slot,mat in saved:slot.material=mat
            sc.view_settings.view_transform,sc.view_settings.look,sc.view_settings.exposure=view
            sc.cycles.samples=48
        print(f'BODY_FRAME {f}/{END}',flush=True)
    sc.frame_set(30)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'S_blade_motion.blend'))
    (OUT/'motion.json').write_text(json.dumps(metadata,indent=2),encoding='utf-8')


if __name__=='__main__':
    main()
