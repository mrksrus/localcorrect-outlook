# Security

Report vulnerabilities through GitHub's private vulnerability reporting for this repository.
Do not include real mail.

LocalCorrect is a per-user COM add-in (.NET Framework 4.8) with no network access and no
external processes. Edits are limited to whole words whose current text still matches what
was read; if the mail or the selection changed meanwhile, nothing is applied. All edits are
one Word Undo record, and a failure part-way undoes the edits made so far. Hyperlinks, fields,
content controls, inline objects, the automatic signature and code-font text are not edited.

The installer writes only to `%LOCALAPPDATA%\LocalCorrect\Outlook` and LocalCorrect's own HKCU
keys, refuses to overwrite a conflicting registration, and on removal deletes only files whose
recorded hash is unchanged. It needs no administrator rights and changes no Office policy.
The executable is unsigned.
