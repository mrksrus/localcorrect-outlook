import test from 'node:test';
import assert from 'node:assert/strict';
import { Controller, officeCall } from '../src/controller.js';
import { Parser, fakeCorrector, officeMock } from './helpers.js';
const options = { language: 'en', mode: 'conservative' };
function setup() { const mock = officeMock(); return { ...mock, c: new Controller({ Office: mock.Office, corrector: fakeCorrector, Parser, timeoutMs: 100 }) }; }
test('output only; formatting/body unchanged; clear replaces Undo', async () => {
  const { c, state } = setup(); const original = state.body;
  assert.deepEqual(await c.correct(options), { text: 'the message', count: 1, skipped: 0 });
  assert.equal(c.getOutput(), 'the message'); assert.equal(state.body, original); assert.equal(state.writes.length, 0);
  c.clear(); assert.equal(c.getOutput(), null); assert.equal(c.undo, undefined);
});
test('late selection race after stale callback never calls any host setter', async () => {
  const { c, state, Office } = setup(); state.body += '<div>UNRELATED VICTIM</div>'; const original = state.body;
  let reads = 0; const item = Office.context.mailbox.item;
  item.getSelectedDataAsync = (type, cb) => { const selected = state.selected; cb({ status: 'succeeded', value: { data: selected, sourceProperty: 'body' } }); if (++reads === 2) state.selected = 'UNRELATED VICTIM'; };
  item.body.setAsync = () => { throw new Error('body setter forbidden'); };
  await c.correct(options);
  assert.equal(state.selected, 'UNRELATED VICTIM'); assert.equal(state.body, original); assert.deepEqual(state.writes, []);
});
test('works without any draft setter capability', async () => { const { c, Office } = setup(); delete Office.context.mailbox.item.setSelectedDataAsync; await c.correct(options); });
test('changed body discards output', async () => { const { c, state } = setup(); state.mutateAt = 2; await assert.rejects(c.correct(options), /changed/); assert.equal(c.getOutput(), null); assert.equal(state.writes.length, 0); });
test('changed selection discards output', async () => { const { c, state } = setup(); state.changedSelectionAt = 2; await assert.rejects(c.correct(options), /changed/); assert.equal(state.writes.length, 0); });
test('concurrent correction rejected', async () => { const { c, state } = setup(); state.delay = 5; const work = c.correct(options); await assert.rejects(c.correct(options), /already/); await work; });
for (const operation of ['clear', 'close']) test(`${operation} during delayed read cancels output`, async () => { const { c, state } = setup(); state.delay = 10; const work = c.correct(options); setTimeout(() => c[operation](), 15); await assert.rejects(work, /cancelled|closed/); assert.equal(c.getOutput(), null); assert.equal(state.writes.length, 0); });
for (const name of ['body', 'selection', 'compose']) test(`${name} callback failure remains generic`, async () => { const { c, state } = setup(); state.fail = name; await assert.rejects(c.correct(options), /API operation failed/); assert.equal(state.writes.length, 0); });
test('timeout and late callback', async () => { let cb; await assert.rejects(officeCall(callback => { cb = callback; }, 5), /did not respond/); cb({ status: 'succeeded', value: 'late' }); });
test('synchronous Office throw contained', async () => { await assert.rejects(officeCall(() => { throw new Error('private contents'); }), /unavailable/); });
test('invalid result is failure', async () => { await assert.rejects(officeCall(cb => cb(null)), /failed/); });
test('unsupported client refused', async () => { const { c, Office, state } = setup(); Office.context.platform = 'PC'; await assert.rejects(c.correct(options), /web only/); assert.equal(state.writes.length, 0); });
test('unsupported requirement refused', async () => { const { c, Office } = setup(); Office.context.requirements.isSetSupported = () => false; await assert.rejects(c.correct(options), /Mailbox/); });
for (const composeType of ['reply', 'forward']) test(`refuses ${composeType}`, async () => { const { c, state } = setup(); state.composeType = composeType; await assert.rejects(c.correct(options), /new HTML/); assert.equal(state.writes.length, 0); });
test('refuses plain-text composition', async () => { const { c, state } = setup(); state.coercionType = 'text'; await assert.rejects(c.correct(options), /new HTML/); });
test('subject selection refused', async () => { const { c, state } = setup(); state.source = 'subject'; await assert.rejects(c.correct(options), /not the subject/); });
test('different item cancels delayed read', async () => { const { c, state, Office } = setup(); state.delay = 10; const work = c.correct(options); setTimeout(() => { Office.context.mailbox.item = {}; }, 15); await assert.rejects(work, /Message changed/); assert.equal(state.writes.length, 0); });
test('item change clears completed output before Copy', async () => { const { c, Office } = setup(); await c.correct(options); Office.context.mailbox.item = {}; assert.equal(c.getOutput(), null); });
test('failed followup discards old output', async () => { const { c, state } = setup(); await c.correct(options); state.fail = 'body'; await assert.rejects(c.correct(options)); assert.equal(c.getOutput(), null); });
test('rejects malformed options', async () => { const { c } = setup(); await assert.rejects(c.correct({ language: 'xx', mode: 'fast' }), /Invalid/); });
