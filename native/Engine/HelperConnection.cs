using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
namespace LocalCorrect {
    internal sealed class HelperConnection : IDisposable {
        Process helper;
        bool disposed;
        readonly string path, arguments;
        readonly int readyTimeout, correctionTimeout;
        internal HelperConnection(string path, string arguments = "", int readyTimeout = 120000, int correctionTimeout = 30000) {
            if (!Path.IsPathRooted(path)) throw new ArgumentException("Absolute helper path required.");
            this.path = path; this.arguments = arguments; this.readyTimeout = readyTimeout; this.correctionTimeout = correctionTimeout;
            try { Start(); } catch { Dispose(); throw new InvalidOperationException("Offline correction helper could not start."); }
        }
        void Start() {
            var start = new ProcessStartInfo { FileName = path, Arguments = arguments, WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            // ORT 1.31.1 Windows NuGet uses legacy ETW. Its pinned constructor
            // checks this internal opt-out before provider registration.
            start.EnvironmentVariables["ORT_RUNNING_UNIT_TESTS"] = "1";
            start.EnvironmentVariables["ORT_DISABLE_TELEMETRY"] = "1";
            start.EnvironmentVariables["DOTNET_DbgEnableMiniDump"] = "0";
            start.EnvironmentVariables["COMPlus_DbgEnableMiniDump"] = "0";
            start.EnvironmentVariables["DOTNET_EnableDiagnostics"] = "0";
            helper = Process.Start(start);
            // Discard without accumulating private exception strings or unbounded output.
            var error = helper.StandardError;
            Task.Run(() => { try { var buffer = new char[1024]; while (error.Read(buffer, 0, buffer.Length) > 0) { } } catch { } });
            var response = Exchange(new Request { version = 1, operation = "ready" }, readyTimeout);
            if (response.status != "ready" || response.result != null) throw new InvalidDataException();
        }
        Response Exchange(Request request, int timeout) {
            var process = helper;
            var pending = Task.Run(() => { Frames.Write(process.StandardInput.BaseStream, request); return Frames.Read<Response>(process.StandardOutput.BaseStream); });
            _ = pending.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            if (!pending.Wait(timeout)) { Stop(); throw new InvalidOperationException("Offline correction exceeded its time limit. No changes made."); }
            var response = pending.GetAwaiter().GetResult();
            if (response.version != 1 || (response.status != "ready" && response.status != "ok" && response.status != "rejected")) throw new InvalidDataException();
            return response;
        }
        internal Result Correct(string text, string language, string mode, string context, int contextOffset) {
            if (disposed) throw new ObjectDisposedException("HelperConnection");
            try {
                if (helper == null || helper.HasExited) { Stop(); Start(); }
                var response = Exchange(new Request { version = 1, operation = "correct", text = text, language = language, mode = mode, context = context, contextOffset = contextOffset }, correctionTimeout);
                if (response.status != "ok" || response.result == null) throw new InvalidDataException();
                Corrector.Validate(text, response.result); return response.result;
            } catch { Stop(); throw new InvalidOperationException("Offline correction could not complete. No changes made. Run correction again."); }
        }
        internal int ProcessId { get { return helper == null ? -1 : helper.Id; } }
        void Stop() {
            var process = helper; helper = null;
            if (process == null) return;
            try { if (!process.HasExited) process.Kill(); } catch { }
            try { process.WaitForExit(5000); } catch { }
            process.Dispose();
        }
        public void Dispose() { if (disposed) return; disposed = true; Stop(); }
    }
}
