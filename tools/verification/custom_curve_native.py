#!/usr/bin/env python3
"""Prepare a clean-room 2.1 fixture or run already-built Godot projects, headless and serially.
`prepare` never starts Godot. Native execution requires the separate explicit `run --ready`
after build/launch clearance. Results go to new subdirectories; no cleanup of other processes.
"""
from __future__ import annotations
import argparse
from copy import deepcopy
from datetime import datetime
import hashlib
import json
import math
import os
from pathlib import Path
from queue import Empty, Queue
import re
import subprocess
import sys
from threading import Thread
import time
import wave

ROOT = Path(__file__).resolve().parents[2]
EXPR = '1-(1-t)^1.5*cos(3*pi*t)'
DRIVER = Path(__file__).with_name('custom_curve_game_driver.gd')
DEFAULT_ENGINE = ROOT.parent / 'godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64.exe'


def need(condition, message):
    if not condition:
        raise RuntimeError(message)


def write_json(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def runtime_stamp(project, assembly):
    binaries = ROOT/project/'.godot/mono/temp/bin/Debug'
    return {name: hashlib.sha256((binaries/name).read_bytes()).hexdigest() for name in
            (assembly, 'DynamiteUniverse.Shared.dll', 'DynamiteUniverse.Presentation.dll')}


def clean_native_log(text, label):
    errors = [line for line in text.splitlines() if re.search(r'(?:^|\s)(?:SCRIPT ERROR|ERROR):', line)]
    need(not errors, f'{label}: unexpected Godot errors: {errors[:4]}')


def bar(n, num=0, den=1):
    return dict(bar=n, numerator=num, denominator=den)


def progress(t):
    return 1 - (1-t)**1.5 * math.cos(3*math.pi*t)


def prepare(folder):
    folder = Path(folder).resolve()
    folder.mkdir(parents=True, exist_ok=False)
    pack = folder / 'pack'
    pack.mkdir()
    base = dict(formatVersion=2, formatMinor=1)
    custom = dict(curveToNext='custom', customExpr=EXPR)
    chart = dict(format='dynamite-uv-chart', **base, chartId='rebound', audioOffsetSec=.5,
        timing=dict(bpms=[dict(time=bar(0), bpm=240)]),
        scrollSpeeds=[dict(time=bar(0), value=1, **custom), dict(time=bar(2), value=1.25)],
        notesLeft=[], notesCenter=[dict(id='park', type='mixer', time=bar(0), center=2, width=1,
            **custom, nodes=[dict(id='park-tail', time=bar(2), center=2.4, width=1)]),
            dict(id='scroll-witness', type='barLine', time=bar(1), center=2.5, width=1, scrollSpeedMultiplier=.25)],
        notesRight=[dict(id='caps', type='hold', time=bar(3), center=1, width=1, **custom,
            nodes=[dict(id='caps-tail', time=bar(3,1,2), center=3, width=1, judge=True)])])
    write_json(pack/'rebound.json', chart)
    write_json(pack/'meta.json',dict(format='dynamite-uv-pack', **base, id='probe.custom-rebound',
        revision=1, title='Custom Rebound Probe', artist='Verification', audio='tone.wav',
        charts=[dict(id='rebound', difficulty='hard', level=1, charters=['Verification'], file='rebound.json')]))
    # A synthetic tone followed by silence. Never copy testdata or use user media.
    with wave.open(str(pack/'tone.wav'), 'wb') as out:
        out.setparams((1, 1, 16000, 0, 'NONE', 'not compressed'))
        out.writeframes(bytes(128 + (round(12*math.sin(2*math.pi*220*i/16000)) if i<4000 else 0) for i in range(16000*6)))
    # Receiver 2, width 1, center = 2 + .4*p; lose coverage when p > 1.25.
    # Determine the two crossings independently from the expression (not the product evaluator).
    def root(a,b):
        positive = progress(a)>1.25
        for _ in range(60):
            m = (a+b)/2
            if (progress(m)>1.25)==positive: a=m
            else: b=m
        return .5+2*(a+b)/2
    leave, reenter = root(0,.3), root(.3,.5)
    missed = [t for k in range(1,17) if leave+.125 <= (t:=.5+k/8)<reenter]
    need(missed==[1.125,1.25], 'bad independent fixture oracle')
    write_json(folder/'fixture.json',dict(expression=EXPR, leave=leave, reenter=reenter, graceSeconds=.125,
        missedMixerTicks=missed, mixerUnits=17, holdUnits=2, visualBarLineUnits=0,
        files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(pack.iterdir())}))
    print('PREPARED', folder, '19 units; only native run can produce observed results.')


