# LocalCorrect for classic Outlook

A local English/German typo corrector for classic Outlook 2019 on Windows.
Press an editable shortcut to correct selected prose or the compose body.
Corrections happen in place, preserve surrounding formatting and form one Word
Undo operation. A ribbon button and settings dialog are included.

[Download the experimental native EXE installer](https://github.com/mrksrus/localcorrect-outlook/releases/tag/v1.0.0-rc.3).
This is a per-user COM add-in. No Exchange/account manifest, hosted service,
cloud correction or Office.js deployment is required.

## Install

Close Outlook, run `LocalCorrect-Outlook-Setup.exe`, then reopen classic Outlook.
The installer detects x86/x64 Office, uses per-user registry/files and provides
uninstall through Windows Apps settings or the installer. It is unsigned.
The runtime targets .NET Framework 4.8. See [native/README.md](native/README.md)
for the exact installation and acceptance steps. New Outlook, Mac and mobile
are outside this native add-in's scope.

The engine initializes static dictionaries in a background worker when the add-in
loads. This reads no email. A shortcut pressed before initialization completes
waits for that same worker. Subsequent correction uses a compact compiled lookup.
The worker rechecks the editor, selection and document before applying any edits.
Selections cutting through a word or surrogate pair are refused. It never sends
or saves a message, logs document text or monitors typing.

## Correction goals and evidence

Avoiding unwanted edits takes priority over covering every typo. Uncertain words
remain unchanged. The 99% target refers to precision among edits actually made,
not correction of 99% of all errors. Both saved modes currently share the same
precision-first policy. Real-word/grammar mistakes and unfamiliar names remain
difficult. Review important messages before sending.

The exact shared source pin is in [core-source.json](core-source.json).
[Browser engine, public data and full measurements](https://github.com/mrksrus/localcorrect-browser)
include 5,000 synthetic documents, 100 separately authored blind documents,
wanted/wrong/missed edit counts, provenance and reproducible comparisons.
Windows CI tests .NET 10 and .NET Framework 4.8, AppDomain runtime parity and
installer filesystem behavior. **Actual Outlook 2019 x86/x64 loading, formatting,
shortcut, Undo and uninstall acceptance remains unverified.**

## Starting point and credits

The shared engine began with Wolf Garbe's
[SymSpell symmetric-delete algorithm](https://github.com/wolfgarbe/SymSpell).
It is an original JavaScript implementation and offline compiler, not a direct
GitHub fork or a neural model. AI-assisted iteration adjusted rules, static data
and performance, without neural training. Bundled data include FrequencyWords,
English/German spelling inventories, codespell and isolated Litkey error pairs.
[THIRD_PARTY.md](THIRD_PARTY.md) records licenses and attribution.
No private chats or correspondence are published.

## Build

`npm ci`, then `npm run build:native -- --core /path/to/localcorrect-browser`.
The reviewed release build adds `--release --locked`; it requires the pinned,
clean shared core and frozen test data. The resulting EXE includes corresponding
source, data, provenance and license texts. Tests run before publication; the
tested Windows CI artifact is promoted unchanged by digest.

The older Office.js prototype is retained for source history only. Its account
manifest is **not** needed for this product. [Legacy details](LEGACY-WEB.md).
[Privacy](PRIVACY.md). [Security](SECURITY.md).
