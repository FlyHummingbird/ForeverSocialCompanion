"""Actual API + Windows Forms access transfer/deletion, synthetic account only."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib, json, os, secrets, socket, subprocess, time, urllib.request

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / 'TestArtifacts' / ('access-' + secrets.token_hex(6))
WORK.mkdir(parents=True)
DOTNET = 'C:/Program Files/dotnet/dotnet.exe'
with socket.socket() as sock:
    sock.bind(('127.0.0.1', 0))
    endpoint = f'http://127.0.0.1:{sock.getsockname()[1]}/'
def request(path, body=None):
    req = urllib.request.Request(endpoint + path, data=None if body is None else json.dumps(body).encode(),
                                 headers={'Content-Type': 'application/json'})
    with urllib.request.urlopen(req, timeout=10) as result:
        return json.load(result)
with (WORK / 'server.log').open('w', encoding='utf-8') as log:
    server = subprocess.Popen([DOTNET, str(ROOT / 'Server/bin/Debug/net10.0/Server.dll')],
        env={**os.environ, 'FS_DATA': str(WORK / 'database'), 'ASPNETCORE_URLS': endpoint.rstrip('/')},
        stdout=log, stderr=subprocess.STDOUT)
    try:
        for _ in range(100):
            try:
                request('health')
                break
            except OSError:
                time.sleep(.1)
        else:
            raise RuntimeError('Local API did not start')
        token = request('v1/enroll', {'format': 1, 'enabled': True, 'client': 'forever',
            'actor': 'Alice-TestRealm', 'community': 'TestRealm'})['token']
        now = int(time.time())
        row = {'kind': 'posts', 'record': {'id': 'Alice-TestRealm@access', 'author': 'Alice-TestRealm',
            'created': now, 'updated': now, 'version': 1, 'category': 'general',
            'text': 'Synthetic access record', 'deleted': False}}
        req = urllib.request.Request(endpoint + 'v1/records', data=json.dumps({'records': [row]}).encode(),
            headers={'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token})
        with urllib.request.urlopen(req, timeout=10) as response:
            assert response.status == 200
        root = WORK / 'client' / '_forever_'
        slot = root / 'Interface/AddOns/ForeverSocial'
        (slot / 'Data').mkdir(parents=True)
        (slot / 'ForeverSocial_Camelot.toc').write_text('## X-ForeverSocial-Companion: 1\nData/Companion.lua\n', encoding='utf-8')
        (slot / 'Data/Companion.lua').write_text('-- Empty cache\n', encoding='utf-8')
        saved = root / 'WTF/Account/TEST/SavedVariables/ForeverSocial.lua'
        saved.parent.mkdir(parents=True)
        saved.write_text('ForeverSocialBridge={format=1,characters={["Alice-TestRealm"]={client="forever",community="TestRealm",enabled=true,savedAt=1,records={}}}}', encoding='utf-8')
        report = ROOT / 'TestArtifacts/access-140-results.json'
        run = subprocess.run([DOTNET, str(ROOT / 'WindowsTests/bin/Debug/net10.0-windows/ForeverSocial.Companion.WindowsTests.dll'),
            '--access-http', str(root), endpoint, str(report)], env={**os.environ, 'FS_TEST_TOKEN': token},
            capture_output=True, text=True, encoding='utf-8', timeout=90)
        assert run.returncode == 0, run.stdout + run.stderr
        data = json.loads(report.read_text(encoding='utf-8'))
        data['sourceSHA256'] = {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in ROOT.rglob('*') if p.suffix in ('.cs', '.csproj', '.py')
            and not {'bin', 'obj', 'dist', '.dotnet', 'TestArtifacts'}.intersection(p.relative_to(ROOT).parts)}
        report.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
        print(run.stdout.strip())
    finally:
        server.terminate()
        server.wait(timeout=15)
