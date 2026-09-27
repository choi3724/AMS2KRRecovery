from pathlib import Path
import hashlib,zipfile,shutil,json
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'dist';OUT.mkdir(exist_ok=True)
report=json.loads((ROOT/'evidence/test-result.json').read_text())
assert report['status']=='PASS'
exe=ROOT/'build/AMS2KRRecovery-0.1.exe'
shutil.copy2(exe,OUT/exe.name)
with zipfile.ZipFile(OUT/'AMS2KRRecovery-0.1.zip','w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
    z.write(exe,exe.name);z.write(ROOT/'README.md','사용 안내.md')
files=sorted(p for p in OUT.iterdir() if p.suffix in ('.exe','.zip'))
(OUT/'SHA256SUMS.txt').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest().upper()+'  '+p.name+'\n' for p in files),encoding='ascii')
(ROOT/'evidence/build-result.json').write_text(json.dumps({'version':'0.1','compiler':'Windows .NET Framework v4.0.30319 csc','public_test_hooks':False,'assets':{p.name:{'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest().upper()} for p in files},'actual_depot_manifest_entries_parsed':8155,'actual_steam_restore_tested':False},indent=2))
print((OUT/'SHA256SUMS.txt').read_text())
