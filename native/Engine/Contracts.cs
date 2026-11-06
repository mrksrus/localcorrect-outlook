using System;
using System.Globalization;
using System.Runtime.Serialization;
namespace LocalCorrect {
    [Serializable, DataContract]
    public sealed class Edit {
        [DataMember] public int offset { get; set; }
        [DataMember] public int length { get; set; }
        [DataMember] public string original { get; set; }
        [DataMember] public string replacement { get; set; }
    }
    [Serializable, DataContract]
    public sealed class Result {
        [DataMember] public string text { get; set; }
        [DataMember] public Edit[] edits { get; set; }
        [DataMember] public int skipped { get; set; }
    }
    public sealed partial class Corrector {
        internal static void ValidateRequest(string text, string language, string mode, string context, int contextOffset) {
            if (text == null || text.Length > 20000) throw new ArgumentException("Select at most 20,000 characters.");
            if (language != "en" && language != "de") throw new ArgumentException("Choose English or German.");
            if (mode != "conservative" && mode != "balanced") throw new ArgumentException("Invalid correction mode.");
            if (context != null && (context.Length > 1_000_000 || contextOffset < 0 || contextOffset > context.Length - text.Length || context.Substring(contextOffset, text.Length) != text)) throw new ArgumentException("The selected text does not match its document context.");
            if (context != null && (InsideToken(context, contextOffset) || InsideToken(context, contextOffset + text.Length))) throw new ArgumentException("Selection cuts through a word. Select complete words; no changes made.");
        }
        internal static bool InsideToken(string text, int position) {
            if (position <= 0 || position >= text.Length) return false;
            if (char.IsHighSurrogate(text[position - 1]) && char.IsLowSurrogate(text[position])) return true;
            return TokenCharacter(text, position - (char.IsLowSurrogate(text[position - 1]) && position >= 2 && char.IsHighSurrogate(text[position - 2]) ? 2 : 1)) && TokenCharacter(text, position);
        }
        static bool TokenCharacter(string text, int position) {
            var category = CharUnicodeInfo.GetUnicodeCategory(text, position);
            return category == UnicodeCategory.UppercaseLetter || category == UnicodeCategory.LowercaseLetter ||
                category == UnicodeCategory.TitlecaseLetter || category == UnicodeCategory.ModifierLetter || category == UnicodeCategory.OtherLetter ||
                category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark || category == UnicodeCategory.EnclosingMark ||
                category == UnicodeCategory.DecimalDigitNumber || category == UnicodeCategory.LetterNumber || category == UnicodeCategory.OtherNumber ||
                category == UnicodeCategory.ConnectorPunctuation || category == UnicodeCategory.Format;
        }
        public static void Validate(string source, Result result) {
            if (result?.edits == null) throw new InvalidOperationException("Invalid correction result.");
            if (result.text == null || result.skipped < 0 || result.skipped > source.Length || result.edits.Length > source.Length) throw new InvalidOperationException("Invalid correction result.");
            int end = 0;
            foreach (var e in result.edits) {
                if (e == null || e.offset < end || e.length <= 0 || e.offset > source.Length - e.length || e.original == null || e.replacement == null || e.replacement.Length > 100 || source.Substring(e.offset, e.length) != e.original || !System.Text.RegularExpressions.Regex.IsMatch(e.replacement, @"\A\p{L}[\p{L}\p{M}-]*\z"))
                    throw new InvalidOperationException("Invalid correction edit.");
                end = e.offset + e.length;
            }
            var output = new System.Text.StringBuilder(source);
            for (int i = result.edits.Length - 1; i >= 0; i--) { var edit = result.edits[i]; output.Remove(edit.offset, edit.length).Insert(edit.offset, edit.replacement); }
            if (output.ToString() != result.text) throw new InvalidOperationException("Invalid correction output.");
        }
    }
}
