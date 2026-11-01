import { build } from 'esbuild';
import { readFile, writeFile, mkdir, rm, cp, readdir } from 'node:fs/promises';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
process.chdir(root);
const arg = key => { const i = process.argv.indexOf(key); return i < 0 ? null : process.argv[i + 1]; };
const coreArg = arg('--core') || process.env.LOCALCORRECT_BROWSER;
if (!coreArg) throw new Error('Pass --core <browser-checkout> or LOCALCORRECT_BROWSER. No substitute engine is bundled.');
const browser = path.resolve(coreArg);
const core = path.join(browser, 'packages/core');
const pkg = JSON.parse(await readFile(path.join(core, 'package.json'), 'utf8'));
if (pkg.name !== '@localcorrect/core') throw new Error('Core package identity mismatch.');
const release = process.argv.includes('--release');
const pin = JSON.parse(await readFile('core-source.json', 'utf8'));
let commit = 'uncommitted';
try { commit = execFileSync('git', ['-C', browser, 'rev-parse', 'HEAD'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim(); } catch { /* development hash still recorded */ }
if (release) {
  if (!/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(pin.repository) || !/^[a-f0-9]{40}$/.test(pin.ref)) throw new Error('Release requires explicitly reviewed repository/full commit in core-source.json.');
  if (commit !== pin.ref) throw new Error('Core checkout does not match reviewed pin.');
}
if (release || process.argv.includes('--require-clean-core')) {
  const status = execFileSync('git', ['-C', browser, 'status', '--porcelain', '--', 'packages/core'], { encoding: 'utf8' });
  if (status.trim()) throw new Error('Core package must be clean.');
}
const base = new URL(arg('--base-url') || 'https://localhost:3000');
if (base.protocol !== 'https:' || base.username || base.password || base.search || base.hash) throw new Error('Use an HTTPS asset base URL without credentials, query or fragment.');
const baseUrl = base.href.replace(/\/$/, '');
await rm('build', { recursive: true, force: true });
await mkdir('build/site/dictionaries', { recursive: true });
await cp('public', 'build/site', { recursive: true });
await cp(core, 'build/core-source', { recursive: true, filter: src => !src.includes('node_modules') });
const hash = createHash('sha256');
async function walk(dir) { let result = []; for (const e of await readdir(dir, { withFileTypes: true })) { const p = path.join(dir, e.name); result.push(...(e.isDirectory() ? await walk(p) : [p])); } return result.sort(); }
for (const f of await walk('build/core-source')) { hash.update(path.relative('build/core-source', f)); hash.update(await readFile(f)); }
for (const language of ['en', 'de']) await cp(path.join(core, 'dictionaries', `${language}.txt`), `build/site/dictionaries/${language}.txt`);
// Carry all shared source/license/provenance records into the distributable.
await mkdir('build/shared-notices', { recursive: true });
for (const name of await readdir(browser)) if (/^(LICENSE|COPYING|NOTICE|THIRD_PARTY|DICTIONAR)/i.test(name)) await cp(path.join(browser, name), path.join('build/shared-notices', name), { recursive: true });
await cp(path.join(browser, 'scripts/dictionaries.py'), 'build/shared-notices/dictionaries.py');
for (const required of ['LICENSE', 'THIRD_PARTY.md', 'dictionary-sources.json', 'licenses']) await readFile(path.join(browser, required === 'licenses' ? 'licenses/CC-BY-SA-4.0.txt' : required));
// Build tooling is not shipped as runtime code, but include complete notices for
// the locked tools and their installed transitive packages alongside source.
await mkdir('build/tooling-notices', { recursive: true });
for (const f of await walk('node_modules')) {
  if (/^(?:licen[cs]e|copying|notice)(?:[.-].*)?$/i.test(path.basename(f))) {
    const dest = path.join('build/tooling-notices', path.relative('node_modules', f).split(path.sep).map(part => part === 'node_modules' ? 'nested-dependencies' : part).join(path.sep));
    await mkdir(path.dirname(dest), { recursive: true }); await cp(f, dest);
  }
}
const result = await build({ entryPoints: ['src/taskpane.js'], bundle: true, format: 'iife', target: ['es2022'], outfile: 'build/site/taskpane.js', sourcemap: false, legalComments: 'eof', alias: { '@localcorrect/core': path.resolve('build/core-source/src/index.js') }, metafile: true });
if (Object.keys(result.metafile.outputs).length !== 1) throw new Error('Unexpected compiled outputs.');
const html = await readFile('public/taskpane.html', 'utf8');
await writeFile('build/site/demo.html', html.replace('body>', 'body data-demo="true">').replace(/<script src="https:\/\/appsforoffice\.microsoft\.com[^<]+<\/script>/, ''));
await writeFile('build/manifest.xml', (await readFile('manifest.template.xml', 'utf8')).replaceAll('__BASE_URL__', baseUrl));
for (const f of ['README.md', 'PRIVACY.md', 'SECURITY.md', 'THIRD_PARTY.md', 'LICENSE']) await cp(f, `build/${f}`);
await writeFile('build/build-info.json', JSON.stringify({ version: '1.0.0-rc.1', corePackage: pkg.name, release, coreRepository: pin.repository || null, coreCommit: commit, coreTreeSha256: hash.digest('hex'), assetBaseUrl: baseUrl }, null, 2) + '\n');
await mkdir('dist', { recursive: true });
execFileSync('python', ['scripts/package.py'], { stdio: 'inherit' });
console.log('Built Outlook web assets and reproducible RC ZIP; no Outlook installation performed.');
