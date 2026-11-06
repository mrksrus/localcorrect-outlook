# LocalCorrect for classic Outlook

A local English/German typo corrector for classic Outlook 2019 on Windows.
Press an editable shortcut to correct selected prose or the compose body.
Corrections happen in place, preserve surrounding formatting and form one Word
Undo operation. A ribbon button and settings dialog are included. Settings selects English or German and whether to preserve vocabulary from both languages. Mixed-language vocabulary stays enabled for older saved settings; turn it off for text written only in the selected language.

Experimental candidate: native `1.0.0-rc.6`, using the browser RC7 language-aware core and unchanged RC6 neural model.
[Download the native RC6 installer and build evidence](https://github.com/mrksrus/localcorrect-outlook/releases/tag/v1.0.0-rc.6).
Actual Outlook 2019 acceptance remains unverified. Release artifacts require the
exact passing Windows CI build; CI logs and digests are linked from the release.
[Download the fixed native RC3 baseline installer](https://github.com/mrksrus/localcorrect-outlook/releases/tag/v1.0.0-rc.3).
This is a per-user COM add-in. No Exchange/account manifest, hosted service,
cloud correction or Office.js deployment is required.

## Install

Close Outlook, run `LocalCorrect-Outlook-Setup.exe`, then reopen classic Outlook.
The candidate supports 32-bit and 64-bit classic Outlook on 64-bit Windows 10/11.
32-bit Windows is unsupported. The installer registers both Office bitnesses, uses per-user registry/files and provides
uninstall through Windows Apps settings or the installer. It is unsigned.
The Office-facing COM proxy requires .NET Framework 4.8. A separate bundled
64-bit helper contains .NET 10, Jint and ONNX Runtime 1.31.1; no separate .NET 10
installation is needed. See [native/README.md](native/README.md)
for the exact installation and acceptance steps. New Outlook, Mac and mobile
are outside this native add-in's scope.

The helper also requires Microsoft's separately installed **Visual C++ v14
Redistributable for x64**, including when Outlook is 32-bit. Setup checks the
installed runtime before changing LocalCorrect files or registry keys. Use the
latest supported version from [Microsoft's official download page](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist).
That separate Microsoft installation may need administrator rights; LocalCorrect
setup remains per-user and does not download or install prerequisites.

The helper initializes dictionaries and its contextual model in the background
when the add-in loads. This reads no email. A shortcut pressed before
initialization completes waits for that same worker. Correction combines static
spelling rules with a local neural model under a precision-first acceptance policy.
The proxy and helper use bounded inherited standard-input/output streams, with
no HTTP server or correction-service connection.
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
[Earlier native RC4 shared-core quality report](https://github.com/mrksrus/localcorrect-browser/blob/57ed9d3c4e1b087c4f27fdd9eeb8fc41db17c32d/QUALITY.md)
records that release's synthetic corpora, including the browser RC5 audits,
wanted/wrong/missed edit counts, provenance and comparisons. Those historical results do not establish native RC6 quality. The RC6 build compares JavaScript/Jint outputs in both vocabulary scopes and
edit spans, checks ONNX tensor parity and exercises the helper protocol;
correction quality comes from the final shared-core evaluation.
Browser RC4 and native RC3 remain fixed comparison releases.
Windows CI is configured to test .NET 10, both .NET Framework 4.8 client
bitnesses, helper runtime parity, binary identity and installer filesystem behavior.
Passing final-candidate results are still required before artifact promotion.
**Actual Outlook 2019 x86/x64 loading, formatting,
shortcut, Undo and uninstall acceptance remains unverified.**
This is an experimental prerelease, not a tested Windows/Office release.

## Starting point and credits

The shared engine began with Wolf Garbe's
[SymSpell symmetric-delete algorithm](https://github.com/wolfgarbe/SymSpell).
It is an original JavaScript implementation and offline compiler, not a direct
GitHub fork. Native RC5 adds a local BERT Mini contextual edit model, approximately
47 MB, fine-tuned from an MIT-licensed pretrained base. The companion browser
source supplies model provenance, source terms and the training/export recipe.
Bundled spelling data include FrequencyWords,
English/German spelling inventories, codespell, isolated Litkey error pairs,
CSpell protective vocabulary and Homebrew formula names.
[THIRD_PARTY.md](THIRD_PARTY.md) records licenses and attribution.
No private chats or correspondence are published.

## Build

`npm ci`, then `npm run build:native -- --core /path/to/localcorrect-browser`.
The reviewed release build adds `--release --locked`; it requires the pinned,
clean shared core and frozen test data. The resulting EXE includes corresponding
source, data, provenance and license texts. Tests run before publication; the
tested Windows CI artifact is promoted unchanged by digest.

Visual C++ runtime DLLs and the Microsoft redistributable installer are not
bundled. The helper uses the system x64 prerequisite. The self-contained .NET
runtime retains its own licensed `vcruntime140_cor3.dll` and notices. See
[THIRD_PARTY.md](THIRD_PARTY.md) for the distribution contents.

The older Office.js prototype is retained for source history only. Its account
manifest is **not** needed for this product. [Legacy details](LEGACY-WEB.md).
[Privacy](PRIVACY.md). [Security](SECURITY.md).
