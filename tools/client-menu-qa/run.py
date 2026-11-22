"""Isolated official-client snapshots. Prepare never invokes Godot, MSBuild, dotnet or import."""
from __future__ import annotations
import argparse, contextlib, datetime as dt, hashlib, json, os, re, shutil, subprocess, sys, uuid
from pathlib import Path
import fixture
HERE=Path(__file__).resolve().parent;ROOT=HERE.parents[1];CLIENT=ROOT/'client'
def sha(f):return hashlib.sha256(f.read_bytes()).hexdigest()
def capture(src,rel,stage):
    rel=Path(rel);f=src/rel
    if not f.resolve().is_relative_to(src.resolve()) or any(part.lower()=='testdata' for part in rel.parts) or not f.is_file():raise RuntimeError(f'Unexpected source {rel}')
    dest=stage/rel;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(f,dest);return dest

def staged_project_text(proj, user_name):
    for section in ('[application]', '[autoload]', '[display]'):
        if proj.count(section) != 1:
            raise RuntimeError(f'Expected exactly one {section} section in project.godot')
    if 'TransitionDirector="*res://scripts/ui/transitions/TransitionDirector.cs"' not in proj:
        raise RuntimeError('Expected the formal TransitionDirector autoload in project.godot')
    proj = proj.replace('[application]', f'[application]\nconfig/use_custom_user_dir=true\nconfig/custom_user_dir_name="{user_name}"')
    return proj.replace('[display]', 'MigrationQA="*res://qa_driver.gd"\n\n[display]')


def verify_capture(avi):
    if not avi.is_file() or avi.stat().st_size <= 65536:
        raise RuntimeError(f'Expected a recorded AVI larger than 64 KiB: {avi}')
    with avi.open('rb') as capture_file:
        header = capture_file.read(12)
    if header[:4] != b'RIFF' or header[8:12] != b'AVI ':
        raise RuntimeError(f'Recorded file lacks an AVI RIFF header: {avi}')


def stage(suite, *, output_root=ROOT/'.tmp/production-migration-qa/runs',
          driver=HERE/'qa_driver.gd', populate=fixture.create_songs):
    run=output_root/(dt.datetime.now().strftime('%Y%m%d-%H%M%S-')+suite+'-'+uuid.uuid4().hex[:8]);run.mkdir(parents=True);p=run/'client';p.mkdir()
    print('RUN:',run,flush=True)
    # Tight whitelist: never walk client/testdata; no whole .godot/imported or mono cache copy.
    for sub in ['scenes','scripts','shaders']:
        for f in sorted((CLIENT/sub).rglob('*')):
            if f.is_file() and f.suffix in {'.cs','.gd','.gdshader','.gdshaderinc','.tscn','.tres','.uid'}:capture(CLIENT,f.relative_to(CLIENT),p)
    assets=json.loads((ROOT/'release/asset-provenance.json').read_bytes())['assets'];asset_hashes={}
    for row in assets:
        if not row['path'].startswith('client/assets/'):continue
        rel=Path(row['path']).relative_to('client');f=capture(CLIENT,rel,p);h=sha(f)
        if h!=row['sha256']:raise RuntimeError('Provenance does not match '+str(rel))
        asset_hashes[rel.as_posix()]=h;imp=Path(str(rel)+'.import')
        if not (CLIENT/imp).exists():continue
        text=capture(CLIENT,imp,p).read_text(encoding='utf8');refs=set(re.findall(r'"res://(\.godot/imported/[^"\r\n]+)"',text))
        if not refs:raise RuntimeError('No imported blob in '+str(imp))
        for path in refs:capture(CLIENT,path,p)
    for f in ['.godot/global_script_class_cache.cfg','.godot/uid_cache.bin','.godot/.gdignore']:
        if (CLIENT/f).exists():capture(CLIENT,f,p)
    b=Path('.godot/mono/temp/bin/Debug');deps=json.loads((CLIENT/b/'DynamiteUniverse.deps.json').read_bytes());files={'DynamiteUniverse.runtimeconfig.json','DynamiteUniverse.deps.json'}
    for group in deps['targets'].values():
        for lib in group.values():
            for rel in lib.get('runtime',{}):files.add(Path(rel).name)
    dlls={};
    for name in sorted(files):dlls[name]=sha(capture(CLIENT,b/name,p))
    user_name='du-migration-qa-'+run.name
    proj=(CLIENT/'project.godot').read_text(encoding='utf8')
    (p/'project.godot').write_text(staged_project_text(proj,user_name),encoding='utf8');shutil.copy2(driver,p/'qa_driver.gd')
    for folder in ['roaming','local','temp','home','frames']:(run/folder).mkdir()
    user=run/'roaming'/user_name;user.mkdir();populate(user)
    (run/'manifest.json').write_text(json.dumps({'suite':suite,'source':str(CLIENT),'dllSha256':dlls,'officialAssetsSha256':asset_hashes,'formalTransitionSHA256':sha(CLIENT/'scripts/ui/transitions/TransitionDirector.cs'),'testdata':False,'user':str(user),'note':'local developer QA only; binaries copied from already-built formal Debug output'},indent=2),encoding='utf8');return run,p,user

