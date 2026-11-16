"""Real HTTP + actual C# parser/client + shipped Lua modules, with mock WoW only."""
from pathlib import Path
import os,sys,json,time,subprocess,secrets,urllib.request,urllib.error,hashlib
from lupa.lua51 import LuaRuntime

ROOT=Path(__file__).resolve().parents[1];ADDON=ROOT.parent/'ForeverSocial'
PY=Path(sys.executable);DOTNET=Path('C:/Program Files/dotnet/dotnet.exe')
WORK=ROOT/'TestArtifacts'/('e2e-'+secrets.token_hex(5));WORK.mkdir(parents=True)
endpoint=os.environ.get('FS_TEST_URL','http://127.0.0.1:18891')
checks=[]
def request(path,data=None,token=None,method=None):
    body=None if data is None else json.dumps(data,ensure_ascii=False).encode()
    req=urllib.request.Request(endpoint+path,data=body,method=method,headers={'Content-Type':'application/json','User-Agent':'ForeverSocialCompanion/1.3.0',**({'Authorization':'Bearer '+token} if token else {})})
    with urllib.request.urlopen(req,timeout=20) as response:return json.loads(response.read() or b'null')
def table(value):
    if isinstance(value,str):return '"'+value.replace('\\','\\\\').replace('"','\\"').replace('\n','\\n')+'"'
    if value is True:return 'true'
    if value is False:return 'false'
    if isinstance(value,(int,float)):return str(int(value))
    return '{'+','.join('['+table(k)+']='+table(v) for k,v in value.items())+'}'
def vm(client,locale,name):
    lua=LuaRuntime(unpack_returned_tuples=True);g=lua.globals();g.TEST_CLIENT=client;g.TEST_INTERFACE=16001 if client=='forever' else 120105
    lua.execute((ADDON/'Tests/mock_wow.lua').read_text(encoding='utf-8'));g.MOCK.locale=locale;g.MOCK.now=int(time.time())
    lua.execute('function UnitFullName(unit) if unit=="player" then return "'+name+'", "TestRealm" end end')
    lua.execute('function GetUnitName(unit) if unit=="player" then return "'+name+'" end end; UnitName=UnitFullName')
    ns=lua.table();toc=ADDON/('ForeverSocial_Camelot.toc' if client=='forever' else 'ForeverSocial_Mainline.toc')
    for line in toc.read_text().splitlines():
        if line.strip() and not line.startswith('#'):lua.execute('return function(...)\n'+(ADDON/line.strip().replace('\\','/')).read_text(encoding='utf-8-sig')+'\nend')('ForeverSocial',ns)
    g.MOCK.Event('ADDON_LOADED','ForeverSocial');g.NS=ns;ns.Companion.Enable(ns.Companion,True)
    return lua,ns
def convert(value):
    if hasattr(value,'items'):return {k:convert(v) for k,v in value.items()}
    return value
def sync(lua,ns,root,token):
    ns.Companion.Export(ns.Companion);path=root/'WTF/Account/TEST/SavedVariables/ForeverSocial.lua';path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text('ForeverSocialBridge = '+table(convert(lua.globals().ForeverSocialBridge)),encoding='utf-8')
    env={**os.environ,'FS_TEST_TOKEN':token}
    result=subprocess.run([str(DOTNET),str(ROOT/'Tests/bin/Debug/net10.0/Tests.dll'),'bridge-test',str(root),str(path),endpoint],env=env,capture_output=True,text=True,encoding='utf-8',timeout=45)
    assert result.returncode==0,result.stdout+result.stderr
    lua.execute((root/'Interface/AddOns/ForeverSocial/Data/Companion.lua').read_text(encoding='utf-8'))
    payload=lua.globals().ForeverSocialCompanionData;payload.service=ns.Companion.SERVICE
    # Test endpoint is loopback; production payload is required to match the configured HTTPS service.
    result=ns.Companion.Import(ns.Companion,payload);assert not isinstance(result,tuple),result
    return None

