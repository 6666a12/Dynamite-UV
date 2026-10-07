"""Layered lighting study; regular straight-alpha PNGs, no game changes."""
from pathlib import Path
import json
import math

import numpy as np
from PIL import Image,ImageDraw,ImageFilter,ImageFont

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'design/grades/v4/motion_S'
SIZE=512
SS=3
CYAN=(53,224,255)
PINK=(255,77,143)


def mask():return Image.new('L',(SIZE*SS,SIZE*SS))


def line(draw,points,width,alpha=255):
    draw.line([(round(x*SS),round(y*SS)) for x,y in points],fill=round(alpha),width=max(1,round(width*SS)),joint='curve')


def layer(alpha,color,strength=1,blur=0):
    if blur:alpha=alpha.filter(ImageFilter.GaussianBlur(blur*SS))
    alpha=alpha.resize((SIZE,SIZE),Image.Resampling.LANCZOS)
    alpha=alpha.point(lambda v:round(max(0,min(255,v*strength))))
    im=Image.new('RGBA',(SIZE,SIZE),color+(0,))
    im.putalpha(alpha)
    return im


def over(*layers):
    result=Image.new('RGBA',(SIZE,SIZE))
    for im in layers:result=Image.alpha_composite(result,im)
    return result


def curve(f,keys):
    for i,(k,v) in enumerate(keys):
        if f<=k:
            if i==0:return v
            pk,pv=keys[i-1]
            return pv+(v-pv)*(f-pk)/(k-pk)
    return keys[-1][1]


def effects(frame,paths,contact):
    rail=mask();d=ImageDraw.Draw(rail)
    for points in paths:line(d,points,1.2)
    strength=curve(frame,[(1,.05),(5,.18),(7,.42),(8,1),(9,.72),(12,.35),(18,.19),(24,.16),(30,.16)])
    back=over(layer(rail,CYAN,strength*.62,4.0),layer(rail,CYAN,strength*.40,1.5))
    front=over(layer(rail,CYAN,strength*.62,.50),layer(rail,(196,252,255),strength*.48,0))
    cx,cy=contact
    for index,start in enumerate((8,10)):
        age=frame-start
        if not 0<=age<=8:continue
        t=age/8
        radius=24+204*(1-(1-t)**1.6)
        opacity=(1-t)**2*(.88 if index==0 else .42)
        ring=mask();dr=ImageDraw.Draw(ring)
        for first,last in [(-20,95),(107,209),(222,327)]:
            pts=[]
            for angle in range(first,last+1,2):
                a=math.radians(angle)
                x,y=radius*math.cos(a),radius*.66*math.sin(a)
                tilt=-.24
                pts.append((cx+x*math.cos(tilt)-y*math.sin(tilt),cy+x*math.sin(tilt)+y*math.cos(tilt)))
            line(dr,pts,1.8 if index==0 else 1.25)
        col=CYAN if index==0 else PINK
        back=over(back,layer(ring,col,opacity*.42,3.2),layer(ring,col,opacity,0))
    age=frame-8
    if 0<=age<=8:
        sparks=mask();ds=ImageDraw.Draw(sparks)
        for i,a in enumerate([-.45,.10,.71,1.65,2.43,2.91,3.80,4.37,5.17]):
            reach=18+(age+1)*(11+(i%3)*5)
            length=max(2,20-age*1.7)
            direction=(math.cos(a),math.sin(a)*.8)
            start=(cx+direction[0]*(reach-length),cy+direction[1]*(reach-length))
            end=(cx+direction[0]*reach,cy+direction[1]*reach)
            line(ds,[start,end],1.3 if i%3 else 2,220)
        opacity=(1-age/9)**1.9
        front=over(front,layer(sparks,CYAN,opacity*.45,2),layer(sparks,(189,250,255),opacity,0))
    if frame in (8,9):
        burst=mask();db=ImageDraw.Draw(burst)
        peak=1 if frame==8 else .28
        line(db,[(cx-44,cy+23),(cx+44,cy-23)],1.4)
        line(db,[(cx-9,cy-18),(cx+9,cy+18)],1.2)
        db.ellipse(((cx-3)*SS,(cy-3)*SS,(cx+3)*SS,(cy+3)*SS),fill=255)
        front=over(front,layer(burst,CYAN,peak*.8,5),layer(burst,(230,255,255),peak,0))
    return back,front


