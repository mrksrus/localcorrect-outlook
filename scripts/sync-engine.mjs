// Copies the word lists from a localcorrect-browser checkout and records the browser engine's
// output for every case in native/Engine.Tests/parity.tsv. The C# port must match it exactly.
//   node scripts/sync-engine.mjs ../localcorrect-browser
import {readFileSync, writeFileSync, copyFileSync} from 'node:fs';
import path from 'node:path';
import {pathToFileURL} from 'node:url';

const browser = path.resolve(process.argv[2] || '../browser');
const {createCorrector} = await import(pathToFileURL(path.join(browser, 'extension/corrector.js')).href);
for (const code of ['en', 'de']) copyFileSync(path.join(browser, `extension/data/${code}.lex`), `native/Engine/data/${code}.lex`);
const c = createCorrector({en: readFileSync('native/Engine/data/en.lex', 'utf8'), de: readFileSync('native/Engine/data/de.lex', 'utf8')});

const SENTENCES = [
  'I received the email yesterday and will answer it tomorrow.',
  'This is a really important message about our project meeting.',
  'Can you please send me the document before Friday?',
  "I don't think we can make it, I'm sorry.",
  'We should definitely meet next week to discuss the separate budget.',
  'The government announced a committee for the recommendation.',
  'Unfortunately the schedule was awkward, but the team handled it well.',
  'Please review the attached invoice and let me know if anything is missing.',
  'Thanks for your quick reply, Sam. See https://example.com/page for details.',
  'Run npm install --save-dev and check src/utils/helper.js, then call getUser(id).',
  'Version 2.0.1 ships on 15.11.2026 with ID abc123, said Dr. Miller.',
  '> I received the quoted line\nThanks a lot!',
  'Ich habe die Email gestern bekommen und werde morgen antworten.',
  'Könnten Sie mir bitte das Dokument schicken?',
  'Vielen Dank für Ihre schnelle Antwort.',
  'Ich würde gerne über das Thema reden, das Haus ist groß.',
  'Wir haben das Problem leider noch nicht gelöst, aber wir arbeiten daran.',
  'Die Projektbesprechung ist am Montag, Grüße an Herrn Schmidtke.',
  'Hallo Sam, wie gehts? Ich freue mich auf die Kundenanfrage zur Rechnungsadresse.',
  'Bitte bestätigen Sie den Empfang der Unterlagen bis Mittwoch.',
  'Das Deployment im Büro ist fertig, das Meeting war gut.',
  'Sehr geehrte Damen und Herren, anbei finden Sie die Rechnung.',
  'Mit freundlichen Grüßen\nMarkus',
  'NASA and the EU use HTML, JSON and OAuth.',
  'Mail teh.user@example.com or ping @dev about #release.',
];
// Deterministic typo generator: delete, swap, double, substitute or drop an umlaut.
let seed = 12345;
const random = n => { seed = (seed * 1103515245 + 12345) % 2147483648; return seed % n; };
function typo(word) {
  const i = random(word.length - 1), kind = random(5);
  if (kind === 0) return word.slice(0, i) + word.slice(i + 1);
  if (kind === 1) return word.slice(0, i) + word[i + 1] + word[i] + word.slice(i + 2);
  if (kind === 2) return word.slice(0, i) + word[i] + word.slice(i);
  if (kind === 3) return word.slice(0, i) + 'abcdefghijklmnopqrstuvwxyz'[random(26)] + word.slice(i + 1);
  return word.replace('ä', 'ae').replace('ö', 'o').replace('ü', 'ue').replace('ß', 'ss');
}
const cases = [];
for (const sentence of SENTENCES) {
  cases.push(sentence);
  for (let round = 0; round < 12; round++)
    cases.push(sentence.replace(/\p{L}{4,}/gu, word => random(3) === 0 ? typo(word) : word));
}
const b64 = s => Buffer.from(s, 'utf8').toString('base64');
const lines = [];
for (const text of cases) for (const language of ['auto', 'en', 'de']) {
  const r = c.correct(text, {language});
  lines.push([language, b64(text), r.language, b64(r.text), r.edits.map(e => `${e.offset}:${e.length}:${b64(e.replacement)}`).join(',')].join('\t'));
}
writeFileSync('native/Engine.Tests/parity.tsv', lines.join('\n') + '\n');
console.log(`${lines.length} parity cases`);
