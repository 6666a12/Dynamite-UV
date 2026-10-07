"""V5: emissive machined bevels, shaped bloom, discharge arcs and impact rays.

Consumes Blender's separate visible-emission mask, retaining ordinary RGBA layers.
The previous restrained study remains untouched in v4.
"""
from pathlib import Path
import json
import math

import numpy as np
from PIL import Image,ImageDraw,ImageFilter,ImageFont
from compose_blade_fx import mask,line,layer,over,curve,CYAN,PINK,SS

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'design/grades/v5/motion_S'
PREVIOUS=ROOT/'design/grades/v4/motion_S'
Y,X=np.mgrid[:512,:512]
EDGE=np.clip(np.minimum.reduce([X,Y,511-X,511-Y])/12,0,1)
EDGE=EDGE*EDGE*(3-2*EDGE)


def taper(image):
    arr=np.array(image)
    arr[:,:,3]=(arr[:,:,3].astype(float)*EDGE).round().astype('uint8')
    arr[arr[:,:,3]==0,:3]=0
    return Image.fromarray(arr)


def star(draw,x,y,length,width=1):
    line(draw,[(x-length,y+length*.2),(x+length,y-length*.2)],width)
    line(draw,[(x-length*.17,y-length*.45),(x+length*.17,y+length*.45)],width*.8)


def bright_effects(frame,source,paths,contact):
    emitter=source.resize((1536,1536),Image.Resampling.LANCZOS)
    power=curve(frame,[(1,0),(3,.2),(5,.50),(7,.95),(8,2.8),(9,2.4),(12,1.6),(18,1.1),(24,1),(30,1)])
    back=over(layer(emitter,CYAN,2.7*power,26),layer(emitter,CYAN,2.6*power,10),layer(emitter,CYAN,1.15*power,3))
    front=over(layer(emitter,CYAN,.48*power,7),layer(emitter,(153,250,255),.42*power,1.2))
    cx,cy=contact
    for index,start in enumerate((8,10)):
        age=frame-start
        if not 0<=age<=12:continue
        t=age/12
        radius=48+174*(1-(1-t)**1.8)
        strength=(1-t)**1.4*(1.25 if index==0 else .94)
        ring=mask();dr=ImageDraw.Draw(ring)
        for a0,a1 in [(-16,128),(138,306),(318,337)]:
            points=[]
            for angle in range(a0,a1+1,2):
                a=math.radians(angle)
                x=radius*math.cos(a);y=radius*(.68 if index==0 else .79)*math.sin(a)
                tilt=-.26 if index==0 else .18
                points.append((cx+x*math.cos(tilt)-y*math.sin(tilt),cy+x*math.sin(tilt)+y*math.cos(tilt)))
            line(dr,points,3.8 if index==0 else 2.3)
        color=CYAN if index==0 else PINK
        back=over(back,layer(ring,color,strength*3.4,8),layer(ring,color,strength*2,2.3),layer(ring,(175,252,255) if index==0 else (255,144,200),strength))
    age=frame-8
    if 0<=age<=12:
        particles=mask();dp=ImageDraw.Draw(particles)
        for i in range(24):
            a=i*math.tau/24+.1*math.sin(i*17)
            reach=25+(age+1)*(8+(i%5)*2.5)
            if reach>232:continue
            length=curve(age,[(0,31),(3,23),(9,9),(12,2)])*(.65+(i%3)*.18)
            ux,uy=math.cos(a),math.sin(a)*.77
            line(dp,[(cx+ux*(reach-length),cy+uy*(reach-length)),(cx+ux*reach,cy+uy*reach)],1.4+(i%3)*.55)
            if i%5==0:
                px,py=cx+ux*reach,cy+uy*reach
                dp.polygon([(int((px-2)*SS),int(py*SS)),(int(px*SS),int((py-3)*SS)),(int((px+2)*SS),int(py*SS)),(int(px*SS),int((py+3)*SS))],fill=255)
        decay=(1-age/13)**1.5
        front=over(front,layer(particles,CYAN,decay*2.1,4),layer(particles,CYAN,decay*1.5,1.4),layer(particles,(220,255,255),decay))
    # Two brief electrical bridges hug the open negative spaces in the glyph.
    if 8<=frame<=16:
        arc=mask();da=ImageDraw.Draw(arc)
        amount=(1-(frame-8)/9)**1.2
        for side in (-1,1):
            points=[]
            for i in range(12):
                t=i/11
                x=cx+side*(-133+270*t)
                y=cy+side*(-116+19*math.sin(t*math.pi))
                y+=math.sin(i*23+frame*3)*7
                points.append((x,y))
            line(da,points,1.2)
        front=over(front,layer(arc,CYAN,amount*2.8,5),layer(arc,CYAN,amount*2,1.4),layer(arc,(213,255,255),amount))
    flash=curve(frame,[(1,0),(7,0),(8,1),(9,.7),(10,.31),(11,.1),(12,0),(30,0)])
    if flash:
        flare=mask();df=ImageDraw.Draw(flare)
        star(df,cx,cy,135,3)
        line(df,[(cx-78,cy+61),(cx+78,cy-61)],1.6)
        df.ellipse(((cx-7)*SS,(cy-7)*SS,(cx+7)*SS,(cy+7)*SS),fill=255)
        front=over(front,layer(flare,CYAN,flash*3.8,16),layer(flare,CYAN,flash*2.5,4),layer(flare,(234,255,255),flash))
    # Small steady glints live on the two long cutting edges.
    glints=mask();dg=ImageDraw.Draw(glints)
    for i,path in enumerate(paths):
        x,y=path[-1 if i==0 else 0]
        star(dg,x,y,10 if i==0 else 15,1)
    front=over(front,layer(glints,CYAN,power*1.8,3),layer(glints,(216,255,255),min(1,power)))
    return taper(back),taper(front)


