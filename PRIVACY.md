# Privacy

LocalCorrect runs entirely inside Outlook on your computer. It makes no network connections:
no correction service, telemetry, analytics, updates or downloads.

It reads mail text only when you press the shortcut or the ribbon button, and only the
selection, or the body above the quoted earlier message. The text is corrected in memory and
the changed words are written back into the editor. Nothing is logged or stored. Settings
(text language and shortcut) are saved in `HKEY_CURRENT_USER\Software\LocalCorrect\Outlook`.

The shortcut is registered with Windows only while a compose window's body has focus; there is
no keyboard hook and typing is never monitored. LocalCorrect never sends or saves mail. Outlook
itself saves and synchronises drafts as usual.