def guard():
    # Refuse even pre-existing non-visible Godot/editor instances rather than killing one.
    need(os.name=='nt','this Windows guard must be ported before running elsewhere')
    cmd = "Get-Process | Where-Object { $_.ProcessName -match '^(Godot|DynaMaker|DynamiteUniverse)' } | ForEach-Object { '{0}: {1}: {2}' -f $_.Id,$_.ProcessName,$_.MainWindowTitle }"
    shell = Path(os.environ['SystemRoot']) / 'System32/WindowsPowerShell/v1.0/powershell.exe'
    out = subprocess.run([str(shell),'-NoProfile','-Command',cmd],check=True,capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW)
    need(not out.stdout.strip(), 'blocked: existing Godot/editor/game process: '+out.stdout.decode(errors='replace').strip())


def environment(work):
    env = {k:v for k,v in os.environ.items() if not k.startswith(('DUV_VERIFY_', 'DYNAMITE_UNIVERSE_START_SEC'))}
    env['DOTNET_ROLL_FORWARD']='LatestMajor'
    env['GODOT_SILENCE_ROOT_WARNING']='1'
    # Each probe gets its own temporary desktop environment; never write a score.
    for key in ('APPDATA','LOCALAPPDATA','XDG_DATA_HOME','XDG_CONFIG_HOME','XDG_CACHE_HOME'):
        subdir = work/key
        subdir.mkdir(exist_ok=True)
        env[key]=str(subdir)
    return env


def game(engine, pack, output, fps, auto):
    guard()
    label=f'game-{fps}-'+('auto' if auto else 'input')
    output=output/label
    output.mkdir()
    trace=output/'frames.jsonl'; log=output/'native.log'
    stamp = runtime_stamp('client','DynamiteUniverse.dll'); write_json(output/'assemblies.json', stamp)
    env=environment(output)
    env.update(DUV_VERIFY_PACK=str(pack), DUV_VERIFY_CHART_ID='rebound',DUV_VERIFY_MODE='v2',DUV_VERIFY_AUTO='1' if auto else '0',
        DUV_VERIFY_FIXED_FPS=str(fps), DUV_VERIFY_FRAME_COUNT=str(5*fps),DUV_VERIFY_TRACE=str(trace),DUV_VERIFY_READY=str(output/'ready'))
    args=[str(engine),'--headless','--audio-driver','Dummy','--path',str(ROOT/'client'), '--log-file',str(output/'engine.log'),
        '--resolution','1920x1080','--fixed-fps',str(fps),'--quit-after',str(5*fps+180)]
    args += ['res://scenes/gameplay.tscn'] if auto else ['--script',str(DRIVER)]
    with log.open('xb') as stream:
        r=subprocess.run(args, stdout=stream, stderr=subprocess.STDOUT,env=env,timeout=60,creationflags=subprocess.CREATE_NO_WINDOW)
    text=log.read_text(encoding='utf-8',errors='replace')
    clean_native_log(text,label)
    need(stamp==runtime_stamp('client','DynamiteUniverse.dll'),f'{label}: assemblies changed during verification')
    need(r.returncode==0 and (output/'ready').is_file() and trace.is_file(), f'{label}: game did not finish cleanly, inspect {log}')
    need(re.search(r'identity pack=probe\.custom-rebound chart=rebound ruleset=dynamite-uv-ruleset-2\.1 digest=[0-9a-f]{64}',text),f'{label}: identity not verified')
    need(re.search(r'notes=\d+ units=19 headline=19\b',text),f'{label}: incorrect main judgement count')
    frames=[json.loads(line) for line in trace.read_text(encoding='utf-8').splitlines()]
    need(len(frames)==5*fps, f'{label}: incomplete trace ({len(frames)} frames)')
    by_t={row['time']:row for row in frames}
    need(5.0 in by_t, f'{label}: unexpected song clock or user offset')
    last=frames[-1]['judgement']
    need((last['resolved'],last['total'],last['pending'])==(19,19,0),f'{label}: incomplete judgement {last}')
    result=dict(fps=fps,auto=auto,frames=len(frames),judgement=last,score=frames[-1]['score'])
    if auto:
        need(last['perfect']==19 and last['miss']==0, f'{label}: Auto expected 19 Perfect: {last}')
    else:
        touches=[json.loads(s.removeprefix('CUSTOM_TOUCH ')) for s in text.splitlines() if s.startswith('CUSTOM_TOUCH ')]
        need([(s['time'],s['pressed']) for s in touches]==[(.5,True),(.75,False)], f'{label}: injected touch sequence differs: {touches}')
        # The Hold is intentionally never touched. The Mixer loses two ticks then reconnects.
        need(by_t[1.0]['judgement']['miss']==0 and by_t[1.25]['judgement']['miss']==2 and
             by_t[1.5]['judgement']['miss']==2 and by_t[2.5]['judgement']['perfect']==15,
             f'{label}: Mixer excursion/return not observed')
        if fps==64:
            missed=[row['time'] for i,row in enumerate(frames) if i and row['time']<=2.5 and row['judgement']['miss']>frames[i-1]['judgement']['miss']]
            need(missed==[1.125,1.25],f'{label}: wrong missed ticks {missed}')
        need((last['perfect'],last['miss'],last['great'],last['good'])==(15,4,0,0),f'{label}: incorrect input grades {last}')
        reports=[json.loads(s.removeprefix('CUSTOM_OUTLINE ')) for s in text.splitlines() if s.startswith('CUSTOM_OUTLINE ')]
        need(len(reports)==1 and reports[0]['uncappedInterior']>0 and reports[0]['sawStart'] and reports[0]['sawEnd'], f'{label}: Hold outline flags not observed: {reports}')
        result.update(touches=touches,outline=reports[0])
    # Cancel fall-speed preference, parent multiplier and base px/s by taking a ratio.
    # The marker is at 1.5s; its runtimeId and local position come from the real renderer.
    def scaled_distance(t):
        v=next(v for v in by_t[t]['active'] if v['runtimeId']=='scroll-witness')
        return (861-v['screenY'])/(1.5-t)
    observed=scaled_distance(1.0)/scaled_distance(.5)
    expected=1+.25*progress(.25)
    need(abs(observed-expected)<.001,f'{label}: Scroll projection ratio {observed} vs {expected}')
    result['scrollRatio']=observed
    write_json(output/'result.json', result)
    print('PASS',label, result,flush=True)
    return by_t,result


