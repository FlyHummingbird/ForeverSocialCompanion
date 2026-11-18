"""Actual API enrollment/auth regressions with isolated synthetic accounts only."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import os
import secrets
import socket
import subprocess
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / 'TestArtifacts' / ('enrollment-' + secrets.token_hex(6))
WORK.mkdir(parents=True)
with socket.socket() as sock:
    sock.bind(('127.0.0.1', 0))
    port = sock.getsockname()[1]
endpoint = f'http://127.0.0.1:{port}'
checks = []
server = None
log = (WORK / 'server.log').open('w', encoding='utf-8')

def request(path, data=None, token=None, method=None):
    req = urllib.request.Request(endpoint + path,
        data=None if data is None else json.dumps(data).encode(), method=method,
        headers={'Content-Type': 'application/json', 'User-Agent': 'ForeverSocialCompanion/1.4.0',
                 **({'Authorization': 'Bearer ' + token} if token else {})})
    try:
        with urllib.request.urlopen(req, timeout=10) as response:
            return response.status, json.loads(response.read() or 'null')
    except urllib.error.HTTPError as error:
        return error.code, None

def check(name, condition):
    assert condition, name
    checks.append({'name': name, 'ok': True})
    print('PASS ' + name)

def start():
    global server
    env = {**os.environ, 'FS_DATA': str(WORK / 'data'), 'ASPNETCORE_URLS': endpoint,
           'ASPNETCORE_ENVIRONMENT': 'Production'}
    env.pop('FS_INVITE', None)
    server = subprocess.Popen(['C:/Program Files/dotnet/dotnet.exe',
        str(ROOT / 'Server/bin/Debug/net10.0/Server.dll')], env=env, stdout=log, stderr=subprocess.STDOUT)
    for _ in range(100):
        try:
            if request('/health')[0] == 200:
                return
        except OSError:
            pass
        time.sleep(.1)
    raise RuntimeError('API did not start')

def stop():
    global server
    if server is not None:
        server.terminate()
        server.wait(timeout=15)
        server = None

try:
    start()
    check('health advertises open registration', request('/health')[1]['enrollment'] == 'open')
    valid = {'client': 'forever', 'actor': 'Alice-TestRealm', 'community': 'TestRealm',
             'format': 1, 'enabled': True}
    invalid = [{k: v for k, v in valid.items() if k != 'enabled'},
               {**valid, 'enabled': False}, {**valid, 'format': 2},
               {**valid, 'actor': 'Invalid'}, {**valid, 'client': 'unknown'},
               {**valid, 'community': 'OtherRealm'}]
    check('invalid scope or missing consent metadata is rejected',
          all(request('/v1/enroll', data)[0] == 400 for data in invalid))
    code, credential = request('/v1/enroll', valid)
    token = credential['token']
    check('first permitted character enrolls without invitation or secret environment', code == 200 and len(token) == 64)
    check('duplicate name returns conflict without issuing a new token', request('/v1/enroll', valid)[0] == 409)
    check('existing token remains valid after conflict', request('/v1/snapshot', token=token)[0] == 200)
    check('snapshot upload and deletion still require authentication',
          request('/v1/snapshot')[0] == 401 and request('/v1/records', {'records': []})[0] == 401
          and request('/v1/account', method='DELETE')[0] == 401)
    check('invalid bearer cannot read or delete accounts',
          request('/v1/snapshot', token='0' * 64)[0] == 401
          and request('/v1/account', token='0' * 64, method='DELETE')[0] == 401)
    now = int(time.time())
    forged = {'kind': 'posts', 'record': {'id': 'Beren-TestRealm@forged', 'author': 'Beren-TestRealm',
        'created': now, 'updated': now, 'version': 1, 'category': 'general', 'text': 'forged', 'deleted': False}}
    check('authenticated account cannot publish another author', request('/v1/records', {'records': [forged]}, token)[0] == 403)
    info_code, info = request('/v1/account', token=token)
    check('authenticated account metadata is scoped and never includes credentials',
          info_code == 200 and info['actor'] == valid['actor'] and info['client'] == 'forever'
          and info['community'] == 'TestRealm' and 'token' not in info)
    check('account inspection requires a valid bearer', request('/v1/account')[0] == 401
          and request('/v1/account', token='0' * 64)[0] == 401)
    own = {'kind': 'posts', 'record': {'id': 'Alice-TestRealm@receipt', 'author': 'Alice-TestRealm',
        'created': now, 'updated': now, 'version': 1, 'category': 'general', 'text': 'Receipt version one', 'deleted': False}}
    assert request('/v1/records', {'records': [own]}, token)[0] == 200
    receipt = request('/v1/snapshot', token=token)[1]['published']
    check('snapshot acknowledges the exact stored post content', len(receipt) == 1 and receipt[0]['record']['text'] == own['record']['text'])
    changed = {**own, 'record': {**own['record'], 'version': 2, 'text': 'Receipt version two'}}
    assert request('/v1/records', {'records': [changed]}, token)[0] == 200
    assert request('/v1/records', {'records': [own]}, token)[0] == 200
    receipt = request('/v1/snapshot', token=token)[1]['published']
    check('stale uploads cannot fabricate publication acknowledgement', receipt[0]['record']['version'] == 2 and receipt[0]['record']['text'] == 'Receipt version two')
    stop()
    start()
    check('unchanged format-1 database preserves pre-restart credentials', request('/v1/snapshot', token=token)[0] == 200)
    for _ in range(20):
        assert request('/v1/enroll', {**valid, 'enabled': False})[0] == 400
    check('enrollment attempts are bounded without weakening existing access',
          request('/v1/enroll', valid)[0] == 429 and request('/v1/snapshot', token=token)[0] == 200)
    check('authenticated deletion revokes only its own access',
          request('/v1/account', token=token, method='DELETE')[0] == 204
          and request('/v1/snapshot', token=token)[0] == 401)
finally:
    stop()
    log.close()
    report = {'generatedAtUTC': datetime.now(timezone.utc).isoformat(), 'total': 15,
              'passed': len(checks), 'checks': checks, 'realPlayerRecordsUploaded': False,
              'sourceSHA256': {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                  for p in ROOT.rglob('*') if p.suffix in ('.cs', '.csproj', '.py')
                  and not {'bin', 'obj', 'dist', '.dotnet', 'TestArtifacts'}.intersection(p.relative_to(ROOT).parts)}}
    (ROOT / 'TestArtifacts/enrollment-140-results.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
