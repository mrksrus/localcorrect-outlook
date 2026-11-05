"""Deterministic ZIP: fixed dates, modes, sorted entries, no host paths."""
from pathlib import Path
import zipfile
import hashlib
root = Path(__file__).resolve().parent.parent
out = root / 'dist' / 'localcorrect-outlook-1.0.0-rc.1.zip'
entries = [(p, p.relative_to(root / 'build').as_posix()) for p in (root / 'build').rglob('*') if p.is_file()]
for p in root.rglob('*'):
    rel = p.relative_to(root)
    if any(x in {'.git', '.core-checkout', '__pycache__', 'node_modules', 'build', 'dist', 'work', 'test-results', 'bin', 'obj'} for x in rel.parts) or not p.is_file():
        continue
    if rel.parts[:2] == ('native', 'assets') and p.name != '.gitkeep':
        continue
    entries.append((p, 'outlook-source/' + rel.as_posix()))
with zipfile.ZipFile(out, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for p, name in sorted(entries, key=lambda pair: pair[1]):
        info = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        info.external_attr = 0o100644 << 16
        z.writestr(info, p.read_bytes(), compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
sha = hashlib.sha256(out.read_bytes()).hexdigest()
(root / 'dist' / (out.name + '.sha256')).write_text(sha + '  ' + out.name + '\n')
print(out.name + ' SHA256 ' + sha)
