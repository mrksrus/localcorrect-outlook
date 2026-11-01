import { fragment, assertSelectionSafe, correctFragment } from './html.js';

export function officeCall(invoke, timeoutMs = 8000) {
  return new Promise((resolve, reject) => {
    let settled = false;
    const timer = setTimeout(() => { settled = true; reject(new Error('Outlook did not respond. Nothing was written; select again and retry.')); }, timeoutMs);
    try {
      invoke(result => {
        if (settled) return;
        settled = true; clearTimeout(timer);
        if (result?.status !== 'succeeded') reject(new Error('Outlook API operation failed. No automatic retry was attempted.'));
        else resolve(result.value);
      });
    } catch {
      if (!settled) { settled = true; clearTimeout(timer); reject(new Error('Outlook API unavailable or rejected the operation.')); }
    }
  });
}
// This controller has NO draft setter or unsafe insertion mode. Office selection
// reads cannot be made atomic with a write. Output is a snapshot for manual paste.
export class Controller {
  constructor({ Office, corrector, Parser, timeoutMs = 8000 }) {
    Object.assign(this, { Office, corrector, Parser, timeoutMs });
    this.busy = false; this.output = null; this.generation = 0; this.closed = false;
  }
  call(invoke) { return officeCall(invoke, this.timeoutMs); }
  item() {
    const O = this.Office;
    if (!O?.context?.requirements?.isSetSupported('Mailbox', '1.10') || O.context.platform !== O.PlatformType.OfficeOnline) throw new Error('RC supports Outlook on the web only, with Mailbox 1.10. Other clients are not enabled.');
    const item = O.context.mailbox?.item;
    if (!item?.getSelectedDataAsync || !item.body?.getAsync || !item.getComposeTypeAsync) throw new Error('Open a new HTML email in Outlook on the web.');
    return item;
  }
  sameItem(item) { if (this.closed || this.Office.context.mailbox.item !== item) throw new Error('Message changed or pane closed; operation cancelled.'); }
  async read(item) {
    const body = await this.call(cb => item.body.getAsync('html', cb));
    const selected = await this.call(cb => item.getSelectedDataAsync('html', cb));
    this.sameItem(item);
    if (selected?.sourceProperty !== 'body' || typeof selected.data !== 'string') throw new Error('Select text in the message body, not the subject.');
    return { body, selected: selected.data };
  }
  clear() { this.output = null; this.generation++; }
  close() { this.closed = true; this.clear(); }
  getOutput() {
    if (this.output) { try { this.sameItem(this.output.item); } catch { this.clear(); } }
    return this.output?.text ?? null;
  }
  async correct(options) {
    if (this.closed) throw new Error('Task pane closed.');
    if (this.busy) throw new Error('An operation is already running.');
    this.clear(); const generation = this.generation; this.busy = true;
    try {
      if (!['en', 'de'].includes(options?.language) || !['conservative', 'balanced'].includes(options?.mode)) throw new Error('Invalid language or mode.');
      const item = this.item();
      const type = await this.call(cb => item.getComposeTypeAsync(cb));
      if (type?.composeType !== 'newMail' || type?.coercionType !== 'html') throw new Error('This RC corrects new HTML messages only; replies, forwards and plain text are refused to protect prior mail.');
      const before = await this.read(item);
      const selection = fragment(before.selected, this.Parser);
      assertSelectionSafe(before.body, selection, this.Parser);
      const patched = correctFragment(selection, this.corrector, options);
      const latest = await this.read(item);
      if (latest.body !== before.body || latest.selected !== before.selected) throw new Error('Draft or selection changed; output discarded. Select again and retry.');
      this.sameItem(item);
      if (generation !== this.generation) throw new Error('Output cleared; operation cancelled.');
      this.output = { item, text: patched.text };
      return { text: patched.text, count: patched.count, skipped: patched.skipped };
    } finally { this.busy = false; }
  }
}
