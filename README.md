# LocalCorrect for classic Outlook

Fixes English and German typos in the e-mail you are writing in classic Outlook on
Windows 10/11, 32- or 64-bit. Built for Outlook 2019; other classic desktop versions (2016, 2021,
Microsoft 365) use the same add-in interface. New Outlook, Mac and Outlook on the web are not supported. Everything runs on your computer: no account,
no server, no text leaves Outlook.

**Status: release candidate 2.0.0-rc.3 (November 2026), not yet tested in a real Outlook.**
It uses the same correction engine as [LocalCorrect for Firefox](https://github.com/mrksrus/localcorrect-browser)
version 2, ported to C#. The old 1.0.0-rc versions left most typos uncorrected and are
replaced; they remain as git tags and releases.

## Install

1. Close Outlook. An existing LocalCorrect install for this Windows user, including 1.0.0-rc.x,
   is replaced. A registration that belongs to another product is left unchanged; setup names
   that key and stops.
2. Download `localcorrect-outlook-2.0.0-rc.3.zip` from the
   [release](https://github.com/mrksrus/localcorrect-outlook/releases/tag/v2.0.0-rc.3), unzip it
   and run `LocalCorrect-Outlook-Setup.exe`. It installs for your Windows user only (no admin
   rights) under `%LOCALAPPDATA%\LocalCorrect\Outlook`.
3. Start Outlook. If Outlook had blocked LocalCorrect after a previous start, setup clears that block for this add-in only. Failures during startup are written to `%LOCALAPPDATA%\LocalCorrect\Outlook\log.txt`.

Needs .NET Framework 4.8 (included in current Windows 10/11). The installer is unsigned, so
Windows SmartScreen may warn; Office policies that allow only signed add-ins will block it.
Remove it again in *Settings → Apps* (or run the installer with `/uninstall`).

## Use

Open a new mail, reply or forward **in its own window** (Pop Out for inline replies). On the
**LocalCorrect** ribbon tab choose *Correct selection / body*, or press **Ctrl+Shift+L**:

- with text selected, only the selection is corrected;
- with nothing selected, the body is corrected down to the quoted earlier message of a reply
  ("From:/Von: … Sent:/Gesendet:", "-----Original Message-----", "Am … schrieb …:").

**Ctrl+Z** undoes the whole correction in one step. *Settings* chooses the text language
(*Detect automatically*, *English*, *Deutsch*) and the shortcut (Ctrl+Shift or Ctrl+Alt plus a letter).

It fixes misspelled words (`recieve` → `receive`, `Dnak` → `Dank`), missing umlauts and ß
(`fur` → `für`, `gross` → `groß`), German noun capitals and English contractions
(`haus` → `Haus`, `dont` → `don't`). It leaves alone names, links, e-mail addresses, code,
numbers, acronyms, quoted `>` lines, Outlook's automatic signature, hyperlinks, fields and text
in code fonts. It does not fix grammar or real words in the wrong place, and when two
corrections fit equally well it takes the more common word: check before sending.

## How it works

`native/Engine/Corrector.cs` is the engine: a word is fine if it is in the word list of the text
language (German compounds are split into known parts); otherwise the most frequent word one
edit away (two for longer words) replaces it. The word lists in `native/Engine/data` are the
browser version's, compiled into the add-in DLL. `native/AddIn` is the Outlook COM add-in
(ribbon, shortcut, Word editor edits with one Undo record); `native/Installer` the per-user setup.

## Build and test

Needs the .NET SDK 10.0.403 (see `global.json`), Node.js 20+ and Python 3. Builds on Windows
and Linux; the add-in itself targets .NET Framework 4.8.

- `node scripts/build-native.mjs [--dotnet <path>]` runs the engine and installer tests and
  writes `dist/LocalCorrect-Outlook-Setup.exe`, `dist/localcorrect-outlook-<version>.zip`
  and `dist/SHA256SUMS`. On Windows the tests also run under .NET Framework 4.8.
- `node scripts/sync-engine.mjs <browser checkout>` copies the browser engine's word lists and
  records its output for 975 English/German cases in `native/Engine.Tests/parity.tsv`.
  The C# engine must produce exactly the same results. `engine-source.json` names the browser
  release it was synced from; CI checks that the files still match it.

The tests do not load Outlook. Things to check by hand in Outlook: the ribbon appears; English
and German corrections in a selection and a whole body; replies stop at the quoted message;
signature, links and formatting stay intact; Ctrl+Z restores everything in one step; the
shortcut changes in Settings.

Licence GPL-3.0; data credits in [THIRD_PARTY.md](THIRD_PARTY.md).
