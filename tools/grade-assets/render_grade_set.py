"""Complete the v5 blade family with original Omega/A/B/C meshes and current masks."""
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
from render_blade_motion import interpolate
from render_surface_current import make_arc,animate_arc,emission

OUT=base.ROOT/'design/grades/v5_current/set'
GRADES={'omega':'#ff4d8f','A':'#fbbf24','B':'#38bdf8','C':'#7c88b0'}


def band(path,width):
    points=[Vector(v) for v in path];left=[];right=[]
    for i,p in enumerate(points):
        before=(p-points[i-1]).normalized() if i else (points[1]-p).normalized()
        after=(points[i+1]-p).normalized() if i<len(points)-1 else before
        tangent=(before+after).normalized();normal=Vector((-tangent.y,tangent.x))
        offset=width*.5/max(.65,normal.dot(Vector((-after.y,after.x))))
        left.append(tuple(p+normal*offset));right.append(tuple(p-normal*offset))
    return left+list(reversed(right))


def setup_palette(color):
    p=base.palette();rgb=base.linear(color)
    for key,factor in [('light',1),('cyan',.54),('deep',.16)]:
        node=p[key].node_tree.nodes.get('Principled BSDF')
        node.inputs['Base Color'].default_value=(*(v*factor for v in rgb),1)
    for key in ['edge','energy']:
        node=p[key].node_tree.nodes.get('Principled BSDF')
        node.inputs['Emission Color'].default_value=(*rgb,1)
    return p


def part(name,poly,p,parent,z=.10):
    outer=base.prism(name,poly,-.22,z,[p['dark'],p['edge'],p['edge']],.030);outer.parent=parent
    return outer


def stroked(name,path,width,p,parent,z=.10):
    part(name,band(path,width),p,parent,z)
    enamel=base.prism(name+' enamel',band(path,width*.66),z+.007,z+.026,[p['deep'],p['cyan'],p['deep']],.009)
    enamel.parent=parent
    return [(x,y,z+.045) for x,y in path]


def plate(name,poly,p,parent,z=.10):
    part(name,poly,p,parent,z)
    cx=sum(v[0] for v in poly)/len(poly);cy=sum(v[1] for v in poly)/len(poly)
    inset=[(cx+(x-cx)*.80,cy+(y-cy)*.92) for x,y in poly]
    enamel=base.prism(name+' enamel',inset,z+.006,z+.025,[p['deep'],p['cyan'],p['deep']],.009);enamel.parent=parent


def build(key,p,groups):
    paths=[]
    if key=='omega':
        points=[(-.70,-.68),(-.96,-.13),(-.94,.52),(-.48,1.03),(.15,1.22),(.73,.96),(.94,.35),(.73,-.42),(.51,-.72)]
        paths.append((stroked('Crowned horseshoe',points,.41,p,groups[0],.12),groups[0]))
        points=[(-1.20,-1.15),(-.41,-1.15),(-.55,-.73)]
        paths.append((stroked('Left blade foot',points,.34,p,groups[1],.14),groups[1]))
        points=[(.54,-.68),(.36,-1.12),(1.20,-1.12)]
        paths.append((stroked('Right blade foot',points,.34,p,groups[1],.10),groups[1]))
        plate('Crown inlay',[(-.14,1.08),(.02,1.41),(.26,1.24),(.35,1.03)],p,groups[0],.19)
    elif key=='A':
        plate('Ascending blade',[(-1.29,-1.23),(-.74,-1.23),(.30,.90),(.49,1.32),(-.02,1.29)],p,groups[0],.14)
        plate('Descending blade',[(.33,1.24),(.11,.53),(.63,-1.22),(1.21,-1.22)],p,groups[1],.08)
        plate('Cross lock',[(-.55,-.28),(.77,-.23),(.94,-.57),(-.72,-.68)],p,groups[1],.20)
        paths=[([(-.96,-1.08,.185),(-.50,-.03,.185),(.16,1.13,.185)],groups[0]),
               ([(.31,.81,.125),(.57,-.14,.125),(.89,-1.07,.125)],groups[1]),
               ([(-.51,-.51,.247),(.15,-.39,.247),(.69,-.38,.247)],groups[1])]
    elif key=='B':
        plate('Slanted spine',[(-.97,-1.28),(-.54,-1.16),(-.23,1.24),(-.69,1.30)],p,groups[0],.14)
        top=[(-.49,1.11),(.37,1.11),(.80,.80),(.68,.46),(.29,.13),(-.43,.13)]
        low=[(-.42,-.02),(.42,-.02),(.91,-.40),(.78,-.91),(.36,-1.12),(-.71,-1.12)]
        paths.append((stroked('Upper angular bowl',top,.32,p,groups[0],.10),groups[0]))
        paths.append((stroked('Lower angular bowl',low,.36,p,groups[1],.06),groups[1]))
        paths.append(([(-.73,-1.10,.188),(-.58,-.02,.188),(-.45,1.09,.188)],groups[0]))
    elif key=='C':
        top=[(1.22,.99),(.63,1.23),(-.43,1.18),(-1.00,.58),(-1.00,.02)]
        low=[(-.99,-.11),(-.82,-.77),(-.34,-1.17),(.66,-1.15),(1.19,-.77)]
        paths.append((stroked('Upper hooked blade',top,.40,p,groups[0],.13),groups[0]))
        paths.append((stroked('Lower hooked blade',low,.42,p,groups[1],.08),groups[1]))
        # A small interior notch gives the terminal a directional cutting edge.
        plate('Upper terminal',[(.94,.75),(1.44,1.13),(1.10,1.20),(.78,.92)],p,groups[0],.14)
    for i,(path,parent) in enumerate(paths):
        # Integrated short emitter, ending in the same material as the rest of v5.
        segment=path[:2]
        rail=base.rail('Live cutting edge '+str(i),[(x,y,z-.018) for x,y,z in segment],.007,p['energy']);rail.parent=parent
    return paths


