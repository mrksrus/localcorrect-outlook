# Third-party components and source

The native add-in and combined program are GPL-3.0-only. Components and data retain
their own licenses. No Office.js or cloud dependency is used by the native EXE.

## Shared correction engine

The exact source pin is in core-source.json. The engine began as an original
JavaScript implementation inspired by [SymSpell, Wolf Garbe](https://github.com/wolfgarbe/SymSpell), MIT upstream.
No upstream implementation code or SymSpell frequency dictionary was copied.
The current compiler uses symmetric-delete keys and distance checks; runtime
uses static priors, lexical validity and preservation gates. Native RC5 additionally
uses a local contextual edit model fine-tuned from an MIT-licensed pretrained
BERT Mini base. The approximately 47 MB ONNX model, tokenizer metadata,
training/export recipe, source revisions and checksums are provided by the
corresponding browser source. The pretrained model retains its MIT notice; the
derived model and combined program also carry the applicable project and source
terms recorded there. Model size and runtime parity do not establish quality.

Hermit Dave's FrequencyWords data are CC BY-SA 4.0. English spelling inventories
retain MIT/BSD notices; selected German inventories use GPL-3.0. codespell typo
data at b53e95197f7c799674c785028a182e2dfd1df881 retain CC BY-SA 3.0, upstream README
and English Wikipedia attribution. Isolated Litkey spelling pairs retain CC BY-SA
4.0 and the original corpus author citation. Raw pupil stories are not bundled.

The complete transformations, source URLs, authors, license texts and checksums
are preserved in source/browser/THIRD_PARTY.md, typo-sources.json,
candidate-sources.json, context-data-sources.json, protect-sources.json, ambiguity-sources.json and
source/browser/packages/core/vendor/typo-data. Source/browser/tests/scale-corpus
contains the separate synthetic-data notices and gold. These paths describe the
installed payload. The companion browser repository provides the same records.

## Native desktop distribution

The native installer has no Office.js dependency. It bundles the core, contextual model, dictionaries and lexicons with their source terms. `source/browser/packages/core` contains corresponding source and notices; `source/browser/scripts` contains the training/export recipe. Public source records attribute the training sources, including ErAConD and the selected Falko/MERLIN material, with their component terms. Raw learner texts and private correspondence are not bundled. `source/outlook` contains the native add-in, helper, installer and build sources.

Runtime dependencies are pinned in `native/*/packages.lock.json`: Jint 4.17.0, Acornima 1.7.0, Microsoft.ML.OnnxRuntime and its managed binding 1.31.1, and System.Numerics.Tensors 9.0.0. The .NET Framework proxy no longer loads Jint or ONNX Runtime into Office. Jint uses BSD-2-Clause and Acornima uses BSD-3-Clause. ONNX Runtime and the Microsoft managed support libraries use MIT with their included third-party notices. Exact NuGet packages, nuspec license declarations and shipped license/notice files are included under `third-party-packages`. Full copyright and license texts, including Acornima's inherited Esprima notice, are retained. [Jint source](https://github.com/sebastienros/jint), [Acornima source](https://github.com/adams85/acornima) and [ONNX Runtime source](https://github.com/microsoft/onnxruntime/tree/v1.31.1) identify their upstream projects; package metadata is authoritative for the exact distribution.

The helper publish is self-contained for `win-x64` and includes the resolved Microsoft.NETCore.App.Runtime.win-x64 .NET 10 runtime. Its exact version comes from the publish runtime configuration and is recorded in `build-info.json` and `native-helper/dotnet-runtime-source.json`. The exact runtime package, MIT license and third-party notices are copied under `third-party-packages`. Build SDK/runtime servicing updates must be reviewed with the resulting artifact identity rather than assuming every .NET 10 publish is byte-identical.

General Microsoft Visual C++ runtime DLLs and the redistributable installer are not included. Users obtain the system x64 VC v14 runtime directly from Microsoft's [official download page](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist). Setup requires the 14.44 toolset family or later and the fixed required system DLLs; the latest supported v14 is recommended. The separate Microsoft installation may require administrator rights. LocalCorrect neither downloads nor installs that prerequisite and makes no assumption about the distributor's Visual Studio redistribution entitlement. Microsoft's [deployment guidance](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files) describes central runtime deployment.

The pinned NuGet 1.31.1 `runtimes/win-x64/native/onnxruntime.dll` is 17,253,728 bytes, SHA-256 `cb2fa518b18356617f6f9f460c012501da2819ec7e5c1d80bd8f5de20492db0c`. Its PE linker version is 14.44; its Rich header records linked MSVC build 35229 and other objects from build 35207. The installer uses the toolset family as its floor, not an asserted universal servicing-build minimum. Microsoft requires a runtime at least as recent as the producer's toolset; see the [runtime compatibility FAQ](https://learn.microsoft.com/en-us/lifecycle/faq/visual-c-faq). Startup still fails closed if a native dependency is incompatible. The build verifies this exact ORT binary identity and records it in `build-info.json`. The self-contained .NET package separately retains its own licensed `vcruntime140_cor3.dll`, package license and notices.

Microsoft .NET Framework reference assemblies are build-time only. The installer requires .NET Framework 4.8 on the target device and does not redistribute the framework. No Office binaries or PIAs are bundled; the COM interfaces use Microsoft's published interface IDs.

## Build/test and legacy components

esbuild 0.25.12 is MIT and build-time only. linkedom 0.18.12 is ISC and used by
legacy synthetic tests. Exact dependencies are pinned in package-lock.json.
The retained web prototype loads Microsoft's Office.js CDN in that prototype only;
it is not part of the native installer. Legacy notices and deployment details are
in LEGACY-WEB.md. Project raster icons are original GPL-3.0-only artwork.
