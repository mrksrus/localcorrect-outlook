#if !NET48
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using Jint;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
namespace LocalCorrect {
    // Only bundled, hash-verified models are accepted. No external-data path is used.
    internal sealed class ContextModel : IDisposable {
        readonly InferenceSession session;
        static readonly string[] InputNames = { "input_ids", "attention_mask", "word_positions", "candidate_ids", "candidate_mask" };
        internal ContextModel(Engine engine) {
            Environment.SetEnvironmentVariable("ORT_DISABLE_TELEMETRY", "1");
            Environment.SetEnvironmentVariable("ORT_RUNNING_UNIT_TESTS", "1");
            using var identity = JsonDocument.Parse(engine.Evaluate("JSON.stringify(LocalCorrectCore.contextIdentity())").AsString());
            string path = Path.Combine(AppContext.BaseDirectory, "models", "context-encoder.onnx");
            byte[] modelBytes;
            using (var file = File.OpenRead(path)) {
                long expected = identity.RootElement.GetProperty("modelBytes").GetInt64();
                if (expected < 1 || expected > 64 * 1024 * 1024 || file.Length != expected) throw new InvalidDataException("Bundled model identity mismatch.");
                modelBytes = new byte[checked((int)expected)]; file.ReadExactly(modelBytes);
                if (Convert.ToHexString(SHA256.HashData(modelBytes)).ToLowerInvariant() != identity.RootElement.GetProperty("modelSha256").GetString()) throw new InvalidDataException("Bundled model identity mismatch.");
            }
            OrtEnv.Instance().DisableTelemetryEvents();
            using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_FATAL };
            session = new InferenceSession(modelBytes, options);
            if (session.InputMetadata.Count != 5 || InputNames.Any(name => !session.InputMetadata.ContainsKey(name)) ||
                InputNames.Any(name => session.InputMetadata[name].ElementType != (name == "candidate_mask" ? typeof(float) : typeof(long))) ||
                session.OutputMetadata.Count != 1 || !session.OutputMetadata.ContainsKey("logits") || session.OutputMetadata["logits"].ElementType != typeof(float))
                { session.Dispose(); throw new InvalidDataException("Bundled model tensor contract mismatch."); }
        }
        static long[] Ints(JsonElement value, int rank, int[] shape, bool mask = false) {
            var items = new List<long>();
            void Walk(JsonElement node, int depth) {
                if (depth == rank) { if (mask) { double number = node.GetDouble(); if (number != 0 && number != 1) throw new InvalidDataException(); items.Add((long)number); } else items.Add(node.GetInt64()); return; }
                if (node.ValueKind != JsonValueKind.Array || node.GetArrayLength() != shape[depth]) throw new InvalidDataException();
                foreach (var item in node.EnumerateArray()) Walk(item, depth + 1);
            }
            Walk(value, 0); return items.ToArray();
        }
        internal string Score(string chunksJson) {
            using var chunks = JsonDocument.Parse(chunksJson, new JsonDocumentOptions { MaxDepth = 8 });
            if (chunks.RootElement.ValueKind != JsonValueKind.Array || chunks.RootElement.GetArrayLength() > 20000) throw new InvalidDataException();
            var scores = new List<object>();
            foreach (var chunk in chunks.RootElement.EnumerateArray()) {
                int sequence = chunk.GetProperty("input_ids")[0].GetArrayLength();
                int words = chunk.GetProperty("word_positions")[0].GetArrayLength();
                if (sequence < 2 || sequence > 128 || words < 1 || words > 128 || chunk.GetProperty("words").GetArrayLength() != words) throw new InvalidDataException();
                var input = new List<NamedOnnxValue>();
                foreach (var name in InputNames) {
                    int[] shape = name == "input_ids" || name == "attention_mask" ? new[] {1,sequence} : name == "word_positions" ? new[] {1,words} : new[] {1,words,9};
                    var values = Ints(chunk.GetProperty(name), shape.Length, shape, name == "candidate_mask");
                    if (name == "candidate_mask") {
                        if (values.Any(value => value != 0 && value != 1)) throw new InvalidDataException();
                        input.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<float>(Array.ConvertAll(values, value => (float)value), shape)));
                    } else input.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(values, shape)));
                }
                using var results = session.Run(input);
                var output = results.Single().AsTensor<float>();
                if (!output.Dimensions.SequenceEqual(new[] {1,words,9})) throw new InvalidDataException();
                int index = 0;
                foreach (var word in chunk.GetProperty("words").EnumerateArray()) {
                    int tokenIndex = word.GetProperty("tokenIndex").GetInt32();
                    if (tokenIndex < 0 || tokenIndex > 20000) throw new InvalidDataException();
                    var logits = new float[9];
                    for (int slot = 0; slot < 9; slot++) { logits[slot] = output[0,index,slot]; if (!float.IsFinite(logits[slot])) throw new InvalidDataException(); }
                    scores.Add(new { tokenIndex, logits }); index++;
                }
            }
            return JsonSerializer.Serialize(scores);
        }
        public void Dispose() { session?.Dispose(); }
    }
}
#endif
