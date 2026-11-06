// Synthetic transport tests only. No model, dictionary or user text is loaded.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LocalCorrect;
using LocalCorrect.Outlook;

void Require(bool condition, string description) { if (!condition) throw new Exception(description); }
Request ReadRequest(string json) {
    var bytes = Encoding.UTF8.GetBytes(json);
    using (var stream = new MemoryStream()) { stream.Write(BitConverter.GetBytes(bytes.Length), 0, 4); stream.Write(bytes, 0, bytes.Length); stream.Position = 0; return Frames.Read<Request>(stream); }
}
if (args.Length == 0 || (args.Length == 1 && !args[0].StartsWith("--"))) {
    foreach (var value in new object[] { null, 1, 2, -1, "0", "false", false, 0L, new byte[] { 0 } })
        Require(MixedLanguageSetting.Read(value), "Missing or malformed saved language scope disabled bilingual protection");
    foreach (var mixedLanguage in new[] { true, false })
        Require(MixedLanguageSetting.Read(MixedLanguageSetting.Write(mixedLanguage)) == mixedLanguage, "Saved language scope did not roundtrip");
    var legacy = ReadRequest("{\"version\":1,\"operation\":\"correct\",\"text\":\"synthetic\"}");
    Require(legacy.mixedLanguage == null && (legacy.mixedLanguage ?? true), "Legacy request changed bilingual default");
    foreach (var mixedLanguage in new[] { true, false }) {
        using (var stream = new MemoryStream()) {
            Frames.Write(stream, new Request { version = 1, operation = "correct", mixedLanguage = mixedLanguage }); stream.Position = 0;
            Require(Frames.Read<Request>(stream).mixedLanguage == mixedLanguage, "Language scope did not roundtrip through frame");
        }
    }
    foreach (var malformed in new[] { "\"false\"", "\"true\"", "0", "1", "null", "{}", "[]" }) {
        bool rejected = false;
        try { ReadRequest("{\"version\":1,\"operation\":\"correct\",\"mixedLanguage\":" + malformed + "}"); } catch { rejected = true; }
        Require(rejected, "Malformed language scope accepted");
    }
    bool duplicateRejected = false;
    try { ReadRequest("{\"version\":1,\"operation\":\"correct\",\"mixedLanguage\":true,\"mixedLanguage\":false}"); } catch { duplicateRejected = true; }
    Require(duplicateRejected, "Duplicate language scope accepted");
    Console.WriteLine("PASS saved language scope, legacy request default, boolean frames and malformed scope rejection");
}

if (args.Length == 1 && args[0] == "--prefix-probe") {
    var input = Console.OpenStandardInput(); var bytes = new byte[4];
    for (int i = 0; i < bytes.Length; i++) { int value = input.ReadByte(); if (value < 0) return 2; bytes[i] = (byte)value; }
    Console.WriteLine("wire-prefix=" + BitConverter.ToString(bytes));
    return 0;
}
if (args.Length == 1 && (args[0] == "--wire-child" || args[0] == "--wire-child-selected-only")) {
    string stage = "ready-frame";
    try {
        var input = Console.OpenStandardInput(); var output = Console.OpenStandardOutput();
        var ready = Frames.Read<Request>(input);
        if (ready.version != 1 || ready.operation != "ready") return 2;
        Frames.Write(output, new Response { version = 1, status = "ready" });
        stage = "correction-frame";
        var request = Frames.Read<Request>(input);
        if (request.version != 1 || request.operation != "correct") return 2;
        if ((request.mixedLanguage ?? true) != (args[0] != "--wire-child-selected-only")) return 2;
        Frames.Write(output, new Response { version = 1, status = "ok", result = new Result { text = request.text, edits = new Edit[0] } });
        return 0;
    } catch (Exception error) {
        // TEST ONLY. Never print exception messages or serialized input.
        Console.Error.WriteLine("wire-child-stage=" + stage + " type=" + error.GetType().FullName + " hresult=0x" + error.HResult.ToString("X8"));
        return 3;
    }
}

