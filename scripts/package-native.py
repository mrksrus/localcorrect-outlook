"""Package the exact native build with deterministic ZIP metadata and dependency notices."""
import json
import os
from pathlib import Path
import shutil
from zipfile import ZipFile, ZIP_DEFLATED

root = Path(__file__).resolve().parents[1]
payload = root / 'build/native/payload'
assets = json.loads((root / 'native/Engine/obj/project.assets.json').read_text())
cache = Path(next(iter(assets['packageFolders'])))
for name, library in sorted(assets['libraries'].items()):
    if library.get('type') != 'package' or name.startswith('Microsoft.NETFramework.ReferenceAssemblies'):
        continue
    package = cache / library['path']
    target = payload / 'third-party-packages' / library['path']
    target.mkdir(parents=True, exist_ok=True)
    for file in package.iterdir():
        if file.name.endswith(('.nupkg', '.nuspec')) or 'license' in file.name.lower() or 'notice' in file.name.lower():
            if file.is_file():
                shutil.copyfile(file, target / file.name)
with ZipFile(root / 'native/assets/payload.zip', 'w', ZIP_DEFLATED, compresslevel=9) as archive:
    for file in sorted(payload.rglob('*')):
        if file.is_file():
            info = __import__('zipfile').ZipInfo(file.relative_to(payload).as_posix(), (2020, 1, 1, 0, 0, 0))
            info.compress_type = ZIP_DEFLATED
            archive.writestr(info, file.read_bytes())
