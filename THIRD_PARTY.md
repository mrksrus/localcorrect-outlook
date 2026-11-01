# Third-party components and source

The add-in project and distributed combined program are GPL-3.0-only; see LICENSE. Components retain their own notices and compatible license terms.

- `@localcorrect/core`: copied from the companion LocalCorrect browser repository at build time. `build-info.json` identifies the exact core source tree and available git commit. Complete source, dictionaries, lexical safety assets and their notices are in `core-source/`; browser-root legal/provenance records are in `shared-notices/`. Those records are authoritative for URLs, checksums, transformations and per-asset licenses. Review new safety lexicon/vendor notices with the final reviewed core pin, not an earlier artifact's license assumptions.
- The frequency data currently used for English/German are Hermit Dave's FrequencyWords (OpenSubtitles2018), separately CC BY-SA 4.0; code GPL licensing does not relicense those data. Shared `THIRD_PARTY.md`, `dictionary-sources.json`, upstream README and CC BY-SA text are copied unchanged. Other lexical safety data may carry separate notices included with the copied core; verify them at the final release gate.
- esbuild 0.25.12 (MIT), build-time only. The compiled output carries any required bundled legal comments. Source: https://github.com/evanw/esbuild .
- linkedom 0.18.12 (ISC), synthetic tests only, not shipped as runtime code. Source: https://github.com/WebReflection/linkedom . Its transitive build/test dependencies are pinned in package-lock.json and are not bundled into the add-in.
- Locked build/test dependency license/notice files are included in `tooling-notices/` (nested packages use `nested-dependencies/` in notice paths). Tool executables/node_modules are not redistributed.
- Microsoft Office.js: loaded from Microsoft's official CDN, not redistributed in this ZIP. Its own terms apply. Documentation: https://learn.microsoft.com/en-us/office/dev/add-ins/develop/referencing-the-javascript-api-for-office-library-from-its-cdn .
- Project icons are original generated raster artwork, under this project's GPL-3.0-only license.

The release ZIP includes corresponding add-in and copied core source. The browser core provenance notices must be complete before public distribution; a build with missing or provisional dictionary provenance is not an approved release. CI's upstream revision pin is also a release gate, not a claim inferred from package names.
