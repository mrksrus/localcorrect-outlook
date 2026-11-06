#if NET48
using System;
using System.IO;
using System.Reflection;
namespace LocalCorrect {
    // Only the fixed bundled child path is reachable from the Office-facing API.
    public sealed partial class Corrector : MarshalByRefObject, IDisposable {
        HelperConnection helper;
        readonly object gate = new object();
        bool disposed;
        public override object InitializeLifetimeService() { return null; }
        public Corrector() {
            string folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            helper = new HelperConnection(Path.GetFullPath(Path.Combine(folder, "native-helper", "LocalCorrect.Helper.exe")), "--parent " + System.Diagnostics.Process.GetCurrentProcess().Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppDomain.CurrentDomain.DomainUnload += OnUnload;
        }
        void OnUnload(object sender, EventArgs args) { Dispose(); }
        public Result Correct(string text, string language, string mode, string context = null, int contextOffset = 0, bool mixedLanguage = true) {
            ValidateRequest(text, language, mode, context, contextOffset);
            lock (gate) {
                if (disposed) throw new ObjectDisposedException("Corrector");
                return helper.Correct(text, language, mode, context, contextOffset, mixedLanguage);
            }
        }
        public void Dispose() { lock (gate) { if (disposed) return; disposed = true; helper?.Dispose(); AppDomain.CurrentDomain.DomainUnload -= OnUnload; } }
    }
}
#endif
