using System;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Runtime.Serialization;
using Jint;

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
    // One worker owns this instance. CLR interop is deliberately never enabled.
    public sealed class Corrector : MarshalByRefObject {
#if NET48
        public override object InitializeLifetimeService() { return null; }
#endif
        readonly Engine engine;
        static string Resource(string name) {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("LocalCorrect." + name))
            using (var r = new StreamReader(s ?? throw new InvalidOperationException("Missing bundled dictionary"))) return r.ReadToEnd();
        }
        public Corrector() {
            engine = new Engine(o => o.LimitMemory(768L * 1024 * 1024).TimeoutInterval(TimeSpan.FromSeconds(90)).MaxStatements(100_000_000));
            engine.Execute(Resource("core.js"));
            engine.SetValue("enSource", Resource("en.txt"));
            engine.SetValue("deSource", Resource("de.txt"));
            engine.Execute("var corrector = LocalCorrectCore.createCorrector({en:enSource,de:deSource}); enSource=null; deSource=null; LocalCorrectCore.prepare();");
        }
        public Result Correct(string text, string language, string mode, string context = null, int contextOffset = 0) {
            if (text == null || text.Length > 20000) throw new ArgumentException("Select at most 20,000 characters.");
            if (language != "en" && language != "de") throw new ArgumentException("Choose English or German.");
            if (mode != "conservative" && mode != "balanced") throw new ArgumentException("Invalid correction mode.");
            if (context != null && (context.Length > 1_000_000 || contextOffset < 0 || contextOffset > context.Length - text.Length || context.Substring(contextOffset, text.Length) != text)) throw new ArgumentException("The selected text does not match its document context.");
            if (context != null && (InsideToken(context, contextOffset) || InsideToken(context, contextOffset + text.Length)))
                throw new ArgumentException("Selection cuts through a word. Select complete words; no changes made.");
            engine.SetValue("inputText", text);
            engine.SetValue("documentText", context ?? text);
            engine.SetValue("contextOffset", context == null ? 0 : contextOffset);
            engine.SetValue("inputLanguage", language);
            engine.SetValue("inputMode", mode);
            try {
                var raw = engine.Evaluate("(function(){ var result=corrector.correct(inputText,{language:inputLanguage,mode:inputMode}); var protectedSpans=LocalCorrectCore.protectedRanges(documentText); result.edits=result.edits.filter(function(e){return !protectedSpans.some(function(p){return e.offset+contextOffset < p[1] && e.offset+contextOffset+e.length > p[0];});}); var output=inputText; for(var i=result.edits.length-1;i>=0;i--){var e=result.edits[i];output=output.slice(0,e.offset)+e.replacement+output.slice(e.offset+e.length);} result.text=output; return result;})()").AsObject();
                var edits = raw.Get("edits").AsArray();
                var result = new Result { text = raw.Get("text").AsString(), skipped = checked((int)raw.Get("skipped").AsNumber()), edits = new Edit[checked((int)edits.Length)] };
                for (int i = 0; i < result.edits.Length; i++) {
                    var edit = edits.Get(i.ToString(CultureInfo.InvariantCulture)).AsObject();
                    result.edits[i] = new Edit { offset = checked((int)edit.Get("offset").AsNumber()), length = checked((int)edit.Get("length").AsNumber()), original = edit.Get("original").AsString(), replacement = edit.Get("replacement").AsString() };
                }
                Validate(text, result);
                return result;
            } finally { engine.SetValue("inputText", ""); engine.SetValue("documentText", ""); }
        }
        static bool InsideToken(string text, int position) {
            if (position <= 0 || position >= text.Length) return false;
            if (char.IsHighSurrogate(text[position - 1]) && char.IsLowSurrogate(text[position])) return true;
            return TokenCharacter(text, position - (char.IsLowSurrogate(text[position - 1]) && position >= 2 && char.IsHighSurrogate(text[position - 2]) ? 2 : 1)) && TokenCharacter(text, position);
        }
        static bool TokenCharacter(string text, int position) {
            if (text[position] == '_') return true;
            var category = CharUnicodeInfo.GetUnicodeCategory(text, position);
            return category == UnicodeCategory.UppercaseLetter || category == UnicodeCategory.LowercaseLetter ||
                category == UnicodeCategory.TitlecaseLetter || category == UnicodeCategory.ModifierLetter || category == UnicodeCategory.OtherLetter ||
                category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark || category == UnicodeCategory.EnclosingMark ||
                category == UnicodeCategory.DecimalDigitNumber || category == UnicodeCategory.LetterNumber || category == UnicodeCategory.OtherNumber;
        }
        public static void Validate(string source, Result result) {
            if (result?.edits == null) throw new InvalidOperationException("Invalid correction result.");
            int end = 0;
            foreach (var e in result.edits) {
                if (e == null || e.offset < end || e.length <= 0 || e.offset > source.Length - e.length || e.original == null || e.replacement == null || e.replacement.Length > 100 || source.Substring(e.offset, e.length) != e.original || !System.Text.RegularExpressions.Regex.IsMatch(e.replacement, @"^\p{L}[\p{L}\p{M}-]*$"))
                    throw new InvalidOperationException("Invalid correction edit.");
                end = e.offset + e.length;
            }
        }
    }
}
