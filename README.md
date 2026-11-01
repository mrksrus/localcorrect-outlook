# LocalCorrect Outlook — 1.0.0-rc.1

GPL-3.0-only. **Output-only release candidate**, not a final release or a verified live Outlook installation. English/German spelling correction runs locally using the actual portable `@localcorrect/core` from the companion browser repository. No grammar checker, LLM, native Windows installer or whole-email in-place correction is supplied.

## Supported scope and use

**Outlook on the web, new HTML messages only**, Mailbox 1.10, Microsoft 365/Exchange Online or Outlook.com and tenant policy allowing custom add-ins. Other Outlook clients (new/classic Windows, Mac, mobile), replies/forwards, plain text, appointments and subject selection are refused. Account/UI availability still needs owner testing.

1. In a new HTML message, open LocalCorrect and select a unique phrase of your **own new prose**, not a signature or quote.
2. Choose EN/DE and conservative/balanced, then **Correct**. Keep selection stable while it is read. The draft remains unchanged.
3. Review the read-only **plain-text output**. Choose **Copy plain text**, then deliberately select the intended draft range and paste yourself. If browser/iframe clipboard permission is blocked, manually select/copy the output instead.
4. **Clear output** discards the pane's result; it is not draft Undo and does not clear the OS clipboard. Closing/reloading discards in-memory output. Changed items, failed correction and changed settings discard old output.

There are **no Office draft setter paths, no opt-in unsafe insertion, no send/save calls, no automatic clipboard writes**. Explicit Copy is used because clipboard user activation cannot be relied on after asynchronous Office reads. Clipboard history/device sync may retain copied text outside this add-in. Review your OS settings before copying confidential text. Output is a snapshot, not a live draft view: subsequent typing/selection changes are not applied to it.

Office's selection API cannot atomically bind read and insertion. This RC therefore removes insertion entirely rather than presenting rereads/Undo as a fix for the demonstrated last-read/target-switch race. Stable-snapshot checks still discard changes observed during reading; a late target switch cannot corrupt the draft because nothing writes to it.

Selected HTML is parsed in an **inert template**, never displayed as HTML or dumped into a textarea. Only corrected selected plain text is displayed. Supported inline markup is retained in the detached intermediate representation; P/DIV/BR are mapped to logical line breaks before the engine. Edits crossing text nodes or synthetic separators are skipped. **Copy/paste is plain text and loses rich formatting**; Outlook's paste behavior can add its own formatting. No exact visual paragraph-spacing claim is made. Unsupported markup/attributes, links, code, ambiguous phrases, known signature/quote containers and boundaries are refused. Unmarked signatures and all proper names cannot be detected universally. Name/correction safety is heuristic: review every result; abstentions are intentional.

## Build and validate

Requires Node.js 22+, Python 3, and the real browser checkout with `packages/core`, dictionaries and complete legal/provenance records.

```sh
npm ci --ignore-scripts
npm run build -- --core ../localcorrect-browser --base-url https://mrksrus.github.io/localcorrect-outlook
npm test
npm run test:integration
npm run validate
```

This is a **development build** until the maintainer fills the reviewed repository/full 40-character commit in `core-source.json`. Final parent build:

```sh
npm run build -- --core ../localcorrect-browser --base-url https://mrksrus.github.io/localcorrect-outlook --release
npm test
npm run test:integration
npm run validate
```

`--release` refuses an absent/mismatched pin or dirty `packages/core`. `--require-clean-core` enables the clean-package gate for development too. Build compiles the copied source snapshot, records its tree SHA-256/available HEAD and release flag in `build/build-info.json`, and creates `dist/localcorrect-outlook-1.0.0-rc.1.zip` plus checksum. Identical inputs yield deterministic ZIPs. The ZIP includes assets, manifest, source/lockfile/tests/scripts, actual core/dictionaries, full licenses/provenance and tooling dependency notices. No fake installer or published substitute engine is used. CI intentionally fails until the reviewed core pin is filled.

To rebuild from ZIP, use `outlook-source` as this checkout; place `core-source` at a sibling checkout's `packages/core`, restore `shared-notices` at that sibling root (move `dictionaries.py` into `scripts/`), then use a development build. A source-only reconstruction has no reviewed Git HEAD, so cannot meet `--release` without the actual pinned checkout.

## Installation: hosted assets or local HTTPS

