# Third-party data and credits

The add-in embeds the word lists of [LocalCorrect for Firefox](https://github.com/mrksrus/localcorrect-browser)
(`native/Engine/data/*.lex`, see `engine-source.json`):

- **Word frequencies**: [FrequencyWords](https://github.com/hermitdave/FrequencyWords) by Hermit Dave,
  OpenSubtitles 2018, revision `525f9b560de45753a5ea01069454e72e9aa541c6`. CC BY-SA 4.0
  (`licenses/CC-BY-SA-4.0.txt`, `licenses/FrequencyWords-LICENSE`).
- **English Hunspell dictionary**: npm `dictionary-en` 4.0.0 (SCOWL / Kevin Atkinson et al.),
  `licenses/dictionary-en-LICENSE`. Used to decide which words are valid.
- **German Hunspell dictionary**: npm `dictionary-de` 3.0.0 (igerman98, Björn Jacke), GPL-2.0/3.0,
  GPL-3.0 chosen; `licenses/dictionary-de-LICENSE`.
- **Algorithm**: one/two-edit candidates ranked by frequency, after Peter Norvig's
  "How to Write a Spelling Corrector" and Wolf Garbe's SymSpell. Original code.

Build-time only: Microsoft .NET SDK and `Microsoft.NETFramework.ReferenceAssemblies` (MIT).
No Office binaries are bundled; the COM interfaces use Microsoft's published interface IDs.
Program licence: GPL-3.0-only.
