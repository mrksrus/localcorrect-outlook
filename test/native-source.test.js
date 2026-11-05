import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp, mkdir, writeFile, readFile, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {browserSourceDirectories, browserSourceFiles, readParityCorpora, sha256} from '../scripts/native-source.mjs';

const emailPaths = ['tests/email-corpus/dev.json', 'tests/email-corpus/holdout.json'];
const scalePaths = ['tests/scale-corpus/dev.json', 'tests/scale-corpus/holdout.json', 'tests/scale-corpus/blind/cases.json', 'tests/scale-corpus/rc5-blind/cases.json',
  'tests/scale-corpus/rc5-blind2/cases.json', 'tests/scale-corpus/rc5-final-blind/cases.json'];
async function fixture(t) {
  const root = await mkdtemp(path.join(tmpdir(), 'localcorrect-native-source-'));
  t.after(() => rm(root, {recursive: true, force: true}));
  const freeze = {corpusSha256: {}};
  const sums = [];
  async function corpus(relativePath) {
    const bytes = JSON.stringify({cases: [{language: 'en', input: 'Synthetic document.'}]});
    await mkdir(path.dirname(path.join(root, relativePath)), {recursive: true});
    await writeFile(path.join(root, relativePath), bytes);
    freeze.corpusSha256[relativePath] = sha256(bytes);
  }
  for (const relativePath of [...emailPaths, ...scalePaths]) await corpus(relativePath);
  for (const relativePath of emailPaths) sums.push(freeze.corpusSha256[relativePath] + '  ' + path.basename(relativePath));
  await writeFile(path.join(root, 'tests/email-corpus/SHA256SUMS'), sums.join('\n'));
  const save = () => writeFile(path.join(root, 'evaluation-freeze.json'), JSON.stringify(freeze));
  await save();
  return {root, freeze, corpus, save};
}

test('native parity includes both old corpora and newly declared corpora without a count limit', async t => {
  const f = await fixture(t);
  const additional = 'tests/scale-corpus/future-blind/cases.json';
  await f.corpus(additional); await f.save();
  const corpora = await readParityCorpora(f.root, {release: true});
  assert.deepEqual(corpora.map(c => c.relativePath), [...emailPaths, ...scalePaths, additional]);
  for (const corpus of corpora) assert.deepEqual(corpus.modes, emailPaths.includes(corpus.relativePath) ? ['conservative', 'balanced'] : ['conservative']);
});

test('native parity refuses any absent RC5 audit before building', async t => {
  const f = await fixture(t);
  for (const relativePath of scalePaths.slice(-3)) {
    await rm(path.join(f.root, relativePath));
    await assert.rejects(readParityCorpora(f.root), error => error.message === 'Required parity corpus is unavailable: ' + relativePath);
    await f.corpus(relativePath);
  }
});

test('native release requires an RC5 freeze entry and matching corpus bytes', async t => {
  const f = await fixture(t);
  const relativePath = scalePaths.at(-1);
  const digest = f.freeze.corpusSha256[relativePath];
  delete f.freeze.corpusSha256[relativePath]; await f.save();
  await assert.rejects(readParityCorpora(f.root, {release: true}), /Release requires a frozen corpus hash/);
  f.freeze.corpusSha256[relativePath] = digest; await f.save();
  await writeFile(path.join(f.root, relativePath), '{"cases":[]}');
  await assert.rejects(readParityCorpora(f.root, {release: true}), /Frozen corpus hash mismatch/);
});

test('native parity validates email hashes even if omitted from evaluation freeze', async t => {
  const f = await fixture(t);
  delete f.freeze.corpusSha256[emailPaths[0]]; await f.save();
  await writeFile(path.join(f.root, emailPaths[0]), '{"cases":[]}');
  await assert.rejects(readParityCorpora(f.root), /Frozen email corpus hash mismatch/);
});

test('native parity rejects unsafe paths and empty declared datasets', async t => {
  const f = await fixture(t);
  f.freeze.corpusSha256['tests/../../outside.json'] = '0'.repeat(64); await f.save();
  await assert.rejects(readParityCorpora(f.root), /Unsafe declared corpus path/);
  delete f.freeze.corpusSha256['tests/../../outside.json'];
  const relativePath = scalePaths.at(-1);
  const bytes = '{"cases":[]}';
  await writeFile(path.join(f.root, relativePath), bytes);
  f.freeze.corpusSha256[relativePath] = sha256(bytes); await f.save();
  await assert.rejects(readParityCorpora(f.root), /Invalid or empty parity corpus/);
});

test('native source manifest carries complete core, tests and new provenance', () => {
  for (const directory of ['packages/core', 'tests', 'scripts', 'licenses']) assert.ok(browserSourceDirectories.includes(directory));
  for (const file of ['candidate-sources.json', 'protect-sources.json', 'ambiguity-sources.json', 'inflection-sources.json']) assert.ok(browserSourceFiles.includes(file));
});

test('native candidate version agrees across package, assembly and installer metadata', async () => {
  const pkg = JSON.parse(await readFile('package.json', 'utf8'));
  const lock = JSON.parse(await readFile('package-lock.json', 'utf8'));
  const props = await readFile('native/Directory.Build.props', 'utf8');
  const installer = await readFile('native/Installer/Program.cs', 'utf8');
  assert.equal(pkg.version, '1.0.0-rc.4');
  assert.equal(lock.version, pkg.version); assert.equal(lock.packages[''].version, pkg.version);
  assert.ok(props.includes('<Version>' + pkg.version + '</Version>'));
  assert.ok(installer.includes('key.SetValue("DisplayVersion", "' + pkg.version + '")'));
});
