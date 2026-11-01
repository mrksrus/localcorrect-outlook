import { createCorrector } from '@localcorrect/core';
import { Controller } from './controller.js';

const $ = id => document.getElementById(id);
let controller;
const summary = $('summary');
async function dictionaries() {
  const load = async language => {
    const response = await fetch(`./dictionaries/${language}.txt`, { credentials: 'omit', cache: 'force-cache', redirect: 'error' });
    if (!response.ok) throw new Error('Could not load local dictionary assets.');
    return response.text();
  };
  const [en, de] = await Promise.all([load('en'), load('de')]);
  return createCorrector({ en, de });
}
async function act(action) {
  $('correct').disabled = true; $('undo').disabled = true;
  try {
    const result = await action();
    summary.textContent = result.undone ? 'Undone. Only the reselected phrase was restored.' : `${result.count} correction(s); ${result.skipped} skipped. To Undo, reselect the corrected phrase without editing the draft.`;
  } catch (error) { summary.textContent = error.message || 'Operation failed.'; }
  finally { $('correct').disabled = !controller || controller.blocked; $('undo').disabled = !controller?.history || controller.blocked; }
}
$('correct').addEventListener('click', () => act(() => controller.correct({ language: $('language').value, mode: $('mode').value })));
$('undo').addEventListener('click', () => act(() => controller.undo()));

async function start() {
  try {
    const engine = await dictionaries();
    if (document.body.dataset.demo === 'true') {
      $('demo-editor').hidden = false;
      const editor = $('draft');
      let saved = null;
      controller = {
        history: null,
        async correct(options) {
          const before = editor.value;
          const result = engine.correct(before, options);
          saved = { before, after: result.text };
          editor.value = result.text;
          this.history = result.edits.length ? saved : null;
          return { count: result.edits.length, skipped: result.skipped };
        },
        async undo() {
          if (!saved || editor.value !== saved.after) { this.history = null; throw new Error('Demo text changed; Undo refused.'); }
          editor.value = saved.before; saved = null; this.history = null;
          return { undone: true };
        }
      };
      summary.textContent = 'Standalone synthetic demo; no Office integration is exercised here.';
    } else {
      if (!globalThis.Office?.onReady) throw new Error('Office.js is unavailable. Open via Outlook, and allow the required Office.js CDN.');
      const ready = await Promise.race([Office.onReady(), new Promise((_, reject) => setTimeout(() => reject(new Error('Outlook initialization timed out.')), 15000))]);
      if (ready.host !== Office.HostType.Outlook) throw new Error('This add-in requires Outlook.');
      controller = new Controller({ Office, corrector: engine });
      controller.item();
      summary.textContent = 'Select your own uniquely identifiable text in a NEW HTML message, then Correct. Do not type or move the selection while working.';
    }
    $('correct').disabled = false;
  } catch (error) { summary.textContent = error.message || 'Initialization failed.'; }
}
start();