def stage(engine,pack,output):
    guard()
    output=output/'stage'; output.mkdir()
    stamp = runtime_stamp('editor','DynaMakerUv.Editor.dll'); write_json(output/'assemblies.json', stamp)
    args=[str(engine),'--headless','--audio-driver','Dummy','--path',str(ROOT/'editor'),'--max-fps','60','--log-file',str(output/'engine.log')]
    proc=subprocess.Popen(args,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,
        env=environment(output),creationflags=subprocess.CREATE_NO_WINDOW)
    events=Queue()
    def pump():
        with (output/'native.log').open('xb') as log:
            for line in proc.stdout:
                log.write(line); log.flush()
                if line.startswith(b'DYNA_STAGE '): events.put(json.loads(line[11:].decode('utf-8')))
        events.put(None)
    reader=Thread(target=pump,daemon=True);reader.start()
    def send(command,**props):
        msg=dict(command=command,**props)
        with (output/'commands.jsonl').open('a',encoding='utf-8') as file: file.write(json.dumps(msg)+'\n')
        proc.stdin.write((json.dumps(msg,separators=(',',':'))+'\n').encode());proc.stdin.flush()
    def wait(pred, expected_error=False):
        deadline=time.monotonic()+20
        while time.monotonic()<deadline:
            try: event=events.get(timeout=max(.01,deadline-time.monotonic()))
            except Empty: break
            need(event is not None,'stage exited early')
            if event['event']=='error' and not expected_error: raise RuntimeError('stage: '+str(event))
            if pred(event): return event
        raise RuntimeError('stage IPC timeout; see '+str(output/'native.log'))
    def tick(rev, req, playing):
        return wait(lambda e:e['event']=='tick' and e['revision']==rev and e['clockRequest']==req and e['playing']==playing)
    def snapshot(idx):
        send('audioSnapshot',requestId=idx)
        return wait(lambda e:e['event']=='audioSnapshot' and e['requestId']==idx)
    chart=json.loads((pack/'rebound.json').read_text(encoding='utf-8'))
    props=dict(title='Custom Rebound Probe',audioPath=str(pack/'tone.wav'),difficulty='hard',level=1,selection=['park'])
    try:
        wait(lambda e:e['event']=='ready'); send('load',chart=json.dumps(chart),revision=1,bar=0,**props)
        load=wait(lambda e:e['event']=='input' and 'loaded chart=rebound' in e['detail'])
        tick(1,0,False);send('seek',bar=.5,clockRequest=1); seek=tick(1,1,False)
        need(abs(seek['second']-1)<1e-9,'stage seek did not follow chart time')
        send('play',playing=True,clockRequest=2);tick(1,2,True)
        start=wait(lambda e:e['event']=='tick' and e['revision']==1 and e['frame'] and e['second']>1.1)
        before=snapshot(1)
        change=deepcopy(chart); change['notesRight'][0]['customExpr']='t^1.5'
        send('update',chart=json.dumps(change),revision=2,**props)
        updated=wait(lambda e:e['event']=='input' and 'updated chart=rebound' in e['detail'] and 'selection=1' in e['detail'])
        counts=[int(re.search(r'\bnotes=(\d+)',item['detail']).group(1)) for item in (load,updated)]
        need(counts[0]>counts[1]>5,'live chart rebuild did not change the flattened path')
        resumed=tick(2,2,True); after=snapshot(2)
        need(resumed['second']>=start['second'] and before['audioLoads']==after['audioLoads']==1 and before['audioRestarts']==after['audioRestarts'], 'update lost transport or reloaded audio')
        send('seek',bar=1,clockRequest=3); at=tick(2,3,True);need(abs(at['second']-1.5)<1e-9,'bad playing seek')
        wait(lambda e:e['event']=='tick' and e['revision']==2 and e['clockRequest']==3 and e['frame'] and e['second']>1.55)
        send('play',playing=False,clockRequest=4); paused=tick(2,4,False)
        bad=deepcopy(change);bad['notesCenter'][0]['customExpr']='t+1'
        send('update',chart=json.dumps(bad),revision=3,**props)
        rejected=wait(lambda e:e['event']=='error',True)
        need('/notesCenter/0/customExpr' in rejected['message'] and 'f(0)' in rejected['message'], 'invalid formula diagnostic was not attributable to the correct slot')
        send('seek',bar=1.25,clockRequest=5); previous=tick(2,5,False)
        need(abs(previous['second']-1.75)<1e-9,'rejected chart replaced valid revision')
        send('quit');proc.stdin.close(); rc=proc.wait(timeout=15);reader.join(timeout=3)
        need(rc==0,'stage teardown exit '+str(rc))
        clean_native_log((output/'native.log').read_text(encoding='utf-8',errors='replace'),'stage')
        need(stamp==runtime_stamp('editor','DynaMakerUv.Editor.dll'),'stage: assemblies changed during verification')
        result=dict(load=load,updated=updated,seek=seek,playing=start,resumed=resumed,paused=paused,
                    audioBefore=before,audioAfter=after,rejected=rejected,recovered=previous,exitCode=rc)
        write_json(output/'result.json',result);print('PASS stage real IPC',flush=True)
        return result
    finally:
        if not proc.stdin.closed: proc.stdin.close()
        if proc.poll() is None:
            try: proc.wait(timeout=5)
            except subprocess.TimeoutExpired: proc.kill();proc.wait(timeout=5) # ONLY this test's Popen handle
        reader.join(timeout=5)


