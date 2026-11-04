import { build } from 'esbuild';
import { readFile, writeFile, mkdir, cp, readdir, rm } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
const arg = name => { const i = process.argv.indexOf(name); return i < 0 ? null : process.argv[i + 1]; };
const browser = path.resolve(arg('--core') || process.env.LOCALCORRECT_BROWSER || (() => { throw new Error('Pass --core <browser checkout>'); })());
const sourceCore = path.join(browser, 'packages/core');
const core = path.resolve('build/native-core');
const pin = JSON.parse(await readFile('core-source.json', 'utf8'));
let commit = 'source-archive', status = 'uncommitted source archive';
try {
  commit = execFileSync('git', ['-C', browser, 'rev-parse', 'HEAD'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
  status = execFileSync('git', ['-C', browser, 'status', '--porcelain', '--', 'packages/core'], { encoding: 'utf8' }).trim();
} catch { /* Source distributions can be rebuilt without a Git checkout. */ }
if (process.argv.includes('--release') && (commit !== pin.ref || status)) throw new Error('Release requires clean core at the reviewed core-source.json pin.');
await rm(core, { recursive: true, force: true });
await cp(sourceCore, core, { recursive: true });
if (JSON.parse(await readFile(path.join(core, 'package.json'), 'utf8')).name !== '@localcorrect/core') throw new Error('Core identity mismatch');
const dotnet = arg('--dotnet') || 'dotnet';
await mkdir('native/assets', { recursive: true });
await build({ entryPoints: [path.join(core, 'src/index.js')], bundle: true, format: 'iife', globalName: 'LocalCorrectCore', target: 'es2022', outfile: 'native/assets/core.js', legalComments: 'eof' });
for (const language of ['en', 'de']) await cp(path.join(core, 'dictionaries', `${language}.txt`), `native/assets/${language}.txt`);
const tree = createHash('sha256');
async function walk(dir) { const files = []; for (const entry of await readdir(dir, { withFileTypes: true })) { const p = path.join(dir, entry.name); if (entry.isDirectory()) files.push(...await walk(p)); else files.push(p); } return files.sort(); }
for (const file of await walk(core)) { tree.update(path.relative(core, file)); tree.update(await readFile(file)); }
await writeFile('native/assets/build-info.json', JSON.stringify({ coreCommit: commit, coreDirty: !!status, coreTreeSha256: tree.digest('hex'), version: '1.0.0-rc.1', runtime: 'Jint 4.17.0 / .NET Framework 4.8', release: process.argv.includes('--release') }, null, 2));
await rm('native/AddIn/bin/Release/net48', {recursive:true, force:true});
execFileSync(dotnet, ['build', 'native/AddIn/LocalCorrect.Outlook.csproj', '-c', 'Release', ...(process.argv.includes('--locked') ? ['-p:RestoreLockedMode=true'] : [])], { stdio: 'inherit' });
// Generate exact expected edits in the JavaScript implementation, then execute the same inputs in Jint.
const {createCorrector} = await import(pathToFileURL(path.join(core, 'src/index.js')).href);
const reference = createCorrector({en: await readFile('native/assets/en.txt', 'utf8'), de: await readFile('native/assets/de.txt', 'utf8')});
const cases = [
 ['en','please teh mesage'], ['en','recieve seperate'], ['en','Thsi works well.'],
 ['de','Bitte senden Sie die Rehcnung.'], ['de','Kanst du später anrufen?'], ['de','die Eamil'],
 ['en','📧 please mesage this'], ['en','From: someone\nrecieve mesage'],
 ['en','```js\nrecieve mesage\n```'], ['en','https://example.com/mesage user@mesage.com'],
 ['de','Bitte merge den Commit und push den Branch.']
];
const fixtures = cases.flatMap(([language,text]) => ['conservative','balanced'].map(mode => ({language,text,mode,expected:reference.correct(text,{language,mode})})));
await writeFile('native/assets/parity.json', JSON.stringify(fixtures));
if (!process.argv.includes('--skip-tests')) {
  execFileSync(dotnet, ['restore', 'native/Engine.Tests/LocalCorrect.Engine.Tests.csproj', ...(process.argv.includes('--locked') ? ['--locked-mode'] : [])], { stdio: 'inherit' });
  execFileSync(dotnet, ['run', '--project', 'native/Engine.Tests/LocalCorrect.Engine.Tests.csproj', '-f', 'net10.0', '-c', 'Release', '--no-restore'], { stdio: 'inherit' });
  if (process.platform === 'win32') {
    execFileSync(dotnet, ['build', 'native/Engine.Tests/LocalCorrect.Engine.Tests.csproj', '-f', 'net48', '-c', 'Release', '--no-restore'], { stdio: 'inherit' });
    execFileSync(path.resolve('native/Engine.Tests/bin/Release/net48/LocalCorrect.Engine.Tests.exe'), [], { stdio: 'inherit' });
  }
}
if (!process.argv.includes('--skip-tests')) {
  const project = 'native/Installer.Tests/LocalCorrect.Installer.Tests.csproj';
  execFileSync(dotnet, ['restore', project, ...(process.argv.includes('--locked') ? ['--locked-mode'] : [])], { stdio: 'inherit' });
  execFileSync(dotnet, ['run', '--project', project, '-f', 'net10.0', '-c', 'Release', '--no-restore'], { stdio: 'inherit' });
  if (process.platform === 'win32') {
    execFileSync(dotnet, ['build', project, '-f', 'net48', '-c', 'Release', '--no-restore'], { stdio: 'inherit' });
    execFileSync(path.resolve('native/Installer.Tests/bin/Release/net48/LocalCorrect.Installer.Tests.exe'), [], { stdio: 'inherit' });
  }
}
await rm('build/native', { recursive: true, force: true }); await mkdir('build/native/payload', { recursive: true });
await cp('native/AddIn/bin/Release/net48', 'build/native/payload', { recursive: true, filter: p => !p.endsWith('.pdb') });
for (const file of ['LICENSE', 'THIRD_PARTY.md', 'PRIVACY.md', 'SECURITY.md', 'native/README.md', 'native/assets/build-info.json']) await cp(file, `build/native/payload/${path.basename(file)}`);
await cp(core, 'build/native/payload/source/browser/packages/core', { recursive: true });
await cp('native/licenses', 'build/native/payload/licenses', { recursive: true });
await cp('native', 'build/native/payload/source/outlook/native', { recursive: true, filter: p => !p.split(path.sep).some(s => ['bin', 'obj', 'assets'].includes(s)) });
await mkdir('build/native/payload/source/outlook/scripts', { recursive: true });
for (const file of ['scripts/build-native.mjs', 'scripts/package-native.py', 'core-source.json', 'package.json', 'package-lock.json', 'LICENSE', 'THIRD_PARTY.md', 'PRIVACY.md', 'SECURITY.md']) await cp(file, 'build/native/payload/source/outlook/' + file);
// Include the exact runtime NuGet packages, their nuspec license declarations and shipped notices.
execFileSync(arg('--python') || (process.platform === 'win32' ? 'python' : 'python3'), ['scripts/package-native.py', '--payload'], { stdio: 'inherit' });
execFileSync(dotnet, ['build', 'native/Installer/LocalCorrect.Setup.csproj', '-c', 'Release', ...(process.argv.includes('--locked') ? ['-p:RestoreLockedMode=true'] : [])], { stdio: 'inherit' });
await mkdir('dist', { recursive: true });
await cp('native/Installer/bin/Release/net48/LocalCorrect-Outlook-Setup.exe', 'dist/LocalCorrect-Outlook-Setup.exe');
const bytes = await readFile('dist/LocalCorrect-Outlook-Setup.exe');
if (bytes[0] !== 0x4d || bytes[1] !== 0x5a) throw new Error('Installer is not a PE executable');
await writeFile('dist/LocalCorrect-Outlook-Setup.exe.sha256', createHash('sha256').update(bytes).digest('hex') + '  LocalCorrect-Outlook-Setup.exe\n');
console.log('Built Windows PE installer. Windows/Outlook acceptance remains a separate required check.');
