#if !NET48
using System;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Runtime.Serialization;
using Jint;

namespace LocalCorrect {
    // One worker owns this instance. CLR interop is deliberately never enabled.
    public sealed partial class Corrector : MarshalByRefObject, IDisposable {
        readonly Engine engine;
        readonly ContextModel model;
        static string Resource(string name) {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("LocalCorrect." + name))
            using (var r = new StreamReader(s ?? throw new InvalidOperationException("Missing bundled dictionary"))) return r.ReadToEnd();
        }
        public Corrector() {
            var stage = "loading bundled data";
            try {
                engine = new Engine(o => o.LimitMemory(768L * 1024 * 1024).TimeoutInterval(TimeSpan.FromSeconds(90)).MaxStatements(100_000_000));
                engine.Execute(Resource("core.js"));
                engine.SetValue("enSource", Resource("en.txt"));
                engine.SetValue("deSource", Resource("de.txt"));
                stage = "preparing word-frequency tables";
                engine.Execute("var corrector = LocalCorrectCore.createCorrector({en:enSource,de:deSource}); enSource=null; deSource=null;");
                // Independent static stages keep transient startup allocations
                // within the same per-entry limits used for every correction.
                // Empty text prepares EN/DE validity without reading any email.
                stage = "preparing spelling validity";
                engine.Execute("corrector.correct('',{language:'en',mode:'conservative'});");
                stage = "preparing lookup indexes";
                engine.Execute("LocalCorrectCore.prepare();");
                stage = "preparing selected English vocabulary";
                engine.Execute("LocalCorrectCore.prepareSelectedVocabulary('en');");
                stage = "preparing selected German vocabulary";
                engine.Execute("LocalCorrectCore.prepareSelectedVocabulary('de');");
                stage = "loading contextual model";
                model = new ContextModel(engine);
            } catch (Exception) {
                try { model?.Dispose(); engine?.Dispose(); } catch { }

                // Jint resource exceptions are not serializable across the
                // .NET Framework AppDomain. Do not retain one as InnerException.
                throw new InvalidOperationException("Offline correction startup failed while " + stage + ".");
            }
        }
        public Result Correct(string text, string language, string mode, string context = null, int contextOffset = 0, bool mixedLanguage = true) {
            ValidateRequest(text, language, mode, context, contextOffset);
            engine.SetValue("inputText", text);
            engine.SetValue("documentText", context ?? text);
            engine.SetValue("contextOffset", context == null ? 0 : contextOffset);
            engine.SetValue("inputLanguage", language);
            engine.SetValue("inputMode", mode);
            engine.SetValue("inputMixedLanguage", mixedLanguage);
            try {
                engine.SetValue("contextScoresJson", model.Score(engine.Evaluate("JSON.stringify(LocalCorrectCore.encodeContext(inputText,inputLanguage,{policyOnly:true}).chunks)").AsString()));
                var raw = engine.Evaluate("(function(){ var result=corrector.correct(inputText,{language:inputLanguage,mode:inputMode,mixedLanguage:inputMixedLanguage,contextScores:JSON.parse(contextScoresJson)}); var protectedSpans=LocalCorrectCore.protectedRanges(documentText); result.edits=result.edits.filter(function(e){return !protectedSpans.some(function(p){return e.offset+contextOffset < p[1] && e.offset+contextOffset+e.length > p[0];});}); var output=inputText; for(var i=result.edits.length-1;i>=0;i--){var e=result.edits[i];output=output.slice(0,e.offset)+e.replacement+output.slice(e.offset+e.length);} result.text=output; return result;})()").AsObject();
                var edits = raw.Get("edits").AsArray();
                var result = new Result { text = raw.Get("text").AsString(), skipped = checked((int)raw.Get("skipped").AsNumber()), edits = new Edit[checked((int)edits.Length)] };
                for (int i = 0; i < result.edits.Length; i++) {
                    var edit = edits.Get(i.ToString(CultureInfo.InvariantCulture)).AsObject();
                    result.edits[i] = new Edit { offset = checked((int)edit.Get("offset").AsNumber()), length = checked((int)edit.Get("length").AsNumber()), original = edit.Get("original").AsString(), replacement = edit.Get("replacement").AsString() };
                }
                Validate(text, result);
                return result;
            } finally { engine.SetValue("inputText", ""); engine.SetValue("documentText", ""); engine.SetValue("contextScoresJson", ""); }
        }
        internal string ScoreTensors(string chunksJson) { return model.Score(chunksJson); }
        internal string EncodeTensors(string text, string language) {
            engine.SetValue("probeText", text); engine.SetValue("probeLanguage", language);
            try { return engine.Evaluate("JSON.stringify(LocalCorrectCore.encodeContext(probeText,probeLanguage))").AsString(); }
            finally { engine.SetValue("probeText", ""); engine.SetValue("probeLanguage", ""); }
        }
        public void Dispose() { model?.Dispose(); engine?.Dispose(); }
    }
}
#endif
