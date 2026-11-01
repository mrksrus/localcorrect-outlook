import { fragment, assertSelectionSafe, correctFragment } from './html.js';

export function officeCall(invoke, timeoutMs = 8000) {
  return new Promise((resolve, reject) => {
    let settled = false;
    const timer = setTimeout(() => { settled = true; reject(new Error('Outlook did not respond. Operation outcome may be unknown; inspect the draft before retrying.')); }, timeoutMs);
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
export class Controller {
  constructor({ Office, corrector, Parser, timeoutMs = 8000 }) {
    this.Office = Office; this.corrector = corrector; this.Parser = Parser;
    this.timeoutMs = timeoutMs; this.busy = false; this.history = null; this.blocked = false;
  }
  call(invoke) { return officeCall(invoke, this.timeoutMs); }
  item() {
    const O = this.Office;
    if (!O?.context?.requirements?.isSetSupported('Mailbox', '1.10') || O.context.platform !== O.PlatformType.OfficeOnline) throw new Error('RC supports Outlook on the web only, with Mailbox 1.10. Other clients are not enabled.');
    const item = O.context.mailbox?.item;
    if (!item?.getSelectedDataAsync || !item.setSelectedDataAsync || !item.body?.getAsync || !item.getComposeTypeAsync) throw new Error('Open a new HTML email in Outlook on the web.');
    return item;
  }
  sameItem(item) { if (this.Office.context.mailbox.item !== item) throw new Error('Message changed; operation cancelled.'); }
  body(item) { return this.call(cb => item.body.getAsync('html', cb)); }
  selection(item) { return this.call(cb => item.getSelectedDataAsync('html', cb)); }
  async read(item) {
    const body = await this.body(item);
    const selected = await this.selection(item);
    this.sameItem(item);
    if (selected?.sourceProperty !== 'body' || typeof selected.data !== 'string') throw new Error('Select text in the message body, not the subject.');
    return { body, selected: selected.data };
  }
  async validateNewMail(item) {
    const type = await this.call(cb => item.getComposeTypeAsync(cb));
    if (type?.composeType !== 'newMail' || type?.coercionType !== 'html') throw new Error('This RC corrects new HTML messages only; replies, forwards and plain text are refused to protect prior mail.');
  }
  async exclusive(fn) {
    if (this.busy) throw new Error('An operation is already running.');
    if (this.blocked) throw new Error('Previous write outcome is uncertain. Close/reopen the task pane after inspecting the draft.');
    this.busy = true;
    try { return await fn(); } finally { this.busy = false; }
  }
  async correct(options) {
    return this.exclusive(async () => {
      if (!['en', 'de'].includes(options?.language) || !['conservative', 'balanced'].includes(options?.mode)) throw new Error('Invalid language or mode.');
      const item = this.item();
      await this.validateNewMail(item);
      const before = await this.read(item);
      const selection = fragment(before.selected, this.Parser);
      assertSelectionSafe(before.body, selection, this.Parser);
      const patched = correctFragment(selection, this.corrector, options);
      if (!patched.count) return { count: 0, skipped: patched.skipped };
      const latest = await this.read(item);
      if (latest.body !== before.body || latest.selected !== before.selected) throw new Error('Draft or selection changed; nothing was written. Select again and retry.');
      this.sameItem(item);
      this.history = null;
      try {
        await this.call(cb => item.setSelectedDataAsync(patched.html, { coercionType: 'html' }, cb));
        const after = await this.body(item);
        this.sameItem(item);
        // Record only a uniquely identifiable inserted selection. Office may normalize
        // markup: never use guessed offsets or a full-body setter for Undo.
        const check = fragment(patched.html, this.Parser);
        assertSelectionSafe(after, check, this.Parser);
        this.history = { item, body: after, original: before.selected, expectedText: patched.text };
      } catch (error) {
        this.blocked = true;
        throw error;
      }
      return { count: patched.count, skipped: patched.skipped };
    });
  }
  async undo() {
    return this.exclusive(async () => {
      const h = this.history;
      if (!h) throw new Error('Nothing to undo.');
      const item = this.item();
      if (item !== h.item) { this.history = null; throw new Error('Message changed; Undo discarded.'); }
      const now = await this.read(item);
      if (now.body !== h.body) { this.history = null; throw new Error('Draft changed after correction; Undo discarded to protect your edits.'); }
      const selected = fragment(now.selected, this.Parser);
      if (selected.text !== h.expectedText) throw new Error('Reselect exactly the corrected phrase in the body before Undo.');
      assertSelectionSafe(now.body, selected, this.Parser);
      const latest = await this.read(item);
      if (latest.body !== now.body || latest.selected !== now.selected) { this.history = null; throw new Error('Draft or selection changed; Undo cancelled.'); }
      this.sameItem(item);
      this.history = null;
      try {
        await this.call(cb => item.setSelectedDataAsync(h.original, { coercionType: 'html' }, cb));
        const after = await this.body(item);
        this.sameItem(item);
        assertSelectionSafe(after, fragment(h.original, this.Parser), this.Parser);
      } catch (error) { this.blocked = true; throw error; }
      return { undone: true };
    });
  }
}