def render(key,selected):
    dest=OUT/key
    for folder in ['body','emission_mask','current_mask']:(dest/folder).mkdir(parents=True,exist_ok=True)
    sc=base.setup(48);p=setup_palette(GRADES[key])
    sc.render.resolution_x=sc.render.resolution_y=512;sc.render.fps=24;sc.frame_start=1;sc.frame_end=30
    root=bpy.data.objects.new('Impact root',None);sc.collection.objects.link(root)
    groups=[]
    for name in ['Upper assembly','Lower assembly']:
        g=bpy.data.objects.new(name,None);sc.collection.objects.link(g);g.parent=root;groups.append(g)
    paths=build(key,p,groups)
    white=emission('Mask white',1);black=emission('Mask black',0)
    arcs=[make_arc('Current '+str(i),path,g,white,.0060,i*.31) for i,(path,g) in enumerate(paths)]
    objects=[o for o in sc.objects if o.type in {'MESH','CURVE'}]
    arc_objects={arc['obj'] for arc in arcs}
    source_names={p['edge'].name,p['energy'].name}
    meta={'grade':key,'color':GRADES[key],'fps':24,'impact_frame':8,'end_frame':30,'frames':[]}
    for f in range(1,31):
        sc.frame_set(f)
        scale=interpolate(f,[(1,1.06),(4,1.06),(7,1.02),(8,.955),(10,1.025),(15,1),(30,1)])
        root.scale=(scale,)*3
        root.rotation_euler.z=math.radians(interpolate(f,[(1,-5),(5,-4),(7,-2),(8,1.8),(11,-.55),(16,0)]))
        root.location.y=interpolate(f,[(1,.10),(5,.10),(8,-.025),(11,.009),(16,0)])
        for prop in ['scale','rotation_euler','location']:root.keyframe_insert(prop,frame=f)
        separation=interpolate(f,[(1,.075),(5,.060),(7,.025),(8,0),(10,.01),(15,0)])
        for i,g in enumerate(groups):
            sign=1 if i==0 else -1;g.location=(separation*sign,separation*.38*sign,0);g.keyframe_insert('location',frame=f)
        power=interpolate(f,[(1,.15),(5,.7),(7,1.6),(8,18),(9,12),(11,6),(15,3),(22,2.2),(30,2.2)])
        for name,mult in [('edge',1),('energy',4),('pink',1.5)]:
            node=p[name].node_tree.nodes.get('Principled BSDF').inputs['Emission Strength'];node.default_value=power*mult;node.keyframe_insert('default_value',frame=f)
        amount=interpolate(f,[(1,0),(3,.1),(5,.45),(7,.9),(8,1.3),(10,1.15),(16,.9),(23,.78),(30,.72)])
        for arc in arcs:
            animate_arc(arc,f,amount,f)
            # Render passes toggle visibility themselves. A visibility F-curve would
            # re-evaluate during render and put the white mask into the beauty pass.
            arc['obj'].animation_data_clear()
        bpy.context.view_layer.update()
        projected=[]
        for path,g in paths:
            segment=[]
            for xyz in path[:2]:
                v=world_to_camera_view(sc,sc.camera,g.matrix_world@Vector(xyz));segment.append([v.x*512,(1-v.y)*512])
            projected.append(segment)
        v=world_to_camera_view(sc,sc.camera,root.matrix_world@Vector((0,-.06,.23)))
        meta['frames'].append({'frame':f,'paths':projected,'contact':[v.x*512,(1-v.y)*512]})
        if f not in selected:continue
        for obj in arc_objects:obj.hide_render=True
        sc.render.filepath=str(dest/'body'/f'{key}_{f:04d}.png');bpy.ops.render.render(write_still=True)
        saved=[]
        for obj in objects:
            if obj in arc_objects:continue
            for slot in obj.material_slots:saved.append((slot,slot.material));slot.material=white if slot.material.name in source_names else black
        view=(sc.view_settings.view_transform,sc.view_settings.look,sc.view_settings.exposure,sc.cycles.samples)
        sc.view_settings.view_transform='Standard';sc.view_settings.look='None';sc.view_settings.exposure=0;sc.cycles.samples=8
        sc.render.filepath=str(dest/'emission_mask'/f'{key}_{f:04d}.png');bpy.ops.render.render(write_still=True)
        for slot,mat in saved:slot.material=black
        for obj in arc_objects:obj.hide_render=amount<=0
        sc.render.filepath=str(dest/'current_mask'/f'{key}_{f:04d}.png');bpy.ops.render.render(write_still=True)
        for slot,mat in saved:slot.material=mat
        sc.view_settings.view_transform,sc.view_settings.look,sc.view_settings.exposure,sc.cycles.samples=view
        for obj in arc_objects:obj.hide_render=True
        print(f'{key} {f}/30',flush=True)
    sc.frame_set(30)
    for obj in arc_objects:obj.hide_render=True
    bpy.ops.wm.save_as_mainfile(filepath=str(dest/(key+'.blend')))
    (dest/'motion.json').write_text(json.dumps(meta,indent=2)+'\n',encoding='utf-8')


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--grade',choices=['all',*GRADES],default='all');parser.add_argument('--frames',default='all')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    selected=set(range(1,31)) if args.frames=='all' else set(map(int,args.frames.split(',')))
    for key in GRADES if args.grade=='all' else [args.grade]:render(key,selected)


if __name__=='__main__':main()
