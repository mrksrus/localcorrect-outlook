# Third-party components and source

The native add-in and combined program are GPL-3.0-only. Components and data retain
their own licenses. No Office.js or cloud dependency is used by the native EXE.

## Shared correction engine

The exact source pin is in core-source.json. The engine began as an original
JavaScript implementation inspired by [SymSpell, Wolf Garbe](https://github.com/wolfgarbe/SymSpell), MIT upstream.
No upstream implementation code or SymSpell frequency dictionary was copied.
The current compiler uses symmetric-delete keys and distance checks; runtime
uses static priors, lexical validity and preservation gates. It is not neural training.

Hermit Dave's FrequencyWords data are CC BY-SA 4.0. English spelling inventories
retain MIT/BSD notices; selected German inventories use GPL-3.0. codespell typo
data at b53e95197f7c799674c785028a182e2dfd1df881 retain CC BY-SA 3.0, upstream README
and English Wikipedia attribution. Isolated Litkey spelling pairs retain CC BY-SA
4.0 and the original corpus author citation. Raw pupil stories are not bundled.

The complete transformations, source URLs, authors, license texts and checksums
are preserved in source/browser/THIRD_PARTY.md, typo-sources.json and
source/browser/packages/core/vendor/typo-data. Source/browser/tests/scale-corpus
contains the separate synthetic-data notices and gold. These paths describe the
installed payload. The companion browser repository provides the same records.

## Native desktop distribution

The native installer has no Office.js dependency. It embeds the core with the same dictionary and lexicon licenses. `source/browser/packages/core` contains their complete source and notices. `source/outlook` contains the native add-in, installer and build sources.

Runtime dependencies are pinned in `native/*/packages.lock.json`: Jint 4.17.0, Acornima 1.7.0 and the .NET Framework support packages System.Memory 4.5.5, System.Buffers 4.5.1, System.Numerics.Vectors 4.5.0 and System.Runtime.CompilerServices.Unsafe 6.0.0. Exact NuGet packages, nuspec license declarations and any shipped license/notice files are included under `third-party-packages`. Jint uses BSD-2-Clause and Acornima uses BSD-3-Clause. Microsoft support packages use MIT and carry their own license and third-party notices in the payload. Full copyright and license texts, including Acornima's inherited Esprima notice, are included under `native/licenses` in source and `licenses` in the installed payload. `sources.json` records pinned upstream URLs and hashes. Their own package metadata is authoritative. Sources: https://github.com/sebastienros/jint, https://github.com/adams85/acornima.

Microsoft .NET Framework reference assemblies are build-time only. The installer requires .NET Framework 4.8 on the target device and does not redistribute the framework. No Office binaries or PIAs are bundled; the COM interfaces use Microsoft's published interface IDs.

## Build/test and legacy components

esbuild 0.25.12 is MIT and build-time only. linkedom 0.18.12 is ISC and used by
legacy synthetic tests. Exact dependencies are pinned in package-lock.json.
The retained web prototype loads Microsoft's Office.js CDN in that prototype only;
it is not part of the native installer. Legacy notices and deployment details are
in LEGACY-WEB.md. Project raster icons are original GPL-3.0-only artwork.
