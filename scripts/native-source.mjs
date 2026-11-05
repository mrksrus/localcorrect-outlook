import {readFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import path from 'node:path';

// Every directory is copied in full, including the core's generators and vendor notices.
export const browserSourceDirectories = ['packages/core', 'tests', 'scripts', 'extension', 'licenses', 'benchmarks'];
export const browserSourceFiles = [
  'package.json', 'package-lock.json', 'README.md', 'USAGE.md', 'QUALITY.md', 'THIRD_PARTY.md',
  'dictionary-sources.json', 'lexicon-sources.json', 'typo-sources.json',
  'candidate-sources.json', 'protect-sources.json', 'ambiguity-sources.json', 'inflection-sources.json',
  'evaluation-freeze.json', 'EVALUATION.json', 'LICENSE'
];
const emailPaths = ['tests/email-corpus/dev.json', 'tests/email-corpus/holdout.json'];
const requiredScalePaths = [
  'tests/scale-corpus/dev.json', 'tests/scale-corpus/holdout.json',
  'tests/scale-corpus/blind/cases.json', 'tests/scale-corpus/rc5-blind/cases.json',
  'tests/scale-corpus/rc5-blind2/cases.json', 'tests/scale-corpus/rc5-final-blind/cases.json'
];
export const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');

export async function readParityCorpora(browser, {release = false} = {}) {
  const freeze = JSON.parse(await readFile(path.join(browser, 'evaluation-freeze.json'), 'utf8'));
  const declared = freeze.corpusSha256;
  if (!declared || typeof declared !== 'object' || Array.isArray(declared)) throw new Error('Missing evaluation-freeze.corpusSha256');
  const corpusPaths = [...new Set([...emailPaths, ...requiredScalePaths, ...Object.keys(declared)])];
  const sums = await readFile(path.join(browser, 'tests/email-corpus/SHA256SUMS'), 'utf8');
  const corpora = [];
  for (const relativePath of corpusPaths) {
    if (!/^tests\/[A-Za-z0-9_./-]+\.json$/.test(relativePath) || relativePath.split('/').some(part => part === '.' || part === '..' || !part)) {
      throw new Error('Unsafe declared corpus path: ' + relativePath);
    }
    const email = emailPaths.includes(relativePath);
    if (release && !email && !Object.hasOwn(declared, relativePath)) throw new Error('Release requires a frozen corpus hash: ' + relativePath);
    let bytes;
    try { bytes = await readFile(path.join(browser, relativePath)); }
    catch (error) { throw new Error('Required parity corpus is unavailable: ' + relativePath, {cause: error}); }
    const digest = sha256(bytes);
    if (Object.hasOwn(declared, relativePath) && (!/^[a-f0-9]{64}$/.test(declared[relativePath]) || declared[relativePath] !== digest)) {
      throw new Error('Frozen corpus hash mismatch: ' + relativePath);
    }
    if (email) {
      const entry = sums.split(/\r?\n/).find(line => line.endsWith('  ' + path.basename(relativePath)));
      if (!entry || entry.split(/\s+/)[0] !== digest) throw new Error('Frozen email corpus hash mismatch: ' + relativePath);
    }
    const document = JSON.parse(bytes.toString('utf8'));
    const rows = Array.isArray(document) ? document : document.cases;
    if (!Array.isArray(rows) || !rows.length || rows.some(row => !row || !['en', 'de'].includes(row.language) || typeof row.input !== 'string')) {
      throw new Error('Invalid or empty parity corpus: ' + relativePath);
    }
    corpora.push({relativePath, sha256: digest, rows, modes: email ? ['conservative', 'balanced'] : ['conservative']});
  }
  return corpora;
}
