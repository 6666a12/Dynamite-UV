"""Prepare review PNGs and check alpha bounds; does not judge the visual design."""
from pathlib import Path
import json
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'design/grades/v4'
KEYS = [('01_armor', '01  断层装甲'), ('02_blades', '02  双刃折返'), ('03_crystal', '03  晶体切面')]


def main():
    sheet = Image.new('RGB', (1920, 1080), '#0a0e1a')
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 24)
    small = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 16)
    manifest = {'status': 'direction studies; awaiting user selection', 'grade': 'S',
                'color': '#35e0ff', 'renderer': 'Blender 5.2 / Cycles', 'assets': []}
    for i, (key, title) in enumerate(KEYS):
        master = Image.open(OUT / f'{key}_S_master.png').convert('RGBA')
        icon = master.resize((512,512), Image.Resampling.LANCZOS)
        path = OUT / f'{key}_S.png'
        icon.save(path)
        a = icon.getchannel('A')
        bbox = a.getbbox()
        assert a.getextrema() == (0,255)
        assert bbox and min(bbox[:2]) >= 8 and max(bbox[2:]) <= 504, (key,bbox)
        cx = 320 + i * 640
        sheet.paste(icon, (cx-256,100),icon)
        draw.text((cx,660),title,font=font,fill='#dfe6ff',anchor='mm')
        thumb=icon.resize((128,128),Image.Resampling.LANCZOS)
        sheet.paste(thumb,(cx-164,788),thumb)
        # The light swatch makes alpha fringes visible without altering the source asset.
        draw.rounded_rectangle((cx+32,768,cx+200,936),radius=12,fill='#d7dfea')
        sheet.paste(thumb,(cx+52,788),thumb)
        manifest['assets'].append({'key':key,'file':path.name,'dimensions':[512,512],
                                   'mode':icon.mode,'alpha_bounds':list(bbox)})
    draw.text((960,1016),'S · 静态方向草案 / 下排为 128 px 对照',font=small,fill='#7c88b0',anchor='mm')
    sheet.save(OUT/'comparison.png')
    (OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(manifest,ensure_ascii=False,indent=2))


if __name__ == '__main__':
    main()
