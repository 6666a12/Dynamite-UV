"""Compose the five-grade v5/current review package, verify it, and make previews."""
from pathlib import Path
import argparse
import colorsys
import hashlib
import json
import shutil
import zipfile

import numpy as np
from PIL import Image,ImageDraw,ImageFont
import compose_blade_fx_bright as fx
from compose_blade_fx import over,curve,layer

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'design/grades/v5_current'
OUT=SOURCE/'delivery'
GRADES={'omega':'#ff4d8f','S':'#35e0ff','A':'#fbbf24','B':'#38bdf8','C':'#7c88b0'}
ADDITIONS={'X':'#a78bfa','Y':'#e879f9'}
LOW_GRADE={'D':'#697f91'}


def attenuate(image,amount):
    arr=np.array(image)
    arr[:,:,3]=(arr[:,:,3].astype(float)*amount).round().astype('uint8')
    arr[arr[:,:,3]==0,:3]=0
    return Image.fromarray(arr)


def label(key):return {'omega':'Ω','Y':'Ψ'}.get(key,key)


def rgb(color):return tuple(int(color[i:i+2],16) for i in (1,3,5))


def tint(image,color):
    """Recolor the cyan channel and preserve white cores; retain the pink accent."""
    hsv=np.array(image.convert('RGB').convert('HSV'))
    target=colorsys.rgb_to_hsv(*(v/255 for v in rgb(color)))
    selection=(hsv[:,:,0]>=113)&(hsv[:,:,0]<=159)
    hsv[:,:,0][selection]=round(target[0]*255)
    hsv[:,:,1][selection]=(hsv[:,:,1][selection].astype(float)*(.55+.45*target[1])).round().astype('uint8')
    result=Image.fromarray(hsv,mode='HSV').convert('RGBA');result.putalpha(image.getchannel('A'))
    return result


def mask(path):
    arr=np.array(Image.open(path).convert('RGBA'))
    return Image.fromarray((arr[:,:,0].astype(float)*arr[:,:,3]/255).round().astype('uint8')).resize((1536,1536),Image.Resampling.LANCZOS)


def compose_grade(key):
    dest=OUT/'animation'/key;dest.mkdir(parents=True,exist_ok=True)
    if key=='S':
        for f in range(1,31):shutil.copy2(SOURCE/'motion_S/frames'/f'S_{f:04d}.png',dest/f'{f:04d}.png')
        shutil.copy2(SOURCE/'motion_S/S_idle_glow.png',OUT/'static/S.png')
        return
    src=SOURCE/'set'/key
    meta=json.loads((src/'motion.json').read_text(encoding='utf-8'))
    for folder in ['fx_back','fx_front','current','body_composite']:(src/folder).mkdir(exist_ok=True)
    for entry in meta['frames']:
        f=entry['frame'];name=f'{key}_{f:04d}.png'
        body=Image.open(src/'body'/name).convert('RGBA');arr=np.array(body)
        arr[:,:,3]=(arr[:,:,3].astype(float)*curve(f,[(1,0),(3,.30),(5,.65),(7,1),(30,1)])).round().astype('uint8')
        arr[:,:,:3]=(arr[:,:,:3].astype(float)*curve(f,[(1,.35),(5,.7),(7,.92),(8,1),(30,1)])).round().astype('uint8')
        body=Image.fromarray(arr);body.save(src/'body_composite'/name)
        source=mask(src/'emission_mask'/name).resize((512,512),Image.Resampling.LANCZOS)
        back,front=fx.bright_effects(f,source,entry['paths'],entry['contact'])
        back,front=tint(back,GRADES[key]),tint(front,GRADES[key])
        current=mask(src/'current_mask'/name)
        color=rgb(GRADES[key]);hot=tuple(round(v*.2+255*.8) for v in color)
        current=fx.taper(over(layer(current,color,2.3,4.2),layer(current,color,1.8,1.4),layer(current,hot,1.5,.22)))
        if key=='D':
            back,front,current=attenuate(back,.55),attenuate(front,.55),attenuate(current,.70)
        back.save(src/'fx_back'/name);front.save(src/'fx_front'/name);current.save(src/'current'/name)
        result=over(back,body,front,current);result.save(dest/f'{f:04d}.png')
    shutil.copy2(dest/'0030.png',OUT/'static'/f'{key}.png')