def main():
    metadata=json.loads((OUT/'motion.json').read_text(encoding='utf-8'))
    for name in ['fx_back','fx_front','frames','preview_frames']:(OUT/name).mkdir(exist_ok=True)
    frames=[]
    for entry in metadata['frames']:
        f=entry['frame']
        body=Image.open(OUT/'body'/f'S_{f:04d}.png').convert('RGBA')
        opacity=curve(f,[(1,0),(3,.30),(5,.65),(7,1),(30,1)])
        arr=np.array(body)
        arr[:,:,3]=(arr[:,:,3].astype(float)*opacity).astype('uint8')
        dim=curve(f,[(1,.27),(5,.52),(7,.78),(8,1),(30,1)])
        arr[:,:,:3]=(arr[:,:,:3].astype(float)*dim).astype('uint8')
        body=Image.fromarray(arr)
        body.save(OUT/'body'/f'S_composite_{f:04d}.png')
        back,front=effects(f,entry['paths'],entry['contact'])
        final=over(back,body,front)
        back.save(OUT/'fx_back'/f'S_{f:04d}.png')
        front.save(OUT/'fx_front'/f'S_{f:04d}.png')
        final.save(OUT/'frames'/f'S_{f:04d}.png')
        frames.append(final)
    frames[-1].save(OUT/'S_idle_glow.png')
    Image.open(OUT/'body/S_0030.png').save(OUT/'S_idle_clean.png')
    sheet=Image.new('RGB',(1536,620),'#0a0e1a')
    font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',21)
    d=ImageDraw.Draw(sheet)
    for i,(frame,label) in enumerate([(Image.open(OUT/'S_idle_clean.png'),'原始材质'),(frames[-1],'常态 · 薄青光'),(frames[7],'命中 · 第 8 帧')]):
        sheet.paste(frame,(i*512,24),frame)
        d.text((i*512+256,568),label,font=font,fill='#dfe6ff',anchor='mm')
    sheet.save(OUT/'lighting_comparison.png')
    # 1.25s animation, then 1.75s rest and 0.5s blank: preview only.
    timeline=frames+[frames[-1]]*42+[Image.new('RGBA',(512,512))]*12
    preview=[]
    for i,im in enumerate(timeline):
        bg=Image.new('RGB',(768,768),'#0a0e1a')
        bg.paste(im,(128,128),im)
        bg.save(OUT/'preview_frames'/f'{i:04d}.png')
        preview.append(bg)
    preview[0].save(OUT/'S_light_study.webp',save_all=True,append_images=preview[1:],duration=round(1000/24),loop=0,quality=88)
    for name in ['frames','fx_back','fx_front']:
        paths=list((OUT/name).glob('S_*.png'))
        assert len(paths)==30
        for path in paths:
            im=Image.open(path)
            assert im.size==(512,512) and im.mode=='RGBA'
    assert np.array_equal(np.array(Image.open(OUT/'S_idle_glow.png')),np.array(frames[-1]))
    metadata['status']='S lighting proposal; user selected blade direction; lighting pending review'
    metadata['layers']=['fx_back/S_####.png (alpha blend)','body/S_composite_####.png (alpha blend)','fx_front/S_####.png (alpha blend)']
    metadata['white_peak_frames']=[8,9]
    metadata['motion_off']='Display S_idle_glow.png immediately, no rings, sparks or pulsing'
    metadata['motion_reduced']='Fade stable image in over 260 ms; no position/scale/rotation/echo'
    (OUT/'motion.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('30 RGBA frames / 24 fps; independent light layers; final frame matches idle exactly.')


if __name__=='__main__':
    main()
