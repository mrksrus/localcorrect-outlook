# Privacy

## Native desktop candidate

The `native/` COM add-in embeds the EN/DE engine, dictionaries and Jint runtime. Installed correction has no network calls or hosted assets. It runs only when the user invokes correction, and never sends/saves mail or records body text. Settings store only language, correction mode and shortcut in HKCU. No keyboard hook is installed. The hotkey is registered while an eligible popped-out compose window is active.

Word range edits are bounded to validated original words, with stale-document/selection rejection and a custom Undo record. Known signature bookmarks, quoted text, links, fields, content controls and code-like fonts are protected; unusual unmarked content still needs user review. The installer writes only LocalCorrect keys under HKCU, respects existing Office policy and does not require administrator rights. Windows/Outlook acceptance is outstanding; see `native/README.md`.

## Legacy web prototype


Correction runs in the pane using bundled local code/dictionaries. Only the composing item's body and explicitly selected fragment are read through Office.js for safety checks. No mailbox enumeration, recipients, attachments, history, identity tokens or send/save API is used. Replies/forwards are refused. The manifest requests **ReadItem**, not ReadWriteItem/ReadWriteMailbox; shipping runtime has no Office setters.

No telemetry, analytics, remote correction service, account registration, cookies, localStorage, IndexedDB, service worker or draft logging. Snapshots and selected-text output remain in JavaScript memory; Clear output/reload/close discards the result, not secure memory erasure. Body HTML is never rendered or displayed; only selected corrected plain text appears in a read-only field. Output is a snapshot, not a live draft view. Outlook itself may save/synchronize drafts independently.

**Explicit Copy** writes only the reviewed output to the OS clipboard. No automatic Copy, clipboard reads or persistence. OS clipboard history/device sync and other clipboard consumers may retain/expose copied text outside this add-in. Clear output does not clear the OS clipboard; review OS settings and avoid copying confidential material if inappropriate. Clipboard-denied fallback is manual select/copy; rich formatting is lost with plain-text Copy.

Static assets/dictionaries load from the configured origin without draft text in requests; Microsoft's Office.js loads from its required CDN. Host/CDN operators see ordinary request metadata such as IP/user agent, not draft text emitted by our code. Office/Outlook/Microsoft scripts have their own network behavior; **the entire runtime is not offline**. Deploy without analytics, injected scripts or content-bearing URLs. The local HTTPS helper logs no requests and makes no OS trust changes. Never put real mail in demos or test fixtures.