def main():
    p=argparse.ArgumentParser(description=__doc__)
    sub=p.add_subparsers(dest='mode',required=True)
    sub.add_parser('prepare').add_argument('folder',type=Path)
    run=sub.add_parser('run');run.add_argument('folder',type=Path);run.add_argument('--ready',action='store_true',required=True)
    run.add_argument('--godot',type=Path,default=DEFAULT_ENGINE)
    run.add_argument('--only',choices=('all','game','stage'),default='all')
    args=p.parse_args()
    if args.mode=='prepare': prepare(args.folder);return
    folder=args.folder.resolve();pack=folder/'pack';engine=args.godot.resolve(); need(engine.is_file(),'missing Godot Mono')
    need(engine.name.lower().endswith('.exe') and not engine.name.lower().endswith('_console.exe'), 'run the real Godot executable, not its launcher')
    expected=json.loads((folder/'fixture.json').read_text(encoding='utf-8'))['files']
    need(all(hashlib.sha256((pack/name).read_bytes()).hexdigest()==h for name,h in expected.items()),'fixture changed after preparation')
    report=folder/datetime.now().strftime('run-%Y%m%d-%H%M%S-%f');report.mkdir()
    print('EVIDENCE',report,flush=True)
    # Independent native suites both get logged, even if one fails. No parallel builds/processes.
    failures=[]
    def attempt(label,fn):
        try: fn()
        except Exception as exc:
            failures.append(f'{label}: {type(exc).__name__}: {exc}');print(failures[-1],file=sys.stderr,flush=True)
    if args.only in ('all','stage'): attempt('stage',lambda:stage(engine,pack,report))
    if args.only in ('all','game'):
        attempt('auto',lambda:game(engine,pack,report,64,True))
        results={}
        for fps in (4,64):
            attempt('input-'+str(fps),lambda:results.update({fps:game(engine,pack,report,fps,False)}))
        if len(results)==2:
            def compare():
                a,b=results[4][0],results[64][0]
                # Each common post-head timestamp in the Mixer body must agree, not just the final score.
                for t in a:
                    if .75<=t<=2.5: need(a[t]['judgement']==b[t]['judgement'] and a[t]['score']==b[t]['score'],f'Mixer coarse/fine disagreement at {t}')
                need(results[4][1]['judgement']==results[64][1]['judgement'],'final coarse/fine grades differ')
                print('PASS same captured input at 4/64 fps, matched Mixer judgement and score at every common body frame')
            attempt('coarse-fine',compare)
    write_json(report/'status.json',dict(suite=args.only,failures=failures));return bool(failures)

if __name__=='__main__': sys.exit(main())
