"""V6 S studies: actual moving lights and surface-anchored continuous electricity.

Original blade geometry, no particles, radial spikes, detached fragments or rings.
Blender 5.2: python render_light_studies.py -- --variant all
"""
from pathlib import Path
import argparse
import json
import math
import sys

import bpy
from mathutils import Vector

sys.path.insert(0,str(Path(__file__).resolve().parent))
import render_concepts as base
from render_blade_motion import interpolate

OUT=base.ROOT/'design/grades/v6'


def pure_emission(name, color, strength):
    mat=bpy.data.materials.new(name)
    mat.use_nodes=True
    nt=mat.node_tree;nt.nodes.clear()
    out=nt.nodes.new('ShaderNodeOutputMaterial')
    node=nt.nodes.new('ShaderNodeEmission')
    node.inputs['Color'].default_value=(*base.linear(color),1)
    node.inputs['Strength'].default_value=strength
    nt.links.new(node.outputs[0],out.inputs['Surface'])
    return mat,node


def anchored_arc(name, guides, parent, mat):
    """Piecewise route with shared segment ends; every strand is one continuous curve."""
    samples=[]
    for i,(a,b) in enumerate(zip(guides,guides[1:])):
        a,b=Vector(a),Vector(b)
        n=max(8,math.ceil((b-a).length/.05))
        for j in range(n):
            t=j/n
            samples.append((a.lerp(b,t),i+t))
    samples.append((Vector(guides[-1]),len(guides)-1))
    obj=base.rail(name,[tuple(p) for p,t in samples],.005,mat)
    obj.parent=parent
    return obj,samples


def set_arc(arc,frame,amplitude,branch=False):
    obj,samples=arc
    for index,(point,t) in enumerate(samples):
        p=point.copy()
        # Fixed geometry knots, with smoothly moving deformation, no independent random jumps.
        envelope=math.sin(math.pi*(t%1))**.7
        wave=math.sin(t*45+frame*.65)*.59+math.sin(t*111-frame*.42)*.28+math.sin(t*223+frame*.29)*.13
        p.x+=wave*amplitude*envelope
        p.y+=math.sin(t*73+frame*.52)*amplitude*.85*envelope
        p.z+=abs(wave)*amplitude*.24
        obj.data.splines[0].points[index].co=(*p,1)
    obj.data.bevel_depth=.0035 if branch else .0055
    obj.data.keyframe_insert('bevel_depth',frame=frame)
    for point in obj.data.splines[0].points:
        point.keyframe_insert('co',frame=frame)


def point_light(name,loc,color,energy,radius,parent):
    d=bpy.data.lights.new(name,'POINT');d.energy=energy;d.color=base.linear(color);d.shadow_soft_size=radius
    obj=bpy.data.objects.new(name,d);bpy.context.collection.objects.link(obj)
    obj.location=loc;obj.parent=parent
    return obj


def mask_render(sc,objects,glow_names,path):
    """Visible emissive surfaces are white, the rest opaque black; lights cannot leak in."""
    white,_=pure_emission('Mask white','#FFFFFF',1)
    black,_=pure_emission('Mask black','#000000',0)
    saved=[]
    for obj in objects:
        for slot in obj.material_slots:
            saved.append((slot,slot.material))
            slot.material=white if slot.material.name in glow_names else black
    old=(sc.view_settings.view_transform,sc.view_settings.look,sc.view_settings.exposure,sc.cycles.samples)
    sc.view_settings.view_transform='Standard';sc.view_settings.look='None';sc.view_settings.exposure=0
    sc.cycles.samples=8;sc.render.filepath=str(path)
    bpy.ops.render.render(write_still=True)
    for slot,mat in saved:slot.material=mat
    sc.view_settings.view_transform,sc.view_settings.look,sc.view_settings.exposure,sc.cycles.samples=old
    bpy.data.materials.remove(white);bpy.data.materials.remove(black)


