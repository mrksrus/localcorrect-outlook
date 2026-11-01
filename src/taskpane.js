import { createCorrector } from '@localcorrect/core';
import { Controller } from './controller.js';

const $ = id => document.getElementById(id);
let controller, running = false;
const summary = $('summary');
function reset() { controller?.clear(); $('output').value = ''; $('copy').disabled = true; }
async function dictionaries() {
  const load = async language => {
    const response = await fetch(`./dictionaries/${language}.txt`, { credentials: 'omit', cache: 'force-cache', redirect: 'error' });
    if (!response.ok) throw new Error('Could not load local dictionary assets.');
    return response.text();
  };
  const [en, de] = await Promise.all([load('en'), load('de')]);
  return createCorrector({ en, de });
}
$('correct').addEventListener('click', async () => {
  if (running || !controller) return;
  running = true; reset(); $('correct').disabled = true;
  try {
    const result = await controller.correct({ language: $('language').value, mode: $('mode').value });
    $('output').value = result.text;
    $('copy').disabled = !result.text;
    summary.textContent = `${result.count} correction(s); ${result.skipped} skipped. Draft unchanged. Review output, then Copy and paste deliberately into your draft. Plain text only.`;
  } catch { summary.textContent = 'Correction unavailable, draft/selection changed, or unsupported selection. Nothing was written. Select your own unique text in a new HTML message and retry.'; }
  finally { running = false; $('correct').disabled = !controller; }
});
// Explicit user gesture: no attempt after asynchronous Office reads, where browser
// clipboard activation is not reliable. Never copy automatically or read clipboard.
$('copy').addEventListener('click', async () => {
  const text = controller?.getOutput();
  if (text === null || text === undefined || text !== $('output').value) { reset(); summary.textContent = 'Message changed or output cleared. Correct again.'; return; }
  if (!navigator.clipboard?.writeText) { summary.textContent = 'Clipboard unavailable. Select the read-only output and copy manually; paste deliberately.'; return; }
  try { await navigator.clipboard.writeText(text); summary.textContent = 'Copied plain text. Paste deliberately into the intended draft selection. Clipboard history/sync may retain this text.'; }
  catch { summary.textContent = 'Clipboard blocked. Select the read-only output and copy manually; paste deliberately.'; }
});
$('clear').addEventListener('click', () => { reset(); summary.textContent = 'Output cleared. Draft and clipboard unchanged.'; });
for (const id of ['language', 'mode']) $(id).addEventListener('change', reset);
window.addEventListener('pagehide', () => { controller?.close(); reset(); });
// Drop an old item's output when Outlook switches messages. A selected-text
// snapshot is not a live view; later typing/selection is deliberately not applied.
setInterval(() => { if (controller && controller.getOutput() === null && $('output').value) reset(); }, 500);
async function start() {
  try {
    const engine = await dictionaries();
    if (document.body.dataset.demo === 'true') {
      $('demo-editor').hidden = false;
      controller = {
        output: null, generation: 0,
        clear() { this.output = null; this.generation++; }, close() { this.clear(); }, getOutput() { return this.output; },
        async correct(options) { const result = engine.correct($('draft').value, options); this.output = result.text; return { text: result.text, count: result.edits.length, skipped: result.skipped }; }
      };
      summary.textContent = 'Standalone synthetic output-only demo; no Office integration is exercised here.';
    } else {
      if (!globalThis.Office?.onReady) throw new Error('Office.js unavailable.');
      const ready = await Promise.race([Office.onReady(), new Promise((_, reject) => setTimeout(() => reject(new Error('Initialization timed out.')), 15000))]);
      if (ready.host !== Office.HostType.Outlook) throw new Error('Requires Outlook.');
      controller = new Controller({ Office, corrector: engine });
      controller.item();
      summary.textContent = 'Select your own unique text in a NEW HTML message, then Correct. Output only: this add-in never changes the draft.';
    }
    $('correct').disabled = false;
  } catch { summary.textContent = 'Initialization unavailable. Open via Outlook on the web with Mailbox 1.10 and allow the required static assets and Office.js CDN.'; }
}
start();