def preview():
    keys=list(GRADES)
    font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',22)
    sheet=Image.new('RGB',(384*len(keys),640),'#0a0e1a');draw=ImageDraw.Draw(sheet)
    for i,key in enumerate(keys):
        im=Image.open(OUT/'static'/f'{key}.png').resize((384,384),Image.Resampling.LANCZOS)
        sheet.paste(im,(i*384,30),im)
        small=im.resize((128,128),Image.Resampling.LANCZOS);sheet.paste(small,(i*384+128,450),small)
        draw.text((i*384+192,612),label(key),font=font,fill=GRADES[key],anchor='mm')
    sheet.save(OUT/'all_grades.png')
    (OUT/'preview_frames').mkdir(exist_ok=True)
    for index in range(90):
        frame=Image.new('RGB',(384*len(keys),480),'#0a0e1a')
        if index<78:
            for i,key in enumerate(keys):
                im=Image.open(OUT/'animation'/key/f'{min(index+1,30):04d}.png').resize((384,384),Image.Resampling.LANCZOS)
                frame.paste(im,(i*384,48),im)
        frame.save(OUT/'preview_frames'/f'{index:04d}.png')


def validate():
    report={'size':[512,512],'format':'RGBA','fps':24,'frames_per_grade':30,'grades':[],'client_integrated':False}
    for key,color in GRADES.items():
        files=sorted((OUT/'animation'/key).glob('*.png'));assert len(files)==30
        for path in files:
            image=Image.open(path);assert image.size==(512,512) and image.mode=='RGBA'
            alpha=np.array(image)[:,:,3]
            assert max(alpha[0].max(),alpha[-1].max(),alpha[:,0].max(),alpha[:,-1].max())==0,(key,path.name)
        static=OUT/'static'/f'{key}.png'
        assert np.array_equal(np.array(Image.open(static)),np.array(Image.open(files[-1])))
        for f,path in enumerate(files,1):
            if key=='S':
                base=ROOT/'design/grades/v5/motion_S/frames'/f'S_{f:04d}.png'
                expected=over(Image.open(base),Image.open(SOURCE/'motion_S/current'/f'S_{f:04d}.png'))
            else:
                p=SOURCE/'set'/key;name=f'{key}_{f:04d}.png'
                expected=over(*(Image.open(p/folder/name) for folder in ['fx_back','body_composite','fx_front','current']))
            assert np.array_equal(np.array(expected),np.array(Image.open(path))),(key,f,'layers')
        report['grades'].append({'grade':label(key),'file_key':key,'color':color,
                                'static':f'static/{key}.png','animation':f'animation/{key}/%04d.png',
                                'checks':'30 frames, exact layer recomposition, zero canvas-edge alpha, idle = final frame',
                                'sha256':hashlib.sha256(static.read_bytes()).hexdigest()})
    (OUT/'manifest.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    return report


def main():
    global SOURCE,OUT,GRADES
    parser=argparse.ArgumentParser();parser.add_argument('--grade',choices=['all','missing',*GRADES,*ADDITIONS,*LOW_GRADE],default='all')
    parser.add_argument('--source',type=Path,help='Workspace containing set/ and optional motion_S/')
    parser.add_argument('--output',type=Path,help='Review delivery directory')
    parser.add_argument('--assemble-only',action='store_true');args=parser.parse_args()
    additions=args.grade=='missing' or args.grade in ADDITIONS
    if additions:SOURCE=ROOT/'design/grades/v5_current_xy'
    if args.grade=='D':SOURCE=ROOT/'design/grades/v5_current_d'
    if args.source:SOURCE=args.source.resolve()
    OUT=args.output.resolve() if args.output else SOURCE/'delivery'
    if additions:GRADES=ADDITIONS if args.grade=='missing' else {args.grade:ADDITIONS[args.grade]}
    if args.grade=='D':GRADES=LOW_GRADE
    (OUT/'static').mkdir(parents=True,exist_ok=True)
    if not args.assemble_only:
        for key in GRADES if args.grade in ('all','missing') else [args.grade]:
            compose_grade(key);print('COMPOSED '+key,flush=True)
    if args.grade!='all' and not additions and args.grade!='D':return
    preview();report=validate()
    readme='''# 评级图腾：v5 炫光 + 表面电流\n\n包含 Ω/S/A/B/C 五个 512×512 RGBA 静态 PNG，以及每个 30 帧、24fps 的透明入场序列（1.25 秒）。\n\n- static/：直接贴图；与对应序列最后一帧逐像素一致。\n- animation/<grade>/0001.png … 0030.png：普通 Alpha 混合，播放一次后停在末帧。\n- omega 对应 Ω；其它文件夹为 S/A/B/C。\n- Full 可播放入场；Reduced 使用静态图淡入；Off 直接显示静态图。\n- 颜色遵循 GradeColor。主轮廓/电流为原创网格与曲线，无字体或原版美术素材依赖。\n- S 完整保留用户选定的 v5 基底，额外添加表面电流。其它四级为同方向新扩展，待用户美术验收。\n- 当前仅交付资产；未接入游戏、未修改结算页代码。\n\n可编辑 Blender 场景、分层帧与生成脚本留在 community/design/grades/v5_current 和 tools/grade-assets。\n'''
    if additions:
        readme='''# X / Ψ：v5 双刃评级扩展（待美术验收）

原创切角金属网格，沿用获选 v5 的 Blender Cycles、分层 emission/current mask、多尺度炫光和普通 Alpha 合成。
X：97.00–98.99%，交叉双刃，紫罗兰；Ψ：99.00–99.99%，折返双叉与贯通中刃，兰紫粉。内部文件键 Y 显示为 Ψ。
原版参考仅用于识别字形；无字体网格、原版素材、球形背景或原版渐变贴图。

- static/{X,Y}.png：512×512 RGBA，与入场末帧逐像素相同。
- animation/{X,Y}/0001.png … 0030.png：24fps、1.25 秒透明入场，第 8 帧碰撞闪光。
- ../set/{X,Y}/{X,Y}.blend：可编辑原创网格、灯光、相机、入场和表面电流关键帧。
- ../set/{X,Y}/：body / emission_mask / current_mask，以及 fx_back / body_composite / fx_front / current。
- all_grades.png / preview_frames/：双档静图与联排动画预览。
- manifest.json：逐帧尺寸、透明边界、普通 Alpha 分层重合、末帧一致与静图 SHA-256 校验。
- Full 使用入场；Reduced/Off 使用静图。未接入客户端，待用户美术验收。
'''
    if args.grade=='D':
        readme='''# D：v5 双刃评级扩展（待美术验收）

原创竖脊与单腔折返外刃，低饱和钢蓝灰 #697F91。预览只标 D，不定义分数阈值。
沿用 Blender Cycles 金属网格、独立 emission/current mask、多尺度辉光、表面电流和普通 Alpha 合成。
为低档降低强度：主体发光 ×0.55，电流半径 ×0.72，前后 FX Alpha ×0.55，电流 Alpha ×0.70。

- static/D.png：512×512 RGBA，与第 30 帧逐像素相同。
- animation/D/0001.png … 0030.png：30 帧、24fps、1.25 秒透明入场。
- ../set/D/D.blend：可编辑原创网格、灯光、相机、动画和表面电流。
- ../set/D/：body / emission_mask / current_mask / fx_back / body_composite / fx_front / current。
- all_grades.png / preview_frames/：大小图与动画预览；manifest.json：分层与格式检查。
- Full 播放入场；Reduced/Off 使用静图。待用户美术验收，未接入客户端。
'''
    (OUT/'README.md').write_text(readme,encoding='utf-8')
    archive_name='grades_v5_current_xy.zip' if additions else 'grades_v5_current.zip'
    if args.grade=='D':archive_name='grades_v5_current_d.zip'
    with zipfile.ZipFile(OUT/archive_name,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
        for folder in ['static','animation']:
            for file in sorted((OUT/folder).rglob('*.png')):archive.write(file,file.relative_to(OUT))
        for name in ['manifest.json','README.md','all_grades.png']:archive.write(OUT/name,name)
    print(json.dumps(report,ensure_ascii=False))


if __name__=='__main__':main()
