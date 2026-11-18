"""Real ASP.NET API + actual WinForms timer, using only isolated synthetic accounts."""
from pathlib import Path
import hashlib
import json
import os
import secrets
import socket
import subprocess
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
WORK = ROOT / 'TestArtifacts' / ('background-' + secrets.token_hex(6))
WORK.mkdir(parents=True)
with socket.socket() as sock:
    sock.bind(('127.0.0.1', 0))
    port = sock.getsockname()[1]
endpoint = f'http://127.0.0.1:{port}'
env = {**os.environ, 'FS_DATA': str(WORK / 'database'),
       'ASPNETCORE_URLS': endpoint, 'ASPNETCORE_ENVIRONMENT': 'Production'}

def request(path, data=None):
    req = urllib.request.Request(endpoint + path,
        data=None if data is None else json.dumps(data).encode(),
        headers={'Content-Type': 'application/json', 'User-Agent': 'ForeverSocialCompanion/1.4.0'})
    with urllib.request.urlopen(req, timeout=10) as result:
        return json.load(result)

with (WORK / 'server.log').open('w', encoding='utf-8') as log:
    server = subprocess.Popen([str(DOTNET), str(ROOT / 'Server/bin/Debug/net10.0/Server.dll')], env=env,
                              stdout=log, stderr=subprocess.STDOUT)
    try:
        for attempt in range(100):
            try:
                request('/health')
                break
            except Exception:
                time.sleep(.1)
        else:
            raise RuntimeError('Local API did not start')
        tokens = [request('/v1/enroll', {'format': 1, 'enabled': True, 'client': 'forever', 'actor': actor,
                                       'community': 'TestRealm'})['token']
                  for actor in ('Alice-TestRealm', 'Beren-TestRealm')]
        root = WORK / 'client' / '_forever_'
        (root / 'Interface/AddOns/ForeverSocial/Data').mkdir(parents=True)
        (root / 'Interface/AddOns/ForeverSocial/ForeverSocial_Camelot.toc').write_text('## X-ForeverSocial-Companion: 1\nData/Companion.lua\n', encoding='utf-8')
        (root / 'Interface/AddOns/ForeverSocial/Data/Companion.lua').write_text('-- Empty optional cache\n', encoding='utf-8')
        saved = root / 'WTF/Account/TEST/SavedVariables/ForeverSocial.lua'
        saved.parent.mkdir(parents=True)
        saved.write_text('ForeverSocialBridge={["format"]=1,["characters"]={["Alice-TestRealm"]='
                         '{["client"]="forever",["community"]="TestRealm",["enabled"]=true,["savedAt"]=1,["records"]={}}}}', encoding='utf-8')
        report = ROOT / 'TestArtifacts' / os.environ.get('FS_BACKGROUND_REPORT', 'background-results.json')
        run = subprocess.run([str(DOTNET), str(ROOT / 'WindowsTests/bin/Debug/net10.0-windows/ForeverSocial.Companion.WindowsTests.dll'),
                              '--background-http', str(root), endpoint, str(report)],
                             env={**os.environ, 'FS_TEST_TOKEN': tokens[0], 'FS_TEST_OTHER_TOKEN': tokens[1]},
                             capture_output=True, text=True, encoding='utf-8', timeout=60)
        if run.returncode:
            raise RuntimeError(run.stdout + run.stderr)
        data = json.loads(report.read_text(encoding='utf-8'))
        data['sourceSHA256'] = {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                               for p in ROOT.rglob('*') if p.suffix in ('.cs', '.csproj', '.py')
                               and not {'bin', 'obj', 'dist', '.dotnet', 'TestArtifacts'}.intersection(p.relative_to(ROOT).parts)}
        report.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
        print(run.stdout.strip())
    finally:
        server.terminate()
        server.wait(timeout=15)
