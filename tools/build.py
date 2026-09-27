from pathlib import Path
import subprocess,hashlib,json
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'build'; OUT.mkdir(exist_ok=True)
compiler=Path('C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe')
common=['/nologo','/platform:anycpu','/main:Ams2KoreanBeta.Program','/optimize+', '/win32icon:'+str(ROOT/'src/ams2-korean.ico')]
common+=['/reference:'+n+'.dll' for n in ['System','System.Core','System.Drawing','System.Windows.Forms','System.IO.Compression','System.IO.Compression.FileSystem','System.Web.Extensions']]
common+=['/resource:'+str(ROOT/'data/managed-paths.txt')+',Recovery.Managed']
common+=[str(p) for p in sorted((ROOT/'src').glob('*.cs'))]
for name,target,manifest,flags in [('AMS2KRRecovery-0.1.exe','winexe','app.manifest',[]),('RecoveryCheck.exe','exe','test.manifest',['/define:STOCK_RESTORE_TEST'])]:
    r=subprocess.run([str(compiler),'/target:'+target,'/out:'+str(OUT/name),'/win32manifest:'+str(ROOT/'src'/manifest),*flags,*common],capture_output=True)
    (OUT/(name+'.log')).write_bytes(r.stdout+r.stderr)
    if r.returncode: raise RuntimeError(r.stdout.decode(errors='replace'))
print('Build PASS',OUT)
