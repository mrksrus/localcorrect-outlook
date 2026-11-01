import { DOMParser as LinkParser } from 'linkedom';
export class Parser {
  parseFromString(html) {
    return new LinkParser().parseFromString(/<html[\s>]/i.test(html) ? html : `<html><head></head><body>${html}</body></html>`, 'text/html');
  }
}
export const fakeCorrector = { correct(text) {
  const edits = [...text.matchAll(/\bteh\b/g)].map(m => ({ offset: m.index, length: 3, original: 'teh', replacement: 'the' }));
  return { text: text.replace(/\bteh\b/g, 'the'), edits, skipped: 0 };
} };
export function officeMock() {
  const state = { body: '<div>Please correct <b>teh message</b> today.</div>', selected: '<b>teh message</b>', writes: [], reads: 0, fail: null, mutateAt: 0, changedSelectionAt: 0, delay: 0, source: 'body', composeType: 'newMail', coercionType: 'html' };
  const callback = (name, value, cb) => setTimeout(() => cb(state.fail === name ? { status: 'failed', error: { message: 'synthetic' } } : { status: 'succeeded', value }), state.delay);
  const item = {
    body: { getAsync(type, cb) { state.reads++; if (state.reads === state.mutateAt) state.body += '<p>User edit</p>'; if (state.reads === state.changedSelectionAt) state.selected = '<b>today</b>'; callback('body', state.body, cb); } },
    getSelectedDataAsync(type, cb) { callback('selection', { data: state.selected, sourceProperty: state.source }, cb); },
    getComposeTypeAsync(cb) { callback('compose', { composeType: state.composeType, coercionType: state.coercionType }, cb); },
    setSelectedDataAsync(html, options, cb) { if (state.fail !== 'write') { state.body = state.body.replace(state.selected, html); state.writes.push({ html, options }); state.selected = ''; } callback('write', undefined, cb); }
  };
  const Office = { PlatformType: { OfficeOnline: 'OfficeOnline' }, context: { platform: 'OfficeOnline', requirements: { isSetSupported: () => true }, mailbox: { item } } };
  return { state, Office };
}
