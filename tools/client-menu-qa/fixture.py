"""A disposable, loopback-only subset of the *existing* client protocol. No private data."""
from __future__ import annotations
import array, hashlib, hmac, io, json, math, secrets, threading, uuid, wave, zipfile
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit, parse_qs
from contextlib import contextmanager

SRC=Path(__file__).resolve().parents[2]
RELEASE_ID='134ecc9a-759a-4df4-b1c5-60af3bbba800'
TITLE='Synthetic Pulse'
def digest(data): return hashlib.sha256(data).hexdigest()
def encode(x): return json.dumps(x,ensure_ascii=False,separators=(',',':')).encode('utf-8')
def create_songs(user):
    # Small strict v2 charts written from scratch; neither original game data nor formal client/testdata is read.
    titles=['Copper Steps','Glass Triads','Violet Echo','Signal Room','Arc Current','Pulse Ladder','North Circuit','Cobalt Sequence','Quiet Relay','Prism Return','Green Detent','Copper Coda']
    for k,title in enumerate(titles):
        dest=user/'charts'/f'migration-{k+1}';dest.mkdir(parents=True,exist_ok=False)
        base=[174.61,220.,146.83][k%3];rate=16000;seconds=8;data=array.array('h')
        for i in range(rate*seconds):
            t=i/rate;j=int(t/.5);phase=t%.5;freq=base*[1.,1.25,1.5,1.125,1.25,1.5,1.75,1.5][(j+k)%8]
            a=min(phase/.012,1.)*math.exp(-7*phase)*min((seconds-t)/.15,1.)
            data.append(round(4000*a*(math.sin(math.tau*freq*t)+.12*math.sin(math.tau*2*freq*t))))
        with wave.open(str(dest/'audio.wav'),'wb') as w:w.setnchannels(1);w.setsampwidth(2);w.setframerate(rate);w.writeframes(data.tobytes())
        def bt(n,num=0,den=1):return dict(bar=n,numerator=num,denominator=den)
        chart=dict(format='dynamite-uv-chart',formatVersion=2,chartId='normal',audioOffsetSec=0.,timing={'bpms':[{'time':bt(0),'bpm':120}]},scrollSpeeds=[],notesLeft=[],notesRight=[],notesCenter=[dict(id=f'tap-{n}',type='tap',time=bt(n,1,4),center=2.5,width=.9) for n in range(3)])
        meta=dict(format='dynamite-uv-pack',formatVersion=2,id=f'qa.migration.{k+1}',revision=1,title=title,artist='Community QA / synthesized audio',audio='audio.wav',preview={'startSec':.1,'durationSec':6.5},charts=[dict(id='normal',difficulty='normal',level=1,charters=['Fixture author'],file='normal.json')]);(dest/'normal.json').write_bytes(encode(chart));(dest/'meta.json').write_bytes(encode(meta))

def release_data():
    folder=SRC/'schemas/chart-format-v2/examples/golden-pack';vector=json.loads((SRC/'schemas/chart-format-v2/vectors/gameplay-digest-v1.json').read_bytes());meta=json.loads((folder/'meta.json').read_bytes());meta['charts']=[meta['charts'][0]];chart=meta['charts'][0];audio=(folder/meta['audio']).read_bytes();assert digest(audio)==vector['audioSha256'];b=io.BytesIO()
    with zipfile.ZipFile(b,'w',zipfile.ZIP_DEFLATED) as z:
        for name,data in [('meta.json',encode(meta)),(chart['file'],(folder/chart['file']).read_bytes()),(meta['audio'],audio)]:z.writestr(zipfile.ZipInfo(name,(2026,1,1,0,0,0)),data,zipfile.ZIP_DEFLATED)
    buf=b.getvalue();bpms=[c['bpm'] for c in json.loads((folder/chart['file']).read_bytes())['timing']['bpms']];row=dict(id=RELEASE_ID,packId=meta['id'],version='1',title=meta['title'],artist=meta['artist'],revision=meta['revision'],size=len(buf),sha256=digest(buf),rights='Project-generated protocol example; loopback QA only',publishedAt='2026-01-01T00:00:00Z',display=dict(validationProfile='desktop-mobile-media-1',cover=None,charts=[dict(chartId=chart['id'],difficulty=chart['difficulty'],difficultyKey=None,level=chart['level'],unrated=False,charters=chart['charters'],bpmMin=min(bpms),bpmMax=max(bpms))]));details={'release':row,'charts':[dict(chartId=chart['id'],digest=vector['expectedSha256'],rulesetId=vector['rulesetId'])]};return row,details,buf

