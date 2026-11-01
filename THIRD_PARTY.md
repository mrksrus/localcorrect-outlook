# Third-party components and source

The add-in project and distributed combined program are GPL-3.0-only; see LICENSE. Components retain their own notices and compatible license terms.

- `@localcorrect/core`: copied from the companion LocalCorrect browser repository at build time. `build-info.json` identifies the exact core source tree and available git commit. Complete copied source, dictionary files and their notices are in `core-source/`; browser-root legal/provenance records are in `shared-notices/`. Those records are authoritative for source URLs, checksums, frequency corpus transformations and per-dictionary licenses.
- The currently integrated English/German data are Hermit Dave's FrequencyWords (OpenSubtitles2018), separately CC BY-SA 4.0; code GPL licensing does not relicense those data. Shared `THIRD_PARTY.md`, `dictionary-sources.json`, upstream README and CC BY-SA license are copied unchanged. No SymSpell.FrequencyDictionary/Hunspell data are used by this core revision. If upstream changes datasets, review and preserve the new attribution/license rather than assuming MIT.
- esbuild 0.25.12 (MIT), build-time only. The compiled output carries any required bundled legal comments. Source: https://github.com/evanw/esbuild .
- linkedom 0.18.12 (ISC), synthetic tests only, not shipped as runtime code. Source: https://github.com/WebReflection/linkedom . Its transitive build/test dependencies are pinned in package-lock.json and are not bundled into the add-in.
- Microsoft Office.js: loaded from Microsoft's official CDN, not redistributed in this ZIP. Its own terms apply. Documentation: https://learn.microsoft.com/en-us/office/dev/add-ins/develop/referencing-the-javascript-api-for-office-library-from-its-cdn .
- Project icons are original generated raster artwork, under this project's GPL-3.0-only license.

The release ZIP includes corresponding add-in and copied core source. The browser core provenance notices must be complete before public distribution; a build with missing or provisional dictionary provenance is not an approved release. CI's upstream revision pin is also a release gate, not a claim inferred from package names.
