import assert from 'node:assert/strict';
import { readFile, readdir } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { parseHTML } from 'linkedom';
const manifest = await readFile('build/manifest.xml', 'utf8');
assert.ok(!manifest.includes('__BASE_URL__'));
assert.match(manifest, /<Permissions>ReadItem<\/Permissions>/);
assert.ok(!manifest.includes('ReadWriteMailbox'));
execFileSync('python', ['-c', 'import xml.etree.ElementTree as E; E.parse("build/manifest.xml")'], { stdio: 'inherit' });
const info = JSON.parse(await readFile('build/build-info.json', 'utf8'));
assert.equal(info.version, '1.0.0-rc.1');
assert.match(info.coreTreeSha256, /^[a-f0-9]{64}$/);
const html = await readFile('build/site/taskpane.html', 'utf8');
const { document } = parseHTML(html);
for (const id of ['correct', 'copy', 'clear', 'output', 'language', 'mode', 'summary']) assert.ok(document.getElementById(id));
assert.ok(document.getElementById('output').hasAttribute('readonly'));
assert.equal(document.getElementById('undo'), null);
const csp = document.querySelector('meta[http-equiv="Content-Security-Policy"]').content;
assert.ok(csp.includes("default-src 'none'"));
assert.ok(!csp.includes('unsafe-inline') && !csp.includes('unsafe-eval'));
assert.deepEqual([...document.querySelectorAll('script[src]')].map(s => s.src), ['https://appsforoffice.microsoft.com/lib/1/hosted/office.js', './taskpane.js']);
const bundle = await readFile('build/site/taskpane.js', 'utf8');
for (const bad of ['setSelectedDataAsync', 'setAsync', 'sendAsync(', 'saveAsync(', 'localStorage.', 'indexedDB.', 'https://api.']) assert.ok(!bundle.includes(bad), bad);
for (const language of ['en', 'de']) assert.ok((await readFile(`build/site/dictionaries/${language}.txt`, 'utf8')).length > 100000);
assert.equal((await readdir('build/site/assets')).filter(x => x.endsWith('.png')).length, 5);
execFileSync('python', ['-c', `import zipfile,pathlib,hashlib,json
p=pathlib.Path('dist/localcorrect-outlook-1.0.0-rc.1.zip')
with zipfile.ZipFile(p) as z:
 assert z.testzip() is None
 names=z.namelist()
 for n in ['manifest.xml','site/taskpane.js','README.md','LICENSE','PRIVACY.md','SECURITY.md','core-source/src/index.js','core-source/dictionaries/de.txt','shared-notices/dictionary-sources.json','shared-notices/licenses/CC-BY-SA-4.0.txt','outlook-source/package-lock.json']:
  assert n in names,n
 assert not any(x.startswith('/') or '..' in x.split('/') or 'node_modules/' in x or '/.git/' in x or '__pycache__/' in x for x in names)
 for n in names:
  if n.endswith(('.js','.md','.json','.html','.xml','.yml','.py','.mjs')):
   data=z.read(n)
   assert b'/opt/' + b'data/' not in data,n
 assert hashlib.sha256(p.read_bytes()).hexdigest() in pathlib.Path(str(p)+'.sha256').read_text()
 print(json.dumps({'zipFiles':len(names),'zipBytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()}))`], { stdio: 'inherit' });
console.log('Compiled assets, manifest XML syntax, CSP, privacy boundaries, dictionary/license presence and ZIP validated. Office host/schema validation not claimed.');
