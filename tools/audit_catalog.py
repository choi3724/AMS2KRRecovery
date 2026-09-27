"""Compare reviewed removal paths with historical package manifests (read only)."""
from pathlib import Path
import sys,csv,io,json,zipfile
ROOT=Path(__file__).resolve().parents[1]
paths=set((ROOT/'data/managed-paths.txt').read_text().splitlines())
results=[];missing=set()
def check(label,data):
    rows=list(csv.DictReader(io.StringIO(data.decode('utf-8-sig')),delimiter='\t'))
    if not rows or 'relative_path' not in rows[0]:return
    names={r['relative_path'].replace('\\','/').lower() for r in rows}
    absent=names-paths;missing.update(absent);results.append({'source':label,'count':len(names),'missing':sorted(absent)})
for arg in sys.argv[1:]:
    base=Path(arg)
    for p in base.rglob('direct-files.tsv'):check(str(p.relative_to(base)),p.read_bytes())
    for p in base.rglob('*.zip'):
        with zipfile.ZipFile(p) as z:
            for n in z.namelist():
                if n.lower().endswith('/direct-files.tsv'):check(str(p.relative_to(base))+'!'+n,z.read(n))
report={'status':'FAIL' if missing else 'PASS','managed_paths':len(paths),'manifests':len(results),'results':results}
(ROOT/'evidence/catalog-audit.json').write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding='utf-8')
print(report['status'],len(results),'manifests;',len(missing),'missing',sorted(missing))
if missing:sys.exit(1)
