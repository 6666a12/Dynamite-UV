"""Package the approved clean-room grade frames and Penpot vectors for Godot.

Only PNG/SVG runtime assets are copied; Blender, previews, and reverse-engineered
reference files never enter res://. Requires Pillow. Run from any directory.
"""
from pathlib import Path
import hashlib
import json
import shutil
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'design/grades/v5_current/delivery'
DEST = ROOT / 'client/assets/results'


def penpot_svg(prefix):
    for line in (ROOT/'design/result-v2/source/result.js').read_text(encoding='utf-8').splitlines():
        if line.startswith(prefix):
            return json.JSONDecoder().raw_decode(line[len(prefix):])[0]
    raise ValueError('Penpot vector missing: '+prefix)


def main():
    (DEST/'grades').mkdir(parents=True, exist_ok=True)
    manifest = {'source':'clean-room v5_current delivery', 'fps':24, 'frame_size':512,
                'atlas_columns':6,'atlas_rows':5,'frames':30,'grades':{}}
    for key in ('omega','S','A','B','C'):
        static=SOURCE/'static'/f'{key}.png'
        shutil.copyfile(static, DEST/'grades'/static.name)
        atlas=Image.new('RGBA',(3072,2560))
        for i in range(30):
            frame=Image.open(SOURCE/'animation'/key/f'{i+1:04}.png').convert('RGBA')
            assert frame.size == (512,512)
            atlas.paste(frame,((i%6)*512,(i//6)*512))
        assert atlas.crop((5*512,4*512,6*512,5*512)).tobytes()==Image.open(static).convert('RGBA').tobytes()
        destination=DEST/'grades'/f'{key}_entry.png'
        atlas.save(destination,optimize=True)
        # Verify exact RGBA reconstruction, not only dimensions or alpha presence.
        packed=Image.open(destination)
        for i in range(30):
            region=packed.crop(((i%6)*512,(i//6)*512,(i%6+1)*512,(i//6+1)*512))
            assert region.tobytes()==Image.open(SOURCE/'animation'/key/f'{i+1:04}.png').convert('RGBA').tobytes()
        manifest['grades'][key]={'static_sha256':hashlib.sha256(static.read_bytes()).hexdigest(),
                                 'atlas_sha256':hashlib.sha256(destination.read_bytes()).hexdigest()}
    background=penpot_svg('storage.svg(B,0,0,')
    for key,color in {'omega':'#FF4D8F','S':'#35E0FF','A':'#FBBF24','B':'#38BDF8','C':'#7C88B0'}.items():
        (DEST/f'background_{key}.svg').write_text(background.replace('#35E0FF',color),encoding='utf-8')
    cover=penpot_svg('storage.svg(B,72,64,')
    (DEST/'cover_placeholder.svg').write_text(cover,encoding='utf-8')
    for key,ap in [('fc',False),('ap',True)]:
        color='#FF4D8F' if ap else '#35E0FF'
        shape=f'<path d="M32 2L60 18V46L32 62L4 46V18Z" fill="{color}" fill-opacity=".08" stroke="{color}" stroke-width="1.5"/>'
        shape+= (f'<path d="M15 25L23 32L32 18L41 32L49 25L43 43H21Z" fill="{color}"/><path d="M22 49H42" stroke="{color}" stroke-width="2"/>' if ap else f'<path d="M17 29L26 38L45 19M20 43L28 48L47 31" fill="none" stroke="{color}" stroke-width="4" stroke-linejoin="bevel"/>')
        (DEST/f'{key}.svg').write_text(f'<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64">{shape}</svg>',encoding='utf-8')
    for key,w,h,path,color in [('back',26,24,'M14 2L4 12L14 22M4 12H24','#7C88B0'),
                                ('retry',28,28,'M6 10A10 10 0 1 1 5 20M6 3V10H13','#04121A')]:
        (DEST/f'{key}.svg').write_text(f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}"><path d="{path}" stroke="{color}" stroke-width="2.4" fill="none"/></svg>',encoding='utf-8')
    (ROOT/'design/result-v2/runtime-assets.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    presets=ROOT/'client/export_presets.cfg'
    data=presets.read_text(encoding='utf-8')
    marker='export_files=PackedStringArray('
    a=data.index(marker)+len(marker)
    b=data.index(')',a)
    files=[v.strip().strip('"') for v in data[a:b].split(',')]
    for asset in sorted(DEST.rglob('*')):
        if asset.suffix not in ('.png','.svg'):continue
        path='res://'+asset.relative_to(ROOT/'client').as_posix()
        if path not in files:files.append(path)
    data=data[:a]+', '.join(json.dumps(v) for v in files)+data[b:]
    presets.write_text(data,encoding='utf-8')
    provenance_path=ROOT/'release/asset-provenance.json'
    provenance=json.loads(provenance_path.read_text(encoding='utf-8'))
    provenance['assets']=[a for a in provenance['assets'] if not a['path'].startswith('client/assets/results/')]
    generator=Path(__file__).resolve()
    for asset in sorted(DEST.rglob('*')):
        if asset.suffix not in ('.png','.svg'):continue
        provenance['assets'].append({
            'path':asset.relative_to(ROOT).as_posix(),
            'sha256':hashlib.sha256(asset.read_bytes()).hexdigest(),
            'kind':'project-generated-ui-asset',
            'source':'Original v5_current Blender renders and project Penpot result-v2 design; no original-game pixels',
            'generator':generator.relative_to(ROOT).as_posix(),
            'generatorSha256':hashlib.sha256(generator.read_bytes()).hexdigest(),
        })
    provenance_path.write_text(json.dumps(provenance,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('Packaged five exact RGBA atlases + static grades and Penpot vectors; Public export list updated.')


if __name__=='__main__':main()