string self = Process.GetCurrentProcess().MainModule.FileName;
string childPath = args.Length == 1 ? Path.GetFullPath(args[0]) : self;
var originalEncoding = Console.InputEncoding;
try {
#if NET48
    Console.WriteLine("wire-parent=net48 bits=" + (Environment.Is64BitProcess ? "64" : "32"));
#else
    Console.WriteLine("wire-parent=net10 bits=" + (Environment.Is64BitProcess ? "64" : "32"));
#endif
    Console.WriteLine("wire-default-input-codepage=" + originalEncoding.CodePage + " preamble=" + BitConverter.ToString(originalEncoding.GetPreamble()));
    ProcessStartInfo StartInfo(string operation, bool forcePreamble = true) {
        var info = new ProcessStartInfo { FileName = childPath, Arguments = operation, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
#if !NET48
        // Reproduce Framework's preamble-producing redirected StreamWriter on
        // modern .NET too, so the failure loop is runnable on Linux.
        info.StandardInputEncoding = forcePreamble ? new UTF8Encoding(true) : null;
#endif
        return info;
    }
    void Stop(Process process) { try { if (!process.HasExited) process.Kill(); } catch { } try { process.WaitForExit(3000); } catch { } }
    void ProbePrefix(bool forcePreamble) {
        using (var process = Process.Start(StartInfo("--prefix-probe", forcePreamble))) {
            try {
                Frames.Write(process.StandardInput.BaseStream, new Request { version = 1, operation = "ready" });
                var line = process.StandardOutput.ReadLineAsync();
                if (!line.Wait(3000)) throw new Exception("Synthetic prefix probe timed out");
                Console.WriteLine((forcePreamble ? "forced-" : "default-") + line.Result);
                if (!process.WaitForExit(3000) || process.ExitCode != 0) throw new Exception("Synthetic prefix probe failed");
            } finally { Stop(process); }
        }
    }
    ProbePrefix(false);
    // Change only this synthetic test process, never Office's global encoding.
    Console.InputEncoding = new UTF8Encoding(true);
    ProbePrefix(true);
    using (var process = Process.Start(StartInfo("--wire-child"))) {
        try {
            var exchange = Task.Run(() => {
                Frames.Write(process.StandardInput.BaseStream, new Request { version = 1, operation = "ready" });
                if (Frames.Read<Response>(process.StandardOutput.BaseStream).status != "ready") throw new Exception("Synthetic handshake rejected");
                Frames.Write(process.StandardInput.BaseStream, new Request { version = 1, operation = "correct", text = "synthetic 📧 Ä", language = "en", mode = "conservative" });
                var result = Frames.Read<Response>(process.StandardOutput.BaseStream);
                if (result.status != "ok" || result.result?.text != "synthetic 📧 Ä") throw new Exception("Synthetic result mismatch");
            });
            if (!exchange.Wait(5000)) throw new Exception("Synthetic frame exchange timed out");
            exchange.GetAwaiter().GetResult();
            Console.WriteLine("PASS redirected binary handshake and UTF8 correction frame");
        } catch (Exception error) {
            Stop(process);
            Console.Error.WriteLine("wire-parent-stage=frame-exchange type=" + error.GetBaseException().GetType().FullName + " hresult=0x" + error.GetBaseException().HResult.ToString("X8"));
            Console.Error.WriteLine(process.StandardError.ReadToEnd());
            throw;
        } finally { Stop(process); }
    }
    using (var helper = new HelperConnection(childPath, "--wire-child", readyTimeout:5000, correctionTimeout:3000)) {
        if (helper.Correct("synthetic 📧 Ä", "en", "conservative", null, 0).text != "synthetic 📧 Ä") throw new Exception("HelperConnection synthetic result mismatch");
    }
    using (var helper = new HelperConnection(childPath, "--wire-child-selected-only", readyTimeout:5000, correctionTimeout:3000)) {
        if (helper.Correct("synthetic 📧 Ä", "en", "conservative", null, 0, false).text != "synthetic 📧 Ä") throw new Exception("HelperConnection selected-only scope did not propagate");
    }
    Console.WriteLine("PASS actual HelperConnection startup, correction and selected-only scope");
} finally { Console.InputEncoding = originalEncoding; }
return 0;
