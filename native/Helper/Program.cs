using System;
using LocalCorrect;
// Must execute before any ONNX Runtime type/native initializer. The parent also
// supplies this variable before process creation to cover native module loading.
Environment.SetEnvironmentVariable("ORT_DISABLE_TELEMETRY", "1");
Environment.SetEnvironmentVariable("ORT_RUNNING_UNIT_TESTS", "1");
try {
    if (args.Length != 0) {
        if (args.Length != 2 || args[0] != "--parent" || !int.TryParse(args[1], out int parentId) || parentId <= 0) return 2;
        var parent = System.Diagnostics.Process.GetProcessById(parentId);
        _ = System.Threading.Tasks.Task.Run(() => { try { parent.WaitForExit(); } finally { Environment.Exit(0); } });
    }
    using var input = Console.OpenStandardInput();
    using var output = Console.OpenStandardOutput();
    using var corrector = new Corrector();
    bool ready = false;
    while (true) {
        var request = Frames.Read<Request>(input);
        if (request.version != 1) return 2;
        if (!ready) {
            if (request.operation != "ready" || request.text != null || request.context != null) return 2;
            ready = true; Frames.Write(output, new Response { version = 1, status = "ready" }); continue;
        }
        if (request.operation != "correct") return 2;
        try {
            var result = corrector.Correct(request.text, request.language, request.mode, request.context, request.contextOffset);
            Frames.Write(output, new Response { version = 1, status = "ok", result = result });
        } catch (ArgumentException) { Frames.Write(output, new Response { version = 1, status = "rejected" }); }
    }
} catch { return 1; } // Never print exceptions or document fragments.
