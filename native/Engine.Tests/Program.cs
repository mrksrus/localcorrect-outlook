using System;
using System.Diagnostics;
using LocalCorrect;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Reflection;
Environment.SetEnvironmentVariable("ORT_DISABLE_TELEMETRY", "1");
Environment.SetEnvironmentVariable("ORT_RUNNING_UNIT_TESTS", "1");
var watch = Stopwatch.StartNew();
#if NET48
var domain = AppDomain.CreateDomain("LocalCorrect test engine", null, new AppDomainSetup { ApplicationBase = AppDomain.CurrentDomain.BaseDirectory, ConfigurationFile = Assembly.GetExecutingAssembly().Location + ".config" });
var engine = (Corrector)domain.CreateInstanceAndUnwrap(typeof(Corrector).Assembly.FullName, typeof(Corrector).FullName);
#else
var engine = new Corrector();
#endif
Console.WriteLine($"Engine initialized in {watch.Elapsed.TotalSeconds:F1}s");
void Check(string input, string language, string expected) {
    var result = engine.Correct(input, language, "conservative");
    if (result.text != expected) throw new Exception($"Unexpected result for synthetic {language} fixture: {result.text}");
    Console.WriteLine($"PASS {language}: {result.edits.Length} edits in {watch.Elapsed.TotalSeconds:F1}s");
}
Check("please teh documetn", "en", "please the document");
Check("please keep the mesage", "en", "please keep the mesage");
Check("Bitte senden Sie die Rehcnung.", "de", "Bitte senden Sie die Rechnung.");
Check("https://example.com/seperate a@documetn.com `seperate`\n-- \nseperate", "en", "https://example.com/seperate a@documetn.com `seperate`\n-- \nseperate");
Check("📧 please documetn this", "en", "📧 please document this");
bool rejected = false;
try { Corrector.Validate("abc", new Result { edits = new[] { new Edit { offset = 2, length = 4, original = "x", replacement = "y" } } }); } catch (InvalidOperationException) { rejected = true; }
if (!rejected) throw new Exception("Out-of-bounds edit was accepted");
Console.WriteLine("PASS edit validation");

var quoted = engine.Correct("documetn", "en", "conservative", "From: someone\ndocumetn", 14);
if (quoted.edits.Length != 0) throw new Exception("Selection lost quote context");
foreach (var greeting in new[] {"Hallo", "hallo", "HALLO"}) {
    var selectedName = engine.Correct("Rehcnung", "de", "conservative", greeting + " Rehcnung", greeting.Length + 1);
    if (selectedName.edits.Length != 0) throw new Exception("Selection lost salutation protection");
}
Console.WriteLine("PASS selected text preserves full-document quote and salutation protection");
foreach (var sample in new[] {
    new { Selection = "pleaes", Context = "pleaesant", Offset = 0 },
    new { Selection = "pleaes", Context = "xpleaes", Offset = 1 },
    new { Selection = "documetn", Context = "documetn\u0301", Offset = 0 },
    new { Selection = "documetn", Context = "documetn_", Offset = 0 },
    new { Selection = "documetn", Context = "documetn\u200dname", Offset = 0 },
    new { Selection = "documetn", Context = "documetn\u203fname", Offset = 0 },
    new { Selection = "documetn", Context = "1documetn", Offset = 1 },
    new { Selection = "documetn", Context = "\U00010400documetn", Offset = 2 },
    new { Selection = "\ud83d", Context = "\ud83d\ude00", Offset = 0 },
}) {
    bool boundaryRejected = false;
    try { engine.Correct(sample.Selection, "en", "conservative", sample.Context, sample.Offset); }
    catch (ArgumentException) { boundaryRejected = true; }
    if (!boundaryRejected) throw new Exception("Partial-token selection was accepted");
}
if (engine.Correct("documetn", "en", "conservative", "a documetn.", 2).text != "document") throw new Exception("Whole-token selection rejected");
Console.WriteLine("PASS token and surrogate selection boundaries");

Check("please teh document.\r-- \rseperate seperate", "en", "please the document.\r-- \rseperate seperate");

Console.WriteLine($"Peak working set: {Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024} MiB; managed memory: {GC.GetTotalMemory(false) / 1024 / 1024} MiB; elapsed: {watch.Elapsed.TotalSeconds:F1}s");

using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("parity.json"))
{
    var fixtures = (Fixture[])new DataContractJsonSerializer(typeof(Fixture[])).ReadObject(stream);
    int count = 0;
    foreach (var fixture in fixtures) {
        var result = engine.Correct(fixture.text, fixture.language, fixture.mode);
        var expected = fixture.expected;
        if (result.text != expected.text || Serialize(result.edits) != Serialize(expected.edits) || result.skipped != expected.skipped) throw new Exception("Jint/JavaScript parity mismatch at case " + count);
        count++;
    }
    Console.WriteLine($"PASS {count} Jint/JavaScript parity cases");
    foreach (var language in new[] { "en", "de" }) {
        Fixture longFixture = null;
        foreach (var f in fixtures) if (f.language == language && f.text.Length >= 4500 && f.text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length >= 900) { longFixture = f; break; }
        if (longFixture == null) throw new Exception("Missing long-email performance fixture");
        var samples = new double[3];
        for (int i = 0; i < samples.Length; i++) { var latency = Stopwatch.StartNew(); engine.Correct(longFixture.text, language, "conservative"); samples[i] = latency.Elapsed.TotalMilliseconds; }
        Console.WriteLine($"Warm ~1000-word {language} engine timings, ms: {string.Join(", ", Array.ConvertAll(samples, value => value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)))}");
    }

}
Console.WriteLine($"Final peak working set: {Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024} MiB; managed memory: {GC.GetTotalMemory(false) / 1024 / 1024} MiB; elapsed: {watch.Elapsed.TotalSeconds:F1}s");

#if NET48
engine.Dispose();
AppDomain.Unload(domain);
#else
engine.Dispose();
#endif

static string Serialize(Edit[] edits) {
    using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(Edit[])).WriteObject(stream, edits); return Encoding.UTF8.GetString(stream.ToArray()); }
}
public sealed class Fixture {
    public string text { get; set; }
    public string language { get; set; }
    public string mode { get; set; }
    public Result expected { get; set; }
}