server=None
try:
    if 'FS_TEST_URL' not in os.environ:
        env={**os.environ,'FS_DATA':str(WORK/'database'),'ASPNETCORE_URLS':endpoint,'ASPNETCORE_ENVIRONMENT':'Production'}
        log=open(WORK/'server.log','w',encoding='utf-8')
        server=subprocess.Popen([str(DOTNET),str(ROOT/'Server/bin/Debug/net10.0/Server.dll')],env=env,stdout=log,stderr=subprocess.STDOUT)
        for _ in range(80):
            try:request('/health');break
            except Exception:time.sleep(.15)
        else:raise RuntimeError('Server did not start')
    request('/health');checks.append({'name':'actual API health','ok':True})
    for client in ('retail','forever'):
        for locale in ('enUS','deDE'):
            suffix=secrets.token_hex(3);aname='A'+suffix;bname='B'+suffix
            a,na=vm(client,locale,aname);b,nb=vm(client,locale,bname)
            ar=WORK/(client+locale+'A')/('_forever_' if client=='forever' else '_retail_');br=WORK/(client+locale+'B')/ar.name
            for root in (ar,br):
                slot=root/'Interface/AddOns/ForeverSocial/Data';slot.mkdir(parents=True)
                toc='ForeverSocial_Camelot.toc' if client=='forever' else 'ForeverSocial_Mainline.toc'
                (slot.parent/toc).write_bytes((ADDON/toc).read_bytes())
                (slot/'Companion.lua').write_bytes((ADDON/'Data/Companion.lua').read_bytes())
            ca=request('/v1/enroll',{'format':1,'enabled':True,'client':client,'actor':na.App.actor});cb=request('/v1/enroll',{'format':1,'enabled':True,'client':client,'actor':nb.App.actor})
            try:
                na.Social.SaveProfile(na.Social,a.table_from({'public':True,'bio':'\U0001F600'*500,'activities':'\U0001F600'*120}))
                post=na.Social.CreatePost(na.Social,'Grüße, ein gemeinsamer Serverbeitrag! \U0001F600 \u2028','general');assert not isinstance(post,tuple),post
                listing=na.Social.CreateListing(na.Social,'Stratholme','TANK,HEALER','Eine gemeinsame Gruppe','20:00');assert not isinstance(listing,tuple),listing
                na.Social.RecordEncounter(na.Social,nb.App.actor);na.Notes.Save(na.Notes,nb.App.actor,'PRIVATE_E2E_SECRET')
                sync(a,na,ar,ca['token']);sync(b,nb,br,cb['token'])
                remote=nb.App.db.posts[post.id];assert remote and remote.provenance=='backend';assert remote.text==post.text
                assert nb.Social.GetProfile(nb.Social,na.App.actor).bio=='\U0001F600'*500
                nb.Social.AddComment(nb.Social,post.id,'Antwort vom zweiten Client');nb.Social.ToggleLike(nb.Social,post.id)
                sync(b,nb,br,cb['token']);sync(a,na,ar,ca['token'])
                assert na.Social.LikeCount(na.Social,post.id)==1
                assert len(na.Social.Comments(na.Social,post.id))==1
                raw=json.dumps(request('/v1/snapshot',token=ca['token']),ensure_ascii=False);assert 'PRIVATE_E2E_SECRET' not in raw
                bad={'records':[{'kind':'posts','record':{'id':nb.App.actor+'@forged','author':nb.App.actor,'created':int(time.time()),'updated':int(time.time()),'version':1,'category':'general','text':'forged','deleted':False}}]}
                try:request('/v1/records',bad,ca['token']);raise AssertionError('Foreign author accepted')
                except urllib.error.HTTPError as e:assert e.code==403,e.code
                checks.append({'name':client+':'+locale+':two-way C# file bridge, Unicode, comments, likes, privacy, author binding','ok':True})
                assert nb.App.db.listings[listing.id].provenance=='backend'
                assert not nb.Social.CanRate(nb.Social,na.App.actor),'A listing must not create an encounter proof'
                nb.Social.RecordEncounter(nb.Social,na.App.actor)
                scores=b.table_from({'friendly':5,'teamwork':4,'reliable':5,'helpful':4})
                rating=nb.Social.Rate(nb.Social,na.App.actor,scores,'Danke für die Gruppe','helpful,leader');assert not isinstance(rating,tuple),rating
                assert nb.Social.Follow(nb.Social,na.App.actor) is True
                assert nb.Social.SetShareFollows(nb.Social,True) is True
                sync(b,nb,br,cb['token']);sync(a,na,ar,ca['token'])
                assert na.App.db.ratings[rating.id].provenance=='backend'
                assert na.Social.RatingSummary(na.Social,na.App.actor).count==1
                assert na.Social.RatingSummary(na.Social,na.App.actor).awards.leader==1
                assert na.Social.ProfileStats(na.Social,na.App.actor).followers==1
                checks.append({'name':client+':'+locale+':reviews, awards, public follows and group listings; listings do not unlock reviews','ok':True})
                scores.friendly=3
                updated=nb.Social.Rate(nb.Social,na.App.actor,scores,'Aktualisierte Meinung','friendly');assert not isinstance(updated,tuple),updated
                assert updated.id==rating.id and updated.version>rating.version
                assert nb.Social.SetShareFollows(nb.Social,False) is True
                sync(b,nb,br,cb['token']);sync(a,na,ar,ca['token'])
                summary=na.Social.RatingSummary(na.Social,na.App.actor)
                assert summary.count==1 and summary.friendly==3
                assert na.Social.ProfileStats(na.Social,na.App.actor).followers==0
                assert not isinstance(na.Social.SetListingActive(na.Social,listing.id,False),tuple)
                saved_profile=na.Social.SaveProfile(na.Social,a.table_from({'public':False}))
                assert (saved_profile[0] if isinstance(saved_profile,tuple) else saved_profile) is not None,saved_profile
                assert not isinstance(na.Social.DeletePost(na.Social,post.id),tuple)
                sync(a,na,ar,ca['token']);sync(b,nb,br,cb['token'])
                assert nb.App.db.listings[listing.id].active is False
                assert nb.Social.GetProfile(nb.Social,na.App.actor) is None
                withdrawn=nb.App.db.posts[post.id]
                assert withdrawn is None or withdrawn.deleted and withdrawn.text==''
                # Other isolated integration runs may share this live community.
                # Withdrawal assertions must concern this run's author only.
                snapshot=request('/v1/snapshot',token=ca['token'])
                raw=json.dumps([r for r in snapshot['records'] if r['record']['author']==na.App.actor],ensure_ascii=False)
                assert 'Grüße, ein gemeinsamer Serverbeitrag!' not in raw and '\U0001F600'*500 not in raw
                checks.append({'name':client+':'+locale+':single updated review, follow/profile withdrawal, closed listing and redacted post deletion','ok':True})
            finally:
                request('/v1/account',token=ca['token'],method='DELETE');request('/v1/account',token=cb['token'],method='DELETE')
    report={'simulationOnly':True,'gameTested':False,'endpoint':endpoint,'checks':checks,'total':len(checks),'passed':len(checks),'sourceSHA256':{p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in ROOT.rglob('*') if p.suffix in ('.cs','.csproj') and not any(x in p.parts for x in ('bin','obj','dist','.dotnet'))},'addonSourceSHA256':{p.relative_to(ADDON).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in ADDON.rglob('*') if p.suffix in ('.lua','.toc')},'testSHA256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest()}
    (ROOT/'TestArtifacts'/os.environ.get('FS_TEST_REPORT','e2e-results.json')).write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print(json.dumps(report['checks'],indent=2))
finally:
    if server:server.terminate();server.wait(timeout=15);log.close()