A manifest needs reachable HTTPS assets; it does not embed them. The planned public base is `https://mrksrus.github.io/localcorrect-outlook`. **This repository change does not deploy or verify that URL.** Once the maintainer hosts `build/site/` there, users do not need a local server. Use the matching release `manifest.xml`. Do not sideload a localhost manifest expecting public assets.

Where custom add-ins are allowed: Outlook on the web **Apps/Get Add-ins → My add-ins → Add custom add-in → Add from file**, select `build/manifest.xml`. Microsoft changes this UI; tenant policy may block it. See [official sideload instructions](https://learn.microsoft.com/en-us/office/dev/add-ins/outlook/sideload-outlook-add-ins-for-testing). Remove the custom add-in to uninstall; remove local/hosted assets separately. No test message needs sending.

### Optional local trusted HTTPS

Rebuild with `--base-url https://localhost:3000`. The supplied server binds **127.0.0.1 only**, serves static assets without request logging/directory listings, and never changes OS trust:

```sh
npm run build -- --core ../localcorrect-browser --base-url https://localhost:3000
python scripts/serve_https.py --cert /path/to/trusted-localhost.pem --key /path/to/localhost-key.pem --directory build/site --port 3000
```

For a disposable **server/TLS smoke test** (not browser trust or Outlook proof), keep keys OUTSIDE the repo/build/site/ZIP. With OpenSSL installed:

```sh
CERTDIR="$(mktemp -d "${TMPDIR:-$HOME}/localcorrect-tls.XXXXXX")"
openssl req -x509 -newkey rsa:2048 -sha256 -nodes -days 1 -subj /CN=localhost -addext 'subjectAltName=DNS:localhost,IP:127.0.0.1' -keyout "$CERTDIR/key.pem" -out "$CERTDIR/cert.pem"
python scripts/serve_https.py --cert "$CERTDIR/cert.pem" --key "$CERTDIR/key.pem" --directory build/site --port 3000
# In another shell, use the actual CERTDIR path printed/retained above:
curl --cacert "$CERTDIR/cert.pem" https://localhost:3000/demo.html
# Ctrl-C stops server; delete the disposable CERTDIR after testing.
```

A self-signed leaf accepted by `curl --cacert` is **not automatically trusted by your browser/Office iframe**. For Outlook local testing supply a localhost certificate from your organization's already-trusted development CA, or deliberately install trust with a reviewed local tool such as mkcert according to your OS/browser policy. Trust setup is a user/admin decision: this project does not automate it or ask you to disable TLS checks. Check `/taskpane.html`, `/taskpane.js`, `/assets/icon-64.png` and `/dictionaries/en.txt` over trusted HTTPS before sideload. `/demo.html` is synthetic only and never exercises Office; no real mail in demos/tests.

Production hosts must serve static assets without analytics/injected scripts, allow Outlook framing (not blanket X-Frame-Options DENY), preserve the page's restrictive CSP, and preferably send equivalent CSP plus `Referrer-Policy: no-referrer` and `X-Content-Type-Options: nosniff`. GitHub Pages response-header control is limited; meta CSP still applies. Office.js loads from Microsoft's required CDN; dictionaries come from the asset origin without draft text. Local text processing **does not mean Outlook or Office.js is offline**.

## Verification and release gates

Local synthetic tests cover zero writes even in the late-selection race, callbacks/timeouts, stale reads, clear/close, changed item, concurrency, structural HTML separators and real-core correction. `npm run validate` checks XML syntax, known structural invariants, output-only compiled code/CSP, dictionaries/legal/source and ZIP CRC/hash. It is **not official Office schema/service validation**. Official manifest validation and actual host sideload/CSP/clipboard behavior remain owner acceptance gates; the Microsoft validator may contact a service, so it was not run with private inputs or represented as an offline check. See [Microsoft manifest validation](https://learn.microsoft.com/en-us/office/dev/add-ins/testing/troubleshoot-manifest).

Before public RC: rebuild with reviewed final core pin, rerun all tests, compare two ZIP hashes, verify full notices, then publish matching hosted assets/manifest. Before relying on real drafts: synthetic new-mail EN/DE, both modes, selected-name preservation, P/DIV/BR, protected content, explicit Copy/manual fallback, clipboard disclosure, item/close/reset and network checks on the owner's actual permitted mailbox. No live Outlook, Windows installer, marketplace deployment or real-mail safety certification is claimed.
