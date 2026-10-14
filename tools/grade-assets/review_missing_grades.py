"""Make local X/Psi or D review sheets/video and audit the isolated v5 additions.

Run after render_grade_set.py --grade missing and package_grade_set.py --grade missing.
Use --grade D for the separate D workspace. Reads existing family images only.
"""
from pathlib import Path
import argparse
import hashlib
import json
import subprocess

import numpy as np
from PIL import Image,ImageDraw,ImageFont

ROOT=Path(__file__).resolve().parents[2]
WORK=ROOT/'design/grades/v5_current_xy'
DELIVERY=WORK/'delivery'
PREVIOUS=ROOT/'design/grades/v5_current/delivery'
FONT='C:/Windows/Fonts/msyh.ttc'


def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    global WORK,DELIVERY
    parser=argparse.ArgumentParser();parser.add_argument('--grade',choices=['missing','D'],default='missing')
    args=parser.parse_args()
    keys=('X','Y')
    context=[('S','S · existing','#35e0ff'),('X','X · 97.00–98.99%','#a78bfa'),
             ('Y','Ψ · 99.00–99.99%','#e879f9'),('omega','Ω · existing','#ff4d8f')]
    video='X_Psi_preview.mp4'
    if args.grade=='D':
        WORK=ROOT/'design/grades/v5_current_d';DELIVERY=WORK/'delivery';keys=('D',)
        context=[('B','B','#38bdf8'),('C','C','#7c88b0'),('D','D','#697f91')]
        video='D_preview.mp4'
    font=ImageFont.truetype(FONT,22)
    smallfont=ImageFont.truetype(FONT,16)
    sheet=Image.new('RGB',(384*len(context),576),'#0a0e1a');draw=ImageDraw.Draw(sheet)
    for i,(key,label,color) in enumerate(context):
        source=DELIVERY if key in keys else PREVIOUS
        im=Image.open(source/'static'/f'{key}.png').resize((384,384),Image.Resampling.LANCZOS)
        sheet.paste(im,(i*384,8),im)
        thumb=im.resize((128,128),Image.Resampling.LANCZOS);sheet.paste(thumb,(i*384+128,385),thumb)
        draw.text((i*384+192,546),label,font=font,fill=color,anchor='mm')
    sheet.save(DELIVERY/'family_context.png')

    contact=Image.new('RGB',(1536,304*len(keys)),'#0a0e1a');draw=ImageDraw.Draw(contact)
    for row,key in enumerate(keys):
        for col,f in enumerate((1,5,8,12,20,30)):
            im=Image.open(DELIVERY/'animation'/key/f'{f:04d}.png').resize((256,256),Image.Resampling.LANCZOS)
            contact.paste(im,(col*256,row*304),im)
            draw.text((col*256+128,row*304+282),f'{"Ψ" if key=="Y" else key} / {f:02d}',
                      font=smallfont,fill='#dfe6ff',anchor='mm')
    contact.save(DELIVERY/'intro_contact_sheet.png')

    backgrounds=Image.new('RGB',(1024,288*len(keys)),'#0a0e1a');draw=ImageDraw.Draw(backgrounds)
    draw.rectangle((512,0,1023,backgrounds.height-1),fill='#dfe6ff')
    for col in range(2):
        for row,key in enumerate(keys):
            im=Image.open(DELIVERY/'static'/f'{key}.png').resize((256,256),Image.Resampling.LANCZOS)
            backgrounds.paste(im,(col*512+128,row*288),im)
    backgrounds.save(DELIVERY/'alpha_backgrounds.png')

    report={'status':'rendered; automated checks passed; awaiting user art acceptance',
            'client_integrated':False,'size':[512,512],'fps':24,'frames':30,'duration_seconds':1.25,
            'display_names':{key:'Ψ' if key=='Y' else key for key in keys},'grades':{},'source_hashes':{}}
    for key in keys:
        bounds=[]
        for f in range(1,31):
            image=Image.open(DELIVERY/'animation'/key/f'{f:04d}.png')
            body=Image.open(WORK/'set'/key/'body'/f'{key}_{f:04d}.png')
            # Body must fit before compositing's edge taper, not merely after it.
            a=np.array(body.getchannel('A'))
            assert not any(np.any(edge) for edge in (a[0],a[-1],a[:,0],a[:,-1])),(key,f,'body clipping')
            bounds.append({'frame':f,'body_bbox':body.getchannel('A').getbbox(),
                           'final_bbox':image.getchannel('A').getbbox(),
                           'sha256':sha(DELIVERY/'animation'/key/f'{f:04d}.png')})
        # Transparent animated WebP is only a review companion. Integer millisecond
        # durations alternate to total exactly 1250ms; PNG + manifest remain master.
        frames=[Image.open(DELIVERY/'animation'/key/f'{f:04d}.png') for f in range(1,31)]
        durations=[round((i+1)*1000/24)-round(i*1000/24) for i in range(30)]
        frames[0].save(DELIVERY/f'{key}_intro.webp',save_all=True,append_images=frames[1:],
                       duration=durations,loop=0,lossless=True)
        decoded=Image.open(DELIVERY/f'{key}_intro.webp');duration=0
        assert decoded.n_frames==30
        for i in range(30):
            decoded.seek(i);decoded.load();duration+=decoded.info['duration']
            assert decoded.mode=='RGBA' and decoded.size==(512,512)
        assert duration==1250
        report['grades'][key]={'body_unclipped':True,'frames':bounds}
        report['source_hashes'][f'set/{key}/{key}.blend']=sha(WORK/'set'/key/f'{key}.blend')
    for name in ('render_grade_set.py','package_grade_set.py','review_missing_grades.py',
                 'render_concepts.py','render_surface_current.py','render_blade_motion.py',
                 'compose_blade_fx.py','compose_blade_fx_bright.py'):
        report['source_hashes']['tools/grade-assets/'+name]=sha(ROOT/'tools/grade-assets'/name)
    snapshot=WORK/'existing_assets_before.json'
    if snapshot.exists():
        before=json.loads(snapshot.read_text(encoding='utf-8'))
        changed=[p for p,digest in before.items() if not (ROOT/p).exists() or sha(ROOT/p)!=digest]
        assert not changed,changed
        report['existing_assets_verified_unchanged']=len(before)
    subprocess.run(['ffmpeg','-y','-loglevel','error','-framerate','24','-i',
                    str(DELIVERY/'preview_frames/%04d.png'),'-frames:v','90','-c:v','libx264',
                    '-pix_fmt','yuv420p','-crf','17',str(DELIVERY/video)],check=True)
    (DELIVERY/'review_checks.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'PASS: {30*len(keys)} unclipped body frames; exact 1.25s intros; source hashes and existing assets audited; preview video created.')


if __name__=='__main__':main()
