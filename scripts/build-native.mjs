import { build } from 'esbuild';
import { readFile, writeFile, mkdir, cp, readdir, rm } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
import {browserSourceDirectories, browserSourceFiles, readParityCorpora} from './native-source.mjs';
const arg = name => { const i = process.argv.indexOf(name); return i < 0 ? null : process.argv[i + 1]; };
const browser = path.resolve(arg('--core') || process.env.LOCALCORRECT_BROWSER || (() => { throw new Error('Pass --core <browser checkout>'); })());
const sourceCore = path.join(browser, 'packages/core');
const core = path.resolve('build/native-core');
const pin = JSON.parse(await readFile('core-source.json', 'utf8'));
const release = process.argv.includes('--release');
if (release && process.argv.includes('--skip-tests')) throw new Error('Release builds must run the native engine, parity and installer tests.');
const {version} = JSON.parse(await readFile('package.json', 'utf8'));
let commit = 'source-archive', status = 'uncommitted source archive';
try {
  commit = execFileSync('git', ['-C', browser, 'rev-parse', 'HEAD'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
  status = execFileSync('git', ['-C', browser, 'status', '--porcelain', '--', ...browserSourceDirectories, ...browserSourceFiles], { encoding: 'utf8' }).trim();
} catch { /* Source distributions can be rebuilt without a Git checkout. */ }
if (release && (commit !== pin.ref || status)) throw new Error('Release requires clean corresponding source, provenance and corpora at the reviewed core-source.json pin.');
// Fail before any build when a required frozen corpus or source record is missing.
const corpora = await readParityCorpora(browser, {release});
for (const file of browserSourceFiles) await readFile(path.join(browser, file));
await rm(core, { recursive: true, force: true });
await cp(sourceCore, core, { recursive: true });
if (JSON.parse(await readFile(path.join(core, 'package.json'), 'utf8')).name !== '@localcorrect/core') throw new Error('Core identity mismatch');
const dotnet = arg('--dotnet') || 'dotnet';
await mkdir('native/assets', { recursive: true });
await build({ entryPoints: [path.join(core, 'src/index.js')], bundle: true, format: 'iife', globalName: 'LocalCorrectCore', target: 'es2022', outfile: 'native/assets/core.js', legalComments: 'eof' });
await cp(path.join(core, 'models/context-encoder.onnx'), 'native/assets/context-encoder.onnx');
for (const language of ['en', 'de']) await cp(path.join(core, 'dictionaries', `${language}.txt`), `native/assets/${language}.txt`);
const tree = createHash('sha256');
async function walk(dir) { const files = []; for (const entry of await readdir(dir, { withFileTypes: true })) { const p = path.join(dir, entry.name); if (entry.isDirectory()) files.push(...await walk(p)); else files.push(p); } return files.sort(); }
async function assertNoVcRedistribution(directory) {
  // The self-contained .NET package licenses its own vcruntime140_cor3.dll.
  for (const file of await walk(directory)) if (/^(?:vc_redist.*\.exe|(?:msvcp|vcruntime|concrt)\d.*\.dll)$/i.test(path.basename(file)) && path.basename(file).toLowerCase() !== 'vcruntime140_cor3.dll') throw new Error('Unexpected redistributed VC runtime file: ' + path.basename(file));
}
for (const file of await walk(core)) { tree.update(path.relative(core, file)); tree.update(await readFile(file)); }
const corpusSha256 = Object.fromEntries(corpora.map(corpus => [corpus.relativePath, corpus.sha256]));
const corpusCases = Object.fromEntries(corpora.map(corpus => [corpus.relativePath, {documents: corpus.rows.length, modes: corpus.modes}]));
const emailCorpusSha256 = Object.fromEntries(corpora.filter(corpus => corpus.relativePath.startsWith('tests/email-corpus/')).map(corpus => [path.basename(corpus.relativePath, '.json'), corpus.sha256]));
const scaleCorpusSha256 = Object.fromEntries(corpora.filter(corpus => corpus.relativePath.startsWith('tests/scale-corpus/')).map(corpus => [corpus.relativePath.slice('tests/scale-corpus/'.length), corpus.sha256]));
await writeFile('native/assets/build-info.json', JSON.stringify({ coreCommit: commit, coreDirty: !!status, emailCorpusSha256, scaleCorpusSha256, corpusSha256, corpusCases, coreTreeSha256: tree.digest('hex'), version, runtime: 'Jint 4.17.0 / ONNX Runtime 1.31.1 / isolated .NET 10 win-x64 helper; .NET Framework 4.8 COM proxy', release }, null, 2));
await rm('native/AddIn/bin/Release/net48', {recursive:true, force:true});
execFileSync(dotnet, ['build', 'native/AddIn/LocalCorrect.Outlook.csproj', '-c', 'Release', ...(process.argv.includes('--locked') ? ['-p:RestoreLockedMode=true'] : [])], { stdio: 'inherit' });
// Verify startup logging before the longer corpus pass. Session logging alone
// does not cover ORT environment/device discovery.
if (!process.argv.includes('--skip-tests')) {
  const project = 'native/OrtPrivacy.Tests/LocalCorrect.OrtPrivacy.Tests.csproj';
  execFileSync(dotnet, ['restore', project, ...(process.argv.includes('--locked') ? ['--locked-mode'] : [])], {stdio:'inherit'});
  execFileSync(dotnet, ['run', '--project', project, '-c', 'Release', '--no-restore'], {stdio:'inherit'});
}
// Generate exact expected edits in the JavaScript implementation, then execute the same inputs in Jint.
const {createContextReference} = await import(pathToFileURL(path.join(browser, 'scripts/context-reference.mjs')).href);
const reference = await createContextReference();
const cases = [
 ['en','please teh mesage'], ['en','recieve seperate'], ['en','Thsi works well.'],
 ['de','Bitte senden Sie die Rehcnung.'], ['de','Kanst du später anrufen?'], ['de','die Eamil'],
 ['en','📧 please mesage this'], ['en','From: someone\nrecieve mesage'],
 ['en','```js\nrecieve mesage\n```'], ['en','https://example.com/mesage user@mesage.com'],
 ['de','Bitte merge den Commit und push den Branch.']
];
const fixtures = [];
for (const [language,text] of cases) for (const mode of ['conservative','balanced']) fixtures.push({language,text,mode,expected:await reference.correct(text,{language,mode})});
// Compare complete EN/DE email outputs and original edit spans across JS/Jint.
// These expected values prove runtime parity, not perfect spelling accuracy.
// Gold correction quality is reported separately by the browser evaluator.
for (const corpus of corpora) {
  for (const row of corpus.rows) for (const mode of corpus.modes) {
    fixtures.push({language: row.language, text: row.input, mode, expected: await reference.correct(row.input, {language: row.language, mode})});
  }
}
await reference.close();
const infoPath = 'native/assets/build-info.json';
const info = JSON.parse(await readFile(infoPath, 'utf8'));
const modelBytes = await readFile('native/assets/context-encoder.onnx');
info.contextModel = {sha256:createHash('sha256').update(modelBytes).digest('hex'), bytes:modelBytes.length};
info.parityCases = fixtures.length;
await writeFile(infoPath, JSON.stringify(info, null, 2));
await writeFile('native/assets/parity.json', JSON.stringify(fixtures));
if (!process.argv.includes('--skip-tests')) {
  execFileSync(dotnet, ['restore', 'native/Engine.Tests/LocalCorrect.Engine.Tests.csproj', ...(process.argv.includes('--locked') ? ['--locked-mode'] : [])], { stdio: 'inherit' });
  execFileSync(dotnet, ['run', '--project', 'native/Engine.Tests/LocalCorrect.Engine.Tests.csproj', '-f', 'net10.0', '-c', 'Release', '--no-restore'], { stdio: 'inherit' });

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
const helper = path.resolve('build/native-helper-win-x64');
await rm(helper, {recursive:true, force:true});
execFileSync(dotnet, ['publish', 'native/Helper/LocalCorrect.Helper.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', helper, ...(process.argv.includes('--locked') ? ['-p:RestoreLockedMode=true'] : [])], {stdio:'inherit'});
info.helperRuntime = JSON.parse(await readFile(path.join(helper, 'LocalCorrect.Helper.runtimeconfig.json'), 'utf8')).runtimeOptions.includedFrameworks;
const ortNativeSha256 = createHash('sha256').update(await readFile(path.join(helper, 'onnxruntime.dll'))).digest('hex');
if (ortNativeSha256 !== 'cb2fa518b18356617f6f9f460c012501da2819ec7e5c1d80bd8f5de20492db0c') throw new Error('Review the ONNX Runtime Windows backend and VC prerequisite before changing its pinned binary.');
info.vcRuntime = {deployment:'system prerequisite; not redistributed', architecture:'x64', minimumToolset:'14.44', onnxRuntimeSha256:ortNativeSha256, officialDownload:'https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist'};
await assertNoVcRedistribution(helper);
await writeFile(infoPath, JSON.stringify(info, null, 2));
if (!process.argv.includes('--skip-tests')) {
  execFileSync(dotnet, ['build', 'native/Helper/LocalCorrect.Helper.csproj', '-c', 'Release', '--no-self-contained', ...(process.argv.includes('--locked') ? ['-p:RestoreLockedMode=true'] : [])], {stdio:'inherit'});
  execFileSync(dotnet, ['restore', 'native/Protocol.Tests/LocalCorrect.Protocol.Tests.csproj', ...(process.argv.includes('--locked') ? ['--locked-mode'] : [])], {stdio:'inherit'});
  const hostHelper = process.platform === 'win32' ? path.join(helper, 'LocalCorrect.Helper.exe') : path.resolve('native/Helper/bin/Release/net10.0/LocalCorrect.Helper');
  execFileSync(dotnet, ['run', '--project', 'native/Protocol.Tests/LocalCorrect.Protocol.Tests.csproj', '-c', 'Release', '--no-restore', '--', hostHelper, path.resolve('native/assets/parity.json')], {stdio:'inherit', env:{...process.env, ORT_DISABLE_TELEMETRY:'1', ...(dotnet.includes(path.sep) ? {DOTNET_ROOT:path.dirname(path.resolve(dotnet))} : {})}});
  if (process.platform === 'win32') for (const bitness of ['x86','x64']) {
    const output = path.resolve('build/native-client-tests/' + bitness) + path.sep;
    await rm(output, {recursive:true, force:true});
    execFileSync(dotnet, ['build', 'native/Engine.Tests/LocalCorrect.Engine.Tests.csproj', '-f', 'net48', '-c', 'Release', '-p:PlatformTarget=' + bitness, '-p:RuntimeIdentifier=', '-t:Rebuild', '-p:OutputPath=' + output, '--no-restore'], {stdio:'inherit'});
    await cp(helper, path.join(output, 'native-helper'), {recursive:true});
    execFileSync(path.join(output, 'LocalCorrect.Engine.Tests.exe'), ['--expected-bitness', bitness], {stdio:'inherit'});
  }
}
await rm('build/native', { recursive: true, force: true }); await mkdir('build/native/payload', { recursive: true });
await cp(helper, 'build/native/payload/native-helper', {recursive:true});
await cp('native/AddIn/bin/Release/net48', 'build/native/payload', { recursive: true, filter: p => !p.endsWith('.pdb') });
for (const file of ['LICENSE', 'THIRD_PARTY.md', 'PRIVACY.md', 'SECURITY.md', 'native/README.md', 'LEGACY-WEB.md', 'native/assets/build-info.json']) await cp(file, `build/native/payload/${path.basename(file)}`);
for (const directory of browserSourceDirectories) await cp(directory === 'packages/core' ? core : path.join(browser, directory), 'build/native/payload/source/browser/' + directory, {recursive: true, filter:p=>!path.relative(directory === 'packages/core' ? core : path.join(browser,directory),p).split(path.sep).some(s=>['__pycache__','node_modules','work'].includes(s))&&!p.endsWith('.pyc')});
for (const name of browserSourceFiles) await cp(path.join(browser, name), 'build/native/payload/source/browser/' + name);
await cp('native/licenses', 'build/native/payload/licenses', { recursive: true });
await cp('native', 'build/native/payload/source/outlook/native', { recursive: true, filter: p => !p.split(path.sep).some(s => ['bin', 'obj', 'assets'].includes(s)) });
await mkdir('build/native/payload/source/outlook/scripts', { recursive: true });
for (const file of ['scripts/build-native.mjs', 'scripts/native-source.mjs', 'scripts/package-native.py', 'scripts/native-helper-notices.py', 'core-source.json', 'package.json', 'package-lock.json', 'global.json', 'LICENSE', 'THIRD_PARTY.md', 'PRIVACY.md', 'SECURITY.md', 'README.md', 'LEGACY-WEB.md', 'PROJECT-PREFERENCES.md']) await cp(file, 'build/native/payload/source/outlook/' + file);
execFileSync(arg('--python') || (process.platform === 'win32' ? 'python' : 'python3'), ['scripts/native-helper-notices.py'], { stdio:'inherit' });
await assertNoVcRedistribution('build/native/payload');
// Include the exact runtime NuGet packages, their nuspec license declarations and shipped notices.
execFileSync(arg('--python') || (process.platform === 'win32' ? 'python' : 'python3'), ['scripts/package-native.py', '--payload'], { stdio: 'inherit' });
execFileSync(dotnet, ['build', 'native/Installer/LocalCorrect.Setup.csproj', '-c', 'Release', ...(process.argv.includes('--locked') ? ['-p:RestoreLockedMode=true'] : [])], { stdio: 'inherit' });
await mkdir('dist', { recursive: true });
await cp('native/Installer/bin/Release/net48/LocalCorrect-Outlook-Setup.exe', 'dist/LocalCorrect-Outlook-Setup.exe');
const bytes = await readFile('dist/LocalCorrect-Outlook-Setup.exe');
if (bytes[0] !== 0x4d || bytes[1] !== 0x5a) throw new Error('Installer is not a PE executable');
await writeFile('dist/LocalCorrect-Outlook-Setup.exe.sha256', createHash('sha256').update(bytes).digest('hex') + '  LocalCorrect-Outlook-Setup.exe\n');
console.log('Built Windows PE installer. Windows/Outlook acceptance remains a separate required check.');
