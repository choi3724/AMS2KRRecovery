"""Disposable fixtures only: no installed game or real Steam invocation."""
from pathlib import Path
import os,subprocess,hashlib,json,uuid,struct
ROOT=Path(__file__).resolve().parents[1]; OUT=ROOT/'build/tests'/uuid.uuid4().hex[:8]; OUT.mkdir(parents=True)
paths=(ROOT/'data/managed-paths.txt').read_text().splitlines(); checks=[]
def fixture(name,build='25391793',all_files=False):
    base=OUT/name; game=base/'steamapps/common/Automobilista 2'; game.mkdir(parents=True)
    (base/'steamapps/appmanifest_1066890.acf').write_text('"appid" "1066890"\n"buildid" "'+build+'"\n"installdir" "Automobilista 2"\n"1066891" { "manifest" "123" }')
    for p in ['AMS2.exe','AMS2AVX.exe','Vehicles/unrelated-mod.dat','save-sentinel.dat']:
        f=game/p; f.parent.mkdir(parents=True,exist_ok=True);f.write_bytes(b'untouched')
    for p in (paths if all_files else paths[:6]):
        f=game/p;f.parent.mkdir(parents=True,exist_ok=True);f.write_bytes(b'old patch '+p.encode())
    return game
def snapshot(g):return {str(p.relative_to(g)):hashlib.sha256(p.read_bytes()).hexdigest() for p in g.rglob('*') if p.is_file() and 'Backup/AMS2KRRecovery/' not in p.as_posix()}
def run(name,g,mode='--remove',expect=0,**env):
    e=os.environ.copy();e.update(AMS2_STOCK_TEST_SHORTCUTS=str(OUT/'shortcuts'),RECOVERY_TEST_STEAM=str(OUT/'steam'));e.update(env)
    r=subprocess.run([str(ROOT/'build/RecoveryCheck.exe'),mode,str(g)],env=e,capture_output=True)
    (OUT/(name+'.log')).write_bytes(r.stdout+r.stderr)
    assert r.returncode==expect,(name,r.returncode,r.stderr.decode(errors='replace'))
    checks.append(name)
def absence(g):assert all(not (g/p).exists() for p in paths)
for version in ['0.1','0.6.4','0.7','0.84','0.85','0.86','0.87','0.88.1']:
    g=fixture('legacy-'+version,all_files=True)
    record=g/('Backup/AMS2-Korean/AMS2-KR-BETA-'+version);record.mkdir(parents=True)
    (record/'install-state.tsv').write_text('corrupt legacy state')
    (record/'files.tsv').write_text('preserve evidence')
    pending=g/'Backup/AMS2-Korean-Upgrade/current/pending.tsv';pending.parent.mkdir(parents=True);pending.write_text('interrupted old upgrade')
    run('legacy-'+version,g);absence(g)
    assert not (record/'install-state.tsv').exists() and not pending.exists()
    assert (record/'files.tsv').read_text()=='preserve evidence'
    assert (g/'Vehicles/unrelated-mod.dat').read_bytes()==b'untouched'
