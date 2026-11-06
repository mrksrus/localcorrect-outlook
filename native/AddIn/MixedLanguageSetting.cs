namespace LocalCorrect.Outlook {
    // Missing or malformed persisted values retain the original bilingual safety.
    internal static class MixedLanguageSetting {
        internal static bool Read(object value) { return !(value is int number && number == 0); }
        internal static int Write(bool mixedLanguage) { return mixedLanguage ? 1 : 0; }
    }
}
