// Never attach email markup to a live document or load its resources.
const ALLOWED = new Set(['SPAN', 'B', 'STRONG', 'I', 'EM', 'U', 'S', 'FONT', 'BR', 'P', 'DIV']);
const FORBIDDEN = 'blockquote,script,style,iframe,object,form,input,textarea,select,a,pre,code,[contenteditable="false"],[hidden]';
const marker = /signature|gmail_quote|gmail_signature|divRplyFwdMsg|appendonsend|replyforward|moz-cite|yahoo_quoted/i;
const boundary = /(?:^|\n)\s*(?:--\s*$|_{5,}|-{5,}|On .{1,160}wrote:|Am .{1,160}schrieb.{0,80}:|From:|Von:|Sent:|Gesendet:|Best regards[,!]?\s*$|Kind regards[,!]?\s*$|Mit freundlichen Gr[üu][ßs]en\s*$)/im;

export function parse(html, Parser) {
  if (typeof html !== 'string' || html.length > 500000) throw new Error('Selection/body too large or invalid.');
  // Template contents are inert, including images/iframes. DOMParser alone can
  // fetch image resources in some browsers, so never feed body markup to it.
  if (!Parser && globalThis.document?.createElement) {
    const template = document.createElement('template');
    template.innerHTML = html;
    const content = template.content;
    return { body: {
      get childNodes() { return content.childNodes; },
      get innerHTML() { return template.innerHTML; },
      get textContent() { return content.textContent; },
      querySelector: selector => content.querySelector(selector),
      querySelectorAll: selector => content.querySelectorAll(selector)
    } };
  }
  if (!Parser) throw new Error('Inert HTML parser unavailable.');
  return new Parser().parseFromString(html, 'text/html');
}
// Logical plain text plus offsets into original text nodes. Structural separators
// are synthetic: never editable and never rendered as active draft HTML.
export function logicalText(root) {
  let text = '', trailingSynthetic = false;
  const spans = [];
  const block = n => /^(P|DIV|BLOCKQUOTE)$/.test(n.tagName);
  const separator = () => { if (text && !text.endsWith('\n')) { text += '\n'; trailingSynthetic = true; } };
  function visit(n) {
    if (n.nodeType === 3) {
      const start = text.length; text += n.data; if (n.data) trailingSynthetic = false;
      spans.push({ node: n, start, end: text.length, edits: [] });
    } else if (n.tagName === 'BR' || n.tagName === 'HR') { text += '\n'; trailingSynthetic = false; }
    else {
      if (block(n)) separator();
      for (const child of n.childNodes) visit(child);
      if (block(n)) separator();
    }
  }
  for (const child of root.childNodes) visit(child);
  // Drop only a trailing synthetic block separator, not explicit BR/text breaks.
  if (trailingSynthetic) text = text.slice(0, -1);
  return { text, spans };
}
function protectedNode(node) {
  for (let el = node.parentElement; el; el = el.parentElement) {
    if (el.matches?.(FORBIDDEN) || marker.test(`${el.id} ${el.className} ${el.getAttribute('data-marker') || ''}`)) return true;
  }
  return false;
}
export function fragment(html, Parser) {
  const doc = parse(html, Parser);
  const root = doc.body;
  if (!root || root.querySelector(FORBIDDEN)) throw new Error('Select only your own simple body text, without links, quotes or code.');
  for (const el of root.querySelectorAll('*')) {
    if (!ALLOWED.has(el.tagName) || marker.test(`${el.id} ${el.className}`)) throw new Error('Unsupported selection markup.');
    for (const attr of el.attributes) {
      // Reject rather than silently strip arbitrary markup. Formatting style is retained.
      if (!['style', 'class', 'color', 'size', 'face', 'lang', 'dir'].includes(attr.name.toLowerCase()) || /url\s*\(|expression\s*\(|javascript:|@import/i.test(attr.value)) throw new Error('Unsupported selection attributes.');
    }
  }
  const { text, spans } = logicalText(root);
  if (!text.trim() || text.length > 20000) throw new Error('Select 1–20,000 characters in the message body.');
  if (boundary.test(text)) throw new Error('Selection contains a quote or signature boundary.');
  return { root, spans, text };
}
export function assertSelectionSafe(bodyHtml, selection, Parser) {
  const doc = parse(bodyHtml, Parser);
  const { text: full, spans } = logicalText(doc.body);
  const offset = full.indexOf(selection.text);
  if (offset < 0 || full.indexOf(selection.text, offset + 1) >= 0) throw new Error('Selection cannot be located uniquely; select a longer unique phrase.');
  for (const { node, start, end } of spans) {
    if (end > offset && start < offset + selection.text.length && protectedNode(node)) throw new Error('Quotes, signatures, links and code are protected.');
  }
  const cut = full.search(boundary);
  if (cut >= 0 && offset + selection.text.length > cut) throw new Error('Selection is at or after a signature/quoted-message boundary.');
  return { offset };
}
export function correctFragment(selection, corrector, options) {
  // Run the core over the complete text so URLs/identifiers remain protected even
  // when their characters are split across formatting nodes.
  const result = corrector.correct(selection.text, options);
  if (!result || typeof result.text !== 'string' || !Array.isArray(result.edits) || !Number.isInteger(result.skipped) || result.skipped < 0) throw new Error('Core returned an invalid result.');
  let end = 0;
  for (const edit of result.edits) {
    if (!Number.isInteger(edit.offset) || !Number.isInteger(edit.length) || edit.offset < end || edit.length <= 0 || edit.offset + edit.length > selection.text.length || selection.text.slice(edit.offset, edit.offset + edit.length) !== edit.original || typeof edit.replacement !== 'string') throw new Error('Core edit contract violation.');
    end = edit.offset + edit.length;
  }
  let reconstructed = selection.text;
  for (const e of [...result.edits].reverse()) reconstructed = reconstructed.slice(0, e.offset) + e.replacement + reconstructed.slice(e.offset + e.length);
  if (reconstructed !== result.text) throw new Error('Core text/edit contract violation.');
  let count = 0, skipped = result.skipped;
  const spans = selection.spans;
  const accepted = [];
  for (const edit of result.edits) {
    const span = spans.find(s => edit.offset >= s.start && edit.offset + edit.length <= s.end);
    if (!span) { skipped++; continue; } // Never redistribute edits across markup.
    // Ignore malformed engine partial-word suggestions at inline-node edges.
    const before = selection.text[edit.offset - 1] || '';
    const after = selection.text[edit.offset + edit.length] || '';
    if (/[\p{L}\p{M}]/u.test(before) || /[\p{L}\p{M}]/u.test(after)) { skipped++; continue; }
    span.edits.push(edit); accepted.push(edit); count++;
  }
  for (const span of spans) for (const edit of span.edits.reverse()) {
    const local = edit.offset - span.start;
    span.node.data = span.node.data.slice(0, local) + edit.replacement + span.node.data.slice(local + edit.length);
  }
  let text = selection.text;
  for (const e of accepted.reverse()) text = text.slice(0, e.offset) + e.replacement + text.slice(e.offset + e.length);
  return { html: selection.root.innerHTML, text, count, skipped };
}
