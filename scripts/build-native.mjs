// Builds and tests the Outlook add-in, then packages dist/LocalCorrect-Outlook-Setup.exe and
// dist/localcorrect-outlook-<version>.zip. Usage: node scripts/build-native.mjs [--dotnet path] [--locked]
// Linux runs the net10 tests; Windows also runs them under .NET Framework 4.8 like Outlook.
import {cpSync, mkdirSync, readFileSync, rmSync, writeFileSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';

const arg = name => { const i = process.argv.indexOf(name); return i < 0 ? null : process.argv[i + 1]; };
const dotnet = arg('--dotnet') || 'dotnet';
const locked = process.argv.includes('--locked') ? ['-p:RestoreLockedMode=true'] : [];
const python = process.platform === 'win32' ? 'python' : 'python3';
const {version} = JSON.parse(readFileSync('package.json', 'utf8'));
const run = (...args) => execFileSync(dotnet, args, {stdio: 'inherit'});
const zip = (from, to) => execFileSync(python, ['-c', `import pathlib,sys,zipfile
src=pathlib.Path(sys.argv[1])
with zipfile.ZipFile(sys.argv[2],'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
  for f in sorted(src.rglob('*')):
    if f.is_file():
      i=zipfile.ZipInfo(f.relative_to(src).as_posix(),(2020,1,1,0,0,0)); i.compress_type=zipfile.ZIP_DEFLATED; z.writestr(i,f.read_bytes())`, from, to], {stdio: 'inherit'});

for (const project of ['native/Engine.Tests/LocalCorrect.Engine.Tests.csproj', 'native/Installer.Tests/LocalCorrect.Installer.Tests.csproj']) {
  run('build', project, '-c', 'Release', ...locked);
  run('run', '--project', project, '-f', 'net10.0', '-c', 'Release', '--no-build');
  if (process.platform === 'win32') {
    const exe = project.replace(/[^/]+\.csproj$/, '') + 'bin/Release/net48/' + project.split('/').pop().replace('.csproj', '.exe');
    execFileSync(exe, [], {stdio: 'inherit'});
  }
}

rmSync('build/native', {recursive: true, force: true});
rmSync('native/AddIn/bin/Release/net48', {recursive: true, force: true});
run('build', 'native/AddIn/LocalCorrect.Outlook.csproj', '-c', 'Release', ...locked);
const payload = 'build/native/payload';
mkdirSync(payload, {recursive: true});
cpSync('native/AddIn/bin/Release/net48', payload, {recursive: true, filter: p => !p.endsWith('.pdb')});
for (const file of ['LICENSE', 'THIRD_PARTY.md', 'PRIVACY.md', 'README.md']) cpSync(file, `${payload}/${file}`);
cpSync('licenses', `${payload}/licenses`, {recursive: true});
mkdirSync('native/assets', {recursive: true});
zip(payload, 'native/assets/payload.zip');
run('build', 'native/Installer/LocalCorrect.Setup.csproj', '-c', 'Release', ...locked);

rmSync('dist', {recursive: true, force: true});
mkdirSync('build/native/release', {recursive: true});
cpSync('native/Installer/bin/Release/net48/LocalCorrect-Outlook-Setup.exe', 'build/native/release/LocalCorrect-Outlook-Setup.exe');
const exe = readFileSync('build/native/release/LocalCorrect-Outlook-Setup.exe');
if (exe[0] !== 0x4d || exe[1] !== 0x5a) throw new Error('Installer is not a Windows executable');
for (const file of ['README.md', 'LICENSE']) cpSync(file, `build/native/release/${file}`);
mkdirSync('dist', {recursive: true});
cpSync('build/native/release/LocalCorrect-Outlook-Setup.exe', 'dist/LocalCorrect-Outlook-Setup.exe');
zip('build/native/release', `dist/localcorrect-outlook-${version}.zip`);
const sums = ['LocalCorrect-Outlook-Setup.exe', `localcorrect-outlook-${version}.zip`]
  .map(name => createHash('sha256').update(readFileSync(`dist/${name}`)).digest('hex') + '  ' + name).join('\n') + '\n';
writeFileSync('dist/SHA256SUMS', sums);
console.log(sums + 'Built. Outlook itself is not tested by this script.');
