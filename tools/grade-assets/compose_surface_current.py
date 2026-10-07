"""Add only a separately rendered surface-current layer to the approved v5 images."""
from pathlib import Path
import hashlib
import json

import numpy as np
from PIL import Image,ImageDraw,ImageFont
from compose_blade_fx import layer,over,CYAN
from compose_blade_fx_bright import taper

ROOT=Path(__file__).resolve().parents[2]
BASE=ROOT/'design/grades/v5/motion_S'
OUT=ROOT/'design/grades/v5_current/motion_S'


def current_layer(path,strength=1):
    image=Image.open(path).convert('RGBA');arr=np.array(image)
    grey=(arr[:,:,0].astype(float)*arr[:,:,3]/255).round().astype('uint8')
    mask=Image.fromarray(grey).resize((1536,1536),Image.Resampling.LANCZOS)
    return taper(over(layer(mask,(34,160,255),strength*2.3,4.2),
                      layer(mask,CYAN,strength*1.8,1.4),
                      layer(mask,(216,255,255),strength*1.5,.22)))


def main():
    for folder in ['current','frames','idle_current','idle_frames','preview_frames','comparison_frames']:(OUT/folder).mkdir(exist_ok=True)
    frames=[];idle=[];hashes={}
    for f in range(1,31):
        name=f'S_{f:04d}.png';base=Image.open(BASE/'frames'/name).convert('RGBA')
        hashes[name]=hashlib.sha256((BASE/'frames'/name).read_bytes()).hexdigest()
        fx=current_layer(OUT/'current_mask'/name)
        final=Image.alpha_composite(base,fx)
        fx.save(OUT/'current'/name);final.save(OUT/'frames'/name);frames.append(final)
    rest=Image.open(BASE/'S_idle_glow.png').convert('RGBA')
    for f in range(1,25):
        name=f'S_{f:04d}.png';fx=current_layer(OUT/'idle_mask'/name)
        final=Image.alpha_composite(rest,fx)
        fx.save(OUT/'idle_current'/name);final.save(OUT/'idle_frames'/name);idle.append(final)
    frames[-1].save(OUT/'S_idle_glow.png')
    font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',22)
    sheet=Image.new('RGB',(1536,620),'#0a0e1a');d=ImageDraw.Draw(sheet)
    for i,(im,label) in enumerate([(rest,'v5 原版'),(frames[-1],'v5 ＋ 表面电流'),(frames[11],'入场电流')]):
        sheet.paste(im,(i*512,24),im);d.text((i*512+256,568),label,font=font,fill='#dfe6ff',anchor='mm')
    sheet.save(OUT/'comparison.png')
    preview=[]
    timeline=frames+idle*2+[Image.new('RGBA',(512,512))]*12
    for i,im in enumerate(timeline):
        bg=Image.new('RGB',(768,768),'#0a0e1a');bg.paste(im,(128,128),im)
        bg.save(OUT/'preview_frames'/f'{i:04d}.png');preview.append(bg)
        old=Image.open(BASE/'frames'/f'S_{min(i+1,30):04d}.png') if i<78 else Image.new('RGBA',(512,512))
        cmp=Image.new('RGB',(1024,620),'#0a0e1a');d=ImageDraw.Draw(cmp)
        cmp.paste(old,(0,24),old);cmp.paste(im,(512,24),im)
        d.text((256,568),'v5 原版',font=font,fill='#7c88b0',anchor='mm')
        d.text((768,568),'v5 ＋ 表面电流',font=font,fill='#35e0ff',anchor='mm')
        cmp.save(OUT/'comparison_frames'/f'{i:04d}.png')
    preview[0].save(OUT/'S_surface_current.webp',save_all=True,append_images=preview[1:],duration=42,loop=0,quality=92)
    manifest={'status':'v5 approved by user; added surface current for review',
              'size':[512,512],'fps':24,'intro_frames':30,'optional_idle_loop_frames':24,
              'base_frames_sha256':hashes,'composition':['v5/frames/S_####.png','current/S_####.png'],
              'blend':'straight alpha','idle':'v5/S_idle_glow.png + idle_current/S_####.png',
              'motion_reduced_off':'use S_idle_glow.png as a still; no current loop'}
    (OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('30 intro frames + 24 optional idle current frames; v5 base is unchanged.')


if __name__=='__main__':main()
