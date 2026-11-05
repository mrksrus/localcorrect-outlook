# LocalCorrect for classic Outlook

This is a native COM add-in for the Windows desktop version of classic Outlook 2019, both 32-bit and 64-bit. It uses .NET Framework 4.8 and embeds the offline English/German correction engine and dictionaries. It needs no Outlook web manifest, Microsoft account deployment, Exchange deployment, local server or hosted scripts. New Outlook and macOS are not supported.

## Install and use

Close Outlook. Run `LocalCorrect-Outlook-Setup.exe` as your regular Windows user. It installs under `%LOCALAPPDATA%\LocalCorrect\Outlook` and registers only LocalCorrect's COM class and Outlook add-in in HKCU. It does not require administrator privileges. The executable is unsigned. Office policies that require signed add-ins can block it; this installer does not change those policies.

Pop out the email compose window. Open the LocalCorrect ribbon tab. Select English or German in Settings. Select prose and choose **Correct selection / body**, or press Ctrl+Shift+L. With no selection it processes the body, up to 20,000 UTF-16 code units. Settings can change the shortcut to Ctrl+Shift or Ctrl+Alt plus a letter. If another application owns the shortcut, use the ribbon. Inline replies in the main Outlook window are not supported, use Pop Out.

Static dictionaries initialize in a background worker when the add-in loads, without reading any email. This takes about 5.5 seconds in the measured Linux development engine. A first action waits if initialization is still running. Correction runs only when invoked. It never sends or saves mail, logs body text, monitors typing, or contacts a correction service. The hotkey is registered only while an eligible compose window is active and its Word body editor has keyboard focus. It does not act on the subject or address fields. A timer checks window eligibility, not typed content. English and German are explicit modes, with conservative correction as the default. Both saved modes currently share the same precision-first policy. Review the result before sending. Ctrl+Z should undo the correction as one action.

Only changed word ranges are replaced. Unchanged ranges and their formatting stay intact. Links, fields, content controls, the Outlook automatic-signature bookmark, code-like fonts, and engine-recognized quoted text are excluded. Arbitrary manually inserted signatures, unmarked code and unusual reply templates cannot always be recognized; select only the prose you intend to correct. Mixed formatting inside a changed word is not guaranteed to survive. Tracked and protected documents are refused. This is not a general rich-editor adapter.

The engine runs on a worker thread in its own .NET AppDomain with private dependency redirects. Outlook-wide assembly policy is unchanged. If the document, selection or foreground window changes during correction, the result is discarded. Selections cutting through a word, combining sequence or surrogate pair are refused. A host without Word custom Undo support is refused before edits. If an edit fails midway, the add-in attempts to undo the complete custom action. No automatic retry occurs.

Remove it through Windows Apps, or run the installer with `/uninstall`. Removal deletes only owned registration keys and unchanged files in its inventory. Language/shortcut preferences remain. Uninstall before installing another version; this candidate refuses to replace an actively registered installation. If removal retains modified or unrelated files, it keeps the ownership marker and inventory so removal can be retried. A later install verifies those records and moves the retained folder intact to a unique sibling named `Outlook.retained-...`, then installs into a fresh folder. The installer reports the retained folder location. Missing or invalid ownership records cause refusal without moving or removing files. An uninstaller launched from its installation folder copies itself to a uniquely named temporary executable so its installed copy can be removed.

## Build

Requirements: Node.js 22+, Python 3, .NET SDK 10. NuGet packages are pinned and lockfiles are checked in. Microsoft reference assemblies permit compiling .NET Framework on Linux. Restore/build need network access; the installed product does not.

```sh
npm ci
node scripts/build-native.mjs --core /path/to/localcorrect-browser --dotnet /path/to/dotnet
```

Output: `dist/LocalCorrect-Outlook-Setup.exe`, a managed Windows PE executable with an embedded ZIP payload. It is not a ZIP renamed to `.exe`. The same AnyCPU add-in supports both Outlook bitnesses. The installer writes the required COM registration in both registry views on 64-bit Windows.

Use `--release --locked` only with a clean core checkout matching `core-source.json`. The core hash, frozen email-corpus hashes and corresponding source are included in the payload. The original 200 documents plus the 5,000-case corpus and 100 blind documents ship with the source so the parity build can be reproduced. `--skip-tests` is for incremental compile checks, never a release gate. The installer carries native/core source, dictionaries and license notices plus the exact runtime NuGet packages. Jint runs without CLR interop. Dependency package metadata records license expressions and source repositories.

## Required acceptance before a tested Windows release

Linux compilation and engine tests do not prove Office COM loading. This candidate requires the following on actual Outlook 2019 x86 and x64:

- Install as a standard user, inspect registered keys, start Outlook and confirm the ribbon loads.
- Test English and German corrections, a selected passage and a whole body. Disconnect networking and repeat.
- Test HTML and plain-text mail, bold/italic text, emoji, tables, automatic signatures, quoted replies, hyperlinks, fields and attachments. Verify untouched text/formatting and attachments remain intact.
- Test Ctrl+Z produces one undo step and restores text and formatting. Induce a mid-edit failure and verify rollback.
- Edit or switch windows while correction is running and verify no stale change is applied. Verify read-only mail and inline replies are excluded.
- Change the shortcut, test a collision, switch away from Outlook and verify the shortcut is released. Use the ribbon when a shortcut is reserved.
- Test install refusal with Outlook open, an existing/conflicting install and restrictive Office policy. Uninstall with unrelated/modified files present and verify they remain.

Do not publish as a tested Windows release until these checks pass. The SDK build and portable engine tests are recorded separately. The build compares 5,522 document/mode cases against the JavaScript engine, including the original 200 in both modes, all 5,000 new documents and the 100-document blind set in the default mode. This is runtime parity; gold correction accuracy is reported separately. On Windows, the build also executes the engine/parity tests under .NET Framework 4.8 in an isolated AppDomain. On Linux it compiles the net48 add-in and executes the portable net10 engine tests. This still does not exercise Office COM integration.

Dedicated frozen-candidate Linux x64 .NET10 engine measurements on an Intel Core i5-1038NG7 recorded 222–487 ms for a first 1,000-word email after initialization, with warm medians of 124–153 ms and 367–369 MiB peak RSS. Initialization took 5.21–5.58 seconds. These are engine measurements, not actual Outlook/COM application timings or a guarantee on every CPU. The companion browser repository publishes fixtures, exact hashes and full measurements. Hosted Windows CI reports its own runtime timings. Real 32-bit Outlook memory headroom and end-to-end latency remain device acceptance work.
