using System;
using LocalCorrect;
using Microsoft.ML.OnnxRuntime;
Environment.SetEnvironmentVariable("ORT_DISABLE_TELEMETRY", "1");
Environment.SetEnvironmentVariable("ORT_RUNNING_UNIT_TESTS", "1");
if (OrtEnv.IsCreated) { Console.Error.WriteLine("The startup logging probe requires a fresh process."); return 1; }
using var corrector = new Corrector();
if (OrtEnv.Instance().EnvLogLevel != OrtLoggingLevel.ORT_LOGGING_LEVEL_FATAL)
    { Console.Error.WriteLine("The actual engine created ONNX Runtime with startup logging enabled."); return 1; }
Console.WriteLine("PASS actual engine creates its ONNX environment with startup logging suppressed");

return 0;
