# LocalCorrect Outlook — 1.0.0-rc.1

GPL-3.0-only. A **release candidate**, not a final 1.0 or a verified live Outlook installation. English/German SymSpell correction runs locally using the portable `@localcorrect/core` from the companion browser repository. This is spelling correction, not grammar checking or an LLM.

## Supported scope

This RC enables **Outlook on the web, new HTML messages only**, with Mailbox API 1.10, a Microsoft 365/Exchange Online or Outlook.com mailbox, and tenant policy permitting add-ins. Personal Outlook.com availability and sideload UI may differ by account. Actual client/account compatibility must be checked by a maintainer; no live mailbox was accessed in development.

Other Outlook clients (including new/classic Windows, Mac and mobile), replies, forwards, appointments, subject selection and plain-text composition are explicitly refused. Windows COM/VSTO and arbitrary IMAP/POP accounts are **not supplied**. This is a standards-based Office web add-in, not a Windows installer.

## Use and safety

1. Open a **new HTML message**, open LocalCorrect from the compose ribbon/add-ins menu, and select a unique phrase of **your own newly composed body text**.
2. Choose EN/DE and conservative/balanced; press **Correct**. Do not type or move the selection until the operation finishes.
3. Review every correction. To **Undo**, reselect exactly the corrected phrase, without editing any part of the draft since correction. Undo is memory-only and disappears when the task pane closes.

Only selected HTML is replaced, using Office's selection API, never `body.setAsync`. Simple inline formatting is retained by text-node edits. Unsupported markup, links, code, ambiguous repeated phrases, known signature/quote containers and quote/signature boundaries are refused. Replies/forwards are refused entirely. Formatting spanning a split word is skipped. Manually pasted/unmarked signatures cannot be identified reliably: never select a signature. Office can normalize HTML and formatting; exact byte preservation of host markup is not promised.

Before any write the complete current body and selection are reread and compared; concurrent add-in operations are rejected. Undo refuses changed drafts. API errors/timeouts are shown without draft contents or automatic retries. An uncertain write outcome blocks further writes until the task pane is reopened. **Office's selection API has no atomic compare-and-swap or locked selection**: a user/other add-in can still change the selection between the final read and insertion. Do not edit during operations; this residual host API race is a release-candidate limitation, not a guarantee of transaction isolation. No send/save calls or mailbox scanning are made.

## Build and validate

Requires Node.js 22+ and Python 3. The browser checkout must contain `packages/core/package.json` named `@localcorrect/core`, source and `dictionaries/en.txt`, `de.txt` with their complete license/provenance notices. No published npm core package or third repository is used.

```sh
npm ci --ignore-scripts
npm test
npm run build -- --core ../localcorrect-browser --base-url https://localhost:3000
npm run test:integration
npm run validate
```

`--core` is an explicit checkout path, also accepted through `LOCALCORRECT_BROWSER`. The build copies the exact shared source and dictionary assets, bundles the core with esbuild, and records source SHA-256 and available git commit in `build/build-info.json`. Build fails if the core is absent; tests use clearly labeled synthetic mocks until integration runs. It creates `dist/localcorrect-outlook-1.0.0-rc.1.zip` plus SHA-256. Fixed ZIP timestamps/order/modes make identical input builds reproducible. The ZIP includes manifest, hosted assets, documentation, corresponding add-in/core source and notices. There is **no fake native installer**.

To rebuild from the ZIP, use `outlook-source` as this checkout and place `core-source` under an otherwise empty sibling checkout's `packages/core`; restore `shared-notices` at that checkout root, then use the commands above. The recorded core tree hash identifies copied core files, even when the parent git checkout was not yet committed. A published release must pin an actual reviewed upstream commit in `core-source.json` and CI; the parent maintainer owns that gate.

## HTTPS hosting and sideloading

The manifest is generated with your **HTTPS asset base URL**; it is not a deployable public release while using localhost. Host **the contents of `build/site/`** at that exact URL, over trusted HTTPS, with no third-party analytics or injected scripts. Do not deploy the source/notice folders as executable assets. The task pane uses a restrictive meta CSP. Set the same policy as a response header and `Referrer-Policy: no-referrer`, `X-Content-Type-Options: nosniff` on the host. Permit Outlook framing according to your tenant/host deployment policy; do not set a blanket `X-Frame-Options: DENY`.

Provide a trusted development HTTPS server/certificate on port 3000 yourself (no production infrastructure is configured by this project). For local synthetic testing only, serve `build/site` on loopback with `python -m http.server 3000 --bind 127.0.0.1 --directory build/site` and open `/demo.html`. This standalone textarea demo does **not** validate Office APIs, selection or host compatibility, and must never contain real mail.

In Outlook on the web use **Get Add-ins / Apps → My add-ins → Add a custom add-in → Add from file**, where tenant policy exposes it, and select `build/manifest.xml`. Microsoft periodically changes this UI; see the official sideloading guide below. Admin deployment uses the Microsoft 365 integrated-apps flow. Installation can be blocked by tenant policy, mailbox type or disabled custom add-ins. Remove the custom add-in to uninstall; remove hosted assets separately. Never send a message just to test this add-in.

Static hosting is necessary: a manifest alone does not contain executable assets. End-user devices must reach the configured host and Microsoft's required Office.js CDN. Local processing does **not** mean the entire Outlook runtime works offline. Dictionaries load from the asset origin, with no draft text in requests. Office.js is loaded from `https://appsforoffice.microsoft.com/lib/1/hosted/office.js`; self-hosting Office.js is not supported here. A restrictive tenant/CSP blocking required Office runtime resources may prevent startup: do not broadly relax network policy without review.

## Verification and limitations

Synthetic Office callback mocks cover errors, stale reads, selection changes, Undo protection and concurrency; detached HTML fixtures cover formatting/protected areas. Integration tests exercise the actual bundled core when present. No real Outlook sideload, tenant deployment, live email, Windows native build or accessibility/client compatibility certification has been performed. Automatic marketplace publication and public hosting are outside this repository.

References: [API selection semantics](https://learn.microsoft.com/en-us/javascript/api/outlook/office.messagecompose), [sideload Outlook add-ins](https://learn.microsoft.com/en-us/office/dev/add-ins/outlook/sideload-outlook-add-ins-for-testing), [Outlook add-in requirements](https://learn.microsoft.com/en-us/office/dev/add-ins/outlook/outlook-add-ins-overview), [Office.js CDN](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/referencing-the-javascript-api-for-office-library-from-its-cdn).
