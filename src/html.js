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
function leaves(root) {
  const output = [];
  function visit(n) {
    if (n.nodeType === 3) output.push(n);
    else for (const child of n.childNodes) visit(child);
  }
  visit(root);
  return output;
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
  const nodes = leaves(root);
  const text = nodes.map(n => n.data).join('');
  if (!text.trim() || text.length > 20000) throw new Error('Select 1–20,000 characters in the message body.');
  if (boundary.test(text)) throw new Error('Selection contains a quote or signature boundary.');
  return { root, nodes, text };
}
export function assertSelectionSafe(bodyHtml, selection, Parser) {
  const doc = parse(bodyHtml, Parser);
  const nodes = leaves(doc.body);
  const full = nodes.map(n => n.data).join('');
  const offset = full.indexOf(selection.text);
  if (offset < 0 || full.indexOf(selection.text, offset + 1) >= 0) throw new Error('Selection cannot be located uniquely; select a longer unique phrase.');
  let pos = 0;
  for (const node of nodes) {
    const end = pos + node.data.length;
    if (end > offset && pos < offset + selection.text.length && protectedNode(node)) throw new Error('Quotes, signatures, links and code are protected.');
    pos = end;
  }
  // Boundaries include structural line breaks, unlike textContent.
  let rendered = '';
  let selectedStart = -1;
  let selectedEnd = -1;
  let flatPos = 0;
  function visit(n) {
    if (n.nodeType === 3) {
      if (offset >= flatPos && offset < flatPos + n.data.length) selectedStart = rendered.length + offset - flatPos;
      if (offset + selection.text.length > flatPos && offset + selection.text.length <= flatPos + n.data.length) selectedEnd = rendered.length + offset + selection.text.length - flatPos;
      rendered += n.data; flatPos += n.data.length;
    } else {
      if (/^(P|DIV|BR|BLOCKQUOTE|HR)$/.test(n.tagName)) rendered += '\n';
      for (const child of n.childNodes) visit(child);
      if (/^(P|DIV|BLOCKQUOTE)$/.test(n.tagName)) rendered += '\n';
    }
  }
  visit(doc.body);
  const cut = rendered.search(boundary);
  if (selectedStart < 0 || selectedEnd < 0 || (cut >= 0 && selectedEnd > cut)) throw new Error('Selection is at or after a signature/quoted-message boundary.');
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
  let offset = 0, count = 0, skipped = result.skipped;
  const spans = selection.nodes.map(node => { const start = offset; offset += node.data.length; return { node, start, end: offset, edits: [] }; });
  for (const edit of result.edits) {
    const span = spans.find(s => edit.offset >= s.start && edit.offset + edit.length <= s.end);
    if (!span) { skipped++; continue; } // Never redistribute edits across markup.
    // Ignore malformed engine partial-word suggestions at inline-node edges.
    const before = selection.text[edit.offset - 1] || '';
    const after = selection.text[edit.offset + edit.length] || '';
    if (/[\p{L}\p{M}]/u.test(before) || /[\p{L}\p{M}]/u.test(after)) { skipped++; continue; }
    span.edits.push(edit); count++;
  }
  for (const span of spans) for (const edit of span.edits.reverse()) {
    const local = edit.offset - span.start;
    span.node.data = span.node.data.slice(0, local) + edit.replacement + span.node.data.slice(local + edit.length);
  }
  return { html: selection.root.innerHTML, text: selection.root.textContent, count, skipped };
}