def render_variant(variant,selected):
    dest=OUT/variant
    for folder in ['body','source']:(dest/folder).mkdir(parents=True,exist_ok=True)
    sc=base.setup(64);p=base.palette();base.blades(p)
    sc.render.resolution_x=sc.render.resolution_y=512
    sc.render.fps=24;sc.frame_start=1;sc.frame_end=30
    sc.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.13
    # Metallic highlights have room to move over dark surfaces.
    for key in ['cyan','light','edge']:
        bsdf=p[key].node_tree.nodes.get('Principled BSDF')
        bsdf.inputs['Roughness'].default_value=.19 if key=='edge' else .23
        bsdf.inputs['Metallic'].default_value=.82 if key=='edge' else .63
    for obj in sc.objects:
        if obj.type=='LIGHT':obj.data.energy*=.65
    root=bpy.data.objects.new('Grade motion',None);sc.collection.objects.link(root)
    groups=[]
    for name in ['Upper blade','Lower blade']:
        g=bpy.data.objects.new(name,None);sc.collection.objects.link(g);g.parent=root;groups.append(g)
    for obj in list(sc.objects):
        if obj.type not in {'MESH','CURVE'}:continue
        lower=obj.name.endswith('1') or obj.name.startswith(('Bottom','Lower'))
        obj.parent=groups[int(lower)]
    # The joint contains a small recessed light source, not a screen-space star.
    core,core_node=pure_emission('Junction light','#63EDFF',5)
    rail=base.rail('Lit assembly seam',[(.135,-.22,.212),(.24,-.115,.255),(.36,-.035,.225)],.012,core)
    rail.parent=root
    contact_light=point_light('Joint illumination',(.24,-.11,.36),'#3DE5FF',10,.11,root)
    base.light('Sweeping white strip',(-3,1,3),700,'#B7F8FF',.9,4)
    sweep=bpy.data.objects['Sweeping white strip']
    base.light('Opposite pink reflection',(3,-2,2),170,'#FF5CAA',1.0,3)
    pink=bpy.data.objects['Opposite pink reflection']
    arcs=[];arc_node=None
    if variant=='electric':
        arc_mat,arc_node=pure_emission('Continuous plasma','#B8F5FF',14)
        arcs.append((anchored_arc('Top blade discharge',[(1.31,1.17,.255),(.76,1.27,.31),(-.49,1.25,.28),(-1.12,.55,.29),(-.60,.20,.30),(.23,-.10,.32)],root,arc_mat),False))
        arcs.append((anchored_arc('Lower blade discharge',[(.23,-.10,.32),(.90,-.51,.26),(.47,-1.25,.24),(-.75,-1.19,.235),(-1.28,-1.14,.22)],root,arc_mat),False))
        # Connected forks start on the main conductor; their ends land on the metal.
        arcs.append((anchored_arc('Upper connected branch',[(-1.12,.55,.29),(-.68,.72,.28),(-.35,.83,.235)],root,arc_mat),True))
        arcs.append((anchored_arc('Lower connected branch',[(.90,-.51,.26),(.48,-.70,.25),(.31,-.86,.15)],root,arc_mat),True))
    objects=[o for o in sc.objects if o.type in {'MESH','CURVE'}]
    glow_names={p['energy'].name,p['pink'].name,core.name}
    if variant=='electric':glow_names.add('Continuous plasma')
    meta={'variant':variant,'fps':24,'frames':30,'impact':8,'particles':False,'rings':False,'frames_meta':[]}
    for f in range(1,31):
        sc.frame_set(f)
        scale=interpolate(f,[(1,1.04),(5,1.04),(7,1.02),(8,.965),(10,1.02),(16,1),(30,1)])
        root.scale=(scale,)*3
        root.rotation_euler.z=math.radians(interpolate(f,[(1,-4),(6,-3),(8,1.5),(11,-.5),(16,0)]))
        root.location.y=interpolate(f,[(1,.1),(6,.1),(8,-.02),(12,0)])
        for key in ['scale','rotation_euler','location']:root.keyframe_insert(key,frame=f)
        separation=interpolate(f,[(1,.07),(5,.055),(7,.025),(8,0),(10,.01),(16,0)])
        for i,g in enumerate(groups):
            direction=1 if i==0 else -1
            g.location=(direction*separation,direction*separation*.35,0)
            g.keyframe_insert('location',frame=f)
        pulse=interpolate(f,[(1,.04),(5,.2),(7,.58),(8,1),(9,.92),(11,.73),(15,.40),(21,.12),(25,0),(30,0)])
        # Physically moving area light; reflection travel is visible on the faces.
        sweep.location=(interpolate(f,[(1,-3.4),(6,-2.4),(8,-1.2),(13,1.0),(18,2.8),(24,3.4),(30,3.4)]),
                        interpolate(f,[(1,2.3),(8,1.4),(15,-.6),(24,-1.6),(30,-1.6)]),2.7)
        sweep.rotation_euler=(Vector((0,0,0))-sweep.location).to_track_quat('-Z','Y').to_euler()
        sweep.data.energy=420+1500*pulse
        pink.data.energy=190+100*pulse
        contact_light.data.energy=9+95*pulse
        core_node.inputs['Strength'].default_value=3+25*pulse
        for obj in [sweep,pink,contact_light]:
            obj.keyframe_insert('location',frame=f);obj.keyframe_insert('rotation_euler',frame=f)
            obj.data.keyframe_insert('energy',frame=f)
        core_node.inputs['Strength'].keyframe_insert('default_value',frame=f)
        p['energy'].node_tree.nodes.get('Principled BSDF').inputs['Emission Strength'].default_value=3+11*pulse
        p['energy'].node_tree.nodes.get('Principled BSDF').inputs['Emission Strength'].keyframe_insert('default_value',frame=f)
        if variant=='electric':
            active=4<=f<=23
            electric_power=interpolate(f,[(1,0),(3,0),(4,.1),(6,.55),(8,1),(10,.92),(14,.68),(18,.44),(21,.22),(24,0),(30,0)])
            # A continuous current persists through the discharge instead of flying outward.
            arc_node.inputs['Strength'].default_value=7+35*electric_power
            arc_node.inputs['Strength'].keyframe_insert('default_value',frame=f)
            for arc,branch in arcs:
                arc[0].hide_render=not active;arc[0].keyframe_insert('hide_render',frame=f)
                set_arc(arc,f,.045 if not branch else .027,branch)
            contact_light.data.energy=14+120*pulse
            contact_light.data.keyframe_insert('energy',frame=f)
        bpy.context.view_layer.update()
        meta['frames_meta'].append({'frame':f,'pulse':pulse,'electric_power':electric_power if variant=='electric' else 0})
        if f in selected:
            sc.render.filepath=str(dest/'body'/f'S_{f:04d}.png');bpy.ops.render.render(write_still=True)
            mask_render(sc,objects,glow_names,dest/'source'/f'S_{f:04d}.png')
            print(f'{variant} {f}/30',flush=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(dest/'S.blend'))
    (dest/'motion.json').write_text(json.dumps(meta,indent=2)+'\n',encoding='utf-8')


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--variant',choices=['all','lamp','electric'],default='all')
    parser.add_argument('--frames',default='all')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    selected=set(range(1,31)) if args.frames=='all' else set(map(int,args.frames.split(',')))
    for variant in (['lamp','electric'] if args.variant=='all' else [args.variant]):render_variant(variant,selected)


if __name__=='__main__':main()
