using System.Text.RegularExpressions;
namespace LocalCorrect {
    public static class Mail {
        // A reply/forward header: "From:/Von: ..." followed within a few lines by "Sent:/Gesendet:/Date:",
        // an "-----Original Message-----" separator, or "On ... wrote:" / "Am ... schrieb ...:".
        static readonly Regex ReplyHeader = new Regex(
            @"^[ \t]*(?:(?:From|Von|De):[^\n]*\n(?:[^\n]*\n){0,3}?[ \t]*(?:Sent|Gesendet|Date|Datum|Envoyé):|-{2,}\s*(?:Original Message|Ursprüngliche Nachricht|Forwarded message|Weitergeleitete Nachricht)\s*-{2,}|(?:On|Am) [^\n]{4,200}(?:wrote|schrieb)[^\n]{0,40}:[ \t]*$)",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        // Index where the quoted earlier message starts, or -1. Text uses \n line ends.
        public static int ReplyStart(string text) { var m = ReplyHeader.Match(text); return m.Success ? m.Index : -1; }
    }
}
