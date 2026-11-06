"""Copy the exact self-contained .NET runtime license and package provenance."""
import json
from pathlib import Path
import shutil
import hashlib
root = Path(__file__).resolve().parents[1]
payload = root / 'build/native/payload'
config = json.loads((payload / 'native-helper/LocalCorrect.Helper.runtimeconfig.json').read_text())
frameworks = config['runtimeOptions']['includedFrameworks']
version = next(item['version'] for item in frameworks if item['name'] == 'Microsoft.NETCore.App')
assets = json.loads((root / 'native/Helper/obj/project.assets.json').read_text())
cache = Path(next(iter(assets['packageFolders'])))
package = cache / 'microsoft.netcore.app.runtime.win-x64' / version
files = []
for file in sorted(package.iterdir()):
    if file.name.endswith(('.nuspec', '.nupkg')) or 'license' in file.name.lower() or 'notice' in file.name.lower():
        if file.is_file():
            target = payload / 'third-party-packages/microsoft.netcore.app.runtime.win-x64' / version / file.name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(file, target)
            files.append({'file': file.name, 'sha256': hashlib.sha256(file.read_bytes()).hexdigest()})
if not any('license' in entry['file'].lower() for entry in files) or not any('notice' in entry['file'].lower() for entry in files):
    raise SystemExit('Missing bundled .NET runtime license or notices.')
(payload / 'native-helper/dotnet-runtime-source.json').write_text(json.dumps({'package': 'Microsoft.NETCore.App.Runtime.win-x64', 'version': version, 'files': files}, indent=2) + '\n')