@contextmanager
def server(run):
    row,details,buf=release_data();name='fixture_'+secrets.token_hex(3);pwd='Loopback'+secrets.token_hex(9);token=secrets.token_hex(32);account={'id':str(uuid.uuid4()),'username':name};instance=str(uuid.uuid4());session={'active':False};events=[];lock=threading.Lock()
    class Handler(BaseHTTPRequestHandler):
        def log_message(self,*_): pass # do not record access tokens, query/address or credentials
        def do_POST(self):self.route(True)
        def do_GET(self):self.route(False)
        def route(self,post):
            u=urlsplit(self.path);path=u.path;received=self.headers.get('Authorization','');authorized=session['active'] and hmac.compare_digest(received,'Bearer '+token)
            def reply(code,body=None,kind='application/json'):
                raw=(body if isinstance(body,bytes) else encode(body)) if body is not None else b''
                with lock:
                    events.append({'method':self.command,'path':path,'status':code,'authenticated':bool(authorized)});(run/'loopback-events.json').write_bytes(encode(events))
                self.send_response(code);self.send_header('Content-Type',kind);self.send_header('Content-Length',str(len(raw)));self.end_headers();self.wfile.write(raw)
            if self.headers.get('Host')!=f'127.0.0.1:{self.server.server_port}':return reply(403)
            if not post and path=='/.well-known/dynamite-universe':return reply(200,dict(protocol='dynamite-universe',protocolVersion=1,instanceId=instance,name='Loopback QA',implementationStage='local-fixture',capabilities=dict(accounts=True,catalog=True,personalRecords=False),contracts=dict(accounts=1,catalog=1,personalRecords=0,packageFormat='dynamite-uv-pack/2',digestAlgorithm='gameplay-v1')))
            if post and path=='/api/v1/accounts/login':
                try:
                    if self.headers.get('Transfer-Encoding','').lower()=='chunked':
                        raw=bytearray()
                        for _ in range(64):
                            n=int(self.rfile.readline(64).split(b';',1)[0].strip(),16)
                            if n==0:
                                if self.rfile.readline(4)!=b'\r\n':return reply(400)
                                break
                            if n<0 or len(raw)+n>2048:return reply(400)
                            raw.extend(self.rfile.read(n))
                            if self.rfile.read(2)!=b'\r\n':return reply(400)
                        else:return reply(400)
                    else:
                        n=int(self.headers.get('Content-Length','0'))
                        if not 1<=n<=2048:return reply(400)
                        raw=self.rfile.read(n)
                    data=json.loads(raw);ok=data.get('username')==name and hmac.compare_digest(str(data.get('password','')),pwd)
                except (TypeError,ValueError,UnicodeError,AttributeError):ok=False
                if not ok:return reply(401)
                session['active']=True;return reply(200,{'accessToken':token,'expiresAt':(datetime.now(timezone.utc)+timedelta(minutes=40)).isoformat(),'account':account})
            if not authorized:return reply(401)
            if not post and path=='/api/v1/accounts/me':return reply(200,account)
            if post and path=='/api/v1/accounts/logout':session['active']=False;return reply(204)
            if not post and path=='/api/v1/catalog/packs':
                q=parse_qs(u.query);query=q.get('q',[''])[0].lower();offset=q.get('offset',['0'])[0];return reply(200,[row] if offset=='0' and query in (row['title']+' '+row['artist']).lower() else [])
            base='/api/v1/catalog/releases/'+RELEASE_ID
            if not post and path==base:return reply(200,details)
            if not post and path==base+'/download':return reply(200,buf,'application/zip')
            return reply(404)
    srv=ThreadingHTTPServer(('127.0.0.1',0),Handler);srv.daemon_threads=True;th=threading.Thread(target=srv.serve_forever,daemon=True);th.start()
    try:yield {'DU_QA_URL':f'http://127.0.0.1:{srv.server_port}','DU_QA_USER':name,'DU_QA_PASS':pwd},events
    finally:srv.shutdown();srv.server_close();th.join(timeout=3)
