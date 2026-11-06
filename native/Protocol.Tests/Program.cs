using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LocalCorrect;
Environment.SetEnvironmentVariable("ORT_DISABLE_TELEMETRY", "1");
Environment.SetEnvironmentVariable("ORT_RUNNING_UNIT_TESTS", "1");
if(args.Length==2 && args[0]=="--driver") {
    var info=new ProcessStartInfo {FileName=Path.GetFullPath(args[1]),Arguments="--parent "+Process.GetCurrentProcess().Id,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
    info.Environment["ORT_DISABLE_TELEMETRY"]="1";
    info.Environment["ORT_RUNNING_UNIT_TESTS"]="1";
    using var child=Process.Start(info); Console.WriteLine(child.Id); Console.Out.Flush(); Thread.Sleep(30000); return 0;
}
if (args.Length == 2 && args[0] == "--child") {
    var input = Console.OpenStandardInput(); var output = Console.OpenStandardOutput();
    if (Environment.GetEnvironmentVariable("ORT_DISABLE_TELEMETRY") != "1" || Environment.GetEnvironmentVariable("ORT_RUNNING_UNIT_TESTS") != "1") return 2;
    if (args[1] == "ready-timeout") { Thread.Sleep(10000); return 0; }
    var ready = Frames.Read<Request>(input);
    Frames.Write(output, new Response { version = 1, status = "ready" });
    var request = Frames.Read<Request>(input);
    switch (args[1]) {
        case "timeout": Thread.Sleep(10000); return 0;
        case "crash": return 3;
        case "oversize": output.Write(new byte[] {255,255,255,255}); output.Flush(); return 0;
        case "malformed": output.Write(new byte[] {1,0,0,0,(byte)'!'}); output.Flush(); return 0;
        case "eof": output.Write(new byte[] {10,0,0,0,(byte)'{'}); output.Flush(); return 0;
        case "bad-span": Frames.Write(output, new Response {version=1,status="ok",result=new Result {text="changed",skipped=0,edits=Array.Empty<Edit>()}}); return 0;
        default:
            Frames.Write(output, new Response {version=1,status="ok",result=new Result {text=request.text,skipped=0,edits=Array.Empty<Edit>()}});
            Thread.Sleep(10000); return 0;
    }
}
void Require(bool value, string description) { if (!value) throw new Exception(description); }
void Reject(Action action, string description) { bool rejected=false; try { action(); } catch { rejected=true; } Require(rejected, description); }
foreach (var bytes in new[] {new byte[]{0,0,0,0},new byte[]{255,255,255,255},new byte[]{10,0,0,0,123},new byte[]{1,0,0,0,255},new byte[]{1,0,0,0,33}})
    Reject(() => Frames.Read<Request>(new MemoryStream(bytes)), "Invalid frame accepted");
foreach (var bytes in new[] {new byte[]{239,187,191},new byte[]{239,187,191,0,0,0,0},new byte[]{239,187,191,255,255,255,255},new byte[]{239,187,191,239,187,191,1,0,0,0,33}})
    Reject(() => Frames.Read<Request>(new MemoryStream(bytes)), "Invalid preamble-prefixed frame accepted");
foreach (var json in new[] {"{\"version\":1,\"operation\":\"ready\",\"unknown\":true}","{\"version\":1,\"version\":2,\"operation\":\"ready\"}","{\"version\":1,\"operation\":\"ready\"}{}"}) {
    var bytes=Encoding.UTF8.GetBytes(json); var prefix=BitConverter.GetBytes(bytes.Length);
    using var stream=new MemoryStream(); stream.Write(prefix); stream.Write(bytes); stream.Position=0;
    Reject(()=>Frames.Read<Request>(stream),"Unknown, duplicate or trailing JSON accepted: " + json);
}
using (var stream = new MemoryStream()) {
    Frames.Write(stream, new Request {version=1,operation="correct",text="synthetic 📧 Ä"}); stream.Position=0;
    Require(Frames.Read<Request>(stream).text == "synthetic 📧 Ä", "UTF8 roundtrip failed");
}
using (var stream = new MemoryStream()) {
    stream.Write(new byte[]{239,187,191});
    Frames.Write(stream, new Request {version=1,operation="ready"});
    Frames.Write(stream, new Request {version=1,operation="correct",text="synthetic 📧 Ä"}); stream.Position=0;
    Require(Frames.Read<Request>(stream).operation=="ready" && Frames.Read<Request>(stream).text=="synthetic 📧 Ä","Framework UTF8 preamble disrupted binary framing");
}
Reject(() => Corrector.Validate("abc",new Result {text="altered",edits=Array.Empty<Edit>()}),"Unexplained output accepted");
Reject(() => Corrector.Validate("abc",new Result {text="abc",edits=new[]{new Edit{offset=0,length=1,original="x",replacement="a"}}}),"Wrong original accepted");
Reject(() => Corrector.Validate("abc",new Result {text="word\n",edits=new[]{new Edit{offset=0,length=3,original="abc",replacement="word\n"}}}),"Trailing newline replacement accepted");
string self=Environment.ProcessPath;
foreach (var scenario in new[] {"ready-timeout","timeout","crash","oversize","malformed","eof","bad-span"}) {
    int pid=-1;
    Reject(() => { using var child = new HelperConnection(self,"--child "+scenario,3000,300); pid=child.ProcessId; child.Correct("synthetic", "en", "conservative", null, 0); },"Fault was accepted: "+scenario);
    if (pid>0) { bool alive; try { alive=!Process.GetProcessById(pid).HasExited; } catch { alive=false; } Require(!alive,"Failed child remains alive"); }
}
using (var child = new HelperConnection(self,"--child valid",3000,1000)) {
    int first=child.ProcessId;
    Require(child.Correct("synthetic", "en", "conservative", null, 0).text=="synthetic","Valid response failed");
    using (var process = Process.GetProcessById(first)) { process.Kill(); process.WaitForExit(); }
    Require(child.Correct("restart", "en", "conservative", null, 0).text=="restart" && child.ProcessId!=first,"Crash recovery failed");
}
if (args.Length >= 1 && args.Length <= 2) {
    using (var direct = new Corrector())
    using (var parityStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("context-encoder-parity.json"))
    using (var parity = JsonDocument.Parse(parityStream)) {
        double maximumError=0;
        foreach (var probe in parity.RootElement.GetProperty("tokenizerProbes").EnumerateArray()) {
            using var encoded=JsonDocument.Parse(direct.EncodeTensors(probe.GetProperty("text").GetString(),probe.GetProperty("language").GetString()));
            var tokens=encoded.RootElement.GetProperty("tokens"); var expectedTokens=probe.GetProperty("tokens");
            Require(tokens.GetArrayLength()==expectedTokens.GetArrayLength(),"Jint tokenizer length mismatch");
            for(int i=0;i<tokens.GetArrayLength();i++) foreach(var field in new[]{"text","start","end","pieceIds"}) Require(JsonElement.DeepEquals(tokens[i].GetProperty(field),expectedTokens[i].GetProperty(field)),"Jint tokenizer value mismatch");
            Require(JsonElement.DeepEquals(encoded.RootElement.GetProperty("tokenChunks"),probe.GetProperty("chunks")),"Jint chunk/tensor mismatch");
        }
        foreach(var probe in parity.RootElement.GetProperty("probes").EnumerateArray()) {
            var chunk=probe.GetProperty("inputs").EnumerateObject().ToDictionary(item=>item.Name,item=>(object)item.Value);
            chunk["words"]=probe.GetProperty("tokenIndices").EnumerateArray().Select(item=>new{tokenIndex=item.GetInt32()}).ToArray();
            using var scores=JsonDocument.Parse(direct.ScoreTensors(JsonSerializer.Serialize(new[]{chunk})));
            var expected=probe.GetProperty("logitsFloat32")[0];
            Require(scores.RootElement.GetArrayLength()==expected.GetArrayLength(),"Native logits shape mismatch");
            for(int row=0;row<expected.GetArrayLength();row++)for(int slot=0;slot<9;slot++) maximumError=Math.Max(maximumError,Math.Abs(scores.RootElement[row].GetProperty("logits")[slot].GetDouble()-expected[row][slot].GetDouble()));
        }
        Require(maximumError<=0.0001,"Native ONNX logits exceed parity tolerance");
        Console.WriteLine($"PASS Jint tokenizer/chunks and C# ONNX synthetic logits parity, maximum absolute error {maximumError:G6}");
    }
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var driverInfo=new ProcessStartInfo {FileName=self,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};
    driverInfo.ArgumentList.Add("--driver"); driverInfo.ArgumentList.Add(Path.GetFullPath(args[0]));
    using(var driver=Process.Start(driverInfo)) {
        var pidLine=driver.StandardOutput.ReadLineAsync(); Require(pidLine.Wait(5000),"Parent cleanup driver did not start");
        int orphanPid=int.Parse(pidLine.Result); driver.Kill(); driver.WaitForExit();
        try { using var orphan=Process.GetProcessById(orphanPid); if(!orphan.WaitForExit(10000)){orphan.Kill();throw new Exception("Helper survived parent process exit");} } catch(ArgumentException) { }
    }
    Console.WriteLine("PASS actual helper exits after parent process terminates");
    var start=Stopwatch.StartNew();
    using var actual = new HelperConnection(Path.GetFullPath(args[0]));
    Console.WriteLine($"Actual helper cold readiness: {start.Elapsed.TotalMilliseconds:F1} ms");
    Require(actual.Correct("synthetic", "en", "conservative",null,0).text=="synthetic","Actual helper failed");
    Require(actual.Correct("She have finished the first draft of the leaflet.", "en", "conservative",null,0).text=="She has finished the first draft of the leaflet.","Actual helper neural correction failed");
    foreach (var sample in new[] {
        new { Text="Please unterstand the instructions.", Language="en", Expected="Please understand the instructions." },
        new { Text="halloo zusammen", Language="de", Expected="hallo zusammen" },
    }) {
        Require(actual.Correct(sample.Text,sample.Language,"conservative",null,0).text==sample.Text,"Actual helper changed legacy mixed protection");
        Require(actual.Correct(sample.Text,sample.Language,"conservative",null,0,false).text==sample.Expected,"Actual helper lost selected-language scope");
    }
    foreach(var greeting in new[]{"Hallo","hallo","HALLO","Sehr geehrter Herr"}) Require(actual.Correct("Rehcnung","de","conservative",greeting+" Rehcnung",greeting.Length+1).edits.Length==0,"Actual helper selection lost greeting protection");
    Reject(()=>actual.Correct("documetn","en","conservative","documetn\u200dname",0),"Actual helper accepted a joined-token selection");
    Require(actual.Correct("synthetic","en","conservative",null,0).text=="synthetic","Actual helper did not restart after rejecting the malformed selection");
    Console.WriteLine("PASS actual helper neural correction, selected greeting names and joined-token boundaries");
    using (var process = Process.GetProcessById(actual.ProcessId)) Console.WriteLine($"Actual helper peak working set: {process.PeakWorkingSet64 / 1024 / 1024} MiB");
    if(args.Length==2) {
        using var fixtures=JsonDocument.Parse(File.ReadAllText(args[1]));
        foreach(var mixedLanguage in new[]{true,false}) foreach(var language in new[]{"en","de"}) {
            var fixture=fixtures.RootElement.EnumerateArray().First(row=>row.GetProperty("language").GetString()==language && row.GetProperty("text").GetString().Length>=4500 && row.GetProperty("text").GetString().Split((char[])null,StringSplitOptions.RemoveEmptyEntries).Length>=900);
            var samples=new double[3]; var text=fixture.GetProperty("text").GetString();
            for(int i=0;i<samples.Length;i++){var latency=Stopwatch.StartNew();actual.Correct(text,language,"conservative",null,0,mixedLanguage);samples[i]=latency.Elapsed.TotalMilliseconds;}
            Console.WriteLine($"Actual helper warm ~1000-word {(mixedLanguage ? "mixed" : "selected")} {language} roundtrip, ms: {string.Join(", ",samples.Select(sample=>sample.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)))}");
        }
        using var process=Process.GetProcessById(actual.ProcessId); Console.WriteLine($"Actual helper final peak working set: {process.PeakWorkingSet64/1024/1024} MiB");
    }

    foreach (var bytes in new[] {new byte[]{255,255,255,255},new byte[]{10,0,0,0,123},new byte[]{1,0,0,0,33}}) {
        var info=new ProcessStartInfo {FileName=Path.GetFullPath(args[0]),UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.Environment["ORT_DISABLE_TELEMETRY"]="1";
        info.Environment["ORT_RUNNING_UNIT_TESTS"]="1";
        using var child=Process.Start(info);
        Frames.Write(child.StandardInput.BaseStream,new Request {version=1,operation="ready"});
        Require(Frames.Read<Response>(child.StandardOutput.BaseStream).status=="ready","Actual helper handshake failed");
        // An old producer omits the new field. Reuse this real helper to verify
        // that omitted fields and explicit false both reach the correction API.
        foreach (bool? scope in new bool?[] { null, false }) {
            Frames.Write(child.StandardInput.BaseStream,new Request {version=1,operation="correct",text="Please unterstand the instructions.",language="en",mode="conservative",mixedLanguage=scope});
            var response=Frames.Read<Response>(child.StandardOutput.BaseStream);
            Require(response.status=="ok" && response.result.text==(scope==false ? "Please understand the instructions." : "Please unterstand the instructions."),"Actual helper rejected compatible language scope");
        }
        child.StandardInput.BaseStream.Write(bytes); child.StandardInput.Close();
        if(!child.WaitForExit(20000)){child.Kill();throw new Exception("Malformed request left actual helper alive");}
        Require(child.ExitCode!=0 && child.StandardOutput.ReadToEnd().Length==0 && child.StandardError.ReadToEnd().Length==0,"Actual helper did not fail silently on malformed frame");
    }
}
Console.WriteLine("PASS bounded frames, malformed UTF8/JSON, original spans/output, startup timeout, correction timeout, crash, oversize, EOF, disposal, restart and compatible language scope");
return 0;