g=fixture('unknown-build-no-record','98765432',True);run('unknown-build-no-record',g);absence(g)
before=snapshot(g);run('repeat',g);assert snapshot(g)==before
g=fixture('rollback',all_files=True);before=snapshot(g);run('rollback',g,expect=1,RECOVERY_FAIL_AFTER='10');assert snapshot(g)==before
g=fixture('interruption',all_files=True);run('interruption',g,expect=77,RECOVERY_CRASH_AFTER='10');run('resume',g);absence(g)
g=fixture('cm'); cm=g/'Mods/state.json';cm.parent.mkdir();cm.write_text('{"Install":{"Mods":{"example":{}}}}');before=snapshot(g);run('cm-block',g,expect=1);assert snapshot(g)==before
cm.write_text('broken');run('cm-corrupt-block',g,expect=1)
g=fixture('corrupt-backup');run('interrupt-before-corruption',g,expect=77,RECOVERY_CRASH_AFTER='2')
backup=next((g/'Backup/AMS2KRRecovery').glob('*/before/0000'));backup.write_bytes(b'corrupt');run('corrupt-backup-rejected',g,expect=1)
g=fixture('preserve-newer');run('interrupt-newer',g,expect=77,RECOVERY_CRASH_AFTER='2');(g/paths[0]).write_bytes(b'newer unrelated content');run('newer-file-not-overwritten',g,expect=1);assert (g/paths[0]).read_bytes()==b'newer unrelated content'
g=fixture('path-guard');p=g/paths[0];p.unlink();p.mkdir();before=snapshot(g);run('directory-at-file-blocked',g,expect=1);assert snapshot(g)==before
g=fixture('steam-update');run('interrupt-before-steam-update',g,expect=77,RECOVERY_CRASH_AFTER='2');acf=g.parents[1]/'appmanifest_1066890.acf';acf.write_text(acf.read_text().replace('25391793','99999999'));run('changed-game-not-rolled-back',g,expect=1);assert not (g/paths[0]).exists()
# Windows file-sharing failure must preserve the entire pre-operation state.
import ctypes
g=fixture('locked');before=snapshot(g);kernel=ctypes.WinDLL('kernel32',use_last_error=True)
kernel.CreateFileW.argtypes=[ctypes.c_wchar_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p,ctypes.c_uint32,ctypes.c_uint32,ctypes.c_void_p];kernel.CreateFileW.restype=ctypes.c_void_p
kernel.CloseHandle.argtypes=[ctypes.c_void_p]
h=kernel.CreateFileW(str(g/paths[0]),0x80000000,0,None,3,0,None)
assert h not in (None,ctypes.c_void_p(-1).value)
try:run('locked-file-preserved',g,expect=1)
finally:kernel.CloseHandle(h)
assert snapshot(g)==before
g=fixture('shortcuts'); shortcuts=OUT/'shortcuts'
subprocess.run(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(ROOT/'tools/create-test-shortcuts.ps1'),'-Folder',str(shortcuts),'-Game',str(g)],check=True,capture_output=True)
links={p.name:p.read_bytes() for p in shortcuts.glob('*.lnk')}
run('shortcut-interruption',g,expect=77,RECOVERY_CRASH_AFTER='7')
run('shortcut-resume',g)
assert not (shortcuts/'normal.lnk').exists() and not (shortcuts/'vr.lnk').exists()
assert (shortcuts/'unrelated.lnk').read_bytes()==links['unrelated.lnk']
# Synthetic Steam protobuf fixture; proves exact content verification, not actual downloading.
def var(n):
    b=bytearray()
    while n>127:b.append((n&127)|128);n>>=7
    return bytes(b)+bytes([n])
def field(n,b):return var((n<<3)|2)+var(len(b))+b
stock={p:('official '+p).encode() for p in paths[2:6]}
payload=b''
for p,b in stock.items():
    row=field(1,p.encode())+var(2<<3)+var(len(b))+field(5,hashlib.sha1(b).digest());payload+=field(1,row)
depot=OUT/'steam/depotcache/1066891_123.manifest';depot.parent.mkdir(parents=True);depot.write_bytes(struct.pack('<II',0x71F617D0,len(payload))+payload)
g=fixture('verify');run('verify-before-remove',g,mode='--verify',expect=1);run('verify-remove',g);run('verify-not-yet-restored',g,mode='--verify',expect=1)
for p,b in stock.items():(g/p).write_bytes(b)
run('verify-current-original',g,mode='--verify')
(g/paths[2]).write_bytes(b'damaged');run('verify-damaged',g,mode='--verify',expect=1)
depot.unlink();run('verify-no-manifest',g,mode='--verify',expect=1)
report={'status':'PASS','checks':checks,'count':len(checks),'managed_paths':len(paths),'real_game_modified':False,'steam_download_tested':False}
(OUT/'result.json').write_text(json.dumps(report,indent=2));(ROOT/'evidence/test-result.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2));print(OUT)
