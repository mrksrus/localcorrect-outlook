# Security policy

## Native desktop candidate

The `native/` COM add-in embeds the EN/DE engine, dictionaries and Jint runtime. Installed correction has no network calls or hosted assets. It runs only when the user invokes correction, and never sends/saves mail or records body text. Settings store only language, correction mode and shortcut in HKCU. No keyboard hook is installed. The hotkey is registered while an eligible popped-out compose window is active.

Word range edits are bounded to validated original words, with stale-document/selection rejection and a custom Undo record. Known signature bookmarks, quoted text, links, fields, content controls and code-like fonts are protected; unusual unmarked content still needs user review. The installer writes only LocalCorrect keys under HKCU, respects existing Office policy and does not require administrator rights. Windows/Outlook acceptance is outstanding; see `native/README.md`.

## Legacy web prototype


Release candidate. Report vulnerabilities privately through the repository host's private reporting facility when available; otherwise request a private channel without mail, tokens or identifying information. Use synthetic fixtures only.

Threat model: unintended draft changes, stale callbacks, unsupported hosts, unsafe quote/signature/HTML selection and application-origin disclosure. **The legacy web prototype is output-only.** No Office setters, insertion opt-in, send/save or background writes exist. Office's non-atomic selection API cannot safely bind a read to insertion; more preflight reads/Undo do not solve its late target-switch race. This implementation avoids that risk by leaving all draft paste actions to the user.

Stable body/selection snapshots are checked before publishing output; concurrent operations, changed item, clear/close and observed stale reads cancel it. A late selection change after the final callback may still yield a stale output snapshot, but cannot modify the draft. Review before copying and deliberately paste into the intended location. Explicit clipboard Copy can expose text to OS history/device sync; no automatic copy or clipboard read is performed. Clear output is not clipboard clearing or draft Undo.

Email markup is parsed in inert template contents, never attached to live DOM or shown as HTML. Only selected plain text is displayed in a read-only field; results/errors are not treated as markup. Unsupported fragment markup/attributes are refused. Logical P/DIV/BR separators preserve line boundaries, and edits cannot cross synthetic separators/text nodes. Inline formatting remains in the detached intermediate structure but plain-text Copy loses it. Contract validation rejects invalid/overlapping edits. Known protected regions, links and ambiguous selections are refused; manual unmarked signatures/proper names are not universally identifiable. New HTML mail/web only.

A malicious host, tampered core/dictionary/dependency or compromised Office runtime is outside this boundary. Use HTTPS, verify ZIP hashes/provenance, review/pin shared core, restrict asset-host writes, retain CSP and avoid injected scripts. Office.js is an externally maintained CDN dependency without fixed SRI; full offline execution and end-to-end isolation are not claimed. The loopback HTTPS helper accepts supplied cert/key; never place keys in hosted assets or release archives, and never bypass browser trust for real use.

Release gates: exact reviewed clean core pin, tests/artifact reproducibility, complete legal notices, official manifest validation where approved, and owner synthetic sideload/CSP/clipboard/network checks on intended mailbox. No live Outlook installation has been verified. Native cross-compilation is separate from Windows/Office acceptance. See README for deliberate RC scope and pending acceptance.