def main():
    meta=json.loads((OUT/'motion.json').read_text(encoding='utf-8'))
    for folder in ['fx_back','fx_front','frames','preview_frames','comparison_frames']:(OUT/folder).mkdir(exist_ok=True)
    frames=[]
    for entry in meta['frames']:
        f=entry['frame'];name=f'S_{f:04d}.png'
        body=Image.open(OUT/'body'/name).convert('RGBA')
        arr=np.array(body)
        arr[:,:,3]=(arr[:,:,3].astype(float)*curve(f,[(1,0),(3,.30),(5,.65),(7,1),(30,1)])).round().astype('uint8')
        arr[:,:,:3]=(arr[:,:,:3].astype(float)*curve(f,[(1,.35),(5,.7),(7,.92),(8,1),(30,1)])).round().astype('uint8')
        body=Image.fromarray(arr)
        body.save(OUT/'body'/f'S_composite_{f:04d}.png')
        emit=Image.open(OUT/'emission_mask'/name).convert('RGBA')
        e=np.array(emit)
        source=Image.fromarray((e[:,:,0].astype(float)*e[:,:,3]/255).round().astype('uint8'))
        back,front=bright_effects(f,source,entry['paths'],entry['contact'])
        final=over(back,body,front)
        back.save(OUT/'fx_back'/name);front.save(OUT/'fx_front'/name);final.save(OUT/'frames'/name)
        frames.append(final)
    frames[-1].save(OUT/'S_idle_glow.png')
    Image.open(OUT/'body/S_0030.png').save(OUT/'S_idle_emissive.png')
    font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',22)
    sheet=Image.new('RGB',(1536,620),'#0a0e1a');draw=ImageDraw.Draw(sheet)
    for i,(im,label) in enumerate([(Image.open(PREVIOUS/'S_idle_glow.png'),'上一版'),(frames[-1],'新版 · 常态炫光'),(frames[7],'新版 · 命中爆发')]):
        sheet.paste(im,(i*512,24),im)
        draw.text((i*512+256,568),label,font=font,fill='#dfe6ff',anchor='mm')
    sheet.save(OUT/'lighting_comparison.png')
    preview=[]
    for i in range(84):
        im=frames[i] if i<30 else frames[-1] if i<72 else Image.new('RGBA',(512,512))
        bg=Image.new('RGB',(768,768),'#0a0e1a');bg.paste(im,(128,128),im)
        bg.save(OUT/'preview_frames'/f'{i:04d}.png');preview.append(bg)
        comparison=Image.new('RGB',(1024,620),'#0a0e1a');dr=ImageDraw.Draw(comparison)
        old=Image.open(PREVIOUS/'frames'/f'S_{min(i+1,30):04d}.png') if i<72 else Image.new('RGBA',(512,512))
        comparison.paste(old,(0,24),old);comparison.paste(im,(512,24),im)
        dr.text((256,568),'上一版',font=font,fill='#7c88b0',anchor='mm')
        dr.text((768,568),'新版 · 炫光增强',font=font,fill='#35e0ff',anchor='mm')
        comparison.save(OUT/'comparison_frames'/f'{i:04d}.png')
    preview[0].save(OUT/'S_light_study.webp',save_all=True,append_images=preview[1:],duration=42,loop=0,quality=90)
    meta['status']='User requested substantially brighter effects; v5 S lighting revision for review'
    meta['layers']=['fx_back/S_####.png','body/S_composite_####.png','fx_front/S_####.png']
    meta['blend']='normal straight alpha; all layers are independently adjustable'
    meta['emission_strength']={'bevel_idle':2.2,'bevel_impact':18,'live_edge_idle':8.8,'live_edge_impact':72}
    meta['white_peak_frames']=[8,9]
    meta['flash_tail_frames']=[10,11]
    meta['motion_off']='Show S_idle_glow.png immediately'
    meta['motion_reduced']='Fade S_idle_glow.png over 260 ms; no impact, arcs, rings or sparks'
    (OUT/'motion.json').write_text(json.dumps(meta,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('V5 bright study complete: 30 RGBA frames, 24 fps, separate visible-emission masks and light layers.')


if __name__=='__main__':main()