def go(args,suite):
    run,project,user=stage(suite)
    if args.prepare_only:return
    engine=Path(args.godot).resolve(strict=True)
    if not engine.is_file() or 'mono' not in engine.name.lower() or engine.suffix.lower() != '.exe':
        raise RuntimeError('Supply original Godot Mono console engine, not an exported player.')
    cmd=[str(engine),'--path',str(project),'--windowed','--resolution','1280x720','--max-fps',str(args.fps),'--log-file',str(run/'player.log'),'--quit-after',str(args.fps*100)]
    if args.record:cmd+=['--fixed-fps',str(args.fps),'--write-movie',str(run/'capture.avi')]
    env=os.environ.copy();env.update(APPDATA=str(run/'roaming'),LOCALAPPDATA=str(run/'local'),TMP=str(run/'temp'),TEMP=str(run/'temp'),HOME=str(run/'home'),USERPROFILE=str(run/'home'),DOTNET_CLI_HOME=str(run/'home'),DOTNET_CLI_TELEMETRY_OPTOUT='1',HTTP_PROXY='',HTTPS_PROXY='',ALL_PROXY='',NO_PROXY='127.0.0.1,localhost',DU_QA_EXPECT_USER=user.as_posix(),DU_QA_OUT=run.as_posix(),DU_QA_SUITE=suite)
    with fixture.server(run) if suite=='store' else contextlib.nullcontext(({},[])) as (net,events):
        env.update(net);print('Starting ONLY this isolated viewport; mode:',suite,flush=True)
        with (run/'console.log').open('wb') as f:
            proc=subprocess.Popen(cmd,cwd=project,env=env,stdout=f,stderr=subprocess.STDOUT,stdin=subprocess.DEVNULL)
            try:ret=proc.wait(timeout=args.timeout)
            finally:
                if proc.poll() is None:proc.kill();proc.wait(timeout=10)
        log=(run/'player.log').read_text(encoding='utf8',errors='replace') if (run/'player.log').exists() else '';ok=(run/'result.json').is_file() and json.loads((run/'result.json').read_text(encoding='utf8')).get('pass') is True
        serious=[l for l in log.splitlines() if 'SCRIPT ERROR:' in l or 'ERROR:' in l];
        if ret or not ok or ('MIGRATION QA PASS '+suite) not in log or serious:raise RuntimeError(f'Client test failed (exit={ret}; marker={ok}); see {run}/player.log; {serious[:4]}')
        if suite=='store':
            required=[('GET','/.well-known/dynamite-universe',False),('POST','/api/v1/accounts/login',False),('GET','/api/v1/catalog/packs',True)];seen=[(e['method'],e['path'],e['authenticated']) for e in events if e['status']==200];pos=0
            for entry in required:pos=seen.index(entry,pos)+1
    if args.record:
        avi=run/'capture.avi'
        verify_capture(avi)
        ff=shutil.which('ffmpeg');
        if ff:subprocess.run([ff,'-nostdin','-v','error','-i',str(avi),'-c:v','libx264','-crf','18','-preset','fast','-pix_fmt','yuv420p','-c:a','aac','-b:a','160k','-movflags','+faststart',str(run/'capture.mp4')],check=True)
    print('PASS',suite,run,flush=True)
if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--godot',required=True);ap.add_argument('--suite',choices=['palette','library','store','calibration','all'],default='palette');ap.add_argument('--record',action='store_true');ap.add_argument('--prepare-only',action='store_true');ap.add_argument('--fps',type=int,default=30);ap.add_argument('--timeout',type=int,default=150);a=ap.parse_args()
    for s in (['library','store','palette','calibration'] if a.suite=='all' else [a.suite]):go(a,s)
